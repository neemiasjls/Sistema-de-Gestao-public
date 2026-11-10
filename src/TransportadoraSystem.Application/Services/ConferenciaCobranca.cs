namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Confere os boletos a receber (CD e Girassol) contra o extrato do Sicoob, sem banco (BOL-10).
/// O Sicoob não credita boleto por boleto: cada dia com boleto pago tem UM "CRÉD.LIQUIDAÇÃO COBRANÇA"
/// com a soma de todos (sem pagador). Para cada crédito, em ordem de data, acha a combinação de boletos
/// (vencimento de 12 dias antes a 6 depois; ainda não usados) que soma o valor. Entre várias, fica a mais
/// perto da data do crédito (já baixado: pela data da baixa; em aberto: pelo vencimento). Empate com
/// boletos em aberto diferentes = dúvida, mostrada para escolher. Os em aberto da escolhida = "pago no
/// banco, falta dar baixa". Nunca liga sem o crédito no extrato.
/// </summary>
public static class ConferenciaCobranca
{
    public record Credito(string Id, DateOnly Data, decimal Valor);

    public record Boleto(int Id, string Cliente, string NossoNumero, decimal Valor, DateOnly? Vencimento, DateOnly? RecebidoEm);

    public record CreditoConferido(Credito Credito, IReadOnlyList<Boleto> Boletos, IReadOnlyList<IReadOnlyList<Boleto>> Alternativas)
    {
        public bool Achou => Boletos.Count > 0;
        public bool Duvida => Alternativas.Count > 0;
    }

    public record Resultado(IReadOnlyList<CreditoConferido> Creditos, IReadOnlyList<(Boleto Boleto, Credito Credito)> PagosSemBaixa,
        IReadOnlyList<Boleto> VencidosSemCredito);

    private const int DiasAntes = 12, DiasDepois = 6, MaxCandidatos = 22, MaxSolucoes = 40;

    /// <summary>Crédito de cobrança no extrato (o boleto pago cai assim).</summary>
    public static bool EhLiquidacaoCobranca(string descricao) =>
        descricao.Contains("LIQUIDA", StringComparison.OrdinalIgnoreCase) &&
        descricao.Contains("COBRAN", StringComparison.OrdinalIgnoreCase);

    public static Resultado Conferir(IEnumerable<Credito> creditos, IReadOnlyList<Boleto> boletos, DateOnly hoje)
    {
        var usados = new HashSet<int>();
        var conferidos = new List<CreditoConferido>();
        foreach (var c in creditos.OrderBy(c => c.Data).ThenBy(c => c.Valor))
        {
            var candidatos = boletos
                .Where(b => !usados.Contains(b.Id) && b.Vencimento is DateOnly v && b.Valor > 0
                    && v.DayNumber - c.Data.DayNumber >= -DiasAntes && v.DayNumber - c.Data.DayNumber <= DiasDepois
                    && (b.RecebidoEm == null || Math.Abs(b.RecebidoEm.Value.DayNumber - c.Data.DayNumber) <= 5))
                .OrderBy(b => Distancia(b, c.Data)).Take(MaxCandidatos)
                .OrderByDescending(b => b.Valor).ToList();
            var solucoes = Somas(candidatos, c.Valor);
            if (solucoes.Count == 0) { conferidos.Add(new(c, [], [])); continue; }

            var melhores = solucoes.Select(s => (S: s, Nota: s.Sum(b => Distancia(b, c.Data))))
                .OrderBy(x => x.Nota).ThenBy(x => x.S.Min(b => b.Vencimento!.Value.DayNumber)).ToList();
            var escolhida = melhores[0].S;
            // Dúvida: outra solução de mesma nota que troca algum boleto em aberto
            var abertos = escolhida.Where(b => b.RecebidoEm == null).Select(b => b.Id).ToHashSet();
            var alternativas = melhores.Skip(1).Where(x => x.Nota == melhores[0].Nota)
                .Select(x => x.S).Where(s => !s.Where(b => b.RecebidoEm == null).Select(b => b.Id).ToHashSet().SetEquals(abertos))
                .Select(s => (IReadOnlyList<Boleto>)s).ToList();
            foreach (var b in escolhida) usados.Add(b.Id);
            conferidos.Add(new(c, escolhida, alternativas));
        }

        var pagos = conferidos.SelectMany(c => c.Boletos.Where(b => b.RecebidoEm == null).Select(b => (b, c.Credito))).ToList();
        var vencidos = boletos.Where(b => b.RecebidoEm == null && b.Vencimento < hoje && !usados.Contains(b.Id))
            .OrderBy(b => b.Vencimento).ToList();
        return new(conferidos, pagos, vencidos);
    }

    // Já baixado: distância da baixa ao crédito; em aberto: do vencimento.
    private static int Distancia(Boleto b, DateOnly data) =>
        Math.Abs((b.RecebidoEm ?? b.Vencimento ?? data).DayNumber - data.DayNumber);

    // Combinações que somam o alvo (centavos), candidatos do maior para o menor. Também usada no Pix (PixBoletos).
    public static List<List<Boleto>> Somas(List<Boleto> c, decimal alvo, int maxSolucoes = MaxSolucoes)
    {
        var res = new List<List<Boleto>>();
        var resto = new decimal[c.Count + 1];
        for (var i = c.Count - 1; i >= 0; i--) resto[i] = resto[i + 1] + c[i].Valor;
        var atual = new List<Boleto>();
        void Ir(int i, decimal soma)
        {
            if (res.Count >= maxSolucoes) return;
            if (Math.Abs(soma - alvo) < 0.005m) { res.Add([.. atual]); return; }
            if (i >= c.Count || soma > alvo + 0.005m || soma + resto[i] < alvo - 0.005m) return;
            atual.Add(c[i]); Ir(i + 1, soma + c[i].Valor); atual.RemoveAt(atual.Count - 1);
            Ir(i + 1, soma);
        }
        Ir(0, 0);
        return res;
    }
}
