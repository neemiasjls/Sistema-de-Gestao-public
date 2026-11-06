using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace TransportadoraSystem.Application.Services;

public record StradaContratoPdfNota(string Nf, string? Dacte);

public record StradaContratoPdfLeitura(
    string? Contrato, DateOnly? DataContrato, string? Placa, string? PlacaCarreta,
    string? Motorista, string? CpfMotorista, decimal? ValorContrato, decimal? Seguro,
    decimal? ValorPedagio, decimal? Adiantamento, IReadOnlyList<StradaContratoPdfNota> Notas,
    IReadOnlyList<string> Avisos, string? Recusa)
{
    public bool Aceito => Recusa is null;
}

/// <summary>Lê o “Baixar contrato” da Strada, sem acesso a banco ou arquivos.</summary>
public static class StradaContratoPdfLeitor
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private const string Numero = @"([0-9]+)(?![0-9])";
    private const string Placa = @"([A-Z]{3}\s*-?\s*[0-9][A-Z0-9][0-9]{2})\b";

    public static StradaContratoPdfLeitura Ler(byte[] pdf)
    {
        if (pdf is null || pdf.Length == 0) return Recusar("Arquivo vazio: envie o PDF Baixar contrato da Strada.");
        var inicio = Encoding.UTF8.GetString(pdf, 0, Math.Min(pdf.Length, 4096));
        if (Regex.IsMatch(inicio, @"<!doctype\s+html|<html\b|<head\b|<body\b", RegexOptions.IgnoreCase))
            return Recusar("Este arquivo é HTML de comprovante de pedágio (como comprovante_finalizacao.pdf), não um PDF de contrato. Baixe o arquivo em Baixar contrato na Strada.");

        List<string> linhas;
        try
        {
            using var documento = PdfDocument.Open(pdf);
            linhas = documento.GetPages().SelectMany(Linhas).ToList();
        }
        catch (Exception)
        {
            return Recusar("Não foi possível abrir este PDF. Envie o PDF Baixar contrato da Strada.");
        }
        var texto = string.Join('\n', linhas);
        if (!Regex.IsMatch(texto, @"CONTRATO\s+DE\s+TRANSPORTE\s+RODOVI[ÁA]RIO\s+DE\s+CARGAS", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(texto, @"\bSTRADA\b", RegexOptions.IgnoreCase))
            return Recusar("Este PDF não é um contrato da Strada com texto legível. Envie o arquivo Baixar contrato (PDF sem texto precisa de conferência manual).");

        var avisos = new List<string>();
        string? Campo(string padrao, string nome, string? fonte = null)
        {
            var m = Regex.Match(fonte ?? texto, padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (m.Success) return m.Groups[1].Value.Trim();
            avisos.Add($"Não foi encontrado o campo {nome} no contrato.");
            return null;
        }
        decimal? Valor(string rotulo, string nome)
        {
            var s = Campo(rotulo + @":\s*R\$\s*([0-9]+(?:\.[0-9]{3})*,[0-9]{2})(?![0-9])", nome);
            if (s is null) return null;
            if (decimal.TryParse(s, NumberStyles.Number, PtBr, out var valor)) return valor;
            avisos.Add($"O campo {nome} tem valor inválido: {s}.");
            return null;
        }
        string? LerPlaca(string rotulo, string nome)
        {
            var s = Campo(rotulo + @":\s*" + Placa, nome);
            return s is null ? null : ViagemCaminhaoCalculo.NormalizarPlaca(s);
        }

        var contrato = Campo(@"CARGAS\s+N[.º°o]*\s*:\s*([0-9]{12})(?![0-9])", "número do contrato (12 dígitos)");
        var dataTexto = Campo(@"Data\s+do\s+Contrato:\s*([0-9]{2}/[0-9]{2}/[0-9]{4})", "data do contrato");
        DateOnly? data = null;
        if (dataTexto is not null)
        {
            if (DateOnly.TryParseExact(dataTexto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) data = d;
            else avisos.Add($"Data do contrato inválida: {dataTexto}.");
        }
        var cavalo = LerPlaca(@"Placa\s+Cavalo", "placa do cavalo");
        var carreta = LerPlaca(@"Placa\s+Carreta(?:\s+1)?", "placa da carreta");
        var motorista = Campo(@"Motorista:\s*([^\r\n:]+?)(?=\s+CNH:|\r?\n|$)", "motorista");
        // CPF isolado: não confundir com o CNPJ/CPF do contratado.
        var cpf = Campo(@"(?<![/\p{L}])\bCPF:\s*([0-9]{3}\.?[0-9]{3}\.?[0-9]{3}-?[0-9]{2})(?![0-9])", "CPF do motorista");
        if (cpf is not null) cpf = Regex.Replace(cpf, "[^0-9]", "");
        var valorContrato = Valor(@"Valor\s+Contrato", "valor do contrato");
        var seguro = Valor("Seguro", "seguro");
        var pedagio = Valor(@"Valor\s+(?:do\s+)?Ped[áa]gio", "valor do pedágio");
        var adiantamento = Valor(@"Valor\s+Disp\s+Adto", "adiantamento (Valor Disp Adto)");

        var notas = new List<StradaContratoPdfNota>();
        var detalhamento = false;
        foreach (var linha in linhas)
        {
            if (Regex.IsMatch(linha, @"DETALHAMENTO\s*-\s*NOTAS\s+FISCAIS", RegexOptions.IgnoreCase)) detalhamento = true;
            if (Regex.IsMatch(linha, @"CONDI[ÇC][ÕO]ES\s+CONTRATUAIS|DOCUMENTO\s+AUXILIAR", RegexOptions.IgnoreCase)) detalhamento = false;
            if (!detalhamento) continue;
            foreach (Match m in Regex.Matches(linha, @"N[.º°o]*\s*NF:\s*" + Numero, RegexOptions.IgnoreCase))
            {
                var nf = m.Groups[1].Value.TrimStart('0');
                if (nf.Length == 0) continue;
                var md = Regex.Match(linha, @"DACTE:\s*" + Numero, RegexOptions.IgnoreCase);
                var dacte = md.Success ? md.Groups[1].Value.TrimStart('0') : null;
                if (string.IsNullOrEmpty(dacte))
                {
                    dacte = null;
                    avisos.Add($"Não foi encontrado o DACTE da NF {nf} no detalhamento.");
                }
                var nota = new StradaContratoPdfNota(nf, dacte);
                if (!notas.Contains(nota)) notas.Add(nota);
            }
        }
        if (notas.Count == 0) avisos.Add("Não foram encontradas NFs válidas no detalhamento do contrato (NF 0 é ignorada).");
        return new(contrato, data, cavalo, carreta, motorista, cpf, valorContrato, seguro, pedagio,
            adiantamento, notas.AsReadOnly(), avisos.AsReadOnly(), null);
    }

    private static StradaContratoPdfLeitura Recusar(string mensagem) =>
        new(null, null, null, null, null, null, null, null, null, null, [], [], mensagem);

    // O PDF desenha rótulos e valores com fontes de baselines diferentes (~1,2 pt).
    // Ordenar o conteúdo bruto ou arredondar Y separa os valores dos seus rótulos.
    private static IEnumerable<string> Linhas(Page pagina)
    {
        var linha = new List<Word>();
        double altura = 0;
        foreach (var palavra in pagina.GetWords().OrderByDescending(p => p.BoundingBox.Bottom))
        {
            if (linha.Count > 0 && altura - palavra.BoundingBox.Bottom > 2)
            {
                yield return string.Join(' ', linha.OrderBy(p => p.BoundingBox.Left).Select(p => p.Text));
                linha.Clear();
            }
            if (linha.Count == 0) altura = palavra.BoundingBox.Bottom;
            linha.Add(palavra);
        }
        if (linha.Count > 0) yield return string.Join(' ', linha.OrderBy(p => p.BoundingBox.Left).Select(p => p.Text));
    }
}
