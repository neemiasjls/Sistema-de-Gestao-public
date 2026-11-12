using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace TransportadoraSystem.Application.Services;

/// <summary>Erro que o cadastro mostra como está (PDF sem texto, arquivo que não abre).</summary>
public class ErroCnh(string mensagem) : Exception(mensagem);

/// <summary>Dados lidos da CNH. Campo nulo = o rótulo não foi achado ou o valor não bateu.</summary>
public record CnhDados(string? Nome, string? Cpf, string? Rg, string? RgOrgaoUf, DateOnly? DataNascimento,
    string? CidadeNascimento, string? UfNascimento, string? CnhRegistro, string? CnhCategoria,
    DateOnly? CnhEmissao, DateOnly? CnhValidade, string? Pai, string? Mae);

/// <summary>
/// Tipo = o que foi reconhecido, para a tela ("CNH digital em PDF, modelo novo"...). Reconhecida = falso quando a
/// imagem não é a da CNH digital (foto da CNH de papel): nada é lido, mas o arquivo pode ser guardado.
/// </summary>
public record CnhLeitura(CnhDados Dados, List<string> NaoAchou, string Tipo = "", bool Reconhecida = true);

/// <summary>
/// Lê o PDF da CNH digital (CNH-e da Senatran / Carteira Digital de Trânsito) com o PdfPig (VIA-20).
/// Não há layout fixo garantido, então a leitura é por rótulo: acha "NOME", "CPF", "VALIDADE"...
/// e pega o valor ao lado (mesma linha) ou logo abaixo, só na coluna do rótulo. O que não acha vai
/// para <see cref="CnhLeitura.NaoAchou"/>; nada é inventado.
///
/// Para afinar com um PDF real: <see cref="Depurar"/> mostra as linhas como o leitor as vê (com a
/// posição), <see cref="Rotulos"/> lista o que ele reconhece como rótulo, e
/// <see cref="LerPalavras"/> lê de uma lista de palavras montada à mão (teste sem PDF).
/// </summary>
public static class CnhLeitor
{
    public record Palavra(int Pagina, string Texto, string Chave, double Esq, double Dir, double Y, double Altura);
    public record Linha(int Pagina, double Y, List<Palavra> Palavras)
    {
        public string Texto => string.Join(' ', Palavras.Select(p => p.Texto));
    }
    public record Rotulo(string Campo, int Pagina, double Esq, double Dir, double Y, HashSet<Palavra> Palavras);

    // ───────────────────────── Rótulos conhecidos ─────────────────────────
    // Para ajustar: Seq = palavras obrigatórias (sem acento, maiúsculas, sem pontuação);
    // Cauda = palavras que, se vierem logo depois, fazem parte do mesmo rótulo.
    // As mais longas primeiro. "primeira", "permissao" etc. só delimitam (não são lidos).
    private static readonly (string Campo, string[] Seq, string[] Cauda)[] Defs =
    [
        ("nascimento", ["DATA", "LOCAL"], ["E", "UF", "DE", "NASCIMENTO"]),
        ("datanasc", ["DATA", "NASCIMENTO"], []),
        ("emissao", ["DATA", "EMISSAO"], []),
        ("emissao", ["EMISSAO"], []),
        ("identidade", ["DOC", "IDENTIDADE"], ["ORG", "EMISSOR", "UF"]),
        ("identidade", ["IDENTIDADE"], ["ORG", "EMISSOR", "UF"]),
        ("cpf", ["CPF"], []),
        ("filiacao", ["FILIACAO"], []),
        ("categoria", ["CAT", "HAB"], []),
        ("categoria", ["CATEGORIA"], []),
        ("registro", ["REGISTRO"], []),
        ("validade", ["VALIDADE"], []),
        ("primeira", ["1A", "HABILITACAO"], []),
        ("primeira", ["PRIMEIRA", "HABILITACAO"], []),
        ("nome", ["NOME"], ["E", "SOBRENOME"]),
        ("permissao", ["PERMISSAO"], []),
        ("acc", ["ACC"], []),
        ("obs", ["OBSERVACOES"], []),
        ("nacionalidade", ["NACIONALIDADE"], []),
    ];

