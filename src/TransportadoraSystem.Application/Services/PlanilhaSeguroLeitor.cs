using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Lê uma planilha SG (Seguro / ICMS) — a gerada na tela de CT-es ou as feitas
/// à mão que estão no Drive — e devolve os totais do mês e por placa.
///
/// Tudo é achado pelos rótulos ("VALOR TOTAL ICMS", "V.FRETE"...), nunca por
/// linha fixa: as planilhas antigas têm linhas em branco com fórmula no meio,
/// a de maio foi salva com 1000 linhas e o RESUMO muda de tamanho com a
/// quantidade de caminhões.
///
/// Os totais principais são a soma das linhas da MÊS-GERAL. Os totais que a
/// própria planilha grava no rodapé também são lidos; quando não batem com a
/// soma, vira aviso em vez de passar calado.
/// </summary>
public static class PlanilhaSeguroLeitor
{
    public sealed class Resumo
    {
        public int? Mes { get; set; }
        public int? Ano { get; set; }
        public string? MesReferente { get; set; }        // o texto da célula MÊS REFERENTE, como está
        public string? OrigemMes { get; set; }           // "nome", "planilha" ou "datas"

        // Soma das linhas da MÊS-GERAL
        public int QuantidadeCtes { get; set; }
        public int QuantidadeCancelados { get; set; }
        public decimal TotalValorNF { get; set; }
        public decimal TotalFrete { get; set; }
        public decimal TotalSeguro { get; set; }
        public decimal TotalIcms { get; set; }

        // O que a planilha grava no rodapé da MÊS-GERAL
        public decimal? TotalValorNFPlanilha { get; set; }
        public decimal? TotalFretePlanilha { get; set; }
        public decimal? TotalSeguroPlanilha { get; set; }
        public decimal? TotalIcmsPlanilha { get; set; }
        public decimal? AliquotaIcms { get; set; }       // em %, como na planilha (14,18755)
        public decimal? ValorTotalSeguros { get; set; }  // seguro + IOF + diferença (= PORTO SEGUROS)
        public decimal? IofSeguros { get; set; }
        public decimal? DiferencaSeguros { get; set; }
        public decimal? ServicoEscritorio { get; set; }
        public decimal? TaxaBSoft { get; set; }
        public int? QuantidadeCaminhoes { get; set; }
        public decimal? EncargosPorCaminhao { get; set; } // VALOR DIVD.

        // Aba RESUMO
        public decimal? PortoSeguros { get; set; }
        public decimal? DasReferente { get; set; }
        public decimal? TotalDespesas { get; set; }

        public List<Placa> Placas { get; set; } = [];
        public List<string> Avisos { get; set; } = [];
    }

    public sealed class Placa
    {
        public string Nome { get; set; } = "";
        public int QuantidadeCtes { get; set; }
        public decimal ValorNF { get; set; }
        public decimal Frete { get; set; }
        public decimal Seguro { get; set; }
        public decimal Icms { get; set; }
        public decimal? TotalCaminhao { get; set; }      // a linha "CAMINHÃO X" do RESUMO (ICMS + seguro + encargos)
    }

    private static readonly string[] Meses =
        ["JANEIRO", "FEVEREIRO", "MARCO", "ABRIL", "MAIO", "JUNHO",
         "JULHO", "AGOSTO", "SETEMBRO", "OUTUBRO", "NOVEMBRO", "DEZEMBRO"];

    // Diferença que ainda conta como "bate" entre a soma das linhas e o rodapé
    private const decimal Tolerancia = 0.05m;

