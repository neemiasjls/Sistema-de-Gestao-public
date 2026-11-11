using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// VIA-27: liga os retornos lançados à mão aos contratos da Strada com PDF (regra pura em
/// RetornoContratoLigador) e grava dos dois lados. Rodar de novo não muda nada; não apaga nada.
/// Linha de contrato com viagem da base AGROTRADE (ViagemId) ou ligada à mão só ganha o RetornoId.
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
            else if (c.RetornoId != null)
            {
                c.RetornoId = null;
                if (livre) { c.Tipo = "indefinido"; c.Situacao = StradaService.SemViagem; }
            }
            else continue;
            c.AtualizadoEm = agora;
            mudou++;
        }
        if (mudou > 0) await db.SaveChangesAsync(ct);
        return mudou;
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
