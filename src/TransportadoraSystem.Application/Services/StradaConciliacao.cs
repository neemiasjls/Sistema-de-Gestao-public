using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Liga os contratos das faturas de repasse da Strada às viagens da base AGROTRADE, sem banco.
///
/// Ordem de ligação de cada contrato:
/// 1) contrato na coluna L da aba VIAGENS → frete; o valor (somado em todas as faturas) é comparado
///    com o esperado G − I ou G + H − I; se não bater com nenhum, vira divergência "valor ≠ esperado";
/// 2) contrato na aba CONTRATOS TIP → usa a placa dali para achar o sócio;
/// 3) senão fica "sem viagem" (tipo indefinido, sem sócio), com as viagens de mesmo valor em
///    DIF DESCARGA (M) ou diária (T), até a data de pagamento, listadas só como dica.
/// Reembolso NUNCA é ligado pelo valor: vale a NF do PDF do contrato (Fase 2) ou a ligação à mão.
/// Nenhum contrato é recusado. Sócio vem do mapa placa → sócio (cadastro de veículos).
/// </summary>
public static class StradaConciliacao
{
    private const decimal Tolerancia = 0.01m;

    public enum TipoContrato { Frete, Indefinido }

    public enum Origem { Viagens, ContratosTip, SemViagem }

    /// <summary>Viagem cujo reembolso anotado tem o mesmo valor do contrato. Só dica, nunca liga.</summary>
    public record Candidato(int Linha, DateOnly Data, string? Nf, string? Placa, string Campo, decimal Valor);

    public record ContratoConciliado(
        string Fatura, DateOnly DataPagamento, string Contrato, decimal Valor,
        TipoContrato Tipo, Origem Origem, string? Placa, Socio? Socio,
        AgrotradeViagem? Viagem, AgrotradeContratoTip? LinhaTip, decimal? ValorTotalContrato,
        IReadOnlyList<decimal> Esperados, IReadOnlyList<Candidato> Candidatos,
        IReadOnlyList<string> Divergencias);

    /// <summary>Parte de uma fatura que cai para um sócio (Socio nulo = "sem sócio").</summary>
    public record TotalSocio(Socio? Socio, decimal Total, decimal Taxa, decimal Repasse);

    public record FaturaConciliada(
        StradaRepasseLeitor.Fatura Fatura, IReadOnlyList<ContratoConciliado> Contratos,
        IReadOnlyList<TotalSocio> PorSocio, IReadOnlyList<string> Observacoes);

    public record Resultado(IReadOnlyList<FaturaConciliada> Faturas, IReadOnlyList<string> Divergencias);

