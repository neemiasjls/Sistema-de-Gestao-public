using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UglyToad.PdfPig;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Confere a semana da fazenda contra os romaneios que o sistema gerou para os
/// clientes (guardados na carga):
///
///  1. Romaneio da fazenda × romaneios dos clientes do mesmo dia — a fazenda manda
///     um romaneio só para os clientes do dia (segunda: Aurora + BRZ; terça:
///     Cedro Frutas + Dunas; sexta: Litoral, de Farol, sozinho). O desconto de
///     funrural tem que bater com a soma do funrural deles.
///  2. Boletos (PDF) × valores de boleto escritos no romaneio de cada cliente.
///
/// Só entra romaneio da fazenda com "DESC FUNRURAL" na observação: é a marca de
/// romaneio de cliente. Os outros (volumes do embalamento, por exemplo) ficam fora.
/// </summary>
public class ConferenciaSemanaService
{
    // ── O que sai para a tela ─────────────────────────────────────────
    public record DiaConferencia(
        DateTime Data,
        int? RomaneioFazenda,
        decimal? FunruralFazenda,
        decimal FunruralClientes,
        List<string> Clientes,
        string Situacao,          // "ok" | "diferenca" | "sem_fazenda" | "sem_clientes"
        List<string> Observacoes);

    public record BoletoConferido(
        string Cliente,
        DateTime Data,
        decimal? NoRomaneio,
        decimal? NoBoleto,
        string? NumeroBoleto,
        decimal Diferenca,
        string Situacao);         // "ok" | "centavo" | "diferenca" | "falta_boleto" | "sobra_boleto"

    // Soma dos boletos da fazenda do cliente no dia × "VALOR A PAGAR" do romaneio dele
    public record TotalCliente(
        string Cliente,
        DateTime Data,
        decimal? TotalRomaneio,
        decimal SomaBoletos,
        int QtdBoletos,
        decimal Diferenca,
        string Situacao);         // "ok" | "centavos" | "diferenca" | "sem_boletos" | "sem_total"

    // Diferença aceita na soma dos boletos × total do romaneio (arredondamento): até 2 centavos
    public const decimal MargemTotal = 0.02m;

    public record Resultado(
        DateTime? Inicio,
        DateTime? Fim,
        List<DiaConferencia> Dias,
        List<BoletoConferido> Boletos,
        List<TotalCliente> Totais,
        List<string> Ignorados,
        List<string> Avisos);

    // ── Entrada ──────────────────────────────────────────────────────
    public record RomaneioClienteArquivo(int Id, string Nome, byte[] Conteudo);

    public record RomaneioFazenda(int Numero, DateTime Data, decimal Desconto, bool DescFunrural);

    public record RomaneioCliente(string Apelido, DateTime Data, string Arquivo,
        decimal Funrural, List<decimal> Boletos, decimal? BoletoProdutosD, decimal? ValorAPagar = null, decimal ProdutosC = 0);

    // Pagador do boleto (rótulo do Acerto) → apelido do romaneio do cliente
    private static readonly Dictionary<string, string> ApelidoPorPagador = new()
    {
        ["AURORA"] = "AURORA",
        ["BRZ"]     = "BRZ",
        ["CD"]      = "CEDRO FRUTAS",
        ["DUNAS"]  = "DUNAS",
        ["LITORAL"]   = "FAROL",
    };

    public static readonly string[] ClientesConferidos = ["AURORA", "BRZ", "CEDRO FRUTAS", "DUNAS", "FAROL"];

    // Nome com que a planilha fica guardada: "174--AURORA-21-09-2026.xlsx"
    private static readonly Regex NomePlanilha = new(@"^\d+--(.+)-(\d{2}-\d{2}-\d{4})\.xlsx$", RegexOptions.IgnoreCase);

    public static (string Apelido, DateTime Data)? LerNomePlanilha(string nome)
    {
        var m = NomePlanilha.Match(nome.Trim());
        if (!m.Success) return null;
        var apelido = m.Groups[1].Value.Trim().ToUpperInvariant();
        if (!ClientesConferidos.Contains(apelido)) return null;
        return (apelido, DateTime.ParseExact(m.Groups[2].Value, "dd-MM-yyyy", CultureInfo.InvariantCulture));
    }