    private static readonly Regex RxData = new(@"\b(\d{2})/(\d{2})/(\d{4})\b", RegexOptions.Compiled);
    private static readonly Regex RxCpf = new(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex RxUf = new(@"^[A-Z]{2}$", RegexOptions.Compiled);

    // ───────────────────────── Entrada ─────────────────────────

    /// <summary>Tipo do arquivo pelo conteúdo (há arquivos sem extensão): "application/pdf", "image/jpeg", "image/png" ou nulo.</summary>
    public static string? TipoArquivo(byte[] b) =>
        b.Length > 4 && b[0] == '%' && b[1] == 'P' && b[2] == 'D' && b[3] == 'F' ? "application/pdf"
        : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length > 8 && b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G' ? "image/png"
        : null;

    // PDF com texto: por rótulo. PDF da CNH digital (só imagem) e JPG/PNG: pelo OCR (CnhOcr)
    public static CnhLeitura Ler(byte[] arquivo)
    {
        switch (TipoArquivo(arquivo))
        {
            case "application/pdf":
                var palavras = Palavras(arquivo);
                return palavras.Any(p => p.Chave == "CPF") ? LerPalavras(palavras) with { Tipo = "CNH em PDF com texto" } : CnhOcr.Ler(arquivo);
            case "image/jpeg" or "image/png":
                return CnhOcr.LerImagem(arquivo);
            default:
                throw new ErroCnh("Este arquivo não é PDF, JPG nem PNG.");
        }
    }

    /// <summary>CPF com 11 números e os dois dígitos verificadores certos ("123.456.789-09"); senão nulo.</summary>
    public static string? CpfConferido(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        // O OCR às vezes pega o número do rótulo junto ("4d CPF", "5 Nº REGISTRO"): valem os 11 do fim, se conferirem
        if (d.Length > 11) d = d[^11..];
        if (d.Length != 11 || d.Distinct().Count() == 1) return null;
        int Dv(int n)
        {
            var soma = 0;
            for (var i = 0; i < n; i++) soma += (d[i] - '0') * (n + 1 - i);
            var r = soma % 11;
            return r < 2 ? 0 : 11 - r;
        }
        return Dv(9) == d[9] - '0' && Dv(10) == d[10] - '0' ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : null;
    }

    /// <summary>
    /// Número de registro da CNH (11 números) com os dois dígitos verificadores do Denatran certos; senão nulo.
    /// </summary>
    public static string? RegistroConferido(string? s)
    {
        var d = new string((s ?? "").Where(char.IsDigit).ToArray());
        // O OCR às vezes pega o número do rótulo junto ("4d CPF", "5 Nº REGISTRO"): valem os 11 do fim, se conferirem
        if (d.Length > 11) d = d[^11..];
        if (d.Length != 11 || d.Distinct().Count() == 1) return null;
        int soma = 0, desconto = 0;
        for (var i = 0; i < 9; i++) soma += (d[i] - '0') * (9 - i);
        var dv1 = soma % 11;
        if (dv1 >= 10) { dv1 = 0; desconto = 2; }
        soma = 0;
        for (var i = 0; i < 9; i++) soma += (d[i] - '0') * (1 + i);
        var x = soma % 11;
        var dv2 = x >= 10 ? 0 : x - desconto;
        if (dv2 < 0) dv2 += 11;
        if (dv2 >= 10) dv2 = 0;
        return dv1 == d[9] - '0' && dv2 == d[10] - '0' ? d : null;
    }

    /// <summary>
    /// Cadastro feito com parte do nome: o nome ou um apelido é o começo do nome da CNH, em palavras
    /// inteiras ("RENATO" acha "RENATO ELIAS BRANDAO"; "RENA" não).
    /// </summary>
    public static bool ComecaComONome(string? nomeCnh, string? nomeCadastro, string? apelidos)
    {
        var cnh = ViagemCaminhaoCalculo.Normalizar(nomeCnh);
        if (cnh.Length == 0) return false;
        return new[] { nomeCadastro }.Concat((apelidos ?? "").Split(','))
            .Select(ViagemCaminhaoCalculo.Normalizar)
            .Any(n => n.Length > 0 && (cnh == n || cnh.StartsWith(n + " ", StringComparison.Ordinal)));
    }

    /// <summary>Texto do PDF linha a linha, com a posição, para ajustar os rótulos.</summary>
    public static string Depurar(byte[] pdf)
    {
        var palavras = Palavras(pdf);
        var sb = new StringBuilder();
        foreach (var l in Linhas(palavras))
            sb.AppendLine($"[pag {l.Pagina}] y={l.Y:0} x={l.Palavras[0].Esq:0}  {l.Texto}");
        sb.AppendLine("— rótulos —");
        foreach (var r in Rotulos(Linhas(palavras)))
            sb.AppendLine($"[pag {r.Pagina}] y={r.Y:0} x={r.Esq:0}-{r.Dir:0}  {r.Campo}");
        return sb.ToString();
    }

    public static List<Palavra> Palavras(byte[] pdf)
    {
        try
        {
            using var doc = PdfDocument.Open(pdf);
            var lista = new List<Palavra>();
            foreach (var pagina in doc.GetPages())
                foreach (var w in pagina.GetWords())
                {
                    var t = w.Text.Trim();
                    if (t.Length == 0) continue;
                    var b = w.BoundingBox;
                    lista.Add(new Palavra(pagina.Number, t, Chave(t), b.Left, b.Right, (b.Bottom + b.Top) / 2, Math.Max(1, b.Height)));
                }
            return lista;
        }
        catch (Exception)
        {
            throw new ErroCnh("Não consegui abrir este PDF. Confira se é o arquivo da CNH digital.");
        }
    }

    public static CnhLeitura LerPalavras(IReadOnlyList<Palavra> palavras)
    {
        if (palavras.Count < 5) throw new ErroCnh("Este PDF é só imagem; preencha à mão.");
        var todas = palavras.ToList();
        var linhas = Linhas(todas);
        var rotulos = Rotulos(linhas);
        var texto = string.Join(' ', linhas.Select(l => l.Texto));

        Rotulo? Primeiro(string campo) => rotulos.FirstOrDefault(r => r.Campo == campo);
        // Candidatos de texto do rótulo: o da mesma linha e depois as linhas de baixo
        List<string> Cands(string campo, int abaixo = 2)
        {
            var r = Primeiro(campo);
            if (r == null) return [];
            var c = new List<string>();
            var mesma = MesmaLinha(r, todas, rotulos);
            if (mesma.Length > 0) c.Add(mesma);
            c.AddRange(Abaixo(r, todas, rotulos, abaixo));
            return c;
        }
        T? Primeira<T>(string campo, Func<string, T?> leitor, int abaixo = 2)
        {
            foreach (var c in Cands(campo, abaixo))
                if (leitor(c) is { } v) return v;
            return default;
        }

        // Nome
        var nome = Primeira("nome", t => Regex.IsMatch(t, @"\p{L}{2}") ? t.ToUpperInvariant() : null, 1);

        // Nascimento: "01/01/1980, CIDADE, GO"; sem ele, a data do rótulo "DATA NASCIMENTO"
        var nasc = Primeira("nascimento", LerNascimento, 1);
        DateOnly? dataNasc = nasc?.data ?? Primeira("datanasc", LerData, 1);

        // Documento de identidade
        var ident = Primeira("identidade", LerIdentidade, 1);

        // CPF: pelo rótulo; senão o primeiro CPF que aparecer no texto
        var cpf = Primeira("cpf", LerCpf, 1);
        if (cpf == null && RxCpf.Match(texto) is { Success: true } mc) cpf = LerCpf(mc.Value);

        // Filiação: uma linha por nome, pai primeiro e mãe depois
        string? pai = null, mae = null;
        if (Primeiro("filiacao") is { } fil)
        {
            var nomes = Abaixo(fil, todas, rotulos, 2).Where(t => Regex.IsMatch(t, @"\p{L}{2}")).Select(t => t.ToUpperInvariant()).ToList();
            if (nomes.Count > 0) pai = nomes[0];
            if (nomes.Count > 1) mae = nomes[1];
        }

        var categoria = Primeira("categoria", t =>
        {
            var s = Regex.Replace(t.ToUpperInvariant(), @"[^A-Z]", "");
            return Regex.IsMatch(s, @"^[A-E]{1,3}$") ? s : null;
        }, 1);
        var registro = Primeira("registro", t => Regex.Match(t, @"\d{9,12}") is { Success: true } m ? m.Value : null, 1);
        var emissao = Primeira("emissao", LerData, 1);
        var validade = Primeira("validade", LerData, 1);

        var dados = new CnhDados(nome, cpf, ident?.rg, ident?.orgaoUf, dataNasc, nasc?.cidade, nasc?.uf,
            registro, categoria, emissao, validade, pai, mae);

        var faltou = new List<string>();
        void Falta(object? v, string rotulo) { if (v == null) faltou.Add(rotulo); }
        Falta(nome, "Nome"); Falta(cpf, "CPF"); Falta(dataNasc, "Data de nascimento");
        
        Falta(dados.Rg, "Documento de identidade"); Falta(dados.RgOrgaoUf, "Órgão emissor e estado do documento");
        Falta(pai, "Pai"); Falta(mae, "Mãe"); Falta(categoria, "Categoria"); Falta(registro, "Número do registro");
        Falta(emissao, "Data de emissão"); Falta(validade, "Validade");
        return new CnhLeitura(dados, faltou);
    }

    // ───────────────────────── Linhas e rótulos ─────────────────────────

    public static string Chave(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormKD).ToUpperInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    // Palavras na mesma altura (meia altura de letra de folga) viram uma linha
    public static List<Linha> Linhas(IEnumerable<Palavra> palavras)
    {
        var res = new List<Linha>();
        foreach (var pag in palavras.GroupBy(p => p.Pagina).OrderBy(g => g.Key))
        {
            Linha? atual = null;
            foreach (var p in pag.OrderByDescending(p => p.Y).ThenBy(p => p.Esq))
            {
                if (atual != null && Math.Abs(p.Y - atual.Y) <= Math.Max(2, p.Altura * 0.5)) atual.Palavras.Add(p);
                else { atual = new Linha(pag.Key, p.Y, [p]); res.Add(atual); }
            }
        }
        foreach (var l in res) l.Palavras.Sort((a, b) => a.Esq.CompareTo(b.Esq));
        return res;
    }

    public static List<Rotulo> Rotulos(IEnumerable<Linha> linhas)
    {
        var res = new List<Rotulo>();
        foreach (var l in linhas)
        {
            // "/" , "," e afins não têm chave: ficam de fora da comparação
            var ps = l.Palavras.Where(p => p.Chave.Length > 0).ToList();
            var i = 0;
            while (i < ps.Count)
            {
                var achou = false;
                foreach (var (campo, seq, cauda) in Defs)
                {
                    if (i + seq.Length > ps.Count) continue;
                    var bate = true;
                    for (var k = 0; k < seq.Length && bate; k++) bate = ps[i + k].Chave == seq[k];
                    if (!bate) continue;
                    var fim = i + seq.Length - 1;
                    while (fim + 1 < ps.Count && cauda.Contains(ps[fim + 1].Chave)) fim++;
                    var usadas = ps.Skip(i).Take(fim - i + 1).ToHashSet();
                    res.Add(new Rotulo(campo, l.Pagina, ps[i].Esq, ps[fim].Dir, l.Y, usadas));
                    i = fim + 1;
                    achou = true;
                    break;
                }
                if (!achou) i++;
            }
        }
        return res;
    }

    private static double Tol(Rotulo r, IEnumerable<Palavra> todas) =>
        Math.Max(2.5, todas.Where(p => r.Palavras.Contains(p)).Select(p => p.Altura).DefaultIfEmpty(8).Max() * 0.5);

    // Fim da coluna: onde começa o próximo rótulo na mesma linha
    private static double LimiteDireito(Rotulo r, IReadOnlyList<Rotulo> rotulos, double tol) =>
        rotulos.Where(x => x != r && x.Pagina == r.Pagina && Math.Abs(x.Y - r.Y) <= tol && x.Esq > r.Esq)
               .Select(x => x.Esq - 2).DefaultIfEmpty(double.MaxValue).Min();

    private static string MesmaLinha(Rotulo r, List<Palavra> todas, IReadOnlyList<Rotulo> rotulos)
    {
        var tol = Tol(r, todas);
        var lim = LimiteDireito(r, rotulos, tol);
        var rotulados = rotulos.SelectMany(x => x.Palavras).ToHashSet();
        return string.Join(' ', todas
            .Where(p => p.Pagina == r.Pagina && Math.Abs(p.Y - r.Y) <= tol && p.Esq >= r.Dir - 1 && p.Esq < lim && !rotulados.Contains(p))
            .OrderBy(p => p.Esq).Select(p => p.Texto)).Trim();
    }

    // As linhas logo abaixo do rótulo, só na coluna dele, até o próximo rótulo dessa coluna
    private static List<string> Abaixo(Rotulo r, List<Palavra> todas, IReadOnlyList<Rotulo> rotulos, int max)
    {
        var tol = Tol(r, todas);
        var dir = LimiteDireito(r, rotulos, tol);
        var esq = r.Esq - 12;
        var rotulados = rotulos.SelectMany(x => x.Palavras).ToHashSet();
        var proximo = rotulos.Where(x => x != r && x.Pagina == r.Pagina && x.Y < r.Y - tol && x.Dir > esq && x.Esq < dir)
                             .Select(x => x.Y).DefaultIfEmpty(double.MinValue).Max();
        var cands = todas
            .Where(p => p.Pagina == r.Pagina && p.Y < r.Y - tol && p.Y > proximo + tol && !rotulados.Contains(p)
                        && (p.Esq + p.Dir) / 2 >= esq && (p.Esq + p.Dir) / 2 < dir)
            .OrderByDescending(p => p.Y).ThenBy(p => p.Esq).ToList();
        var res = new List<string>();
        List<Palavra>? linha = null;
        double yLinha = 0;
        foreach (var p in cands)
        {
            if (linha != null && Math.Abs(p.Y - yLinha) <= Math.Max(2, p.Altura * 0.5)) { linha.Add(p); continue; }
            if (linha != null) res.Add(Juntar(linha));
            if (res.Count >= max) { linha = null; break; }
            linha = [p];
            yLinha = p.Y;
        }
        if (linha != null && res.Count < max) res.Add(Juntar(linha));
        return res;
    }

    private static string Juntar(List<Palavra> l) => string.Join(' ', l.OrderBy(p => p.Esq).Select(p => p.Texto)).Trim();

    // ───────────────────────── Valores ─────────────────────────

    private static DateOnly? LerData(string t)
    {
        var m = RxData.Match(t);
        return m.Success && DateOnly.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string? LerCpf(string t)
    {
        var m = RxCpf.Match(t);
        if (!m.Success) return null;
        var d = Regex.Replace(m.Value, @"\D", "");
        return CpfConferido(d);
    }

    // "01/01/1980, CIDADE, GO" (ou "01/01/1980 CIDADE GO", "01/01/1980 CIDADE/GO")
    private static (DateOnly? data, string? cidade, string? uf)? LerNascimento(string t)
    {
        var m = RxData.Match(t);
        if (!m.Success) return null;
        var data = LerData(t);
        var resto = t.Remove(m.Index, m.Length);
        var partes = resto.Split([',', ';', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        string? uf = null;
        if (partes.Count > 0)
        {
            var ult = partes[^1].ToUpperInvariant();
            if (RxUf.IsMatch(ult)) { uf = ult; partes.RemoveAt(partes.Count - 1); }
            else
            {
                var toks = ult.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length > 1 && RxUf.IsMatch(toks[^1])) { uf = toks[^1]; partes[^1] = string.Join(' ', toks[..^1]); }
            }
        }
        var cidade = string.Join(", ", partes).Trim().ToUpperInvariant();
        return (data, cidade.Length > 0 ? cidade : null, uf);
    }

    // "1234567 SSP GO", "1234567 SSP/GO", "MG-12.345.678 SSP MG"
    private static (string rg, string? orgaoUf)? LerIdentidade(string t)
    {
        var toks = t.Replace('/', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (toks.Length == 0 || !toks.Any(x => x.Any(char.IsDigit))) return null;
        if (toks.Length >= 3 && RxUf.IsMatch(toks[^1].ToUpperInvariant()))
            return (string.Join(' ', toks[..^2]), $"{toks[^2].ToUpperInvariant()}/{toks[^1].ToUpperInvariant()}");
        if (toks.Length == 1) return (toks[0], null);
        return (toks[0], string.Join('/', toks[1..]).ToUpperInvariant());
    }
}
