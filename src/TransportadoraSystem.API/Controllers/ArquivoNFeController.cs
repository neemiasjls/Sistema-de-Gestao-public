using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Arquivo das notas: o XML e o DANFE de cada NF-e ficam guardados para baixar
/// depois pelo sistema, sem depender da pasta onde vieram.
/// </summary>
[Authorize]
[ApiController]
[Route("api/nfe-arquivos")]
public class ArquivoNFeController(AppDbContext db, ArquivoNFeService servico,
    ILogger<ArquivoNFeController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Guardar(IFormFileCollection? arquivos)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie os XMLs e/ou os PDFs das notas." });

        try
        {
            var lista = new List<(string, byte[])>();
            foreach (var a in arquivos)
            {
                using var ms = new MemoryStream();
                await a.CopyToAsync(ms);
                lista.Add((a.FileName, ms.ToArray()));
            }

            var r = await servico.GuardarAsync(lista);
            return Ok(new
            {
                novos = r.XmlsNovos,
                atualizados = r.XmlsAtualizados,
                pdfs = r.PdfsVinculados,
                avisos = r.Avisos,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro guardando arquivos de NF-e");
            return StatusCode(500, new { erro = "Erro ao guardar os arquivos." });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? busca, [FromQuery] DateOnly? de,
        [FromQuery] DateOnly? ate, [FromQuery] bool? semPdf)
    {
        var q = db.ArquivosNFe.AsQueryable();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim().ToUpperInvariant();
            q = q.Where(a => a.NumeroNF.Contains(b)
                          || a.ChaveAcesso.Contains(b)
                          || a.Emitente.ToUpper().Contains(b)
                          || a.Destinatario.ToUpper().Contains(b));
        }
        if (de.HasValue) q = q.Where(a => a.DataEmissao >= de.Value);
        if (ate.HasValue) q = q.Where(a => a.DataEmissao <= ate.Value);
        if (semPdf == true) q = q.Where(a => a.Pdf == null);

        int total = await q.CountAsync();

        // O conteúdo dos arquivos fica de fora: a lista precisa só do cabeçalho.
        var itens = await q
            .OrderByDescending(a => a.DataEmissao).ThenByDescending(a => a.Id)
            .Take(300)
            .Select(a => new
            {
                a.Id,
                a.ChaveAcesso,
                a.NumeroNF,
                a.DataEmissao,
                a.Emitente,
                a.Destinatario,
                a.CidadeDestino,
                a.Uf,
                a.ValorNF,
                a.NomeXml,
                a.NomePdf,
                temPdf = a.Pdf != null,
            })
            .ToListAsync();

        return Ok(new { total, mostrando = itens.Count, itens });
    }

    [HttpGet("{id:int}/xml")]
    public async Task<IActionResult> BaixarXml(int id)
    {
        var a = await db.ArquivosNFe.FindAsync(id);
        if (a == null) return NotFound(new { erro = "Nota não encontrada." });
        return File(a.Xml, "application/xml", a.NomeXml);
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> BaixarPdf(int id)
    {
        var a = await db.ArquivosNFe.FindAsync(id);
        if (a?.Pdf == null) return NotFound(new { erro = "Essa nota não tem PDF guardado." });
        return File(a.Pdf, "application/pdf", a.NomePdf ?? $"NF-{a.NumeroNF}.pdf");
    }

    // Editar uma carga salva: a tela traz de volta os XMLs das notas dela (pela chave)
    [HttpGet("chave/{chave}/xml")]
    public async Task<IActionResult> BaixarXmlPorChave(string chave)
    {
        var a = await db.ArquivosNFe.AsNoTracking().FirstOrDefaultAsync(x => x.ChaveAcesso == chave);
        if (a == null) return NotFound(new { erro = "XML dessa nota não está guardado." });
        return File(a.Xml, "application/xml", string.IsNullOrWhiteSpace(a.NomeXml) ? $"NF-{a.NumeroNF}.xml" : a.NomeXml);
    }

    // A tela de Viagens conhece a chave da nota, não o id do arquivo
    [HttpGet("chave/{chave}/pdf")]
    public async Task<IActionResult> BaixarPdfPorChave(string chave)
    {
        var a = await db.ArquivosNFe.FirstOrDefaultAsync(x => x.ChaveAcesso == chave);
        if (a?.Pdf == null)
            return NotFound(new { erro = "Essa nota não tem PDF guardado. Suba o PDF em Notas Fiscais." });

        return File(a.Pdf, "application/pdf", a.NomePdf ?? $"NF-{a.NumeroNF}.pdf");
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var a = await db.ArquivosNFe.FindAsync(id);
        if (a == null) return NotFound();
        db.ArquivosNFe.Remove(a);
        await db.SaveChangesAsync();
        return NoContent();
    }
}
