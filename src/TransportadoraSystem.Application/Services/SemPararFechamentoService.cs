using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê os arquivos do fechamento do Sem Parar (pedágio/VPO):
///  - Relatório de VPO em Excel (ConsultaViagensEmbarcador) → bloco "VALE PEDÁGIO" (Realizado)
///  - PDF da fatura do Sem Parar → linha FATURA (Resumo) + bloco "CRÉDITOS INSERIDOS DETALHADOS"
/// O resultado vira um controle guardado; a planilha sai dele (SemPararPlanilhaService).
/// </summary>
public class SemPararFechamentoService
{
    // Placa (sem traço, como aparece no PDF/relatório) → coluna do template
    private static readonly Dictionary<string, string> PlacaCol = new()
    {
        ["XAA9457"] = "B", ["XAD5H46"] = "C", ["XAE4D55"] = "D", ["XAJ2B10"] = "E",
        ["XAK3B51"] = "F", ["XAB4C58"] = "G", ["XAC6D57"] = "H", ["XAG2G58"] = "I",
        ["XAL5A77"] = "J", ["XAF2J52"] = "K", ["XAI0F06"] = "L", ["XAH2I15"] = "M",
    };

    // Tag do dispositivo → placa. No "Resumo da sua Fatura" a placa vem deslocada,
    // então identificamos pela tag (fixa por veículo). Atualizar se trocar de tag/veículo.
    private static readonly Dictionary<string, string> TagPlaca = new()
    {
        ["0900000001"] = "XAA9457", ["0900000002"] = "XAD5H46", ["0900000003"] = "XAE4D55",
        ["0900000004"] = "XAJ2B10", ["0900000005"] = "XAK3B51", ["0900000006"] = "XAB4C58",
        ["0900000007"] = "XAC6D57", ["0900000008"] = "XAG2G58", ["0900000009"] = "XAL5A77",
        ["0900000010"] = "XAF2J52", ["0900000011"] = "XAI0F06", ["0900000012"] = "XAH2I15",
    };

    private static readonly Regex ValRe = new(@"^[\d.]+,\d{2}$", RegexOptions.Compiled);
    private static readonly Regex TagRe = new(@"^\d{10}$", RegexOptions.Compiled);

    private static double ParseNum(string s) =>
        double.Parse(s.Replace(".", "").Replace(",", "."), CultureInfo.InvariantCulture);

    public record VpoLinha(DateTime Data, string Placa, double Realizado);

    /// <summary>
    /// Placas da frota na ordem das colunas da planilha, já no formato "AAA-0000"
    /// (é a lista de um controle novo, gerado ou feito à mão).
    /// </summary>
    public static IReadOnlyList<string> PlacasPadrao { get; } =
        PlacaCol.OrderBy(kv => kv.Value).Select(kv => SemPararPlanilhaService.NormalizarPlaca(kv.Key)).ToList();

    /// <summary>
    /// O que veio do relatório de VPO e da fatura, já com as placas no formato
    /// "AAA-0000": VPOs na ordem do relatório, fatura por placa e créditos por placa
    /// na ordem do PDF.
    /// </summary>
    public record Leitura(List<VpoLinha> Vpos, Dictionary<string, double> Fatura,
        Dictionary<string, List<double>> Creditos, List<string> Avisos);

    public Leitura Ler(byte[] vpoXlsx, byte[] faturaPdf)
    {
        var avisos = new List<string>();

        var vpos = LerVpos(vpoXlsx, out var foraDaLista);
        foreach (var (placa, qtd) in foraDaLista)
            avisos.Add($"{qtd} VPO da placa {placa} ficou de fora: a placa não está na lista da frota do Sem Parar.");
        if (vpos.Count == 0)
            avisos.Add("Nenhum VPO reconhecido no relatório (verifique se é o ConsultaViagensEmbarcador).");

        var (fatura, creditos, subtotalPdf, totalCredPdf) = LerPdf(faturaPdf);

        double somaFatura = fatura.Values.Sum();
        double somaCred = creditos.Sum(kv => kv.Value.Sum());
        if (fatura.Count == 0)
            avisos.Add("Não achei o resumo por placa na fatura (PDF). A fatura das placas ficou vazia.");
        if (subtotalPdf.HasValue && Math.Abs(somaFatura - subtotalPdf.Value) > 0.05)
            avisos.Add($"FATURA somou R$ {somaFatura:N2} mas o subtotal do PDF é R$ {subtotalPdf.Value:N2}. Confira as tags das placas.");
        if (totalCredPdf.HasValue && Math.Abs(somaCred - totalCredPdf.Value) > 0.05)
            avisos.Add($"CRÉDITOS somaram R$ {somaCred:N2} mas o total do PDF é R$ {totalCredPdf.Value:N2}.");

        string N(string p) => SemPararPlanilhaService.NormalizarPlaca(p);
        return new Leitura(
            vpos.Select(v => v with { Placa = N(v.Placa) }).ToList(),
            fatura.ToDictionary(kv => N(kv.Key), kv => Math.Round(kv.Value, 2)),
            creditos.Where(kv => kv.Value.Count > 0).ToDictionary(kv => N(kv.Key), kv => kv.Value),
            avisos);
    }

