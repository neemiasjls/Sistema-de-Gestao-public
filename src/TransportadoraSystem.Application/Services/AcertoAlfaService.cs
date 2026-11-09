using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// O acerto semanal com a Fazenda Alfa, com a mesma cara da planilha "ACERTO G. ALFA":
/// à esquerda o CONTROLE DESCONTOS ROMANEIOS (romaneios da fazenda com o desconto por
/// caixa), à direita PAGAMENTOS E DEPÓSITOS (saldo anterior, PIX, boletos, cheques...).
/// Dos PDFs lê boletos (vencimento, valor, número, pagador) e romaneios da fazenda
/// (data, número, valor, caixas); das planilhas feitas à mão lê os dois blocos inteiros.
/// A planilha de saída sai sempre dos dados, pelo modelo Templates/ACERTO_ALFA.xlsx.
/// </summary>
public class AcertoAlfaService
{
    // Dígito verificador pode ser número OU a letra X (regra bancária p/ resto 10)
    private static readonly Regex NossoNum = new(@"^(\d{10,})-(\d|X)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Data     = new(@"^\d{2}/\d{2}/\d{4}$", RegexOptions.Compiled);
    private static readonly Regex UsMoney  = new(@"^\d{1,3}(,\d{3})*\.\d{2}$", RegexOptions.Compiled); // 5,127.54
    private static readonly Regex Inteiro  = new(@"^\d{1,3}(,\d{3})*$", RegexOptions.Compiled);         // 1,335

    // Cabeçalho do romaneio: "CONTROLE...: 132778   EMISSÃO: 07/09/2026   PEDIDO: 000000".
    // Mais abaixo o número se repete em "CONTROLE: 132778  VENCIMENTO: ...", por isso
    // só vale como cabeçalho a linha que traz a EMISSÃO junto.
    private static readonly Regex RomCabecalho = new(
        @"CONTROLE[^\d]{0,8}(\d{3,})\W*EMISS\w*\W*(\d{2}/\d{2}/\d{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public record Boleto(string Cliente, DateTime Vencimento, DateTime DataDoc, double Valor, string Numero);
    public record Romaneio(int Numero, DateTime Data, double Valor, int Caixas);
    public record AcertoResult(byte[] Bytes, string NomeArquivo, List<string> Avisos);

    // Texto do pagador → rótulo usado na coluna L do template
    private static string? ClienteLabel(string texto)
    {
        var t = texto.ToUpperInvariant();
        if (t.Contains("COMERCIO DE HORTIFRUTI")) return "CD";
        if (t.Contains("VERDURAO DUNAS") || t.Contains("DUNAS")) return "DUNAS";
        if (t.Contains("BRZ DISTRIBUIDOR") || t.Contains("BRZ ")) return "BRZ";
        if (t.Contains("AURORA")) return "AURORA";
        if (t.Contains("LITORAL")) return "LITORAL";
        return null;
    }

    // Rótulos da coluna L do template que recebem boleto lido do PDF
    public static readonly string[] Clientes = ["AURORA", "BRZ", "CD", "DUNAS", "LITORAL"];

    // O que saiu dos PDFs. Tipos traz, na ordem dos PDFs, o que cada um era:
    // BOLETO, ROMANEIO ou OUTRO (não reconhecido).
    public record Leitura(List<Boleto> Boletos, List<Romaneio> Romaneios, List<string> Avisos, List<string> Tipos);

    public AcertoResult Gerar(IEnumerable<byte[]> pdfs, int numero) => Gerar(Ler(pdfs), numero);

    /// <summary>
    /// A planilha de uma leitura já feita — o controller lê uma vez só, gera e
    /// guarda os mesmos dados. Sem nada reconhecido, avisa e sai sem data no nome.
    /// Com origem, o SALDO ANT. sai com o saldo do acerto anterior.
    /// </summary>
    public AcertoResult Gerar(Leitura lido, int numero, SaldoOrigem? origem = null)
    {
        var avisos = lido.Avisos;

        if (lido.Boletos.Count == 0 && lido.Romaneios.Count == 0)
        {
            avisos.Add("Nenhum boleto ou romaneio reconhecido nos PDFs.");
            var vazio = ComSaldoAnterior(ParaDados(lido.Boletos, lido.Romaneios, DateTime.Today), origem);
            return new AcertoResult(Preencher(vazio, avisos), $"{numero}-ACERTO G. ALFA.xlsx", avisos);
        }

        var segunda = Semana(lido.Boletos, lido.Romaneios);
        var dados = ComSaldoAnterior(ParaDados(lido.Boletos, lido.Romaneios, segunda, avisos), origem);
        return GerarDeDados(dados, numero, segunda, avisos);
    }

    /// <summary>Lê os PDFs sem gerar planilha nenhuma.</summary>
    public Leitura Ler(IEnumerable<byte[]> pdfs)
    {
        var avisos = new List<string>();
        var boletos = new List<Boleto>();
        var romaneios = new List<Romaneio>();
        var tipos = new List<string>();

        // O mesmo campo recebe boleto e romaneio da fazenda: quem decide é o conteúdo
        // do PDF, não o nome do arquivo.
        foreach (var pdf in pdfs)
        {
            var (eRomaneio, itens) = LerRomaneios(pdf, avisos);
            if (eRomaneio) { romaneios.AddRange(itens); tipos.Add("ROMANEIO"); continue; }

            var b = LerBoleto(pdf, avisos);
            if (b != null) boletos.Add(b);
            tipos.Add(b != null ? "BOLETO" : "OUTRO");
        }

        // O mesmo romaneio pode chegar sozinho e dentro do PDF do dia; contar duas
        // vezes estragaria o total, então fica só o primeiro.
        var repetidos = romaneios.GroupBy(r => r.Numero).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0)
        {
            avisos.Add($"Romaneio(s) repetido(s) nos PDFs, considerei uma vez só: {string.Join(", ", repetidos)}.");
            romaneios = romaneios.GroupBy(r => r.Numero).Select(g => g.First()).ToList();
        }

        return new Leitura(boletos, romaneios, avisos, tipos);
    }

    // Semana de referência = segunda-feira da semana da DATA DO DOCUMENTO do
    // primeiro boleto (sem boleto, vale a data do romaneio mais antigo)
    public static DateTime Semana(List<Boleto> boletos, List<Romaneio> romaneios)
    {
        var docDate = boletos.Count > 0 ? boletos[0].DataDoc
                    : romaneios.Count > 0 ? romaneios.Min(r => r.Data)
                    : DateTime.Today;
        return Segunda(docDate);
    }

    public static DateTime Segunda(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    public static DateOnly Segunda(DateOnly d) => DateOnly.FromDateTime(Segunda(d.ToDateTime(TimeOnly.MinValue)));

    /// <summary>
    /// Gera a planilha a partir dos dados, lidos agora ou guardados e editados na
    /// tela. O Excel é só a saída: tudo o que vai nele vem destes dados.
    /// </summary>
    public AcertoResult GerarDeDados(Dados dados, int numero, DateTime segunda, List<string>? avisos = null)
    {
        avisos ??= [];
        var bytes = Preencher(dados, avisos);
        var nome = $"{numero}-ACERTO G. ALFA-{segunda:dd-MM-yyyy}.xlsx";
        return new AcertoResult(bytes, nome, avisos);
    }

    // ───────────────────── Dados guardados (DadosJson) ─────────────────────
    // O acerto fica no banco como JSON com a cara da planilha, linha a linha e na
    // ordem em que saem nela, com datas sem hora ("2026-09-28"):
    //   { romaneios:  [{ numero, data, valor, caixas, descontoCaixa, rotulo }],
    //     pagamentos: [{ data, condicao, valor, observacao, numero }],
    //     notas:      ["texto livre embaixo do total dos romaneios"] }
    // O formato antigo, { boletos: [...], romaneios: [...] }, continua abrindo: os
    // boletos são encaixados nas linhas do modelo, do mesmo jeito que a planilha
    // saía antes, e o romaneio sem descontoCaixa fica com a coluna E vazia.

    // O romaneio que chega pelo PDF entra sem desconto por caixa (coluna E vazia):
    // o Admin preenche na tela, porque o valor muda de semana para semana.

    /// <summary>
    /// Uma linha do CONTROLE DESCONTOS ROMANEIOS. Rotulo é o texto que às vezes vai no
    /// lugar do número ("BOLETO", "CH DEV"), e aí Numero fica 0. DescontoCaixa nulo é a
    /// coluna E vazia (sem desconto).
    /// </summary>
    /// DoArquivo (ACE-11): true = veio do PDF do romaneio e não se edita na tela (só o
    /// desconto por caixa, que não está no documento); false = lançada à mão; null =
    /// gravada antes desta marca (MarcarOrigem decide pelos PDFs guardados no acerto).
    public record RomaneioDados(int Numero, DateOnly? Data, double Valor, int Caixas, double? DescontoCaixa = null, string? Rotulo = null,
        bool? DoArquivo = null);

    /// <summary>
    /// Uma linha de PAGAMENTOS E DEPÓSITOS. Data é o vencimento no boleto; Valor nulo
    /// é a linha lançada antes do dinheiro (ou do boleto) chegar. DoArquivo como no romaneio:
    /// true só no boleto que veio do PDF.
    /// </summary>
    public record PagamentoDados(DateOnly? Data, string Condicao, double? Valor, string Observacao, string Numero, bool? DoArquivo = null);

    public record Dados(List<RomaneioDados> Romaneios, List<PagamentoDados> Pagamentos, List<string> Notas);

    /// <summary>Boleto do formato antigo do DadosJson (e do PUT antigo).</summary>
    public record BoletoDados(string Cliente, DateOnly Vencimento, DateOnly? DataDoc, double Valor, string Numero);

    private record DadosGuardados(List<RomaneioDados>? Romaneios, List<PagamentoDados>? Pagamentos, List<string>? Notas, List<BoletoDados>? Boletos);

    /// <summary>Os totais do pé da planilha, calculados como as fórmulas dela.</summary>
    public record Totais(double ValorRomaneios, double Descontos, double APagar, int Caixas, double Pagamentos, double Saldo);

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static double ValorDesconto(RomaneioDados r) => r.Caixas * (r.DescontoCaixa ?? 0);   // F = D*E
    public static double APagar(RomaneioDados r) => r.Valor - ValorDesconto(r);                 // G = C-F

    // G25 = SUM(G), K27 = SUM(K), L28 = K27 - G25
    public static Totais Totalizar(Dados d)
    {
        var aPagar = Math.Round(d.Romaneios.Sum(APagar), 2);
        var pagos = Math.Round(d.Pagamentos.Sum(p => p.Valor ?? 0), 2);
        return new Totais(
            Math.Round(d.Romaneios.Sum(r => r.Valor), 2),
            Math.Round(d.Romaneios.Sum(ValorDesconto), 2),
            aPagar,
            d.Romaneios.Sum(r => r.Caixas),
            pagos,
            Math.Round(pagos - aPagar, 2));
    }

    // ───────────────────── Saldo anterior em cadeia ─────────────────────
    // Nas planilhas (180 a 194) o SALDO ANT. de um acerto é o "créditos e débitos
    // com Livia" (L28 = K27 − G25) do acerto de número anterior, com o mesmo sinal,
    // e a data dele é a segunda-feira daquele acerto. Aqui ele é calculado na hora:
    // a linha SALDO ANT. SEM valor guardado é automática; com valor, foi digitada à
    // mão e vale o que está lá. Corrigir um acerto antigo muda o saldo final dele
    // e, com isso, o SALDO ANT. de todos os seguintes que estão no automático.

    public const string CondicaoSaldoAnterior = "SALDO ANT.";

    /// <summary>De onde vem o saldo anterior automático: o acerto de número anterior e o saldo com que fechou.</summary>
    public record SaldoOrigem(int Numero, DateOnly Semana, double Saldo);

    public static bool EhSaldoAnterior(PagamentoDados p) => Igual(p.Condicao, CondicaoSaldoAnterior);

    /// <summary>A linha SALDO ANT. que é automática (só a primeira), ou −1.</summary>
    public static int IndiceSaldoAnterior(Dados d) => d.Pagamentos.FindIndex(EhSaldoAnterior);

    /// <summary>Linha SALDO ANT. sem valor guardado = automática.</summary>
    public static bool SaldoAnteriorAutomatico(Dados d) =>
        IndiceSaldoAnterior(d) is var i and >= 0 && d.Pagamentos[i].Valor == null;

    /// <summary>
    /// Os dados como valem para os totais e para o Excel: a linha SALDO ANT.
    /// automática ganha o saldo e a data do acerto de origem. Digitada à mão (ou
    /// sem acerto anterior no sistema) fica como está.
    /// </summary>
    public static Dados ComSaldoAnterior(Dados d, SaldoOrigem? origem)
    {
        var i = IndiceSaldoAnterior(d);
        if (i < 0 || origem == null || d.Pagamentos[i].Valor != null) return d;
        var pagamentos = d.Pagamentos.ToList();
        pagamentos[i] = pagamentos[i] with { Valor = origem.Saldo, Data = origem.Semana };
        return d with { Pagamentos = pagamentos };
    }

    /// <summary>Um acerto com o saldo anterior resolvido.</summary>
    public record Encadeado(int Numero, DateOnly Semana, Dados Guardado, Dados Valendo, SaldoOrigem? Origem, Totais Totais);

    /// <summary>
    /// Resolve o SALDO ANT. de todos, do menor número para o maior: o saldo final
    /// de cada um é a origem do seguinte. Número repetido não existe (índice único).
    /// </summary>
    public static List<Encadeado> Encadear(IEnumerable<(int Numero, DateOnly Semana, Dados Dados)> acertos)
    {
        var lista = new List<Encadeado>();
        SaldoOrigem? origem = null;
        foreach (var a in acertos.OrderBy(x => x.Numero))
        {
            var valendo = ComSaldoAnterior(a.Dados, origem);
            var totais = Totalizar(valendo);
            lista.Add(new Encadeado(a.Numero, a.Semana, a.Dados, valendo, origem, totais));
            origem = new SaldoOrigem(a.Numero, a.Semana, totais.Saldo);
        }
        return lista;
    }

    public static bool EhBoleto(PagamentoDados p) => Igual(p.Condicao, "BOLETO");
    private static bool Igual(string? a, string? b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    // Acerto sem romaneio e sem nenhum pagamento de verdade: só as linhas do modelo
    public static bool Vazio(Dados d) =>
        d.Romaneios.Count == 0 && d.Pagamentos.All(p => p.Valor == null && string.IsNullOrWhiteSpace(p.Numero));

    /// <summary>
    /// As linhas de PAGAMENTOS E DEPÓSITOS que o modelo já traz (SALDO ANT., PIX V. M. ROCHA,
    /// BOLETO de cada cliente), sem valor. O SALDO ANT. é da segunda anterior.
    /// </summary>
    public static List<PagamentoDados> Esqueleto(DateOnly semana) =>
        LinhasDoModelo.Value
            .Select(l => new PagamentoDados(Igual(l.Condicao, "SALDO ANT.") ? semana.AddDays(-7) : null, l.Condicao, null, l.Observacao, ""))
            .ToList();

    public static Dados Novo(DateOnly semana) => new([], Esqueleto(semana), []);

    /// <summary>Os dados de um acerto novo a partir do que foi lido dos PDFs.</summary>
    public static Dados ParaDados(IEnumerable<Boleto> boletos, IEnumerable<Romaneio> romaneios, DateTime segunda, List<string>? avisos = null)
    {
        var (dados, _, _, lidos) = Acrescentar(Novo(DateOnly.FromDateTime(segunda)), boletos, romaneios);
        avisos?.AddRange(lidos);
        return dados;
    }

    /// <summary>
    /// Soma boletos e romaneios lidos a um acerto que já existe sem mexer no que
    /// está lá: o boleto ocupa uma linha do cliente ainda sem valor (ou a lançada à
    /// mão com o mesmo valor, que só ganha número e vencimento); sem linha livre,
    /// entra logo depois da última do cliente. O romaneio entra na ordem de número.
    /// Boleto ou romaneio de número que o acerto já tem fica de fora.
    /// </summary>
    public static (Dados Dados, int BoletosNovos, int RomaneiosNovos, List<string> Avisos) Acrescentar(
        Dados atual, IEnumerable<Boleto> boletos, IEnumerable<Romaneio> romaneios)
    {
        var avisos = new List<string>();
        var pagamentos = atual.Pagamentos.ToList();
        var linhas = atual.Romaneios.ToList();
        int boletosNovos = 0, romaneiosNovos = 0;

        // A ordem de antes: cliente e, dentro dele, número do boleto
        foreach (var b in boletos.OrderBy(x => x.Cliente).ThenBy(x => x.Numero))
        {
            if (EncaixarBoleto(pagamentos, b.Cliente, DateOnly.FromDateTime(b.Vencimento), b.Valor, b.Numero)) boletosNovos++;
            else avisos.Add($"O boleto {b.Numero} já estava no acerto e ficou de fora.");
        }

        foreach (var r in romaneios.OrderBy(x => x.Numero))
        {
            if (linhas.Any(x => x.Numero == r.Numero))
            {
                avisos.Add($"O romaneio {r.Numero} já estava no acerto e ficou de fora.");
                continue;
            }
            // Logo depois do último romaneio de número menor; as linhas de texto
            // ("BOLETO", "CH DEV") ficam onde estavam, depois dos romaneios
            var antes = linhas.FindLastIndex(x => x.Numero > 0 && x.Numero < r.Numero);
            var depois = linhas.FindIndex(x => x.Numero > r.Numero);
            var pos = antes >= 0 ? antes + 1 : depois >= 0 ? depois : linhas.FindIndex(x => x.Numero == 0) is var t and >= 0 ? t : linhas.Count;
            linhas.Insert(pos, new RomaneioDados(r.Numero, DateOnly.FromDateTime(r.Data), r.Valor, r.Caixas, null, null, true));
            romaneiosNovos++;
        }

        return (atual with { Romaneios = linhas, Pagamentos = pagamentos }, boletosNovos, romaneiosNovos, avisos);
    }

    /// <summary>Põe um boleto entre os pagamentos. Falso quando o número já está lá.</summary>
    public static bool EncaixarBoleto(List<PagamentoDados> pagamentos, string cliente, DateOnly vencimento, double valor, string numero)
    {
        if (!string.IsNullOrWhiteSpace(numero) && pagamentos.Any(p => Igual(p.Numero, numero))) return false;

        bool Livre(PagamentoDados p) => EhBoleto(p) && Igual(p.Observacao, cliente) && string.IsNullOrWhiteSpace(p.Numero);

        // Lançado à mão com o mesmo valor antes do PDF chegar: ganha o número e,
        // se não tinha, o vencimento. Senão, a primeira linha do cliente sem valor.
        var i = pagamentos.FindIndex(p => Livre(p) && p.Valor is { } v && Math.Abs(v - valor) < 0.005);
        if (i < 0) i = pagamentos.FindIndex(p => Livre(p) && p.Valor == null);
        if (i >= 0)
        {
            var p = pagamentos[i];
            pagamentos[i] = new PagamentoDados(p.Data ?? vencimento, "BOLETO", valor,
                string.IsNullOrWhiteSpace(p.Observacao) ? cliente : p.Observacao, numero, true);
            return true;
        }

        var ultima = pagamentos.FindLastIndex(p => EhBoleto(p) && Igual(p.Observacao, cliente));
        pagamentos.Insert(ultima >= 0 ? ultima + 1 : pagamentos.Count, new PagamentoDados(vencimento, "BOLETO", valor, cliente, numero, true));
        return true;
    }

    // ───────────────────── Origem e documento repetido (ACE-11) ─────────────────────

    /// <summary>Uma diferença entre o documento guardado e o do arquivo novo, campo a campo.</summary>
    public record Diferenca(string Documento, string Campo, string Atual, string Novo);

    private static readonly CultureInfo PtBrDif = CultureInfo.GetCultureInfo("pt-BR");
    private static string TextoValor(double? v) => v is { } x ? x.ToString("N2", PtBrDif) : "vazio";
    private static string TextoData(DateOnly? d) => d is { } x ? x.ToString("dd/MM/yyyy") : "vazio";
    private static string TextoLivre(string? s) => string.IsNullOrWhiteSpace(s) ? "vazio" : s.Trim();
    private static bool ValorIgual(double? a, double? b) => a is { } x && b is { } y ? Math.Abs(x - y) < 0.005 : a == null && b == null;

    /// <summary>Chave do boleto (nosso número) para achar o mesmo documento.</summary>
    public static string ChaveBoleto(string? numero) => (numero ?? "").Trim().ToUpperInvariant();

    /// <summary>Linha de boleto com número: a que pode ter vindo de um PDF.</summary>
    public static bool BoletoComNumero(PagamentoDados p) => EhBoleto(p) && !string.IsNullOrWhiteSpace(p.Numero);

    /// <summary>
    /// O romaneio guardado × o do arquivo: data, valor e caixas (o número é a chave; o
    /// desconto por caixa não está no documento).
    /// </summary>
    public static List<Diferenca> CompararRomaneio(RomaneioDados atual, Romaneio novo)
    {
        var doc = $"Romaneio {novo.Numero}";
        var dif = new List<Diferenca>();
        var data = DateOnly.FromDateTime(novo.Data);
        if (atual.Data != data) dif.Add(new(doc, "Data", TextoData(atual.Data), TextoData(data)));
        if (!ValorIgual(atual.Valor, novo.Valor)) dif.Add(new(doc, "Valor", TextoValor(atual.Valor), TextoValor(novo.Valor)));
        if (atual.Caixas != novo.Caixas) dif.Add(new(doc, "Caixas", atual.Caixas.ToString("N0", PtBrDif), novo.Caixas.ToString("N0", PtBrDif)));
        return dif;
    }

    /// <summary>O boleto guardado × o do arquivo: vencimento, valor, pagador (e a condição, se não era BOLETO).</summary>
    public static List<Diferenca> CompararBoleto(PagamentoDados atual, Boleto novo)
    {
        var doc = $"Boleto {novo.Numero}";
        var dif = new List<Diferenca>();
        var venc = DateOnly.FromDateTime(novo.Vencimento);
        if (!EhBoleto(atual)) dif.Add(new(doc, "Condição", TextoLivre(atual.Condicao), "BOLETO"));
        if (atual.Data != venc) dif.Add(new(doc, "Vencimento", TextoData(atual.Data), TextoData(venc)));
        if (!ValorIgual(atual.Valor, novo.Valor)) dif.Add(new(doc, "Valor", TextoValor(atual.Valor), TextoValor(novo.Valor)));
        if (!Igual(atual.Observacao, novo.Cliente)) dif.Add(new(doc, "Pagador", TextoLivre(atual.Observacao), TextoLivre(novo.Cliente)));
        return dif;
    }

    /// <summary>
    /// Troca o romaneio de mesmo número pelo do arquivo, no mesmo lugar. O desconto por
    /// caixa digitado na tela fica (não faz parte do documento).
    /// </summary>
    public static Dados SubstituirRomaneio(Dados d, Romaneio novo)
    {
        var linhas = d.Romaneios
            .Select(r => r.Numero == novo.Numero
                ? new RomaneioDados(novo.Numero, DateOnly.FromDateTime(novo.Data), novo.Valor, novo.Caixas, r.DescontoCaixa, null, true)
                : r)
            .ToList();
        return d with { Romaneios = linhas };
    }

    /// <summary>Troca o boleto de mesmo número pelo do arquivo, no mesmo lugar, inteiro.</summary>
    public static Dados SubstituirBoleto(Dados d, Boleto novo)
    {
        var chave = ChaveBoleto(novo.Numero);
        var linhas = d.Pagamentos
            .Select(p => ChaveBoleto(p.Numero) == chave && chave != ""
                ? new PagamentoDados(DateOnly.FromDateTime(novo.Vencimento), "BOLETO", novo.Valor, novo.Cliente, novo.Numero, true)
                : p)
            .ToList();
        return d with { Pagamentos = linhas };
    }

    /// <summary>Falta decidir a origem de alguma linha gravada antes da marca DoArquivo?</summary>
    public static bool OrigemPendente(Dados d) =>
        d.Romaneios.Any(r => r.Numero > 0 && r.DoArquivo == null) || d.Pagamentos.Any(p => BoletoComNumero(p) && p.DoArquivo == null);

    /// <summary>
    /// Dados gravados antes da marca: romaneio ou boleto cujo número está num PDF guardado
    /// no acerto veio do arquivo; o resto foi lançado à mão (ou por planilha do Excel).
    /// Só mexe no que ainda está sem marca.
    /// </summary>
    public static Dados MarcarOrigem(Dados d, ISet<int> romaneiosDosPdfs, ISet<string> boletosDosPdfs) => d with
    {
        Romaneios = d.Romaneios
            .Select(r => r.DoArquivo != null ? r : r with { DoArquivo = r.Numero > 0 && romaneiosDosPdfs.Contains(r.Numero) })
            .ToList(),
        Pagamentos = d.Pagamentos
            .Select(p => p.DoArquivo != null ? p : p with { DoArquivo = BoletoComNumero(p) && boletosDosPdfs.Contains(ChaveBoleto(p.Numero)) })
            .ToList(),
    };

    public static string Serializar(Dados d) => JsonSerializer.Serialize(d, Json);

    /// <summary>
    /// Lê o DadosJson de um acerto. A semana só é usada no formato antigo, para
    /// montar o esqueleto de pagamentos em que os boletos são encaixados.
    /// </summary>
    public static Dados Desserializar(string? json, DateOnly semana)
    {
        DadosGuardados? g = null;
        if (!string.IsNullOrWhiteSpace(json)) g = JsonSerializer.Deserialize<DadosGuardados>(json, Json);

        var romaneios = (g?.Romaneios ?? []).Select(r => r with { Rotulo = string.IsNullOrWhiteSpace(r.Rotulo) ? null : r.Rotulo }).ToList();
        var notas = g?.Notas ?? [];
        if (g?.Pagamentos != null)
            return new Dados(romaneios, g.Pagamentos.Select(Limpar).ToList(), notas);

        // Formato antigo: esqueleto do modelo + boletos, na ordem em que saíam
        var pagamentos = Esqueleto(semana);
        foreach (var b in (g?.Boletos ?? []).OrderBy(x => x.Cliente).ThenBy(x => x.Numero))
            EncaixarBoleto(pagamentos, b.Cliente, b.Vencimento, b.Valor, b.Numero);
        return new Dados(romaneios, pagamentos, notas);
    }

    private static PagamentoDados Limpar(PagamentoDados p) =>
        new(p.Data, p.Condicao ?? "", p.Valor, p.Observacao ?? "", p.Numero ?? "", p.DoArquivo);

    // ───────────────────────── PDF do boleto ─────────────────────────

    /// <summary>
    /// Um boleto da fazenda guardado num acerto, lido de novo (o e-mail semanal da
    /// Cedro Frutas e do Dunas procura os boletos de cada cliente por aqui).
    /// Null quando não é boleto de cliente conhecido ou o PDF não abre.
    /// </summary>
    public static Boleto? LerBoletoGuardado(byte[] pdf)
    {
        try { return LerBoleto(pdf, []); }
        catch (Exception) { return null; }
    }

    internal static Boleto? LerBoleto(byte[] pdf, List<string> avisos)
    {
        string texto;
        var linhas = new List<List<string>>();
        using (var doc = PdfDocument.Open(pdf))
        {
            texto = string.Join("\n", doc.GetPages().Select(p => p.Text));
            foreach (var page in doc.GetPages())
                linhas.AddRange(Linhas(page));
        }

        var cliente = ClienteLabel(texto);
        if (cliente == null) { avisos.Add("Não é boleto de pagador conhecido (Aurora, BRZ, CD, Dunas, Litoral) nem romaneio da fazenda."); return null; }

        string? numero = null;
        DateTime? venc = null, docDate = null;
        double? valor = null;

        DateTime Parse(string s) => DateTime.ParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture);

        foreach (var linha in linhas)
        {
            // Nosso Número → número significativo (tira o prefixo com zeros)
            foreach (var t in linha)
            {
                var m = NossoNum.Match(t);
                if (m.Success)
                {
                    var sig = Regex.Match(m.Groups[1].Value, @"0([1-9]\d*)$");
                    var numSig = sig.Success ? sig.Groups[1].Value : m.Groups[1].Value;
                    numero = $"{numSig}-{m.Groups[2].Value}";
                }
            }

            // Linha de valores tem VENCIMENTO (data) + VALOR (US money) juntos:
            //   "04/08/2026 1413721 R$ 5,127.54"
            var datas   = linha.Where(t => Data.IsMatch(t)).ToList();
            var moedas  = linha.Where(t => UsMoney.IsMatch(t)).ToList();
            if (venc == null && datas.Count > 0 && moedas.Count > 0)
            {
                venc  = Parse(datas[0]);
                valor = double.Parse(moedas[0].Replace(",", ""), CultureInfo.InvariantCulture);
            }
        }

        // Data do documento = a data que NÃO é o vencimento (ex: 14/07 vs venc 04/08)
        foreach (var linha in linhas)
        {
            foreach (var t in linha)
                if (Data.IsMatch(t))
                {
                    var d = Parse(t);
                    if (venc == null || d != venc.Value) { docDate = d; break; }
                }
            if (docDate != null) break;
        }

        if (numero == null || venc == null || valor == null || docDate == null)
        {
            avisos.Add($"Boleto {cliente}: não consegui ler todos os campos (venc/valor/número).");
            return null;
        }
        return new Boleto(cliente, venc.Value, docDate.Value, valor.Value, numero);
    }

    // ─────────────────────── Romaneio da fazenda ───────────────────────
    // Cada romaneio abre no cabeçalho com CONTROLE + EMISSÃO e fecha em
    // "T O T A L G E R A L...  535  68,061.75" (caixas e valor). Um PDF pode trazer
    // vários em sequência, então a leitura é linha a linha, do começo ao fim.
    private static (bool eRomaneio, List<Romaneio> itens) LerRomaneios(byte[] pdf, List<string> avisos)
    {
        var itens = new List<Romaneio>();
        bool eRomaneio = false;
        int? numero = null;
        DateTime? data = null;

        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            foreach (var linha in Linhas(page))
            {
                var m = RomCabecalho.Match(string.Join(" ", linha));
                if (m.Success)
                {
                    if (numero != null)
                        avisos.Add($"Romaneio {numero}: não achei o TOTAL GERAL (deixei de fora).");
                    eRomaneio = true;
                    numero = int.Parse(m.Groups[1].Value);
                    data   = DateTime.ParseExact(m.Groups[2].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    continue;
                }

                if (numero == null || data == null || !EhTotalGeral(linha)) continue;

                // Os dois últimos números da linha são a quantidade e o valor
                var nums = linha.Where(t => UsMoney.IsMatch(t) || Inteiro.IsMatch(t)).ToList();
                if (nums.Count < 2) continue;

                double Num(string t) => double.Parse(t.Replace(",", ""), CultureInfo.InvariantCulture);
                itens.Add(new Romaneio(numero.Value, data.Value, Num(nums[^1]), (int)Num(nums[^2])));
                numero = null;
                data   = null;
            }

        if (numero != null)
            avisos.Add($"Romaneio {numero}: não achei o TOTAL GERAL (deixei de fora).");

        return (eRomaneio, itens);
    }

    // "T O T A L G E R A L..." vem com as letras separadas; juntar só as letras
    // distingue do "T O T A L B R U T O..." e do "T O T A L ....." logo acima.
    private static bool EhTotalGeral(List<string> linha)
    {
        var letras = new string(string.Concat(linha).Where(char.IsLetter).ToArray());
        return letras.StartsWith("TOTALGERAL", StringComparison.OrdinalIgnoreCase);
    }

    internal static IEnumerable<List<string>> Linhas(Page page) =>
        page.GetWords()
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom))
            .OrderByDescending(g => g.Key)
            .Select(g => g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text).ToList());


