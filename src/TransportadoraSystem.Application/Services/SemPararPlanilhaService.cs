using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// A planilha "N-SEM PARAR -MÊS-AA.xlsx" de um lado e os dados do controle do
/// outro: lê uma planilha pronta (as do Drive têm layouts diferentes de mês para
/// mês, então tudo é achado pelos rótulos) e gera a planilha a partir dos dados,
/// no modelo SEMPARAR.xlsx, crescendo linhas e colunas quando o mês não cabe.
/// </summary>
public class SemPararPlanilhaService
{
    public const string Fatura = "FATURA";
    public const string Vpo = "VPO";
    public const string Credito = "CREDITO";

    /// <summary>Uma linha do controle (ver SemPararLinha).</summary>
    public record Linha(string Tipo, string Placa, DateOnly? Data, decimal Valor, int Ordem);

    public record PlanilhaLida(int Mes, int Ano, List<string> Placas, List<Linha> Linhas, List<string> Avisos);

    public record TotalPlaca(string Placa, decimal Fatura, decimal Creditos, decimal Vpo, decimal Valor);
    public record Totais(decimal Fatura, decimal Creditos, decimal Vpo, decimal Valor, List<TotalPlaca> PorPlaca);

    // ───────────────────────── Placas, mês e nome ─────────────────────────

