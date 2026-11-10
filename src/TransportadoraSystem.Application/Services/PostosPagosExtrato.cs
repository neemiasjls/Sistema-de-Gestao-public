using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Boleto do posto pago identificado pelo extrato do Sicoob (ABA-07): débito da conta × boleto de posto em aberto.
/// Nada aqui paga nada: só marca o que o Admin já pagou.
///
/// Suposições (o débito real de pagamento de boleto ainda não foi visto, só o que a API documenta):
/// - certo é só o débito e o valor exato; o que a descrição/documento/complemento trazem é usado quando ajuda;
/// - débito de Pix, TED, tarifa, transferência etc. não é pagamento de boleto e fica de fora;
/// - "código bate" = o nosso número do boleto (6+ dígitos) aparece no documento/complemento (linha digitável ou
///   código de barras) ou o documento do débito é o próprio número do boleto: aí é exato mesmo fora da janela;
/// - o CNPJ do posto não fica guardado no boleto, então não entra na conta;
/// - o nome do posto na descrição só desempata dois boletos de mesmo valor na janela.
/// </summary>
public static class PostosPagosExtrato
{
    // Pagamento de boleto do posto: até 10 dias antes do vencimento (pagou adiantado) e até 5 depois
    // (vencimento no fim de semana ou feriado vai para o dia útil seguinte; um atraso curto).
    public const int DiasAntes = 10, DiasDepois = 5;
    // Agendado: o débito cai no dia agendado ou logo depois (fim de semana, feriado)
    public const int AgendadoAntes = 3, AgendadoDepois = 5;
    public const string Nota = "identificado no extrato do Sicoob";

    public record Debito(string Chave, DateOnly Data, decimal Valor, string Descricao, string Documento, string CpfCnpj, string Complemento);

    public record Boleto(int Id, string Posto, string NumeroDocumento, string NossoNumero, decimal Valor,
        DateOnly? Vencimento, DateOnly? PagoEm, DateOnly? AgendadoPara, string PagoExtratoId, IReadOnlyCollection<string> Recusados);

    public record Exato(Debito Debito, Boleto Boleto, string Motivo);
    public record Duvida(Debito Debito, IReadOnlyList<Boleto> Opcoes, string Motivo);
    public record Resultado(List<Exato> Exatos, List<Duvida> Duvidas);

    /// <summary>Chave do lançamento: o id do Sicoob; sem id, data + valor + documento.</summary>
    public static string Chave(SicoobService.Lancamento l) =>
        !string.IsNullOrWhiteSpace(l.Id) ? l.Id.Trim()
            : $"{l.Data:yyyyMMdd}-{l.Valor.ToString("0.00", CultureInfo.InvariantCulture)}-{l.Documento.Trim()}";

    public static List<Debito> Debitos(IEnumerable<SicoobService.Lancamento> extrato) =>
        extrato.Where(l => !l.Credito && l.Data != null && l.Valor > 0)
            .Select(l => new Debito(Chave(l), l.Data!.Value, l.Valor, l.Descricao, l.Documento, l.CpfCnpj, l.Complemento))
            .ToList();

