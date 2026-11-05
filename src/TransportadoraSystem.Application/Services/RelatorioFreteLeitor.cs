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
        List<string> NFs, List<string> Avisos);

    private static readonly Regex DataRx = new(@"Data:\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
    private static readonly Regex NormaisRx = new(@"Total\s+caixas\s+normais\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
    private static readonly Regex ProdutoERx = new(@"Total\s+caixas\s+produtoE\s*:\s*([\d.,]+)", RegexOptions.IgnoreCase);
    private static readonly Regex TotalRx = new(@"TOTAL\s+FRETE:\s*R\$\s*([\d.]+,\d{2})", RegexOptions.IgnoreCase);
    private static readonly Regex NfRx = new(@"^\s*NF\s+(\d+)\s+-", RegexOptions.Multiline);

    /// <summary>É um relatório de frete? (o cabeçalho que o sistema escreve)</summary>
    public static bool EhRelatorio(string texto) =>
        texto.Contains("RELATORIO DE FRETE", StringComparison.OrdinalIgnoreCase) && TotalRx.IsMatch(texto);

    public static string Texto(byte[] conteudo) => Encoding.UTF8.GetString(conteudo).TrimStart('﻿');

    public static Relatorio Ler(byte[] conteudo, string nomeArquivo) => Ler(Texto(conteudo), nomeArquivo);

    public static Relatorio Ler(string texto, string nomeArquivo)
    {
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
        return new Relatorio(data, placa, normais, produtoE, total, nfs, avisos);
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
