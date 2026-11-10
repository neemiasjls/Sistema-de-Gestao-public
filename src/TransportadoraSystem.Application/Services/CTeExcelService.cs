using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

public record CTeLinha(
    string Data,
    string Fornecedor,
    string UfFornecedor,
    string Cliente,
    string UfCliente,
    int?   NF,
    double? ValorNF,
    int?   Cte,
    string Placa,
    double? VFrete,
    double? Aliquota
);

public class CTeExcelService
{
    // Alíquotas do seguro por UF (em %). As que valem ficam no banco (AliquotasSeguro,
    // SEG-06); estas são só as de começo, para quando ninguém passa a tabela.
    public static readonly IReadOnlyList<(string uf, double aliq)> AliquotasPadrao =
        [("GO", 0.069), ("DF", 0.069), ("MG", 0.069), ("TO", 0.121), ("PA", 0.147), ("SP", 0.069)];

    private static readonly XNamespace NsCte = "http://www.portalfiscal.inf.br/cte";

    // ── Parsing ─────────────────────────────────────────────────────
    public List<CTeLinha> ParsearXmls(IEnumerable<string> xmlContents,
        IReadOnlyList<(string uf, double aliq)>? aliquotasSeguro = null)
    {
        var lista = new List<CTeLinha>();
        var aliquotas = (aliquotasSeguro ?? AliquotasPadrao)
            .GroupBy(a => a.uf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(gp => gp.Key, gp => gp.First().aliq, StringComparer.OrdinalIgnoreCase);

        foreach (var xml in xmlContents)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                var root = doc.Root!;

                // CT-e cancelado
                var tpEvento = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "tpEvento");
                if (tpEvento?.Value == "110111")
                {
                    var chCTe = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "chCTe")?.Value ?? "";
                    int? numCanc = chCTe.Length >= 34 ? int.Parse(chCTe.Substring(25, 9)) : null;
                    lista.Add(new CTeLinha("CANCELADO", "CANCELADO", "CANCELADO", "CANCELADO", "CANCELADO",
                        null, null, numCanc, "", null, null));
                    continue;
                }

