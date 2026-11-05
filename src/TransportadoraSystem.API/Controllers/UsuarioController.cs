using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

// Tudo aqui é do dono: criar, editar, desativar e mandar senha nova. A
// conferência é no banco, não no token — um token antigo não vira passe livre.
[ApiController]
[Route("api/usuario")]
[Authorize]
public class UsuarioController(AuthService auth, ILogger<UsuarioController> logger) : ControllerBase, IAsyncActionFilter
{
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var id = AuthController.UsuarioId(User);
        if (id == null || !await auth.EhDonoAsync(id.Value))
        {
            ctx.Result = StatusCode(403, new { erro = "Só o dono do sistema gerencia usuários." });
            return;
        }
        await next();
    }

    [HttpGet]
    public async Task<IActionResult> Listar() => Ok(await auth.ListarAsync());

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarUsuarioDto dto)
    {
        try
        {
            var r = await auth.CriarAsync(dto.Nome ?? "", dto.Email ?? "");
            if (r.Erro != null) return BadRequest(new { erro = r.Erro });
            return Ok(r);
        }
        catch (Exception ex) { logger.LogError(ex, "Erro ao criar usuário"); return StatusCode(500, new { erro = "Erro ao criar usuário." }); }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, [FromBody] CriarUsuarioDto dto)
    {
        var erro = await auth.EditarAsync(id, dto.Nome ?? "", dto.Email ?? "");
        return erro == null ? Ok(new { mensagem = "Usuário atualizado." }) : BadRequest(new { erro });
    }

    [HttpPost("{id:int}/nova-senha")]
    public async Task<IActionResult> NovaSenha(int id)
    {
        try
        {
            var r = await auth.GerarNovaSenhaAsync(id);
            if (r.Erro != null) return BadRequest(new { erro = r.Erro });
            return Ok(r);
        }
        catch (Exception ex) { logger.LogError(ex, "Erro ao gerar nova senha"); return StatusCode(500, new { erro = "Erro ao gerar a senha." }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desativar(int id)
    {
        var erro = await auth.DefinirAtivoAsync(id, false);
        return erro == null ? Ok(new { mensagem = "Usuário desativado." }) : BadRequest(new { erro });
    }

    [HttpPut("{id:int}/reativar")]
    public async Task<IActionResult> Reativar(int id)
    {
        var erro = await auth.DefinirAtivoAsync(id, true);
        return erro == null ? Ok(new { mensagem = "Usuário reativado." }) : BadRequest(new { erro });
    }
}

public record CriarUsuarioDto(string? Nome, string? Email);
