using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-22 (Admin, 09/11/2026): o retorno nasce só da planilha importada; a "outra carga"
/// que o motorista anota no painel é ligada pelo SISTEMA ao retorno importado:
///  1. mesma placa (sem traço, maiúscula; cavalo ou carreta da viagem) e mesma data;
///  2. sem nenhum na data, aceita 1 dia antes ou depois SÓ se houver um único;
///  3. mais de um na data: desempata pelo valor e depois pelo destino anotados; persistindo, "conferir";
///  4. um retorno liga a no máximo uma anotação: duas anotações no mesmo retorno = as duas "conferir";
///  5. retorno que já está em OUTRA viagem não é ligado ("conferir").
/// Só decide; quem grava é ViagensCaminhaoService.LigarAnotacoesAsync.
/// </summary>
public static class RetornoAnotacaoLigador
{
    public const string Ligado = "ligado";      // o sistema ligou
    public const string Manual = "manual";      // o escritório ligou à mão
    public const string Conferir = "conferir";  // mais de um candidato ou conflito
    public const string SemPar = "sem_par";     // nenhum retorno importado
    public const string Solto = "solto";        // o escritório tirou o retorno da viagem: não liga sozinho de novo

    public record Anotacao(int Id, int ViagemId, IReadOnlyList<string?> Placas, DateOnly? Data, decimal Valor, string? Destino);
    public record Candidato(int Id, string? Placa, DateOnly Data, decimal ValorFrete, decimal FreteTotal, string? Destino, int? ViagemId);
    public record Resultado(int AnotacaoId, int? RetornoId, string Situacao, string? Aviso);

    /// <summary>Placa sem traço nem espaço, maiúscula (XAC-6D57 = xac6d57).</summary>
    public static string ChavePlaca(string? placa) =>
        new((placa ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static List<Resultado> Ligar(IEnumerable<Anotacao> anotacoes, IEnumerable<Candidato> livres)
    {
        var cands = livres.ToList();
        var escolhas = new List<Resultado>();
        foreach (var a in anotacoes.OrderBy(a => a.Data).ThenBy(a => a.Id))
            escolhas.Add(Escolher(a, cands));

        // Um retorno, uma anotação: se duas escolheram o mesmo, nenhuma liga
        var disputados = escolhas.Where(e => e.RetornoId != null).GroupBy(e => e.RetornoId).Where(g => g.Count() > 1)
            .Select(g => g.Key!.Value).ToHashSet();
        return escolhas.Select(e => e.RetornoId is { } rid && disputados.Contains(rid)
            ? new Resultado(e.AnotacaoId, null, Conferir, "Mais de uma anotação do motorista bate com o mesmo retorno.")
            : e).ToList();
    }

    private static Resultado Escolher(Anotacao a, List<Candidato> cands)
    {
        var placas = a.Placas.Select(ChavePlaca).Where(p => p.Length > 0).ToHashSet();
        var placaTexto = a.Placas.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "sem placa";
        if (a.Data is not { } dia)
            return new(a.Id, null, SemPar, "A anotação está sem data.");
        if (placas.Count == 0)
            return new(a.Id, null, SemPar, "A viagem está sem placa.");

        var daPlaca = cands.Where(c => placas.Contains(ChavePlaca(c.Placa))).ToList();
        var noDia = daPlaca.Where(c => c.Data == dia).ToList();
        Candidato? escolhido;
        string? aviso = null;
        if (noDia.Count == 1) escolhido = noDia[0];
        else if (noDia.Count > 1)
        {
            escolhido = Desempatar(a, noDia);
            if (escolhido == null)
                return new(a.Id, null, Conferir, $"{noDia.Count} retornos da placa {placaTexto} em {dia:dd/MM}: o valor e o destino anotados não dizem qual é.");
        }
        else
        {
            var vizinhos = daPlaca.Where(c => Math.Abs(c.Data.DayNumber - dia.DayNumber) == 1).ToList();
            if (vizinhos.Count == 0)
                return new(a.Id, null, SemPar, $"Nenhum retorno importado da placa {placaTexto} em {dia:dd/MM}.");
            if (vizinhos.Count > 1)
                return new(a.Id, null, Conferir, $"Nenhum retorno da placa {placaTexto} em {dia:dd/MM} e {vizinhos.Count} com 1 dia de diferença.");
            escolhido = vizinhos[0];
            aviso = $"Data do retorno {escolhido.Data:dd/MM}, 1 dia de diferença da anotada ({dia:dd/MM}).";
        }

        if (escolhido.ViagemId is { } outra && outra != a.ViagemId)
            return new(a.Id, null, Conferir, $"O retorno de {escolhido.Data:dd/MM} desta placa já está ligado a outra viagem.");
        return new(a.Id, escolhido.Id, Ligado, aviso);
    }

    /// <summary>Valor anotado (frete ou frete total, centavos) e depois destino; null se não sobrar um só.</summary>
    private static Candidato? Desempatar(Anotacao a, List<Candidato> lista)
    {
        var resto = lista;
        if (a.Valor > 0)
        {
            var porValor = resto.Where(c => Math.Abs(c.ValorFrete - a.Valor) < 0.01m || Math.Abs(c.FreteTotal - a.Valor) < 0.01m).ToList();
            if (porValor.Count > 0) resto = porValor;
        }
        if (resto.Count > 1 && Normalizar(a.Destino) is { Length: > 0 } dest)
        {
            var porDestino = resto.Where(c => Normalizar(c.Destino) is { Length: > 0 } d && (d.Contains(dest) || dest.Contains(d))).ToList();
            if (porDestino.Count > 0) resto = porDestino;
        }
        return resto.Count == 1 ? resto[0] : null;
    }
}
