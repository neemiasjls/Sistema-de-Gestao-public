using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Dono de cada placa, lido do cadastro de veículos (Veiculos.Socio). É o único
/// lugar que diz de quem é o caminhão (ABA-06): a divisão do abastecimento e a
/// busca leem daqui, sem lista repetida no código. Placa comparada sem traço e em
/// maiúscula ("abc-1d23" = "ABC1D23").
/// </summary>
public sealed class DonosDasPlacas
{
    // Nomes das seções da planilha de divisão (o Dudu aparece como Davi)
    public const string Bento = "BENTO", Caio = "CAIO", Davi = "DAVI";

    private readonly Dictionary<string, Socio> socios = new();

    public DonosDasPlacas(IEnumerable<(string Placa, Socio Socio)> veiculos)
    {
        foreach (var (placa, socio) in veiculos)
        {
            var chave = Chave(placa);
            if (chave != "") socios.TryAdd(chave, socio);
        }
    }

    /// <summary>Todo o cadastro, ativos e fora da frota: o dono não muda por estar parado.</summary>
    public static async Task<DonosDasPlacas> CarregarAsync(AppDbContext db, CancellationToken ct = default)
    {
        var veiculos = await db.Veiculos.AsNoTracking()
            .OrderBy(v => v.Id)
            .Select(v => new { v.Placa, v.Socio })
            .ToListAsync(ct);
        return new DonosDasPlacas(veiculos.Select(v => (v.Placa, v.Socio)));
    }

    public static string Chave(string? placa) =>
        Regex.Replace(placa ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant();

    public Socio? SocioDe(string? placa) =>
        socios.TryGetValue(Chave(placa), out var s) ? s : null;

    /// <summary>Seção da planilha (BENTO, CAIO, DAVI) ou null se a placa não está no cadastro.</summary>
    public string? Dono(string? placa) => SocioDe(placa) is { } s ? Secao(s) : null;

    public bool Tem(string? placa) => socios.ContainsKey(Chave(placa));

    public static string Secao(Socio s) => s switch
    {
        Core.Enums.Socio.Bento => Bento,
        Core.Enums.Socio.Caio => Caio,
        Core.Enums.Socio.Dudu => Davi,
        _ => s.ToString().ToUpperInvariant(),
    };

    /// <summary>Nome para a tela: Bento, Caio, Davi.</summary>
    public static string Nome(Socio s) => s switch
    {
        Core.Enums.Socio.Bento => "Bento",
        Core.Enums.Socio.Caio => "Caio",
        Core.Enums.Socio.Dudu => "Davi",
        _ => s.ToString(),
    };
}

/// <summary>Placa da fatura que não está no cadastro de veículos: a tela pede o dono.</summary>
public record PlacaSemCadastro(string Placa, int Abastecimentos, double Valor, List<string> Motoristas);

/// <summary>
/// A planilha não sai com placa sem dono (nenhuma placa fica de fora, ABA-06):
/// quem chama devolve a lista para a tela perguntar o dono de cada uma.
/// </summary>
public class PlacasSemCadastroException(List<PlacaSemCadastro> placas)
    : Exception($"Placas sem cadastro: {string.Join(", ", placas.Select(p => p.Placa))}.")
{
    public List<PlacaSemCadastro> Placas { get; } = placas;
}
