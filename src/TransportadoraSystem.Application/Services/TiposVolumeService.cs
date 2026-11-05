using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Os tipos de volumes e as escritas de cada um nas notas. A tabela nasce vazia e
/// é semeada com o mapa que já estava no código, para a leitura das notas
/// continuar igual no primeiro dia.
/// </summary>
public class TiposVolumeService(AppDbContext db)
{
    public async Task SemearAsync()
    {
        if (await db.TiposVolume.AnyAsync()) return;

        int ordem = 0;
        foreach (var grupo in NFeImportService.EscritasPadrao.GroupBy(e => e.Tipo))
        {
            db.TiposVolume.Add(new TipoVolume
            {
                Nome = grupo.Key,
                Cor = NFeImportService.CorDoTipo(grupo.Key) ?? "OUTRO",
                Ordem = ++ordem,
                Escritas = [.. grupo.Select(e => new EscritaVolume { Texto = e.Codigo })],
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Texto normalizado → nome do tipo, do jeito que a leitura da nota usa.</summary>
    public async Task<IReadOnlyDictionary<string, string>> EscritasAsync()
    {
        await SemearAsync();
        var lista = await db.EscritasVolume.Include(e => e.TipoVolume).ToListAsync();
        var mapa = new Dictionary<string, string>();
        foreach (var e in lista)
            mapa[NFeImportService.NormEscrita(e.Texto)] = e.TipoVolume!.Nome;
        return mapa;
    }
}