    public static Resumo Ler(byte[] xlsx, string? nomeArquivo = null)
    {
        using var ms = new MemoryStream(xlsx);
        using var wb = new XLWorkbook(ms);
        var r = new Resumo();

        var geral = wb.Worksheets.FirstOrDefault(w => Norm(w.Name).Contains("GERAL"))
                    ?? wb.Worksheets.FirstOrDefault(w => Norm(w.Name) != "RESUMO")
                    ?? throw new InvalidOperationException("A planilha não tem a aba MÊS-GERAL.");

        var rotulos = Rotulos(geral);
        var datas = LerLinhas(geral, rotulos, r);
        LerRodape(geral, rotulos, r);

        var resumo = wb.Worksheets.FirstOrDefault(w => Norm(w.Name) == "RESUMO");
        var porCaminhao = new Dictionary<string, (string nome, decimal? valor)>();
        if (resumo != null) LerResumo(resumo, r, porCaminhao);
        else r.Avisos.Add("A planilha não tem a aba RESUMO: o total de despesas por caminhão ficou de fora.");

        LigarCaminhoes(wb, r, porCaminhao);
        DescobrirMes(r, nomeArquivo, datas);
        Conferir(r);
        return r;
    }

    // ───────────────────────── MÊS-GERAL ─────────────────────────

    private static List<DateTime> LerLinhas(IXLWorksheet ws, List<(string texto, int linha, int coluna)> rotulos, Resumo r)
    {
        var datas = new List<DateTime>();
        int ultimaLinha = ws.LastRowUsed()?.RowNumber() ?? 0;

        // Cabeçalho: a linha que tem DATA, PLACA e V.FRETE
        int cab = 0;
        var col = new Dictionary<string, int>();
        for (int l = 1; l <= Math.Min(ultimaLinha, 15) && cab == 0; l++)
        {
            var nomes = new Dictionary<string, int>();
            foreach (var c in ws.Row(l).CellsUsed())
            {
                var t = Norm(Texto(c));
                if (t.Length > 0 && !nomes.ContainsKey(t)) nomes[t] = c.Address.ColumnNumber;
            }
            if (nomes.ContainsKey("DATA") && nomes.ContainsKey("PLACA") && nomes.ContainsKey("VFRETE"))
            { cab = l; col = nomes; }
        }
        if (cab == 0)
            throw new InvalidOperationException("Não achei o cabeçalho (DATA, PLACA, V.FRETE) na aba MÊS-GERAL.");

        foreach (var obrigatoria in new[] { "VALORNF", "CTE", "VSEGURO", "VICMS" })
            if (!col.ContainsKey(obrigatoria))
                r.Avisos.Add($"A coluna {obrigatoria switch
                {
                    "VALORNF" => "VALOR NF", "VSEGURO" => "V. SEGURO", "VICMS" => "V.ICMS", _ => obrigatoria,
                }} não está na MÊS-GERAL: o total dela ficou zerado.");

        // As linhas acabam no rodapé (VALOR TOTAL NOTAS FISCAIS)
        int fim = rotulos.Where(x => x.texto == "VALORTOTALNOTASFISCAIS" && x.linha > cab)
                         .Select(x => x.linha - 1).DefaultIfEmpty(ultimaLinha).Min();

        var placas = new Dictionary<string, Placa>();
        for (int l = cab + 1; l <= fim; l++)
        {
            var celData = ws.Cell(l, col["DATA"]);
            var vData = Valor(celData);
            if (Norm(Texto(celData)) == "CANCELADO") { r.QuantidadeCancelados++; continue; }

            double? cte = Pega(ws, l, col, "CTE");
            var placa = Texto(ws.Cell(l, col["PLACA"])).Trim().ToUpperInvariant();
            double? frete = Pega(ws, l, col, "VFRETE");
            var data = Data(vData);

            // Linha em branco (as planilhas antigas têm várias, só com a fórmula dando 0)
            if (cte == null && data == null && placa.Length == 0 && (frete ?? 0) == 0) continue;

            r.QuantidadeCtes++;
            if (data != null) datas.Add(data.Value);
            var nf = Dec(Pega(ws, l, col, "VALORNF"));
            var fr = Dec(frete);
            var seg = Dec(Pega(ws, l, col, "VSEGURO"));
            var icms = Dec(Pega(ws, l, col, "VICMS"));
            r.TotalValorNF += nf; r.TotalFrete += fr; r.TotalSeguro += seg; r.TotalIcms += icms;

            var chave = placa.Length > 0 ? placa : "SEM PLACA";
            if (!placas.TryGetValue(chave, out var p)) placas[chave] = p = new Placa { Nome = chave };
            p.QuantidadeCtes++; p.ValorNF += nf; p.Frete += fr; p.Seguro += seg; p.Icms += icms;
        }

        r.TotalValorNF = Din(r.TotalValorNF); r.TotalFrete = Din(r.TotalFrete);
        r.TotalSeguro = Din(r.TotalSeguro); r.TotalIcms = Din(r.TotalIcms);
        foreach (var p in placas.Values)
        { p.ValorNF = Din(p.ValorNF); p.Frete = Din(p.Frete); p.Seguro = Din(p.Seguro); p.Icms = Din(p.Icms); }
        r.Placas = placas.Values.OrderBy(p => p.Nome, StringComparer.Ordinal).ToList();

        if (placas.ContainsKey("SEM PLACA"))
            r.Avisos.Add($"{placas["SEM PLACA"].QuantidadeCtes} CT-e(s) da MÊS-GERAL estão sem placa.");
        if (r.QuantidadeCtes == 0)
            r.Avisos.Add("A aba MÊS-GERAL não tem nenhum CT-e.");
        return datas;
    }

