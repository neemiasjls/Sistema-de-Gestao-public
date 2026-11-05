using System.Text.RegularExpressions;
using ClosedXML.Excel;
using TransportadoraSystem.Core.Entities;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// ProdutosC da BRZ (NFE-05, EST-07): a produtoC do controle de carregamento é a
/// mesma que o romaneio da nota da BRZ cobra (linha "ProdutosC", C = quantidade,
/// D = preço). Não se cobra duas vezes: a saída do estoque marca que foi cobrada
/// no romaneio. Ao importar o controle, a quantidade do romaneio guardado do mesmo
/// dia passa a ser a do controle, sempre com aviso quando eram diferentes.
/// </summary>
public static class ProdutosCBrz
{
    public const string Apelido = "BRZ";
    public const string TextoCobrada = "Romaneio da nota da BRZ";

    public static bool EhBrz(PrecoFreteCliente? frete, string nomeNoControle)
    {
        var nomes = new[] { nomeNoControle, frete?.Cliente ?? "" }
            .Concat((frete?.Apelidos ?? "").Split(','))
            .Select(Normalizar).Where(n => n.Length > 0);
        return nomes.Any(n => n == Apelido || n.StartsWith(Apelido + " "));
    }

    /// <summary>Nome do romaneio guardado: "156--BRZ-02-10-2026.xlsx" ou "BRZ-02-10-2026.xlsx".</summary>
    public static bool RomaneioDoDia(string nomeArquivo, DateOnly dia) =>
        Regex.IsMatch(nomeArquivo.Trim(), $@"(^|--)BRZ-{dia:dd-MM-yyyy}\.xlsx$", RegexOptions.IgnoreCase);

    /// <summary>Data do romaneio pelo nome; null quando não é da BRZ.</summary>
    public static DateOnly? DiaDoRomaneio(string nomeArquivo)
    {
        var m = Regex.Match(nomeArquivo.Trim(), @"(^|--)BRZ-(\d{2})-(\d{2})-(\d{4})\.xlsx$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        try { return new DateOnly(int.Parse(m.Groups[4].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public record Linha(decimal Quantidade, decimal? Preco, string? Placa);

    /// <summary>Quantidade e preço da linha "ProdutosC" do romaneio; sem a linha, null.</summary>
    public static Linha? Ler(byte[] xlsx)
    {
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheets.First();
        var r = LinhaProdutosC(ws);
        if (r == null) return null;
        return new Linha(Numero(ws.Cell(r.Value, 3)) ?? 0, Numero(ws.Cell(r.Value, 4)), Texto(ws.Cell("E3")));
    }

    /// <summary>Grava a quantidade (zero deixa a linha em branco, como o romaneio sem produtoC).</summary>
    public static byte[]? Gravar(byte[] xlsx, decimal quantidade, decimal precoSeVazio)
    {
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheets.First();
        var r = LinhaProdutosC(ws);
        if (r == null) return null;
        if (quantidade <= 0)
        {
            ws.Cell(r.Value, 3).Clear(XLClearOptions.Contents);
            ws.Cell(r.Value, 4).Clear(XLClearOptions.Contents);
        }
        else
        {
            ws.Cell(r.Value, 3).Value = (double)quantidade;
            if (Numero(ws.Cell(r.Value, 4)) is null or 0) ws.Cell(r.Value, 4).Value = (double)precoSeVazio;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static int? LinhaProdutosC(IXLWorksheet ws)
    {
        foreach (var row in ws.RowsUsed())
        {
            var b = row.Cell(2);
            if (b.Value.IsText && Normalizar(b.Value.GetText()).Replace(" ", "") is "PRODUTOSC" or "PRODUTOSC") return row.RowNumber();
        }
        return null;
    }

    private static decimal? Numero(IXLCell c) => c.Value.IsNumber ? (decimal)c.Value.GetNumber() : null;
    private static string? Texto(IXLCell c) => c.Value.IsText ? c.Value.GetText() : null;
}
