using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Gera a planilha de controle de abastecimentos a partir dos PDFs (Faturas de Vendas dos postos).
/// Cada abastecimento (Data, Placa, Motorista, Valor) é alocado na seção do PAGADOR dono do caminhão.
/// Preenche o template embutido ABASTECIMENTO.xlsx (mantém as fórmulas de total).
/// </summary>
public class AbastecimentoFechamentoService
{
    // Placa (sem traço) → pagador (dono do caminhão)
    private static readonly Dictionary<string, string> PlacaPagador = new()
    {
        ["XAA9457"] = "BENTO", ["XAB4C58"] = "BENTO", ["XAC6D57"] = "BENTO",
        ["XAD5H46"] = "CAIO", ["XAE4D55"] = "CAIO", ["XAF2J52"] = "CAIO",
        ["XAG2G58"] = "CAIO", ["XAH2I15"] = "CAIO", ["XAI0F06"] = "CAIO",
        ["XAJ2B10"] = "DAVI", ["XAK3B51"] = "DAVI", ["XAL5A77"] = "DAVI",
    };

    // Seção do pagador: linha inicial dos dados e linha do TOTAL (nº de vagas = TotalRow - StartRow)
    private record Secao(int StartRow, int TotalRow);
    private static readonly Dictionary<string, Secao> Secoes = new()
    {
        ["BENTO"]  = new Secao(4, 14),
        ["CAIO"]    = new Secao(17, 28),
        ["DAVI"] = new Secao(33, 43),
    };