    public static string Normalizar(string? s)
    {
        var d = (s ?? "").ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    private static readonly string[] Fora = ["PIX", "TED", "TARIFA", "TAR ", "TAR.", "TRANSF", "IOF", "SAQUE", "CHEQUE", "ESTORNO",
        "APLICA", "RESGATE", "INTEGRALIZ", "CAPITAL", "EMPRESTIMO", "PARCELA", "SALARIO", "FOLHA", "DEB.AUT", "DEBITO AUTOMATICO"];
    private static readonly string[] DeBoleto = ["BOLETO", "TITULO", "TIT.", "TIT ", "PAG", "PGTO", "COBRAN", "CONVENIO", "COMPE", "CODIGO DE BARRAS"];

    /// <summary>Pix, TED, tarifa, transferência... não é pagamento de boleto.</summary>
    public static bool NaoEhBoleto(Debito d)
    {
        var t = Normalizar(d.Descricao);
        return Fora.Any(t.Contains);
    }

    /// <summary>A descrição diz que é pagamento de título/boleto.</summary>
    public static bool PareceBoleto(Debito d)
    {
        var t = Normalizar(d.Descricao + " " + d.Complemento);
        return DeBoleto.Any(t.Contains);
    }

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    public static bool CodigoBate(Debito d, Boleto b)
    {
        var nosso = Digitos(b.NossoNumero).TrimStart('0');
        var numero = Digitos(b.NumeroDocumento).TrimStart('0');
        foreach (var campo in new[] { d.Documento, d.Complemento, d.Descricao })
        {
            var dig = Digitos(campo);
            if (dig.Length == 0) continue;
            if (nosso.Length >= 6 && (dig.TrimStart('0') == nosso || (dig.Length >= 30 && dig.Contains(nosso)))) return true;
            if (numero.Length >= 4 && campo == d.Documento && dig.TrimStart('0') == numero) return true;
        }
        return false;
    }

    private static readonly HashSet<string> Genericas = ["POSTO", "POSTOS", "COMBUSTIVEIS", "COMBUSTIVEL", "LTDA", "AUTO", "NOVA", "COMERCIO", "DERIVADOS", "PETROLEO"];

    public static bool NomeBate(Debito d, Boleto b)
    {
        var texto = Normalizar(d.Descricao + " " + d.Complemento);
        var palavras = Normalizar(b.Posto).Split([' ', '-', '.', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 5 && !Genericas.Contains(p) && !p.All(char.IsDigit)).ToList();
        return palavras.Any(texto.Contains);
    }

    public static bool NaJanela(Debito d, Boleto b) =>
        (b.Vencimento is { } v && d.Data >= v.AddDays(-DiasAntes) && d.Data <= v.AddDays(DiasDepois)) ||
        (b.AgendadoPara is { } a && d.Data >= a.AddDays(-AgendadoAntes) && d.Data <= a.AddDays(AgendadoDepois));

    /// <summary>
    /// Casa cada débito com um boleto de posto em aberto de MESMO valor. Exato: um só boleto possível (código que bate,
    /// ou na janela do vencimento/agendado com descrição de pagamento de boleto) e nenhum outro débito querendo o mesmo
    /// boleto. Dois boletos para um débito, dois débitos para um boleto ou descrição que não diz boleto: dúvida.
    /// </summary>
    public static Resultado Casar(IEnumerable<Debito> debitos, IReadOnlyList<Boleto> boletos)
    {
        var usados = boletos.Where(b => b.PagoEm != null && b.PagoExtratoId != "").Select(b => b.PagoExtratoId).ToHashSet();
        var provaveis = new List<(Debito D, Boleto B, string Motivo)>();
        var duvidas = new List<Duvida>();

        foreach (var d in debitos.Where(d => !NaoEhBoleto(d) && !usados.Contains(d.Chave)).OrderBy(d => d.Data).ThenBy(d => d.Chave))
        {
            // Boleto de mesmo valor já marcado pago à mão no dia do débito, ou pelo DDA perto dele (a data do
            // pagamento no DDA pode diferir um dia do débito): esse débito já está contado
            if (boletos.Any(b => b.Valor == d.Valor && b.PagoEm is { } p && (
                    (b.PagoExtratoId == "" && p == d.Data) ||
                    (PostosDda.PeloDda(b.PagoExtratoId) && p >= d.Data.AddDays(-2) && p <= d.Data.AddDays(2))))) continue;

            var abertos = boletos.Where(b => b.PagoEm == null && b.Valor == d.Valor && !b.Recusados.Contains(d.Chave)).ToList();
            if (abertos.Count == 0) continue;
            var porCodigo = abertos.Where(b => CodigoBate(d, b)).ToList();
            var opcoes = porCodigo.Count > 0 ? porCodigo : abertos.Where(b => NaJanela(d, b)).ToList();
            if (opcoes.Count == 0) continue;
            var motivo = porCodigo.Count > 0 ? "o número do boleto aparece no débito" : "mesmo valor, perto do vencimento";
            if (opcoes.Count > 1 && porCodigo.Count == 0)
            {
                var peloNome = opcoes.Where(b => NomeBate(d, b)).ToList();
                if (peloNome.Count == 1) { opcoes = peloNome; motivo = "mesmo valor, perto do vencimento e o nome do posto no débito"; }
            }

            if (opcoes.Count > 1)
                duvidas.Add(new(d, opcoes, $"{opcoes.Count} boletos de posto em aberto com o mesmo valor"));
            else if (porCodigo.Count == 0 && !PareceBoleto(d))
                duvidas.Add(new(d, opcoes, "a descrição do débito não diz que é pagamento de boleto"));
            else
                provaveis.Add((d, opcoes[0], motivo));
        }

        // Dois débitos para o mesmo boleto: não escolhe
        var exatos = new List<Exato>();
        foreach (var g in provaveis.GroupBy(p => p.B.Id))
        {
            if (g.Count() == 1) { var p = g.First(); exatos.Add(new(p.D, p.B, p.Motivo)); continue; }
            foreach (var p in g) duvidas.Add(new(p.D, [p.B], $"{g.Count()} débitos de mesmo valor para o mesmo boleto"));
        }
        return new(exatos, duvidas);
    }

    public static List<string> LerRecusados(string? s) =>
        (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// Mudança da data de pagamento por fora da conferência (tela, desfazer): tirar o pago de um boleto marcado
    /// pelo extrato guarda o débito como "não é este", para a próxima conferência não marcar de novo.
    /// Marcar pago tira o agendado.
    /// </summary>
    public static void MudarPagamento(BoletoPosto b, DateOnly? pagoEm, string? chaveExtrato = null)
    {
        if (b.PagoEm == pagoEm && chaveExtrato == null) return;
        if (pagoEm == null && b.PagoExtratoId != "")
        {
            var recusados = LerRecusados(b.ExtratoRecusados);
            if (!recusados.Contains(b.PagoExtratoId)) recusados.Add(b.PagoExtratoId);
            b.ExtratoRecusados = string.Join(";", recusados.TakeLast(20));
        }
        b.PagoEm = pagoEm;
        b.PagoExtratoId = pagoEm == null ? "" : chaveExtrato ?? "";
        if (pagoEm != null) { b.AgendadoPara = null; b.AgendadoPeloBanco = false; }
        b.AtualizadoEm = DateTime.UtcNow;
    }
}

/// <summary>
/// Conferência dos boletos dos postos com o extrato (sem tela): chamada ao abrir o Extrato e pela consulta
/// automática. Marca pago o que casou exato e devolve o que marcou, o que já estava marcado pelo extrato e as dúvidas.
/// </summary>
public class PostosPagosExtratoService(AppDbContext db)
{
    public record BoletoVisto(int Id, string Posto, string Numero, decimal Valor, DateOnly? Vencimento, DateOnly? AgendadoPara);
    public record Marcado(BoletoVisto Boleto, DateOnly Data, string Chave, string Descricao, string Motivo, bool Agora);
    public record DuvidaVista(string Chave, DateOnly Data, decimal Valor, string Descricao, string Motivo, List<BoletoVisto> Opcoes);
    public record Conferencia(List<Marcado> Marcados, List<DuvidaVista> Duvidas);

    private static BoletoVisto Visto(PostosPagosExtrato.Boleto b) =>
        new(b.Id, b.Posto, b.NumeroDocumento != "" ? b.NumeroDocumento : b.NossoNumero, b.Valor, b.Vencimento, b.AgendadoPara);

    public async Task<Conferencia> ConferirPostosAsync(IReadOnlyList<SicoobService.Lancamento> extrato, CancellationToken ct = default)
    {
        var debitos = PostosPagosExtrato.Debitos(extrato);
        if (debitos.Count == 0) return new([], []);
        var desde = debitos.Min(d => d.Data).AddDays(-30);
        var entidades = await db.BoletosPosto
            .Where(b => b.Valor != null && (b.PagoEm == null || b.PagoEm >= desde))
            .ToListAsync(ct);
        var boletos = entidades.Select(b => new PostosPagosExtrato.Boleto(b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero,
            b.Valor!.Value, b.Vencimento, b.PagoEm, b.AgendadoPara, b.PagoExtratoId ?? "",
            PostosPagosExtrato.LerRecusados(b.ExtratoRecusados))).ToList();

        var r = PostosPagosExtrato.Casar(debitos, boletos);
        var porId = entidades.ToDictionary(b => b.Id);
        foreach (var e in r.Exatos)
            PostosPagosExtrato.MudarPagamento(porId[e.Boleto.Id], e.Debito.Data, e.Debito.Chave);
        if (r.Exatos.Count > 0) await db.SaveChangesAsync(ct);

        var porChave = debitos.GroupBy(d => d.Chave).ToDictionary(g => g.Key, g => g.First());
        var agora = r.Exatos.Select(e => e.Boleto.Id).ToHashSet();
        var marcados = r.Exatos.Select(e => new Marcado(Visto(e.Boleto), e.Debito.Data, e.Debito.Chave, e.Debito.Descricao, e.Motivo, true))
            .Concat(boletos.Where(b => !agora.Contains(b.Id) && b.PagoEm != null && porChave.ContainsKey(b.PagoExtratoId))
                .Select(b => new Marcado(Visto(b), b.PagoEm!.Value, b.PagoExtratoId, porChave[b.PagoExtratoId].Descricao,
                    "marcado antes pelo extrato", false)))
            .OrderByDescending(m => m.Data).ToList();
        var duvidas = r.Duvidas.Select(d => new DuvidaVista(d.Debito.Chave, d.Debito.Data, d.Debito.Valor, d.Debito.Descricao, d.Motivo,
            d.Opcoes.Select(Visto).ToList())).OrderByDescending(d => d.Data).ToList();
        return new(marcados, duvidas);
    }

    /// <summary>Ele escolheu na dúvida: marca este boleto pago com a data do débito.</summary>
    public async Task<string?> MarcarAsync(int boletoId, string chave, DateOnly data, CancellationToken ct = default)
    {
        var b = await db.BoletosPosto.FirstOrDefaultAsync(x => x.Id == boletoId, ct);
        if (b == null) return "Boleto não encontrado.";
        if (b.PagoEm != null) return $"Este boleto já está pago em {b.PagoEm:dd/MM/yyyy}.";
        if (await db.BoletosPosto.AnyAsync(x => x.PagoExtratoId == chave && x.PagoEm != null, ct))
            return "Este débito já foi usado para outro boleto. Atualize a tela.";
        PostosPagosExtrato.MudarPagamento(b, data, chave);
        await db.SaveChangesAsync(ct);
        return null;
    }
}
