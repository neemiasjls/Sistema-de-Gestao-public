using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Extrato da conta PJ no Sicoob, lido pela API do banco (só consulta). Cada
/// entrada de dinheiro vem com os boletos a receber em aberto de mesmo valor, para
/// a tela oferecer "Marcar recebido" (a marcação é a rota de sempre de Boletos).
/// </summary>
[Authorize]
[ApiController]
[Route("api/sicoob")]
public class SicoobController(AppDbContext db, SicoobService sicoob) : ControllerBase
{
    // O dia no Brasil: o servidor roda em UTC e à noite já seria amanhã
    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    private static DateOnly? Data(string? s) =>
        DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [HttpGet("extrato")]
    public async Task<IActionResult> Extrato([FromQuery] string? inicio, [FromQuery] string? fim,
        [FromServices] BoletoMontagemService montagem, CancellationToken ct)
    {
        var hoje = Hoje();
        var ate = Data(fim) ?? hoje;
        if (ate > hoje) ate = hoje;
        var de = Data(inicio) ?? new DateOnly(ate.Year, ate.Month, 1);
        if (de > ate) return BadRequest(new { erro = "A data inicial está depois da final." });
        if (ate.DayNumber - de.DayNumber > 92) return BadRequest(new { erro = "Escolha um período de até 3 meses." });

        var falta = sicoob.FaltaConfigurar();
        if (falta != null) return Ok(new { Configurado = false, Erro = falta, Inicio = de, Fim = ate });

        var (extrato, erro) = await sicoob.ExtratoAsync(de, ate, ct);
        if (extrato == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro });

        // Entrada só vira boleto pelo PAGADOR (Pix com CNPJ ou nome do cliente, PixBoletos). Casar só pelo valor
        // saiu (Admin, 10/11/2026: Pix da Avícola Gotty de 1.080,00 aparecia como "pode ser" boleto da CD)
        var pix = await PixDoExtrato(extrato.Lancamentos, de, montagem, ct);

        var lancamentos = extrato.Lancamentos.Select(l =>
        {
            var achado = pix.GetValueOrDefault(l.Id);
            return new
            {
                l.Id, l.Data, l.Credito, l.Valor, l.Descricao, l.Documento, l.CpfCnpj, l.Complemento,
                Pix = achado == null ? null : new
                {
                    Cliente = achado.Pagador?.Codigo, Pagador = achado.Pagador?.Nome, achado.SemCnpj, achado.Certo, achado.Duvida,
                    achado.MaisOpcoes,
                    Opcoes = achado.Opcoes.Select(o => o.Select(b => pixBoleto[b.Id])),
                    JaRecebidos = achado.JaRecebidos.Select(b => pixBoleto[b.Id]),
                },
            };
        }).ToList();

