using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Planilhas SG (Seguro / ICMS) guardadas no sistema, uma por mês: as que a
/// tela de CT-es gera e as trazidas do Drive. Lista com os totais, abre o
/// resumo lido da planilha, baixa de novo e exclui.
/// </summary>
[Authorize]
[ApiController]
[Route("api/planilhas-seguro")]
public class PlanilhasSeguroController(AppDbContext db, PlanilhaSeguroService servico,
    ILogger<PlanilhasSeguroController> logger) : ControllerBase
{
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Sem o conteúdo: a lista só precisa dos totais, e o arquivo vem no download
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var registros = await db.PlanilhasSeguro.AsNoTracking()
            .OrderByDescending(p => p.Ano).ThenByDescending(p => p.Mes)
            .Select(p => new
            {
                p.Id, p.Mes, p.Ano, p.Nome, p.Origem, p.QuantidadeCtes,
                p.TotalValorNF, p.TotalFrete, p.TotalSeguro, p.TotalIcms, p.TotalDespesas,
                p.ResumoJson, p.CriadoEm, p.AtualizadoEm,
                tamanho = p.Conteudo.Length,
            })
            .ToListAsync();

        return Ok(registros.Select(p =>
        {
            int avisos = 0;
            try { avisos = System.Text.Json.JsonSerializer.Deserialize<PlanilhaSeguroLeitor.Resumo>(p.ResumoJson, PlanilhaSeguroService.Json)?.Avisos.Count ?? 0; }
            catch (System.Text.Json.JsonException) { /* sem contagem */ }
            return new
            {
                p.Id, p.Mes, p.Ano, p.Nome, p.Origem, p.QuantidadeCtes,
                p.TotalValorNF, p.TotalFrete, p.TotalSeguro, p.TotalIcms, p.TotalDespesas,
                avisos, p.CriadoEm, p.AtualizadoEm, p.tamanho,
            };
        }));
    }

    // O resumo do mês: totais, aba RESUMO, tabela por placa e avisos
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var p = await db.PlanilhasSeguro.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { erro = "Planilha não encontrada." });

        PlanilhaSeguroLeitor.Resumo resumo;
        try { resumo = PlanilhaSeguroService.ResumoDe(p); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não consegui ler a planilha SG {Nome}", p.Nome);
            return UnprocessableEntity(new { erro = $"Não consegui ler a planilha {p.Nome}. Baixe e abra no Excel." });
        }

        return Ok(new
        {
            p.Id, p.Mes, p.Ano, p.Nome, p.Origem, p.CriadoEm, p.AtualizadoEm,
            valoresInformados = p.Origem == "gerada"
                ? new { das = p.Das, iofSeguros = p.IofSeguros, servicoEscritorio = p.ServicoEscritorio, taxaBSoft = p.TaxaBSoft }
                : null,
            resumo,
        });
    }

    [HttpGet("{id:int}/arquivo")]
    public async Task<IActionResult> Baixar(int id)
    {
        var p = await db.PlanilhasSeguro.AsNoTracking()
            .Where(x => x.Id == id).Select(x => new { x.Nome, x.Conteudo }).FirstOrDefaultAsync();
        if (p == null) return NotFound(new { erro = "Planilha não encontrada." });
        return File(p.Conteudo, TipoXlsx, p.Nome);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        // Sem carregar o arquivo inteiro só para apagar
        var apagadas = await db.PlanilhasSeguro.Where(x => x.Id == id).ExecuteDeleteAsync();
        if (apagadas == 0) return NotFound(new { erro = "Planilha não encontrada." });
        return Ok(new { mensagem = "Planilha excluída." });
    }

    // Traz planilhas SG feitas fora do sistema (as do Drive). Cada uma é lida
    // antes de guardar: a que não abre como planilha SG ou não diz de que mês
    // é fica de fora e volta no aviso. Mês que já estava guardado é trocado.
    [HttpPost("importar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Importar(IFormFileCollection arquivos)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Selecione as planilhas SG (.xlsx)." });

        var guardadas = new List<object>();
        var recusados = new List<string>();

        foreach (var arq in arquivos)
        {
            var nome = Path.GetFileName(arq.FileName.Trim());
            if (nome.StartsWith("~$")) continue;   // arquivo temporário do Excel aberto
            if (!nome.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            { recusados.Add($"{nome}: não é planilha .xlsx."); continue; }

            using var ms = new MemoryStream();
            await arq.CopyToAsync(ms);

            try
            {
                var g = await servico.GuardarAsync(ms.ToArray(), nome, null, null, "importada", null);
                guardadas.Add(new
                {
                    nome, g.Registro.Id, g.Registro.Mes, g.Registro.Ano, g.Substituiu, g.OrigemAnterior,
                    avisos = g.Resumo.Avisos,
                });
            }
            catch (InvalidOperationException ex)
            {
                db.ChangeTracker.Clear();
                recusados.Add($"{nome}: {ex.Message}");
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning(ex, "Planilha SG {Nome} recusada na importação", nome);
                recusados.Add($"{nome}: não abriu como planilha SG.");
            }
        }

        return Ok(new { guardadas, recusados });
    }
}
