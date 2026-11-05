using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/veiculo")]
public class VeiculoController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var veiculos = await db.Veiculos
            .Include(v => v.MotoristaAtual)
            .OrderBy(v => v.Socio)
            .ThenBy(v => v.Placa)
            .Select(v => new
            {
                v.Id,
                v.Placa,
                Socio = v.Socio.ToString(),
                SocioId = (int)v.Socio,
                v.Ativo,
                Motorista = v.MotoristaAtual == null ? null : new
                {
                    v.MotoristaAtual.Id,
                    v.MotoristaAtual.Nome,
                    v.MotoristaAtual.Apelido,
                },
            })
            .ToListAsync();

        // Agrupar por sócio
        var agrupado = veiculos
            .GroupBy(v => v.Socio)
            .Select(g => new { Socio = g.Key, Veiculos = g.ToList() });

        return Ok(agrupado);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var v = await db.Veiculos
            .Include(v => v.MotoristaAtual)
            .FirstOrDefaultAsync(v => v.Id == id);

        if (v == null) return NotFound();
        return Ok(v);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarVeiculoDto dto)
    {
        var veiculo = await db.Veiculos.FindAsync(id);
        if (veiculo == null) return NotFound();

        if (dto.MotoristaAtualId.HasValue)
        {
            var motorista = await db.Motoristas.FindAsync(dto.MotoristaAtualId.Value);
            if (motorista == null) return BadRequest("Motorista não encontrado.");
            veiculo.MotoristaAtualId = dto.MotoristaAtualId.Value;
        }

        if (dto.Ativo.HasValue)
            veiculo.Ativo = dto.Ativo.Value;

        await db.SaveChangesAsync();
        return Ok(veiculo);
    }
}

public record AtualizarVeiculoDto(int? MotoristaAtualId, bool? Ativo);
