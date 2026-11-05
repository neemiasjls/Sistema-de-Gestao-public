using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/boleto")]
public class BoletoController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? clienteId,
        [FromQuery] string? status,
        [FromQuery] int? mes,
        [FromQuery] int? ano)
    {
        var q = db.Boletos
            .Include(b => b.Cliente)
            .Include(b => b.Veiculo)
            .AsQueryable();

        if (clienteId.HasValue) q = q.Where(b => b.ClienteId == clienteId.Value);
        if (mes.HasValue) q = q.Where(b => b.DataEmissao.Month == mes.Value);
        if (ano.HasValue) q = q.Where(b => b.DataEmissao.Year == ano.Value);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<StatusBoleto>(status, true, out var st))
            q = q.Where(b => b.Status == st);

        var hoje = DateTime.Today;
        var boletos = await q.OrderByDescending(b => b.DataVencimento).ToListAsync();

        // Atualizar status de vencidos automaticamente
        foreach (var b in boletos.Where(b => b.Status == StatusBoleto.Pendente && b.DataVencimento < hoje))
            b.Status = StatusBoleto.Vencido;

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();

        return Ok(boletos.Select(b => new
        {
            b.Id,
            b.Valor,
            b.DataEmissao,
            b.DataVencimento,
            Status = b.Status.ToString(),
            b.DataPagamento,
            b.Descricao,
            b.NossoNumero,
            TipoProduto = b.TipoProduto?.ToString(),
            DiasVencimento = (b.DataVencimento - hoje).Days,
            Cliente = b.Cliente == null ? null : new { b.Cliente.Id, b.Cliente.Nome },
            Veiculo = b.Veiculo == null ? null : new { b.Veiculo.Id, b.Veiculo.Placa },
        }));
    }

    [HttpGet("pendentes")]
    public async Task<IActionResult> Pendentes()
    {
        var hoje = DateTime.Today;
        var boletos = await db.Boletos
            .Include(b => b.Cliente)
            .Where(b => b.Status == StatusBoleto.Pendente || b.Status == StatusBoleto.Vencido)
            .OrderBy(b => b.DataVencimento)
            .ToListAsync();

        var agrupados = boletos
            .GroupBy(b => b.Cliente?.Nome ?? "Sem cliente")
            .Select(g => new
            {
                Cliente = g.Key,
                Total = g.Sum(b => b.Valor),
                Boletos = g.Select(b => new
                {
                    b.Id,
                    b.Valor,
                    b.DataVencimento,
                    Status = b.Status.ToString(),
                    DiasVencimento = (b.DataVencimento - hoje).Days,
                    b.Descricao,
                }),
            });

        return Ok(agrupados);
    }

    [HttpGet("vencendo")]
    public async Task<IActionResult> Vencendo([FromQuery] int dias = 3)
    {
        var hoje = DateTime.Today;
        var limite = hoje.AddDays(dias);

        var boletos = await db.Boletos
            .Include(b => b.Cliente)
            .Where(b =>
                b.Status == StatusBoleto.Pendente &&
                b.DataVencimento >= hoje &&
                b.DataVencimento <= limite)
            .OrderBy(b => b.DataVencimento)
            .Select(b => new
            {
                b.Id,
                b.Valor,
                b.DataVencimento,
                DiasRestantes = (int)(b.DataVencimento - hoje).TotalDays,
                b.Descricao,
                Cliente = b.Cliente == null ? null : new { b.Cliente.Id, b.Cliente.Nome },
            })
            .ToListAsync();

        return Ok(boletos);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarBoletoDto dto)
    {
        var boleto = new Boleto
        {
            ClienteId = dto.ClienteId,
            RomaneioId = dto.RomaneioId,
            VeiculoId = dto.VeiculoId,
            Valor = dto.Valor,
            DataEmissao = dto.DataEmissao ?? DateTime.Today,
            DataVencimento = dto.DataVencimento,
            Descricao = dto.Descricao,
            NossoNumero = dto.NossoNumero,
            TipoProduto = dto.TipoProduto,
            Status = StatusBoleto.Pendente,
        };

        db.Boletos.Add(boleto);
        await db.SaveChangesAsync();
        return Created($"/api/boleto/{boleto.Id}", boleto);
    }

    [HttpPut("{id}/pagar")]
    public async Task<IActionResult> Pagar(int id, [FromBody] PagarBoletoDto dto)
    {
        var boleto = await db.Boletos.FindAsync(id);
        if (boleto == null) return NotFound();

        boleto.Status = StatusBoleto.Pago;
        boleto.DataPagamento = dto.DataPagamento ?? DateTime.Today;

        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Boleto marcado como pago.", dataPagamento = boleto.DataPagamento });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var boleto = await db.Boletos.FindAsync(id);
        if (boleto == null) return NotFound();
        db.Boletos.Remove(boleto);
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CriarBoletoDto(
    int? ClienteId,
    int? RomaneioId,
    int? VeiculoId,
    decimal Valor,
    DateTime? DataEmissao,
    DateTime DataVencimento,
    string? Descricao,
    string? NossoNumero,
    TipoProduto? TipoProduto);

public record PagarBoletoDto(DateTime? DataPagamento);
