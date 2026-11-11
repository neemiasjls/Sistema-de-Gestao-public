namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-25: retorno sem viagem entra na viagem do caminhão cuja placa (cavalo ou carreta) é a do retorno e cujo
/// período (saída até chegada) contém a data. Viagem sem chegada: da saída até a véspera da próxima saída da mesma
/// placa, no máximo <see cref="DiasSemChegada"/> dias. Uma viagem → liga; nenhuma ou mais de uma → não liga (avisa).
/// Retorno já ligado ou tirado da viagem à mão (<see cref="Solto"/>) não entra. Puro: não toca o banco.
/// </summary>
public static class ViagemPeriodoLigador
{
    // Situação em RetornosFrete.ViagemLigacao
    public const string Auto = "auto", Manual = "manual", Solto = "solto";
    public const int DiasSemChegada = 10;

    public record Viagem(int Id, string? Placa, string? Carreta, DateOnly? Saida, DateOnly? Chegada, int? MotoristaId);
    public record Retorno(int Id, string? Placa, DateOnly Data, int? ViagemId, string? ViagemLigacao);
    /// <summary>ViagemId nulo = não ligou; Aviso diz por quê.</summary>
    public record Resultado(int RetornoId, int? ViagemId, int? MotoristaId, string? Aviso);

    public static (DateOnly Ini, DateOnly Fim)? Periodo(Viagem v, IEnumerable<Viagem> todas)
    {
        if (v.Saida is not { } saida) return null;
        if (v.Chegada is { } chegada) return (saida, chegada < saida ? saida : chegada);
        var chave = RetornoAnotacaoLigador.ChavePlaca(v.Placa);
        var fim = saida.AddDays(DiasSemChegada);
        var proxima = todas.Where(o => o.Id != v.Id && o.Saida > saida && RetornoAnotacaoLigador.ChavePlaca(o.Placa) == chave)
            .Select(o => o.Saida!.Value).DefaultIfEmpty(DateOnly.MaxValue).Min();
        if (proxima != DateOnly.MaxValue && proxima.AddDays(-1) < fim) fim = proxima.AddDays(-1);
        return (saida, fim < saida ? saida : fim);
    }

    public static bool Elegivel(Retorno r) =>
        r.ViagemId == null && r.ViagemLigacao != Solto && RetornoAnotacaoLigador.ChavePlaca(r.Placa).Length > 0;

    public static List<Resultado> Ligar(IEnumerable<Retorno> retornos, IEnumerable<Viagem> viagens)
    {
        var todas = viagens.ToList();
        var periodos = todas.Select(v => (v, p: Periodo(v, todas))).Where(x => x.p != null).ToList();
        var res = new List<Resultado>();
        foreach (var r in retornos.Where(Elegivel))
        {
            var chave = RetornoAnotacaoLigador.ChavePlaca(r.Placa);
            var cand = periodos.Where(x => (RetornoAnotacaoLigador.ChavePlaca(x.v.Placa) == chave
                                            || RetornoAnotacaoLigador.ChavePlaca(x.v.Carreta) == chave)
                                           && x.p!.Value.Ini <= r.Data && r.Data <= x.p.Value.Fim)
                .Select(x => x.v).ToList();
            res.Add(cand.Count switch
            {
                1 => new Resultado(r.Id, cand[0].Id, cand[0].MotoristaId, null),
                0 => new Resultado(r.Id, null, null, $"Nenhuma viagem da {r.Placa} em {r.Data:dd/MM}."),
                _ => new Resultado(r.Id, null, null,
                    $"{cand.Count} viagens da {r.Placa} em {r.Data:dd/MM} ({string.Join(", ", cand.Select(c => $"saída {c.Saida:dd/MM}"))}): escolha à mão."),
            });
        }
        return res;
    }
}