    // ───────────────────────── Template ─────────────────────────
    private static readonly Lazy<byte[]> Modelo = new(() =>
    {
        var asm = typeof(AcertoAlfaService).Assembly;
        using var stream = asm.GetManifestResourceStream("TransportadoraSystem.Application.Templates.ACERTO_ALFA.xlsx")
            ?? throw new InvalidOperationException("Template ACERTO_ALFA.xlsx não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    });

    // Linhas 4..26 do bloco PAGAMENTOS E DEPÓSITOS e 4..24 do CONTROLE DESCONTOS
    // ROMANEIOS, como vêm no modelo
    private const int Primeira = 4, UltimaRomaneio = 24, UltimaPagamento = 26;

    // O que o modelo já traz escrito em PAGAMENTOS E DEPÓSITOS (condição e observação)
    private static readonly Lazy<List<(string Condicao, string Observacao)>> LinhasDoModelo = new(() =>
    {
        using var wb = new XLWorkbook(new MemoryStream(Modelo.Value));
        var ws = wb.Worksheets.First();
        var linhas = new List<(string, string)>();
        for (int r = Primeira; r <= UltimaPagamento; r++)
        {
            var condicao = ws.Cell(r, 10).GetString().Trim();
            var observacao = ws.Cell(r, 12).GetString().Trim();
            if (condicao != "" || observacao != "") linhas.Add((condicao, observacao));
        }
        return linhas;
    });

    private static byte[] Preencher(Dados dados, List<string> avisos)
    {
        using var wb = new XLWorkbook(new MemoryStream(Modelo.Value));
        var ws = wb.Worksheets.First();

        // O bloco de pagamentos sai inteiro dos dados, inclusive as linhas que o
        // modelo já trazia (elas vêm no esqueleto de todo acerto)
        ws.Range(Primeira, 9, UltimaPagamento, 13).Clear(XLClearOptions.Contents);

        // Linhas que faltam abrem DENTRO dos dois blocos, para o SUM de cada total
        // esticar junto. As fórmulas de desconto das linhas novas entram na mão.
        int sobrando = Math.Max(
            dados.Romaneios.Count - (UltimaRomaneio - Primeira + 1),
            dados.Pagamentos.Count - (UltimaPagamento - Primeira + 1));
        if (sobrando > 0)
        {
            ws.Row(UltimaRomaneio).InsertRowsAbove(sobrando);
            for (int r = UltimaRomaneio; r < UltimaRomaneio + sobrando; r++)
            {
                ws.Cell(r, 6).FormulaA1 = $"D{r}*E{r}";
                ws.Cell(r, 7).FormulaA1 = $"C{r}-F{r}";
            }
        }
        else sobrando = 0;

        // CONTROLE DESCONTOS ROMANEIOS (A..E), na ordem dos dados. F e G são as
        // fórmulas que o modelo já traz.
        int linha = Primeira;
        foreach (var rom in dados.Romaneios)
        {
            if (rom.Data is { } data)
            {
                ws.Cell(linha, 1).Value = data.ToDateTime(TimeOnly.MinValue);   // A DATA
                ws.Cell(linha, 1).Style.DateFormat.Format = "DD/MM/YYYY";
            }
            if (!string.IsNullOrWhiteSpace(rom.Rotulo)) ws.Cell(linha, 2).Value = rom.Rotulo.Trim(); // B texto no lugar do número
            else ws.Cell(linha, 2).Value = rom.Numero;                           // B Nº ROM
            ws.Cell(linha, 3).Value = rom.Valor;                                 // C VALOR ROMANEIO
            ws.Cell(linha, 4).Value = rom.Caixas;                                // D QDE CXS
            if (rom.DescontoCaixa is { } desconto) ws.Cell(linha, 5).Value = desconto; // E DESC.CXS
            linha++;
        }

        // PAGAMENTOS E DEPÓSITOS (I..M), linha a linha
        linha = Primeira;
        foreach (var p in dados.Pagamentos)
        {
            if (p.Data is { } data)
            {
                ws.Cell(linha, 9).Value = data.ToDateTime(TimeOnly.MinValue);    // I DATA / VENCIMENTO
                ws.Cell(linha, 9).Style.DateFormat.Format = "DD/MM/YYYY";
            }
            if (!string.IsNullOrWhiteSpace(p.Condicao)) ws.Cell(linha, 10).Value = p.Condicao.Trim();     // J CONDIÇÕES
            if (p.Valor is { } valor) ws.Cell(linha, 11).Value = valor;                                   // K VALOR
            if (!string.IsNullOrWhiteSpace(p.Observacao)) ws.Cell(linha, 12).Value = p.Observacao.Trim(); // L OBSERVAÇÕES
            if (!string.IsNullOrWhiteSpace(p.Numero)) ws.Cell(linha, 13).Value = p.Numero.Trim();         // M NÚMERO
            linha++;
        }

        // Observações embaixo do total dos romaneios: o modelo tem quatro linhas
        // mescladas de A a G; o que passar disso vai junto na última
        var notas = dados.Notas.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (notas.Count > 4) notas = [.. notas.Take(3), string.Join(" / ", notas.Skip(3))];
        for (int i = 0; i < notas.Count; i++)
            ws.Cell(UltimaRomaneio + sobrando + 2 + i, 1).Value = notas[i];

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);
        return outMs.ToArray();
    }

    // ───────────────── Planilha de acerto feita à mão ─────────────────
    // As planilhas "NNN-ACERTO G. ALFA-dd-MM-aaaa.xlsx" que ela já fazia no Excel.
    // A leitura vai pelos rótulos (DATA, VALOR TOTAL ROMANEIOS, DEPÓSITOS E
    // PAGAMENTOS, CRÉDITOS E DÉBITOS COM LIVIA) e pelas colunas do modelo, porque
    // cada semana tem um tanto de linhas. O que vem abaixo dos totais (as cópias
    // quebradas com #REF!) fica de fora.

    private static readonly Regex NomePlanilha = new(
        @"^\s*(\d+)\s*-\s*ACERTO\s+G\.?\s*ALFA\s*-\s*(\d{2})-(\d{2})-(\d{4})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Número e semana pelo nome do arquivo.</summary>
    public static (int Numero, DateOnly Semana)? LerNomePlanilha(string nome)
    {
        var m = NomePlanilha.Match(Path.GetFileNameWithoutExtension(nome));
        if (!m.Success) return null;
        if (!DateOnly.TryParseExact($"{m.Groups[2].Value}/{m.Groups[3].Value}/{m.Groups[4].Value}", "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)) return null;
        return (int.Parse(m.Groups[1].Value), data);
    }

    /// <summary>Os três totais que o Excel gravou na planilha (G25, K27 e L28 no modelo).</summary>
    public record TotaisGravados(double? APagar, double? Pagamentos, double? Saldo);

    public record PlanilhaLida(Dados Dados, TotaisGravados Gravados, List<string> Avisos);

    public PlanilhaLida LerPlanilha(byte[] xlsx)
    {
        var avisos = new List<string>();
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheets.First();
        int fim = Math.Max(ws.LastRowUsed()?.RowNumber() ?? 0, 30);

        int Achar(int de, int coluna, Func<string, bool> teste)
        {
            for (int r = de; r <= fim; r++)
                if (teste(Rotulo(ws.Cell(r, coluna)))) return r;
            return -1;
        }

        int cabecalho = Achar(1, 1, t => t == "DATA");
        if (cabecalho < 0 || !Rotulo(ws.Cell(cabecalho, 3)).StartsWith("VALOR"))
            throw new InvalidDataException("Não achei o cabeçalho DATA / Nº ROM / VALOR ROMANEIO na planilha.");
        int totalRomaneios = Achar(cabecalho + 1, 1, t => t.StartsWith("VALOR TOTAL ROMANEIOS"));
        if (totalRomaneios < 0) throw new InvalidDataException("Não achei a linha VALOR TOTAL ROMANEIOS.");
        int totalPagamentos = Achar(cabecalho + 1, 9, t => t.Contains("DEPOSITOS E PAGAMENTOS"));
        if (totalPagamentos < 0) throw new InvalidDataException("Não achei a linha DEPÓSITOS E PAGAMENTOS.");
        int linhaSaldo = Achar(totalPagamentos + 1, 9, t => t.StartsWith("CREDITOS E DEBITOS"));

        // CONTROLE DESCONTOS ROMANEIOS: A data, B número (ou texto), C valor, D caixas, E desconto
        var romaneios = new List<RomaneioDados>();
        for (int r = cabecalho + 1; r < totalRomaneios; r++)
        {
            if (Enumerable.Range(1, 4).All(c => Valor(ws.Cell(r, c)).IsBlank)) continue;

            var data = LerDataCelula(ws.Cell(r, 1));
            if (data == null) avisos.Add($"Romaneio da linha {r}: sem data.");

            int numero = 0;
            string? rotulo = null;
            var b = Valor(ws.Cell(r, 2));
            if (b.IsNumber && b.GetNumber() is var n && n > 0 && n == Math.Floor(n) && n < int.MaxValue) numero = (int)n;
            else if (Texto(ws.Cell(r, 2)) is { Length: > 0 } t)
            {
                if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var ni) && ni > 0) numero = ni;
                else rotulo = t.ToUpperInvariant();
            }
            else avisos.Add($"Romaneio da linha {r}: sem número.");

            var valor = LerNumeroCelula(ws.Cell(r, 3), $"Romaneio da linha {r}: valor", avisos) ?? 0;
            var caixasLidas = LerNumeroCelula(ws.Cell(r, 4), $"Romaneio da linha {r}: caixas", avisos) ?? 0;
            var caixas = (int)Math.Round(caixasLidas);
            if (Math.Abs(caixas - caixasLidas) > 1e-9) avisos.Add($"Romaneio da linha {r}: {caixasLidas} caixas não é número inteiro, ficou {caixas}.");
            var desconto = LerNumeroCelula(ws.Cell(r, 5), $"Romaneio da linha {r}: desconto por caixa", avisos);

            romaneios.Add(new RomaneioDados(numero, data, Math.Round(valor, 2), caixas, desconto is { } dc ? Math.Round(dc, 2) : null, rotulo));
        }
        var repetidos = romaneios.Where(x => x.Numero > 0).GroupBy(x => x.Numero).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0) avisos.Add($"Romaneio(s) repetido(s) na planilha: {string.Join(", ", repetidos)}.");

        // PAGAMENTOS E DEPÓSITOS: I data, J condição, K valor, L observação, M número
        var pagamentos = new List<PagamentoDados>();
        for (int r = cabecalho + 1; r < totalPagamentos; r++)
        {
            if (Enumerable.Range(9, 5).All(c => Valor(ws.Cell(r, c)).IsBlank)) continue;
            pagamentos.Add(new PagamentoDados(
                LerDataCelula(ws.Cell(r, 9)),
                Texto(ws.Cell(r, 10)).ToUpperInvariant(),
                LerNumeroCelula(ws.Cell(r, 11), $"Pagamento da linha {r}: valor", avisos) is { } v ? Math.Round(v, 2) : null,
                Texto(ws.Cell(r, 12)),
                Texto(ws.Cell(r, 13))));
        }

        // Observações embaixo do VALOR TOTAL ROMANEIOS (as quatro linhas mescladas)
        var notas = new List<string>();
        for (int r = totalRomaneios + 1; r <= totalRomaneios + 4; r++)
        {
            var c = ws.Cell(r, 1);
            if (!c.HasFormula && Texto(c) is { Length: > 0 } t) notas.Add(t);
        }

        double? Gravado(int r, int c) => r > 0 && Valor(ws.Cell(r, c)) is { IsNumber: true } v ? v.GetNumber() : null;
        var gravados = new TotaisGravados(Gravado(totalRomaneios, 7), Gravado(totalPagamentos, 11), Gravado(linhaSaldo, 12));

        var dados = new Dados(romaneios, pagamentos, notas);
        var calculado = Totalizar(dados);
        void Conferir(string nome, double? gravado, double conta)
        {
            if (gravado is { } g && Math.Abs(g - conta) > 0.005)
                avisos.Add($"{nome}: a planilha diz {g.ToString("N2", PtBr)}, mas as linhas somam {conta.ToString("N2", PtBr)}. Confira se alguma fórmula foi trocada por valor.");
        }
        Conferir("Total dos romaneios", gravados.APagar, calculado.APagar);
        Conferir("Total de depósitos e pagamentos", gravados.Pagamentos, calculado.Pagamentos);
        Conferir("Créditos e débitos com Livia", gravados.Saldo, calculado.Saldo);

        return new PlanilhaLida(dados, gravados, avisos);
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Fórmula vale o que o Excel gravou da última vez que calculou
    private static XLCellValue Valor(IXLCell c) => c.HasFormula ? c.CachedValue : c.Value;

    private static string Texto(IXLCell c)
    {
        var v = Valor(c);
        if (v.IsBlank) return "";
        if (v.IsText) return v.GetText().Trim();
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        if (v.IsDateTime) return v.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return v.ToString().Trim();
    }

    // Rótulo sem acento, em maiúsculas e com um espaço só: "DEPÓSITOS  E" → "DEPOSITOS E"
    private static string Rotulo(IXLCell c)
    {
        var t = Texto(c).Normalize(NormalizationForm.FormD);
        var sem = new string(t.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(sem.ToUpperInvariant(), @"\s+", " ").Trim();
    }

    private static DateOnly? LerDataCelula(IXLCell c)
    {
        var v = Valor(c);
        if (v.IsDateTime) return DateOnly.FromDateTime(v.GetDateTime());
        if (v.IsNumber && v.GetNumber() is var n && n > 20000 && n < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n));
        if (v.IsText && DateOnly.TryParseExact(v.GetText().Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    private static double? LerNumeroCelula(IXLCell c, string campo, List<string> avisos)
    {
        var v = Valor(c);
        if (v.IsBlank) return null;
        if (v.IsNumber) return v.GetNumber();
        if (v.IsText)
        {
            var t = v.GetText().Trim();
            if (t == "" || t == "-") return null;
            if (double.TryParse(t.Replace("R$", "").Trim(), NumberStyles.Number, PtBr, out var n)) return n;
        }
        avisos.Add($"{campo} \"{Texto(c)}\" não é número e ficou de fora.");
        return null;
    }
}
