using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê os boletos que a transportadora emite para os clientes (Sicoob): o das
/// produtosD da CD e o do frete da Girassol. Tira do PDF valor, vencimento, número
/// do documento, nosso número, pagador, placa e a data da carga; do nome do
/// arquivo ("197-REF. 29-09-2025-XAC.pdf") tira a sequência e confere a data.
///
/// A data que vale é a do boleto: nos arquivos da CD o ano do nome costuma vir
/// errado ("2025" num boleto de 2026), e nos da Girassol o nome às vezes leva o
/// dia em que o boleto foi feito, não o da carga. Quando o nome diverge, avisa.
/// </summary>
public static class BoletoReceberLeitor
{
    public const string Cd = "CD";
    public const string Perfil = "GIRASSOL";
    public static readonly string[] Clientes = [Cd, Perfil];

    public static string RotuloCliente(string cliente) => cliente switch
    {
        Cd => "CD (Cedro Frutas)",
        Perfil => "Girassol (Horacio)",
        _ => cliente,
    };

    public record Lido(
        string? Cliente, int? Sequencia, string Numero, string NossoNumero, string Pagador, string Placa,
        decimal? Valor, DateOnly? Vencimento, DateOnly? Emissao, DateOnly? DataCarga,
        bool LidoDoPdf, List<string> Avisos);

    private static readonly CultureInfo PtBr = new("pt-BR");
    private static readonly Regex LinhaDigitavel = new(
        @"\b\d{5}\.\d{5}\s+\d{5}\.\d{6}\s+\d{5}\.\d{6}\s+\d\s+(\d{4})(\d{10})\b", RegexOptions.Compiled);
    private static readonly Regex DataBr = new(@"^\d{2}/\d{2}/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex DinheiroBr = new(@"^\d{1,3}(\.\d{3})*,\d{2}$", RegexOptions.Compiled);
    private static readonly Regex NossoNumeroRx = new(@"^\d{1,12}-[0-9Xx]$", RegexOptions.Compiled);
    private static readonly Regex PlacaRx = new(@"\b([A-Z]{3})-?(\d[A-Z0-9]\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex Cnpj = new(@"\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}", RegexOptions.Compiled);

    // "197-REF. 29-09-2025-XAC.pdf", "509-GIRASSOL. 24-09-2026-XAC.pdf", "01 -REF.- 15-11-2022-XAB.pdf"
    private static readonly Regex NomeArquivo = new(
        @"^\s*(\d+)\s*-.*?(\d{2})-(\d{2})-(\d{4})(?:\s*-\s*([A-Za-z]{3}))?", RegexOptions.Compiled);

    public record Nome(int? Sequencia, int? Dia, int? Mes, int? Ano, string? Placa);

    public static Nome LerNome(string nomeArquivo)
    {
        var m = NomeArquivo.Match(Path.GetFileNameWithoutExtension(nomeArquivo ?? ""));
        if (!m.Success) return new Nome(null, null, null, null, null);
        return new Nome(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
            int.Parse(m.Groups[4].Value), m.Groups[5].Success ? m.Groups[5].Value.ToUpperInvariant() : null);
    }

    /// <summary>
    /// Cliente pelo pagador impresso no boleto. A CD é a C.D. Distribuidora de
    /// Hortifruti; a Girassol é a G.F. Fontes Filho (Horacio).
    /// </summary>
    public static string? ClienteDoPagador(string pagador)
    {
        var t = SemAcento(pagador).ToUpperInvariant();
        if (t.Contains("DISTRIB") && t.Contains("FRUTAS")) return Cd;
        if (t.Contains("FONTES") || t.Contains("GIRASSOL")) return Perfil;
        return null;
    }

    // A transportadora no boleto: CAIO JOSE MOREIRA E CIA LTDA, CNPJ XX.XXX.XXX/0001-XX (EMP-01)
    private const string CnpjTransportadora = "11.222.333";

    /// <summary>
    /// O boleto foi emitido pela transportadora (é dela como beneficiária)? É o que
    /// separa o boleto da CD e o da Girassol dos boletos da Fazenda Alfa, que podem
    /// ter o mesmo pagador (a C.D. Distribuidora paga os dois). Boleto da fazenda
    /// traz "ALFA" como beneficiário e nunca é contado aqui.
    /// </summary>
    public static bool EmitidoPelaTransportadora(byte[] pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            // Palavras separadas por espaço (o Text da página cola umas nas outras)
            var texto = string.Join("\n", doc.GetPages().SelectMany(Linhas).Select(l => string.Join(" ", l)));
            var t = SemAcento(texto).ToUpperInvariant();
            if (t.Contains("ALFA")) return false;
            // Só boleto: outro documento da empresa (contrato, alvará) também traz o CNPJ
            var ehBoleto = Regex.IsMatch(t, @"NOSSO\s+NUMERO") || LinhaDigitavel.IsMatch(texto);
            return ehBoleto && (t.Contains(CnpjTransportadora) || t.Contains("MOREIRA"));
        }
        catch (Exception) { return false; }
    }

