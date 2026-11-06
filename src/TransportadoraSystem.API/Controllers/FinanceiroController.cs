using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/financeiro")]
public class FinanceiroController(AppDbContext db) : ControllerBase
{
    // ── Lançamentos Banco ──────────────────────────────────────────

    [HttpGet("lancamentos")]
    public async Task<IActionResult> ListarLancamentos(
        [FromQuery] Socio? socio,
        [FromQuery] int? mes,
        [FromQuery] int? ano)
    {
        var q = db.LancamentosBanco
            .Include(l => l.Veiculo)
            .AsQueryable();

        if (socio.HasValue) q = q.Where(l => l.Socio == socio.Value);
        if (mes.HasValue)   q = q.Where(l => l.Mes == mes.Value);
        if (ano.HasValue)   q = q.Where(l => l.Ano == ano.Value);

        var lista = await q.OrderByDescending(l => l.Data).ToListAsync();

        var entradas  = lista.Where(l => l.Tipo == TipoLancamento.Entrada).Sum(l => l.Valor);
        var saidas    = lista.Where(l => l.Tipo == TipoLancamento.Saida).Sum(l => l.Valor);

        return Ok(new
        {
            resumo = new { entradas, saidas, saldo = entradas - saidas },
            lancamentos = lista.Select(l => new
            {
                l.Id,
                Socio = l.Socio.ToString(),
                Tipo  = l.Tipo.ToString(),
                l.Descricao,
                l.Valor,
                Data  = l.Data.ToString("dd/MM/yyyy"),
                l.Mes,
                l.Ano,
                Placa = l.Veiculo?.Placa,
            }),
        });
    }

    [HttpPost("lancamentos")]
    public async Task<IActionResult> CriarLancamento([FromBody] LancamentoDto dto)
    {
        var lanc = new LancamentoBanco
        {
            Socio      = dto.Socio,
            Tipo       = dto.Tipo,
            Descricao  = dto.Descricao,
            Valor      = dto.Valor,
            Data       = dto.Data,
            Mes        = dto.Data.Month,
            Ano        = dto.Data.Year,
            VeiculoId  = dto.VeiculoId,
        };
        db.LancamentosBanco.Add(lanc);
        await db.SaveChangesAsync();
        return Created($"/api/financeiro/lancamentos/{lanc.Id}", lanc);
    }

