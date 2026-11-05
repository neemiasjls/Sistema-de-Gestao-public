namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Contas puras do fechamento mensal (VIA-18), iguais às fórmulas da planilha:
///   ENTRADA TOTAL = resultados das viagens + créditos
///   SAÍDA TOTAL   = soma das despesas do mês
///   SOBRA         = entrada − saída;  TOTAL = sobra + saldo anterior
///   SALÁRIO LÍQUIDO (COMISSÃO I11) = salário + horas extras + comissão + quinquênio − INSS − IRR − vales
/// </summary>
public static class FechamentoMensalCalculo
{
    public record Totais(decimal EntradaViagens, decimal Creditos, decimal EntradaTotal, decimal SaidaTotal,
        decimal Sobra, decimal SaldoAnterior, decimal Total);

    public static Totais CalcularTotais(IEnumerable<decimal> resultadosViagens, IEnumerable<decimal> creditos,
        IEnumerable<decimal?> despesas, decimal saldoAnterior)
    {
        var viagens = Math.Round(resultadosViagens.Sum(), 2);
        var cred = Math.Round(creditos.Sum(), 2);
        var entrada = viagens + cred;
        var saida = Math.Round(despesas.Sum(d => d ?? 0), 2);
        var sobra = entrada - saida;
        return new Totais(viagens, cred, entrada, saida, sobra, saldoAnterior, sobra + saldoAnterior);
    }

    public static decimal SalarioLiquido(decimal salario, decimal horasExtras, decimal comissaoTotal,
        decimal quinquenio, decimal inss, decimal irr, decimal totalVales) =>
        salario + horasExtras + comissaoTotal + quinquenio - inss - irr - totalVales;

    public static (int Ano, int Mes) MesAnterior(int ano, int mes) => mes == 1 ? (ano - 1, 12) : (ano, mes - 1);

    /// <summary>Número da parcela do mês (1..total) ou nulo se o mês está fora do prazo.</summary>
    public static int? NumeroParcela(int primeiroAno, int primeiroMes, int totalParcelas, int ano, int mes)
    {
        var n = (ano - primeiroAno) * 12 + (mes - primeiroMes) + 1;
        return n >= 1 && n <= totalParcelas ? n : null;
    }

    /// <summary>
    /// Total do caminhão na planilha SG: a linha "CAMINHÃO placa" do RESUMO (ICMS + seguro + encargos).
    /// Sem planilha ou sem a placa nela, valor nulo e aviso.
    /// </summary>
    public static (decimal? Valor, string? Aviso) TotalSeguroDaPlaca(PlanilhaSeguroLeitor.Resumo? resumo, string placa, int mesSg, int anoSg)
    {
        var nome = $"{PlanilhaSeguroLeitor.NomeMes(mesSg)} de {anoSg}";
        if (resumo == null) return (null, $"Falta a planilha SG de {nome}");
        var p = resumo.Placas.FirstOrDefault(x => ViagemCaminhaoCalculo.NormalizarPlaca(x.Nome) == placa && x.TotalCaminhao != null);
        return p?.TotalCaminhao is { } v
            ? (v, null)
            : (null, $"A planilha SG de {nome} não traz o total de {placa}");
    }
}
