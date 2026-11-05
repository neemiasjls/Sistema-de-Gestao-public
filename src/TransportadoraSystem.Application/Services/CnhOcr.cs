using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;
using UglyToad.PdfPig;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// CNH digital exportada da Carteira Digital de Trânsito: o PDF não tem texto, só duas imagens
/// (963×680; modelo de 2021 com frente e verso, modelo novo com três) com os campos sempre no mesmo lugar (VIA-20). Cada campo é recortado,
/// ampliado e passado a preto e branco (a letra é fina sobre fundo verde) e lido sozinho pelo
/// Tesseract (linha de comando, instalado na imagem Docker da API). Valor que não bate com o
/// formato fica nulo; nada é inventado.
/// </summary>
public static class CnhOcr
{
    // Faixa do valor de cada campo, em pixels do modelo 963×680 (escala pelo tamanho real):
    // x0, x1, centro da linha e meia altura. Pai e mãe têm uma segunda linha (nome comprido quebra).
    private record Caixa(string Campo, bool Verso, int X0, int X1, int Cy, string? Letras, int Meia = 17);

    // Modelo de 2021 (só frente e verso): emissão fica no verso
    private static readonly Caixa[] Antigo =
    [
        new("nome", false, 160, 900, 195, Maiusculas),
        new("identidade", false, 484, 900, 258, Misto),
        new("cpf", false, 484, 712, 323, Algarismos),
        new("nascimento", false, 734, 900, 323, Algarismos),
        new("pai", false, 484, 900, 391, Maiusculas),
        new("pai2", false, 484, 900, 427, Maiusculas),
        new("mae", false, 484, 900, 463, Maiusculas),
        new("mae2", false, 484, 900, 498, Maiusculas),
        new("categoria", false, 806, 900, 566, Maiusculas),
        new("registro", false, 158, 460, 632, Algarismos),
        new("validade", false, 484, 672, 632, Algarismos),
        new("emissao", true, 696, 900, 420, Algarismos),
    ];

    // Modelo novo (2022+, "MINISTÉRIO DOS TRANSPORTES", três imagens): tudo na frente
    private static readonly Caixa[] Novo =
    [
        new("nome", false, 172, 770, 205, Maiusculas),
        new("nascimento", false, 440, 940, 260, Misto),
        new("emissao", false, 440, 600, 318, Algarismos),
        new("validade", false, 615, 770, 318, Algarismos),
        new("identidade", false, 440, 940, 371, Misto),
        new("cpf", false, 440, 625, 427, Algarismos),
        new("registro", false, 632, 805, 427, Algarismos),
        new("categoria", false, 820, 940, 427, Maiusculas),
        new("pai", false, 440, 940, 537, Maiusculas, 14),
        new("pai2", false, 440, 940, 567, Maiusculas, 14),
        new("mae", false, 440, 940, 597, Maiusculas, 14),
        new("mae2", false, 440, 940, 627, Maiusculas, 14),
    ];
    private const string Maiusculas = "ABCDEFGHIJKLMNOPQRSTUVWXYZÁÂÃÀÉÊÍÓÔÕÚÇ ";
    private const string Numeros = "0123456789";
    // Poucos conjuntos de letras = poucas chamadas do Tesseract; o formato de cada campo é conferido depois
    private const string Algarismos = Numeros + "./-", Misto = Maiusculas + Numeros + "/,";
    private const int Escala = 3;
    // Corte do preto e branco no canal verde: a letra do modelo antigo é cinza-clara e fina (com 190 o 7 virava 1)
    private const int LimiarAntigo = 235, LimiarNovo = 190;

    public static string Executavel =>
        Environment.GetEnvironmentVariable("TESSERACT_PATH") is { Length: > 0 } p ? p
        : OperatingSystem.IsWindows() ? @"C:\Program Files\Tesseract-OCR\tesseract.exe" : "tesseract";

    public static CnhLeitura Ler(byte[] pdf)
    {
        var imgs = Imagens(pdf);
        using var bf = SKBitmap.Decode(imgs[0]) ?? throw new ErroCnh("Não consegui abrir a imagem da CNH; preencha à mão.");
        using var bv = imgs.Count > 1 ? SKBitmap.Decode(imgs[1]) : null;

        // Modelo novo vem com três imagens (frente, categorias, verso); se o CPF não sair, tenta o outro
        var primeiro = imgs.Count >= 3 ? Novo : Antigo;
        var r = LerModelo(primeiro, bf, bv);
        if (r.Dados.Cpf == null)
        {
            var outro = LerModelo(primeiro == Novo ? Antigo : Novo, bf, bv);
            if (outro.NaoAchou.Count < r.NaoAchou.Count) r = outro;
        }
        return r;
    }

