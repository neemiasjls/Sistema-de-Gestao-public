using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Exceptions;
using static TransportadoraSystem.Application.Services.AbastecimentoFechamentoService;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Cobranças dos postos: separa fatura de boleto pelo conteúdo do PDF, lê o
/// boleto e confere fatura × soma dos abastecimentos × planilha de divisão ×
/// boleto. Não mexe no banco: o controller guarda, aqui só se lê e se confere.
/// </summary>
public class CobrancaAbastecimentoService
{
    // Senhas dos PDFs que vêm protegidos. O boleto do Posto 7 abre com os cinco
    // primeiros dígitos do CNPJ do pagador (a transportadora). As variações do
    // CNPJ inteiro ficam para o caso de outro posto usar outro corte.
    private const string CnpjTransportadora = "11222333000181";
    public static readonly string[] SenhasConhecidas = SenhasDoCnpj(CnpjTransportadora).ToArray();

    public static IEnumerable<string> SenhasDoCnpj(string cnpj)
    {
        var d = Regex.Replace(cnpj ?? "", @"\D", "");
        if (d.Length < 8) yield break;
        foreach (var n in new[] { 5, 4, 6, 8 }) yield return d[..n];
        yield return d;
    }

    // ───────────────────────── Postos ─────────────────────────

    // Os nomes da planilha "BOLETOS POSTOS", que é como o usuário chama cada um
    private static readonly (string Chave, string Nome)[] Postos =
    [
        ("ESTRADA", "ESTRADA"),
        ("RODOVIA", "RODOVIA"),
        ("VIAGEM", "BOA VIAGEM"),
        ("TREVO", "TREVO"),
        ("VILA NORTE", "7 VILA NORTE"),
        ("POSTO 7", "7 VILA NORTE"),
    ];

    private static string SemAcento(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", " ").Trim();
    }

    /// <summary>Posto conhecido citado no texto do documento, ou null.</summary>
    public static string? DetectarPosto(string texto)
    {
        var t = SemAcento(texto);
        foreach (var (chave, nome) in Postos)
            if (Regex.IsMatch(t, $@"\b{Regex.Escape(chave)}\b")) return nome;
        return null;
    }

    /// <summary>
    /// Nome do posto como na planilha dos boletos. Um nome desconhecido fica
    /// como veio (em maiúsculas, sem "LTDA"), para a ligação por posto funcionar.
    /// </summary>
    public static string NomePosto(string? nome)
    {
        var t = SemAcento(nome ?? "");
        if (t == "") return "";
        if (Regex.IsMatch(t, @"^ZT\b|\bZT$")) return "ZT";
        var conhecido = DetectarPosto(t);
        if (conhecido != null) return conhecido;
        t = Regex.Replace(t, @"\b(LTDA|ME|EPP|EIRELI|S/?A)\.?$", "").Trim();
        return t;
    }

    // Número de fatura/documento para comparar: só os dígitos antes de uma
    // barra ou traço ("83421/1" é a parcela 1 da fatura 83421), sem zeros à esquerda.
    public static string NumeroChave(string? numero)
    {
        var m = Regex.Match(numero ?? "", @"\d+");
        return m.Success ? m.Value.TrimStart('0') : "";
    }

    // ───────────────────────── Leitura ─────────────────────────

    public enum TipoDocumento { Fatura, Boleto }

    public record BoletoLido(
        string Posto, string NumeroDocumento, string NossoNumero, double? Valor,
        DateOnly? Vencimento, DateOnly? DataDocumento, bool Lido, List<string> Avisos);

    public record Documento(TipoDocumento Tipo, FaturaLida? Fatura, BoletoLido? Boleto);

