using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

public class ExcelExportService(AppDbContext db)
{
    // ──────────────────────────────────────────────────────────────
    // CT-es Geral (substitui cte_geral.py)
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]> ExportarCTeGeralAsync(int mes, int ano)
    {
        var ctes = await db.CTes
            .Include(c => c.Veiculo)
            .Where(c => c.DataEmissao.Month == mes && c.DataEmissao.Year == ano)
            .OrderBy(c => c.NumeroCTe)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("CT-es");

        // Cabeçalho — mesmas colunas do script Python
        var headers = new[]
        {
            "DATA", "FORNECEDOR", "UF_FORNECEDOR", "CLIENTE", "UF_CLIENTE",
            "NF", "VALOR_NF", "CTE", "PLACA", "V_FRETE", "ALIQUOTA"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var c in ctes)
        {
            bool cancelado = c.Status == StatusCTe.Cancelado;

            ws.Cell(row, 1).Value = cancelado ? "CANCELADO" : c.DataEmissao.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = cancelado ? "CANCELADO" : (c.Remetente ?? "");
            ws.Cell(row, 3).Value = cancelado ? "CANCELADO" : (c.UFOrigem ?? "");
            ws.Cell(row, 4).Value = cancelado ? "CANCELADO" : (c.Destinatario ?? "");
            ws.Cell(row, 5).Value = cancelado ? "CANCELADO" : (c.UFDestino ?? "");
            ws.Cell(row, 6).Value = c.NumeroNF ?? "";
            ws.Cell(row, 7).Value = cancelado ? 0 : (double)c.ValorNF;
            ws.Cell(row, 8).Value = c.NumeroCTe;
            ws.Cell(row, 9).Value = c.Veiculo?.Placa ?? "";
            ws.Cell(row, 10).Value = cancelado ? 0 : (double)c.ValorFrete;
            ws.Cell(row, 11).Value = cancelado ? 0 : (double)c.AliquotaICMS;

            // Formatação numérica
            ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 10).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 11).Style.NumberFormat.Format = "0.0000";

            // UF em vermelho+negrito — igual ao script Python
            AplicarEstiloUF(ws.Cell(row, 3));
            AplicarEstiloUF(ws.Cell(row, 5));
            // Alíquota em azul+negrito
            ws.Cell(row, 11).Style.Font.Bold = true;
            ws.Cell(row, 11).Style.Font.FontColor = XLColor.Blue;

            // Fonte itálica em tudo
            for (int col = 1; col <= 11; col++)
            {
                ws.Cell(row, col).Style.Font.Italic = true;
                ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }

            if (cancelado)
            {
                var range = ws.Range(row, 1, row, 11);
                range.Style.Font.FontColor = XLColor.Red;
            }

            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────────
    // CT-es Por Placa (substitui cte_placas.py)
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]> ExportarCtePorPlacaAsync(int mes, int ano)
    {
        var ctes = await db.CTes
            .Include(c => c.Veiculo)
            .Where(c => c.DataEmissao.Month == mes && c.DataEmissao.Year == ano)
            .OrderBy(c => c.Veiculo!.Placa).ThenBy(c => c.NumeroCTe)
            .ToListAsync();

        using var wb = new XLWorkbook();

        var grupos = ctes.GroupBy(c => c.Veiculo?.Placa ?? "SEM-PLACA");

        foreach (var grupo in grupos)
        {
            var placa = grupo.Key;
            var wsName = placa.Length > 31 ? placa[..31] : placa;
            var ws = wb.AddWorksheet(wsName);

            // Cabeçalho
            var headers = new[]
            {
                "DATA", "FORNECEDOR", "UF_FORNECEDOR", "CLIENTE", "UF_CLIENTE",
                "NF", "VALOR_NF", "CTE", "PLACA", "V_FRETE", "ALIQUOTA"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#15803d");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 2;
            foreach (var c in grupo)
            {
                bool cancelado = c.Status == StatusCTe.Cancelado;
                ws.Cell(row, 1).Value = cancelado ? "CANCELADO" : c.DataEmissao.ToString("dd/MM/yyyy");
                ws.Cell(row, 2).Value = cancelado ? "CANCELADO" : (c.Remetente ?? "");
                ws.Cell(row, 3).Value = cancelado ? "CANCELADO" : (c.UFOrigem ?? "");
                ws.Cell(row, 4).Value = cancelado ? "CANCELADO" : (c.Destinatario ?? "");
                ws.Cell(row, 5).Value = cancelado ? "CANCELADO" : (c.UFDestino ?? "");
                ws.Cell(row, 6).Value = c.NumeroNF ?? "";
                ws.Cell(row, 7).Value = cancelado ? 0 : (double)c.ValorNF;
                ws.Cell(row, 8).Value = c.NumeroCTe;
                ws.Cell(row, 9).Value = placa;
                ws.Cell(row, 10).Value = cancelado ? 0 : (double)c.ValorFrete;
                ws.Cell(row, 11).Value = cancelado ? 0 : (double)c.AliquotaICMS;

                ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 10).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 11).Style.NumberFormat.Format = "0.0000";

                AplicarEstiloUF(ws.Cell(row, 3));
                AplicarEstiloUF(ws.Cell(row, 5));
                ws.Cell(row, 11).Style.Font.Bold = true;
                ws.Cell(row, 11).Style.Font.FontColor = XLColor.Blue;

                for (int col = 1; col <= 11; col++)
                {
                    ws.Cell(row, col).Style.Font.Italic = true;
                    ws.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                row++;
            }

            // Totais
            ws.Cell(row, 6).Value = "TOTAL";
            ws.Cell(row, 7).Value = (double)grupo.Where(c => c.Status == StatusCTe.Ativo).Sum(c => c.ValorNF);
            ws.Cell(row, 10).Value = (double)grupo.Where(c => c.Status == StatusCTe.Ativo).Sum(c => c.ValorFrete);
            ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 10).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 6).Style.Font.Bold = true;
            ws.Cell(row, 7).Style.Font.Bold = true;
            ws.Cell(row, 10).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────────
    // Romaneio por cliente (substitui GERAR_PLANILHAS.py)
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]> ExportarRomaneioAsync(int romaneioId)
    {
        var romaneio = await db.Romaneios
            .Include(r => r.Cliente)
            .Include(r => r.Itens)
            .FirstOrDefaultAsync(r => r.Id == romaneioId)
            ?? throw new KeyNotFoundException($"Romaneio {romaneioId} não encontrado.");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Romaneio");

        // Título
        ws.Cell("A1").Value = $"ROMANEIO — {romaneio.Cliente?.Nome?.ToUpper()}";
        ws.Range("A1:G1").Merge();
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
        ws.Cell("A1").Style.Font.FontColor = XLColor.White;

        // Data
        ws.Cell("A2").Value = $"Data: {romaneio.DataRomaneio:dd/MM/yyyy}";
        ws.Cell("A2").Style.Font.Italic = true;

        // Cabeçalho da tabela
        int headerRow = 4;
        string[] cols = { "Produto", "Descrição", "Qtd", "Unidade", "Vlr Unit.", "Vlr Total" };
        for (int i = 0; i < cols.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = cols[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Itens
        int dataRow = headerRow + 1;
        foreach (var item in romaneio.Itens)
        {
            ws.Cell(dataRow, 1).Value = item.Produto.ToString();
            ws.Cell(dataRow, 2).Value = item.Descricao ?? "";
            ws.Cell(dataRow, 3).Value = (double)item.Quantidade;
            ws.Cell(dataRow, 4).Value = item.Unidade ?? "";
            ws.Cell(dataRow, 5).Value = (double)item.ValorUnitario;
            ws.Cell(dataRow, 6).Value = (double)(item.Quantidade * item.ValorUnitario);

            ws.Cell(dataRow, 5).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(dataRow, 6).Style.NumberFormat.Format = "R$ #,##0.00";

            for (int col = 1; col <= 6; col++)
                ws.Cell(dataRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            dataRow++;
        }

        // Total
        ws.Cell(dataRow, 5).Value = "TOTAL";
        ws.Cell(dataRow, 5).Style.Font.Bold = true;
        ws.Cell(dataRow, 6).Value = (double)romaneio.Itens.Sum(i => i.Quantidade * i.ValorUnitario);
        ws.Cell(dataRow, 6).Style.Font.Bold = true;
        ws.Cell(dataRow, 6).Style.NumberFormat.Format = "R$ #,##0.00";
        ws.Cell(dataRow, 6).Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");

        ws.Columns().AdjustToContents();
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 30);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────────
    // Abastecimento por posto (relatório mensal)
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]> ExportarAbastecimentoAsync(int mes, int ano)
    {
        var abastecimentos = await db.Abastecimentos
            .Include(a => a.Veiculo)
            .Include(a => a.Posto)
            .Where(a => a.Mes == mes && a.Ano == ano)
            .OrderBy(a => a.Posto!.Nome).ThenBy(a => a.Veiculo!.Placa).ThenBy(a => a.Data)
            .ToListAsync();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Abastecimento");

        ws.Cell("A1").Value = $"ABASTECIMENTO — {new DateTime(ano, mes, 1):MMMM/yyyy}".ToUpper();
        ws.Range("A1:G1").Merge();
        EstiloCabecalhoTitulo(ws.Cell("A1"));

        string[] headers = { "Data", "Posto", "Placa", "Sócio", "Litros", "Vlr Litro", "Total" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(3, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#f3f4f6");
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 4;
        foreach (var a in abastecimentos)
        {
            ws.Cell(row, 1).Value = a.Data.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = a.Posto?.Nome ?? "";
            ws.Cell(row, 3).Value = a.Veiculo?.Placa ?? "";
            ws.Cell(row, 4).Value = a.Veiculo?.Socio.ToString() ?? "";
            ws.Cell(row, 5).Value = (double)a.Litros;
            ws.Cell(row, 6).Value = (double)a.ValorLitro;
            ws.Cell(row, 7).Value = (double)a.ValorTotal;
            ws.Cell(row, 6).Style.NumberFormat.Format = "R$ #,##0.000";
            ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
            for (int col = 1; col <= 7; col++)
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;
        }

        // Subtotais por posto
        var porPosto = abastecimentos.GroupBy(a => a.Posto?.Nome ?? "?");
        row++;
        ws.Cell(row, 1).Value = "RESUMO POR POSTO";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        foreach (var g in porPosto)
        {
            ws.Cell(row, 2).Value = g.Key;
            ws.Cell(row, 7).Value = (double)g.Sum(a => a.ValorTotal);
            ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(row, 7).Style.Font.Bold = true;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ──────────────────────────────────────────────────────────────
    // Seguro mensal por placa
    // ──────────────────────────────────────────────────────────────
    public async Task<byte[]> ExportarSeguroAsync(int mes, int ano)
    {
        var seguro = await db.SegurosMensais
            .Include(s => s.Placas).ThenInclude(p => p.Veiculo)
            .FirstOrDefaultAsync(s => s.Mes == mes && s.Ano == ano);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Seguro");

        ws.Cell("A1").Value = $"SEGURO DE CARGAS — {new DateTime(ano, mes, 1):MMMM/yyyy}".ToUpper();
        ws.Range("A1:E1").Merge();
        EstiloCabecalhoTitulo(ws.Cell("A1"));

        if (seguro == null)
        {
            ws.Cell("A3").Value = "Nenhum seguro registrado para este mês.";
            using var ms2 = new MemoryStream();
            wb.SaveAs(ms2);
            return ms2.ToArray();
        }

        ws.Cell("A2").Value = $"Seguradora: {seguro.Seguradora} | Apólice: {seguro.NumeroApolice} | Total: R$ {seguro.ValorTotal:N2}";
        ws.Cell("A2").Style.Font.Italic = true;

        string[] headers = { "Placa", "Sócio", "Nº Viagens", "Valor" };
        for (int i = 0; i < headers.Length; i++)
        {
            var c = ws.Cell(4, i + 1);
            c.Value = headers[i];
            c.Style.Font.Bold = true;
            c.Style.Fill.BackgroundColor = XLColor.FromHtml("#f3f4f6");
            c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 5;
        foreach (var sp in seguro.Placas.OrderBy(p => p.Veiculo?.Socio).ThenBy(p => p.Veiculo?.Placa))
        {
            ws.Cell(row, 1).Value = sp.Veiculo?.Placa ?? "";
            ws.Cell(row, 2).Value = sp.Veiculo?.Socio.ToString() ?? "";
            ws.Cell(row, 3).Value = sp.NumeroViagens;
            ws.Cell(row, 4).Value = (double)sp.Valor;
            ws.Cell(row, 4).Style.NumberFormat.Format = "R$ #,##0.00";
            for (int col = 1; col <= 4; col++)
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── helpers ───────────────────────────────────────────────────

    private static void AplicarEstiloUF(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.Italic = true;
        cell.Style.Font.FontColor = XLColor.Red;
    }

    private static void EstiloCabecalhoTitulo(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 13;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e40af");
        cell.Style.Font.FontColor = XLColor.White;
    }
}
