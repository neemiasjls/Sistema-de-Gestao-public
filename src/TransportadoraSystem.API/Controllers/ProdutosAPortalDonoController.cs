using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Lado do dono do portal das produtosA (EST-09): contas criadas pelo QR, o código do QR do barracão
/// e o histórico. Só o dono (conferido no banco); os outros recebem 403. Desativar, reativar e
/// "nova senha" das contas usam as rotas de /api/usuario, que valem para qualquer perfil.
/// Não existe rota para alterar ou apagar o histórico.
/// </summary>
[Authorize]
[ApiController]
[Route("api/estoque/produtosA-portal")]
public class ProdutosAPortalDonoController(AppDbContext db, AuthService auth, PortalProdutosAHistoricoService historico)
    : ControllerBase, IAsyncActionFilter
{
    private const int PorPagina = 50;
    private const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    private int _usuarioId;
    private string _usuarioNome = "";

    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var id = AuthController.UsuarioId(User);
        if (id == null || !await auth.EhDonoAsync(id.Value))
        {
            ctx.Result = StatusCode(403, new { erro = "Só o dono do sistema vê isto." });
            return;
        }
        _usuarioId = id.Value;
        _usuarioNome = await db.Usuarios.Where(u => u.Id == id).Select(u => u.Nome).FirstAsync();
        await next();
    }

    // ── Contas do portal ──────────────────────────────────────────────────
    // GET /api/estoque/produtosA-portal/contas — ativas primeiro, depois desativadas
    [HttpGet("contas")]
    public async Task<IActionResult> Contas()
    {
        var contas = await db.Usuarios.AsNoTracking()
            .Where(u => u.Perfil == AuthService.PerfilProdutosA)
            .OrderByDescending(u => u.Ativo).ThenBy(u => u.Nome)
            .Select(u => new
            {
                u.Id, u.Nome, u.Email, u.Ativo, u.TrocarSenha, u.CriadoEm,
                anotacoes = db.VendasProdutoA.Count(v => v.AnotadaPorUsuarioId == u.Id),
                ultimaAnotacao = db.VendasProdutoA.Where(v => v.AnotadaPorUsuarioId == u.Id).Max(v => (DateTime?)v.AnotadaEm),
            })
            .ToListAsync();
        return Ok(contas);
    }

    // ── Código do QR ──────────────────────────────────────────────────────
    // GET /api/estoque/produtosA-portal/codigo — sem código ainda: codigo = null (a tela mostra "Gerar QR")
    [HttpGet("codigo")]
    public async Task<IActionResult> Codigo()
    {
        var cfg = await db.PortalProdutosAConfig.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        return Ok(string.IsNullOrWhiteSpace(cfg?.Codigo)
            ? new { codigo = (string?)null, trocadoEm = (DateTime?)null }
            : new { codigo = (string?)cfg.Codigo, trocadoEm = (DateTime?)cfg.TrocadoEm });
    }

    // POST /api/estoque/produtosA-portal/codigo/trocar — gera (ou troca) o código; o QR velho para de valer
    [HttpPost("codigo/trocar")]
    public async Task<IActionResult> TrocarCodigo()
    {
        var cfg = await db.PortalProdutosAConfig.OrderBy(c => c.Id).FirstOrDefaultAsync();
        bool primeiro = cfg == null || string.IsNullOrWhiteSpace(cfg.Codigo);
        if (cfg == null) { cfg = new PortalProdutosAConfig(); db.PortalProdutosAConfig.Add(cfg); }

        cfg.Codigo = RandomNumberGenerator.GetString(Letras, 32);
        cfg.TrocadoEm = DateTime.UtcNow;
        // O código não vai para o histórico: só o fato de ter trocado
        historico.Adicionar(_usuarioId, _usuarioNome, PortalProdutosAHistoricoService.CodigoTrocado,
            primeiro ? "Gerou o QR do barracão" : "Trocou o código do QR; o QR impresso antes parou de valer",
            ip: HttpContext.Connection.RemoteIpAddress?.ToString());
        await db.SaveChangesAsync();
        return Ok(new { codigo = cfg.Codigo, trocadoEm = cfg.TrocadoEm });
    }

    // ── Histórico ─────────────────────────────────────────────────────────
    // GET /api/estoque/produtosA-portal/historico?de&ate&usuarioId&acao&pagina — mais novo primeiro.
    // de/ate são dias no horário de Brasília.
    [HttpGet("historico")]
    public async Task<IActionResult> Historico([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate,
        [FromQuery] int? usuarioId, [FromQuery] string? acao, [FromQuery] int pagina = 1)
    {
        if (pagina < 1) pagina = 1;
        var q = db.PortalProdutosAHistorico.AsNoTracking().AsQueryable();
        if (de is { } d)
        {
            var inicio = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue).AddHours(3), DateTimeKind.Utc);
            q = q.Where(h => h.Quando >= inicio);
        }
        if (ate is { } a)
        {
            var fim = DateTime.SpecifyKind(a.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(3), DateTimeKind.Utc);
            q = q.Where(h => h.Quando < fim);
        }
        if (usuarioId is { } uid) q = q.Where(h => h.UsuarioId == uid);
        if (!string.IsNullOrWhiteSpace(acao)) q = q.Where(h => h.Acao == acao);

        var total = await q.CountAsync();
        var itens = await q
            .OrderByDescending(h => h.Quando).ThenByDescending(h => h.Id)
            .Skip((pagina - 1) * PorPagina).Take(PorPagina)
            .Select(h => new { h.Id, h.Quando, h.UsuarioId, h.UsuarioNome, h.Acao, h.VendaProdutoAId, h.Resumo, h.Antes, h.Depois })
            .ToListAsync();

        // Pessoas para o filtro: todo mundo que já aparece no histórico (nome mais recente)
        var pessoas = (await db.PortalProdutosAHistorico.AsNoTracking()
                .Where(h => h.UsuarioId != null)
                .Select(h => new { h.UsuarioId, h.UsuarioNome, h.Quando })
                .ToListAsync())
            .GroupBy(h => h.UsuarioId!.Value)
            .Select(g => new { id = g.Key, nome = g.OrderByDescending(x => x.Quando).First().UsuarioNome })
            .OrderBy(p => p.nome)
            .ToList();

        return Ok(new { itens, total, pagina, porPagina = PorPagina, pessoas });
    }
}
