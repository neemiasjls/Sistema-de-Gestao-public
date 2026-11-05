using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/motorista")]
public class MotoristaController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool? ativo, [FromQuery] Socio? socio)
    {
        var query = db.Motoristas.AsQueryable();
        if (ativo.HasValue) query = query.Where(m => m.Ativo == ativo.Value);
        if (socio.HasValue) query = query.Where(m => m.SocioPrincipal == socio.Value);

        var lista = await query
            .OrderBy(m => m.Nome)
            .Select(m => new
            {
                m.Id,
                m.Nome,
                m.Apelido,
                m.CPF,
                m.Telefone,
                m.Ativo,
                SocioPrincipal = m.SocioPrincipal.HasValue ? m.SocioPrincipal.ToString() : null,
            })
            .ToListAsync();

        return Ok(lista);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var m = await db.Motoristas.FindAsync(id);
        if (m == null) return NotFound();
        return Ok(m);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarMotoristaDto dto)
    {
        var motorista = new Motorista
        {
            Nome = dto.Nome,
            Apelido = dto.Apelido,
            CPF = dto.CPF,
            Telefone = dto.Telefone,
            SocioPrincipal = dto.SocioPrincipal,
            Ativo = true,
        };

        db.Motoristas.Add(motorista);
        await db.SaveChangesAsync();
        return Created($"/api/motorista/{motorista.Id}", motorista);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] CriarMotoristaDto dto)
    {
        var motorista = await db.Motoristas.FindAsync(id);
        if (motorista == null) return NotFound();

        motorista.Nome = dto.Nome;
        motorista.Apelido = dto.Apelido;
        motorista.CPF = dto.CPF;
        motorista.Telefone = dto.Telefone;
        motorista.SocioPrincipal = dto.SocioPrincipal;

        await db.SaveChangesAsync();
        return Ok(motorista);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Desativar(int id)
    {
        var motorista = await db.Motoristas.FindAsync(id);
        if (motorista == null) return NotFound();
        motorista.Ativo = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

public record CriarMotoristaDto(
    string Nome,
    string? Apelido,
    string? CPF,
    string? Telefone,
    Socio? SocioPrincipal);
