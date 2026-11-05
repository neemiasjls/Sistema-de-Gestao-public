using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TransportadoraSystem.Core.Entities;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// As contas da aba VIAGEMn do acerto mensal, iguais às fórmulas da planilha
/// (conferidas em 98 abas de jan a set/2026 dos três caminhões, sem diferença):
///   volumes por cliente (G) = caixas × valor + produtoE × valor da produtoE (antigas: valor / 2)
///       + produtoD × valor da produtoD + produtosB × frete da produtoB + produtosC × frete da produtoC
///   TOTAL VOLUMES (G58) = soma; TOTAL RETORNOS (B57) = soma do frete total dos retornos
///   VALOR TOTAL (G59) = volumes + retornos
///   TOTAL-DIESEL (I20) = diesel na nota + diesel pago
///   DESPESAS (I58) = diárias + todas as outras despesas (descarga inclusive)
///   TOTAL DESPESAS (I59) = diesel + despesas
///   DINHEIRO VIAGEM (B58) = sobra anterior + levou
///   SOBRA VIAGEM (B60) = dinheiro − devolveu − total despesas + diesel na nota
///       (o diesel da nota a empresa pagou: não saiu do dinheiro do motorista)
///   SOBRA (I60, resultado da viagem) = valor total − total despesas
/// Comissão (confirmado, Admin, 06/10/2026): % do motorista sobre o frete
/// (volumes + retornos) menos só a CAIXINHA e a DESCARGA. A planilha antiga (G60)
/// tirava também carregamento, gasolina e ICMS dos retornos: esse valor sai em
/// ComissaoPlanilha só para conferência (divergência em pendencias.md).
/// </summary>
public static class ViagemCaminhaoCalculo
{
    // Tipos de despesa da folha do motorista e da aba (H21..H57)
    public const string Carregamento = "CARREGAMENTO";
    public const string Gasolina = "GASOLINA";
    public const string CredCelular = "CRED_CELULAR";
    public const string Caixinha = "CAIXINHA";
    public const string Borracharia = "BORRACHARIA";
    public const string AutoEletrico = "AUTO_ELETRICO";
    public const string Lavagem = "LAVAGEM";
    public const string Lubrificacao = "LUBRIFICACAO";
    public const string Mecanico = "MECANICO";
    public const string Descarga = "DESCARGA";
    public const string Outra = "OUTRA";

    public static readonly (string Tipo, string Nome)[] TiposDespesa =
    [
        (Carregamento, "Carregamento"), (Gasolina, "Gasolina"), (CredCelular, "Crédito de celular"),
        (Caixinha, "Caixinha"), (Borracharia, "Borracharia"), (AutoEletrico, "Auto elétrico"),
        (Lavagem, "Lavagem"), (Lubrificacao, "Lubrificação"), (Mecanico, "Mecânico"),
        (Descarga, "Descarga"), (Outra, "Outra despesa"),
    ];

    public static string NomeTipo(string tipo) =>
        TiposDespesa.FirstOrDefault(t => t.Tipo == tipo).Nome ?? tipo;

    public static bool TipoValido(string? tipo) => TiposDespesa.Any(t => t.Tipo == tipo);

    /// <summary>
    /// Frete do cliente na viagem (VIA-06): caixas × caixa + produtoE × produtoE +
    /// produtoD × produtoD + produtosB (amarelas e brancas) × frete da produtoB +
    /// produtosC × frete da produtoC, com os valores gravados no dia. ProdutoE sem
    /// valor gravado (viagens antigas) = metade da caixa, como na planilha.
    /// </summary>
    public static decimal TotalVolumes(ViagemCaminhaoVolumes o) =>
        o.CaixasVolumes * o.ValorCaixa + o.CaixasProdutoE * ValorProdutoE(o)
        + o.CaixasProdutoD * o.ValorProdutoD + o.ProdutosB * o.ValorProdutoB + o.ProdutosC * o.ValorProdutoC;

    public static decimal ValorProdutoE(ViagemCaminhaoVolumes o) => o.ValorProdutoE ?? o.ValorCaixa / 2m;

    public static decimal DieselNota(ViagemCaminhao v) => v.Abastecimentos.Where(a => a.Forma != "PAGO").Sum(a => a.Valor);
    public static decimal DieselPago(ViagemCaminhao v) => v.Abastecimentos.Where(a => a.Forma == "PAGO").Sum(a => a.Valor);
    public static decimal ValorDiarias(ViagemCaminhao v) => Math.Round(v.DiariasQuantidade * v.ValorDiaria, 2);

