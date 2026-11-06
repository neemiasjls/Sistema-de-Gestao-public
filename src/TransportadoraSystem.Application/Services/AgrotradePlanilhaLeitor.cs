using System.Globalization;
using ClosedXML.Excel;

namespace TransportadoraSystem.Application.Services;

public sealed record AgrotradeViagem(
    int Linha, DateOnly Data, string? Motorista, string? Placa, string? Destino, string? Unidade,
    string? Nf, string? NfOriginal, decimal? Frete, decimal? AdiantDescarga, decimal? Descontos,
    decimal? ValorDescarga, decimal? FreteTotal, string? Contrato, decimal? DifDescarga,
    string? ProtocoloDescarga, DateOnly? DataOcorrenciaDescarga, string? Recebido,
    DateOnly? DataDescargaRecebidaPlanilha, string? ProtocoloDiaria, DateOnly? DataDiaria,
    decimal? ValorDiaria, string? Observacao, IReadOnlyList<string> Avisos);

public sealed record AgrotradeContratoTip(
    int Linha, DateOnly? Data, string? Placa, string? Contrato, decimal? Frete, decimal? Desconto,
    decimal? ValorAReceber, string? Transferido, DateOnly? DataTransferencia, bool DataSuspeita,
    IReadOnlyList<string> Avisos);

public sealed record AgrotradePlanilhaResultado(
    IReadOnlyList<AgrotradeViagem> Viagens, IReadOnlyList<AgrotradeContratoTip> ContratosTip,
    IReadOnlyList<string> Avisos);

/// <summary>Lê a base AGROTRADE sem acessar banco nem alterar o arquivo de origem.</summary>
public static class AgrotradePlanilhaLeitor
{
    /// <summary>Início do controle (painel a receber). A base entra desde 01/08: reembolso de setembro é de viagem de agosto.</summary>
    public static readonly DateOnly Inicio = new(2026, 9, 1);
    public static readonly DateOnly InicioBase = new(2026, 8, 1);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static AgrotradePlanilhaResultado Ler(byte[] xlsx) => Ler(xlsx, Inicio);

    public static AgrotradePlanilhaResultado Ler(byte[] xlsx, DateOnly inicio)
    {
        using var stream = new MemoryStream(xlsx);
        using var workbook = new XLWorkbook(stream);
        var avisos = new List<string>();
        var viagens = new List<AgrotradeViagem>();
        var contratos = new List<AgrotradeContratoTip>();
        var nfs = new Dictionary<string, int>(StringComparer.Ordinal);

        var aba = AcharAba(workbook, "VIAGENS", avisos);
        if (aba != null)
        {
            for (var linha = 4; linha <= (aba.LastRowUsed()?.RowNumber() ?? 0); linha++)
            {
                var leitura = new LeituraLinha(aba, linha);
                if (leitura.Vazia(21)) continue;
                var data = leitura.Data(1, "data da viagem", obrigatoria: true);
                if (data == null)
                {
                    // Linha sem data só é viagem se tiver NF ou contrato; total/rodapé é ignorado calado.
                    var temViagem = !string.IsNullOrWhiteSpace(aba.Cell(linha, 6).GetString())
                        || !string.IsNullOrWhiteSpace(aba.Cell(linha, 12).GetString());
                    if (temViagem) avisos.AddRange(leitura.Avisos);
                    continue;
                }
                if (data < inicio) continue;

                var original = leitura.Texto(6, "NF", obrigatorio: true);
                var nf = original?.Split('/')[0].Trim();
                if (nf != null && (nf.Length == 0 || !nf.All(char.IsAsciiDigit)))
                {
                    leitura.Avisar("NF inválida: esperado um número antes da barra.");
                    nf = null;
                }
                if (nf != null && !nfs.TryAdd(nf, linha))
                    leitura.Avisar($"NF repetida {nf} (também na linha {nfs[nf]}): confira; as duas linhas entram.");

                var viagem = new AgrotradeViagem(
                    linha, data.Value, leitura.Texto(2, "motorista", true), leitura.Placa(3),
                    leitura.Texto(4, "destino", true), leitura.Texto(5, "unidade", true), nf, original,
                    leitura.Numero(7, "frete", true), leitura.Numero(8, "adiantamento da descarga"),
                    leitura.Numero(9, "descontos"), leitura.Numero(10, "valor da descarga"),
                    leitura.Numero(11, "frete total"), leitura.Texto(12, "contrato", true),
                    leitura.Numero(13, "diferença da descarga"), leitura.Texto(14, "protocolo da descarga"),
                    leitura.Data(15, "data da ocorrência da descarga"), leitura.Texto(16, "recebido"),
                    leitura.Data(17, "data do recebimento da descarga"), leitura.Texto(18, "protocolo da diária"),
                    leitura.Data(19, "data da diária"), leitura.Numero(20, "valor da diária"),
                    leitura.Texto(21, "observação da diária"), leitura.Avisos);

                // Datas são opcionais enquanto não houver protocolo ou recebimento anotado.
                leitura.ExigirData(viagem.ProtocoloDescarga != null, viagem.DataOcorrenciaDescarga, "data da ocorrência da descarga");
                leitura.ExigirData(viagem.Recebido != null, viagem.DataDescargaRecebidaPlanilha, "data do recebimento da descarga");
                leitura.ExigirData(viagem.ProtocoloDiaria != null, viagem.DataDiaria, "data da diária");
                viagens.Add(viagem);
                avisos.AddRange(leitura.Avisos);
            }
        }

        var datasPorContrato = viagens.Where(v => v.Contrato != null)
            .GroupBy(v => v.Contrato!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(v => v.Data), StringComparer.Ordinal);
        aba = AcharAba(workbook, "CONTRATOS TIP", avisos);
        if (aba != null)
        {
            for (var linha = 3; linha <= (aba.LastRowUsed()?.RowNumber() ?? 0); linha++)
            {
                var leitura = new LeituraLinha(aba, linha);
                if (leitura.Vazia(9)) continue;
                var data = leitura.Data(1, "data do contrato", true);
                var placa = leitura.Placa(3);
                var contrato = leitura.Texto(4, "contrato", true);
                var frete = leitura.Numero(5, "frete");
                var desconto = leitura.Numero(6, "desconto");
                var valor = leitura.Numero(7, "valor a receber", true);
                var transferido = leitura.Texto(8, "transferido");
                var transferencia = leitura.Data(9, "data da transferência", transferido != null);
                var suspeita = contrato != null && transferencia != null
                    && datasPorContrato.TryGetValue(contrato, out var dataViagem) && transferencia < dataViagem;
                if (suspeita)
                    leitura.Avisar($"Contrato {contrato}: data suspeita — transferência em {transferencia:dd/MM/yyyy} anterior à viagem em {datasPorContrato[contrato!]:dd/MM/yyyy}.");
                contratos.Add(new AgrotradeContratoTip(linha, data, placa, contrato, frete, desconto,
                    valor, transferido, transferencia, suspeita, leitura.Avisos));
                avisos.AddRange(leitura.Avisos);
            }
        }
        return new AgrotradePlanilhaResultado(viagens, contratos, avisos);
    }

