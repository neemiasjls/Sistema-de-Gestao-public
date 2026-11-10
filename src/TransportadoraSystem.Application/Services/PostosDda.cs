using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Boleto do posto AGENDADO ou PAGO achado no DDA do Sicoob (ABA-07): o DDA lista os boletos emitidos contra o CNPJ
/// da conta, com a situação (2 agendado, 3 liquidado). Só consulta; nada aqui paga, agenda ou baixa no banco.
///
/// Casamento de cada boleto do DDA com o boleto do posto guardado no site:
///  1. código de barras (o do PDF, guardado ao importar) igual ao do DDA;
///  2. senão, nosso número do boleto contido no do DDA e mesmo valor;
///  3. senão, mesmo valor e mesmo vencimento (ou, sem nenhum igual, vencimento até 3 dias de diferença); o CNPJ do
///     posto não fica guardado no boleto; o número do documento e o nome do posto no beneficiário só desempatam.
/// Mais de um boleto possível, ou dois boletos do DDA para o mesmo boleto: dúvida, não marca.
/// Boleto do DDA sem nenhum boleto do site: só relata (não cria).
/// </summary>
public static class PostosDda
{
    public const string Prefixo = "dda:";
    // Janela por vencimento: o boleto pago com atraso curto e o agendado com antecedência
    public const int DiasAtras = 15, DiasFrente = 45;
    public const int SituacaoAgendado = 2, SituacaoLiquidado = 3;
    public const int DiasVencimentoProximo = 3;

    public record Item(string Chave, int Situacao, string CnpjBeneficiario, string Beneficiario, decimal Valor,
        DateOnly? Vencimento, string CodigoBarras, string NossoNumero, string NumeroDocumento,
        DateOnly? DataPagamento, decimal? ValorPagamento);

    public record Boleto(int Id, string Posto, string NumeroDocumento, string NossoNumero, string CodigoBarras,
        decimal? Valor, DateOnly? Vencimento, DateOnly? PagoEm, DateOnly? AgendadoPara, bool AgendadoPeloBanco,
        string PagoExtratoId, IReadOnlyCollection<string> Recusados);

    public record Casado(Item Item, Boleto Boleto, string Motivo);
    public record Duvida(Item Item, IReadOnlyList<Boleto> Opcoes, string Motivo);
    public record Resultado(List<Casado> Casados, List<Duvida> Duvidas, List<Item> SemCadastro);

    public enum Tipo { Pagar, Agendar, TirarAgendado }
    public record Acao(Tipo Tipo, Boleto Boleto, Item? Item, DateOnly? Data, bool SemDataNoBanco, string Motivo);

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    /// <summary>
    /// Código de barras de 44 dígitos a partir do código (44) ou da linha digitável (47), com o dígito verificador
    /// conferido. null quando não é um boleto bancário válido (convênio de 48 dígitos, leitura errada...).
    /// </summary>
    public static string? CodigoDeBarras(string? texto)
    {
        var d = Digitos(texto);
        string codigo;
        if (d.Length == 44) codigo = d;
        else if (d.Length == 47)
            // linha: campo 1 (banco, moeda, livre 1–5, dv) · campo 2 (livre 6–15, dv) · campo 3 (livre 16–25, dv) · dv geral · fator+valor
            codigo = d[..4] + d[32] + d[33..47] + d[4..9] + d[10..20] + d[21..31];
        else return null;
        if (codigo[0] == '8') return null; // convênio (água, luz): outro formato
        return DvGeral(codigo) == codigo[4] - '0' ? codigo : null;
    }

    // Módulo 11 (Febraban): pesos 2 a 9 da direita para a esquerda, sem a posição 5
    private static int DvGeral(string codigo)
    {
        var sem = codigo[..4] + codigo[5..];
        int soma = 0, peso = 2;
        for (var i = sem.Length - 1; i >= 0; i--) { soma += (sem[i] - '0') * peso; peso = peso == 9 ? 2 : peso + 1; }
        var dv = 11 - soma % 11;
        return dv is 0 or 10 or 11 ? 1 : dv;
    }

