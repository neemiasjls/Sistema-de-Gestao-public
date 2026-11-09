using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.AbastecimentoFechamentoService;
using static TransportadoraSystem.Application.Services.CobrancaAbastecimentoService;

namespace TransportadoraSystem.API.Controllers;

// Cobranças dos postos guardadas no sistema: faturas, boletos e as planilhas de
// divisão. A tela Abastecimentos importa faturas e boletos juntos, confere uma
// coisa com a outra e baixa qualquer um dos arquivos depois.
[Authorize]
[ApiController]
[Route("api/abastecimento/cobrancas")]
public class CobrancasAbastecimentoController(
    AbastecimentoFechamentoService svc, CobrancasAbastecimentoGuarda guarda, AppDbContext db,
    ILogger<CobrancasAbastecimentoController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // ───────────────────────── Painel e lista ─────────────────────────

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var faturas = (await db.FaturasPosto.AsNoTracking()
            .Select(f => new
            {
                f.Id, f.Posto, f.Numero, f.Emissao, f.Vencimento, f.ValorFatura, f.SomaAbastecimentos,
                f.SomaNaPlanilha, f.Quantidade, f.ValorBento, f.QuantidadeBento, f.NomeArquivo, f.CriadoEm,
                f.AbastecimentosJson,
            })
            .ToListAsync())
            .Select(f => new FaturaResumo(f.Id, f.Posto, f.Numero, f.Emissao, f.Vencimento, f.ValorFatura,
                f.SomaAbastecimentos, f.SomaNaPlanilha, f.Quantidade, f.ValorBento, f.QuantidadeBento,
                f.NomeArquivo, f.CriadoEm, LinhaBento(f.Posto, PlacasBento(f.AbastecimentosJson))))
            .ToList();
        var boletos = await db.BoletosPosto.AsNoTracking()
            .Select(b => new
            {
                b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, b.Vencimento, b.DataDocumento,
                b.DataChegada, b.PagoEm, b.FaturaId, b.LigacaoManual, b.LidoDoPdf, b.AvisosJson, b.NomeArquivo,
                TemPdf = b.Pdf != null,
            })
            .ToListAsync();
        var planilhas = await db.PlanilhasAbastecimento.AsNoTracking()
            .OrderByDescending(p => p.CriadoEm)
            .Select(p => new { p.Id, p.Nome, p.Faturas, p.FaturasIds, p.Soma, p.Quantidade, p.CriadoEm })
            .ToListAsync();

        var porFatura = faturas.ToDictionary(f => f.Id);
        var porBoleto = boletos.ToDictionary(b => b.Id, b => new BoletoResumo(
            b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, b.Vencimento, b.DataDocumento,
            b.DataChegada, b.PagoEm, b.FaturaId, b.LigacaoManual, b.LidoDoPdf,
            CobrancasAbastecimentoGuarda.LerLista(b.AvisosJson), b.NomeArquivo, b.TemPdf));

        var cobrancas = Conferir(
            faturas.Select(CobrancasAbastecimentoGuarda.ParaConferir),
            porBoleto.Values.Select(b => new BoletoConf(b.Id, b.FaturaId, b.Posto, b.NumeroDocumento,
                b.Valor, b.Vencimento, b.DataChegada, b.PagoEm)));

        var hoje = CobrancasAbastecimentoGuarda.Hoje();
        return Ok(new
        {
            Hoje = hoje,
            Resumo = Resumir(cobrancas, hoje),
            Cobrancas = cobrancas.Select(c => new
            {
                Chave = c.Fatura != null ? $"f{c.Fatura.Id}" : $"b{c.Boletos[0].Id}",
                c.Status, c.Diferencas, c.Posto, c.Numero, c.Valor, c.Vencimento, c.DataChegada, c.Paga,
                Fatura = c.Fatura != null ? porFatura[c.Fatura.Id] : null,
                Boletos = c.Boletos.Select(b => porBoleto[b.Id]).ToList(),
            }),
            Planilhas = planilhas,
        });
    }

    public record FaturaResumo(int Id, string Posto, string Numero, DateOnly? Emissao, DateOnly? Vencimento,
        decimal? ValorFatura, decimal Soma, decimal SomaNaPlanilha, int Quantidade, decimal ValorBento,
        int QuantidadeBento, string NomeArquivo, DateTime CriadoEm, string? DescricaoBento);

    // O Bento lança a parte dele na planilha do banco numa linha só:
    // vencimento | valor | "POSTO 7 VILA NORTE (VOLUME-XAA)" — o posto e as três
    // letras das placas dele, na ordem em que aparecem na fatura.
    public static string? LinhaBento(string posto, IEnumerable<string> placas)
    {
        var siglas = placas
            .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())
            .Where(p => p.Length >= 3)
            .Select(p => p[..3])
            .Distinct()
            .ToList();
        if (siglas.Count == 0) return null;
        var nome = posto.Trim().ToUpperInvariant();
        if (!nome.StartsWith("POSTO")) nome = "POSTO " + nome;
        return $"{nome} ({string.Join("-", siglas)})";
    }

    private static List<string> PlacasBento(string abastecimentosJson)
    {
        try
        {
            return (JsonSerializer.Deserialize<List<CobrancasAbastecimentoGuarda.AbastecimentoGuardado>>(
                        abastecimentosJson, CobrancasAbastecimentoGuarda.Json) ?? [])
                .Where(a => a.Dono == "BENTO").Select(a => a.Placa).ToList();
        }
        catch (JsonException) { return []; }
    }

    public record BoletoResumo(int Id, string Posto, string NumeroDocumento, string NossoNumero, decimal? Valor,
        DateOnly? Vencimento, DateOnly? DataDocumento, DateOnly DataChegada, DateOnly? PagoEm, int? FaturaId,
        bool LigacaoManual, bool LidoDoPdf, List<string> Avisos, string NomeArquivo, bool TemPdf);

    // Detalhe da fatura: os abastecimentos lidos, por dono
    [HttpGet("faturas/{id:int}")]
    public async Task<IActionResult> DetalheFatura(int id)
    {
        var f = await db.FaturasPosto.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Posto, x.Numero, x.Emissao, x.Vencimento, x.ValorFatura, x.SomaAbastecimentos,
                x.SomaNaPlanilha, x.Quantidade, x.ValorBento, x.QuantidadeBento, x.AbastecimentosJson,
                x.AvisosJson, x.NomeArquivo, x.CriadoEm, x.AtualizadoEm,
            })
            .FirstOrDefaultAsync();
        if (f == null) return NotFound(new { erro = "Fatura não encontrada." });

        List<CobrancasAbastecimentoGuarda.AbastecimentoGuardado> abastecimentos;
        try { abastecimentos = JsonSerializer.Deserialize<List<CobrancasAbastecimentoGuarda.AbastecimentoGuardado>>(f.AbastecimentosJson, CobrancasAbastecimentoGuarda.Json) ?? []; }
        catch (JsonException) { abastecimentos = []; }

        return Ok(new
        {
            f.Id, f.Posto, f.Numero, f.Emissao, f.Vencimento, f.ValorFatura,
            Soma = f.SomaAbastecimentos, f.SomaNaPlanilha, f.Quantidade, f.ValorBento, f.QuantidadeBento,
            Abastecimentos = abastecimentos,
            Avisos = CobrancasAbastecimentoGuarda.LerLista(f.AvisosJson),
            f.NomeArquivo, f.CriadoEm, f.AtualizadoEm,
        });
    }

    // ───────────────────────── Importar ─────────────────────────

    // Faturas e boletos juntos: cada PDF é reconhecido pelo conteúdo. A mesma
    // fatura (posto + número) importada de novo atualiza a guardada; boleto
    // repetido fica de fora. Repetir a importação não duplica nada.
    [HttpPost("importar")]
    public async Task<IActionResult> Importar(IFormFileCollection arquivos)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs das faturas e dos boletos." });

            var lidos = await CobrancasAbastecimentoGuarda.LerArquivos(arquivos);
            var avisos = new List<string>();
            var docs = lidos.Select(a => (a.Nome, a.Bytes, Doc: Ler(a.Bytes, a.Nome))).ToList();

            // Boleto com senha: tenta de novo com os CNPJs das faturas do mesmo envio
            var cnpjs = docs.Where(d => d.Doc.Fatura != null).SelectMany(d => d.Doc.Fatura!.Cnpjs).Distinct().ToList();
            if (cnpjs.Count > 0)
            {
                var senhas = cnpjs.SelectMany(SenhasDoCnpj).Distinct().ToList();
                docs = docs.Select(d => d.Doc.Boleto is { Lido: false } ? (d.Nome, d.Bytes, Doc: Ler(d.Bytes, d.Nome, senhas)) : d).ToList();
            }

            // Nenhuma placa fica fora da divisão (ABA-06): placa fora do cadastro de
            // veículos volta para a tela pedir o dono, antes de guardar qualquer coisa
            // (repetir o envio depois de cadastrar é seguro).
            var donos = await DonosDasPlacas.CarregarAsync(db);
            var semCadastro = SemCadastro(
                docs.Where(d => d.Doc.Fatura != null).SelectMany(d => d.Doc.Fatura!.Abastecimentos), donos);
            if (semCadastro.Count > 0) return CobrancasAbastecimentoGuarda.PedirDonos(semCadastro);

            await using var tx = await db.Database.BeginTransactionAsync();
            var agora = DateTime.UtcNow;
            var hoje = CobrancasAbastecimentoGuarda.Hoje();

            var faturas = new List<object>();
            var idsFaturas = new List<int>();
            foreach (var (nome, bytes, doc) in docs.Where(d => d.Doc.Fatura != null))
            {
                var f = doc.Fatura!;
                if (f.Abastecimentos.Count == 0)
                    avisos.Add($"{nome}: nenhum abastecimento reconhecido na fatura.");
                if (f.Numero == "")
                    avisos.Add($"{nome}: não achei o número da fatura; preencha na lista para o boleto se ligar a ela.");
                if (f.ValorFatura == null)
                    avisos.Add($"{nome}: não achei o valor total da fatura.");

                var (fatura, atualizada) = await guarda.GuardarFatura(f, nome, bytes, agora, donos);
                await db.SaveChangesAsync();
                if (atualizada) avisos.Add($"A fatura {fatura.Posto} {fatura.Numero} já estava guardada e foi atualizada.");
                avisos.AddRange(CobrancasAbastecimentoGuarda.LerLista(fatura.AvisosJson)
                    .Select(a => $"Fatura {fatura.Posto} {fatura.Numero}: {a}"));
                idsFaturas.Add(fatura.Id);
                faturas.Add(new
                {
                    fatura.Id, fatura.Posto, fatura.Numero, fatura.Emissao, fatura.Vencimento, fatura.ValorFatura,
                    Soma = fatura.SomaAbastecimentos, fatura.SomaNaPlanilha, fatura.Quantidade,
                    fatura.ValorBento, fatura.QuantidadeBento, Atualizada = atualizada,
                    DescricaoBento = LinhaBento(fatura.Posto, f.DoBento(donos).Select(a => a.Placa)),
                });
            }

            var boletos = new List<BoletoPosto>();
            foreach (var (nome, bytes, doc) in docs.Where(d => d.Doc.Boleto != null))
            {
                var b = doc.Boleto!;
                avisos.AddRange(b.Avisos);
                var (boleto, repetido) = await guarda.GuardarBoleto(b, nome, bytes, hoje, agora);
                if (repetido) { avisos.Add($"{nome}: esse boleto já estava guardado e ficou de fora."); continue; }
                boletos.Add(boleto);
            }
            await db.SaveChangesAsync();

            await guarda.Religar();
            await db.SaveChangesAsync();
            await tx.CommitAsync();

            var bento = docs.Where(d => d.Doc.Fatura != null).Select(d => d.Doc.Fatura!).ToList();
            return Ok(new
            {
                Faturas = faturas,
                IdsFaturas = idsFaturas,
                Boletos = boletos.Select(b => new
                {
                    b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, b.Vencimento, b.LidoDoPdf, b.FaturaId,
                }),
                Bento = new
                {
                    Valor = Math.Round(bento.Sum(f => f.ValorBento(donos)), 2),
                    Quantidade = bento.Sum(f => f.DoBento(donos).Count),
                },
                Avisos = avisos,
            });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Erro ao guardar as cobranças dos postos");
            return Conflict(new { erro = "Não consegui guardar: outra importação da mesma fatura pode ter acontecido ao mesmo tempo. Tente de novo." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao importar as cobranças dos postos");
            return StatusCode(500, new { erro = $"Erro ao importar: {ex.Message}" });
        }
    }

    public record PlanilhaPedido(List<int>? FaturaIds);

    // Planilha de divisão das faturas guardadas, na ordem pedida. Sai do PDF
    // guardado de cada fatura, pelo mesmo gerador do POST api/abastecimento/gerar.
    [HttpPost("planilha")]
    public async Task<IActionResult> GerarPlanilha([FromBody] PlanilhaPedido req)
    {
        try
        {
            var ids = (req.FaturaIds ?? []).Distinct().ToList();
            if (ids.Count == 0) return BadRequest(new { erro = "Escolha as faturas da planilha." });

            var faturas = await db.FaturasPosto.Where(f => ids.Contains(f.Id)).ToListAsync();
            if (faturas.Count != ids.Count) return NotFound(new { erro = "Alguma das faturas não existe mais." });
            faturas = ids.Select(id => faturas.First(f => f.Id == id)).ToList();

            // Cada posto cobra no seu boleto, então a planilha de divisão é por
            // posto: juntar Posto 7 e Estrada numa só misturava as duas cobranças.
            var postos = faturas.Select(f => f.Posto.Trim().ToUpperInvariant()).Distinct().ToList();
            if (postos.Count > 1)
                return BadRequest(new { erro = $"A planilha é de um posto só. Essas faturas são de {string.Join(" e ", postos)}: gere uma para cada." });

            var donos = await DonosDasPlacas.CarregarAsync(db);
            FechamentoResult res;
            try { res = svc.Gerar(faturas.Select(f => f.Pdf), donos); }
            catch (PlacasSemCadastroException ex) { return CobrancasAbastecimentoGuarda.PedirDonos(ex.Placas); }
            var avisos = new List<string>(res.Avisos);
            try
            {
                await guarda.GuardarPlanilha(res, faturas);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao guardar a planilha de abastecimentos");
                db.ChangeTracker.Clear();
                avisos.Add("A planilha foi gerada, mas não consegui guardá-la no sistema.");
            }

            CobrancasAbastecimentoGuarda.Cabecalhos(Response, res, avisos);
            return File(res.Bytes, Xlsx, res.NomeArquivo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar a planilha das faturas guardadas");
            return StatusCode(500, new { erro = $"Erro ao gerar planilha: {ex.Message}" });
        }
    }

    // ───────────────────────── Editar ─────────────────────────

    public record FaturaEdicao(string? Posto, string? Numero, string? Emissao, string? Vencimento, decimal? ValorFatura);

    // Para o que não veio do PDF (número, vencimento, valor). Os abastecimentos
    // não se editam: vêm do PDF, que é o que vai para a planilha.
    [HttpPut("faturas/{id:int}")]
    public async Task<IActionResult> EditarFatura(int id, [FromBody] FaturaEdicao req)
    {
        var f = await db.FaturasPosto.FirstOrDefaultAsync(x => x.Id == id);
        if (f == null) return NotFound(new { erro = "Fatura não encontrada." });

        var posto = NomePosto(req.Posto);
        if (posto == "") return BadRequest(new { erro = "Informe o posto." });
        var numero = (req.Numero ?? "").Trim();
        if (!CobrancasAbastecimentoGuarda.Data(req.Emissao, out var emissao))
            return BadRequest(new { erro = "Data de emissão inválida." });
        if (!CobrancasAbastecimentoGuarda.Data(req.Vencimento, out var vencimento))
            return BadRequest(new { erro = "Vencimento inválido." });
        if (req.ValorFatura is < 0) return BadRequest(new { erro = "O valor não pode ser negativo." });
        if (numero != "" && await db.FaturasPosto.AnyAsync(x => x.Id != id && x.Posto == posto && x.Numero == numero))
            return Conflict(new { erro = $"Já existe outra fatura {posto} número {numero}." });

        f.Posto = posto;
        f.Numero = numero;
        f.Emissao = emissao;
        f.Vencimento = vencimento;
        f.ValorFatura = req.ValorFatura is decimal v ? Math.Round(v, 2) : null;
        f.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await guarda.Religar();
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record BoletoEdicao(string? Posto, string? NumeroDocumento, string? NossoNumero, decimal? Valor,
        string? Vencimento, string? DataChegada, string? PagoEm, int? FaturaId, bool LigacaoAutomatica);

    // Dados do boleto e a ligação com a fatura. Com LigacaoAutomatica o sistema
    // liga pelo número e pelo posto; sem ela vale a fatura escolhida (ou nenhuma).
    [HttpPut("boletos/{id:int}")]
    public async Task<IActionResult> EditarBoleto(int id, [FromBody] BoletoEdicao req)
    {
        var b = await db.BoletosPosto.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });

        if (!CobrancasAbastecimentoGuarda.Data(req.Vencimento, out var vencimento))
            return BadRequest(new { erro = "Vencimento inválido." });
        if (!CobrancasAbastecimentoGuarda.Data(req.DataChegada, out var chegada) || chegada == null)
            return BadRequest(new { erro = "Data de chegada inválida." });
        if (!CobrancasAbastecimentoGuarda.Data(req.PagoEm, out var pagoEm))
            return BadRequest(new { erro = "Data do pagamento inválida." });
        if (req.Valor is < 0) return BadRequest(new { erro = "O valor não pode ser negativo." });
        if (!req.LigacaoAutomatica && req.FaturaId is int fid && !await db.FaturasPosto.AnyAsync(f => f.Id == fid))
            return BadRequest(new { erro = "A fatura escolhida não existe mais." });

        b.Posto = NomePosto(req.Posto);
        b.NumeroDocumento = (req.NumeroDocumento ?? "").Trim();
        b.NossoNumero = (req.NossoNumero ?? "").Trim();
        b.Valor = req.Valor is decimal v ? Math.Round(v, 2) : null;
        b.Vencimento = vencimento;
        b.DataChegada = chegada.Value;
        b.PagoEm = pagoEm;
        b.LigacaoManual = !req.LigacaoAutomatica;
        if (b.LigacaoManual) b.FaturaId = req.FaturaId;
        b.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await guarda.Religar();
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────── Baixar ─────────────────────────

    [HttpGet("faturas/{id:int}/pdf")]
    public async Task<IActionResult> BaixarFatura(int id)
    {
        var f = await db.FaturasPosto.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.NomeArquivo, x.Pdf, x.Posto, x.Numero }).FirstOrDefaultAsync();
        if (f == null) return NotFound(new { erro = "Fatura não encontrada." });
        return File(f.Pdf, "application/pdf", NomeOuPadrao(f.NomeArquivo, $"FATURA {f.Posto} {f.Numero}.pdf"));
    }

    [HttpGet("boletos/{id:int}/pdf")]
    public async Task<IActionResult> BaixarBoleto(int id)
    {
        var b = await db.BoletosPosto.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.NomeArquivo, x.Pdf, x.Posto, x.NumeroDocumento }).FirstOrDefaultAsync();
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (b.Pdf == null) return NotFound(new { erro = "Esse boleto não tem PDF guardado." });
        return File(b.Pdf, "application/pdf", NomeOuPadrao(b.NomeArquivo, $"BOLETO {b.Posto} {b.NumeroDocumento}.pdf"));
    }

    [HttpGet("planilhas/{id:int}")]
    public async Task<IActionResult> BaixarPlanilha(int id)
    {
        var p = await db.PlanilhasAbastecimento.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Nome, x.Conteudo }).FirstOrDefaultAsync();
        if (p == null) return NotFound(new { erro = "Planilha não encontrada." });
        return File(p.Conteudo, Xlsx, NomeOuPadrao(p.Nome, "ABASTECIMENTOS.xlsx"));
    }

    private static string NomeOuPadrao(string nome, string padrao) =>
        string.IsNullOrWhiteSpace(nome) ? padrao.Replace("  ", " ").Trim() : nome;

    // ───────────────────────── Excluir ─────────────────────────

    // O boleto da fatura fica, desligado: se a fatura voltar, ele se liga de novo
    [HttpDelete("faturas/{id:int}")]
    public async Task<IActionResult> ExcluirFatura(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.BoletosPosto.Where(b => b.FaturaId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.FaturaId, (int?)null).SetProperty(b => b.LigacaoManual, false));
        var n = await db.FaturasPosto.Where(f => f.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Fatura não encontrada." });
        await tx.CommitAsync();
        return NoContent();
    }

    [HttpDelete("boletos/{id:int}")]
    public async Task<IActionResult> ExcluirBoleto(int id)
    {
        var n = await db.BoletosPosto.Where(b => b.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Boleto não encontrado." });
        return NoContent();
    }

    [HttpDelete("planilhas/{id:int}")]
    public async Task<IActionResult> ExcluirPlanilha(int id)
    {
        var n = await db.PlanilhasAbastecimento.Where(p => p.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Planilha não encontrada." });
        return NoContent();
    }
}

/// <summary>
/// Guarda faturas, boletos e planilhas. Fica à parte porque o POST antigo
/// (api/abastecimento/gerar) também guarda o que gera.
/// </summary>
public class CobrancasAbastecimentoGuarda(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public record Arquivo(string Nome, byte[] Bytes);
    public record AbastecimentoGuardado(DateOnly Data, string Placa, string Motorista, double Valor, string? Dono);

    // O dia no Brasil: o servidor roda em UTC e à noite já seria amanhã
    public static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
    private static DateOnly DiaBrasil(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(-3));

    public static async Task<List<Arquivo>> LerArquivos(IFormFileCollection arquivos)
    {
        var lista = new List<Arquivo>();
        var vistos = new HashSet<(string, long)>();
        foreach (var arq in arquivos)
        {
            var nome = Path.GetFileName(arq.FileName);
            if (!vistos.Add((nome, arq.Length))) continue;   // o mesmo arquivo duas vezes no envio
            using var ms = new MemoryStream();
            await arq.CopyToAsync(ms);
            lista.Add(new Arquivo(nome, ms.ToArray()));
        }
        return lista;
    }

    public static List<string> LerLista(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    // Data da tela: "aaaa-mm-dd" (input date) ou "dd/mm/aaaa"; vazio é "sem data"
    public static bool Data(string? s, out DateOnly? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (!DateOnly.TryParseExact(s.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        if (d.Year is < 2000 or > 2100) return false;
        data = d;
        return true;
    }

    public static FaturaConf ParaConferir(CobrancasAbastecimentoController.FaturaResumo f) =>
        new(f.Id, f.Posto, f.Numero, f.Emissao, f.Vencimento, f.ValorFatura, f.Soma, f.SomaNaPlanilha,
            f.Quantidade, DiaBrasil(f.CriadoEm));

    // Os mesmos cabeçalhos de conferência do POST api/abastecimento/gerar
    public static void Cabecalhos(HttpResponse response, FechamentoResult res, List<string> avisos)
    {
        var inv = CultureInfo.InvariantCulture;
        if (avisos.Count > 0)
            response.Headers["X-Avisos"] = Uri.EscapeDataString(string.Join(" | ", avisos));
        response.Headers["X-Soma"] = res.Soma.ToString(inv);
        response.Headers["X-Qtde"] = res.Quantidade.ToString(inv);
        if (res.TotalFatura.HasValue)
            response.Headers["X-Total-Fatura"] = res.TotalFatura.Value.ToString(inv);
    }

    /// <summary>
    /// Guarda a fatura. Com número, a do mesmo posto e número é atualizada (o
    /// PDF novo substitui o velho); sem número, o mesmo arquivo não entra duas vezes.
    /// </summary>
    /// <summary>
    /// 409 com as placas fora do cadastro: a tela pergunta o dono de cada uma,
    /// cadastra (POST api/veiculo/donos) e manda de novo.
    /// </summary>
    public static ObjectResult PedirDonos(List<PlacaSemCadastro> placas) =>
        new(new
        {
            erro = placas.Count == 1
                ? $"A placa {placas[0].Placa} não está no cadastro de veículos: diga de quem é o caminhão."
                : $"As placas {string.Join(", ", placas.Select(p => p.Placa))} não estão no cadastro de veículos: diga de quem é cada caminhão.",
            placasSemCadastro = placas,
        })
        { StatusCode = StatusCodes.Status409Conflict };

    public async Task<(FaturaPosto Fatura, bool Atualizada)> GuardarFatura(FaturaLida f, string nome, byte[] pdf, DateTime agora, DonosDasPlacas donos)
    {
        FaturaPosto? fatura = f.Numero != ""
            ? await db.FaturasPosto.FirstOrDefaultAsync(x => x.Posto == f.Posto && x.Numero == f.Numero)
            : await db.FaturasPosto.FirstOrDefaultAsync(x => x.Numero == "" && x.NomeArquivo == nome && x.Pdf.Length == pdf.Length);
        bool atualizada = fatura != null;
        if (fatura == null)
        {
            fatura = new FaturaPosto { CriadoEm = agora };
            db.FaturasPosto.Add(fatura);
        }

        var avisos = new List<string>();
        if (f.ValorFatura is double vf && Math.Abs(f.Soma - vf) > 0.05)
            avisos.Add($"Os abastecimentos somaram R$ {f.Soma:N2} mas a fatura diz R$ {vf:N2}.");

        fatura.Posto = f.Posto;
        fatura.Numero = f.Numero;
        fatura.Emissao = f.Emissao;
        fatura.Vencimento = f.Vencimento;
        fatura.ValorFatura = f.ValorFatura is double v ? Math.Round((decimal)v, 2) : null;
        fatura.SomaAbastecimentos = Math.Round((decimal)f.Soma, 2);
        fatura.SomaNaPlanilha = Math.Round((decimal)f.SomaNaPlanilha(donos), 2);
        fatura.Quantidade = f.Abastecimentos.Count;
        fatura.ValorBento = Math.Round((decimal)f.ValorBento(donos), 2);
        fatura.QuantidadeBento = f.DoBento(donos).Count;
        fatura.AbastecimentosJson = JsonSerializer.Serialize(
            f.Abastecimentos.Select(a => new AbastecimentoGuardado(DateOnly.FromDateTime(a.Data), a.Placa, a.Motorista, a.Valor, donos.Dono(a.Placa))),
            Json);
        fatura.AvisosJson = JsonSerializer.Serialize(avisos, Json);
        fatura.NomeArquivo = nome;
        fatura.Pdf = pdf;
        fatura.AtualizadoEm = agora;
        return (fatura, atualizada);
    }

    /// <summary>
    /// Guarda o boleto, a não ser que já esteja guardado: o mesmo nosso número,
    /// o mesmo arquivo, ou o mesmo posto, número e valor.
    /// </summary>
    public async Task<(BoletoPosto Boleto, bool Repetido)> GuardarBoleto(BoletoLido b, string nome, byte[] pdf, DateOnly hoje, DateTime agora)
    {
        var valor = b.Valor is double v ? Math.Round((decimal)v, 2) : (decimal?)null;
        BoletoPosto? ja = null;
        if (b.Lido && b.NossoNumero != "")
            ja = await db.BoletosPosto.FirstOrDefaultAsync(x => x.NossoNumero == b.NossoNumero);
        ja ??= await db.BoletosPosto.FirstOrDefaultAsync(x => x.NomeArquivo == nome && x.Pdf != null && x.Pdf.Length == pdf.Length);
        if (ja == null && b.Lido && b.NumeroDocumento != "" && valor != null)
            ja = await db.BoletosPosto.FirstOrDefaultAsync(x => x.Posto == b.Posto && x.NumeroDocumento == b.NumeroDocumento && x.Valor == valor);
        // também dentro do mesmo envio, que ainda não foi salvo
        ja ??= db.BoletosPosto.Local.FirstOrDefault(x => x.Id == 0 && (
            (b.Lido && b.NossoNumero != "" && x.NossoNumero == b.NossoNumero) || x.Pdf?.Length == pdf.Length && x.NomeArquivo == nome));
        if (ja != null) return (ja, true);

        var boleto = new BoletoPosto
        {
            Posto = b.Posto,
            NumeroDocumento = b.NumeroDocumento,
            NossoNumero = b.NossoNumero,
            Valor = valor,
            Vencimento = b.Vencimento,
            DataDocumento = b.DataDocumento,
            DataChegada = hoje,
            LidoDoPdf = b.Lido,
            AvisosJson = JsonSerializer.Serialize(b.Avisos, Json),
            NomeArquivo = nome,
            Pdf = pdf,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };
        db.BoletosPosto.Add(boleto);
        return (boleto, false);
    }

    /// <summary>
    /// Refaz a ligação automática de todos os boletos que não foram ligados à
    /// mão. São poucas linhas; refazer tudo evita ligação velha depois de uma
    /// fatura ter o número corrigido.
    /// </summary>
    public async Task Religar()
    {
        var faturas = await db.FaturasPosto.AsNoTracking()
            .Select(f => new FaturaConf(f.Id, f.Posto, f.Numero, null, null, null, 0, 0, 0, default))
            .ToListAsync();
        var boletos = await db.BoletosPosto.Where(b => !b.LigacaoManual).ToListAsync();
        foreach (var b in boletos)
        {
            var id = AcharFatura(b.Posto, b.NumeroDocumento, faturas);
            if (b.FaturaId != id) b.FaturaId = id;
        }
    }

    // A mesma combinação de faturas gerada de novo substitui a planilha anterior
    public async Task<PlanilhaAbastecimento> GuardarPlanilha(FechamentoResult res, List<FaturaPosto> faturas)
    {
        var ids = string.Join(",", faturas.Select(f => f.Id));
        var planilha = await db.PlanilhasAbastecimento.FirstOrDefaultAsync(p => p.FaturasIds == ids);
        if (planilha == null)
        {
            planilha = new PlanilhaAbastecimento { FaturasIds = ids };
            db.PlanilhasAbastecimento.Add(planilha);
        }
        planilha.Nome = res.NomeArquivo;
        planilha.Conteudo = res.Bytes;
        planilha.Faturas = string.Join(", ", faturas.Select(f => $"{f.Posto} {f.Numero}".Trim()));
        planilha.Soma = Math.Round((decimal)res.Soma, 2);
        planilha.Quantidade = res.Quantidade;
        planilha.CriadoEm = DateTime.UtcNow;
        return planilha;
    }
}