    public static Resultado Conciliar(
        IEnumerable<StradaRepasseLeitor.Fatura> faturas,
        IReadOnlyList<AgrotradeViagem> viagens,
        IReadOnlyList<AgrotradeContratoTip> contratosTip,
        IReadOnlyDictionary<string, Socio> socioPorPlaca)
    {
        var placas = socioPorPlaca.ToDictionary(
            p => ViagemCaminhaoCalculo.NormalizarPlaca(p.Key), p => p.Value, StringComparer.OrdinalIgnoreCase);
        var divergencias = new List<string>();

        // Fatura repetida (mesmo arquivo baixado duas vezes) somaria o contrato em dobro: entra uma vez.
        var distintas = new List<StradaRepasseLeitor.Fatura>();
        foreach (var grupo in faturas.GroupBy(f => f.Numero))
        {
            distintas.Add(grupo.First());
            if (grupo.Count() > 1)
                divergencias.Add($"Fatura {grupo.Key} veio {grupo.Count()} vezes: considerada uma vez.");
        }

        var viagemPorContrato = viagens.Where(v => !string.IsNullOrWhiteSpace(v.Contrato))
            .GroupBy(v => v.Contrato!.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var tipPorContrato = contratosTip.Where(c => !string.IsNullOrWhiteSpace(c.Contrato))
            .GroupBy(c => c.Contrato!.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Contrato pode vir em mais de uma fatura (adiantamento numa, saldo noutra): compara a soma.
        var totalPorContrato = distintas.SelectMany(f => f.Contratos)
            .GroupBy(c => c.Numero, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Valor), StringComparer.Ordinal);

        var resultado = new List<FaturaConciliada>();
        foreach (var fatura in distintas.OrderBy(f => f.DataPagamento).ThenBy(f => f.Numero))
        {
            var contratos = fatura.Contratos.Select(c => Ligar(
                fatura, c, totalPorContrato[c.Numero], viagemPorContrato, tipPorContrato, viagens, placas)).ToList();
            divergencias.AddRange(contratos.SelectMany(c => c.Divergencias.Select(d => $"Fatura {fatura.Numero}, contrato {c.Contrato}: {d}")));

            var porSocio = contratos.GroupBy(c => c.Socio)
                .OrderBy(g => g.Key is null ? int.MaxValue : (int)g.Key.Value)
                .Select(g =>
                {
                    var total = g.Sum(c => c.Valor);
                    var taxa = StradaRepasseLeitor.CalcularTaxa(total);
                    return new TotalSocio(g.Key, total, taxa, total - taxa);
                }).ToList();

            var observacoes = new List<string>();
            if (fatura.Incompleta)
                observacoes.Add("Fatura sem contratos no arquivo: subir o \"Exportar detalhamento\" para dividir por sócio.");
            else
            {
                var somaTaxas = porSocio.Sum(s => s.Taxa);
                var diferenca = Math.Abs(somaTaxas - fatura.Taxa);
                if (diferenca == 0.01m)
                    observacoes.Add($"Soma das taxas por sócio ({somaTaxas:F2}) difere 1 centavo da taxa da fatura ({fatura.Taxa:F2}).");
                else if (diferenca > 0.01m)
                    divergencias.Add($"Fatura {fatura.Numero}: soma das taxas por sócio {somaTaxas:F2} × taxa da fatura {fatura.Taxa:F2}.");
            }
            resultado.Add(new FaturaConciliada(fatura, contratos, porSocio, observacoes));
        }
        return new Resultado(resultado, divergencias);
    }

    private static ContratoConciliado Ligar(
        StradaRepasseLeitor.Fatura fatura, StradaRepasseLeitor.Contrato contrato, decimal totalContrato,
        Dictionary<string, List<AgrotradeViagem>> viagemPorContrato,
        Dictionary<string, List<AgrotradeContratoTip>> tipPorContrato,
        IReadOnlyList<AgrotradeViagem> viagens, Dictionary<string, Socio> placas)
    {
        var divergencias = new List<string>();

        if (viagemPorContrato.TryGetValue(contrato.Numero, out var ligadas))
        {
            var viagem = ligadas[0];
            if (ligadas.Count > 1)
                divergencias.Add($"contrato em {ligadas.Count} viagens (linhas {string.Join(", ", ligadas.Select(v => v.Linha))}); ligado à linha {viagem.Linha}.");
            var g = viagem.Frete ?? 0; var h = viagem.AdiantDescarga ?? 0; var i = viagem.Descontos ?? 0;
            var esperados = new[] { Math.Round(g - i, 2), Math.Round(g + h - i, 2) }.Distinct().ToList();
            if (!esperados.Any(e => Math.Abs(e - totalContrato) < Tolerancia))
                divergencias.Add($"valor ≠ esperado: recebido {totalContrato:F2}, esperado {string.Join(" ou ", esperados.Select(e => e.ToString("F2")))}.");
            var socio = SocioDaPlaca(viagem.Placa, placas, divergencias);
            return new ContratoConciliado(fatura.Numero, fatura.DataPagamento, contrato.Numero, contrato.Valor,
                TipoContrato.Frete, Origem.Viagens, viagem.Placa, socio, viagem, null, totalContrato,
                esperados, [], divergencias);
        }

        if (tipPorContrato.TryGetValue(contrato.Numero, out var linhasTip))
        {
            var linha = linhasTip[0];
            if (linhasTip.Count > 1)
                divergencias.Add($"contrato repetido na CONTRATOS TIP (linhas {string.Join(", ", linhasTip.Select(l => l.Linha))}); usada a linha {linha.Linha}.");
            var socio = SocioDaPlaca(linha.Placa, placas, divergencias);
            var tipo = (linha.Frete ?? 0) > 0 ? TipoContrato.Frete : TipoContrato.Indefinido;
            return new ContratoConciliado(fatura.Numero, fatura.DataPagamento, contrato.Numero, contrato.Valor,
                tipo, Origem.ContratosTip, linha.Placa, socio, null, linha, totalContrato, [], [], divergencias);
        }

        var candidatos = new List<Candidato>();
        foreach (var v in viagens.Where(v => v.Data <= fatura.DataPagamento))
        {
            if (v.DifDescarga is > 0 && Math.Abs(v.DifDescarga.Value - totalContrato) < Tolerancia)
                candidatos.Add(new Candidato(v.Linha, v.Data, v.NfOriginal ?? v.Nf, v.Placa, "DIF DESCARGA", v.DifDescarga.Value));
            if (v.ValorDiaria is > 0 && Math.Abs(v.ValorDiaria.Value - totalContrato) < Tolerancia)
                candidatos.Add(new Candidato(v.Linha, v.Data, v.NfOriginal ?? v.Nf, v.Placa, "diária", v.ValorDiaria.Value));
        }
        divergencias.Add("sem viagem: ligar pela NF do contrato ou à mão.");
        return new ContratoConciliado(fatura.Numero, fatura.DataPagamento, contrato.Numero, contrato.Valor,
            TipoContrato.Indefinido, Origem.SemViagem, null, null, null, null, totalContrato, [], candidatos, divergencias);
    }

    private static Socio? SocioDaPlaca(string? placa, Dictionary<string, Socio> placas, List<string> divergencias)
    {
        if (string.IsNullOrWhiteSpace(placa))
        {
            divergencias.Add("placa sem sócio: linha sem placa.");
            return null;
        }
        if (placas.TryGetValue(ViagemCaminhaoCalculo.NormalizarPlaca(placa), out var socio)) return socio;
        divergencias.Add($"placa sem sócio: {placa} fora do cadastro de veículos.");
        return null;
    }

    // ───────────── Fase 2: reembolso pela NF do PDF "Baixar contrato" ─────────────

    public enum SituacaoNf { Exata, Sugestao, Nenhuma }

    /// <summary>
    /// Resultado da ligação de um contrato pela NF. Campo = "frete", "DIF DESCARGA" ou "diária";
    /// Esperado é o valor anotado na viagem; Diferenca = valor pago − esperado (nunca recusa).
    /// </summary>
    public record LigacaoNf(SituacaoNf Situacao, AgrotradeViagem? Viagem, string? NfViagem, string? Campo,
        decimal? Esperado, decimal? Diferenca, string? Aviso);

    /// <summary>
    /// Regra confirmada (Admin, 05/11/2026): vale a NF, não o valor. NF exata → liga (descarga ou
    /// diária conforme a viagem tiver M ou T; contrato da coluna L = frete). NF com 1 número de
    /// diferença (1 dígito trocado ou ±1) E mesma placa → sugestão para confirmar. Senão nenhuma.
    /// </summary>
    public static LigacaoNf LigarPorNf(string contrato, IEnumerable<string> nfs, string? placa, decimal valor,
        IReadOnlyList<AgrotradeViagem> viagens)
    {
        var lista = nfs.Select(n => n.Trim().TrimStart('0')).Where(n => n.Length > 0).Distinct().ToList();
        var placaNorm = string.IsNullOrWhiteSpace(placa) ? null : ViagemCaminhaoCalculo.NormalizarPlaca(placa);

        foreach (var nf in lista)
        {
            var exatas = viagens.Where(v => v.Nf != null && v.Nf.TrimStart('0') == nf).ToList();
            if (exatas.Count == 0) continue;
            var viagem = exatas.FirstOrDefault(v => MesmaPlaca(v.Placa, placaNorm)) ?? exatas[0];
            var (campo, esperado) = ValorAnotado(viagem, contrato, valor);
            string? aviso = null;
            if (placaNorm != null && !MesmaPlaca(viagem.Placa, placaNorm))
                aviso = $"placa do contrato {placa} diferente da viagem {viagem.Placa}.";
            return new LigacaoNf(SituacaoNf.Exata, viagem, viagem.Nf, campo, esperado,
                esperado is null ? null : Math.Round(valor - esperado.Value, 2), aviso);
        }

        if (placaNorm != null)
            foreach (var nf in lista)
            {
                var quase = viagens.Where(v => v.Nf != null && MesmaPlaca(v.Placa, placaNorm) && UmNumeroDeDiferenca(nf, v.Nf.TrimStart('0'))).ToList();
                if (quase.Count != 1) continue;
                var viagem = quase[0];
                var (campo, esperado) = ValorAnotado(viagem, contrato, valor);
                return new LigacaoNf(SituacaoNf.Sugestao, viagem, viagem.Nf, campo, esperado,
                    esperado is null ? null : Math.Round(valor - esperado.Value, 2),
                    $"NF do contrato {nf} quase igual à da viagem {viagem.Nf}: confirmar.");
            }

        return new LigacaoNf(SituacaoNf.Nenhuma, null, null, null, null, null,
            lista.Count == 0 ? "contrato sem NF." : $"nenhuma viagem com a NF {string.Join(", ", lista)}.");
    }

    // Campo da viagem que o contrato paga: frete (contrato na coluna L), DIF DESCARGA (M) ou diária (T).
    // Viagem com as duas anotadas: a mais próxima do valor pago.
    public static (string? Campo, decimal? Esperado) ValorAnotado(AgrotradeViagem v, string contrato, decimal valor)
    {
        if (v.Contrato?.Trim() == contrato)
        {
            var g = v.Frete ?? 0; var h = v.AdiantDescarga ?? 0; var i = v.Descontos ?? 0;
            var opcoes = new[] { Math.Round(g - i, 2), Math.Round(g + h - i, 2) };
            return ("frete", opcoes.OrderBy(e => Math.Abs(e - valor)).First());
        }
        var dif = v.DifDescarga is { } d && d != 0 ? d : (decimal?)null;
        var diaria = v.ValorDiaria is { } t && t != 0 ? t : (decimal?)null;
        if (dif != null && diaria != null)
            return Math.Abs(dif.Value - valor) <= Math.Abs(diaria.Value - valor) ? ("DIF DESCARGA", dif) : ("diária", diaria);
        if (dif != null) return ("DIF DESCARGA", dif);
        if (diaria != null) return ("diária", diaria);
        return (null, null);
    }

    private static bool MesmaPlaca(string? a, string? bNormalizada) =>
        a != null && bNormalizada != null &&
        string.Equals(ViagemCaminhaoCalculo.NormalizarPlaca(a), bNormalizada, StringComparison.OrdinalIgnoreCase);

    /// <summary>1 dígito trocado (mesmo tamanho) ou número vizinho (±1).</summary>
    public static bool UmNumeroDeDiferenca(string a, string b)
    {
        if (a == b) return false;
        if (long.TryParse(a, out var x) && long.TryParse(b, out var y) && Math.Abs(x - y) == 1) return true;
        return a.Length == b.Length && a.Zip(b).Count(p => p.First != p.Second) == 1;
    }
}
