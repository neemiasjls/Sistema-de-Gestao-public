using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

public class ErroEstoque(string mensagem) : Exception(mensagem);

public record ItemVendaNova(int TipoProdutoBId, int Pacotes, int Unidades);
public record VendaNova(int CargaId, int ClienteId, DateOnly Data, List<ItemVendaNova>? Itens, string? Observacao);
public record VendaCriada(int Id, bool CargaEncerrada);

/// <summary>Pacotes que um cliente levou no controle de carregamento.</summary>
public record SaidaDoCarregamento(string Cliente, decimal Amarelas, decimal Brancas, decimal ProdutosC);

/// <summary>
/// Saídas de produtosB e de produtosC (EST-01, EST-02, EST-07). Três origens:
/// lançada à mão na tela; o controle de carregamento (amarela, branca e produtoC por
/// cliente, VIA-09); e o uso nos volumes embalados (amarelas = Abe × 30, brancas =
/// Ave × 30 unidades, VOLUME-04), na conta do cliente EMBALADOS. A saída copia o
/// custo da carga, o frete e o preço do cliente no dia (congelados) e a carga fecha
/// sozinha quando acaba.
/// </summary>
public class EstoqueVendasService(AppDbContext db, PrecosClienteService precos)
{
    /// <summary>Cliente do estoque que recebe o uso nos volumes embalados (as saídas à mão eram lançadas nele).</summary>
    public const string ClienteEmbalados = "EMBALADOS";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // ───────────────────────── Saída lançada à mão ─────────────────────────

    public async Task<VendaCriada> CriarVendaAsync(VendaNova dto)
    {
        var carga = await db.CargasProdutoB.FindAsync(dto.CargaId) ?? throw new ErroEstoque("Carga não encontrada.");
        var cliente = await db.EstoqueClientes.Include(c => c.PrecosProdutoB).FirstOrDefaultAsync(c => c.Id == dto.ClienteId)
            ?? throw new ErroEstoque("Cliente não encontrado.");
        var itens = (dto.Itens ?? []).Where(i => i.Pacotes > 0 || i.Unidades > 0).ToList();
        if (itens.Count == 0) throw new ErroEstoque("Informe a quantidade de pelo menos um tipo.");

        var venda = NovaVenda(carga, cliente, dto.Data, dto.Observacao, itens.Select(i => (i.TipoProdutoBId, i.Pacotes, i.Unidades)));
        db.VendasProdutoB.Add(venda);
        await db.SaveChangesAsync();
        var acabou = await FecharSeAcabouAsync(carga.Id);
        return new VendaCriada(venda.Id, acabou);
    }

    // ───────────────────────── Controle de carregamento ─────────────────────────

