using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.API.Controllers;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Application.Services.Acesso;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Acesso;

/// <summary>
/// Controle de permissão por área (USU-11): a cada pedido autenticado para /api/, lê do banco o
/// usuário do token (Ativo, Dono, Perfil, Permissoes) e se há cadastro de motorista ativo ligado,
/// e decide pela regra de <see cref="AreasAcesso"/>. A decisão vem do banco, nunca das claims do
/// token: mudou a permissão ou desativou, vale no pedido seguinte. Anônimo segue para o [Authorize]
/// de sempre. As conferências de dono dentro dos controllers continuam (duas camadas).
/// </summary>
public static class PermissoesMiddleware
{
    public const string MensagemSessao = "Sua conta foi desativada ou a sessão terminou. Entre de novo.";

    public static IApplicationBuilder UsarPermissoes(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var path = ctx.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (ctx.User.Identity?.IsAuthenticated != true || !path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await next();
            return;
        }

        // Rota pública: não depende de quem é (a própria rota se protege).
        var regra = AreasAcesso.Achar(path);
        var lista = regra == null ? null : (AreasAcesso.EhLeitura(ctx.Request.Method) ? regra.Leem : regra.Escrevem);
        if (lista != null && lista.Contains(AreasAcesso.Anonimo))
        {
            await next();
            return;
        }

        var id = AuthController.UsuarioId(ctx.User) ?? 0;
        AreasAcesso.UsuarioAcesso? usuario;
        try
        {
            var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
            var u = id <= 0 ? null : await db.Usuarios.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.Ativo, x.Dono, x.Perfil, x.Permissoes, x.SenhaAlteradaEm })
                .FirstOrDefaultAsync();
            // Token emitido antes da última troca de senha: sessão encerrada (USU-12)
            var iat = long.TryParse(ctx.User.FindFirst("iat")?.Value, out var s) ? s : (long?)null;
            usuario = u == null || !u.Ativo || !AuthService.TokenVale(u.SenhaAlteradaEm, iat) ? null
                : new AreasAcesso.UsuarioAcesso(u.Perfil, u.Dono, u.Permissoes,
                    await db.MotoristasFrota.AsNoTracking().AnyAsync(m => m.UsuarioId == id && m.Ativo));
        }
        catch (Exception ex)
        {
            // Erro na consulta não libera.
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Permissoes")
                .LogError(ex, "Falha ao conferir a permissão do usuário {Id}", id);
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await ctx.Response.WriteAsJsonAsync(new { erro = "Não foi possível conferir o acesso. Tente de novo." });
            return;
        }

        if (usuario == null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { erro = MensagemSessao, sessaoEncerrada = true });
            return;
        }

        var d = AreasAcesso.Decidir(usuario, ctx.Request.Method, path);
        if (d.Permitido)
        {
            await next();
            return;
        }

        ctx.Response.StatusCode = d.Status;
        var perfil = usuario.Perfil ?? "";
        if (perfil == AuthService.PerfilMotorista)
            await ctx.Response.WriteAsJsonAsync(new { erro = d.Mensagem, motorista = true });
        else if (perfil == AuthService.PerfilProdutosA)
            await ctx.Response.WriteAsJsonAsync(new { erro = d.Mensagem, produtosA = true });
        else if (perfil.Length == 0)
            await ctx.Response.WriteAsJsonAsync(new { erro = d.Mensagem, semPermissao = true, area = d.AreaFaltando });
        else
            await ctx.Response.WriteAsJsonAsync(new { erro = d.Mensagem });
    });
}
