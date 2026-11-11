using System.Globalization;
using System.Text;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Conta do frete da Girassol com a queda do volume (BOL-17), sem banco e sem data de hoje: linhas por tipo de volume
/// tiradas do .txt e dos tipos das notas (caixas e frete nunca mudam), queda por caixa por tipo normal, totais, cadeia do saldo entre
/// as cargas e o texto do desconto (tabelinha do e-mail e bloco "QUEDA DO VOLUME" no fim do .txt).
/// </summary>
public static class FretePerfilCalculo
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public const string TipoProdutosE = "ProdutosE";
    public const string TipoSemTipo = "Sem tipo identificado";
    public const string TipoNaoConferido = "Volumes (tipos não conferidos)";

    /// <summary>Um tipo de volume de uma nota (caixas daquele tipo na nota).</summary>
    public record TipoNota(string Tipo, double Caixas);

    /// <summary>Uma linha da conta: caixas de um tipo, frete e desconto por caixa.</summary>
    public record Linha(string Tipo, bool ProdutoE, double Caixas, decimal FretePorCaixa,
        decimal DescontoPorCaixa = 0m)
    {
        public decimal Frete => Arred((decimal)Caixas * FretePorCaixa);
        public decimal Desconto => Arred((decimal)Caixas * DescontoPorCaixa);
    }

    public record Calculo(List<Linha> Linhas, List<string> Avisos);

    public record TotaisCarga(double Caixas, decimal FreteBruto, double CaixasComDesconto, decimal DescontoTotal, decimal Devido);

    /// <summary>Carga na cadeia do saldo. <paramref name="ValorEmitido"/> = valor do boleto já emitido (nulo = sem boleto).</summary>
    public record CargaCadeia(DateOnly Data, string Placa, decimal Devido, decimal DescontoTotal, decimal? ValorEmitido);

    /// <summary>Resultado da cadeia por carga. <c>ValorBoleto</c> nulo quando não precisa de boleto.</summary>
    public record PosicaoCadeia(DateOnly Data, string Placa, bool TemBoleto, decimal SaldoAnterior, decimal SaldoAbatido,
        decimal? ValorBoleto, bool SemBoleto, decimal SaldoALevar, List<string> Avisos);

    // ── Linhas por tipo ─────────────────────────────────────────────────────

    /// <summary>
    /// Linhas por tipo a partir do .txt e dos tipos de cada nota (por número da NF). Notas normais somam por tipo;
    /// notas produtoE e a linha "(sem NF)" viram "ProdutosE". Diferença com o .txt: linha de sobra + aviso.
    /// </summary>
    public static Calculo MontarLinhas(RelatorioFreteLeitor.Relatorio rel,
        IReadOnlyDictionary<string, IReadOnlyList<TipoNota>>? tiposPorNota)
    {
        var avisos = new List<string>();
        var notas = rel.Linhas ?? new();

        var freteNormal = rel.FreteCaixaNormal ?? 0m;
        var freteProdutoE = rel.FreteCaixaProdutoE ?? 0m;
        if (rel.FreteCaixaNormal == null) avisos.Add("O .txt não traz o frete por caixa das normais; ficou R$ 0,00.");
        if (rel.FreteCaixaProdutoE == null) avisos.Add("O .txt não traz o frete por caixa da produtoE; ficou R$ 0,00.");

        var somaNotasNormais = notas.Where(n => !n.ProdutoE && n.NF != null).Sum(n => n.Caixas);
        var somaNotasProdutoE = notas.Where(n => n.ProdutoE || n.NF == null).Sum(n => n.Caixas);
        var totalNormais = rel.CaixasNormais ?? somaNotasNormais;
        var totalProdutoE = rel.CaixasProdutoE ?? somaNotasProdutoE;
        if (rel.CaixasNormais == null) avisos.Add($"O .txt não traz o total de caixas normais; valeu a soma das notas ({Cx(totalNormais)}).");
        if (rel.CaixasProdutoE == null) avisos.Add($"O .txt não traz o total de caixas de produtoE; valeu a soma das notas ({Cx(totalProdutoE)}).");
        if (Math.Abs(somaNotasProdutoE - totalProdutoE) > 0.001)
            avisos.Add($"As notas de produtoE somam {Cx(somaNotasProdutoE)} caixas, mas o .txt diz {Cx(totalProdutoE)}; valeu o .txt.");

        // Tipos das notas normais, na ordem em que aparecem.
        var porTipo = new List<(string Tipo, double Caixas)>();
        var semTipo = new List<string>();
        foreach (var n in notas.Where(n => !n.ProdutoE && n.NF != null))
        {
            if (tiposPorNota == null || !tiposPorNota.TryGetValue(n.NF!, out var tipos) || tipos.Count == 0)
            {
                semTipo.Add(n.NF!);
                continue;
            }
            foreach (var t in tipos)
            {
                var i = porTipo.FindIndex(p => p.Tipo.Equals(t.Tipo, StringComparison.OrdinalIgnoreCase));
                if (i < 0) porTipo.Add((t.Tipo, t.Caixas));
                else porTipo[i] = (porTipo[i].Tipo, porTipo[i].Caixas + t.Caixas);
            }
        }
        var somaTipos = porTipo.Sum(p => p.Caixas);

        var linhas = new List<Linha>();
        if (totalNormais > 0)
        {
            if (porTipo.Count == 0 || somaTipos > totalNormais + 0.001)
            {
                linhas.Add(new Linha(TipoNaoConferido, false, totalNormais, freteNormal));
                avisos.Add(porTipo.Count == 0
                    ? "Não achei os tipos de volume das notas (XML não guardado?); as caixas normais ficaram numa linha só."
                    : $"Os tipos das notas somam {Cx(somaTipos)} caixas, mais que as {Cx(totalNormais)} normais do .txt; as caixas normais ficaram numa linha só.");
            }
            else
            {
                linhas.AddRange(porTipo.Select(p => new Linha(p.Tipo, false, p.Caixas, freteNormal)));
                var dif = totalNormais - somaTipos;
                if (dif > 0.001)
                {
                    linhas.Add(new Linha(TipoSemTipo, false, dif, freteNormal));
                    avisos.Add($"{Cx(dif)} caixas normais sem tipo identificado" +
                        (semTipo.Count > 0 ? $" (nota {string.Join(", ", semTipo)} sem XML guardado)." : "."));
                }
            }
        }
        if (totalProdutoE > 0) linhas.Add(new Linha(TipoProdutosE, true, totalProdutoE, freteProdutoE));

        var calc = new Calculo(linhas, avisos);
        var tot = Totais(calc);
        if (Math.Abs(tot.Caixas - (totalNormais + totalProdutoE)) > 0.001)
            avisos.Add($"O cálculo soma {Cx(tot.Caixas)} caixas, mas o .txt diz {Cx(totalNormais + totalProdutoE)}.");
        if (rel.TotalFrete is decimal tf && tot.FreteBruto != tf)
            avisos.Add($"O frete do cálculo ({Reais(tot.FreteBruto)}) não bate com o TOTAL FRETE do .txt ({Reais(tf)}).");
        return calc;
    }

    // ── Descontos (queda do volume) ──────────────────────────────────────────────────

    /// <summary>
    /// Queda por caixa por tipo (desconto do valor da caixa na fazenda, repassado no frete). Só tipos normais:
    /// produtoE e produtoD nunca levam desconto. Tipo que não está na carga ou que não leva queda: ignorado + aviso.
    /// </summary>
    public static Calculo AplicarDescontos(Calculo calc, IReadOnlyDictionary<string, decimal>? descontos)
    {
        if (descontos == null || descontos.Count == 0) return calc;
        var avisos = new List<string>(calc.Avisos);
        foreach (var tipo in descontos.Keys.Where(k => !calc.Linhas.Any(l => Mesmo(l.Tipo, k))))
            avisos.Add($"A queda tem o tipo \"{tipo}\", que não está nesta carga; ficou de fora.");
        var linhas = calc.Linhas.Select(l =>
        {
            var d = descontos.FirstOrDefault(k => Mesmo(k.Key, l.Tipo));
            if (d.Key == null) return l;
            if (SemQueda(l))
            {
                if (d.Value > 0) avisos.Add($"\"{l.Tipo}\" não leva queda (produtoE e produtoD nunca levam desconto); ficou de fora.");
                return l;
            }
            return l with { DescontoPorCaixa = Arred(d.Value) };
        }).ToList();
        return new Calculo(linhas, avisos);
    }

    /// <summary>A linha não leva queda: produtoE ou produtoD.</summary>
    public static bool SemQueda(Linha l) =>
        l.ProdutoE || l.Tipo.Contains("produtoE", StringComparison.OrdinalIgnoreCase)
            || l.Tipo.Contains("produtoD", StringComparison.OrdinalIgnoreCase);

    public static TotaisCarga Totais(Calculo calc)
    {
        var bruto = calc.Linhas.Sum(l => l.Frete);
        var desconto = calc.Linhas.Sum(l => l.Desconto);
        return new TotaisCarga(
            calc.Linhas.Sum(l => l.Caixas), bruto,
            calc.Linhas.Where(l => l.DescontoPorCaixa > 0).Sum(l => l.Caixas), desconto,
            bruto - desconto);
    }

    // ── Cadeia do saldo ─────────────────────────────────────────────────────

    /// <summary>
    /// Cadeia do saldo (crédito do cliente) pelas cargas em ordem de data e placa, a partir da primeira com desconto.
    /// Sem boleto: líquido = devido − saldo; líquido ≤ 0 = não precisa de boleto e o saldo vai adiante.
    /// Com boleto já emitido a carga é fechada: queda lançada depois só gera aviso (dar baixa e emitir de novo).
    /// </summary>
    public static List<PosicaoCadeia> Encadear(IEnumerable<CargaCadeia> cargas)
    {
        var res = new List<PosicaoCadeia>();
        var saldo = 0m;
        var ativo = false;
        foreach (var c in cargas.OrderBy(c => c.Data).ThenBy(c => c.Placa, StringComparer.OrdinalIgnoreCase))
        {
            if (c.DescontoTotal > 0) ativo = true;
            var avisos = new List<string>();
            var anterior = saldo;
            var devido = Arred(c.Devido);

            if (c.ValorEmitido is decimal v)
            {
                var abatido = 0m;
                if (v > devido)
                {
                    if (c.DescontoTotal > 0)
                        avisos.Add($"Boleto já emitido sem a queda ({Reais(v)}; com a queda daria {Reais(devido)}): dê baixa e emita de novo.");
                    else if (ativo)
                        avisos.Add($"Boleto maior que o cálculo: {Reais(v)} emitido, cálculo {Reais(devido)}.");
                }
                else if (v < devido)
                {
                    abatido = Math.Min(saldo, devido - v);
                    saldo -= abatido;
                    if (ativo && devido - v > abatido)
                        avisos.Add($"Boleto menor que o cálculo: {Reais(v)} emitido, cálculo {Reais(devido)}" +
                            (abatido > 0 ? $" menos saldo de {Reais(abatido)}." : "."));
                }
                res.Add(new PosicaoCadeia(c.Data, c.Placa, true, anterior, abatido, v, false, saldo, avisos));
                continue;
            }

            var liquido = devido - saldo;
            if (liquido > 0)
            {
                res.Add(new PosicaoCadeia(c.Data, c.Placa, false, anterior, saldo, liquido, false, 0m, avisos));
                saldo = 0m;
            }
            else
            {
                saldo = -liquido;
                avisos.Add(saldo > 0
                    ? $"Não precisa fazer o boleto: o desconto passou do frete. Saldo de {Reais(saldo)} para o próximo boleto."
                    : "Não precisa fazer o boleto: o desconto é igual ao frete.");
                res.Add(new PosicaoCadeia(c.Data, c.Placa, false, anterior, anterior, null, true, saldo, avisos));
            }
        }
        return res;
    }

    // ── Texto do desconto ───────────────────────────────────────────────────

    /// <summary>
    /// Tabelinha do desconto para o e-mail (cada linha com " | "): cabeçalho, um tipo por linha (só com desconto),
    /// "Total do desconto" e "Saldo anterior abatido" quando &gt; 0. Sem desconto e sem saldo: lista vazia.
    /// </summary>
    public static List<string> Tabela(Calculo calc, decimal saldoAbatido = 0m)
    {
        var com = calc.Linhas.Where(l => l.DescontoPorCaixa > 0).ToList();
        if (com.Count == 0 && saldoAbatido <= 0) return new();
        var t = new List<string> { "Tipo | Caixas | Desconto por caixa | Total" };
        t.AddRange(com.Select(l => $"{l.Tipo} | {Cx(l.Caixas)} | {Reais(l.DescontoPorCaixa)} | {Reais(l.Desconto)}"));
        if (com.Count > 0)
            t.Add($"Total do desconto | {Cx(com.Sum(l => l.Caixas))} |  | {Reais(com.Sum(l => l.Desconto))}");
        if (saldoAbatido > 0) t.Add($"Saldo anterior abatido |  |  | {Reais(saldoAbatido)}");
        return t;
    }

    /// <summary>
    /// Bloco "QUEDA DO VOLUME" que vai no fim do .txt: caixas × queda por caixa = total por tipo, total da queda,
    /// saldo anterior abatido (quando houver) e VALOR DO BOLETO (frete − queda − saldo), ou "sem boleto".
    /// </summary>
    public static string BlocoQueda(Calculo calc, decimal saldoAbatido, decimal? valorBoleto, decimal saldoALevar)
    {
        var sep = new string('=', 60);
        var sb = new StringBuilder();
        sb.Append(sep).Append('\n');
        sb.Append("  ").Append(RelatorioFreteLeitor.TituloQueda).Append('\n');
        sb.Append(sep).Append('\n').Append('\n');
        var com = calc.Linhas.Where(l => l.DescontoPorCaixa > 0).ToList();
        var largura = Math.Max(10, com.Select(l => l.Tipo.Length).DefaultIfEmpty(0).Max());
        foreach (var l in com)
            sb.Append($"  {l.Tipo.PadRight(largura)}  {Cx(l.Caixas),6} cx x {Reais(l.DescontoPorCaixa)} = {Reais(l.Desconto)}\n");
        sb.Append('\n').Append(new string('-', 60)).Append('\n');
        var tot = Totais(calc);
        sb.Append($"  Frete das notas      : {Reais(tot.FreteBruto)}\n");
        sb.Append($"  Total da queda       : {Reais(tot.DescontoTotal)}\n");
        if (saldoAbatido > 0) sb.Append($"  Saldo anterior abatido: {Reais(saldoAbatido)}\n");
        sb.Append('\n');
        sb.Append(valorBoleto is decimal v && v > 0
            ? $"  *** VALOR DO BOLETO: {Reais(v)} ***\n"
            : $"  *** VALOR DO BOLETO: sem boleto (a queda passou do frete; saldo de {Reais(saldoALevar)} para o próximo) ***\n");
        sb.Append(sep).Append('\n');
        return sb.ToString();
    }

    /// <summary>O .txt com o bloco da queda no fim (troca o bloco que já existir). Bloco vazio = só tira o bloco.</summary>
    public static string ComQueda(string textoTxt, string? bloco)
    {
        var base_ = RelatorioFreteLeitor.SemQueda(textoTxt);
        if (string.IsNullOrEmpty(bloco)) return base_;
        var nl = base_.Contains("\r\n") ? "\r\n" : "\n";
        if (!base_.EndsWith("\n")) base_ += nl;
        return base_ + nl + bloco.Replace("\r\n", "\n").Replace("\n", nl);
    }

    // ── Formatos ────────────────────────────────────────────────────────────

    public static string Reais(decimal v) => "R$ " + v.ToString("N2", PtBr);
    private static string Cx(double n) => RelatorioFreteLeitor.Caixas(n);
    private static decimal Arred(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static bool Mesmo(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
}
