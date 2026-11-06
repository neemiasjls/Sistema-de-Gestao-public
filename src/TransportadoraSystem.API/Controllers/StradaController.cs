using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Agrotrade / TIP Bank (PEN-39): base de viagens (AGROTRADE.xlsx), faturas da Strada (.txt de repasse),
/// PDFs "Baixar contrato", ligação dos contratos às viagens e painel por sócio.
/// Arquivo repetido com mudança: responde "confirmar" e só grava com ?modo=manter|substituir (UI-21).
/// Motorista não entra (o filtro do Program.cs barra tudo fora do painel dele).
/// </summary>
[Authorize]
[ApiController]
[Route("api/strada")]
public class StradaController(StradaService strada, ILogger<StradaController> logger) : ControllerBase
{
    public record LigarPedido(int? ViagemId, string? Placa);

    [HttpPost("base/importar")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> ImportarBase(IFormFile? arquivo, [FromQuery] string? modo, CancellationToken ct)
    {
        if (arquivo == null || !arquivo.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { erro = "Envie a planilha AGROTRADE (.xlsx)." });
        try
        {
            return Ok(await strada.ImportarBaseAsync(arquivo.FileName, await Bytes(arquivo), modo, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao importar a base AGROTRADE {Nome}", arquivo.FileName);
            return StatusCode(500, new { erro = $"Não consegui importar a planilha: {ex.Message}" });
        }
    }

    [HttpPost("faturas/importar")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> ImportarFaturas(IFormFileCollection arquivos, [FromQuery] string? modo, CancellationToken ct)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie os arquivos de repasse da Strada (.txt)." });
        var lidos = new List<(string, byte[])>();
        var recusados = new List<StradaService.ResultadoArquivo>();
        foreach (var a in arquivos)
        {
            if (a.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) lidos.Add((a.FileName, await Bytes(a)));
            else recusados.Add(new(a.FileName, "erro", null,
                a.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? "O \"Exportar detalhamento\" (.xlsx) ainda não é aceito aqui: suba o arquivo de repasse (.txt)."
                    : "Não é arquivo de repasse (.txt).", 0, 0, 0, [], []));
        }
        try
        {
            var resultado = await strada.ImportarFaturasAsync(lidos, modo, ct);
            return Ok(recusados.Concat(resultado));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao importar faturas da Strada");
            return StatusCode(500, new { erro = $"Não consegui importar as faturas: {ex.Message}" });
        }
    }

    [HttpPost("contratos-pdf/importar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarPdfs(IFormFileCollection arquivos, [FromQuery] string? modo, CancellationToken ct)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie os PDFs \"Baixar contrato\" da Strada." });
        var lidos = new List<(string, byte[])>();
        foreach (var a in arquivos) lidos.Add((a.FileName, await Bytes(a)));
        try
        {
            return Ok(await strada.ImportarPdfsAsync(lidos, modo, ct));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao importar PDFs da Strada");
            return StatusCode(500, new { erro = $"Não consegui importar os PDFs: {ex.Message}" });
        }
    }

    [HttpGet("faturas")]
    public async Task<IActionResult> Faturas([FromQuery] int? ano, [FromQuery] int? mes, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest(new { erro = "Mês inválido." });
        return Ok(await strada.ListarFaturasAsync(ano, mes, ct));
    }

    [HttpGet("faturas/{numero}")]
    public async Task<IActionResult> Fatura(string numero, CancellationToken ct)
    {
        var f = await strada.FaturaAsync(numero, ct);
        return f == null ? NotFound(new { erro = $"Fatura {numero} não encontrada." }) : Ok(f);
    }

    [HttpGet("viagens")]
    public async Task<IActionResult> Viagens([FromQuery] int? ano, [FromQuery] int? mes, [FromQuery] string? busca, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest(new { erro = "Mês inválido." });
        return Ok(await strada.ListarViagensAsync(ano, mes, busca, ct));
    }

    [HttpGet("painel")]
    public async Task<IActionResult> Painel([FromQuery] int ano, [FromQuery] int mes, CancellationToken ct)
    {
        if (ano < 2000 || mes is < 1 or > 12) return BadRequest(new { erro = "Escolha o mês e o ano." });
        return Ok(await strada.PainelAsync(ano, mes, ct));
    }

    [HttpPut("contratos/{id:int}/ligar")]
    public async Task<IActionResult> Ligar(int id, [FromBody] LigarPedido pedido, CancellationToken ct)
    {
        var erro = await strada.LigarAsync(id, pedido.ViagemId, pedido.Placa, ct);
        return erro == null ? NoContent() : BadRequest(new { erro });
    }

    [HttpDelete("contratos/{id:int}/ligar")]
    public async Task<IActionResult> Desligar(int id, CancellationToken ct)
    {
        var erro = await strada.DesligarAsync(id, ct);
        return erro == null ? NoContent() : BadRequest(new { erro });
    }

    [HttpPut("contratos/{id:int}/confirmar")]
    public async Task<IActionResult> Confirmar(int id, CancellationToken ct)
    {
        var erro = await strada.ConfirmarAsync(id, ct);
        return erro == null ? NoContent() : BadRequest(new { erro });
    }

    private static async Task<byte[]> Bytes(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }
}
