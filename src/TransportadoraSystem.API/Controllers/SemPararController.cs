using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.SemPararPlanilhaService;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Sem Parar: um controle de pedágio por mês, guardado como dados (fatura por placa,
/// vales-pedágio e créditos inseridos). Nasce do relatório de VPO + fatura em PDF,
/// de uma planilha pronta (importação) ou à mão; a planilha é gerada destes dados.
/// O que foi mudado pela tela não é sobrescrito sem confirmar (substituir=true).
/// </summary>
[Authorize]
[ApiController]
[Route("api/semparar")]
public class SemPararController(SemPararFechamentoService fechamento, SemPararPlanilhaService planilha,
    AppDbContext db, ILogger<SemPararController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Tipos = [Fatura, Vpo, Credito];

    // ───────────────────────── Gerar do relatório de VPO + fatura ─────────────────────────

    // Lê o relatório de VPO (.xlsx) e a fatura (.pdf) e guarda como o controle do mês.
    // Se o controle do mês já foi mudado pela tela, só substitui com substituir=true.
    [HttpPost("gerar")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Gerar(IFormFile? vpo, IFormFile? fatura,
        [FromQuery] int mes, [FromQuery] int ano, [FromQuery] bool substituir = false)
    {
        try
        {
            if (vpo == null || vpo.Length == 0)
                return BadRequest(new { erro = "Envie o relatório de VPO (.xlsx)." });
            if (fatura == null || fatura.Length == 0)
                return BadRequest(new { erro = "Envie a fatura do Sem Parar (.pdf)." });
            if (mes < 1 || mes > 12 || ano < 2000 || ano > 2100)
                return BadRequest(new { erro = "Mês ou ano inválido." });

            var vpoBytes = await Bytes(vpo);
            var faturaBytes = await Bytes(fatura);
            var lido = fechamento.Ler(vpoBytes, faturaBytes);

            var existente = await db.SemPararControles.FirstOrDefaultAsync(c => c.Ano == ano && c.Mes == mes);
            if (existente is { Editado: true } && !substituir)
                return Conflict(new
                {
                    erro = $"O controle de {Mes(mes, ano)} foi mudado pela tela. Gerar de novo apaga essas mudanças.",
                    conflito = true,
                    id = existente.Id,
                });

            var linhas = new List<Linha>();
            linhas.AddRange(lido.Fatura.Select(kv => new Linha(Fatura, kv.Key, null, (decimal)kv.Value, 0)));
            linhas.AddRange(lido.Vpos.Select((v, i) =>
                new Linha(Vpo, v.Placa, DateOnly.FromDateTime(v.Data), Math.Round((decimal)v.Realizado, 2), i)));
            foreach (var (placa, valores) in lido.Creditos)
                linhas.AddRange(valores.Select((v, i) => new Linha(Credito, placa, null, Math.Round((decimal)v, 2), i)));

            var placas = PlacasCom(SemPararFechamentoService.PlacasPadrao, linhas);
            var controle = await Substituir(existente, ano, mes, "gerado", placas, linhas, lido.Avisos);
            await AnexarArquivos(controle, [(vpo.FileName, "VPO", vpoBytes), (fatura.FileName, "FATURA", faturaBytes)]);
            await db.SaveChangesAsync();

            return Ok(new { id = controle.Id, criado = existente == null, avisos = lido.Avisos, linhas = linhas.Count });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Erro ao guardar o controle Sem Parar");
            return Conflict(new { erro = "Outro controle deste mês foi salvo ao mesmo tempo. Abra a lista e confira." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar o controle Sem Parar");
            return StatusCode(500, new { erro = $"Erro ao ler os arquivos: {ex.Message}" });
        }
    }

    // ───────────────────────── Importar planilhas prontas ─────────────────────────

    public record ResultadoImportacao(string Nome, string Status, int? Id, int? Mes, int? Ano,
        int Linhas, List<string> Avisos, string? Erro);

    // As planilhas "N-SEM PARAR -MÊS-AA.xlsx" já feitas viram controles. Mês sem
    // controle: cria. Mês com controle não mudado pela tela: atualiza com a planilha
    // (rodar de novo não duplica). Mês com controle mudado pela tela: não mexe, a
    // menos que venha substituir=true (status "editado" pede a confirmação).
    [HttpPost("importar-planilhas")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarPlanilhas(IFormFileCollection arquivos, [FromQuery] bool substituir = false)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie as planilhas do Sem Parar (.xlsx)." });

        var resultado = new List<ResultadoImportacao>();
        var vistos = new HashSet<(int, int)>();
        foreach (var arq in arquivos)
        {
            if (!arq.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                resultado.Add(new(arq.FileName, "erro", null, null, null, 0, [], "Não é planilha .xlsx."));
                continue;
            }
            try
            {
                var bytes = await Bytes(arq);
                var lida = planilha.LerPlanilha(bytes, arq.FileName);
                if (!vistos.Add((lida.Ano, lida.Mes)))
                {
                    resultado.Add(new(arq.FileName, "erro", null, lida.Mes, lida.Ano, 0, [],
                        $"Outra planilha deste envio já é de {Mes(lida.Mes, lida.Ano)}."));
                    continue;
                }
                var existente = await db.SemPararControles.FirstOrDefaultAsync(c => c.Ano == lida.Ano && c.Mes == lida.Mes);
                if (existente is { Editado: true } && !substituir)
                {
                    resultado.Add(new(arq.FileName, "editado", existente.Id, lida.Mes, lida.Ano, lida.Linhas.Count, lida.Avisos,
                        $"O controle de {Mes(lida.Mes, lida.Ano)} foi mudado pela tela; não substituí."));
                    continue;
                }
                var controle = await Substituir(existente, lida.Ano, lida.Mes, "planilha", lida.Placas, lida.Linhas, lida.Avisos);
                await AnexarArquivos(controle, [(arq.FileName, "PLANILHA", bytes)]);
                await db.SaveChangesAsync();
                resultado.Add(new(arq.FileName, existente == null ? "criado" : "atualizado", controle.Id,
                    lida.Mes, lida.Ano, lida.Linhas.Count, lida.Avisos, null));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao importar a planilha Sem Parar {Nome}", arq.FileName);
                db.ChangeTracker.Clear();
                resultado.Add(new(arq.FileName, "erro", null, null, null, 0, [], ex is InvalidOperationException
                    ? ex.Message : $"Não consegui ler: {ex.Message}"));
            }
        }
        return Ok(resultado);
    }

    // Troca as linhas do controle pelas novas (ou cria o controle). Não salva.
    private async Task<SemPararControle> Substituir(SemPararControle? controle, int ano, int mes, string origem,
        List<string> placas, List<Linha> linhas, List<string> avisos)
    {
        var agora = DateTime.UtcNow;
        if (controle == null)
        {
            controle = new SemPararControle { Ano = ano, Mes = mes, CriadoEm = agora };
            db.SemPararControles.Add(controle);
        }
        else
        {
            // Removidas no mesmo SaveChanges das novas: se falhar, as antigas ficam
            db.SemPararLinhas.RemoveRange(await db.SemPararLinhas.Where(l => l.ControleId == controle.Id).ToListAsync());
        }
        controle.Placas = string.Join(",", placas);
        controle.Origem = origem;
        controle.Editado = false;
        controle.AvisosJson = JsonSerializer.Serialize(avisos);
        controle.AtualizadoEm = agora;
        controle.Linhas.AddRange(linhas.Select(l => new SemPararLinha
        {
            Tipo = l.Tipo, Placa = l.Placa, Data = l.Data, Valor = l.Valor, Ordem = l.Ordem, CriadoEm = agora,
        }));
        return controle;
    }

    // Guarda os arquivos de origem que o controle ainda não tem (mesmo nome e tamanho = o mesmo)
    private async Task AnexarArquivos(SemPararControle controle, List<(string Nome, string Tipo, byte[] Bytes)> arquivos)
    {
        var jaTem = controle.Id == 0
            ? []
            : await db.SemPararArquivos.Where(a => a.ControleId == controle.Id)
                .Select(a => new { a.Nome, Tamanho = a.Conteudo.Length }).ToListAsync();
        var chaves = jaTem.Select(a => (a.Nome, a.Tamanho)).ToHashSet();
        foreach (var (nome, tipo, bytes) in arquivos)
            if (chaves.Add((nome, bytes.Length)))
                controle.Arquivos.Add(new SemPararArquivo { Nome = nome, Tipo = tipo, Conteudo = bytes, CriadoEm = DateTime.UtcNow });
    }

    // ───────────────────────── Lista e detalhe ─────────────────────────

    [HttpGet("controles")]
    public async Task<IActionResult> Listar()
    {
        var controles = await db.SemPararControles.AsNoTracking()
            .OrderByDescending(c => c.Ano).ThenByDescending(c => c.Mes)
            .Select(c => new
            {
                c.Id, c.Ano, c.Mes, c.Placas, c.Origem, c.Editado, c.AtualizadoEm,
                Linhas = c.Linhas.Select(l => new { l.Tipo, l.Placa, l.Valor }).ToList(),
                Arquivos = c.Arquivos.Count,
            })
            .ToListAsync();

        return Ok(controles.Select(c =>
        {
            var linhas = c.Linhas.Select(l => new Linha(l.Tipo, l.Placa, null, l.Valor, 0)).ToList();
            var t = Calcular(Placas(c.Placas), linhas);
            return new
            {
                c.Id, c.Ano, c.Mes, c.Origem, c.Editado, c.AtualizadoEm, c.Arquivos,
                Nome = NomeArquivo(c.Mes, c.Ano),
                t.Fatura, t.Creditos, t.Vpo, t.Valor,
                QtdVpo = linhas.Count(l => l.Tipo == Vpo),
                QtdCreditos = linhas.Count(l => l.Tipo == Credito),
                QtdPlacas = t.PorPlaca.Count,
            };
        }));
    }

    [HttpGet("controles/{id:int}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var c = await db.SemPararControles.AsNoTracking().Include(x => x.Linhas).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { erro = "Controle não encontrado." });

        var arquivos = await db.SemPararArquivos.AsNoTracking().Where(a => a.ControleId == id)
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Nome, a.Tipo, Tamanho = a.Conteudo.Length, a.CriadoEm })
            .ToListAsync();
        var vizinhos = await db.SemPararControles.AsNoTracking()
            .Select(x => new { x.Id, Chave = x.Ano * 100 + x.Mes }).ToListAsync();
        int chave = c.Ano * 100 + c.Mes;

        var linhas = Ordenadas(c.Linhas).ToList();
        var dados = linhas.Select(ParaLinha).ToList();
        var t = Calcular(Placas(c.Placas), dados);
        return Ok(new
        {
            c.Id, c.Ano, c.Mes, c.Origem, c.Editado, c.CriadoEm, c.AtualizadoEm,
            Nome = NomeArquivo(c.Mes, c.Ano),
            Placas = t.PorPlaca.Select(p => p.Placa).ToList(),
            Avisos = JsonSerializer.Deserialize<List<string>>(c.AvisosJson) ?? [],
            Linhas = linhas.Select(l => new { l.Id, l.Tipo, l.Placa, l.Data, l.Valor, l.Ordem }),
            Totais = t,
            Arquivos = arquivos,
            AnteriorId = vizinhos.Where(v => v.Chave < chave).OrderByDescending(v => v.Chave).Select(v => (int?)v.Id).FirstOrDefault(),
            ProximoId = vizinhos.Where(v => v.Chave > chave).OrderBy(v => v.Chave).Select(v => (int?)v.Id).FirstOrDefault(),
        });
    }

    // Controle novo, vazio, com as placas da frota (para lançar à mão)
    public record NovoControle(int Mes, int Ano);

    [HttpPost("controles")]
    public async Task<IActionResult> Criar([FromBody] NovoControle dto)
    {
        if (dto.Mes < 1 || dto.Mes > 12 || dto.Ano < 2000 || dto.Ano > 2100)
            return BadRequest(new { erro = "Mês ou ano inválido." });
        var existe = await db.SemPararControles.Where(c => c.Ano == dto.Ano && c.Mes == dto.Mes).Select(c => (int?)c.Id).FirstOrDefaultAsync();
        if (existe != null)
            return Conflict(new { erro = $"Já existe o controle de {Mes(dto.Mes, dto.Ano)}.", id = existe });
        var c = new SemPararControle
        {
            Ano = dto.Ano, Mes = dto.Mes, Origem = "manual", Editado = true,
            Placas = string.Join(",", SemPararFechamentoService.PlacasPadrao),
        };
        db.SemPararControles.Add(c);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return Conflict(new { erro = $"Já existe o controle de {Mes(dto.Mes, dto.Ano)}." }); }
        return Ok(new { id = c.Id });
    }

    [HttpDelete("controles/{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var n = await db.SemPararControles.Where(c => c.Id == id).ExecuteDeleteAsync();
        return n == 0 ? NotFound(new { erro = "Controle não encontrado." }) : NoContent();
    }

    // Placas (colunas) do controle, na ordem. Placa com linhas não sai.
    public record PlacasDto(List<string> Placas);

    [HttpPut("controles/{id:int}/placas")]
    public async Task<IActionResult> AtualizarPlacas(int id, [FromBody] PlacasDto dto)
    {
        var c = await db.SemPararControles.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { erro = "Controle não encontrado." });
        var novas = (dto.Placas ?? []).Select(NormalizarPlaca).Where(p => p.Length > 0).Distinct().ToList();
        var comLinhas = await db.SemPararLinhas.Where(l => l.ControleId == id).Select(l => l.Placa).Distinct().ToListAsync();
        var faltando = comLinhas.Where(p => !novas.Contains(p)).ToList();
        if (faltando.Count > 0)
            return BadRequest(new { erro = $"A placa {string.Join(", ", faltando)} tem valores neste mês; apague as linhas dela antes." });
        c.Placas = string.Join(",", novas);
        Mudou(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────── Linhas ─────────────────────────

    public record LinhaDto(string Tipo, string Placa, DateOnly? Data, decimal Valor);

    [HttpPost("controles/{id:int}/linhas")]
    public async Task<IActionResult> CriarLinha(int id, [FromBody] LinhaDto dto)
    {
        var c = await db.SemPararControles.Include(x => x.Linhas).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { erro = "Controle não encontrado." });
        var erro = Validar(dto, out var tipo, out var placa);
        if (erro != null) return BadRequest(new { erro });

        int ordem = 0;
        if (tipo == Fatura)
        {
            // Uma fatura por placa: lançar de novo troca o valor
            var ja = c.Linhas.FirstOrDefault(l => l.Tipo == Fatura && l.Placa == placa);
            if (ja != null)
            {
                ja.Valor = Math.Round(dto.Valor, 2);
                foreach (var dup in c.Linhas.Where(l => l.Tipo == Fatura && l.Placa == placa && l != ja).ToList())
                    db.SemPararLinhas.Remove(dup);
                IncluirPlaca(c, placa);
                Mudou(c);
                await db.SaveChangesAsync();
                return Ok(new { id = ja.Id });
            }
        }
        else if (tipo == Credito)
        {
            ordem = c.Linhas.Where(l => l.Tipo == Credito && l.Placa == placa).Select(l => l.Ordem + 1).DefaultIfEmpty(0).Max();
        }
        else
        {
            // Vale-pedágio novo entra depois do último com data até a dele (a planilha
            // vai em ordem de data); os de baixo descem uma linha
            var vpos = c.Linhas.Where(l => l.Tipo == Vpo).ToList();
            ordem = dto.Data == null
                ? vpos.Select(l => l.Ordem + 1).DefaultIfEmpty(0).Max()
                : vpos.Where(l => l.Data != null && l.Data <= dto.Data).Select(l => l.Ordem + 1).DefaultIfEmpty(0).Max();
            foreach (var l in vpos.Where(l => l.Ordem >= ordem)) l.Ordem++;
        }

        var nova = new SemPararLinha
        {
            Tipo = tipo, Placa = placa, Data = tipo == Vpo ? dto.Data : null,
            Valor = Math.Round(dto.Valor, 2), Ordem = ordem, CriadoEm = DateTime.UtcNow,
        };
        c.Linhas.Add(nova);
        IncluirPlaca(c, placa);
        Mudou(c);
        await db.SaveChangesAsync();
        return Ok(new { id = nova.Id });
    }

    [HttpPut("linhas/{linhaId:int}")]
    public async Task<IActionResult> AtualizarLinha(int linhaId, [FromBody] LinhaDto dto)
    {
        var l = await db.SemPararLinhas.Include(x => x.Controle).FirstOrDefaultAsync(x => x.Id == linhaId);
        if (l?.Controle == null) return NotFound(new { erro = "Linha não encontrada (pode ter sido apagada)." });
        var erro = Validar(dto, out var tipo, out var placa);
        if (erro != null) return BadRequest(new { erro });
        if (tipo != l.Tipo) return BadRequest(new { erro = "Não dá para mudar o tipo da linha; apague e lance de novo." });
        if (tipo == Fatura && placa != l.Placa &&
            await db.SemPararLinhas.AnyAsync(x => x.ControleId == l.ControleId && x.Tipo == Fatura && x.Placa == placa))
            return BadRequest(new { erro = $"A placa {placa} já tem fatura neste mês." });

        if (tipo == Credito && placa != l.Placa)
            l.Ordem = await db.SemPararLinhas.Where(x => x.ControleId == l.ControleId && x.Tipo == Credito && x.Placa == placa)
                .Select(x => x.Ordem + 1).DefaultIfEmpty(0).MaxAsync();
        l.Placa = placa;
        l.Data = tipo == Vpo ? dto.Data : null;
        l.Valor = Math.Round(dto.Valor, 2);
        IncluirPlaca(l.Controle, placa);
        Mudou(l.Controle);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("linhas/{linhaId:int}")]
    public async Task<IActionResult> ExcluirLinha(int linhaId)
    {
        var l = await db.SemPararLinhas.Include(x => x.Controle).FirstOrDefaultAsync(x => x.Id == linhaId);
        if (l?.Controle == null) return NoContent();   // já apagada: repetir não dá erro
        db.SemPararLinhas.Remove(l);
        Mudou(l.Controle);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────── Excel e arquivos ─────────────────────────

    [HttpGet("controles/{id:int}/excel")]
    public async Task<IActionResult> Excel(int id)
    {
        var c = await db.SemPararControles.AsNoTracking().Include(x => x.Linhas).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound(new { erro = "Controle não encontrado." });
        try
        {
            var linhas = Ordenadas(c.Linhas).Select(ParaLinha).ToList();
            var bytes = planilha.Exportar(c.Mes, c.Ano, Placas(c.Placas), linhas);
            return File(bytes, Xlsx, NomeArquivo(c.Mes, c.Ano));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao exportar o controle Sem Parar {Id}", id);
            return StatusCode(500, new { erro = $"Erro ao gerar a planilha: {ex.Message}" });
        }
    }

    [HttpGet("arquivos/{arquivoId:int}")]
    public async Task<IActionResult> Arquivo(int arquivoId)
    {
        var a = await db.SemPararArquivos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == arquivoId);
        if (a == null) return NotFound(new { erro = "Arquivo não encontrado." });
        var tipo = a.Nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf" : Xlsx;
        return File(a.Conteudo, tipo, a.Nome);
    }

    // ───────────────────────── Apoio ─────────────────────────

    private static string? Validar(LinhaDto dto, out string tipo, out string placa)
    {
        tipo = (dto.Tipo ?? "").Trim().ToUpperInvariant();
        placa = NormalizarPlaca(dto.Placa);
        if (!Tipos.Contains(tipo)) return "Tipo de linha inválido.";
        if (placa.Length == 0) return "Informe a placa.";
        if (dto.Valor < -1_000_000 || dto.Valor > 1_000_000) return "Valor fora do normal; confira.";
        if (tipo == Vpo && dto.Data == null) return "Informe a data do vale-pedágio.";
        return null;
    }

    private static void IncluirPlaca(SemPararControle c, string placa)
    {
        var lista = Placas(c.Placas);
        if (lista.Contains(placa)) return;
        lista.Add(placa);
        c.Placas = string.Join(",", lista);
    }

    private static void Mudou(SemPararControle c)
    {
        c.Editado = true;
        c.AtualizadoEm = DateTime.UtcNow;
    }

    private static List<string> Placas(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static IEnumerable<SemPararLinha> Ordenadas(IEnumerable<SemPararLinha> linhas) =>
        linhas.OrderBy(l => Array.IndexOf(Tipos, l.Tipo)).ThenBy(l => l.Ordem).ThenBy(l => l.Id);

    private static Linha ParaLinha(SemPararLinha l) => new(l.Tipo, l.Placa, l.Data, l.Valor, l.Ordem);

    private static string Mes(int mes, int ano) =>
        $"{char.ToUpper(MesNome(mes)[0])}{MesNome(mes)[1..].ToLowerInvariant()} de {ano}";

    private static async Task<byte[]> Bytes(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }
}