    /// <param name="placasConhecidas">Placas da frota: quando o boleto não traz a
    /// placa, as três letras do nome do arquivo ("...-XAC.pdf") apontam uma delas.</param>
    public static Lido Ler(byte[] pdf, string nomeArquivo, IEnumerable<string>? placasConhecidas = null)
    {
        var avisos = new List<string>();
        var nome = LerNome(nomeArquivo);
        List<List<string>> linhas;
        try
        {
            using var doc = PdfDocument.Open(pdf);
            linhas = doc.GetPages().SelectMany(Linhas).ToList();
        }
        catch (Exception)
        {
            avisos.Add($"{nomeArquivo}: não consegui ler o PDF. Ficou guardado: preencha os dados à mão.");
            return new Lido(null, nome.Sequencia, "", "", "", "", null, null, null, DataDoNome(nome), false, avisos);
        }

        var textos = linhas.Select(l => string.Join(" ", l)).ToList();
        var texto = string.Join("\n", textos);
        if (textos.Count == 0)
        {
            avisos.Add($"{nomeArquivo}: o PDF não tem texto (pode ser imagem). Preencha os dados à mão.");
            return new Lido(null, nome.Sequencia, "", "", "", "", null, null, null, DataDoNome(nome), false, avisos);
        }

        // Linha digitável: fator de vencimento e valor
        decimal? valorLinha = null;
        int? fator = null;
        var ml = LinhaDigitavel.Match(texto);
        if (ml.Success)
        {
            fator = int.Parse(ml.Groups[1].Value);
            var v = long.Parse(ml.Groups[2].Value) / 100m;
            if (v > 0) valorLinha = v;
        }

        // "29/09/2026 29092026 DM N 29/09/2026 668-8": data do documento, número,
        // espécie, aceite, data de processamento e nosso número, debaixo dos rótulos.
        string numero = "", nosso = "";
        DateOnly? dataDoc = null;
        foreach (var l in linhas)
        {
            // O número às vezes vem com barras ("29/09/2026 29/09/2026 DM N …", boleto 510):
            // aceita a data antes da espécie e guarda só os dígitos, igual ao "29092026"
            var numeroData = l.Count >= 3 && DataBr.IsMatch(l[1]) && l[2] == "DM";
            if (l.Count >= 3 && DataBr.IsMatch(l[0]) && (numeroData || Regex.IsMatch(l[1], @"^\d{3,12}$")) && !DinheiroBr.IsMatch(l[1]))
            {
                numero = numeroData ? l[1].Replace("/", "") : l[1];
                dataDoc = D(l[0]);
                if (NossoNumeroRx.IsMatch(l[^1])) nosso = l[^1];
                break;
            }
        }
        if (nosso == "")
            nosso = Depois(linhas, @"^Nosso\s+N[uú]mero$", t => NossoNumeroRx.IsMatch(t)) ?? "";
        if (nosso == "")
            nosso = linhas.SelectMany(l => l).FirstOrDefault(t => NossoNumeroRx.IsMatch(t)) ?? "";

        // Valor: o da linha digitável, conferido com o impresso
        var valores = linhas.SelectMany(l => l).Where(t => DinheiroBr.IsMatch(t))
            .Select(t => decimal.Parse(t, NumberStyles.Number, PtBr)).Where(v => v > 0).ToList();
        decimal? valor = valorLinha;
        if (valor == null && valores.Count > 0)
            valor = valores.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        if (valor == null) avisos.Add($"{nomeArquivo}: não achei o valor do boleto.");
        else if (valorLinha != null && valores.Count > 0 && !valores.Contains(valorLinha.Value))
            avisos.Add($"{nomeArquivo}: o valor da linha digitável ({valorLinha.Value.ToString("C", PtBr)}) não aparece impresso no boleto.");

        // Vencimento: a data do fator que estiver impressa; senão a que vem
        // debaixo do rótulo "Vencimento"
        var datas = linhas
            .Where(l => !string.Join(" ", l).Contains("partir", StringComparison.OrdinalIgnoreCase))
            .SelectMany(l => l).Where(t => DataBr.IsMatch(t)).Select(D).Where(d => d != null).Select(d => d!.Value)
            .Distinct().ToList();
        DateOnly? vencimento = null;
        if (fator is int f && f > 0)
            vencimento = CobrancaAbastecimentoService.DatasDoFator(f).Cast<DateOnly?>().FirstOrDefault(d => datas.Contains(d!.Value));
        vencimento ??= D(Depois(linhas, @"Vencimento$", t => DataBr.IsMatch(t)) ?? "");
        if (vencimento == null) avisos.Add($"{nomeArquivo}: não achei o vencimento do boleto.");

        var emissao = D(Depois(linhas, @"Data\s+de\s+Emiss[aã]o", t => DataBr.IsMatch(t)) ?? "") ?? dataDoc;

        // Pagador: a linha debaixo de "Nome do pagador", sem o número do documento
        var pagador = "";
        for (int i = 0; i < linhas.Count - 1 && pagador == ""; i++)
        {
            if (!Regex.IsMatch(textos[i], @"^Nome\s+do\s+pagador", RegexOptions.IgnoreCase)) continue;
            var l = linhas[i + 1].ToList();
            if (l.Count > 1 && (Regex.IsMatch(l[^1], @"^\d{3,12}$") || DataBr.IsMatch(l[^1]))) l.RemoveAt(l.Count - 1);
            pagador = string.Join(" ", l).Trim();
        }
        if (pagador == "")
        {
            // Na ficha de compensação: "Pagador" e, abaixo, o nome com o CNPJ
            for (int i = 0; i < linhas.Count - 1 && pagador == ""; i++)
                if (Regex.IsMatch(textos[i], @"^Pagador\b", RegexOptions.IgnoreCase) && Cnpj.IsMatch(textos[i + 1]))
                    pagador = Cnpj.Replace(textos[i + 1], "").Trim();
        }
        if (pagador == "") avisos.Add($"{nomeArquivo}: não achei o pagador no boleto.");

        // Placa: "Caminhão (XAC-6D57)" nas instruções
        var placa = "";
        var linhaCaminhao = textos.FirstOrDefault(t => Regex.IsMatch(SemAcento(t), @"Caminhao", RegexOptions.IgnoreCase));
        var mp = PlacaRx.Match(linhaCaminhao ?? "");
        if (!mp.Success)
            mp = textos.Select(t => PlacaRx.Match(t)).FirstOrDefault(m => m.Success && !Cnpj.IsMatch(m.Value)) ?? Match.Empty;
        if (mp.Success) placa = $"{mp.Groups[1].Value}-{mp.Groups[2].Value}";
        else
        {
            var candidatas = nome.Placa == null ? [] : (placasConhecidas ?? [])
                .Select(p => p.Trim().ToUpperInvariant()).Distinct()
                .Where(p => p.StartsWith(nome.Placa, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidatas.Count == 1)
            {
                placa = candidatas[0];
                avisos.Add($"{nomeArquivo}: o boleto não traz a placa; pelo nome do arquivo é o caminhão {placa}.");
            }
            else avisos.Add($"{nomeArquivo}: o boleto não traz a placa do caminhão; preencha à mão.");
        }

        // Data da carga: no Sicoob o número do documento é a própria data (29092026)
        DateOnly? dataCarga = null;
        if (numero.Length == 8 && DateOnly.TryParseExact(numero, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dn)
            && dn.Year is >= 2000 and <= 2100)
            dataCarga = dn;
        dataCarga ??= dataDoc ?? emissao;
        if (dataCarga == null)
        {
            dataCarga = DataDoNome(nome);
            if (dataCarga != null) avisos.Add($"{nomeArquivo}: a data da carga saiu do nome do arquivo; confira.");
            else avisos.Add($"{nomeArquivo}: não achei a data da carga.");
        }
        else ConferirNome(nomeArquivo, nome, dataCarga.Value, placa, avisos);

        var cliente = ClienteDoPagador(pagador);
        if (numero == "") avisos.Add($"{nomeArquivo}: não achei o número do documento no boleto.");
        if (nosso == "") avisos.Add($"{nomeArquivo}: não achei o nosso número no boleto.");

        return new Lido(cliente, nome.Sequencia, numero, nosso, pagador, placa, valor is decimal x ? Math.Round(x, 2) : null,
            vencimento, emissao, dataCarga, true, avisos);
    }

    // O nome do arquivo contra o boleto: ano, dia e placa
    private static void ConferirNome(string nomeArquivo, Nome nome, DateOnly data, string placa, List<string> avisos)
    {
        if (nome.Dia is int d && nome.Mes is int m && nome.Ano is int a)
        {
            if (d == data.Day && m == data.Month && a != data.Year)
                avisos.Add($"{nomeArquivo}: o nome do arquivo diz {a}, mas o boleto é de {data.Year}. Valeu a data do boleto, {data:dd/MM/yyyy}.");
            else if (d != data.Day || m != data.Month)
                avisos.Add($"{nomeArquivo}: o nome do arquivo diz {d:00}/{m:00}/{a}, mas o boleto é da carga de {data:dd/MM/yyyy}. Valeu a data do boleto.");
        }
        if (nome.Placa != null && placa != "" && !placa.StartsWith(nome.Placa, StringComparison.OrdinalIgnoreCase))
            avisos.Add($"{nomeArquivo}: o nome do arquivo diz {nome.Placa}, mas o boleto diz caminhão {placa}.");
    }

    private static DateOnly? DataDoNome(Nome n)
    {
        if (n.Dia is not int d || n.Mes is not int m || n.Ano is not int a) return null;
        try { return new DateOnly(a, m, d); } catch (ArgumentOutOfRangeException) { return null; }
    }

    // O primeiro pedaço que casa com "aceita" nas três linhas depois do rótulo
    private static string? Depois(List<List<string>> linhas, string rotulo, Func<string, bool> aceita)
    {
        for (int i = 0; i < linhas.Count; i++)
        {
            if (!Regex.IsMatch(string.Join(" ", linhas[i]), rotulo, RegexOptions.IgnoreCase)) continue;
            for (int j = i + 1; j < Math.Min(linhas.Count, i + 4); j++)
            {
                var achou = linhas[j].FirstOrDefault(aceita);
                if (achou != null) return achou;
            }
        }
        return null;
    }

    private static DateOnly? D(string s) =>
        DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    // Palavras agrupadas por linha (mesma altura), da esquerda para a direita
    private static IEnumerable<List<string>> Linhas(Page page)
    {
        const double tol = 0.1;
        var linha = new List<Word>();
        double y = 0;
        foreach (var w in page.GetWords().OrderByDescending(w => w.BoundingBox.Bottom))
        {
            if (linha.Count > 0 && Math.Abs(w.BoundingBox.Bottom - y) > tol)
            {
                yield return linha.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text).ToList();
                linha = [];
            }
            if (linha.Count == 0) y = w.BoundingBox.Bottom;
            linha.Add(w);
        }
        if (linha.Count > 0) yield return linha.OrderBy(x => x.BoundingBox.Left).Select(x => x.Text).ToList();
    }

    private static string SemAcento(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
