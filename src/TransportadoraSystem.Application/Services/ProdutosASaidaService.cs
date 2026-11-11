using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Regra da saída de produtosA (VendaProdutoA), a mesma para a tela de ProdutosA e para o portal do
/// celular (EST-09): preço do cliente congelado na venda (embalado sai pelo custo, VOLUME-20), carga
/// que encerra ao sair a última produtoA e despesas do embalado sincronizadas.
/// Não abre transação: quem chama decide (o portal grava a saída e o histórico juntos).
/// </summary>
public class ProdutosASaidaService(AppDbContext db, EmbaladosService embalados)
{
    /// <summary>Preço por kg que a venda congela: o do cliente, ou o custo da carga se for o embalado (VOLUME-20).</summary>
    public static decimal PrecoDaSaida(EstoqueCliente cliente, CargaProdutoA carga) =>
        EmbaladosService.EhEmbalado(cliente.Nome) ? carga.CustoKg : cliente.PrecoProdutoAKg;

    /// <summary>Cria a saída. Devolve a venda e se a carga acabou com ela.</summary>
    public async Task<(VendaProdutoA Venda, bool CargaEncerrada)> CriarAsync(
        CargaProdutoA carga, EstoqueCliente cliente, DateOnly data, int? espessura, string? observacao,
        IReadOnlyList<int> pesos, int? anotadaPorUsuarioId = null)
    {
        var venda = new VendaProdutoA
        {
            CargaId = carga.Id,
            ClienteId = cliente.Id,
            Data = data,
            Espessura = espessura,
            PrecoKg = PrecoDaSaida(cliente, carga),
            CustoKg = carga.CustoKg,
            Observacao = observacao,
            AnotadaPorUsuarioId = anotadaPorUsuarioId,
            AnotadaEm = anotadaPorUsuarioId == null ? null : DateTime.UtcNow,
            ProdutosA = pesos.Select(p => new ProdutoAVendida { PesoGramas = p }).ToList(),
        };
        db.VendasProdutoA.Add(venda);
        await db.SaveChangesAsync();
        if (EmbaladosService.EhEmbalado(cliente.Nome)) await embalados.SincronizarProdutosAEmbaladoAsync();

        // Saiu a última produtoA: a carga acaba e a próxima venda vai para a nova.
        int vendidas = await ContarVendidasAsync(carga.Id);
        bool acabou = vendidas >= carga.Quantidade;
        if (acabou && !carga.Encerrada)
        {
            carga.Encerrada = true;
            carga.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return (venda, acabou);
    }

    /// <summary>
    /// Altera data, cliente e pesos de uma saída (portal). Venda carregada com ProdutosA, Cliente e Carga.
    /// Trocou o cliente: o preço é o do cliente novo. A carga encerra se chegou ao fim e reabre se
    /// estava encerrada pela contagem e deixou de estar cheia.
    /// </summary>
    public async Task AlterarAsync(VendaProdutoA venda, DateOnly data, EstoqueCliente cliente, IReadOnlyList<int> pesos)
    {
        var carga = venda.Carga ?? await db.CargasProdutoA.FirstAsync(c => c.Id == venda.CargaId);
        int vendidasAntes = await ContarVendidasAsync(carga.Id);
        bool eraEmbalado = EmbaladosService.EhEmbalado(venda.Cliente?.Nome);

        venda.Data = data;
        if (venda.ClienteId != cliente.Id)
        {
            venda.ClienteId = cliente.Id;
            venda.Cliente = cliente;
            venda.PrecoKg = PrecoDaSaida(cliente, carga);
        }
        var atuais = venda.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();
        if (!atuais.SequenceEqual(pesos))
        {
            db.ProdutosAVendidas.RemoveRange(venda.ProdutosA);
            venda.ProdutosA = pesos.Select(p => new ProdutoAVendida { VendaId = venda.Id, PesoGramas = p }).ToList();
        }
        venda.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (eraEmbalado || EmbaladosService.EhEmbalado(cliente.Nome)) await embalados.SincronizarProdutosAEmbaladoAsync();

        int vendidas = await ContarVendidasAsync(carga.Id);
        if (vendidas >= carga.Quantidade && !carga.Encerrada)
        {
            carga.Encerrada = true;
            carga.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        else if (carga.Encerrada && vendidasAntes >= carga.Quantidade && vendidas < carga.Quantidade)
        {
            carga.Encerrada = false;
            carga.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Exclui a saída (as produtosA voltam ao estoque da carga) e acerta as despesas do embalado.</summary>
    public async Task ExcluirAsync(VendaProdutoA venda)
    {
        db.VendasProdutoA.Remove(venda);
        await db.SaveChangesAsync();
        await embalados.SincronizarProdutosAEmbaladoAsync();
    }

    private Task<int> ContarVendidasAsync(int cargaId) =>
        db.ProdutosAVendidas.CountAsync(b => b.Venda!.CargaId == cargaId);
}
