using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Excluir romaneio mantendo a sequência (Admin, 02/11/2026, NFE-12): o romaneio
/// gerado na carga é da carga; excluir a carga apaga os dela (e as cópias soltas de
/// mesmo nome, para não duplicar). O número que sumiu não fica de buraco: os romaneios
/// seguintes do mesmo cliente descem um número e o contador volta um.
/// </summary>
public static class RomaneiosNumeracao
{
    private static readonly Regex Prefixo = new(@"^(\d+)--", RegexOptions.Compiled);

    /// <summary>Remove os registros (sem salvar) e renumera; devolve "antigo → novo" de cada renomeado.</summary>
    public static async Task<List<string>> ExcluirERenumerarAsync(AppDbContext db, IReadOnlyCollection<PlanilhaCarga> remover)
    {
        var renomeados = new List<string>();
        if (remover.Count == 0) return renomeados;

        var idsRemover = remover.Select(p => p.Id).ToHashSet();
        db.PlanilhasCarga.RemoveRange(remover);

        // Só os .xlsx com número contam para a sequência; o .txt da Girassol segue o boleto
        var numerados = remover
            .Where(p => !p.Nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .Select(p => (p.Nome, ident: RomaneioClienteLeitor.LerNome(p.Nome)))
            .Where(x => x.ident.Numero != null && !string.IsNullOrWhiteSpace(x.ident.Apelido) && x.ident.Apelido != "OUTROS")
            .GroupBy(x => x.Nome.Trim().ToUpperInvariant()).Select(g => g.First())
            .OrderByDescending(x => x.ident.Numero)
            .ToList();
        if (numerados.Count == 0) return renomeados;

        // Os que ficam (sem o conteúdo, que só se renomeia)
        var restantes = await db.PlanilhasCarga
            .Where(p => !idsRemover.Contains(p.Id))
            .ToListAsync();

        foreach (var (nome, ident) in numerados)
        {
            // Outra carga ainda tem esse mesmo romaneio: o número continua em uso
            if (restantes.Any(p => string.Equals(p.Nome.Trim(), nome.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;

            var n = ident.Numero!.Value;
            foreach (var p in restantes)
            {
                if (p.Nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                var outro = RomaneioClienteLeitor.LerNome(p.Nome);
                if (outro.Apelido != ident.Apelido || outro.Numero is not { } num || num <= n) continue;
                var antigo = p.Nome;
                p.Nome = Prefixo.Replace(p.Nome, $"{num - 1}--", 1);
                renomeados.Add($"{antigo} → {p.Nome}");
            }

            var contador = await db.RomaneioContadores.FirstOrDefaultAsync(c => c.Apelido == ident.Apelido);
            if (contador != null && contador.Ultimo >= n) contador.Ultimo--;
        }

        return renomeados.Distinct().ToList();
    }
}