    [HttpDelete("lancamentos/{id}")]
    public async Task<IActionResult> ExcluirLancamento(int id)
    {
        var l = await db.LancamentosBanco.FindAsync(id);
        if (l == null) return NotFound();
        db.LancamentosBanco.Remove(l);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Resumo mensal por sócio ────────────────────────────────────

    [HttpGet("resumo-mensal")]
    public async Task<IActionResult> ResumoMensal([FromQuery] int mes, [FromQuery] int ano)
    {
        var lancamentos = await db.LancamentosBanco
            .Where(l => l.Mes == mes && l.Ano == ano)
            .ToListAsync();

        var socios = Enum.GetValues<Socio>();
        var resumo = socios.Select(s => new
        {
            Socio    = s.ToString(),
            Entradas = lancamentos.Where(l => l.Socio == s && l.Tipo == TipoLancamento.Entrada).Sum(l => l.Valor),
            Saidas   = lancamentos.Where(l => l.Socio == s && l.Tipo == TipoLancamento.Saida).Sum(l => l.Valor),
            Saldo    = lancamentos.Where(l => l.Socio == s && l.Tipo == TipoLancamento.Entrada).Sum(l => l.Valor)
                     - lancamentos.Where(l => l.Socio == s && l.Tipo == TipoLancamento.Saida).Sum(l => l.Valor),
        });

        return Ok(resumo);
    }

    // ── Saldos a Receber ──────────────────────────────────────────

    [HttpGet("saldos")]
    public async Task<IActionResult> ListarSaldos([FromQuery] bool? recebido, [FromQuery] string? empresa)
    {
        var q = db.SaldosReceber.Include(s => s.Veiculo).AsQueryable();
        if (recebido.HasValue) q = q.Where(s => s.Recebido == recebido.Value);
        if (!string.IsNullOrEmpty(empresa))
            q = q.Where(s => s.Empresa.Contains(empresa));

        var lista = await q.OrderByDescending(s => s.DataLancamento).ToListAsync();

        var totalPendente = lista.Where(s => !s.Recebido).Sum(s => s.Valor);

        return Ok(new
        {
            totalPendente,
            saldos = lista.Select(s => new
            {
                s.Id,
                s.Empresa,
                s.Tipo,
                s.Valor,
                DataLancamento  = s.DataLancamento.ToString("dd/MM/yyyy"),
                s.Recebido,
                DataRecebimento = s.DataRecebimento?.ToString("dd/MM/yyyy"),
                s.NumeroDocumento,
                CartaoFrete     = s.CartaoFrete?.ToString(),
                Placa           = s.Veiculo?.Placa,
            }),
        });
    }

    [HttpPost("saldos")]
    public async Task<IActionResult> CriarSaldo([FromBody] SaldoReceberDto dto)
    {
        var saldo = new SaldoReceber
        {
            Empresa         = dto.Empresa,
            Tipo            = dto.Tipo,
            Valor           = dto.Valor,
            DataLancamento  = dto.DataLancamento,
            Recebido        = false,
            CartaoFrete     = dto.CartaoFrete,
            VeiculoId       = dto.VeiculoId,
        };
        db.SaldosReceber.Add(saldo);
        await db.SaveChangesAsync();
        return Created($"/api/financeiro/saldos/{saldo.Id}", saldo);
    }

    [HttpPut("saldos/{id}/receber")]
    public async Task<IActionResult> ReceberSaldo(int id, [FromBody] ReceberSaldoDto dto)
    {
        var saldo = await db.SaldosReceber.FindAsync(id);
        if (saldo == null) return NotFound();

        saldo.Recebido        = true;
        saldo.DataRecebimento = dto.DataRecebimento ?? DateTime.Today;
        saldo.NumeroDocumento = dto.NumeroDocumento;

        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Saldo marcado como recebido.", saldo });
    }

    [HttpDelete("saldos/{id}")]
    public async Task<IActionResult> ExcluirSaldo(int id)
    {
        var s = await db.SaldosReceber.FindAsync(id);
        if (s == null) return NotFound();
        db.SaldosReceber.Remove(s);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Abastecimento ─────────────────────────────────────────────

    [HttpGet("abastecimentos")]
    public async Task<IActionResult> ListarAbastecimentos(
        [FromQuery] int? mes, [FromQuery] int? ano, [FromQuery] int? veiculoId)
    {
        var q = db.Abastecimentos
            .Include(a => a.Veiculo)
            .Include(a => a.Posto)
            .AsQueryable();

        if (mes.HasValue)      q = q.Where(a => a.Mes == mes.Value);
        if (ano.HasValue)      q = q.Where(a => a.Ano == ano.Value);
        if (veiculoId.HasValue) q = q.Where(a => a.VeiculoId == veiculoId.Value);

        var lista = await q.OrderByDescending(a => a.Data).ToListAsync();

        var totalMes = lista.Sum(a => a.ValorTotal);

        // Divisão por sócio
        var porSocio = lista
            .GroupBy(a => a.Veiculo?.Socio.ToString() ?? "?")
            .Select(g => new { Socio = g.Key, Total = g.Sum(a => a.ValorTotal) });

        return Ok(new
        {
            totalMes,
            porSocio,
            abastecimentos = lista.Select(a => new
            {
                a.Id,
                a.Data,
                Posto     = a.Posto?.Nome,
                Placa     = a.Veiculo?.Placa,
                Socio     = a.Veiculo?.Socio.ToString(),
                a.Litros,
                a.ValorLitro,
                a.ValorTotal,
            }),
        });
    }

    [HttpPost("abastecimentos")]
    public async Task<IActionResult> CriarAbastecimento([FromBody] AbastecimentoDto dto)
    {
        var ab = new Abastecimento
        {
            VeiculoId   = dto.VeiculoId,
            PostoId     = dto.PostoId,
            Data        = dto.Data,
            Litros      = dto.Litros,
            ValorLitro  = dto.ValorLitro,
            ValorTotal  = dto.Litros * dto.ValorLitro,
            Mes         = dto.Data.Month,
            Ano         = dto.Data.Year,
        };
        db.Abastecimentos.Add(ab);
        await db.SaveChangesAsync();
        return Created($"/api/financeiro/abastecimentos/{ab.Id}", ab);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────
public record LancamentoDto(
    Socio Socio, TipoLancamento Tipo, string? Descricao,
    decimal Valor, DateTime Data, int? VeiculoId);

public record SaldoReceberDto(
    string Empresa, string? Tipo, decimal Valor,
    DateTime DataLancamento, TipoCartaoFrete? CartaoFrete, int? VeiculoId);

public record ReceberSaldoDto(DateTime? DataRecebimento, string? NumeroDocumento);

public record AbastecimentoDto(
    int VeiculoId, int PostoId, DateTime Data,
    decimal Litros, decimal ValorLitro);
