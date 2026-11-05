using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Traz para o sistema o histórico das planilhas preenchidas à mão.
///
/// São dois passos de propósito: a prévia diz o que foi lido e quais clientes
/// aparecem, e só a importação grava. Como o preço fica congelado dentro de
/// cada venda, importar antes de acertar os preços deixaria a comissão errada
/// para sempre — por isso o preço é confirmado na prévia.
/// </summary>
[Authorize]
[ApiController]
[Route("api/estoque")]
public class EstoqueImportacaoController(AppDbContext db, ImportacaoEstoqueService leitor,
    EmbaladosService embalados, ILogger<EstoqueImportacaoController> logger) : ControllerBase
{
    private static string N(string? s) => ImportacaoEstoqueService.Norm(s);

    private static async Task<byte[]> Bytes(IFormFile arquivo)
    {
        using var ms = new MemoryStream();
        await arquivo.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static List<PrecoImportado> LerPrecos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<PrecoImportado>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch { return []; }
    }

    // ── Mesma planilha de novo ───────────────────────────────────────────
    // Subir de novo a planilha de uma carga que já entrou é normal: ela volta com
    // anotações a mais (UI-21, Admin, 20/10/2026). A prévia compara com o que
    // está no site e a importação grava só o que é novo ou mudou; o que só existe
    // no site (lançado à mão) fica como está.

    public record LinhaResumo(string Cliente, DateOnly Data, string Texto);
    public record MudancaResumo(string Cliente, DateOnly Data, string Antes, string Depois);
    public record ResumoReimportacao(List<string> Carga, List<LinhaResumo> Novas, List<MudancaResumo> Mudadas,
        int Iguais, List<LinhaResumo> SoNoSite, List<LinhaResumo> Mantidas);

    // Casa cada saída lida com uma do site do mesmo cliente e dia (uma para uma);
    // a chave extra (espessura) desempata quando o cliente leva duas no mesmo dia.
    private static List<(TLida Lida, TSite? Site)> Parear<TLida, TSite>(
        List<TLida> lidas, List<TSite> site,
        Func<TLida, string> clienteLida, Func<TLida, DateOnly> dataLida, Func<TLida, string> extraLida,
        Func<TSite, string> clienteSite, Func<TSite, DateOnly> dataSite, Func<TSite, string> extraSite,
        out List<TSite> sobra) where TSite : class
    {
        var livres = site.ToList();
        var pares = new List<(TLida, TSite?)>();
        foreach (var l in lidas)
        {
            var candidatos = livres.Where(x => N(clienteSite(x)) == N(clienteLida(l)) && dataSite(x) == dataLida(l)).ToList();
            var achado = candidatos.FirstOrDefault(x => extraSite(x) == extraLida(l)) ?? candidatos.FirstOrDefault();
            if (achado != null) livres.Remove(achado);
            pares.Add((l, achado));
        }
        sobra = livres;
        return pares;
    }

    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");
    private static string TextoProdutosA(IReadOnlyCollection<int> pesos) =>
        $"{pesos.Count} {(pesos.Count == 1 ? "produtoA" : "produtosA")} · {(pesos.Sum() / 1000m).ToString("#,0.##", PtBr)} kg";
    private static bool MesmosPesos(IEnumerable<int> a, IEnumerable<int> b) => a.Order().SequenceEqual(b.Order());
    private static string Dinheiro(decimal v) => v.ToString("C2", PtBr);
    private static void Comparar(List<string> lista, string campo, string antes, string depois)
    {
        if (antes != depois) lista.Add($"{campo}: {antes} → {depois}");
    }

    private Task<CargaProdutoA?> CargaProdutoAExistente(ImportacaoEstoqueService.CargaProdutoALida c) =>
        db.CargasProdutoA.Include(x => x.Vendas).ThenInclude(v => v.Cliente)
            .Include(x => x.Vendas).ThenInclude(v => v.ProdutosA)
            .FirstOrDefaultAsync(x => x.Data == c.Data && x.Fornecedor == c.Fornecedor.ToUpper());

    private static List<(ImportacaoEstoqueService.VendaProdutoALida Lida, VendaProdutoA? Site)> ParesProdutosA(
        CargaProdutoA carga, ImportacaoEstoqueService.ProdutosALidas lido, out List<VendaProdutoA> sobra) =>
        Parear(lido.Vendas, carga.Vendas.ToList(),
            l => l.Cliente, l => l.Data, l => l.Espessura?.ToString() ?? "",
            v => v.Cliente?.Nome ?? "", v => v.Data, v => v.Espessura?.ToString() ?? "", out sobra);

    private static bool ProdutoAMudou(ImportacaoEstoqueService.VendaProdutoALida l, VendaProdutoA site) =>
        !MesmosPesos(l.Pesos, site.ProdutosA.Select(b => b.PesoGramas)) || l.Espessura != site.Espessura;

    private static ResumoReimportacao ResumoProdutosA(CargaProdutoA carga, ImportacaoEstoqueService.ProdutosALidas lido)
    {
        var mudCarga = new List<string>();
        Comparar(mudCarga, "ProdutosA da carga", carga.Quantidade.ToString(), lido.Carga.Quantidade.ToString());
        Comparar(mudCarga, "Valor da carga", Dinheiro(carga.ValorTotal), Dinheiro(lido.Carga.ValorTotal));
        Comparar(mudCarga, "Custo por kg", Dinheiro(carga.CustoKg), Dinheiro(lido.Carga.CustoKg));
        Comparar(mudCarga, "Controle", carga.Controle ?? "", lido.Carga.Controle ?? "");

        var pares = ParesProdutosA(carga, lido, out var sobra);
        var novas = pares.Where(p => p.Site == null)
            .Select(p => new LinhaResumo(p.Lida.Cliente, p.Lida.Data, TextoProdutosA(p.Lida.Pesos))).ToList();
        var mudadas = pares.Where(p => p.Site != null && ProdutoAMudou(p.Lida, p.Site))
            .Select(p => new MudancaResumo(p.Lida.Cliente, p.Lida.Data,
                TextoProdutosA(p.Site!.ProdutosA.Select(b => b.PesoGramas).ToList()), TextoProdutosA(p.Lida.Pesos))).ToList();
        var soNoSite = sobra.Select(v => new LinhaResumo(v.Cliente?.Nome ?? "", v.Data,
            TextoProdutosA(v.ProdutosA.Select(b => b.PesoGramas).ToList()))).ToList();
        return new ResumoReimportacao(mudCarga, novas, mudadas, pares.Count(p => p.Site != null) - mudadas.Count, soNoSite, []);
    }

    // ── ProdutosA ───────────────────────────────────────────────────────────

    [HttpPost("produtosA/importar/previa")]
    public async Task<IActionResult> PreviaProdutosA(IFormFile? arquivo)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { erro = "Envie a planilha de controle de produtosA." });

        try
        {
            var lido = leitor.LerProdutosA(await Bytes(arquivo));
            var clientes = await db.EstoqueClientes.ToListAsync();
            var existente = await CargaProdutoAExistente(lido.Carga);
            var resumo = existente == null ? null : ResumoProdutosA(existente, lido);

            var porCliente = lido.Vendas
                .GroupBy(v => v.Cliente)
                .Select(g =>
                {
                    var cadastro = clientes.FirstOrDefault(c => N(c.Nome) == N(g.Key));
                    return new
                    {
                        nome = g.Key,
                        existe = cadastro != null,
                        // Sem preço conhecido, o padrão é o próprio custo: baixa o
                        // estoque sem inventar comissão.
                        precoSugerido = cadastro?.PrecoProdutoAKg is > 0
                            ? cadastro.PrecoProdutoAKg : lido.Carga.CustoKg,
                        vendas = g.Count(),
                        produtosA = g.Sum(v => v.Pesos.Count),
                        pesoKg = g.Sum(v => v.Pesos.Sum()) / 1000m,
                        // Na reimportação o preço só vale para as saídas novas
                        temNova = resumo == null || resumo.Novas.Any(n => N(n.Cliente) == N(g.Key)),
                    };
                })
                .OrderByDescending(c => c.pesoKg)
                .ToList();

            return Ok(new
            {
                carga = lido.Carga,
                jaImportada = existente != null,
                resumo,
                totalVendas = lido.Vendas.Count,
                totalProdutosA = lido.Vendas.Sum(v => v.Pesos.Count),
                pesoKg = lido.Vendas.Sum(v => v.Pesos.Sum()) / 1000m,
                clientes = porCliente,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha lendo planilha de produtosA");
            return BadRequest(new { erro = $"Não consegui ler a planilha: {ex.Message}" });
        }
    }

    [HttpPost("produtosA/importar")]
    public async Task<IActionResult> ImportarProdutosA(IFormFile? arquivo, [FromForm] string? precos)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { erro = "Envie a planilha de controle de produtosA." });

        try
        {
            var lido = leitor.LerProdutosA(await Bytes(arquivo));
            var tabela = LerPrecos(precos).ToDictionary(p => N(p.Nome), p => p.Preco);
            var clientes = await db.EstoqueClientes.ToListAsync();

            async Task<(EstoqueCliente, decimal)> ClienteEPreco(string nome, decimal custoKg)
            {
                var cliente = clientes.FirstOrDefault(c => N(c.Nome) == N(nome));
                decimal preco = tabela.TryGetValue(N(nome), out var p) && p > 0
                    ? p : (cliente?.PrecoProdutoAKg ?? custoKg);
                if (cliente == null)
                {
                    cliente = new EstoqueCliente { Nome = nome, PrecoProdutoAKg = preco };
                    db.EstoqueClientes.Add(cliente);
                    await db.SaveChangesAsync();
                    clientes.Add(cliente);
                }
                else if (preco > 0 && cliente.PrecoProdutoAKg != preco)
                {
                    cliente.PrecoProdutoAKg = preco;
                    cliente.AtualizadoEm = DateTime.UtcNow;
                }
                return (cliente, preco);
            }

            var carga = await CargaProdutoAExistente(lido.Carga);
            if (carga != null)
            {
                // Mesma carga de novo: entra o que é novo e muda o que mudou
                var resumo = ResumoProdutosA(carga, lido);
                if (carga.CustoKg != lido.Carga.CustoKg)
                    foreach (var v in carga.Vendas.Where(v => v.CustoKg == carga.CustoKg)) v.CustoKg = lido.Carga.CustoKg;
                carga.Quantidade = lido.Carga.Quantidade;
                carga.ValorTotal = lido.Carga.ValorTotal;
                carga.CustoKg = lido.Carga.CustoKg;
                carga.Controle = lido.Carga.Controle;
                carga.AtualizadoEm = DateTime.UtcNow;

                foreach (var (l, site) in ParesProdutosA(carga, lido, out _))
                {
                    if (site == null)
                    {
                        var (cliente, preco) = await ClienteEPreco(l.Cliente, carga.CustoKg);
                        db.VendasProdutoA.Add(new VendaProdutoA
                        {
                            CargaId = carga.Id, ClienteId = cliente.Id, Data = l.Data, Espessura = l.Espessura,
                            PrecoKg = preco, CustoKg = carga.CustoKg,
                            ProdutosA = l.Pesos.Select(g => new ProdutoAVendida { PesoGramas = g }).ToList(),
                        });
                    }
                    else if (ProdutoAMudou(l, site))
                    {
                        // A saída que já existia fica com o preço que tinha
                        db.ProdutosAVendidas.RemoveRange(site.ProdutosA);
                        site.ProdutosA = l.Pesos.Select(g => new ProdutoAVendida { PesoGramas = g }).ToList();
                        site.Espessura = l.Espessura;
                        site.AtualizadoEm = DateTime.UtcNow;
                    }
                }
                await db.SaveChangesAsync();
                await embalados.SincronizarProdutosAEmbaladoAsync();
                return Ok(new { cargaId = carga.Id, atualizada = true, resumo, vendas = resumo.Novas.Count });
            }

            carga = new CargaProdutoA
            {
                Data = lido.Carga.Data,
                Fornecedor = lido.Carga.Fornecedor.ToUpperInvariant(),
                Quantidade = lido.Carga.Quantidade,
                ValorTotal = lido.Carga.ValorTotal,
                CustoKg = lido.Carga.CustoKg,
                Controle = lido.Carga.Controle,
                Observacao = "Importada da planilha",
            };
            db.CargasProdutoA.Add(carga);
            await db.SaveChangesAsync();

            foreach (var venda in lido.Vendas)
            {
                var (cliente, preco) = await ClienteEPreco(venda.Cliente, lido.Carga.CustoKg);
                db.VendasProdutoA.Add(new VendaProdutoA
                {
                    CargaId = carga.Id,
                    ClienteId = cliente.Id,
                    Data = venda.Data,
                    Espessura = venda.Espessura,
                    PrecoKg = preco,
                    CustoKg = carga.CustoKg,
                    ProdutosA = venda.Pesos.Select(g => new ProdutoAVendida { PesoGramas = g }).ToList(),
                });
            }

            await db.SaveChangesAsync();
            await embalados.SincronizarProdutosAEmbaladoAsync();

            return Ok(new
            {
                cargaId = carga.Id,
                vendas = lido.Vendas.Count,
                produtosA = lido.Vendas.Sum(v => v.Pesos.Count),
                clientes = lido.Vendas.Select(v => v.Cliente).Distinct().Count(),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha importando planilha de produtosA");
            return BadRequest(new { erro = $"Não consegui importar: {ex.Message}" });
        }
    }

    // ── ProdutosB ────────────────────────────────────────────────────────

    private static decimal CustoPacote(ImportacaoEstoqueService.CargaProdutoBLida c) =>
        c.QuantidadePacotes > 0 ? (c.ValorProdutosB + c.ValorFrete) / c.QuantidadePacotes : 0;

    private Task<CargaProdutoB?> CargaProdutoBExistente(ImportacaoEstoqueService.CargaProdutoBLida c) =>
        db.CargasProdutoB.Include(x => x.Itens).ThenInclude(i => i.Tipo)
            .Include(x => x.Vendas).ThenInclude(v => v.Cliente)
            .Include(x => x.Vendas).ThenInclude(v => v.Itens).ThenInclude(i => i.Tipo)
            .FirstOrDefaultAsync(x => x.Produto == ProdutoPacote.ProdutoB && x.Data == c.Data && x.Fornecedor == c.Fornecedor.ToUpper());

    private static string TextoItens(IEnumerable<(string Tipo, int Pacotes, int Unidades)> itens)
    {
        var partes = itens.Where(i => i.Pacotes > 0 || i.Unidades > 0)
            .OrderBy(i => ImportacaoEstoqueService.NormTipo(i.Tipo))
            .Select(i => $"{i.Tipo.ToUpperInvariant()} {i.Pacotes} {(i.Pacotes == 1 ? "pacote" : "pacotes")}" +
                         (i.Unidades > 0 ? $" + {i.Unidades} unidades" : "")).ToList();
        return partes.Count == 0 ? "nada" : string.Join(" · ", partes);
    }
    private static List<(string Tipo, int Pacotes, int Unidades)> ItensLidos(ImportacaoEstoqueService.VendaProdutoBLida v) =>
        v.Itens.Select(i => (i.Tipo, i.Pacotes, i.Unidades)).ToList();
    private static List<(string Tipo, int Pacotes, int Unidades)> ItensSite(VendaProdutoB v) =>
        v.Itens.Select(i => (i.Tipo?.Nome ?? "", i.Pacotes, i.Unidades)).ToList();
    private static bool MesmosItens(List<(string Tipo, int Pacotes, int Unidades)> a, List<(string Tipo, int Pacotes, int Unidades)> b)
    {
        static IEnumerable<string> Chaves(List<(string Tipo, int Pacotes, int Unidades)> x) => x
            .Where(i => i.Pacotes > 0 || i.Unidades > 0)
            .Select(i => $"{ImportacaoEstoqueService.NormTipo(i.Tipo)}|{i.Pacotes}|{i.Unidades}").Order();
        return Chaves(a).SequenceEqual(Chaves(b));
    }
    // Saída que nasceu do controle de carregamento ou dos volumes embalados não é da planilha
    private static bool DeOutroLugar(VendaProdutoB v) => v.CarregamentoId != null || v.VolumesControleId != null;

    private static List<(ImportacaoEstoqueService.VendaProdutoBLida Lida, VendaProdutoB? Site)> ParesProdutosB(
        CargaProdutoB carga, ImportacaoEstoqueService.ProdutosBLidas lido, out List<VendaProdutoB> sobra) =>
        Parear(lido.Vendas, carga.Vendas.ToList(),
            l => l.Cliente, l => l.Data, _ => "", v => v.Cliente?.Nome ?? "", v => v.Data, _ => "", out sobra);

    private static ResumoReimportacao ResumoProdutosB(CargaProdutoB carga, ImportacaoEstoqueService.ProdutosBLidas lido)
    {
        var mudCarga = new List<string>();
        Comparar(mudCarga, "Pacotes comprados", carga.QuantidadePacotes.ToString(), lido.Carga.QuantidadePacotes.ToString());
        Comparar(mudCarga, "Valor das produtosB", Dinheiro(carga.ValorProdutosB), Dinheiro(lido.Carga.ValorProdutosB));
        Comparar(mudCarga, "Frete", Dinheiro(carga.ValorFrete), Dinheiro(lido.Carga.ValorFrete));
        Comparar(mudCarga, "Transportador", carga.Transportador ?? "", lido.Carga.Transportador ?? "");
        foreach (var t in lido.Carga.Tipos)
        {
            var item = carga.Itens.FirstOrDefault(i =>
                ImportacaoEstoqueService.NormTipo(i.Tipo?.Nome) == ImportacaoEstoqueService.NormTipo(t.Nome));
            Comparar(mudCarga, $"Entrada {t.Nome.ToUpperInvariant()}", (item?.Quantidade ?? 0).ToString(), t.Entrada.ToString());
        }

        var pares = ParesProdutosB(carga, lido, out var sobra);
        var novas = pares.Where(p => p.Site == null)
            .Select(p => new LinhaResumo(p.Lida.Cliente, p.Lida.Data, TextoItens(ItensLidos(p.Lida)))).ToList();
        var diferentes = pares.Where(p => p.Site != null && !MesmosItens(ItensLidos(p.Lida), ItensSite(p.Site))).ToList();
        var mudadas = diferentes.Where(p => !DeOutroLugar(p.Site!))
            .Select(p => new MudancaResumo(p.Lida.Cliente, p.Lida.Data, TextoItens(ItensSite(p.Site!)), TextoItens(ItensLidos(p.Lida)))).ToList();
        var mantidas = diferentes.Where(p => DeOutroLugar(p.Site!))
            .Select(p => new LinhaResumo(p.Lida.Cliente, p.Lida.Data,
                $"No site (do carregamento): {TextoItens(ItensSite(p.Site!))}; na planilha: {TextoItens(ItensLidos(p.Lida))}")).ToList();
        var soNoSite = sobra.Select(v => new LinhaResumo(v.Cliente?.Nome ?? "", v.Data, TextoItens(ItensSite(v)))).ToList();
        return new ResumoReimportacao(mudCarga, novas, mudadas, pares.Count(p => p.Site != null) - diferentes.Count, soNoSite, mantidas);
    }

    [HttpPost("produtosB/importar/previa")]
    public async Task<IActionResult> PreviaProdutosB(IFormFile? arquivo)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { erro = "Envie a planilha de controle de produtosB." });

        try
        {
            var lido = leitor.LerProdutosB(await Bytes(arquivo));
            var clientes = await db.EstoqueClientes.Include(c => c.PrecosProdutoB).ToListAsync();
            var custoPacote = CustoPacote(lido.Carga);
            var existente = await CargaProdutoBExistente(lido.Carga);
            var resumo = existente == null ? null : ResumoProdutosB(existente, lido);

            var porCliente = lido.Vendas
                .GroupBy(v => v.Cliente)
                .Select(g =>
                {
                    var cadastro = clientes.FirstOrDefault(c => N(c.Nome) == N(g.Key));
                    var precoAtual = cadastro?.PrecosProdutoB.FirstOrDefault(p => p.Preco > 0)?.Preco;
                    return new
                    {
                        nome = g.Key,
                        existe = cadastro != null,
                        precoSugerido = precoAtual is > 0 ? precoAtual.Value : custoPacote,
                        vendas = g.Count(),
                        pacotes = g.Sum(v => v.Itens.Sum(i => i.Pacotes + i.Unidades / 150m)),
                        temNova = resumo == null || resumo.Novas.Any(n => N(n.Cliente) == N(g.Key)),
                    };
                })
                .OrderByDescending(c => c.pacotes)
                .ToList();

            return Ok(new
            {
                carga = lido.Carga,
                custoPacote,
                jaImportada = existente != null,
                resumo,
                totalVendas = lido.Vendas.Count,
                clientes = porCliente,
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha lendo planilha de produtosB");
            return BadRequest(new { erro = $"Não consegui ler a planilha: {ex.Message}" });
        }
    }

    [HttpPost("produtosB/importar")]
    public async Task<IActionResult> ImportarProdutosB(IFormFile? arquivo, [FromForm] string? precos)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { erro = "Envie a planilha de controle de produtosB." });

        try
        {
            var lido = leitor.LerProdutosB(await Bytes(arquivo));
            var tabela = LerPrecos(precos).ToDictionary(p => N(p.Nome), p => p.Preco);

            // Tipos: casa pelo nome sem espaço ("MdoVr" x "MDO VR"); o que não
            // existir é cadastrado.
            var tipos = await db.TiposProdutoB.ToListAsync();
            async Task<TipoProdutoB> Tipo(string nome)
            {
                var achado = tipos.FirstOrDefault(t =>
                    ImportacaoEstoqueService.NormTipo(t.Nome) == ImportacaoEstoqueService.NormTipo(nome));
                if (achado != null) return achado;

                achado = new TipoProdutoB { Nome = nome.ToUpperInvariant(), UnidadesPorPacote = 150 };
                db.TiposProdutoB.Add(achado);
                await db.SaveChangesAsync();
                tipos.Add(achado);
                return achado;
            }

            var custoPacote = CustoPacote(lido.Carga);
            var clientes = await db.EstoqueClientes.Include(c => c.PrecosProdutoB).ToListAsync();

            async Task<EstoqueCliente> Cliente(string nome)
            {
                var cliente = clientes.FirstOrDefault(c => N(c.Nome) == N(nome));
                if (cliente != null) return cliente;
                cliente = new EstoqueCliente { Nome = nome };
                db.EstoqueClientes.Add(cliente);
                await db.SaveChangesAsync();
                clientes.Add(cliente);
                return cliente;
            }

            async Task<List<VendaProdutoBItem>> Itens(EstoqueCliente cliente, ImportacaoEstoqueService.VendaProdutoBLida venda,
                VendaProdutoB? anterior = null)
            {
                decimal precoTabela = tabela.TryGetValue(N(venda.Cliente), out var p) && p > 0 ? p : custoPacote;
                var itens = new List<VendaProdutoBItem>();
                foreach (var i in venda.Itens)
                {
                    var tipo = await Tipo(i.Tipo);
                    // A saída que já existia fica com o preço que tinha
                    var precoAnterior = anterior?.Itens.FirstOrDefault(x => x.TipoProdutoBId == tipo.Id)?.Preco;
                    var preco = precoAnterior ?? precoTabela;

                    // Guarda o preço no cadastro também, para as próximas vendas
                    // já virem preenchidas.
                    var cadastro = cliente.PrecosProdutoB.FirstOrDefault(x => x.TipoProdutoBId == tipo.Id);
                    if (cadastro == null)
                    {
                        cadastro = new EstoqueClientePreco
                        {
                            ClienteId = cliente.Id, TipoProdutoBId = tipo.Id, Preco = preco,
                        };
                        db.EstoqueClientePrecos.Add(cadastro);
                        cliente.PrecosProdutoB.Add(cadastro);
                    }
                    else if (precoAnterior == null && preco > 0 && cadastro.Preco != preco)
                    {
                        cadastro.Preco = preco;
                        cadastro.AtualizadoEm = DateTime.UtcNow;
                    }

                    itens.Add(new VendaProdutoBItem
                    {
                        TipoProdutoBId = tipo.Id,
                        Pacotes = i.Pacotes,
                        Unidades = i.Unidades,
                        Preco = preco,
                    });
                }
                return itens;
            }

            var carga = await CargaProdutoBExistente(lido.Carga);
            if (carga != null)
            {
                // Mesma carga de novo: entra o que é novo e muda o que mudou
                var resumo = ResumoProdutosB(carga, lido);
                var custoAntigo = carga.QuantidadePacotes > 0
                    ? (carga.ValorProdutosB + carga.ValorFrete) / carga.QuantidadePacotes : 0;
                if (custoAntigo != custoPacote)
                    foreach (var v in carga.Vendas.Where(v => v.CustoPacote == custoAntigo)) v.CustoPacote = custoPacote;
                carga.QuantidadePacotes = lido.Carga.QuantidadePacotes;
                carga.ValorProdutosB = lido.Carga.ValorProdutosB;
                carga.ValorFrete = lido.Carga.ValorFrete;
                carga.Transportador = lido.Carga.Transportador;
                carga.Observacao = lido.Carga.Observacao;
                carga.AtualizadoEm = DateTime.UtcNow;
                foreach (var t in lido.Carga.Tipos)
                {
                    var tipo = await Tipo(t.Nome);
                    var item = carga.Itens.FirstOrDefault(i => i.TipoProdutoBId == tipo.Id);
                    if (item != null) item.Quantidade = t.Entrada;
                    else if (t.Entrada != 0)
                        db.CargaProdutoBItens.Add(new CargaProdutoBItem { CargaId = carga.Id, TipoProdutoBId = tipo.Id, Quantidade = t.Entrada });
                }

                foreach (var (l, site) in ParesProdutosB(carga, lido, out _))
                {
                    if (site == null)
                    {
                        var cliente = await Cliente(l.Cliente);
                        db.VendasProdutoB.Add(new VendaProdutoB
                        {
                            CargaId = carga.Id, ClienteId = cliente.Id, Data = l.Data, CustoPacote = custoPacote,
                            Itens = await Itens(cliente, l),
                        });
                    }
                    else if (!DeOutroLugar(site) && !MesmosItens(ItensLidos(l), ItensSite(site)))
                    {
                        var novos = await Itens(site.Cliente ?? await Cliente(l.Cliente), l, site);
                        db.VendaProdutoBItens.RemoveRange(site.Itens);
                        site.Itens = novos;
                        site.AtualizadoEm = DateTime.UtcNow;
                    }
                }
                await db.SaveChangesAsync();
                return Ok(new { cargaId = carga.Id, atualizada = true, resumo, vendas = resumo.Novas.Count });
            }

            carga = new CargaProdutoB
            {
                Data = lido.Carga.Data,
                Fornecedor = lido.Carga.Fornecedor.ToUpperInvariant(),
                QuantidadePacotes = lido.Carga.QuantidadePacotes,
                ValorProdutosB = lido.Carga.ValorProdutosB,
                ValorFrete = lido.Carga.ValorFrete,
                Transportador = lido.Carga.Transportador,
                Observacao = lido.Carga.Observacao,
            };
            db.CargasProdutoB.Add(carga);
            await db.SaveChangesAsync();

            foreach (var t in lido.Carga.Tipos)
            {
                var tipo = await Tipo(t.Nome);
                db.CargaProdutoBItens.Add(new CargaProdutoBItem
                {
                    CargaId = carga.Id,
                    TipoProdutoBId = tipo.Id,
                    Quantidade = t.Entrada,
                });
            }
            await db.SaveChangesAsync();

            foreach (var venda in lido.Vendas)
            {
                var cliente = await Cliente(venda.Cliente);
                db.VendasProdutoB.Add(new VendaProdutoB
                {
                    CargaId = carga.Id,
                    ClienteId = cliente.Id,
                    Data = venda.Data,
                    CustoPacote = custoPacote,
                    Itens = await Itens(cliente, venda),
                });
            }

            await db.SaveChangesAsync();

            return Ok(new
            {
                cargaId = carga.Id,
                vendas = lido.Vendas.Count,
                clientes = lido.Vendas.Select(v => v.Cliente).Distinct().Count(),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha importando planilha de produtosB");
            return BadRequest(new { erro = $"Não consegui importar: {ex.Message}" });
        }
    }
}

public record PrecoImportado(string Nome, decimal Preco);
