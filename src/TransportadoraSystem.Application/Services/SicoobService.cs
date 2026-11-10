using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TransportadoraSystem.Application.Services;

// Conta PJ no Sicoob: extrato e saldo (Conta Corrente v4); consulta e emissão do boleto a receber
// (Cobrança Bancária v3). O aplicativo do Sicoob foi criado só com escopos de consulta e de boletos;
// nada aqui paga, transfere ou devolve Pix (PEN-37, BOL-10).
//
// Configuração (variáveis de ambiente na Render):
//   Sicoob__ClientId            Client ID do aplicativo no Sicoob Developers
//   Sicoob__CertificadoBase64   o .pfx do e-CNPJ A1 em base64
//   Sicoob__CertificadoSenha    a senha do .pfx
//   Sicoob__Conta               conta corrente, só números com o dígito (538868)
//   Sicoob__UrlToken            opcional; o padrão é o do Sicoob
public class SicoobService(HttpClient http, IConfiguration config, ILogger<SicoobService> log)
{
    private const string UrlTokenPadrao = "https://auth.sicoob.com.br/auth/realms/cooperado/protocol/openid-connect/token";
    private const string UrlContaCorrente = "https://api.sicoob.com.br/conta-corrente/v4";
    private const string Escopos = "cco_consulta";
    private const string UrlCobranca = "https://api.sicoob.com.br/cobranca-bancaria/v3";
    // Todas as da Cobrança do aplicativo: o Sicoob recusou ("Not registered to plan") com só a de consulta
    private const string EscoposCobranca = "boletos_consulta boletos_inclusao boletos_alteracao webhooks_inclusao webhooks_consulta webhooks_alteracao";
    private string? NumeroCliente => new string((config["Sicoob:NumeroCliente"] ?? "").Where(char.IsDigit).ToArray()) is { Length: > 0 } n ? n : null;
    private string Modalidade => config["Sicoob:CodigoModalidade"] ?? "1";

    private string? ClientId => config["Sicoob:ClientId"];
    private string? Conta    => new string((config["Sicoob:Conta"] ?? "").Where(char.IsDigit).ToArray());
    private string  UrlToken => config["Sicoob:UrlToken"] ?? UrlTokenPadrao;

    // O token vale alguns minutos e serve para todas as chamadas até lá
    private static readonly SemaphoreSlim TravaToken = new(1, 1);
    // Um token por conjunto de escopos: a cobrança recusada não derruba o extrato
    private static readonly Dictionary<string, (string Token, DateTime VenceEm)> Tokens = [];
    private static string? TokenGuardado(string escopos)
    {
        lock (Tokens) return Tokens.TryGetValue(escopos, out var t) && DateTime.UtcNow < t.VenceEm ? t.Token : null;
    }

    public string? FaltaConfigurar()
    {
        var falta = new List<string>();
        if (string.IsNullOrWhiteSpace(ClientId)) falta.Add("Client ID");
        if (string.IsNullOrWhiteSpace(config["Sicoob:CertificadoBase64"])) falta.Add("certificado");
        if (string.IsNullOrWhiteSpace(Conta)) falta.Add("conta");
        return falta.Count == 0 ? null : $"Falta configurar no servidor: {string.Join(", ", falta)}.";
    }

    // O certificado vai na conexão (mTLS): sem ele o Sicoob nem responde
    public static HttpMessageHandler CriarHandler(IConfiguration config, ILogger log)
    {
        var handler = new HttpClientHandler();
        var b64 = config["Sicoob:CertificadoBase64"];
        if (string.IsNullOrWhiteSpace(b64)) return handler;
        try
        {
            var cert = new X509Certificate2(Convert.FromBase64String(b64.Trim()), config["Sicoob:CertificadoSenha"],
                X509KeyStorageFlags.EphemeralKeySet);
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ClientCertificates.Add(cert);
            log.LogInformation("Sicoob: certificado carregado, vence em {Vence:dd/MM/yyyy}", cert.NotAfter);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Senha errada ou base64 cortado; a mensagem não leva nada do certificado
            log.LogError("Sicoob: não consegui abrir o certificado ({Tipo})", ex.GetType().Name);
        }
        return handler;
    }

    public record Lancamento(string Id, DateOnly? Data, bool Credito, decimal Valor, string Descricao,
                             string Documento, string CpfCnpj, string Complemento);

