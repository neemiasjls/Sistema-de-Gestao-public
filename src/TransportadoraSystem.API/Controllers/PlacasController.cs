using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Cadastro central de placas (VIA-20): cavalo ou carreta, com quem cuida. O painel do motorista
/// só aceita placa daqui. Só escritório e dono (o perfil motorista recebe 403 no Program.cs).
/// </summary>
[Authorize]
[ApiController]
[Route("api/placas")]
public class PlacasController(AppDbContext db) : ControllerBase
{
    public const string Cavalo = "Cavalo";
    public const string Carreta = "Carreta";

    private static readonly Regex RxValida = new(@"^[A-Z]{3}-\d[A-Z0-9]\d{2}$", RegexOptions.Compiled);

    // O responsável da placa é um dos sócios (VIA-19)
    public static readonly string[] Responsaveis = ["Bento", "Caio", "Dudu"];

    public record PlacaDto(string? Placa, string? Tipo, string? Responsavel, bool? Ativo);

    private static string? TipoValido(string? t) =>
        string.Equals(t?.Trim(), Cavalo, StringComparison.OrdinalIgnoreCase) ? Cavalo
        : string.Equals(t?.Trim(), Carreta, StringComparison.OrdinalIgnoreCase) ? Carreta : null;

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? tipo, [FromQuery] bool? ativas)
    {
        var q = db.PlacasFrota.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var t = TipoValido(tipo);
            if (t == null) return BadRequest(new { erro = "Tipo deve ser Cavalo ou Carreta." });
            q = q.Where(p => p.Tipo == t);
        }
        if (ativas == true) q = q.Where(p => p.Ativo);
        var lista = await q.OrderByDescending(p => p.Ativo).ThenBy(p => p.Placa).ToListAsync();

        // Quantas viagens usam a placa (cavalo na Placa, carreta na PlacaCarreta)
        var cavalos = await db.ViagensCaminhao.AsNoTracking().GroupBy(v => v.Placa)
            .Select(g => new { Placa = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Placa, x => x.N);
        var carretas = await db.ViagensCaminhao.AsNoTracking().Where(v => v.PlacaCarreta != null).GroupBy(v => v.PlacaCarreta!)
            .Select(g => new { Placa = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Placa, x => x.N);

        return Ok(lista.Select(p => new
        {
            p.Id, p.Placa, p.Tipo, p.Responsavel, p.Ativo,
            Viagens = (p.Tipo == Carreta ? carretas : cavalos).GetValueOrDefault(p.Placa),
        }));
    }

    [HttpPost]
    public Task<IActionResult> Criar([FromBody] PlacaDto dto) => Salvar(null, dto);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Atualizar(int id, [FromBody] PlacaDto dto) => Salvar(id, dto);

    private async Task<IActionResult> Salvar(int? id, PlacaDto dto)
    {
        var placa = NormalizarPlaca(dto.Placa);
        if (!RxValida.IsMatch(placa)) return BadRequest(new { erro = "Placa inválida (ex.: XAI-0F06)." });
        var tipo = TipoValido(dto.Tipo);
        if (tipo == null) return BadRequest(new { erro = "Escolha se é cavalo ou carreta." });

        var resp = string.IsNullOrWhiteSpace(dto.Responsavel) ? null
            : Responsaveis.FirstOrDefault(r => string.Equals(r, dto.Responsavel.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dto.Responsavel) && resp == null)
            return BadRequest(new { erro = "O responsável deve ser Bento, Caio ou Dudu." });

        var outra = await db.PlacasFrota.AsNoTracking().FirstOrDefaultAsync(p => p.Placa == placa && p.Id != (id ?? 0));
        if (outra != null)
            return BadRequest(new { erro = $"A placa {placa} já está cadastrada como {outra.Tipo.ToLowerInvariant()}." });

        PlacaFrota p;
        if (id is { } pid)
        {
            var achada = await db.PlacasFrota.FindAsync(pid);
            if (achada == null) return NotFound(new { erro = "Placa não encontrada." });
            p = achada;
        }
        else { p = new PlacaFrota(); db.PlacasFrota.Add(p); }

        p.Placa = placa;
        p.Tipo = tipo;
        p.Responsavel = resp;
        p.Ativo = dto.Ativo ?? true;
        p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { id = p.Id });
    }

    // Placa com viagens não se apaga (as viagens guardam o texto da placa): só desativa
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var p = await db.PlacasFrota.FindAsync(id);
        if (p == null) return NotFound(new { erro = "Placa não encontrada." });
        var usada = p.Tipo == Carreta
            ? await db.ViagensCaminhao.AnyAsync(v => v.PlacaCarreta == p.Placa)
            : await db.ViagensCaminhao.AnyAsync(v => v.Placa == p.Placa);
        if (usada)
        {
            p.Ativo = false;
            p.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Ok(new { desativada = true, mensagem = $"A placa {p.Placa} tem viagens: ficou desativada em vez de apagada." });
        }
        db.PlacasFrota.Remove(p);
        await db.SaveChangesAsync();
        return Ok(new { desativada = false, mensagem = $"Placa {p.Placa} apagada." });
    }

    // ── Importar CRLV (PDF): lê vários, mostra o resumo; só grava ao confirmar ──

    private const long MaxPdf = 8 * 1024 * 1024;

    [HttpPost("crlv/ler")]
    public async Task<IActionResult> LerCrlv([FromForm] List<IFormFile>? arquivos)
    {
        if (arquivos == null || arquivos.Count == 0) return BadRequest(new { erro = "Escolha os PDFs dos documentos (CRLV)." });
        var existentes = await db.PlacasFrota.AsNoTracking().ToDictionaryAsync(p => p.Placa, p => p.Tipo);
        var itens = new List<object>();
        var vistas = new HashSet<string>();
        foreach (var f in arquivos)
        {
            CrlvLeitura r;
            if (f.Length == 0 || f.Length > MaxPdf)
                r = new(null, null, null, "Arquivo vazio ou maior que 8 MB.");
            else
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms);
                var b = ms.ToArray();
                if (b.Length < 5 || b[0] != '%' || b[1] != 'P' || b[2] != 'D' || b[3] != 'F') r = new(null, null, null, "Este arquivo não é um PDF.");
                else
                {
                    try { r = CrlvLeitor.Ler(b); }
                    catch (Exception) { r = new(null, null, null, "Não consegui ler este PDF."); }
                }
            }
            string situacao;
            string? atual = null;
            if (r.Aviso != null || r.Placa == null || r.Tipo == null) situacao = "aviso";
            else if (!vistas.Add(r.Placa)) { situacao = "aviso"; r = r with { Aviso = $"A placa {r.Placa} já veio em outro arquivo." }; }
            else if (!existentes.TryGetValue(r.Placa, out atual)) situacao = "nova";
            else situacao = atual == r.Tipo ? "igual" : "muda";
            itens.Add(new { arquivo = f.FileName, placa = r.Placa, especie = r.Especie, tipo = r.Tipo, tipoAtual = atual, situacao, aviso = r.Aviso });
        }
        return Ok(itens);
    }

    public record CrlvItem(string? Placa, string? Tipo);

    [HttpPost("crlv/aplicar")]
    public async Task<IActionResult> AplicarCrlv([FromBody] List<CrlvItem>? itens)
    {
        if (itens == null || itens.Count == 0) return BadRequest(new { erro = "Nada para gravar." });
        int criadas = 0, mudadas = 0;
        var feitas = new HashSet<string>();
        foreach (var i in itens)
        {
            var placa = NormalizarPlaca(i.Placa);
            var tipo = TipoValido(i.Tipo);
            if (!RxValida.IsMatch(placa) || tipo == null || !feitas.Add(placa)) continue;
            var p = await db.PlacasFrota.FirstOrDefaultAsync(x => x.Placa == placa);
            if (p == null)
            {
                db.PlacasFrota.Add(new PlacaFrota { Placa = placa, Tipo = tipo, Ativo = true, AtualizadoEm = DateTime.UtcNow });
                criadas++;
            }
            else if (p.Tipo != tipo)
            {
                p.Tipo = tipo; p.AtualizadoEm = DateTime.UtcNow; mudadas++;
            }
        }
        await db.SaveChangesAsync();
        return Ok(new { criadas, mudadas });
    }
}