    // ───────────────────────── Relatório de VPO ─────────────────────────
    private static List<VpoLinha> LerVpos(byte[] xlsx, out Dictionary<string, int> foraDaLista)
    {
        foraDaLista = [];
        using var wb = new XLWorkbook(new MemoryStream(CorrigirNomeAba(xlsx)));
        var ws = wb.Worksheets.First();
        var lista = new List<VpoLinha>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        for (int r = 4; r <= lastRow; r++)            // dados começam na linha 4
        {
            var placa = ws.Cell(r, 6).GetString().Trim();   // F = Placa
            if (!PlacaCol.ContainsKey(placa))
            {
                if (placa.Length > 0 && LerData(ws.Cell(r, 3)) != null)
                    foraDaLista[placa] = foraDaLista.GetValueOrDefault(placa) + 1;
                continue;
            }
            var data = LerData(ws.Cell(r, 3));               // C = Data da Compra
            if (data == null) continue;
            double realizado = ws.Cell(r, 20).TryGetValue<double>(out var v) ? v : 0; // T = Valor Realizado
            lista.Add(new VpoLinha(data.Value, placa, Math.Round(realizado, 2)));
        }
        return lista;   // mantém a ordem do relatório (ordem de emissão)
    }

    // O Sem Parar exporta o xlsx com nome de aba > 31 caracteres, que o ClosedXML
    // rejeita ao abrir. Reescreve o nome da aba no workbook.xml para algo curto.
    private static byte[] CorrigirNomeAba(byte[] xlsx)
    {
        var ms = new MemoryStream();
        ms.Write(xlsx, 0, xlsx.Length);
        ms.Position = 0;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/workbook.xml");
            if (entry != null)
            {
                string content;
                using (var r = new StreamReader(entry.Open())) content = r.ReadToEnd();
                var fixado = Regex.Replace(content, "(<sheet\\b[^>]*?\\sname=\")[^\"]*(\")", "$1Dados$2");
                if (fixado != content)
                {
                    entry.Delete();
                    var novo = zip.CreateEntry("xl/workbook.xml");
                    using var w = new StreamWriter(novo.Open());
                    w.Write(fixado);
                }
            }
        }
        return ms.ToArray();
    }

    private static DateTime? LerData(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime) return cell.GetDateTime();
        var s = cell.GetString().Trim();
        if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        return null;
    }

    // ───────────────────────── PDF da fatura ─────────────────────────
    private static (Dictionary<string, double> fatura, Dictionary<string, List<double>> creditos,
                    double? subtotal, double? totalCred) LerPdf(byte[] pdf)
    {
        var fatura = new Dictionary<string, double>();
        var creditos = PlacaCol.Keys.ToDictionary(k => k, _ => new List<double>());
        double? subtotal = null, totalCred = null;

        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var texto = page.Text;

            if (texto.Contains("Resumo da sua Fatura"))
            {
                foreach (var row in Linhas(page))
                {
                    var tag = row.FirstOrDefault(t => TagRe.IsMatch(t));
                    var vals = row.Where(t => ValRe.IsMatch(t)).ToList();
                    if (tag != null && vals.Count > 0 && TagPlaca.TryGetValue(tag, out var placa))
                        fatura[placa] = fatura.GetValueOrDefault(placa) + ParseNum(vals[^1]); // soma se placa repetir
                    if (row.Any(t => t.StartsWith("Subtotal", StringComparison.OrdinalIgnoreCase)) && vals.Count > 0)
                        subtotal = ParseNum(vals[^1]);
                }
            }

            if (texto.Contains("Detalhamento de Cr"))   // Detalhamento de Créditos
            {
                foreach (var row in Linhas(page))
                {
                    if (row.Any(t => t.StartsWith("Lan", StringComparison.Ordinal)))
                    {
                        var v = row.Where(t => ValRe.IsMatch(t)).ToList();
                        if (v.Count > 0) totalCred = ParseNum(v[^1]);   // total no cabeçalho
                    }
                    if (!row.Contains("VIAGEM") && !row.Contains("CREDITO")) continue;
                    var placa = row.FirstOrDefault(t => PlacaCol.ContainsKey(t));
                    if (placa == null) continue;
                    for (int i = 0; i < row.Count - 1; i++)
                        if (ValRe.IsMatch(row[i]) && row[i + 1] == "C")
                            creditos[placa].Add(ParseNum(row[i]));
                }
            }
        }
        return (fatura, creditos, subtotal, totalCred);
    }

    // Agrupa as palavras da página em linhas (por Y) na ordem de leitura
    private static IEnumerable<List<string>> Linhas(Page page) =>
        page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom))
            .OrderByDescending(g => g.Key)              // topo da página primeiro (Y cresce pra cima)
            .Select(g => g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text).ToList());
}
