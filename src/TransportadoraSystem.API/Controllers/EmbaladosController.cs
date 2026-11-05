using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Módulo Embalados (VOLUME-16..18): quebras, despesas das produtosB, comissões e
/// salários, com o resumo (comissões − despesas = sobra, dividida por 3) e a
/// importação da planilha COMISSÕES VOLUMES EMBALADOS.
/// </summary>
[Authorize]
[ApiController]
[Route("api/embalados")]
public class EmbaladosController(AppDbContext db, EmbaladosService servico) : ControllerBase
{
    public record QuebraDto(DateOnly Data, string Cor, decimal? ProdutosC, decimal? PrecoCaixa, string? Observacao);
    public record DespesaDto(DateOnly Data, string Descricao, decimal? Valor, string? Observacao);
    public record ComissaoDto(DateOnly Data, string Cliente, string? Tipo, decimal Caixas, decimal? PrecoCompra,
        decimal? PrecoVenda, decimal Frete, string? Fornecedor, string? Observacao);
    public record SalarioDto(DateOnly Mes, string Referencia, decimal? Valor, decimal? Inss, decimal? Fgts, string? Observacao);

    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate) =>
        Ok(await servico.ResumoAsync(de, ate));

    [HttpPost("importar")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Importar([FromForm] IFormFile arquivo)
        => await ImportarArquivo(arquivo, true);

    [HttpPost("importar/previa")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Previa([FromForm] IFormFile arquivo)
        => await ImportarArquivo(arquivo, false);

    private async Task<IActionResult> ImportarArquivo(IFormFile arquivo, bool aplicar)
    {
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { erro = "Envie a planilha COMISSÕES VOLUMES EMBALADOS." });
        using var ms = new MemoryStream();
        await arquivo.CopyToAsync(ms);
        try { return Ok(await servico.ImportarAsync(ms.ToArray(), aplicar)); }
        catch (Exception ex) when (ex is InvalidOperationException or ExcelDataReader.Exceptions.HeaderException)
        {
            return BadRequest(new { erro = "Não consegui ler a planilha: " + ex.Message });
        }
    }

    // ───────────────────────────── Quebras ─────────────────────────────

    [HttpGet("quebras")]
    public async Task<IActionResult> Quebras([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate) =>
        Ok(await servico.QuebrasAsync(de, ate));

    /// <summary>
    /// Guarda a quebra do dia e cor (uma linha só): nos dias com controle só o
    /// preço e a observação valem; sem controle, as produtosC também. Preço vazio
    /// volta ao calculado pelo romaneio.
    /// </summary>
    [HttpPut("quebras")]
    public async Task<IActionResult> SalvarQuebra([FromBody] QuebraDto dto)
    {
        var cor = (dto.Cor ?? "").Trim().ToUpperInvariant();
        if (cor is not (QuebraVolumes.Branco or QuebraVolumes.Vermelho)) return BadRequest(new { erro = "Cor: branco ou vermelho." });
        if (dto.ProdutosC is < 0 || dto.PrecoCaixa is < 0) return BadRequest(new { erro = "ProdutosC e preço não podem ser negativos." });
        var q = await db.QuebrasVolumes.FirstOrDefaultAsync(x => x.Data == dto.Data && x.Cor == cor);
        if (q == null) { q = new QuebraVolumes { Data = dto.Data, Cor = cor }; db.QuebrasVolumes.Add(q); }
        q.ProdutosC = dto.ProdutosC;
        q.PrecoCaixa = dto.PrecoCaixa is > 0 ? dto.PrecoCaixa : null;
        q.Observacao = Nulo(dto.Observacao);
        q.Origem = "manual";   // importar de novo a planilha não apaga o que foi mexido aqui
        q.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { q.Id });
    }

    [HttpDelete("quebras/{id:int}")]
    public async Task<IActionResult> ExcluirQuebra(int id)
    {
        var q = await db.QuebrasVolumes.FindAsync(id);
        if (q == null) return NotFound(new { erro = "Quebra não encontrada." });
        db.QuebrasVolumes.Remove(q);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────────── Despesas ─────────────────────────────

    [HttpGet("despesas")]
    public async Task<IActionResult> Despesas([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate) =>
        Ok(await db.DespesasEmbalados.AsNoTracking()
            .Where(d => (de == null || d.Data >= de) && (ate == null || d.Data <= ate))
            .OrderByDescending(d => d.Data).ThenByDescending(d => d.Id)
            .Select(d => new { d.Id, d.Data, d.Descricao, d.Valor, d.Observacao, d.Origem }).ToListAsync());

    [HttpPost("despesas")]
    public Task<IActionResult> CriarDespesa([FromBody] DespesaDto dto) => SalvarDespesa(null, dto);

    [HttpPut("despesas/{id:int}")]
    public Task<IActionResult> AtualizarDespesa(int id, [FromBody] DespesaDto dto) => SalvarDespesa(id, dto);

    private async Task<IActionResult> SalvarDespesa(int? id, DespesaDto dto)
    {
        if (Nulo(dto.Descricao) == null) return BadRequest(new { erro = "Diga o que foi a despesa." });
        var d = id == null ? new DespesaEmbalados() : await db.DespesasEmbalados.FindAsync(id.Value);
        if (d == null) return NotFound(new { erro = "Despesa não encontrada." });
        if (id == null) db.DespesasEmbalados.Add(d);
        d.Data = dto.Data; d.Descricao = dto.Descricao.Trim(); d.Valor = dto.Valor; d.Observacao = Nulo(dto.Observacao);
        d.Origem = "manual"; d.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { d.Id });
    }

    [HttpDelete("despesas/{id:int}")]
    public async Task<IActionResult> ExcluirDespesa(int id)
    {
        var d = await db.DespesasEmbalados.FindAsync(id);
        if (d == null) return NotFound(new { erro = "Despesa não encontrada." });
        db.DespesasEmbalados.Remove(d);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────────── Comissões ─────────────────────────────

    [HttpGet("comissoes")]
    public async Task<IActionResult> Comissoes([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate)
    {
        var lista = await db.ComissoesEmbalados.AsNoTracking()
            .Where(c => (de == null || c.Data >= de) && (ate == null || c.Data <= ate))
            .OrderByDescending(c => c.Data).ThenByDescending(c => c.Ordem).ThenByDescending(c => c.Id).ToListAsync();
        return Ok(lista.Select(c => new
        {
            c.Id, c.Data, c.Cliente, c.Tipo, c.Caixas, c.PrecoCompra, c.PrecoVenda, c.Frete, c.Fornecedor, c.Observacao, c.Origem,
            porCaixa = EmbaladosService.PorCaixa(c), total = EmbaladosService.TotalComissao(c),
        }));
    }

    [HttpPost("comissoes")]
    public Task<IActionResult> CriarComissao([FromBody] ComissaoDto dto) => SalvarComissao(null, dto);

    [HttpPut("comissoes/{id:int}")]
    public Task<IActionResult> AtualizarComissao(int id, [FromBody] ComissaoDto dto) => SalvarComissao(id, dto);

    private async Task<IActionResult> SalvarComissao(int? id, ComissaoDto dto)
    {
        if (Nulo(dto.Cliente) == null) return BadRequest(new { erro = "Diga o cliente." });
        if (dto.Caixas < 0) return BadRequest(new { erro = "Caixas não podem ser negativas." });
        var c = id == null ? new ComissaoEmbalados() : await db.ComissoesEmbalados.FindAsync(id.Value);
        if (c == null) return NotFound(new { erro = "Venda não encontrada." });
        if (id == null)
        {
            db.ComissoesEmbalados.Add(c);
            c.Ordem = (await db.ComissoesEmbalados.MaxAsync(x => (int?)x.Ordem) ?? 0) + 1;
        }
        c.Data = dto.Data; c.Cliente = dto.Cliente.Trim(); c.Tipo = Nulo(dto.Tipo); c.Caixas = dto.Caixas;
        c.PrecoCompra = dto.PrecoCompra; c.PrecoVenda = dto.PrecoVenda; c.Frete = dto.Frete;
        c.Fornecedor = Nulo(dto.Fornecedor); c.Observacao = Nulo(dto.Observacao);
        c.Origem = "manual"; c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { c.Id });
    }

    [HttpDelete("comissoes/{id:int}")]
    public async Task<IActionResult> ExcluirComissao(int id)
    {
        var c = await db.ComissoesEmbalados.FindAsync(id);
        if (c == null) return NotFound(new { erro = "Venda não encontrada." });
        db.ComissoesEmbalados.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ───────────────────────────── Salários ─────────────────────────────

    [HttpGet("salarios")]
    public async Task<IActionResult> Salarios([FromQuery] DateOnly? de, [FromQuery] DateOnly? ate) =>
        Ok(await db.SalariosEmbalados.AsNoTracking()
            .Where(s => (de == null || s.Mes >= new DateOnly(de.Value.Year, de.Value.Month, 1)) && (ate == null || s.Mes <= ate))
            .OrderByDescending(s => s.Mes).ThenBy(s => s.Ordem).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.Mes, s.Referencia, s.Valor, s.Inss, s.Fgts, s.Observacao, s.Origem }).ToListAsync());

    [HttpPost("salarios")]
    public Task<IActionResult> CriarSalario([FromBody] SalarioDto dto) => SalvarSalario(null, dto);

    [HttpPut("salarios/{id:int}")]
    public Task<IActionResult> AtualizarSalario(int id, [FromBody] SalarioDto dto) => SalvarSalario(id, dto);

    private async Task<IActionResult> SalvarSalario(int? id, SalarioDto dto)
    {
        if (Nulo(dto.Referencia) == null) return BadRequest(new { erro = "Diga de quem é o pagamento." });
        var s = id == null ? new SalarioEmbalados() : await db.SalariosEmbalados.FindAsync(id.Value);
        if (s == null) return NotFound(new { erro = "Pagamento não encontrado." });
        if (id == null)
        {
            db.SalariosEmbalados.Add(s);
            s.Ordem = (await db.SalariosEmbalados.MaxAsync(x => (int?)x.Ordem) ?? 0) + 1;
        }
        s.Mes = new DateOnly(dto.Mes.Year, dto.Mes.Month, 1); s.Referencia = dto.Referencia.Trim();
        s.Valor = dto.Valor; s.Inss = dto.Inss; s.Fgts = dto.Fgts; s.Observacao = Nulo(dto.Observacao);
        s.Origem = "manual"; s.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { s.Id });
    }

    [HttpDelete("salarios/{id:int}")]
    public async Task<IActionResult> ExcluirSalario(int id)
    {
        var s = await db.SalariosEmbalados.FindAsync(id);
        if (s == null) return NotFound(new { erro = "Pagamento não encontrado." });
        db.SalariosEmbalados.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