    /// <summary>
    /// Deixa as saídas do carregamento iguais ao controle: uma por cliente e carga
    /// (produtosB numa, produtosC noutra). Saída igual fica como está; diferente é
    /// trocada, menos a que já tem romaneio (avisa). Pode rodar de novo sem duplicar.
    /// </summary>
    public async Task<List<string>> SincronizarCarregamentoAsync(CarregamentoVolumes c, IReadOnlyList<SaidaDoCarregamento> saidas)
    {
        var avisos = new List<string>();
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var amarela = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoAmarela);
        var branca = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoBranca);
        var produtoC = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoProdutoC);
        var tabela = await precos.TabelaAsync();
        var existentes = await db.VendasProdutoB.Include(v => v.Itens).Include(v => v.Carga)
            .Where(v => v.CarregamentoId == c.Id).ToListAsync();
        var usadas = new HashSet<int>();
        var cargasMexidas = new HashSet<int>();
        var obs = $"Controle de carregamento {c.Placa} {c.Data.ToString("dd/MM/yyyy", PtBr)}";

        foreach (var s in saidas.Where(s => s.Amarelas > 0 || s.Brancas > 0 || s.ProdutosC > 0))
        {
            var cliente = await EstoqueDoClienteAsync(s.Cliente, tabela, avisos);
            if (cliente == null) continue;
            var brz = ProdutosCBrz.EhBrz(tabela.AcharFrete(s.Cliente), s.Cliente);

            var grupos = new List<(string Produto, List<(TipoProdutoB Tipo, decimal Qtd)> Itens)>
            {
                (ProdutoPacote.ProdutoB, new() { (amarela!, s.Amarelas), (branca!, s.Brancas) }),
                (ProdutoPacote.ProdutoC, new() { (produtoC!, s.ProdutosC) }),
            };
            foreach (var (produto, lista) in grupos)
            {
                var itens = lista.Where(i => i.Qtd > 0).ToList();
                if (itens.Count == 0) continue;
                if (itens.Any(i => i.Tipo == null))
                {
                    avisos.Add($"{s.Cliente}: falta o tipo de {(produto == ProdutoPacote.ProdutoC ? "produtoC" : "produtoB")} no cadastro (Codigo). A saída não foi lançada.");
                    continue;
                }
                var nome = produto == ProdutoPacote.ProdutoC ? "produtosC" : "produtosB";
                var quer = itens.Select(i => (i.Tipo.Id, Pacotes: (int)Math.Floor(i.Qtd),
                    Unidades: (int)Math.Round((i.Qtd - Math.Floor(i.Qtd)) * Math.Max(1, i.Tipo.UnidadesPorPacote)))).ToList();

                // Já existe a saída deste cliente e produto para este carregamento?
                var atual = existentes.FirstOrDefault(v => !usadas.Contains(v.Id) && v.ClienteId == cliente.Id
                    && ProdutoPacote.Normalizar(v.Carga?.Produto) == produto);
                if (atual != null)
                {
                    usadas.Add(atual.Id);
                    if (MesmosItens(atual, quer))
                    {
                        PreencherPrecosVazios(atual, cliente);
                        if (produto == ProdutoPacote.ProdutoC && brz && atual.CobradaNoRomaneio == null) atual.CobradaNoRomaneio = ProdutosCBrz.TextoCobrada;
                        continue;
                    }
                    if (atual.Numero != null)
                    {
                        avisos.Add($"{cliente.Nome}: a saída de {nome} de {c.Data:dd/MM} já tem romaneio {atual.Numero} e ficou como estava; o controle diz outra quantidade. Confira na tela.");
                        continue;
                    }
                    cargasMexidas.Add(atual.CargaId);
                    db.VendasProdutoB.Remove(atual);
                    await db.SaveChangesAsync();
                }

                var carga = await CargaAbertaAsync(produto, quer.Select(q => q.Id));
                if (carga == null)
                {
                    avisos.Add($"{cliente.Nome}: sem carga de {nome} aberta, a saída de {Qtd(itens.Sum(i => i.Qtd))} pacotes não foi lançada. Cadastre a carga e leia o carregamento de novo.");
                    continue;
                }
                var venda = NovaVenda(carga, cliente, c.Data, obs, quer);
                venda.CarregamentoId = c.Id;
                if (produto == ProdutoPacote.ProdutoC && brz) venda.CobradaNoRomaneio = ProdutosCBrz.TextoCobrada;
                db.VendasProdutoB.Add(venda);
                cargasMexidas.Add(carga.Id);
                foreach (var (tipo, qtd) in itens)
                    if (!cliente.PrecosProdutoB.Any(p => p.TipoProdutoBId == tipo.Id && p.Preco > 0))
                        avisos.Add($"{cliente.Nome}: sem preço de venda de {tipo.Nome.ToLowerInvariant()} no painel de preços; a saída ficou sem valor.");
            }
        }

        // O que o controle não tem mais sai (menos as que já têm romaneio)
        foreach (var v in existentes.Where(v => !usadas.Contains(v.Id)))
        {
            if (v.Numero != null) { avisos.Add($"Saída de {c.Data:dd/MM} com romaneio {v.Numero} não está mais no controle e ficou. Confira."); continue; }
            cargasMexidas.Add(v.CargaId);
            db.VendasProdutoB.Remove(v);
        }
        await db.SaveChangesAsync();
        foreach (var id in cargasMexidas) await FecharSeAcabouAsync(id);
        avisos.AddRange(await EstoqueNegativoAsync(cargasMexidas));
        return avisos;
    }

    /// <summary>Apaga as saídas que vieram do carregamento (as que já têm romaneio ficam, com aviso).</summary>
    public async Task<List<string>> ApagarDoCarregamentoAsync(int carregamentoId)
    {
        var avisos = new List<string>();
        var vendas = await db.VendasProdutoB.Where(v => v.CarregamentoId == carregamentoId).ToListAsync();
        foreach (var v in vendas)
        {
            if (v.Numero != null) { v.CarregamentoId = null; avisos.Add($"A saída com romaneio {v.Numero} ficou (agora como lançada à mão)."); }
            else db.VendasProdutoB.Remove(v);
        }
        await db.SaveChangesAsync();
        return avisos;
    }

    // ───────────────────────── Volumes embalados ─────────────────────────

    /// <summary>
    /// Saída das produtosB usadas para embalar no controle (amarelas = Abe × 30,
    /// brancas = Ave × 30 unidades; pacote de 150), no cliente EMBALADOS e na data
    /// do controle. A saída lançada à mão no EMBALADOS no mesmo dia (como era antes)
    /// é adotada, em vez de criar outra: não conta duas vezes.
    /// </summary>
    public async Task<List<string>> SincronizarEmbaladosAsync(int controleId)
    {
        var avisos = new List<string>();
        var controle = await db.VolumesControles.AsNoTracking().Include(c => c.Linhas).FirstOrDefaultAsync(c => c.Id == controleId);
        var ligadas = await db.VendasProdutoB.Include(v => v.Itens).Include(v => v.Carga)
            .Where(v => v.VolumesControleId == controleId).ToListAsync();
        if (controle == null)
        {
            foreach (var v in ligadas)
                if (v.Numero == null) db.VendasProdutoB.Remove(v); else v.VolumesControleId = null;
            await db.SaveChangesAsync();
            return avisos;
        }

        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var amarela = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoAmarela);
        var branca = tipos.FirstOrDefault(t => t.Codigo == TipoProdutoB.CodigoBranca);
        if (amarela == null || branca == null)
        {
            avisos.Add("Faltam os tipos de produtoB amarela e branca no cadastro (Codigo): o uso nos embalados não foi lançado.");
            return avisos;
        }
        var uso = ConferenciaVolumes.ProdutosBUsadas(controle);
        var quer = new List<(int Id, int Pacotes, int Unidades)>();
        void Por(TipoProdutoB t, decimal unidades)
        {
            if (unidades <= 0) return;
            var porPacote = Math.Max(1, t.UnidadesPorPacote);
            var un = (int)Math.Round(unidades);
            quer.Add((t.Id, un / porPacote, un % porPacote));
        }
        Por(amarela, uso.UnidadesAmarelas);
        Por(branca, uso.UnidadesBrancas);

        var cliente = await ClienteEmbaladosAsync();
        // Saída à mão de antes, no mesmo dia, ainda sem ligação: vira a deste controle
        if (ligadas.Count == 0)
        {
            var manual = await db.VendasProdutoB.Include(v => v.Itens).Include(v => v.Carga)
                .Where(v => v.ClienteId == cliente.Id && v.Data == controle.Data && v.VolumesControleId == null && v.CarregamentoId == null)
                .OrderBy(v => v.Id).FirstOrDefaultAsync();
            if (manual != null)
            {
                manual.VolumesControleId = controle.Id;
                ligadas.Add(manual);
                if (!MesmosItens(manual, quer))
                    avisos.Add($"Controle {controle.Numero}: a saída lançada à mão em {controle.Data:dd/MM} ({Pacotes(manual)}) foi trocada pelo que o controle diz ({Pacotes(quer, tipos)}).");
            }
        }

        var atual = ligadas.FirstOrDefault();
        foreach (var sobra in ligadas.Skip(1))
            if (sobra.Numero == null) db.VendasProdutoB.Remove(sobra);
        var cargasMexidas = new HashSet<int>();
        if (atual != null && MesmosItens(atual, quer))
        {
            await db.SaveChangesAsync();
            return avisos;
        }
        if (atual != null)
        {
            if (atual.Numero != null)
            {
                avisos.Add($"Controle {controle.Numero}: a saída de produtosB tem romaneio {atual.Numero} e ficou como estava. Confira.");
                await db.SaveChangesAsync();
                return avisos;
            }
            cargasMexidas.Add(atual.CargaId);
            db.VendasProdutoB.Remove(atual);
        }
        if (quer.Count > 0)
        {
            var carga = await CargaAbertaAsync(ProdutoPacote.ProdutoB, quer.Select(q => q.Id));
            if (carga == null)
                avisos.Add($"Controle {controle.Numero}: sem carga de produtosB aberta, o uso ({Pacotes(quer, tipos)}) não saiu do estoque. Cadastre a carga.");
            else
            {
                // Uso próprio: sem comissão, vale o custo da carga (como as saídas à mão do EMBALADOS)
                var venda = NovaVenda(carga, cliente, controle.Data, $"Usadas para embalar (controle {controle.Numero})", quer, precoDoCusto: true);
                venda.VolumesControleId = controle.Id;
                venda.FretePacote = 0;
                db.VendasProdutoB.Add(venda);
                cargasMexidas.Add(carga.Id);
            }
        }
        await db.SaveChangesAsync();
        foreach (var id in cargasMexidas) await FecharSeAcabouAsync(id);
        avisos.AddRange(await EstoqueNegativoAsync(cargasMexidas));
        return avisos;
    }

    /// <summary>Todos os controles de volumes embalados (botão da tela de ProdutosB; roda de novo sem duplicar).</summary>
    public async Task<object> SincronizarTodosEmbaladosAsync()
    {
        var ids = await db.VolumesControles.AsNoTracking().OrderBy(c => c.Data).ThenBy(c => c.Numero).Select(c => c.Id).ToListAsync();
        var avisos = new List<string>();
        foreach (var id in ids) avisos.AddRange(await SincronizarEmbaladosAsync(id));
        return new { controles = ids.Count, avisos = avisos.Distinct().ToList() };
    }

    // ───────────────────────── Peças ─────────────────────────

    private VendaProdutoB NovaVenda(CargaProdutoB carga, EstoqueCliente cliente, DateOnly data, string? obs,
        IEnumerable<(int Tipo, int Pacotes, int Unidades)> itens, bool precoDoCusto = false)
    {
        var custoPacote = CustoPacote(carga);
        var frete = ProdutoPacote.Normalizar(carga.Produto) == ProdutoPacote.ProdutoC ? cliente.FreteDaProdutoC : cliente.FreteProdutoB;
        return new VendaProdutoB
        {
            CargaId = carga.Id, ClienteId = cliente.Id, Data = data, CustoPacote = custoPacote, FretePacote = frete, Observacao = obs,
            Itens = itens.Select(i => new VendaProdutoBItem
            {
                TipoProdutoBId = i.Tipo, Pacotes = i.Pacotes, Unidades = i.Unidades,
                Preco = precoDoCusto ? custoPacote : cliente.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == i.Tipo)?.Preco ?? 0,
            }).ToList(),
        };
    }

    public static decimal CustoPacote(CargaProdutoB c) =>
        c.QuantidadePacotes > 0 ? (c.ValorProdutosB + c.ValorFrete) / c.QuantidadePacotes : 0;

    // Venda gravada sem preço (estava em branco no painel) ganha o preço cadastrado depois
    private static void PreencherPrecosVazios(VendaProdutoB v, EstoqueCliente cliente)
    {
        foreach (var i in v.Itens.Where(i => i.Preco == 0))
            if (cliente.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == i.TipoProdutoBId) is { Preco: > 0 } p)
                i.Preco = p.Preco;
    }

    private static bool MesmosItens(VendaProdutoB v, IEnumerable<(int Id, int Pacotes, int Unidades)> quer)
    {
        var a = v.Itens.Where(i => i.Pacotes > 0 || i.Unidades > 0).Select(i => (i.TipoProdutoBId, i.Pacotes, i.Unidades)).OrderBy(x => x.TipoProdutoBId).ToList();
        var b = quer.Where(q => q.Pacotes > 0 || q.Unidades > 0).OrderBy(x => x.Id).ToList();
        return a.Count == b.Count && a.Zip(b).All(p => p.First.TipoProdutoBId == p.Second.Id && p.First.Pacotes == p.Second.Pacotes && p.First.Unidades == p.Second.Unidades);
    }

    /// <summary>
    /// A carga aberta do produto que tem o tipo (a mais antiga com estoque dele; sem
    /// estoque, a mais recente aberta): é a que "assume" até acabar (EST-01).
    /// </summary>
    private async Task<CargaProdutoB?> CargaAbertaAsync(string produto, IEnumerable<int> tipoIds)
    {
        var ids = tipoIds.ToList();
        var abertas = await db.CargasProdutoB.Include(c => c.Itens).Include(c => c.Vendas).ThenInclude(v => v.Itens)
            .Where(c => !c.Encerrada).OrderBy(c => c.Data).ThenBy(c => c.Id).ToListAsync();
        abertas = abertas.Where(c => ProdutoPacote.Normalizar(c.Produto) == produto && c.Itens.Any(i => ids.Contains(i.TipoProdutoBId))).ToList();
        if (abertas.Count == 0) return null;
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        decimal Estoque(CargaProdutoB c) => c.Itens.Where(i => ids.Contains(i.TipoProdutoBId)).Sum(i => i.Quantidade)
            - c.Vendas.SelectMany(v => v.Itens).Where(i => ids.Contains(i.TipoProdutoBId)).Sum(i => Equivalente(i, tipos));
        return abertas.FirstOrDefault(c => Estoque(c) > 0) ?? abertas[^1];
    }

    /// <summary>Acabaram os pacotes de todos os tipos: a carga se encerra sozinha.</summary>
    public async Task<bool> FecharSeAcabouAsync(int cargaId)
    {
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var carga = await db.CargasProdutoB.Include(c => c.Itens).Include(c => c.Vendas).ThenInclude(v => v.Itens)
            .FirstOrDefaultAsync(c => c.Id == cargaId);
        if (carga == null) return false;
        decimal entrada = carga.Itens.Sum(i => i.Quantidade);
        decimal saiu = carga.Vendas.SelectMany(v => v.Itens).Sum(i => Equivalente(i, tipos));
        bool acabou = entrada > 0 && saiu >= entrada;
        if (acabou && !carga.Encerrada)
        {
            carga.Encerrada = true;
            carga.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return acabou;
    }

    private async Task<List<string>> EstoqueNegativoAsync(IEnumerable<int> cargaIds)
    {
        var avisos = new List<string>();
        var ids = cargaIds.ToList();
        if (ids.Count == 0) return avisos;
        var tipos = await db.TiposProdutoB.AsNoTracking().ToListAsync();
        var cargas = await db.CargasProdutoB.AsNoTracking().Include(c => c.Itens).Include(c => c.Vendas).ThenInclude(v => v.Itens)
            .Where(c => ids.Contains(c.Id)).ToListAsync();
        foreach (var c in cargas)
            foreach (var item in c.Itens)
            {
                var saiu = c.Vendas.SelectMany(v => v.Itens).Where(i => i.TipoProdutoBId == item.TipoProdutoBId).Sum(i => Equivalente(i, tipos));
                var estoque = item.Quantidade - saiu;
                if (estoque < 0)
                    avisos.Add($"{tipos.FirstOrDefault(t => t.Id == item.TipoProdutoBId)?.Nome}: a carga de {c.Data:dd/MM/yyyy} ficou com {Qtd(estoque)} pacotes. Cadastre a carga nova.");
            }
        return avisos;
    }

    public static decimal Equivalente(VendaProdutoBItem item, List<TipoProdutoB> tipos)
    {
        int porPacote = tipos.FirstOrDefault(t => t.Id == item.TipoProdutoBId)?.UnidadesPorPacote ?? 150;
        if (porPacote <= 0) porPacote = 150;
        return item.Pacotes + (decimal)item.Unidades / porPacote;
    }

    /// <summary>
    /// Cliente do controle → cadastro do estoque: pelo vínculo do painel de preços ou
    /// pelo nome. Cliente da tabela de frete ainda sem cadastro no estoque ganha um
    /// (fretes padrão, sem preço de venda) e fica ligado; fora das duas, avisa.
    /// </summary>
    private async Task<EstoqueCliente?> EstoqueDoClienteAsync(string nome, TabelaPrecos tabela, List<string> avisos)
    {
        var achado = tabela.EstoqueDoCliente(nome);
        if (achado != null)
            return await db.EstoqueClientes.Include(c => c.PrecosProdutoB).FirstAsync(c => c.Id == achado.Id);
        var frete = tabela.AcharFrete(nome);
        if (frete == null)
        {
            avisos.Add($"{nome}: não está no painel de preços; a saída de produtosB e produtosC não foi lançada.");
            return null;
        }
        var novo = PrecosClienteService.NovoEstoque(frete.Cliente);
        db.EstoqueClientes.Add(novo);
        await db.SaveChangesAsync();
        var p = await db.PrecosFreteCliente.FirstAsync(x => x.Id == frete.Id);
        p.EstoqueClienteId = novo.Id;
        await db.SaveChangesAsync();
        tabela.Estoque.Add(novo);
        frete.EstoqueClienteId = novo.Id;
        avisos.Add($"{frete.Cliente}: cadastrado no estoque agora, sem preço de venda. Cadastre no painel de preços.");
        return novo;
    }

    private async Task<EstoqueCliente> ClienteEmbaladosAsync()
    {
        var todos = await db.EstoqueClientes.Include(c => c.PrecosProdutoB).ToListAsync();
        var c = todos.FirstOrDefault(x => Normalizar(x.Nome) == ClienteEmbalados);
        if (c != null) return c;
        c = new EstoqueCliente { Nome = ClienteEmbalados, FreteProdutoB = 0, FreteProdutoC = 0 };
        db.EstoqueClientes.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private static string Qtd(decimal v) => v.ToString(v % 1 == 0 ? "N0" : "N1", PtBr);

    private static string Pacotes(VendaProdutoB v) =>
        string.Join(" + ", v.Itens.Select(i => i.Unidades > 0 ? $"{i.Pacotes} pacotes e {i.Unidades} unidades" : $"{i.Pacotes} pacotes"));

    private static string Pacotes(List<(int Id, int Pacotes, int Unidades)> quer, List<TipoProdutoB> tipos) =>
        quer.Count == 0 ? "nada" : string.Join(" + ", quer.Select(q =>
            $"{(tipos.FirstOrDefault(t => t.Id == q.Id)?.Codigo == TipoProdutoB.CodigoAmarela ? "amarelas" : "brancas")} {q.Pacotes} pacotes{(q.Unidades > 0 ? $" e {q.Unidades} unidades" : "")}"));
}
