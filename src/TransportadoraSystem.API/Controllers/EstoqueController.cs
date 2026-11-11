using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Estoque de produtosA e produtosB.
///
/// A carga define o CUSTO e muda a cada pedido; o cliente define o PREÇO que
/// paga. A diferença entre os dois é a comissão. Preço e custo são copiados
/// para dentro da venda quando ela é lançada, senão mudar o preço de um
/// cliente reescreveria a comissão de todas as vendas passadas dele.
/// </summary>
[Authorize]
[ApiController]
[Route("api/estoque")]
public class EstoqueController(AppDbContext db, RomaneioEstoqueService romaneios, EstoqueVendasService vendas,
    EmbaladosService embalados, ProdutosASaidaService saidas, PortalProdutosAHistoricoService historico,
    ProdutosCReceberService produtosC, ViagensCaminhaoService viagens, SicoobService sicoob,
    ProdutosCRomaneioService produtosCRomaneio, AuthService auth) : ControllerBase
{
    // ── Clientes e preços ─────────────────────────────────────────────────

    [HttpGet("clientes")]
    public async Task<IActionResult> Clientes()
    {
        var clientes = await db.EstoqueClientes
            .Include(c => c.PrecosProdutoB)
            .OrderBy(c => c.Nome)
            .ToListAsync();

        return Ok(clientes.Select(c => new
        {
            c.Id,
            c.Nome,
            c.Cidade,
            c.PrecoProdutoAKg,
            c.FreteProdutoB,
            freteProdutoC = c.FreteDaProdutoC,
            c.Ativo,
            precos = c.PrecosProdutoB.Select(p => new { p.TipoProdutoBId, p.Preco }),
        }));
    }

    [HttpPost("clientes")]
    public async Task<IActionResult> CriarCliente([FromBody] ClienteEstoqueDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome))
            return BadRequest(new { erro = "Informe o nome do cliente." });

        var cliente = new EstoqueCliente
        {
            Nome = dto.Nome.Trim().ToUpperInvariant(),
            Cidade = dto.Cidade,
            PrecoProdutoAKg = dto.PrecoProdutoAKg,
            FreteProdutoB = dto.FreteProdutoB ?? 0,
            FreteProdutoC = dto.FreteProdutoC ?? dto.FreteProdutoB ?? 0,
            Ativo = dto.Ativo ?? true,
        };
        db.EstoqueClientes.Add(cliente);
        await db.SaveChangesAsync();

        await SalvarPrecos(cliente.Id, dto.Precos);
        return Created($"/api/estoque/clientes/{cliente.Id}", new { cliente.Id });
    }

    [HttpPut("clientes/{id}")]
    public async Task<IActionResult> AtualizarCliente(int id, [FromBody] ClienteEstoqueDto dto)
    {
        var cliente = await db.EstoqueClientes.FindAsync(id);
        if (cliente == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Nome)) cliente.Nome = dto.Nome.Trim().ToUpperInvariant();
        cliente.Cidade = dto.Cidade;
        // Cliente que estava sem preço por kg (ex.: cadastrado pelo portal, EST-09): o preço novo vale
        // para as saídas dele que ficaram a R$ 0. As que já tinham preço continuam congeladas.
        if (dto.PrecoProdutoAKg > 0 && !EmbaladosService.EhEmbalado(cliente.Nome))
            await db.VendasProdutoA.Where(v => v.ClienteId == id && v.PrecoKg == 0)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.PrecoKg, dto.PrecoProdutoAKg).SetProperty(v => v.AtualizadoEm, DateTime.UtcNow));
        cliente.PrecoProdutoAKg = dto.PrecoProdutoAKg;
        if (dto.FreteProdutoB.HasValue) cliente.FreteProdutoB = dto.FreteProdutoB.Value;
        if (dto.FreteProdutoC.HasValue) cliente.FreteProdutoC = dto.FreteProdutoC.Value;
        if (dto.Ativo.HasValue) cliente.Ativo = dto.Ativo.Value;
        cliente.AtualizadoEm = DateTime.UtcNow;

        await SalvarPrecos(id, dto.Precos);
        await db.SaveChangesAsync();
        return Ok(new { cliente.Id });
    }

    private async Task SalvarPrecos(int clienteId, List<PrecoTipoDto>? precos)
    {
        if (precos == null) return;

        var atuais = await db.EstoqueClientePrecos
            .Where(p => p.ClienteId == clienteId).ToListAsync();

        foreach (var item in precos)
        {
            var atual = atuais.FirstOrDefault(a => a.TipoProdutoBId == item.TipoProdutoBId);
            if (atual == null)
                db.EstoqueClientePrecos.Add(new EstoqueClientePreco
                {
                    ClienteId = clienteId,
                    TipoProdutoBId = item.TipoProdutoBId,
                    Preco = item.Preco,
                });
            else
            {
                atual.Preco = item.Preco;
                atual.AtualizadoEm = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync();
    }

    // ── Tipos de produtoB ────────────────────────────────────────────────

    [HttpGet("tipos")]
    public async Task<IActionResult> Tipos() =>
        Ok(await db.TiposProdutoB.Where(t => t.Ativo).OrderBy(t => t.Id).ToListAsync());

    [HttpPost("tipos")]
    public async Task<IActionResult> CriarTipo([FromBody] TipoProdutoBDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome))
            return BadRequest(new { erro = "Informe o nome do tipo." });

        var tipo = new TipoProdutoB
        {
            Nome = dto.Nome.Trim().ToUpperInvariant(),
            UnidadesPorPacote = dto.UnidadesPorPacote > 0 ? dto.UnidadesPorPacote : 150,
            Produto = ProdutoPacote.Normalizar(dto.Produto),
        };
        db.TiposProdutoB.Add(tipo);
        await db.SaveChangesAsync();
        return Created($"/api/estoque/tipos/{tipo.Id}", tipo);
    }

    [HttpPut("tipos/{id}")]
    public async Task<IActionResult> AtualizarTipo(int id, [FromBody] TipoProdutoBDto dto)
    {
        var tipo = await db.TiposProdutoB.FindAsync(id);
        if (tipo == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Nome)) tipo.Nome = dto.Nome.Trim().ToUpperInvariant();
        if (dto.UnidadesPorPacote > 0) tipo.UnidadesPorPacote = dto.UnidadesPorPacote;
        if (dto.Ativo.HasValue) tipo.Ativo = dto.Ativo.Value;
        tipo.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Ok(tipo);
    }

    // ── ProdutosA ───────────────────────────────────────────────────────────

    [HttpGet("produtosA")]
    public async Task<IActionResult> ProdutosA()
    {
        var cargas = await db.CargasProdutoA
            .Include(c => c.Vendas).ThenInclude(v => v.ProdutosA)
            .OrderByDescending(c => c.Data).ThenByDescending(c => c.Id)
            .ToListAsync();

        return Ok(cargas.Select(ResumoCarga));
    }

    [HttpGet("produtosA/{id}")]
    public async Task<IActionResult> ProdutoA(int id)
    {
        var carga = await db.CargasProdutoA
            .Include(c => c.Vendas).ThenInclude(v => v.ProdutosA)
            .Include(c => c.Vendas).ThenInclude(v => v.Cliente)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (carga == null) return NotFound();

        // Sugestão de nº do romaneio: o último que esse cliente recebeu, + 1.
        var ultimoPorCliente = await db.VendasProdutoA
            .Where(v => v.Numero != null)
            .GroupBy(v => v.ClienteId)
            .Select(g => new { ClienteId = g.Key, Ultimo = g.Max(v => v.Numero) })
            .ToDictionaryAsync(x => x.ClienteId, x => x.Ultimo ?? 0);

        // Anotadas no celular (EST-09): quem anotou e as marcas do histórico
        var anotadas = carga.Vendas.Where(v => v.AnotadaPorUsuarioId != null).ToList();
        var idsQuem = anotadas.Select(v => v.AnotadaPorUsuarioId!.Value).Distinct().ToList();
        var nomes = idsQuem.Count == 0 ? new Dictionary<int, string>()
            : await db.Usuarios.Where(u => idsQuem.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nome);
        var (semLote, depoisDoRomaneio) = await VendasMarcadasAsync(anotadas.Select(v => v.Id).ToList());

        var vendas = carga.Vendas
            .OrderByDescending(v => v.Data).ThenByDescending(v => v.Id)
            .Select(v =>
            {
                decimal pesoKg = v.ProdutosA.Sum(b => b.PesoGramas) / 1000m;
                decimal valor = pesoKg * v.PrecoKg;
                decimal custo = pesoKg * v.CustoKg;
                return new
                {
                    v.Id,
                    v.Data,
                    v.ClienteId,
                    cliente = v.Cliente?.Nome ?? "",
                    v.Espessura,
                    v.Numero,
                    proximoNumero = ultimoPorCliente.TryGetValue(v.ClienteId, out var u) ? u + 1 : (int?)null,
                    v.PrecoKg,
                    v.CustoKg,
                    v.Observacao,
                    qtdeProdutosA = v.ProdutosA.Count,
                    pesoKg,
                    valor,
                    custo,
                    comissao = valor - custo,
                    produtosA = v.ProdutosA.OrderBy(b => b.Id)
                                       .Select(b => new { b.Id, b.PesoGramas }),
                    anotadaPor = v.AnotadaPorUsuarioId == null ? null
                        : nomes.TryGetValue(v.AnotadaPorUsuarioId.Value, out var nome) ? nome : "conta apagada",
                    v.AnotadaEm,
                    semLoteEmAberto = semLote.Contains(v.Id),
                    alteradaDepoisDoRomaneio = depoisDoRomaneio.Contains(v.Id),
                };
            });

        return Ok(new { carga = ResumoCarga(carga), vendas });
    }

    private static object ResumoCarga(CargaProdutoA c)
    {
        int vendidas = c.Vendas.Sum(v => v.ProdutosA.Count);
        decimal pesoKg = c.Vendas.Sum(v => v.ProdutosA.Sum(b => b.PesoGramas)) / 1000m;
        decimal custo = c.Vendas.Sum(v => v.ProdutosA.Sum(b => b.PesoGramas) / 1000m * v.CustoKg);
        decimal valor = c.Vendas.Sum(v => v.ProdutosA.Sum(b => b.PesoGramas) / 1000m * v.PrecoKg);

        return new
        {
            c.Id,
            c.Data,
            c.Fornecedor,
            c.Quantidade,
            c.ValorTotal,
            c.CustoKg,
            c.Controle,
            c.Observacao,
            c.Encerrada,
            vendidas,
            estoque = c.Quantidade - vendidas,
            pesoKg,
            // O que já voltou do valor da carga é a soma dos custos, igual à
            // planilha: a comissão não abate o pedido.
            recuperado = custo,
            aReceber = c.ValorTotal - custo,
            valorVendido = valor,
            comissao = valor - custo,
            totalVendas = c.Vendas.Count,
        };
    }

    [HttpPost("produtosA/cargas")]
    public async Task<IActionResult> CriarCargaProdutoA([FromBody] CargaProdutoADto dto)
    {
        if (dto.Quantidade <= 0) return BadRequest(new { erro = "Informe a quantidade de produtosA." });
        if (dto.CustoKg <= 0) return BadRequest(new { erro = "Informe o custo por kg da carga." });

        var carga = new CargaProdutoA
        {
            Data = dto.Data,
            Fornecedor = (dto.Fornecedor ?? "").Trim().ToUpperInvariant(),
            Quantidade = dto.Quantidade,
            ValorTotal = dto.ValorTotal,
            CustoKg = dto.CustoKg,
            Controle = dto.Controle,
            Observacao = dto.Observacao,
        };
        db.CargasProdutoA.Add(carga);
        await db.SaveChangesAsync();
        return Created($"/api/estoque/produtosA/{carga.Id}", new { carga.Id });
    }

    [HttpPut("produtosA/cargas/{id}")]
    public async Task<IActionResult> AtualizarCargaProdutoA(int id, [FromBody] CargaProdutoADto dto)
    {
        var carga = await db.CargasProdutoA.FindAsync(id);
        if (carga == null) return NotFound();

        carga.Data = dto.Data;
        carga.Fornecedor = (dto.Fornecedor ?? "").Trim().ToUpperInvariant();
        carga.Quantidade = dto.Quantidade;
        carga.ValorTotal = dto.ValorTotal;
        // Custo por kg é da carga (cada controle de produtosA): corrigido, vale para as saídas dela (VOLUME-20)
        if (dto.CustoKg <= 0) return BadRequest(new { erro = "Informe o custo por kg da carga." });
        var custoMudou = carga.CustoKg != dto.CustoKg;
        if (custoMudou)
            await db.VendasProdutoA.Where(v => v.CargaId == carga.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CustoKg, dto.CustoKg).SetProperty(v => v.AtualizadoEm, DateTime.UtcNow));
        carga.CustoKg = dto.CustoKg;
        carga.Controle = dto.Controle;
        carga.Observacao = dto.Observacao;
        if (dto.Encerrada.HasValue) carga.Encerrada = dto.Encerrada.Value;
        carga.AtualizadoEm = DateTime.UtcNow;

        await db.SaveChangesAsync();
        if (custoMudou) await embalados.SincronizarProdutosAEmbaladoAsync();
        return Ok(new { carga.Id });
    }

    [HttpDelete("produtosA/cargas/{id}")]
    public async Task<IActionResult> ExcluirCargaProdutoA(int id)
    {
        var carga = await db.CargasProdutoA.Include(c => c.Vendas).FirstOrDefaultAsync(c => c.Id == id);
        if (carga == null) return NotFound();
        if (carga.Vendas.Count > 0)
            return BadRequest(new { erro = $"A carga tem {carga.Vendas.Count} venda(s) lançada(s). Apague as vendas primeiro." });

        db.CargasProdutoA.Remove(carga);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("produtosA/vendas")]
    public async Task<IActionResult> CriarVendaProdutoA([FromBody] VendaProdutoADto dto)
    {
        var carga = await db.CargasProdutoA.FindAsync(dto.CargaId);
        if (carga == null) return BadRequest(new { erro = "Carga não encontrada." });

        var cliente = await db.EstoqueClientes.FindAsync(dto.ClienteId);
        if (cliente == null) return BadRequest(new { erro = "Cliente não encontrado." });

        var pesos = (dto.Pesos ?? []).Where(p => p > 0).ToList();
        if (pesos.Count == 0)
            return BadRequest(new { erro = "Informe o peso de pelo menos uma produtoA." });
        if (dto.PrecoKg < 0) return BadRequest(new { erro = "Preço negativo." });

        // Preço do cliente (embalado pelo custo, VOLUME-20), encerra a carga na última produtoA
        var (venda, acabou) = await saidas.CriarAsync(carga, cliente, dto.Data, dto.Espessura, dto.Observacao, pesos);
        // Preço digitado na saída (EST-12): vale só para esta saída, não muda o cadastro do cliente
        if (dto.PrecoKg is > 0) { venda.PrecoKg = dto.PrecoKg.Value; await db.SaveChangesAsync(); }

        return Created($"/api/estoque/produtosA/{carga.Id}", new { venda.Id, cargaEncerrada = acabou });
    }

    /// <summary>
    /// Edita a saída pelo escritório: data, cliente, espessura, observação e pesos. A carga é a
    /// mesma (o CargaId do corpo é ignorado). Trocou o cliente: preço do novo (embalado pelo custo).
    /// Venda anotada no celular (EST-09): grava antes/depois no histórico do dono.
    /// </summary>
    [HttpPut("produtosA/vendas/{id}")]
    public async Task<IActionResult> AlterarVendaProdutoA(int id, [FromBody] VendaProdutoADto dto)
    {
        var venda = await db.VendasProdutoA.Include(v => v.ProdutosA).Include(v => v.Cliente).Include(v => v.Carga)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (venda == null) return NotFound();

        var cliente = venda.ClienteId == dto.ClienteId ? venda.Cliente
            : await db.EstoqueClientes.FindAsync(dto.ClienteId);
        if (cliente == null) return BadRequest(new { erro = "Cliente não encontrado." });

        var pesos = (dto.Pesos ?? []).Where(p => p > 0).ToList();
        if (pesos.Count == 0)
            return BadRequest(new { erro = "Informe o peso de pelo menos uma produtoA." });

        var pesosAntes = venda.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();
        var antes = new { data = venda.Data, cliente = venda.Cliente?.Nome, pesos = pesosAntes };
        bool mudouAnotacao = dto.Data != venda.Data || cliente.Id != venda.ClienteId || !pesosAntes.SequenceEqual(pesos);

        await using var tx = await db.Database.BeginTransactionAsync();
        venda.Espessura = dto.Espessura;
        venda.Observacao = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim();
        // Preço (só se trocou o cliente), carga reabre/encerra pela contagem, embalado sincronizado
        await saidas.AlterarAsync(venda, dto.Data, cliente, pesos);
        if (dto.PrecoKg is > 0) venda.PrecoKg = dto.PrecoKg.Value;

        if (venda.AnotadaPorUsuarioId != null && mudouAnotacao)
        {
            var quemAnotou = await db.Usuarios.Where(u => u.Id == venda.AnotadaPorUsuarioId)
                .Select(u => u.Nome).FirstOrDefaultAsync();
            var eu = AuthController.UsuarioId(User);
            var meuNome = eu == null ? "" : await db.Usuarios.Where(u => u.Id == eu).Select(u => u.Nome).FirstOrDefaultAsync() ?? "";
            historico.Adicionar(eu, meuNome, PortalProdutosAHistoricoService.AnotacaoEditadaNoSistema,
                $"Alterou no sistema a anotação de {quemAnotou ?? "?"}: {antes.cliente}, {antes.data:dd/MM/yyyy}",
                venda.Id, antes,
                depois: new { data = dto.Data, cliente = cliente.Nome, pesos },
                ip: HttpContext.Connection.RemoteIpAddress?.ToString());
            await db.SaveChangesAsync();
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(new { venda.Id, cargaEncerrada = venda.Carga?.Encerrada ?? false });
    }

    [HttpDelete("produtosA/vendas/{id}")]
    public async Task<IActionResult> ExcluirVendaProdutoA(int id)
    {
        var venda = await db.VendasProdutoA.FindAsync(id);
        if (venda == null) return NotFound();

        if (venda.AnotadaPorUsuarioId == null)
        {
            await saidas.ExcluirAsync(venda);
            return NoContent();
        }

        // Anotada no celular (EST-09): o histórico do dono guarda o retrato de antes, na mesma transação
        await db.Entry(venda).Collection(v => v.ProdutosA).LoadAsync();
        await db.Entry(venda).Reference(v => v.Cliente).LoadAsync();
        var quemAnotou = await db.Usuarios.Where(u => u.Id == venda.AnotadaPorUsuarioId)
            .Select(u => u.Nome).FirstOrDefaultAsync();
        var eu = AuthController.UsuarioId(User);
        var meuNome = eu == null ? "" : await db.Usuarios.Where(u => u.Id == eu).Select(u => u.Nome).FirstOrDefaultAsync() ?? "";
        var pesosAntes = venda.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();

        await using var tx = await db.Database.BeginTransactionAsync();
        historico.Adicionar(eu, meuNome, PortalProdutosAHistoricoService.AnotacaoExcluidaNoSistema,
            $"Excluiu no sistema a anotação de {quemAnotou ?? "?"}: {venda.Cliente?.Nome}, {venda.Data:dd/MM/yyyy}, {pesosAntes.Count} produtoA(s)",
            venda.Id,
            antes: new { data = venda.Data, cliente = venda.Cliente?.Nome, pesos = pesosAntes, anotadaPor = quemAnotou, numero = venda.Numero },
            ip: HttpContext.Connection.RemoteIpAddress?.ToString());
        await saidas.ExcluirAsync(venda);
        await tx.CommitAsync();
        return NoContent();
    }

    /// <summary>
    /// Avisos do topo da tela de ProdutosA: clientes sem preço por kg com saída a R$ 0 (o preço se acerta
    /// no cadastro do cliente e passa para essas saídas) e anotações do celular que caíram sem lote em aberto.
    /// </summary>
    [HttpGet("produtosA/avisos")]
    public async Task<IActionResult> AvisosProdutosA()
    {
        var semPreco = (await db.VendasProdutoA
                .Where(v => v.PrecoKg == 0 && v.Cliente!.PrecoProdutoAKg == 0)
                .GroupBy(v => new { v.ClienteId, v.Cliente!.Nome })
                .Select(g => new { id = g.Key.ClienteId, nome = g.Key.Nome, saidas = g.Count() })
                .ToListAsync())
            .Where(c => !EmbaladosService.EhEmbalado(c.nome))
            .OrderBy(c => c.nome)
            .ToList();

        // Sem lote em aberto: nenhuma carga aberta e a mais recente recebeu anotação do celular assim
        int? cargaSemLote = null;
        if (!await db.CargasProdutoA.AnyAsync(c => !c.Encerrada))
        {
            var ultima = await db.CargasProdutoA.OrderByDescending(c => c.Data).ThenByDescending(c => c.Id)
                .Select(c => (int?)c.Id).FirstOrDefaultAsync();
            if (ultima != null)
            {
                var ids = await db.VendasProdutoA.Where(v => v.CargaId == ultima && v.AnotadaPorUsuarioId != null)
                    .Select(v => v.Id).ToListAsync();
                var marcadas = await VendasMarcadasAsync(ids);
                if (marcadas.SemLote.Count > 0) cargaSemLote = ultima;
            }
        }

        return Ok(new { clientesSemPreco = semPreco, cargaSemLoteEmAberto = cargaSemLote });
    }

    /// <summary>
    /// Marcas tiradas do histórico do portal (sem coluna nova): anotação criada sem lote em aberto e
    /// anotação alterada no celular depois de exportado o romaneio.
    /// </summary>
    private async Task<(HashSet<int> SemLote, HashSet<int> DepoisDoRomaneio)> VendasMarcadasAsync(List<int> vendaIds)
    {
        var semLote = new HashSet<int>();
        var depois = new HashSet<int>();
        if (vendaIds.Count == 0) return (semLote, depois);

        var linhas = await db.PortalProdutosAHistorico
            .Where(h => h.VendaProdutoAId != null && vendaIds.Contains(h.VendaProdutoAId.Value)
                     && (h.Acao == PortalProdutosAHistoricoService.AnotacaoCriada || h.Acao == PortalProdutosAHistoricoService.AnotacaoEditada)
                     && h.Depois != null)
            .Select(h => new { h.VendaProdutoAId, h.Acao, h.Depois })
            .ToListAsync();
        foreach (var l in linhas)
        {
            if (l.Acao == PortalProdutosAHistoricoService.AnotacaoCriada && l.Depois!.Contains("\"semLoteEmAberto\":true"))
                semLote.Add(l.VendaProdutoAId!.Value);
            if (l.Acao == PortalProdutosAHistoricoService.AnotacaoEditada && l.Depois!.Contains("\"depoisDoRomaneio\":true"))
                depois.Add(l.VendaProdutoAId!.Value);
        }
        return (semLote, depois);
    }

    // ── ProdutosB ────────────────────────────────────────────────────────

    // ProdutosB e produtosC têm cargas e saídas próprias (EST-07): ?produto=PRODUTOC
    [HttpGet("produtosB")]
    public async Task<IActionResult> ProdutosB([FromQuery] string? produto)
    {
        var tipos = await db.TiposProdutoB.ToListAsync();
        var qual = ProdutoPacote.Normalizar(produto);
        var cargas = await db.CargasProdutoB
            .Include(c => c.Itens)
            .Include(c => c.Vendas).ThenInclude(v => v.Itens)
            .Where(c => c.Produto == qual)
            .OrderByDescending(c => c.Data).ThenByDescending(c => c.Id)
            .ToListAsync();

        return Ok(cargas.Select(c => ResumoCargaEmb(c, tipos)));
    }

    [HttpGet("produtosB/{id}")]
    public async Task<IActionResult> ProdutoB(int id)
    {
        var tipos = await db.TiposProdutoB.ToListAsync();
        var carga = await db.CargasProdutoB
            .Include(c => c.Itens)
            .Include(c => c.Vendas).ThenInclude(v => v.Itens)
            .Include(c => c.Vendas).ThenInclude(v => v.Cliente)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (carga == null) return NotFound();

        var ultimoPorCliente = await db.VendasProdutoB
            .Where(v => v.Numero != null)
            .GroupBy(v => v.ClienteId)
            .Select(g => new { ClienteId = g.Key, Ultimo = g.Max(v => v.Numero) })
            .ToDictionaryAsync(x => x.ClienteId, x => x.Ultimo ?? 0);

        var vendas = carga.Vendas
            .OrderByDescending(v => v.Data).ThenByDescending(v => v.Id)
            .Select(v =>
            {
                decimal pacotes = v.Itens.Sum(i => Equivalente(i, tipos));
                decimal valor = v.Itens.Sum(i => Equivalente(i, tipos) * i.Preco);
                decimal custo = pacotes * v.CustoPacote;
                decimal frete = pacotes * v.FretePacote;
                return new
                {
                    v.Id,
                    v.Data,
                    v.ClienteId,
                    cliente = v.Cliente?.Nome ?? "",
                    v.Numero,
                    proximoNumero = ultimoPorCliente.TryGetValue(v.ClienteId, out var u) ? u + 1 : (int?)null,
                    v.CustoPacote,
                    v.FretePacote,
                    frete,
                    totalCliente = valor + frete,
                    v.Observacao,
                    // De onde veio: controle de carregamento, uso nos embalados ou à mão
                    origem = v.CarregamentoId != null ? "controle" : v.VolumesControleId != null ? "embalados" : "mao",
                    v.CobradaNoRomaneio,
                    pacotes,
                    valor,
                    custo,
                    comissao = valor - custo,
                    itens = v.Itens.Select(i => new
                    {
                        i.TipoProdutoBId,
                        tipo = tipos.FirstOrDefault(t => t.Id == i.TipoProdutoBId)?.Nome ?? "",
                        i.Pacotes,
                        i.Unidades,
                        i.Preco,
                        equivalente = Equivalente(i, tipos),
                    }),
                };
            });

        return Ok(new { carga = ResumoCargaEmb(carga, tipos), vendas });
    }

    // Pacotes cheios + as avulsas convertidas (150 unidades fecham um pacote)
    private static decimal Equivalente(VendaProdutoBItem item, List<TipoProdutoB> tipos)
    {
        int porPacote = tipos.FirstOrDefault(t => t.Id == item.TipoProdutoBId)?.UnidadesPorPacote ?? 150;
        if (porPacote <= 0) porPacote = 150;
        return item.Pacotes + (decimal)item.Unidades / porPacote;
    }

    private static object ResumoCargaEmb(CargaProdutoB c, List<TipoProdutoB> tipos)
    {
        decimal custoPacote = c.QuantidadePacotes > 0
            ? (c.ValorProdutosB + c.ValorFrete) / c.QuantidadePacotes : 0;

        var porTipo = c.Itens.Select(item =>
        {
            decimal saida = c.Vendas
                .SelectMany(v => v.Itens)
                .Where(i => i.TipoProdutoBId == item.TipoProdutoBId)
                .Sum(i => Equivalente(i, tipos));

            return new
            {
                tipoId = item.TipoProdutoBId,
                tipo = tipos.FirstOrDefault(t => t.Id == item.TipoProdutoBId)?.Nome ?? "",
                entrada = item.Quantidade,
                saida,
                estoque = item.Quantidade - saida,
            };
        }).ToList();

        decimal vendidos = c.Vendas.SelectMany(v => v.Itens).Sum(i => Equivalente(i, tipos));
        decimal valor = c.Vendas.SelectMany(v => v.Itens).Sum(i => Equivalente(i, tipos) * i.Preco);
        decimal custo = c.Vendas.Sum(v => v.Itens.Sum(i => Equivalente(i, tipos)) * v.CustoPacote);

        return new
        {
            c.Id,
            c.Data,
            produto = ProdutoPacote.Normalizar(c.Produto),
            c.Fornecedor,
            c.QuantidadePacotes,
            c.ValorProdutosB,
            c.ValorFrete,
            c.Transportador,
            c.Observacao,
            c.Encerrada,
            valorTotal = c.ValorProdutosB + c.ValorFrete,
            custoPacote,
            entrada = porTipo.Sum(t => t.entrada),
            vendidos,
            estoque = porTipo.Sum(t => t.estoque),
            porTipo,
            recuperado = custo,
            aReceber = c.ValorProdutosB + c.ValorFrete - custo,
            valorVendido = valor,
            comissao = valor - custo,
            totalVendas = c.Vendas.Count,
        };
    }

    [HttpPost("produtosB/cargas")]
    public async Task<IActionResult> CriarCargaEmb([FromBody] CargaProdutoBDto dto)
    {
        if (dto.QuantidadePacotes <= 0)
            return BadRequest(new { erro = "Informe a quantidade de pacotes comprados." });

        var produto = ProdutoPacote.Normalizar(dto.Produto);
        var tiposDoProduto = await db.TiposProdutoB.Where(t => t.Produto == produto).Select(t => t.Id).ToListAsync();
        if ((dto.Itens ?? []).Any(i => i.Quantidade > 0 && !tiposDoProduto.Contains(i.TipoProdutoBId)))
            return BadRequest(new { erro = produto == ProdutoPacote.ProdutoC ? "A carga de produtosC só leva produtosC." : "A carga de produtosB não leva produtosC." });

        var carga = new CargaProdutoB
        {
            Data = dto.Data,
            Produto = produto,
            Fornecedor = (dto.Fornecedor ?? "").Trim().ToUpperInvariant(),
            QuantidadePacotes = dto.QuantidadePacotes,
            ValorProdutosB = dto.ValorProdutosB,
            ValorFrete = dto.ValorFrete,
            Transportador = dto.Transportador,
            Observacao = dto.Observacao,
            Itens = (dto.Itens ?? []).Where(i => i.Quantidade > 0).Select(i => new CargaProdutoBItem
            {
                TipoProdutoBId = i.TipoProdutoBId,
                Quantidade = i.Quantidade,
            }).ToList(),
        };
        db.CargasProdutoB.Add(carga);
        await db.SaveChangesAsync();
        return Created($"/api/estoque/produtosB/{carga.Id}", new { carga.Id });
    }

    [HttpPut("produtosB/cargas/{id}")]
    public async Task<IActionResult> AtualizarCargaEmb(int id, [FromBody] CargaProdutoBDto dto)
    {
        var carga = await db.CargasProdutoB.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Id == id);
        if (carga == null) return NotFound();

        carga.Data = dto.Data;
        carga.Fornecedor = (dto.Fornecedor ?? "").Trim().ToUpperInvariant();
        carga.QuantidadePacotes = dto.QuantidadePacotes;
        carga.ValorProdutosB = dto.ValorProdutosB;
        carga.ValorFrete = dto.ValorFrete;
        carga.Transportador = dto.Transportador;
        carga.Observacao = dto.Observacao;
        if (dto.Encerrada.HasValue) carga.Encerrada = dto.Encerrada.Value;
        carga.AtualizadoEm = DateTime.UtcNow;

        foreach (var item in dto.Itens ?? [])
        {
            var atual = carga.Itens.FirstOrDefault(i => i.TipoProdutoBId == item.TipoProdutoBId);
            if (atual == null)
            {
                if (item.Quantidade > 0)
                    db.CargaProdutoBItens.Add(new CargaProdutoBItem
                    {
                        CargaId = carga.Id,
                        TipoProdutoBId = item.TipoProdutoBId,
                        Quantidade = item.Quantidade,
                    });
            }
            else
            {
                atual.Quantidade = item.Quantidade;
                atual.AtualizadoEm = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { carga.Id });
    }

    [HttpDelete("produtosB/cargas/{id}")]
    public async Task<IActionResult> ExcluirCargaEmb(int id)
    {
        var carga = await db.CargasProdutoB.Include(c => c.Vendas).FirstOrDefaultAsync(c => c.Id == id);
        if (carga == null) return NotFound();
        if (carga.Vendas.Count > 0)
            return BadRequest(new { erro = $"A carga tem {carga.Vendas.Count} venda(s) lançada(s). Apague as vendas primeiro." });

        db.CargasProdutoB.Remove(carga);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("produtosB/vendas")]
    public async Task<IActionResult> CriarVendaEmb([FromBody] VendaProdutoBDto dto)
    {
        try
        {
            var r = await vendas.CriarVendaAsync(new VendaNova(dto.CargaId, dto.ClienteId, dto.Data,
                dto.Itens?.Select(i => new ItemVendaNova(i.TipoProdutoBId, i.Pacotes, i.Unidades)).ToList(), dto.Observacao));
            return Created($"/api/estoque/produtosB/{dto.CargaId}", new { r.Id, cargaEncerrada = r.CargaEncerrada });
        }
        catch (ErroEstoque ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // Uso das produtosB nos volumes embalados (VOLUME-04): todos os controles. Adota as
    // saídas lançadas à mão no EMBALADOS no mesmo dia; rodar de novo não duplica.
    [HttpPost("produtosB/sincronizar-embalados")]
    public async Task<IActionResult> SincronizarEmbalados() => Ok(await vendas.SincronizarTodosEmbaladosAsync());

    [HttpDelete("produtosB/vendas/{id}")]
    public async Task<IActionResult> ExcluirVendaEmb(int id)
    {
        var venda = await db.VendasProdutoB.FindAsync(id);
        if (venda == null) return NotFound();
        db.VendasProdutoB.Remove(venda);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── ProdutosC a receber (EST-14/EST-08): sem carga, só vendas; tela em Boletos → A receber ──

    [HttpGet("produtosC")]
    public async Task<IActionResult> ProdutosC() => Ok(await produtosC.ListarAsync());

    [HttpPut("produtosC/vendas/{id}")]
    public async Task<IActionResult> AlterarVendaProdutoC(int id, [FromBody] VendaProdutoCEdicao dto)
    {
        try { await vendas.AlterarVendaProdutoCAsync(id, dto); return Ok(new { id }); }
        catch (ErroEstoque ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // Recebida à mão (data) ou volta para "a receber" (sem data)
    [HttpPost("produtosC/vendas/{id}/recebido")]
    public async Task<IActionResult> MarcarProdutoCRecebida(int id, [FromBody] RecebidoDto dto)
    {
        try { await produtosC.MarcarAsync(id, dto.RecebidoEm); return Ok(new { id }); }
        catch (ErroEstoque ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // Histórico: cria a venda de produtoC que falta de cada controle de carregamento guardado (não duplica nem apaga)
    [HttpPost("produtosC/preencher-historico")]
    public async Task<IActionResult> PreencherHistoricoProdutosC() => Ok(await viagens.PreencherProdutosCAsync());

    // Gravar as produtosC (histórico e baixa pelo Pix) é só do dono; a prévia (aplicar=false) qualquer um vê
    private async Task<bool> EhDonoAsync()
    {
        var id = AuthController.UsuarioId(User);
        return id != null && await auth.EhDonoAsync(id.Value);
    }

    // Histórico pelos romaneios da BRZ guardados (EST-08): uma venda a receber por dia de carga, com a quantidade e o
    // preço da linha ProdutosC. aplicar=false só mostra o que faria (nada é gravado); rodar de novo não duplica.
    [HttpPost("produtosC/preencher-romaneios")]
    public async Task<IActionResult> PreencherProdutosCRomaneios([FromQuery] DateOnly? desde, [FromQuery] bool aplicar = false)
    {
        if (aplicar && !await EhDonoAsync()) return StatusCode(403, new { erro = "Só o dono grava as produtosC a receber." });
        return Ok(await produtosCRomaneio.PreencherAsync(desde ?? new DateOnly(2026, 10, 1), aplicar));
    }

    // Pix da BRZ no extrato (padrão 40 dias; desde= amplia) × produtosC em aberto: baixa só a combinação única; dúvida e
    // sem soma voltam para a tela. aplicar=false só mostra (nada é gravado).
    [HttpPost("produtosC/conferir-pix")]
    public async Task<IActionResult> ConferirPixProdutosC([FromQuery] DateOnly? desde, CancellationToken ct, [FromQuery] bool aplicar = true)
    {
        if (aplicar && !await EhDonoAsync()) return StatusCode(403, new { erro = "Só o dono dá baixa nas produtosC." });
        if (sicoob.FaltaConfigurar() is { } falta) return Ok(new ProdutosCReceberService.ResultadoPix([], 0, falta));
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var inicio = desde is { } d && d < hoje ? (d < hoje.AddDays(-400) ? hoje.AddDays(-400) : d) : hoje.AddDays(-40);
        var (extrato, erro) = await sicoob.ExtratoAsync(inicio, hoje, ct);
        if (extrato == null) return Ok(new ProdutosCReceberService.ResultadoPix([], 0, erro));
        return Ok(await produtosC.ConferirPixAsync(ConferenciaAutomaticaService.PixDoExtrato(extrato.Lancamentos), aplicar, ct));
    }

    // Dúvida escolhida na tela: estas vendas foram pagas por este Pix
    [HttpPost("produtosC/baixar-pix")]
    public async Task<IActionResult> BaixarPixProdutosC([FromBody] BaixaPixProdutosCDto dto)
    {
        if (!await EhDonoAsync()) return StatusCode(403, new { erro = "Só o dono dá baixa nas produtosC." });
        if (string.IsNullOrWhiteSpace(dto.Chave) || dto.VendaIds is not { Count: > 0 }) return BadRequest(new { erro = "Escolha as vendas." });
        try { return Ok(new { marcadas = await produtosC.BaixarAsync(dto.VendaIds, dto.Data, dto.Chave) }); }
        catch (ErroEstoque ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // ── Romaneio da venda em Excel ────────────────────────────────────────
    // Mesmo modelo preenchido à mão hoje, com as duas vias. O número informado
    // fica gravado na venda, então reexportar sai com o mesmo número.

    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("produtosA/vendas/{id}/excel")]
    public async Task<IActionResult> ExportarVendaProdutoA(int id, [FromQuery] int? numero)
    {
        var venda = await db.VendasProdutoA
            .Include(v => v.ProdutosA).Include(v => v.Cliente)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (venda?.Cliente == null) return NotFound();

        if (numero is > 0 && numero != venda.Numero)
        {
            venda.Numero = numero;
            venda.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var (bytes, nome) = romaneios.ProdutosA(venda, venda.Cliente);
        return File(bytes, Xlsx, nome);
    }

    [HttpGet("produtosB/vendas/{id}/excel")]
    public async Task<IActionResult> ExportarVendaProdutoB(int id, [FromQuery] int? numero)
    {
        var venda = await db.VendasProdutoB
            .Include(v => v.Itens).Include(v => v.Cliente)
            .FirstOrDefaultAsync(v => v.Id == id);
        if (venda?.Cliente == null) return NotFound();

        if (numero is > 0 && numero != venda.Numero)
        {
            venda.Numero = numero;
            venda.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var tipos = await db.TiposProdutoB.ToListAsync();
        var (bytes, nome) = romaneios.ProdutosB(venda, venda.Cliente, tipos);
        return File(bytes, Xlsx, nome);
    }

    // ── Comissões ─────────────────────────────────────────────────────────
    // Uma linha por venda, dos dois produtos, com a diferença entre o que o
    // cliente paga e o que custou.

    [HttpGet("comissoes")]
    public async Task<IActionResult> Comissoes([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate)
    {
        var tipos = await db.TiposProdutoB.ToListAsync();

        var produtosA = await db.VendasProdutoA
            .Include(v => v.ProdutosA).Include(v => v.Cliente).Include(v => v.Carga)
            .Where(v => (!de.HasValue || v.Data >= de.Value) && (!ate.HasValue || v.Data <= ate.Value))
            .ToListAsync();

        var produtosB = await db.VendasProdutoB
            .Include(v => v.Itens).Include(v => v.Cliente).Include(v => v.Carga)
            .Where(v => (!de.HasValue || v.Data >= de.Value) && (!ate.HasValue || v.Data <= ate.Value))
            .ToListAsync();

        var linhas = new List<LinhaComissao>();

        foreach (var v in produtosA)
        {
            decimal pesoKg = v.ProdutosA.Sum(b => b.PesoGramas) / 1000m;
            linhas.Add(new LinhaComissao("PRODUTOA", v.Data, v.Cliente?.Nome ?? "",
                v.Carga?.Fornecedor ?? "", $"{v.ProdutosA.Count} produtoA(s)", pesoKg, "kg",
                v.PrecoKg, v.CustoKg, v.PrecoKg - v.CustoKg, pesoKg * (v.PrecoKg - v.CustoKg)));
        }

        foreach (var v in produtosB)
            foreach (var i in v.Itens)
            {
                decimal eq = Equivalente(i, tipos);
                var produto = tipos.FirstOrDefault(t => t.Id == i.TipoProdutoBId)?.Produto == ProdutoPacote.ProdutoC ? "PRODUTOC" : "PRODUTOB";
                // ProdutoC não tem custo (EST-14): fica fora da comissão
                if (produto == "PRODUTOC") continue;
                linhas.Add(new LinhaComissao(produto, v.Data, v.Cliente?.Nome ?? "",
                    v.Carga?.Fornecedor ?? "",
                    tipos.FirstOrDefault(t => t.Id == i.TipoProdutoBId)?.Nome ?? "",
                    eq, "pct", i.Preco, v.CustoPacote, i.Preco - v.CustoPacote,
                    eq * (i.Preco - v.CustoPacote)));
            }

        return Ok(new
        {
            linhas = linhas.OrderByDescending(l => l.Data).ThenBy(l => l.Cliente),
            total = linhas.Sum(l => l.Comissao),
            totalProdutosA = linhas.Where(l => l.Produto == "PRODUTOA").Sum(l => l.Comissao),
            totalProdutosB = linhas.Where(l => l.Produto == "PRODUTOB").Sum(l => l.Comissao),
            totalProdutosC = linhas.Where(l => l.Produto == "PRODUTOC").Sum(l => l.Comissao),
        });
    }
}

public record LinhaComissao(string Produto, DateOnly Data, string Cliente, string Fornecedor,
    string Detalhe, decimal Quantidade, string Unidade, decimal Preco, decimal Custo,
    decimal Diferenca, decimal Comissao);

public record ClienteEstoqueDto(string Nome, string? Cidade, decimal PrecoProdutoAKg, bool? Ativo, List<PrecoTipoDto>? Precos,
    decimal? FreteProdutoB = null, decimal? FreteProdutoC = null);
public record PrecoTipoDto(int TipoProdutoBId, decimal Preco);
public record TipoProdutoBDto(string Nome, int UnidadesPorPacote, bool? Ativo, string? Produto = null);

public record CargaProdutoADto(DateOnly Data, string? Fornecedor, int Quantidade,
    decimal ValorTotal, decimal CustoKg, string? Controle, string? Observacao, bool? Encerrada);

public record VendaProdutoADto(int CargaId, int ClienteId, DateOnly Data, int? Espessura,
    List<int>? Pesos, string? Observacao, decimal? PrecoKg = null);

public record CargaProdutoBDto(DateOnly Data, string? Fornecedor, int QuantidadePacotes,
    decimal ValorProdutosB, decimal ValorFrete, string? Transportador, string? Observacao,
    bool? Encerrada, List<ItemCargaDto>? Itens, string? Produto = null);

public record ItemCargaDto(int TipoProdutoBId, int Quantidade);

public record RecebidoDto(DateOnly? RecebidoEm);
public record BaixaPixProdutosCDto(string Chave, DateOnly Data, List<int>? VendaIds);

// CargaId nulo = produtoC (EST-14: sem carga)
public record VendaProdutoBDto(int? CargaId, int ClienteId, DateOnly Data,
    List<ItemVendaDto>? Itens, string? Observacao);

public record ItemVendaDto(int TipoProdutoBId, int Pacotes, int Unidades);
