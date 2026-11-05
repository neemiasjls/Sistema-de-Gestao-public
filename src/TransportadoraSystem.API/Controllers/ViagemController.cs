using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/viagem")]
public class ViagemController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? mes,
        [FromQuery] int? ano,
        [FromQuery] int? veiculoId,
        [FromQuery] Socio? socio)
    {
        var query = db.Viagens
            .Include(v => v.Veiculo)
            .Include(v => v.Motorista)
            .Include(v => v.Despesas)
            .Include(v => v.CTes)
            .AsQueryable();

        if (mes.HasValue) query = query.Where(v => v.Mes == mes.Value);
        if (ano.HasValue) query = query.Where(v => v.Ano == ano.Value);
        if (veiculoId.HasValue) query = query.Where(v => v.VeiculoId == veiculoId.Value);
        if (socio.HasValue) query = query.Where(v => v.Veiculo!.Socio == socio.Value);

        var viagens = await query
            .OrderByDescending(v => v.DataSaida)
            .ToListAsync();

        return Ok(viagens.Select(v => new
        {
            v.Id,
            v.DataSaida,
            v.DataRetorno,
            v.Destino,
            v.FreteRecebido,
            v.FreteRetorno,
            v.Diarias,
            v.Comissao,
            v.Adiantamento,
            v.SaldoAnterior,
            v.Mes,
            v.Ano,
            v.FechamentoRealizado,
            SaldoFinal = v.SaldoAnterior + v.FreteRecebido + v.FreteRetorno
                - v.Diarias - v.Comissao - v.Adiantamento
                - v.Despesas.Sum(d => d.Valor),
            TotalDespesas = v.Despesas.Sum(d => d.Valor),
            QtdCTes = v.CTes.Count,
            Veiculo = new { v.Veiculo!.Id, v.Veiculo.Placa, Socio = v.Veiculo.Socio.ToString() },
            Motorista = new { v.Motorista!.Id, v.Motorista.Nome, v.Motorista.Apelido },
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var viagem = await db.Viagens
            .Include(v => v.Veiculo)
            .Include(v => v.Motorista)
            .Include(v => v.Despesas)
            .Include(v => v.CTes).ThenInclude(c => c.Veiculo)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (viagem == null) return NotFound();

        return Ok(new
        {
            viagem.Id,
            viagem.DataSaida,
            viagem.DataRetorno,
            viagem.Destino,
            viagem.FreteRecebido,
            viagem.FreteRetorno,
            viagem.Diarias,
            viagem.Comissao,
            viagem.Adiantamento,
            viagem.SaldoAnterior,
            viagem.Mes,
            viagem.Ano,
            viagem.FechamentoRealizado,
            SaldoFinal = viagem.SaldoAnterior + viagem.FreteRecebido + viagem.FreteRetorno
                - viagem.Diarias - viagem.Comissao - viagem.Adiantamento
                - viagem.Despesas.Sum(d => d.Valor),
            TotalDespesas = viagem.Despesas.Sum(d => d.Valor),
            Veiculo = new { viagem.Veiculo!.Id, viagem.Veiculo.Placa, Socio = viagem.Veiculo.Socio.ToString() },
            Motorista = new { viagem.Motorista!.Id, viagem.Motorista.Nome },
            Despesas = viagem.Despesas,
            CTes = viagem.CTes.Select(c => new
            {
                c.Id, c.NumeroCTe, c.DataEmissao, c.Destinatario, c.ValorFrete, c.UFDestino
            }),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarViagemDto dto)
    {
        var veiculo = await db.Veiculos.FindAsync(dto.VeiculoId);
        if (veiculo == null) return BadRequest("Veículo não encontrado.");

        var motorista = await db.Motoristas.FindAsync(dto.MotoristaId);
        if (motorista == null) return BadRequest("Motorista não encontrado.");

        var viagem = new Viagem
        {
            VeiculoId = dto.VeiculoId,
            MotoristaId = dto.MotoristaId,
            DataSaida = dto.DataSaida,
            DataRetorno = dto.DataRetorno,
            Destino = dto.Destino,
            FreteRecebido = dto.FreteRecebido,
            FreteRetorno = dto.FreteRetorno,
            Diarias = dto.Diarias,
            Comissao = dto.Comissao,
            Adiantamento = dto.Adiantamento,
            SaldoAnterior = dto.SaldoAnterior,
            Mes = dto.DataSaida.Month,
            Ano = dto.DataSaida.Year,
        };

        db.Viagens.Add(viagem);
        await db.SaveChangesAsync();

        // Vincular CT-es se informados
        if (dto.CTeIds?.Any() == true)
        {
            var ctes = await db.CTes
                .Where(c => dto.CTeIds.Contains(c.Id) && c.ViagemId == null)
                .ToListAsync();

            foreach (var cte in ctes)
                cte.ViagemId = viagem.Id;

            await db.SaveChangesAsync();
        }

        return Created($"/api/viagem/{viagem.Id}", viagem);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] CriarViagemDto dto)
    {
        var viagem = await db.Viagens.FindAsync(id);
        if (viagem == null) return NotFound();

        viagem.VeiculoId = dto.VeiculoId;
        viagem.MotoristaId = dto.MotoristaId;
        viagem.DataSaida = dto.DataSaida;
        viagem.DataRetorno = dto.DataRetorno;
        viagem.Destino = dto.Destino;
        viagem.FreteRecebido = dto.FreteRecebido;
        viagem.FreteRetorno = dto.FreteRetorno;
        viagem.Diarias = dto.Diarias;
        viagem.Comissao = dto.Comissao;
        viagem.Adiantamento = dto.Adiantamento;
        viagem.SaldoAnterior = dto.SaldoAnterior;

        await db.SaveChangesAsync();
        return Ok(viagem);
    }

    [HttpPost("{id}/despesas")]
    public async Task<IActionResult> AdicionarDespesa(int id, [FromBody] DespesaDto dto)
    {
        var viagem = await db.Viagens.FindAsync(id);
        if (viagem == null) return NotFound();

        var despesa = new DespesaViagem
        {
            ViagemId = id,
            Tipo = dto.Tipo,
            Descricao = dto.Descricao,
            Valor = dto.Valor,
            Data = dto.Data,
        };

        db.DespesasViagem.Add(despesa);
        await db.SaveChangesAsync();
        return Created($"/api/viagem/{id}/despesas/{despesa.Id}", despesa);
    }

    [HttpDelete("{id}/despesas/{despesaId}")]
    public async Task<IActionResult> RemoverDespesa(int id, int despesaId)
    {
        var despesa = await db.DespesasViagem
            .FirstOrDefaultAsync(d => d.Id == despesaId && d.ViagemId == id);

        if (despesa == null) return NotFound();

        db.DespesasViagem.Remove(despesa);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/vincular-ctes")]
    public async Task<IActionResult> VincularCTes(int id, [FromBody] VincularCTesDto dto)
    {
        var viagem = await db.Viagens.FindAsync(id);
        if (viagem == null) return NotFound();

        var ctes = await db.CTes
            .Where(c => dto.CTeIds.Contains(c.Id))
            .ToListAsync();

        foreach (var cte in ctes)
            cte.ViagemId = id;

        await db.SaveChangesAsync();
        return Ok(new { vinculados = ctes.Count });
    }

    [HttpPost("fechar-mes")]
    public async Task<IActionResult> FecharMes([FromBody] FecharMesDto dto)
    {
        var viagens = await db.Viagens
            .Include(v => v.Despesas)
            .Where(v => v.VeiculoId == dto.VeiculoId && v.Mes == dto.Mes && v.Ano == dto.Ano && !v.FechamentoRealizado)
            .ToListAsync();

        if (!viagens.Any())
            return BadRequest("Nenhuma viagem encontrada para o período.");

        var acerto = new AcertoCaminhao
        {
            VeiculoId = dto.VeiculoId,
            MotoristaId = viagens.First().MotoristaId,
            Mes = dto.Mes,
            Ano = dto.Ano,
            TotalFrete = viagens.Sum(v => v.FreteRecebido + v.FreteRetorno),
            TotalDespesas = viagens.Sum(v => v.Despesas.Sum(d => d.Valor)),
            TotalDiarias = viagens.Sum(v => v.Diarias),
            TotalComissao = viagens.Sum(v => v.Comissao),
            TotalAdiantamento = viagens.Sum(v => v.Adiantamento),
            SaldoFinal = viagens.Sum(v => v.SaldoAnterior + v.FreteRecebido + v.FreteRetorno
                - v.Diarias - v.Comissao - v.Adiantamento
                - v.Despesas.Sum(d => d.Valor)),
        };

        db.AcertosCaminhao.Add(acerto);

        foreach (var v in viagens)
            v.FechamentoRealizado = true;

        await db.SaveChangesAsync();
        return Ok(acerto);
    }
}

public record CriarViagemDto(
    int VeiculoId,
    int MotoristaId,
    DateTime DataSaida,
    DateTime? DataRetorno,
    string? Destino,
    decimal FreteRecebido,
    decimal FreteRetorno,
    decimal Diarias,
    decimal Comissao,
    decimal Adiantamento,
    decimal SaldoAnterior,
    List<int>? CTeIds);

public record DespesaDto(TipoDespesa Tipo, string? Descricao, decimal Valor, DateTime Data);
public record VincularCTesDto(List<int> CTeIds);
public record FecharMesDto(int VeiculoId, int Mes, int Ano);
