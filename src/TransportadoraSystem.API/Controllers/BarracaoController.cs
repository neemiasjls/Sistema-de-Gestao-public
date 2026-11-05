using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/barracao")]
public sealed class BarracaoController(AppDbContext db, BarracaoService servico) : ControllerBase
{
    public record ComissaoDto(DateOnly Data, string Cliente, string? Produto, decimal Quantidade,
        decimal? Valor, string? Fornecedor, string? Observacao);
    public record DespesaDto(DateOnly Data, string Descricao, decimal? Valor, string? Observacao);

    [HttpGet("comissoes")]
    public async Task<IActionResult> Comissoes(DateOnly? de, DateOnly? ate) => Ok(await db.ComissoesBarracao.AsNoTracking()
        .Where(c => (de == null || c.Data >= de) && (ate == null || c.Data <= ate))
        .OrderByDescending(c => c.Data).ThenBy(c => c.Ordem).ThenBy(c => c.Id)
        .Select(c => new { c.Id, c.Data, c.Cliente, c.Produto, c.Quantidade, c.Valor, c.Fornecedor, c.Observacao,
            total = c.Valor == null ? (decimal?)null : c.Quantidade * c.Valor, c.Origem }).ToListAsync());

    [HttpPost("comissoes")]
    public Task<IActionResult> CriarComissao(ComissaoDto dto) => SalvarComissao(null, dto);

    [HttpPut("comissoes/{id:int}")]
    public Task<IActionResult> AlterarComissao(int id, ComissaoDto dto) => SalvarComissao(id, dto);

    private async Task<IActionResult> SalvarComissao(int? id, ComissaoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Cliente) || dto.Data == default || dto.Quantidade < 0)
            return BadRequest(new { erro = "Informe dia, cliente e quantidade válida." });
        var c = id == null ? new ComissaoBarracao() : await db.ComissoesBarracao.FindAsync(id.Value);
        if (c == null) return NotFound(new { erro = "Comissão não encontrada." });
        if (id == null) db.ComissoesBarracao.Add(c);
        c.Data = dto.Data; c.Cliente = dto.Cliente.Trim(); c.Produto = dto.Produto;
        c.Quantidade = dto.Quantidade; c.Valor = dto.Valor; c.Fornecedor = dto.Fornecedor;
        c.Observacao = dto.Observacao; c.Origem = "manual"; c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { c.Id });
    }

    [HttpDelete("comissoes/{id:int}")]
    public async Task<IActionResult> ExcluirComissao(int id)
    {
        var c = await db.ComissoesBarracao.FindAsync(id);
        if (c == null) return NotFound();
        db.ComissoesBarracao.Remove(c); await db.SaveChangesAsync(); return NoContent();
    }

    [HttpGet("despesas")]
    public async Task<IActionResult> Despesas(DateOnly? de, DateOnly? ate) => Ok(await db.DespesasDoBarracao.AsNoTracking()
        .Where(d => (de == null || d.Data >= de) && (ate == null || d.Data <= ate))
        .OrderByDescending(d => d.Data).ThenBy(d => d.Id).ToListAsync());

    [HttpPost("despesas")]
    public Task<IActionResult> CriarDespesa(DespesaDto dto) => SalvarDespesa(null, dto);

    [HttpPut("despesas/{id:int}")]
    public Task<IActionResult> AlterarDespesa(int id, DespesaDto dto) => SalvarDespesa(id, dto);

    private async Task<IActionResult> SalvarDespesa(int? id, DespesaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Descricao) || dto.Data == default)
            return BadRequest(new { erro = "Informe dia e referência da despesa." });
        var d = id == null ? new DespesaDoBarracao() : await db.DespesasDoBarracao.FindAsync(id.Value);
        if (d == null) return NotFound();
        if (id == null) db.DespesasDoBarracao.Add(d);
        d.Data = dto.Data; d.Descricao = dto.Descricao.Trim(); d.Valor = dto.Valor;
        d.Observacao = dto.Observacao; d.Origem = "manual"; d.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(); return Ok(new { d.Id });
    }

    [HttpDelete("despesas/{id:int}")]
    public async Task<IActionResult> ExcluirDespesa(int id)
    {
        var d = await db.DespesasDoBarracao.FindAsync(id);
        if (d == null) return NotFound();
        db.DespesasDoBarracao.Remove(d); await db.SaveChangesAsync(); return NoContent();
    }

    [HttpPost("importar/previa")]
    [RequestSizeLimit(30_000_000)]
    public Task<IActionResult> Previa([FromForm] IFormFile arquivo) => ImportarArquivo(arquivo, false);

    [HttpPost("importar")]
    [RequestSizeLimit(30_000_000)]
    public Task<IActionResult> Importar([FromForm] IFormFile arquivo) => ImportarArquivo(arquivo, true);

    private async Task<IActionResult> ImportarArquivo(IFormFile arquivo, bool aplicar)
    {
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { erro = "Envie a planilha do barracão." });
        using var ms = new MemoryStream(); await arquivo.CopyToAsync(ms);
        try { return Ok(await servico.ImportarAsync(ms.ToArray(), aplicar)); }
        catch (Exception ex) when (ex is InvalidOperationException or ExcelDataReader.Exceptions.HeaderException)
        { return BadRequest(new { erro = "Não consegui ler a planilha: " + ex.Message }); }
    }
}