    /// <summary>Tudo que conta como despesa da viagem (I59): diesel, diárias e as outras.</summary>
    public static decimal TotalDespesas(ViagemCaminhao v) =>
        DieselNota(v) + DieselPago(v) + ValorDiarias(v) + v.Despesas.Sum(d => d.Valor);

    /// <summary>B60 a partir da sobra anterior: só precisa das despesas, não dos fretes.</summary>
    public static decimal Sobra(ViagemCaminhao v, decimal sobraAnterior) =>
        sobraAnterior + v.Levou - v.Devolveu - TotalDespesas(v) + DieselNota(v);

    public record Resultado(
        decimal TotalVolumes, decimal TotalRetornos, decimal ValorTotal,
        decimal DieselNota, decimal DieselPago, decimal TotalDiesel,
        decimal Diarias, decimal OutrasDespesas, decimal Despesas, decimal TotalDespesas,
        decimal SobraAnterior, decimal DinheiroViagem, decimal Sobra, decimal Resultado_,
        decimal Caixinha, decimal Descarga, decimal BaseComissao, decimal Comissao,
        decimal IcmsRetornos, decimal ComissaoPlanilha,
        decimal CaixasVolumes, decimal CaixasProdutoE, decimal CaixasProdutoD, decimal ProdutosB,
        int? KmRodados, decimal? Media,
        decimal ProdutosBAmarelas = 0, decimal ProdutosBBrancas = 0, decimal ProdutosC = 0);

    public static Resultado Calcular(ViagemCaminhao v, IEnumerable<RetornoFrete> retornos, decimal sobraAnterior)
    {
        var rets = retornos.ToList();
        var totalVolumes = v.Volumes.Sum(TotalVolumes);
        var totalRet = rets.Sum(r => r.FreteTotal);
        var valorTotal = totalVolumes + totalRet;
        var nota = DieselNota(v);
        var pago = DieselPago(v);
        var diarias = ValorDiarias(v);
        var outras = v.Despesas.Sum(d => d.Valor);
        var despesas = diarias + outras;
        var totalDesp = nota + pago + despesas;
        var dinheiro = sobraAnterior + v.Levou;
        var sobra = dinheiro - v.Devolveu - totalDesp + nota;
        var caixinha = v.Despesas.Where(d => d.Tipo == Caixinha).Sum(d => d.Valor);
        var descarga = v.Despesas.Where(d => d.Tipo == Descarga).Sum(d => d.Valor);
        var baseCom = valorTotal - caixinha - descarga;
        var comissao = Math.Round(baseCom / 100m * v.PorcentagemComissao, 2);
        var icms = rets.Sum(r => r.FreteTotal / 100m * r.PorcentagemIcms);
        var carreg = v.Despesas.Where(d => d.Tipo == Carregamento).Sum(d => d.Valor);
        var gas = v.Despesas.Where(d => d.Tipo == Gasolina).Sum(d => d.Valor);
        var comPlan = Math.Round((valorTotal - caixinha - descarga - carreg - gas - icms) / 100m * v.PorcentagemComissao, 2);
        int? km = v.KmSaida.HasValue && v.KmChegada.HasValue ? v.KmChegada - v.KmSaida : null;
        var litros = v.Litros ?? (v.Abastecimentos.Any(a => a.Litros > 0) ? v.Abastecimentos.Sum(a => a.Litros ?? 0) : null);
        decimal? media = km is > 0 && litros is > 0 ? Math.Round(km.Value / litros.Value, 2) : null;

        return new Resultado(totalVolumes, totalRet, valorTotal, nota, pago, nota + pago, diarias, outras, despesas, totalDesp,
            sobraAnterior, dinheiro, sobra, valorTotal - totalDesp, caixinha, descarga, baseCom, comissao, icms, comPlan,
            v.Volumes.Sum(o => o.CaixasVolumes), v.Volumes.Sum(o => o.CaixasProdutoE), v.Volumes.Sum(o => o.CaixasProdutoD),
            v.Volumes.Sum(o => o.ProdutosB), km, media,
            v.Volumes.Sum(o => o.ProdutosBAmarelas), v.Volumes.Sum(o => o.ProdutosBBrancas), v.Volumes.Sum(o => o.ProdutosC));
    }

    /// <summary>
    /// Ordem das viagens de um caminhão: mês do acerto, data de saída, id. É a ordem
    /// das abas VIAGEM1, VIAGEM2... e da cadeia da sobra (a VIAGEM1 de um mês pega a
    /// sobra da última do mês anterior, como o VLOOKUP para o arquivo do mês passado).
    /// </summary>
    public static IOrderedEnumerable<ViagemCaminhao> EmOrdem(IEnumerable<ViagemCaminhao> viagens) =>
        viagens.OrderBy(v => v.AnoReferencia).ThenBy(v => v.MesReferencia)
               .ThenBy(v => v.DataSaida ?? DateOnly.MaxValue).ThenBy(v => v.Id);