    private static CnhLeitura LerModelo(Caixa[] caixas, SKBitmap bf, SKBitmap? bv)
    {
        // Um Tesseract por conjunto de letras, com todos os recortes dele: abrir o programa e carregar o
        // português é o que mais demora (no servidor de 0,1 CPU, ~3 s por chamada)
        var lidos = new Dictionary<string, string?>();
        foreach (var grupo in caixas.Where(c => (c.Verso ? bv : bf) != null).GroupBy(c => c.Letras))
        {
            var lista = grupo.ToList();
            var textos = Ocr(lista.Select(c => Recorte(c.Verso ? bv! : bf, c.X0, c.X1, c.Cy, c.Meia, caixas == Antigo ? LimiarAntigo : LimiarNovo)).ToList(), grupo.Key);
            for (var i = 0; i < lista.Count; i++) lidos[lista[i].Campo] = textos[i];
        }
        string? Campo(string nome) => lidos.GetValueOrDefault(nome);

        // "01/01/1980, CIDADE, UF": vale a primeira data do campo
        DateOnly? Data(string nome)
        {
            var t = Campo(nome);
            if (t == null) return null;
            var m = Regex.Match(t, @"(\d{2})\s*/\s*(\d{2})\s*/\s*(\d{4})");
            var dig = m.Success ? m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value : Digitos(t);
            return dig.Length == 8 && DateOnly.TryParseExact(dig, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }

        string? Nome(string campo) => Limpo(Campo(campo)) is { Length: >= 3 } n ? n : null;

        // Pai e mãe: nome comprido quebra na linha de baixo (no modelo antigo, até no meio da palavra)
        string? Filiacao(string campo)
        {
            var l1 = Nome(campo);
            if (l1 == null) return null;
            var l2 = Limpo(Campo(campo + "2"));
            if (l2 is not { Length: >= 2 }) return l1;
            return l1 + (caixas == Antigo && l1.Length >= 26 ? "" : " ") + l2;
        }

        var cpfDig = Digitos(Campo("cpf"));
        var cpf = cpfDig.Length == 11 ? $"{cpfDig[..3]}.{cpfDig[3..6]}.{cpfDig[6..9]}-{cpfDig[9..]}" : null;

        // "23605437 SSP SP": número e depois órgão/UF
        string? rg = null, orgao = null;
        if (Limpo(Campo("identidade")) is { } ident)
        {
            var m = Regex.Match(ident, @"^([\dX]{5,})\s+([A-Z ]+)$");
            if (m.Success) { rg = m.Groups[1].Value; orgao = m.Groups[2].Value.Trim(); }
            else if (Regex.IsMatch(ident, @"^\d{5,}$")) rg = ident;
        }

        var reg = Digitos(Campo("registro"));
        var cat = Campo("categoria") is { } ct && Regex.Match(ct.Replace(" ", ""), @"^A?[BCDE]$|^A$") is { Success: true } mc ? mc.Value : null;

        var dados = new CnhDados(Nome("nome"), cpf, rg, orgao, Data("nascimento"), null, null,
            reg.Length == 11 ? reg : null, cat, Data("emissao"), Data("validade"), Filiacao("pai"), Filiacao("mae"));

        var faltou = new List<string>();
        void Falta(object? v, string r) { if (v == null) faltou.Add(r); }
        Falta(dados.Nome, "Nome"); Falta(dados.Cpf, "CPF"); Falta(dados.DataNascimento, "Data de nascimento");
        Falta(dados.Rg, "Documento de identidade"); Falta(dados.Pai, "Pai"); Falta(dados.Mae, "Mãe");
        Falta(dados.CnhCategoria, "Categoria"); Falta(dados.CnhRegistro, "Número do registro");
        Falta(dados.CnhEmissao, "Data de emissão"); Falta(dados.CnhValidade, "Validade");
        return new CnhLeitura(dados, faltou);
    }

    private static string? Limpo(string? s) => s == null ? null : Regex.Replace(s, @"\s+", " ").Trim() is { Length: > 0 } t ? t : null;

    private static string Digitos(string? s) => s == null ? "" : new string(s.Where(char.IsDigit).ToArray());

    // As imagens grandes da página (deitadas, ~1,4:1), de cima para baixo: a primeira é a frente
    private static List<byte[]> Imagens(byte[] pdf)
    {
        List<(double topo, byte[] bytes)> imgs = [];
        try
        {
            using var doc = PdfDocument.Open(pdf);
            foreach (var p in doc.GetPages())
                foreach (var im in p.GetImages())
                {
                    if (im.WidthInSamples < 600 || im.WidthInSamples < im.HeightInSamples * 1.2) continue;
                    var bytes = im.TryGetPng(out var png) ? png : im.RawBytes.ToArray();
                    imgs.Add((im.Bounds.Top - p.Number * 10000, bytes));
                }
        }
        catch (Exception) { throw new ErroCnh("Não consegui abrir este PDF. Confira se é o arquivo da CNH digital."); }
        if (imgs.Count == 0) throw new ErroCnh("Este PDF não tem texto nem a imagem da CNH; preencha à mão.");
        return imgs.OrderByDescending(i => i.topo).Select(i => i.bytes).ToList();
    }

    // Faixa do campo ampliada 3× em preto e branco, com margem branca, em PNG
    private static byte[] Recorte(SKBitmap src, int x0, int x1, int cy, int meia, int limiar)
    {
        double sx = src.Width / 963.0, sy = src.Height / 680.0;
        var r = SKRectI.Create((int)(x0 * sx), (int)((cy - meia) * sy), (int)((x1 - x0) * sx), (int)(2 * meia * sy));
        r.Intersect(new SKRectI(0, 0, src.Width, src.Height));
        const int margem = 20;
        using var dst = new SKBitmap(new SKImageInfo(r.Width * Escala + 2 * margem, r.Height * Escala + 2 * margem, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(dst))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(src, r, SKRect.Create(margem, margem, r.Width * Escala, r.Height * Escala), paint);
        }
        // Texto da CNH é cinza ou vermelho e o fundo é verde-claro: no canal verde o fundo é quase
        // branco e as duas cores de texto ficam escuras (só a luminância apagava o vermelho)
        // Direto nos bytes (RGBA): GetPixel/SetPixel pixel a pixel levava segundos no servidor
        var px = dst.GetPixelSpan().ToArray();
        var cinza = new byte[dst.Width * dst.Height];
        for (var i = 0; i < cinza.Length; i++) cinza[i] = px[i * 4 + 1] < limiar ? (byte)0 : (byte)255;
        using var pb = new SKBitmap(new SKImageInfo(dst.Width, dst.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(cinza, 0, pb.GetPixels(), cinza.Length);
        using var img = SKImage.FromBitmap(pb);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // Vários recortes numa chamada: o Tesseract recebe a lista dos arquivos e separa o texto de cada
    // um com quebra de página (\f). Se a conta não bater, lê um por um.
    private static List<string?> Ocr(List<byte[]> pngs, string? letras)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "cnh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arqs = pngs.Select((p, i) => { var a = Path.Combine(pasta, i + ".png"); File.WriteAllBytes(a, p); return a; }).ToList();
            if (arqs.Count == 1) return [Texto(Tesseract(arqs[0], letras))];
            var lista = Path.Combine(pasta, "lista.txt");
            File.WriteAllLines(lista, arqs);
            var partes = Tesseract(lista, letras).Split('\f');
            if (partes.Length >= arqs.Count) return partes.Take(arqs.Count).Select(Texto).ToList();
            return arqs.Select(a => Texto(Tesseract(a, letras))).ToList();
        }
        finally { try { Directory.Delete(pasta, true); } catch { } }
    }

    private static string? Texto(string s) => Regex.Replace(s, @"\s+", " ").Trim() is { Length: > 0 } t ? t : null;

    private static string Tesseract(string entrada, string? letras)
    {
        try
        {
            var psi = new ProcessStartInfo(Executavel)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            // Uma thread só: com CPU limitada, as threads do Tesseract só disputam entre si
            psi.Environment["OMP_THREAD_LIMIT"] = "1";
            var args = new List<string> { entrada, "stdout", "-l", "por", "--psm", "7" };
            if (letras != null) { args.Add("-c"); args.Add("tessedit_char_whitelist=" + letras); }
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var pr = Process.Start(psi) ?? throw new ErroCnh("Leitor de imagem (OCR) indisponível; preencha à mão.");
            var saida = pr.StandardOutput.ReadToEndAsync();
            var erro = pr.StandardError.ReadToEndAsync();
            if (!pr.WaitForExit(60_000)) { try { pr.Kill(); } catch { } throw new ErroCnh("A leitura da imagem demorou demais; tente de novo."); }
            pr.WaitForExit();
            if (pr.ExitCode != 0) Console.Error.WriteLine($"CNH: tesseract saiu com {pr.ExitCode}: {erro.Result.Trim()}");
            return saida.Result;
        }
        catch (System.ComponentModel.Win32Exception) { throw new ErroCnh("Leitor de imagem (OCR) não instalado no servidor; preencha à mão."); }
    }
}
