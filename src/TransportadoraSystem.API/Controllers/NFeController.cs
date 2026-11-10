using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/nfe")]
public class NFeController(NFeImportService nfeService, AppDbContext db,
    ArquivoNFeService arquivoNFe, TiposVolumeService tiposVolume, ViagensCaminhaoService viagens, ILogger<NFeController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Todo XML que passa por aqui vai para o arquivo de notas, para poder ser
    // baixado depois pelo sistema. Nunca derruba a tela: se guardar falhar, o
    // que o usuário pediu continua valendo.
    private async Task GuardarNoArquivo(List<(string Nome, byte[] Conteudo)> lista)
    {
        try { await arquivoNFe.GuardarAsync(lista); }
        catch (Exception ex) { logger.LogWarning(ex, "Não consegui arquivar os XMLs enviados"); }
    }

    // Tudo o que é gerado fica guardado sem carga, para a lista de romaneios
    // guardados. Gerar de novo o mesmo arquivo troca o conteúdo em vez de
    // empilhar; quando a carga é salva em Viagens, ela adota esse registro.
    // Falhar aqui não pode derrubar a geração: o usuário já tem os arquivos.
    // Primeira linha do .txt do frete da Girassol = nome do boleto a dar ao PDF do Sicoob
    // ("515-GIRASSOL. 13-10-2026-XAF", BOL-02), para copiar e renomear (Admin, 14/10/2026).
    // Número: o do .txt já guardado com o mesmo nome (gerar de novo não pula número);
    // senão o maior entre os boletos da Girassol subidos e os .txt já numerados, mais um.
    private static readonly System.Text.RegularExpressions.Regex LinhaBoleto =
        new(@"^\s*(\d+)-GIRASSOL\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private static readonly System.Text.RegularExpressions.Regex NomeTxt =
        new(@"^(\d{2}-\d{2}-\d{4})\s+([A-Z]{3})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static int? NumeroDoTxt(byte[] conteudo)
    {
        var primeira = System.Text.Encoding.UTF8.GetString(conteudo).Split('\n').FirstOrDefault() ?? "";
        var m = LinhaBoleto.Match(primeira.TrimStart((char)0xFEFF));
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private async Task NumerarBoletoPerfilAsync(List<PlanilhaGerada> arquivos)
    {
        var txts = arquivos.Where(a => a.NomeArquivo.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).ToList();
        if (txts.Count == 0) return;
        var guardados = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Nome.EndsWith(".txt"))
            .Select(p => new { p.Nome, p.Conteudo }).ToListAsync();
        var numerados = guardados.Select(g => (g.Nome, Numero: NumeroDoTxt(g.Conteudo))).Where(g => g.Numero != null).ToList();
        var ultimo = Math.Max(
            await db.BoletosReceber.Where(b => b.Cliente == "GIRASSOL").MaxAsync(b => (int?)b.Sequencia) ?? 0,
            numerados.Select(n => n.Numero!.Value).DefaultIfEmpty(0).Max());
        foreach (var t in txts)
        {
            var m = NomeTxt.Match(t.NomeArquivo);
            if (!m.Success) continue;   // sem data ou placa no nome: fica sem a linha
            var numero = numerados.FirstOrDefault(n => n.Nome == t.NomeArquivo).Numero ?? ++ultimo;
            var linha = $"{numero}-GIRASSOL. {m.Groups[1].Value}-{m.Groups[2].Value.ToUpperInvariant()}";
            t.Bytes = System.Text.Encoding.UTF8.GetBytes(linha + Environment.NewLine + Environment.NewLine + System.Text.Encoding.UTF8.GetString(t.Bytes));
        }
    }

    private async Task<bool> GuardarGerados(List<PlanilhaGerada> arquivos)
    {
        try
        {
            var nomes = arquivos.Select(a => a.NomeArquivo).Distinct().ToList();
            var soltos = await db.PlanilhasCarga
                .Where(p => p.CalculoId == null && nomes.Contains(p.Nome))
                .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
                .ToListAsync();

            foreach (var a in arquivos)
            {
                var atual = soltos.FirstOrDefault(p => p.Nome == a.NomeArquivo);
                if (atual == null)
                {
                    atual = new PlanilhaCarga { CalculoId = null, Nome = a.NomeArquivo };
                    db.PlanilhasCarga.Add(atual);
                    soltos.Insert(0, atual);
                }
                atual.Conteudo = a.Bytes;
                atual.CriadoEm = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não consegui guardar as planilhas geradas");
            db.ChangeTracker.Clear();
            return false;
        }
    }

    [HttpPost("gerar-planilhas")]
    public async Task<IActionResult> GerarPlanilhas(
        IFormFileCollection arquivos,
        [FromForm] string? numeros,
        [FromForm] string? correcoes,
        [FromQuery] int qtdProdutosDCedro = 0,
        [FromQuery] double caixasProdutoE = 0,
        [FromQuery] int produtosCBrz = 0,
        [FromQuery] double precoProdutoCBrz = NFeImportService.PrecoProdutoCPadrao)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Nenhum arquivo enviado." });

            var xmls = new List<string>();
            foreach (var arquivo in arquivos)
            {
                using var sr = new StreamReader(arquivo.OpenReadStream());
                xmls.Add(await sr.ReadToEndAsync());
            }

            // Números dos romaneios por cliente (JSON: {"AURORA":165,"BRZ":156,...})
            Dictionary<string, int>? nums = null;
            if (!string.IsNullOrWhiteSpace(numeros))
                nums = JsonSerializer.Deserialize<Dictionary<string, int>>(numeros);

            // Tipos trocados na tela quando a nota veio com o volume errado
            List<CorrecaoTipo>? trocas = null;
            if (!string.IsNullOrWhiteSpace(correcoes))
                trocas = JsonSerializer.Deserialize<List<CorrecaoTipo>>(correcoes, Json);

            var escritas = await tiposVolume.EscritasAsync();
            // Preço da produtoD da CD: o mesmo do boleto (aba Sicoob → Emissão de boletos, BOL-12)
            decimal precoProdutoD;
            try { precoProdutoD = await db.ConfigEmissaoBoleto.AsNoTracking().Where(c => c.Cliente == "CD").Select(c => c.PrecoCaixaProdutoD).FirstOrDefaultAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Sem a configuração da emissão; produtoD a 120"); precoProdutoD = 0; }
            var resultado = nfeService.GerarPlanilhas(xmls, qtdProdutosDCedro, nums, caixasProdutoE, escritas, trocas,
                produtosCBrz, precoProdutoCBrz, precoProdutoD);

            if (resultado.SemTipo.Count > 0)
                return BadRequest(new { erro = resultado.Erro, produtosSemTipo = resultado.SemTipo });
            if (resultado.Arquivos.Count == 0)
                return BadRequest(new { erro = resultado.Erro ?? "Nenhuma planilha gerada." });

            // Guarda os números usados (pra sugerir +1 na próxima)
            if (nums != null)
            {
                foreach (var (apelido, num) in nums)
                {
                    if (!resultado.ClientesEncontrados.Contains(apelido)) continue;
                    var contador = await db.RomaneioContadores.FirstOrDefaultAsync(c => c.Apelido == apelido);
                    if (contador == null) db.RomaneioContadores.Add(new() { Apelido = apelido, Ultimo = num });
                    else contador.Ultimo = num;
                }
                await db.SaveChangesAsync();
            }

            await NumerarBoletoPerfilAsync(resultado.Arquivos);

            if (!await GuardarGerados(resultado.Arquivos))
                resultado.Avisos.Add("Os arquivos foram gerados, mas não consegui guardar no sistema: " +
                    "eles não vão aparecer em Romaneios guardados.");

            // A produtoC da BRZ é a mesma do controle de carregamento (NFE-05, EST-07):
            // quantidade diferente, avisa (não se cobra duas vezes)
            foreach (var a in resultado.Arquivos)
            {
                if (ProdutosCBrz.DiaDoRomaneio(a.NomeArquivo) is not { } dia) continue;
                try
                {
                    if (await viagens.ProdutosCBrzNoControleAsync(dia) is { } noControle && noControle != produtosCBrz)
                        resultado.Avisos.Add($"BRZ: o controle de carregamento de {dia:dd/MM} tem {noControle:0.##} produtosC e o romaneio saiu com {produtosCBrz}. " +
                            "Confira: é a mesma produtoC, não cobre duas vezes.");
                }
                catch (Exception ex) { logger.LogWarning(ex, "Não conferi as produtosC da BRZ com o controle"); }
            }

            // Arquivos individuais (sem zip) — o frontend baixa um a um
            return Ok(new
            {
                arquivos = resultado.Arquivos.Select(a => new
                {
                    nome     = a.NomeArquivo,
                    conteudo = Convert.ToBase64String(a.Bytes),
                }),
                avisos = resultado.Avisos,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar planilhas");
            return StatusCode(500, new { erro = $"Erro ao gerar planilhas: {ex.Message}" });
        }
    }

    [HttpPost("analisar")]
    public async Task<IActionResult> Analisar(IFormFileCollection arquivos)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Nenhum arquivo enviado." });

            var xmls = new List<string>();
            var guardar = new List<(string, byte[])>();
            foreach (var arquivo in arquivos)
            {
                using var ms = new MemoryStream();
                await arquivo.CopyToAsync(ms);
                var bytes = ms.ToArray();
                guardar.Add((arquivo.FileName, bytes));
                xmls.Add(System.Text.Encoding.UTF8.GetString(bytes));
            }
            await GuardarNoArquivo(guardar);

            var escritas = await tiposVolume.EscritasAsync();
            var nfes   = new List<NFeData>();
            var erros  = new List<string>();
            foreach (var xml in xmls)
            {
                try { nfes.Add(NFeImportService.ParseXml(xml, escritas)); }
                catch (Exception ex) { erros.Add(ex.Message); logger.LogWarning("XML inválido ignorado: {Msg}", ex.Message); }
            }

            var grupos      = NFeImportService.AgruparPorCliente(nfes);
            var gruposFrete = NFeImportService.AgruparPorClienteFrete(nfes);

            var clientes = grupos.Keys
                .Select(k => NFeImportService.Clientes.TryGetValue(k, out var c) ? c.Apelido : k)
                .Concat(gruposFrete.Values.Select(g => g.Apelido))
                .Distinct()
                .ToList();

            // Sugere o próximo número de romaneio (último + 1) para os clientes de planilha
            var apelidosPlanilha = grupos.Keys
                .Select(k => NFeImportService.Clientes.TryGetValue(k, out var c3) ? c3.Apelido : k)
                .Distinct()
                .ToList();
            var contadores = await db.RomaneioContadores
                .Where(rc => apelidosPlanilha.Contains(rc.Apelido))
                .ToListAsync();
            var numerosSugeridos = apelidosPlanilha.ToDictionary(
                a => a,
                a => (contadores.FirstOrDefault(c => c.Apelido == a)?.Ultimo ?? 0) + 1);

            return Ok(new
            {
                totalXmls           = xmls.Count,
                totalNfes           = nfes.Count,
                clientesEncontrados = clientes,
                numerosSugeridos,
                // A BRZ às vezes leva produtoC junto, que não vem na nota
                temBrz              = grupos.Any(g =>
                    NFeImportService.Clientes.TryGetValue(g.Key, out var c4) && c4.Template == "BRZ"),
                precoProdutoCPadrao  = NFeImportService.PrecoProdutoCPadrao,
                temCedro             = grupos.Any(g =>
                    NFeImportService.Clientes.TryGetValue(g.Key, out var c2) && c2.Template == "CEDRO"),
                // O .txt de frete é o que separa caixa normal de produtoE
                temFrete            = gruposFrete.Count > 0,
                produtosENasNotas    = gruposFrete.Values
                    .SelectMany(g => g.NFs).Where(n => n.ProdutoE).Sum(n => n.Caixas),
                errosParseamento    = erros,
                // Mesmo tipo com dois preços, ou vermelho mais barato que branco
                conflitosTipo       = NFeImportService.DetectarConflitosTipo(nfes),
                // Todo tipo cadastrado vira linha quando vem na nota (NFE-09)
                tiposRomaneio       = await db.TiposVolume.OrderBy(t => t.Ordem).ThenBy(t => t.Nome).Select(t => t.Nome).ToListAsync(),
                // Sem tipo: a tela pergunta o nome antes de gerar
                produtosSemTipo     = NFeImportService.ProdutosSemTipo(nfes, escritas),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao analisar XMLs");
            return StatusCode(500, new { erro = $"Erro ao analisar arquivos: {ex.Message}" });
        }
    }

    [HttpPost("extrair-para-frete")]
    public async Task<IActionResult> ExtrairParaFrete(IFormFileCollection arquivos)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Nenhum arquivo enviado." });

            var pares = new List<(string xml, string arquivo)>();
            var guardar = new List<(string, byte[])>();
            foreach (var arquivo in arquivos)
            {
                using var ms = new MemoryStream();
                await arquivo.CopyToAsync(ms);
                var bytes = ms.ToArray();
                guardar.Add((arquivo.FileName, bytes));
                pares.Add((System.Text.Encoding.UTF8.GetString(bytes), arquivo.FileName));
            }
            await GuardarNoArquivo(guardar);

            var itens = NFeImportService.ExtrairParaFrete(pares);

            // Marca quais notas já têm o DANFE guardado, para a tela poder
            // oferecer o download ao lado de cada NF.
            var chaves = itens.SelectMany(i => i.Nfs).Select(n => n.Chave)
                              .Where(c => c.Length == 44).Distinct().ToList();
            if (chaves.Count > 0)
            {
                var comPdf = await db.ArquivosNFe
                    .Where(a => chaves.Contains(a.ChaveAcesso) && a.Pdf != null)
                    .Select(a => a.ChaveAcesso)
                    .ToListAsync();

                foreach (var nf in itens.SelectMany(i => i.Nfs))
                    nf.TemPdf = comPdf.Contains(nf.Chave);
            }

            // Nota com placa e sem motorista escrito: o motorista é o da última viagem
            // lançada com aquela placa (cavalo ou carreta); sem viagem, cai na placa
            // habitual do cadastro antigo da frota (VIA-20: motorista não tem mais placa padrão).
            if (itens.SelectMany(i => i.Nfs).Any(n => n.Placa.Length > 0 && n.Motorista.Length == 0))
            {
                var placas = itens.SelectMany(i => i.Nfs).Where(n => n.Placa.Length > 0 && n.Motorista.Length == 0)
                    .Select(n => n.Placa).Distinct().ToList();
                var viagens = await db.ViagensCaminhao.AsNoTracking()
                    .Where(v => v.MotoristaNome != "" && (placas.Contains(v.Placa) || (v.PlacaCarreta != null && placas.Contains(v.PlacaCarreta))))
                    .OrderByDescending(v => v.DataSaida).ThenByDescending(v => v.Id)
                    .Select(v => new { v.Placa, v.PlacaCarreta, v.MotoristaNome })
                    .ToListAsync();
                var frota = await db.MotoristasFrota.AsNoTracking().Where(m => m.Ativo)
                    .Select(m => new { m.Nome, m.PlacaCavalo, m.PlacaCarreta }).ToListAsync();
                foreach (var nf in itens.SelectMany(i => i.Nfs).Where(n => n.Placa.Length > 0 && n.Motorista.Length == 0))
                {
                    var ultima = viagens.FirstOrDefault(v => v.Placa == nf.Placa || v.PlacaCarreta == nf.Placa);
                    if (ultima != null) { nf.Motorista = ultima.MotoristaNome.ToUpperInvariant(); continue; }
                    var dono = frota.FirstOrDefault(m => SemPararPlanilhaService.NormalizarPlaca(m.PlacaCavalo) == nf.Placa)
                            ?? frota.FirstOrDefault(m => SemPararPlanilhaService.NormalizarPlaca(m.PlacaCarreta) == nf.Placa);
                    if (dono != null) nf.Motorista = dono.Nome.ToUpperInvariant();
                }
            }

            return Ok(itens);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao extrair XMLs para frete");
            return StatusCode(500, new { erro = $"Erro ao processar XMLs: {ex.Message}" });
        }
    }
}
