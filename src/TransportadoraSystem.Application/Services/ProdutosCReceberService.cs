using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// ProdutosC a receber (EST-14/EST-08), em Boletos → A receber: as vendas de produtoC (sem carga), por cliente com a
/// BRZ primeiro. Valor = pacotes × preço da produtoC do cliente congelado na venda (o frete não entra na cobrança).
/// Marca recebida à mão; a BRZ também pelo Pix do extrato do Sicoob (PixProdutosC): só a combinação única é baixada
/// sozinha, a dúvida fica para escolher, e o EndToEndId gravado impede baixar o mesmo Pix duas vezes.
/// </summary>
public class ProdutosCReceberService(AppDbContext db, PrecosClienteService precos)
{
    public record VendaProdutoC(int Id, DateOnly Data, int ClienteId, string Cliente, int Pacotes, int Unidades, decimal Quantidade,
        decimal Preco, decimal Valor, string Origem, string? Observacao, string? CobradaNoRomaneio, DateOnly? RecebidoEm,
        string? RecebidoPix, bool SemPreco);
    public record GrupoCliente(int ClienteId, string Cliente, bool Brz, decimal AReceber, decimal Recebido, List<VendaProdutoC> Vendas);
    public record PixRelato(string Chave, DateOnly Data, decimal Valor, string Situacao, List<List<VendaProdutoC>> Opcoes);
    public record Resumo(List<GrupoCliente> Grupos, decimal AReceber, decimal AReceberBrz, int SemPreco);
    public record ResultadoPix(List<PixRelato> Pix, int Marcadas, string? Erro);

    /// <summary>Todas as vendas de produtoC, por cliente (BRZ primeiro, depois quem tem mais a receber).</summary>
    public async Task<Resumo> ListarAsync()
    {
        var (vendas, brzIds) = await VendasAsync();
        var grupos = vendas.GroupBy(v => (v.ClienteId, v.Cliente))
            .Select(g => new GrupoCliente(g.Key.ClienteId, g.Key.Cliente, brzIds.Contains(g.Key.ClienteId),
                g.Where(v => v.RecebidoEm == null).Sum(v => v.Valor), g.Where(v => v.RecebidoEm != null).Sum(v => v.Valor),
                g.OrderByDescending(v => v.Data).ThenByDescending(v => v.Id).ToList()))
            .OrderByDescending(g => g.Brz).ThenByDescending(g => g.AReceber).ThenBy(g => g.Cliente).ToList();
        return new(grupos, grupos.Sum(g => g.AReceber), grupos.Where(g => g.Brz).Sum(g => g.AReceber), vendas.Count(v => v.SemPreco));
    }

    private async Task<(List<VendaProdutoC> Vendas, HashSet<int> BrzIds)> VendasAsync()
    {
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var idsProdutoC = tipos.Where(t => ProdutoPacote.Normalizar(t.Produto) == ProdutoPacote.ProdutoC).Select(t => t.Id).ToList();
        var lidas = await db.VendasProdutoB.AsNoTracking().Include(v => v.Itens).Include(v => v.Cliente)
            .Where(v => v.Itens.Any(i => idsProdutoC.Contains(i.TipoProdutoBId))).ToListAsync();
        var tabela = await precos.TabelaAsync();
        var brzIds = new HashSet<int>();
        foreach (var c in lidas.Select(v => v.Cliente).Where(c => c != null).DistinctBy(c => c!.Id))
            if (ProdutosCBrz.EhBrz(tabela.Frete.FirstOrDefault(f => f.EstoqueClienteId == c!.Id), c!.Nome)) brzIds.Add(c.Id);

        var lista = lidas.Select(v =>
        {
            var itens = v.Itens.Where(i => idsProdutoC.Contains(i.TipoProdutoBId)).ToList();
            var qtd = itens.Sum(i => EstoqueVendasService.Equivalente(i, tipos));
            var preco = itens.FirstOrDefault()?.Preco ?? 0;
            return new VendaProdutoC(v.Id, v.Data, v.ClienteId, v.Cliente?.Nome ?? "", itens.Sum(i => i.Pacotes), itens.Sum(i => i.Unidades),
                qtd, preco, itens.Sum(i => EstoqueVendasService.Equivalente(i, tipos) * i.Preco),
                ProdutosCRomaneioService.EhDoRomaneio(v) ? "romaneio" : v.CarregamentoId != null ? "controle" : "mao", v.Observacao, v.CobradaNoRomaneio, v.RecebidoEm, v.RecebidoPix,
                itens.Any(i => i.Preco <= 0));
        }).ToList();
        return (lista, brzIds);
    }

