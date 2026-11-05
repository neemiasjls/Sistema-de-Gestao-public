using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Romaneio da fazenda (PDF "BENTO ...") lido por inteiro: cabeçalho, cada
/// produto e a marca de funrural. Com "OBSERVAÇÃO: DESC FUNRURAL" é romaneio de
/// cliente (ACE-05); sem ela, são os volumes que pegam na fazenda para embalar no
/// barracão, e viram entrada do controle de volumes embalados.
///
///   CONTROLE...: 133423 EMISSÃO: 29/09/2026 PEDIDO: 000000
///   009 GV-VOLUMES GRANDE VERMELHO CX 170 130.000 22,100.00
///   T O T A L G E R A L... 170 22,100.00
///   OBSERVAÇÃO:
/// </summary>
public static class RomaneioFazendaLeitor
{
    public record Item(string Codigo, string Produto, string Descricao, string Unidade, decimal Quantidade, decimal Valor);

    public record Lido(int Numero, DateOnly Data, int Caixas, decimal Valor, bool DescFunrural, List<Item> Itens);

    private static readonly Regex Cabecalho = new(@"CONTROLE[^\d]{0,8}(\d{3,})\W*EMISS\w*\W*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
    private static readonly Regex Numero = new(@"^\d{1,3}(,\d{3})*(\.\d+)?$");
    private static readonly Regex Codigo = new(@"^\d{3}$");

    public static List<Lido> Ler(byte[] pdf)
    {
        var lista = new List<Lido>();
        int? numero = null; DateOnly data = default;
        var itens = new List<Item>();
        bool funrural = false; int caixas = 0; decimal valor = 0; bool temTotal = false;

        void Fechar()
        {
            if (numero != null && temTotal) lista.Add(new Lido(numero.Value, data, caixas, valor, funrural, itens));
            numero = null; itens = []; funrural = false; caixas = 0; valor = 0; temTotal = false;
        }

        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            foreach (var bruta in AcertoAlfaService.Linhas(page))
            {
                var linha = bruta.Where(t => t != "|" && t != "+").ToList();
                var texto = string.Join(" ", linha);

                var m = Cabecalho.Match(texto);
                if (m.Success)
                {
                    Fechar();
                    numero = int.Parse(m.Groups[1].Value);
                    data = DateOnly.ParseExact(m.Groups[2].Value, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    continue;
                }
                if (numero == null) continue;

                var letras = new string(string.Concat(linha).Where(char.IsLetter).ToArray()).ToUpperInvariant();
                if (letras.StartsWith("TOTALGERAL"))
                {
                    var nums = linha.Where(t => Numero.IsMatch(t)).ToList();
                    if (nums.Count >= 2)
                    {
                        caixas = (int)Num(nums[^2]);
                        valor = Num(nums[^1]);
                        temTotal = true;
                    }
                    continue;
                }
                if (letras.StartsWith("OBSERVA"))
                {
                    if (letras.Contains("FUNRURAL")) funrural = true;
                    continue;
                }

                // Linha de produto: código de 3 dígitos, descrição com o código do
                // produto antes do traço, unidade e os três números do fim
                if (linha.Count >= 6 && Codigo.IsMatch(linha[0]) && linha[1].Contains('-')
                    && Numero.IsMatch(linha[^1]) && Numero.IsMatch(linha[^2]) && Numero.IsMatch(linha[^3]))
                {
                    var descricao = string.Join(" ", linha.Skip(1).Take(linha.Count - 5));
                    var produto = linha[1][..linha[1].IndexOf('-')].Trim().ToUpperInvariant();
                    itens.Add(new Item(linha[0], produto, descricao, linha[^4].ToUpperInvariant(), Num(linha[^3]), Num(linha[^1])));
                }
            }
        Fechar();
        return lista;
    }

    private static decimal Num(string t) => decimal.Parse(t.Replace(",", ""), CultureInfo.InvariantCulture);

    // ───────────────── Produto da fazenda → caixa crua do barracão ─────────────────
    // Inferido dos tipos (NFeImportService.MapaProduto / Tipos de volumes) e do
    // histórico dos controles 312–328, em que a entrada do PAULO é sempre Av:
    //   GV, EV, JV (grande, extra, jumbo vermelho) → Av
    //   GB, EB, JB (grande, extra, jumbo branco)   → Ab
    //   MB (médio branco)                           → Bb, que está fora de uso (avisa)
    // O resto (PVC, dúzia embalada, 10x20, 10x30, médio/pequeno vermelho, casca
    // fina) não é caixa de 12 produtosC de 30 que se embala: não entra e avisa.

    public const int ProdutosCPorCaixa = 12;

    public record Cru(string Campo, string Nome, string? Aviso);

    public static Cru? CruDoProduto(string produto) => produto.Trim().ToUpperInvariant() switch
    {
        "GV" or "EV" or "JV" => new("Av", "Vermelho", null),
        "GB" or "EB" or "JB" => new("Ab", "Branco", null),
        "MB" => new("Bb", "Branco médio", "Branco médio (Bb) está fora de uso desde 30/09/2026: confira esta entrada."),
        _ => null,
    };
}
