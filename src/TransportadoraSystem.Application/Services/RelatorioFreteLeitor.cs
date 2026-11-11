using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê o relatório de frete da Girassol (.txt), o mesmo que a tela Planilhas NF-e gera
/// (NFeImportService.GerarRelatorioFreteTexto) e guarda em PlanilhasCarga com o nome
/// "dd-MM-yyyy PLACA.txt". Tira a data da carga, as caixas normais e de produtoE e o
/// TOTAL FRETE; a placa vem do nome do arquivo, porque o texto não a traz.
/// </summary>
public static class RelatorioFreteLeitor
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public record Relatorio(
        DateOnly? Data, string? Placa, double? CaixasNormais, double? CaixasProdutoE, decimal? TotalFrete,
        List<string> NFs, List<string> Avisos,
        List<LinhaNota>? Linhas = null, decimal? FreteCaixaNormal = null, decimal? FreteCaixaProdutoE = null,
        int? NumeroBoleto = null);

    /// <summary>Uma linha de nota do relatório; NF nula = produtoE digitada à mão ("sem NF").</summary>
    public record LinhaNota(string? NF, double Caixas, bool ProdutoE, string Emitente);

    private static readonly Regex DataRx = new(@"Data:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
    private static readonly Regex NormaisRx = new(@"Total\s+caixas\s+normais\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
    private static readonly Regex ProdutoERx = new(@"Total\s+caixas\s+produtoE\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
    private static readonly Regex TotalRx = new(@"TOTAL\s+FRETE:\s*R\$\s*([\d.]+,\d{2})", RegexOptions.IgnoreCase);
    private static readonly Regex LinhaNfRx = new(
        @"^\s*NF\s+(\d+)\s+-\s*([\d.,]+)\s+caixas\s*(?:\[(NORMAL|PRODUTOE)\])?\s*(?:\((.*)\))?\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex LinhaSemNfRx = new(
        @"^\s*\(sem NF\)\s*-?\s*([\d.,]+)\s+caixas\s*(?:\[(NORMAL|PRODUTOE)\])?\s*(?:\((.*)\))?\s*$",
        RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex FreteNormaisRx = new(
        @"Frete\s+normais\s*\([^)]*?R\$\s*([\d.]+,\d{2})\s*\)", RegexOptions.IgnoreCase);
    private static readonly Regex FreteProdutosERx = new(
        @"Frete\s+produtosE\s*\([^)]*?R\$\s*([\d.]+,\d{2})\s*\)", RegexOptions.IgnoreCase);
    private static readonly Regex NumeroBoletoRx = new(@"^\s*(\d+)\s*-\s*GIRASSOL", RegexOptions.IgnoreCase);
    private static readonly Regex NfRx = new(@"^\s*NF\s+(\d+)\s+-", RegexOptions.Multiline);

    /// <summary>É um relatório de frete? (o cabeçalho que o sistema escreve)</summary>
    public static bool EhRelatorio(string texto) =>
        texto.Contains("RELATORIO DE FRETE", StringComparison.OrdinalIgnoreCase) && TotalRx.IsMatch(texto);

    public static string Texto(byte[] conteudo) => Encoding.UTF8.GetString(conteudo).TrimStart('﻿');

    public static Relatorio Ler(byte[] conteudo, string nomeArquivo) => Ler(Texto(conteudo), nomeArquivo);

    /// <summary>Título do bloco da queda do volume acrescentado no fim do .txt (FretePerfilCalculo.ComQueda).</summary>
    public const string TituloQueda = "QUEDA DO VOLUME";

    // Começo do bloco: a linha "====" logo antes do título; sem ela, a própria linha do título.
    private static readonly Regex InicioQuedaRx = new(
        @"^[ \t]*=+[ \t]*\r?\n[ \t]*QUEDA DO VOLUME|^[ \t]*QUEDA DO VOLUME", RegexOptions.Multiline);

    /// <summary>O .txt sem o bloco "QUEDA DO VOLUME" (como saiu das notas). Sem o bloco, devolve o mesmo texto.</summary>
    public static string SemQueda(string texto)
    {
        var m = InicioQuedaRx.Match(texto);
        if (!m.Success) return texto;
        var antes = texto[..m.Index];
        // ComQueda põe uma linha em branco antes do bloco: tira só ela.
        if (antes.EndsWith("\r\n\r\n")) antes = antes[..^2];
        else if (antes.EndsWith("\n\n")) antes = antes[..^1];
        return antes;
    }

    public static Relatorio Ler(string texto, string nomeArquivo)
    {
        // O bloco da queda (acrescentado depois) não entra na leitura: TOTAL FRETE e notas são os das notas.
        texto = SemQueda(texto);
        var avisos = new List<string>();
        var ident = RomaneioClienteLeitor.LerNome(nomeArquivo ?? "");

        DateOnly? data = null;
        var md = DataRx.Match(texto);
        if (md.Success && DateOnly.TryParseExact(md.Groups[1].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            data = d;
        if (data == null && ident.Data is DateTime dn)
        {
            data = DateOnly.FromDateTime(dn);
            avisos.Add($"{nomeArquivo}: o relatório não traz a data; valeu a do nome do arquivo, {data:dd/MM/yyyy}.");
        }
        else if (data != null && ident.Data is DateTime dnome && DateOnly.FromDateTime(dnome) != data)
            avisos.Add($"{nomeArquivo}: o nome do arquivo diz {dnome:dd/MM/yyyy}, mas o relatório é de {data:dd/MM/yyyy}. Valeu a do relatório.");
        if (data == null) avisos.Add($"{nomeArquivo}: não achei a data da carga.");

        var normais = Numero(NormaisRx, texto);
        var produtoE = Numero(ProdutoERx, texto);
        if (normais == null) avisos.Add($"{nomeArquivo}: não achei o total de caixas normais.");
        if (produtoE == null) avisos.Add($"{nomeArquivo}: não achei o total de caixas de produtoE.");

        decimal? total = null;
        var mt = TotalRx.Match(texto);
        if (mt.Success && decimal.TryParse(mt.Groups[1].Value, NumberStyles.Number, PtBr, out var t)) total = t;
        if (total == null) avisos.Add($"{nomeArquivo}: não achei o TOTAL FRETE.");

        var placa = string.IsNullOrWhiteSpace(ident.Placa) ? null : EmailClienteService.Placa(ident.Placa);
        if (placa == null) avisos.Add($"{nomeArquivo}: o nome do arquivo não traz a placa do caminhão.");

        var nfs = NfRx.Matches(texto).Select(m => m.Groups[1].Value).ToList();

        var linhas = new List<LinhaNota>();
        foreach (Match m in LinhaNfRx.Matches(texto))
            if (double.TryParse(m.Groups[2].Value, NumberStyles.Number, PtBr, out var cx))
                linhas.Add(new LinhaNota(m.Groups[1].Value, cx, ProdutoE(m.Groups[3].Value), m.Groups[4].Value.Trim()));
        foreach (Match m in LinhaSemNfRx.Matches(texto))
            if (double.TryParse(m.Groups[1].Value, NumberStyles.Number, PtBr, out var cx))
                linhas.Add(new LinhaNota(null, cx, ProdutoE(m.Groups[2].Value), m.Groups[3].Value.Trim()));
        if (linhas.Count == 0) avisos.Add($"{nomeArquivo}: não achei as linhas das notas.");

        var freteNormal = Valor(FreteNormaisRx, texto);
        var freteProdutoE = Valor(FreteProdutosERx, texto);
        if (freteNormal == null) avisos.Add($"{nomeArquivo}: não achei o frete por caixa das normais.");
        if (freteProdutoE == null) avisos.Add($"{nomeArquivo}: não achei o frete por caixa da produtoE.");

        int? numero = null;
        var primeira = texto.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        var mn = NumeroBoletoRx.Match(primeira);
        if (mn.Success && int.TryParse(mn.Groups[1].Value, out var nb)) numero = nb;
        if (numero == null) avisos.Add($"{nomeArquivo}: não achei o número do boleto na primeira linha.");

        return new Relatorio(data, placa, normais, produtoE, total, nfs, avisos, linhas, freteNormal, freteProdutoE, numero);
    }

    // "(sem NF)" sem marca = normal; a marca [PRODUTOE] vale quando existe.
    private static bool ProdutoE(string marca) => marca.Equals("PRODUTOE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Frete por caixa: "(1278 cx x R$ 9,00)" -> 9,00.</summary>
    private static decimal? Valor(Regex rx, string texto)
    {
        var m = rx.Match(texto);
        return m.Success && decimal.TryParse(m.Groups[1].Value, NumberStyles.Number, PtBr, out var v) ? v : null;
    }

    private static double? Numero(Regex rx, string texto)
    {
        var m = rx.Match(texto);
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Number, PtBr, out var n) ? n : null;
    }

    /// <summary>"1247", "12,5" — caixas como o relatório escreve.</summary>
    public static string Caixas(double n) =>
        n == Math.Floor(n) ? ((long)n).ToString(CultureInfo.InvariantCulture) : n.ToString(PtBr);
}
