using TransportadoraSystem.Core.Entities;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// O que a aba (ou o envio) traz para reconhecer a mesma viagem pela regra "provável": os valores
/// de abastecimento, o total de caixas de volumes e as viagens que já têm ligado o controle de
/// carregamento do período da aba.
/// </summary>
public record SinaisViagem(IReadOnlyCollection<decimal> Abastecimentos, decimal Caixas, IReadOnlySet<int>? ComCarregamentoDoDia = null)
{
    public static readonly SinaisViagem Nenhum = new([], 0);
}

/// <summary>
/// Casamento único de viagem (docs/planos/importacoes-mesclar.md, "Regra de junção"): diz qual
/// viagem do sistema é a mesma da aba do acerto / do envio. Sem banco: recebe as candidatas.
/// Nesta ordem, cada viagem do sistema casando com uma aba só (<c>jaAchadas</c> fica de fora):
/// 1. chave: mesma ChaveImportacao e saída a até 3 dias (mais longe = a aba mudou de número: não vale);
/// 2. data: sem chave, mesma placa, mesma data de saída;
/// 3. provável: sem chave, mesma placa, saída a até 3 dias OU períodos que se cruzam, UMA só candidata
///    e pelo menos um sinal igual (abastecimento, total de caixas ou controle de carregamento).
///    Provável nunca grava sozinho: quem chama manda para confirmação.
/// </summary>
public static class ViagemCasamento
{
    public const string Chave = "chave", Data = "data", Provavel = "provavel";
    public const int DiasPerto = 3;

    public static (ViagemCaminhao? Viagem, string? Grau, string? Motivo) Achar(string placa, DateOnly? saida, DateOnly? chegada,
        string? chave, SinaisViagem? sinais, IEnumerable<ViagemCaminhao> candidatas, IReadOnlySet<int> jaAchadas)
    {
        var p = NormalizarPlaca(placa);
        var livres = candidatas.Where(v => !jaAchadas.Contains(v.Id)).ToList();
        string? motivo = null;
        ViagemCaminhao? chaveLonge = null;

        // 1. Chave
        if (!string.IsNullOrEmpty(chave) && livres.FirstOrDefault(v => v.ChaveImportacao == chave) is { } pc)
        {
            if (saida == null || pc.DataSaida == null || Math.Abs(pc.DataSaida.Value.DayNumber - saida.Value.DayNumber) <= DiasPerto)
                return (pc, Chave, null);
            chaveLonge = pc;
            motivo = $"a viagem {pc.Id} tem a chave desta aba, mas sai em {pc.DataSaida:dd/MM} (aba: {saida:dd/MM}); a aba deve ter mudado de número na planilha";
        }
        if (saida == null) return (null, null, motivo);

        // Candidatas das regras 2 e 3: sem chave (ou a da chave que ficou longe), mesma placa
        var semChave = livres.Where(v => (v.ChaveImportacao == null || v == chaveLonge) && v.DataSaida != null && NormalizarPlaca(v.Placa) == p).ToList();

        // 2. Data
        if (semChave.Where(v => v.DataSaida == saida).OrderBy(v => v.Id).FirstOrDefault() is { } pd)
            return (pd, Data, motivo);

        // 3. Provável
        var fimAba = chegada ?? saida.Value;
        var perto = semChave.Where(v =>
            Math.Abs(v.DataSaida!.Value.DayNumber - saida.Value.DayNumber) <= DiasPerto
            || v.DataSaida!.Value <= fimAba && (v.DataChegada ?? v.DataSaida!.Value) >= saida.Value).ToList();
        if (perto.Count == 0) return (null, null, motivo);
        if (perto.Count > 1)
            return (null, null, Juntar(motivo, $"{perto.Count} viagens perto da saída {saida:dd/MM} ({string.Join(", ", perto.OrderBy(v => v.DataSaida).Select(v => $"{v.Id} em {v.DataSaida:dd/MM}"))}): nenhuma foi casada"));
        var c = perto[0];
        var sinal = SinalIgual(c, sinais ?? SinaisViagem.Nenhum);
        if (sinal == null) return (null, null, motivo);
        return (c, Provavel, Juntar(motivo, $"provável: viagem {c.Id} sai em {c.DataSaida:dd/MM} (aba: {saida:dd/MM}); {sinal}"));
    }

    /// <summary>O primeiro sinal igual entre a viagem e a aba, ou nulo.</summary>
    public static string? SinalIgual(ViagemCaminhao v, SinaisViagem s)
    {
        var abs = s.Abastecimentos.Where(x => x > 0).ToList();
        if (v.Abastecimentos.FirstOrDefault(a => a.Valor > 0 && abs.Any(x => Math.Abs(x - a.Valor) < 0.005m)) is { } ab)
            return $"mesmo abastecimento de R$ {ab.Valor:N2}";
        var caixas = v.Volumes.Sum(o => o.CaixasVolumes + o.CaixasProdutoE + o.CaixasProdutoD);
        if (s.Caixas > 0 && Math.Abs(caixas - s.Caixas) < 0.005m) return $"mesmo total de {s.Caixas:N0} caixas de volumes";
        if (s.ComCarregamentoDoDia?.Contains(v.Id) == true) return "mesmo controle de carregamento";
        return null;
    }

    private static string Juntar(string? a, string b) => a == null ? b : a + "; " + b;
}