    /// <summary>Chave do boleto do DDA (vai no PagoExtratoId do boleto pago por ele, com o prefixo "dda:").</summary>
    public static string Chave(string codigoBarras, string nossoNumero, decimal valor, DateOnly? vencimento) =>
        Prefixo + (codigoBarras != "" ? codigoBarras
            : $"{Digitos(nossoNumero)}-{valor.ToString("0.00", CultureInfo.InvariantCulture)}-{vencimento:yyyyMMdd}");

    public static bool PeloDda(string? pagoExtratoId) => (pagoExtratoId ?? "").StartsWith(Prefixo, StringComparison.Ordinal);

    // Nosso número do posto ("09/12345678901-P"): o miolo (6+ dígitos) aparece no do DDA
    private static bool NossoBate(Item i, Boleto b)
    {
        var partes = b.NossoNumero.Split('/', '-');
        var miolo = Digitos(partes.Length >= 2 ? partes[1] : b.NossoNumero).TrimStart('0');
        var dda = Digitos(i.NossoNumero).TrimStart('0');
        return miolo.Length >= 6 && dda.Length >= miolo.Length && dda.Contains(miolo, StringComparison.Ordinal);
    }

    private static bool DocumentoBate(Item i, Boleto b)
    {
        var a = CobrancaAbastecimentoService.NumeroChave(i.NumeroDocumento);
        return a.Length >= 3 && a == CobrancaAbastecimentoService.NumeroChave(b.NumeroDocumento);
    }

    private static bool NomeBate(Item i, Boleto b) =>
        b.Posto != "" && CobrancaAbastecimentoService.NomePosto(i.Beneficiario) == b.Posto;

    /// <summary>O posto que o nome do beneficiário lembra (para o relato do que não está cadastrado).</summary>
    public static string? PostoDoBeneficiario(string beneficiario) => CobrancaAbastecimentoService.DetectarPosto(beneficiario);

    // Dois códigos lidos e diferentes: são boletos diferentes, mesmo com valor e vencimento iguais
    private static bool CodigoContra(Item i, Boleto b) => i.CodigoBarras != "" && b.CodigoBarras != "" && i.CodigoBarras != b.CodigoBarras;

    /// <summary>
    /// O mesmo boleto pode vir nas duas consultas (agendado e liquidado): fica o liquidado.
    /// </summary>
    public static List<Item> SemRepetidos(IEnumerable<Item> itens) =>
        itens.GroupBy(i => i.Chave).Select(g => g.OrderByDescending(i => i.Situacao == SituacaoLiquidado).First()).ToList();

