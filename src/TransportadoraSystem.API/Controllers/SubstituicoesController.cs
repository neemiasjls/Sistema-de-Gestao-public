using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Aba Substituições da tela Viagens (FRE-03): os grupos do frete, a cidade de
/// cada um e os clientes da nota que viram aquele grupo no cálculo.
/// </summary>
[Authorize]
[ApiController]
[Route("api/calculo-frete/substituicoes")]
public class SubstituicoesController(AppDbContext db) : ControllerBase
{
    public record GrupoDto(string Nome, string Cidade, string Uf);
    public record ClienteDto(string Nome);

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var grupos = await db.GruposDestinatario.AsNoTracking().Include(g => g.Clientes)
            .OrderBy(g => g.Nome).ToListAsync();
        return Ok(grupos.Select(g => new
        {
            g.Id, g.Nome, g.Cidade, g.Uf,
            clientes = g.Clientes.OrderBy(c => c.Nome).Select(c => new { c.Id, c.Nome }),
        }));
    }

    [HttpPost("grupos")]
    public async Task<IActionResult> CriarGrupo(GrupoDto dto)
    {
        var (g, erro) = await ValidarGrupo(dto, null);
        if (erro != null) return erro;
        db.GruposDestinatario.Add(g!);
        await Salvar();
        return Ok(new { g!.Id });
    }

    [HttpPut("grupos/{id:int}")]
    public async Task<IActionResult> EditarGrupo(int id, GrupoDto dto)
    {
        var grupo = await db.GruposDestinatario.FindAsync(id);
        if (grupo == null) return NotFound();
        var (g, erro) = await ValidarGrupo(dto, id);
        if (erro != null) return erro;
        grupo.Nome = g!.Nome; grupo.Cidade = g.Cidade; grupo.Uf = g.Uf;
        await Salvar();
        return NoContent();
    }

    [HttpDelete("grupos/{id:int}")]
    public async Task<IActionResult> ExcluirGrupo(int id)
    {
        var grupo = await db.GruposDestinatario.FindAsync(id);
        if (grupo == null) return NotFound();
        db.GruposDestinatario.Remove(grupo);   // os clientes vão junto (cascade)
        await Salvar();
        return NoContent();
    }

    [HttpPost("grupos/{id:int}/clientes")]
    public async Task<IActionResult> AdicionarCliente(int id, ClienteDto dto)
    {
        if (!await db.GruposDestinatario.AnyAsync(g => g.Id == id)) return NotFound();
        var (nome, erro) = await ValidarCliente(dto, null);
        if (erro != null) return erro;
        var c = new ClienteSubstituido { GrupoDestinatarioId = id, Nome = nome! };
        db.ClientesSubstituidos.Add(c);
        await Salvar();
        return Ok(new { c.Id });
    }

    [HttpPut("clientes/{id:int}")]
    public async Task<IActionResult> EditarCliente(int id, ClienteDto dto)
    {
        var c = await db.ClientesSubstituidos.FindAsync(id);
        if (c == null) return NotFound();
        var (nome, erro) = await ValidarCliente(dto, id);
        if (erro != null) return erro;
        c.Nome = nome!;
        await Salvar();
        return NoContent();
    }

    [HttpDelete("clientes/{id:int}")]
    public async Task<IActionResult> ExcluirCliente(int id)
    {
        var c = await db.ClientesSubstituidos.FindAsync(id);
        if (c == null) return NotFound();
        db.ClientesSubstituidos.Remove(c);
        await Salvar();
        return NoContent();
    }

    private async Task Salvar()
    {
        await db.SaveChangesAsync();
        SubstituicoesService.Invalidar();
    }

    // A conta compara pelos dois jeitos de normalizar (NF-e e CT-e); o nome
    // repetido é barrado se bater em qualquer um deles.
    private static bool Mesmo(string a, string b) =>
        NFeImportService.Norm(a) == NFeImportService.Norm(b)
        || CTeImportService.NormalizarTextoFrete(a) == CTeImportService.NormalizarTextoFrete(b);

    private async Task<(GrupoDestinatario?, IActionResult?)> ValidarGrupo(GrupoDto dto, int? id)
    {
        var nome = (dto.Nome ?? "").Trim();
        var cidade = CTeImportService.NormalizarTextoFrete(dto.Cidade);
        var uf = CTeImportService.NormalizarTextoFrete(dto.Uf);
        if (nome.Length == 0) return (null, BadRequest(new { erro = "Informe o nome do grupo." }));
        if (cidade.Length == 0 || uf.Length != 2) return (null, BadRequest(new { erro = "Informe a cidade e a UF do grupo." }));

        var grupos = await db.GruposDestinatario.AsNoTracking().Where(g => g.Id != id).ToListAsync();
        if (grupos.Any(g => Mesmo(g.Nome, nome)))
            return (null, Conflict(new { erro = $"Já existe o grupo \"{nome}\"." }));
        var cliente = (await db.ClientesSubstituidos.AsNoTracking().Include(c => c.GrupoDestinatario).ToListAsync())
            .FirstOrDefault(c => Mesmo(c.Nome, nome));
        if (cliente != null)
            return (null, Conflict(new { erro = $"\"{nome}\" já é cliente do grupo {cliente.GrupoDestinatario!.Nome}." }));
        return (new GrupoDestinatario { Nome = nome, Cidade = cidade, Uf = uf }, null);
    }

    private async Task<(string?, IActionResult?)> ValidarCliente(ClienteDto dto, int? id)
    {
        var nome = (dto.Nome ?? "").Trim();
        if (nome.Length == 0) return (null, BadRequest(new { erro = "Informe o nome do cliente como vem na nota." }));

        // Um cliente só pode virar um grupo, senão a conta não sabe para onde ir
        var outro = (await db.ClientesSubstituidos.AsNoTracking().Include(c => c.GrupoDestinatario)
                .Where(c => c.Id != id).ToListAsync())
            .FirstOrDefault(c => Mesmo(c.Nome, nome));
        if (outro != null)
            return (null, Conflict(new { erro = $"\"{nome}\" já está no grupo {outro.GrupoDestinatario!.Nome}." }));
        var grupo = (await db.GruposDestinatario.AsNoTracking().ToListAsync()).FirstOrDefault(g => Mesmo(g.Nome, nome));
        if (grupo != null)
            return (null, Conflict(new { erro = $"\"{nome}\" já é um grupo." }));
        return (nome, null);
    }
}
