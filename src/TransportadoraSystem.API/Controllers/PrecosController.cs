using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Painel de preços por cliente (VIA-10): frete (caixa normal, produtoE, produtoD,
/// produtoB, produtoC) e venda (produtosB, produtoC, produtoA). Lê e grava a tabela
/// de frete das viagens e o cadastro do estoque, ligados um ao outro: não existe
/// cadastro de preço repetido.
/// </summary>
[Authorize]
[ApiController]
[Route("api/precos")]
public class PrecosController(PrecosClienteService svc) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Painel() => Ok(await svc.PainelAsync());

    [HttpPut("frete/{id:int}")]
    public Task<IActionResult> SalvarFrete(int id, [FromBody] PrecoFreteEntrada e) =>
        Executar(async () => { await svc.SalvarFreteAsync(id, e); return new { id }; });

    [HttpPut("estoque/{id:int}")]
    public Task<IActionResult> SalvarEstoque(int id, [FromBody] PrecoEstoqueEntrada e) =>
        Executar(async () => { await svc.SalvarEstoqueAsync(id, e); return new { id }; });

    // Liga o cliente de frete ao cadastro do estoque (ou cria um), ou solta
    [HttpPut("frete/{id:int}/estoque")]
    public Task<IActionResult> Ligar(int id, [FromBody] VinculoEstoque v) =>
        Executar(async () => new { estoqueId = await svc.LigarAsync(id, v) });

    [HttpPost("clientes")]
    public Task<IActionResult> NovoCliente([FromBody] NovoClientePreco e) => Executar(() => svc.CriarClienteAsync(e));

    private async Task<IActionResult> Executar(Func<Task<object>> acao)
    {
        try { return Ok(await acao()); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }
}
