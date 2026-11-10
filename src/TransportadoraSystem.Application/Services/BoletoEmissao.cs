using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Emite no Sicoob o boleto a receber montado pelo BoletoMontagemService (BOL-10/BOL-11) e grava no
/// BoletosReceber o nosso número, a linha digitável e o PDF. Cobrança REAL: a tela pede a confirmação
/// do Admin a cada boleto e manda de volta o valor e o vencimento que ele viu; se a montagem mudou
/// desde então, não emite. Uma carga com boleto (mesmo o lançado à mão) não emite de novo.
/// </summary>
public class BoletoEmissaoService(AppDbContext db, BoletoMontagemService montagem, SicoobService sicoob,
    ILogger<BoletoEmissaoService> log)
{
    // A API roda numa instância só: a trava impede o duplo clique de emitir duas vezes
    private static readonly SemaphoreSlim Trava = new(1, 1);

    // Caixas: CD, as caixas de produtoD confirmadas (nulo = as do romaneio); o valor é recalculado com elas
    public record Pedido(string Cliente, DateOnly Data, string? Placa, decimal Valor, DateOnly Vencimento, decimal? Caixas = null);

    public record Resultado(int? Id, string? NossoNumero, string? LinhaDigitavel, string? NomeArquivo, bool TemPdf,
        string? Erro, bool Incerto);

    public async Task<Resultado> Emitir(Pedido p, CancellationToken ct = default)
    {
        await Trava.WaitAsync(ct);
        try { return await EmitirTravado(p, ct); }
        finally { Trava.Release(); }
    }

    private static Resultado Falha(string erro, bool incerto = false) => new(null, null, null, null, false, erro, incerto);

    private async Task<Resultado> EmitirTravado(Pedido p, CancellationToken ct)
    {
        var m = await montagem.Montar(p.Cliente, p.Data, p.Placa, p.Caixas);
        var b = m.Boleto;
        if (m.ImpedeEmitir.Count > 0) return Falha("Não dá para emitir: " + string.Join(" ", m.ImpedeEmitir));
        if (b.Valor is not decimal valor || Math.Abs(valor - p.Valor) >= 0.005m || b.Vencimento != p.Vencimento)
            return Falha("O boleto mudou desde que foi montado (valor ou vencimento). Monte de novo e confira antes de emitir.");

        var (emitido, erro, incerto) = await sicoob.IncluirBoletoAsync(b, ct);
        var agora = DateTime.UtcNow;
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

        var reg = new BoletoReceber
        {
            Cliente = b.Cliente,
            Sequencia = m.Sequencia,
            Numero = b.SeuNumero,
            Pagador = b.Pagador.Nome,
            Placa = b.Placa ?? "",
            Valor = valor,
            Vencimento = b.Vencimento,
            Emissao = b.Emissao,
            DataCarga = p.Data,
            NomeArquivo = m.NomeArquivo,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };

        if (emitido == null)
        {
            if (!incerto) return Falha(erro ?? "O Sicoob não emitiu o boleto.");
            // Pode ter sido registrado: guarda a carga como "a conferir" para não emitir duas vezes
            reg.Observacao = $"Emissão enviada ao Sicoob em {hoje:dd/MM/yyyy} sem resposta: confira no Sicoobnet. "
                + "Se o boleto existir, suba o PDF dele aqui; se não existir, exclua este registro e emita de novo.";
            db.BoletosReceber.Add(reg);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) { log.LogError(ex, "Não gravei a emissão incerta {Cliente} {Data}", b.Cliente, p.Data); }
            return new Resultado(reg.Id == 0 ? null : reg.Id, null, null, reg.NomeArquivo, false, erro, true);
        }

        reg.NossoNumero = ComDigito(emitido.NossoNumero);
        reg.LinhaDigitavel = emitido.LinhaDigitavel;
        reg.Pdf = emitido.Pdf;
        reg.Hash = emitido.Pdf == null ? null : Convert.ToHexString(SHA256.HashData(emitido.Pdf));
        reg.Observacao = $"Emitido pelo sistema no Sicoob em {hoje:dd/MM/yyyy}.";
        db.BoletosReceber.Add(reg);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            log.LogError(ex, "Boleto EMITIDO no Sicoob e não gravado: {Cliente} {Data} nosso número {Nosso} linha {Linha}",
                b.Cliente, p.Data, emitido.NossoNumero, emitido.LinhaDigitavel);
            return Falha($"O boleto FOI emitido no Sicoob (nosso número {reg.NossoNumero}), mas não consegui gravar aqui. "
                + "Baixe o PDF no Sicoobnet e suba em Boletos → A receber; não emita de novo.");
        }
        return new Resultado(reg.Id, reg.NossoNumero, reg.LinhaDigitavel, reg.NomeArquivo, reg.Pdf != null, null, false);
    }

    /// <summary>
    /// O Sicoob devolve o nosso número com o dígito no fim (6710); o sistema guarda como o boleto
    /// impresso ("671-0"), que é como a consulta (SicoobService.ConsultarBoletoAsync) e a planilha usam.
    /// </summary>
    public static string ComDigito(string nosso)
    {
        var d = new string(nosso.Where(char.IsDigit).ToArray()).TrimStart('0');
        return d.Length > 1 ? $"{d[..^1]}-{d[^1]}" : nosso.Trim();
    }
}
