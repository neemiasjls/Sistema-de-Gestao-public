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

    public record DonoDaPlaca(string? Placa, int Socio);

    // Placas que vieram numa fatura de posto sem estar no cadastro (ABA-06): grava
    // cada uma com o dono escolhido na tela (ativa). Placa que já está no cadastro
    // com o mesmo dono passa (repetir é seguro); com outro dono, recusa sem mudar nada.
    [HttpPost("donos")]
    public async Task<IActionResult> CadastrarDonos([FromBody] List<DonoDaPlaca>? lista)
    {
        if (lista == null || lista.Count == 0) return BadRequest(new { erro = "Diga o dono de cada placa." });

        var pedidos = new Dictionary<string, (string Placa, Socio Socio)>();
        foreach (var p in lista)
        {
            var placa = Application.Services.ViagemCaminhaoCalculo.NormalizarPlaca(p.Placa);
            var chave = Application.Services.DonosDasPlacas.Chave(placa);
            if (chave.Length < 7) return BadRequest(new { erro = $"Placa inválida: {p.Placa}." });
            if (!Enum.IsDefined(typeof(Socio), p.Socio)) return BadRequest(new { erro = $"Escolha o dono da placa {placa}." });
            if (pedidos.TryGetValue(chave, out var ja) && ja.Socio != (Socio)p.Socio)
                return BadRequest(new { erro = $"A placa {placa} veio com dois donos diferentes." });
            pedidos[chave] = (placa, (Socio)p.Socio);
        }

        var donos = await Application.Services.DonosDasPlacas.CarregarAsync(db);
        foreach (var (_, (placa, socio)) in pedidos)
        {
            if (donos.SocioDe(placa) is { } atual && atual != socio)
                return Conflict(new { erro = $"A placa {placa} já está no cadastro como do {Application.Services.DonosDasPlacas.Nome(atual)}." });
        }

        var agora = DateTime.UtcNow;
        foreach (var (_, (placa, socio)) in pedidos)
        {
            if (donos.Tem(placa)) continue;
            db.Veiculos.Add(new Core.Entities.Veiculo { Placa = placa, Socio = socio, Ativo = true, CriadoEm = agora, AtualizadoEm = agora });
        }
        await db.SaveChangesAsync();
        return Ok(new { cadastradas = pedidos.Values.Select(p => new { p.Placa, Socio = (int)p.Socio }) });
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
