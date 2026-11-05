using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/calculo-frete")]
public class CalculoFreteController(AppDbContext db) : ControllerBase
{
    // Lista resumida (sem o payload) — para identificar por placa + dia
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var lista = await db.CalculosFreteSalvos
            .OrderByDescending(c => c.CriadoEm)
            .Select(c => new
            {
                c.Id,
                c.Placa,
                c.CriadoEm,
                c.ValorCarga,
                c.ValorPedagio,
                c.PesoTotal,
                c.LiquotaArredondada,
                c.TotalNFs,
                c.TotalCTes,
                totalPlanilhas = db.PlanilhasCarga.Count(p => p.CalculoId == c.Id),
            })
            .ToListAsync();
        return Ok(lista);
    }

    // Cálculo completo (com PayloadJson) — para reabrir tudo
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Obter(int id)
    {
        var c = await db.CalculosFreteSalvos.FindAsync(id);
        if (c == null) return NotFound(new { erro = "Cálculo não encontrado." });

        // O conteúdo fica de fora: a lista só precisa do nome e do tamanho, e o
        // arquivo vem no download.
        var planilhas = await db.PlanilhasCarga
            .Where(p => p.CalculoId == id)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome, p.CriadoEm, tamanho = p.Conteudo.Length })
            .ToListAsync();

        return Ok(new
        {
            c.Id, c.Placa, c.ValorCarga, c.ValorPedagio, c.PesoTotal,
            c.LiquotaBruta, c.LiquotaArredondada, c.TotalNFs, c.TotalCTes,
            c.PayloadJson, c.CriadoEm,
            planilhas,
        });
    }

    // Baixa de novo uma planilha guardada na carga
    [HttpGet("{id:int}/planilha/{planilhaId:int}")]
    public async Task<IActionResult> BaixarPlanilha(int id, int planilhaId)
    {
        var p = await db.PlanilhasCarga
            .FirstOrDefaultAsync(x => x.Id == planilhaId && x.CalculoId == id);
        if (p == null) return NotFound(new { erro = "Planilha não encontrada." });

        // O relatório de frete é .txt; o resto é planilha
        var tipo = p.Nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? "text/plain"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        return File(p.Conteudo, tipo, p.Nome);
    }

    // Guarda planilhas numa carga que já existe (geradas depois de reabrir)
    [HttpPost("{id:int}/planilhas")]
    public async Task<IActionResult> AnexarPlanilhas(int id, [FromBody] List<PlanilhaCargaDto> planilhas)
    {
        var carga = await db.CalculosFreteSalvos.FindAsync(id);
        if (carga == null) return NotFound(new { erro = "Carga não encontrada." });

        int guardadas = await GuardarPlanilhas(id, planilhas);
        await db.SaveChangesAsync();
        return Ok(new { guardadas });
    }

    // Gerar de novo com o mesmo número troca o arquivo, em vez de empilhar duas
    // versões do mesmo romaneio. Tudo o que é gerado já fica guardado sem carga
    // (ver NFeController); quando a planilha chega aqui, a carga adota esse
    // registro em vez de criar outro, senão o romaneio apareceria duas vezes na
    // lista de romaneios guardados.
    private async Task<int> GuardarPlanilhas(int calculoId, List<PlanilhaCargaDto>? planilhas)
    {
        if (planilhas == null || planilhas.Count == 0) return 0;

        var existentes = await db.PlanilhasCarga.Where(p => p.CalculoId == calculoId).ToListAsync();
        var nomes = planilhas.Select(p => p.Nome).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        var soltas = await db.PlanilhasCarga
            .Where(p => p.CalculoId == null && nomes.Contains(p.Nome))
            .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
            .ToListAsync();
        int n = 0;

        foreach (var item in planilhas)
        {
            if (string.IsNullOrWhiteSpace(item.Nome) || string.IsNullOrWhiteSpace(item.Conteudo)) continue;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(item.Conteudo); }
            catch { continue; }

            bool MesmoNome(PlanilhaCarga p) => string.Equals(p.Nome, item.Nome, StringComparison.OrdinalIgnoreCase);

            var atual = existentes.FirstOrDefault(MesmoNome);
            if (atual == null)
            {
                // A cópia sem carga mais recente vira a da carga
                atual = soltas.FirstOrDefault(MesmoNome);
                if (atual != null)
                {
                    atual.CalculoId = calculoId;
                    soltas.Remove(atual);
                }
                else
                {
                    atual = new PlanilhaCarga { CalculoId = calculoId, Nome = item.Nome };
                    db.PlanilhasCarga.Add(atual);
                }
                existentes.Add(atual);
            }
            atual.Conteudo = bytes;
            atual.CriadoEm = DateTime.UtcNow;

            // Sobrou outra cópia sem carga com o mesmo nome (gerada de novo antes
            // de salvar): o conteúdo que vale agora é o da carga.
            foreach (var sobra in soltas.Where(MesmoNome).ToList())
            {
                db.PlanilhasCarga.Remove(sobra);
                soltas.Remove(sobra);
            }
            n++;
        }
        return n;
    }

    [HttpPost]
    public async Task<IActionResult> Salvar([FromBody] SalvarCalculoFreteDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Placa))
            return BadRequest(new { erro = "Informe a placa para salvar o cálculo." });

        var calc = new CalculoFreteSalvo
        {
            Placa              = dto.Placa.Trim().ToUpper(),
            ValorCarga         = dto.ValorCarga,
            ValorPedagio       = dto.ValorPedagio,
            PesoTotal          = dto.PesoTotal,
            LiquotaBruta       = dto.LiquotaBruta,
            LiquotaArredondada = dto.LiquotaArredondada,
            TotalNFs           = dto.TotalNFs,
            TotalCTes          = dto.TotalCTes,
            PayloadJson        = dto.PayloadJson ?? "",
            CriadoEm           = DateTime.UtcNow,
        };
        db.CalculosFreteSalvos.Add(calc);
        await db.SaveChangesAsync();

        int planilhas = await GuardarPlanilhas(calc.Id, dto.Planilhas);
        if (planilhas > 0) await db.SaveChangesAsync();

        return Created($"/api/calculo-frete/{calc.Id}", new { calc.Id, planilhas });
    }

    // Editar a carga (Admin, 15/10/2026: NF cancelada e trocada na fazenda; antes tinha de
    // excluir e fazer outra): grava por cima o cálculo e as planilhas geradas de novo
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] SalvarCalculoFreteDto dto)
    {
        var calc = await db.CalculosFreteSalvos.FindAsync(id);
        if (calc == null) return NotFound(new { erro = "Carga não encontrada." });
        if (string.IsNullOrWhiteSpace(dto.Placa))
            return BadRequest(new { erro = "Informe a placa para salvar o cálculo." });

        calc.Placa              = dto.Placa.Trim().ToUpper();
        calc.ValorCarga         = dto.ValorCarga;
        calc.ValorPedagio       = dto.ValorPedagio;
        calc.PesoTotal          = dto.PesoTotal;
        calc.LiquotaBruta       = dto.LiquotaBruta;
        calc.LiquotaArredondada = dto.LiquotaArredondada;
        calc.TotalNFs           = dto.TotalNFs;
        calc.TotalCTes          = dto.TotalCTes;
        calc.PayloadJson        = dto.PayloadJson ?? "";
        int planilhas = await GuardarPlanilhas(calc.Id, dto.Planilhas);
        await db.SaveChangesAsync();
        return Ok(new { calc.Id, planilhas });
    }

    // Os romaneios da carga não vão junto: a chave estrangeira solta a carga
    // (ON DELETE SET NULL) e eles continuam em Romaneios guardados.
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var c = await db.CalculosFreteSalvos.FindAsync(id);
        if (c == null) return NotFound(new { erro = "Cálculo não encontrado." });
        db.CalculosFreteSalvos.Remove(c);
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Cálculo excluído." });
    }
}

public record SalvarCalculoFreteDto(
    string Placa,
    decimal ValorCarga,
    decimal ValorPedagio,
    decimal PesoTotal,
    decimal LiquotaBruta,
    decimal LiquotaArredondada,
    int TotalNFs,
    int TotalCTes,
    string? PayloadJson,
    List<PlanilhaCargaDto>? Planilhas = null);

public record PlanilhaCargaDto(string Nome, string Conteudo);