    /// <summary>"XAA -9457", "xaa9457", "XAA-9457" → "XAA-9457".</summary>
    public static string NormalizarPlaca(string? placa)
    {
        var s = Regex.Replace((placa ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "");
        return s.Length == 7 ? $"{s[..3]}-{s[3..]}" : s;
    }

    public static string MesNome(int m) => m switch
    {
        1 => "JANEIRO", 2 => "FEVEREIRO", 3 => "MARÇO", 4 => "ABRIL",
        5 => "MAIO", 6 => "JUNHO", 7 => "JULHO", 8 => "AGOSTO",
        9 => "SETEMBRO", 10 => "OUTUBRO", 11 => "NOVEMBRO", 12 => "DEZEMBRO",
        _ => "MES",
    };

    /// <summary>SEM-01: "8-SEM PARAR -AGOSTO-26.xlsx".</summary>
    public static string NomeArquivo(int mes, int ano) => $"{mes}-SEM PARAR -{MesNome(mes)}-{ano % 100:00}.xlsx";

    /// <summary>Placas do controle mais as que aparecem nas linhas e não estão na lista (no fim).</summary>
    public static List<string> PlacasCom(IEnumerable<string> placas, IEnumerable<Linha> linhas)
    {
        var lista = placas.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        foreach (var p in linhas.Select(l => l.Placa))
            if (!string.IsNullOrWhiteSpace(p) && !lista.Contains(p)) lista.Add(p);
        return lista;
    }

    public static Totais Calcular(IEnumerable<string> placas, IReadOnlyCollection<Linha> linhas)
    {
        var por = PlacasCom(placas, linhas).Select(p =>
        {
            decimal Soma(string tipo) => linhas.Where(l => l.Placa == p && l.Tipo == tipo).Sum(l => l.Valor);
            var f = Soma(Fatura); var c = Soma(Credito); var v = Soma(Vpo);
            return new TotalPlaca(p, f, c, v, f - c + v);
        }).ToList();
        return new Totais(por.Sum(x => x.Fatura), por.Sum(x => x.Creditos), por.Sum(x => x.Vpo), por.Sum(x => x.Valor), por);
    }

    // ───────────────────────── Ler uma planilha pronta ─────────────────────────

    public PlanilhaLida LerPlanilha(byte[] xlsx, string nomeArquivo)
    {
        using var wb = new XLWorkbook(new MemoryStream(xlsx));
        var ws = wb.Worksheets.First();
        var avisos = new List<string>();
        int ultimaLinha = ws.LastRowUsed()?.RowNumber() ?? 0;

        string Texto(int r, int c) => ws.Cell(r, c).GetString().Trim();
        string TextoA(int r) => SemAcento(Texto(r, 1)).ToUpperInvariant();

        // Mês: "AGOSTO--26" em E2; se não tiver, o nome do arquivo
        var (mes, ano) = MesAno(Texto(2, 5));
        if (mes == 0) (mes, ano) = MesAno(Path.GetFileNameWithoutExtension(nomeArquivo));
        if (mes == 0)
            throw new InvalidOperationException("Não achei o mês da planilha (nem em MÊS REFERENTE nem no nome do arquivo).");

        int linhaPlaca = Enumerable.Range(1, Math.Max(ultimaLinha, 1)).FirstOrDefault(r => TextoA(r) == "PLACA");
        if (linhaPlaca == 0)
            throw new InvalidOperationException("Não achei a linha PLACA: não parece uma planilha do Sem Parar.");

        // Colunas das placas: da B até a coluna RESUMO /DATA
        int ultimaColuna = ws.LastColumnUsed()?.ColumnNumber() ?? 14;
        int colResumo = Enumerable.Range(2, Math.Max(ultimaColuna - 1, 1))
            .FirstOrDefault(c => SemAcento(Texto(linhaPlaca, c)).ToUpperInvariant().Contains("RESUMO"));
        if (colResumo == 0) colResumo = 14;
        var placaDaColuna = new Dictionary<int, string>();
        var placas = new List<string>();
        for (int c = 2; c < colResumo; c++)
        {
            var p = NormalizarPlaca(Texto(linhaPlaca, c));
            if (p.Length == 0) continue;
            placaDaColuna[c] = p;
            if (!placas.Contains(p)) placas.Add(p);
        }

        var linhas = new List<Linha>();
        string Coord(int r, int c) => ws.Cell(r, c).Address.ToString() ?? $"{r},{c}";

        // Lê o valor de uma célula de dado; avisa o que não for número
        bool Valor(int r, int c, out decimal v)
        {
            v = 0;
            var cel = ws.Cell(r, c);
            if (cel.HasFormula)
            {
                if (cel.FormulaA1.Contains("SUM", StringComparison.OrdinalIgnoreCase)) return false;
                if (cel.CachedValue.IsNumber) { v = Math.Round((decimal)cel.CachedValue.GetNumber(), 2); return true; }
                avisos.Add($"{Coord(r, c)}: fórmula sem valor, ficou de fora.");
                return false;
            }
            if (cel.DataType == XLDataType.Number) { v = Math.Round((decimal)cel.GetDouble(), 2); return true; }
            var t = cel.GetString().Trim();
            if (t.Length == 0) return false;
            if (decimal.TryParse(t.Replace("R$", "").Trim(), NumberStyles.Number, new CultureInfo("pt-BR"), out var n))
            { v = Math.Round(n, 2); return true; }
            avisos.Add($"{Coord(r, c)}: \"{t}\" não é valor, ficou de fora.");
            return false;
        }

        string PlacaOuAviso(int r, int c)
        {
            if (placaDaColuna.TryGetValue(c, out var p)) return p;
            avisos.Add($"{Coord(r, c)}: valor numa coluna sem placa no cabeçalho, ficou de fora.");
            return "";
        }

        // FATURA
        int linhaFatura = Enumerable.Range(linhaPlaca, Math.Max(ultimaLinha - linhaPlaca + 1, 1))
            .FirstOrDefault(r => TextoA(r).StartsWith("FATURA"));
        if (linhaFatura == 0) avisos.Add("Não achei a linha FATURA: a fatura das placas ficou vazia.");
        else
            for (int c = 2; c < colResumo; c++)
                if (Valor(linhaFatura, c, out var v))
                {
                    var p = PlacaOuAviso(linhaFatura, c);
                    if (p.Length > 0) linhas.Add(new Linha(Fatura, p, null, v, 0));
                }

        // VALE PEDÁGIO: uma linha da planilha por grupo (Ordem); a data fica na coluna RESUMO
        int ordemVpo = 0;
        for (int r = linhaPlaca + 1; r <= ultimaLinha; r++)
        {
            if (!TextoA(r).StartsWith("VALE PED")) continue;
            var data = LerData(ws.Cell(r, colResumo));
            var daLinha = new List<Linha>();
            for (int c = 2; c < colResumo; c++)
                if (Valor(r, c, out var v))
                {
                    var p = PlacaOuAviso(r, c);
                    if (p.Length > 0) daLinha.Add(new Linha(Vpo, p, data, v, ordemVpo));
                }
            if (daLinha.Count == 0)
            {
                if (data != null) avisos.Add($"Linha {r}: vale-pedágio com data e sem valor, ficou de fora.");
                continue;
            }
            if (data == null) avisos.Add($"Linha {r}: vale-pedágio sem data.");
            linhas.AddRange(daLinha);
            ordemVpo++;
        }

        // CRÉDITOS INSERIDOS DETALHADOS: cabeçalho de placas logo abaixo do título,
        // valores empilhados por coluna até a linha das somas
        int linhaTitulo = 0;
        for (int r = linhaPlaca + 1; r <= ultimaLinha && linhaTitulo == 0; r++)
            for (int c = 1; c <= colResumo; c++)
                if (SemAcento(Texto(r, c)).ToUpperInvariant().Contains("CREDITOS INSERIDOS DETALHADOS")) { linhaTitulo = r; break; }
        if (linhaTitulo == 0) avisos.Add("Não achei o bloco CRÉDITOS INSERIDOS DETALHADOS: os créditos ficaram vazios.");
        else
        {
            // Cabeçalho próprio do bloco; coluna sem nome usa a placa do topo
            for (int c = 2; c < colResumo; c++)
            {
                var p = NormalizarPlaca(Texto(linhaTitulo + 1, c));
                if (p.Length > 0)
                {
                    if (placaDaColuna.TryGetValue(c, out var topo) && topo != p)
                        avisos.Add($"Coluna {ws.Column(c).ColumnLetter()}: {topo} no topo e {p} nos créditos; os créditos ficaram em {p}.");
                    placaDaColuna[c] = p;
                    if (!placas.Contains(p)) placas.Add(p);
                }
            }
            var ordemCred = new Dictionary<string, int>();
            for (int r = linhaTitulo + 2; r <= ultimaLinha; r++)
            {
                bool fim = false;
                for (int c = 1; c <= colResumo && !fim; c++)
                {
                    var cel = ws.Cell(r, c);
                    if (cel.HasFormula && cel.FormulaA1.Contains("SUM", StringComparison.OrdinalIgnoreCase)) fim = true;
                    else if (SemAcento(cel.GetString()).ToUpperInvariant().Contains("TOTAL DE CREDITOS")) fim = true;
                }
                if (fim) break;
                for (int c = 2; c < colResumo; c++)
                    if (Valor(r, c, out var v))
                    {
                        var p = PlacaOuAviso(r, c);
                        if (p.Length == 0) continue;
                        int o = ordemCred.GetValueOrDefault(p);
                        ordemCred[p] = o + 1;
                        linhas.Add(new Linha(Credito, p, null, v, o));
                    }
            }
        }

        return new PlanilhaLida(mes, ano, placas, linhas, avisos);
    }

    private static DateOnly? LerData(IXLCell cel)
    {
        if (cel.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cel.GetDateTime());
        if (cel.DataType == XLDataType.Number)
        {
            var n = cel.GetDouble();
            if (n > 20000 && n < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n));
            return null;
        }
        var s = cel.GetString().Trim();
        foreach (var f in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy" })
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return DateOnly.FromDateTime(d);
        return null;
    }

    // "AGOSTO--26", "8-SEM PARAR -MARÇO -26" → (mês, ano)
    private static (int Mes, int Ano) MesAno(string texto)
    {
        var t = SemAcento(texto).ToUpperInvariant();
        for (int m = 1; m <= 12; m++)
        {
            var nome = SemAcento(MesNome(m));
            var i = t.IndexOf(nome, StringComparison.Ordinal);
            if (i < 0) continue;
            var resto = Regex.Match(t[(i + nome.Length)..], @"(\d{4}|\d{2})(?!\d)");
            if (!resto.Success) return (0, 0);
            int a = int.Parse(resto.Groups[1].Value);
            return (m, a < 100 ? 2000 + a : a);
        }
        return (0, 0);
    }

    private static string SemAcento(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // ───────────────────────── Gerar a planilha a partir dos dados ─────────────────────────

    // Posições do modelo SEMPARAR.xlsx (12 placas de B a M, RESUMO /DATA em N)
    private const int ColunasModelo = 12;
    private const int VpoInicio = 8, VpoLinhasModelo = 34;          // 8 a 41
    private const int CredTitulo = 42, CredInicio = 44, CredLinhasModelo = 23; // 44 a 66

    public byte[] Exportar(int mes, int ano, IEnumerable<string> placasDoControle, IReadOnlyCollection<Linha> linhas)
    {
        linhas = linhas.Where(l => !string.IsNullOrWhiteSpace(l.Placa)).ToList();
        var placas = PlacasCom(placasDoControle, linhas);
        int colunas = Math.Max(ColunasModelo, placas.Count);

        // VPO: uma linha da planilha por Ordem; duas da mesma placa na mesma Ordem
        // não cabem na mesma célula e vão para a linha de baixo
        var linhasVpo = new List<List<Linha>>();
        foreach (var grupo in linhas.Where(l => l.Tipo == Vpo).Select((l, i) => (l, i))
                     .OrderBy(x => x.l.Ordem).ThenBy(x => x.i).GroupBy(x => x.l.Ordem))
        {
            var atual = new List<Linha>();
            foreach (var (l, _) in grupo)
            {
                if (atual.Any(x => x.Placa == l.Placa)) { linhasVpo.Add(atual); atual = []; }
                atual.Add(l);
            }
            linhasVpo.Add(atual);
        }
        var creditos = placas.ToDictionary(p => p, p => linhas.Where(l => l.Tipo == Credito && l.Placa == p)
            .Select((l, i) => (l, i)).OrderBy(x => x.l.Ordem).ThenBy(x => x.i).Select(x => x.l.Valor).ToList());

        int nVpo = Math.Max(VpoLinhasModelo, linhasVpo.Count);
        int nCred = Math.Max(CredLinhasModelo, creditos.Values.Select(v => v.Count).DefaultIfEmpty(0).Max());

        var asm = typeof(SemPararPlanilhaService).Assembly;
        using var stream = asm.GetManifestResourceStream("TransportadoraSystem.Application.Templates.SEMPARAR.xlsx")
            ?? throw new InvalidOperationException("Template SEMPARAR.xlsx não encontrado.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        using var wb = new XLWorkbook(new MemoryStream(ms.ToArray()));
        var ws = wb.Worksheets.First();

        // Mais de 12 placas: colunas novas antes do RESUMO; mais linhas que o modelo: linhas novas
        if (colunas > ColunasModelo)
        {
            ws.Column(1 + ColunasModelo).InsertColumnsAfter(colunas - ColunasModelo);
            for (int c = 2 + ColunasModelo; c <= 1 + colunas; c++) ws.Column(c).Width = ws.Column(1 + ColunasModelo).Width;
        }
        int extraVpo = nVpo - VpoLinhasModelo;
        if (extraVpo > 0) ws.Row(VpoInicio + VpoLinhasModelo - 1).InsertRowsBelow(extraVpo);
        int credInicio = CredInicio + extraVpo;
        int extraCred = nCred - CredLinhasModelo;
        if (extraCred > 0) ws.Row(credInicio + CredLinhasModelo - 1).InsertRowsBelow(extraCred);

        int colResumo = 2 + colunas;
        int vpoFim = VpoInicio + nVpo - 1;
        int credTitulo = CredTitulo + extraVpo, credFim = credInicio + nCred - 1;
        int linhaSoma = credFim + 1, linhaTotal = linhaSoma + 1;
        string L(int c) => XLHelper.GetColumnLetterFromNumber(c);
        string ultima = L(1 + colunas);

        if (colunas > ColunasModelo)
        {
            // O título do bloco de créditos acompanha as colunas novas
            foreach (var m in ws.MergedRanges.Where(m => m.FirstRow().RowNumber() == credTitulo).ToList()) m.Unmerge();
            ws.Range(credTitulo, 2, credTitulo, 1 + colunas).Merge();
        }

        ws.Cell(2, 5).Value = $"{MesNome(mes)}--{ano % 100:00}";   // MÊS REFERENTE
        ws.Cell(3, colResumo).Value = "RESUMO /DATA";

        var colDa = new Dictionary<string, int>();
        for (int i = 0; i < colunas; i++)
        {
            var p = i < placas.Count ? placas[i] : "";
            if (p.Length > 0) colDa[p] = 2 + i;
            ws.Cell(3, 2 + i).Value = p;
            ws.Cell(credTitulo + 1, 2 + i).Value = p;
        }

        foreach (var l in linhas.Where(l => l.Tipo == Fatura).GroupBy(l => l.Placa))
            ws.Cell(4, colDa[l.Key]).Value = (double)l.Sum(x => x.Valor);

        for (int i = 0; i < nVpo; i++)
        {
            int r = VpoInicio + i;
            ws.Cell(r, 1).Value = "VALE PEDÁGIO";
            if (i >= linhasVpo.Count) continue;
            foreach (var l in linhasVpo[i]) ws.Cell(r, colDa[l.Placa]).Value = (double)l.Valor;
            var data = linhasVpo[i].Select(l => l.Data).FirstOrDefault(d => d != null);
            if (data != null)
            {
                var dc = ws.Cell(r, colResumo);
                dc.Value = data.Value.ToDateTime(TimeOnly.MinValue);
                dc.Style.DateFormat.Format = "DD/MM/YYYY";
            }
        }

        foreach (var (p, valores) in creditos)
            for (int i = 0; i < valores.Count; i++)
                ws.Cell(credInicio + i, colDa[p]).Value = (double)valores[i];

        // Fórmulas reescritas para o tamanho deste mês. O VALOR soma todas as linhas
        // de vale-pedágio (o modelo antigo pulava as linhas 32 a 39).
        for (int c = 2; c <= 1 + colunas; c++)
        {
            var x = L(c);
            ws.Cell(5, c).FormulaA1 = $"{x}{linhaSoma}";
            ws.Cell(6, c).FormulaA1 = $"{x}4-{x}5+SUM({x}{VpoInicio}:{x}{vpoFim})";
            ws.Cell(linhaSoma, c).FormulaA1 = $"SUM({x}{credInicio}:{x}{credFim})";
        }
        foreach (var r in new[] { 4, 5, 6 })
            ws.Cell(r, colResumo).FormulaA1 = $"SUM(B{r}:{ultima}{r})";
        ws.Cell(linhaTotal, 2).Value = "TOTAL DE CREDITOS INSERIDOS";
        ws.Cell(linhaTotal, 4).FormulaA1 = $"SUM(B{linhaSoma}:{ultima}{linhaSoma})";

        using var outMs = new MemoryStream();
        wb.SaveAs(outMs);
        return outMs.ToArray();
    }
}
