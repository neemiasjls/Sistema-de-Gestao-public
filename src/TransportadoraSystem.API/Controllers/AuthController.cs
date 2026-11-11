using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, ILogger<AuthController> logger) : ControllerBase
{
    // POST /api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            // "usuario" é o nome antigo do campo; aceito os dois para uma aba
            // aberta com o site velho não quebrar no meio da troca.
            var login = dto.Email ?? dto.Usuario;
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(dto.Senha))
                return BadRequest(new { erro = "Informe o e-mail e a senha." });

            var result = await auth.LoginAsync(login, dto.Senha);
            if (!result.Sucesso)
                return Unauthorized(new { erro = result.Erro });

            return Ok(new { token = result.Token, usuario = result.Usuario });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao fazer login");
            return StatusCode(500, new { erro = "Erro ao processar login." });
        }
    }

    // GET /api/auth/me — dados do usuário logado, lidos do banco (não do
    // token), para refletir na hora uma troca de senha ou de e-mail.
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        if (UsuarioId(User) is not { } id) return Unauthorized();
        var u = await auth.ObterAsync(id);
        if (u == null || !u.Ativo) return Unauthorized();
        return Ok(u);
    }

    // POST /api/auth/trocar-senha — a pessoa troca a própria senha. É a única
    // coisa liberada enquanto a senha ainda é a provisória.
    [HttpPost("trocar-senha")]
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> TrocarSenha([FromBody] TrocarSenhaDto dto)
    {
        if (UsuarioId(User) is not { } id) return Unauthorized();
        if (string.IsNullOrEmpty(dto.SenhaAtual) || string.IsNullOrEmpty(dto.NovaSenha))
            return BadRequest(new { erro = "Informe a senha atual e a nova." });

        var r = await auth.TrocarSenhaAsync(id, dto.SenhaAtual, dto.NovaSenha);
        if (!r.Sucesso) return BadRequest(new { erro = r.Erro });
        return Ok(new { token = r.Token, usuario = r.Usuario });
    }

    // PUT /api/auth/perfil — o próprio nome e e-mail, com a senha atual
    [HttpPut("perfil")]
    [Authorize]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> Perfil([FromBody] PerfilDto dto)
    {
        if (UsuarioId(User) is not { } id) return Unauthorized();
        if (string.IsNullOrEmpty(dto.SenhaAtual))
            return BadRequest(new { erro = "Informe a senha atual para salvar." });

        var r = await auth.AtualizarPerfilAsync(id, dto.Nome ?? "", dto.Email ?? "", dto.SenhaAtual);
        if (!r.Sucesso) return BadRequest(new { erro = r.Erro });
        return Ok(new { token = r.Token, usuario = r.Usuario });
    }

    // ── Portal das produtosA: cadastro pelo QR e senha esquecida (anônimos) ──

    // GET /api/auth/produtosA/codigo?c= — a tela do QR sabe se o código ainda vale
    [HttpGet("produtosA/codigo")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("produtosA-codigo")]
    public async Task<IActionResult> CodigoProdutosA([FromQuery] string? c) =>
        Ok(new { valido = await auth.CodigoProdutosAValidoAsync(c) });

    // POST /api/auth/produtosA/cadastro — cria a conta do portal e já devolve o login
    [HttpPost("produtosA/cadastro")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("cadastro")]
    public async Task<IActionResult> CadastroProdutosA([FromBody] CadastroProdutosADto dto)
    {
        try
        {
            var r = await auth.CadastrarProdutosAAsync(dto.Codigo, dto.Nome, dto.Email, dto.Senha, Ip());
            if (r.Erro != null) return StatusCode(r.Status, new { erro = r.Erro });
            return StatusCode(StatusCodes.Status201Created, new { token = r.Login!.Token, usuario = r.Login.Usuario });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro no cadastro do portal das produtosA");
            return StatusCode(500, new { erro = "Não deu para criar a conta agora. Tente de novo." });
        }
    }

    // POST /api/auth/produtosA/esqueci-senha — resposta sempre igual (não revela quem tem conta)
    [HttpPost("produtosA/esqueci-senha")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("esqueci-senha")]
    public async Task<IActionResult> EsqueciSenhaProdutosA([FromBody] EsqueciSenhaDto dto)
    {
        try { await auth.EsqueciSenhaProdutosAAsync(dto.Email, Ip()); }
        catch (Exception ex) { logger.LogError(ex, "Erro no esqueci a senha do portal das produtosA"); }
        return Ok(new { mensagem = "Se o e-mail estiver cadastrado, a senha nova chega nele." });
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ASP.NET mapeia "sub" para NameIdentifier
    public static int? UsuarioId(ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? user.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        return int.TryParse(id, out var n) ? n : null;
    }
}

public record LoginDto(string? Email, string? Usuario, string Senha);
public record TrocarSenhaDto(string SenhaAtual, string NovaSenha);
public record PerfilDto(string? Nome, string? Email, string SenhaAtual);
public record CadastroProdutosADto(string? Codigo, string? Nome, string? Email, string? Senha);
public record EsqueciSenhaDto(string? Email);
