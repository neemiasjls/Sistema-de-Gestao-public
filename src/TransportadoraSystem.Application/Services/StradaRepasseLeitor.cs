using System.Globalization;
using System.Text;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê o "Arquivo de repasse" (.txt) que a Strada gera para cada fatura paga pela TIP Bank.
/// Latin-1, campos separados por "|".
///
/// Linha "1" (cabeçalho): CNPJ da Strada, CNPJ da transportadora, data de geração e data de
/// pagamento (DDMMAAAA), total em centavos e número da fatura.
/// Linhas "2": contrato (campo 2), sequência (campo 3: 1 = adiantamento, 2 = saldo), data (campo 4)
/// e valor em centavos (campo 7).
///
/// O total da fatura é a SOMA DAS LINHAS, nunca o cabeçalho: nos reembolsos a Strada grava o
/// cabeçalho sem os centavos ("532" para R$ 532,00). Diferença vira aviso, não erro. Fatura só com
/// cabeçalho fica incompleta: os contratos têm de vir do "Exportar detalhamento".
/// </summary>
public static class StradaRepasseLeitor
{
    /// <summary>Taxa da TIP Bank sobre cada transferência.</summary>
    public const decimal TaxaTip = 0.01m;

    public record Linha(string Contrato, int Sequencia, DateOnly? Data, decimal Valor);

    public record Contrato(string Numero, decimal Valor, List<Linha> Linhas);

    public record Fatura(
        string Numero, string CnpjStrada, string CnpjTransportadora,
        DateOnly DataGeracao, DateOnly DataPagamento,
        decimal TotalCabecalho, decimal Total, decimal Taxa, decimal Repasse,
        bool Incompleta, List<Contrato> Contratos, List<Linha> Linhas, List<string> Avisos);

    /// <summary>Resultado da leitura: a fatura, ou o motivo de o arquivo ter sido recusado.</summary>
    public record Resultado(Fatura? Fatura, string? Erro)
    {
        public bool Ok => Fatura is not null;
    }

    public static decimal CalcularTaxa(decimal total) =>
        Math.Round(total * TaxaTip, 2, MidpointRounding.AwayFromZero);

    public static Resultado Ler(byte[] conteudo) => Ler(Encoding.Latin1.GetString(conteudo));

    public static Resultado Ler(string texto)
    {
        var linhas = texto.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        if (linhas.Count == 0)
            return Recusar("arquivo vazio.");

        var cab = linhas[0].Split('|');
        if (cab[0] != "1" || cab.Length < 7)
            return Recusar("a primeira linha não é o cabeçalho \"1|...\" do Arquivo de repasse da Strada.");

        string cnpjStrada = cab[1].Trim(), cnpjTrx = cab[2].Trim(), numero = cab[6].Trim();
        if (!SoDigitos(cnpjStrada, 14) || !SoDigitos(cnpjTrx, 14))
            return Recusar("o cabeçalho não traz os dois CNPJs (14 dígitos).");
        var geracao = Data(cab[3]);
        var pagamento = Data(cab[4]);
        if (geracao is null || pagamento is null)
            return Recusar("o cabeçalho não traz data de geração e de pagamento válidas (DDMMAAAA).");
        var totalCab = Centavos(cab[5]);
        if (totalCab is null)
            return Recusar("o cabeçalho não traz o total em centavos.");
        if (numero.Length == 0 || !numero.All(char.IsDigit))
            return Recusar("o cabeçalho não traz o número da fatura.");

        var avisos = new List<string>();
        var itens = new List<Linha>();
        for (var i = 1; i < linhas.Count; i++)
        {
            var c = linhas[i].Split('|');
            if (c[0] != "2")
            {
                avisos.Add($"Linha {i + 1} ignorada: não é linha de contrato (\"2|...\").");
                continue;
            }
            var contrato = c.Length > 1 ? c[1].Trim() : "";
            var valor = c.Length > 6 ? Centavos(c[6]) : null;
            if (contrato.Length == 0 || valor is null)
            {
                avisos.Add($"Linha {i + 1} ignorada: sem contrato ou sem valor.");
                continue;
            }
            var seq = c.Length > 2 && int.TryParse(c[2].Trim(), out var s) ? s : 0;
            if (seq is not (1 or 2))
                avisos.Add($"Contrato {contrato}: sequência \"{(c.Length > 2 ? c[2] : "")}\" desconhecida (esperado 1 = adiantamento ou 2 = saldo).");
            var data = c.Length > 3 ? Data(c[3]) : null;
            if (data is null)
                avisos.Add($"Contrato {contrato}: linha sem data.");
            itens.Add(new Linha(contrato, seq, data, valor.Value));
        }

        var contratos = itens
            .GroupBy(l => l.Contrato)
            .Select(g => new Contrato(g.Key, g.Sum(l => l.Valor), g.ToList()))
            .ToList();
        var total = itens.Sum(l => l.Valor);
        var incompleta = itens.Count == 0;

        if (incompleta)
            avisos.Add("Fatura só com o cabeçalho, sem os contratos. Suba o Exportar detalhamento desta fatura.");
        else if (totalCab.Value * 100 == total)
            avisos.Add($"Cabeçalho sem centavos ({cab[5].Trim()}): reembolso sem centavos. Vale a soma das linhas, {Reais(total)}.");
        else if (totalCab.Value != total)
            avisos.Add($"Total do cabeçalho ({Reais(totalCab.Value)}) diferente da soma das linhas ({Reais(total)}). Vale a soma das linhas.");

        var taxa = CalcularTaxa(total);
        return new Resultado(new Fatura(
            numero, cnpjStrada, cnpjTrx, geracao.Value, pagamento.Value,
            totalCab.Value, total, taxa, total - taxa,
            incompleta, contratos, itens, avisos), null);
    }

    /// <summary>Faturas que aparecem em mais de um arquivo (mesmo número): número → quantas vezes.</summary>
    public static Dictionary<string, int> Repetidas(IEnumerable<Fatura> faturas) =>
        faturas.GroupBy(f => f.Numero)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => g.Count());

    private static Resultado Recusar(string motivo) =>
        new(null, "Não é um Arquivo de repasse da Strada: " + motivo);

    private static bool SoDigitos(string s, int tamanho) => s.Length == tamanho && s.All(char.IsDigit);

    private static DateOnly? Data(string s) =>
        DateOnly.TryParseExact(s.Trim(), "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static decimal? Centavos(string s) =>
        long.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c / 100m : null;

    private static string Reais(decimal v) => v.ToString("N2", new CultureInfo("pt-BR"));
}
