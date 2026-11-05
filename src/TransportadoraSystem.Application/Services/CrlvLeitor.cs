using System.Text.RegularExpressions;
using static TransportadoraSystem.Application.Services.CnhLeitor;

namespace TransportadoraSystem.Application.Services;

public record CrlvLeitura(string? Placa, string? Especie, string? Tipo, string? Aviso);

/// <summary>
/// Lê o CRLV digital (PDF com texto) para o cadastro de placas (VIA-20): placa, espécie/tipo e, daí,
/// se é cavalo ou carreta. Usa as palavras e linhas do <see cref="CnhLeitor"/>. A placa vem na linha
/// abaixo de "PLACA EXERCÍCIO"; a espécie, logo abaixo do rótulo "ESPÉCIE / TIPO", na mesma coluna.
/// </summary>
public static class CrlvLeitor
{
    private static readonly Regex RxToken = new(@"^[A-Z]{3}\d[A-Z0-9]\d{2}$", RegexOptions.Compiled);
    private static readonly Regex RxTexto = new(@"(?<![A-Z0-9])[A-Z]{3}-?\d[A-Z0-9]\d{2}(?![A-Z0-9])", RegexOptions.Compiled);

    public static CrlvLeitura Ler(byte[] pdf)
    {
        List<Palavra> todas;
        try { todas = Palavras(pdf); }
        catch (ErroCnh) { return new(null, null, null, "Não consegui abrir este PDF."); }
        var palavras = todas.Where(p => p.Pagina == 1).ToList();
        if (palavras.Count == 0) return new(null, null, null, "PDF sem texto (só imagem): cadastre a placa à mão.");

        var linhas = Linhas(palavras);
        var placa = AcharPlaca(linhas, palavras);
        if (placa == null) return new(null, null, null, "Não achei a placa neste PDF. Não parece um CRLV.");

        var especie = AcharEspecie(palavras);
        if (especie == null) return new(placa, null, null, "Não achei a espécie/tipo do veículo.");
        var k = Chave(especie);
        var tipo = k.Contains("REBOQUE") ? "Carreta" : k.Contains("TRATOR") ? "Cavalo" : null;
        return tipo == null
            ? new(placa, especie, null, $"Espécie \"{especie}\": não é cavalo nem carreta.")
            : new(placa, especie, tipo, null);
    }

    private static string? AcharPlaca(List<Linha> linhas, List<Palavra> palavras)
    {
        for (var i = 0; i < linhas.Count; i++)
        {
            var chaves = linhas[i].Palavras.Select(p => p.Chave).ToList();
            if (!chaves.Contains("PLACA") || !chaves.Contains("EXERCICIO")) continue;
            for (var j = i + 1; j < linhas.Count && j <= i + 3; j++)
            {
                var t = linhas[j].Palavras.Select(p => p.Texto.ToUpperInvariant()).FirstOrDefault(x => RxToken.IsMatch(x));
                if (t != null) return ViagemCaminhaoCalculo.NormalizarPlaca(t);
            }
        }
        var m = RxTexto.Match(string.Join(' ', palavras.Select(p => p.Texto.ToUpperInvariant())));
        return m.Success ? ViagemCaminhaoCalculo.NormalizarPlaca(m.Value) : null;
    }

    // Palavra mais próxima abaixo do rótulo, na mesma coluna (x do rótulo); o valor segue pela linha
    // até um vão grande (outra coluna).
    private static string? AcharEspecie(List<Palavra> palavras)
    {
        var rot = palavras.FirstOrDefault(p => p.Chave == "ESPECIE");
        if (rot == null) return null;
        var abaixo = palavras.Where(p => p.Y < rot.Y - 2 && Math.Abs(p.Esq - rot.Esq) <= 8 && p.Chave.Length > 0)
            .OrderByDescending(p => p.Y).FirstOrDefault();
        if (abaixo == null) return null;
        var linha = palavras.Where(p => Math.Abs(p.Y - abaixo.Y) <= Math.Max(2, abaixo.Altura * 0.5) && p.Esq >= abaixo.Esq - 1)
            .OrderBy(p => p.Esq).ToList();
        var partes = new List<string>();
        double fim = abaixo.Esq;
        foreach (var p in linha)
        {
            if (partes.Count > 0 && p.Esq - fim > abaixo.Altura * 2.5) break;
            partes.Add(p.Texto); fim = p.Dir;
        }
        return string.Join(' ', partes);
    }
}
