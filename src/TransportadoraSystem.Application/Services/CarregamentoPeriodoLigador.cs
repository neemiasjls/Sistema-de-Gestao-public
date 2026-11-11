namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-09/VIA-25: o controle de carregamento solto vai para a viagem do caminhão da mesma placa (cavalo) pela janela
/// de saída − <see cref="DiasAntesDaSaida"/> até o fim do período (<see cref="ViagemPeriodoLigador.Periodo"/>).
/// Vale a viagem que sai no dia do controle ou tem anotação de carga VOLUMES nesse dia; sem isso, a única da janela; mais
/// de uma, a de saída mais perto do dia (com aviso). Empate ou mais de uma exata → não liga (dúvida). Viagem que já
/// tem controle não é candidata. O filtro "placa do Bento" (VIA-24) fica com quem chama. Puro: não toca o banco.
/// </summary>
public static class CarregamentoPeriodoLigador
{
    public const int DiasAntesDaSaida = 2;

    public record Carga(int Id, string? Placa, DateOnly Data);
    /// <summary>ViagemId nulo = não ligou; Duvida = havia mais de uma e não deu para escolher.</summary>
    public record Escolha(int? ViagemId, string? Aviso, bool Duvida);

    /// <summary>Janela da viagem para a carga: saída − 2 até o fim do período. Nula sem saída.</summary>
    public static (DateOnly Ini, DateOnly Fim)? Janela(ViagemPeriodoLigador.Viagem v, IEnumerable<ViagemPeriodoLigador.Viagem> todas) =>
        ViagemPeriodoLigador.Periodo(v, todas) is { } p ? (p.Ini.AddDays(-DiasAntesDaSaida), p.Fim) : null;

    public static Escolha EscolherViagem(Carga c, IReadOnlyList<ViagemPeriodoLigador.Viagem> viagens,
        Func<int, IEnumerable<DateOnly>> diasDeCargaVolumes, ISet<int> viagensComControle)
    {
        var chave = RetornoAnotacaoLigador.ChavePlaca(c.Placa);
        if (chave.Length == 0) return new(null, null, false);
        var cand = viagens
            .Where(v => v.Saida != null && !viagensComControle.Contains(v.Id) && RetornoAnotacaoLigador.ChavePlaca(v.Placa) == chave)
            .Select(v => (v, j: Janela(v, viagens)))
            .Where(x => x.j is { } j && j.Ini <= c.Data && c.Data <= j.Fim)
            .ToList();
        if (cand.Count == 0) return new(null, null, false);
        var exatas = cand.Where(x => x.v.Saida == c.Data || diasDeCargaVolumes(x.v.Id).Contains(c.Data)).ToList();
        if (exatas.Count == 1) return new(exatas[0].v.Id, null, false);
        if (exatas.Count > 1)
            return new(null, $"Controle de {c.Data:dd/MM}: {exatas.Count} viagens carregam nesse dia ({Saidas(exatas.Select(x => x.v))}): escolha na viagem.", true);
        if (cand.Count == 1) return new(cand[0].v.Id, null, false);
        int Dist(ViagemPeriodoLigador.Viagem v) => Math.Abs(v.Saida!.Value.DayNumber - c.Data.DayNumber);
        var ordem = cand.OrderBy(x => Dist(x.v)).ToList();
        if (Dist(ordem[0].v) == Dist(ordem[1].v))
            return new(null, $"Controle de {c.Data:dd/MM} cabe em {cand.Count} viagens ({Saidas(cand.Select(x => x.v))}): escolha na viagem.", true);
        var outras = string.Join(", ", ordem.Skip(1).Select(x => $"{x.v.Saida:dd/MM} a {x.j!.Value.Fim:dd/MM}"));
        return new(ordem[0].v.Id,
            $"Controle de {c.Data:dd/MM} ligado à viagem que saiu em {ordem[0].v.Saida:dd/MM}; a de {outras} também cabia.", false);
    }

    /// <summary>
    /// A mesma regra vista pela viagem: das cargas soltas da janela, as que escolheriam esta viagem; dessas, a do dia da
    /// saída ou da anotação; senão a única. Mais de uma → nulo e aviso com as datas. As anotações das outras viagens vêm de
    /// <paramref name="diasDasOutras"/> (sem ele, contam só pela saída). <paramref name="viagensComControle"/>: as outras que já têm controle.
    /// </summary>
    public static (int? CargaId, string? Aviso) EscolherCarga(ViagemPeriodoLigador.Viagem v, IReadOnlyList<ViagemPeriodoLigador.Viagem> todas,
        IEnumerable<DateOnly> diasDeCargaVolumes, IReadOnlyList<Carga> soltas, ISet<int>? viagensComControle = null,
        Func<int, IEnumerable<DateOnly>>? diasDasOutras = null)
    {
        if (v.Saida is not { } saida || Janela(v, todas) is not { } j) return (null, null);
        var dias = diasDeCargaVolumes.ToHashSet();
        var lista = todas.Any(o => o.Id == v.Id) ? todas : todas.Append(v).ToList();
        var comControle = new HashSet<int>(viagensComControle ?? new HashSet<int>());
        comControle.Remove(v.Id);
        var chave = RetornoAnotacaoLigador.ChavePlaca(v.Placa);
        var naJanela = soltas.Where(c => RetornoAnotacaoLigador.ChavePlaca(c.Placa) == chave && j.Ini <= c.Data && c.Data <= j.Fim).ToList();
        if (naJanela.Count == 0) return (null, null);
        var escolhas = naJanela.Select(c => (c, e: EscolherViagem(c, lista, id => id == v.Id ? dias : diasDasOutras?.Invoke(id) ?? [], comControle))).ToList();
        var minhas = escolhas.Where(x => x.e.ViagemId == v.Id).Select(x => x.c).ToList();
        if (minhas.Count == 0)
        {
            var duvida = escolhas.FirstOrDefault(x => x.e.Duvida);
            return (null, duvida.e?.Aviso);
        }
        var exatas = minhas.Where(c => c.Data == saida || dias.Contains(c.Data)).ToList();
        if (exatas.Count == 1) return (exatas[0].Id, null);
        if (exatas.Count == 0 && minhas.Count == 1) return (minhas[0].Id, null);
        var possiveis = exatas.Count > 1 ? exatas : minhas;
        return (null, $"{possiveis.Count} controles possíveis para a viagem de {saida:dd/MM} ({string.Join(", ", possiveis.OrderBy(c => c.Data).Select(c => c.Data.ToString("dd/MM")))}): escolha na viagem.");
    }

    private static string Saidas(IEnumerable<ViagemPeriodoLigador.Viagem> vs) =>
        string.Join(", ", vs.OrderBy(v => v.Saida).Select(v => $"saída {v.Saida:dd/MM}"));
}
