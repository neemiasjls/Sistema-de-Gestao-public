using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê um romaneio de cliente (.xlsx) guardado e devolve o que ele diz, para a
/// tela mostrar sem abrir o Excel. Acha tudo pelos rótulos da planilha, não
/// por posição fixa: os cinco modelos têm linhas em lugares diferentes e os
/// romaneios antigos do Drive também variam.
///
/// O valor a pagar é recalculado aqui (linhas + acréscimos − descontos): a
/// planilha gerada pelo sistema sai com fórmula sem valor guardado, e ler a
/// fórmula devolveria vazio.
/// </summary>
public static class RomaneioClienteLeitor
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    // ───────────────────────────── Nome do arquivo ─────────────────────────────

    public record Identificacao(string Tipo, string Apelido, DateTime? Data, int? Numero, string? Placa);

    // "164--AURORA-13-07-2026.xlsx", "AURORA-13-07-2026.xlsx" e os do Drive,
    // que têm espaço depois do traço duplo: "100-- BRZ-16-06-2025.xlsx"
    private static readonly Regex NomeXlsx = new(
        @"^(?:(\d+)\s*--?\s*)?(.+?)[\s-]+(\d{2}-\d{2}-\d{4})\s*(?:\(\d+\))?\.xlsx$", RegexOptions.IgnoreCase);

    // Relatório de frete da Girassol: "23-07-2026 XAG-2G58.txt" ou só "23-07-2026.txt"
    private static readonly Regex NomeTxt = new(
        @"^(\d{2}-\d{2}-\d{4})(?:\s+(.+?))?(?:\s*\(\d+\))?\.txt$", RegexOptions.IgnoreCase);

    public static Identificacao LerNome(string nome)
    {
        nome = (nome ?? "").Trim();

        if (nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            var t = NomeTxt.Match(nome);
            return new Identificacao("frete", "GIRASSOL",
                t.Success ? Data(t.Groups[1].Value) : null, null,
                t.Success && t.Groups[2].Success ? t.Groups[2].Value.Trim().ToUpperInvariant() : null);
        }

        var m = NomeXlsx.Match(nome);
        if (!m.Success) return new Identificacao("romaneio", "OUTROS", null, null, null);

        int? numero = m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;
        return new Identificacao("romaneio", Apelido(m.Groups[2].Value), Data(m.Groups[3].Value), numero, null);
    }

    private static DateTime? Data(string s) =>
        DateTime.TryParseExact(s, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Os arquivos antigos escrevem o cliente de outros jeitos ("FAROL",
    // "LAURO", "DISTRIBUIDORA DUNAS"); junta no apelido que o sistema usa,
    // senão a mesma loja apareceria em dois grupos na tela.
    private static string Apelido(string bruto)
    {
        var a = SemAcento(bruto).Trim().ToUpperInvariant();
        if (a.Contains("FAROL")) return "FAROL";
        if (a.Contains("DUNAS")) return "DUNAS";
        if (a.Contains("CEDRO")) return "CEDRO FRUTAS";
        if (a.Contains("AURORA") || a.Contains("LAURO")) return "AURORA";
        if (a == "BRZ" || a.StartsWith("BRZ ")) return "BRZ";
        return a;
    }

    // ───────────────────────────── Conteúdo ─────────────────────────────

    public record Linha(string Tipo, string Categoria, double Caixas, decimal? PrecoUnitario, decimal Total);
    public record ParteFunrural(string NF, decimal Valor);

    public record Resumo(
        DateTime? Data, string? Motorista, string? Placa, string? Destinatario, string? Fornecedor, string? Vendedor,
        List<Linha> Linhas,
        double TotalCaixasVolumes, double TotalCaixasProdutosD,
        decimal SomaLinhas, decimal Acrescimos, decimal Descontos, decimal ValorAPagar,
        decimal? ValorAPagarPlanilha,
        string? Funrural, List<ParteFunrural> FunruralPorNota,
        string? Boletos, List<decimal> ValoresBoletos,
        List<string> Avisos);

    private static readonly Regex ValorBr = new(@"R\$\s*([\d.]+,\d{2})");
    private static readonly Regex NotaFunrural = new(@"NF\s*(\d+)\s*-\s*R\$\s*([\d.]+,\d{2})", RegexOptions.IgnoreCase);
    private static readonly Regex SomaColunaC = new(@"SUM\(\s*\$?C\$?(\d+)\s*:\s*\$?C\$?(\d+)\s*\)", RegexOptions.IgnoreCase);

    public static Resumo Ler(byte[] conteudo)
    {
        using var wb = new XLWorkbook(new MemoryStream(conteudo));
        var ws = wb.Worksheets.First();
        var avisos = new List<string>();

        // ── Cabeçalho: o valor fica na célula à direita do rótulo ──
        DateTime? data = null;
        string? motorista = null, placa = null, destinatario = null, fornecedor = null, vendedor = null;
        int linhaTitulos = 0;

        var usadas = ws.RowsUsed().ToList();
        foreach (var row in usadas)
        {
            for (int col = 2; col <= 4; col++)
            {
                var rotulo = Rotulo(row.Cell(col));
                if (rotulo == "") continue;
                var valor = row.Cell(col + 1);

                if (rotulo == "DATA" && data == null) data = LerData(valor);
                else if (rotulo == "MOTORISTA") motorista ??= Texto(valor);
                else if (rotulo == "PLACA") placa ??= Texto(valor);
                else if (rotulo == "FORNECEDOR") fornecedor ??= Texto(valor);
                else if (rotulo.StartsWith("DESTIN")) destinatario ??= Texto(valor);
                else if (rotulo == "VENDEDOR") vendedor ??= Texto(valor);
                else if (col == 2 && rotulo.StartsWith("TIPO VOLUME") && linhaTitulos == 0) linhaTitulos = row.RowNumber();
            }
        }

        // ── Fim da tabela de tipos e linhas de totais ──
        int linhaTotalVolumes = 0, linhaAcrescimos = 0, linhaDescontos = 0;
        string? funrural = null, boletos = null;
        foreach (var row in usadas)
        {
            int r = row.RowNumber();
            var b = Rotulo(row.Cell(2));
            var d = Rotulo(row.Cell(4));

            if (linhaTotalVolumes == 0 && b.StartsWith("T.") && b.Contains("CXS") && b.Contains("VOLUME")) linhaTotalVolumes = r;
            if (linhaAcrescimos == 0 && d.StartsWith("ACRESCIMO")) linhaAcrescimos = r;
            if (linhaDescontos == 0 && d == "DESCONTOS") linhaDescontos = r;
            if (funrural == null && b.Contains("FUNRURAL")) funrural = Texto(row.Cell(2));
            if (boletos == null && b.Contains("BOLET")) boletos = Texto(row.Cell(2));
        }

        // Sem o rodapé "T. CXS VOLUMES" a tabela vai até a primeira linha de
        // totais que aparecer; sem o título, começa logo abaixo do cabeçalho.
        int inicio = linhaTitulos > 0 ? linhaTitulos + 1 : 6;
        int fim = linhaTotalVolumes > 0 ? linhaTotalVolumes - 1
                : new[] { linhaAcrescimos, linhaDescontos }.Where(x => x > 0).DefaultIfEmpty(ws.LastRowUsed()?.RowNumber() ?? 0).Min() - 1;
        if (linhaTitulos == 0 || linhaTotalVolumes == 0)
            avisos.Add("A planilha não tem o layout de sempre (título \"TIPO VOLUMES\" ou total de caixas); confira os números no Excel.");

        // ── Linhas preenchidas ──
        var linhas = new List<Linha>();
        decimal somaExata = 0;
        var caixasPorLinha = new Dictionary<int, double>();
        for (int r = inicio; r <= fim; r++)
        {
            var nomeTipo = Texto(ws.Cell(r, 2));
            if (string.IsNullOrWhiteSpace(nomeTipo) || nomeTipo.Trim().All(c => c == '/' || c == '-' || c == ' ')) continue;

            var caixas = Numero(ws.Cell(r, 3));
            var preco = Numero(ws.Cell(r, 4));
            if ((caixas ?? 0) == 0) continue;

            caixasPorLinha[r] = caixas!.Value;
            // Sem arredondar linha a linha, igual à fórmula =C*D do Excel
            decimal? precoDec = preco.HasValue ? (decimal)preco.Value : null;
            var total = precoDec.HasValue ? (decimal)caixas.Value * precoDec.Value : 0m;
            if (!precoDec.HasValue || precoDec.Value == 0)
                avisos.Add($"{nomeTipo.Trim()}: tem {caixas.Value:0.##} caixa(s) mas está sem preço.");

            somaExata += total;
            linhas.Add(new Linha(nomeTipo.Trim(), Categoria(nomeTipo), caixas.Value, precoDec, Math.Round(total, 2)));
        }

        // ── Totais de caixas: segue o intervalo que a própria planilha soma ──
        // (o modelo do FAROL, por exemplo, conta a linha de produtosD junto)
        double totalVolumes;
        var formulaVolumes = linhaTotalVolumes > 0 && ws.Cell(linhaTotalVolumes, 3).HasFormula ? ws.Cell(linhaTotalVolumes, 3).FormulaA1 : "";
        var intervalo = SomaColunaC.Match(formulaVolumes);
        if (intervalo.Success)
        {
            int de = int.Parse(intervalo.Groups[1].Value), ate = int.Parse(intervalo.Groups[2].Value);
            totalVolumes = caixasPorLinha.Where(kv => kv.Key >= de && kv.Key <= ate).Sum(kv => kv.Value);
        }
        else totalVolumes = linhas.Where(l => l.Categoria == "volume").Sum(l => l.Caixas);
        var totalProdutosD = linhas.Where(l => l.Categoria == "produtoD").Sum(l => l.Caixas);

        // ── Acréscimos, descontos e valor a pagar ──
        decimal acrescimos = linhaAcrescimos > 0 ? Math.Round((decimal)(Numero(ws.Cell(linhaAcrescimos, 5)) ?? 0), 2) : 0;
        decimal descontos = linhaDescontos > 0 ? Math.Round((decimal)(Numero(ws.Cell(linhaDescontos, 5)) ?? 0), 2) : 0;
        var soma = Math.Round(somaExata, 2);
        var valorAPagar = Math.Round(somaExata + acrescimos - descontos, 2);

        // O que o Excel gravou da última vez que abriu, quando gravou: serve só
        // para avisar se a planilha foi mexida à mão depois de gerada.
        decimal? valorPlanilha = ValorGuardadoDoTotal(ws);
        if (valorPlanilha.HasValue && Math.Abs(valorPlanilha.Value - valorAPagar) > 0.01m)
            avisos.Add($"O valor a pagar gravado no Excel ({valorPlanilha.Value.ToString("C", PtBr)}) é diferente da conta das linhas ({valorAPagar.ToString("C", PtBr)}).");

        var partesFunrural = funrural == null ? []
            : NotaFunrural.Matches(funrural).Select(m => new ParteFunrural(m.Groups[1].Value, Br(m.Groups[2].Value))).ToList();
        var valoresBoletos = boletos == null ? []
            : ValorBr.Matches(boletos).Select(m => Br(m.Groups[1].Value)).ToList();

        if (partesFunrural.Count > 0 && Math.Abs(partesFunrural.Sum(p => p.Valor) - descontos) > 0.01m)
            avisos.Add($"A soma do funrural por nota ({partesFunrural.Sum(p => p.Valor).ToString("C", PtBr)}) não bate com os descontos ({descontos.ToString("C", PtBr)}).");
        // Os boletos saem das notas (valor da NF − funrural), não das linhas da
        // planilha; somados têm de dar o valor a pagar em todos os clientes. Se
        // não dão, alguma linha foi trocada de tipo ou mexida à mão.
        // ProdutoC e produtoB vão junto na carga mas não vêm nas notas (BRZ leva
        // produtoC às vezes): ficam fora dos boletos, e isso não é erro.
        var foraDasNotas = linhas.Where(l => l.Categoria == "produtoB").Sum(l => l.Total);
        if (valoresBoletos.Count > 0
            && Math.Abs(valoresBoletos.Sum() - valorAPagar) > 0.05m
            && Math.Abs(valoresBoletos.Sum() - (valorAPagar - foraDasNotas)) > 0.05m)
            avisos.Add($"Os boletos somam {valoresBoletos.Sum().ToString("C", PtBr)}, mas o valor a pagar dá {valorAPagar.ToString("C", PtBr)}. Confira as linhas.");
        if (linhas.Count == 0) avisos.Add("Nenhum tipo de volume preenchido na planilha.");
        if (string.IsNullOrWhiteSpace(motorista)) avisos.Add("Planilha sem motorista.");
        if (string.IsNullOrWhiteSpace(placa)) avisos.Add("Planilha sem placa.");

        return new Resumo(data, motorista, placa, destinatario, fornecedor, vendedor,
            linhas, totalVolumes, totalProdutosD, soma, acrescimos, descontos, valorAPagar, valorPlanilha,
            funrural, partesFunrural, boletos, valoresBoletos, avisos);
    }

    // A célula de "VALOR A PAGAR" é a da fórmula =SUM(E..)+E..-E.. na coluna E
    private static decimal? ValorGuardadoDoTotal(IXLWorksheet ws)
    {
        foreach (var c in ws.Column(5).CellsUsed())
        {
            if (!c.HasFormula) continue;
            var f = c.FormulaA1.ToUpperInvariant();
            if (!f.StartsWith("SUM(E") || !f.Contains("-E")) continue;
            try
            {
                var v = c.CachedValue;
                if (v.IsNumber && v.GetNumber() != 0) return Math.Round((decimal)v.GetNumber(), 2);
            }
            catch { }
            return null;
        }
        return null;
    }

    private static string Categoria(string tipo)
    {
        var t = SemAcento(tipo).ToUpperInvariant();
        if (t.Contains("PRODUTOD")) return "produtoD";
        // Só as linhas fixas "Embal.Amar."/"Embal.Branca"; volume da nota pode ser "... EMBALADO"
        if (t.TrimStart().StartsWith("EMBAL") || t.Contains("PRODUTOC") || t.Contains("PRODUTOC")) return "produtoB";
        return "volume";
    }

    // Rótulo comparável: maiúsculo, sem acento, sem ":" no fim
    private static string Rotulo(IXLCell c)
    {
        if (!c.Value.IsText) return "";
        return SemAcento(c.Value.GetText()).Trim().TrimEnd(':').Trim().ToUpperInvariant();
    }

    private static string? Texto(IXLCell c)
    {
        try
        {
            if (c.Value.IsBlank) return null;
            var s = c.Value.IsText ? c.Value.GetText() : c.GetFormattedString();
            s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s == "" ? null : s;
        }
        catch { return null; }
    }

    private static double? Numero(IXLCell c)
    {
        try
        {
            if (c.Value.IsNumber) return c.Value.GetNumber();
            if (c.Value.IsText)
            {
                var s = c.Value.GetText().Replace("R$", "").Trim();
                if (s == "") return null;
                if (double.TryParse(s, NumberStyles.Number, PtBr, out var v)) return v;
            }
        }
        catch { }
        return null;
    }

    private static DateTime? LerData(IXLCell c)
    {
        try
        {
            if (c.Value.IsDateTime) return c.Value.GetDateTime().Date;
            if (c.Value.IsNumber) return DateTime.FromOADate(c.Value.GetNumber()).Date;
            if (c.Value.IsText && DateTime.TryParse(c.Value.GetText(), PtBr, DateTimeStyles.None, out var d)) return d.Date;
        }
        catch { }
        return null;
    }

    private static decimal Br(string s) => decimal.Parse(s, PtBr);

    private static string SemAcento(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
