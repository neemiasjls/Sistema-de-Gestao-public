using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>Compara uma planilha com o histórico, mantendo IDs e lançamentos manuais.</summary>
public sealed class ImportacaoLancamentos(AppDbContext db, bool aplicar)
{
    public record Linha(string Grupo, string Descricao, string? Antes, string? Depois);
    public List<Linha> Novas { get; } = [];
    public List<Linha> Mudadas { get; } = [];
    public List<Linha> Mantidas { get; } = [];
    public List<Linha> SoNoSite { get; } = [];
    public int Iguais { get; private set; }

    public void Comparar<T>(string grupo, List<T> lidas, List<T> existentes,
        Func<T, string> chave, Func<T, string> descricao, Func<T, string> texto,
        Func<T, bool> daPlanilha, DbSet<T> tabela) where T : BaseEntity
    {
        var livres = existentes.ToList();
        foreach (var nova in lidas)
        {
            var candidatas = livres.Where(x => chave(x) == chave(nova)).ToList();
            // Prioriza uma linha idêntica quando há mais de uma venda no mesmo dia.
            var anterior = candidatas.FirstOrDefault(x => texto(x) == texto(nova)) ?? candidatas.FirstOrDefault();
            if (anterior == null)
            {
                Novas.Add(new(grupo, descricao(nova), null, texto(nova)));
                if (aplicar) tabela.Add(nova);
                continue;
            }
            livres.Remove(anterior);
            if (!daPlanilha(anterior))
            {
                Mantidas.Add(new(grupo, descricao(nova), texto(anterior), texto(nova)));
                continue;
            }
            if (texto(anterior) == texto(nova)) { Iguais++; continue; }
            Mudadas.Add(new(grupo, descricao(nova), texto(anterior), texto(nova)));
            if (!aplicar) continue;
            var destino = db.Entry(anterior);
            var fonte = db.Entry(nova);
            foreach (var propriedade in destino.Metadata.GetProperties())
            {
                if (propriedade.IsPrimaryKey() || propriedade.Name is nameof(BaseEntity.CriadoEm) or nameof(BaseEntity.AtualizadoEm)) continue;
                destino.Property(propriedade.Name).CurrentValue = fonte.Property(propriedade.Name).CurrentValue;
            }
            anterior.AtualizadoEm = DateTime.UtcNow;
        }
        foreach (var extra in livres)
            SoNoSite.Add(new(grupo, descricao(extra), texto(extra), null));
    }

    public object Resultado(IEnumerable<string> avisos) => new
    {
        novas = Novas, mudadas = Mudadas, mantidas = Mantidas, soNoSite = SoNoSite,
        iguais = Iguais, avisos,
    };
}