        return Ok(new
        {
            Configurado = true,
            Inicio = de,
            Fim = ate,
            Hoje = hoje,
            extrato.Saldo,
            extrato.SaldoLimite,
            extrato.SaldoBloqueado,
            Entradas = extrato.Lancamentos.Where(l => l.Credito).Sum(l => l.Valor),
            Saidas = extrato.Lancamentos.Where(l => !l.Credito).Sum(l => l.Valor),
            Lancamentos = lancamentos,
        });
    }

    // Conferência dos boletos a receber (CD e Girassol) pela SOMA dos créditos "CRÉD.LIQUIDAÇÃO COBRANÇA"
    // (o Sicoob credita um valor por dia com todos os boletos pagos). Só lê: a baixa é a rota de sempre.
    [HttpGet("conferir-boletos")]
    public async Task<IActionResult> ConferirBoletos([FromQuery] string? inicio, [FromQuery] string? fim, CancellationToken ct)
    {
        var hoje = Hoje();
        var ate = Data(fim) ?? hoje;
        if (ate > hoje) ate = hoje;
        var de = Data(inicio) ?? ate.AddDays(-60);
        if (de > ate) return BadRequest(new { erro = "A data inicial está depois da final." });
        if (ate.DayNumber - de.DayNumber > 92) return BadRequest(new { erro = "Escolha um período de até 3 meses." });

        var falta = sicoob.FaltaConfigurar();
        if (falta != null) return Ok(new { Configurado = false, Erro = falta, Inicio = de, Fim = ate });
        var (extrato, erro) = await sicoob.ExtratoAsync(de, ate, ct);
        if (extrato == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro });

        var creditos = extrato.Lancamentos
            .Where(l => l.Credito && l.Data != null && ConferenciaCobranca.EhLiquidacaoCobranca(l.Descricao))
            .Select(l => new ConferenciaCobranca.Credito(l.Id, l.Data!.Value, l.Valor)).ToList();
        var boletos = (await db.BoletosReceber.AsNoTracking()
                .Where(b => b.Valor != null && b.Vencimento != null && b.Vencimento >= de.AddDays(-15))
                .Select(b => new { b.Id, b.Cliente, b.NossoNumero, b.Valor, b.Vencimento, b.RecebidoEm, b.Placa, b.DataCarga })
                .ToListAsync(ct));
        var porId = boletos.ToDictionary(b => b.Id);
        var r = ConferenciaCobranca.Conferir(creditos,
            boletos.Select(b => new ConferenciaCobranca.Boleto(b.Id, b.Cliente, b.NossoNumero, b.Valor!.Value, b.Vencimento, b.RecebidoEm)).ToList(), hoje);

        object Item(ConferenciaCobranca.Boleto b) => new
        {
            b.Id, b.Cliente, Rotulo = BoletoReceberLeitor.RotuloCliente(b.Cliente), b.NossoNumero, b.Valor, b.Vencimento,
            b.RecebidoEm, porId[b.Id].Placa, porId[b.Id].DataCarga,
        };
        return Ok(new
        {
            Configurado = true, Inicio = de, Fim = ate, Hoje = hoje,
            PagosSemBaixa = r.PagosSemBaixa.Select(p => new { Boleto = Item(p.Boleto), DataCredito = p.Credito.Data, ValorCredito = p.Credito.Valor }),
            VencidosSemCredito = r.VencidosSemCredito.Select(Item),
            Creditos = r.Creditos.OrderByDescending(c => c.Credito.Data).Select(c => new
            {
                c.Credito.Data, c.Credito.Valor, c.Achou, c.Duvida,
                Boletos = c.Boletos.Select(Item),
                Alternativas = c.Alternativas.Select(a => a.Select(Item)),
            }),
        });
    }

    // Boletos a receber vistos pelo Pix (em aberto, e os marcados no período para "já recebido"), por id, para a tela
    private Dictionary<int, object> pixBoleto = [];

    /// <summary>Pix do extrato × boletos em aberto do cliente pagador (BOL-14, PixBoletos). Só lê.</summary>
    private async Task<Dictionary<string, PixBoletos.Achado>> PixDoExtrato(IReadOnlyList<SicoobService.Lancamento> lancamentos,
        DateOnly de, BoletoMontagemService montagem, CancellationToken ct)
    {
        var pixes = lancamentos.Where(l => l.Credito && l.Data != null && PixBoletos.EhPix(l.Descricao, l.Complemento))
            .Select(l => new PixBoletos.Pix(l.Id, l.Data!.Value, l.Valor, l.Descricao, l.Complemento, l.CpfCnpj)).ToList();
        if (pixes.Count == 0) return [];
        var clientes = new List<PixBoletos.Cliente>();
        foreach (var c in new[] { BoletoReceberLeitor.Perfil, BoletoReceberLeitor.Cd })
        {
            var (regras, _) = await montagem.Regras(c);
            clientes.Add(new(c, regras.Pagador.Nome, regras.Pagador.Cnpj));
        }
        var lidos = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Valor != null && b.Valor > 0 && (b.RecebidoEm == null || b.RecebidoEm >= de))
            .Select(b => new { b.Id, b.Cliente, b.NossoNumero, b.Valor, b.Vencimento, b.RecebidoEm, b.Placa, b.DataCarga })
            .ToListAsync(ct);
        pixBoleto = lidos.ToDictionary(b => b.Id, b => (object)new
        {
            b.Id, b.Cliente, Rotulo = BoletoReceberLeitor.RotuloCliente(b.Cliente), b.NossoNumero, b.Valor, b.Vencimento,
            b.RecebidoEm, b.Placa, b.DataCarga,
        });
        var achados = PixBoletos.Achar(pixes,
            lidos.Select(b => new ConferenciaCobranca.Boleto(b.Id, b.Cliente, b.NossoNumero, b.Valor!.Value, b.Vencimento, b.RecebidoEm)).ToList(),
            clientes);
        return achados.GroupBy(a => a.Pix.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public record BaixaPixPedido(List<int>? Boletos, string? DataPix, decimal? ValorPix, bool Confirmado);

    /// <summary>
    /// Boletos pagos por Pix (BOL-14): para cada boleto, consulta no Sicoob; já liquidado/baixado = só marca
    /// recebido; senão dá a BAIXA no banco (ação real, só com confirmado) e, se baixou, marca recebido na data do
    /// Pix e anota na observação. Recusado ou sem resposta = não marca. Resultado boleto por boleto.
    /// </summary>
    // Boleto emitido errado: baixa no Sicoob e tira do sistema (Admin, 10/11/2026, caso 669-7).
    // Só o dono, com confirmação. Liquidado (pago) não baixa nem exclui.
    public record CancelarBoletoPedido(bool Confirmado);

    [HttpPost("boletos/{id:int}/baixar-e-excluir")]
    public async Task<IActionResult> BaixarEExcluir(int id, [FromBody] CancelarBoletoPedido req, CancellationToken ct)
    {
        if (User.FindFirst(AuthService.ClaimDono)?.Value != "true")
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "Só o Admin pode dar baixa de boleto no banco." });
        if (!req.Confirmado) return BadRequest(new { erro = "Confirme a baixa e a exclusão do boleto." });
        if (sicoob.FaltaConfigurar() is { } falta) return BadRequest(new { erro = falta });
        var b = await db.BoletosReceber.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (b.RecebidoEm != null) return BadRequest(new { erro = "Esse boleto está marcado como recebido: não excluo." });
        if (string.IsNullOrWhiteSpace(b.NossoNumero)) return BadRequest(new { erro = "Esse boleto não tem nosso número." });

        var (sit, erroConsulta) = await sicoob.ConsultarBoletoAsync(b.NossoNumero, b.Valor, ct);
        if (sit == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro = $"Não consegui consultar no Sicoob: {erroConsulta} Nada foi feito." });
        if (sit.Liquidado) return BadRequest(new { erro = "Esse boleto já foi PAGO no Sicoob: não baixei nem excluí." });
        string noBanco;
        if ((sit.Situacao ?? "").Contains("baix", StringComparison.OrdinalIgnoreCase)) noBanco = "já estava baixado no Sicoob";
        else
        {
            var (baixou, erroBaixa, incerto) = await sicoob.BaixarBoletoAsync(b.NossoNumero, ct);
            if (!baixou)
                return StatusCode(StatusCodes.Status502BadGateway, new { erro = (erroBaixa ?? "O Sicoob não baixou.") + (incerto ? " Confira no Sicoobnet." : "") + " Não excluí do sistema." });
            noBanco = "baixado no Sicoob";
        }
        db.BoletosReceber.Remove(b);
        await db.SaveChangesAsync(ct);
        return Ok(new { b.NossoNumero, mensagem = $"Boleto {b.NossoNumero} {noBanco} e excluído do sistema." });
    }

    [HttpPost("pix/baixar")]
    public async Task<IActionResult> BaixarPix([FromBody] BaixaPixPedido req, CancellationToken ct)
    {
        // Baixa no banco é ação real: só o dono (Admin), sempre com a confirmação dele (BOL-14)
        if (User.FindFirst(AuthService.ClaimDono)?.Value != "true")
            return StatusCode(StatusCodes.Status403Forbidden, new { erro = "Só o Admin pode dar baixa de boleto no banco." });
        if (!req.Confirmado) return BadRequest(new { erro = "Confirme a baixa dos boletos no banco." });
        var dataPix = Data(req.DataPix);
        if (dataPix == null) return BadRequest(new { erro = "Informe a data do Pix." });
        if (req.ValorPix is not > 0) return BadRequest(new { erro = "Informe o valor do Pix." });
        var ids = (req.Boletos ?? []).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { erro = "Escolha os boletos pagos pelo Pix." });
        if (ids.Count > 20) return BadRequest(new { erro = "Escolha no máximo 20 boletos por Pix." });
        if (sicoob.FaltaConfigurar() is { } falta) return BadRequest(new { erro = falta });

        var boletos = await db.BoletosReceber.Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        if (boletos.Count != ids.Count) return NotFound(new { erro = "Algum dos boletos não foi encontrado. Atualize a tela." });
        if (boletos.Select(b => b.Cliente).Distinct().Count() > 1) return BadRequest(new { erro = "Um Pix paga boletos de um cliente só." });
        if (boletos.Any(b => string.IsNullOrWhiteSpace(b.NossoNumero))) return BadRequest(new { erro = "Algum dos boletos não tem nosso número." });
        if (boletos.Sum(b => b.Valor ?? 0) != req.ValorPix)
            return BadRequest(new { erro = "A soma dos boletos não bate com o valor do Pix. Atualize a tela." });

        var hoje = Hoje();
        var resultado = new List<object>();
        foreach (var b in boletos.OrderBy(b => b.Vencimento))
        {
            object Item(string situacao, string mensagem) => new { b.Id, b.NossoNumero, b.Valor, b.Vencimento, Situacao = situacao, Mensagem = mensagem };
            if (b.RecebidoEm != null)
            {
                resultado.Add(Item("ja-recebido", $"Já estava marcado como recebido em {b.RecebidoEm:dd/MM/yyyy}. Nada feito no banco."));
                continue;
            }
            var (sit, erroConsulta) = await sicoob.ConsultarBoletoAsync(b.NossoNumero, b.Valor, ct);
            if (sit == null)
            {
                resultado.Add(Item("erro", $"Não consegui consultar no Sicoob antes de baixar: {erroConsulta} Nada foi feito neste boleto."));
                continue;
            }
            string anotacao, situacao, mensagem;
            if (sit.Liquidado || (sit.Situacao ?? "").Contains("baix", StringComparison.OrdinalIgnoreCase))
            {
                var como = sit.Liquidado ? "liquidado" : "baixado";
                anotacao = $"Pago por Pix em {dataPix:dd/MM}; já estava {como} no Sicoob";
                situacao = "ja-no-banco";
                mensagem = $"Já estava {como} no Sicoob: não baixei de novo, só marquei recebido em {dataPix:dd/MM/yyyy}.";
            }
            else
            {
                var (baixou, erroBaixa, incerto) = await sicoob.BaixarBoletoAsync(b.NossoNumero, ct);
                if (!baixou)
                {
                    resultado.Add(Item(incerto ? "incerto" : "recusado", (erroBaixa ?? "O Sicoob não baixou.") + " Não marquei como recebido."));
                    continue;
                }
                anotacao = $"Pago por Pix em {dataPix:dd/MM}; baixado no Sicoob em {hoje:dd/MM}";
                situacao = "baixado";
                mensagem = $"Baixado no Sicoob e marcado recebido em {dataPix:dd/MM/yyyy}.";
            }
            b.RecebidoEm = dataPix;
            b.Observacao = string.IsNullOrWhiteSpace(b.Observacao) ? anotacao : $"{b.Observacao.Trim()} · {anotacao}";
            b.AtualizadoEm = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            resultado.Add(Item(situacao, mensagem));
        }
        return Ok(new { Resultados = resultado });
    }

    public record PeriodoPedido(string? Inicio, string? Fim);

    /// <summary>
    /// Boletos dos postos pagos pelo extrato (ABA-07): o que casou exato vira pago com a data do débito; o resto
    /// volta como dúvida. Usa o extrato guardado do período (a tela chama logo depois de ler o extrato).
    /// Nada é pago no banco: só marca o que já foi pago.
    /// </summary>
    [HttpPost("postos/conferir")]
    public async Task<IActionResult> ConferirPostos([FromBody] PeriodoPedido req, [FromServices] PostosPagosExtratoService postos,
        CancellationToken ct)
    {
        var hoje = Hoje();
        var ate = Data(req.Fim) ?? hoje;
        if (ate > hoje) ate = hoje;
        var de = Data(req.Inicio) ?? new DateOnly(ate.Year, ate.Month, 1);
        if (de > ate) return BadRequest(new { erro = "A data inicial está depois da final." });
        if (ate.DayNumber - de.DayNumber > 92) return BadRequest(new { erro = "Escolha um período de até 3 meses." });
        if (sicoob.FaltaConfigurar() is { } falta) return BadRequest(new { erro = falta });
        var (extrato, erro) = await sicoob.ExtratoAsync(de, ate, ct);
        if (extrato == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro });
        var r = await postos.ConferirPostosAsync(extrato.Lancamentos, ct);
        return Ok(new { Inicio = de, Fim = ate, r.Marcados, r.Duvidas });
    }

    public record EscolhaPostoPedido(int BoletoId, string? Chave, string? Data);

    // Dúvida resolvida por ele: este débito pagou este boleto
    [HttpPost("postos/marcar")]
    public async Task<IActionResult> MarcarPosto([FromBody] EscolhaPostoPedido req, [FromServices] PostosPagosExtratoService postos,
        CancellationToken ct)
    {
        var data = Data(req.Data);
        if (data == null || string.IsNullOrWhiteSpace(req.Chave)) return BadRequest(new { erro = "Informe o débito do extrato." });
        var erro = await postos.MarcarAsync(req.BoletoId, req.Chave.Trim(), data.Value, ct);
        return erro == null ? NoContent() : BadRequest(new { erro });
    }

    /// <summary>
    /// "Conferir com o banco" da tela: boletos a receber liquidados viram recebidos, postos pagos pelo extrato e
    /// Pix de boleto só identificado (ConferenciaAutomaticaService). Nada é baixado, pago ou transferido no banco.
    /// </summary>
    [HttpPost("conferir-tudo")]
    public async Task<IActionResult> ConferirTudo([FromServices] ConferenciaAutomaticaService conferencia, CancellationToken ct)
    {
        var r = await conferencia.ConferirTudoAsync(ct);
        if (r == null) return Conflict(new { erro = "Já tem uma conferência com o banco rodando. Tente de novo em um minuto." });
        return Ok(r);
    }

    /// <summary>
    /// A mesma conferência, chamada de hora em hora pelo pg_cron do Supabase (docs/planos/cron-conferir.sql).
    /// Sem login: exige o cabeçalho X-Chave-Agendada igual ao segredo "conferir_agendado" do Vault do Supabase
    /// (lido pelo banco a cada chamada; não fica no código nem em variável). Errado ou sem segredo = 401 seco.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("conferir-agendado")]
    public async Task<IActionResult> ConferirAgendado([FromServices] ConferenciaAutomaticaService conferencia,
        [FromServices] ILogger<SicoobController> log)
    {
        var enviada = Request.Headers["X-Chave-Agendada"].ToString();
        if (string.IsNullOrEmpty(enviada)) return Unauthorized();
        string? segredo;
        try
        {
            segredo = (await db.Database
                .SqlQueryRaw<string>("select decrypted_secret as \"Value\" from vault.decrypted_secrets where name = 'conferir_agendado'")
                .ToListAsync()).FirstOrDefault();
        }
        catch (Exception ex)
        {
            log.LogWarning("Conferência agendada: não consegui ler o segredo do Vault ({Tipo})", ex.GetType().Name);
            return Unauthorized();
        }
        if (string.IsNullOrEmpty(segredo)) return Unauthorized();
        // Compara o hash dos dois em tempo constante (tamanho igual, nada vaza pelo tempo)
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(enviada)), SHA256.HashData(Encoding.UTF8.GetBytes(segredo))))
            return Unauthorized();

        // Sem o token da requisição: o pg_net pode desistir de esperar e a conferência termina mesmo assim
        var r = await conferencia.ConferirTudoAsync(TimeSpan.Zero, CancellationToken.None);
        if (r == null)
        {
            log.LogInformation("Conferência agendada: outra ainda rodando, pulei esta");
            return Ok(new { emAndamento = true });
        }
        log.LogInformation("Conferência agendada: {Resumo}", r.Texto);
        return Ok(new { r.Em, Texto = r.Texto });
    }

    // Situação de UM boleto na Cobrança do Sicoob (só consulta): pega o pago com juros ou desconto,
    // que não fecha na soma do crédito do dia. Um por vez, para o banco não pedir tempo (429).
    [HttpGet("boletos/{id:int}/situacao")]
    public async Task<IActionResult> SituacaoBoleto(int id, CancellationToken ct)
    {
        var b = await db.BoletosReceber.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.NossoNumero, x.Valor, x.RecebidoEm }).FirstOrDefaultAsync(ct);
        if (b == null) return NotFound(new { erro = "Boleto não encontrado." });
        if (string.IsNullOrWhiteSpace(b.NossoNumero)) return BadRequest(new { erro = "Este boleto não tem nosso número." });
        var (sit, erro) = await sicoob.ConsultarBoletoAsync(b.NossoNumero, b.Valor, ct);
        if (sit == null) return StatusCode(StatusCodes.Status502BadGateway, new { erro });
        return Ok(new { b.Id, b.NossoNumero, b.Valor, b.RecebidoEm, sit.Situacao, sit.Liquidado, sit.DataLiquidacao, sit.ValorPago });
    }
}
