using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>Um grupo do frete, a cidade dele e os clientes que viram esse grupo.</summary>
public record GrupoSubstituicao(string Nome, string Cidade, string Uf, IReadOnlyList<string> Clientes);

/// <summary>
/// Substituições de destinatário do frete (FRE-03): o cliente da nota que, no
/// cálculo, é cobrado pelo nome do grupo. Moram no banco e são editadas na aba
/// Substituições da tela Viagens; o mapa abaixo é só reserva para quando o banco
/// não responde.
/// </summary>
public class SubstituicoesService(AppDbContext db, ILogger<SubstituicoesService> log)
{
    // Os serviços são por requisição: o cache fica estático e cai a cada gravação.
    // A versão sobe a cada gravação: leitura que começou antes não republica
    // no cache o estado velho.
    private static IReadOnlyList<GrupoSubstituicao>? cache;
    private static int versao;

    public static void Invalidar()
    {
        Interlocked.Increment(ref versao);
        cache = null;
    }

    /// <summary>O mapa que estava no código até 13/11/2026 (o mesmo que semeou o banco).</summary>
    public static readonly IReadOnlyList<GrupoSubstituicao> Padrao =
    [
        new("A. IPANEMA COMERCIAL DE HORTIFRUTIGRANJEIROS LTDA", "ANAPOLIS", "GO",
        [
            "MARIELA R DE C PEREIRA EIRELI", "DISTRIBUIDORA DE ALIMENTOS CAMPESTRE LTDA",
            "MX SUPERMERCADO LTDA", "LX9 EMPREENDIMENTOS LTDA", "F G H COMERCIO LTDA",
        ]),
        new("IRINEU FERRAZ DE PRADO DISTRIBUIDORA PRADO", "ANAPOLIS", "GO",
        [
            "P.Q. R. SANTOS COMERCIO DE ALIMENTOS LTD", "R. DE ALMEIDA LIMA COMERCIO LTDA",
            "BOX FRUIT COMERCIO LTDA", "J. M. DA LUZ ROCHA LTDA",
        ]),
        new("MF AMARAL TRANSPORTE LTDA", "ANAPOLIS", "GO", ["VINHEDO DIST.DE HORTIFRUTIGRANJEIROS LTD"]),
        new("DISTRIBUIDORA DE HORTIGRANJEIROS VALE VERDE LTDA", "GOIANIA", "GO",
        [
            "C.D. COMERCIO DE HORTIFRUTI LTDA", "VERDURAO DUNAS LTDA ME",
        ]),
    ];

    /// <summary>
    /// Grupos do banco. Se o banco falhar, devolve o mapa do código (sem guardar
    /// no cache, para tentar de novo na próxima). Lido com sucesso, o banco é a
    /// única fonte: cliente excluído na tela deixa de ser substituído.
    /// </summary>
    public async Task<IReadOnlyList<GrupoSubstituicao>> GruposAsync()
    {
        var atual = cache;
        if (atual != null) return atual;
        var lida = Volatile.Read(ref versao);
        try
        {
            var grupos = await db.GruposDestinatario.AsNoTracking().Include(g => g.Clientes).OrderBy(g => g.Id).ToListAsync();
            atual = grupos.Select(g => new GrupoSubstituicao(g.Nome, g.Cidade, g.Uf,
                g.Clientes.Select(c => c.Nome).ToList())).ToList();
            if (Volatile.Read(ref versao) == lida) cache = atual;
            return atual;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Substituições do frete: banco não respondeu, usando o mapa do código");
            return Padrao;
        }
    }

    /// <summary>
    /// Monta os dois mapas da conta com a normalização de quem vai usar
    /// (a da NF-e e a do CT-e são diferentes): cliente → grupo e nome → cidade/UF.
    /// A cidade vale também para nota que já vem no nome do próprio grupo.
    /// </summary>
    public static (Dictionary<string, string> clienteParaGrupo, Dictionary<string, (string cidade, string uf)> cidadeDoGrupo)
        Mapas(IEnumerable<GrupoSubstituicao> grupos, Func<string, string> norm)
    {
        var clientes = new Dictionary<string, string>();
        var cidades = new Dictionary<string, (string, string)>();
        foreach (var g in grupos)
        {
            if (g.Cidade.Length > 0 && g.Uf.Length > 0)
                cidades[norm(g.Nome)] = (g.Cidade, g.Uf);
            foreach (var c in g.Clientes)
                clientes[norm(c)] = g.Nome;
        }
        return (clientes, cidades);
    }
}
