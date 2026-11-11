using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>Fretes de um cliente no dia, para gravar na viagem (VIA-10).</summary>
public record FretesDoDia(string Cliente, decimal Caixa, decimal ProdutoE, decimal ProdutoD, decimal ProdutoB,
    decimal ProdutoC, PrecoFreteCliente? Frete, EstoqueCliente? Estoque);

/// <summary>
/// As duas metades do cadastro de preços, carregadas juntas: a tabela de frete
/// (clientes de volumes: caixa, produtoE e produtoD) e o cadastro do estoque (frete de
/// produtoB e de produtoC, preços de venda). Ligadas por PrecoFreteCliente.EstoqueClienteId.
/// </summary>
public sealed class TabelaPrecos(List<PrecoFreteCliente> frete, List<EstoqueCliente> estoque)
{
    // Padrões da VIA-10 para cliente que não está na tabela
    public const decimal PadraoCaixa = 9m, PadraoProdutoD = 3m, PadraoProdutoB = 10m, PadraoProdutoC = 10m;

    public List<PrecoFreteCliente> Frete { get; } = frete;
    public List<EstoqueCliente> Estoque { get; } = estoque;

    public PrecoFreteCliente? AcharFrete(string nome) => AcharCliente(nome, Frete);

    public EstoqueCliente? EstoqueDe(PrecoFreteCliente? p) =>
        p?.EstoqueClienteId is { } id ? Estoque.FirstOrDefault(e => e.Id == id) : null;

    /// <summary>Cliente do estoque com o mesmo nome (sem acento e pontuação), só se for um.</summary>
    public EstoqueCliente? AcharEstoquePorNome(string nome)
    {
        var alvo = Normalizar(nome);
        if (alvo.Length == 0) return null;
        var achados = Estoque.Where(e => e.Ativo && Normalizar(e.Nome) == alvo).ToList();
        return achados.Count == 1 ? achados[0] : null;
    }

    /// <summary>Cliente do controle de carregamento → cadastro do estoque (pelo vínculo; sem vínculo, pelo nome).</summary>
    public EstoqueCliente? EstoqueDoCliente(string nome)
    {
        var p = AcharFrete(nome);
        return EstoqueDe(p) ?? AcharEstoquePorNome(p?.Cliente ?? nome) ?? (p == null ? null : AcharEstoquePorNome(nome));
    }

    public FretesDoDia Fretes(string nome)
    {
        var p = AcharFrete(nome);
        var e = EstoqueDe(p) ?? (p == null ? AcharEstoquePorNome(nome) : null);
        return new FretesDoDia(
            p?.Cliente ?? nome.Trim(),
            p?.ValorCaixa ?? PadraoCaixa,
            p?.ProdutoE ?? Math.Round(PadraoCaixa / 2m, 2),
            p?.ValorProdutoD ?? PadraoProdutoD,
            e?.FreteProdutoB ?? PadraoProdutoB,
            e?.FreteDaProdutoC ?? PadraoProdutoC,
            p, e);
    }
}

public record PrecoFreteEntrada(string? Cliente, string? Apelidos, decimal ValorCaixa, decimal? ValorProdutoE, decimal ValorProdutoD, bool? Ativo);
public record PrecoVendaTipo(int TipoProdutoBId, decimal? Preco);
public record PrecoEstoqueEntrada(decimal FreteProdutoB, decimal? FreteProdutoC, decimal? PrecoProdutoAKg, List<PrecoVendaTipo>? Precos);
public record NovoClientePreco(string? Cliente, string? Apelidos, bool LevaVolumes);
public record VinculoEstoque(int? EstoqueClienteId, bool Criar);

/// <summary>
/// Painel de preços por cliente (VIA-10): frete (caixa normal, produtoE, produtoD,
/// produtoB, produtoC) e venda (produtosB, produtoA, produtoC). Não tem cadastro
/// próprio: lê e grava a tabela de frete e o cadastro do estoque.
/// </summary>
public class PrecosClienteService(AppDbContext db)
{
    public async Task<TabelaPrecos> TabelaAsync()
    {
        var frete = await db.PrecosFreteCliente.AsNoTracking().OrderBy(p => p.Ordem).ThenBy(p => p.Cliente).ToListAsync();
        var estoque = await db.EstoqueClientes.AsNoTracking().Include(c => c.PrecosProdutoB).OrderBy(c => c.Nome).ToListAsync();
        return new TabelaPrecos(frete, estoque);
    }

