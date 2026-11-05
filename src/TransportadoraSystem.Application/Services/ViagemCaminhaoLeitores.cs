using System.Globalization;
using System.Text;
using ExcelDataReader;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Uma aba de planilha como grade de valores (o valor guardado das fórmulas, como
/// o Excel mostra). Lê .xlsx e .xls (as planilhas antigas do Drive são .xls).
/// Linha e coluna começam em 1, como no Excel (B4 = Valor(4, 2)).
/// </summary>
public sealed class AbaPlanilha(string nome, List<object?[]> linhas)
{
    public string Nome { get; } = nome;
    public int TotalLinhas => linhas.Count;

    public object? Valor(int linha, int coluna)
    {
        if (linha < 1 || linha > linhas.Count) return null;
        var l = linhas[linha - 1];
        return coluna < 1 || coluna > l.Length ? null : l[coluna - 1];
    }

    public string Texto(int linha, int coluna) => Valor(linha, coluna) switch
    {
        null => "",
        DateTime d => d.ToString("dd/MM/yyyy"),
        double n => n.ToString(CultureInfo.InvariantCulture),
        var o => o.ToString()?.Trim() ?? "",
    };

    public decimal? Numero(int linha, int coluna) => Valor(linha, coluna) switch
    {
        double n when !double.IsNaN(n) && !double.IsInfinity(n) => Math.Round((decimal)n, 6),
        int i => i,
        decimal m => m,
        string s when decimal.TryParse(s.Trim().Replace("R$", "").Trim(), NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var x)
                      && s.Trim().Length > 0 && (s.Contains(',') || !s.Contains('.')) => x,
        string s when decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var y) => y,
        _ => null,
    };

    public decimal N(int linha, int coluna) => Numero(linha, coluna) ?? 0;

    public DateOnly? Data(int linha, int coluna) => Valor(linha, coluna) switch
    {
        DateTime d => DateOnly.FromDateTime(d),
        double n when n is > 20000 and < 80000 => DateOnly.FromDateTime(DateTime.FromOADate(n)),
        string s when DateTime.TryParseExact(s.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "d/M/yy"],
                         CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => DateOnly.FromDateTime(d),
        _ => null,
    };

    /// <summary>Primeira célula (linha, coluna) cujo texto normalizado começa com o procurado.</summary>
    public (int Linha, int Coluna)? Achar(string comeco, int ateLinha = 15, int ateColuna = 30)
    {
        var alvo = Normalizar(comeco);
        for (var r = 1; r <= Math.Min(ateLinha, TotalLinhas); r++)
            for (var c = 1; c <= ateColuna; c++)
                if (Normalizar(Texto(r, c)).StartsWith(alvo)) return (r, c);
        return null;
    }

    public static List<AbaPlanilha> Ler(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var ms = new MemoryStream(bytes);
        using var leitor = ExcelReaderFactory.CreateReader(ms);
        var abas = new List<AbaPlanilha>();
        do
        {
            var linhas = new List<object?[]>();
            while (leitor.Read())
            {
                var l = new object?[leitor.FieldCount];
                for (var i = 0; i < leitor.FieldCount; i++) l[i] = leitor.GetValue(i);
                linhas.Add(l);
            }
            abas.Add(new AbaPlanilha(leitor.Name ?? "", linhas));
        } while (leitor.NextResult());
        return abas;
    }
}

// ───────────────────────────── Retornos ─────────────────────────────

public record RetornoLido(
    int Linha, DateOnly Data, string? Destino, string? Transportadora, decimal ValorFrete, decimal Pedagio,
    decimal Adicional, decimal DescontoSeguro, decimal? PorcentagemTip, decimal? DescontoManual,
    string? Motorista, string? Placa, string? Observacoes, string? FormaPagamento, string Chave, decimal FreteTotal);

public record RetornosLidos(int Mes, int Ano, List<RetornoLido> Linhas, List<string> Avisos, decimal? TotalPlanilha, int ForaDoMes);

