using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Fechamento mensal por caminhão (VIA-18): montar o mês, salvar as anotações,
/// fechar e reabrir, e o cadastro de consórcios. O perfil motorista não chega
/// aqui (Program.cs barra tudo fora do painel dele).
/// </summary>
[Authorize]
[ApiController]
[Route("api/fechamentos")]
public class FechamentosController(FechamentoMensalService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Montar([FromQuery] string placa, [FromQuery] int ano, [FromQuery] int mes)
    {
        try { return Ok(await svc.MontarAsync(placa, ano, mes)); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPut]
    public async Task<IActionResult> Salvar([FromBody] FechamentoEntrada e)
    {
        try
        {
            await svc.SalvarAsync(e);
            return Ok(await svc.MontarAsync(e.Placa, e.Ano, e.Mes));
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPost("fechar")]
    public async Task<IActionResult> Fechar([FromBody] MesFechamentoEntrada e)
    {
        try
        {
            await svc.FecharAsync(e);
            return Ok(await svc.MontarAsync(e.Placa, e.Ano, e.Mes));
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPost("reabrir")]
    public async Task<IActionResult> Reabrir([FromBody] MesFechamentoEntrada e)
    {
        try
        {
            await svc.ReabrirAsync(e);
            return Ok(await svc.MontarAsync(e.Placa, e.Ano, e.Mes));
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpGet("consorcios")]
    public async Task<IActionResult> Consorcios([FromQuery] string? placa) => Ok(await svc.ConsorciosAsync(placa));

    [HttpPost("consorcios")]
    public Task<IActionResult> CriarConsorcio([FromBody] ConsorcioEntrada e) => SalvarConsorcio(null, e);

    [HttpPut("consorcios/{id:int}")]
    public Task<IActionResult> AtualizarConsorcio(int id, [FromBody] ConsorcioEntrada e) => SalvarConsorcio(id, e);

    private async Task<IActionResult> SalvarConsorcio(int? id, ConsorcioEntrada e)
    {
        try { return Ok(await svc.SalvarConsorcioAsync(id, e)); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpDelete("consorcios/{id:int}")]
    public async Task<IActionResult> ExcluirConsorcio(int id)
    {
        try { await svc.ExcluirConsorcioAsync(id); return Ok(new { mensagem = "Consórcio apagado." }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }
}
