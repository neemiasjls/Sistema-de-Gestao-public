namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-32: o retorno anotado na aba VIAGEMn do acerto junta com uma linha da planilha de retornos (RetornosFrete)
/// só quando há um candidato único: mesma placa (cavalo ou carreta da viagem), mesma transportadora (se a aba
/// trouxer), data a até <see cref="Dias"/> dias da data de referência, valor que cabe (<see cref="ValorCabe"/>) e livre
/// (sem viagem e não tirada à mão). Linha que já está nesta viagem casa primeiro: a escolha à mão ("manual") vale só
/// pelo valor; a automática tem de ser a única que cabe. Puro: não toca o banco.
/// </summary>
public static class RetornoAcertoCasamento
{
    public const int Dias = 5;
    public const decimal FolgaPercentual = 0.01m, FolgaReais = 50m, FolgaContrato = 1m, AdiantamentoMaximo = 1500m;

    public record Linha(int Id, string? Placa, string? Transportadora, DateOnly Data, decimal ValorFrete, decimal Adicional,
        decimal FreteTotal, int? ViagemId, string? ViagemLigacao, bool ForaDoControle, decimal? ValorContrato = null);

    /// <summary>Escolhida nula = não junta; Candidatos = as que cabem (0 ou 2+; JaNaViagem = duas automáticas desta viagem).</summary>
    public record Resultado(Linha? Escolhida, bool JaNaViagem, List<Linha> Candidatos);

    /// <summary>
    /// A aba costuma trazer o valor do contrato da Strada = frete contratado + adiantamento de descarga (VIA-27), não o
    /// frete + a descarga real. Cabe quando: (a) = valor do contrato (±R$ 1); (b) = frete, frete + descarga ou frete total
    /// (folga de 1% ou R$ 50); (c) entre o frete e o frete + R$ 1.500 (adiantamento de descarga).
    /// </summary>
    public static bool ValorCabe(decimal valorAba, Linha l)
    {
        if (l.ValorContrato is decimal c && Math.Abs(c - valorAba) <= FolgaContrato) return true;
        var folga = Math.Max(FolgaReais, Math.Abs(valorAba) * FolgaPercentual);
        if (new[] { l.ValorFrete, l.ValorFrete + l.Adicional, l.FreteTotal }.Any(x => Math.Abs(x - valorAba) <= folga)) return true;
        return valorAba >= l.ValorFrete && valorAba <= l.ValorFrete + AdiantamentoMaximo;
    }

    public static Resultado Escolher(decimal valor, string? transportadora, DateOnly data, string? placa, string? carreta,
        int viagemId, IEnumerable<Linha> linhas, IReadOnlySet<int> usados)
    {
        var chaves = new[] { placa, carreta }.Select(RetornoAnotacaoLigador.ChavePlaca).Where(x => x.Length > 0).ToHashSet();
        var transp = ViagemCaminhaoCalculo.Normalizar(transportadora);
        bool Cabe(Linha l) =>
            chaves.Contains(RetornoAnotacaoLigador.ChavePlaca(l.Placa))
            && (transp.Length == 0 || ViagemCaminhaoCalculo.Normalizar(l.Transportadora) == transp)
            && Math.Abs(l.Data.DayNumber - data.DayNumber) <= Dias
            && ValorCabe(valor, l);
        var disponiveis = linhas.Where(l => !usados.Contains(l.Id) && !l.ForaDoControle).ToList();

        // Já nesta viagem: a ligada à mão vale pelo valor (escolha do Admin); a automática tem de caber em tudo e
        // ser a única (duas que cabem = não escolhe, avisa)
        var manual = disponiveis.Where(l => l.ViagemId == viagemId && l.ViagemLigacao == ViagemPeriodoLigador.Manual && ValorCabe(valor, l))
            .OrderBy(l => Math.Abs(l.Data.DayNumber - data.DayNumber)).FirstOrDefault();
        if (manual != null) return new(manual, true, []);
        var auto = disponiveis.Where(l => l.ViagemId == viagemId && l.ViagemLigacao != ViagemPeriodoLigador.Manual && Cabe(l))
            .OrderBy(l => l.Data).ToList();
        if (auto.Count == 1) return new(auto[0], true, auto);
        if (auto.Count > 1) return new(null, true, auto);

        var livres = disponiveis.Where(l => l.ViagemId == null && l.ViagemLigacao != ViagemPeriodoLigador.Solto && Cabe(l))
            .OrderBy(l => l.Data).ToList();
        return livres.Count == 1 ? new(livres[0], false, livres) : new(null, false, livres);
    }
}