    private static void LerRodape(IXLWorksheet ws, List<(string texto, int linha, int coluna)> rotulos, Resumo r)
    {
        decimal? Achar(string rotulo)
        {
            var x = rotulos.FirstOrDefault(t => t.texto == rotulo);
            return x.texto == null ? null : ValorAoLado(ws, x.linha, x.coluna);
        }

        r.TotalValorNFPlanilha = Din(Achar("VALORTOTALNOTASFISCAIS"));
        r.TotalFretePlanilha = Din(Achar("VALORTOTALDEFRETES") ?? Achar("FATMENSAL"));
        r.TotalSeguroPlanilha = Din(Achar("VALORSEGUROSMENSAL"));
        r.TotalIcmsPlanilha = Din(Achar("VALORTOTALICMS"));
        var aliq = Achar("ALIQUOTAICMSFRETE");
        r.AliquotaIcms = aliq == null ? null : Math.Round(aliq.Value, 6);
        r.ValorTotalSeguros = Din(Achar("VALORTOTALSEGUROS"));
        r.IofSeguros = Din(Achar("IOFSEGUROS"));
        r.DiferencaSeguros = Din(Achar("DIFERENCASEGUROS"));
        r.ServicoEscritorio = Din(Achar("SERVICOSESCRITORIO") ?? Achar("SERVICOESCRITORIO"));
        r.TaxaBSoft = Din(Achar("TAXABSOFT"));
        var qde = Achar("QDECAMINHOES");
        r.QuantidadeCaminhoes = qde == null ? null : (int)Math.Round(qde.Value);
        r.EncargosPorCaminhao = Din(Achar("VALORDIVD"));

        // MÊS REFERENTE: o primeiro texto depois do rótulo
        var mr = rotulos.FirstOrDefault(t => t.texto == "MESREFERENTE");
        if (mr.texto != null)
        {
            int ultimaCol = ws.Row(mr.linha).LastCellUsed()?.Address.ColumnNumber ?? mr.coluna;
            for (int c = mr.coluna + 1; c <= ultimaCol; c++)
            {
                var t = Texto(ws.Cell(mr.linha, c)).Trim();
                if (t.Length > 0) { r.MesReferente = t; break; }
            }
        }
    }

    // ───────────────────────── RESUMO ─────────────────────────

    private static void LerResumo(IXLWorksheet ws, Resumo r, Dictionary<string, (string nome, decimal? valor)> porCaminhao)
    {
        var rotulos = Rotulos(ws);
        int totais = 0;
        foreach (var (texto, linha, coluna) in rotulos)
        {
            if (texto.StartsWith("CAMINHAO") && texto.Length > "CAMINHAO".Length)
            {
                var original = Texto(ws.Cell(linha, coluna)).Trim();
                var nome = Regex.Replace(original, @"^\s*CAMINH\S*\s*", "", RegexOptions.IgnoreCase).Trim().ToUpperInvariant();
                var chave = texto["CAMINHAO".Length..];
                porCaminhao.TryAdd(chave, (nome, Din(ValorAoLado(ws, linha, coluna))));
            }
            else if (texto == "TOTALDESPESAS")
            {
                // O primeiro TOTAL DESPESAS soma os caminhões; o segundo, seguro +
                // DAS + escritório + taxa. As duas contas dão o mesmo número.
                if (totais++ == 0) r.TotalDespesas = Din(ValorAoLado(ws, linha, coluna));
            }
            else if (texto == "PORTOSEGUROS") r.PortoSeguros = Din(ValorAoLado(ws, linha, coluna));
            else if (texto.StartsWith("DASREFERENTE")) r.DasReferente = Din(ValorAoLado(ws, linha, coluna));
        }
    }

