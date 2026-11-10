using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Conferência automática com o Sicoob (plano postos-pagos-extrato, Etapa 5), pela tela ("Conferir com o banco")
/// e pelo agendamento de hora em hora no Supabase:
///  (a) boletos a receber CD/Perfil em aberto: consulta cada um na Cobrança; liquidado = marca recebido na data
///      da liquidação; baixado ou outra situação só relata. Para no 429 (o Sicoob limita as consultas seguidas);
///  (b) boletos dos postos pagos pelo extrato dos últimos dias (PostosPagosExtratoService, só os exatos);
///  (c) Pix de boleto (BOL-14): só IDENTIFICA e fica esperando a confirmação dele. Nada aqui dá baixa no banco,
///      paga, transfere ou devolve Pix.
/// </summary>
public class ConferenciaAutomaticaService(AppDbContext db, SicoobService sicoob, PostosPagosExtratoService postos,
    BoletoMontagemService montagem, ILogger<ConferenciaAutomaticaService> log)
{
    public const int DiasExtrato = 10;
    public const int MaxConsultas = 40;
    public const string Nota = "liquidado no Sicoob (conferência automática)";

    /// <summary>Pausa entre uma consulta de boleto e a próxima (o teste usa zero).</summary>
    public TimeSpan Pausa { get; set; } = TimeSpan.FromSeconds(1.5);

    // Uma conferência por vez no servidor (tela e agendamento juntos não consultam o banco em dobro)
    private static readonly SemaphoreSlim Trava = new(1, 1);

    public record ReceberMarcado(int Id, string Cliente, string NossoNumero, decimal? Valor, DateOnly Data, decimal? ValorPago, bool SemDataNoBanco);
    public record ReceberRelato(int Id, string Cliente, string NossoNumero, decimal? Valor, string Situacao);
    public record ParteReceber(int EmAberto, int Consultados, int AindaEmAberto, bool ParouNoLimite,
        List<ReceberMarcado> Marcados, List<ReceberRelato> NaoMarcados, string? Erro);
    public record PostoMarcado(int Id, string Posto, string Numero, decimal Valor, DateOnly Data);
    public record PartePostos(List<PostoMarcado> Marcados, int Duvidas, string? Erro);
    public record PixAguardando(string Id, DateOnly Data, decimal Valor, string Pagador, bool Duvida, List<string> Boletos);
    public record PartePix(List<PixAguardando> Aguardando, string? Erro);
    public record Resumo(DateTime Em, DateOnly Inicio, DateOnly Fim, ParteReceber Receber, PartePostos Postos, PartePix Pix)
    {
        // Para o log: só contagens, nada de conta, CNPJ ou nome
        public string Texto =>
            $"recebidos marcados {Receber.Marcados.Count} (consultados {Receber.Consultados}/{Receber.EmAberto}" +
            $"{(Receber.ParouNoLimite ? ", parou no limite do Sicoob" : "")}{(Receber.Erro != null ? ", erro" : "")}), " +
            $"não marcados {Receber.NaoMarcados.Count}; postos marcados {Postos.Marcados.Count}, dúvidas {Postos.Duvidas}" +
            $"{(Postos.Erro != null ? ", erro" : "")}; Pix aguardando confirmação {Pix.Aguardando.Count}{(Pix.Erro != null ? ", erro" : "")}";
    }

    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

    /// <summary>Roda as três conferências. null = outra conferência ainda rodando depois de esperar "espera".</summary>
    public async Task<Resumo?> ConferirTudoAsync(TimeSpan espera, CancellationToken ct = default)
    {
        if (!await Trava.WaitAsync(espera, ct)) return null;
        try
        {
            var hoje = Hoje();
            var de = hoje.AddDays(-DiasExtrato);
            if (sicoob.FaltaConfigurar() is { } falta)
                return new(DateTime.UtcNow, de, hoje, new(0, 0, 0, false, [], [], falta), new([], 0, falta), new([], falta));

            var receber = await ConferirReceberAsync(hoje, ct);

            var (extrato, erroExtrato) = await sicoob.ExtratoAsync(de, hoje, ct);
            PartePostos partePostos;
            PartePix partePix;
            if (extrato == null)
            {
                partePostos = new([], 0, erroExtrato);
                partePix = new([], erroExtrato);
            }
            else
            {
                var r = await postos.ConferirPostosAsync(extrato.Lancamentos, ct);
                partePostos = new(r.Marcados.Where(m => m.Agora)
                    .Select(m => new PostoMarcado(m.Boleto.Id, m.Boleto.Posto, m.Boleto.Numero, m.Boleto.Valor, m.Data)).ToList(),
                    r.Duvidas.Count, null);
                partePix = await IdentificarPixAsync(extrato.Lancamentos, de, ct);
            }
            return new(DateTime.UtcNow, de, hoje, receber, partePostos, partePix);
        }
        finally { Trava.Release(); }
    }

    public Task<Resumo?> ConferirTudoAsync(CancellationToken ct = default) => ConferirTudoAsync(TimeSpan.FromSeconds(90), ct);

    private async Task<ParteReceber> ConferirReceberAsync(DateOnly hoje, CancellationToken ct)
    {
        var abertos = await db.BoletosReceber
            .Where(b => b.RecebidoEm == null && b.NossoNumero != "" && b.Valor != null && b.Valor > 0)
            .OrderBy(b => b.Vencimento).ThenBy(b => b.Id)
            .ToListAsync(ct);
        var marcados = new List<ReceberMarcado>();
        var relatos = new List<ReceberRelato>();
        int consultados = 0, aindaAberto = 0;
        var parou = false;
        string? erroGeral = null;

        foreach (var b in abertos.Take(MaxConsultas))
        {
            if (consultados > 0 && Pausa > TimeSpan.Zero) await Task.Delay(Pausa, ct);
            var (sit, erro) = await sicoob.ConsultarBoletoAsync(b.NossoNumero, b.Valor, ct);
            if (sit == null)
            {
                if (erro == SicoobService.PediuTempo) { parou = true; break; }
                consultados++;
                // O banco não liberou a consulta: as próximas dariam o mesmo
                if (erro != null && erro.Contains("não liberou", StringComparison.OrdinalIgnoreCase)) { erroGeral = erro; break; }
                relatos.Add(new(b.Id, b.Cliente, b.NossoNumero, b.Valor, $"não consultado: {erro}"));
                continue;
            }
            consultados++;
            if (sit.Liquidado)
            {
                var data = sit.DataLiquidacao ?? hoje;
                var anotacao = sit.DataLiquidacao != null
                    ? $"{Nota} em {data:dd/MM/yyyy}"
                    : $"{Nota}; o banco não informou a data, marcado no dia da conferência {data:dd/MM/yyyy}";
                if (sit.ValorPago != null) anotacao += $"; valor pago {sit.ValorPago.Value.ToString("N2", new System.Globalization.CultureInfo("pt-BR"))}";
                b.RecebidoEm = data;
                b.Observacao = string.IsNullOrWhiteSpace(b.Observacao) ? anotacao : $"{b.Observacao.Trim()} · {anotacao}";
                b.AtualizadoEm = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                marcados.Add(new(b.Id, b.Cliente, b.NossoNumero, b.Valor, data, sit.ValorPago, sit.DataLiquidacao == null));
            }
            else if ((sit.Situacao ?? "").Contains("aberto", StringComparison.OrdinalIgnoreCase)) aindaAberto++;
            else relatos.Add(new(b.Id, b.Cliente, b.NossoNumero, b.Valor, string.IsNullOrWhiteSpace(sit.Situacao) ? "sem situação no banco" : sit.Situacao!));
        }
        if (parou) log.LogWarning("Conferência automática: o Sicoob pediu um tempo depois de {N} consultas", consultados);
        return new(abertos.Count, consultados, aindaAberto, parou, marcados, relatos, erroGeral);
    }

    // Mesmo critério da tela do extrato (SicoobController.PixDoExtrato): só aponta, nunca baixa
    private async Task<PartePix> IdentificarPixAsync(IReadOnlyList<SicoobService.Lancamento> lancamentos, DateOnly de, CancellationToken ct)
    {
        var pixes = lancamentos.Where(l => l.Credito && l.Data != null && PixBoletos.EhPix(l.Descricao, l.Complemento))
            .Select(l => new PixBoletos.Pix(l.Id, l.Data!.Value, l.Valor, l.Descricao, l.Complemento, l.CpfCnpj)).ToList();
        if (pixes.Count == 0) return new([], null);
        var clientes = new List<PixBoletos.Cliente>();
        foreach (var c in new[] { BoletoReceberLeitor.Perfil, BoletoReceberLeitor.Cd })
        {
            var (regras, _) = await montagem.Regras(c);
            clientes.Add(new(c, regras.Pagador.Nome, regras.Pagador.Cnpj));
        }
        var lidos = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Valor != null && b.Valor > 0 && (b.RecebidoEm == null || b.RecebidoEm >= de))
            .Select(b => new ConferenciaCobranca.Boleto(b.Id, b.Cliente, b.NossoNumero, b.Valor!.Value, b.Vencimento, b.RecebidoEm))
            .ToListAsync(ct);
        var achados = PixBoletos.Achar(pixes, lidos, clientes)
            .GroupBy(a => a.Pix.Id).Select(g => g.First())
            .Where(a => a.Opcoes.Count > 0)
            .Select(a => new PixAguardando(a.Pix.Id, a.Pix.Data, a.Pix.Valor, a.Pagador?.Nome ?? "pagador não reconhecido", a.Duvida,
                a.Opcoes[0].Select(b => b.NossoNumero).ToList()))
            .OrderByDescending(p => p.Data).ToList();
        return new(achados, null);
    }
}