/// <summary>
/// Planilha "NN-R-MÊS -AA.xlsx" (CONTROLE MENSAL DE RETORNOS): cabeçalho na linha
/// 4 (DATA-FRETE, DESTINO, TRANSPORT., VALOR FRETE, PEDÁGIO, ADICIONAL, DESC. SEG.,
/// DESCONT, PORC. TIP, FRETE TOTAL, MOTORISTA, PLACA, OBSERVAÇÕES, FORMA PTO) e o
/// total na linha "VALOR TOTAL DE RETORNOS". Só entram as linhas do mês do arquivo:
/// a planilha do mês novo nasce da cópia da anterior e às vezes sobram linhas dela.
/// </summary>
public static class RetornosLeitor
{
    public static RetornosLidos Ler(byte[] bytes, string nomeArquivo)
    {
        var abas = AbaPlanilha.Ler(bytes);
        var aba = abas.FirstOrDefault(a => a.Achar("DATA FRETE", 10, 5) != null)
            ?? throw new InvalidOperationException("Não achei o cabeçalho DATA-FRETE: não parece a planilha de retornos.");
        var (hl, hc) = aba.Achar("DATA FRETE", 10, 5)!.Value;

        // Colunas pelo cabeçalho (a ordem é a mesma em todos os meses; o nome confirma)
        int Col(string nome, int padrao)
        {
            var alvo = Normalizar(nome);
            for (var c = hc; c < hc + 20; c++) if (Normalizar(aba.Texto(hl, c)).StartsWith(alvo)) return c;
            return padrao;
        }
        int cData = hc, cDest = Col("DESTINO", hc + 1), cTransp = Col("TRANSPORT", hc + 2), cValor = Col("VALOR FRETE", hc + 3),
            cPed = Col("PEDAGIO", hc + 4), cAdic = Col("ADICIONAL", hc + 5), cSeg = Col("DESC SEG", hc + 6),
            cDesc = Col("DESCONT", hc + 7), cPorc = Col("PORC", hc + 8), cTotal = Col("FRETE TOTAL", hc + 9),
            cMot = Col("MOTORISTA", hc + 10), cPlaca = Col("PLACA", hc + 11), cObs = Col("OBSERVA", hc + 12), cForma = Col("FORMA", hc + 13);

        // Mês: "REFERENTE AO MÊS DE | SETEMBRO --2026" ou o nome do arquivo
        (int Mes, int Ano)? mes = null;
        if (aba.Achar("REFERENTE AO MES", 6, 5) is { } rf)
            for (var c = rf.Coluna + 1; c < rf.Coluna + 8 && mes == null; c++) mes = MesDoNome(aba.Texto(rf.Linha, c));
        mes ??= MesDoNome(nomeArquivo);
        var avisos = new List<string>();
        var linhas = new List<RetornoLido>();
        decimal? totalPlanilha = null;
        var foraDoMes = 0;
        var vistas = new Dictionary<string, int>();

        for (var r = hl + 1; r <= aba.TotalLinhas; r++)
        {
            var linhaTexto = string.Join(" ", Enumerable.Range(1, 14).Select(c => aba.Texto(r, c)));
            if (Normalizar(linhaTexto).Contains("VALOR TOTAL DE RETORNOS"))
            {
                totalPlanilha = aba.Numero(r, cTotal);
                break;
            }
            var valor = aba.Numero(r, cValor);
            var data = aba.Data(r, cData);
            if (valor is null or 0 && data == null) continue;
            if (data == null) { avisos.Add($"Linha {r}: sem data, ficou de fora."); continue; }
            if (valor is null) { avisos.Add($"Linha {r}: VALOR FRETE não é número, ficou de fora."); continue; }
            mes ??= (data.Value.Month, data.Value.Year);
            var inicioMes = new DateOnly(mes.Value.Ano, mes.Value.Mes, 1);
            if (data.Value >= inicioMes.AddMonths(1) && data.Value.Day <= DateTime.DaysInMonth(mes.Value.Ano, mes.Value.Mes))
            {
                // Data depois do mês do arquivo não é sobra de cópia (essas são do mês anterior):
                // é digitação (13/12 no lugar de 13/01). Entra no dia dentro do mês, com aviso.
                var corrigida = new DateOnly(mes.Value.Ano, mes.Value.Mes, data.Value.Day);
                avisos.Add($"Linha {r}: data {data:dd/MM/yyyy} depois do mês da planilha; entrou como {corrigida:dd/MM/yyyy}. Confira.");
                data = corrigida;
            }
            if (data.Value.Month != mes.Value.Mes || data.Value.Year != mes.Value.Ano) { foraDoMes++; continue; }

            var ped = aba.N(r, cPed);
            var adicional = aba.Numero(r, cAdic);
            var obs = aba.Texto(r, cObs);
            if (adicional == null && aba.Texto(r, cAdic).Length > 0)
            {
                avisos.Add($"Linha {r} ({data:dd/MM}, {aba.Texto(r, cTransp)}): ADICIONAL \"{aba.Texto(r, cAdic)}\" não é número; entrou 0.");
                obs = string.Join(" · ", new[] { obs, $"Adicional: {aba.Texto(r, cAdic)}" }.Where(x => x.Length > 0));
            }
            var adic = adicional ?? 0;
            var seg = aba.N(r, cSeg);
            var porc = aba.Numero(r, cPorc);
            var calculado = Math.Round((valor.Value + adic - ped) / 100m * (porc ?? 0), 4);
            var h = aba.Numero(r, cDesc);
            decimal? manual = h.HasValue && Math.Abs(h.Value - calculado) > 0.005m ? h : null;
            var desconto = manual ?? calculado;
            var placa = NormalizarPlaca(aba.Texto(r, cPlaca));
            var transp = aba.Texto(r, cTransp);
            var destino = aba.Texto(r, cDest);

            var baseChave = $"{mes.Value.Ano}-{mes.Value.Mes:00}|{data:yyyy-MM-dd}|{placa}|{Normalizar(transp)}|{valor.Value.ToString("0.00", CultureInfo.InvariantCulture)}|{Normalizar(destino)}";
            vistas[baseChave] = vistas.GetValueOrDefault(baseChave) + 1;
            linhas.Add(new RetornoLido(r, data.Value, Nulo(destino), Nulo(transp), valor.Value, ped, adic, seg, porc, manual,
                Nulo(aba.Texto(r, cMot)), Nulo(placa), Nulo(obs), Nulo(aba.Texto(r, cForma)),
                $"{baseChave}#{vistas[baseChave]}", valor.Value + adic - ped - desconto - seg));
        }
        if (mes == null) throw new InvalidOperationException("Não consegui saber o mês da planilha (nem pelo nome do arquivo).");
        if (foraDoMes > 0) avisos.Add($"{foraDoMes} linha(s) com data de outro mês ficaram de fora (sobra da planilha do mês anterior).");
        return new RetornosLidos(mes.Value.Mes, mes.Value.Ano, linhas, avisos, totalPlanilha, foraDoMes);
    }

    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ───────────────────────────── Acerto (histórico) ─────────────────────────────

public record VolumesLido(string Cliente, decimal Volumes, decimal ProdutoE, decimal ProdutoD, decimal ProdutosB, decimal ValorCaixa, decimal ValorProdutoD);
public record AbastLido(string Forma, decimal Valor, string? Observacao);
public record DespesaLida(string Tipo, string? Descricao, decimal Valor, string? Observacao);
public record RetornoDaViagem(string? Transportadora, decimal Valor, decimal PorcentagemIcms, string? OrigemDestino);

public record ViagemLida(
    string Aba, int Numero, string Motorista, string? PlacaTexto, string? Carreta, string? Destino,
    DateOnly? Saida, DateOnly? Chegada, int? KmSaida, int? KmChegada, decimal? Litros,
    decimal SobraAnterior, decimal Levou, decimal Devolveu, decimal Diarias, decimal Porcentagem,
    List<VolumesLido> Volumes, List<AbastLido> Abastecimentos, List<DespesaLida> Despesas, List<RetornoDaViagem> Retornos,
    Dictionary<string, decimal?> Conferencia);

public record AcertoLido(string Placa, int Mes, int Ano, List<ViagemLida> Viagens, List<string> Avisos);

/// <summary>
/// Acerto mensal do caminhão ("9-V -SETEMBRO-2026.xlsx"), uma aba VIAGEMn por
/// viagem (as vazias, sem data de saída, ficam de fora). Posições fixas do modelo:
/// B2 motorista, B3 placas, B4/B5 saída/chegada, B6 sobra, B7 levou, I2 destino,
/// H3/H4 km, H5 litros, J6 %; clientes nas linhas 9–44; diesel na nota 9–14 e pago
/// 15–19 (coluna I); diárias I21; despesas pelo rótulo da coluna H até a 57;
/// retornos 47–53; B59 devolveu. Os totais da planilha vêm junto para conferir.
/// </summary>
public static class AcertoCaminhaoLeitor
{
    public static AcertoLido Ler(byte[] bytes, string nomeArquivo, string? placaInformada)
    {
        var abas = AbaPlanilha.Ler(bytes).Where(a => System.Text.RegularExpressions.Regex.IsMatch(a.Nome.Trim().ToUpperInvariant(), @"^VIAGEM\s*\d+$")).ToList();
        if (abas.Count == 0) throw new InvalidOperationException("Não achei abas VIAGEM1, VIAGEM2...: não parece o acerto do caminhão.");
        var avisos = new List<string>();
        var viagens = new List<ViagemLida>();

        foreach (var a in abas)
        {
            var saida = a.Data(4, 2);
            if (saida == null) continue;
            var numero = int.Parse(new string(a.Nome.Where(char.IsDigit).ToArray()));
            var placas = Placas(a.Texto(3, 2));
            // Aba com linha inserida no meio dos clientes (XAC abril/2026, VIAGEM5): tudo da
            // linha 45 para baixo desce. A âncora é o rótulo "RETORNOS E OUTROS" (A45).
            var k = 0;
            for (var r = 43; r <= 52; r++)
                if (Normalizar(a.Texto(r, 1)).StartsWith("RETORNOS E OUTROS")) { k = r - 45; break; }
            if (k != 0) avisos.Add($"{a.Nome.Trim()}: a aba tem {Math.Abs(k)} linha(s) a {(k > 0 ? "mais" : "menos")} no meio; lida pelo rótulo RETORNOS E OUTROS.");

            var volumes = new List<VolumesLido>();
            for (var r = 9; r <= 44 + k; r++)
            {
                var cliente = a.Texto(r, 1);
                decimal b = a.N(r, 2), c = a.N(r, 3), d = a.N(r, 4), e = a.N(r, 5), f = a.N(r, 6);
                if (cliente.Length == 0 || b + c + d + e == 0) continue;
                // A produtoD muda por cliente (3; Caldas Novas 3,5; Girassol 2,5): sai do total da linha
                var g = a.N(r, 7);
                var cod = d > 0 ? Math.Round((g - b * f - c * f / 2m - e * 10m) / d, 4) : 3m;
                if (cod <= 0) cod = 3m;
                volumes.Add(new VolumesLido(cliente, b, c, d, e, f, cod));
            }

            var abast = new List<AbastLido>();
            for (var r = 9; r <= 19; r++)
            {
                var v = a.N(r, 9);
                if (v != 0) abast.Add(new AbastLido(r <= 14 ? "NOTA" : "PAGO", v, Nulo(a.Texto(r, 10))));
            }

            var desp = new List<DespesaLida>();
            for (var r = 22; r <= 57 + k; r++)
            {
                var v = a.N(r, 9);
                if (v == 0) continue;
                var rotulo = a.Texto(r, 8);
                var tipo = TipoDoRotulo(rotulo);
                if (r >= 55 + k && tipo == Outra && rotulo.Length == 0) tipo = Descarga;
                desp.Add(new DespesaLida(tipo, tipo == Outra ? Nulo(rotulo) ?? "Outra" : null, v, Nulo(a.Texto(r, 10))));
            }

            var rets = new List<RetornoDaViagem>();
            for (var r = 47 + k; r <= 53 + k; r++)
            {
                var v = a.N(r, 2);
                if (v == 0) continue;
                rets.Add(new RetornoDaViagem(Nulo(a.Texto(r, 1)), v, a.N(r, 3), Nulo(a.Texto(r, 5))));
            }

            var diarias = a.N(21, 9);
            var conf = new Dictionary<string, decimal?>
            {
                ["TotalVolumes"] = a.Numero(58 + k, 7), ["TotalRetornos"] = a.Numero(57 + k, 2), ["ValorTotal"] = a.Numero(59 + k, 7),
                ["TotalDespesas"] = a.Numero(59 + k, 9), ["Sobra"] = a.Numero(60 + k, 2), ["Resultado"] = a.Numero(60 + k, 9),
                ["ComissaoPlanilha"] = a.Numero(7, 9),
            };
            viagens.Add(new ViagemLida(a.Nome.Trim(), numero, a.Texto(2, 2), Nulo(a.Texto(3, 2)), placas.Skip(1).FirstOrDefault(),
                Nulo(a.Texto(2, 9)), saida, a.Data(5, 2), Inteiro(a.Numero(3, 8)), Inteiro(a.Numero(4, 8)), a.Numero(5, 8),
                a.N(6, 2), a.N(7, 2), a.N(59 + k, 2), diarias, a.Numero(6, 10) ?? 6, volumes, abast, desp, rets, conf));
        }
        viagens = viagens.OrderBy(v => v.Numero).ToList();

        // Placa do caminhão: a informada, senão a mais comum nas abas (uma aba pode vir
        // com a placa de outro caminhão digitada por engano)
        var placa = NormalizarPlaca(placaInformada);
        if (string.IsNullOrWhiteSpace(placa))
            placa = viagens.Select(v => Placas(v.PlacaTexto).FirstOrDefault()).Where(p => p != null)
                .GroupBy(p => p!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
        if (placa.Length == 0) throw new InvalidOperationException("Não achei a placa do caminhão nas abas.");

        var mes = MesDoNome(nomeArquivo);
        if (mes == null)
        {
            var d = viagens.Select(v => v.Saida).LastOrDefault(x => x != null);
            if (d == null) throw new InvalidOperationException("Não consegui saber o mês do acerto.");
            mes = (d.Value.Month, d.Value.Year);
            avisos.Add($"Mês tirado das datas das viagens: {NomeMes(mes.Value.Mes)} de {mes.Value.Ano}.");
        }
        return new AcertoLido(placa, mes.Value.Mes, mes.Value.Ano, viagens, avisos);
    }

    public static string TipoDoRotulo(string rotulo)
    {
        var n = Normalizar(rotulo).Replace(" ", "");
        return n switch
        {
            "CARREGAMENTO" => Carregamento,
            "GASOLINA" => Gasolina,
            "CREDCELULAR" or "CREDITOCELULAR" => CredCelular,
            "CAIXINHA" => Caixinha,
            "BORRACHARIA" => Borracharia,
            "AUTOELETRICO" => AutoEletrico,
            "LAVAGEM" => Lavagem,
            "LUBRIFICACAO" => Lubrificacao,
            "MECANICO" => Mecanico,
            "DESCARGA" => Descarga,
            _ => Outra,
        };
    }

    private static int? Inteiro(decimal? v) => v is > 0 ? (int)Math.Round(v.Value) : null;
    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ───────────────────────────── Controle de carregamento ─────────────────────────────

/// <summary>
/// Um cliente do controle de carregamento: caixas normais, produtosE, produtosD e os
/// pacotes de produtoB amarela, produtoB branca e produtoC, cada um à parte (VIA-09).
/// Colunas guarda o que veio de cada coluna da linha (diferente de zero), pelo
/// nome do cabeçalho, para conferir e para reler o carregamento guardado.
/// </summary>
public record ItemCarregamento(string Cliente, decimal Volumes, decimal ProdutoE, decimal ProdutoD,
    decimal Amarelas, decimal Brancas, decimal ProdutosC, Dictionary<string, decimal> Colunas)
{
    public decimal ProdutosB => Amarelas + Brancas;
    public bool Vazio => Volumes == 0 && ProdutoE == 0 && ProdutoD == 0 && Amarelas == 0 && Brancas == 0 && ProdutosC == 0;
}

public record CarregamentoLido(string Aba, DateOnly? Data, string? Placa, string? Motorista, string? Destino,
    string? Observacao, List<ItemCarregamento> Itens, List<string> Avisos);

/// <summary>
/// "CONTROLE DE CARREGAMENTO" (.xls, aba Plan1, uma carga por arquivo): DATA, PLACA,
/// MOTORISTA e DESTINO na linha 2; na linha 4 CLIENTES e os cabeçalhos; um cliente
/// por linha, nome na coluna A, até a linha "TOTAL PRODUTOS" (que não se usa).
/// Por cliente valem os totais da linha, cada um à parte (Admin, 07/10/2026):
///   V VOLUMES = caixas normais, W PRODUTOSE, X PRODUTOSD;
///   S AMARELAS e T BRANCAS = pacotes de produtoB, U PRODUTOSC = pacotes de produtoC.
/// Y ("EMBAL./BAND" = S+T+U) não se usa, nem para conferir. As colunas de detalhe
/// (JB, EB, AB... C-20/N, C-48/V) ficam só guardadas.
/// Antes a leitura somava todas as colunas — detalhe e totais — e dobrava as caixas.
/// Sem as colunas de total (modelo antigo "RESUMO DE CARREGAMENTO", com o nome na
/// coluna B), soma as colunas de cada tipo e avisa.
/// </summary>
public static class CarregamentoLeitor
{
    public static List<CarregamentoLido> Ler(byte[] bytes)
    {
        var lidos = new List<CarregamentoLido>();
        var abas = AbaPlanilha.Ler(bytes);
        foreach (var a in abas)
        {
            var cab = a.Achar("CLIENTES", 10, 6);
            if (cab == null) continue;
            var avisos = new List<string>();
            var (hl, hc) = cab.Value;

            DateOnly? data = null; string? placa = null, motorista = null, destino = null;
            for (var r = 1; r < hl; r++)
                for (var c = 1; c <= 30; c++)
                {
                    var t = Normalizar(a.Texto(r, c));
                    if (t == "DATA" && data == null) data = Proximo(a, r, c, (x, y) => a.Data(x, y));
                    else if (t == "PLACA" && placa == null) placa = Proximo(a, r, c, (x, y) => Placas(a.Texto(x, y)).FirstOrDefault());
                    else if (t == "MOTORISTA" && motorista == null) motorista = Proximo(a, r, c, (x, y) => TextoOuNulo(a, x, y));
                    else if (t == "DESTINO" && destino == null) destino = Proximo(a, r, c, (x, y) => TextoOuNulo(a, x, y));
                }

            var colunas = new List<(int Col, string Nome)>();
            for (var c = 1; c <= hc + 40; c++)
            {
                if (c == hc) continue;
                var nome = a.Texto(hl, c).Trim();
                if (nome.Length > 0 && Normalizar(nome) is not ("CLIENTES" or "CLIENTE")) colunas.Add((c, nome));
            }
            var comTotais = colunas.Any(x => Chave(x.Nome) == "VOLUMES");
            if (!comTotais)
                avisos.Add("Planilha sem as colunas de total (VOLUMES, PRODUTOSE, PRODUTOSD): somei as colunas de cada tipo. Confira.");

            var itens = new List<ItemCarregamento>();
            var ultimaLinha = hl;
            for (var r = hl + 1; r <= a.TotalLinhas; r++)
            {
                var nome = NomeDaLinha(a, r, hc);
                if (Normalizar(nome).StartsWith("TOTAL")) { ultimaLinha = r; break; }
                ultimaLinha = r;
                var valores = new Dictionary<string, decimal>();
                foreach (var (col, cn) in colunas)
                {
                    var v = a.N(r, col);
                    if (v != 0) valores[cn] = v;
                }
                if (Normalizar(nome).Length == 0)
                {
                    if (Montar("", valores, comTotais) is { Vazio: false })
                        avisos.Add($"Linha {r}: caixas sem nome de cliente ficaram de fora.");
                    continue;
                }
                var item = Montar(nome.Trim(), valores, comTotais);
                if (item.Vazio) continue;
                var igual = itens.FindIndex(i => Normalizar(i.Cliente) == Normalizar(item.Cliente));
                if (igual >= 0)
                {
                    avisos.Add($"{item.Cliente} aparece em mais de uma linha: somei.");
                    itens[igual] = Somar(itens[igual], item);
                }
                else itens.Add(item);
            }

            if (data == null) avisos.Add("Sem data na planilha.");
            if (placa == null) avisos.Add("Sem placa na planilha.");
            if (itens.Count == 0) avisos.Add("Nenhum cliente com caixas.");
            lidos.Add(new CarregamentoLido(a.Nome, data, placa, Nulo(motorista), Nulo(destino), Observacao(a, ultimaLinha, hc), itens, avisos));
        }
        // Controle de produtosA subido aqui por engano: dizer onde ele entra
        // ("CONTROLE DE VENDAS DE PRODUTOSA", "QUANTIDADE PRODUTOSA", "DATA RETIRA")
        if (lidos.Count == 0 && abas.Any(a => a.Achar("CONTROLE DE VENDAS DE B", 6, 15) != null
                || a.Achar("QUANTIDADE PRODUTOSA", 6, 15) != null || a.Achar("DATA RETIRA", 6, 15) != null))
            throw new InvalidOperationException("Isto é um controle de produtosA: importe em Estoques → ProdutosA.");
        if (lidos.Count == 0) throw new InvalidOperationException("Não achei o cabeçalho CLIENTES: não parece o controle de carregamento.");
        return lidos;
    }

    /// <summary>
    /// Monta o cliente a partir dos valores da linha (nome do cabeçalho → número).
    /// É a mesma conta para a planilha e para o carregamento guardado (Colunas).
    /// </summary>
    public static ItemCarregamento Montar(string cliente, Dictionary<string, decimal> valores, bool? comTotais = null)
    {
        var totais = comTotais ?? valores.Keys.Any(k => Chave(k) == "VOLUMES");
        decimal volumes = 0, produtoE = 0, produtoD = 0, amarelas = 0, brancas = 0, produtosC = 0;
        foreach (var (nome, v) in valores)
        {
            var k = Chave(nome);
            if (k.StartsWith("AMAREL")) amarelas += v;
            else if (k.StartsWith("BRANC")) brancas += v;
            else if (k.StartsWith("BANDE") || k.StartsWith("BANDJ")) produtosC += v;
            else if (totais)
            {
                // Só os totais da linha: V, W e X
                if (k == "VOLUMES") volumes += v;
                else if (k == "PRODUTOSE") produtoE += v;
                else if (k == "PRODUTOSD") produtoD += v;
            }
            else
            {
                // Modelo sem totais: cada coluna no seu tipo
                if (k.StartsWith("TOTAL") || k.StartsWith("EMBAL") || k is "VOLUMES" or "PRODUTOSE" or "PRODUTOSD") continue;
                if (k.StartsWith("PRODUTOE")) produtoE += v;
                else if (k.StartsWith("COD") || (k.Length > 1 && k[0] == 'C' && char.IsDigit(k[1]))) produtoD += v;
                else if (k.StartsWith("AM")) amarelas += v;
                else if (k.StartsWith("BR")) brancas += v;
                else volumes += v;
            }
        }
        return new ItemCarregamento(cliente, volumes, produtoE, produtoD, amarelas, brancas, produtosC, valores);
    }

    private static ItemCarregamento Somar(ItemCarregamento x, ItemCarregamento y)
    {
        var col = new Dictionary<string, decimal>(x.Colunas);
        foreach (var (k, v) in y.Colunas) col[k] = col.GetValueOrDefault(k) + v;
        return new ItemCarregamento(x.Cliente, x.Volumes + y.Volumes, x.ProdutoE + y.ProdutoE, x.ProdutoD + y.ProdutoD,
            x.Amarelas + y.Amarelas, x.Brancas + y.Brancas, x.ProdutosC + y.ProdutosC, col);
    }

    // Cabeçalho sem acento, espaço nem pontuação: "EMBAL./BAND" → "EMBALBAND", "C-20/N" → "C20N"
    private static string Chave(string nome) => Normalizar(nome).Replace(" ", "");

    // Nome do cliente: coluna do cabeçalho CLIENTES; no modelo antigo ela traz 1º, 2º... e o nome vem na seguinte
    private static string NomeDaLinha(AbaPlanilha a, int r, int hc)
    {
        var nome = a.Texto(r, hc);
        if (!string.IsNullOrWhiteSpace(nome) && !Ordinal(nome)) return nome;
        // Número na coluna seguinte é caixa (JB), não nome
        return a.Numero(r, hc + 1) == null ? a.Texto(r, hc + 1) : "";
    }

    private static bool Ordinal(string s)
    {
        var t = (s ?? "").Trim().TrimEnd('º', '°', 'o', 'O', '.');
        return t.Length is > 0 and <= 3 && t.All(char.IsDigit);
    }

    // "OBSERVAÇÕES SOBRE O CARREGAMENTO" na coluna do nome, depois da linha TOTAL: o texto das linhas de baixo
    private static string? Observacao(AbaPlanilha a, int depois, int hc)
    {
        for (var r = depois; r <= Math.Min(a.TotalLinhas, depois + 10); r++)
        {
            if (!Normalizar(a.Texto(r, hc)).StartsWith("OBSERVAC")) continue;
            var partes = new List<string>();
            for (var x = r + 1; x <= Math.Min(a.TotalLinhas, r + 5); x++)
                if (a.Texto(x, hc) is { Length: > 0 } t) partes.Add(t.Trim());
            return partes.Count > 0 ? string.Join(" / ", partes) : null;
        }
        return null;
    }

    private static string? TextoOuNulo(AbaPlanilha a, int r, int c) => a.Texto(r, c) is { Length: > 0 } s ? s : null;

    private static T? Proximo<T>(AbaPlanilha a, int r, int c, Func<int, int, T?> ler)
    {
        for (var x = c + 1; x <= c + 8; x++)
            if (ler(r, x) is { } v) return v;
        return default;
    }

    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
