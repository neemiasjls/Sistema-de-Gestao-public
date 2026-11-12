using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Application.Services.Acesso;

namespace TransportadoraSystem.API.Controllers;

// Tudo aqui é do dono: criar, editar, desativar e mandar senha nova. A
// conferência é no banco, não no token — um token antigo não vira passe livre.
[ApiController]
[Route("api/usuario")]
[Authorize]
public class UsuarioController(AuthService auth, PessoasService pessoas, ILogger<UsuarioController> logger) : ControllerBase, IAsyncActionFilter
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
            var v = PessoasService.ValidarEntrada(dto.Total == true, dto.Areas, "");
            if (!v.Ok) return BadRequest(new { erro = v.Erro });
            var r = await auth.CriarAsync(dto.Nome ?? "", dto.Email ?? "", v.Valor == "" ? [] : v.Valor.Split(','));
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
        var erro = await pessoas.DesativarAsync(id, Eu);
        return erro == null ? Ok(new { mensagem = "Usuário desativado." }) : BadRequest(new { erro });
    }

    [HttpPut("{id:int}/reativar")]
    public async Task<IActionResult> Reativar(int id)
    {
        var erro = await auth.DefinirAtivoAsync(id, true);
        return erro == null ? Ok(new { mensagem = "Usuário reativado." }) : BadRequest(new { erro });
    }
    int Eu => AuthController.UsuarioId(User)!.Value;

    IActionResult Responder(PessoasService.Resultado r) =>
        r.Erro == null ? Ok(r.Dados ?? new { }) : BadRequest(new { erro = r.Erro });

    // ── Tela única "Usuários" (USU-11, Parte 6) ───────────────────────────
    [HttpGet("areas")]
    public IActionResult Areas() => Ok(PessoasService.Catalogo());

    [HttpGet("pessoas")]
    public async Task<IActionResult> Pessoas() => Ok(await pessoas.ListarAsync());

    [HttpPut("{id:int}/permissoes")]
    public async Task<IActionResult> Permissoes(int id, [FromBody] PermissoesDto dto) =>
        Responder(await pessoas.DefinirPermissoesAsync(id, dto.Total == true, dto.Areas, Eu));

    [HttpPut("{id:int}/admin")]
    public async Task<IActionResult> Admin(int id, [FromBody] AdminDto dto) =>
        Responder(await pessoas.DefinirAdminAsync(id, dto.Admin == true, Eu));

    [HttpPost("{id:int}/motorista")]
    public async Task<IActionResult> DarMotorista(int id, [FromBody] DarMotoristaDto dto) =>
        Responder(await pessoas.DarMotoristaAsync(id, dto.MotoristaId,
            new PessoasService.NovoCadastro(dto.Nome, dto.Login, dto.PorcentagemComissao), Eu));

    [HttpDelete("{id:int}/motorista")]
    public async Task<IActionResult> TirarMotorista(int id) => Responder(await pessoas.TirarMotoristaAsync(id, Eu));

    [HttpPost("motorista/{motoristaId:int}/usuario")]
    public async Task<IActionResult> DarUsuario(int motoristaId, [FromBody] DarUsuarioDto dto) =>
        Responder(await pessoas.DarUsuarioAsync(motoristaId, dto.Email, dto.Total == true, dto.Areas, Eu));

    // Primeiro acesso / senha temporária do motorista pelo e-mail do cadastro (plano login-motorista-email,
    // Parte 4). O filtro acima já exige administrador conferido no banco (USU-12). A senha só volta
    // quando o e-mail não saiu; nunca vai para o log.
    [HttpPost("motorista/{motoristaId:int}/acesso-email")]
    public async Task<IActionResult> AcessoEmail(int motoristaId)
    {
        try
        {
            var r = await auth.MandarAcessoMotoristaAsync(motoristaId);
            logger.LogInformation("Acesso por e-mail do motorista {MotoristaId} mandado pelo usuário {Quem}: {Situacao}",
                motoristaId, Eu, r.Erro != null ? "recusado" : r.EmailEnviado ? "e-mail saiu" : "e-mail não saiu");
            if (r.Erro != null) return BadRequest(new { erro = r.Erro });
            return Ok(new { senha = r });
        }
        catch (Exception ex) { logger.LogError(ex, "Erro ao mandar o acesso do motorista"); return StatusCode(500, new { erro = "Erro ao mandar o acesso." }); }
    }

    [HttpDelete("{id:int}/usuario")]
    public async Task<IActionResult> TirarUsuario(int id) => Responder(await pessoas.TirarUsuarioAsync(id, Eu));
}

public record CriarUsuarioDto(string? Nome, string? Email, bool? Total = null, List<string>? Areas = null);
public record PermissoesDto(bool? Total, List<string>? Areas);
public record AdminDto(bool? Admin);
public record DarMotoristaDto(int? MotoristaId, string? Nome, string? Login, decimal? PorcentagemComissao);
public record DarUsuarioDto(string? Email, bool? Total, List<string>? Areas);