    /// <summary>Marca (data) ou desmarca (null) o recebimento à mão. Desmarcar apaga também a ligação com o Pix.</summary>
    public async Task MarcarAsync(int id, DateOnly? recebidoEm)
    {
        var (vendas, _) = await VendasAsync();
        if (vendas.All(v => v.Id != id)) throw new ErroEstoque("Venda de produtosC não encontrada.");
        var venda = await db.VendasProdutoB.FirstAsync(v => v.Id == id);
        venda.RecebidoEm = recebidoEm;
        if (recebidoEm == null) venda.RecebidoPix = null;
        venda.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Pix do extrato × vendas da BRZ em aberto. aplicar = true baixa sozinho a combinação única (conferência
    /// automática e o botão da tela); a dúvida e o "sem produtosC que somem" só voltam para a tela.
    /// </summary>
    public async Task<ResultadoPix> ConferirPixAsync(IEnumerable<PixBoletos.Pix> pixes, bool aplicar, CancellationToken ct = default)
    {
        var (vendas, brzIds) = await VendasAsync();
        var brz = vendas.Where(v => brzIds.Contains(v.ClienteId)).ToList();
        var abertas = brz.Where(v => v.RecebidoEm == null && v.Valor > 0).Select(v => new PixProdutosC.Venda(v.Id, v.Data, v.Valor)).ToList();
        var chaves = brz.Where(v => v.RecebidoPix != null).Select(v => v.RecebidoPix!).ToHashSet();
        var achados = PixProdutosC.Achar(pixes, abertas, chaves);
        var porId = brz.ToDictionary(v => v.Id);
        var marcadas = 0;
        var relatos = new List<PixRelato>();
        foreach (var a in achados)
        {
            var opcoes = a.Opcoes.Select(o => o.Select(v => porId[v.Id]).ToList()).ToList();
            string situacao;
            if (a.JaBaixado) situacao = "ja-baixado";
            else if (a.Certo && aplicar)
            {
                marcadas += await BaixarAsync(a.Opcoes[0].Select(v => v.Id), a.Pix.Data, a.Chave, ct);
                situacao = "baixado";
            }
            else situacao = a.Certo ? "certo" : a.Duvida ? "duvida" : "sem-casamento";
            relatos.Add(new(a.Chave, a.Pix.Data, a.Pix.Valor, situacao, opcoes));
        }
        return new(relatos.OrderByDescending(r => r.Data).ToList(), marcadas, null);
    }

    /// <summary>Baixa escolhida na tela (dúvida): as vendas recebem a data e a chave do Pix.</summary>
    public async Task<int> BaixarAsync(IEnumerable<int> vendaIds, DateOnly data, string chave, CancellationToken ct = default)
    {
        var ids = vendaIds.ToList();
        if (await db.VendasProdutoB.AnyAsync(v => v.RecebidoPix == chave, ct))
            throw new ErroEstoque("Este Pix já deu baixa em produtosC.");
        var vendas = await db.VendasProdutoB.Where(v => ids.Contains(v.Id) && v.RecebidoEm == null).ToListAsync(ct);
        if (vendas.Count != ids.Count) throw new ErroEstoque("Alguma dessas vendas já está recebida ou não existe.");
        foreach (var v in vendas)
        {
            v.RecebidoEm = data;
            v.RecebidoPix = chave;
            v.AtualizadoEm = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return vendas.Count;
    }
}
