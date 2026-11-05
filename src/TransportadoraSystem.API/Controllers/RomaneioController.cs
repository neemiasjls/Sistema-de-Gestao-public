using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/romaneio")]
public class RomaneioController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? clienteId,
        [FromQuery] int? mes,
        [FromQuery] int? ano,
        [FromQuery] bool? enviado)
    {
        var q = db.Romaneios
            .Include(r => r.Cliente)
            .Include(r => r.Itens)
            .Include(r => r.Boletos)
            .AsQueryable();

        if (clienteId.HasValue) q = q.Where(r => r.ClienteId == clienteId.Value);
        if (mes.HasValue) q = q.Where(r => r.Mes == mes.Value);
        if (ano.HasValue) q = q.Where(r => r.Ano == ano.Value);
        if (enviado.HasValue) q = q.Where(r => r.Enviado == enviado.Value);

        var lista = await q.OrderByDescending(r => r.DataRomaneio).ToListAsync();

        return Ok(lista.Select(r => new
        {
            r.Id,
            r.DataRomaneio,
            r.Mes,
            r.Ano,
            r.Enviado,
            r.DataEnvio,
            CanalEnvio = r.CanalEnvio?.ToString(),
            Total = r.Itens.Sum(i => i.Quantidade * i.ValorUnitario),
            QtdItens = r.Itens.Count,
            BoletosPendentes = r.Boletos.Count(b => b.Status == StatusBoleto.Pendente),
            Cliente = new { r.Cliente!.Id, r.Cliente.Nome, r.Cliente.NomeFantasia },
        }));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var r = await db.Romaneios
            .Include(r => r.Cliente)
            .Include(r => r.Itens)
            .Include(r => r.Boletos)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (r == null) return NotFound();

        return Ok(new
        {
            r.Id,
            r.DataRomaneio,
            r.Mes,
            r.Ano,
            r.Enviado,
            r.DataEnvio,
            CanalEnvio = r.CanalEnvio?.ToString(),
            Total = r.Itens.Sum(i => i.Quantidade * i.ValorUnitario),
            Cliente = new { r.Cliente!.Id, r.Cliente.Nome, r.Cliente.NomeFantasia, r.Cliente.Email },
            Itens = r.Itens.Select(i => new
            {
                i.Id,
                Produto = i.Produto.ToString(),
                i.Descricao,
                i.Quantidade,
                i.Unidade,
                i.ValorUnitario,
                ValorTotal = i.Quantidade * i.ValorUnitario,
            }),
            Boletos = r.Boletos,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] CriarRomaneioDto dto)
    {
        var cliente = await db.Clientes.FindAsync(dto.ClienteId);
        if (cliente == null) return BadRequest("Cliente não encontrado.");

        var data = dto.DataRomaneio ?? DateTime.Today;
        var romaneio = new Romaneio
        {
            ClienteId = dto.ClienteId,
            DataRomaneio = data,
            Mes = data.Month,
            Ano = data.Year,
        };

        db.Romaneios.Add(romaneio);
        await db.SaveChangesAsync();
        return Created($"/api/romaneio/{romaneio.Id}", new { romaneio.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] AtualizarRomaneioDto dto)
    {
        var romaneio = await db.Romaneios.FindAsync(id);
        if (romaneio == null) return NotFound();

        if (dto.DataRomaneio.HasValue)
        {
            romaneio.DataRomaneio = dto.DataRomaneio.Value;
            romaneio.Mes = dto.DataRomaneio.Value.Month;
            romaneio.Ano = dto.DataRomaneio.Value.Year;
        }

        await db.SaveChangesAsync();
        return Ok(romaneio);
    }

    [HttpPost("{id}/itens")]
    public async Task<IActionResult> AdicionarItem(int id, [FromBody] ItemRomaneioDto dto)
    {
        var romaneio = await db.Romaneios.FindAsync(id);
        if (romaneio == null) return NotFound();

        var item = new ItemRomaneio
        {
            RomaneioId = id,
            Produto = dto.Produto,
            Descricao = dto.Descricao,
            Quantidade = dto.Quantidade,
            Unidade = dto.Unidade,
            ValorUnitario = dto.ValorUnitario,
        };

        db.ItensRomaneio.Add(item);
        await db.SaveChangesAsync();
        return Created($"/api/romaneio/{id}/itens/{item.Id}", item);
    }

    [HttpPut("{id}/itens/{itemId}")]
    public async Task<IActionResult> AtualizarItem(int id, int itemId, [FromBody] ItemRomaneioDto dto)
    {
        var item = await db.ItensRomaneio
            .FirstOrDefaultAsync(i => i.Id == itemId && i.RomaneioId == id);
        if (item == null) return NotFound();

        item.Produto = dto.Produto;
        item.Descricao = dto.Descricao;
        item.Quantidade = dto.Quantidade;
        item.Unidade = dto.Unidade;
        item.ValorUnitario = dto.ValorUnitario;

        await db.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}/itens/{itemId}")]
    public async Task<IActionResult> RemoverItem(int id, int itemId)
    {
        var item = await db.ItensRomaneio
            .FirstOrDefaultAsync(i => i.Id == itemId && i.RomaneioId == id);
        if (item == null) return NotFound();

        db.ItensRomaneio.Remove(item);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id}/marcar-enviado")]
    public async Task<IActionResult> MarcarEnviado(int id, [FromQuery] string canal)
    {
        var romaneio = await db.Romaneios.FindAsync(id);
        if (romaneio == null) return NotFound();

        romaneio.Enviado = true;
        romaneio.DataEnvio = DateTime.UtcNow;
        romaneio.CanalEnvio = canal.ToLower() == "email" ? CanalEnvio.Email : CanalEnvio.WhatsApp;

        await db.SaveChangesAsync();
        return Ok(new { mensagem = $"Romaneio marcado como enviado por {canal}." });
    }

    // Copiar romaneio anterior do mesmo cliente
    [HttpPost("{id}/copiar")]
    public async Task<IActionResult> Copiar(int id)
    {
        var original = await db.Romaneios
            .Include(r => r.Itens)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (original == null) return NotFound();

        var hoje = DateTime.Today;
        var novo = new Romaneio
        {
            ClienteId = original.ClienteId,
            DataRomaneio = hoje,
            Mes = hoje.Month,
            Ano = hoje.Year,
            Itens = original.Itens.Select(i => new ItemRomaneio
            {
                Produto = i.Produto,
                Descricao = i.Descricao,
                Quantidade = i.Quantidade,
                Unidade = i.Unidade,
                ValorUnitario = i.ValorUnitario,
            }).ToList(),
        };

        db.Romaneios.Add(novo);
        await db.SaveChangesAsync();
        return Created($"/api/romaneio/{novo.Id}", new { novo.Id, copiaDe = id });
    }
}

public record CriarRomaneioDto(int ClienteId, DateTime? DataRomaneio);
public record AtualizarRomaneioDto(DateTime? DataRomaneio);
public record ItemRomaneioDto(
    TipoProduto Produto,
    string? Descricao,
    decimal Quantidade,
    string? Unidade,
    decimal ValorUnitario);
