using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Carga salva guarda, em cada NF, se o DANFE já estava no arquivo de notas NA HORA em que o XML
/// foi lido ("temPdf"). Como os PDFs sobem depois dos XMLs, quase sempre ficava "false" para sempre
/// e a tela mostrava "PDF ainda não guardado" com o PDF guardado. Ao abrir a carga, o valor é
/// conferido de novo no arquivo de notas.
/// </summary>
public static class PdfNotasCarga
{
    static readonly JsonSerializerOptions Escrita = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Chaves de acesso (44 dígitos) das NFs que aparecem em qualquer ponto do payload.</summary>
    public static HashSet<string> Chaves(string? payloadJson)
    {
        var r = new HashSet<string>(StringComparer.Ordinal);
        var raiz = Ler(payloadJson);
        if (raiz != null) foreach (var nf in Notas(raiz)) r.Add(ChaveDe(nf)!);
        return r;
    }

    /// <summary>
    /// Devolve o payload com "temPdf" de cada NF igual ao arquivo de notas. Payload inválido, sem NF
    /// com chave ou sem nada a mudar volta exatamente como veio.
    /// </summary>
    public static string Atualizar(string payloadJson, IReadOnlySet<string> chavesComPdf)
    {
        var raiz = Ler(payloadJson);
        if (raiz == null) return payloadJson;

        var mudou = false;
        foreach (var nf in Notas(raiz))
        {
            var tem = chavesComPdf.Contains(ChaveDe(nf)!);
            var atual = nf["temPdf"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : (bool?)null;
            if (atual == tem) continue;
            nf["temPdf"] = tem;
            mudou = true;
        }
        return mudou ? raiz.ToJsonString(Escrita) : payloadJson;
    }

    static JsonNode? Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json); } catch (JsonException) { return null; }
    }

    // NF da carga = objeto com "numero" e "chave" de 44 dígitos (NfDetalhe), onde quer que esteja
    // (itens[].nfsDetalhadas e, se houver, cópias dentro do resultado).
    static List<JsonObject> Notas(JsonNode raiz)
    {
        var achadas = new List<JsonObject>();
        var pilha = new Stack<JsonNode>();
        pilha.Push(raiz);
        while (pilha.Count > 0)
        {
            switch (pilha.Pop())
            {
                case JsonObject o:
                    if (o.ContainsKey("numero") && ChaveDe(o) != null) achadas.Add(o);
                    foreach (var (_, filho) in o) if (filho != null) pilha.Push(filho);
                    break;
                case JsonArray a:
                    foreach (var filho in a) if (filho != null) pilha.Push(filho);
                    break;
            }
        }
        return achadas;
    }

    static string? ChaveDe(JsonObject o) =>
        o["chave"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length == 44 && s.All(char.IsAsciiDigit) ? s : null;
}
