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
public class StradaController(StradaService strada, SicoobService sicoob, ILogger<StradaController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public record LigarPedido(int? ViagemId, string? Placa);
    public record LigarRetornoPedido(int? RetornoId);

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
            return BadRequest(new { erro = "Envie os arquivos de repasse da Strada (.txt) ou o \"Exportar detalhamento\" (.xlsx)." });
        var lidos = new List<(string, byte[])>();
        var recusados = new List<StradaService.ResultadoArquivo>();
        var detalhes = new List<(string, byte[])>();
        foreach (var a in arquivos)
        {
            if (a.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) lidos.Add((a.FileName, await Bytes(a)));
            else if (a.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) detalhes.Add((a.FileName, await Bytes(a)));
            else recusados.Add(new(a.FileName, "erro", null, "Não é arquivo de repasse (.txt) nem \"Exportar detalhamento\" (.xlsx).", 0, 0, 0, [], []));
        }
        try
        {
            // Primeiro os .txt (criam a fatura incompleta), depois o detalhamento que a completa
            var resultado = await strada.ImportarFaturasAsync(lidos, modo, ct);
            foreach (var (nome, bytes) in detalhes) resultado.Add(await strada.ImportarDetalhamentoAsync(nome, bytes, modo, ct));
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

    // Confere com o extrato do Sicoob (só leitura) se o repasse de cada fatura caiu na conta.
    [HttpPost("faturas/conferir-banco")]
    public async Task<IActionResult> ConferirBanco([FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var ate = fim is { } f && f < hoje ? f : hoje;
        var de = inicio ?? ate.AddDays(-60);
        if (de > ate) return BadRequest(new { erro = "A data inicial está depois da final." });
        if (ate.DayNumber - de.DayNumber > 92) return BadRequest(new { erro = "Escolha um período de até 3 meses." });
        var falta = sicoob.FaltaConfigurar();
        if (falta != null) return BadRequest(new { erro = falta });
        var (extrato, erro) = await sicoob.ExtratoAsync(de, ate, ct);
        if (extrato == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro });
        // Fatura paga até 5 dias antes do fim do extrato: procura o crédito no período lido
        return Ok(await strada.ConferirBancoAsync(extrato.Lancamentos, de, ate, hoje, ct));
    }

    [HttpGet("faturas")]
    public async Task<IActionResult> Faturas([FromQuery] int? ano, [FromQuery] int? mes, [FromQuery] string? agrupar, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return BadRequest(new { erro = "Mês inválido." });
        if (agrupar is not (null or "" or "vencimento" or "caiu")) return BadRequest(new { erro = "Agrupar por \"vencimento\" ou \"caiu\"." });
        return Ok(await strada.ListarFaturasAsync(ano, mes, agrupar, ct));
    }

    /// <summary>Todas as planilhas de transferência do mês (vencimento) num .zip; as travadas vão no LEIA.txt.</summary>
    [HttpGet("faturas/planilhas")]
    public async Task<IActionResult> Planilhas([FromQuery] int ano, [FromQuery] int mes, CancellationToken ct)
    {
        if (ano < 2000 || mes is < 1 or > 12) return BadRequest(new { erro = "Escolha o mês e o ano." });
        var (bytes, nome, _) = await strada.PlanilhasDoMesAsync(ano, mes, ct);
        return File(bytes, "application/zip", nome);
    }

    /// <summary>Planilha "{nº}IP-TRANSFERÊNCIA (vencimento).xlsx"; 409 se houver contrato sem sócio ou sem número.</summary>
    [HttpGet("faturas/{numero}/planilha")]
    public async Task<IActionResult> Planilha(string numero, CancellationToken ct)
    {
        var p = await strada.PlanilhaAsync(numero, ct);
        if (p is not { } r) return NotFound(new { erro = $"Fatura {numero} não encontrada." });
        if (r.Erro != null) return Conflict(new { erro = r.Erro });
        return File(r.Bytes!, Xlsx, r.Nome);
    }

    public record NumeroPedido(int Numero);

    /// <summary>Troca o número da transferência; recusa número já usado.</summary>
    [HttpPut("faturas/{numero}/numero")]
    public async Task<IActionResult> MudarNumero(string numero, [FromBody] NumeroPedido pedido, CancellationToken ct)
    {
        var erro = await strada.MudarNumeroAsync(numero, pedido.Numero, ct);
        if (erro == "Fatura não encontrada.") return NotFound(new { erro = $"Fatura {numero} não encontrada." });
        return erro == null ? NoContent() : BadRequest(new { erro });
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

    /// <summary>Descargas dos retornos do mês (VIA-27): esperado no TIP × recebido, por retorno.</summary>
    [HttpGet("descargas")]
    public async Task<IActionResult> Descargas([FromQuery] int ano, [FromQuery] int mes, CancellationToken ct)
    {
        if (ano < 2000 || mes is < 1 or > 12) return BadRequest(new { erro = "Escolha o mês e o ano." });
        return Ok(await strada.DescargasAsync(ano, mes, ct));
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

    /// <summary>Reembolso de descarga ligado à mão a um retorno (VIA-27); retornoId nulo solta.</summary>
    [HttpPut("contratos/{id:int}/retorno")]
    public async Task<IActionResult> LigarRetorno(int id, [FromBody] LigarRetornoPedido pedido, CancellationToken ct)
    {
        var erro = await strada.LigarRetornoAsync(id, pedido.RetornoId, ct);
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
