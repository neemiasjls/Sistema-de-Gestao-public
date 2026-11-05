using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê as planilhas de controle que eram preenchidas à mão (CONTROLE PRODUTOSA e
/// CONTROLE DE PRODUTOSB) para trazer o histórico pra dentro do sistema.
///
/// Os dois arquivos têm posição fixa de cabeçalho, mas o fim da lista de saídas
/// varia, então os limites são achados pelos rótulos e não por número de linha.
/// </summary>
public class ImportacaoEstoqueService
{
    public record CargaProdutoALida(DateOnly Data, string Fornecedor, int Quantidade,
        decimal ValorTotal, decimal CustoKg, string? Controle);

    public record VendaProdutoALida(string Cliente, DateOnly Data, int? Espessura, List<int> Pesos);

    public record ProdutosALidas(CargaProdutoALida Carga, List<VendaProdutoALida> Vendas);

    public record TipoLido(string Nome, int Entrada);

    public record CargaProdutoBLida(DateOnly Data, string Fornecedor, int QuantidadePacotes,
        decimal ValorProdutosB, decimal ValorFrete, string? Transportador, string? Observacao,
        List<TipoLido> Tipos);

    public record ItemVendaLido(string Tipo, int Pacotes, int Unidades);

    public record VendaProdutoBLida(string Cliente, DateOnly Data, List<ItemVendaLido> Itens);

    public record ProdutosBLidas(CargaProdutoBLida Carga, List<VendaProdutoBLida> Vendas);

    // ── ProdutosA ───────────────────────────────────────────────────────────

    public ProdutosALidas LerProdutosA(byte[] xlsx)
    {
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheet(1);

        var data = Data(ws.Cell("B2")) ?? throw new InvalidOperationException(
            "Não achei a data do pedido em B2. Essa planilha é o CONTROLE DE PRODUTOSA?");

        // O custo por kg fica ao lado do rótulo VALOR UNIT, que muda de linha
        // conforme o tamanho da lista.
        decimal custoKg = 0;
        foreach (var celula in ws.CellsUsed(c => Norm(c.GetString()).StartsWith("VALOR UNIT")))
        {
            custoKg = Numero(ws.Cell(celula.Address.RowNumber, celula.Address.ColumnNumber + 1));
            if (custoKg > 0) break;
        }

        var carga = new CargaProdutoALida(
            data,
            ws.Cell("B3").GetString().Trim(),
            (int)Numero(ws.Cell("F2")),
            Numero(ws.Cell("E3")),
            custoKg,
            ws.Cell("H3").GetString().Trim());

        // Uma linha por produtoA. Vira venda o conjunto do mesmo cliente, no mesmo
        // dia, com a mesma espessura — que é como o romaneio sai.
        var vendas = new List<VendaProdutoALida>();
        var indice = new Dictionary<string, VendaProdutoALida>();

        for (int r = 5; r <= ws.LastRowUsed()!.RowNumber(); r++)
        {
            if (Norm(ws.Cell(r, 2).GetString()).StartsWith("PESO TOTAL")) break;

            var cliente = ws.Cell(r, 1).GetString().Trim();
            var dataSaida = Data(ws.Cell(r, 2));
            int peso = (int)Numero(ws.Cell(r, 3));
            if (cliente.Length == 0 || dataSaida == null || peso <= 0) continue;

            int? espessura = Numero(ws.Cell(r, 4)) is var e && e > 0 ? (int)e : null;

            var chave = $"{Norm(cliente)}|{dataSaida:yyyy-MM-dd}|{espessura}";
            if (!indice.TryGetValue(chave, out var venda))
            {
                venda = new VendaProdutoALida(cliente.Trim().ToUpperInvariant(), dataSaida.Value, espessura, []);
                indice[chave] = venda;
                vendas.Add(venda);
            }
            venda.Pesos.Add(peso);
        }

        return new ProdutosALidas(carga, vendas);
    }

    // ── ProdutosB ────────────────────────────────────────────────────────

    public ProdutosBLidas LerProdutosB(byte[] xlsx)
    {
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheet(1);
        int ultima = ws.LastRowUsed()!.RowNumber();

        var data = Data(ws.Cell("G6")) ?? throw new InvalidOperationException(
            "Não achei a data do pedido em G6. Essa planilha é o CONTROLE DE PRODUTOSB?");

        // Pacotes comprados: as linhas de entrada logo abaixo do cabeçalho.
        int comprados = 0;
        for (int r = 6; r <= 8; r++) comprados += (int)Numero(ws.Cell(r, 13));

        // Tabela de vendas: cada tipo ocupa duas colunas, a do pacote e a das
        // unidades avulsas. É o "Unid" ao lado que diz onde os tipos terminam —
        // depois deles vêm as colunas de valor.
        int cabecalho = 0;
        for (int r = 1; r <= ultima; r++)
            if (Norm(ws.Cell(r, 7).GetString()) == "DATA" && Norm(ws.Cell(r, 8).GetString()).StartsWith("CLIENTE"))
            { cabecalho = r; break; }

        if (cabecalho == 0)
            throw new InvalidOperationException("Não achei a tabela de vendas (cabeçalho DATA / CLIENTES).");

        var colunas = new List<(string Tipo, int ColPacote, int ColUnidade)>();
        for (int col = 9; col <= 20; col += 2)
        {
            var nome = ws.Cell(cabecalho, col).GetString().Trim();
            if (nome.Length == 0) break;
            // Exatamente "Unid": a coluna VALOR é seguida de "UNIDADE", que
            // passaria num teste de prefixo e entraria como se fosse um tipo.
            if (Norm(ws.Cell(cabecalho, col + 1).GetString()) != "UNID") break;
            colunas.Add((nome.ToUpperInvariant(), col, col + 1));
        }

        // Entrada por tipo: linha TRX. COMÉRCIO, nomes na linha de cima. Fica
        // antes do bloco ESTOQUES, que repete os mesmos rótulos mais à direita —
        // por isso a leitura para na coluna O e segue a ordem dos tipos acima.
        var tipos = new List<TipoLido>();
        for (int r = 1; r <= ultima; r++)
        {
            if (!Norm(ws.Cell(r, 7).GetString()).StartsWith("TRX")) continue;
            int i = 0;
            for (int col = 8; col <= 15 && i < colunas.Count; col++)
            {
                if (ws.Cell(r - 1, col).GetString().Trim().Length == 0) continue;
                tipos.Add(new TipoLido(colunas[i].Tipo, (int)Numero(ws.Cell(r, col))));
                i++;
            }
            break;
        }

        var carga = new CargaProdutoBLida(data,
            ws.Cell("H6").GetString().Trim(),
            comprados,
            Numero(ws.Cell("M11")),
            Numero(ws.Cell("M12")),
            ws.Cell("I18").GetString().Trim(),
            ws.Cell("G15").GetString().Trim(),
            tipos);

        var vendas = new List<VendaProdutoBLida>();
        for (int r = cabecalho + 1; r <= ultima; r++)
        {
            var rotulo = Norm(ws.Cell(r, 7).GetString());
            if (rotulo.StartsWith("CONTROLE") || rotulo.StartsWith("TOTAL")) break;

            var cliente = ws.Cell(r, 8).GetString().Trim();
            var dataSaida = Data(ws.Cell(r, 7));
            if (cliente.Length == 0 || dataSaida == null) continue;

            var itens = new List<ItemVendaLido>();
            foreach (var (tipo, cp, cu) in colunas)
            {
                int pacotes = (int)Numero(ws.Cell(r, cp));
                int unidades = (int)Numero(ws.Cell(r, cu));
                if (pacotes > 0 || unidades > 0) itens.Add(new ItemVendaLido(tipo, pacotes, unidades));
            }
            if (itens.Count == 0) continue;

            vendas.Add(new VendaProdutoBLida(cliente.ToUpperInvariant(), dataSaida.Value, itens));
        }

        return new ProdutosBLidas(carga, vendas);
    }

    // ── Leitura tolerante ─────────────────────────────────────────────────

    private static DateOnly? Data(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.DataType == XLDataType.DateTime && c.GetDateTime() != default)
            return DateOnly.FromDateTime(c.GetDateTime());
        var texto = c.GetString().Trim();
        return DateTime.TryParse(texto, new CultureInfo("pt-BR"), DateTimeStyles.None, out var d)
            ? DateOnly.FromDateTime(d) : null;
    }

    private static decimal Numero(IXLCell c)
    {
        if (c.IsEmpty()) return 0;
        if (c.DataType == XLDataType.Number) return (decimal)c.GetDouble();
        if (c.TryGetValue<double>(out var d)) return (decimal)d;
        var texto = c.GetString().Replace(".", "").Replace(",", ".").Trim();
        return decimal.TryParse(texto, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public static string Norm(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var ch in texto.Normalize(NormalizationForm.FormKD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        return string.Join(" ", sb.ToString().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Para casar "MdoVr" do cabeçalho com "MDO VR" do cadastro.
    // A planilha de controle escreve GDE BR / GDE VR / MDO VR; no sistema o
    // nome é por extenso ("Pacote amarelo (volume grande branco)"). Os dois viram
    // a mesma chave, senão a importação criaria um tipo novo para cada nome.
    // Pacote amarelo vai no volume branco; pacote branco, no volume vermelho.
    public static string NormTipo(string? texto)
    {
        var n = Norm(texto).Replace(" ", "");
        if (n == "GDEBR" || n.Contains("AMAREL")) return "GDEBR";
        if (n == "MDOVR" || n.Contains("MEDIO")) return "MDOVR";
        if (n == "GDEVR" || n.Contains("PACOTEBRANCO")) return "GDEVR";
        return n;
    }
}