    // Junta o total de cada caminhão do RESUMO à placa da MÊS-GERAL. Caminhão
    // que está no RESUMO com valor mas sem CT-e no mês entra também, com aviso.
    private static void LigarCaminhoes(XLWorkbook wb, Resumo r, Dictionary<string, (string nome, decimal? valor)> porCaminhao)
    {
        bool temResumo = porCaminhao.Count > 0;

        // Primeiro as placas escritas igual; depois, entre as que sobraram, a
        // placa digitada com um erro no RESUMO ("XAB-4258" no lugar de
        // "XAB-4C58", "XAK-4B51" no lugar de "XAK-3B51"). Só junta quando há
        // um candidato só, bem parecido.
        var semPar = new List<Placa>();
        foreach (var p in r.Placas)
        {
            if (p.Nome == "SEM PLACA") continue;
            var chave = Norm(p.Nome);
            if (porCaminhao.Remove(chave, out var c)) p.TotalCaminhao = c.valor;
            else semPar.Add(p);
        }
        foreach (var p in semPar.ToList())
        {
            var chave = Norm(p.Nome);
            var perto = porCaminhao.Keys.Select(k => (k, d: Distancia(k, chave))).Where(x => x.d <= 2).ToList();
            if (perto.Count == 0) continue;
            int menor = perto.Min(x => x.d);
            var melhores = perto.Where(x => x.d == menor).ToList();
            if (melhores.Count != 1) continue;
            // Outra placa sem par tão parecida com a mesma linha: deixa quieto
            if (semPar.Any(o => o != p && Distancia(Norm(o.Nome), melhores[0].k) <= menor)) continue;
            var (nome, valor) = porCaminhao[melhores[0].k];
            porCaminhao.Remove(melhores[0].k);
            p.TotalCaminhao = valor;
            semPar.Remove(p);
            r.Avisos.Add($"No RESUMO o caminhão {p.Nome} está escrito {nome}; o total dele ({Brl(valor ?? 0)}) foi ligado à placa {p.Nome}.");
        }

        foreach (var p in semPar)
        {
            var chave = Norm(p.Nome);

            // Sem linha no RESUMO: usa o VALOR TOTAL da aba da própria placa
            var aba = wb.Worksheets.FirstOrDefault(w => Norm(w.Name) == chave);
            if (aba != null)
            {
                var vt = Rotulos(aba).FirstOrDefault(t => t.texto == "VALORTOTAL");
                if (vt.texto != null) p.TotalCaminhao = Din(ValorAoLado(aba, vt.linha, vt.coluna));
            }
            if (temResumo)
                r.Avisos.Add($"O caminhão {p.Nome} tem CT-e no mês mas não aparece no RESUMO"
                             + (p.TotalCaminhao != null ? $"; o total dele ({Brl(p.TotalCaminhao.Value)}) veio da aba da placa." : "."));
        }

        foreach (var (_, (nome, valor)) in porCaminhao)
        {
            if ((valor ?? 0) == 0) continue;   // caminhão parado no mês (ou placa digitada errada) com zero
            r.Placas.Add(new Placa { Nome = nome, TotalCaminhao = valor });
            r.Avisos.Add($"O caminhão {nome} está no RESUMO com {Brl(valor!.Value)} mas não tem CT-e na MÊS-GERAL.");
        }
    }

    // ───────────────────────── Mês e ano ─────────────────────────