    private static readonly Regex LinhaDigitavel = new(
        @"\b\d{5}\.\d{5}\s+\d{5}\.\d{6}\s+\d{5}\.\d{6}\s+\d\s+(\d{4})(\d{10})\b", RegexOptions.Compiled);
    private static readonly Regex Dinheiro = new(@"^\d{1,3}(\.\d{3})*,\d{2}$", RegexOptions.Compiled);
    private static readonly Regex DataBr = new(@"^\d{2}/\d{2}/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex NossoNumeroRx = new(@"^\d{1,3}/\d{6,}-[0-9A-Za-z]$", RegexOptions.Compiled);
    private static readonly Regex NumeroDocRx = new(@"^\d{3,12}([/-]\d{1,3})?$", RegexOptions.Compiled);

    /// <summary>
    /// Lê um PDF que pode ser fatura ou boleto — o usuário solta tudo junto.
    /// PDF que não abre (senha desconhecida) é tratado como boleto sem leitura:
    /// fica guardado e os dados são digitados na tela.
    /// </summary>
    public static Documento Ler(byte[] pdf, string nomeArquivo, IEnumerable<string>? senhasExtras = null)
    {
        var senhas = SenhasConhecidas.Concat(senhasExtras ?? []).Distinct().ToList();
        List<List<string>> linhas;
        try
        {
            using var doc = AbrirPdf(pdf, senhas);
            linhas = doc.GetPages().SelectMany(p => Linhas(p)).ToList();
        }
        catch (PdfDocumentEncryptedException)
        {
            return new Documento(TipoDocumento.Boleto, null, NaoLido(nomeArquivo,
                $"{nomeArquivo}: o PDF tem senha e não abriu. Ficou guardado como boleto: preencha os dados à mão."));
        }
        catch (Exception)
        {
            return new Documento(TipoDocumento.Boleto, null, NaoLido(nomeArquivo,
                $"{nomeArquivo}: não consegui ler o PDF. Ficou guardado como boleto: preencha os dados à mão."));
        }

        var texto = string.Join("\n", linhas.Select(l => string.Join(" ", l)));
        if (EhBoleto(texto))
            return new Documento(TipoDocumento.Boleto, null, LerBoleto(linhas, texto, nomeArquivo));

        return new Documento(TipoDocumento.Fatura, LerFatura(pdf), null);
    }

    private static BoletoLido NaoLido(string nomeArquivo, string aviso) =>
        new(DetectarPosto(nomeArquivo) ?? "", "", "", null, null, null, false, [aviso]);

    // Boleto tem a linha digitável, ou pelo menos "Nosso Número" com
    // Cedente/Beneficiário. A fatura do Posto 7 fala em "Boleto" no cabeçalho,
    // por isso a palavra sozinha não serve.
    private static bool EhBoleto(string texto) =>
        LinhaDigitavel.IsMatch(texto) ||
        (Regex.IsMatch(texto, @"Nosso\s+N[uú]mero", RegexOptions.IgnoreCase) &&
         Regex.IsMatch(texto, @"Cedente|Benefici[aá]rio", RegexOptions.IgnoreCase));

    // Os campos do boleto pelo formato, não pela posição: cada banco imprime
    // o boleto de um jeito (Bradesco do Estrada e do Posto 7 já diferem).
    private static BoletoLido LerBoleto(List<List<string>> linhas, string texto, string nomeArquivo)
    {
        var avisos = new List<string>();
        static DateOnly? D(string s) =>
            DateOnly.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        static double N(string s) => double.Parse(s.Replace(".", "").Replace(",", "."), CultureInfo.InvariantCulture);

        // Linha digitável: os 14 últimos dígitos são o fator de vencimento e o valor
        double? valorLinha = null;
        int? fator = null;
        var ml = LinhaDigitavel.Match(texto);
        if (ml.Success)
        {
            fator = int.Parse(ml.Groups[1].Value);
            var v = long.Parse(ml.Groups[2].Value) / 100.0;
            if (v > 0) valorLinha = v;
        }

        // Número do documento: "28/09/2026 187298 DM N 28/09/2026" — a data do
        // documento seguida do número, na linha debaixo do cabeçalho.
        var candidatos = linhas
            .Where(l => l.Count >= 3 && DataBr.IsMatch(l[0]) && NumeroDocRx.IsMatch(l[1]) && !Dinheiro.IsMatch(l[1]))
            .Select(l => (Numero: l[1], Data: l[0]))
            .GroupBy(x => x.Numero)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        var numero = candidatos?.Key ?? "";
        var dataDoc = candidatos != null ? D(candidatos.First().Data) : null;
        if (numero == "") avisos.Add($"{nomeArquivo}: não achei o número do documento no boleto.");

        var nosso = linhas.SelectMany(l => l).FirstOrDefault(t => NossoNumeroRx.IsMatch(t)) ?? "";

        // Valor: o da linha digitável; sem ela, o valor que mais se repete
        // (o do documento aparece em todas as vias; juros e multa uma vez só).
        var valores = linhas.SelectMany(l => l).Where(t => Dinheiro.IsMatch(t)).Select(N).ToList();
        double? valor = valorLinha;
        if (valor == null && valores.Count > 0)
            valor = valores.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        if (valor == null) avisos.Add($"{nomeArquivo}: não achei o valor do boleto.");
        else if (valorLinha != null && valores.Count > 0 && !valores.Any(v => Math.Abs(v - valorLinha.Value) < 0.005))
            avisos.Add($"{nomeArquivo}: o valor da linha digitável (R$ {valorLinha:N2}) não aparece impresso no boleto.");

        // Vencimento: a data do fator, se ela estiver impressa no boleto; senão a
        // data mais adiante do documento (tirando "pagar a partir de ...").
        var datas = linhas
            .Where(l => !string.Join(" ", l).Contains("partir", StringComparison.OrdinalIgnoreCase))
            .SelectMany(l => l).Where(t => DataBr.IsMatch(t)).Select(D).Where(d => d != null).Select(d => d!.Value)
            .Distinct().ToList();
        DateOnly? vencimento = null;
        if (fator is int f && f > 0)
            vencimento = DatasDoFator(f).Cast<DateOnly?>().FirstOrDefault(d => datas.Contains(d!.Value));
        if (vencimento == null && datas.Count > 0)
        {
            vencimento = datas.Max();
            if (dataDoc != null && vencimento == dataDoc) vencimento = null;
        }
        if (vencimento == null) avisos.Add($"{nomeArquivo}: não achei o vencimento do boleto.");

        var posto = DetectarPosto(texto) ?? Beneficiario(linhas);
        if (posto == "") avisos.Add($"{nomeArquivo}: não reconheci o posto do boleto.");

        return new BoletoLido(posto, numero, nosso, valor is double x ? Math.Round(x, 2) : null,
            vencimento, dataDoc, true, avisos);
    }

    // Fator de vencimento do boleto (Febraban): dias desde 07/10/1997; chegou a
    // 9999 em 21/02/2025 e recomeçou em 1000 no dia seguinte. O mesmo fator
    // serve às duas contagens, por isso as duas datas saem e vale a que estiver
    // impressa no boleto.
    public static IEnumerable<DateOnly> DatasDoFator(int fator)
    {
        if (fator >= 1000) yield return new DateOnly(2025, 2, 22).AddDays(fator - 1000);
        yield return new DateOnly(1997, 10, 7).AddDays(fator);
    }

    // Nome do cedente/beneficiário para posto que não está na lista: a linha
    // logo depois do rótulo, até o primeiro CNPJ, agência ou data.
    private static string Beneficiario(List<List<string>> linhas)
    {
        for (int i = 0; i < linhas.Count - 1; i++)
        {
            var rotulo = string.Join(" ", linhas[i]);
            if (!Regex.IsMatch(rotulo, @"^(Cedente|Benefici[aá]rio)\b", RegexOptions.IgnoreCase) ||
                rotulo.Contains("Final", StringComparison.OrdinalIgnoreCase)) continue;

            for (int j = i + 1; j < Math.Min(linhas.Count, i + 4); j++)
            {
                var l = linhas[j];
                if (l.Count == 0 || !Regex.IsMatch(l[0], @"^[A-ZÀ-Ú0-9]{2,}$")) continue;
                var nome = l.TakeWhile(t => !Regex.IsMatch(t, @"\d[./-]\d")).ToList();
                if (nome.Count > 0) return NomePosto(string.Join(" ", nome));
            }
        }
        return "";
    }

    // ───────────────────────── Conferência ─────────────────────────

    public record FaturaConf(int Id, string Posto, string Numero, DateOnly? Emissao, DateOnly? Vencimento,
        decimal? ValorFatura, decimal Soma, decimal SomaNaPlanilha, int Quantidade, DateOnly Chegada);

    public record BoletoConf(int Id, int? FaturaId, string Posto, string NumeroDocumento,
        decimal? Valor, DateOnly? Vencimento, DateOnly DataChegada, DateOnly? PagoEm);

    public const string Confere = "CONFERE";
    public const string Diverge = "DIVERGE";
    public const string FaltaBoleto = "FALTA_BOLETO";
    public const string FaltaFatura = "FALTA_FATURA";

    /// <summary>
    /// Uma cobrança: a fatura com os boletos ligados a ela, ou um boleto que
    /// ainda não achou a fatura. Posto, número, valor e vencimento são os do
    /// boleto quando há boleto (é o que se paga), senão os da fatura.
    /// </summary>
    public record Cobranca(string Status, List<string> Diferencas, FaturaConf? Fatura, List<BoletoConf> Boletos,
        string Posto, string Numero, decimal? Valor, DateOnly? Vencimento, DateOnly DataChegada, bool Paga);

    private const decimal Tolerancia = 0.05m;
    private static string R(decimal v) => v.ToString("N2", new CultureInfo("pt-BR"));

    public static List<Cobranca> Conferir(IEnumerable<FaturaConf> faturas, IEnumerable<BoletoConf> boletos)
    {
        var bols = boletos.ToList();
        var lista = new List<Cobranca>();

        foreach (var f in faturas)
        {
            var dela = bols.Where(b => b.FaturaId == f.Id).OrderBy(b => b.Id).ToList();
            var dif = new List<string>();
            bool diverge = false;

            if (f.ValorFatura is not decimal vf)
            {
                dif.Add("O valor total da fatura não foi lido do PDF.");
                diverge = true;
                vf = f.Soma;
            }
            else if (Math.Abs(f.Soma - vf) > Tolerancia)
            {
                dif.Add($"Os abastecimentos somam R$ {R(f.Soma)}, a fatura diz R$ {R(vf)} (diferença de R$ {R(Math.Abs(f.Soma - vf))}).");
                diverge = true;
            }

            if (Math.Abs(f.SomaNaPlanilha - f.Soma) > Tolerancia)
            {
                dif.Add($"A planilha de divisão ficou com R$ {R(f.SomaNaPlanilha)}: R$ {R(f.Soma - f.SomaNaPlanilha)} são de placas sem dono.");
                diverge = true;
            }

            if (dela.Count == 0)
                dif.Add("Falta o boleto.");
            else
            {
                if (dela.Any(b => b.Valor == null))
                {
                    dif.Add("Boleto sem valor: preencha o valor.");
                    diverge = true;
                }
                var totalBoletos = dela.Sum(b => b.Valor ?? 0);
                if (dela.All(b => b.Valor != null) && Math.Abs(totalBoletos - vf) > Tolerancia)
                {
                    dif.Add(dela.Count == 1
                        ? $"O boleto é de R$ {R(totalBoletos)} e a fatura de R$ {R(vf)} (diferença de R$ {R(Math.Abs(totalBoletos - vf))})."
                        : $"Os {dela.Count} boletos somam R$ {R(totalBoletos)} e a fatura é de R$ {R(vf)} (diferença de R$ {R(Math.Abs(totalBoletos - vf))}).");
                    diverge = true;
                }
                foreach (var b in dela.Where(b => b.Vencimento != null && f.Vencimento != null && b.Vencimento != f.Vencimento))
                {
                    dif.Add($"O boleto vence em {b.Vencimento:dd/MM/yyyy} e a fatura em {f.Vencimento:dd/MM/yyyy}.");
                    diverge = true;
                }
            }

            var status = diverge ? Diverge : dela.Count == 0 ? FaltaBoleto : Confere;
            decimal? valor = dela.Count > 0 && dela.All(b => b.Valor != null) ? dela.Sum(b => b.Valor!.Value) : f.ValorFatura ?? f.Soma;
            var venc = dela.Select(b => b.Vencimento).Where(v => v != null).Min() ?? f.Vencimento;
            var chegada = dela.Count > 0 ? dela.Min(b => b.DataChegada) : f.Chegada;
            bool paga = dela.Count > 0 && dela.All(b => b.PagoEm != null);

            lista.Add(new Cobranca(status, dif, f, dela, f.Posto, f.Numero, valor, venc, chegada, paga));
        }

        var idsFaturas = lista.Select(c => c.Fatura!.Id).ToHashSet();
        foreach (var b in bols.Where(b => b.FaturaId == null || !idsFaturas.Contains(b.FaturaId.Value)))
        {
            var dif = new List<string> { "Falta a fatura deste boleto." };
            if (b.Valor == null) dif.Add("Boleto sem valor: preencha o valor.");
            lista.Add(new Cobranca(FaltaFatura, dif, null, [b], b.Posto, b.NumeroDocumento, b.Valor,
                b.Vencimento, b.DataChegada, b.PagoEm != null));
        }

        return lista
            .OrderByDescending(c => c.DataChegada)
            .ThenBy(c => c.Posto)
            .ThenByDescending(c => c.Numero)
            .ToList();
    }

    public record Vencimento(string Posto, string Numero, decimal? Valor, DateOnly Data, string Status);

    public record Resumo(int Abertas, decimal TotalAberto, int SemBoleto, int SemFatura, int Divergentes,
        decimal TotalAVencer, int Vencidas, decimal TotalVencido, List<Vencimento> ProximosVencimentos);

    /// <summary>Números do painel. "Em aberto" é tudo o que ainda não foi marcado como pago.</summary>
    public static Resumo Resumir(List<Cobranca> cobrancas, DateOnly hoje)
    {
        var abertas = cobrancas.Where(c => !c.Paga).ToList();
        var aVencer = abertas.Where(c => c.Vencimento == null || c.Vencimento >= hoje).ToList();
        var vencidas = abertas.Where(c => c.Vencimento < hoje).ToList();
        var proximos = abertas
            .Where(c => c.Vencimento >= hoje)
            .OrderBy(c => c.Vencimento)
            .Take(5)
            .Select(c => new Vencimento(c.Posto, c.Numero, c.Valor, c.Vencimento!.Value, c.Status))
            .ToList();

        return new Resumo(
            abertas.Count, abertas.Sum(c => c.Valor ?? 0),
            cobrancas.Count(c => c.Status == FaltaBoleto),
            cobrancas.Count(c => c.Status == FaltaFatura),
            cobrancas.Count(c => c.Status == Diverge),
            aVencer.Sum(c => c.Valor ?? 0),
            vencidas.Count, vencidas.Sum(c => c.Valor ?? 0),
            proximos);
    }

    /// <summary>
    /// A fatura do boleto: mesmo número (= número da fatura) e mesmo posto. Se o
    /// posto do boleto não foi reconhecido, vale o número sozinho quando só uma
    /// fatura tem esse número.
    /// </summary>
    public static int? AcharFatura(string postoBoleto, string numeroDocumento, IEnumerable<FaturaConf> faturas)
    {
        var chave = NumeroChave(numeroDocumento);
        if (chave == "") return null;
        var mesmoNumero = faturas.Where(f => NumeroChave(f.Numero) == chave).ToList();
        var mesmoPosto = mesmoNumero.Where(f => f.Posto == postoBoleto).ToList();
        if (mesmoPosto.Count >= 1) return mesmoPosto.OrderByDescending(f => f.Id).First().Id;
        if (postoBoleto == "" && mesmoNumero.Count == 1) return mesmoNumero[0].Id;
        return null;
    }
}
