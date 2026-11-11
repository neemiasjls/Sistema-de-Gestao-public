using System.Net;
using System.Text;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TransportadoraSystem.Application.Services;

// Envio de e-mail pela API do Brevo. Não dá para usar SMTP: a Render bloqueia
// as portas 25, 465 e 587 no plano gratuito desde setembro de 2025.
//
// Configuração (variáveis de ambiente na Render):
//   Email__BrevoApiKey   chave da API do Brevo
//   Email__Remetente     e-mail verificado como remetente no Brevo
//   Email__SiteUrl       endereço do site, para o link do e-mail (opcional)
public class EmailService(HttpClient http, IConfiguration config, ILogger<EmailService> log)
{
    private string? ApiKey    => config["Email:BrevoApiKey"];
    private string? Remetente => config["Email:Remetente"];
    private string  SiteUrl   => config["Email:SiteUrl"] ?? "https://seu-site.exemplo.com.br";

    public bool Configurado => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Remetente);

    // Devolve null quando mandou; senão, o motivo — para aparecer na tela.
    public async Task<string?> EnviarSenhaProvisoriaAsync(string nome, string email, string senha, bool primeiroAcesso)
    {
        if (!Configurado)
            return "O envio de e-mail ainda não está configurado no servidor.";

        var assunto = primeiroAcesso
            ? "Seu acesso ao sistema da TRX Transportes"
            : "Nova senha provisória — TRX Transportes";

        var abertura = primeiroAcesso
            ? "Seu acesso ao sistema da TRX Transportes foi criado."
            : "Uma nova senha provisória foi gerada para o seu acesso.";

        var texto = $"""
            Olá, {nome}.

            {abertura}

            E-mail: {email}
            Senha provisória: {senha}

            Entre em {SiteUrl}. No primeiro acesso o sistema vai pedir que você crie a sua própria senha; a provisória deixa de valer na hora.
            """;

        static string h(string s) => WebUtility.HtmlEncode(s);
        var html = $"""
            <div style="font-family:Arial,sans-serif;font-size:15px;color:#161d25;max-width:520px">
              <p>Olá, {h(nome)}.</p>
              <p>{h(abertura)}</p>
              <table style="margin:16px 0;border-collapse:collapse">
                <tr><td style="padding:4px 12px 4px 0;color:#687583">E-mail</td><td><b>{h(email)}</b></td></tr>
                <tr><td style="padding:4px 12px 4px 0;color:#687583">Senha provisória</td>
                    <td><code style="font-size:17px;background:#fff8e5;padding:4px 8px;border-radius:4px">{h(senha)}</code></td></tr>
              </table>
              <p><a href="{h(SiteUrl)}" style="background:#213a52;color:#fff;padding:10px 18px;border-radius:8px;text-decoration:none;display:inline-block">Entrar no sistema</a></p>
              <p style="color:#687583">No primeiro acesso o sistema vai pedir que você crie a sua própria senha. A provisória deixa de valer na hora.</p>
            </div>
            """;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = JsonContent.Create(new
                {
                    sender      = new { name = "TRX Transportes", email = Remetente },
                    to          = new[] { new { email, name = nome } },
                    subject     = assunto,
                    htmlContent = html,
                    textContent = texto,
                }),
            };
            req.Headers.Add("api-key", ApiKey);

            var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return null;

            var corpo = await resp.Content.ReadAsStringAsync();
            log.LogError("Brevo recusou o e-mail para {Email}: {Status} {Corpo}", email, (int)resp.StatusCode, corpo);
            return $"O serviço de e-mail recusou o envio ({(int)resp.StatusCode}).";
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao enviar e-mail para {Email}", email);
            return "Não consegui falar com o serviço de e-mail.";
        }
    }

    // ───────────────────────── E-mail com anexos ─────────────────────────
    //
    // Para os clientes (romaneio e boletos da semana). Configuração a mais:
    //   Email__RemetenteNome  nome que aparece como remetente (padrão "TRX Transportes")
    //   Email__DestinoTeste   com ela preenchida, TODO e-mail com anexo vai só para
    //                         esse endereço; os destinatários de verdade aparecem no
    //                         corpo. Tirar a variável é o que libera o envio aos clientes.

    public string  RemetenteEmail => Remetente ?? "";
    public string  RemetenteNome  => string.IsNullOrWhiteSpace(config["Email:RemetenteNome"]) ? "TRX Transportes" : config["Email:RemetenteNome"]!.Trim();
    public string? DestinoTeste   => string.IsNullOrWhiteSpace(config["Email:DestinoTeste"]) ? null : config["Email:DestinoTeste"]!.Trim();
    public bool    ModoTeste      => DestinoTeste != null;

    // Folga abaixo do limite de tamanho do Brevo (o base64 cresce um terço);
    // o .zip da semana tem uns 100 KB
    public const int LimiteAnexosBytes = 10 * 1024 * 1024;

    public record Anexo(string Nome, byte[] Conteudo);
    public record EmailComAnexos(IReadOnlyList<string> Para, string Assunto, string Texto, IReadOnlyList<Anexo> Anexos);

    /// <summary>O que vai de fato para o Brevo, já com o modo de teste aplicado.</summary>
    public record EnvioPreparado(IReadOnlyList<string> Para, IReadOnlyList<string> DestinatariosReais, string Assunto,
        string Texto, string Html, IReadOnlyList<Anexo> Anexos, bool ModoTeste);

    public record ResultadoEnvio(bool Enviado, string? MessageId, string? Erro);

    public EnvioPreparado Preparar(EmailComAnexos e)
    {
        var reais = e.Para.Select(p => p.Trim()).Where(p => p != "").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (DestinoTeste is not string teste)
            return new EnvioPreparado(reais, reais, e.Assunto, e.Texto, Html(e.Texto, null), e.Anexos, false);

        var aviso = $"MODO DE TESTE — este e-mail iria para: {string.Join(", ", reais)}";
        return new EnvioPreparado([teste], reais, $"[TESTE] {e.Assunto}", $"{aviso}\n\n{e.Texto}", Html(e.Texto, aviso), e.Anexos, true);
    }

    /// <summary>
    /// O corpo JSON do POST /v3/smtp/email. Fica separado do envio para dar para
    /// conferir o que iria para o Brevo sem mandar nada.
    /// </summary>
    public string MontarJsonBrevo(EnvioPreparado e) => e.Anexos.Count == 0
        // Sem anexo (Perfil sem boleto, BOL-17): o Brevo não aceita "attachment" vazio
        ? System.Text.Json.JsonSerializer.Serialize(new
        {
            sender      = new { name = RemetenteNome, email = Remetente },
            replyTo     = new { name = RemetenteNome, email = Remetente },
            to          = e.Para.Select(p => new { email = p }).ToArray(),
            subject     = e.Assunto,
            htmlContent = e.Html,
            textContent = e.Texto,
        })
        : System.Text.Json.JsonSerializer.Serialize(new
    {
        sender      = new { name = RemetenteNome, email = Remetente },
        // Remetente Gmail/Hotmail o Brevo troca por "...@NNN.brevosend.com"; a resposta
        // do cliente tem de voltar para o e-mail de verdade
        replyTo     = new { name = RemetenteNome, email = Remetente },
        to          = e.Para.Select(p => new { email = p }).ToArray(),
        subject     = e.Assunto,
        htmlContent = e.Html,
        textContent = e.Texto,
        attachment  = e.Anexos.Select(a => new { name = a.Nome, content = Convert.ToBase64String(a.Conteudo) }).ToArray(),
    });

    public async Task<ResultadoEnvio> EnviarComAnexosAsync(EnvioPreparado e)
    {
        if (!Configurado)
            return new ResultadoEnvio(false, null, "O envio de e-mail ainda não está configurado no servidor (falta a chave do Brevo ou o remetente).");
        if (e.Para.Count == 0)
            return new ResultadoEnvio(false, null, "Nenhum destinatário.");
        if (e.Anexos.Sum(a => (long)a.Conteudo.Length) > LimiteAnexosBytes)
            return new ResultadoEnvio(false, null, "Os anexos passam de 10 MB; o serviço de e-mail não aceita.");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
            {
                Content = new StringContent(MontarJsonBrevo(e), System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("api-key", ApiKey);

            var resp = await http.SendAsync(req);
            var corpo = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                string? id = null;
                try { id = System.Text.Json.JsonDocument.Parse(corpo).RootElement.GetProperty("messageId").GetString(); }
                catch { }
                return new ResultadoEnvio(true, id, null);
            }

            log.LogError("Brevo recusou o e-mail \"{Assunto}\": {Status} {Corpo}", e.Assunto, (int)resp.StatusCode, corpo);
            return new ResultadoEnvio(false, null, TraduzirErroBrevo((int)resp.StatusCode, corpo));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao enviar o e-mail \"{Assunto}\"", e.Assunto);
            return new ResultadoEnvio(false, null, "Não consegui falar com o serviço de e-mail. Tente de novo em instantes.");
        }
    }

    /// <summary>
    /// A recusa do Brevo em português. Remetente não confirmado é o caso que
    /// mais aparece (a mensagem dele fala em "sender" e "valid"/"verified").
    /// </summary>
    public string TraduzirErroBrevo(int status, string corpo)
    {
        var t = (corpo ?? "").ToLowerInvariant();
        if (t.Contains("sender") && (t.Contains("valid") || t.Contains("verif") || t.Contains("not allowed") || t.Contains("authori")))
            return $"O remetente {Remetente} ainda não foi confirmado no Brevo.";
        if (status == 401 || t.Contains("key not found") || t.Contains("unauthorized"))
            return "A chave do Brevo foi recusada; confira a variável Email__BrevoApiKey na Render.";
        if (t.Contains("attachment") || t.Contains("extension"))
            return "O Brevo recusou o anexo.";
        if (t.Contains("email") && t.Contains("valid"))
            return "O Brevo recusou um dos endereços de e-mail; confira os destinatários.";
        return $"O serviço de e-mail recusou o envio ({status}).";
    }

    /// <summary>
    /// Texto → HTML: um &lt;p&gt; por parágrafo. Linhas seguidas com " | " (tabelinha da queda da Girassol, BOL-17)
    /// viram uma &lt;table&gt; simples: primeira linha em negrito, números à direita. Sem " | ", igual a sempre.
    /// </summary>
    public static string Html(string texto, string? avisoTeste)
    {
        static string h(string s) => WebUtility.HtmlEncode(s);
        static string Paragrafo(string p)
        {
            var linhas = p.Trim().Split('\n');
            if (!linhas.Any(l => l.Contains(" | "))) return $"<p>{h(p.Trim()).Replace("\n", "<br>")}</p>";
            var sb = new StringBuilder();
            var i = 0;
            while (i < linhas.Length)
            {
                var tabela = linhas[i].Contains(" | ");
                var grupo = linhas.Skip(i).TakeWhile(l => l.Contains(" | ") == tabela).ToList();
                i += grupo.Count;
                if (!tabela) { sb.Append($"<p style=\"margin:8px 0\">{string.Join("<br>", grupo.Select(h))}</p>"); continue; }
                sb.Append("<table style=\"border-collapse:collapse;margin:4px 0 8px\">");
                for (var r = 0; r < grupo.Count; r++)
                {
                    var celulas = grupo[r].Split(" | ");
                    sb.Append("<tr>");
                    for (var k = 0; k < celulas.Length; k++)
                    {
                        var estilo = "border:1px solid #c9d2dc;padding:4px 8px" + (k > 0 ? ";text-align:right" : "") + (r == 0 ? ";font-weight:bold;background:#f1f4f7" : "");
                        sb.Append($"<td style=\"{estilo}\">{h(celulas[k].Trim())}</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }
            return sb.ToString();
        }
        var paragrafos = texto.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(Paragrafo);
        var faixa = avisoTeste == null ? ""
            : $"<p style=\"background:#fff3cd;border:1px solid #e0b000;padding:8px 12px;border-radius:6px\"><b>{h(avisoTeste)}</b></p>";
        return $"<div style=\"font-family:Arial,sans-serif;font-size:15px;color:#161d25;max-width:620px\">{faixa}{string.Join("", paragrafos)}</div>";
    }
}
