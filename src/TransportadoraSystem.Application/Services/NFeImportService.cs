using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

// ──── Models ────

public class NFeData
{
    public string NumeroNF { get; set; } = "";
    public string Chave { get; set; } = "";
    public DateTime? DataEmissao { get; set; }
    public string EmitOriginal { get; set; } = "";
    public string EmitNorm { get; set; } = "";
    public string CidadeEmit { get; set; } = "";
    public string CnpjEmit { get; set; } = "";   // usado na chave de agrupamento (igual ao FRETE.py)
    public string IeEmit { get; set; } = "";      // idem
    public string DestOriginal { get; set; } = "";
    public string DestNorm { get; set; } = "";
    public string CidadeDestino { get; set; } = "";
    public string Uf { get; set; } = "";
    public decimal? ValorNF { get; set; }
    public decimal? Funrural { get; set; }
    public double PesoKg { get; set; }
    public string Placa { get; set; } = "";
    public string Motorista { get; set; } = "";
    public List<ProdutoNFe> Produtos { get; set; } = new();
}

public class ProdutoNFe
{
    public string Label { get; set; } = "";
    public string Original { get; set; } = "";   // xProd como veio na nota
    public double QCom { get; set; }
    public double VUnCom { get; set; }
}

/// <summary>Uma linha de nota envolvida num conflito de tipo de volume.</summary>
public class OcorrenciaTipo
{
    public string NF { get; set; } = "";
    public string ProdutoNota { get; set; } = "";
    public string Tipo { get; set; } = "";
    public double Quantidade { get; set; }
    public double Preco { get; set; }
}

/// <summary>
/// Tipo de volume que não fecha dentro de um cliente: o mesmo tipo com dois
/// preços, ou vermelho mais barato que o branco equivalente. Quase sempre é
/// quem emitiu a nota que escreveu o tipo errado.
/// </summary>
public class ConflitoTipo
{
    public string Cliente { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string? Sugestao { get; set; }
    // A linha que provavelmente é a divergente, para a tela destacá-la.
    public string? SugestaoNF { get; set; }
    public string? SugestaoProduto { get; set; }
    public double? SugestaoPreco { get; set; }
    public List<OcorrenciaTipo> Ocorrencias { get; set; } = [];
}

/// <summary>Troca feita na tela: "a linha tal da NF tal é, na verdade, este tipo".</summary>
public record CorrecaoTipo(string NF, string ProdutoNota, double Preco, string NovoTipo);

// Produto de nota de cliente que não cai em nenhum tipo: a tela pergunta o nome antes de gerar (NFE-09)
public record ProdutoSemTipo(string Cliente, string NF, string Produto, double Quantidade, double Preco);

public class NFeNFInfo
{
    public string NF { get; set; } = "";
    public decimal? Funrural { get; set; }
    public decimal? VNF { get; set; }
}

public class GrupoCliente
{
    public string DestOriginal { get; set; } = "";
    public DateTime? DataEmissao { get; set; }
    public string Placa { get; set; } = "";
    public string Motorista { get; set; } = "";
    public List<NFeNFInfo> NFs { get; set; } = new();
    public Dictionary<string, (double QCom, double VUnCom)> ProdutosTotal { get; set; } = new();
    // Cada produto como veio na nota: vira linha do romaneio com o nome da nota (NFE-09)
    public List<(string NF, string Nome, string Label, double QCom, double VUnCom)> Itens { get; set; } = new();
}

public class PlanilhaGerada
{
    public string NomeArquivo { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}

public class PlanilhasResult
{
    public List<string> ClientesEncontrados { get; set; } = new();
    public List<string> ArquivosGerados { get; set; } = new();
    public List<PlanilhaGerada> Arquivos { get; set; } = new();   // arquivos individuais (sem zip)
    public byte[] ZipBytes { get; set; } = [];
    public List<string> Avisos { get; set; } = new();
    public string? Erro { get; set; }
    public List<ProdutoSemTipo> SemTipo { get; set; } = new();
}

public class NFeFreteItem
{
    public string NumeroNF       { get; set; } = "";  // NFs do grupo, separadas por vírgula
    public string CnpjEmit       { get; set; } = "";
    public string IeEmit         { get; set; } = "";
    public string Emitente       { get; set; } = "";
    public string CidadeEmitente { get; set; } = "";
    public string Destinatario   { get; set; } = "";
    public string CidadeDestino  { get; set; } = "";
    public string Uf             { get; set; } = "";
    public double PesoKg         { get; set; }
    public List<NfDetalhe> Nfs   { get; set; } = [];

    // Grupo: o nome pelo qual este destinatário é cobrado. Fica vazio quando o
    // cliente é cobrado com o próprio nome. A tela mostra o nome da nota e o
    // grupo ao lado; a junção só acontece na hora de calcular.
    public string Grupo       { get; set; } = "";
    public string CidadeGrupo { get; set; } = "";
    public string UfGrupo     { get; set; } = "";
    public string ChaveGrupo  { get; set; } = "";

    // Conflito: NFs do mesmo grupo têm cidades de destino diferentes no XML
    public bool         CidadeConflito     { get; set; } = false;
    public List<string> CidadesEncontradas { get; set; } = [];

    // Peso incompleto: algum produto não tem peso cadastrado
    public bool                  PesoIncompleto    { get; set; } = false;
    public List<ProdutoSemPeso>  ProdutosSemPeso   { get; set; } = [];
}

public class ProdutoSemPeso
{
    public string NumerNF { get; set; } = "";
    public string Produto  { get; set; } = "";
    public double Qtd      { get; set; }
}

public class GrupoFrete
{
    public string DestOriginal { get; set; } = "";
    public DateTime? DataEmissao { get; set; }
    public string Apelido { get; set; } = "";
    public string Placa { get; set; } = "";
    public double FreteCaixa { get; set; }
    public double FreteCapira { get; set; }
    public List<(string NF, string EmitOriginal, double Caixas, bool ProdutoE)> NFs { get; set; } = new();
}

// ──── Service ────

public class NFeImportService
{
    // CLIENTES_FRETE: destinatários que geram .txt de frete (GIRASSOL)
    private static readonly string[] IgnorarEmitentesPerfl = [Norm("TARO GAMA E OUTROS")];
    private static readonly string[] ProdutoEEmitentesPerfl = [Norm("HIRO BETA")];
    public static readonly Dictionary<string, (string Apelido, double FreteCaixa, double FreteCapira)> ClientesFrete = new()
    {
        [Norm("DIST.ECOM.DE VOLUMES GIRASSOL LTDA-ME")]              = ("DIST_ECOM", 9.00, 4.50),
        [Norm("DISTRIBUIDORA E COM. DE VOLUMES GIRASSOL LTDA")]      = ("DIST_ECOM", 9.00, 4.50),
        [Norm("DISTRIBUIDORA E COMERCIO DE VOLUMES GIRASSOL LTDA")]  = ("DIST_ECOM", 9.00, 4.50),
    };

    // Public so controller can use
    public static readonly Dictionary<string, (string Template, string Apelido)> Clientes = new()
    {
        [Norm("VOLUMES AURORA ATACADO E VAREJO LTDA")] = ("AURORA", "AURORA"),
        [Norm("BRZ DISTRIBUIDOR LTDA")] = ("BRZ", "BRZ"),
        [Norm("C.D. COMERCIO DE HORTIFRUTI LTDA")] = ("CEDRO", "CEDRO FRUTAS"),
        [Norm("VERDURAO DUNAS LTDA ME")] = ("DUNAS", "DUNAS"),
        [Norm("LITORAL COMERCIO E TRANSPORTES LTDA")] = ("FAROL", "FAROL"),
    };

    private static readonly Dictionary<string, string> BoletoEstilo = new()
    {
        ["BRZ"] = "por_nf",
        ["AURORA"] = "por_nf",
        ["FAROL"] = "por_nf",
        ["DUNAS"] = "metade",
        ["CEDRO FRUTAS"] = "metade_cod",
    };