    public async Task<object> PainelAsync()
    {
        var t = await TabelaAsync();
        var tipos = await db.TiposProdutoB.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Id).ToListAsync();
        var ligados = t.Frete.Where(p => p.EstoqueClienteId != null).Select(p => p.EstoqueClienteId!.Value).ToHashSet();

        object Venda(EstoqueCliente e) => new
        {
            produtoAKg = e.PrecoProdutoAKg,
            precos = tipos.Select(x => new
            {
                tipoProdutoBId = x.Id,
                preco = e.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == x.Id) is { Preco: > 0 } pr ? pr.Preco : (decimal?)null,
            }),
        };

        var linhas = new List<object>();
        foreach (var p in t.Frete)
        {
            var e = t.EstoqueDe(p);
            linhas.Add(new
            {
                chave = $"f{p.Id}", freteId = p.Id, estoqueId = e?.Id, cliente = p.Cliente, p.Apelidos, p.Ativo,
                nomeEstoque = e != null && Normalizar(e.Nome) != Normalizar(p.Cliente) ? e.Nome : null,
                caixa = p.ValorCaixa, produtoE = p.ProdutoE, produtoD = p.ValorProdutoD,
                produtoB = e?.FreteProdutoB, produtoC = e?.FreteDaProdutoC,
                venda = e == null ? null : Venda(e),
            });
        }
        foreach (var e in t.Estoque.Where(e => !ligados.Contains(e.Id)))
            linhas.Add(new
            {
                chave = $"e{e.Id}", freteId = (int?)null, estoqueId = (int?)e.Id, cliente = e.Nome, apelidos = (string?)null, e.Ativo,
                nomeEstoque = (string?)null, caixa = (decimal?)null, produtoE = (decimal?)null, produtoD = (decimal?)null,
                produtoB = (decimal?)e.FreteProdutoB, produtoC = (decimal?)e.FreteDaProdutoC, venda = Venda(e),
            });

        return new
        {
            tipos = tipos.Select(x => new { x.Id, x.Nome, x.Codigo, x.Produto, x.UnidadesPorPacote }),
            linhas,
            estoqueLivres = t.Estoque.Where(e => !ligados.Contains(e.Id)).Select(e => new { e.Id, e.Nome, e.Ativo }),
        };
    }

    public async Task SalvarFreteAsync(int id, PrecoFreteEntrada e)
    {
        var p = await db.PrecosFreteCliente.FindAsync(id) ?? throw new ErroViagem("Cliente não encontrado na tabela de frete.");
        if (e.ValorCaixa < 0 || e.ValorProdutoD < 0 || e.ValorProdutoE < 0) throw new ErroViagem("Valor negativo.");
        if (!string.IsNullOrWhiteSpace(e.Cliente)) p.Cliente = e.Cliente.Trim().ToUpperInvariant();
        p.Apelidos = string.IsNullOrWhiteSpace(e.Apelidos) ? null : e.Apelidos.Trim().ToUpperInvariant();
        p.ValorCaixa = e.ValorCaixa;
        p.ValorProdutoE = e.ValorProdutoE ?? Math.Round(e.ValorCaixa / 2m, 2);
        p.ValorProdutoD = e.ValorProdutoD;
        if (e.Ativo is { } ativo) p.Ativo = ativo;
        p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SalvarEstoqueAsync(int id, PrecoEstoqueEntrada e)
    {
        var c = await db.EstoqueClientes.Include(x => x.PrecosProdutoB).FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new ErroViagem("Cliente não encontrado no estoque.");
        if (e.FreteProdutoB < 0 || e.FreteProdutoC < 0 || e.PrecoProdutoAKg < 0 || (e.Precos ?? []).Any(p => p.Preco < 0))
            throw new ErroViagem("Valor negativo.");
        c.FreteProdutoB = e.FreteProdutoB;
        c.FreteProdutoC = e.FreteProdutoC ?? e.FreteProdutoB;
        if (e.PrecoProdutoAKg is { } kg)
        {
            c.PrecoProdutoAKg = kg;
            // As saídas deste cliente que ficaram a R$ 0 passam a ter o preço (as com preço continuam congeladas)
            if (kg > 0 && !EmbaladosService.EhEmbalado(c.Nome))
                await db.VendasProdutoA.Where(v => v.ClienteId == c.Id && v.PrecoKg == 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.PrecoKg, kg).SetProperty(v => v.AtualizadoEm, DateTime.UtcNow));
        }
        foreach (var item in e.Precos ?? [])
        {
            var atual = c.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == item.TipoProdutoBId);
            // Em branco = sem preço (a venda avisa); a linha some
            if (item.Preco is null or 0)
            {
                if (atual != null) db.EstoqueClientePrecos.Remove(atual);
                continue;
            }
            if (atual == null)
                db.EstoqueClientePrecos.Add(new EstoqueClientePreco { ClienteId = c.Id, TipoProdutoBId = item.TipoProdutoBId, Preco = item.Preco.Value });
            else { atual.Preco = item.Preco.Value; atual.AtualizadoEm = DateTime.UtcNow; }
        }
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Liga o cliente de frete a um cliente do estoque (ou cria um com o mesmo nome),
    /// ou solta (null). Um cliente do estoque só pode estar ligado a um de frete.
    /// </summary>
    public async Task<int?> LigarAsync(int freteId, VinculoEstoque v)
    {
        var p = await db.PrecosFreteCliente.FindAsync(freteId) ?? throw new ErroViagem("Cliente não encontrado na tabela de frete.");
        int? alvo = v.EstoqueClienteId;
        if (v.Criar)
        {
            var novo = NovoEstoque(p.Cliente);
            db.EstoqueClientes.Add(novo);
            await db.SaveChangesAsync();
            alvo = novo.Id;
        }
        else if (alvo is { } id)
        {
            if (!await db.EstoqueClientes.AnyAsync(e => e.Id == id)) throw new ErroViagem("Cliente do estoque não encontrado.");
            var outro = await db.PrecosFreteCliente.FirstOrDefaultAsync(x => x.EstoqueClienteId == id && x.Id != freteId);
            if (outro != null) throw new ErroViagem($"Esse cliente do estoque já está ligado a {outro.Cliente}.");
        }
        p.EstoqueClienteId = alvo;
        p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return alvo;
    }

    public async Task<object> CriarClienteAsync(NovoClientePreco e)
    {
        var nome = (e.Cliente ?? "").Trim().ToUpperInvariant();
        if (nome.Length == 0) throw new ErroViagem("Informe o cliente.");
        if (await db.EstoqueClientes.AnyAsync(c => c.Nome.ToUpper() == nome) || await db.PrecosFreteCliente.AnyAsync(p => p.Cliente.ToUpper() == nome))
            throw new ErroViagem("Já existe um cliente com esse nome.");
        var estoque = NovoEstoque(nome);
        db.EstoqueClientes.Add(estoque);
        await db.SaveChangesAsync();
        int? freteId = null;
        if (e.LevaVolumes)
        {
            var p = new PrecoFreteCliente
            {
                Cliente = nome, Apelidos = string.IsNullOrWhiteSpace(e.Apelidos) ? null : e.Apelidos.Trim().ToUpperInvariant(),
                ValorCaixa = TabelaPrecos.PadraoCaixa, ValorProdutoE = Math.Round(TabelaPrecos.PadraoCaixa / 2m, 2),
                ValorProdutoD = TabelaPrecos.PadraoProdutoD, EstoqueClienteId = estoque.Id,
                Ordem = (await db.PrecosFreteCliente.MaxAsync(x => (int?)x.Ordem) ?? 0) + 1,
            };
            db.PrecosFreteCliente.Add(p);
            await db.SaveChangesAsync();
            freteId = p.Id;
        }
        return new { estoqueId = estoque.Id, freteId };
    }

    /// <summary>Cliente novo no estoque com os fretes padrão (VIA-10) e sem preço de venda.</summary>
    public static EstoqueCliente NovoEstoque(string nome) => new()
    {
        Nome = nome.Trim().ToUpperInvariant(), FreteProdutoB = TabelaPrecos.PadraoProdutoB, FreteProdutoC = TabelaPrecos.PadraoProdutoC,
    };
}
