using ClosedXML.Excel;
using TransportadoraSystem.Core.Entities;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Gera as planilhas no layout das antigas, a partir dos dados:
///  - acerto mensal do caminhão: uma aba VIAGEMn por viagem, mesmas células e
///    fórmulas da "N-V -MÊS-AAAA.xlsx" (a comissão já pela regra do Admin:
///    G60 = valor total − caixinha − descargas);
///  - "CONTROLE MENSAL DE RETORNOS" do mês.
/// O ClosedXML não calcula fórmulas: o Excel recalcula ao abrir (INF-04).
/// </summary>
public static class AcertoCaminhaoExcel
{
    private const string Moeda = "#,##0.00";

    public static byte[] Gerar(List<(ViagemCaminhao V, List<RetornoFrete> Rets, decimal SobraAnterior)> viagens, List<PrecoFreteCliente> tabela)
    {
        using var wb = new XLWorkbook();
        for (var n = 0; n < viagens.Count; n++)
            Aba(wb.Worksheets.Add($"VIAGEM{n + 1}"), viagens[n].V, viagens[n].Rets, viagens[n].SobraAnterior, n, tabela);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Aba(IXLWorksheet ws, ViagemCaminhao v, List<RetornoFrete> rets, decimal sobraAnterior, int indice, List<PrecoFreteCliente> tabela)
    {
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 10;
        ws.Cell("A1").Value = "CONTROLE DE VIAGENS CAMINHÕES";
        ws.Range("A1:J1").Merge().Style.Font.SetBold().Font.SetFontSize(13).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        Rot(ws, "A2", "MOTORISTA"); ws.Cell("B2").Value = v.MotoristaNome;
        Rot(ws, "G2", "DESTINOS VIAGENS"); ws.Cell("I2").Value = v.Destino ?? "";
        Rot(ws, "A3", "PLACA CAMINHÃO");
        ws.Cell("B3").Value = v.PlacaCarreta != null ? $"({v.Placa})-({v.PlacaCarreta})" : $"({v.Placa})";
        Rot(ws, "G3", "KM SAIDA"); if (v.KmSaida is { } ks) ws.Cell("H3").Value = ks;
        Rot(ws, "A4", "DATA SAIDA"); Data(ws.Cell("B4"), v.DataSaida);
        Rot(ws, "G4", "KM CHEGADA"); if (v.KmChegada is { } kc) ws.Cell("H4").Value = kc;
        ws.Cell("I4").FormulaA1 = "(H4-H3)"; Rot(ws, "J4", "DIF.KM");
        Rot(ws, "A5", "DATA CHEGADA"); Data(ws.Cell("B5"), v.DataChegada);
        Rot(ws, "G5", "LITROS");
        var litros = v.Litros ?? (v.Abastecimentos.Any(a => a.Litros > 0) ? v.Abastecimentos.Sum(a => a.Litros ?? 0) : null);
        if (litros is { } l) ws.Cell("H5").Value = l;
        Rot(ws, "A6", "SOBRA-VIAGEM");
        // Dentro do mês a sobra vem da aba anterior, como na planilha; a primeira leva o valor
        if (indice > 0 && v.SobraAnteriorInformada == null) ws.Cell("B6").FormulaA1 = $"VIAGEM{indice}!B60";
        else ws.Cell("B6").Value = sobraAnterior;
        Rot(ws, "G6", "MÉDIA"); ws.Cell("H6").FormulaA1 = "IFERROR(I4/H5,0)";
        Rot(ws, "I6", "PORC/COM"); ws.Cell("J6").Value = v.PorcentagemComissao;
        Rot(ws, "A7", "LEVOU"); ws.Cell("B7").Value = v.Levou;
        Rot(ws, "G7", "COMISSÃO VIAGEM"); ws.Cell("I7").FormulaA1 = "G60/100*J6";

        string[] cab = ["FRETE E RETORNO", "QT .VOLUMES", "QT.PRODUTOE.", "QT.COD.", "QT.EMBAL.", "VALOR", "TOTAL", "DESPESAS", "VALOR", "OBSEVAÇÕES"];
        for (var c = 0; c < cab.Length; c++) Rot(ws, ws.Cell(8, c + 1).Address.ToString()!, cab[c]);
        ws.Range("A8:J8").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E8EEF4"));

        // Clientes (linhas 9–44): a lista da tabela de frete, com as caixas desta viagem;
        // cliente da viagem fora da tabela entra no fim. Sem espaço, saem os da tabela sem caixas.
        var linhas = tabela.Select(p => (Cliente: p.Cliente, Preco: p, Volumes: (ViagemCaminhaoVolumes?)null)).ToList();
        foreach (var o in v.Volumes.OrderBy(o => o.Ordem))
        {
            var i = linhas.FindIndex(x => x.Volumes == null && Normalizar(x.Cliente) == Normalizar(o.Cliente));
            if (i >= 0) linhas[i] = (linhas[i].Cliente, linhas[i].Preco, o);
            else linhas.Add((o.Cliente, null!, o));
        }
        while (linhas.Count > 36)
        {
            var i = linhas.FindLastIndex(x => x.Volumes == null);
            if (i < 0) break;
            linhas.RemoveAt(i);
        }
        for (var k = 0; k < 36; k++)
        {
            var r = 9 + k;
            if (k < linhas.Count)
            {
                var (cliente, preco, o) = linhas[k];
                ws.Cell(r, 1).Value = cliente;
                if (o != null)
                {
                    Num(ws.Cell(r, 2), o.CaixasVolumes); Num(ws.Cell(r, 3), o.CaixasProdutoE);
                    Num(ws.Cell(r, 4), o.CaixasProdutoD); Num(ws.Cell(r, 5), o.ProdutosB);
                }
                var valor = o?.ValorCaixa ?? preco?.ValorCaixa ?? 9;
                var cod = o?.ValorProdutoD ?? preco?.ValorProdutoD ?? 3;
                var emb = o?.ValorProdutoB ?? 10;
                // ProdutoE gravada na viagem (VIA-10); as antigas, metade da caixa como na planilha
                decimal? caip = o != null ? o.ValorProdutoE : preco?.ValorProdutoE;
                var produtoE = caip is { } vc ? $"C{r}*{Txt(vc)}" : $"C{r}*F{r}/2";
                // ProdutosC não têm coluna no modelo: entram na conta do cliente (G)
                var produtosC = o is { ProdutosC: > 0 } ? $"+{Txt(o.ProdutosC)}*{Txt(o.ValorProdutoC)}" : "";
                ws.Cell(r, 6).Value = valor;
                ws.Cell(r, 7).FormulaA1 = $"B{r}*F{r}+{produtoE}+D{r}*{Txt(cod)}+E{r}*{Txt(emb)}{produtosC}";
            }
            else ws.Cell(r, 7).FormulaA1 = $"B{r}*F{r}+C{r}*F{r}/2+D{r}*3+E{r}*10";
        }

        // Diesel: nota (9–14) e pago (15–19)
        Lancar(ws, v.Abastecimentos.Where(a => a.Forma != "PAGO").OrderBy(a => a.Ordem).ToList(), 9, 6, "DIESEL-NOTA");
        Lancar(ws, v.Abastecimentos.Where(a => a.Forma == "PAGO").OrderBy(a => a.Ordem).ToList(), 15, 5, "DIESEL-PAGO");
        Rot(ws, "H20", "TOTAL-DIESEL"); ws.Cell("I20").FormulaA1 = "SUM(I9:I19)";

        Rot(ws, "H21", "DIÁRIAS"); Num(ws.Cell("I21"), ValorDiarias(v));
        if (v.DiariasQuantidade > 0) ws.Cell("J21").Value = $"{v.DiariasQuantidade:0.#} diária(s)";
        var fixos = new (int Linha, string Tipo, string Rotulo)[]
        {
            (22, Carregamento, "CARREGAMENTO"), (23, Gasolina, "GASOLINA"), (24, CredCelular, "CRED. CELULAR"),
            (25, Caixinha, "CAIXINHA"), (26, Borracharia, "BORRACHARIA"),
        };
        foreach (var (linha, tipo, rotulo) in fixos)
        {
            Rot(ws, $"H{linha}", rotulo);
            var ds = v.Despesas.Where(d => d.Tipo == tipo).ToList();
            Num(ws.Cell(linha, 9), ds.Sum(d => d.Valor));
            // Caixinha: total na linha e de quem é cada uma na observação (J)
            var obs = tipo == Caixinha
                ? string.Join("; ", ds.Select(d => string.IsNullOrWhiteSpace(d.Descricao) ? null : $"{d.Descricao} {d.Valor:N2}").Where(x => x != null))
                : string.Join("; ", ds.Select(d => d.Observacao).Where(x => !string.IsNullOrWhiteSpace(x)));
            if (obs.Length > 0) ws.Cell(linha, 10).Value = obs;
        }
        // Outras (27–54), uma por linha; se não couber, a última soma o resto
        var outras = v.Despesas.Where(d => fixos.All(f => f.Tipo != d.Tipo) && d.Tipo != Descarga).OrderBy(d => d.Ordem).ToList();
        for (var k = 0; k < outras.Count && k < 28; k++)
        {
            var r = 27 + k;
            var d = outras[k];
            var resto = k == 27 ? outras.Skip(27).ToList() : [d];
            ws.Cell(r, 8).Value = resto.Count > 1 ? "OUTRAS" : (d.Tipo == Outra ? d.Descricao ?? "OUTRA" : NomeTipo(d.Tipo)).ToUpperInvariant();
            Num(ws.Cell(r, 9), resto.Sum(x => x.Valor));
            if (resto.Count == 1 && !string.IsNullOrWhiteSpace(d.Observacao)) ws.Cell(r, 10).Value = d.Observacao;
        }
        var descargas = v.Despesas.Where(d => d.Tipo == Descarga).OrderBy(d => d.Ordem).ToList();
        for (var k = 0; k < 3; k++)
        {
            var r = 55 + k;
            Rot(ws, $"H{r}", "DESCARGA");
            var parte = k < 2 ? descargas.Skip(k).Take(1) : descargas.Skip(2);
            Num(ws.Cell(r, 9), parte.Sum(d => d.Valor));
        }

        // Retornos (47–53)
        Rot(ws, "A45", "RETORNOS E OUTROS"); Rot(ws, "C45", "ICMS FRETES E CIDADE  DESTINO");
        Rot(ws, "A46", "TRANSPORTADORA"); Rot(ws, "B46", "VALOR  FRETE"); Rot(ws, "C46", "PORCENT.");
        Rot(ws, "D46", "VALOR ICMS"); Rot(ws, "E46", "ORIGEM      //         DESTINO");
        for (var k = 0; k < 7; k++)
        {
            var r = 47 + k;
            ws.Cell(r, 4).FormulaA1 = $"B{r}/100*C{r}";
            if (k >= rets.Count) continue;
            var grupo = k == 6 ? rets.Skip(6).ToList() : [rets[k]];
            var ret = grupo[0];
            ws.Cell(r, 1).Value = grupo.Count > 1 ? "VÁRIOS" : ret.Transportadora ?? "";
            Num(ws.Cell(r, 2), grupo.Sum(x => x.FreteTotal));
            if (ret.PorcentagemIcms != 0 && grupo.Count == 1) ws.Cell(r, 3).Value = ret.PorcentagemIcms;
            ws.Cell(r, 5).Value = string.Join(" / ", new[] { ret.Observacoes, ret.Destino }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        Rot(ws, "C54", "QUANTIDADE DE CXS");
        Rot(ws, "C55", "T.CXS "); Rot(ws, "D55", "T. PRODUTOE."); Rot(ws, "E55", "T. PRODUTOD."); Rot(ws, "F55", "T.EMBAL.");
        ws.Cell("C56").FormulaA1 = "SUM(B9:B44)"; ws.Cell("D56").FormulaA1 = "SUM(C9:C44)";
        ws.Cell("E56").FormulaA1 = "SUM(D9:D44)"; ws.Cell("F56").FormulaA1 = "SUM(E9:E44)";
        Rot(ws, "A57", "TOTAL RETORNOS"); ws.Cell("B57").FormulaA1 = "SUM(B47:B53)";
        Rot(ws, "A58", "DINHEIRO VIAGEM"); ws.Cell("B58").FormulaA1 = "B6+B7";
        Rot(ws, "F58", "TOTAL VOLUMES"); ws.Cell("G58").FormulaA1 = "SUM(G9:G44)";
        Rot(ws, "H58", "DESPESAS"); ws.Cell("I58").FormulaA1 = "SUM(I21:I57)";
        Rot(ws, "A59", "DEVOL. DINHEIRO"); Num(ws.Cell("B59"), v.Devolveu); ws.Cell("C59").Value = "Motorista Devolveu";
        Rot(ws, "F59", "VALOR TOTAL"); ws.Cell("G59").FormulaA1 = "G58+B57";
        Rot(ws, "H59", "TOTAL DESPESAS"); ws.Cell("I59").FormulaA1 = "I20+I58";
        Rot(ws, "A60", "SOBRA VIAGEM"); ws.Cell("B60").FormulaA1 = "B58-B59-I59+I9+I10+I11+I12+I13+I14";
        // Comissão (Admin, 06/10/2026): frete total menos só caixinha e descargas
        Rot(ws, "C60", "VALOR CALC. COMISSÃO"); ws.Cell("G60").FormulaA1 = "G59-I25-I55-I56-I57";
        Rot(ws, "H60", "SOBRA VIAGEM"); ws.Cell("I60").FormulaA1 = "G59-I59";
        Rot(ws, "A61", "OBSERVAÇÕES"); if (v.Observacoes != null) ws.Cell("B61").Value = v.Observacoes;

        ws.Range("B4:B5").Style.DateFormat.Format = "dd/mm/yyyy";
        foreach (var faixa in new[] { "F9:G44", "I7", "I9:I60", "B6:B7", "B47:B60", "D47:D53", "G58:G60" })
            ws.Range(faixa).Style.NumberFormat.Format = Moeda;
        ws.Range("A58:J60").Style.Font.SetBold();
        ws.Column(1).Width = 22; ws.Column(2).Width = 14; ws.Columns(3, 6).Width = 10;
        ws.Column(7).Width = 13; ws.Column(8).Width = 16; ws.Column(9).Width = 12; ws.Column(10).Width = 26;
        ws.SheetView.FreezeRows(8);
    }

    private static void Lancar(IXLWorksheet ws, List<ViagemCaminhaoAbastecimento> lista, int inicio, int vagas, string rotulo)
    {
        for (var k = 0; k < vagas; k++)
        {
            var r = inicio + k;
            Rot(ws, $"H{r}", rotulo);
            if (k >= lista.Count) continue;
            var grupo = k == vagas - 1 ? lista.Skip(k).ToList() : [lista[k]];
            Num(ws.Cell(r, 9), grupo.Sum(a => a.Valor));
            var a0 = grupo[0];
            var obs = grupo.Count > 1 ? $"{grupo.Count} abastecimentos"
                : string.Join(" -", new[] { a0.Local, a0.Data?.ToString("dd/MM") }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (obs.Length > 0) ws.Cell(r, 10).Value = obs;
        }
    }

    public static byte[] GerarRetornos(List<RetornoFrete> lista, int mes, int ano)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Plan1");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 10;
        ws.Cell("A2").Value = "CONTROLE  MENSAL  DE  RETORNOS";
        ws.Range("A2:N2").Merge().Style.Font.SetBold().Font.SetFontSize(13).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Cell("A3").Value = "REFERENTE AO MÊS DE";
        ws.Cell("D3").Value = $"{NomeMes(mes)} --{ano}";
        ws.Range("A3:D3").Style.Font.SetBold();
        string[] cab = ["DATA-FRETE", "DESTINO", "TRANSPORT.", "VALOR FRETE", "PEDÁGIO", "ADICIONAL", "DESC. SEG.", "DESCONT", "PORC. TIP", "FRETE TOTAL", "MOTORISTA", "PLACA", "OBSERVAÇÕES", "FORMA PTO"];
        for (var c = 0; c < cab.Length; c++) ws.Cell(4, c + 1).Value = cab[c];
        ws.Range("A4:N4").Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EEF4"));

        var r = 5;
        foreach (var x in lista)
        {
            ws.Cell(r, 1).Value = x.Data.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 2).Value = x.Destino ?? "";
            ws.Cell(r, 3).Value = x.Transportadora ?? "";
            ws.Cell(r, 4).Value = x.ValorFrete;
            ws.Cell(r, 5).Value = x.Pedagio;
            ws.Cell(r, 6).Value = x.Adicional;
            ws.Cell(r, 7).Value = x.DescontoSeguro;
            if (x.DescontoManual is { } dm) ws.Cell(r, 8).Value = dm;
            else ws.Cell(r, 8).FormulaA1 = $"SUM(D{r}+F{r}-E{r})/100*I{r}";
            if (x.PorcentagemTip is { } pt) ws.Cell(r, 9).Value = pt;
            ws.Cell(r, 10).FormulaA1 = $"D{r}+F{r}-E{r}-H{r}-G{r}";
            ws.Cell(r, 11).Value = x.Motorista ?? "";
            ws.Cell(r, 12).Value = x.Placa ?? "";
            ws.Cell(r, 13).Value = x.Observacoes ?? "";
            ws.Cell(r, 14).Value = x.FormaPagamento ?? "";
            r++;
        }
        var ultima = r - 1;
        r++;
        ws.Cell(r, 5).Value = "VALOR TOTAL DE RETORNOS";
        ws.Cell(r, 10).FormulaA1 = ultima >= 5 ? $"SUM(J5:J{ultima})" : "0";
        ws.Range(r, 5, r, 10).Style.Font.SetBold();

        ws.Range($"A5:A{Math.Max(5, ultima)}").Style.DateFormat.Format = "dd/mm/yyyy";
        ws.Range($"D5:H{Math.Max(5, r)}").Style.NumberFormat.Format = Moeda;
        ws.Range($"J5:J{Math.Max(5, r)}").Style.NumberFormat.Format = Moeda;
        ws.Column(1).Width = 12; ws.Column(2).Width = 24; ws.Column(3).Width = 22; ws.Columns(4, 10).Width = 12;
        ws.Column(11).Width = 14; ws.Column(12).Width = 11; ws.Column(13).Width = 20; ws.Column(14).Width = 12;
        ws.SheetView.FreezeRows(4);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Rot(IXLWorksheet ws, string celula, string texto)
    {
        ws.Cell(celula).Value = texto;
        ws.Cell(celula).Style.Font.SetBold();
    }

    private static void Data(IXLCell c, DateOnly? d)
    {
        if (d is { } x) c.Value = x.ToDateTime(TimeOnly.MinValue);
    }

    private static void Num(IXLCell c, decimal v)
    {
        if (v != 0) c.Value = v;
    }

    private static string Txt(decimal v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
