using System.Globalization;
using System.Text;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Casa as linhas da planilha de retornos com os retornos já gravados (UI-22: importar de novo
/// atualiza, nunca duplica). A chave da linha leva valor e destino, então corrigir um valor ou a
/// placa na planilha criava outra linha. Ordem: chave exata → mesma data, placa e transportadora →
/// mesma transportadora, valor e destino com até 3 dias e placa igual ou com 1 caractere trocado.
/// Cada retorno gravado casa com uma linha só; empate não casa (entra como novo só se nada for parecido).
/// </summary>
public static class RetornosCasamento
{
    public record Gravado(int Id, DateOnly Data, string? Placa, string? Transportadora, string? Destino, decimal ValorFrete, string? Chave);

    public record Par(RetornoLido Linha, int? GravadoId, string Grau, string? Aviso);

    public static List<Par> Casar(IReadOnlyList<RetornoLido> linhas, IReadOnlyList<Gravado> gravados)
    {
        var usados = new HashSet<int>();
        var resultado = new Par?[linhas.Count];

        // 1) chave exata
        var porChave = gravados.Where(g => g.Chave != null).GroupBy(g => g.Chave!).ToDictionary(g => g.Key, g => g.First());
        for (var i = 0; i < linhas.Count; i++)
            if (porChave.TryGetValue(linhas[i].Chave, out var g) && usados.Add(g.Id))
                resultado[i] = new Par(linhas[i], g.Id, "chave", null);

        // 2) mesma data, placa e transportadora (valor ou destino corrigido)
        for (var i = 0; i < linhas.Count; i++)
        {
            if (resultado[i] != null) continue;
            var l = linhas[i];
            var cand = gravados.Where(g => !usados.Contains(g.Id) && g.Data == l.Data
                && Placa(g.Placa) == Placa(l.Placa) && Texto(g.Transportadora) == Texto(l.Transportadora)).ToList();
            var g = Unico(cand, l);
            if (g != null && usados.Add(g.Id)) resultado[i] = new Par(l, g.Id, "data", null);
            else if (cand.Count > 1) resultado[i] = new Par(l, null, "empate",
                $"{l.Data:dd/MM} {l.Placa} {l.Transportadora}: {cand.Count} retornos parecidos no sistema; entrou como novo, confira.");
        }

        // 3) mesma transportadora, valor e destino, até 3 dias, placa igual ou com 1 caractere trocado
        for (var i = 0; i < linhas.Count; i++)
        {
            if (resultado[i] is { GravadoId: not null } or { Grau: "empate" }) continue;
            var l = linhas[i];
            var cand = gravados.Where(g => !usados.Contains(g.Id) && Texto(g.Transportadora) == Texto(l.Transportadora)
                && g.ValorFrete == l.ValorFrete && Texto(g.Destino) == Texto(l.Destino)
                && Math.Abs(g.Data.DayNumber - l.Data.DayNumber) <= 3 && Diferenca(Placa(g.Placa), Placa(l.Placa)) <= 1).ToList();
            if (cand.Count == 1 && usados.Add(cand[0].Id)) resultado[i] = new Par(l, cand[0].Id, "parecido", null);
        }

        return resultado.Select((p, i) => p ?? new Par(linhas[i], null, "novo", null)).ToList();
    }

    // Empate na etapa 2: prefere o de mesmo valor, depois o de mesmo destino
    private static Gravado? Unico(List<Gravado> cand, RetornoLido l)
    {
        if (cand.Count <= 1) return cand.FirstOrDefault();
        var mesmoValor = cand.Where(g => g.ValorFrete == l.ValorFrete).ToList();
        if (mesmoValor.Count == 1) return mesmoValor[0];
        var mesmoDestino = (mesmoValor.Count > 0 ? mesmoValor : cand).Where(g => Texto(g.Destino) == Texto(l.Destino)).ToList();
        return mesmoDestino.Count == 1 ? mesmoDestino[0] : null;
    }

    public static string Placa(string? p) => new string((p ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static string Texto(string? s)
    {
        var d = (s ?? "").Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsWhiteSpace(c) ? ' ' : c);
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Diferenca(string a, string b)
    {
        if (a.Length != b.Length) return a == b ? 0 : 99;
        return a.Zip(b).Count(x => x.First != x.Second);
    }
}
