using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Cadastro dos tipos de volumes e de como cada nota os escreve, e a auditoria:
/// toda descrição de produto que já passou pelas notas guardadas, com o tipo
/// em que ela cai hoje.
/// </summary>
[Authorize]
[ApiController]
[Route("api/tipos-volume")]
public class TiposVolumeController(AppDbContext db, TiposVolumeService servico) : ControllerBase
{
    public record TipoDto(string Nome, string Cor, string? NomeExtenso = null);
    public record EscritaDto(string Texto);

    private static readonly string[] Cores = ["BRANCO", "VERMELHO", "OUTRO"];

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        await servico.SemearAsync();
        var tipos = await db.TiposVolume.Include(t => t.Escritas).OrderBy(t => t.Ordem).ThenBy(t => t.Nome).ToListAsync();
        return Ok(new
        {
            tipos = tipos.Select(t => new
            {
                t.Id, t.Nome, t.NomeExtenso, t.Cor,
                // Todo produto da nota vira linha no romaneio com o nome da nota (NFE-09)
                naPlanilha = true,
                par = NFeImportService.ParDoTipo(t.Nome),
                escritas = t.Escritas.OrderBy(e => e.Texto).Select(e => new { e.Id, e.Texto }),
            }),
            linhasRomaneio = NFeImportService.LinhasRomaneio,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Criar(TipoDto dto)
    {
        var erro = Validar(dto);
        if (erro != null) return BadRequest(new { erro });
        if (await db.TiposVolume.AnyAsync(t => t.Nome.ToLower() == dto.Nome.Trim().ToLower()))
            return Conflict(new { erro = $"Já existe o tipo \"{dto.Nome.Trim()}\"." });

        var ordem = (await db.TiposVolume.MaxAsync(t => (int?)t.Ordem) ?? 0) + 1;
        var tipo = new TipoVolume { Nome = dto.Nome.Trim(), NomeExtenso = Extenso(dto), Cor = dto.Cor, Ordem = ordem };
        db.TiposVolume.Add(tipo);
        await db.SaveChangesAsync();
        return Ok(new { tipo.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, TipoDto dto)
    {
        var erro = Validar(dto);
        if (erro != null) return BadRequest(new { erro });
        var tipo = await db.TiposVolume.FindAsync(id);
        if (tipo == null) return NotFound();
        if (await db.TiposVolume.AnyAsync(t => t.Id != id && t.Nome.ToLower() == dto.Nome.Trim().ToLower()))
            return Conflict(new { erro = $"Já existe o tipo \"{dto.Nome.Trim()}\"." });

        tipo.Nome = dto.Nome.Trim();
        tipo.NomeExtenso = Extenso(dto);
        tipo.Cor = dto.Cor;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var tipo = await db.TiposVolume.FindAsync(id);
        if (tipo == null) return NotFound();
        db.TiposVolume.Remove(tipo);   // as escritas vão junto (cascade)
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/escritas")]
    public async Task<IActionResult> AdicionarEscrita(int id, EscritaDto dto)
    {
        var texto = (dto.Texto ?? "").Trim();
        if (texto.Length == 0) return BadRequest(new { erro = "Informe a escrita." });
        if (!await db.TiposVolume.AnyAsync(t => t.Id == id)) return NotFound();

        // Uma escrita só pode cair num tipo, senão a nota não sabe para onde ir
        var norm = NFeImportService.NormEscrita(texto);
        var existentes = await db.EscritasVolume.Include(e => e.TipoVolume).ToListAsync();
        var repetida = existentes.FirstOrDefault(e => NFeImportService.NormEscrita(e.Texto) == norm);
        if (repetida != null)
            return Conflict(new { erro = $"\"{texto}\" já está em {repetida.TipoVolume!.Nome}." });

        var escrita = new EscritaVolume { TipoVolumeId = id, Texto = texto };
        db.EscritasVolume.Add(escrita);
        await db.SaveChangesAsync();
        return Ok(new { escrita.Id });
    }

    [HttpDelete("escritas/{id:int}")]
    public async Task<IActionResult> ExcluirEscrita(int id)
    {
        var e = await db.EscritasVolume.FindAsync(id);
        if (e == null) return NotFound();
        db.EscritasVolume.Remove(e);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Toda descrição de produto das notas guardadas, com o tipo em que cai
    /// hoje. As que não caem em nenhum tipo aparecem primeiro.
    /// </summary>
    [HttpGet("auditoria")]
    public async Task<IActionResult> Auditoria()
    {
        var escritas = await servico.EscritasAsync();
        var notas = await db.ArquivosNFe
            .OrderByDescending(a => a.DataEmissao)
            .Select(a => new { a.NumeroNF, a.DataEmissao, a.Emitente, a.Xml })
            .ToListAsync();

        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";
        var produtos = new Dictionary<string, Linha>();
        foreach (var nota in notas)
        {
            XDocument doc;
            try { doc = XDocument.Parse(Encoding.UTF8.GetString(nota.Xml)); }
            catch { continue; }

            foreach (var prod in doc.Descendants(ns + "prod"))
            {
                var xProd = prod.Element(ns + "xProd")?.Value.Trim() ?? "";
                if (xProd.Length == 0) continue;
                double.TryParse(prod.Element(ns + "vUnCom")?.Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var preco);

                var chave = NFeImportService.NormEscrita(xProd);
                if (!produtos.TryGetValue(chave, out var l))
                {
                    l = new Linha
                    {
                        Produto = xProd,
                        Tipo = NFeImportService.TipoDaEscrita(xProd, escritas),
                        Emitente = nota.Emitente,
                        UltimaNF = nota.NumeroNF,
                        UltimaData = nota.DataEmissao,
                        PrecoMin = preco, PrecoMax = preco,
                    };
                    produtos[chave] = l;
                }
                l.Vezes++;
                if (preco > 0)
                {
                    l.PrecoMin = l.PrecoMin <= 0 ? preco : Math.Min(l.PrecoMin, preco);
                    l.PrecoMax = Math.Max(l.PrecoMax, preco);
                }
            }
        }

        return Ok(produtos.Values
            .OrderBy(l => l.Tipo != null)
            .ThenBy(l => l.Tipo)
            .ThenBy(l => l.Produto)
            .Select(l => new
            {
                l.Produto, l.Tipo, l.Emitente, l.UltimaNF, l.UltimaData, l.Vezes, l.PrecoMin, l.PrecoMax,
                naPlanilha = true,
            }));
    }

    private class Linha
    {
        public string Produto = "";
        public string? Tipo;
        public string Emitente = "";
        public string UltimaNF = "";
        public DateOnly? UltimaData;
        public int Vezes;
        public double PrecoMin, PrecoMax;
    }

    private static string? Extenso(TipoDto dto) =>
        string.IsNullOrWhiteSpace(dto.NomeExtenso) ? null : dto.NomeExtenso.Trim();

    private static string? Validar(TipoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome)) return "Informe o nome do tipo.";
        if (!Cores.Contains(dto.Cor)) return "Cor inválida.";
        return null;
    }
}
