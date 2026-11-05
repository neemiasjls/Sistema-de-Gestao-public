using ClosedXML.Excel;
using TransportadoraSystem.Core.Entities;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Romaneio de uma venda, no mesmo modelo usado à mão: CONTROLE DE REMESSAS DE
/// MERCADORIAS, com as duas vias na mesma folha (a de baixo só espelha a de
/// cima). ProdutoA cobra por peso (valor = peso em gramas × R$/kg ÷ 1000);
/// produtoB cobra por quantidade (valor = qde × R$/pacote).
/// </summary>
public class RomaneioEstoqueService
{
    // Onde cada coisa mora no modelo. A segunda via repete o bloco mais abaixo.
    private record Layout(
        int Cliente1, int Data1, int Inicio1, int Fim1,
        int Cliente2, int Data2, int Inicio2);

    //                                        cli1 dat1 ini1 fim1 cli2 dat2 ini2
    private static readonly Layout Bob = new(    5,   6,   9,  23,  31,  32,  35);
    private static readonly Layout Emb = new(    5,   6,   9,  26,  33,  34,  37);

    public record Linha(DateOnly Data, string Produto, string Descricao,
        double Qde, double Peso, decimal ValorUnitario);

    private byte[] Gerar(string template, Layout l, string cliente, string? cidade,
        DateOnly data, List<Linha> linhas, bool porPeso)
    {
        var asm = typeof(RomaneioEstoqueService).Assembly;
        using var stream = asm.GetManifestResourceStream($"TransportadoraSystem.Application.Templates.{template}.xlsx")
            ?? throw new InvalidOperationException($"Modelo {template}.xlsx não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var wb = new XLWorkbook(new MemoryStream(ms.ToArray()));
        var ws = wb.Worksheets.First();

        int vagas = l.Fim1 - l.Inicio1 + 1;
        int sobrando = linhas.Count - vagas;

        // Mais linhas do que o modelo comporta: abre espaço nos dois blocos, de
        // baixo pra cima, para não empurrar o bloco de cima enquanto mexo nele.
        int fim1 = l.Fim1, inicio2 = l.Inicio2;
        if (sobrando > 0)
        {
            ws.Row(l.Inicio2 + vagas - 1).InsertRowsAbove(sobrando);
            ws.Row(l.Fim1).InsertRowsAbove(sobrando);
            fim1 += sobrando;
            inicio2 += sobrando;   // o 1º bloco empurrou o 2º pra baixo
        }

        ws.Cell(l.Cliente1, 2).Value = cliente;
        ws.Cell(l.Cliente1, 6).Value = cidade ?? "";
        ws.Cell(l.Data1, 2).Value = data.ToDateTime(TimeOnly.MinValue);
        ws.Cell(l.Data1, 2).Style.DateFormat.Format = "DD/MM/YYYY";

        for (int i = 0; i < linhas.Count; i++)
        {
            int r = l.Inicio1 + i;
            var x = linhas[i];
            ws.Cell(r, 1).Value = x.Data.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 1).Style.DateFormat.Format = "DD/MM/YYYY";
            ws.Cell(r, 2).Value = x.Produto;
            ws.Cell(r, 3).Value = x.Descricao;
            ws.Cell(r, 4).Value = x.Qde;
            ws.Cell(r, 5).Value = x.Peso;
            ws.Cell(r, 6).Value = x.ValorUnitario;
            ws.Cell(r, 7).FormulaA1 = porPeso ? $"E{r}*F{r}/1000" : $"D{r}*F{r}";
        }

        // Sobrou linha do modelo: a de produtoB fica com o tracinho, como na
        // planilha feita à mão.
        if (!porPeso)
            for (int r = l.Inicio1 + linhas.Count; r <= fim1; r++)
                for (int col = 1; col <= 3; col++)
                    ws.Cell(r, col).Value = "-";

        // Segunda via: reescrevo as referências em vez de confiar nas que o
        // modelo traz, porque as linhas podem ter mudado de lugar.
        for (int i = 0; i < linhas.Count; i++)
        {
            int origem = l.Inicio1 + i;
            int destino = inicio2 + i;
            for (int col = 1; col <= 6; col++)
                ws.Cell(destino, col).FormulaA1 = $"{Letra(col)}{origem}";
            ws.Cell(destino, 7).FormulaA1 = porPeso
                ? $"E{destino}*F{destino}/1000"
                : $"D{destino}*F{destino}";
        }

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);
        return outMs.ToArray();
    }

    private static char Letra(int col) => (char)('A' + col - 1);

    // ── ProdutosA ───────────────────────────────────────────────────────────

    public (byte[] bytes, string nome) ProdutosA(VendaProdutoA venda, EstoqueCliente cliente)
    {
        var linhas = venda.ProdutosA
            .OrderBy(b => b.Id)
            .Select(b => new Linha(venda.Data, "PRODUTOA", "CX", 1, b.PesoGramas, venda.PrecoKg))
            .ToList();

        var bytes = Gerar("ROMANEIO_PRODUTOSA", Bob, cliente.Nome, cliente.Cidade,
            venda.Data, linhas, porPeso: true);

        return (bytes, NomeArquivo(venda.Numero, cliente.Nome, "", venda.Data));
    }

    // ── ProdutosB ────────────────────────────────────────────────────────
    // Pacote e unidade avulsa viram linhas separadas: o preço da avulsa é o do
    // pacote dividido pelo tanto que cabe nele.

    public (byte[] bytes, string nome) ProdutosB(VendaProdutoB venda, EstoqueCliente cliente,
        List<TipoProdutoB> tipos)
    {
        var linhas = new List<Linha>();
        foreach (var item in venda.Itens.OrderBy(i => i.TipoProdutoBId))
        {
            var tipo = tipos.FirstOrDefault(t => t.Id == item.TipoProdutoBId);
            string nomeTipo = tipo?.Nome ?? "PRODUTOB";
            int porPacote = tipo?.UnidadesPorPacote ?? 150;
            if (porPacote <= 0) porPacote = 150;

            if (item.Pacotes > 0)
                linhas.Add(new Linha(venda.Data, "PRODUTOSB", nomeTipo, item.Pacotes, 1, item.Preco));

            if (item.Unidades > 0)
                linhas.Add(new Linha(venda.Data, "PRODUTOSB", $"{nomeTipo} (UNID.)",
                    item.Unidades, 1, Math.Round(item.Preco / porPacote, 4)));
        }

        // Frete por pacote, numa linha própria: entra no VALOR TOTAL do cliente.
        // Unidade avulsa paga frete na proporção do pacote.
        if (venda.FretePacote > 0)
        {
            double pacotes = venda.Itens.Sum(i =>
            {
                int porPacote = tipos.FirstOrDefault(t => t.Id == i.TipoProdutoBId)?.UnidadesPorPacote ?? 150;
                return i.Pacotes + (double)i.Unidades / (porPacote <= 0 ? 150 : porPacote);
            });
            linhas.Add(new Linha(venda.Data, "FRETE", "FRETE POR PACOTE", Math.Round(pacotes, 2), 1, venda.FretePacote));
        }

        var bytes = Gerar("ROMANEIO_PRODUTOSB", Emb, cliente.Nome, cliente.Cidade,
            venda.Data, linhas, porPeso: false);

        return (bytes, NomeArquivo(venda.Numero, cliente.Nome, " EMB.", venda.Data));
    }

    private static string NomeArquivo(int? numero, string cliente, string sufixo, DateOnly data)
    {
        var limpo = new string(cliente.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        var prefixo = numero.HasValue ? $"{numero}--" : "";
        return $"{prefixo}{limpo}{sufixo}-{data:dd-MM-yyyy}.xlsx";
    }
}
