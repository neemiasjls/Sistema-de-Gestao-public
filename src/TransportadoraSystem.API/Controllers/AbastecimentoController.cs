using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/abastecimento")]
public class AbastecimentoController(
    AbastecimentoFechamentoService svc, CobrancasAbastecimentoGuarda guarda, AppDbContext db,
    ILogger<AbastecimentoController> logger) : ControllerBase
{
    // Gera a planilha de controle de abastecimentos a partir das faturas de venda
    // dos postos (uma ou várias, de postos diferentes)
    [HttpPost("gerar")]
    public async Task<IActionResult> Gerar(IFormFileCollection? faturas)
    {
        try
        {
            if (faturas == null || faturas.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs das faturas dos postos." });

            var nomes = new List<string>();
            var pdfs = new List<byte[]>();
            foreach (var arq in faturas)
            {
                using var ms = new MemoryStream();
                await arq.CopyToAsync(ms);
                pdfs.Add(ms.ToArray());
                nomes.Add(Path.GetFileName(arq.FileName));
            }

            var donos = await DonosDasPlacas.CarregarAsync(db);
            AbastecimentoFechamentoService.FechamentoResult res;
            try { res = svc.Gerar(pdfs, donos); }
            catch (PlacasSemCadastroException ex) { return CobrancasAbastecimentoGuarda.PedirDonos(ex.Placas); }

            // Guarda as faturas e a planilha. É um passo a mais: se falhar, a
            // planilha sai do mesmo jeito e o aviso conta que não ficou guardada.
            var avisos = new List<string>(res.Avisos);
            try
            {
                await Guardar(res, nomes, pdfs, donos);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao guardar as faturas e a planilha de abastecimentos");
                db.ChangeTracker.Clear();
                avisos.Add("A planilha foi gerada, mas não consegui guardar as faturas no sistema.");
            }

            if (avisos.Count > 0)
                Response.Headers["X-Avisos"] = Uri.EscapeDataString(string.Join(" | ", avisos));

            // conferência: soma lida x valor da fatura (pra tela mostrar se fechou)
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Response.Headers["X-Soma"] = res.Soma.ToString(inv);
            Response.Headers["X-Qtde"] = res.Quantidade.ToString(inv);
            if (res.TotalFatura.HasValue)
                Response.Headers["X-Total-Fatura"] = res.TotalFatura.Value.ToString(inv);

            // A liberação desses cabeçalhos fica na política de CORS (Program.cs) — setar
            // Access-Control-Expose-Headers aqui não funciona, o middleware sobrescreve depois.

            return File(res.Bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                res.NomeArquivo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar planilha de abastecimentos");
            return StatusCode(500, new { erro = $"Erro ao gerar planilha: {ex.Message}" });
        }
    }

    // PDF sem nenhum abastecimento e sem número de fatura não é fatura (um boleto
    // solto no campo errado, por exemplo): fica fora do que é guardado.
    private async Task Guardar(AbastecimentoFechamentoService.FechamentoResult res, List<string> nomes, List<byte[]> pdfs, DonosDasPlacas donos)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var agora = DateTime.UtcNow;
        var guardadas = new List<Core.Entities.FaturaPosto>();
        for (int i = 0; i < res.Faturas.Count; i++)
        {
            var f = res.Faturas[i];
            if (f.Abastecimentos.Count == 0 && f.Numero == "") continue;
            var (fatura, _) = await guarda.GuardarFatura(f, nomes[i], pdfs[i], agora, donos);
            await db.SaveChangesAsync();
            guardadas.Add(fatura);
        }
        if (guardadas.Count == 0) return;

        await guarda.GuardarPlanilha(res, guardadas);
        await guarda.Religar();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
