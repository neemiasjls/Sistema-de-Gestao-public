using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// A planilha "{número}IP-TRANSFERÊNCIA ({vencimento}).xlsx" de uma transferência da Strada, no modelo
/// TIP_TRANSFERENCIA.xlsx (feito da 511): três blocos por sócio (BENTO D:G, CAIO AD:AG, DUDU AK:AN), um
/// contrato por linha a partir da 3, em ordem crescente. As fórmulas e os estilos vêm do molde; mais de 9 contratos
/// num bloco insere linhas acima da 11 (na planilha toda) e as fórmulas se esticam.
/// </summary>
public static class TipTransferenciaPlanilha
{
    public record Linha(string Contrato, string? Placa, decimal Valor);
    public record Dados(int Numero, DateOnly Vencimento, IReadOnlyList<Linha> Bento, IReadOnlyList<Linha> Caio, IReadOnlyList<Linha> Dudu);

    public const int PrimeiraLinha = 3;
    public const int LinhasModelo = 9;               // 3 a 11
    public static readonly int[] ColunaBloco = { 4, 30, 37 };   // D, AD, AK (CONTRATO; depois PLACA, VALOR, CRÉDITO)

    public static string NomeArquivo(int numero, DateOnly vencimento) =>
        $"{numero}IP-TRANSFERÊNCIA ({vencimento:dd-MM-yyyy}).xlsx";

    public static (byte[] Bytes, string Nome) Gerar(Dados d)
    {
        var blocos = new[] { d.Bento, d.Caio, d.Dudu };

        var asm = typeof(TipTransferenciaPlanilha).Assembly;
        using var stream = asm.GetManifestResourceStream("TransportadoraSystem.Application.Templates.TIP_TRANSFERENCIA.xlsx")
            ?? throw new InvalidOperationException("Template TIP_TRANSFERENCIA.xlsx não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var wb = new XLWorkbook(new MemoryStream(ms.ToArray()));
        var ws = wb.Worksheet("Plan1");

        int maior = blocos.Max(b => b.Count);
        if (maior > LinhasModelo)
            ws.Row(PrimeiraLinha + LinhasModelo - 2).InsertRowsBelow(maior - LinhasModelo);   // acima da linha 11

        for (int b = 0; b < blocos.Length; b++)
        {
            int col = ColunaBloco[b];
            int linha = PrimeiraLinha;
            foreach (var l in blocos[b].OrderBy(x => NumeroContrato(x.Contrato)).ThenBy(x => x.Contrato, StringComparer.Ordinal))
            {
                var num = NumeroContrato(l.Contrato);
                if (num.HasValue) ws.Cell(linha, col).Value = num.Value;
                else ws.Cell(linha, col).Value = l.Contrato.Trim();
                var placa = (l.Placa ?? "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
                if (placa.Length > 0) ws.Cell(linha, col + 1).Value = placa;
                ws.Cell(linha, col + 2).Value = l.Valor;
                linha++;
            }
        }

        using var saida = new MemoryStream();
        wb.SaveAs(saida);
        return (saida.ToArray(), NomeArquivo(d.Numero, d.Vencimento));
    }

    static long? NumeroContrato(string c) => long.TryParse(c.Trim(), out var n) ? n : null;
}
