using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

public sealed class BarracaoService(AppDbContext db)
{
    public record Planilha(List<ComissaoBarracao> Comissoes, List<DespesaDoBarracao> Despesas, List<string> Avisos);

    public static Planilha LerPlanilha(byte[] bytes)
    {
        var abas = AbaPlanilha.Ler(bytes);
        var com = abas.FirstOrDefault(a => Normalizar(a.Nome).StartsWith("COMISS"));
        var desp = abas.FirstOrDefault(a => Normalizar(a.Nome).StartsWith("DESP"));
        if (com == null || desp == null) throw new InvalidOperationException("Envie a planilha do barracão com as abas COMISSÕES e DESPESAS.");
        var comissoes = new List<ComissaoBarracao>();
        var despesas = new List<DespesaDoBarracao>();
        var avisos = new List<string>();
        static string? Nulo(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        // A venda de produtoB tem a linha das Amarelas e logo abaixo, sem data nem
        // cliente, a das Brancas: o total da 1ª soma as duas (5 + 1 × 18,59 = 111,54)
        ComissaoBarracao? anterior = null;
        for (var r = 3; r <= com.TotalLinhas; r++)
        {
            var data = com.Data(r, 1);
            var cliente = Nulo(com.Texto(r, 2));
            var produto = Nulo(com.Texto(r, 8));
            if (Normalizar(produto) == "PRODUTOSA") produto = "ProdutosA";
            if (data == null && cliente == null)
            {
                if (anterior != null && produto != null && com.N(r, 3) != 0)
                    comissoes.Add(new()
                    {
                        Data = anterior.Data, Cliente = anterior.Cliente, Quantidade = com.N(r, 3), Valor = anterior.Valor,
                        Fornecedor = anterior.Fornecedor, Produto = produto, Ordem = r, Origem = "planilha",
                    });
                continue;
            }
            anterior = null;
            if (data is not { Year: >= 2000 } || cliente == null) continue;
            // De abril em diante a mesma linha traz "ProdutosB" com as amarelas na
            // coluna C e as brancas na D: total = (C + D) × valor
            var brancas = com.N(r, 4);
            anterior = new()
            {
                Data = data.Value, Cliente = cliente, Quantidade = com.N(r, 3), Valor = com.Numero(r, 5),
                Fornecedor = Nulo(com.Texto(r, 7)), Ordem = r, Origem = "planilha",
                Produto = Normalizar(produto) == "PRODUTOSB" ? "Amarelas" : produto,
            };
            if (anterior.Quantidade != 0) comissoes.Add(anterior);
            if (brancas != 0)
                comissoes.Add(new()
                {
                    Data = anterior.Data, Cliente = anterior.Cliente, Quantidade = brancas, Valor = anterior.Valor,
                    Fornecedor = anterior.Fornecedor, Produto = "Brancas", Ordem = r, Origem = "planilha",
                });
        }
        for (var r = 3; r <= desp.TotalLinhas; r++)
        {
            var data = desp.Data(r, 1);
            var descricao = Nulo(desp.Texto(r, 2));
            if (data is not { Year: >= 2000 } || descricao == null) continue;
            despesas.Add(new()
            {
                Data = data.Value, Descricao = descricao, Valor = desp.Numero(r, 4),
                Observacao = Nulo(desp.Texto(r, 5)), Origem = "planilha",
            });
        }
        var semValor = comissoes.Count(c => c.Valor == null) + despesas.Count(d => d.Valor == null);
        if (semValor > 0) avisos.Add($"{semValor} linha(s) sem valor na planilha; confira os campos marcados como falta.");
        if (comissoes.Count + despesas.Count == 0) avisos.Add("A planilha não tem lançamentos.");
        return new(comissoes, despesas, avisos);
    }

    public async Task<object> ImportarAsync(byte[] bytes, bool aplicar)
    {
        var p = LerPlanilha(bytes);
        var resumo = new ImportacaoLancamentos(db, aplicar);
        // Cada grupo compara só com o período que a planilha traz (menor e maior data lidas)
        if (p.Comissoes.Count > 0)
        {
        var ini = p.Comissoes.Min(c => c.Data); var fim = p.Comissoes.Max(c => c.Data);
        resumo.Comparar("Comissões barracão", p.Comissoes,
            await db.ComissoesBarracao.Where(c => c.Data >= ini && c.Data <= fim).ToListAsync(),
            c => $"{c.Data}|{Normalizar(c.Cliente)}|{Normalizar(c.Produto)}|{Normalizar(c.Fornecedor)}",
            c => $"{c.Data:dd/MM/yyyy} · {c.Cliente}",
            c => $"{c.Quantidade:0.####} × {c.Valor:0.####} · {c.Produto} · {c.Fornecedor} · {c.Observacao}",
            c => c.Origem == "planilha", db.ComissoesBarracao);
        }
        if (p.Despesas.Count > 0)
        {
        var ini = p.Despesas.Min(d => d.Data); var fim = p.Despesas.Max(d => d.Data);
        resumo.Comparar("Despesas barracão", p.Despesas,
            await db.DespesasDoBarracao.Where(d => d.Data >= ini && d.Data <= fim).ToListAsync(),
            d => $"{d.Data}|{Normalizar(d.Descricao)}", d => $"{d.Data:dd/MM/yyyy} · {d.Descricao}",
            d => $"{d.Valor:0.####} · {d.Observacao}", d => d.Origem == "planilha", db.DespesasDoBarracao);
        }
        if (aplicar) await db.SaveChangesAsync();
        return resumo.Resultado(p.Avisos);
    }
}
