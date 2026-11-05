using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/export")]
public class ExportController(ExcelExportService exportService) : ControllerBase
{
    private FileContentResult Excel(byte[] bytes, string filename) =>
        File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);

    [HttpGet("cte-geral")]
    public async Task<IActionResult> CTeGeral([FromQuery] int mes, [FromQuery] int ano)
    {
        var bytes = await exportService.ExportarCTeGeralAsync(mes, ano);
        return Excel(bytes, $"ctes_geral_{ano}_{mes:D2}.xlsx");
    }

    [HttpGet("cte-por-placa")]
    public async Task<IActionResult> CtePorPlaca([FromQuery] int mes, [FromQuery] int ano)
    {
        var bytes = await exportService.ExportarCtePorPlacaAsync(mes, ano);
        return Excel(bytes, $"ctes_por_placa_{ano}_{mes:D2}.xlsx");
    }

    [HttpGet("romaneio/{id}")]
    public async Task<IActionResult> Romaneio(int id)
    {
        try
        {
            var bytes = await exportService.ExportarRomaneioAsync(id);
            return Excel(bytes, $"romaneio_{id}.xlsx");
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { erro = ex.Message });
        }
    }

    [HttpGet("abastecimento")]
    public async Task<IActionResult> Abastecimento([FromQuery] int mes, [FromQuery] int ano)
    {
        var bytes = await exportService.ExportarAbastecimentoAsync(mes, ano);
        return Excel(bytes, $"abastecimento_{ano}_{mes:D2}.xlsx");
    }

    [HttpGet("seguro")]
    public async Task<IActionResult> Seguro([FromQuery] int mes, [FromQuery] int ano)
    {
        var bytes = await exportService.ExportarSeguroAsync(mes, ano);
        return Excel(bytes, $"seguro_{ano}_{mes:D2}.xlsx");
    }
}
