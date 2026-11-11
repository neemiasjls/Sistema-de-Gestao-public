namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-27 (Admin, 10/11/2026): o retorno é lançado à mão (frete contratado e descarga); o sistema acha
/// sozinho o contrato da Strada (PDF subido em Agrotrade / TIP Bank) que paga esse frete:
///  1. "manual": mantém o contrato escolhido; sem PDF, avisa;
///  2. "solto": o escritório tirou o contrato, não liga sozinho;
///  3. automático: mesma placa (cavalo do PDF), data do contrato a até 3 dias da data do retorno,
///     valor do contrato ≥ frete contratado (tira os contratos de reembolso) e contrato sem outro retorno.
///     Um candidato liga; vários, o de data mais perto; empate na distância = "conferir";
///  4. dois retornos no mesmo contrato = os dois "conferir".
/// Só decide, não grava; quem grava é StradaRetornos.SincronizarAsync. Só contratos COM PDF entram,
/// um por número (o valor é o ValorContratoPdf, valor cheio antes do seguro).
/// </summary>
public static class RetornoContratoLigador
{
    public const string Ligado = "ligado";
    public const string Manual = "manual";
    public const string Conferir = "conferir";
    public const string SemPar = "sem_par";
    public const string Solto = "solto";
    public const int JanelaDias = 3;

    public record Retorno(int Id, string? Placa, DateOnly Data, decimal ValorFrete, string? Contrato,
        string? ContratoLigacao, string? Transportadora = null);
    public record Contrato(string Numero, string? Placa, DateOnly? DataContrato, decimal ValorContratoPdf, string? Nf);
    public record Resultado(int RetornoId, string? Contrato, decimal? ValorContrato, string Situacao, string? Aviso);

    /// <summary>Retorno da Agrotrade (transportadora contém "AGROTRADE") avisa que espera o PDF; os outros não.</summary>
    public static bool EhAgrotrade(string? transportadora) =>
        (transportadora ?? "").Contains("AGROTRADE", StringComparison.OrdinalIgnoreCase);

    public static List<Resultado> Ligar(IEnumerable<Retorno> retornos, IEnumerable<Contrato> contratos)
    {
        var lista = retornos.ToList();
        var porNumero = contratos.GroupBy(c => c.Numero).ToDictionary(g => g.Key, g => g.First());

        // Os escolhidos à mão reservam o contrato antes da busca automática
        var reservados = lista.Where(r => r.ContratoLigacao == Manual && !string.IsNullOrWhiteSpace(r.Contrato))
            .Select(r => r.Contrato!).ToHashSet();
        var livres = porNumero.Values.Where(c => !reservados.Contains(c.Numero)).ToList();

        var escolhas = lista.OrderBy(r => r.Data).ThenBy(r => r.Id).Select(r => r.ContratoLigacao switch
        {
            Manual when !string.IsNullOrWhiteSpace(r.Contrato) => Manter(r, porNumero),
            Solto => new Resultado(r.Id, null, null, Solto, null),
            _ => Escolher(r, livres),
        }).ToList();

        // Um contrato, um retorno: se dois ficaram com o mesmo, os dois vão para "conferir"
        var disputados = escolhas.Where(e => e.Contrato != null).GroupBy(e => e.Contrato!)
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());
        return escolhas.Select(e => e.Contrato is { } n && disputados.TryGetValue(n, out var qtd)
            ? new Resultado(e.RetornoId, null, null, Conferir, $"O contrato {n} bate com {qtd} retornos: escolha qual é.")
            : e).ToList();
    }

    private static Resultado Manter(Retorno r, Dictionary<string, Contrato> porNumero)
    {
        if (!porNumero.TryGetValue(r.Contrato!, out var c))
            return new(r.Id, r.Contrato, null, Manual, $"Contrato {r.Contrato} sem PDF: suba o PDF.");
        var aviso = c.ValorContratoPdf < r.ValorFrete ? "Contrato menor que o frete contratado: confira o frete." : null;
        return new(r.Id, c.Numero, c.ValorContratoPdf, Manual, aviso);
    }

    private static Resultado Escolher(Retorno r, List<Contrato> livres)
    {
        var placa = RetornoAnotacaoLigador.ChavePlaca(r.Placa);
        if (placa.Length == 0) return new(r.Id, null, null, SemPar, "O retorno está sem placa.");

        var cands = livres
            .Where(c => RetornoAnotacaoLigador.ChavePlaca(c.Placa) == placa && c.DataContrato is { } d
                && Math.Abs(d.DayNumber - r.Data.DayNumber) <= JanelaDias && c.ValorContratoPdf >= r.ValorFrete)
            .Select(c => (c, dist: Math.Abs(c.DataContrato!.Value.DayNumber - r.Data.DayNumber)))
            .ToList();
        if (cands.Count == 0)
            return new(r.Id, null, null, SemPar, EhAgrotrade(r.Transportadora) ? "Aguardando o PDF do contrato." : null);

        var menor = cands.Min(x => x.dist);
        var maisPerto = cands.Where(x => x.dist == menor).Select(x => x.c).ToList();
        if (maisPerto.Count > 1)
            return new(r.Id, null, null, Conferir,
                $"{maisPerto.Count} contratos da placa {r.Placa} na mesma distância da data: {string.Join(", ", maisPerto.Select(c => c.Numero))}.");
        var e = maisPerto[0];
        return new(r.Id, e.Numero, e.ValorContratoPdf, Ligado, null);
    }
}