    public static Resultado Casar(IEnumerable<Item> itens, IReadOnlyList<Boleto> boletos)
    {
        var provaveis = new List<Casado>();
        var duvidas = new List<Duvida>();
        var sem = new List<Item>();

        foreach (var i in SemRepetidos(itens).OrderBy(i => i.Vencimento).ThenBy(i => i.Chave))
        {
            List<Boleto> opcoes;
            string motivo;
            var porCodigo = i.CodigoBarras == "" ? [] : boletos.Where(b => b.CodigoBarras == i.CodigoBarras).ToList();
            if (porCodigo.Count > 0) { opcoes = porCodigo; motivo = "código de barras igual"; }
            else
            {
                var mesmoValor = boletos.Where(b => b.Valor == i.Valor && !CodigoContra(i, b)).ToList();
                var porNosso = mesmoValor.Where(b => NossoBate(i, b)).ToList();
                if (porNosso.Count > 0) { opcoes = porNosso; motivo = "nosso número e valor iguais"; }
                else
                {
                    opcoes = mesmoValor.Where(b => b.Vencimento != null && b.Vencimento == i.Vencimento).ToList();
                    motivo = "mesmo valor e vencimento";
                    // Vencimento no fim de semana: o lido do PDF e o do banco podem diferir alguns dias (Estrada: impresso
                    // 14/11, código 15/11). Só sem nenhum de vencimento igual.
                    if (opcoes.Count == 0 && i.Vencimento is { } vi)
                    {
                        opcoes = mesmoValor.Where(b => b.Vencimento is { } vb && Math.Abs(vb.DayNumber - vi.DayNumber) <= DiasVencimentoProximo).ToList();
                        motivo = "mesmo valor e vencimento próximo";
                    }
                    if (opcoes.Count > 1)
                    {
                        var peloDoc = opcoes.Where(b => DocumentoBate(i, b)).ToList();
                        var peloNome = opcoes.Where(b => NomeBate(i, b)).ToList();
                        if (peloDoc.Count == 1) { opcoes = peloDoc; motivo = "mesmo valor, vencimento e número do documento"; }
                        else if (peloNome.Count == 1) { opcoes = peloNome; motivo = "mesmo valor, vencimento e nome do posto"; }
                    }
                    else if (opcoes.Count == 1 && (DocumentoBate(i, opcoes[0]) || NomeBate(i, opcoes[0])))
                        motivo += DocumentoBate(i, opcoes[0]) ? " e número do documento" : " e nome do posto";
                }
            }

            if (opcoes.Count == 0) sem.Add(i);
            else if (opcoes.Count > 1) duvidas.Add(new(i, opcoes, $"{opcoes.Count} boletos de posto possíveis ({motivo})"));
            else provaveis.Add(new(i, opcoes[0], motivo));
        }

        // Dois boletos do DDA para o mesmo boleto do site: não escolhe
        var casados = new List<Casado>();
        foreach (var g in provaveis.GroupBy(p => p.Boleto.Id))
        {
            if (g.Count() == 1) { casados.Add(g.First()); continue; }
            foreach (var p in g) duvidas.Add(new(p.Item, [p.Boleto], $"{g.Count()} boletos do DDA para o mesmo boleto do posto"));
        }
        return new(casados, duvidas, sem);
    }

    /// <summary>
    /// O que fazer com o que casou: liquidado = pago na data do pagamento (se ele não desfez antes esse mesmo
    /// boleto do DDA); agendado = agendado pelo banco na data do agendamento. "listasCompletas" = as duas consultas
    /// deram certo: aí o agendado pelo banco que sumiu do DDA (agendamento cancelado) é retirado — só para boleto
    /// com vencimento dentro do período consultado.
    /// </summary>
    public static List<Acao> Acoes(Resultado r, IReadOnlyList<Boleto> boletos, bool listasCompletas, DateOnly inicio, DateOnly fim, DateOnly hoje)
    {
        var acoes = new List<Acao>();
        foreach (var c in r.Casados)
        {
            var b = c.Boleto;
            if (b.PagoEm != null) continue; // já pago (à mão, pelo extrato ou pelo DDA antes)
            if (c.Item.Situacao == SituacaoLiquidado)
            {
                if (b.Recusados.Contains(c.Item.Chave)) continue;
                acoes.Add(new(Tipo.Pagar, b, c.Item, c.Item.DataPagamento ?? hoje, c.Item.DataPagamento == null, c.Motivo));
            }
            else if (c.Item.Situacao == SituacaoAgendado)
            {
                var data = c.Item.DataPagamento ?? c.Item.Vencimento ?? b.Vencimento;
                if (data == null || (b.AgendadoPeloBanco && b.AgendadoPara == data)) continue;
                acoes.Add(new(Tipo.Agendar, b, c.Item, data, false, c.Motivo));
            }
        }
        if (listasCompletas)
        {
            var vistos = r.Casados.Select(c => c.Boleto.Id).Concat(r.Duvidas.SelectMany(d => d.Opcoes.Select(o => o.Id))).ToHashSet();
            foreach (var b in boletos.Where(b => b.AgendadoPeloBanco && b.AgendadoPara != null && b.PagoEm == null && !vistos.Contains(b.Id)
                         && b.Vencimento is { } v && v >= inicio && v <= fim))
                acoes.Add(new(Tipo.TirarAgendado, b, null, null, false, "o agendamento sumiu do DDA"));
        }
        return acoes;
    }
}