    public record Extrato(decimal? Saldo, decimal? SaldoLimite, decimal? SaldoBloqueado, List<Lancamento> Lancamentos);

    // Extrato guardado por período: consultar muitas vezes seguidas dá 429 do Sicoob (visto em 06/11/2026).
    // Vale 10 minutos; se o banco recusar, usa o último lido do mesmo período (até 12 horas).
    private static readonly Dictionary<(DateOnly, DateOnly), (DateTime Lido, Extrato Extrato)> Guardados = [];
    private static readonly SemaphoreSlim TravaExtrato = new(1, 1);
    private const string NaoLiberou = "O Sicoob não liberou a consulta. Confira se o aplicativo está aprovado com o escopo de consulta.";

    // Erro vira texto para a tela; null no lugar do extrato quando falhou
    public async Task<(Extrato? Extrato, string? Erro)> ExtratoAsync(DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        await TravaExtrato.WaitAsync(ct);
        try
        {
            var chave = (inicio, fim);
            if (Guardados.TryGetValue(chave, out var g) && DateTime.UtcNow - g.Lido < TimeSpan.FromMinutes(10))
                return (g.Extrato, null);
            var (extrato, erro) = await BuscarExtratoAsync(inicio, fim, ct);
            // 401/403 com token vencido (visto em 06/11/2026): o token foi descartado; tenta uma vez com outro
            if (extrato == null && erro == NaoLiberou)
                (extrato, erro) = await BuscarExtratoAsync(inicio, fim, ct);
            if (extrato != null)
            {
                foreach (var velho in Guardados.Where(x => DateTime.UtcNow - x.Value.Lido > TimeSpan.FromHours(12)).Select(x => x.Key).ToList())
                    Guardados.Remove(velho);
                Guardados[chave] = (DateTime.UtcNow, extrato);
                return (extrato, null);
            }
            if (Guardados.TryGetValue(chave, out g) && DateTime.UtcNow - g.Lido < TimeSpan.FromHours(12))
            {
                log.LogWarning("Sicoob recusou o extrato ({Erro}); usando o lido às {Hora:HH:mm} UTC", erro, g.Lido);
                return (g.Extrato, null);
            }
            return (null, erro);
        }
        finally { TravaExtrato.Release(); }
    }

    private async Task<(Extrato? Extrato, string? Erro)> BuscarExtratoAsync(DateOnly inicio, DateOnly fim, CancellationToken ct)
    {
        var falta = FaltaConfigurar();
        if (falta != null) return (null, falta);

        var (token, erroToken) = await TokenAsync(ct);
        if (token == null) return (null, erroToken);

        var lancamentos = new List<Lancamento>();
        decimal? saldo = null, limite = null, bloqueado = null;

        // A API devolve um mês por chamada: período que cruza o mês vira várias
        for (var mes = new DateOnly(inicio.Year, inicio.Month, 1); mes <= fim; mes = mes.AddMonths(1))
        {
            var diaIni = mes == new DateOnly(inicio.Year, inicio.Month, 1) ? inicio.Day : 1;
            var ultimo = mes.AddMonths(1).AddDays(-1);
            var diaFim = fim < ultimo ? fim.Day : ultimo.Day;
            var url = $"{UrlContaCorrente}/extrato/{mes.Month}/{mes.Year}?diaInicial={diaIni}&diaFinal={diaFim}" +
                      $"&agruparCNAB=false&numeroContaCorrente={Conta}";

            var (json, erro) = await GetAsync(url, token, ct);
            if (json == null) return (null, erro);

            var raiz = Resultado(json.RootElement);
            saldo = Valor(raiz, "saldoAtual") ?? saldo;
            limite = Valor(raiz, "saldoLimite") ?? limite;
            bloqueado = Valor(raiz, "saldoBloqueado") ?? bloqueado;
            if (raiz.TryGetProperty("transacoes", out var tr) && tr.ValueKind == JsonValueKind.Array)
                lancamentos.AddRange(tr.EnumerateArray().Select(LerLancamento));
        }

        return (new Extrato(saldo, limite, bloqueado,
            lancamentos.OrderByDescending(l => l.Data).ThenByDescending(l => l.Id).ToList()), null);
    }