                var infCte = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "infCte");
                if (infCte == null) continue;

                var ufCliente    = Get(infCte, "dest",  "UF");
                var ufFornecedor = Get(infCte, "rem",   "UF");
                var fornecedor   = Get(infCte, "rem",   "xNome");
                var cliente      = Get(infCte, "dest",  "xNome");
                var dhEmi        = Get(infCte, "ide",   "dhEmi");
                var nCT          = Get(infCte, "ide",   "nCT");
                var vTPrest      = Get(infCte, "vPrest","vTPrest");
                var vCarga       = Get(infCte, "infCarga", "vCarga");
                var chave        = infCte.Descendants().FirstOrDefault(e => e.Name.LocalName == "chave")?.Value ?? "";
                var placa        = Get(infCte, "veicTracao", "placa");
                if (string.IsNullOrEmpty(placa))
                    placa = Get(infCte, "veic", "placa");
                if (string.IsNullOrEmpty(placa))
                    placa = ObterPlacaXObs(infCte);

                int? nf = chave.Length >= 34 ? int.Parse(chave.Substring(25, 9)) : null;

                lista.Add(new CTeLinha(
                    Data:         !string.IsNullOrEmpty(dhEmi) ? dhEmi[..10] : "",
                    Fornecedor:   fornecedor,
                    UfFornecedor: ufFornecedor,
                    Cliente:      cliente,
                    UfCliente:    ufCliente,
                    NF:           nf,
                    ValorNF:      string.IsNullOrEmpty(vCarga)   ? null : double.Parse(vCarga,   System.Globalization.CultureInfo.InvariantCulture),
                    Cte:          string.IsNullOrEmpty(nCT)      ? null : int.Parse(nCT),
                    Placa:        placa,
                    VFrete:       string.IsNullOrEmpty(vTPrest)  ? null : double.Parse(vTPrest,  System.Globalization.CultureInfo.InvariantCulture),
                    Aliquota:     aliquotas.TryGetValue(ufCliente, out var al) ? al : null
                ));
            }
            catch { /* ignora XML inválido */ }
        }

        return lista.OrderBy(l => l.Cte).ToList();
    }

    // Alíquota ICMS frete = valor do DAS ÷ soma de todos os fretes.
    // Retorna null se não houver DAS ou fretes (planilha sai como antes).
    private static double? CalcAliquotaIcms(List<CTeLinha> linhas, double? valorDas)
    {
        if (!valorDas.HasValue || valorDas.Value <= 0) return null;
        var somaFrete = linhas.Where(l => l.VFrete.HasValue).Sum(l => l.VFrete!.Value);
        return somaFrete > 0 ? valorDas.Value / somaFrete : null;
    }

    // ── Gera Excel geral (cte_geral.py) ─────────────────────────────
    public byte[] GerarGeral(List<CTeLinha> linhas, double? valorDas = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("CT-es");
        var aliqIcms = CalcAliquotaIcms(linhas, valorDas);
        // No geral, a soma do ICMS tem que bater EXATO com o DAS
        EscreverPlanilha(ws, linhas, aliqIcms, aliqIcms.HasValue ? valorDas : null);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── Gera ZIP com planilha por placa (cte_placas.py) ─────────────
    public byte[] GerarPorPlaca(List<CTeLinha> linhas, double? valorDas = null)
    {
        // Mesma alíquota global (do DAS do mês inteiro) em todos os arquivos
        var aliqIcms = CalcAliquotaIcms(linhas, valorDas);
        var grupos = linhas.GroupBy(l => string.IsNullOrEmpty(l.Placa) ? "SEM-PLACA" : l.Placa);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var grp in grupos)
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("CT-es");
                EscreverPlanilha(ws, grp.ToList(), aliqIcms, totalIcmsExato: null);

                using var planMs = new MemoryStream();
                wb.SaveAs(planMs);

                var entry = zip.CreateEntry($"CTE-{grp.Key}.xlsx");
                using var entryStream = entry.Open();
                planMs.Position = 0;
                planMs.CopyTo(entryStream);
            }
        }

        ms.Position = 0;
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Planilha SG (arquivo único): MÊS-GERAL + 1 aba por placa + RESUMO
    // ═══════════════════════════════════════════════════════════════════
    // FaturaSeguro: valor da fatura do seguro (SEG-05). AliquotasSeguro: a tabela
    // ALIQUOTA SEGURO CARGA do rodapé (a mesma usada nas linhas); nula = a padrão.
    public record ParametrosSG(double DasValor, double IofSeguros, double ServicoEscritorio,
        double TaxaBSoft, string MesReferente, double FaturaSeguro,
        IReadOnlyList<(string uf, double aliq)>? AliquotasSeguro = null);

    private const string GERAL = "MÊS-GERAL";

    /// <summary>VALOR SEGUROS MENSAL: a soma da coluna L (valor da NF × alíquota / 100).</summary>
    public static double SegurosMensal(IEnumerable<CTeLinha> linhas) =>
        linhas.Where(l => l.ValorNF.HasValue && l.Aliquota.HasValue)
              .Sum(l => l.ValorNF!.Value * l.Aliquota!.Value / 100);

    /// <summary>DIFERENÇA SEGUROS (SEG-05) = fatura − (seguros mensal + IOF).</summary>
    public static double DiferencaSeguros(IEnumerable<CTeLinha> linhas, double fatura, double iof) =>
        fatura - (SegurosMensal(linhas) + iof);

    // Cores (fundo) — resolvidas do tema do modelo
    private const string cCinza = "#7C7C7C", cCinza2 = "#767171", cAmbar = "#FFC000",
        cAmbarClaro = "#FFD966", cAmbarEsc = "#BF9000", cVermelho = "#FF0000", cVerdeEsc = "#548235",
        cVerde = "#00B050", cAzulClaro = "#8FAADC", cLimao = "#99FF33", cCiano = "#00B0F0",
        cBranco = "#FFFFFF", cConfHdr = "#808080";
    // Cores (fonte)
    private const string fPreto = "#000000", fVermEsc = "#C00000", fVerm = "#FF0000",
        fAzulK = "#0000FF", fAzulB = "#0070C0";
    // Formatos numéricos ("R$ " com espaço nos dados/resumo; "R$" sem espaço nos totais)
    private const string nfRsSp = "\"R$ \"#,##0.00", nfRs = "\"R$\"#,##0.00";

    public byte[] GerarSG(List<CTeLinha> linhas, ParametrosSG p)
    {
        const XLAlignmentHorizontalValues R = XLAlignmentHorizontalValues.Right;
        const XLAlignmentHorizontalValues C = XLAlignmentHorizontalValues.Center;
        const XLAlignmentHorizontalValues L = XLAlignmentHorizontalValues.Left;

        using var wb = new XLWorkbook();
        double somaFrete = linhas.Where(l => l.VFrete.HasValue).Sum(l => l.VFrete!.Value);
        // QDE CAMINHÕES não é mais digitada: conta as placas distintas que vieram nos
        // CT-es do mês. É o mesmo conjunto que vira uma aba cada (ver o laço das abas
        // por placa), então o divisor do VALOR DIVD. sempre bate com as abas geradas.
        int qdeCaminhoes = linhas.Where(l => !string.IsNullOrEmpty(l.Placa))
                                 .Select(l => l.Placa).Distinct().Count();
        // Alíquota (%) do DAS — valor cheio pra soma do ICMS bater EXATO com o DAS
        double aliqIcmsPct = (somaFrete > 0 && p.DasValor > 0) ? p.DasValor / somaFrete * 100 : 0;

        double[] wGeral = [12.57, 25.14, 7.14, 40.71, 7.0, 14.14, 17.14, 9.57, 12.14, 15.71, 10.0, 16.14, 16.14];
        double[] wPlaca = [12.57, 19.57, 7.14, 28.57, 7.14, 9.14, 18.14, 9.57, 12.14, 18.57, 10.0, 14.29, 16.14, 15.86];

        // ═════════ MÊS-GERAL ═════════
        var g = wb.AddWorksheet(GERAL);
        int n = linhas.Count, B = n + 4, gUlt = n + 3;
        for (int c = 0; c < wGeral.Length; c++) g.Column(c + 1).Width = wGeral[c];
        CabecalhoSG(g, ehPlaca: false, p.MesReferente);
        EscreverLinhasSG(g, linhas, $"M{B}");

        // linha B
        MR(g, B, 1, 6, 12, true, fPreto, cCinza, R).FirstCell().Value = "VALOR TOTAL NOTAS FISCAIS";
        SC(g, B, 7, 12, true, fPreto, cCinza, C, nfRs).FormulaA1 = $"SUM(G4:G{gUlt})";
        MR(g, B, 8, 9, 12, true, fPreto, cCinza, R).FirstCell().Value = "FAT. MENSAL";
        SC(g, B, 10, 12, true, fPreto, cCinza, C, nfRs).FormulaA1 = $"SUM(J4:J{gUlt})";
        MR(g, B, 11, 12, 12, true, fPreto, cAmbar, R).FirstCell().Value = "ALIQUOTA ICMS FRETE";
        SC(g, B, 13, 12, true, fVermEsc, cAmbar, C, "#,##0.00000").Value = aliqIcmsPct;
        // linha B+1
        MR(g, B + 1, 7, 9, 12, true, fPreto, cVermelho, R).FirstCell().Value = "VALOR SEGUROS MENSAL";
        SC(g, B + 1, 10, 12, true, fPreto, cVermelho, C, nfRs).FormulaA1 = $"SUM(L4:L{gUlt})";
        MR(g, B + 1, 11, 12, 12, true, fPreto, cCinza2, R).FirstCell().Value = "VALOR TOTAL ICMS";
        SC(g, B + 1, 13, 12, true, fPreto, cCinza2, C, nfRs).FormulaA1 = $"SUM(M4:M{gUlt})";
        // linha B+2
        MR(g, B + 2, 1, 2, 11, true, fPreto, null, C).FirstCell().Value = "ALIQUOTA SEGURO CARGA";
        MR(g, B + 2, 6, 7, 12, true, fPreto, cVerdeEsc, R).FirstCell().Value = "VALOR TOTAL DE FRETES";
        MR(g, B + 2, 8, 9, 12, true, fPreto, cVerdeEsc, C, nfRs).FirstCell().FormulaA1 = $"SUM(J4:J{gUlt})";
        MR(g, B + 2, 10, 13, 12, true, fPreto, cVerde, C).FirstCell().Value = "ENCARGOS SEGUROS CARGAS ";
        // tabela seguro (esquerda) GO..PA
        var TabelaSeguro = p.AliquotasSeguro ?? AliquotasPadrao;
        for (int i = 0; i < TabelaSeguro.Count; i++)
        {
            SC(g, B + 3 + i, 1, 11, true, fVerm, null, C).Value = TabelaSeguro[i].uf;
            SC(g, B + 3 + i, 2, 10, true, fAzulB, null, C, "#,##0.0000").Value = TabelaSeguro[i].aliq;
        }
        // direita
        MR(g, B + 3, 8, 9, 11, true, fPreto, cAzulClaro, C).FirstCell().Value = "VALOR TOTAL SEGUROS";
        SC(g, B + 3, 10, 11, true, fPreto, cAzulClaro, C, nfRsSp).FormulaA1 = $"J{B + 1}+M{B + 3}+M{B + 4}";
        MR(g, B + 3, 11, 12, 12, true, fPreto, cLimao, R).FirstCell().Value = "IOF SEGUROS";
        SC(g, B + 3, 13, 12, true, fPreto, cLimao, C, nfRs).Value = p.IofSeguros;
        MR(g, B + 4, 11, 12, 12, true, fPreto, cLimao, R).FirstCell().Value = "DIFERENÇA SEGUROS";
        // fatura − (seguros mensal + IOF): o VALOR TOTAL SEGUROS fica igual à fatura (SEG-05)
        SC(g, B + 4, 13, 12, true, fPreto, cBranco, C, nfRs).FormulaA1 =
            $"{p.FaturaSeguro.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}-J{B + 1}-M{B + 3}";
        MR(g, B + 5, 11, 12, 12, true, fPreto, cLimao, R).FirstCell().Value = "SERVIÇOS ESCRITÓRIO";
        SC(g, B + 5, 13, 12, true, fPreto, cBranco, C, nfRs).Value = p.ServicoEscritorio;
        MR(g, B + 6, 11, 12, 12, true, fPreto, cLimao, R).FirstCell().Value = "TAXA B SOFT   ";
        SC(g, B + 6, 13, 12, true, fPreto, cBranco, C, nfRs).Value = p.TaxaBSoft;
        MR(g, B + 7, 11, 12, 12, true, fPreto, cLimao, R).FirstCell().Value = "QDE CAMINHÕES";
        SC(g, B + 7, 13, 12, true, fPreto, cCiano, C, "#,##0").Value = qdeCaminhoes;
        SC(g, B + 8, 12, 12, true, fPreto, cLimao, R).Value = "VALOR DIVD.";
        SC(g, B + 8, 13, 12, true, fPreto, cLimao, C, nfRs).FormulaA1 = $"SUM(M{B + 3}:M{B + 6})/M{B + 7}";
        Bordas(g, B + 8, 13);

        // refs do geral
        string refAliq = $"'{GERAL}'!M{B}";
        string refDivd = $"'{GERAL}'!M{B + 8}";
        string refTotSeguros = $"'{GERAL}'!J{B + 3}";
        string refTotIcms = $"'{GERAL}'!M{B + 1}";
        string refServico = $"'{GERAL}'!M{B + 5}";
        string refTaxa = $"'{GERAL}'!M{B + 6}";

        // ═════════ Abas por placa (cancelados sem placa ficam só no MÊS-GERAL) ═════════
        var placasResumo = new List<(string nome, int totalRow)>();
        foreach (var grp in linhas.Where(l => !string.IsNullOrEmpty(l.Placa))
                                   .GroupBy(l => l.Placa).OrderBy(x => x.Key))
        {
            var ls = grp.ToList();
            int m = ls.Count, P = m + 4, pUlt = m + 3;
            var ws = wb.AddWorksheet(grp.Key.Length > 31 ? grp.Key[..31] : grp.Key);
            for (int c = 0; c < wPlaca.Length; c++) ws.Column(c + 1).Width = wPlaca[c];
            CabecalhoSG(ws, ehPlaca: true, null);
            EscreverLinhasSG(ws, ls, $"M{P}");   // V.ICMS usa a alíquota da própria aba (= MÊS-GERAL)

            MR(ws, P, 1, 6, 12, true, fPreto, cCinza, R).FirstCell().Value = "VALOR TOTAL NOTAS FISCAIS";
            SC(ws, P, 7, 12, true, fPreto, cCinza, C, nfRs).FormulaA1 = $"SUM(G4:G{pUlt})";
            MR(ws, P, 8, 9, 12, true, fPreto, cCinza, R).FirstCell().Value = "FAT. MENSAL";
            SC(ws, P, 10, 12, true, fPreto, cCinza, C, nfRs).FormulaA1 = $"SUM(J4:J{pUlt})";
            MR(ws, P, 11, 12, 12, true, fPreto, cAmbar, R).FirstCell().Value = "ALIQUOTA ICMS FRETE";
            SC(ws, P, 13, 12, true, fVermEsc, cAmbar, C, "#,##0.0").FormulaA1 = refAliq;
            MR(ws, P + 1, 7, 9, 12, true, fPreto, cVermelho, R).FirstCell().Value = "VALOR SEGUROS MENSAL";
            SC(ws, P + 1, 10, 12, true, fPreto, cVermelho, C, nfRs).FormulaA1 = $"SUM(L4:L{pUlt})";
            MR(ws, P + 1, 11, 12, 12, true, fPreto, cCinza2, R).FirstCell().Value = "VALOR TOTAL ICMS";
            SC(ws, P + 1, 13, 12, true, fPreto, cCinza2, C, nfRs).FormulaA1 = $"SUM(M4:M{pUlt})";
            SC(ws, P + 1, 14, 12, true, fPreto, cAmbar, C, nfRsSp).FormulaA1 = $"SUM(N4:N{P})";
            MR(ws, P + 2, 11, 12, 12, true, fPreto, cAmbarClaro, R).FirstCell().Value = "ENCARGOS SEGURO";
            SC(ws, P + 2, 13, 12, true, fPreto, cAmbarClaro, R, nfRs).FormulaA1 = refDivd;
            SC(ws, P + 3, 12, 12, true, fPreto, cAmbarClaro, C).Value = "VALOR TOTAL";
            SC(ws, P + 3, 13, 12, true, fPreto, cAmbarClaro, R, nfRs).FormulaA1 = $"M{P + 1}+J{P + 1}+M{P + 2}";
            Bordas(ws, P + 3, 14);
            placasResumo.Add((ws.Name, P + 3));
        }

        // ═════════ RESUMO ═════════
        var res = wb.AddWorksheet("RESUMO");
        double[] wRes = [27.29, 17.86, 25.86, 26.0, 21.0];
        for (int c = 0; c < wRes.Length; c++) res.Column(c + 1).Width = wRes[c];
        res.Row(1).Height = 27;
        MR(res, 1, 1, 4, 18, true, fPreto, cAmbarEsc, C).FirstCell().Value =
            "CONTROLE DE DESPESAS  CAMINHÕES SEGURO E ICMS CARGAS";
        MR(res, 2, 1, 2, 16, true, fPreto, cAmbarEsc, R).FirstCell().Value = " SEGURO CARGAS /ICMS ";
        MR(res, 2, 3, 4, 16, false, fVerm, cBranco, L).FirstCell().FormulaA1 = $"'{GERAL}'!F2";
        int rr = 3;
        foreach (var (nome, totalRow) in placasResumo)
        {
            MR(res, rr, 1, 3, 14, true, fPreto, null, R).FirstCell().Value = $"CAMINHÃO  {nome}";
            SC(res, rr, 4, 16, false, fPreto, null, C, nfRsSp).FormulaA1 = $"'{nome}'!M{totalRow}";
            res.Row(rr).Height = 21;
            rr++;
        }
        MR(res, rr, 1, 3, 16, true, fPreto, null, R).FirstCell().Value = "TOTAL DESPESAS";
        SC(res, rr, 4, 16, true, fVermEsc, null, C, nfRsSp).FormulaA1 = $"SUM(D3:D{rr - 1})";
        int d = rr + 2;
        MR(res, d, 1, 4, 16, true, fPreto, cAmbarEsc, C).FirstCell().Value = "SEGUROS /ICMS /ESCRITÓRIO E OUTROS";
        (string lbl, string formula)[] despesas =
        [
            ("PORTO SEGUROS", refTotSeguros), ("DAS REFERENTE AO MÊS", refTotIcms),
            ("SERVIÇO ESCRITÓRIO", refServico), ("TAXA B SOFT", refTaxa),
        ];
        for (int i = 0; i < despesas.Length; i++)
        {
            MR(res, d + 1 + i, 1, 3, 14, true, fPreto, null, R).FirstCell().Value = despesas[i].lbl;
            SC(res, d + 1 + i, 4, 16, false, fPreto, null, C, nfRsSp).FormulaA1 = despesas[i].formula;
        }
        MR(res, d + 6, 1, 3, 16, true, fPreto, null, R).FirstCell().Value = "TOTAL DESPESAS";
        SC(res, d + 6, 4, 16, true, fVermEsc, null, C, nfRsSp).FormulaA1 = $"SUM(D{d + 1}:D{d + 5})";
        for (int row = 2; row <= d + 6; row++) if (res.Row(row).Height < 21) res.Row(row).Height = 21;
        res.Range(1, 1, d + 6, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        res.Range(1, 1, d + 6, 4).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Cabeçalho (linhas 1-3) igual ao modelo
    private static void CabecalhoSG(IXLWorksheet ws, bool ehPlaca, string? mesRef)
    {
        var R = XLAlignmentHorizontalValues.Right;
        var C = XLAlignmentHorizontalValues.Center;
        var Lf = XLAlignmentHorizontalValues.Left;
        int lastCol = ehPlaca ? 14 : 13;
        for (int r = 1; r <= 3; r++) ws.Row(r).Height = 18.75;

        MR(ws, 1, 1, lastCol, 14, true, fPreto, cCinza, C).FirstCell().Value =
            "CONTROLE  DE  ICMS DE TRANSPORTES  DE VOLUMES  E OUTROS";
        MR(ws, 2, 1, 5, 14, true, fPreto, cCinza, R).FirstCell().Value = "MÊS REFERENTE";
        var mes = MR(ws, 2, 6, lastCol, 14, true, fVermEsc, cBranco, Lf);
        if (ehPlaca) mes.FirstCell().FormulaA1 = $"'{GERAL}'!F2";
        else mes.FirstCell().Value = mesRef ?? "";

        string[] h = ["DATA", "FORNECEDOR", "UF", "CLIENTE", "UF", "NF", "VALOR NF", "CTE",
                      "PLACA", "V.FRETE", "ALIQ.", "V. SEGURO", "V.ICMS", "CONFERÊNCIA"];
        for (int c = 0; c < lastCol; c++)
        {
            double sz = c == 0 ? 14 : 12;
            string fill = c == 13 ? cConfHdr : cCinza;   // N (CONFERÊNCIA) tem fundo diferente
            SC(ws, 3, c + 1, sz, true, fPreto, fill, c == 0 ? Lf : C).Value = h[c];
        }
    }

    private static void EscreverLinhasSG(IXLWorksheet ws, List<CTeLinha> linhas, string aliqCell)
    {
        var C = XLAlignmentHorizontalValues.Center;
        for (int r = 0; r < linhas.Count; r++)
        {
            var l = linhas[r];
            int row = r + 4;
            ws.Row(row).Height = 14.1;
            // Cancelado: um "CANCELADO" só, da data ao valor da NF, e o número do CT-e (Admin, 27/10/2026)
            if (l.Data == "CANCELADO")
            {
                MR(ws, row, 1, 7, 10, true, fVerm, null, C).FirstCell().Value = "CANCELADO";
                var cte = SC(ws, row, 8, 10, false, fPreto, null, C);
                if (l.Cte.HasValue) cte.Value = l.Cte.Value;
                for (int c = 9; c <= 13; c++) SC(ws, row, c, 10, false, fPreto, null, C);
                continue;
            }
            if (DateTime.TryParse(l.Data, out var dt))
            {
                SC(ws, row, 1, 10, false, fPreto, null, C, "dd/mm/yyyy").Value = dt;
            }
            else SC(ws, row, 1, 10, false, fPreto, null, C).Value = l.Data;
            SC(ws, row, 2, 10, false, fPreto, null, C).Value = l.Fornecedor;
            SC(ws, row, 3, 10, true, fVerm, null, C).Value = l.UfFornecedor;
            SC(ws, row, 4, 10, false, fPreto, null, C).Value = l.Cliente;
            SC(ws, row, 5, 10, true, fVerm, null, C).Value = l.UfCliente;
            if (l.NF.HasValue) SC(ws, row, 6, 10, false, fPreto, null, C, "0").Value = l.NF.Value;
            if (l.ValorNF.HasValue) SC(ws, row, 7, 10, false, fPreto, null, C, nfRsSp).Value = l.ValorNF.Value;
            else SC(ws, row, 7, 10, false, fPreto, null, C, nfRsSp);
            if (l.Cte.HasValue) SC(ws, row, 8, 10, false, fPreto, null, C).Value = l.Cte.Value;
            SC(ws, row, 9, 10, false, fPreto, null, C).Value = l.Placa;
            if (l.VFrete.HasValue) SC(ws, row, 10, 10, false, fPreto, null, C, nfRsSp).Value = l.VFrete.Value;
            else SC(ws, row, 10, 10, false, fPreto, null, C, nfRsSp);
            if (l.Aliquota.HasValue) SC(ws, row, 11, 10, true, fAzulK, null, C, "0.0000").Value = l.Aliquota.Value;
            else SC(ws, row, 11, 10, true, fAzulK, null, C, "0.0000");
            SC(ws, row, 12, 10, true, fVermEsc, null, C, nfRs).FormulaA1 = $"G{row}*K{row}/100";
            SC(ws, row, 13, 10, true, fPreto, null, C, nfRs).FormulaA1 = $"J{row}*{aliqCell}/100";
        }
    }

    // ── Helpers de estilo ─────────────────────────────────────────────
    private static IXLCell SC(IXLWorksheet ws, int r, int c, double sz, bool bold,
        string fontColor, string? fill, XLAlignmentHorizontalValues h, string? nf = null)
    {
        var cell = ws.Cell(r, c);
        var s = cell.Style;
        s.Font.FontName = "Calibri";
        s.Font.FontSize = sz;
        s.Font.Bold = bold;
        s.Font.FontColor = XLColor.FromHtml(fontColor);
        if (fill != null) s.Fill.BackgroundColor = XLColor.FromHtml(fill);
        if (nf != null) s.NumberFormat.Format = nf;
        s.Alignment.Horizontal = h;
        s.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        return cell;
    }

    private static IXLRange MR(IXLWorksheet ws, int r, int c1, int c2, double sz, bool bold,
        string fontColor, string? fill, XLAlignmentHorizontalValues h, string? nf = null)
    {
        var rng = ws.Range(r, c1, r, c2).Merge();
        var s = rng.Style;
        s.Font.FontName = "Calibri";
        s.Font.FontSize = sz;
        s.Font.Bold = bold;
        s.Font.FontColor = XLColor.FromHtml(fontColor);
        if (fill != null) s.Fill.BackgroundColor = XLColor.FromHtml(fill);
        if (nf != null) s.NumberFormat.Format = nf;
        s.Alignment.Horizontal = h;
        s.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        return rng;
    }

    private static void Bordas(IXLWorksheet ws, int lastRow, int lastCol)
    {
        var rng = ws.Range(1, 1, lastRow, lastCol);
        rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        rng.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    // ── Escrita da planilha ──────────────────────────────────────────
    // aliquotaIcms: quando informado, adiciona as colunas ALIQUOTA ICMS FRETE (L)
    //   e VALOR TOTAL ICMS (M). totalIcmsExato: quando informado (geral), ajusta a
    //   última linha com frete pra que a soma do ICMS bata EXATO com o DAS.
    private static void EscreverPlanilha(IXLWorksheet ws, List<CTeLinha> linhas,
        double? aliquotaIcms = null, double? totalIcmsExato = null)
    {
        bool comIcms = aliquotaIcms.HasValue;

        // Cabeçalho
        var headers = new List<string> { "DATA", "FORNECEDOR", "UF_FORNECEDOR", "CLIENTE", "UF_CLIENTE",
                             "NF", "VALOR_NF", "CTE", "PLACA", "V_FRETE", "ALIQUOTA" };
        if (comIcms) { headers.Add("ALIQUOTA ICMS FRETE"); headers.Add("VALOR TOTAL ICMS"); }
        for (int c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            AplicarEstilo(cell, negrito: true);
        }

        int ultimaLinhaFrete = -1;   // pro ajuste exato

        // Dados
        for (int r = 0; r < linhas.Count; r++)
        {
            var l = linhas[r];
            int row = r + 2;

            SetCel(ws, row, 1,  l.Data);
            SetCel(ws, row, 2,  l.Fornecedor);
            SetCel(ws, row, 3,  l.UfFornecedor);
            SetCel(ws, row, 4,  l.Cliente);
            SetCel(ws, row, 5,  l.UfCliente);
            SetCel(ws, row, 6,  (object?)l.NF    ?? "");
            SetCel(ws, row, 7,  (object?)l.ValorNF ?? "");
            SetCel(ws, row, 8,  (object?)l.Cte   ?? "");
            SetCel(ws, row, 9,  l.Placa);
            SetCel(ws, row, 10, (object?)l.VFrete   ?? "");
            SetCel(ws, row, 11, (object?)l.Aliquota ?? "");

            if (comIcms && l.VFrete.HasValue)
            {
                // valor cheio (sem arredondar) — assim a soma bate EXATO com o DAS
                double icms = l.VFrete.Value * aliquotaIcms!.Value;
                SetCel(ws, row, 12, aliquotaIcms.Value);
                SetCel(ws, row, 13, icms);
                ws.Cell(row, 12).Style.NumberFormat.Format = "0.0000000000";
                ws.Cell(row, 13).Style.NumberFormat.Format = "R$ #,##0.00";
                ultimaLinhaFrete = row;
            }

            // Formatos de número
            if (l.Data != "CANCELADO" && !string.IsNullOrEmpty(l.Data))
            {
                try
                {
                    ws.Cell(row, 1).Value = DateTime.Parse(l.Data);
                    ws.Cell(row, 1).Style.NumberFormat.Format = "DD/MM/YYYY";
                }
                catch { }
            }
            ws.Cell(row, 6).Style.NumberFormat.Format  = "0";
            ws.Cell(row, 7).Style.NumberFormat.Format  = "R$ #,##0.00";
            ws.Cell(row, 10).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 11).Style.NumberFormat.Format = "0.0000";
        }

        // Ajuste bit-exato: joga o resíduo (mínimo) na última linha com frete,
        // pra soma bater 100% com o DAS mesmo com arredondamento de ponto flutuante.
        if (comIcms && totalIcmsExato.HasValue && ultimaLinhaFrete > 0)
        {
            double soma = 0;
            for (int row = 2; row <= linhas.Count + 1; row++)
                if (ws.Cell(row, 13).TryGetValue<double>(out var v)) soma += v;
            double diff = totalIcmsExato.Value - soma;
            if (diff != 0)
            {
                var atual = ws.Cell(ultimaLinhaFrete, 13).GetDouble();
                ws.Cell(ultimaLinhaFrete, 13).Value = atual + diff;
            }
        }

        // Linha de TOTAL (soma de V_FRETE e do VALOR TOTAL ICMS)
        if (comIcms && linhas.Count > 0)
        {
            int totalRow = linhas.Count + 2;
            int ultimaData = linhas.Count + 1;
            ws.Cell(totalRow, 9).Value = "TOTAL";
            ws.Cell(totalRow, 10).FormulaA1 = $"SUM(J2:J{ultimaData})";
            ws.Cell(totalRow, 10).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(totalRow, 13).FormulaA1 = $"SUM(M2:M{ultimaData})";
            ws.Cell(totalRow, 13).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Range(totalRow, 9, totalRow, 13).Style.Font.Bold = true;
        }

        // Estilos especiais
        for (int r = 2; r <= linhas.Count + 1; r++)
        {
            // UF_FORNECEDOR (C) e UF_CLIENTE (E) — vermelho
            ws.Cell(r, 3).Style.Font.FontColor  = XLColor.Red;
            ws.Cell(r, 3).Style.Font.Bold       = true;
            ws.Cell(r, 5).Style.Font.FontColor  = XLColor.Red;
            ws.Cell(r, 5).Style.Font.Bold       = true;
            // ALIQUOTA (K) — azul
            ws.Cell(r, 11).Style.Font.FontColor = XLColor.Blue;
            ws.Cell(r, 11).Style.Font.Bold      = true;
        }

        // Aplica borda + alinhamento + fonte em todos
        var range = ws.RangeUsed();
        if (range != null)
        {
            range.Style.Font.FontSize           = 10;
            range.Style.Font.Italic             = true;
            range.Style.Alignment.Horizontal    = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;
            range.Style.Border.OutsideBorder    = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder     = XLBorderStyleValues.Thin;
        }

        // Ajusta largura das colunas
        ws.ColumnsUsed().AdjustToContents();
    }

    private static void SetCel(IXLWorksheet ws, int row, int col, object val)
    {
        var cell = ws.Cell(row, col);
        if (val is double d) cell.Value = d;
        else if (val is int i) cell.Value = i;
        else cell.Value = val?.ToString() ?? "";
        AplicarEstilo(cell);
    }

    private static void AplicarEstilo(IXLCell cell, bool negrito = false)
    {
        cell.Style.Font.FontSize        = 10;
        cell.Style.Font.Italic          = true;
        cell.Style.Font.Bold            = negrito;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
    }

    // ── Helpers XML ──────────────────────────────────────────────────
    private static string Get(XElement root, string ancestor, string localName)
    {
        if (!string.IsNullOrEmpty(ancestor))
        {
            var anc = root.Descendants().FirstOrDefault(e => e.Name.LocalName == ancestor);
            if (anc != null)
            {
                var el = anc.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
                if (el != null) return el.Value.Trim();
            }
        }
        return root.Descendants().FirstOrDefault(e => e.Name.LocalName == localName)?.Value.Trim() ?? "";
    }

    private static string ObterPlacaXObs(XElement inf)
    {
        var xObs = inf.Descendants().FirstOrDefault(e => e.Name.LocalName == "xObs")?.Value ?? "";
        xObs = xObs.ToUpper().Replace("\n", " ");
        var m = Regex.Match(xObs, @"([A-Z]{3}\s*-?\s*\d[A-Z0-9]\d{2})");
        return m.Success ? m.Groups[1].Value.Replace(" ", "") : "";
    }
}