    // Separa os PDFs e devolve as datas que eles cobrem — o controller usa isso
    // para buscar só os romaneios de cliente daquela semana.
    public (List<RomaneioFazenda> Fazenda, List<AcertoAlfaService.Boleto> Boletos, List<string> Avisos) LerPdfs(IEnumerable<byte[]> pdfs)
    {
        var fazenda = new List<RomaneioFazenda>();
        var boletos = new List<AcertoAlfaService.Boleto>();
        var avisos = new List<string>();

        foreach (var pdf in pdfs)
        {
            var roms = LerRomaneiosFazenda(pdf);
            if (roms.Count > 0) { fazenda.AddRange(roms); continue; }
            var b = AcertoAlfaService.LerBoleto(pdf, avisos);
            if (b != null) boletos.Add(b);
        }

        // O mesmo romaneio pode vir sozinho e dentro do PDF do dia
        fazenda = fazenda.GroupBy(r => r.Numero).Select(g => g.First()).ToList();
        return (fazenda, boletos, avisos);
    }

    public Resultado Conferir(List<RomaneioFazenda> fazendaTodos, List<AcertoAlfaService.Boleto> boletos,
                              List<RomaneioClienteArquivo> planilhas, List<string> avisos)
    {
        var ignorados = new List<string>();
        var fazenda = new List<RomaneioFazenda>();
        foreach (var r in fazendaTodos.OrderBy(r => r.Numero))
        {
            if (r.DescFunrural) fazenda.Add(r);
            else ignorados.Add($"Romaneio {r.Numero} de {r.Data:dd/MM} não tem desconto de funrural, então não é de cliente e ficou fora.");
        }

        // Romaneios de cliente: se a mesma planilha foi gerada mais de uma vez,
        // vale a última.
        var clientes = new List<RomaneioCliente>();
        foreach (var p in planilhas.OrderByDescending(p => p.Id))
        {
            var id = LerNomePlanilha(p.Nome);
            if (id == null) continue;
            if (clientes.Any(c => c.Apelido == id.Value.Apelido && c.Data == id.Value.Data)) continue;
            try { clientes.Add(LerRomaneioCliente(p.Nome, id.Value.Apelido, id.Value.Data, p.Conteudo)); }
            catch (Exception ex) { avisos.Add($"Não consegui ler o romaneio {p.Nome}: {ex.Message}"); }
        }

        // ── 1. Fazenda × clientes, dia a dia ──
        var dias = new List<DiaConferencia>();
        var datas = fazenda.Select(g => g.Data.Date).Concat(clientes.Select(c => c.Data.Date)).Distinct().OrderBy(d => d);
        foreach (var data in datas)
        {
            var gs = fazenda.Where(g => g.Data.Date == data).ToList();
            var cs = clientes.Where(c => c.Data.Date == data).OrderBy(c => c.Apelido).ToList();
            var obs = new List<string>();

            decimal? funFazenda = gs.Count > 0 ? gs.Sum(g => g.Desconto) : null;
            var funClientes = cs.Sum(c => c.Funrural);

            string situacao;
            if (gs.Count == 0)
            {
                situacao = "sem_fazenda";
                obs.Add("O romaneio da fazenda deste dia não veio nos PDFs.");
            }
            else if (cs.Count == 0)
            {
                situacao = "sem_clientes";
                obs.Add("Nenhum romaneio de cliente salvo no sistema com esta data.");
            }
            else
            {
                var difFun = Math.Abs(funClientes - funFazenda!.Value);
                situacao = difFun <= 0.01m ? "ok" : "diferenca";
                if (difFun > 0 && difFun <= 0.01m) obs.Add("Funrural com 1 centavo de diferença (arredondamento).");
                if (gs.Count > 1) obs.Add($"{gs.Count} romaneios da fazenda neste dia, somados.");
            }

            dias.Add(new DiaConferencia(data, gs.Count == 1 ? gs[0].Numero : gs.FirstOrDefault()?.Numero,
                funFazenda, funClientes, cs.Select(c => c.Apelido).ToList(), situacao, obs));
        }

        // ── 2. Boletos × romaneio do cliente ──
        var conferidos = new List<BoletoConferido>();
        var sobra = boletos
            .Select(b => (b, apelido: ApelidoPorPagador.GetValueOrDefault(b.Cliente)))
            .ToList();

        foreach (var c in clientes.OrderBy(c => c.Data).ThenBy(c => c.Apelido))
        {
            foreach (var esperado in c.Boletos)
            {
                // O boleto do cliente, com a data do documento no dia do romaneio,
                // de valor mais próximo
                var candidato = sobra
                    .Where(x => x.apelido == c.Apelido && x.b.DataDoc.Date == c.Data.Date)
                    .OrderBy(x => Math.Abs((decimal)x.b.Valor - esperado))
                    .Select(x => ((AcertoAlfaService.Boleto?)x.b, x.apelido))
                    .FirstOrDefault();

                if (candidato.Item1 == null)
                {
                    conferidos.Add(new BoletoConferido(c.Apelido, c.Data, esperado, null, null, 0, "falta_boleto"));
                    continue;
                }

                var b = candidato.Item1;
                sobra.RemoveAll(x => ReferenceEquals(x.b, b));
                var valor = Math.Round((decimal)b.Valor, 2);
                var dif = valor - esperado;
                var situacao = dif == 0 ? "ok" : Math.Abs(dif) <= 0.01m ? "centavo" : "diferenca";
                conferidos.Add(new BoletoConferido(c.Apelido, c.Data, esperado, valor, b.Numero, dif, situacao));
            }
        }

        foreach (var (b, apelido) in sobra)
        {
            // Boleto de cliente que não é conferido aqui não é sobra
            if (apelido == null) continue;
            conferidos.Add(new BoletoConferido(apelido, b.DataDoc, null, Math.Round((decimal)b.Valor, 2), b.Numero, 0, "sobra_boleto"));
        }

        // ── 3. Soma dos boletos da fazenda × valor total do romaneio do cliente ──
        // Todos os boletos do cliente com a data do documento no dia do romaneio. As produtosD da
        // Cedro Frutas entram no total do romaneio mas não vêm da fazenda: saem da conta. As produtosC
        // nunca entram (Admin, 30/10/2026).
        var totais = new List<TotalCliente>();
        foreach (var c in clientes.OrderBy(c => c.Data).ThenBy(c => c.Apelido))
        {
            var doDia = boletos.Where(b => ApelidoPorPagador.GetValueOrDefault(b.Cliente) == c.Apelido && b.DataDoc.Date == c.Data.Date).ToList();
            var soma = doDia.Sum(b => Math.Round((decimal)b.Valor, 2));
            decimal? total = c.ValorAPagar is { } v ? v - (c.BoletoProdutosD ?? 0) - c.ProdutosC : null;
            var dif = total is { } t ? soma - t : 0;
            var situacao = total == null ? "sem_total"
                : doDia.Count == 0 ? "sem_boletos"
                : dif == 0 ? "ok"
                : Math.Abs(dif) <= MargemTotal ? "centavos" : "diferenca";
            totais.Add(new TotalCliente(c.Apelido, c.Data, total, soma, doDia.Count, dif, situacao));
        }

        var todasDatas = dias.Select(d => d.Data).Concat(conferidos.Select(b => b.Data)).ToList();
        return new Resultado(
            todasDatas.Count > 0 ? todasDatas.Min() : null,
            todasDatas.Count > 0 ? todasDatas.Max() : null,
            dias, conferidos, totais, ignorados, avisos);
    }

