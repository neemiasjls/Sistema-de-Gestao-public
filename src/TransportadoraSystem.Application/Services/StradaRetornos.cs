using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-27: liga os retornos lançados à mão aos contratos da Strada com PDF (regra pura em
/// RetornoContratoLigador) e grava dos dois lados. Rodar de novo não muda nada; não apaga nada.
/// Linha de contrato com viagem da base AGROTRADE (ViagemId) ou ligada à mão só ganha o RetornoId.
/// Parte 6: o contrato de reembolso de descarga liga ao retorno pela NF do contrato de frete dele, ou à mão.
/// </summary>
public static class StradaRetornos
{
    /// <summary>Devolve quantas linhas (retornos + contratos) mudaram.</summary>
    public static async Task<int> SincronizarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var contratos = await db.StradaContratos.ToListAsync(ct);
        var mudou = await PreencherDatasAsync(db, contratos, ct);

        var comPdf = contratos.Where(c => c.ValorContratoPdf != null).GroupBy(c => c.Contrato)
            .Select(g => g.OrderByDescending(c => c.DataContrato.HasValue).First())
            .Select(c => new RetornoContratoLigador.Contrato(c.Contrato, c.Placa, c.DataContrato, c.ValorContratoPdf!.Value, c.Nf))
            .ToList();

        // Só os retornos do período dos contratos (os antigos ficam sem aviso) e os que já têm ligação
        var datas = comPdf.Where(c => c.DataContrato != null).Select(c => c.DataContrato!.Value).ToList();
        var temContrato = datas.Count > 0;
        var inicio = temContrato ? datas.Min().AddDays(-RetornoContratoLigador.JanelaDias) : DateOnly.MaxValue;
        var retornos = await db.RetornosFrete
            .Where(r => r.Contrato != null || r.ContratoLigacao != null || (temContrato && r.Data >= inicio))
            .ToListAsync(ct);
        var resultados = RetornoContratoLigador.Ligar(
            retornos.Select(r => new RetornoContratoLigador.Retorno(r.Id, r.Placa, r.Data, r.ValorFrete, r.Contrato,
                r.ContratoLigacao, r.Transportadora)),
            comPdf).ToDictionary(x => x.RetornoId);

        var agora = DateTime.UtcNow;
        foreach (var r in retornos)
        {
            var x = resultados[r.Id];
            if (r.Contrato == x.Contrato && r.ValorContrato == x.ValorContrato && r.ContratoLigacao == x.Situacao && r.ContratoAviso == x.Aviso)
                continue;
            r.Contrato = x.Contrato; r.ValorContrato = x.ValorContrato; r.ContratoLigacao = x.Situacao; r.ContratoAviso = x.Aviso;
            r.AtualizadoEm = agora;
            mudou++;
        }

        // Contrato → retorno (só quem ficou com um contrato: ligado ou manual)
        var retornoDoContrato = retornos.Where(r => r.Contrato != null && r.ContratoLigacao is RetornoContratoLigador.Ligado or RetornoContratoLigador.Manual)
            .GroupBy(r => r.Contrato!).ToDictionary(g => g.Key, g => g.First());
        var socios = await StradaService.SociosPorPlacaAsync(db, ct);

        // Reembolso de descarga (Parte 6): contrato com PDF cuja NF é a do contrato de frete de um retorno.
        // Nunca pelo valor. Ligado à mão (manual, sem viagem da base) fica no retorno escolhido.
        var retornoPorNf = new Dictionary<string, RetornoFrete>();
        foreach (var c in contratos.Where(c => retornoDoContrato.ContainsKey(c.Contrato)))
            foreach (var nf in Nfs(c.Nf))
                retornoPorNf.TryAdd(nf, retornoDoContrato[c.Contrato]);
        var porId = retornos.ToDictionary(r => r.Id);
        var faltam = contratos.Where(c => c.RetornoId is int id && !porId.ContainsKey(id)).Select(c => c.RetornoId!.Value).Distinct().ToList();
        if (faltam.Count > 0)
            foreach (var r in await db.RetornosFrete.Where(r => faltam.Contains(r.Id)).ToListAsync(ct)) porId[r.Id] = r;

        foreach (var c in contratos)
        {
            var livre = c.ViagemId == null && c.Situacao != StradaService.Manual;
            if (retornoDoContrato.TryGetValue(c.Contrato, out var r))
            {
                var socio = StradaService.SocioDe(c.Placa ?? r.Placa, socios);
                if (c.RetornoId == r.Id && (!livre || (c.Tipo == "frete" && c.Situacao == StradaService.Ligado && c.Socio == socio)))
                    continue;
                c.RetornoId = r.Id;
                if (livre) { c.Tipo = "frete"; c.Situacao = StradaService.Ligado; c.Socio = socio; }
            }
            else if (ReembolsoDe(c, porId, retornoPorNf) is RetornoFrete rr)
            {
                if (!LigarReembolso(c, rr, socios)) continue;
            }
            else if (c.RetornoId != null)
            {
                c.RetornoId = null;
                if (livre)
                {
                    if (c.Tipo == "frete") c.Tipo = "indefinido";
                    c.Situacao = StradaService.SemViagem; c.ValorEsperado = null; c.Diferenca = null;
                }
            }
            else continue;
            c.AtualizadoEm = agora;
            mudou++;
        }
        if (mudou > 0) await db.SaveChangesAsync(ct);
        return mudou;
    }

    /// <summary>
    /// Dica para o reembolso sem viagem: retornos da mesma placa, até a data do contrato, com reembolso esperado
    /// e nenhum reembolso ligado ainda. Só mostra; quem liga é a pessoa (nunca pelo valor).
    /// </summary>
    public static List<RetornoFrete> Dicas(StradaContrato c, IEnumerable<RetornoFrete> retornos, ISet<int> comReembolso)
    {
        if (string.IsNullOrWhiteSpace(c.Placa)) return [];
        var placa = ViagemCaminhaoCalculo.NormalizarPlaca(c.Placa);
        var ate = c.DataContrato ?? DateOnly.FromDateTime(DateTime.Today);
        return retornos.Where(r => r.ReembolsoDescarga > 0 && !comReembolso.Contains(r.Id) && r.Data <= ate
                && ViagemCaminhaoCalculo.NormalizarPlaca(r.Placa) == placa)
            .OrderByDescending(r => r.Data).Take(5).ToList();
    }

    /// <summary>NFs do contrato (lista com vírgula), sem zeros à esquerda.</summary>
    internal static IEnumerable<string> Nfs(string? nf) =>
        (nf ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.TrimStart('0')).Where(n => n.Length > 0);

    /// <summary>Linha ligada à mão a um retorno: manual, reembolso, sem viagem da base AGROTRADE.</summary>
    internal static bool ReembolsoManual(StradaContrato c) =>
        c.Situacao == StradaService.Manual && c.Tipo == "reembolso" && c.ViagemId == null && c.RetornoId != null;

    private static RetornoFrete? ReembolsoDe(StradaContrato c, Dictionary<int, RetornoFrete> porId, Dictionary<string, RetornoFrete> porNf)
    {
        if (ReembolsoManual(c)) return porId.GetValueOrDefault(c.RetornoId!.Value);
        if (c.ValorContratoPdf == null) return null;
        foreach (var nf in Nfs(c.Nf))
            if (porNf.TryGetValue(nf, out var r)) return r;
        return null;
    }

    /// <summary>Grava o reembolso esperado (descarga − adiantamento) e a diferença. Devolve se mudou algo.</summary>
    private static bool LigarReembolso(StradaContrato c, RetornoFrete r, Dictionary<string, string> socios)
    {
        // Ligado à mão a uma viagem da base AGROTRADE: só ganha o RetornoId
        if (c.Situacao == StradaService.Manual && c.ViagemId != null)
        {
            if (c.RetornoId == r.Id) return false;
            c.RetornoId = r.Id;
            return true;
        }
        var esperado = r.ReembolsoDescarga;
        var diferenca = esperado is decimal e ? Math.Round(c.Valor - e, 2) : (decimal?)null;
        var situacao = ReembolsoManual(c) ? StradaService.Manual : StradaService.Ligado;
        var socio = StradaService.SocioDe(c.Placa ?? r.Placa, socios);
        if (c.RetornoId == r.Id && c.Tipo == "reembolso" && c.ValorEsperado == esperado && c.Diferenca == diferenca
            && c.Situacao == situacao && c.Socio == socio && c.NfSugerida == null)
            return false;
        c.RetornoId = r.Id; c.Tipo = "reembolso"; c.ValorEsperado = esperado; c.Diferenca = diferenca;
        c.Situacao = situacao; c.Socio = socio; c.NfSugerida = null;
        return true;
    }

    /// <summary>Contrato com PDF gravado antes de existir a coluna: relê o PDF guardado e preenche a data.</summary>
    private static async Task<int> PreencherDatasAsync(AppDbContext db, List<StradaContrato> contratos, CancellationToken ct)
    {
        var faltam = contratos.Where(c => c.ValorContratoPdf != null && c.DataContrato == null).Select(c => c.Contrato).Distinct().ToList();
        if (faltam.Count == 0) return 0;
        var arquivos = await db.StradaArquivos.AsNoTracking()
            .Where(a => a.Tipo == "pdf" && a.Referencia != null && faltam.Contains(a.Referencia))
            .OrderByDescending(a => a.Id).ToListAsync(ct);
        var mudou = 0;
        foreach (var num in faltam)
        {
            var data = arquivos.Where(a => a.Referencia == num)
                .Select(a => StradaContratoPdfLeitor.Ler(a.Conteudo)).FirstOrDefault(l => l.Aceito && l.DataContrato != null)?.DataContrato;
            if (data == null) continue;
            foreach (var c in contratos.Where(c => c.Contrato == num && c.ValorContratoPdf != null && c.DataContrato == null))
            {
                c.DataContrato = data;
                mudou++;
            }
        }
        return mudou;
    }
}