    private static readonly Regex Money = new(@"^\d{1,3}(\.\d{3})*,\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NumDoc = new(@"^\d{5,7}$", RegexOptions.Compiled);
    private static readonly Regex Data = new(@"^\d{2}/\d{2}/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex Nome = new(@"^[A-Za-zÀ-Ú]{2,}$", RegexOptions.Compiled);
    // formato Estrada: placa com ou sem traço (XAB-4C58) e nº do título (565578/1)
    private static readonly Regex Placa = new(@"^[A-Z]{3}-?\d[A-Z0-9]\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Titulo = new(@"^\d{3,9}/\d{1,3}$", RegexOptions.Compiled);

    private static double ParseNum(string s) =>
        double.Parse(s.Replace(".", "").Replace(",", "."), CultureInfo.InvariantCulture);

    public record Abast(DateTime Data, string Placa, string Motorista, double Valor);
    public record FechamentoResult(byte[] Bytes, string NomeArquivo, List<string> Avisos,
        double Soma, double? TotalFatura, int Quantidade, List<FaturaLida> Faturas);

    /// <summary>
    /// Uma fatura lida: o cabeçalho (posto, número, datas, total) e os
    /// abastecimentos. Posto já vem no nome da planilha dos boletos.
    /// </summary>
    public record FaturaLida(
        string Posto, string Numero, DateOnly? Emissao, DateOnly? Vencimento, double? ValorFatura,
        List<Abast> Abastecimentos, List<string> Cnpjs)
    {
        public double Soma => Math.Round(Abastecimentos.Sum(a => a.Valor), 2);
        // O que vai para a planilha de divisão: placa sem dono fica de fora
        public double SomaNaPlanilha => Math.Round(Abastecimentos.Where(a => Dono(a.Placa) != null).Sum(a => a.Valor), 2);
        public List<Abast> DoBento => Abastecimentos.Where(a => Dono(a.Placa) == "BENTO").ToList();
        public double ValorBento => Math.Round(DoBento.Sum(a => a.Valor), 2);
        public List<string> PlacasSemDono => Abastecimentos.Select(a => a.Placa).Where(p => Dono(p) == null).Distinct().ToList();
    }

    /// <summary>Dono do caminhão pela placa (BENTO, CAIO, DAVI) ou null.</summary>
    public static string? Dono(string placa) =>
        PlacaPagador.TryGetValue(Regex.Replace(placa ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant(), out var d) ? d : null;

    // Recebe uma ou várias faturas: o mês pode ter abastecimento no Posto 7 e no
    // Estrada, e a divisão por dono do caminhão é a mesma nos dois. Tudo cai numa
    // planilha só, cada dono com a sua seção.
    public FechamentoResult Gerar(IEnumerable<byte[]> pdfs)
    {
        var avisos = new List<string>();
        var todos = new List<Abast>();
        var postos = new List<string>();     // na ordem de chegada, sem repetir
        string numPosto = "";
        double? totalFaturas = null;
        int arquivos = 0;
        var faturas = new List<FaturaLida>();

        foreach (var pdf in pdfs)
        {
            arquivos++;
            var (itens, posto, num, total, fatura) = LerPdf(pdf);
            faturas.Add(fatura);

            if (itens.Count == 0)
                avisos.Add($"{posto}: nenhum abastecimento reconhecido no PDF.");

            // A conferência é por arquivo: cada fatura fecha com o seu próprio total,
            // então dá pra saber QUAL posto não bateu.
            double somaArquivo = itens.Sum(a => a.Valor);
            if (total.HasValue && Math.Abs(somaArquivo - total.Value) > 0.05)
                avisos.Add($"{posto}: os abastecimentos somaram R$ {somaArquivo:N2} mas a fatura diz R$ {total.Value:N2}.");

            todos.AddRange(itens);
            if (!postos.Contains(posto)) postos.Add(posto);
            if (numPosto == "") numPosto = num;
            if (total.HasValue) totalFaturas = (totalFaturas ?? 0) + total.Value;
        }

        var semDono = todos.Where(a => !PlacaPagador.ContainsKey(a.Placa))
                           .Select(a => a.Placa).Distinct().ToList();
        if (semDono.Count > 0)
            avisos.Add($"Placas sem dono na divisão (não entraram): {string.Join(", ", semDono)}.");

        double soma = todos.Sum(a => a.Valor);
        var titulo = postos.Count > 0 ? string.Join(" + ", postos) : "ABASTECIMENTOS";
        var bytes = Preencher(todos, titulo);

        // Com um arquivo só, o nome continua sendo nº da fatura + posto, como sempre
        // foi; com mais de um, vira a lista dos postos.
        var nome = (arquivos == 1 && !string.IsNullOrWhiteSpace(numPosto)
            ? $"{numPosto} {titulo}.xlsx"
            : $"{titulo}.xlsx").ToUpper();

        return new FechamentoResult(bytes, nome, avisos,
            Math.Round(soma, 2), totalFaturas, todos.Count, faturas);
    }

    /// <summary>Lê uma fatura sem gerar planilha (para guardar e conferir).</summary>
    public static FaturaLida LerFatura(byte[] pdf) => LerPdf(pdf).Fatura;

    // ───────────────────────── PDF ─────────────────────────
    // Cada posto manda a fatura num layout diferente. O formato é reconhecido pelo
    // conteúdo e cada um tem seu leitor — a divisão por dono do caminhão é a mesma.
    private static (List<Abast>, string posto, string numPosto, double? total, FaturaLida Fatura) LerPdf(byte[] pdf)
    {
        using var doc = AbrirPdf(pdf);
        var paginas = doc.GetPages().ToList();

        bool ehEstrada = paginas.Any(p =>
            Regex.IsMatch(p.Text, "PRAT[AÃ]O", RegexOptions.IgnoreCase) ||
            p.Text.Contains("Num.Titulo", StringComparison.OrdinalIgnoreCase));

        var (itens, posto, numPosto, total) = ehEstrada ? LerEstrada(paginas) : LerPosto7(paginas);

        // Cabeçalho da fatura, lido à parte para não mexer no que vai para a planilha
        var linhas = paginas.SelectMany(p => Linhas(p)).Select(l => string.Join(" ", l)).ToList();
        var (numero, emissao, vencimento) = Cabecalho(linhas);
        var texto = string.Join('\n', linhas);
        var nomePosto = CobrancaAbastecimentoService.DetectarPosto(texto)
            ?? CobrancaAbastecimentoService.NomePosto(ehEstrada ? posto : $"{numPosto} {posto}");
        var cnpjs = Regex.Matches(texto, @"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}")
            .Select(m => Regex.Replace(m.Value, @"\D", "")).Distinct().ToList();

        var fatura = new FaturaLida(nomePosto, numero, emissao, vencimento, total, itens, cnpjs);
        return (itens, posto, numPosto, total, fatura);
    }

    // Os PDFs dos postos às vezes vêm com senha (o boleto do Posto 7 vem). Com
    // senha de usuário vazia abre direto; as outras são as que os postos usam.
    internal static PdfDocument AbrirPdf(byte[] pdf, IEnumerable<string>? senhas = null) =>
        PdfDocument.Open(pdf, new ParsingOptions
        {
            Passwords = [.. new[] { "" }.Concat(senhas ?? CobrancaAbastecimentoService.SenhasConhecidas).Distinct()],
        });

    // Número, emissão e vencimento da fatura.
    //   Posto 7: "Emissão: 28/09/2026 Fatura: 83421 ..." e "Valor da fatura: R$ ... Vencimento: 05/10/2026"
    //   Estrada:  "187298 28/09/2026 03/10/2026 14.393,06" (debaixo de NUMERO EMISSÃO VENCIMENTO VALOR)
    private static (string numero, DateOnly? emissao, DateOnly? vencimento) Cabecalho(List<string> linhas)
    {
        string numero = "";
        DateOnly? emissao = null, vencimento = null;
        static DateOnly? D(string s) =>
            DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        foreach (var l in linhas)
        {
            var mf = Regex.Match(l, @"\bFatura:\s*(\d+)", RegexOptions.IgnoreCase);
            if (mf.Success && numero == "") numero = mf.Groups[1].Value;
            var me = Regex.Match(l, @"Emiss[aã]o:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
            if (me.Success) emissao ??= D(me.Groups[1].Value);
            var mv = Regex.Match(l, @"Vencimento:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
            if (mv.Success) vencimento ??= D(mv.Groups[1].Value);
        }

        if (numero == "")
        {
            foreach (var l in linhas)
            {
                var m = Regex.Match(l, @"^(\d{3,9}) (\d{2}/\d{2}/\d{4}) (\d{2}/\d{2}/\d{4}) \d{1,3}(\.\d{3})*,\d{2}$");
                if (!m.Success) continue;
                numero = m.Groups[1].Value;
                emissao ??= D(m.Groups[2].Value);
                vencimento ??= D(m.Groups[3].Value);
                break;
            }
        }
        return (numero, emissao, vencimento);
    }

    // Posto 7 (Vila Norte): blocos de "Nota a cobrar"; motorista e placa vêm no
    // texto corrido, à direita da linha do produto.
    private static (List<Abast>, string posto, string numPosto, double? total) LerPosto7(IReadOnlyList<Page> paginas)
    {
        var lista = new List<Abast>();
        string posto = "POSTO", numPosto = "";
        double? total = null;

        DateTime? curData = null; double? curValor = null; string? curMot = null;

        foreach (var page in paginas)
        {
            // "Posto 7 Vila Norte"
            var mp = Regex.Match(page.Text, @"Posto\s+(\d+)\s+([A-Za-zÀ-ú ]+?)\s*[-\r\n]", RegexOptions.IgnoreCase);
            if (mp.Success && numPosto == "") { numPosto = mp.Groups[1].Value; posto = mp.Groups[2].Value.Trim(); }

            foreach (var row in Linhas(page))
            {
                // "Valor da fatura: R$ 29.291,49"
                if (row.Contains("fatura:") || (row.Contains("Valor") && row.Contains("R$")))
                {
                    var mv = row.FirstOrDefault(t => Money.IsMatch(t));
                    if (mv != null && row.Any(t => t.Equals("fatura:", StringComparison.OrdinalIgnoreCase))) total = ParseNum(mv);
                }

                // valor + nº doc  →  total do abastecimento (ex: "3.935,16 267123")
                bool achouValor = false;
                for (int i = 0; i < row.Count - 1; i++)
                    if (Money.IsMatch(row[i]) && NumDoc.IsMatch(row[i + 1]))
                    { curValor = ParseNum(row[i]); achouValor = true; break; }

                // rede de segurança: linha da "Nota a cobrar" em que o nº do documento
                // acabou noutra linha — pega o primeiro valor depois de "cobrar".
                if (!achouValor && row.Contains("cobrar"))
                {
                    var v = row.Skip(row.IndexOf("cobrar") + 1).FirstOrDefault(t => Money.IsMatch(t));
                    if (v != null) curValor = ParseNum(v);
                }

                // data (linha da "Nota a cobrar")
                if (row.Any(t => t.Equals("Nota", StringComparison.Ordinal)))
                {
                    var dt = row.FirstOrDefault(t => Data.IsMatch(t));
                    if (dt != null) curData = DateTime.ParseExact(dt, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                }

                // motorista antecipado ("... Motorista: CAIO,")
                int mi = row.IndexOf("Motorista:");
                if (mi >= 0 && mi + 1 < row.Count)
                {
                    var w = row[mi + 1].TrimEnd(',');
                    if (Nome.IsMatch(w)) curMot = w;
                }

                // placa (fecha o abastecimento)
                int pi = row.IndexOf("Placa:");
                if (pi >= 0 && pi + 1 < row.Count)
                {
                    var placa = Regex.Match(row[pi + 1], "[A-Z0-9]+").Value;
                    // motorista: palavras logo antes de "Placa:" (senão usa o antecipado)
                    var nomes = new List<string>();
                    for (int k = pi - 1; k >= 0; k--)
                    {
                        var w = row[k].TrimEnd(',');
                        if (Nome.IsMatch(w)) nomes.Insert(0, w); else break;
                    }
                    var motorista = nomes.Count > 0 ? string.Join(" ", nomes) : (curMot ?? "");

                    if (curData.HasValue && curValor.HasValue && placa.Length >= 5)
                        lista.Add(new Abast(curData.Value, placa, motorista, Math.Round(curValor.Value, 2)));
                    curMot = null;   // motorista é por abastecimento
                }
            }
        }
        return (lista, posto.ToUpperInvariant(), numPosto, total);
    }

    // Posto Estrada: tabela limpa, uma linha por item —
    //   "CF 565578/1 18/08/2026 XAB-4C58 400,000 DIESEL IPIMAX S10 ADITIVADO 2.496,00 1.374.366 0 0,00"
    // Não traz o motorista (a coluna sai vazia). Itens do mesmo título somam num
    // lançamento só, do mesmo jeito que o Arla entra na nota do Posto 7.
    private static (List<Abast>, string posto, string numPosto, double? total) LerEstrada(IReadOnlyList<Page> paginas)
    {
        var porTitulo = new Dictionary<string, Abast>();
        var ordem = new List<string>();
        double? total = null;

        foreach (var page in paginas)
        {
            foreach (var row in Linhas(page))
            {
                // "Total 12.800,76" fecha o detalhamento (o RESUMO repete depois)
                if (row.Count > 1 && row[0].Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    var t = row.FirstOrDefault(x => Money.IsMatch(x));
                    if (t != null) total ??= ParseNum(t);
                    continue;
                }

                int pi = row.FindIndex(t => Placa.IsMatch(t));
                if (pi < 0) continue;                       // sem placa não é linha de item

                var dt = row.FirstOrDefault(t => Data.IsMatch(t));
                if (dt == null) continue;

                // o valor é o primeiro dinheiro DEPOIS da placa: a quantidade tem tres
                // casas decimais (400,000) e por isso não casa com Money.
                var vl = row.Skip(pi + 1).FirstOrDefault(t => Money.IsMatch(t));
                if (vl == null) continue;

                var placa = Regex.Replace(row[pi], "[^A-Za-z0-9]", "").ToUpperInvariant();
                var data = DateTime.ParseExact(dt, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                var valor = ParseNum(vl);

                var titulo = row.FirstOrDefault(t => Titulo.IsMatch(t)) ?? $"{dt}|{placa}";
                if (porTitulo.TryGetValue(titulo, out var ja))
                    porTitulo[titulo] = ja with { Valor = Math.Round(ja.Valor + valor, 2) };
                else
                {
                    porTitulo[titulo] = new Abast(data, placa, "", Math.Round(valor, 2));
                    ordem.Add(titulo);
                }
            }
        }

        return (ordem.Select(t => porTitulo[t]).ToList(), "ESTRADA", "", total);
    }

    // Agrupa as palavras em linhas pela altura. A tolerância tem que ser MÍNIMA:
    // quando o posto imprime um produto colado no cabeçalho do documento seguinte,
    // as duas linhas ficam a apenas 0,2 pt uma da outra (ex.: 723,82 e 723,62).
    // Arredondar pra inteiro fundia as duas e o valor do produto era lido como o
    // valor do documento (a fatura fechava com R$ 49,00 a menos).
    internal static IEnumerable<List<string>> Linhas(Page page)
    {
        const double tol = 0.1;
        var linha = new List<Word>();
        double y = 0;

        foreach (var w in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom))
        {
            if (linha.Count > 0 && Math.Abs(w.BoundingBox.Bottom - y) > tol)
            {
                yield return Ordenar(linha);
                linha = [];
            }
            if (linha.Count == 0) y = w.BoundingBox.Bottom;
            linha.Add(w);
        }
        if (linha.Count > 0) yield return Ordenar(linha);

        static List<string> Ordenar(List<Word> l) =>
            l.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text).ToList();
    }

    // ───────────────────────── Template ─────────────────────────
    private static byte[] Preencher(List<Abast> abastecimentos, string posto)
    {
        var asm = typeof(AbastecimentoFechamentoService).Assembly;
        using var stream = asm.GetManifestResourceStream("TransportadoraSystem.Application.Templates.ABASTECIMENTO.xlsx")
            ?? throw new InvalidOperationException("Template ABASTECIMENTO.xlsx não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var wb = new XLWorkbook(new MemoryStream(ms.ToArray()));
        var ws = wb.Worksheets.First();

        ws.Cell("A1").Value = $"CONTROLE ABASTECIMENTOS - {posto}";

        // Agrupa por pagador, em ordem de data. Com duas faturas na mesma planilha a
        // ordem dos PDFs deixaria as datas embaralhadas (uma fatura depois da outra).
        var porPagador = abastecimentos
            .Where(a => PlacaPagador.ContainsKey(a.Placa))
            .GroupBy(a => PlacaPagador[a.Placa])
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Data).ToList());

        // Preenche de baixo pra cima (pra inserção de linhas não bagunçar as seções acima)
        foreach (var pagador in new[] { "DAVI", "CAIO", "BENTO" })
        {
            if (!porPagador.TryGetValue(pagador, out var itens) || itens.Count == 0) continue;
            var sec = Secoes[pagador];
            int vagas = sec.TotalRow - sec.StartRow;
            if (itens.Count > vagas)
                ws.Row(sec.TotalRow - 1).InsertRowsAbove(itens.Count - vagas); // estende o SUM

            int r = sec.StartRow;
            foreach (var a in itens)
            {
                ws.Cell(r, 1).Value = a.Data;                 // A DATA
                ws.Cell(r, 1).Style.DateFormat.Format = "DD/MM/YYYY";
                ws.Cell(r, 2).Value = a.Placa;                // B PLACA
                ws.Cell(r, 3).Value = a.Motorista;            // C MOTORISTA
                ws.Cell(r, 5).Value = a.Valor;                // E VALOR (D PREÇO LITRO fica vazio)
                r++;
            }
        }

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);
        return outMs.ToArray();
    }
}