    // ───────────────────── Romaneio da fazenda (PDF) ─────────────────────
    private static readonly Regex UsMoney   = new(@"^\d{1,3}(,\d{3})*\.\d{2}$");
    private static readonly Regex Cabecalho = new(@"CONTROLE[^\d]{0,8}(\d{3,})\W*EMISS\w*\W*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);

    public static List<RomaneioFazenda> LerRomaneiosFazenda(byte[] pdf)
    {
        var lista = new List<RomaneioFazenda>();
        int? numero = null; DateTime data = default;
        decimal desconto = 0; bool descFunrural = false;

        void Fechar()
        {
            if (numero != null) lista.Add(new RomaneioFazenda(numero.Value, data, desconto, descFunrural));
            numero = null; desconto = 0; descFunrural = false;
        }

        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            foreach (var bruta in AcertoAlfaService.Linhas(page))
            {
                var linha = bruta.Where(t => t != "|").ToList();
                var texto = string.Join(" ", linha);

                var m = Cabecalho.Match(texto);
                if (m.Success)
                {
                    Fechar();
                    numero = int.Parse(m.Groups[1].Value);
                    data = DateTime.ParseExact(m.Groups[2].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    continue;
                }
                if (numero == null) continue;

                var letras = new string(string.Concat(linha).Where(char.IsLetter).ToArray()).ToUpperInvariant();
                if (letras.StartsWith("DESCONTO"))
                {
                    var v = linha.LastOrDefault(t => UsMoney.IsMatch(t));
                    if (v != null) desconto += Dinheiro(v);
                    continue;
                }
                if (letras.StartsWith("OBSERVA") && letras.Contains("FUNRURAL"))
                    descFunrural = true;
            }
        Fechar();
        return lista;
    }

    private static decimal Dinheiro(string t) => decimal.Parse(t.Replace(",", ""), CultureInfo.InvariantCulture);

    // ───────────────────── Romaneio do cliente (xlsx) ─────────────────────
    private static readonly Regex ValorBr = new(@"R\$\s*([\d.]+,\d{2})");

    public static RomaneioCliente LerRomaneioCliente(string arquivo, string apelido, DateTime data, byte[] conteudo)
    {
        using var wb = new XLWorkbook(new MemoryStream(conteudo));
        var ws = wb.Worksheets.First();

        decimal funrural = 0;
        var boletos = new List<decimal>();
        decimal produtosD = 0;
        decimal? valorAPagar = null;
        decimal produtosC = 0;

        foreach (var row in ws.RowsUsed())
        {
            // "VALOR A PAGAR": o valor é a primeira célula com número depois do rótulo (a fórmula
            // muda de lugar entre as planilhas; o ClosedXML calcula, a planilha gerada não traz o resultado)
            if (valorAPagar == null)
            {
                var celulas = row.CellsUsed().ToList();
                var i = celulas.FindIndex(c => c.GetString().Trim().ToUpperInvariant() == "VALOR A PAGAR");
                if (i >= 0)
                    foreach (var c in celulas.Skip(i + 1))
                    {
                        if (c.GetString().Trim().Length > 0 && !c.HasFormula && !c.Value.IsNumber) break;
                        if (c.TryGetValue<double>(out var v)) { valorAPagar = Math.Round((decimal)v, 2); break; }
                    }
            }

            var b = row.Cell(2).GetString().Trim();
            var d = row.Cell(4).GetString().Trim().ToUpperInvariant();

            if (d == "DESCONTOS" && row.Cell(5).TryGetValue<double>(out var desc))
                funrural = Math.Round((decimal)desc, 2);

            if (b.ToUpperInvariant().Contains("BOLET"))
            {
                foreach (Match m in ValorBr.Matches(b))
                    boletos.Add(decimal.Parse(m.Groups[1].Value, new CultureInfo("pt-BR")));
                continue;
            }

            // ProdutosC: ficam fora do total conferido com os boletos
            if (b.ToUpperInvariant().Contains("PRODUTOC")
                && row.Cell(3).TryGetValue<double>(out var qb) && qb > 0
                && row.Cell(4).TryGetValue<double>(out var pb))
                produtosC += Math.Round((decimal)(qb * pb), 2);

            // Valor das produtosD, para achar o boleto delas no texto
            if (b.ToUpperInvariant().Contains("PRODUTOD")
                && row.Cell(3).TryGetValue<double>(out var qtd) && qtd > 0
                && row.Cell(4).TryGetValue<double>(out var preco))
                produtosD = Math.Round((decimal)(qtd * preco), 2);
        }

        // A Cedro Frutas leva produtosD de outro fornecedor: o boleto delas vai junto
        // no texto do romaneio, mas não sai da fazenda — fica fora da conferência.
        decimal? boletoProdutosD = null;
        if (produtosD > 0)
        {
            var i = boletos.FindIndex(v => v == produtosD);
            if (i >= 0) { boletoProdutosD = boletos[i]; boletos.RemoveAt(i); }
        }

        return new RomaneioCliente(apelido, data, arquivo, funrural, boletos, boletoProdutosD, valorAPagar, produtosC);
    }
}
