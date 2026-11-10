using static TransportadoraSystem.Application.Services.ConferenciaCobranca;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Boleto pago por Pix (BOL-14), sem banco nem HTTP: para cada crédito de Pix do extrato do Sicoob cujo
/// CPF/CNPJ do pagador é o de um cliente (CD/Perfil, configuração da emissão; só dígitos), acha entre os
/// boletos a receber em aberto DELE um de mesmo valor ou a combinação cuja soma bata (centavos exatos).
/// Entram também os vencidos, pelo valor de face, sem alerta (atraso pago por Pix é normal). Mais de uma
/// combinação = mostra as opções e não escolhe. Pix sem CNPJ no extrato: busca só pela soma em todos os
/// clientes e a tela avisa (UI-04). "CRÉD.LIQUIDAÇÃO COBRANÇA" não é Pix (é a ConferenciaCobranca).
/// </summary>
public static class PixBoletos
{
    /// <summary>Cliente pagador: código (CD/GIRASSOL), nome e CNPJ da configuração da emissão.</summary>
    public record Cliente(string Codigo, string Nome, string Cnpj);

    public record Pix(string Id, DateOnly Data, decimal Valor, string Descricao, string Complemento, string CpfCnpj);

    /// <summary>
    /// Opcoes: cada uma é um conjunto de boletos em aberto que soma o Pix (uma só = certo; várias = escolher).
    /// MaisOpcoes: havia mais que as mostradas. JaRecebidos: nada em aberto, mas boletos já marcados no dia do Pix somam o valor.
    /// </summary>
    public record Achado(Pix Pix, Cliente? Pagador, bool SemCnpj, IReadOnlyList<IReadOnlyList<Boleto>> Opcoes,
        bool MaisOpcoes, IReadOnlyList<Boleto> JaRecebidos)
    {
        public bool Certo => Opcoes.Count == 1 && !MaisOpcoes;
        public bool Duvida => Opcoes.Count > 1 || MaisOpcoes;
    }

    // Candidatos: os 20 em aberto de vencimento mais perto do Pix (um cliente tem poucos em aberto); até 6 opções na tela
    private const int MaxCandidatos = 20, MaxOpcoes = 6;

    public static bool EhPix(string descricao, string complemento) =>
        !EhLiquidacaoCobranca(descricao) && (descricao + " " + complemento).Contains("PIX", StringComparison.OrdinalIgnoreCase);

