using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/cte")]
public class CTeController(AppDbContext db, CTeImportService importService, CTeExcelService excelService,
    PlanilhaSeguroService planilhasSeguro, ILogger<CTeController> logger) : ControllerBase
{
    [HttpPost("importar")]
    public async Task<IActionResult> Importar(IFormFile arquivo)
    {
        if (arquivo == null || arquivo.Length == 0)
            return BadRequest("Arquivo XML não enviado.");

        using var sr = new StreamReader(arquivo.OpenReadStream());
        var xml = await sr.ReadToEndAsync();

        var resultado = await importService.ImportarXmlAsync(xml);

        if (!resultado.Sucesso)
            return BadRequest(new { erro = resultado.Erro });

        if (resultado.JaExistia)
            return Ok(new { mensagem = "CT-e já importado anteriormente.", cte = resultado.CTe });

        return Created($"/api/cte/{resultado.CTe!.Id}", resultado.CTe);
    }

    [HttpPost("importar-multiplos")]
    public async Task<IActionResult> ImportarMultiplos(IFormFileCollection arquivos)
    {
        var resultados = new List<object>();

        foreach (var arquivo in arquivos)
        {
            using var sr = new StreamReader(arquivo.OpenReadStream());
            var xml = await sr.ReadToEndAsync();
            var r = await importService.ImportarXmlAsync(xml);

            resultados.Add(new
            {
                arquivo = arquivo.FileName,
                sucesso = r.Sucesso,
                erro = r.Erro,
                jaExistia = r.JaExistia,
                cteId = r.CTe?.Id,
                numeroCte = r.CTe?.NumeroCTe,
            });
        }

        return Ok(resultados);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? mes,
        [FromQuery] int? ano,
        [FromQuery] int? veiculoId,
        [FromQuery] string? status)
    {
        var query = db.CTes
            .Include(c => c.Veiculo)
            .AsQueryable();

        if (mes.HasValue) query = query.Where(c => c.DataEmissao.Month == mes.Value);
        if (ano.HasValue) query = query.Where(c => c.DataEmissao.Year == ano.Value);
        if (veiculoId.HasValue) query = query.Where(c => c.VeiculoId == veiculoId.Value);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<Core.Enums.StatusCTe>(status, true, out var st))
            query = query.Where(c => c.Status == st);

        var ctes = await query
            .OrderByDescending(c => c.DataEmissao)
            .Select(c => new
            {
                c.Id,
                c.NumeroCTe,
                c.ChaveAcesso,
                c.DataEmissao,
                c.Remetente,
                c.Destinatario,
                c.UFOrigem,
                c.UFDestino,
                c.CidadeDestino,
                c.ValorNF,
                c.ValorFrete,
                c.AliquotaICMS,
                c.PesoKg,
                c.Status,
                c.NumeroNF,
                c.ClienteNome,
                Placa = c.Veiculo!.Placa,
                Socio = c.Veiculo.Socio.ToString(),
            })
            .ToListAsync();

        return Ok(ctes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var cte = await db.CTes
            .Include(c => c.Veiculo)
            .Include(c => c.Viagem)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (cte == null) return NotFound();
        return Ok(cte);
    }

    [HttpGet("por-placa")]
    public async Task<IActionResult> PorPlaca([FromQuery] int? mes, [FromQuery] int? ano)
    {
        var query = db.CTes.Include(c => c.Veiculo).AsQueryable();
        if (mes.HasValue) query = query.Where(c => c.DataEmissao.Month == mes.Value);
        if (ano.HasValue) query = query.Where(c => c.DataEmissao.Year == ano.Value);

        var agrupados = await query
            .GroupBy(c => new { c.Veiculo!.Placa, Socio = c.Veiculo.Socio.ToString() })
            .Select(g => new
            {
                g.Key.Placa,
                g.Key.Socio,
                Quantidade = g.Count(),
                TotalFrete = g.Sum(c => c.ValorFrete),
                TotalNF = g.Sum(c => c.ValorNF),
                CTes = g.OrderByDescending(c => c.DataEmissao).Select(c => new
                {
                    c.Id,
                    c.NumeroCTe,
                    c.DataEmissao,
                    c.Destinatario,
                    c.UFDestino,
                    c.ValorNF,
                    c.ValorFrete,
                    c.AliquotaICMS,
                    c.Status,
                })
            })
            .ToListAsync();

        return Ok(agrupados);
    }

    [HttpPost("calcular-frete")]
    public async Task<IActionResult> CalcularFrete([FromBody] FreteCalculoInput input)
    {
        try
        {
            if (input?.Itens == null || input.Itens.Count == 0)
                return BadRequest(new { erro = "Nenhum item informado." });

            var resultado = await importService.CalcularFreteAsync(input);
            return Ok(resultado);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao calcular frete");
            return StatusCode(500, new { erro = $"Erro ao calcular frete: {ex.Message}" });
        }
    }

    [HttpGet("distancias")]
    public async Task<IActionResult> ListarDistancias()
    {
        try
        {
            var lista = await importService.ListarDistanciasAsync();
            return Ok(lista.Select(r => new
            {
                id       = r.Id,
                origem   = r.Origem,
                ufOrigem = r.UFOrigem,
                destino  = r.Destino,
                uf       = r.UF,
                km       = r.Km,
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar distâncias");
            return StatusCode(500, new { erro = $"Erro ao listar rotas: {ex.Message}" });
        }
    }

    [HttpPost("distancias")]
    public async Task<IActionResult> AdicionarDistancia([FromBody] AdicionarDistanciaDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Origem) || string.IsNullOrWhiteSpace(dto.Destino) || dto.Km <= 0)
                return BadRequest(new { erro = "Origem, destino e km são obrigatórios." });

            await importService.AdicionarDistanciaAsync(dto.Origem, dto.UFOrigem, dto.Destino, dto.UF, dto.Km);
            return Ok(new { mensagem = "Distância cadastrada com sucesso." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao adicionar distância");
            return StatusCode(500, new { erro = $"Erro ao cadastrar rota: {ex.Message}" });
        }
    }

    [HttpPut("distancias/{id:int}")]
    public async Task<IActionResult> AtualizarDistancia(int id, [FromBody] AdicionarDistanciaDto dto)
    {
        try
        {
            var ok = await importService.AtualizarDistanciaAsync(id, dto.Origem, dto.UFOrigem, dto.Destino, dto.UF, dto.Km);
            if (!ok) return NotFound(new { mensagem = "Rota não encontrada." });
            return Ok(new { mensagem = "Rota atualizada." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao atualizar distância {Id}", id);
            return StatusCode(500, new { erro = $"Erro ao atualizar rota: {ex.Message}" });
        }
    }

    [HttpDelete("distancias")]
    public async Task<IActionResult> RemoverDistancia([FromBody] RemoverDistanciaDto dto)
    {
        try
        {
            var ok = await importService.RemoverDistanciaAsync(dto.Origem, dto.Destino, dto.UF);
            if (!ok) return NotFound(new { mensagem = "Rota não encontrada." });
            return Ok(new { mensagem = "Rota removida." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao remover distância");
            return StatusCode(500, new { erro = $"Erro ao remover rota: {ex.Message}" });
        }
    }

    private static string Brl(double v) =>
        v.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    // ── Alíquotas do seguro por UF (SEG-06): valem para as próximas planilhas SG ──
    [HttpGet("aliquotas-seguro")]
    public async Task<IActionResult> ListarAliquotas() =>
        Ok(await db.AliquotasSeguro.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Uf, a.Aliquota, a.AtualizadoEm }).ToListAsync());

    /// <summary>Grava a tabela inteira como veio da tela (muda, acrescenta e tira UF).</summary>
    [HttpPut("aliquotas-seguro")]
    public async Task<IActionResult> GravarAliquotas([FromBody] List<AliquotaSeguroDto> lista)
    {
        if (lista == null || lista.Count == 0)
            return BadRequest(new { erro = "A tabela precisa de pelo menos uma UF." });
        var limpas = lista.Select(a => (Uf: (a.Uf ?? "").Trim().ToUpperInvariant(), a.Aliquota)).ToList();
        if (limpas.Any(a => !System.Text.RegularExpressions.Regex.IsMatch(a.Uf, "^[A-Z]{2}$")))
            return BadRequest(new { erro = "UF tem duas letras, ex.: GO." });
        var repetida = limpas.GroupBy(a => a.Uf).FirstOrDefault(gp => gp.Count() > 1);
        if (repetida != null)
            return BadRequest(new { erro = $"{repetida.Key} está duas vezes." });
        if (limpas.Any(a => a.Aliquota <= 0 || a.Aliquota >= 100))
            return BadRequest(new { erro = "A alíquota tem de ser maior que zero (em %, ex.: 0,069)." });

        var atuais = await db.AliquotasSeguro.ToListAsync();
        foreach (var velha in atuais.Where(v => limpas.All(a => a.Uf != v.Uf)))
            db.AliquotasSeguro.Remove(velha);
        foreach (var (uf, aliq) in limpas)
        {
            var reg = atuais.FirstOrDefault(v => v.Uf == uf);
            if (reg == null) db.AliquotasSeguro.Add(new Core.Entities.AliquotaSeguro { Uf = uf, Aliquota = aliq });
            else if (reg.Aliquota != aliq) { reg.Aliquota = aliq; reg.AtualizadoEm = DateTime.UtcNow; }
        }
        await db.SaveChangesAsync();
        return await ListarAliquotas();
    }

    // ── Exportar planilha SG (arquivo único: MÊS-GERAL + abas por placa + RESUMO) ──
    private static readonly string[] MesesPt =
        ["", "JANEIRO", "FEVEREIRO", "MARÇO", "ABRIL", "MAIO", "JUNHO",
         "JULHO", "AGOSTO", "SETEMBRO", "OUTUBRO", "NOVEMBRO", "DEZEMBRO"];

    [HttpPost("exportar-planilha")]
    public async Task<IActionResult> ExportarPlanilha(
        IFormFileCollection arquivos,
        [FromQuery] double das = 0,
        [FromQuery] double iofSeguros = 0,
        [FromQuery] double servicoEscritorio = 0,
        [FromQuery] double taxaBSoft = 0,
        [FromQuery] double? faturaSeguro = null,
        [FromForm] string? placas = null,
        [FromQuery] bool semPlacaOk = false)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Nenhum arquivo enviado." });
            // A diferença dos seguros sai da fatura: sem ela não gera (SEG-05)
            if (faturaSeguro is not > 0)
                return BadRequest(new { erro = "Digite o valor da fatura do seguro." });

            var xmls = new List<string>();
            foreach (var arq in arquivos)
            {
                using var sr = new StreamReader(arq.OpenReadStream());
                xmls.Add(await sr.ReadToEndAsync());
            }

            // Alíquotas do seguro de agora (SEG-06); a planilha guardada fica com as de quando saiu
            var aliquotas = (await db.AliquotasSeguro.AsNoTracking().OrderBy(a => a.Id).ToListAsync())
                .Select(a => (a.Uf, (double)a.Aliquota)).ToList();
            IReadOnlyList<(string uf, double aliq)>? tabela = aliquotas.Count > 0 ? aliquotas : null;
            var linhas = excelService.ParsearXmls(xmls, tabela);

            // Placas digitadas na tela para os CT-es que vieram sem (número do CT-e → placa).
            // Fica com a mesma grafia da placa que já veio em outro CT-e do mês, senão com traço,
            // para o mesmo caminhão não virar duas abas.
            var manuais = string.IsNullOrWhiteSpace(placas) ? new Dictionary<int, string>()
                : System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, string>>(placas) ?? new();
            if (manuais.Count > 0)
            {
                string Chave(string p) => ViagemCaminhaoCalculo.NormalizarPlaca(p);
                var doMes = linhas.Where(l => !string.IsNullOrEmpty(l.Placa)).Select(l => l.Placa).Distinct()
                    .GroupBy(Chave).ToDictionary(gp => gp.Key, gp => gp.First());
                linhas = linhas.Select(l =>
                    string.IsNullOrEmpty(l.Placa) && l.Data != "CANCELADO" && l.Cte is int n
                        && manuais.TryGetValue(n, out var pl) && !string.IsNullOrWhiteSpace(pl)
                        ? l with { Placa = doMes.GetValueOrDefault(Chave(pl), Chave(pl)) }
                        : l).ToList();
            }

            // Frete sem placa não entra em aba nenhuma: a tela pergunta a placa antes de gerar
            var semPlaca = linhas.Where(l => string.IsNullOrEmpty(l.Placa) && l.Data != "CANCELADO").ToList();
            if (semPlaca.Count > 0 && !semPlacaOk)
                return UnprocessableEntity(new
                {
                    erro = semPlaca.Count == 1 ? "Um frete veio sem placa." : $"{semPlaca.Count} fretes vieram sem placa.",
                    semPlaca = semPlaca.Select(l => new { l.Cte, l.Data, l.Cliente, l.UfCliente, l.VFrete }),
                    placasDoMes = linhas.Where(l => !string.IsNullOrEmpty(l.Placa)).Select(l => l.Placa).Distinct().OrderBy(x => x),
                });

            // Mês/ano mais frequente das datas dos CT-es
            var datas = linhas
                .Select(l => DateTime.TryParse(l.Data, out var dt) ? (DateTime?)dt : null)
                .Where(d => d.HasValue).Select(d => d!.Value).ToList();
            var refMes = datas.Count > 0
                ? datas.GroupBy(d => new { d.Month, d.Year }).OrderByDescending(gp => gp.Count())
                       .Select(gp => gp.Key).First()
                : new { Month = DateTime.Now.Month, Year = DateTime.Now.Year };

            string mesRef = $"{MesesPt[refMes.Month]}  DE  {refMes.Year}";
            string nomeArquivo = $"{refMes.Month:D2}-SEGURO {MesesPt[refMes.Month]} {refMes.Year}.xlsx";

            var parametros = new CTeExcelService.ParametrosSG(
                das, iofSeguros, servicoEscritorio, taxaBSoft, mesRef, faturaSeguro.Value, tabela);

            var xlsxBytes = excelService.GerarSG(linhas, parametros);

            // Guarda no sistema (tela CT-es › Planilhas SG guardadas). Se não
            // der, a planilha sai assim mesmo e o aviso conta o que houve.
            var avisos = new List<string>();
            if (tabela == null)
                avisos.Add("As alíquotas do seguro não vieram do banco: usei as de sempre (GO/DF/MG/SP 0,069; TO 0,121; PA 0,147).");
            var semAliquota = linhas.Where(l => l.Data != "CANCELADO" && l.Aliquota == null)
                .Select(l => string.IsNullOrEmpty(l.UfCliente) ? "sem UF" : l.UfCliente).Distinct().ToList();
            if (semAliquota.Count > 0)
                avisos.Add($"Sem alíquota do seguro para {string.Join(", ", semAliquota)}: esses CT-es ficaram sem seguro. Cadastre em Seguro › Alíquotas e gere de novo.");
            var seguros = CTeExcelService.SegurosMensal(linhas);
            var diferenca = CTeExcelService.DiferencaSeguros(linhas, faturaSeguro.Value, iofSeguros);
            avisos.Add($"Diferença dos seguros: {Brl(diferenca)} (fatura {Brl(faturaSeguro.Value)} − seguros {Brl(seguros)} − IOF {Brl(iofSeguros)}).");
            try
            {
                var g = await planilhasSeguro.GuardarAsync(xlsxBytes, nomeArquivo, refMes.Month, refMes.Year, "gerada",
                    new PlanilhaSeguroService.ValoresInformados(
                        (decimal)das, (decimal)iofSeguros, (decimal)servicoEscritorio, (decimal)taxaBSoft,
                        (decimal)faturaSeguro.Value));
                if (g.Substituiu)
                    avisos.Add($"A planilha de {PlanilhaSeguroService.MesAno(refMes.Month, refMes.Year)} já estava guardada"
                               + (g.OrigemAnterior == "importada" ? " (importada do Drive)" : "")
                               + " e foi substituída por esta.");
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Planilha SG gerada, mas não foi guardada");
                avisos.Add("A planilha foi gerada, mas não consegui guardar no sistema. Guarde o arquivo baixado.");
            }
            if (avisos.Count > 0)
                Response.Headers["X-Avisos"] = Uri.EscapeDataString(string.Join(" | ", avisos));

            // nome exposto pro frontend (com acento -> URL-encode)
            Response.Headers.Append("X-Filename", Uri.EscapeDataString(nomeArquivo));
            return File(xlsxBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                nomeArquivo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao exportar planilha SG");
            return StatusCode(500, new { erro = $"Erro ao exportar: {ex.Message}" });
        }
    }
}

public record AliquotaSeguroDto(string Uf, decimal Aliquota);

public record AdicionarDistanciaDto(string Origem, string UFOrigem, string Destino, string UF, int Km);
public record RemoverDistanciaDto(string Origem, string Destino, string UF);
