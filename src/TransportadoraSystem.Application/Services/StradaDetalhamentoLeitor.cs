using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// "Exportar detalhamento" da fatura de repasse da Strada (.xlsx). Colunas pelo nome do cabeçalho
/// (CONTRATO, VALOR OPERAÇÃO, VALOR TAXA ADM, VALOR REPASSE, DATA), sem depender da posição; os acentos
/// do cabeçalho podem vir quebrados, então a comparação usa só letras e números. Não traz o número
/// da fatura: quem chama acha a fatura pelo total.
/// </summary>
public static class StradaDetalhamentoLeitor
{
    public record Contrato(string Numero, decimal Valor, decimal Taxa, decimal Repasse, DateOnly? Data);

    public record Detalhamento(List<Contrato> Contratos, decimal Total, decimal Taxa, decimal Repasse);

    public record Resultado(Detalhamento? Detalhamento, string? Erro);

    public static Resultado Ler(byte[] xlsx)
    {
        try
        {
            using var stream = new MemoryStream(xlsx);
            using var wb = new XLWorkbook(stream);
            foreach (var ws in wb.Worksheets)
            {
                var r = LerAba(ws);
                if (r.Detalhamento != null) return r;
            }
            return new(null, "Não achei as colunas CONTRATO e VALOR OPERAÇÃO.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(null, "Não consegui abrir a planilha.");
        }
    }

    private static Resultado LerAba(IXLWorksheet ws)
    {
        var usado = ws.RangeUsed();
        if (usado == null) return new(null, null);
        int ultimaLinha = usado.LastRow().RowNumber(), ultimaColuna = usado.LastColumn().ColumnNumber();
        for (var cab = 1; cab <= Math.Min(10, ultimaLinha); cab++)
        {
            var cols = new Dictionary<string, int>();
            for (var c = 1; c <= ultimaColuna; c++)
            {
                var nome = Chave(ws.Cell(cab, c).GetString());
                if (nome.Length > 0) cols.TryAdd(nome, c);
            }
            int? Col(string inicio) => cols.FirstOrDefault(k => k.Key.StartsWith(inicio)).Value is var v && v > 0 ? v : null;
            var cContrato = cols.TryGetValue("CONTRATO", out var cc) ? cc : (int?)null;
            var cValor = Col("VALOROPERA");
            if (cContrato == null || cValor == null) continue;
            var cTaxa = Col("VALORTAXA");
            var cRepasse = Col("VALORREPASSE");
            var cData = cols.TryGetValue("DATA", out var cd) ? cd : (int?)null;

            var linhas = new List<(string Contrato, decimal Valor, decimal Taxa, decimal Repasse, DateOnly? Data)>();
            for (var l = cab + 1; l <= ultimaLinha; l++)
            {
                var contrato = new string(ws.Cell(l, cContrato.Value).GetString().Where(char.IsDigit).ToArray());
                if (contrato.Length == 0) continue;
                var valor = Dinheiro(ws.Cell(l, cValor.Value)) ?? 0;
                var taxa = cTaxa is { } t ? Dinheiro(ws.Cell(l, t)) ?? 0 : 0;
                var repasse = cRepasse is { } rp ? Dinheiro(ws.Cell(l, rp)) ?? valor - taxa : valor - taxa;
                linhas.Add((contrato, valor, taxa, repasse, cData is { } dc ? DataDe(ws.Cell(l, dc)) : null));
            }
            if (linhas.Count == 0) return new(null, "A planilha não tem contratos.");
            var contratos = linhas.GroupBy(x => x.Contrato)
                .Select(g => new Contrato(g.Key, g.Sum(x => x.Valor), g.Sum(x => x.Taxa), g.Sum(x => x.Repasse), g.Min(x => x.Data)))
                .ToList();
            return new(new(contratos, contratos.Sum(c => c.Valor), contratos.Sum(c => c.Taxa), contratos.Sum(c => c.Repasse)), null);
        }
        return new(null, null);
    }

    // Só letras e números, sem acento: "VALOR OPERA��O" e "VALOR OPERAÇÃO" viram "VALOROPERA..."
    private static string Chave(string s)
    {
        var sem = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in sem.ToUpperInvariant())
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(ch);
        return sb.ToString();
    }

    private static decimal? Dinheiro(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number) return Math.Round((decimal)cell.GetDouble(), 2);
        var s = new string(cell.GetString().Where(ch => char.IsDigit(ch) || ch is ',' or '.' or '-').ToArray());
        if (s.Length == 0) return null;
        if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateOnly? DataDe(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        var s = cell.GetString().Trim();
        if (s.Length >= 10 && DateOnly.TryParseExact(s[..10], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }
}
