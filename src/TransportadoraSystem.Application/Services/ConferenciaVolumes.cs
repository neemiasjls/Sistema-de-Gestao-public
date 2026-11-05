using TransportadoraSystem.Core.Entities;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Conferência de um controle de volumes embalados — as fórmulas das linhas 16,
/// 23 e 26 da planilha, feitas em produtosC para não carregar o 0,0833.
///
/// Por tipo cru (caixa da fazenda):
///   estoque = entradas + sobra anterior (+ PVC de 20 feito com caixa de 30, no Ab-20)
///           − consumo das SAÍDAS dos produzidos (embalado = 12 produtosC, PVC = 10)
///           − saídas cruas diretas − sobra − quebra (− usado para PVC de 20, no Ab)
/// Tem de dar zero. Por tipo produzido: estoque = embalou − saídas.
/// </summary>
public static class ConferenciaVolumes
{
    public const int ProdutosCPorCaixa = 12;
    public const decimal Tolerancia = 0.05m;   // produtoC: resto de arredondamento

    public record TipoCru(string Chave, string Nome, string Codigo, Func<ControleVolumesLinha, decimal> Ler);

    public record TipoProduzido(string Chave, string Nome, string Codigo, string Cru, int ProdutosCConsumidas,
        Func<ControleVolumesLinha, decimal> Ler);

    public static readonly TipoCru[] Crus =
    [
        new("ab",   "Ab",    "AB",   l => l.Ab),
        new("ab20", "Ab-20", "AB20", l => l.Ab20),
        new("bb",   "Bb",    "BB",   l => l.Bb),
        new("av",   "Av",    "AV",   l => l.Av),
    ];

    public static readonly TipoProduzido[] Produzidos =
    [
        new("abe",     "Abe",      "AB",      "ab",   12, l => l.Abe),
        new("ave",     "Ave",      "AVE",     "av",   12, l => l.Ave),
        new("grPvcBr", "Gr-PvcBr", "PVCAB",   "ab",   10, l => l.GrPvcBr),
        new("mdPvcBr", "Md-PvcBr", "PVCMB",   "bb",   10, l => l.MdPvcBr),
        new("grPvc20", "GrPvc-20", "PVCAB20", "ab20", 10, l => l.GrPvc20),
        new("grPvcVr", "Gr PvcVr", "PVCV",    "av",   10, l => l.GrPvcVr),
    ];

    // PVC de 20 com caixa de 30: 10 produtosC × 20 volumes = 200 volumes, e a
    // produtoC de Ab tem 30 → 200/30 produtosC de Ab por caixa de PVC de 20.
    public const decimal ProdutosCAbPorPvcVinte = 200m / 30m;
    // A mesma caixa entra no Ab-20 como se viesse da fazenda (10 produtosC de 20),
    // para a saída do PVCAB20 descontar dali como sempre (10/12 de caixa).
    public const decimal ProdutosCAb20PorPvcVinte = 10m;

    public record LinhaCru(
        string Chave, string Nome, decimal Entradas, decimal SobraAnterior, decimal PvcVinteEntrada,
        decimal ConsumoProduzidos, decimal UsadoPvcVinte, decimal SaidasCruas, decimal Sobra,
        decimal Quebra, decimal Estoque, bool Bate);

    public record LinhaProduzido(string Chave, string Nome, string Codigo, decimal Embalou, decimal Saidas,
        decimal Estoque, bool Bate);

    public record ProdutosB(decimal UnidadesAmarelas, decimal UnidadesBrancas);

    public record Resultado(
        List<LinhaCru> Crus, List<LinhaProduzido> Produzidos, ProdutosB ProdutosB,
        string Status, List<string> Avisos);

    public static ControleVolumesLinha Somar(IEnumerable<ControleVolumesLinha> linhas)
    {
        var t = new ControleVolumesLinha();
        foreach (var l in linhas)
        {
            t.Ab += l.Ab; t.Ab20 += l.Ab20; t.Bb += l.Bb; t.Av += l.Av;
            t.Abe += l.Abe; t.Ave += l.Ave; t.GrPvcBr += l.GrPvcBr; t.MdPvcBr += l.MdPvcBr;
            t.GrPvc20 += l.GrPvc20; t.GrPvcVr += l.GrPvcVr;
        }
        return t;
    }