    private static IXLWorksheet? AcharAba(XLWorkbook workbook, string nome, List<string> avisos)
    {
        var aba = workbook.Worksheets.FirstOrDefault(a => a.Name.Trim().Equals(nome, StringComparison.OrdinalIgnoreCase));
        if (aba == null) avisos.Add($"A planilha não tem a aba {nome}.");
        return aba;
    }

    private sealed class LeituraLinha(IXLWorksheet aba, int linha)
    {
        public List<string> Avisos { get; } = [];

        // Usa o resultado guardado pelo Excel, sem recalcular fórmulas do arquivo do usuário.
        private XLCellValue Valor(int coluna)
        {
            var celula = aba.Cell(linha, coluna);
            return celula.HasFormula ? celula.CachedValue : celula.Value;
        }

        public bool Vazia(int colunas) => Enumerable.Range(1, colunas)
            .All(c => string.IsNullOrWhiteSpace(Valor(c).ToString(CultureInfo.InvariantCulture)));

        public void Avisar(string mensagem) => Avisos.Add($"{aba.Name}, linha {linha}: {mensagem}");

        public string? Texto(int coluna, string campo, bool obrigatorio = false)
        {
            var valor = Valor(coluna);
            if (valor.IsError)
            {
                Avisar($"{campo} com erro na planilha ({valor}).");
                return null;
            }
            var texto = valor.ToString(CultureInfo.InvariantCulture).Trim();
            if (texto.Length > 0) return texto;
            if (obrigatorio) Avisar($"Falta {campo}.");
            return null;
        }

        public string? Placa(int coluna)
        {
            var texto = Texto(coluna, "placa", true);
            return texto == null ? null : ViagemCaminhaoCalculo.NormalizarPlaca(texto);
        }

        public decimal? Numero(int coluna, string campo, bool obrigatorio = false)
        {
            var texto = Texto(coluna, campo, obrigatorio);
            if (texto == null) return null;
            texto = texto.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();
            var cultura = texto.Contains(',') ? PtBr : CultureInfo.InvariantCulture;
            if (decimal.TryParse(texto, NumberStyles.Number | NumberStyles.AllowExponent, cultura, out var numero))
                return Math.Round(numero, 2, MidpointRounding.AwayFromZero);
            Avisar($"{campo} inválido: \"{texto}\".");
            return null;
        }

        public DateOnly? Data(int coluna, string campo, bool obrigatoria = false)
        {
            var valor = Valor(coluna);
            if (valor.IsDateTime) return DateOnly.FromDateTime(valor.GetDateTime());
            if (valor.IsNumber && valor.GetNumber() is >= 1 and < 2958466)
            {
                var serial = valor.GetNumber();
                // O calendário 1904 tem origem 1.462 dias depois do calendário padrão do Excel.
                if (aba.Workbook.Use1904DateSystem) serial += 1462;
                if (serial < 2958466) return DateOnly.FromDateTime(DateTime.FromOADate(serial));
            }
            var texto = Texto(coluna, campo, obrigatoria);
            if (texto == null) return null;
            if (DateOnly.TryParseExact(texto, ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "yyyy-MM-dd"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)) return data;
            Avisar($"{campo} inválida: \"{texto}\".");
            return null;
        }

        public void ExigirData(bool exigida, DateOnly? data, string campo)
        {
            if (exigida && data == null && !Avisos.Any(a => a.Contains(campo, StringComparison.Ordinal)))
                Avisar($"Falta {campo}.");
        }
    }
}