    private static readonly Dictionary<string, string> MapaProduto = new()
    {
        ["JB"] = "Jumbo Br.", ["EB"] = "Extra Br.", ["GB"] = "Grande Br.",
        ["MB"] = "Medio Br.", ["PB"] = "Pequeno Br.", ["CF"] = "Casca Fina",
        ["JV"] = "Jumbo Vr.", ["EV"] = "Extra Vr.", ["GV"] = "Grande Vr.",
        ["MV"] = "Medio Vr.", ["PV"] = "Pequeno Vr.",
        ["GB30EM"] = "Grande Br Dz", ["GV30EM"] = "Grande Vr Dz",
        ["GB10X30"] = "Grande Br Pvc", ["GB10X20"] = "Grande Br 10x20",
        ["GV10X30"] = "Grande Vr Pvc", ["MBPVC"] = "Médio Br Pvc", ["MVPVC"] = "Médio Vr Pvc",
    };

    private static readonly Dictionary<string, string> LabelNormMap;

    // ── Peso por produto quando pesoL == 0 no XML ──
    // Chave: nome do produto normalizado (uppercase, sem espaços duplos)
    // Valor: kg por caixa
    private static readonly Dictionary<string, double> PesoPorProduto;

    static NFeImportService()
    {
        var variants = new (string variant, string canon)[]
        {
            ("Jumbo Br.", "Jumbo Br."), ("Jumbo Br", "Jumbo Br."),
            ("Extra Br.", "Extra Br."), ("Extra Br", "Extra Br."),
            ("Grande Br.", "Grande Br."),
            ("Medio Br.", "Medio Br."), ("Medio Br", "Medio Br."), ("Médio Br.", "Medio Br."),
            ("Pequeno Br.", "Pequeno Br."),
            ("Casca Fina", "Casca Fina"),
            ("Jumbo Vr.", "Jumbo Vr."), ("Jumbo Vr", "Jumbo Vr."),
            ("Extra Vr.", "Extra Vr."), ("Extra Vr", "Extra Vr."),
            ("Grande Vr.", "Grande Vr."),
            ("Medio Vr.", "Medio Vr."), ("Médio Vr.", "Medio Vr."),
            ("Pequeno Vr.", "Pequeno Vr."),
            ("Grande Br Dz", "Grande Br Dz"), ("Grande Vr Dz", "Grande Vr Dz"),
            ("Grande Br Pvc", "Grande Br Pvc"), ("Grande Br 10x20", "Grande Br 10x20"),
            ("Br 10X20", "Grande Br 10x20"), ("Grande Vr Pvc", "Grande Vr Pvc"),
            ("Médio Br Pvc", "Médio Br Pvc"), ("Médio Vr Pvc", "Médio Vr Pvc"),
            ("ProdutosD", "ProdutosD"),
            ("Embal.Amar.", "Embal.Amar."), ("Embal. Amar.", "Embal.Amar."),
            ("Embal.Branca", "Embal.Branca"), ("Embal. Branca", "Embal.Branca"),
            ("ProdutosC", "ProdutosC"), ("ProdutosC", "ProdutosC"),
        };
        LabelNormMap = new Dictionary<string, string>();
        foreach (var (v, c) in variants)
            LabelNormMap[Norm(v)] = c;

        // Peso por produto (kg/cx) quando pesoL == 0 no XML
        // Chave = produto normalizado (uppercase, sem acento, sem espaços duplos)
        PesoPorProduto = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [NormProduto("VOLUMES DE PRODUTOE C/10. CXS C/ 18 PRODUTOSC")] = 12.0,  // HIRO BETA
            [NormProduto("VOLUMES TIPO VERMELHO C/180 VOLUMES")]            = 12.0,  // HIRO BETA: o mesmo produtoE, descrição nova (set/2026)
            [NormProduto("VOLUMES PRODUTOD CX 50 DZ")]                     =  7.0,  // MAURO EPSILON
            // MARIO AKIRA DELTA manda a nota sem peso. Mesma caixa de 30 dz do
            // grande vermelho da Alfa, que nas notas dela sai com 21,0 kg.
            [NormProduto("GCXV - VOLUME GRANDE - VERMELHO 30 DZ")]        = 21.0,
        };
    }

    // ── Parse NF-e XML ──

    /// <param name="escritas">
    /// Escritas cadastradas na tela de Tipos de volumes (texto normalizado → tipo).
    /// Quando vem, é a única fonte; sem ela vale o mapa fixo do código.
    /// </param>
    public static NFeData ParseXml(string xml, IReadOnlyDictionary<string, string>? escritas = null)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";

        IEnumerable<XElement> Desc(string local) =>
            doc.Descendants(ns + local).Any()
                ? doc.Descendants(ns + local)
                : doc.Descendants(local);

        XElement? First(string local) => Desc(local).FirstOrDefault();

        var dados = new NFeData();

        // Chave de acesso — vem no Id do infNFe ("NFe" + 44 dígitos)
        var infEl = doc.Descendants(ns + "infNFe").FirstOrDefault()
                 ?? doc.Descendants("infNFe").FirstOrDefault();
        if (infEl != null)
            dados.Chave = new string((infEl.Attribute("Id")?.Value ?? "").Where(char.IsDigit).ToArray());

        // nNF
        var nNFEl = First("nNF");
        if (nNFEl != null) dados.NumeroNF = nNFEl.Value.Trim();

        // dhEmi
        var dhEmiEl = First("dhEmi");
        if (dhEmiEl != null && DateTime.TryParse(dhEmiEl.Value.Trim(), out var dt))
            dados.DataEmissao = dt;

        // emit
        var emitEl = doc.Descendants(ns + "emit").FirstOrDefault()
                  ?? doc.Descendants("emit").FirstOrDefault();
        if (emitEl != null)
        {
            var xn = emitEl.Descendants(ns + "xNome").FirstOrDefault()
                  ?? emitEl.Descendants("xNome").FirstOrDefault();
            if (xn != null) { dados.EmitOriginal = xn.Value.Trim(); dados.EmitNorm = Norm(dados.EmitOriginal); }
            var xMun = emitEl.Descendants(ns + "xMun").FirstOrDefault()
                    ?? emitEl.Descendants("xMun").FirstOrDefault();
            if (xMun != null) dados.CidadeEmit = xMun.Value.Trim().ToUpper();
            // CNPJ e IE — usados na chave de agrupamento exata do FRETE.py
            var cnpjEl = emitEl.Descendants(ns + "CNPJ").FirstOrDefault()
                      ?? emitEl.Descendants("CNPJ").FirstOrDefault();
            if (cnpjEl != null) dados.CnpjEmit = cnpjEl.Value.Trim();
            var ieEl = emitEl.Descendants(ns + "IE").FirstOrDefault()
                    ?? emitEl.Descendants("IE").FirstOrDefault();
            if (ieEl != null) dados.IeEmit = ieEl.Value.Trim();
        }

        // dest
        var destEl = doc.Descendants(ns + "dest").FirstOrDefault()
                  ?? doc.Descendants("dest").FirstOrDefault();
        if (destEl != null)
        {
            var xn = destEl.Descendants(ns + "xNome").FirstOrDefault()
                  ?? destEl.Descendants("xNome").FirstOrDefault();
            if (xn != null) { dados.DestOriginal = xn.Value.Trim(); dados.DestNorm = Norm(dados.DestOriginal); }
            var xMun = destEl.Descendants(ns + "xMun").FirstOrDefault()
                    ?? destEl.Descendants("xMun").FirstOrDefault();
            if (xMun != null) dados.CidadeDestino = xMun.Value.Trim().ToUpper();
            var uf = destEl.Descendants(ns + "UF").FirstOrDefault()
                  ?? destEl.Descendants("UF").FirstOrDefault();
            if (uf != null) dados.Uf = uf.Value.Trim().ToUpper();
        }

        // pesoL (peso líquido) — soma de todos
        foreach (var pesoEl in Desc("pesoL"))
        {
            if (double.TryParse(pesoEl.Value.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var p))
                dados.PesoKg += p;
        }

        // vNF
        var vNFEl = First("vNF");
        if (vNFEl != null && decimal.TryParse(vNFEl.Value.Replace(",", "."),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var vNF))
            dados.ValorNF = vNF;