    /// <summary>
    /// A sobra que fica no controle: a anotada menos o que foi vendido de fazenda (cru)
    /// pela anotação, que sai da própria sobra (Admin, 21/10/2026: sobra 3, vendeu 3,
    /// fica 0 e passa 0 ao próximo). Nunca abaixo de zero.
    /// </summary>
    public static ControleVolumesLinha SobraFinal(IEnumerable<ControleVolumesLinha> linhasDoControle)
    {
        var linhas = linhasDoControle.ToList();
        var s = Somar(linhas.Where(l => l.Grupo == ControleVolumesLinha.Sobra));
        var v = Somar(linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida && l.Origem == ControleVolumesLinha.OrigemAnotacao));
        s.Ab = Math.Max(0, s.Ab - v.Ab); s.Ab20 = Math.Max(0, s.Ab20 - v.Ab20);
        s.Bb = Math.Max(0, s.Bb - v.Bb); s.Av = Math.Max(0, s.Av - v.Av);
        return s;
    }

    public static bool TemAlgum(ControleVolumesLinha l) =>
        Crus.Any(c => c.Ler(l) != 0) || Produzidos.Any(p => p.Ler(l) != 0);

    // Linhas 28–29 da planilha: cada caixa embalada usa 30 produtosB (uma por
    // dúzia); o pacote tem 150. Amarela vai no embalado branco (Abe); branca, no
    // embalado vermelho (Ave). É o que sai do estoque de produtosB (VOLUME-04, EST-02).
    public const int ProdutosBPorCaixa = 30;

    public static ProdutosB ProdutosBUsadas(ControleVolumes c)
    {
        var embalou = Somar(c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Embalou));
        return new ProdutosB(embalou.Abe * ProdutosBPorCaixa, embalou.Ave * ProdutosBPorCaixa);
    }

    /// <param name="sobraAnterior">Sobra que vem do controle anterior (ou a digitada à mão).</param>
    public static Resultado Calcular(ControleVolumes c, ControleVolumesLinha sobraAnterior)
    {
        var linhas = c.Linhas.ToList();
        ControleVolumesLinha Grupo(string g) => Somar(linhas.Where(l => l.Grupo == g));

        var entradas = Grupo(ControleVolumesLinha.Entrada);
        var saidas = Grupo(ControleVolumesLinha.Saida);
        var embalou = Grupo(ControleVolumesLinha.Embalou);
        var sobra = SobraFinal(linhas);
        var quebra = Grupo(ControleVolumesLinha.Quebra);

        var crus = Crus.Select(cru =>
        {
            var consumo = Produzidos.Where(p => p.Cru == cru.Chave)
                .Sum(p => p.Ler(saidas) * p.ProdutosCConsumidas);
            var pvcEntrada = cru.Chave == "ab20" ? c.PvcVinteComCaixaAb * ProdutosCAb20PorPvcVinte : 0;
            var pvcUsado = cru.Chave == "ab" ? c.PvcVinteComCaixaAb * ProdutosCAbPorPvcVinte : 0;

            var estoque = cru.Ler(entradas) + cru.Ler(sobraAnterior) + pvcEntrada
                        - consumo - pvcUsado - cru.Ler(saidas) - cru.Ler(sobra) - cru.Ler(quebra);
            estoque = Math.Round(estoque, 4);

            return new LinhaCru(cru.Chave, cru.Nome, cru.Ler(entradas), cru.Ler(sobraAnterior),
                Math.Round(pvcEntrada, 4), consumo, Math.Round(pvcUsado, 4), cru.Ler(saidas),
                cru.Ler(sobra), cru.Ler(quebra), estoque, Math.Abs(estoque) < Tolerancia);
        }).ToList();

        var produzidos = Produzidos.Select(p =>
        {
            var est = p.Ler(embalou) - p.Ler(saidas);
            return new LinhaProduzido(p.Chave, p.Nome, p.Codigo, p.Ler(embalou), p.Ler(saidas), est,
                Math.Abs(est) < 0.001m);
        }).ToList();

        var produtosB = ProdutosBUsadas(c);

        var avisos = new List<string>();
        var temEntrada = linhas.Any(l => l.Grupo == ControleVolumesLinha.Entrada);
        var temSaida = linhas.Any(l => l.Grupo == ControleVolumesLinha.Saida);
        if (!linhas.Any(l => l.Grupo == ControleVolumesLinha.Embalou && TemAlgum(l)))
            avisos.Add("Não tem o que foi embalado.");
        if (!temEntrada) avisos.Add("Faltam as entradas da fazenda.");
        if (!temSaida) avisos.Add("Faltam as saídas.");
        foreach (var l in linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida))
        {
            var quem = string.IsNullOrWhiteSpace(l.Nome) ? "sem nome" : l.Nome;
            if (string.IsNullOrWhiteSpace(l.Nome)) avisos.Add("Tem saída sem nome.");
            if (l.Data == null) avisos.Add($"Saída de {quem} sem data.");
            else if (l.Data < c.Data) avisos.Add($"Saída de {quem} com data antes da produtoB ({l.Data:dd/MM}).");
        }
        foreach (var l in linhas.Where(l => l.Grupo == ControleVolumesLinha.Entrada && l.Data == null))
            avisos.Add($"Entrada {(string.IsNullOrWhiteSpace(l.Nome) ? "sem nome" : l.Nome)} sem data.");
        if (c.PvcVinteComCaixaAb > embalou.GrPvc20)
            avisos.Add("PVC de 20 feito com caixa de 30 é maior que o PVCAB20 embalado.");

        string status;
        if (!temEntrada || !temSaida) status = "incompleto";
        else if (crus.All(x => x.Bate) && produzidos.All(x => x.Bate)) status = "bate";
        else status = "nao_bate";

        return new Resultado(crus, produzidos, produtosB, status, avisos.Distinct().ToList());
    }
}
