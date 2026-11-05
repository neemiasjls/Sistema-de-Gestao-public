using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/cliente")]
public class ClienteController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool? ativo, [FromQuery] string? nome)
    {
        var query = db.Clientes.AsQueryable();
        if (ativo.HasValue) query = query.Where(c => c.Ativo == ativo.Value);
        if (!string.IsNullOrEmpty(nome))
            query = query.Where(c => c.Nome.Contains(nome) || (c.NomeFantasia != null && c.NomeFantasia.Contains(nome)));

        return Ok(await query.OrderBy(c => c.Nome).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var cliente = await db.Clientes
            .Include(c => c.Romaneios.OrderByDescending(r => r.DataRomaneio).Take(10))
            .Include(c => c.Boletos.OrderByDescending(b => b.DataEmissao).Take(10))
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cliente == null) return NotFound();
        return Ok(cliente);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] ClienteDto dto)
    {
        var cliente = new Cliente
        {
            Nome = dto.Nome,
            NomeFantasia = dto.NomeFantasia,
            CNPJ = dto.CNPJ,
            Email = dto.Email,
            Telefone = dto.Telefone,
            Cidade = dto.Cidade,
            UF = dto.UF,
            Ativo = true,
        };

        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        return Created($"/api/cliente/{cliente.Id}", cliente);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] ClienteDto dto)
    {
        var cliente = await db.Clientes.FindAsync(id);
        if (cliente == null) return NotFound();

        cliente.Nome = dto.Nome;
        cliente.NomeFantasia = dto.NomeFantasia;
        cliente.CNPJ = dto.CNPJ;
        cliente.Email = dto.Email;
        cliente.Telefone = dto.Telefone;
        cliente.Cidade = dto.Cidade;
        cliente.UF = dto.UF;
        if (dto.Ativo.HasValue) cliente.Ativo = dto.Ativo.Value;

        await db.SaveChangesAsync();
        return Ok(cliente);
    }
}

public record ClienteDto(
    string Nome,
    string? NomeFantasia,
    string? CNPJ,
    string? Email,
    string? Telefone,
    string? Cidade,
    string? UF,
    bool? Ativo);