        // infCpl
        var infCplEl = First("infCpl");
        if (infCplEl != null)
        {
            var infCpl = infCplEl.Value.Trim();
            dados.Funrural = ExtrairFunrural(infCpl);
            (dados.Placa, dados.Motorista) = ExtrairPlacaMotorista(infCpl);
        }

        // Sem placa no texto, vale a do grupo de transporte (transp/veicTransp)
        if (dados.Placa.Length == 0)
        {
            var placaVeic = Desc("veicTransp").SelectMany(v => v.Elements())
                .FirstOrDefault(e => e.Name.LocalName == "placa")?.Value.Trim();
            if (!string.IsNullOrEmpty(placaVeic)) dados.Placa = placaVeic;
        }

        // det/prod
        foreach (var detEl in doc.Descendants(ns + "det").Any()
            ? doc.Descendants(ns + "det")
            : doc.Descendants("det"))
        {
            var prodEl = detEl.Descendants(ns + "prod").FirstOrDefault()
                      ?? detEl.Descendants("prod").FirstOrDefault();
            if (prodEl == null) continue;

            var xProdEl = prodEl.Descendants(ns + "xProd").FirstOrDefault()
                       ?? prodEl.Descendants("xProd").FirstOrDefault();
            var qComEl = prodEl.Descendants(ns + "qCom").FirstOrDefault()
                      ?? prodEl.Descendants("qCom").FirstOrDefault();
            var vUnComEl = prodEl.Descendants(ns + "vUnCom").FirstOrDefault()
                       ?? prodEl.Descendants("vUnCom").FirstOrDefault();

            if (xProdEl == null) continue;
            double qCom = 0, vUnCom = 0;
            if (qComEl != null) double.TryParse(qComEl.Value.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out qCom);
            if (vUnComEl != null) double.TryParse(vUnComEl.Value.Replace(",", "."), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out vUnCom);

            var xProd = xProdEl.Value.Trim();
            dados.Produtos.Add(new ProdutoNFe
            {
                Label = XmlParaLabel(xProd, escritas), Original = xProd, QCom = qCom, VUnCom = vUnCom,
            });
        }

        return dados;
    }