    private static Lancamento LerLancamento(JsonElement t)
    {
        var valor = Valor(t, "valor") ?? 0;
        var tipo = Texto(t, "tipo").ToUpperInvariant();
        // Tipo "CREDITO"/"DEBITO" (ou C/D); sem tipo, o sinal do valor decide
        var credito = tipo.StartsWith('C') || (!tipo.StartsWith('D') && valor > 0);
        return new Lancamento(
            Texto(t, "transactionId"), DataDe(Texto(t, "data")), credito, Math.Abs(valor),
            Texto(t, "descricao"), Texto(t, "numeroDocumento"), Texto(t, "cpfCnpj"), Texto(t, "descInfComplementar"));
    }

    // ───────────── Cobrança: situação de um boleto (só consulta) ─────────────

    public record SituacaoBoleto(string NossoNumero, string? Situacao, bool Liquidado, DateOnly? DataLiquidacao, decimal? ValorPago);

    /// <summary>
    /// Consulta o boleto pelo nosso número ("670-9"): tenta com o dígito (6709) e, se o banco não achar, sem ele (670).
    /// Liquidado = situação "Liquidado" ou histórico com liquidação; a data vem do histórico.
    /// </summary>
    public async Task<(SituacaoBoleto? Situacao, string? Erro)> ConsultarBoletoAsync(string nossoNumero, decimal? valorEsperado, CancellationToken ct = default)
    {
        if (FaltaConfigurar() is { } falta) return (null, falta);
        if (NumeroCliente == null) return (null, "Falta Sicoob__NumeroCliente (código do beneficiário) no Render.");
        var digitos = new string(nossoNumero.Where(char.IsDigit).ToArray());
        if (digitos.Length == 0) return (null, "Boleto sem nosso número.");
        var tentativas = new List<string> { digitos };
        if (nossoNumero.Contains('-') && digitos.Length > 1) tentativas.Add(digitos[..^1]);

        string? erro = null;
        foreach (var n in tentativas)
        {
            var (token, erroToken) = await TokenAsync(ct, EscoposCobranca);
            if (token == null) return (null, erroToken);
            var url = $"{UrlCobranca}/boletos?numeroCliente={NumeroCliente}&codigoModalidade={Modalidade}&nossoNumero={n}";
            var (json, e) = await GetAsync(url, token, ct);
            if (json == null)
            {
                if (e == NaoLiberou) return (null, "O Sicoob não liberou a consulta de boletos para este aplicativo (API de Cobrança Bancária).");
                erro = e; continue;
            }
            using (json)
            {
                var r = Resultado(json.RootElement);
                if (r.ValueKind == JsonValueKind.Array) r = r.EnumerateArray().FirstOrDefault();
                // Resposta vazia (204 vira {}) ou de outro título não serve: confere nosso número e valor
                if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("nossoNumero", out var nn)) { erro = "O Sicoob não achou o boleto."; continue; }
                var devolvido = new string((nn.ValueKind == JsonValueKind.Number ? nn.GetRawText() : nn.GetString() ?? "").Where(char.IsDigit).ToArray());
                var valorBanco = Valor(r, "valor");
                if (devolvido.TrimStart('0') != n.TrimStart('0') || (valorEsperado != null && valorBanco != null && Math.Abs(valorBanco.Value - valorEsperado.Value) > 0.01m))
                {
                    log.LogWarning("Sicoob boleto {Nosso}: veio outro título ({Devolvido}, {Valor})", n, devolvido, valorBanco);
                    erro = "O Sicoob devolveu outro boleto para esse número; confira no internet banking."; continue;
                }
                log.LogInformation("Sicoob boleto {Nosso}: {Corpo}", n, Curto(r.GetRawText()));
                var situacao = Texto(r, "situacaoBoleto");
                DateOnly? data = null;
                if (r.TryGetProperty("listaHistorico", out var h) && h.ValueKind == JsonValueKind.Array)
                    foreach (var item in h.EnumerateArray())
                        if ((Texto(item, "descricaoHistorico") + " " + Texto(item, "tipoHistorico")).Contains("iquida", StringComparison.OrdinalIgnoreCase))
                            data = DataDe(Texto(item, "dataHistorico")) ?? data;
                decimal? pago = null;
                foreach (var campo in new[] { "valorPago", "valorRecebido", "valorLiquidado", "valorLiquidacao" })
                    pago ??= Valor(r, campo);
                var liquidado = situacao.Contains("iquida", StringComparison.OrdinalIgnoreCase) || data != null;
                return (new SituacaoBoleto(nossoNumero, situacao, liquidado, data, pago), null);
            }
        }
        return (null, erro ?? "O Sicoob não achou o boleto.");
    }

    // ───────────── Cobrança: emitir o boleto a receber (CD e Girassol) ─────────────

    public record BoletoEmitido(string NossoNumero, string LinhaDigitavel, string CodigoBarras, byte[]? Pdf);

    /// <summary>
    /// POST /boletos com um boleto (objeto único, BoletoMontador.CorpoSicoob). Cobrança REAL: só chamar
    /// depois da confirmação do Admin para este boleto. Incerto = o pedido saiu e a resposta não voltou
    /// (o boleto pode ter sido registrado): conferir no Sicoobnet antes de tentar de novo.
    /// </summary>
    public async Task<(BoletoEmitido? Boleto, string? Erro, bool Incerto)> IncluirBoletoAsync(BoletoMontador.Boleto b, CancellationToken ct = default)
    {
        if (FaltaConfigurar() is { } falta) return (null, falta, false);
        if (NumeroCliente == null || !long.TryParse(NumeroCliente, out var numeroCliente))
            return (null, "Falta Sicoob__NumeroCliente (código do beneficiário) no Render.", false);
        if (!long.TryParse(Conta, out var conta)) return (null, "Conta do Sicoob inválida no Render.", false);
        var (token, erroToken) = await TokenAsync(ct, EscoposCobranca);
        if (token == null) return (null, erroToken, false);

        var corpo = JsonSerializer.Serialize(BoletoMontador.CorpoSicoob(b, numeroCliente, conta));
        HttpResponseMessage resp;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{UrlCobranca}/boletos")
            {
                Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("client_id", ClientId);
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogError("Sicoob emissão {Cliente} {Seu}: sem resposta {Tipo} {Msg}", b.Cliente, b.SeuNumero, ex.GetType().Name, ex.Message);
            return (null, "O Sicoob não respondeu. O boleto pode ter sido registrado: confira no Sicoobnet (Cobrança → Boletos) antes de tentar de novo.", true);
        }
        using (resp)
        {
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                lock (Tokens) Tokens.Remove(EscoposCobranca);
                log.LogWarning("Sicoob emissão: {Status} {Corpo}", (int)resp.StatusCode, Curto(texto));
                return (null, "O Sicoob não liberou a emissão de boletos para este aplicativo (escopo boletos_inclusao).", false);
            }
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Sicoob emissão {Cliente} {Seu}: {Status} {Corpo}", b.Cliente, b.SeuNumero, (int)resp.StatusCode, Curto(texto));
                if ((int)resp.StatusCode == 429)
                    return (null, "O Sicoob pediu um tempo (muitos pedidos seguidos). Tente de novo em alguns minutos.", false);
                // 400/406: o banco recusou e nada foi registrado; todas as mensagens dele vão para a tela
                var msgs = MensagensSicoob(texto);
                if (msgs.Count > 0) return (null, $"O Sicoob recusou o boleto: {string.Join(" · ", msgs)}", false);
                if ((int)resp.StatusCode >= 500)
                    return (null, $"O Sicoob deu erro ({(int)resp.StatusCode}). O boleto pode ter sido registrado: confira no Sicoobnet antes de tentar de novo.", true);
                return (null, $"O Sicoob recusou o boleto ({(int)resp.StatusCode}).", false);
            }
            try
            {
                using var doc = JsonDocument.Parse(texto);
                var r = Resultado(doc.RootElement);
                var nosso = Texto(r, "nossoNumero");
                if (nosso == "") throw new JsonException("sem nossoNumero");
                byte[]? pdf = null;
                var b64 = Texto(r, "pdfBoleto");
                if (b64 != "") try { pdf = Convert.FromBase64String(b64); } catch (FormatException) { pdf = null; }
                log.LogInformation("Sicoob emissão {Cliente} {Seu}: nosso número {Nosso}", b.Cliente, b.SeuNumero, nosso);
                return (new BoletoEmitido(nosso, Texto(r, "linhaDigitavel"), Texto(r, "codigoBarras"), pdf), null, false);
            }
            catch (JsonException ex)
            {
                log.LogError("Sicoob emissão {Cliente} {Seu}: resposta {Status} ilegível {Msg} {Corpo}", b.Cliente, b.SeuNumero, (int)resp.StatusCode, ex.Message, Curto(texto));
                return (null, "O Sicoob aceitou o pedido, mas não consegui ler a resposta. Confira o boleto no Sicoobnet antes de tentar de novo.", true);
            }
        }
    }

    // ───────────── Cobrança: baixar o boleto pago por fora (Pix, BOL-14) ─────────────

    /// <summary>
    /// POST /boletos/{nossoNumero}/baixar com {numeroCliente, codigoModalidade}. Ação REAL no banco: só chamar
    /// depois da confirmação do Admin para estes boletos. O nosso número vai com o dígito no fim ("669-6" → 6696),
    /// como o Sicoob devolveu na emissão (6711 → 671-1). 204 = baixado. Recusado (400/406) = mensagens do banco.
    /// Incerto = o pedido saiu e a resposta não voltou: conferir no Sicoobnet.
    /// </summary>
    public async Task<(bool Baixou, string? Erro, bool Incerto)> BaixarBoletoAsync(string nossoNumero, CancellationToken ct = default)
    {
        if (FaltaConfigurar() is { } falta) return (false, falta, false);
        if (NumeroCliente == null || !long.TryParse(NumeroCliente, out var numeroCliente))
            return (false, "Falta Sicoob__NumeroCliente (código do beneficiário) no Render.", false);
        var digitos = new string(nossoNumero.Where(char.IsDigit).ToArray());
        if (!long.TryParse(digitos, out var nosso)) return (false, "Boleto sem nosso número.", false);
        if (!int.TryParse(Modalidade, out var modalidade)) modalidade = 1;
        var (token, erroToken) = await TokenAsync(ct, EscoposCobranca);
        if (token == null) return (false, erroToken, false);

        var corpo = JsonSerializer.Serialize(new { numeroCliente, codigoModalidade = modalidade });
        HttpResponseMessage resp;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{UrlCobranca}/boletos/{nosso}/baixar")
            {
                Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("client_id", ClientId);
            resp = await http.SendAsync(req, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogError("Sicoob baixa {Nosso}: sem resposta {Tipo} {Msg}", nosso, ex.GetType().Name, ex.Message);
            return (false, "O Sicoob não respondeu: não sei se baixou. Confira no Sicoobnet (Cobrança → Boletos) antes de tentar de novo.", true);
        }
        using (resp)
        {
            var texto = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
            {
                log.LogInformation("Sicoob baixa {Nosso}: {Status}", nosso, (int)resp.StatusCode);
                return (true, null, false);
            }
            log.LogWarning("Sicoob baixa {Nosso}: {Status} {Corpo}", nosso, (int)resp.StatusCode, Curto(texto));
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                lock (Tokens) Tokens.Remove(EscoposCobranca);
                return (false, "O Sicoob não liberou a baixa de boletos para este aplicativo (escopo boletos_alteracao).", false);
            }
            if ((int)resp.StatusCode == 429)
                return (false, "O Sicoob pediu um tempo (muitos pedidos seguidos). Tente de novo em alguns minutos.", false);
            var msgs = MensagensSicoob(texto);
            if ((int)resp.StatusCode >= 500)
                return (false, $"O Sicoob deu erro ({(int)resp.StatusCode}){(msgs.Count > 0 ? ": " + string.Join(" · ", msgs) : "")}. Não sei se baixou: confira no Sicoobnet antes de tentar de novo.", true);
            if (msgs.Count > 0) return (false, $"O Sicoob recusou a baixa: {string.Join(" · ", msgs)}", false);
            return (false, $"O Sicoob recusou a baixa ({(int)resp.StatusCode}).", false);
        }
    }

    private static List<string> MensagensSicoob(string corpo)
    {
        var l = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            if (doc.RootElement.TryGetProperty("mensagens", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray())
                {
                    var t = Texto(m, "mensagem"); var c = Texto(m, "codigo");
                    if (t != "") l.Add(c == "" ? t : $"{t} ({c})");
                }
            if (l.Count == 0 && Texto(doc.RootElement, "mensagem") is { Length: > 0 } msg) l.Add(msg);
        }
        catch (JsonException) { }
        return l;
    }

    private async Task<(string? Token, string? Erro)> TokenAsync(CancellationToken ct, string escopos = Escopos)
    {
        if (TokenGuardado(escopos) is { } guardado) return (guardado, null);
        await TravaToken.WaitAsync(ct);
        try
        {
            if (TokenGuardado(escopos) is { } guardado2) return (guardado2, null);

            using var req = new HttpRequestMessage(HttpMethod.Post, UrlToken)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"]  = ClientId!,
                    ["scope"]      = escopos,
                }),
            };
            using var resp = await http.SendAsync(req, ct);
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Sicoob token: {Status} {Corpo}", (int)resp.StatusCode, Curto(corpo));
                return (null, escopos == Escopos
                    ? "O Sicoob recusou o acesso. Confira o Client ID, o certificado e se o aplicativo está aprovado."
                    : "O Sicoob recusou a consulta de boletos: no portal do desenvolvedor, o aplicativo precisa ter a API de Cobrança Bancária (só consulta) liberada.");
            }
            using var doc = JsonDocument.Parse(corpo);
            var token = Texto(doc.RootElement, "access_token");
            var segundos = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300;
            if (string.IsNullOrEmpty(token)) return (null, "O Sicoob não devolveu o acesso.");
            lock (Tokens) Tokens[escopos] = (token, DateTime.UtcNow.AddSeconds(Math.Max(30, segundos - 30)));
            return (token, null);
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning("Sicoob token: falha de conexão {Msg}", ex.Message);
            return (null, "Não consegui falar com o Sicoob. Se persistir, o certificado pode não ter sido aceito.");
        }
        finally { TravaToken.Release(); }
    }

    private async Task<(JsonDocument? Json, string? Erro)> GetAsync(string url, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("client_id", ClientId);
            using var resp = await http.SendAsync(req, ct);
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                lock (Tokens) foreach (var k in Tokens.Where(t => t.Value.Token == token).Select(t => t.Key).ToList()) Tokens.Remove(k);
                log.LogWarning("Sicoob {Url}: {Status} {Corpo}", SemConta(url), (int)resp.StatusCode, Curto(corpo));
                return (null, NaoLiberou);
            }
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Sicoob {Url}: {Status} {Corpo}", SemConta(url), (int)resp.StatusCode, Curto(corpo));
                if ((int)resp.StatusCode == 429)
                    return (null, "O Sicoob pediu um tempo (muitas consultas seguidas). Tente de novo em alguns minutos.");
                return (null, MensagemSicoob(corpo) ?? $"O Sicoob respondeu com erro ({(int)resp.StatusCode}).");
            }
            return (JsonDocument.Parse(string.IsNullOrWhiteSpace(corpo) ? "{}" : corpo), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            log.LogWarning("Sicoob {Url}: {Tipo} {Msg}", SemConta(url), ex.GetType().Name, ex.Message);
            return (null, "Não consegui ler a resposta do Sicoob. Tente de novo em instantes.");
        }
    }

    // A resposta às vezes vem embrulhada em "resultado"
    private static JsonElement Resultado(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("resultado", out var r) && r.ValueKind == JsonValueKind.Object ? r : e;

    private static string? MensagemSicoob(string corpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(corpo);
            var r = doc.RootElement;
            if (r.TryGetProperty("mensagens", out var ms) && ms.ValueKind == JsonValueKind.Array)
                foreach (var m in ms.EnumerateArray()) { var t = Texto(m, "mensagem"); if (t != "") return $"Sicoob: {t}"; }
            var msg = Texto(r, "mensagem");
            return msg == "" ? null : $"Sicoob: {msg}";
        }
        catch (JsonException) { return null; }
    }

    private static string Texto(JsonElement e, string nome) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : v.ToString()
            : "";

    // Os valores vêm como texto ("1234.56"); às vezes como número
    private static decimal? Valor(JsonElement e, string nome)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(nome, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n)) return n;
        var s = v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() : null;
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Contains(',') && s.Contains('.')) s = s.Replace(".", "").Replace(',', '.');
        else if (s.Contains(',')) s = s.Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static DateOnly? DataDe(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return DateOnly.FromDateTime(dt);
        return DateOnly.TryParseExact(s, ["dd/MM/yyyy", "yyyyMMdd", "ddMMyyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    private static string Curto(string s) => s.Length > 300 ? s[..300] : s;
    private static string SemConta(string url) => url.Split('?')[0];
}
