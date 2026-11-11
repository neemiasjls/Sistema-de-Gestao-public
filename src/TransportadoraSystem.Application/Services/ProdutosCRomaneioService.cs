using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// ProdutosC da BRZ a receber pelo romaneio (EST-08, pedido de 11/11/2026): a quantidade e o preço que o Admin digita
/// em Viagens ("foram produtosC da BRZ?") vão para a linha ProdutosC do romaneio da BRZ, guardado em PlanilhasCarga.
/// Cada dia de carga da BRZ vira UMA venda de produtoC (a receber) com o total do romaneio guardado mais recente de cada
/// placa daquele dia; gerar de novo atualiza, nunca duplica. Venda recebida nunca muda. A venda da coluna U do controle
/// do mesmo dia é a mesma produtoC (NFE-05): é adotada (passa a seguir o romaneio), não se cria outra. Romaneio sem
/// produtoC só apaga a venda em aberto que veio do próprio romaneio.
/// </summary>
public class ProdutosCRomaneioService(AppDbContext db, PrecosClienteService precos)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public record LinhaDia(DateOnly Dia, string Romaneios, string? Placas, decimal ProdutosC, decimal Preco, decimal Total,
        string Acao, int? VendaId, DateOnly? RecebidoEm, string? Aviso);

    public record Resultado(List<LinhaDia> Dias, bool Aplicado, List<string> Avisos);

    /// <summary>A venda nasceu (ou foi adotada) do romaneio: CobradaNoRomaneio guarda o nome do arquivo.</summary>
    public static bool EhDoRomaneio(VendaProdutoB v) =>
        v.CobradaNoRomaneio != null && v.CobradaNoRomaneio.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    /// <summary>Depois de gerar, editar ou importar romaneios: sincroniza os dias da BRZ que aparecem nos nomes.</summary>
    public async Task<List<string>> AposGuardarAsync(IEnumerable<string> nomes)
    {
        var avisos = new List<string>();
        foreach (var dia in nomes.Select(ProdutosCBrz.DiaDoRomaneio).OfType<DateOnly>().Distinct())
        {
            var r = await SincronizarDiasAsync([dia], aplicar: true);
            avisos.AddRange(r.Avisos);
        }
        return avisos;
    }

    /// <summary>Histórico: todos os dias com romaneio da BRZ guardado de <paramref name="desde"/> em diante.</summary>
    public async Task<Resultado> PreencherAsync(DateOnly desde, bool aplicar)
    {
        var nomes = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Nome.Contains("BRZ-")).Select(p => p.Nome).ToListAsync();
        var dias = nomes.Select(ProdutosCBrz.DiaDoRomaneio).OfType<DateOnly>().Where(d => d >= desde).Distinct().OrderBy(d => d).ToList();
        return await SincronizarDiasAsync(dias, aplicar);
    }

    private async Task<Resultado> SincronizarDiasAsync(List<DateOnly> dias, bool aplicar)
    {
        var avisos = new List<string>();
        var linhas = new List<LinhaDia>();
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var tipoProdutoC = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoProdutoC);
        if (tipoProdutoC == null) return new([], false, ["Falta o tipo ProdutoC no cadastro de produtosB (Codigo PRODUTOC)."]);
        var tabela = await precos.TabelaAsync();
        var brz = BrzNoEstoque(tabela);
        if (brz == null) return new([], false, ["A BRZ não está ligada a um cliente do estoque no painel de preços: as produtosC não viraram venda."]);
        var precoPainel = brz.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == tipoProdutoC.Id)?.Preco ?? 0;
        var idsProdutoC = tipos.Where(t => ProdutoPacote.Normalizar(t.Produto) == ProdutoPacote.ProdutoC).Select(t => t.Id).ToList();

        foreach (var dia in dias)
        {
            var linha = await DiaAsync(dia, brz, tipoProdutoC, idsProdutoC, precoPainel, aplicar);
            linhas.Add(linha);
            if (linha.Aviso != null) avisos.Add($"BRZ {dia:dd/MM}: {linha.Aviso}");
        }
        if (aplicar) await db.SaveChangesAsync();
        return new(linhas, aplicar, avisos);
    }

    private async Task<LinhaDia> DiaAsync(DateOnly dia, EstoqueCliente brz, TipoProdutoB tipoProdutoC, List<int> idsProdutoC,
        decimal precoPainel, bool aplicar)
    {
        var texto = dia.ToString("dd-MM-yyyy");
        var guardados = (await db.PlanilhasCarga.AsNoTracking().Where(p => p.Nome.Contains(texto)).ToListAsync())
            .Where(p => ProdutosCBrz.RomaneioDoDia(p.Nome, dia)).ToList();

        // O mais recente de cada placa (167 e 168 de 05/10 são o mesmo romaneio gerado duas vezes)
        var lidos = new List<(PlanilhaCarga P, ProdutosCBrz.Linha L)>();
        var semLinha = new List<string>();
        foreach (var p in guardados)
        {
            ProdutosCBrz.Linha? l;
            try { l = ProdutosCBrz.Ler(p.Conteudo); }
            catch (Exception) { semLinha.Add($"{p.Nome} não abre"); continue; }
            if (l == null) { semLinha.Add($"{p.Nome} sem a linha ProdutosC"); continue; }
            lidos.Add((p, l));
        }
        var porPlaca = lidos.GroupBy(x => ViagemCaminhaoCalculo.Placas(x.L.Placa).FirstOrDefault() ?? "")
            .Select(g => g.OrderByDescending(x => x.P.CriadoEm).ThenByDescending(x => x.P.Id).First()).ToList();
        var nomes = string.Join(", ", porPlaca.Select(x => x.P.Nome));
        var placas = string.Join(", ", porPlaca.Select(x => ViagemCaminhaoCalculo.Placas(x.L.Placa).FirstOrDefault()).Where(x => x != null));
        string? aviso = semLinha.Count > 0 ? string.Join("; ", semLinha) + "." : null;
        string Junta(string a) => aviso == null ? a : $"{aviso} {a}";

        var qtd = porPlaca.Sum(x => x.L.Quantidade);
        var precosLidos = porPlaca.Where(x => x.L.Quantidade > 0 && x.L.Preco is > 0).Select(x => x.L.Preco!.Value).Distinct().ToList();
        if (precosLidos.Count > 1) aviso = Junta($"preços diferentes nos romaneios do dia ({string.Join(" e ", precosLidos.Select(Dinheiro))}); ficou o primeiro.");
        var preco = precosLidos.FirstOrDefault();
        if (qtd > 0 && preco <= 0)
        {
            preco = precoPainel;
            aviso = Junta(preco > 0 ? $"o romaneio está sem preço; usei o do painel ({Dinheiro(preco)})." : "o romaneio e o painel estão sem preço de produtoC: a venda ficou sem valor.");
        }
        var total = Math.Round(qtd * preco, 2);

        var existentes = (await db.VendasProdutoB.Include(v => v.Itens)
                .Where(v => v.ClienteId == brz.Id && v.Data == dia).ToListAsync())
            .Where(v => v.Itens.Any(i => idsProdutoC.Contains(i.TipoProdutoBId))).ToList();
        var doRomaneio = existentes.Where(EhDoRomaneio).ToList();
        var outras = existentes.Except(doRomaneio).ToList();

        LinhaDia Linha(string acao, VendaProdutoB? v, string? av = null) =>
            new(dia, nomes, placas.Length > 0 ? placas : null, qtd, preco, total, acao, v?.Id, v?.RecebidoEm, av ?? aviso);

        if (porPlaca.Count == 0) return Linha("sem romaneio legível", null);

        if (qtd <= 0)
        {
            // Sem produtoC: só sai a venda em aberto que veio do próprio romaneio
            var apagar = doRomaneio.Where(v => v.RecebidoEm == null).ToList();
            if (aplicar) db.VendasProdutoB.RemoveRange(apagar);
            if (doRomaneio.Any(v => v.RecebidoEm != null))
                return Linha("recebida, ficou", doRomaneio.First(v => v.RecebidoEm != null), Junta("o romaneio agora está sem produtosC, mas a venda já foi recebida. Confira."));
            if (outras.Count > 0)
                return Linha(apagar.Count > 0 ? "apagada" : "sem produtosC", null,
                    Junta($"o romaneio está sem produtosC, mas o controle de carregamento tem {Qtd(outras.Sum(v => Pacotes(v, idsProdutoC)))}. Confira: é a mesma produtoC."));
            return Linha(apagar.Count > 0 ? "apagada" : "sem produtosC", null);
        }

        var quer = (Pacotes: (int)Math.Floor(qtd), Unidades: (int)Math.Round((qtd - Math.Floor(qtd)) * Math.Max(1, tipoProdutoC.UnidadesPorPacote)));
        var obs = $"Romaneio da BRZ {dia.ToString("dd/MM/yyyy", PtBr)}" + (placas.Length > 0 ? $" {placas}" : "");
        var nomeRomaneio = porPlaca[0].P.Nome.Length > 200 ? porPlaca[0].P.Nome[..200] : porPlaca[0].P.Nome;

        // 1) Já tem a venda do romaneio (ou a da coluna U, que é a mesma produtoC e passa a seguir o romaneio)
        var alvo = doRomaneio.FirstOrDefault() ?? (outras.Count == 1 ? outras[0] : null);
        if (outras.Count > 1 && doRomaneio.Count == 0)
            return Linha("conferir", null, Junta($"há {outras.Count} vendas de produtoC da BRZ neste dia (controle de carregamento); nada mudou. Confira à mão."));
        if (doRomaneio.Count > 1) aviso = Junta($"{doRomaneio.Count} vendas do romaneio no mesmo dia; ficou a primeira.");

        if (alvo != null)
        {
            var item = alvo.Itens.First(i => idsProdutoC.Contains(i.TipoProdutoBId));
            var igual = alvo.Itens.Count(i => idsProdutoC.Contains(i.TipoProdutoBId)) == 1
                        && item.Pacotes == quer.Pacotes && item.Unidades == quer.Unidades && item.Preco == preco;
            if (alvo.RecebidoEm != null)
                return Linha("recebida, ficou", alvo, igual ? aviso : Junta($"a venda já foi recebida em {alvo.RecebidoEm:dd/MM} com outra quantidade ou preço; ficou como estava. Confira."));
            var adotar = !EhDoRomaneio(alvo);
            if (igual && !adotar) return Linha("igual", alvo);
            var antes = Pacotes(alvo, idsProdutoC);
            if (aplicar)
            {
                foreach (var extra in alvo.Itens.Where(i => i != item && idsProdutoC.Contains(i.TipoProdutoBId)).ToList()) alvo.Itens.Remove(extra);
                item.TipoProdutoBId = tipoProdutoC.Id;
                item.Pacotes = quer.Pacotes;
                item.Unidades = quer.Unidades;
                item.Preco = preco;
                item.AtualizadoEm = DateTime.UtcNow;
                alvo.CobradaNoRomaneio = nomeRomaneio;
                alvo.CarregamentoId = null;    // a do controle passa a seguir o romaneio (o controle não a recria)
                alvo.Observacao = obs;
                alvo.AtualizadoEm = DateTime.UtcNow;
            }
            return Linha(adotar ? "a do controle passa a seguir o romaneio" : "atualizada", alvo,
                adotar && antes != qtd ? Junta($"o controle de carregamento tinha {Qtd(antes)} produtosC; vale o romaneio ({Qtd(qtd)}).") : aviso);
        }

        // 2) Nova venda a receber
        if (aplicar)
            db.VendasProdutoB.Add(new VendaProdutoB
            {
                ClienteId = brz.Id, Data = dia, CustoPacote = 0, FretePacote = brz.FreteDaProdutoC, Observacao = obs,
                CobradaNoRomaneio = nomeRomaneio,
                Itens = [new VendaProdutoBItem { TipoProdutoBId = tipoProdutoC.Id, Pacotes = quer.Pacotes, Unidades = quer.Unidades, Preco = preco }],
            });
        return Linha("nova", null);
    }

    /// <summary>Cliente do estoque da BRZ: pelo vínculo do painel de preços; sem vínculo, pelo nome.</summary>
    public static EstoqueCliente? BrzNoEstoque(TabelaPrecos tabela)
    {
        foreach (var f in tabela.Frete.Where(f => ProdutosCBrz.EhBrz(f, f.Cliente)))
            if ((tabela.EstoqueDe(f) ?? tabela.AcharEstoquePorNome(f.Cliente)) is { } e) return e;
        var porNome = tabela.Estoque.Where(e => ProdutosCBrz.EhBrz(null, e.Nome)).ToList();
        return porNome.Count == 1 ? porNome[0] : null;
    }

    private static decimal Pacotes(VendaProdutoB v, List<int> idsProdutoC) =>
        v.Itens.Where(i => idsProdutoC.Contains(i.TipoProdutoBId)).Sum(i => i.Pacotes + i.Unidades / 100m);

    private static string Qtd(decimal v) => v.ToString(v % 1 == 0 ? "N0" : "N1", PtBr);
    private static string Dinheiro(decimal v) => v.ToString("C", PtBr);
}
