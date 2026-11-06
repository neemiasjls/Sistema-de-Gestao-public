using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TransportadoraSystem.Application.Services;

// Leitura da conta PJ no Sicoob (API Conta Corrente v4): extrato e saldo.
// O aplicativo do Sicoob foi criado só com escopos de consulta e de boletos; nada
// aqui paga, transfere ou devolve Pix (PEN-37, BOL-10).
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

    private string? ClientId => config["Sicoob:ClientId"];
    private string? Conta    => new string((config["Sicoob:Conta"] ?? "").Where(char.IsDigit).ToArray());
    private string  UrlToken => config["Sicoob:UrlToken"] ?? UrlTokenPadrao;

    // O token vale alguns minutos e serve para todas as chamadas até lá
    private static readonly SemaphoreSlim TravaToken = new(1, 1);
    private static string? _token;
    private static DateTime _tokenVenceEm;

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

    // Erro vira texto para a tela; null no lugar do extrato quando falhou
    public async Task<(Extrato? Extrato, string? Erro)> ExtratoAsync(DateOnly inicio, DateOnly fim, CancellationToken ct = default)
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

    private async Task<(string? Token, string? Erro)> TokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _tokenVenceEm) return (_token, null);
        await TravaToken.WaitAsync(ct);
        try
        {
            if (_token != null && DateTime.UtcNow < _tokenVenceEm) return (_token, null);

            using var req = new HttpRequestMessage(HttpMethod.Post, UrlToken)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"]  = ClientId!,
                    ["scope"]      = Escopos,
                }),
            };
            using var resp = await http.SendAsync(req, ct);
            var corpo = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Sicoob token: {Status} {Corpo}", (int)resp.StatusCode, Curto(corpo));
                return (null, "O Sicoob recusou o acesso. Confira o Client ID, o certificado e se o aplicativo está aprovado.");
            }
            using var doc = JsonDocument.Parse(corpo);
            _token = Texto(doc.RootElement, "access_token");
            var segundos = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 300;
            _tokenVenceEm = DateTime.UtcNow.AddSeconds(Math.Max(30, segundos - 30));
            return string.IsNullOrEmpty(_token) ? (null, "O Sicoob não devolveu o acesso.") : (_token, null);
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
                _token = null;
                log.LogWarning("Sicoob {Url}: {Status} {Corpo}", SemConta(url), (int)resp.StatusCode, Curto(corpo));
                return (null, "O Sicoob não liberou a consulta. Confira se o aplicativo está aprovado com o escopo de consulta.");
            }
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Sicoob {Url}: {Status} {Corpo}", SemConta(url), (int)resp.StatusCode, Curto(corpo));
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
