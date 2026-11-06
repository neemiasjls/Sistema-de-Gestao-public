using System.Globalization;
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
    public async Task<IActionResult> Extrato([FromQuery] string? inicio, [FromQuery] string? fim, CancellationToken ct)
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

        // Boletos a receber de mesmo valor: os em aberto (para marcar) e os já
        // marcados no dia do crédito (para a tela dizer que já está feito)
        var valores = extrato.Lancamentos.Where(l => l.Credito).Select(l => l.Valor).Distinct().ToList();
        var boletos = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Valor != null && valores.Contains(b.Valor.Value))
            .Select(b => new { b.Id, b.Cliente, b.NossoNumero, b.Valor, b.Vencimento, b.DataCarga, b.Placa, b.RecebidoEm })
            .ToListAsync(ct);

        var lancamentos = extrato.Lancamentos.Select(l =>
        {
            var mesmoValor = l.Credito ? boletos.Where(b => b.Valor == l.Valor).ToList() : [];
            var jaMarcado = mesmoValor.FirstOrDefault(b => b.RecebidoEm != null && b.RecebidoEm == l.Data);
            var emAberto = jaMarcado != null ? [] : mesmoValor
                .Where(b => b.RecebidoEm == null)
                .OrderBy(b => b.Vencimento == null || l.Data == null ? int.MaxValue : Math.Abs(b.Vencimento.Value.DayNumber - l.Data.Value.DayNumber))
                .ToList();
            return new
            {
                l.Id, l.Data, l.Credito, l.Valor, l.Descricao, l.Documento, l.CpfCnpj, l.Complemento,
                JaMarcado = jaMarcado == null ? null : new
                {
                    jaMarcado.Id, jaMarcado.Cliente, Rotulo = BoletoReceberLeitor.RotuloCliente(jaMarcado.Cliente),
                    jaMarcado.NossoNumero, jaMarcado.DataCarga,
                },
                Sugestoes = emAberto.Select(b => new
                {
                    b.Id, b.Cliente, Rotulo = BoletoReceberLeitor.RotuloCliente(b.Cliente),
                    b.NossoNumero, b.Valor, b.Vencimento, b.DataCarga, b.Placa,
                }),
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
}
