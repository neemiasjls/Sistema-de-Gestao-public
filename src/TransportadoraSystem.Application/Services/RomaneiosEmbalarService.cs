using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Liga o romaneio da fazenda SEM funrural (volumes que pegam na fazenda para embalar
/// no barracão) ao controle de volumes embalados, como ENTRADA "PAULO" com a data do
/// romaneio e as caixas em produtosC (× 12) no tipo cru certo.
///
/// Qual controle: o primeiro com data de produtoB ≥ data do romaneio e no máximo
/// JanelaDias depois (embalam quarta e sexta; o romaneio da quarta vem com a data
/// da terça ou da quarta, o da sexta com a da sexta). Sem controle ainda, fica
/// aguardando e entra quando o controle for criado. Nunca entra em controle
/// importado das planilhas (312–328, Origem = 'planilha'), nem romaneio com data
/// até a do último deles: esse histórico já tem as entradas digitadas.
///
/// Um romaneio vira no máximo UMA linha (Origem = 'romaneio:NUMERO', com índice
/// único no banco). Trocar de controle move a linha; tirar apaga a linha e marca
/// Retirado, para não voltar sozinho.
/// </summary>
public class RomaneiosEmbalarService(AppDbContext db, ILogger<RomaneiosEmbalarService> logger)
{
    public const int JanelaDias = 3;
    public const string Fornecedor = "PAULO";   // a fazenda é do Paulo Alfa (VOLUME-07)
    private const string TipoRomaneio = "ROMANEIO";
    private const string OrigemPlanilha = "planilha";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public record ItemInfo(string Produto, string Descricao, decimal Caixas, string? Cru, string? CruNome, decimal ProdutosC);

    public record Info(
        int Numero, DateOnly Data, int Caixas, List<ItemInfo> Itens, List<string> Avisos,
        string Situacao,          // lancado | aguardando | retirado | historico | sem_tipo
        int? ControleId, int? ControleNumero, DateOnly? ControleData, int? LinhaId,
        int? AcertoId, int? AcertoNumero);

    private record ControleResumo(int Id, int Numero, DateOnly Data, string Origem);

    // ───────────────────────── Leitura dos PDFs ─────────────────────────

    /// <summary>
    /// Lê os PDFs de romaneio guardados nos acertos que ainda não foram lidos e
    /// guarda cada romaneio (com e sem funrural) uma vez, pelo número. Romaneio
    /// que já estava guardado não é tocado (a ligação com os volumes continua).
    /// </summary>
    public async Task SincronizarAsync()
    {
        var arquivos = await db.AcertoAlfaArquivos
            .Where(a => a.Tipo == TipoRomaneio && a.RomaneiosLidosEm == null)
            .OrderBy(a => a.Id)
            .ToListAsync();
        if (arquivos.Count == 0) return;

        var numeros = new HashSet<int>();
        var lidos = new List<(AcertoAlfaArquivo Arquivo, RomaneioFazendaLeitor.Lido Romaneio)>();
        foreach (var a in arquivos)
        {
            try { lidos.AddRange(RomaneioFazendaLeitor.Ler(a.Conteudo).Select(r => (a, r))); }
            catch (Exception ex) { logger.LogWarning(ex, "Romaneio da fazenda ilegível no arquivo {Id}", a.Id); }
            a.RomaneiosLidosEm = DateTime.UtcNow;
        }

        var numerosLidos = lidos.Select(l => l.Romaneio.Numero).Distinct().ToList();
        var existentes = await db.RomaneiosFazendaLidos
            .Where(r => numerosLidos.Contains(r.Numero))
            .ToDictionaryAsync(r => r.Numero);

        foreach (var (arquivo, r) in lidos)
        {
            if (!numeros.Add(r.Numero)) continue;   // repetido no mesmo lote
            if (existentes.TryGetValue(r.Numero, out var ja))
            {
                // PDF subido de novo (o antigo pode ter sido removido): só aponta para ele
                if (ja.ArquivoId == null) { ja.ArquivoId = arquivo.Id; ja.AcertoId = arquivo.AcertoId; ja.AtualizadoEm = DateTime.UtcNow; }
                continue;
            }
            db.RomaneiosFazendaLidos.Add(new RomaneioFazendaLido
            {
                Numero = r.Numero, Data = r.Data, AcertoId = arquivo.AcertoId, ArquivoId = arquivo.Id,
                DescFunrural = r.DescFunrural, Caixas = r.Caixas, Valor = r.Valor,
                ItensJson = JsonSerializer.Serialize(r.Itens, Json),
            });
        }

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            // Outro envio guardou o mesmo romaneio ao mesmo tempo: na próxima leitura acerta
            logger.LogWarning(ex, "Conflito ao guardar romaneios da fazenda lidos");
            db.ChangeTracker.Clear();
        }
    }

    // ───────────────────────── Ligação com os volumes ─────────────────────────

    /// <summary>
    /// Põe como entrada, no controle da regra, cada romaneio sem funrural que ainda
    /// não está em controle nenhum e não foi tirado à mão. Devolve quantos entraram.
    /// </summary>
    public async Task<int> LigarPendentesAsync()
    {
        var candidatos = await db.RomaneiosFazendaLidos.AsNoTracking()
            .Where(r => !r.DescFunrural && !r.Retirado)
            .ToListAsync();
        if (candidatos.Count == 0) return 0;

        var lancados = await OrigensLancadas();
        var pendentes = candidatos.Where(r => !lancados.ContainsKey(ControleVolumesLinha.OrigemRomaneio(r.Numero))).ToList();
        if (pendentes.Count == 0) return 0;

        var controles = await Controles();
        var limite = LimiteHistorico(controles);
        var ordens = new Dictionary<int, int>();   // dois romaneios no mesmo controle
        int novos = 0;
        foreach (var r in pendentes.OrderBy(r => r.Data).ThenBy(r => r.Numero))
        {
            if (limite is { } l && r.Data <= l) continue;
            var alvo = Alvo(r.Data, controles);
            if (alvo == null || alvo.Origem == OrigemPlanilha) continue;
            var linha = await NovaLinha(r, alvo.Id);
            if (linha == null) continue;
            if (ordens.TryGetValue(alvo.Id, out var ultima)) linha.Ordem = ultima + 1;
            ordens[alvo.Id] = linha.Ordem;
            db.VolumesControleLinhas.Add(linha);
            novos++;
        }
        if (novos == 0) return 0;

        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            // Dois pedidos ligando ao mesmo tempo: o índice único segura a segunda linha
            logger.LogWarning(ex, "Conflito ao lançar romaneio da fazenda nos volumes");
            db.ChangeTracker.Clear();
            return 0;
        }
        return novos;
    }

    /// <summary>Leitura + ligação, sem deixar erro derrubar a tela que chamou.</summary>
    public async Task AtualizarAsync()
    {
        try
        {
            await SincronizarAsync();
            await LigarPendentesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao ligar romaneios da fazenda aos volumes embalados");
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Põe (ou passa) o romaneio para o controle escolhido à mão.</summary>
    public async Task<string?> MoverAsync(int numero, int controleId)
    {
        var r = await db.RomaneiosFazendaLidos.FirstOrDefaultAsync(x => x.Numero == numero);
        if (r == null) return "Romaneio da fazenda não encontrado.";
        if (r.DescFunrural) return $"O romaneio {numero} é de cliente (tem desconto de funrural); não entra nos volumes.";
        if (!await db.VolumesControles.AnyAsync(c => c.Id == controleId)) return "Controle de volumes não encontrado.";

        var origem = ControleVolumesLinha.OrigemRomaneio(numero);
        var linha = await db.VolumesControleLinhas.FirstOrDefaultAsync(l => l.Origem == origem);
        if (linha == null)
        {
            linha = await NovaLinha(r, controleId);
            if (linha == null) return $"O romaneio {numero} não tem produto que vire caixa de volume para embalar.";
            db.VolumesControleLinhas.Add(linha);
        }
        else if (linha.ControleId != controleId)
        {
            linha.ControleId = controleId;
            linha.Ordem = await ProximaOrdem(controleId);
            linha.AtualizadoEm = DateTime.UtcNow;
        }
        r.Retirado = false;
        r.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Tira o romaneio dos volumes: apaga a entrada e não deixa voltar sozinho.</summary>
    public async Task<bool> RetirarAsync(int numero)
    {
        var r = await db.RomaneiosFazendaLidos.FirstOrDefaultAsync(x => x.Numero == numero);
        if (r == null) return false;
        var origem = ControleVolumesLinha.OrigemRomaneio(numero);
        var linhas = await db.VolumesControleLinhas.Where(l => l.Origem == origem).ToListAsync();
        db.VolumesControleLinhas.RemoveRange(linhas);
        r.Retirado = true;
        r.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// A linha de entrada de origem "romaneio:N" foi apagada na aba Entradas: marca
    /// o romaneio como tirado (o SaveChanges é de quem chamou, junto com a exclusão).
    /// </summary>
    public async Task MarcarRetiradoAsync(string? origem)
    {
        if (NumeroDaOrigem(origem) is not { } numero) return;
        var r = await db.RomaneiosFazendaLidos.FirstOrDefaultAsync(x => x.Numero == numero);
        if (r == null) return;
        r.Retirado = true;
        r.AtualizadoEm = DateTime.UtcNow;
    }

    public static int? NumeroDaOrigem(string? origem) =>
        origem != null && origem.StartsWith(ControleVolumesLinha.PrefixoOrigemRomaneio, StringComparison.Ordinal)
        && int.TryParse(origem.AsSpan(ControleVolumesLinha.PrefixoOrigemRomaneio.Length), out var n) ? n : null;

    // ───────────────────────── Para as telas ─────────────────────────

    /// <summary>
    /// Os romaneios sem funrural com a situação de cada um. Com acertoId, só os
    /// daquele acerto; com numeros, só esses.
    /// </summary>
    public async Task<List<Info>> ListarAsync(int? acertoId = null, IReadOnlyCollection<int>? numeros = null)
    {
        var q = db.RomaneiosFazendaLidos.AsNoTracking().Where(r => !r.DescFunrural);
        if (acertoId != null) q = q.Where(r => r.AcertoId == acertoId);
        if (numeros != null) q = q.Where(r => numeros.Contains(r.Numero));
        var rs = await q.OrderByDescending(r => r.Data).ThenByDescending(r => r.Numero).ToListAsync();
        if (rs.Count == 0) return [];

        var lancados = await OrigensLancadas();
        var controles = await Controles();
        var porId = controles.ToDictionary(c => c.Id);
        var limite = LimiteHistorico(controles);
        var acertoIds = rs.Where(r => r.AcertoId != null).Select(r => r.AcertoId!.Value).Distinct().ToList();
        var acertos = await db.AcertosAlfa.AsNoTracking().Where(a => acertoIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Numero);

        return rs.Select(r =>
        {
            var (itens, avisos) = Itens(r);
            ControleResumo? controle = null;
            int? linhaId = null;
            string situacao;
            if (lancados.TryGetValue(ControleVolumesLinha.OrigemRomaneio(r.Numero), out var l))
            {
                situacao = "lancado";
                controle = porId.GetValueOrDefault(l.ControleId);
                linhaId = l.Id;
            }
            else if (r.Retirado) situacao = "retirado";
            else if (itens.All(i => i.Cru == null)) situacao = "sem_tipo";
            else if (limite is { } lim && r.Data <= lim || Alvo(r.Data, controles)?.Origem == OrigemPlanilha) situacao = "historico";
            else situacao = "aguardando";

            return new Info(r.Numero, r.Data, r.Caixas, itens, avisos, situacao,
                controle?.Id, controle?.Numero, controle?.Data, linhaId,
                r.AcertoId, r.AcertoId is { } aid && acertos.TryGetValue(aid, out var n) ? n : null);
        }).ToList();
    }

    // ───────────────────────── Apoio ─────────────────────────

    private async Task<Dictionary<string, (int Id, int ControleId)>> OrigensLancadas() =>
        (await db.VolumesControleLinhas.AsNoTracking()
            .Where(l => l.Origem != null && l.Origem.StartsWith(ControleVolumesLinha.PrefixoOrigemRomaneio))
            .Select(l => new { l.Origem, l.Id, l.ControleId })
            .ToListAsync())
        .GroupBy(l => l.Origem!)
        .ToDictionary(g => g.Key, g => (g.First().Id, g.First().ControleId));

    private async Task<List<ControleResumo>> Controles() =>
        (await db.VolumesControles.AsNoTracking()
            .Select(c => new { c.Id, c.Numero, c.Data, c.Origem })
            .ToListAsync())
        .Select(c => new ControleResumo(c.Id, c.Numero, c.Data, c.Origem))
        .OrderBy(c => c.Data).ThenBy(c => c.Numero)
        .ToList();

    // Até o último controle importado das planilhas é histórico: não se religa
    private static DateOnly? LimiteHistorico(List<ControleResumo> controles) =>
        controles.Where(c => c.Origem == OrigemPlanilha).Select(c => (DateOnly?)c.Data).Max();

    private static ControleResumo? Alvo(DateOnly data, List<ControleResumo> controles) =>
        controles.FirstOrDefault(c => c.Data >= data && c.Data <= data.AddDays(JanelaDias));

    /// <summary>Os produtos do romaneio com o tipo cru de cada um e o que ficou de fora.</summary>
    private static (List<ItemInfo> Itens, List<string> Avisos) Itens(RomaneioFazendaLido r)
    {
        List<RomaneioFazendaLeitor.Item> itens;
        try { itens = JsonSerializer.Deserialize<List<RomaneioFazendaLeitor.Item>>(r.ItensJson, Json) ?? []; }
        catch (JsonException) { itens = []; }

        var avisos = new List<string>();
        if (itens.Count == 0) avisos.Add($"Não consegui ler os produtos do romaneio {r.Numero}.");
        var lista = new List<ItemInfo>();
        foreach (var i in itens)
        {
            var cru = RomaneioFazendaLeitor.CruDoProduto(i.Produto);
            if (cru == null)
                avisos.Add($"{i.Descricao} ({i.Quantidade:0.##} {i.Unidade}) não é caixa de volume para embalar: ficou de fora.");
            else if (!string.Equals(i.Unidade, "CX", StringComparison.OrdinalIgnoreCase))
            {
                avisos.Add($"{i.Descricao} veio em {i.Unidade}, não em caixa: ficou de fora.");
                cru = null;
            }
            else if (cru.Aviso != null) avisos.Add(cru.Aviso);
            lista.Add(new ItemInfo(i.Produto, i.Descricao, i.Quantidade, cru?.Campo, cru?.Nome,
                cru == null ? 0 : i.Quantidade * RomaneioFazendaLeitor.ProdutosCPorCaixa));
        }
        var somaItens = itens.Sum(i => i.Quantidade);
        if (itens.Count > 0 && somaItens != r.Caixas)
            avisos.Add($"Os produtos somam {somaItens:0.##} caixas, mas o total do romaneio diz {r.Caixas}.");
        return (lista, avisos);
    }

    private async Task<ControleVolumesLinha?> NovaLinha(RomaneioFazendaLido r, int controleId)
    {
        var (itens, _) = Itens(r);
        var linha = new ControleVolumesLinha
        {
            ControleId = controleId,
            Grupo = ControleVolumesLinha.Entrada,
            Nome = Fornecedor,
            Data = r.Data,
            Origem = ControleVolumesLinha.OrigemRomaneio(r.Numero),
            Ordem = await ProximaOrdem(controleId),
        };
        foreach (var i in itens.Where(i => i.Cru != null))
        {
            switch (i.Cru)
            {
                case "Ab": linha.Ab += i.ProdutosC; break;
                case "Av": linha.Av += i.ProdutosC; break;
                case "Bb": linha.Bb += i.ProdutosC; break;
            }
        }
        return ConferenciaVolumes.TemAlgum(linha) ? linha : null;
    }

    private async Task<int> ProximaOrdem(int controleId) =>
        (await db.VolumesControleLinhas.Where(l => l.ControleId == controleId && l.Grupo == ControleVolumesLinha.Entrada)
            .MaxAsync(l => (int?)l.Ordem) ?? 0) + 1;
}