/// <summary>Conferência dos boletos dos postos com o DDA do Sicoob (consulta automática de hora em hora e tela).</summary>
public class PostosDdaService(AppDbContext db, SicoobService sicoob)
{
    public record Marcado(int Id, string Posto, string Numero, decimal? Valor, DateOnly Data, string Motivo, bool SemDataNoBanco);
    public record SemCadastro(string Beneficiario, string? Posto, decimal Valor, DateOnly? Vencimento, string Situacao);
    public record Parte(List<Marcado> Agendados, List<Marcado> Pagos, int AgendamentosRetirados, int Duvidas,
        List<SemCadastro> NaoCadastrados, string? Erro);

    public static PostosDda.Boleto Converter(BoletoPosto b) =>
        new(b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.CodigoBarras ?? "", b.Valor, b.Vencimento, b.PagoEm,
            b.AgendadoPara, b.AgendadoPeloBanco, b.PagoExtratoId ?? "", PostosPagosExtrato.LerRecusados(b.ExtratoRecusados));

    public async Task<Parte> ConferirAsync(DateOnly hoje, CancellationToken ct = default)
    {
        var inicio = hoje.AddDays(-PostosDda.DiasAtras);
        var fim = hoje.AddDays(PostosDda.DiasFrente);

        var itens = new List<PostosDda.Item>();
        var erros = new List<string>();
        foreach (var situacao in new[] { PostosDda.SituacaoAgendado, PostosDda.SituacaoLiquidado })
        {
            var (lista, erro) = await sicoob.BoletosDdaAsync(situacao, inicio, fim, ct);
            if (lista == null) { erros.Add(erro ?? "erro desconhecido"); continue; }
            itens.AddRange(lista);
        }
        // As duas falharam: nada a casar (a mesma causa, em geral: app sem a API liberada)
        if (erros.Count == 2) return new([], [], 0, 0, [], $"DDA indisponível: {erros.Distinct().First()}");
        var listasCompletas = erros.Count == 0;

        var desde = inicio.AddDays(-30);
        var entidades = await db.BoletosPosto
            .Where(b => b.Valor != null && (b.PagoEm == null || b.PagoEm >= desde))
            .ToListAsync(ct);
        var boletos = entidades.Select(Converter).ToList();
        var r = PostosDda.Casar(itens, boletos);
        var acoes = PostosDda.Acoes(r, boletos, listasCompletas, inicio, fim, hoje);

        var porId = entidades.ToDictionary(b => b.Id);
        var agendados = new List<Marcado>();
        var pagos = new List<Marcado>();
        var retirados = 0;
        foreach (var a in acoes)
        {
            var e = porId[a.Boleto.Id];
            var numero = e.NumeroDocumento != "" ? e.NumeroDocumento : e.NossoNumero;
            switch (a.Tipo)
            {
                case PostosDda.Tipo.Pagar:
                    PostosPagosExtrato.MudarPagamento(e, a.Data, a.Item!.Chave);
                    pagos.Add(new(e.Id, e.Posto, numero, e.Valor, a.Data!.Value, a.Motivo, a.SemDataNoBanco));
                    break;
                case PostosDda.Tipo.Agendar:
                    e.AgendadoPara = a.Data;
                    e.AgendadoPeloBanco = true;
                    e.AtualizadoEm = DateTime.UtcNow;
                    agendados.Add(new(e.Id, e.Posto, numero, e.Valor, a.Data!.Value, a.Motivo, false));
                    break;
                case PostosDda.Tipo.TirarAgendado:
                    e.AgendadoPara = null;
                    e.AgendadoPeloBanco = false;
                    e.AtualizadoEm = DateTime.UtcNow;
                    retirados++;
                    break;
            }
        }
        if (acoes.Count > 0) await db.SaveChangesAsync(ct);

        var semCadastro = r.SemCadastro
            .Select(i => new SemCadastro(i.Beneficiario, PostosDda.PostoDoBeneficiario(i.Beneficiario), i.Valor, i.Vencimento,
                i.Situacao == PostosDda.SituacaoLiquidado ? "pago" : "agendado"))
            .OrderBy(s => s.Posto == null).ThenBy(s => s.Vencimento).ToList();
        var erroParcial = erros.Count == 0 ? null : $"DDA em parte indisponível: {erros[0]}";
        return new(agendados, pagos, retirados, r.Duvidas.Count, semCadastro, erroParcial);
    }
}