    /// <summary>Sobra anterior de cada viagem da cadeia do caminhão (precisa de abastecimentos e despesas).</summary>
    public static Dictionary<int, decimal> SobrasAnteriores(IEnumerable<ViagemCaminhao> viagensDoCaminhao)
    {
        var mapa = new Dictionary<int, decimal>();
        decimal anterior = 0;
        foreach (var v in EmOrdem(viagensDoCaminhao))
        {
            var sa = v.SobraAnteriorInformada ?? anterior;
            mapa[v.Id] = sa;
            anterior = Sobra(v, sa);
        }
        return mapa;
    }

    // ── Textos ────────────────────────────────────────────────────

    private static readonly Regex RxPlaca = new(@"([A-Z]{3})\s*-?\s*(\d[A-Z0-9]\d{2})", RegexOptions.Compiled);

    /// <summary>"xac6d57", "XAC-6D57 " → "XAC-6D57". Sem placa reconhecível, devolve o texto limpo.</summary>
    public static string NormalizarPlaca(string? placa)
    {
        var t = (placa ?? "").Trim().ToUpperInvariant();
        var m = RxPlaca.Match(t.Replace(" ", ""));
        return m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}" : t;
    }

    /// <summary>Todas as placas de um texto, na ordem ("(XAC-6D57)-(XAV-8C09)").</summary>
    public static List<string> Placas(string? texto) =>
        RxPlaca.Matches((texto ?? "").ToUpperInvariant().Replace(" ", "")).Select(m => $"{m.Groups[1].Value}-{m.Groups[2].Value}").ToList();

    /// <summary>Maiúsculas sem acento e sem pontuação, espaços simples (para casar nomes).</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    /// <summary>
    /// Acha o cliente da tabela de frete pelo nome do controle de carregamento:
    /// igual, apelido igual, ou um nome contido no outro (COM. OMEGA → COMERCIAL OMEGA
    /// pelo apelido; IRINEU → IRINEU ANAPOLIS). Nenhum ou mais de um: nulo.
    /// </summary>
    public static PrecoFreteCliente? AcharCliente(string nome, IEnumerable<PrecoFreteCliente> tabela)
    {
        var alvo = Normalizar(nome);
        if (alvo.Length == 0) return null;
        var lista = tabela.Where(p => p.Ativo).ToList();
        List<string> Nomes(PrecoFreteCliente p) =>
            new[] { p.Cliente }.Concat((p.Apelidos ?? "").Split(',')).Select(Normalizar).Where(x => x.Length > 0).ToList();

        var iguais = lista.Where(p => Nomes(p).Contains(alvo)).ToList();
        if (iguais.Count == 1) return iguais[0];
        var contidos = lista.Where(p => Nomes(p).Any(n => (" " + n + " ").Contains(" " + alvo + " ") || (" " + alvo + " ").Contains(" " + n + " "))).ToList();
        return contidos.Count == 1 ? contidos[0] : null;
    }

    public static string NomeMes(int mes) =>
        new[] { "JANEIRO", "FEVEREIRO", "MARÇO", "ABRIL", "MAIO", "JUNHO", "JULHO", "AGOSTO", "SETEMBRO", "OUTUBRO", "NOVEMBRO", "DEZEMBRO" }[Math.Clamp(mes, 1, 12) - 1];

    /// <summary>Mês pelo nome no nome do arquivo ("9-V -SETEMBRO-2026.xlsx", "09-R-SETEMBRO -26.xlsx").</summary>
    public static (int Mes, int Ano)? MesDoNome(string nomeArquivo)
    {
        var n = Normalizar(nomeArquivo);
        int mes = 0;
        for (var i = 1; i <= 12; i++)
            if (n.Contains(Normalizar(NomeMes(i)))) { mes = i; break; }
        if (mes == 0) return null;
        var anos = Regex.Matches(n, @"\b(20\d{2}|\d{2})\b").Select(m => int.Parse(m.Value)).ToList();
        var ano = anos.FirstOrDefault(a => a >= 2000);
        if (ano == 0)
        {
            var curto = anos.LastOrDefault(a => a is >= 20 and <= 99);
            ano = curto > 0 ? 2000 + curto : 0;
        }
        return ano == 0 ? null : (mes, ano);
    }
}