    // "09-SEGURO SETEMBRO 2026.xlsx" (antes "08-SG AGOSTO 2026.xlsx") → (mês, ano). O número manda; o nome do mês só confere.
    public static (int Mes, int Ano)? MesDoNome(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return null;
        var m = Regex.Match(Norm2(Path.GetFileNameWithoutExtension(nome)), @"^\s*(\d{1,2})\s*-\s*(?:SEGURO|SG)\b\D*?(\d{4})");
        if (!m.Success) return null;
        int mes = int.Parse(m.Groups[1].Value), ano = int.Parse(m.Groups[2].Value);
        return mes is >= 1 and <= 12 && ano is >= 2000 and <= 2100 ? (mes, ano) : null;
    }

    // "AGOSTO  DE  2026", "JANEIRO--2025" → (mês, ano)
    public static (int Mes, int Ano)? MesDoTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = Norm2(texto);
        int mes = Array.FindIndex(Meses, m => Regex.IsMatch(t, $@"\b{m}\b")) + 1;
        var a = Regex.Match(t, @"\b(20\d{2})\b");
        return mes > 0 && a.Success ? (mes, int.Parse(a.Groups[1].Value)) : null;
    }

    private static void DescobrirMes(Resumo r, string? nomeArquivo, List<DateTime> datas)
    {
        var doNome = MesDoNome(nomeArquivo);
        var daCelula = MesDoTexto(r.MesReferente);
        (int Mes, int Ano)? dasDatas = datas.Count == 0 ? null
            : datas.GroupBy(d => (d.Month, d.Year)).OrderByDescending(g => g.Count()).First().Key;

        // Ordem: nome do arquivo, depois a célula MÊS REFERENTE. A célula foi
        // digitada à mão nas planilhas antigas e às vezes está com o ano errado
        // ("JANEIRO--2025" com CT-es de 2026): se discorda das datas dos CT-es,
        // valem as datas.
        (int Mes, int Ano)? escolhido;
        if (doNome != null) { escolhido = doNome; r.OrigemMes = "nome"; }
        else if (daCelula != null && (dasDatas == null || dasDatas == daCelula)) { escolhido = daCelula; r.OrigemMes = "planilha"; }
        else if (dasDatas != null) { escolhido = dasDatas; r.OrigemMes = "datas"; }
        else { escolhido = daCelula; r.OrigemMes = daCelula != null ? "planilha" : null; }

        r.Mes = escolhido?.Mes;
        r.Ano = escolhido?.Ano;
        if (escolhido == null) return;

        string Nome((int Mes, int Ano) x) => $"{NomeMes(x.Mes)} de {x.Ano}";
        if (daCelula != null && daCelula != escolhido)
            r.Avisos.Add($"A célula MÊS REFERENTE diz \"{r.MesReferente}\", mas a planilha é de {Nome(escolhido.Value)}.");
        if (dasDatas != null && dasDatas != escolhido)
            r.Avisos.Add($"A maior parte dos CT-es é de {Nome(dasDatas.Value)}, mas a planilha é de {Nome(escolhido.Value)}.");
    }

    public static string NomeMes(int mes) =>
        mes is >= 1 and <= 12
            ? new[] { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho",
                      "agosto", "setembro", "outubro", "novembro", "dezembro" }[mes - 1]
            : "";

    // ───────────────────────── Conferência ─────────────────────────

    private static void Conferir(Resumo r)
    {
        void Bate(string oQue, decimal soma, decimal? planilha)
        {
            if (planilha == null) { r.Avisos.Add($"Não achei o total de {oQue} no rodapé da MÊS-GERAL."); return; }
            if (Math.Abs(soma - planilha.Value) > Tolerancia)
                r.Avisos.Add($"O total de {oQue} gravado na planilha ({Brl(planilha.Value)}) não bate com a soma das linhas ({Brl(soma)}).");
        }
        if (r.QuantidadeCtes == 0) return;
        Bate("valor das notas", r.TotalValorNF, r.TotalValorNFPlanilha);
        Bate("frete", r.TotalFrete, r.TotalFretePlanilha);
        Bate("seguro", r.TotalSeguro, r.TotalSeguroPlanilha);
        Bate("ICMS", r.TotalIcms, r.TotalIcmsPlanilha);

        var somaCaminhoes = r.Placas.Sum(p => p.TotalCaminhao ?? 0);
        if (r.TotalDespesas != null && r.Placas.Any(p => p.TotalCaminhao != null)
            && Math.Abs(somaCaminhoes - r.TotalDespesas.Value) > Tolerancia)
            r.Avisos.Add($"A soma dos caminhões lidos ({Brl(somaCaminhoes)}) não bate com o TOTAL DESPESAS do RESUMO ({Brl(r.TotalDespesas.Value)}).");
    }

    // ───────────────────────── Ajudantes ─────────────────────────

    // Todos os textos da aba, já normalizados, na ordem de leitura
    private static List<(string texto, int linha, int coluna)> Rotulos(IXLWorksheet ws)
    {
        var lista = new List<(string, int, int)>();
        foreach (var c in ws.CellsUsed())
        {
            var v = Valor(c);
            if (!v.IsText) continue;
            var t = Norm(v.GetText());
            if (t.Length > 0) lista.Add((t, c.Address.RowNumber, c.Address.ColumnNumber));
        }
        return lista.OrderBy(x => x.Item2).ThenBy(x => x.Item3).ToList();
    }

    // O primeiro número à direita do rótulo, na mesma linha. Para no próximo
    // texto: rótulo sem valor ("DIFERENÇA SEGUROS" vazio) devolve nulo em vez
    // de pegar o número do rótulo seguinte.
    private static decimal? ValorAoLado(IXLWorksheet ws, int linha, int coluna)
    {
        int ultima = Math.Max(ws.Row(linha).LastCellUsed()?.Address.ColumnNumber ?? coluna, coluna);
        for (int c = coluna + 1; c <= ultima; c++)
        {
            var cel = ws.Cell(linha, c);
            if (cel.IsMerged() && cel.MergedRange().FirstCell().Address.ColumnNumber != c) continue;
            var v = Valor(cel);
            if (v.IsNumber) return (decimal)v.GetNumber();
            if (v.IsText && v.GetText().Trim().Length > 0) return null;
        }
        return null;
    }

    private static double? Pega(IXLWorksheet ws, int linha, Dictionary<string, int> col, string nome)
    {
        if (!col.TryGetValue(nome, out var c)) return null;
        var v = Valor(ws.Cell(linha, c));
        return v.IsNumber ? v.GetNumber() : null;
    }

    // Planilha salva pelo Excel traz o valor da fórmula já calculado; a gerada
    // pelo sistema (ClosedXML) não traz, e aí a fórmula é calculada aqui.
    private static XLCellValue Valor(IXLCell c)
    {
        if (!c.HasFormula) return c.Value;
        var guardado = c.CachedValue;
        if (!guardado.IsBlank) return guardado;
        try { return c.Value; } catch { return Blank.Value; }
    }

    private static string Texto(IXLCell c)
    {
        var v = Valor(c);
        return v.IsText ? v.GetText() : v.IsBlank ? "" : v.ToString(CultureInfo.InvariantCulture);
    }

    private static DateTime? Data(XLCellValue v)
    {
        if (v.IsDateTime) return v.GetDateTime();
        // Data sem formato de data chega como número serial do Excel
        if (v.IsNumber && v.GetNumber() is > 40000 and < 60000) return DateTime.FromOADate(v.GetNumber());
        return null;
    }

    // Quantas letras trocadas, faltando, sobrando ou invertidas (vizinhas)
    // separam duas placas
    private static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int custo = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + custo);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    private static decimal Dec(double? v) => v is null or double.NaN ? 0 : (decimal)v.Value;
    private static decimal Din(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static decimal? Din(decimal? v) => v == null ? null : Din(v.Value);

    // Maiúsculas, sem acento e só letras e números: "V. SEGURO" → "VSEGURO"
    private static string Norm(string? s)
    {
        var sb = new StringBuilder();
        foreach (var ch in Norm2(s)) if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    // Maiúsculas e sem acento, mas com os espaços e traços
    private static string Norm2(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().ToUpperInvariant();
    }

    // "R$ 1.234,56" sem depender da cultura do servidor
    public static string Brl(decimal v) =>
        "R$ " + v.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(",", "#").Replace(".", ",").Replace("#", ".");
}
