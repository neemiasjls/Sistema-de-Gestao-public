using System.Text.RegularExpressions;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Pix das produtosC da BRZ (EST-08), sem banco nem HTTP. O Pix é da BRZ pelo NOME do pagador escrito no extrato
/// ("BRZ DISTRIBUIDOR LTDA"); o CNPJ vem mascarado ("**.**0.991/0001-**") e só ajuda quando o nome não veio
/// (dígitos visíveis 0.991/0001). Nunca só pelo valor. O Pix soma as vendas de produtoC em aberto mais antigas
/// (1, 2 ou 3 carregamentos), sem pular uma em aberto mais antiga. Uma combinação = certo; mais de uma = escolher;
/// nenhuma = avisa "Pix da BRZ sem produtosC que somem R$ X" (não força).
/// </summary>
public static class PixProdutosC
{
    public record Venda(int Id, DateOnly Data, decimal Valor);

    public record Achado(PixBoletos.Pix Pix, string Chave, IReadOnlyList<IReadOnlyList<Venda>> Opcoes, bool JaBaixado)
    {
        public bool Certo => !JaBaixado && Opcoes.Count == 1;
        public bool Duvida => !JaBaixado && Opcoes.Count > 1;
        public bool SemCasamento => !JaBaixado && Opcoes.Count == 0;
    }

    public const string NomeBrz = "BRZ DISTRIBUIDOR LTDA";
    private const int MaxOpcoes = 6;

    private static readonly Regex CnpjMascarado = new(@"0\.?991[/ ]?0001");
    private static readonly Regex EndToEnd = new(@"\bE\d{20}[A-Za-z0-9]{11}\b");

    private static string Compacto(string? s) => new((s ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>É Pix da BRZ: o nome no texto (inteiro ou cortado pelo banco, 10+ letras) ou, sem nome, os dígitos visíveis do CNPJ.</summary>
    public static bool EhDaBrz(PixBoletos.Pix p)
    {
        var alvo = Compacto(NomeBrz);
        foreach (var parte in (p.Descricao + "|" + p.Complemento).Split('|'))
        {
            var nome = Compacto(parte.Trim().TrimStart('@'));
            if (nome.Length >= 10 && (nome.StartsWith(alvo, StringComparison.Ordinal) || alvo.StartsWith(nome, StringComparison.Ordinal)))
                return true;
        }
        return CnpjMascarado.IsMatch(p.Descricao + " " + p.Complemento + " " + p.CpfCnpj);
    }

    /// <summary>Chave do Pix para não dar baixa duas vezes: o EndToEndId escrito no extrato; sem ele, o id do lançamento.</summary>
    public static string Chave(PixBoletos.Pix p)
    {
        var m = EndToEnd.Match(p.Descricao + " " + p.Complemento);
        return m.Success ? m.Value : p.Id;
    }

    /// <param name="abertas">Vendas de produtoC da BRZ em aberto (sem recebimento).</param>
    /// <param name="chavesUsadas">Chaves de Pix já gravadas nas vendas (RecebidoPix).</param>
    public static List<Achado> Achar(IEnumerable<PixBoletos.Pix> pixes, IReadOnlyList<Venda> abertas, IReadOnlySet<string> chavesUsadas)
    {
        var usadas = new HashSet<int>();
        var achados = new List<Achado>();
        foreach (var p in pixes.Where(p => p.Valor > 0 && PixBoletos.EhPix(p.Descricao, p.Complemento) && EhDaBrz(p))
                     .OrderBy(p => p.Data).ThenBy(p => p.Id))
        {
            var chave = Chave(p);
            if (chavesUsadas.Contains(chave) || chavesUsadas.Contains(p.Id)) { achados.Add(new(p, chave, [], true)); continue; }
            // Só as vendas até o dia do Pix (não paga carga que ainda não saiu)
            var livres = abertas.Where(v => !usadas.Contains(v.Id) && v.Data <= p.Data)
                .OrderBy(v => v.Data).ThenBy(v => v.Id).ToList();
            var opcoes = Combinacoes(livres, p.Valor);
            if (opcoes.Count == 1) foreach (var v in opcoes[0]) usadas.Add(v.Id);
            achados.Add(new(p, chave, opcoes, false));
        }
        return achados;
    }

    /// <summary>
    /// Conjuntos que somam o valor sem pular venda mais antiga: para cada dia-limite, entram todas as dos dias
    /// anteriores e alguma(s) do próprio dia (empate de dia é o único jeito de haver mais de uma opção).
    /// </summary>
    public static List<IReadOnlyList<Venda>> Combinacoes(List<Venda> livres, decimal valor)
    {
        var res = new List<IReadOnlyList<Venda>>();
        decimal antes = 0;
        foreach (var dia in livres.Select(v => v.Data).Distinct().OrderBy(d => d))
        {
            var doDia = livres.Where(v => v.Data == dia).ToList();
            var falta = valor - antes;
            if (falta <= 0.005m) break;
            var obrigatorias = livres.Where(v => v.Data < dia).ToList();
            foreach (var sub in Somas(doDia, falta))
            {
                res.Add([.. obrigatorias, .. sub]);
                if (res.Count >= MaxOpcoes) return res;
            }
            antes += doDia.Sum(v => v.Valor);
        }
        return res;
    }

    private static List<List<Venda>> Somas(List<Venda> c, decimal alvo)
    {
        var res = new List<List<Venda>>();
        var atual = new List<Venda>();
        void Ir(int i, decimal soma)
        {
            if (res.Count >= MaxOpcoes) return;
            if (atual.Count > 0 && Math.Abs(soma - alvo) < 0.005m) { res.Add([.. atual]); return; }
            if (i >= c.Count || soma > alvo + 0.005m) return;
            atual.Add(c[i]); Ir(i + 1, soma + c[i].Valor); atual.RemoveAt(atual.Count - 1);
            Ir(i + 1, soma);
        }
        Ir(0, 0);
        return res;
    }
}
