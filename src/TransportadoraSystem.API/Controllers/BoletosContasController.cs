using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// A tela Boletos em duas abas. "A pagar": os boletos dos postos (os mesmos da
/// tela Abastecimentos, tabela BoletosPosto) mais os avulsos subidos aqui.
/// "A receber": os boletos que a transportadora emite para os clientes — o das
/// produtosD da CD e o do frete da Girassol —, no formato da planilha de controle
/// de cada um (data da carga, caminhão, número, valor, vencimento, recebido).
/// </summary>
[Authorize]
[ApiController]
[Route("api/boletos-contas")]
public class BoletosContasController(AppDbContext db, ILogger<BoletosContasController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // O dia no Brasil: o servidor roda em UTC e à noite já seria amanhã
    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private static bool Data(string? s, out DateOnly? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (!DateOnly.TryParseExact(s.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return false;
        if (d.Year is < 2000 or > 2100) return false;
        data = d;
        return true;
    }

    private static List<string> Lista(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static async Task<List<(string Nome, byte[] Bytes)>> LerArquivos(IFormFileCollection arquivos)
    {
        var lista = new List<(string, byte[])>();
        foreach (var arq in arquivos)
        {
            using var ms = new MemoryStream();
            await arq.CopyToAsync(ms);
            lista.Add((Path.GetFileName(arq.FileName), ms.ToArray()));
        }
        return lista;
    }

    private static string NomeOuPadrao(string nome, string padrao) => string.IsNullOrWhiteSpace(nome) ? padrao : nome;

    // ═════════════════════════ A pagar ═════════════════════════

    // Postos e avulsos juntos, numa lista só. O filtro (em aberto / pagos) é da tela.
    [HttpGet("pagar")]
    public async Task<IActionResult> ListarAPagar()
    {
        var hoje = Hoje();
        var postos = await db.BoletosPosto.AsNoTracking()
            .Select(b => new
            {
                b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, b.Vencimento, b.DataChegada, b.PagoEm,
                b.AgendadoPara, PeloExtrato = b.PagoExtratoId != "",
                b.LidoDoPdf, b.AvisosJson, b.NomeArquivo, TemPdf = b.Pdf != null,
                Fatura = b.Fatura != null ? b.Fatura.Numero : null,
            })
            .ToListAsync();
        var avulsos = await db.BoletosPagarAvulsos.AsNoTracking()
            .Select(b => new
            {
                b.Id, b.Descricao, b.Beneficiario, b.Numero, b.Valor, b.Vencimento, b.DataChegada, b.PagoEm,
                b.LidoDoPdf, b.AvisosJson, b.NomeArquivo, TemPdf = b.Pdf != null,
            })
            .ToListAsync();

        var boletos = postos.Select(b => new BoletoAPagar(
                "POSTO", b.Id, b.Posto, b.Fatura != null ? $"Fatura {b.Fatura}" : "Posto de combustível",
                b.NumeroDocumento != "" ? b.NumeroDocumento : b.NossoNumero, b.Valor, b.Vencimento, b.DataChegada, b.PagoEm,
                b.LidoDoPdf, Lista(b.AvisosJson), b.NomeArquivo, b.TemPdf, b.AgendadoPara, b.PeloExtrato))
            .Concat(avulsos.Select(b => new BoletoAPagar(
                "AVULSO", b.Id, b.Beneficiario, b.Descricao, b.Numero, b.Valor, b.Vencimento, b.DataChegada, b.PagoEm,
                b.LidoDoPdf, Lista(b.AvisosJson), b.NomeArquivo, b.TemPdf, null, false)))
            .OrderBy(b => b.PagoEm != null)
            .ThenBy(b => b.Vencimento ?? DateOnly.MaxValue)
            .ThenByDescending(b => b.DataChegada)
            .ToList();

        var abertos = boletos.Where(b => b.PagoEm == null).ToList();
        var vencidos = abertos.Where(b => b.Vencimento < hoje).ToList();
        var aVencer = abertos.Where(b => b.Vencimento == null || b.Vencimento >= hoje).ToList();
        return Ok(new
        {
            Hoje = hoje,
            Resumo = new
            {
                Abertos = abertos.Count,
                TotalAberto = abertos.Sum(b => b.Valor ?? 0),
                TotalAVencer = aVencer.Sum(b => b.Valor ?? 0),
                Vencidos = vencidos.Count,
                TotalVencido = vencidos.Sum(b => b.Valor ?? 0),
                SemValor = abertos.Count(b => b.Valor == null),
                VencemEm7Dias = abertos.Where(b => b.Vencimento >= hoje && b.Vencimento <= hoje.AddDays(7)).Sum(b => b.Valor ?? 0),
            },
            Boletos = boletos,
        });
    }

    public record BoletoAPagar(string Origem, int Id, string Beneficiario, string Descricao, string Numero, decimal? Valor,
        DateOnly? Vencimento, DateOnly DataChegada, DateOnly? PagoEm, bool LidoDoPdf, List<string> Avisos,
        string NomeArquivo, bool TemPdf, DateOnly? AgendadoPara = null, bool PeloExtrato = false);

    public record PagoPedido(string? PagoEm);

    // Marcar (ou desmarcar, com data vazia) o pagamento do boleto de posto. Só
    // a data do pagamento: o resto do boleto do posto se edita na tela Abastecimentos.
    [HttpPut("pagar/posto/{id:int}/pago")]
    public async Task<IActionResult> PagarPosto(int id, [FromBody] PagoPedido req)
    {
        if (!Data(req.PagoEm, out var pagoEm)) return BadRequest(new { erro = "Data do pagamento inválida." });
        var b = await db.BoletosPosto.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        // Desfazer o pago achado no extrato guarda o débito como "não é este" (ABA-07)
        PostosPagosExtrato.MudarPagamento(b, pagoEm);
        await db.SaveChangesAsync();
        return NoContent();
    }

    public record AgendadoPedido(string? Data);

    // Pagamento agendado no banco, marcado à mão (ABA-07, opção a): data vazia tira o agendado.
    // Quando o débito cair no extrato, a conferência marca pago e o agendado some.
    [HttpPut("pagar/posto/{id:int}/agendado")]
    public async Task<IActionResult> AgendarPosto(int id, [FromBody] AgendadoPedido req)
    {
        if (!Data(req.Data, out var data)) return BadRequest(new { erro = "Data do agendamento inválida." });
        var b = await db.BoletosPosto.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (data != null && b.PagoEm != null) return BadRequest(new { erro = "Este boleto já está pago." });
        b.AgendadoPara = data;
        b.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("pagar/avulsos/{id:int}/pago")]
    public async Task<IActionResult> PagarAvulso(int id, [FromBody] PagoPedido req)
    {
        if (!Data(req.PagoEm, out var pagoEm)) return BadRequest(new { erro = "Data do pagamento inválida." });
        var n = await db.BoletosPagarAvulsos.Where(b => b.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.PagoEm, pagoEm).SetProperty(b => b.AtualizadoEm, DateTime.UtcNow));
        if (n == 0) return NotFound(new { erro = "Boleto não encontrado." });
        return NoContent();
    }

    // Boletos avulsos em PDF: cada um é lido (beneficiário, número, valor,
    // vencimento); o que não der para ler fica guardado para digitar. O mesmo
    // PDF subido de novo fica de fora.
    [HttpPost("pagar/avulsos")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarAvulsos(IFormFileCollection arquivos, [FromForm] string? descricao,
        [FromServices] BoletoReceberImportacao importacao)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie os PDFs dos boletos." });

        var avisos = new List<string>();
        var novos = new List<BoletoPagarAvulso>();
        var hoje = Hoje();
        var agora = DateTime.UtcNow;
        // Boleto emitido pela TRX (CD, Girassol) solto aqui vai para A receber, como no Acerto
        // e nos E-mails: o sistema reconhece pelo beneficiário (Admin, 06/10/2026)
        var daTransportadora = new List<(string Nome, byte[] Bytes)>();
        foreach (var (nome, bytes) in await LerArquivos(arquivos))
        {
            if (!nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) { avisos.Add($"{nome}: não é PDF (ficou de fora)."); continue; }
            if (BoletoReceberLeitor.EmitidoPelaTransportadora(bytes)) { daTransportadora.Add((nome, bytes)); continue; }
            var hash = Hash(bytes);
            if (novos.Any(b => b.Hash == hash) || await db.BoletosPagarAvulsos.AnyAsync(b => b.Hash == hash))
            { avisos.Add($"{nome}: esse boleto já estava guardado e ficou de fora."); continue; }

            var lido = CobrancaAbastecimentoService.Ler(bytes, nome).Boleto;
            var avisosBoleto = lido?.Avisos ?? [$"{nome}: não reconheci como boleto; preencha os dados à mão."];
            avisos.AddRange(avisosBoleto);
            var b = new BoletoPagarAvulso
            {
                Descricao = (descricao ?? "").Trim(),
                Beneficiario = lido?.Posto ?? "",
                Numero = lido == null ? "" : lido.NumeroDocumento != "" ? lido.NumeroDocumento : lido.NossoNumero,
                Valor = lido?.Valor is double v ? Math.Round((decimal)v, 2) : null,
                Vencimento = lido?.Vencimento,
                DataChegada = hoje,
                LidoDoPdf = lido?.Lido ?? false,
                AvisosJson = JsonSerializer.Serialize(avisosBoleto, Json),
                NomeArquivo = nome,
                Pdf = bytes,
                Hash = hash,
                CriadoEm = agora,
                AtualizadoEm = agora,
            };
            db.BoletosPagarAvulsos.Add(b);
            novos.Add(b);
        }

        var aReceber = daTransportadora.Count > 0 ? await importacao.Importar(daTransportadora) : null;
        if (aReceber != null)
        {
            foreach (var i in aReceber.Itens)
            {
                if (i.Boleto != null && i.Situacao != "REPETIDO")
                    avisos.Add($"{i.Arquivo}: é boleto emitido pela TRX para {BoletoReceberLeitor.RotuloCliente(i.Boleto.Cliente)}; foi para A receber.");
                else if (i.Situacao == "REPETIDO")
                    avisos.Add($"{i.Arquivo}: boleto da TRX que já estava em A receber; ficou de fora.");
                avisos.AddRange(i.Avisos);
            }
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Erro ao guardar boletos avulsos a pagar");
            return Conflict(new { erro = "Não consegui guardar: o mesmo boleto pode ter sido subido ao mesmo tempo. Tente de novo." });
        }
        return Ok(new
        {
            Boletos = novos.Select(b => new { b.Id, b.Beneficiario, b.Numero, b.Valor, b.Vencimento, b.LidoDoPdf, b.NomeArquivo }),
            ParaReceber = aReceber?.Guardados.Count ?? 0,
            Avisos = avisos,
        });
    }

    public record AvulsoEdicao(string? Descricao, string? Beneficiario, string? Numero, decimal? Valor,
        string? Vencimento, string? DataChegada, string? PagoEm);

    // Boleto a pagar sem PDF (ou antes de o PDF chegar)
    [HttpPost("pagar/avulsos/manual")]
    public async Task<IActionResult> CriarAvulso([FromBody] AvulsoEdicao req)
    {
        var b = new BoletoPagarAvulso { DataChegada = Hoje(), CriadoEm = DateTime.UtcNow };
        var erro = AplicarAvulso(b, req);
        if (erro != null) return BadRequest(new { erro });
        db.BoletosPagarAvulsos.Add(b);
        await db.SaveChangesAsync();
        return Ok(new { b.Id });
    }

    [HttpPut("pagar/avulsos/{id:int}")]
    public async Task<IActionResult> EditarAvulso(int id, [FromBody] AvulsoEdicao req)
    {
        var b = await db.BoletosPagarAvulsos.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        var erro = AplicarAvulso(b, req);
        if (erro != null) return BadRequest(new { erro });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? AplicarAvulso(BoletoPagarAvulso b, AvulsoEdicao req)
    {
        if (!Data(req.Vencimento, out var venc)) return "Vencimento inválido.";
        if (!Data(req.PagoEm, out var pago)) return "Data do pagamento inválida.";
        if (!Data(req.DataChegada, out var chegada)) return "Data de chegada inválida.";
        if (req.Valor is < 0) return "O valor não pode ser negativo.";
        var descricao = (req.Descricao ?? "").Trim();
        var beneficiario = (req.Beneficiario ?? "").Trim();
        if (descricao == "" && beneficiario == "") return "Informe a descrição ou o beneficiário.";

        b.Descricao = descricao;
        b.Beneficiario = beneficiario;
        b.Numero = (req.Numero ?? "").Trim();
        b.Valor = req.Valor is decimal v ? Math.Round(v, 2) : null;
        b.Vencimento = venc;
        if (chegada != null) b.DataChegada = chegada.Value;
        b.PagoEm = pago;
        b.AtualizadoEm = DateTime.UtcNow;
        return null;
    }

    [HttpGet("pagar/avulsos/{id:int}/pdf")]
    public async Task<IActionResult> BaixarAvulso(int id)
    {
        var b = await db.BoletosPagarAvulsos.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.NomeArquivo, x.Pdf, x.Numero }).FirstOrDefaultAsync();
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (b.Pdf == null) return NotFound(new { erro = "Esse boleto não tem PDF guardado." });
        return File(b.Pdf, "application/pdf", NomeOuPadrao(b.NomeArquivo, $"BOLETO {b.Numero}.pdf".Trim()));
    }

    [HttpDelete("pagar/avulsos/{id:int}")]
    public async Task<IActionResult> ExcluirAvulso(int id)
    {
        var n = await db.BoletosPagarAvulsos.Where(b => b.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Boleto não encontrado." });
        return NoContent();
    }

    // ═════════════════════════ A receber ═════════════════════════

    // Monta o boleto da CD ou da Girassol com as regras do BOL-11, para mostrar (nada vai
    // ao banco nem ao Sicoob). Se o boleto da carga já foi feito, compara.
    [HttpGet("receber/montar")]
    public async Task<IActionResult> MontarAReceber([FromQuery] string? cliente, [FromQuery] DateOnly? data,
        [FromQuery] string? placa, [FromQuery] decimal? caixas, [FromServices] BoletoMontagemService montagem)
    {
        var c = (cliente ?? "").Trim().ToUpperInvariant();
        if (!BoletoReceberLeitor.Clientes.Contains(c)) return BadRequest(new { erro = "Escolha o cliente: CD ou Girassol." });
        if (data == null) return BadRequest(new { erro = "Informe a data da carga." });
        if (caixas is <= 0) return BadRequest(new { erro = "Informe as caixas de produtoD (mais que zero)." });
        return Ok(await montagem.Montar(c, data.Value, placa, c == BoletoReceberLeitor.Perfil ? null : caixas));
    }

    // Cargas dos últimos 30 dias ainda sem boleto, já montadas (lista "Para emitir"; nada vai ao Sicoob)
    [HttpGet("receber/para-emitir")]
    public async Task<IActionResult> ParaEmitirAReceber([FromQuery] string? cliente, [FromServices] BoletoMontagemService montagem)
    {
        var c = (cliente ?? "").Trim().ToUpperInvariant();
        if (!BoletoReceberLeitor.Clientes.Contains(c)) return BadRequest(new { erro = "Escolha o cliente: CD ou Girassol." });
        return Ok(await montagem.ParaEmitir(c, Hoje()));
    }

    // ── Cálculo do frete da Girassol e queda do volume (BOL-17) ──
    // Ver: quem tem a área Boletos (o controle de permissão, Acesso/PermissoesMiddleware, barra o resto). Lançar a queda: só administrador ativo, conferido no banco (PodeLancarQueda).

    public record QuedaTipo(string? Tipo, decimal? QuedaPorCaixa);
    public record QuedaPedido(DateOnly? Data, string? Placa, List<QuedaTipo>? Descontos);

    private const int MaxTiposQueda = 50;

    /// <summary>
    /// Administrador (Usuarios.Dono), conferido no banco a cada pedido: login do escritório (sem perfil), ativo
    /// e dono (Admin, 11/11/2026: só Admin e Bento, os dois administradores, lançam a queda).
    /// Nada pelo nome: o nome o próprio usuário edita no perfil.
    /// </summary>
    private async Task<bool> PodeLancarQueda()
    {
        if (!string.IsNullOrEmpty(User.FindFirst(AuthService.ClaimPerfil)?.Value)) return false;
        if (AuthController.UsuarioId(User) is not { } id) return false;
        return await db.Usuarios.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Ativo && x.Dono && (x.Perfil == null || x.Perfil == ""));
    }

    /// <summary>Dono conferido no banco a cada pedido (o token pode ser de antes de a pessoa deixar de ser).</summary>
    private async Task<bool> EhDonoNoBancoAsync() =>
        AuthController.UsuarioId(User) is { } id && await db.Usuarios.AsNoTracking().AnyAsync(u => u.Id == id && u.Dono && u.Ativo);

    private static object CalculoDto(FretePerfilService.CalculoCarga c, bool podeLancar)
    {
        var t = c.Totais;
        var p = c.Posicao;
        return new
        {
            data = c.Data,
            placa = c.Placa,
            arquivo = c.Arquivo,
            numeroBoleto = c.Relatorio?.NumeroBoleto,
            notas = (c.Relatorio?.Linhas ?? []).Select(n => new { nf = n.NF, n.Caixas, n.ProdutoE, n.Emitente }),
            linhas = c.Calculo.Linhas.Select(l => new
            {
                l.Tipo, l.ProdutoE, l.Caixas, l.FretePorCaixa, quedaPorCaixa = l.DescontoPorCaixa,
                l.Frete, queda = l.Desconto, podeTerQueda = !FretePerfilCalculo.SemQueda(l),
            }),
            totais = new { t.Caixas, t.FreteBruto, t.CaixasComDesconto, quedaTotal = t.DescontoTotal, t.Devido },
            saldoAnterior = p?.SaldoAnterior ?? 0m,
            saldoAbatido = p?.SaldoAbatido ?? 0m,
            valorBoleto = p == null ? (c.Arquivo == null ? (decimal?)null : t.Devido) : p.ValorBoleto,
            semBoleto = p?.SemBoleto ?? false,
            saldoALevar = p?.SaldoALevar ?? 0m,
            temBoleto = c.TemBoleto,
            comQueda = c.Descontos.Count > 0,
            houveQueda = c.HouveQueda,
            descontos = c.Descontos,
            emitido = c.Emitido,
            atualizadoEm = c.AtualizadoEm,
            atualizadoPor = c.AtualizadoPor,
            avisos = c.Avisos,
            podeLancar,
        };
    }

    // O cálculo da carga (.txt das notas + queda guardada + saldo da cadeia). Só leitura.
    [HttpGet("receber/frete-perfil")]
    public async Task<IActionResult> FretePerfil([FromQuery] string? data, [FromQuery] string? placa,
        [FromServices] FretePerfilService frete)
    {
        if (!Data(data, out var d) || d == null) return BadRequest(new { erro = "Informe a data da carga (aaaa-mm-dd)." });
        if (string.IsNullOrWhiteSpace(placa)) return BadRequest(new { erro = "Informe a placa da carga." });
        if (placa.Trim().Length > 10) return BadRequest(new { erro = "Placa inválida." });
        var c = await frete.ObterAsync(d.Value, placa);
        if (c.Arquivo == null)
            return NotFound(new { erro = c.Avisos.FirstOrDefault() ?? "Não há cálculo do frete da Girassol desta data e placa." });
        return Ok(CalculoDto(c, await PodeLancarQueda()));
    }

    // Grava a queda do volume por caixa de cada tipo normal da carga (descontos vazios = marca "não houve queda") e
    // devolve o cálculo novo. O cálculo das notas não se edita (Admin, 11/11/2026).
    [HttpPut("receber/frete-perfil")]
    public async Task<IActionResult> GravarFretePerfil([FromBody] QuedaPedido req, [FromServices] FretePerfilService frete)
    {
        if (!await PodeLancarQueda())
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "Só o Admin e o Bento lançam a queda do volume." });
        if (req.Data == null) return BadRequest(new { erro = "Informe a data da carga." });
        if (req.Data.Value.Year is < 2000 or > 2100) return BadRequest(new { erro = "Data da carga inválida." });
        if (string.IsNullOrWhiteSpace(req.Placa)) return BadRequest(new { erro = "Informe a placa da carga." });
        if (req.Placa.Trim().Length > 10) return BadRequest(new { erro = "Placa inválida." });
        var lista = req.Descontos ?? [];
        if (lista.Count > MaxTiposQueda) return BadRequest(new { erro = "Tipos de volume demais no pedido." });

        var descontos = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in lista)
        {
            var tipo = (q.Tipo ?? "").Trim();
            if (tipo.Length is 0 or > 100) return BadRequest(new { erro = "Tipo de volume inválido na queda." });
            var v = q.QuedaPorCaixa ?? 0m;
            if (v < 0 || v > FretePerfilService.QuedaMaxima || decimal.Round(v, 2) != v)
                return BadRequest(new { erro = $"Queda por caixa de \"{tipo}\" inválida: de R$ 0,00 a R$ 999,99, com até duas casas." });
            if (!descontos.TryAdd(tipo, v)) return BadRequest(new { erro = $"O tipo \"{tipo}\" veio repetido." });
        }

        var atual = await frete.ObterAsync(req.Data.Value, req.Placa);
        if (atual.Arquivo == null)
            return NotFound(new { erro = atual.Avisos.FirstOrDefault() ?? "Não há cálculo do frete da Girassol desta data e placa." });
        foreach (var tipo in descontos.Keys)
        {
            var linha = atual.Calculo.Linhas.FirstOrDefault(l => l.Tipo.Trim().Equals(tipo, StringComparison.OrdinalIgnoreCase));
            if (linha == null) return BadRequest(new { erro = $"O tipo \"{tipo}\" não está nas notas desta carga." });
            if (FretePerfilCalculo.SemQueda(linha)) return BadRequest(new { erro = $"\"{tipo}\" não leva queda (produtoE e produtoD nunca levam)." });
        }

        var usuario = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "";
        FretePerfilService.CalculoCarga c;
        try { c = await frete.SalvarAsync(req.Data.Value, req.Placa, descontos, usuario); }
        catch (ArgumentException ex) { return BadRequest(new { erro = ex.Message }); }
        catch (InvalidOperationException ex) { return NotFound(new { erro = ex.Message }); }
        logger.LogInformation("Queda do volume da Girassol {Data} {Placa} gravada: {Tipos} tipo(s) com queda",
            c.Data, c.Placa, c.Descontos.Count);
        return Ok(CalculoDto(c, true));
    }

    // ── Configuração da emissão (BOL-12): uma linha por cliente; sem ela valem os valores do BOL-11 ──
    public record ConfigEmissaoDto(string? Cliente, int DiasVencimento, string? PagadorNome, string? PagadorCnpj,
        string? PagadorEndereco, string? PagadorBairro, string? PagadorCidade, string? PagadorUf, string? PagadorCep,
        decimal PrecoCaixaProdutoD, decimal MultaPct, decimal JurosMes, string? Instrucao3, bool Pix);

    private static ConfigEmissaoDto Dto(BoletoMontador.Config c) => new(c.Cliente, c.DiasVencimento,
        c.Pagador.Nome, c.Pagador.Cnpj, c.Pagador.Endereco, c.Pagador.Bairro, c.Pagador.Cidade, c.Pagador.Uf, c.Pagador.Cep,
        c.PrecoCaixaProdutoD, c.MultaPct, c.JurosMes, c.Instrucao3, c.Pix);

    [HttpGet("receber/config-emissao")]
    public async Task<IActionResult> ConfigEmissao([FromServices] BoletoMontagemService montagem)
    {
        var lista = new List<object>();
        foreach (var c in new[] { BoletoReceberLeitor.Perfil, BoletoReceberLeitor.Cd })
        {
            var (regras, aviso) = await montagem.Regras(c);
            lista.Add(new { config = Dto(regras), aviso });
        }
        return Ok(new { clientes = lista, podeSalvar = await EhDonoNoBancoAsync() });
    }

    /// <summary>Grava a configuração de um cliente (só o dono). Valida tudo antes; nada vai ao Sicoob.</summary>
    [HttpPut("receber/config-emissao")]
    public async Task<IActionResult> GravarConfigEmissao([FromBody] ConfigEmissaoDto req, [FromServices] BoletoMontagemService montagem)
    {
        if (!await EhDonoNoBancoAsync())
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "Só o dono pode mudar a configuração da emissão." });
        var c = (req.Cliente ?? "").Trim().ToUpperInvariant();
        if (!BoletoReceberLeitor.Clientes.Contains(c)) return BadRequest(new { erro = "Escolha o cliente: CD ou Girassol." });
        static string T(string? s) => (s ?? "").Trim();
        var cfg = new BoletoMontador.Config(c, req.DiasVencimento,
            new(T(req.PagadorNome), T(req.PagadorCnpj), T(req.PagadorEndereco), T(req.PagadorBairro), T(req.PagadorCidade),
                T(req.PagadorUf).ToUpperInvariant(), T(req.PagadorCep)),
            Math.Round(req.PrecoCaixaProdutoD, 2), Math.Round(req.MultaPct, 2), Math.Round(req.JurosMes, 2), T(req.Instrucao3), req.Pix);
        var erros = BoletoMontador.Validar(cfg);
        if (erros.Count > 0) return BadRequest(new { erro = string.Join(" ", erros) });

        var reg = await db.ConfigEmissaoBoleto.FirstOrDefaultAsync(x => x.Cliente == c);
        if (reg == null) db.ConfigEmissaoBoleto.Add(reg = new ConfigEmissaoBoleto { Cliente = c });
        reg.DiasVencimento = cfg.DiasVencimento;
        reg.PagadorNome = cfg.Pagador.Nome;
        reg.PagadorCnpj = cfg.Pagador.Cnpj;
        reg.PagadorEndereco = cfg.Pagador.Endereco ?? "";
        reg.PagadorBairro = cfg.Pagador.Bairro ?? "";
        reg.PagadorCidade = cfg.Pagador.Cidade ?? "";
        reg.PagadorUf = cfg.Pagador.Uf ?? "";
        reg.PagadorCep = cfg.Pagador.Cep ?? "";
        reg.PrecoCaixaProdutoD = cfg.PrecoCaixaProdutoD;
        reg.MultaPct = cfg.MultaPct;
        reg.JurosMes = cfg.JurosMes;
        reg.Instrucao3 = cfg.Instrucao3;
        reg.Pix = cfg.Pix;
        reg.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Configuração da emissão de boleto {Cliente} alterada", c);
        return await ConfigEmissao(montagem);
    }

    public record EmitirPedido(string? Cliente, DateOnly? Data, string? Placa, decimal? Valor, DateOnly? Vencimento, bool Confirmado,
        decimal? Caixas = null);

    // Emite no Sicoob o boleto montado (cobrança REAL, BOL-10). A tela confirma a cada boleto e manda
    // o valor e o vencimento que o Admin viu; se a montagem mudou, ou a carga já tem boleto, não emite.
    [HttpPost("receber/emitir")]
    public async Task<IActionResult> EmitirAReceber([FromBody] EmitirPedido req, [FromServices] BoletoEmissaoService emissao,
        CancellationToken ct)
    {
        var c = (req.Cliente ?? "").Trim().ToUpperInvariant();
        if (!BoletoReceberLeitor.Clientes.Contains(c)) return BadRequest(new { erro = "Escolha o cliente: CD ou Girassol." });
        if (req.Data == null || req.Valor == null || req.Vencimento == null)
            return BadRequest(new { erro = "Informe a data da carga, o valor e o vencimento conferidos." });
        if (!req.Confirmado) return BadRequest(new { erro = "Confirme a emissão do boleto." });
        if (req.Caixas is <= 0) return BadRequest(new { erro = "Informe as caixas de produtoD (mais que zero)." });
        var r = await emissao.Emitir(new(c, req.Data.Value, req.Placa, req.Valor.Value, req.Vencimento.Value,
            c == BoletoReceberLeitor.Perfil ? null : req.Caixas,
            User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value), ct);
        if (r.Erro != null)
            return r.Incerto ? StatusCode(StatusCodes.Status502BadGateway, new { erro = r.Erro, r.Id, incerto = true })
                             : UnprocessableEntity(new { erro = r.Erro });
        return Ok(r);
    }

    [HttpGet("receber")]
    public async Task<IActionResult> ListarAReceber()
    {
        var hoje = Hoje();
        var lista = await db.BoletosReceber.AsNoTracking()
            .OrderByDescending(b => b.DataCarga).ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.Id, b.Cliente, b.Sequencia, b.Numero, b.NossoNumero, b.LinhaDigitavel, b.Pagador, b.Placa, b.Valor, b.Vencimento,
                b.Emissao, b.DataCarga, b.RecebidoEm, b.Observacao, b.LidoDoPdf, b.AvisosJson, b.NomeArquivo,
                TemPdf = b.Pdf != null, b.CriadoEm,
            })
            .ToListAsync();

        var clientes = BoletoReceberLeitor.Clientes.Select(c =>
        {
            var doCliente = lista.Where(b => b.Cliente == c).ToList();
            var abertos = doCliente.Where(b => b.RecebidoEm == null).ToList();
            var vencidos = abertos.Where(b => b.Vencimento < hoje).ToList();
            return new
            {
                Chave = c,
                Rotulo = BoletoReceberLeitor.RotuloCliente(c),
                Quantidade = doCliente.Count,
                TotalEmitido = doCliente.Sum(b => b.Valor ?? 0),
                TotalRecebido = doCliente.Where(b => b.RecebidoEm != null).Sum(b => b.Valor ?? 0),
                Abertos = abertos.Count,
                TotalAReceber = abertos.Sum(b => b.Valor ?? 0),
                Vencidos = vencidos.Count,
                TotalVencido = vencidos.Sum(b => b.Valor ?? 0),
            };
        });

        return Ok(new
        {
            Hoje = hoje,
            Clientes = clientes,
            Boletos = lista.Select(b => new
            {
                b.Id, b.Cliente, b.Sequencia, b.Numero, b.NossoNumero, b.LinhaDigitavel, b.Pagador, b.Placa, b.Valor, b.Vencimento,
                b.Emissao, b.DataCarga, b.RecebidoEm, b.Observacao, b.LidoDoPdf, Avisos = Lista(b.AvisosJson),
                b.NomeArquivo, b.TemPdf, b.CriadoEm,
            }),
        });
    }

    // Vários PDFs de uma vez. O cliente vem do pagador impresso no boleto; se o
    // PDF não disser, vale o da aba. O mesmo PDF subido de novo fica de fora;
    // boleto de mesmo nosso número já lançado sem PDF (vindo da planilha de
    // controle) ganha o PDF e os dados lidos.
    // soDaTransportadora (tela E-mails aos clientes, onde se arrasta qualquer PDF):
    // só entra o boleto emitido pela transportadora. Boleto e romaneio da fazenda
    // ficam de fora com aviso: eles têm o caminho próprio no Acerto Alfa (ACE-07),
    // e a fazenda pode ter o mesmo pagador da CD (o pagador sozinho não separa).
    [HttpPost("receber/importar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarAReceber(IFormFileCollection arquivos, [FromQuery] string? cliente,
        [FromServices] BoletoReceberImportacao importacao, [FromQuery] bool soDaTransportadora = false,
        [FromQuery] bool substituir = false)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie os PDFs dos boletos." });
        var clienteAba = (cliente ?? "").Trim().ToUpperInvariant();
        if (clienteAba != "" && !BoletoReceberLeitor.Clientes.Contains(clienteAba))
            return BadRequest(new { erro = "Cliente desconhecido." });

        var lidos = await LerArquivos(arquivos);
        var deFora = new List<(string Nome, string Aviso)>();
        if (soDaTransportadora)
        {
            var dela = new List<(string Nome, byte[] Bytes)>();
            foreach (var a in lidos)
            {
                if (!a.Nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    deFora.Add((a.Nome, $"{a.Nome}: não é PDF (ficou de fora)."));
                else if (!BoletoReceberLeitor.EmitidoPelaTransportadora(a.Bytes))
                    deFora.Add((a.Nome, $"{a.Nome}: não é boleto emitido pela TRX. Boleto e romaneio da fazenda entram pelo Acerto Alfa (ficou de fora)."));
                else dela.Add(a);
            }
            lidos = dela;
        }

        var r = await importacao.Importar(lidos, clienteAba, substituir);

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Erro ao guardar boletos a receber");
            return Conflict(new { erro = "Não consegui guardar: o mesmo boleto pode ter sido subido ao mesmo tempo. Tente de novo." });
        }

        return Ok(new
        {
            r.Novos,
            r.Completados,
            r.Repetidos,
            r.Substituidos,
            Boletos = r.Guardados.Select(b => new
            {
                b.Id, b.Cliente, b.Sequencia, b.NossoNumero, b.Placa, b.Valor, b.Vencimento, b.DataCarga, b.NomeArquivo, b.LidoDoPdf,
            }),
            Avisos = r.Avisos.Concat(deFora.Select(f => f.Aviso)).ToList(),
            // Um por arquivo, na ordem: o que foi lido e onde ficou
            Itens = r.Itens.Select(i => new
            {
                i.Arquivo, i.Situacao, i.Pagador,
                Cliente = i.Boleto?.Cliente, Valor = i.Boleto?.Valor, Vencimento = i.Boleto?.Vencimento,
                DataCarga = i.Boleto?.DataCarga, Placa = i.Boleto?.Placa, NossoNumero = i.Boleto?.NossoNumero,
                Numero = i.Boleto?.Numero, Emissao = i.Boleto?.Emissao,
                i.Avisos,
                // Repetido: o boleto do PDF subido, para comparar com o guardado (acima)
                Novo = i.Novo == null ? null : (object)new
                {
                    i.Novo.Numero, i.Novo.NossoNumero, i.Novo.Valor, i.Novo.Emissao, i.Novo.Vencimento, i.Novo.DataCarga,
                },
            }).Concat(deFora.Select(f => new
            {
                Arquivo = f.Nome, Situacao = "RECUSADO", Pagador = (string?)null,
                Cliente = (string?)null, Valor = (decimal?)null, Vencimento = (DateOnly?)null,
                DataCarga = (DateOnly?)null, Placa = (string?)null, NossoNumero = (string?)null,
                Numero = (string?)null, Emissao = (DateOnly?)null,
                Avisos = new List<string> { f.Aviso },
                Novo = (object?)null,
            })),
        });
    }

    public record ReceberEdicao(string? Cliente, int? Sequencia, string? Numero, string? NossoNumero, string? Pagador,
        string? Placa, decimal? Valor, string? Vencimento, string? Emissao, string? DataCarga, string? RecebidoEm, string? Observacao);

    // Boleto lançado à mão (o que ainda não tem PDF, ou o que veio da planilha)
    [HttpPost("receber/manual")]
    public async Task<IActionResult> CriarAReceber([FromBody] ReceberEdicao req)
    {
        var b = new BoletoReceber { CriadoEm = DateTime.UtcNow };
        var erro = AplicarReceber(b, req);
        if (erro != null) return BadRequest(new { erro });
        db.BoletosReceber.Add(b);
        await db.SaveChangesAsync();
        return Ok(new { b.Id });
    }

    [HttpPut("receber/{id:int}")]
    public async Task<IActionResult> EditarAReceber(int id, [FromBody] ReceberEdicao req)
    {
        var b = await db.BoletosReceber.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        var erro = AplicarReceber(b, req);
        if (erro != null) return BadRequest(new { erro });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static string? AplicarReceber(BoletoReceber b, ReceberEdicao req)
    {
        var cliente = (req.Cliente ?? "").Trim().ToUpperInvariant();
        if (!BoletoReceberLeitor.Clientes.Contains(cliente)) return "Escolha o cliente.";
        if (!Data(req.Vencimento, out var venc)) return "Vencimento inválido.";
        if (!Data(req.Emissao, out var emissao)) return "Data de emissão inválida.";
        if (!Data(req.DataCarga, out var carga)) return "Data da carga inválida.";
        if (!Data(req.RecebidoEm, out var recebido)) return "Data do recebimento inválida.";
        if (req.Valor is < 0) return "O valor não pode ser negativo.";
        if (carga == null) return "Informe a data da carga.";

        b.Cliente = cliente;
        b.Sequencia = req.Sequencia;
        b.Numero = (req.Numero ?? "").Trim();
        b.NossoNumero = (req.NossoNumero ?? "").Trim();
        b.Pagador = (req.Pagador ?? "").Trim();
        b.Placa = EmailClienteService.Placa(req.Placa);
        b.Valor = req.Valor is decimal v ? Math.Round(v, 2) : null;
        b.Vencimento = venc;
        b.Emissao = emissao;
        b.DataCarga = carga;
        b.RecebidoEm = recebido;
        b.Observacao = (req.Observacao ?? "").Trim();
        b.AtualizadoEm = DateTime.UtcNow;
        return null;
    }

    public record RecebidoPedido(string? RecebidoEm);

    [HttpPut("receber/{id:int}/recebido")]
    public async Task<IActionResult> MarcarRecebido(int id, [FromBody] RecebidoPedido req)
    {
        if (!Data(req.RecebidoEm, out var recebido)) return BadRequest(new { erro = "Data do recebimento inválida." });
        var n = await db.BoletosReceber.Where(b => b.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.RecebidoEm, recebido).SetProperty(b => b.AtualizadoEm, DateTime.UtcNow));
        if (n == 0) return NotFound(new { erro = "Boleto não encontrado." });
        return NoContent();
    }

    [HttpGet("receber/{id:int}/pdf")]
    public async Task<IActionResult> BaixarAReceber(int id)
    {
        var b = await db.BoletosReceber.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.NomeArquivo, x.Pdf, x.Cliente, x.NossoNumero }).FirstOrDefaultAsync();
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (b.Pdf == null) return NotFound(new { erro = "Esse boleto não tem PDF guardado." });
        return File(b.Pdf, "application/pdf", NomeOuPadrao(b.NomeArquivo, $"BOLETO {b.Cliente} {b.NossoNumero}.pdf"));
    }

    [HttpDelete("receber/{id:int}")]
    public async Task<IActionResult> ExcluirAReceber(int id)
    {
        var n = await db.BoletosReceber.Where(b => b.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Boleto não encontrado." });
        return NoContent();
    }
}