    private static decimal? ExtrairFunrural(string infCpl)
    {
        var m = Regex.Match(infCpl, @"R\$\s*([\d]+[.,][\d]+)");
        if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(",", "."),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    // Nome de gente: palavras de letras (com acento). Para antes de "PLACA" para
    // não engolir o resto da frase quando o nome vem escrito antes da placa.
    private const string PadraoNome = @"\p{L}+(?:\s+(?!PLACA\b)\p{L}+)*";

    private static (string placa, string motorista) ExtrairPlacaMotorista(string infCpl)
    {
        string placa = "", motorista = "";

        // 1) Nome com rótulo — "MOT. FULANO", "MOTORISTA: FULANO". Procura em
        //    qualquer ponto do texto, então continua achando se a nota mudar o
        //    campo de lugar.
        var mRotulo = Regex.Match(infCpl,
            @"\bMOT(?:ORISTA)?\b\s*[:.]?\s*(" + PadraoNome + ")",
            RegexOptions.IgnoreCase);
        if (mRotulo.Success) motorista = mRotulo.Groups[1].Value.Trim();

        // 2) Sem rótulo, logo depois da placa — foi o que passou a chegar em
        //    agosto/2026: "PLACA XAB 4C58 - RENATO ELIAS BRANDAO".
        var mPlaca = Regex.Match(infCpl,
            @"PLACA\s+([A-Z0-9]+(?:\s[A-Z0-9]+)?)\s*-\s*(?:MOT(?:ORISTA)?\b\s*[:.]?\s*)?(" + PadraoNome + ")?",
            RegexOptions.IgnoreCase);
        if (mPlaca.Success)
        {
            placa = mPlaca.Groups[1].Value.Trim().Replace(" ", "-");
            if (motorista.Length == 0) motorista = mPlaca.Groups[2].Value.Trim();
        }

        return (placa, motorista);
    }

    // A descrição inteira ganha do código: fazenda sem código ("VOLUMES TIPO
    // VERMELHO C/180 VOLUMES") só pode ser cadastrada pelo texto todo.
    /// <summary>
    /// O tipo em que a escrita da nota cai, ou null. Diferente de comparar o
    /// XmlParaLabel com o texto: o tipo pode ter o próprio texto da nota como nome
    /// (ex.: "VOLUMES PRODUTOD C/ 30", ProdutosD Gama) e ainda assim estar cadastrado.
    /// </summary>
    public static string? TipoDaEscrita(string xProd, IReadOnlyDictionary<string, string> escritas) =>
        escritas.TryGetValue(NormEscrita(xProd), out var tipo) || escritas.TryGetValue(NormEscrita(xProd.Split('-')[0]), out tipo)
            ? tipo : null;

    public static string XmlParaLabel(string xProd, IReadOnlyDictionary<string, string>? escritas = null)
    {
        if (escritas != null)
        {
            if (escritas.TryGetValue(NormEscrita(xProd), out var tipo)) return tipo;
            if (escritas.TryGetValue(NormEscrita(xProd.Split('-')[0]), out tipo)) return tipo;
            return xProd;
        }
        var codigo = xProd.Split('-')[0].Trim().ToUpper();
        return MapaProduto.TryGetValue(codigo, out var label) ? label : xProd;
    }

    /// <summary>Chave de comparação das escritas: maiúsculas, sem acento, espaços simples.</summary>
    public static string NormEscrita(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormKD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return string.Join(" ", sb.ToString().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>O mapa fixo do código, para semear a tela de Tipos de volumes.</summary>
    public static IEnumerable<(string Codigo, string Tipo)> EscritasPadrao =>
        MapaProduto.Select(kv => (kv.Key, kv.Value));

    /// <summary>Os rótulos que existem como linha nos modelos de romaneio.</summary>
    public static IReadOnlyList<string> LinhasRomaneio =>
        [.. LabelNormMap.Values.Distinct()];

    /// <summary>Nome do tipo como a linha do romaneio o conhece, ou o próprio texto.</summary>
    public static string CanonTipo(string label) =>
        LabelNormMap.TryGetValue(Norm(label), out var c) ? c : label;

    public static bool TemLinhaNoRomaneio(string label) => LabelNormMap.ContainsKey(Norm(label));

    private static readonly Regex RxVermelho = new(@"\b(VR|VERMELHO)\b", RegexOptions.IgnoreCase);
    private static readonly Regex RxBranco = new(@"\b(BR|BRANCO)\b", RegexOptions.IgnoreCase);

    public static string? CorDoTipo(string label) =>
        RxVermelho.IsMatch(label) ? "VERMELHO" : RxBranco.IsMatch(label) ? "BRANCO" : null;

    // O mesmo tamanho na outra cor: "Grande Vr Pvc" ↔ "Grande Br Pvc". Só
    // devolve se esse par existir como linha do romaneio.
    public static string? ParDoTipo(string label)
    {
        string? outro = CorDoTipo(label) switch
        {
            "VERMELHO" => Regex.Replace(label, @"\bVr\b", "Br", RegexOptions.IgnoreCase),
            "BRANCO"   => Regex.Replace(label, @"\bBr\b", "Vr", RegexOptions.IgnoreCase),
            _ => null,
        };
        return outro != null && LabelNormMap.TryGetValue(Norm(outro), out var canon) ? canon : null;
    }

    // ── Conferência dos tipos ──
    // O romaneio tem uma linha por tipo. Se duas linhas de nota caem no mesmo
    // tipo com preços diferentes, as quantidades somam e sobra um preço só —
    // foi assim que o GB10X30 do Farol, escrito como GV10X30 na NF 144373,
    // virou 133 caixas de vermelho a R$ 118,50. Vermelho é sempre mais caro que
    // branco, e é isso que aponta qual das linhas está errada.

    public static List<ConflitoTipo> DetectarConflitosTipo(IEnumerable<NFeData> nfes)
    {
        var ptBr = new System.Globalization.CultureInfo("pt-BR");
        string R(double v) => "R$ " + v.ToString("N2", ptBr);

        var lista = new List<ConflitoTipo>();
        foreach (var cliente in nfes.Where(n => Clientes.ContainsKey(n.DestNorm)).GroupBy(n => n.DestNorm))
        {
            var apelido = Clientes[cliente.Key].Apelido;
            var porTipo = cliente
                .OrderBy(n => n.NumeroNF)
                .SelectMany(n => n.Produtos.Select(p => new OcorrenciaTipo
                {
                    NF = n.NumeroNF, ProdutoNota = p.Original, Tipo = CanonTipo(p.Label),
                    Quantidade = p.QCom, Preco = p.VUnCom,
                }))
                .GroupBy(o => o.Tipo)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 1) O mesmo tipo com mais de um preço
            foreach (var (tipo, ocs) in porTipo)
            {
                if (ocs.Count < 2 || ocs.Select(o => Math.Round(o.Preco, 2)).Distinct().Count() < 2) continue;

                string? sugestao = null;
                OcorrenciaTipo? errada = null;
                if (CorDoTipo(tipo) == "VERMELHO")
                {
                    var barata = errada = ocs.MinBy(o => o.Preco)!;
                    sugestao = $"Vermelho é sempre mais caro que branco: provavelmente a linha divergente é a de {R(barata.Preco)} da NF {barata.NF}.";
                }
                else if (CorDoTipo(tipo) == "BRANCO")
                {
                    var cara = errada = ocs.MaxBy(o => o.Preco)!;
                    sugestao = $"Vermelho é sempre mais caro que branco: provavelmente a linha divergente é a de {R(cara.Preco)} da NF {cara.NF}.";
                }

                lista.Add(new ConflitoTipo
                {
                    Cliente = apelido, Tipo = tipo, Sugestao = sugestao, Ocorrencias = ocs,
                    SugestaoNF = errada?.NF,
                    SugestaoProduto = errada?.ProdutoNota,
                    SugestaoPreco = errada?.Preco,
                    Motivo = $"{tipo} aparece em {ocs.Count} linhas com preços diferentes. O romaneio tem uma " +
                             "linha só para ele: as caixas seriam somadas e ficaria um preço só.",
                });
            }

            // 2) Vermelho mais barato que o branco do mesmo tamanho
            foreach (var (tipo, vermelhos) in porTipo)
            {
                if (CorDoTipo(tipo) != "VERMELHO") continue;
                var par = ParDoTipo(tipo);
                if (par == null || !porTipo.TryGetValue(par, out var brancos)) continue;
                if (lista.Any(c => c.Cliente == apelido && (c.Tipo == tipo || c.Tipo == par))) continue;
                if (vermelhos.Min(o => o.Preco) > brancos.Max(o => o.Preco)) continue;

                lista.Add(new ConflitoTipo
                {
                    Cliente = apelido, Tipo = tipo,
                    Motivo = $"{tipo} saiu pelo mesmo preço ou mais barato que {par}.",
                    Sugestao = "Vermelho é sempre mais caro que branco: confira se os tipos não foram trocados na nota.",
                    Ocorrencias = [.. vermelhos, .. brancos],
                });
            }
        }
        return lista;
    }

    // Produto sem tipo = nenhuma escrita casou (o rótulo ficou o próprio texto da nota) e
    // ninguém trocou o tipo na tela. Tipo com o mesmo nome do texto da nota conta como tipo.
    public static List<ProdutoSemTipo> ProdutosSemTipo(IEnumerable<NFeData> nfes, IReadOnlyDictionary<string, string>? escritas)
    {
        var lista = new List<ProdutoSemTipo>();
        foreach (var n in nfes)
        {
            if (!Clientes.TryGetValue(n.DestNorm, out var cli)) continue;
            foreach (var p in n.Produtos)
            {
                if (NormEscrita(p.Label) != NormEscrita(p.Original)) continue;
                if (escritas != null ? TipoDaEscrita(p.Original, escritas) != null : LabelNormMap.ContainsKey(Norm(p.Label))) continue;
                lista.Add(new ProdutoSemTipo(cli.Apelido, n.NumeroNF, p.Original, p.QCom, p.VUnCom));
            }
        }
        return lista;
    }

    public static void AplicarCorrecoes(IEnumerable<NFeData> nfes, IEnumerable<CorrecaoTipo>? correcoes)
    {
        if (correcoes == null) return;
        foreach (var c in correcoes)
            foreach (var n in nfes.Where(n => n.NumeroNF == c.NF))
                foreach (var p in n.Produtos.Where(p =>
                    NormEscrita(p.Original) == NormEscrita(c.ProdutoNota) && Math.Abs(p.VUnCom - c.Preco) < 0.005))
                    p.Label = c.NovoTipo;
    }

    // ── Group by client ──

    public static Dictionary<string, GrupoCliente> AgruparPorCliente(IEnumerable<NFeData> nfes)
    {
        var grupos = new Dictionary<string, GrupoCliente>();
        foreach (var dados in nfes)
        {
            if (!Clientes.ContainsKey(dados.DestNorm)) continue;
            if (!grupos.ContainsKey(dados.DestNorm))
                grupos[dados.DestNorm] = new GrupoCliente { DestOriginal = dados.DestOriginal, DataEmissao = dados.DataEmissao };

            var g = grupos[dados.DestNorm];
            if (string.IsNullOrEmpty(g.Placa) && !string.IsNullOrEmpty(dados.Placa)) g.Placa = dados.Placa;
            if (string.IsNullOrEmpty(g.Motorista) && !string.IsNullOrEmpty(dados.Motorista)) g.Motorista = dados.Motorista;

            decimal? totalNF = dados.ValorNF ?? (dados.Produtos.Any() ? (decimal?)dados.Produtos.Sum(p => p.QCom * p.VUnCom) : null);
            g.NFs.Add(new NFeNFInfo { NF = dados.NumeroNF, Funrural = dados.Funrural, VNF = totalNF });

            foreach (var p in dados.Produtos)
            {
                var cur = g.ProdutosTotal.TryGetValue(p.Label, out var ex) ? ex : (0, 0);
                g.ProdutosTotal[p.Label] = (cur.QCom + p.QCom, p.VUnCom);
                g.Itens.Add((dados.NumeroNF, (p.Original ?? "").Trim(), p.Label, p.QCom, p.VUnCom));
            }
        }
        return grupos;
    }

    // ── Group CLIENTES_FRETE ──

    public static Dictionary<string, GrupoFrete> AgruparPorClienteFrete(IEnumerable<NFeData> nfes)
    {
        var grupos = new Dictionary<string, GrupoFrete>();
        foreach (var dados in nfes)
        {
            if (!ClientesFrete.TryGetValue(dados.DestNorm, out var cfg)) continue;
            if (IgnorarEmitentesPerfl.Contains(dados.EmitNorm)) continue;

            if (!grupos.ContainsKey(cfg.Apelido))
                grupos[cfg.Apelido] = new GrupoFrete
                {
                    DestOriginal = dados.DestOriginal,
                    DataEmissao  = dados.DataEmissao,
                    Apelido      = cfg.Apelido,
                    FreteCaixa   = cfg.FreteCaixa,
                    FreteCapira  = cfg.FreteCapira,
                };

            bool produtoE = ProdutoEEmitentesPerfl.Contains(dados.EmitNorm);
            double caixas = dados.Produtos.Sum(p => p.QCom);
            grupos[cfg.Apelido].NFs.Add((dados.NumeroNF, dados.EmitOriginal, caixas, produtoE));
            if (string.IsNullOrEmpty(grupos[cfg.Apelido].Placa) && !string.IsNullOrEmpty(dados.Placa))
                grupos[cfg.Apelido].Placa = dados.Placa;
        }
        return grupos;
    }

    /// <param name="produtoEManual">
    /// Caixas de produtoE digitadas na tela. A nota do Beta às vezes não chega
    /// a tempo de gerar o relatório, e sem ela a linha de produtoE sai zerada —
    /// o frete fecharia errado.
    /// </param>
    public static string GerarRelatorioFreteTexto(GrupoFrete grupo, double produtoEManual = 0)
    {
        var ptBr = new System.Globalization.CultureInfo("pt-BR");
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 60));
        sb.AppendLine($"  RELATORIO DE FRETE  -  {grupo.DestOriginal}");
        if (grupo.DataEmissao.HasValue)
            sb.AppendLine($"  Data: {grupo.DataEmissao.Value:dd/MM/yyyy}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        double totalNormais = 0, totalProdutosE = 0;
        foreach (var (nf, emit, caixas, produtoE) in grupo.NFs.OrderBy(n => n.NF))
        {
            var tipo = produtoE ? "PRODUTOE" : "NORMAL";
            var cxFmt = caixas == Math.Floor(caixas) ? ((int)caixas).ToString() : caixas.ToString(ptBr);
            sb.AppendLine($"  NF {nf,6}  -  {cxFmt,6} caixas  [{tipo}]  ({emit})");
            if (produtoE) totalProdutosE += caixas; else totalNormais += caixas;
        }

        // Fica escrito no documento que veio da mão, para quem ler depois saber
        // que aquelas caixas não tinham nota na hora.
        if (produtoEManual > 0)
        {
            var cxManual = produtoEManual == Math.Floor(produtoEManual)
                ? ((int)produtoEManual).ToString()
                : produtoEManual.ToString(ptBr);
            sb.AppendLine($"  {"(sem NF)",9}  -  {cxManual,6} caixas  [PRODUTOE]  (informado a mao)");
            totalProdutosE += produtoEManual;
        }

        string FmtInt(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString(ptBr);
        string FmtBr(double v) => v.ToString("N2", ptBr);

        sb.AppendLine();
        sb.AppendLine(new string('-', 60));
        sb.AppendLine($"  Total caixas normais : {FmtInt(totalNormais),6}");
        sb.AppendLine($"  Total caixas produtoE : {FmtInt(totalProdutosE),6}");
        sb.AppendLine();
        double valorNormais  = totalNormais  * grupo.FreteCaixa;
        double valorProdutosE = totalProdutosE * grupo.FreteCapira;
        double valorTotal    = valorNormais  + valorProdutosE;
        sb.AppendLine($"  Frete normais  ({FmtInt(totalNormais)} cx x R$ {FmtBr(grupo.FreteCaixa)}): R$ {FmtBr(valorNormais)}");
        sb.AppendLine($"  Frete produtosE ({FmtInt(totalProdutosE)} cx x R$ {FmtBr(grupo.FreteCapira)}): R$ {FmtBr(valorProdutosE)}");
        sb.AppendLine();
        sb.AppendLine($"  *** TOTAL FRETE: R$ {FmtBr(valorTotal)} ***");
        sb.AppendLine(new string('=', 60));
        return sb.ToString();
    }

    // ── Extrair itens para FreteCalculoPage ──
    // Replica EXATAMENTE o comportamento do FRETE.py:
    //   • Aplica substituições de destinatário e cidade/UF
    //   • Calcula peso quando pesoL == 0 (PESO_POR_CLIENTE ou 10 kg/cx)
    //   • Agrupa pela MESMA chave do script: cnpj_emit|ie_emit|norm(emit)|norm(dest)
    //   • Cada grupo expõe a lista de NFs individuais com seu peso e nome de arquivo

    /// <param name="substituicoes">
    /// Grupos da aba Substituições (FRE-03). Sem eles vale o mapa do código.
    /// </param>
    public static List<NFeFreteItem> ExtrairParaFrete(IEnumerable<(string xml, string arquivo)> arquivos,
        IEnumerable<GrupoSubstituicao>? substituicoes = null)
    {
        var (substDestinatarios, substCidadeUF) = SubstituicoesService.Mapas(substituicoes ?? SubstituicoesService.Padrao, Norm);

        // 1. Parsear cada XML e aplicar substituições
        var parseds = new List<(NFeData d, double peso, string arquivo, List<ProdutoSemPeso> semPeso,
            string grupo, string grupoCidade, string grupoUf)>();
        foreach (var (xml, nomeArquivo) in arquivos)
        {
            try
            {
                var d = ParseXml(xml);

                // O nome da nota não é mais sobrescrito: a substituição vira o
                // "grupo" do destinatário, e é o grupo que entra na conta. Assim
                // a tela mostra quem realmente emitiu e para quem vai, e ainda
                // diz por qual nome aquilo é cobrado.
                string grupo = "", grupoCidade = "", grupoUf = "";
                if (substDestinatarios.TryGetValue(d.DestNorm, out var destSubst))
                    grupo = destSubst;

                // A cidade é procurada pelo nome que vale para a cobrança — é o
                // grupo quando existe, senão o próprio destinatário.
                var normCobranca = grupo.Length > 0 ? Norm(grupo) : d.DestNorm;
                if (substCidadeUF.TryGetValue(normCobranca, out var cidadeSubst))
                {
                    grupoCidade = cidadeSubst.cidade;
                    grupoUf     = cidadeSubst.uf;
                }

                // Se pesoL == 0, calcula pelo peso por produto cadastrado
                double peso = d.PesoKg;
                var prodsSemPeso = new List<ProdutoSemPeso>();

                if (peso == 0 && d.Produtos.Any())
                {
                    foreach (var prod in d.Produtos)
                    {
                        // O peso é cadastrado pela descrição da nota; o rótulo
                        // fica de reserva para quando só ele bate.
                        if (PesoPorProduto.TryGetValue(NormProduto(prod.Original), out var kgCx)
                            || PesoPorProduto.TryGetValue(NormProduto(prod.Label), out kgCx))
                            peso += prod.QCom * kgCx;
                        else
                            prodsSemPeso.Add(new ProdutoSemPeso
                            {
                                NumerNF  = d.NumeroNF,
                                Produto  = prod.Label,
                                Qtd      = prod.QCom,
                            });
                    }
                }

                parseds.Add((d, peso, nomeArquivo, prodsSemPeso, grupo, grupoCidade, grupoUf));
            }
            catch { }
        }

        // 2. Agrupar — chave idêntica ao FRETE.py:
        //    cnpj_emit + "|" + ie_emit + "|" + norm(emitente) + "|" + norm(destinatário)
        var grupos        = new Dictionary<string, NFeFreteItem>();
        // Rastreia todas as cidades (cidade+UF) vistas por grupo para detectar conflitos
        var cidadesPorGrupo = new Dictionary<string, HashSet<string>>();

        foreach (var (d, peso, nomeArquivo, semPeso, grupo, grupoCidade, grupoUf) in parseds)
        {
            var chave = $"{d.CnpjEmit}|{d.IeEmit}|{d.EmitNorm}|{d.DestNorm}";

            if (!grupos.ContainsKey(chave))
            {
                grupos[chave] = new NFeFreteItem
                {
                    CnpjEmit       = d.CnpjEmit,
                    IeEmit         = d.IeEmit,
                    Emitente       = d.EmitOriginal,
                    CidadeEmitente = d.CidadeEmit,
                    Destinatario   = d.DestOriginal,
                    CidadeDestino  = d.CidadeDestino,
                    Uf             = d.Uf,
                    PesoKg         = 0,
                    Grupo          = grupo,
                    CidadeGrupo    = grupoCidade,
                    UfGrupo        = grupoUf,
                    // Linhas com a mesma chave viram uma só quando calcula
                    ChaveGrupo     = $"{d.CnpjEmit}|{d.IeEmit}|{d.EmitNorm}|"
                                   + Norm(grupo.Length > 0 ? grupo : d.DestOriginal),
                };
                cidadesPorGrupo[chave] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            // Registra a cidade desta NF no conjunto do grupo
            if (!string.IsNullOrEmpty(d.CidadeDestino))
                cidadesPorGrupo[chave].Add($"{d.CidadeDestino}/{d.Uf}");

            // Preenche campos vazios
            var g = grupos[chave];
            if (string.IsNullOrEmpty(g.Uf)             && !string.IsNullOrEmpty(d.Uf))
                g.Uf = d.Uf;
            if (string.IsNullOrEmpty(g.CidadeDestino)  && !string.IsNullOrEmpty(d.CidadeDestino))
                g.CidadeDestino = d.CidadeDestino;
            if (string.IsNullOrEmpty(g.CidadeEmitente) && !string.IsNullOrEmpty(d.CidadeEmit))
                g.CidadeEmitente = d.CidadeEmit;

            g.PesoKg += peso;

            // Acumula produtos sem peso cadastrado
            if (semPeso.Count > 0)
            {
                g.PesoIncompleto = true;
                g.ProdutosSemPeso.AddRange(semPeso);
            }

            grupos[chave].Nfs.Add(new NfDetalhe
            {
                Numero  = d.NumeroNF,
                PesoKg  = peso,
                Arquivo = nomeArquivo,
                Chave   = d.Chave,
                Placa     = SemPararPlanilhaService.NormalizarPlaca(d.Placa),
                Motorista = d.Motorista.ToUpperInvariant(),
            });
        }

        // 3. Preencher NumeroNF e detectar conflitos de cidade
        foreach (var (chave, item) in grupos)
        {
            item.NumeroNF = string.Join(", ", item.Nfs.Select(n => n.Numero).OrderBy(n => n));

            var cidades = cidadesPorGrupo[chave];
            if (cidades.Count > 1)
            {
                item.CidadeConflito    = true;
                item.CidadesEncontradas = [.. cidades.Order()];
                // Não define CidadeDestino/Uf automaticamente — deixa o usuário escolher
                item.CidadeDestino = "";
                item.Uf            = "";
            }
        }

        // Ordena pelo grupo para as linhas que viram uma só ficarem juntas na
        // tela — é o que deixa a junção visível antes de calcular.
        return [.. grupos.Values.OrderBy(i => i.ChaveGrupo).ThenBy(i => i.Destinatario)];
    }

    // ── Generate planilhas ──

    public PlanilhasResult GerarPlanilhas(IEnumerable<string> xmls, int qtdProdutosDCedro = 0,
        Dictionary<string, int>? numeros = null, double caixasProdutoE = 0,
        IReadOnlyDictionary<string, string>? escritas = null, IEnumerable<CorrecaoTipo>? correcoes = null,
        int produtosCBrz = 0, double precoProdutoCBrz = PrecoProdutoCPadrao, decimal precoProdutoDCedro = PrecoCaixaProdutoDCedro)
    {
        precoProdutoD = precoProdutoDCedro > 0 ? precoProdutoDCedro : PrecoCaixaProdutoDCedro;
        var result = new PlanilhasResult();
        var nfes = new List<NFeData>();
        foreach (var xml in xmls)
        {
            try { nfes.Add(ParseXml(xml, escritas)); } catch { }
        }

        AplicarCorrecoes(nfes, correcoes);

        // Nenhum produto da nota pode ficar fora do romaneio (NFE-09): sem tipo, não gera
        result.SemTipo = ProdutosSemTipo(nfes, escritas);
        if (result.SemTipo.Count > 0)
        {
            result.Erro = "Produto sem nome no romaneio: " + string.Join("; ", result.SemTipo.Select(p => $"{p.Cliente} NF {p.NF} \"{p.Produto}\"")) +
                ". Escolha o nome antes de gerar.";
            return result;
        }

        // Gera mesmo assim, mas não deixa passar calado: o conflito que sobrou
        // depois das correções vai para a tela.
        var ptBr = new System.Globalization.CultureInfo("pt-BR");
        foreach (var c in DetectarConflitosTipo(nfes))
            result.Avisos.Add($"{c.Cliente}: {c.Motivo} " + string.Join("; ", c.Ocorrencias.Select(o =>
                $"NF {o.NF} — {o.Quantidade:0.##} cx a R$ {o.Preco.ToString("N2", ptBr)}")));

        var grupos      = AgruparPorCliente(nfes);
        var gruposFrete = AgruparPorClienteFrete(nfes);

        if (!grupos.Any() && !gruposFrete.Any())
        {
            result.Erro = "Nenhum XML de cliente configurado encontrado.";
            return result;
        }

        var planilhas = new List<PlanilhaGerada>();

        // Gera .xlsx para cada cliente da planilha
        foreach (var (destNorm, grupo) in grupos)
        {
            var (templateKey, apelido) = Clientes[destNorm];
            var dataStr     = grupo.DataEmissao?.ToString("dd-MM-yyyy") ?? "";
            // Com número do romaneio: "164--AURORA-13-07-2026.xlsx" (padrão dos arquivos do Drive)
            var nomeArquivo = numeros != null && numeros.TryGetValue(apelido, out var num)
                ? $"{num}--{apelido}-{dataStr}.xlsx"
                : $"{apelido}-{dataStr}.xlsx";
            int qtdCod      = templateKey == "CEDRO" ? qtdProdutosDCedro : 0;
            int produtosC    = templateKey == "BRZ" ? produtosCBrz : 0;

            var (bytes, semLinha) = PreencherTemplate(grupo, templateKey, apelido, qtdCod, produtosC, precoProdutoCBrz);
            planilhas.Add(new PlanilhaGerada { NomeArquivo = nomeArquivo, Bytes = bytes });
            foreach (var tipo in semLinha)
                result.Avisos.Add($"{apelido}: \"{tipo}\" não tem linha no romaneio e ficou de fora. " +
                    "Cadastre essa escrita em Tipos de volumes ou preencha à mão.");
            result.ClientesEncontrados.Add(apelido);
            result.ArquivosGerados.Add(nomeArquivo);

            // Célula vazia sem avisar já passou batido uma vez: quando a nota não
            // traz o dado, fala na tela em vez de entregar a planilha capenga.
            if (string.IsNullOrEmpty(grupo.Motorista))
                result.Avisos.Add($"{apelido}: não achei o motorista nas notas — preencha à mão.");
            if (string.IsNullOrEmpty(grupo.Placa))
                result.Avisos.Add($"{apelido}: não achei a placa nas notas — preencha à mão.");
        }

        // Gera .txt para cada cliente de frete (GIRASSOL)
        foreach (var (_, grupoFrete) in gruposFrete)
        {
            var dataStr     = grupoFrete.DataEmissao?.ToString("dd-MM-yyyy") ?? "";
            // Padrão do nome: "23-07-2026 XAG-2G58.txt" (data + placa do veículo)
            var nomeArquivo = string.IsNullOrEmpty(grupoFrete.Placa)
                ? $"{dataStr}.txt"
                : $"{dataStr} {grupoFrete.Placa}.txt";
            var txt         = GerarRelatorioFreteTexto(grupoFrete, caixasProdutoE);
            var bytes       = Encoding.UTF8.GetBytes(txt);
            planilhas.Add(new PlanilhaGerada { NomeArquivo = nomeArquivo, Bytes = bytes });
            result.ClientesEncontrados.Add(grupoFrete.Apelido);
            result.ArquivosGerados.Add(nomeArquivo);
        }

        // Relatório de frete sem nenhuma caixa de produtoE quase sempre é a nota
        // do Beta que ainda não chegou — avisa em vez de entregar zerado.
        if (caixasProdutoE <= 0)
            foreach (var (_, g) in gruposFrete)
                if (!g.NFs.Any(n => n.ProdutoE))
                    result.Avisos.Add($"{g.Apelido}: nenhuma caixa de produtoE nas notas. " +
                        "Se teve produtoE, informe a quantidade e gere de novo.");

        // Entrega os arquivos individuais — o frontend baixa um a um (sem zip)
        result.Arquivos = planilhas;
        return result;
    }

    // Ordem das linhas de volumes de cada cliente, igual à dos romaneios de sempre
    // (o modelo só traz as linhas de exemplo). Tipo fora da lista vai no fim.
    private static readonly Dictionary<string, string[]> OrdemTipos = new()
    {
        ["BRZ"] = ["Jumbo Br.", "Extra Br.", "Grande Br.", "Medio Br.", "Pequeno Br.", "Casca Fina", "Jumbo Vr.", "Extra Vr.",
            "Grande Vr.", "Medio Vr.", "Pequeno Vr.", "Grande Br Dz", "Grande Vr Dz", "Grande Br Pvc", "Médio Br Pvc",
            "Grande Vr Pvc", "Médio Vr Pvc", "Grande Br 10x20", "ProdutosD"],
        ["AURORA"] = ["Jumbo Br.", "Extra Br.", "Grande Br.", "Medio Br.", "Pequeno Br.", "Casca Fina", "Jumbo Vr.", "Extra Vr.",
            "Grande Vr.", "Medio Vr.", "Pequeno Vr.", "Grande Br Dz", "Grande Vr Dz", "Grande Br Pvc", "Médio Br Pvc",
            "Grande Vr Pvc", "ProdutosD"],
        ["CEDRO"] = ["Grande Br.", "Grande Vr.", "Médio Br.", "Grande Br Dz", "Grande Vr Dz", "Grande Br Pvc", "Grande Vr Pvc", "ProdutosD"],
        ["DUNAS"] = ["Grande Br.", "Grande Vr.", "Médio Br.", "Grande Br Dz", "Grande Vr Dz", "Grande Br Pvc", "Grande Vr Pvc", "ProdutosD"],
        ["FAROL"] = ["Jumbo Br", "Extra Br", "Grande Br.", "Medio Br", "Pequeno Br.", "Jumbo Vr", "Extra Vr.", "Grande Vr.",
            "Médio Vr.", "Grande Br Dz", "Grande Vr Dz", "Grande Br Pvc", "Grande Vr Pvc", "ProdutosD", "Br 10X20"],
    };

    // ProdutoC vai junto com a carga da BRZ só às vezes, e não vem na nota: a
    // tela pergunta. Entra no valor a pagar, mas não nos boletos, que seguem
    // as notas — é assim nos romaneios feitos à mão.
    public const double PrecoProdutoCPadrao = 22.00;
    // Caixa de produtoD da Cedro no romaneio (Admin, 20/10/2026: era 115)
    public const decimal PrecoCaixaProdutoDCedro = 120m;
    // Preço da caixa de produtoD desta geração: o da aba Sicoob → Emissão de boletos (BOL-12), padrão 120
    private decimal precoProdutoD = PrecoCaixaProdutoDCedro;

    private (byte[] Bytes, List<string> SemLinha) PreencherTemplate(GrupoCliente grupo, string templateKey,
        string apelido, int qtdProdutosD, int produtosC = 0, double precoProdutoC = PrecoProdutoCPadrao)
    {
        var semLinha = new List<string>();
        var assembly = typeof(NFeImportService).Assembly;
        var resourceName = $"TransportadoraSystem.Application.Templates.{templateKey}.xlsx";
        byte[] templateBytes;
        using (var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Template {templateKey} não encontrado."))
        using (var ms = new MemoryStream())
        {
            stream.CopyTo(ms);
            templateBytes = ms.ToArray();
        }

        using var wb = new XLWorkbook(new MemoryStream(templateBytes));
        var ws = wb.Worksheets.First();

        // Remove proteção de somente leitura
        ws.Unprotect();

        // Data
        if (grupo.DataEmissao.HasValue)
        {
            ws.Cell("C2").Value = grupo.DataEmissao.Value.Date;
            ws.Cell("C2").Style.NumberFormat.Format = "DD/MM/YYYY";
            ws.Cell("H2").Value = grupo.DataEmissao.Value.Date;
            ws.Cell("H2").Style.NumberFormat.Format = "DD/MM/YYYY";
        }

        // Motorista (primeiro nome)
        if (!string.IsNullOrEmpty(grupo.Motorista))
        {
            var mot = grupo.Motorista.Split(' ')[0].ToUpper();
            ws.Cell("E2").Value = mot;
            ws.Cell("J2").Value = mot;
        }

        // Placa
        if (!string.IsNullOrEmpty(grupo.Placa))
        {
            ws.Cell("E3").Value = grupo.Placa;
            ws.Cell("J3").Value = grupo.Placa;
        }

        // Espelha Fornecedor / Destinatário / Vendedor no lado direito
        ws.Cell("H3").Value = ws.Cell("C3").Value;
        ws.Cell("H4").Value = ws.Cell("C4").Value;
        ws.Cell("J4").Value = ws.Cell("E4").Value;

        // O bloco do modelo vai da linha 8 até antes do "T. CXS VOLUMES"
        const int primeira = 8;
        int LinhaDoRotulo(Func<string, bool> teste, int col = 2) =>
            ws.RowsUsed().FirstOrDefault(r => r.Cell(col).Value.IsText && teste(NormEscrita(r.Cell(col).Value.GetText())))?.RowNumber() ?? 0;
        int linhaTotal = LinhaDoRotulo(t => t.StartsWith("T. CXS VOLUME"));
        if (linhaTotal <= primeira) throw new InvalidOperationException($"Modelo {templateKey} sem a linha \"T. CXS VOLUMES\".");

        // Linhas de volumes (NFE-09): os nomes e a ordem do modelo de sempre, mas só os
        // tipos que vieram nas notas. Tipo que o modelo não tem (cadastrado na tela)
        // entra depois, com o nome do tipo. Nada da nota fica de fora: a coluna fixa
        // deixou o "EXTRA BRANCO 10 X 30 PVC" fora da BRZ de 26/10/2026.
        var doModelo = new Dictionary<string, (int Pos, string Texto)>();
        var ordem = OrdemTipos.TryGetValue(templateKey, out var o) ? o : [];
        for (int k = 0; k < ordem.Length; k++)
            if (LabelNormMap.TryGetValue(Norm(ordem[k]), out var canonModelo)) doModelo.TryAdd(canonModelo, (k, ordem[k]));
        var itens = grupo.Itens
            .Select((i, ordem) => (i, ordem, Tipo: CanonTipo(i.Label)))
            .GroupBy(x => (NormEscrita(x.Tipo), Math.Round(x.i.VUnCom, 4)))
            .Select(g =>
            {
                var tipo = g.First().Tipo;
                var noModelo = doModelo.TryGetValue(tipo, out var m);
                return (Nome: noModelo ? m.Texto : tipo, Pos: noModelo ? m.Pos : 1000 + g.Min(x => x.ordem),
                    QCom: g.Sum(x => x.i.QCom), VUnCom: g.First().i.VUnCom);
            })
            .OrderBy(i => i.Pos).ThenBy(i => i.VUnCom)
            .ToList();
        var volumes = itens.Where(i => !NormEscrita(i.Nome).Contains("PRODUTOD")).ToList();
        var produtosDNota = itens.Where(i => NormEscrita(i.Nome).Contains("PRODUTOD")).ToList();

        var linhas = new List<(string Nome, double? Q, double? P)>();
        linhas.AddRange(volumes.Select(i => (i.Nome, (double?)i.QCom, (double?)i.VUnCom)));
        linhas.AddRange(produtosDNota.Select(i => (i.Nome, (double?)i.QCom, (double?)i.VUnCom)));
        // As produtosD da Cedro não vêm na nota (NFE-03): linha fixa, digitada na tela
        if (templateKey == "CEDRO")
            linhas.Add(("ProdutosD", qtdProdutosD > 0 ? qtdProdutosD : null, qtdProdutosD > 0 ? (double)precoProdutoD : null));
        linhas.Add(("Embal.Amar.", null, null));
        linhas.Add(("Embal.Branca", null, null));
        linhas.Add(("ProdutosC", produtosC > 0 ? produtosC : null, produtosC > 0 ? precoProdutoC : null));

        // O bloco cresce ou encolhe para caber exatamente as linhas acima
        var estilo = Enumerable.Range(1, 11).ToDictionary(c => c, c => ws.Cell(primeira, c).Style);
        var altura = ws.Row(primeira).Height;
        int noModelo = linhaTotal - primeira;
        if (linhas.Count > noModelo) ws.Row(linhaTotal - 1).InsertRowsBelow(linhas.Count - noModelo);
        else if (linhas.Count < noModelo) ws.Rows(primeira + linhas.Count, linhaTotal - 1).Delete();

        for (int k = 0; k < linhas.Count; k++)
        {
            int r = primeira + k;
            ws.Row(r).Height = altura;
            for (int c = 1; c <= 11; c++) { ws.Cell(r, c).Clear(XLClearOptions.Contents); ws.Cell(r, c).Style = estilo[c]; }
            var (nome, q, p) = linhas[k];
            ws.Cell(r, 2).Value = nome;
            if (q.HasValue) ws.Cell(r, 3).Value = q.Value == Math.Floor(q.Value) ? (double)(long)q.Value : q.Value;
            if (p.HasValue) ws.Cell(r, 4).Value = p.Value;
            ws.Cell(r, 5).FormulaA1 = $"C{r}*D{r}";
            ws.Cell(r, 7).FormulaA1 = $"B{r}";
            ws.Cell(r, 8).FormulaA1 = $"C{r}";
            ws.Cell(r, 9).FormulaA1 = $"D{r}";
            ws.Cell(r, 10).FormulaA1 = $"H{r}*I{r}";
        }


        // Totais refeitos sobre o bloco novo
        int ultima = primeira + linhas.Count - 1;
        int ultimoVolume = primeira + volumes.Count - 1;
        int primeiraCod = primeira + volumes.Count;
        int ultimaCod = primeiraCod + produtosDNota.Count + (templateKey == "CEDRO" ? 1 : 0) - 1;
        string Soma(char col, int de, int ate) => ate >= de ? $"SUM({col}{de}:{col}{ate})" : "0";

        linhaTotal = primeira + linhas.Count;
        int linhaDesc = LinhaDoRotulo(t => t == "DESCONTOS", 4);
        int linhaPagar = LinhaDoRotulo(t => t == "VALOR A PAGAR", 2);
        if (linhaPagar == 0) linhaPagar = LinhaDoRotulo(t => t == "VALOR A PAGAR", 7);
        ws.Cell(linhaTotal, 3).FormulaA1 = Soma('C', primeira, ultimoVolume);
        ws.Cell(linhaTotal, 8).FormulaA1 = Soma('H', primeira, ultimoVolume);
        if (linhaDesc > 0)
        {
            ws.Cell(linhaDesc, 3).FormulaA1 = Soma('C', primeiraCod, ultimaCod);
            ws.Cell(linhaDesc, 8).FormulaA1 = Soma('H', primeiraCod, ultimaCod);
        }
        if (linhaPagar > 0 && linhaDesc > 0)
        {
            ws.Cell(linhaPagar, 5).FormulaA1 = $"SUM(E{primeira}:E{ultima})+E{linhaTotal}-E{linhaDesc}";
            ws.Cell(linhaPagar, 10).FormulaA1 = $"SUM(J{primeira}:J{ultima})+J{linhaTotal}-J{linhaDesc}";
        }

        // Funrural total → DESCONTOS row col E
        var totalFunrural = grupo.NFs.Where(nf => nf.Funrural.HasValue).Sum(nf => nf.Funrural!.Value);
        foreach (var row in ws.RowsUsed())
        {
            var cellD = row.Cell(4);
            if (cellD.Value.IsText && cellD.Value.GetText().ToUpper() == "DESCONTOS")
            {
                ws.Cell(row.RowNumber(), 5).Value = (double)Math.Round(totalFunrural, 2);
                break;
            }
        }

        // FUNRURAL text cell in col B
        var nfsOrdenados = grupo.NFs.OrderBy(n => n.NF).ToList();
        var partesFunrural = nfsOrdenados
            .Where(n => n.Funrural.HasValue)
            .Select(n => $"(NF {n.NF}  - R$ {n.Funrural!.Value.ToString("N2", new System.Globalization.CultureInfo("pt-BR"))})")
            .ToList();
        var funruralStr = string.Join("  ", partesFunrural);

        foreach (var row in ws.RowsUsed())
        {
            var cellB = row.Cell(2);
            if (cellB.Value.IsText && cellB.Value.GetText().ToUpper().Contains("FUNRURAL"))
            {
                var txt = cellB.Value.GetText().ToUpper().TrimStart();
                var funruralFmt = txt.StartsWith("DESCONTO")
                    ? $"DESCONTO FUNRURAL {funruralStr}"
                    : $"FUNRURAL -  {funruralStr}";
                cellB.Value = funruralFmt;
                ws.Cell(row.RowNumber(), 7).Value = funruralFmt; // G lado direito
                break;
            }
        }


        // valor_a_pagar from filled cells
        decimal? valorAPagar = null;
        try
        {
            double soma = 0, desc = 0;
            foreach (var row in ws.RowsUsed())
            {
                var cC = row.Cell(3); var cD4 = row.Cell(4);
                if (cC.Value.IsNumber && cD4.Value.IsNumber)
                {
                    var q = cC.Value.GetNumber(); var p = cD4.Value.GetNumber();
                    if (q > 0 && p > 0) soma += q * p;
                }
                if (cD4.Value.IsText && cD4.Value.GetText().ToUpper() == "DESCONTOS")
                {
                    var cE = row.Cell(5);
                    if (cE.Value.IsNumber) desc = cE.Value.GetNumber();
                }
            }
            if (soma > 0) valorAPagar = (decimal)(soma - desc);
        }
        catch { }

        // Boleto text — preenche B e G (lado direito)
        var boletoTxt = CalcularBoletosTexto(grupo, apelido, qtdProdutosD, valorAPagar, precoProdutoD);
        foreach (var row in ws.RowsUsed())
        {
            var cellB = row.Cell(2);
            if (cellB.Value.IsText && cellB.Value.GetText().ToUpper().Contains("BOLET"))
            {
                cellB.Value = boletoTxt;
                ws.Cell(row.RowNumber(), 7).Value = boletoTxt; // G (lado direito)
                break;
            }
        }


        var output = new MemoryStream();
        wb.SaveAs(output);
        return (output.ToArray(), semLinha);
    }

    private static string CalcularBoletosTexto(GrupoCliente grupo, string apelido, int qtdProdutosD, decimal? valorAPagar,
        decimal precoProdutoD)
    {
        var estilo = BoletoEstilo.GetValueOrDefault(apelido, "por_nf");
        var nfs = grupo.NFs.OrderBy(n => n.NF).ToList();
        var ptBr = new System.Globalization.CultureInfo("pt-BR");

        if (estilo == "por_nf")
        {
            var partes = nfs.Select(nf => $"({FmtVal(((nf.VNF ?? 0m) - (nf.Funrural ?? 0m)), ptBr)})");
            return "BOLETOS - " + string.Join(" ", partes);
        }
        else if (estilo == "metade")
        {
            decimal boleto;
            if (valorAPagar.HasValue) boleto = Math.Round(valorAPagar.Value / 2, 2);
            else { var tn = nfs.Sum(n => n.VNF ?? 0m); var tf = nfs.Sum(n => n.Funrural ?? 0m); boleto = Math.Round((tn - tf) / 2, 2); }
            return $"BOLETOS ({FmtVal(boleto, ptBr)}) ({FmtVal(boleto, ptBr)})";
        }
        else // metade_cod
        {
            var valorCod = qtdProdutosD * precoProdutoD;
            decimal boletoVolumes;
            if (valorAPagar.HasValue) boletoVolumes = Math.Round((valorAPagar.Value - valorCod) / 2, 2);
            else { var tn = nfs.Sum(n => n.VNF ?? 0m); var tf = nfs.Sum(n => n.Funrural ?? 0m); boletoVolumes = Math.Round((tn - tf - valorCod) / 2, 2); }
            var partes = new List<string> { $"({FmtVal(boletoVolumes, ptBr)})", $"({FmtVal(boletoVolumes, ptBr)})" };
            partes.Add(qtdProdutosD > 0 ? $"({FmtVal(valorCod, ptBr)})" : "(R$ )");
            return "BOLETOS " + string.Join(" ", partes);
        }
    }

    private static string FmtVal(decimal valor, System.Globalization.CultureInfo ptBr)
        => $"R$ {valor.ToString("N2", ptBr)}";

    // Normaliza nome de produto para lookup no dicionário de pesos
    private static string NormProduto(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return string.Join(" ", text.Trim().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Norm(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var normalized = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        var result = sb.ToString().ToUpperInvariant();
        foreach (var rem in new[] { " LTDA", " LT", " ME", ".", ",", "-", "/" })
            result = result.Replace(rem, "");
        return string.Join(" ", result.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