    public static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());

    // CNPJ/CPF escrito no texto do Pix: "Recebimento Pix|@TRANSPORTADORA EXEMPLO LTDA|@XX.XXX.XXX/0004-XX|@..."
    // (o Sicoob põe espaço no lugar da barra e às vezes deixa o campo CpfCnpj vazio)
    private static readonly System.Text.RegularExpressions.Regex DocNoTexto = new(
        @"(?<!\d)(\d{2}\.?\d{3}\.?\d{3}[/ ]?\d{4}-?\d{2}|\d{3}\.\d{3}\.\d{3}-\d{2})(?!\d)");

    /// <summary>Documento do pagador: o campo do extrato; sem ele, o CNPJ/CPF escrito na descrição ou no complemento.</summary>
    public static string DocumentoDoPix(Pix p)
    {
        var doc = Digitos(p.CpfCnpj);
        if (doc.Length > 0) return doc;
        var m = DocNoTexto.Match(p.Descricao + " | " + p.Complemento);
        return m.Success ? Digitos(m.Value) : "";
    }

    /// <summary>
    /// Quantos boletos em aberto do cliente, mais antigos que o último da combinação, ela deixa de fora. O cliente
    /// paga os que estão para vencer e às vezes alguns adiantados, mas não pula os mais antigos (Admin, BOL-14).
    /// </summary>
    private static int Pulados(IReadOnlyList<Boleto> combinacao, IEnumerable<Boleto> abertosDoCliente)
    {
        var ultimo = combinacao.Max(b => b.Vencimento ?? DateOnly.MaxValue);
        var ids = combinacao.Select(b => b.Id).ToHashSet();
        return abertosDoCliente.Count(b => !ids.Contains(b.Id) && (b.Vencimento ?? DateOnly.MaxValue) < ultimo);
    }

    public static List<Achado> Achar(IEnumerable<Pix> pixes, IReadOnlyList<Boleto> boletos, IReadOnlyList<Cliente> clientes)
    {
        var usados = new HashSet<int>();
        var achados = new List<Achado>();
        foreach (var p in pixes.Where(p => p.Valor > 0 && EhPix(p.Descricao, p.Complemento)).OrderBy(p => p.Data).ThenBy(p => p.Id))
        {
            var doc = DocumentoDoPix(p);
            var semCnpj = doc.Length == 0;
            Cliente? pagador = null;
            if (semCnpj)
            {
                // Repasse da Strada é Pix e não é boleto (StradaService)
                if ((p.Descricao + " " + p.Complemento).Contains("STRADA", StringComparison.OrdinalIgnoreCase)) continue;
            }
            else
            {
                pagador = clientes.FirstOrDefault(c => Digitos(c.Cnpj) == doc);
                if (pagador == null) continue; // Pix de quem não é cliente de boleto
            }
            IReadOnlyList<Cliente> deQuem = pagador != null ? [pagador] : clientes;

            var opcoes = new List<(List<Boleto> Boletos, int Pulados)>();
            foreach (var c in deQuem)
            {
                var todosAbertos = boletos
                    .Where(b => b.Cliente == c.Codigo && b.RecebidoEm == null && b.Valor > 0 && !usados.Contains(b.Id)).ToList();
                var abertos = todosAbertos
                    .OrderBy(b => Math.Abs((b.Vencimento ?? p.Data).DayNumber - p.Data.DayNumber)).Take(MaxCandidatos)
                    .OrderByDescending(b => b.Valor).ThenBy(b => b.Vencimento).ToList();
                // Mais combinações que o mostrado: as que pulam boleto antigo são descartadas abaixo
                opcoes.AddRange(Somas(abertos, p.Valor, MaxOpcoes * 4)
                    .Select(o => (o, Pulados(o, todosAbertos))));
            }
            // Com o pagador certo (CNPJ), vale o costume dele: as que não pulam boleto mais antigo ficam e as outras
            // somem; sem nenhuma assim, todas ficam, das que pulam menos para as que pulam mais. Sem CNPJ, só ordena.
            if (pagador != null && opcoes.Any(o => o.Pulados == 0)) opcoes = opcoes.Where(o => o.Pulados == 0).ToList();
            var ordenadas = opcoes
                .OrderBy(o => o.Pulados).ThenBy(o => o.Boletos.Count)
                .Select(o => o.Boletos.OrderBy(b => b.Vencimento ?? DateOnly.MaxValue).ThenBy(b => b.NossoNumero).ToList())
                .ToList();
            var mais = ordenadas.Count > MaxOpcoes;
            var mostrar = ordenadas.Take(MaxOpcoes).Select(o => (IReadOnlyList<Boleto>)o).ToList();
            if (mostrar.Count == 1 && !mais) foreach (var b in mostrar[0]) usados.Add(b.Id);

            IReadOnlyList<Boleto> ja = [];
            if (mostrar.Count == 0)
                foreach (var c in deQuem)
                {
                    var marcados = boletos.Where(b => b.Cliente == c.Codigo && b.RecebidoEm == p.Data && b.Valor > 0)
                        .OrderByDescending(b => b.Valor).Take(MaxCandidatos).ToList();
                    if (Somas(marcados, p.Valor, 1) is [var s]) { ja = s; break; }
                }
            achados.Add(new(p, pagador, semCnpj, mostrar, mais, ja));
        }
        return achados;
    }
}
