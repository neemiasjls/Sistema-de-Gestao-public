using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Portal das produtosA no celular (EST-09). Quem entra anota a retirada (data, cliente, pesos em
/// gramas) e vê/altera/exclui só as dele. Cada anotação é a própria saída de produtoA (VendaProdutoA),
/// pela mesma regra da tela de ProdutosA (ProdutosASaidaService). Nunca devolve carga, custo, preço,
/// valor nem número de romaneio. Tudo vai para o histórico do dono, na mesma transação.
/// A conferência do perfil é no banco, não só no token.
/// </summary>
[Authorize]
[ApiController]
[Route("api/produtosA-portal")]
public class ProdutosAPortalController(AppDbContext db, ProdutosASaidaService saidas, PortalProdutosAHistoricoService historico)
    : ControllerBase, IAsyncActionFilter
{
    private const int DiasLista = 60;        // anotações e clientes mais usados
    private const int DiasParaTras = 30;     // data da anotação: hoje até 30 dias atrás
    private const int PesoMin = 1000, PesoMax = 30000, MaxProdutosA = 60;

    private int _usuarioId;
    private string _usuarioNome = "";

    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var id = AuthController.UsuarioId(User);
        var u = id == null ? null : await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        // Perfil produtosA ou o escritório (perfil vazio: Admin e Bento anotam com o login deles, USU-05)
        if (u == null || (u.Perfil != AuthService.PerfilProdutosA && u.Perfil != ""))
        {
            ctx.Result = StatusCode(403, new { erro = "Acesso só ao portal das produtosA." });
            return;
        }
        if (!u.Ativo)
        {
            ctx.Result = StatusCode(403, new { erro = "Sua conta foi desativada. Fale com o Admin." });
            return;
        }
        _usuarioId = u.Id;
        _usuarioNome = u.Nome;
        await next();
    }

    // O dia no Brasil: o servidor roda em UTC e à noite já seria amanhã
    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ── Clientes ──────────────────────────────────────────────────────────

    [HttpGet("clientes")]
    public async Task<IActionResult> Clientes()
    {
        var desde = Hoje().AddDays(-DiasLista);
        var uso = await db.VendasProdutoA
            .Where(v => v.AnotadaPorUsuarioId == _usuarioId && v.Data >= desde)
            .GroupBy(v => v.ClienteId)
            .Select(g => new { g.Key, Qtde = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Qtde);

        var clientes = await db.EstoqueClientes.AsNoTracking()
            .Where(c => c.Ativo)
            .Select(c => new { c.Id, c.Nome })
            .ToListAsync();

        return Ok(clientes
            .OrderByDescending(c => uso.TryGetValue(c.Id, out var q) ? q : 0)
            .ThenBy(c => c.Nome)
            .Select(c => new { id = c.Id, nome = c.Nome }));
    }

    [HttpPost("clientes")]
    public async Task<IActionResult> CriarCliente([FromBody] PortalClienteDto dto)
    {
        var nome = System.Text.RegularExpressions.Regex.Replace((dto.Nome ?? "").Trim(), @"\s+", " ").ToUpperInvariant();
        if (nome.Length < 2 || nome.Length > 80)
            return BadRequest(new { erro = "Escreva o nome do cliente (2 a 80 letras)." });
        var cidade = string.IsNullOrWhiteSpace(dto.Cidade) ? null
            : System.Text.RegularExpressions.Regex.Replace(dto.Cidade.Trim(), @"\s+", " ").ToUpperInvariant();
        if (cidade is { Length: > 80 })
            return BadRequest(new { erro = "Cidade muito longa." });

        // Mesmo nome sem diferença de maiúscula, acento ou espaço: devolve o que já existe
        var chave = ViagemCaminhaoCalculo.Normalizar(nome);
        var existentes = await db.EstoqueClientes.AsNoTracking().Select(c => new { c.Id, c.Nome }).ToListAsync();
        var igual = existentes.FirstOrDefault(c => ViagemCaminhaoCalculo.Normalizar(c.Nome) == chave);
        if (igual != null) return Ok(new { id = igual.Id, nome = igual.Nome, jaExistia = true });

        await using var tx = await db.Database.BeginTransactionAsync();
        var cliente = new EstoqueCliente { Nome = nome, Cidade = cidade, PrecoProdutoAKg = 0, Ativo = true };
        db.EstoqueClientes.Add(cliente);
        await db.SaveChangesAsync();
        historico.Adicionar(_usuarioId, _usuarioNome, PortalProdutosAHistoricoService.ClienteCadastrado,
            $"Cadastrou o cliente {nome}" + (cidade == null ? "" : $" ({cidade})"),
            depois: new { cliente = nome, cidade }, ip: Ip());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return StatusCode(201, new { id = cliente.Id, nome = cliente.Nome, jaExistia = false });
    }

    // ── Anotações ─────────────────────────────────────────────────────────

    [HttpGet("anotacoes")]
    public async Task<IActionResult> Anotacoes()
    {
        var desde = Hoje().AddDays(-DiasLista);
        var vendas = await db.VendasProdutoA.AsNoTracking()
            .Include(v => v.ProdutosA).Include(v => v.Cliente)
            .Where(v => v.AnotadaPorUsuarioId == _usuarioId && v.Data >= desde)
            .OrderByDescending(v => v.Data).ThenByDescending(v => v.Id)
            .ToListAsync();
        return Ok(vendas.Select(Resposta));
    }

    [HttpPost("anotacoes")]
    public async Task<IActionResult> Anotar([FromBody] AnotacaoDto dto)
    {
        var erro = ValidarData(dto.Data) ?? ValidarPesos(dto.Pesos);
        if (erro != null) return BadRequest(new { erro });
        var pesos = dto.Pesos!;

        var cliente = await db.EstoqueClientes.FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.Ativo);
        if (cliente == null) return BadRequest(new { erro = "Cliente não encontrado. Escolha da lista." });

        // Toque duplo / reenvio com sinal ruim: a mesma anotação nos últimos 2 minutos volta a já gravada
        var limite = DateTime.UtcNow.AddMinutes(-2);
        var recentes = await db.VendasProdutoA.AsNoTracking().Include(v => v.ProdutosA).Include(v => v.Cliente)
            .Where(v => v.AnotadaPorUsuarioId == _usuarioId && v.ClienteId == cliente.Id && v.Data == dto.Data
                     && v.AnotadaEm >= limite)
            .ToListAsync();
        var repetida = recentes.FirstOrDefault(v => v.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).SequenceEqual(pesos));
        if (repetida != null) return Ok(Resposta(repetida));

        // Lote: a carga em aberto mais antiga; sem nenhuma em aberto, a mais recente (o Admin é avisado)
        var carga = await db.CargasProdutoA.Where(c => !c.Encerrada).OrderBy(c => c.Data).ThenBy(c => c.Id).FirstOrDefaultAsync();
        bool semLote = carga == null;
        carga ??= await db.CargasProdutoA.OrderByDescending(c => c.Data).ThenByDescending(c => c.Id).FirstOrDefaultAsync();
        if (carga == null) return Conflict(new { erro = "Ainda não há lote de produtosA no sistema. Fale com o Admin." });

        await using var tx = await db.Database.BeginTransactionAsync();
        var (venda, _) = await saidas.CriarAsync(carga, cliente, dto.Data, null, null, pesos, _usuarioId);
        historico.Adicionar(_usuarioId, _usuarioNome, PortalProdutosAHistoricoService.AnotacaoCriada,
            $"Anotou {pesos.Count} produtoA(s) para {cliente.Nome} em {dto.Data:dd/MM/yyyy}" + (semLote ? " (sem lote em aberto)" : ""),
            venda.Id,
            depois: semLote
                ? new { data = dto.Data, cliente = cliente.Nome, pesos, semLoteEmAberto = true }
                : (object)new { data = dto.Data, cliente = cliente.Nome, pesos },
            ip: Ip());
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        venda.Cliente = cliente;
        return StatusCode(201, Resposta(venda));
    }

    [HttpPut("anotacoes/{id:int}")]
    public async Task<IActionResult> Alterar(int id, [FromBody] AnotacaoDto dto)
    {
        var venda = await MinhaAsync(id);
        if (venda == null) return NotFound(new { erro = "Anotação não encontrada." });

        // Data antiga mantida não trava a edição; só a data nova precisa estar no prazo
        var erro = (dto.Data != venda.Data ? ValidarData(dto.Data) : null) ?? ValidarPesos(dto.Pesos);
        if (erro != null) return BadRequest(new { erro });
        var pesos = dto.Pesos!;

        var cliente = venda.ClienteId == dto.ClienteId ? venda.Cliente
            : await db.EstoqueClientes.FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.Ativo);
        if (cliente == null) return BadRequest(new { erro = "Cliente não encontrado. Escolha da lista." });

        var pesosAntes = venda.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();
        var antes = new { data = venda.Data, cliente = venda.Cliente?.Nome, pesos = pesosAntes };
        if (dto.Data == venda.Data && cliente.Id == venda.ClienteId && pesosAntes.SequenceEqual(pesos))
            return Ok(Resposta(venda));

        // Já saiu no romaneio (número gravado): pode alterar, mas a tela de ProdutosA avisa
        bool depoisDoRomaneio = venda.Numero != null;

        await using var tx = await db.Database.BeginTransactionAsync();
        await saidas.AlterarAsync(venda, dto.Data, cliente, pesos);
        historico.Adicionar(_usuarioId, _usuarioNome, PortalProdutosAHistoricoService.AnotacaoEditada,
            $"Alterou a anotação de {antes.cliente} em {antes.data:dd/MM/yyyy}" + (depoisDoRomaneio ? " (depois do romaneio)" : ""),
            venda.Id, antes,
            depois: depoisDoRomaneio
                ? new { data = dto.Data, cliente = cliente.Nome, pesos, depoisDoRomaneio = true }
                : (object)new { data = dto.Data, cliente = cliente.Nome, pesos },
            ip: Ip());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(Resposta(venda));
    }

    [HttpDelete("anotacoes/{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var venda = await MinhaAsync(id);
        if (venda == null) return NotFound(new { erro = "Anotação não encontrada." });

        var pesosAntes = venda.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();
        await using var tx = await db.Database.BeginTransactionAsync();
        historico.Adicionar(_usuarioId, _usuarioNome, PortalProdutosAHistoricoService.AnotacaoExcluida,
            $"Excluiu a anotação de {venda.Cliente?.Nome} em {venda.Data:dd/MM/yyyy}, {pesosAntes.Count} produtoA(s)"
                + (venda.Numero != null ? " (depois do romaneio)" : ""),
            venda.Id,
            antes: new { data = venda.Data, cliente = venda.Cliente?.Nome, pesos = pesosAntes },
            ip: Ip());
        await saidas.ExcluirAsync(venda);   // salva o histórico junto
        await tx.CommitAsync();
        return NoContent();
    }

    // ── Apoio ─────────────────────────────────────────────────────────────

    /// <summary>Só a anotação desta pessoa; de outra (ou inexistente) é null → 404, sem revelar que existe.</summary>
    private Task<VendaProdutoA?> MinhaAsync(int id) =>
        db.VendasProdutoA.Include(v => v.ProdutosA).Include(v => v.Cliente).Include(v => v.Carga)
            .FirstOrDefaultAsync(v => v.Id == id && v.AnotadaPorUsuarioId == _usuarioId);

    private static string? ValidarData(DateOnly data)
    {
        var hoje = Hoje();
        if (data > hoje) return "A data não pode ser depois de hoje.";
        if (data < hoje.AddDays(-DiasParaTras)) return $"A data pode ser de até {DiasParaTras} dias atrás.";
        return null;
    }

    private static string? ValidarPesos(List<int>? pesos)
    {
        if (pesos == null || pesos.Count == 0) return "Digite o peso de pelo menos uma produtoA.";
        if (pesos.Count > MaxProdutosA) return $"No máximo {MaxProdutosA} produtosA por anotação.";
        if (pesos.Any(p => p < PesoMin || p > PesoMax))
            return "Peso em gramas, entre 1.000 e 30.000 (ex.: 12740).";
        return null;
    }

    /// <summary>O que o portal vê: sem carga, custo, preço, valor nem número de romaneio.</summary>
    private static object Resposta(VendaProdutoA v)
    {
        var pesos = v.ProdutosA.OrderBy(b => b.Id).Select(b => b.PesoGramas).ToList();
        return new
        {
            id = v.Id,
            data = v.Data,
            clienteId = v.ClienteId,
            cliente = v.Cliente?.Nome ?? "",
            pesos,
            quantidade = pesos.Count,
            totalKg = pesos.Sum() / 1000m,
            anotadaEm = v.AnotadaEm,
            podeAlterar = true,
        };
    }
}

public record PortalClienteDto(string? Nome, string? Cidade);
public record AnotacaoDto(DateOnly Data, int ClienteId, List<int>? Pesos);
