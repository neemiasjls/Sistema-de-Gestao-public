using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

// ── O que chega da tela ──────────────────────────────────────────
// Volumes: os fretes são da API (tabela de preços no dia, VIA-10). Linha que já
// existia (Id) e continua com o mesmo cliente guarda os valores que tinha; linha
// nova (ou de outro cliente) pega os da tabela. Os Valor* da entrada não mandam.
public record VolumesEntrada(string? Cliente, decimal CaixasVolumes, decimal CaixasProdutoE, decimal CaixasProdutoD, decimal ProdutosB,
    decimal? ValorCaixa = null, decimal? ValorProdutoD = null, decimal? ValorProdutoB = null,
    decimal ProdutosBAmarelas = 0, decimal ProdutosBBrancas = 0, decimal ProdutosC = 0, int? Id = null);
public record AbastEntrada(DateOnly? Data, int? Km, decimal? Litros, decimal Valor, string? Local, string? Observacao, string? Forma);
public record DespesaEntrada(string? Tipo, string? Descricao, decimal Valor, string? Observacao);
public record CargaEntrada(string? Tipo, string? Transportadora, DateOnly? Data, string? Origem, string? Destino, decimal Valor, int? RetornoId);

public record ViagemEntrada(
    int? MotoristaId, string? MotoristaNome, string? Placa, string? PlacaCarreta, string? Destino,
    DateOnly? DataSaida, DateOnly? DataChegada, int? KmSaida, int? KmChegada, decimal? Litros,
    decimal Levou, DateOnly? DataDinheiro, decimal Devolveu, decimal? SobraAnteriorInformada,
    decimal DiariasQuantidade, decimal? PorcentagemComissao, int? MesReferencia, int? AnoReferencia, string? Observacoes,
    List<VolumesEntrada>? Volumes, List<AbastEntrada>? Abastecimentos, List<DespesaEntrada>? Despesas, List<CargaEntrada>? Cargas);

public record RetornoEntrada(
    DateOnly? Data, string? Destino, string? Transportadora, decimal ValorFrete, decimal Pedagio, decimal Adicional,
    decimal DescontoSeguro, decimal? PorcentagemTip, decimal? DescontoManual, string? Motorista, string? Placa,
    string? Observacoes, string? FormaPagamento, int? ViagemId, decimal PorcentagemIcms, bool ForaDoControle, bool? Conferido,
    // VIA-27: a descarga é o Adicional (não há campo à parte). ContratoManual com número = escolhido à mão;
    // sem número = solto (não liga sozinho); false = automático. O adiantamento nunca vem da tela.
    string? Contrato = null, bool ContratoManual = false);

public class ErroViagem(string mensagem) : Exception(mensagem);

/// <summary>Viagem nova que já existe (mesma placa e saída): a tela oferece abrir a existente.</summary>
public class ErroViagemExistente(string mensagem, int viagemId) : ErroViagem(mensagem)
{
    public int ViagemId { get; } = viagemId;
}

/// <summary>
/// Viagens dos caminhões: guardar, calcular (ViagemCaminhaoCalculo), casar com o
/// controle de carregamento e com os retornos, importar as planilhas antigas.
/// </summary>
public class ViagensCaminhaoService(AppDbContext db, PrecosClienteService precos, EstoqueVendasService vendas)
{
    public const decimal ValorDiariaPadrao = 100m;

    private IQueryable<ViagemCaminhao> ComTudo() => db.ViagensCaminhao
        .Include(v => v.Volumes).Include(v => v.Abastecimentos).Include(v => v.Despesas).Include(v => v.Cargas)
        .AsSplitQuery();

    // ───────────────────────── Leitura ─────────────────────────

    /// <summary>
    /// De quem é a cadeia da sobra e o acerto (VIA-21, Admin, 02/11/2026): do MOTORISTA, com
    /// qualquer placa que ele usar; viagem sem motorista ligado fica na cadeia da placa.
    /// </summary>
    public static string Cadeia(ViagemCaminhao v) => v.MotoristaId is { } m ? $"M{m}" : $"P{v.Placa}";

    /// <summary>Sobra anterior das viagens pedidas (cadeia inteira do motorista de cada uma).</summary>
    public async Task<Dictionary<int, decimal>> SobrasAsync(IEnumerable<ViagemCaminhao> pedidas)
    {
        var ped = pedidas.ToList();
        var mots = ped.Where(v => v.MotoristaId != null).Select(v => v.MotoristaId!.Value).Distinct().ToList();
        var placas = ped.Where(v => v.MotoristaId == null).Select(v => v.Placa).Distinct().ToList();
        var viagens = await db.ViagensCaminhao
            .Where(v => (v.MotoristaId != null && mots.Contains(v.MotoristaId.Value)) || (v.MotoristaId == null && placas.Contains(v.Placa)))
            .Include(v => v.Abastecimentos).Include(v => v.Despesas).AsSplitQuery().AsNoTracking().ToListAsync();
        var mapa = new Dictionary<int, decimal>();
        foreach (var g in viagens.GroupBy(Cadeia))
            foreach (var kv in SobrasAnteriores(g)) mapa[kv.Key] = kv.Value;
        return mapa;
    }

    public record SobraAnteriorPainel(bool TemAnterior, decimal Sobra, DateOnly? DataViagemAnterior);

    /// <summary>
    /// Sobra que a viagem que o motorista está preenchendo recebe da anterior DELE (VIA-07,
    /// VIA-21: por motorista, com qualquer placa), igual à conta do escritório: a folha entra na cadeia como uma
    /// viagem a mais (mês da saída, data de saída) e a sobra é a da viagem logo antes
    /// dela, em qualquer situação. Ao corrigir um envio, a própria viagem sai da cadeia
    /// e fica no mês de acerto que já tem. Só devolve sobra e data, nada de frete.
    /// </summary>
    public async Task<SobraAnteriorPainel> SobraAnteriorPainelAsync(int motoristaId, string? placaInformada, DateOnly? dataSaida, int? viagemPropriaId)
    {
        var placa = NormalizarPlaca(placaInformada);
        var propria = viagemPropriaId is { } pid ? await db.ViagensCaminhao.AsNoTracking().FirstOrDefaultAsync(v => v.Id == pid) : null;
        var excluir = propria?.Id ?? 0;
        var viagens = await db.ViagensCaminhao.AsNoTracking().Where(v => v.MotoristaId == motoristaId && v.Id != excluir)
            .Include(v => v.Abastecimentos).Include(v => v.Despesas).AsSplitQuery().ToListAsync();
        if (viagens.Count == 0) return new(false, 0, null);

        var dia = dataSaida ?? propria?.DataSaida;
        var refDia = dia ?? DateOnly.FromDateTime(DateTime.Today);
        var folha = new ViagemCaminhao
        {
            Id = propria?.Id ?? int.MaxValue, Placa = placa, MotoristaId = motoristaId, DataSaida = dia,
            AnoReferencia = propria?.AnoReferencia ?? refDia.Year, MesReferencia = propria?.MesReferencia ?? refDia.Month,
        };
        var ordem = EmOrdem(viagens.Append(folha)).ToList();
        var pos = ordem.IndexOf(folha);
        if (pos <= 0) return new(false, 0, null);
        var antes = ordem.Take(pos).ToList();
        var mapa = SobrasAnteriores(antes);
        var anterior = antes[^1];
        return new(true, Math.Round(Sobra(anterior, mapa[anterior.Id]), 2), anterior.DataSaida);
    }

    public async Task<object> ListarAsync(int ano, int mes, string? placa, int? motoristaId)
    {
        var q = ComTudo().AsNoTracking().Where(v => v.AnoReferencia == ano && v.MesReferencia == mes);
        if (!string.IsNullOrWhiteSpace(placa)) { var p = NormalizarPlaca(placa); q = q.Where(v => v.Placa == p); }
        if (motoristaId != null) q = q.Where(v => v.MotoristaId == motoristaId);
        var viagens = await q.ToListAsync();
        var ids = viagens.Select(v => v.Id).ToList();
        var rets = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null && ids.Contains(r.ViagemId.Value)).ToListAsync();
        var sobras = await SobrasAsync(viagens);
        var nomes = await db.MotoristasFrota.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nome);
        string NomeDe(ViagemCaminhao v) => v.MotoristaId is { } mid && nomes.TryGetValue(mid, out var n) ? n : (v.MotoristaNome ?? "");

        // Um acerto por motorista (VIA-21); VIAGEMn conta dentro do motorista no mês
        var itens = viagens.GroupBy(Cadeia).OrderBy(g => NomeDe(g.First())).ThenBy(g => g.Key).SelectMany(g => EmOrdem(g).Select((v, i) =>
        {
            var rv = rets.Where(r => r.ViagemId == v.Id).ToList();
            var c = Calcular(v, rv, sobras.GetValueOrDefault(v.Id));
            return new
            {
                v.Id, v.Placa, v.PlacaCarreta, Numero = i + 1,
                Motorista = NomeDe(v), Grupo = Cadeia(v),
                v.MotoristaId, v.Destino, v.DataSaida, v.DataChegada, v.Status, v.Origem, v.EnviadoEm,
                c.TotalVolumes, c.TotalRetornos, c.ValorTotal, c.TotalDespesas, c.Comissao, c.Sobra, Resultado = c.Resultado_,
                SemVolumes = v.Volumes.Count == 0,
                RetornosAConferir = rv.Count(r => !r.Conferido),
                RetornosDeclarados = v.Cargas.Count(x => x.Tipo == "RETORNO"),
            };
        })).ToList();

        // Placas que têm viagem em algum mês (filtro da tela)
        var placas = await db.ViagensCaminhao.Select(v => v.Placa).Distinct().OrderBy(p => p).ToListAsync();
        var aConferir = await db.ViagensCaminhao.CountAsync(v => v.Status == "lancada" && v.Origem == "motorista");
        return new { ano, mes, viagens = itens, placas, aConferir };
    }

    public async Task<object?> DetalheAsync(int id)
    {
        var v = await ComTudo().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (v == null) return null;
        var rets = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId == id).OrderBy(r => r.Data).ThenBy(r => r.Id).ToListAsync();
        var sobras = await SobrasAsync([v]);
        var c = Calcular(v, rets, sobras.GetValueOrDefault(id));

        // Posição na cadeia do mês (VIAGEMn, por motorista) e vizinhas
        var doMes = (await db.ViagensCaminhao.AsNoTracking()
            .Where(x => x.AnoReferencia == v.AnoReferencia && x.MesReferencia == v.MesReferencia
                && (v.MotoristaId != null ? x.MotoristaId == v.MotoristaId : x.MotoristaId == null && x.Placa == v.Placa)).ToListAsync());
        var ordem = EmOrdem(doMes).Select(x => x.Id).ToList();

        var sugeridos = await RetornosSugeridosAsync(v);
        var carregamentos = await CarregamentosCandidatosAsync(v);
        var motorista = v.MotoristaId is { } mid ? await db.MotoristasFrota.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid) : null;
        var idsLigados = v.Cargas.Where(x => x.RetornoId != null).Select(x => x.RetornoId!.Value).ToList();
        var ligadosDaViagem = await db.RetornosFrete.AsNoTracking().Where(r => idsLigados.Contains(r.Id)).ToDictionaryAsync(r => r.Id);

        return new
        {
            v.Id, v.MotoristaId, Motorista = motorista?.Nome ?? v.MotoristaNome, v.MotoristaNome, v.Placa, v.PlacaCarreta, v.Destino,
            v.DataSaida, v.DataChegada, v.KmSaida, v.KmChegada, v.Litros, v.Levou, v.DataDinheiro, v.Devolveu,
            v.SobraAnteriorInformada, v.DiariasQuantidade, v.ValorDiaria, v.PorcentagemComissao, v.MesReferencia, v.AnoReferencia,
            v.Status, v.Origem, v.Observacoes, v.EnviadoEm, v.ConferidoEm, v.CriadoEm, v.AtualizadoEm,
            Numero = ordem.IndexOf(v.Id) + 1,
            AnteriorId = ordem.IndexOf(v.Id) > 0 ? ordem[ordem.IndexOf(v.Id) - 1] : (int?)null,
            ProximaId = ordem.IndexOf(v.Id) < ordem.Count - 1 ? ordem[ordem.IndexOf(v.Id) + 1] : (int?)null,
            Volumes = v.Volumes.OrderBy(o => o.Ordem).Select(o => new
            {
                o.Id, o.Cliente, o.CaixasVolumes, o.CaixasProdutoE, o.CaixasProdutoD, o.ProdutosB,
                o.ProdutosBAmarelas, o.ProdutosBBrancas, o.ProdutosC,
                // ProdutosB de viagem antiga, sem dizer se amarela ou branca
                ProdutosBSemCor = Math.Max(0, o.ProdutosB - o.ProdutosBAmarelas - o.ProdutosBBrancas),
                o.ValorCaixa, ValorProdutoE = ValorProdutoE(o), o.ValorProdutoD, o.ValorProdutoB, o.ValorProdutoC, Total = TotalVolumes(o),
            }),
            Abastecimentos = v.Abastecimentos.OrderBy(a => a.Ordem).Select(a => new { a.Id, a.Data, a.Km, a.Litros, a.Valor, a.Local, a.Observacao, a.Forma }),
            Despesas = v.Despesas.OrderBy(d => d.Ordem).Select(d => new { d.Id, d.Tipo, d.Descricao, d.Valor, d.Observacao, Nome = NomeTipo(d.Tipo) }),
            Cargas = v.Cargas.OrderBy(x => x.Ordem).Select(x => new
            {
                x.Id, x.Tipo, x.Transportadora, x.Data, x.Origem, x.Destino, x.Valor, x.RetornoId, x.Ligacao, x.LigacaoAviso,
                // Retorno importado ligado à anotação (VIA-22)
                RetornoLigado = x.RetornoId is { } rid && ligadosDaViagem.TryGetValue(rid, out var rl)
                    ? new { rl.Id, rl.Data, rl.Transportadora, rl.FreteTotal, rl.ViagemId } : null,
            }),
            Retornos = rets.Select(RetornoDto),
            RetornosSugeridos = sugeridos.Select(RetornoDto),
            Carregamentos = carregamentos,
            Calculo = c,
        };
    }

    // ───────────────────────── Gravar ─────────────────────────

    /// <summary>
    /// Cria ou substitui a viagem inteira (linhas de volumes, abastecimentos, despesas
    /// e cargas). origem: escritorio ou motorista (o motorista não mexe em volumes,
    /// devolução, sobra informada, % nem mês do acerto).
    /// </summary>
    public async Task<ViagemCaminhao> SalvarAsync(int? id, ViagemEntrada e, string origem, int? usuarioId, MotoristaFrota? motoristaFixo = null)
    {
        var doMotorista = origem == "motorista";
        // Ordem inversa (plano importacoes-mesclar, Parte 3): viagem nova da mesma placa e mesma saída de uma
        // que já existe (importada do acerto ou lançada antes) nunca vira segunda viagem.
        if (id == null && await ViagemMesmaSaidaAsync(e) is { } existente)
        {
            if (!doMotorista)
                throw new ErroViagemExistente(
                    $"Já existe viagem desta placa com saída em {existente.DataSaida:dd/MM} (situação {NomeSituacao(existente.Status)}): abra aquela.",
                    existente.Id);
            var junta = await JuntarEnvioDoMotoristaAsync(existente, e, origem, usuarioId, motoristaFixo);
            await LigarRetornosDaViagemAsync(junta);
            return junta;
        }
        ViagemCaminhao v;
        if (id is { } vid)
        {
            v = await ComTudo().FirstOrDefaultAsync(x => x.Id == vid) ?? throw new ErroViagem("Viagem não encontrada.");
            if (v.Status == "fechada") throw new ErroViagem("Viagem fechada: reabra antes de mudar.");
        }
        else
        {
            v = new ViagemCaminhao { Origem = origem, CriadoPorUsuarioId = usuarioId, ValorDiaria = ValorDiariaPadrao };
            db.ViagensCaminhao.Add(v);
        }

        var placa = NormalizarPlaca(e.Placa);
        if (placa.Length == 0) throw new ErroViagem("Informe a placa do caminhão.");
        if (e.DataSaida == null) throw new ErroViagem("Informe a data de saída.");
        if (e.DataChegada != null && e.DataChegada < e.DataSaida) throw new ErroViagem("A chegada está antes da saída.");
        if (e.DiariasQuantidade < 0 || e.DiariasQuantidade * 2 != Math.Floor(e.DiariasQuantidade * 2))
            throw new ErroViagem("Diárias em passos de meia (0,5).");
        if (e.Levou < 0) throw new ErroViagem("Dinheiro levado não pode ser negativo.");
        // Painel do motorista (VIA-04): a data em que pegou o dinheiro só é obrigatória quando levou
        // algum. Só na folha do motorista: as viagens importadas têm "levou" sem data.
        if (doMotorista && e.Levou > 0 && e.DataDinheiro == null) throw new ErroViagem("Informe a data em que pegou o dinheiro.");
        // Painel do motorista (VIA-20): sem placa padrão; cavalo e carreta, as duas, têm de ser placas ativas do
        // cadastro. Ao corrigir, a placa que o envio já tinha continua valendo (pode ter sido desativada depois).
        if (doMotorista)
        {
            var carreta = NormalizarPlaca(e.PlacaCarreta);
            if (carreta.Length == 0) throw new ErroViagem("Escolha a carreta.");
            if (!(id != null && v.Placa == placa) && !await db.PlacasFrota.AnyAsync(p => p.Placa == placa && p.Tipo == "Cavalo" && p.Ativo))
                throw new ErroViagem($"A placa {placa} não está no cadastro de cavalos. Escolha uma da lista.");
            if (!(id != null && v.PlacaCarreta == carreta) && !await db.PlacasFrota.AnyAsync(p => p.Placa == carreta && p.Tipo == "Carreta" && p.Ativo))
                throw new ErroViagem($"A placa {carreta} não está no cadastro de carretas. Escolha uma da lista.");
        }

        MotoristaFrota? mot = motoristaFixo;
        if (mot == null && e.MotoristaId is { } mid)
            mot = await db.MotoristasFrota.FindAsync(mid) ?? throw new ErroViagem("Motorista não encontrado.");

        v.MotoristaId = mot?.Id;
        v.MotoristaNome = mot?.Nome ?? (e.MotoristaNome ?? "").Trim();
        if (v.MotoristaNome.Length == 0) throw new ErroViagem("Informe o motorista.");
        v.Placa = placa;
        v.PlacaCarreta = NormalizarPlaca(e.PlacaCarreta) is { Length: > 0 } pc ? pc : null;
        v.Destino = Limpo(e.Destino);
        v.DataSaida = e.DataSaida;
        v.DataChegada = e.DataChegada;
        v.KmSaida = e.KmSaida is > 0 ? e.KmSaida : null;
        v.KmChegada = e.KmChegada is > 0 ? e.KmChegada : null;
        v.Litros = e.Litros is > 0 ? e.Litros : null;
        v.Levou = e.Levou;
        v.DataDinheiro = e.DataDinheiro;
        v.DiariasQuantidade = e.DiariasQuantidade;
        v.Observacoes = Limpo(e.Observacoes);
        if (!doMotorista)
        {
            // Devolveu não muda pela tela (VIA-14): a sobra fica com o motorista. Viagem nova
            // nasce com 0; a importada da planilha guarda o que veio de lá.
            v.SobraAnteriorInformada = e.SobraAnteriorInformada;
            v.PorcentagemComissao = e.PorcentagemComissao ?? mot?.PorcentagemComissao ?? v.PorcentagemComissao;
            v.MesReferencia = e.MesReferencia is >= 1 and <= 12 ? e.MesReferencia.Value : e.DataSaida.Value.Month;
            v.AnoReferencia = e.AnoReferencia is >= 2000 ? e.AnoReferencia.Value : e.DataSaida.Value.Year;
        }
        else
        {
            if (id == null)
            {
                v.PorcentagemComissao = mot?.PorcentagemComissao ?? 6;
                v.MesReferencia = e.DataSaida.Value.Month;
                v.AnoReferencia = e.DataSaida.Value.Year;
                v.EnviadoEm = DateTime.UtcNow;
            }
            v.Status = "lancada";
        }
        if (v.PorcentagemComissao < 0 || v.PorcentagemComissao > 100) throw new ErroViagem("% de comissão inválida.");

        // Linhas: troca todas pelas da tela
        if (!doMotorista && e.Volumes != null)
        {
            var tabela = await precos.TabelaAsync();
            var antigos = v.Volumes.ToDictionary(o => o.Id);
            db.ViagemCaminhaoVolumes.RemoveRange(v.Volumes);
            v.Volumes = e.Volumes.Where(o => !string.IsNullOrWhiteSpace(o.Cliente)).Select((o, i) =>
            {
                if (o.CaixasVolumes < 0 || o.CaixasProdutoE < 0 || o.CaixasProdutoD < 0 || o.ProdutosB < 0
                    || o.ProdutosBAmarelas < 0 || o.ProdutosBBrancas < 0 || o.ProdutosC < 0)
                    throw new ErroViagem($"{o.Cliente!.Trim()}: quantidade negativa.");
                // A viagem guarda o valor do dia (VIA-10): linha que já existia, do mesmo cliente, fica como estava
                var velho = o.Id is { } oid && antigos.TryGetValue(oid, out var x) && Normalizar(x.Cliente) == Normalizar(o.Cliente) ? x : null;
                var f = velho == null ? tabela.Fretes(o.Cliente!) : null;
                var cor = o.ProdutosBAmarelas + o.ProdutosBBrancas;
                return new ViagemCaminhaoVolumes
                {
                    Ordem = i, Cliente = o.Cliente!.Trim(), CaixasVolumes = o.CaixasVolumes, CaixasProdutoE = o.CaixasProdutoE,
                    CaixasProdutoD = o.CaixasProdutoD, ProdutosBAmarelas = o.ProdutosBAmarelas, ProdutosBBrancas = o.ProdutosBBrancas,
                    // Sem cor (linha antiga): fica o total que já estava
                    ProdutosB = cor > 0 ? cor : o.ProdutosB, ProdutosC = o.ProdutosC,
                    ValorCaixa = velho?.ValorCaixa ?? f!.Caixa, ValorProdutoE = velho != null ? velho.ValorProdutoE : f!.ProdutoE,
                    ValorProdutoD = velho?.ValorProdutoD ?? f!.ProdutoD, ValorProdutoB = velho?.ValorProdutoB ?? f!.ProdutoB,
                    ValorProdutoC = velho?.ValorProdutoC ?? f!.ProdutoC,
                };
            }).ToList();
        }
        db.ViagemCaminhaoAbastecimentos.RemoveRange(v.Abastecimentos);
        v.Abastecimentos = (e.Abastecimentos ?? []).Where(a => a.Valor != 0 || a.Litros > 0).Select((a, i) => new ViagemCaminhaoAbastecimento
        {
            Ordem = i, Data = a.Data, Km = a.Km is > 0 ? a.Km : null, Litros = a.Litros is > 0 ? a.Litros : null, Valor = a.Valor,
            Local = Limpo(a.Local), Observacao = Limpo(a.Observacao), Forma = a.Forma == "PAGO" ? "PAGO" : "NOTA",
        }).ToList();
        db.ViagemCaminhaoDespesas.RemoveRange(v.Despesas);
        v.Despesas = (e.Despesas ?? []).Where(d => d.Valor != 0).Select((d, i) =>
        {
            var tipo = TipoValido(d.Tipo) ? d.Tipo! : Outra;
            if (tipo == Outra && string.IsNullOrWhiteSpace(d.Descricao)) throw new ErroViagem("Diga o que é a outra despesa.");
            // Caixinha (Admin, 06/10/2026): cada uma diz de quem é (cliente ou lugar), na Descricao.
            // Obrigatório na folha do motorista; as importadas das planilhas antigas vêm sem nome.
            if (tipo == Caixinha && doMotorista && string.IsNullOrWhiteSpace(d.Descricao)) throw new ErroViagem("Diga de quem é cada caixinha.");
            if (tipo != Outra && tipo != Caixinha) d = d with { Descricao = null };
            return new ViagemCaminhaoDespesa { Ordem = i, Tipo = tipo, Descricao = Limpo(d.Descricao), Valor = d.Valor, Observacao = Limpo(d.Observacao) };
        }).ToList();

        await SincronizarCargasAsync(v, e.Cargas ?? []);
        await db.SaveChangesAsync();
        // Anotação "outra carga" sem retorno ligado: o sistema procura o importado (VIA-22)
        await LigarAnotacoesAsync(v.Id);
        // VIA-25: retornos livres da placa no período da viagem
        await LigarRetornosDaViagemAsync(v);

        // Viagem sem volumes ainda: tenta o controle de carregamento da placa e do dia
        if (v.Volumes.Count == 0) await CasarCarregamentoAsync(v);
        // Viagem que já tem volumes e não tem controle: só liga o controle solto (VIA-25)
        else if (v.DataSaida is { } saidaV) await LigarCarregamentoSemTrocarAsync(v, $"viagem de {saidaV:dd/MM}", []);
        return v;
    }

    /// <summary>
    /// Cargas da folha. A "outra carga" (RETORNO) é só a anotação do motorista (VIA-22,
    /// Admin, 09/11/2026): NÃO cria retorno; o retorno nasce da planilha importada e o
    /// sistema liga sozinho (LigarAnotacoesAsync, depois de gravar). Anotação que voltou igual
    /// (data, valor, transportadora, origem e destino) guarda a ligação que tinha; a que mudou
    /// é ligada de novo. O RetornoId que vem da tela não manda (o motorista não escolhe).
    /// </summary>
    private async Task SincronizarCargasAsync(ViagemCaminhao v, List<CargaEntrada> cargas)
    {
        var antigas = v.Cargas.ToList();
        var sobrando = antigas.Where(a => a.Tipo == "RETORNO").ToList();
        var retIds = sobrando.Where(a => a.RetornoId != null).Select(a => a.RetornoId!.Value).ToList();
        var retornos = await db.RetornosFrete.AsNoTracking().Where(r => retIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var placasViagem = new[] { v.Placa, v.PlacaCarreta }.Select(RetornoAnotacaoLigador.ChavePlaca).Where(p => p.Length > 0).ToHashSet();

        var novas = new List<ViagemCaminhaoCarga>();
        foreach (var (c, i) in cargas.Select((c, i) => (c, i)))
        {
            var tipo = c.Tipo == "RETORNO" ? "RETORNO" : "VOLUMES";
            var nova = new ViagemCaminhaoCarga
            {
                Ordem = i, Tipo = tipo, Transportadora = Limpo(c.Transportadora), Data = c.Data,
                Origem = Limpo(c.Origem), Destino = Limpo(c.Destino), Valor = c.Valor,
            };
            if (tipo == "RETORNO")
            {
                if (c.Valor <= 0) throw new ErroViagem("Informe o valor do frete da outra carga.");
                var igual = sobrando.FirstOrDefault(a => MesmaAnotacao(a, nova));
                if (igual != null)
                {
                    sobrando.Remove(igual);
                    // A ligação automática só continua se o retorno ainda é da placa da viagem (a placa pode ter mudado)
                    var vale = igual.RetornoId is not { } rid
                        || igual.Ligacao == RetornoAnotacaoLigador.Manual
                        || (retornos.TryGetValue(rid, out var r) && placasViagem.Contains(RetornoAnotacaoLigador.ChavePlaca(r.Placa)));
                    if (vale)
                    {
                        nova.RetornoId = igual.RetornoId;
                        nova.Ligacao = igual.Ligacao;
                        nova.LigacaoAviso = igual.LigacaoAviso;
                    }
                }
            }
            novas.Add(nova);
        }

        db.ViagemCaminhaoCargas.RemoveRange(antigas);
        v.Cargas = novas;
    }

    /// <summary>
    /// A viagem que já existe com a mesma placa e a mesma saída (graus chave/data da Regra de junção;
    /// nunca "provável"). Vale também a importada do acerto (com chave): é a ordem inversa.
    /// </summary>
    private async Task<ViagemCaminhao?> ViagemMesmaSaidaAsync(ViagemEntrada e)
    {
        var placa = NormalizarPlaca(e.Placa);
        if (placa.Length == 0 || e.DataSaida == null) return null;
        return await db.ViagensCaminhao.Where(x => x.Placa == placa && x.DataSaida == e.DataSaida)
            .OrderBy(x => x.Id).FirstOrDefaultAsync();
    }

    private static string NomeSituacao(string? s) => s switch { "conferida" => "conferida", "fechada" => "fechada", _ => "lançada" };

    /// <summary>
    /// Envio do motorista de uma viagem que já existe (suposição S2 do plano): lançada = o envio a corrige
    /// (mesmo caminho do PUT); conferida/fechada = só entram as cargas anotadas que ainda não existem e a
    /// observação; o resto não muda.
    /// </summary>
    private async Task<ViagemCaminhao> JuntarEnvioDoMotoristaAsync(ViagemCaminhao existente, ViagemEntrada e, string origem, int? usuarioId, MotoristaFrota? motoristaFixo)
    {
        if (existente.Status == "lancada")
        {
            var corrigida = await SalvarAsync(existente.Id, e, origem, usuarioId, motoristaFixo);
            if (corrigida.EnviadoEm == null) { corrigida.EnviadoEm = DateTime.UtcNow; await db.SaveChangesAsync(); }
            return corrigida;
        }

        var v = await ComTudo().FirstAsync(x => x.Id == existente.Id);
        var ordem = v.Cargas.Count == 0 ? 0 : v.Cargas.Max(c => c.Ordem) + 1;
        foreach (var c in e.Cargas ?? [])
        {
            var tipo = c.Tipo == "RETORNO" ? "RETORNO" : "VOLUMES";
            var nova = new ViagemCaminhaoCarga
            {
                Ordem = ordem, Tipo = tipo, Transportadora = Limpo(c.Transportadora), Data = c.Data,
                Origem = Limpo(c.Origem), Destino = Limpo(c.Destino), Valor = c.Valor,
            };
            if (v.Cargas.Any(a => a.Tipo == tipo && MesmaAnotacao(a, nova))) continue;
            v.Cargas.Add(nova);
            ordem++;
        }
        var obs = Limpo(e.Observacoes);
        if (obs != null && !(v.Observacoes ?? "").Contains(obs))
            v.Observacoes = v.Observacoes == null ? $"Motorista: {obs}" : $"{v.Observacoes}\nMotorista: {obs}";
        v.EnviadoEm ??= DateTime.UtcNow;
        await db.SaveChangesAsync();
        await LigarAnotacoesAsync(v.Id);
        return v;
    }

    private static bool MesmaAnotacao(ViagemCaminhaoCarga a, ViagemCaminhaoCarga b) =>
        a.Data == b.Data && a.Valor == b.Valor && Normalizar(a.Transportadora) == Normalizar(b.Transportadora)
        && Normalizar(a.Origem) == Normalizar(b.Origem) && Normalizar(a.Destino) == Normalizar(b.Destino);

    // ───────────────────────── Anotação do motorista × retorno importado (VIA-22) ─────────────────────────

    /// <summary>
    /// Liga as anotações "outra carga" ainda sem retorno aos retornos importados (RetornoAnotacaoLigador).
    /// Roda ao importar a planilha (anotações das datas do arquivo) e ao gravar a viagem (as dela).
    /// Não mexe em anotação já ligada (reimportar não desfaz) nem na que o escritório soltou.
    /// Ao ligar, o retorno sem viagem passa a ser desta viagem. Devolve quantas ligou.
    /// </summary>
    public async Task<int> LigarAnotacoesAsync(int? viagemId = null, DateOnly? ini = null, DateOnly? fim = null)
    {
        var q = db.ViagemCaminhaoCargas.Include(c => c.Viagem)
            .Where(c => c.Tipo == "RETORNO" && c.RetornoId == null && (c.Ligacao == null || c.Ligacao != RetornoAnotacaoLigador.Solto));
        if (viagemId is { } vid) q = q.Where(c => c.ViagemId == vid);
        var cargas = (await q.ToListAsync())
            .Where(c => DataDaAnotacao(c) is not { } d || ((ini == null || d >= ini) && (fim == null || d <= fim))).ToList();
        if (cargas.Count == 0) return 0;

        var datas = cargas.Select(DataDaAnotacao).Where(d => d != null).Select(d => d!.Value).ToList();
        var retornos = new List<RetornoFrete>();
        if (datas.Count > 0)
        {
            var de = datas.Min().AddDays(-1);
            var ate = datas.Max().AddDays(1);
            var jaLigados = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId != null).Select(c => c.RetornoId!.Value).ToListAsync();
            // Retorno de verdade é o importado (ou lançado na tela Retornos); o antigo "do motorista" não entra
            retornos = (await db.RetornosFrete.Where(r => r.Data >= de && r.Data <= ate && r.Origem != "motorista").ToListAsync())
                .Where(r => !jaLigados.Contains(r.Id)).ToList();
        }

        var resultados = RetornoAnotacaoLigador.Ligar(
            cargas.Select(c => new RetornoAnotacaoLigador.Anotacao(c.Id, c.ViagemId, [c.Viagem?.Placa, c.Viagem?.PlacaCarreta],
                DataDaAnotacao(c), c.Valor, c.Destino)),
            retornos.Select(r => new RetornoAnotacaoLigador.Candidato(r.Id, r.Placa, r.Data, r.ValorFrete, r.FreteTotal, r.Destino, r.ViagemId)));

        var porId = cargas.ToDictionary(c => c.Id);
        var retPorId = retornos.ToDictionary(r => r.Id);
        var ligados = 0;
        foreach (var res in resultados)
        {
            var c = porId[res.AnotacaoId];
            c.RetornoId = res.RetornoId;
            c.Ligacao = res.Situacao;
            c.LigacaoAviso = res.Aviso;
            if (res.RetornoId is { } rid && retPorId.TryGetValue(rid, out var r))
            {
                if (r.ViagemId == null) { r.ViagemId = c.ViagemId; r.AtualizadoEm = DateTime.UtcNow; }
                r.MotoristaId ??= c.Viagem?.MotoristaId;
                ligados++;
            }
        }
        await db.SaveChangesAsync();
        return ligados;
    }

    // Anotação sem data: vale a data de saída da viagem
    private static DateOnly? DataDaAnotacao(ViagemCaminhaoCarga c) => c.Data ?? c.Viagem?.DataSaida;

    /// <summary>
    /// O escritório resolve à mão a anotação que ficou para conferir ou sem par: liga ao retorno
    /// (que não pode estar em outra anotação nem em outra viagem) ou solta (retornoId nulo).
    /// </summary>
    public async Task LigarAnotacaoManualAsync(int cargaId, int? retornoId)
    {
        var c = await db.ViagemCaminhaoCargas.Include(x => x.Viagem).FirstOrDefaultAsync(x => x.Id == cargaId && x.Tipo == "RETORNO")
            ?? throw new ErroViagem("Anotação do motorista não encontrada.");
        if (c.Viagem?.Status == "fechada") throw new ErroViagem("Viagem fechada: reabra antes de mudar.");
        if (retornoId is not { } rid)
        {
            c.RetornoId = null;
            c.Ligacao = RetornoAnotacaoLigador.Solto;
            c.LigacaoAviso = "Retorno solto desta anotação no escritório.";
            await db.SaveChangesAsync();
            return;
        }
        var r = await db.RetornosFrete.FindAsync(rid) ?? throw new ErroViagem("Retorno não encontrado.");
        if (await db.ViagemCaminhaoCargas.AnyAsync(x => x.RetornoId == rid && x.Id != cargaId))
            throw new ErroViagem("Este retorno já está ligado a outra anotação do motorista.");
        if (r.ViagemId != null && r.ViagemId != c.ViagemId)
            throw new ErroViagem("Este retorno está em outra viagem. Solte-o de lá antes.");
        r.ViagemId = c.ViagemId;
        r.MotoristaId ??= c.Viagem?.MotoristaId;
        r.AtualizadoEm = DateTime.UtcNow;
        c.RetornoId = rid;
        c.Ligacao = RetornoAnotacaoLigador.Manual;
        c.LigacaoAviso = null;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Anotações dos motoristas com data no mês que não estão ligadas (para conferir, sem par
    /// ou soltas), com os retornos da mesma placa perto da data que ainda estão livres.
    /// </summary>
    public async Task<List<object>> AnotacoesPendentesAsync(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1).AddDays(-1);
        var cargas = (await db.ViagemCaminhaoCargas.AsNoTracking().Include(c => c.Viagem)
                .Where(c => c.Tipo == "RETORNO" && c.RetornoId == null).ToListAsync())
            .Where(c => DataDaAnotacao(c) is { } d && d >= ini && d <= fim)
            .OrderBy(DataDaAnotacao).ThenBy(c => c.Id).ToList();
        if (cargas.Count == 0) return [];
        var jaLigados = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId != null).Select(c => c.RetornoId!.Value).ToListAsync();
        var perto = (await db.RetornosFrete.AsNoTracking().Where(r => r.Data >= ini.AddDays(-3) && r.Data <= fim.AddDays(3) && r.Origem != "motorista").ToListAsync())
            .Where(r => !jaLigados.Contains(r.Id)).ToList();
        var nomes = await db.MotoristasFrota.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nome);
        return cargas.Select(c =>
        {
            var v = c.Viagem!;
            var placas = new[] { v.Placa, v.PlacaCarreta }.Select(RetornoAnotacaoLigador.ChavePlaca).Where(p => p.Length > 0).ToHashSet();
            var d = DataDaAnotacao(c)!.Value;
            var candidatos = perto.Where(r => placas.Contains(RetornoAnotacaoLigador.ChavePlaca(r.Placa))
                    && Math.Abs(r.Data.DayNumber - d.DayNumber) <= 3 && (r.ViagemId == null || r.ViagemId == c.ViagemId))
                .OrderBy(r => Math.Abs(r.Data.DayNumber - d.DayNumber)).ThenBy(r => r.Id).Select(RetornoDto).ToList();
            return (object)new
            {
                c.Id, c.ViagemId, c.Transportadora, c.Data, c.Origem, c.Destino, c.Valor,
                Situacao = c.Ligacao ?? RetornoAnotacaoLigador.SemPar, Aviso = c.LigacaoAviso,
                v.Placa, v.PlacaCarreta, ViagemSaida = v.DataSaida, ViagemStatus = v.Status,
                Motorista = v.MotoristaId is { } mid && nomes.TryGetValue(mid, out var n) ? n : v.MotoristaNome,
                Candidatos = candidatos,
            };
        }).ToList();
    }

    public async Task MudarStatusAsync(int id, string status)
    {
        if (status is not ("lancada" or "conferida" or "fechada")) throw new ErroViagem("Situação inválida.");
        var v = await db.ViagensCaminhao.FindAsync(id) ?? throw new ErroViagem("Viagem não encontrada.");
        v.Status = status;
        if (status == "conferida" && v.ConferidoEm == null) v.ConferidoEm = DateTime.UtcNow;
        if (status != "lancada")
        {
            // Conferir a viagem confere junto os retornos que vieram da folha do motorista
            var rets = await db.RetornosFrete.Where(r => r.ViagemId == id && !r.Conferido).ToListAsync();
            foreach (var r in rets) r.Conferido = true;
        }
        await db.SaveChangesAsync();
    }

    public async Task ExcluirAsync(int id)
    {
        var v = await db.ViagensCaminhao.FindAsync(id) ?? throw new ErroViagem("Viagem não encontrada.");
        // Retornos da folha do motorista ainda não conferidos vão junto; os outros só se soltam
        var rets = await db.RetornosFrete.Where(r => r.ViagemId == id).ToListAsync();
        foreach (var r in rets)
        {
            if (r.Origem == "motorista" && !r.Conferido) db.RetornosFrete.Remove(r);
            else if (r.ForaDoControle && r.Origem == "importacao") db.RetornosFrete.Remove(r);
            else r.ViagemId = null;
        }
        var carreg = await db.CarregamentosVolumes.Where(c => c.ViagemId == id).ToListAsync();
        foreach (var c in carreg) c.ViagemId = null;
        db.ViagensCaminhao.Remove(v);
        await db.SaveChangesAsync();
    }

    // ───────────────────────── Retornos ─────────────────────────

    public static object RetornoDto(RetornoFrete r) => new
    {
        r.Id, r.Data, r.Destino, r.Transportadora, r.ValorFrete, r.Pedagio, r.Adicional, r.DescontoSeguro, r.PorcentagemTip,
        r.DescontoManual, r.Desconto, r.FreteTotal, r.Motorista, r.Placa, r.Observacoes, r.FormaPagamento, r.ViagemId,
        r.PorcentagemIcms, r.ForaDoControle, r.Origem, r.Conferido,
        // VIA-27: descarga = Adicional (ou a coluna Descarga, se um dia for gravada)
        Descarga = r.DescargaPaga, r.Contrato, r.ValorContrato, r.AdiantamentoDescarga, r.ReembolsoDescarga,
        r.ContratoLigacao, r.ContratoAviso,
    };

    public async Task<object> RetornosDoMesAsync(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1);
        var lista = await db.RetornosFrete.AsNoTracking().Where(r => r.Data >= ini && r.Data < fim)
            .OrderBy(r => r.Data).ThenBy(r => r.Id).ToListAsync();
        var viagemIds = lista.Where(r => r.ViagemId != null).Select(r => r.ViagemId!.Value).Distinct().ToList();
        var viagens = await db.ViagensCaminhao.AsNoTracking().Where(v => viagemIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Placa, v.DataSaida, v.Destino, v.Status }).ToDictionaryAsync(v => v.Id);
        var noControle = lista.Where(r => !r.ForaDoControle).ToList();
        // Anotação do motorista ligada a cada retorno (VIA-22)
        var ids = lista.Select(r => r.Id).ToList();
        var anotacoes = await db.ViagemCaminhaoCargas.AsNoTracking().Where(c => c.RetornoId != null && ids.Contains(c.RetornoId.Value))
            .Select(c => new { c.Id, RetornoId = c.RetornoId!.Value, c.ViagemId, c.Data, c.Valor, c.Transportadora, c.Destino, c.Ligacao, c.LigacaoAviso })
            .ToListAsync();
        var anotacaoDo = anotacoes.GroupBy(a => a.RetornoId).ToDictionary(g => g.Key, g => g.First());
        var pendentes = await AnotacoesPendentesAsync(ano, mes);
        return new
        {
            ano, mes,
            retornos = lista.Select(r => new
            {
                Retorno = RetornoDto(r),
                Viagem = r.ViagemId is { } vid && viagens.TryGetValue(vid, out var vv) ? vv : null,
                Anotacao = anotacaoDo.TryGetValue(r.Id, out var an) ? an : null,
            }),
            anotacoesPendentes = pendentes,
            comAnotacao = anotacaoDo.Count,
            total = noControle.Sum(r => r.FreteTotal),
            totalFrete = noControle.Sum(r => r.ValorFrete),
            quantidade = noControle.Count,
            aConferir = lista.Count(r => !r.Conferido),
            semViagem = noControle.Count(r => r.ViagemId == null),
        };
    }

    public async Task<RetornoFrete> SalvarRetornoAsync(int? id, RetornoEntrada e)
    {
        if (e.Data == null) throw new ErroViagem("Informe a data do frete.");
        if (e.ValorFrete <= 0) throw new ErroViagem("Informe o valor do frete.");
        RetornoFrete r;
        if (id is { } rid) r = await db.RetornosFrete.FindAsync(rid) ?? throw new ErroViagem("Retorno não encontrado.");
        else { r = new RetornoFrete { Origem = "escritorio" }; db.RetornosFrete.Add(r); }
        r.Data = e.Data.Value;
        r.Destino = Limpo(e.Destino);
        r.Transportadora = Limpo(e.Transportadora);
        r.ValorFrete = e.ValorFrete;
        r.Pedagio = e.Pedagio;
        r.Adicional = e.Adicional;
        r.DescontoSeguro = e.DescontoSeguro;
        r.PorcentagemTip = e.PorcentagemTip;
        r.DescontoManual = e.DescontoManual;
        r.Motorista = Limpo(e.Motorista);
        r.Placa = NormalizarPlaca(e.Placa) is { Length: > 0 } p ? p : null;
        r.Observacoes = Limpo(e.Observacoes);
        r.FormaPagamento = Limpo(e.FormaPagamento);
        r.PorcentagemIcms = e.PorcentagemIcms;
        r.ForaDoControle = e.ForaDoControle;
        r.Conferido = e.Conferido ?? true;
        r.AtualizadoEm = DateTime.UtcNow;
        var viagemAntes = r.ViagemId;
        if (e.ViagemId is { } vid)
        {
            if (!await db.ViagensCaminhao.AnyAsync(v => v.Id == vid)) throw new ErroViagem("Viagem não encontrada.");
            r.ViagemId = vid;
            // VIA-25: viagem escolhida à mão; a mesma de antes mantém como estava (auto continua auto)
            if (vid != viagemAntes) r.ViagemLigacao = ViagemPeriodoLigador.Manual;
        }
        else
        {
            r.ViagemId = null;
            if (viagemAntes != null) r.ViagemLigacao = ViagemPeriodoLigador.Solto;   // tirado da viagem: não volta sozinho
        }
        if (r.ViagemId != null && r.MotoristaId == null)
            r.MotoristaId = await db.ViagensCaminhao.Where(v => v.Id == r.ViagemId).Select(v => v.MotoristaId).FirstOrDefaultAsync();
        AplicarEscolhaDoContrato(r, e);
        await db.SaveChangesAsync();
        await AtualizarCargaDoRetornoAsync(r);
        // VIA-25: sem viagem (e não tirado à mão) procura a viagem da placa pelo período
        if (r.ViagemId == null && r.ViagemLigacao != ViagemPeriodoLigador.Solto) await LigarRetornosPeloPeriodoAsync(r.Data, r.Data);
        // VIA-27: liga ao contrato da Strada (PDF) e recalcula o adiantamento com o frete novo
        await StradaRetornos.SincronizarAsync(db);
        return r;
    }

    private static void AplicarEscolhaDoContrato(RetornoFrete r, RetornoEntrada e)
    {
        var numero = Limpo(e.Contrato);
        if (e.ContratoManual)
        {
            var situacao = numero == null ? RetornoContratoLigador.Solto : RetornoContratoLigador.Manual;
            if (r.ContratoLigacao == situacao && r.Contrato == numero) return;
            r.ContratoLigacao = situacao;
            r.Contrato = numero;
            r.ValorContrato = null; r.ContratoAviso = null;   // o SincronizarAsync preenche
        }
        else if (r.ContratoLigacao is RetornoContratoLigador.Manual or RetornoContratoLigador.Solto)
        {
            // Volta ao automático: o sincronizador procura de novo pela placa e data
            r.ContratoLigacao = null; r.Contrato = null; r.ValorContrato = null; r.ContratoAviso = null;
        }
    }

    /// <summary>Contratos da Strada com PDF ainda sem retorno; os da placa primeiro, depois pela data mais perto.</summary>
    public async Task<object> ContratosLivresAsync(string? placa, DateOnly? data)
    {
        var linhas = await db.StradaContratos.AsNoTracking().ToListAsync();
        var chave = RetornoAnotacaoLigador.ChavePlaca(placa);
        return linhas.GroupBy(c => c.Contrato)
            .Where(g => g.Any(c => c.ValorContratoPdf != null) && g.All(c => c.RetornoId == null))
            .Select(g => g.Where(c => c.ValorContratoPdf != null).OrderByDescending(c => c.DataContrato.HasValue).First())
            .Select(c => new { Numero = c.Contrato, c.Placa, Data = c.DataContrato, Valor = c.ValorContratoPdf, c.Nf })
            .OrderByDescending(c => chave.Length > 0 && RetornoAnotacaoLigador.ChavePlaca(c.Placa) == chave)
            .ThenBy(c => data is { } d && c.Data is { } cd ? Math.Abs(cd.DayNumber - d.DayNumber) : int.MaxValue)
            .ThenByDescending(c => c.Data)
            .Take(200)
            .ToList();
    }

    // Retorno tirado da viagem (ou posto em outra) solta a anotação do motorista ligada a ele e ela
    // não volta a ligar sozinha (o escritório decidiu). O valor anotado pelo motorista não muda.
    private async Task AtualizarCargaDoRetornoAsync(RetornoFrete r)
    {
        var cargas = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId == r.Id && c.ViagemId != r.ViagemId).ToListAsync();
        foreach (var c in cargas)
        {
            c.RetornoId = null;
            c.Ligacao = RetornoAnotacaoLigador.Solto;
            c.LigacaoAviso = $"O retorno de {r.Data:dd/MM} foi tirado desta viagem no escritório.";
        }
        await db.SaveChangesAsync();
    }

    public async Task VincularRetornoAsync(int retornoId, int? viagemId)
    {
        var r = await db.RetornosFrete.FindAsync(retornoId) ?? throw new ErroViagem("Retorno não encontrado.");
        if (viagemId is { } vid && !await db.ViagensCaminhao.AnyAsync(v => v.Id == vid)) throw new ErroViagem("Viagem não encontrada.");
        r.ViagemId = viagemId;
        // VIA-25: escolha à mão vale; tirado da viagem não volta sozinho
        r.ViagemLigacao = viagemId == null ? ViagemPeriodoLigador.Solto : ViagemPeriodoLigador.Manual;
        r.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AtualizarCargaDoRetornoAsync(r);
    }

    public async Task ExcluirRetornoAsync(int id)
    {
        var r = await db.RetornosFrete.FindAsync(id) ?? throw new ErroViagem("Retorno não encontrado.");
        var cargas = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId == id).ToListAsync();
        foreach (var c in cargas) { c.RetornoId = null; c.Ligacao = null; c.LigacaoAviso = null; }
        // VIA-27: o contrato da Strada ligado a ele volta a "sem viagem" (a FK só zera o RetornoId e deixava "ligado").
        // Linha com viagem da base AGROTRADE ou ligada à mão só perde o RetornoId.
        var contratos = await db.StradaContratos.Where(c => c.RetornoId == id).ToListAsync();
        foreach (var c in contratos)
        {
            c.RetornoId = null;
            if (c.ViagemId == null && c.Situacao != StradaService.Manual) { c.Tipo = "indefinido"; c.Situacao = StradaService.SemViagem; }
            c.AtualizadoEm = DateTime.UtcNow;
        }
        db.RetornosFrete.Remove(r);
        await db.SaveChangesAsync();
        if (contratos.Count > 0) await StradaRetornos.SincronizarAsync(db);
        // A anotação que estava nele procura outro retorno (ou fica sem par)
        foreach (var vid in cargas.Select(c => c.ViagemId).Distinct()) await LigarAnotacoesAsync(vid);
    }

    /// <summary>Retornos da mesma placa, perto das datas da viagem, ainda sem viagem.</summary>
    private async Task<List<RetornoFrete>> RetornosSugeridosAsync(ViagemCaminhao v)
    {
        if (v.DataSaida == null) return [];
        var ini = v.DataSaida.Value.AddDays(-3);
        var fim = (v.DataChegada ?? v.DataSaida.Value).AddDays(5);
        return await db.RetornosFrete.AsNoTracking()
            .Where(r => r.ViagemId == null && r.Placa == v.Placa && r.Data >= ini && r.Data <= fim)
            .OrderBy(r => r.Data).ToListAsync();
    }

    public async Task<object> ImportarRetornosAsync(byte[] bytes, string nome)
    {
        var lido = RetornosLeitor.Ler(bytes, nome);
        var inicioMes = new DateOnly(lido.Ano, lido.Mes, 1);
        var fimMes = inicioMes.AddMonths(1).AddDays(-1);
        // UI-22: importar de novo mescla com o que já está gravado (qualquer origem, mês ± 3 dias), nunca duplica
        var gravados = await db.RetornosFrete.Where(r => r.Data >= inicioMes.AddDays(-3) && r.Data <= fimMes.AddDays(3)).ToListAsync();
        var pares = RetornosCasamento.Casar(lido.Linhas, gravados
            .Select(r => new RetornosCasamento.Gravado(r.Id, r.Data, r.Placa, r.Transportadora, r.Destino, r.ValorFrete, r.ChaveImportacao)).ToList());
        var motoristas = await db.MotoristasFrota.AsNoTracking().ToListAsync();
        var avisos = new List<string>(lido.Avisos);
        int novos = 0, atualizados = 0, iguais = 0;
        foreach (var p in pares)
        {
            var l = p.Linha;
            if (p.Aviso != null) avisos.Add(p.Aviso);
            var motoristaId = AcharMotorista(l.Motorista, motoristas)?.Id;
            if (p.GravadoId is not { } gid)
            {
                db.RetornosFrete.Add(new RetornoFrete
                {
                    Data = l.Data, Destino = l.Destino, Transportadora = l.Transportadora, ValorFrete = l.ValorFrete,
                    Pedagio = l.Pedagio, Adicional = l.Adicional, DescontoSeguro = l.DescontoSeguro, PorcentagemTip = l.PorcentagemTip,
                    DescontoManual = l.DescontoManual, Motorista = l.Motorista, Placa = l.Placa, Observacoes = l.Observacoes,
                    FormaPagamento = l.FormaPagamento, Origem = "importacao", Conferido = true, ChaveImportacao = l.Chave,
                    MotoristaId = motoristaId,
                });
                novos++;
                continue;
            }
            var r = gravados.First(g => g.Id == gid);
            var trocas = AtualizarRetornoDaPlanilha(r, l, motoristaId);
            var mudouChave = r.ChaveImportacao != l.Chave;
            r.ChaveImportacao = l.Chave;
            if (trocas.Count > 0)
            {
                atualizados++;
                avisos.AddRange(trocas.Select(t => $"{l.Data:dd/MM} {l.Placa} {l.Transportadora}: {t}"));
            }
            else iguais++;
            if (trocas.Count > 0 || mudouChave) r.AtualizadoEm = DateTime.UtcNow;
        }
        // Importado antes e sumiu da planilha: só avisa, nunca apaga
        var casados = pares.Where(p => p.GravadoId != null).Select(p => p.GravadoId!.Value).ToHashSet();
        foreach (var r in gravados.Where(r => r.Origem == "importacao" && r.Data >= inicioMes && r.Data <= fimMes && !casados.Contains(r.Id)).OrderBy(r => r.Data))
            avisos.Add($"{r.Data:dd/MM} {r.Placa} {r.Transportadora} {r.ValorFrete:N2}: está no sistema e não na planilha.");
        await db.SaveChangesAsync();
        // Primeiro as anotações dos motoristas (VIA-22: a ligação delas diz a viagem), depois a viagem pelas datas
        var anotacoesLigadas = await LigarAnotacoesAsync(null, inicioMes.AddDays(-2), inicioMes.AddMonths(1).AddDays(1));
        var anotacoesPendentes = (await AnotacoesPendentesAsync(lido.Ano, lido.Mes)).Count;
        var vinculados = await VincularRetornosSemViagemAsync(lido.Ano, lido.Mes);
        // VIA-27: liga ao contrato da Strada e recalcula o adiantamento com o frete atualizado
        await StradaRetornos.SincronizarAsync(db);
        var total = lido.Linhas.Sum(l => l.FreteTotal);
        return new
        {
            arquivo = nome, mes = lido.Mes, ano = lido.Ano, linhas = lido.Linhas.Count, novos, atualizados, iguais,
            jaExistiam = lido.Linhas.Count - novos, vinculados, anotacoesLigadas, anotacoesPendentes, total, totalPlanilha = lido.TotalPlanilha,
            confere = lido.TotalPlanilha == null || Math.Abs(lido.TotalPlanilha.Value - total) < 0.01m,
            avisos,
        };
    }

    /// <summary>
    /// Copia a linha da planilha para o retorno casado e devolve cada troca ("campo: anterior → atual").
    /// Viagem, fora do controle, conferido, contrato e a coluna Descarga ficam como estão (UI-22, VIA-27).
    /// </summary>
    private static List<string> AtualizarRetornoDaPlanilha(RetornoFrete r, RetornoLido l, int? motoristaId)
    {
        var trocas = new List<string>();
        static string T(string? s) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s.Trim();
        static string V(decimal? v) => v is { } x ? x.ToString("N2", new System.Globalization.CultureInfo("pt-BR")) : "(vazio)";
        void Texto(string campo, string? antes, string? depois, Action<string?> gravar)
        {
            if ((antes ?? "").Trim() == (depois ?? "").Trim()) return;
            trocas.Add($"{campo} {T(antes)} → {T(depois)}");
            gravar(depois);
        }
        void Valor(string campo, decimal? antes, decimal? depois, Action<decimal?> gravar)
        {
            if (antes == depois) return;
            trocas.Add($"{campo} {V(antes)} → {V(depois)}");
            gravar(depois);
        }
        if (r.Data != l.Data) { trocas.Add($"data {r.Data:dd/MM} → {l.Data:dd/MM}"); r.Data = l.Data; }
        Texto("placa", r.Placa, l.Placa, v => r.Placa = v);
        Texto("transportadora", r.Transportadora, l.Transportadora, v => r.Transportadora = v);
        Texto("destino", r.Destino, l.Destino, v => r.Destino = v);
        Valor("valor do frete", r.ValorFrete, l.ValorFrete, v => r.ValorFrete = v ?? 0);
        Valor("pedágio", r.Pedagio, l.Pedagio, v => r.Pedagio = v ?? 0);
        Valor("adicional", r.Adicional, l.Adicional, v => r.Adicional = v ?? 0);
        Valor("seguro", r.DescontoSeguro, l.DescontoSeguro, v => r.DescontoSeguro = v ?? 0);
        Valor("% TIP", r.PorcentagemTip, l.PorcentagemTip, v => r.PorcentagemTip = v);
        Valor("desconto", r.DescontoManual, l.DescontoManual, v => r.DescontoManual = v);
        Texto("motorista", r.Motorista, l.Motorista, v => r.Motorista = v);
        Texto("observações", r.Observacoes, l.Observacoes, v => r.Observacoes = v);
        Texto("forma de pagamento", r.FormaPagamento, l.FormaPagamento, v => r.FormaPagamento = v);
        // Motorista do cadastro: só troca quando a planilha acha um (não solta o que veio da viagem)
        if (motoristaId != null && r.MotoristaId != motoristaId) r.MotoristaId = motoristaId;
        return trocas;
    }

    /// <summary>Liga retornos sem viagem do mês (± 7 dias) pela placa e pelo período da viagem (VIA-25).</summary>
    public Task<int> VincularRetornosSemViagemAsync(int ano, int mes) =>
        LigarRetornosPeloPeriodoAsync(new DateOnly(ano, mes, 1).AddDays(-7), new DateOnly(ano, mes, 1).AddMonths(1).AddDays(6));

    /// <summary>
    /// VIA-25: retornos livres com data entre ini e fim entram na viagem da placa cujo período contém a data
    /// (ViagemPeriodoLigador). Só quando há uma viagem; retorno tirado à mão ("solto") ou fora do controle não entra.
    /// </summary>
    private async Task<int> LigarRetornosPeloPeriodoAsync(DateOnly ini, DateOnly fim)
    {
        var livres = await db.RetornosFrete.Where(r => r.ViagemId == null && !r.ForaDoControle && r.Placa != null
                && (r.ViagemLigacao == null || r.ViagemLigacao != ViagemPeriodoLigador.Solto) && r.Data >= ini && r.Data <= fim)
            .ToListAsync();
        if (livres.Count == 0) return 0;
        // Viagens que podem cobrir o intervalo e as seguintes (a próxima saída fecha o período da que não tem chegada)
        var viagens = await db.ViagensCaminhao.AsNoTracking()
            .Where(v => v.DataSaida != null && v.DataSaida >= ini.AddDays(-60) && v.DataSaida <= fim.AddDays(ViagemPeriodoLigador.DiasSemChegada + 1))
            .Select(v => new ViagemPeriodoLigador.Viagem(v.Id, v.Placa, v.PlacaCarreta, v.DataSaida, v.DataChegada, v.MotoristaId))
            .ToListAsync();
        var res = ViagemPeriodoLigador.Ligar(
            livres.Select(r => new ViagemPeriodoLigador.Retorno(r.Id, r.Placa, r.Data, r.ViagemId, r.ViagemLigacao)), viagens);
        var n = 0;
        foreach (var x in res.Where(x => x.ViagemId != null))
        {
            var r = livres.First(l => l.Id == x.RetornoId);
            r.ViagemId = x.ViagemId;
            r.ViagemLigacao = ViagemPeriodoLigador.Auto;
            if (x.MotoristaId != null) r.MotoristaId = x.MotoristaId;
            r.AtualizadoEm = DateTime.UtcNow;
            n++;
        }
        if (n > 0) await db.SaveChangesAsync();
        return n;
    }

    /// <summary>Depois de gravar a viagem: os retornos livres do período dela (VIA-25).</summary>
    private async Task LigarRetornosDaViagemAsync(ViagemCaminhao v)
    {
        if (v.DataSaida is not { } s) return;
        var fim = v.DataChegada is { } c && c >= s ? c : s.AddDays(ViagemPeriodoLigador.DiasSemChegada);
        await LigarRetornosPeloPeriodoAsync(s, fim);
    }

    public static MotoristaFrota? AcharMotorista(string? nome, IEnumerable<MotoristaFrota> lista)
    {
        var alvo = Normalizar(nome);
        if (alvo.Length == 0) return null;
        var achados = lista.Where(m => Normalizar(m.Nome) == alvo
            || (m.Apelidos ?? "").Split(',').Select(Normalizar).Any(a => a.Length > 0 && a == alvo)).ToList();
        return achados.Count == 1 ? achados[0] : null;
    }

    // ───────────────────────── Controle de carregamento ─────────────────────────

    public async Task<object> ImportarCarregamentoAsync(byte[] bytes, string nome)
    {
        var lidos = CarregamentoLeitor.Ler(bytes);
        var resultado = new List<object>();
        var conhecidas = await PlacasDaFrotaAsync();
        var donos = await DonosDasPlacas.CarregarAsync(db);
        foreach (var l in lidos)
        {
            if (l.Data == null || l.Placa == null || l.Itens.Count == 0)
            {
                resultado.Add(new { aba = l.Aba, guardado = false, avisos = l.Avisos });
                continue;
            }
            var placa = CorrigirPlaca(NormalizarPlaca(l.Placa), conhecidas);
            if (placa != NormalizarPlaca(l.Placa))
                l.Avisos.Add($"Placa \"{l.Placa.Trim()}\" do controle corrigida para {placa} (a da frota mais parecida).");
            else if (!conhecidas.Contains(placa))
                l.Avisos.Add($"Placa {placa} não é da frota e não parece com nenhuma: confira.");
            // VIA-24: todo controle entra (produtosB, produtosC e volumes embalados), mas só os caminhões do
            // Bento aparecem na aba Carregamentos e se ligam às viagens do acerto; dono pelo cadastro de veículos
            var doBento = donos.Dono(placa) == DonosDasPlacas.Bento;
            if (!doBento)
            {
                var dono = donos.SocioDe(placa) is { } s ? DonosDasPlacas.Nome(s) : "sem dono no cadastro";
                l.Avisos.Add($"Placa {placa} ({dono}) não é placa do Bento: entra só para produtosB e volumes embalados, fora da aba Carregamentos.");
            }
            // Mesma placa e dia: substitui (importar de novo não duplica)
            var c = await db.CarregamentosVolumes.FirstOrDefaultAsync(x => x.Placa == placa && x.Data == l.Data);
            if (c == null) { c = new CarregamentoVolumes { Placa = placa, Data = l.Data.Value }; db.CarregamentosVolumes.Add(c); }
            c.Motorista = l.Motorista;
            c.Destino = l.Destino;
            c.Observacao = l.Observacao;
            c.NomeArquivo = nome;
            c.ItensJson = ItensParaJson(l.Itens);
            await db.SaveChangesAsync();

            var (viagemId, aplicado) = await DepoisDeGravarAsync(c, l.Avisos, doBento);
            resultado.Add(new
            {
                aba = l.Aba, guardado = true, id = c.Id, placa, doBento, data = c.Data, clientes = l.Itens.Count, viagemId, aplicado,
                volumes = l.Itens.Sum(i => i.Volumes), produtoE = l.Itens.Sum(i => i.ProdutoE), produtoD = l.Itens.Sum(i => i.ProdutoD),
                amarelas = l.Itens.Sum(i => i.Amarelas), brancas = l.Itens.Sum(i => i.Brancas), produtosC = l.Itens.Sum(i => i.ProdutosC),
                avisos = l.Avisos,
            });
        }
        return new { arquivo = nome, cargas = resultado };
    }

    /// <summary>
    /// Lê de novo o carregamento guardado com a regra atual (VIA-09), pelas colunas
    /// que ficaram guardadas de cada cliente, sem precisar da planilha. É o que
    /// conserta um carregamento lido pela regra antiga (que dobrava as caixas).
    /// </summary>
    public async Task<object> ReprocessarCarregamentoAsync(int id)
    {
        var c = await db.CarregamentosVolumes.FindAsync(id) ?? throw new ErroViagem("Carregamento não encontrado.");
        var antigos = ItensDe(c);
        var comTotais = antigos.Any(i => i.colunas?.Keys.Any(k => Normalizar(k).Replace(" ", "") == "VOLUMES") == true);
        var itens = antigos
            .Select(i => i.colunas is { Count: > 0 } col
                ? CarregamentoLeitor.Montar(i.cliente, col, comTotais)
                : new ItemCarregamento(i.cliente, i.volumes, i.produtoE, i.produtoD, i.amarelas, i.brancas, i.produtosC, []))
            .Where(i => !i.Vazio).ToList();
        c.ItensJson = ItensParaJson(itens);
        await db.SaveChangesAsync();
        var avisos = new List<string>();
        var doBento = (await DonosDasPlacas.CarregarAsync(db)).Dono(c.Placa) == DonosDasPlacas.Bento;
        var (viagemId, aplicado) = await DepoisDeGravarAsync(c, avisos, doBento);
        return new
        {
            id = c.Id, placa = c.Placa, data = c.Data, clientes = itens.Count, viagemId, aplicado,
            volumes = itens.Sum(i => i.Volumes), produtoE = itens.Sum(i => i.ProdutoE), produtoD = itens.Sum(i => i.ProdutoD),
            amarelas = itens.Sum(i => i.Amarelas), brancas = itens.Sum(i => i.Brancas), produtosC = itens.Sum(i => i.ProdutosC),
            itens = itens.Select(i => new { cliente = i.Cliente, volumes = i.Volumes, produtoE = i.ProdutoE, produtoD = i.ProdutoD, amarelas = i.Amarelas, brancas = i.Brancas, produtosC = i.ProdutosC }),
            avisos,
        };
    }

    private static string ItensParaJson(IEnumerable<ItemCarregamento> itens) => JsonSerializer.Serialize(itens.Select(i => new
    {
        cliente = i.Cliente, volumes = i.Volumes, produtoE = i.ProdutoE, produtoD = i.ProdutoD, produtosB = i.ProdutosB,
        amarelas = i.Amarelas, brancas = i.Brancas, produtosC = i.ProdutosC, colunas = i.Colunas,
    }));

    /// <summary>
    /// Depois de gravar o carregamento: volumes na viagem (a nova sem volumes recebe; a
    /// ligada e ainda lançada é trocada; conferida ou fechada só avisa), saídas de
    /// produtosB e produtosC por cliente (EST-02, EST-07) e produtosC da BRZ (NFE-05).
    /// </summary>
    private async Task<(int? ViagemId, bool Aplicado)> DepoisDeGravarAsync(CarregamentoVolumes c, List<string> avisos, bool ligarViagem)
    {
        int? viagemId = c.ViagemId;
        var aplicado = false;
        if (!ligarViagem) { }
        else if (viagemId == null)
        {
            var v = await ViagemDoCarregamentoAsync(c, avisos);
            if (v != null && !await db.ViagemCaminhaoVolumes.AnyAsync(o => o.ViagemId == v.Id))
            {
                avisos.AddRange(await AplicarCarregamentoAsync(v.Id, c.Id));
                viagemId = v.Id;
                aplicado = true;
            }
            else if (v != null)
            {
                // A viagem já tem volumes (lançados ou da planilha do acerto): só liga, sem
                // trocar. Antes ficava solta e a viagem aparecia sem carregamento.
                if (await db.CarregamentosVolumes.AnyAsync(x => x.ViagemId == v.Id && x.Id != c.Id))
                    avisos.Add($"A viagem de {v.DataSaida:dd/MM} desta placa já tem outro controle de carregamento ligado: este ficou solto.");
                else
                {
                    c.ViagemId = v.Id;
                    await db.SaveChangesAsync();
                    viagemId = v.Id;
                    avisos.Add($"Ligado à viagem de {v.DataSaida:dd/MM}, que já tinha os volumes: eles não mudaram. Para trocar pelos do controle, use o carregamento na viagem.");
                }
            }
        }
        else
        {
            var v = await db.ViagensCaminhao.AsNoTracking().FirstOrDefaultAsync(x => x.Id == viagemId);
            if (v?.Status == "lancada")
            {
                avisos.AddRange(await AplicarCarregamentoAsync(v.Id, c.Id));
                aplicado = true;
            }
            else if (v != null)
                avisos.Add($"A viagem ligada já está {(v.Status == "fechada" ? "fechada" : "conferida")}: os volumes dela não mudaram. Para trocar, reabra e use o carregamento na viagem.");
        }

        var itens = ItensDe(c);
        avisos.AddRange(await vendas.SincronizarCarregamentoAsync(c,
            itens.Select(i => new SaidaDoCarregamento(i.cliente, i.amarelas, i.brancas, i.produtosC)).ToList()));
        avisos.AddRange(await RomaneioBrzAsync(c, itens));
        avisos.AddRange(await SaidasEmbaladosAsync(c, itens));
        return (viagemId, aplicado);
    }

    /// <summary>
    /// Saídas dos volumes embalados pelo controle de carregamento (VOLUME-15): por cliente,
    /// AVE = embalado vermelho (Ave) e AV PVC = PVC vermelho (Gr PvcVr), Origem =
    /// 'carregamento:ID' (importar de novo troca as dele). Vale a planilha: a saída
    /// lançada à mão do mesmo cliente no dia, na véspera ou no dia seguinte é trocada
    /// pela do carregamento no mesmo controle (Admin, 20/10/2026). Sem ela, entra no
    /// último controle com data até a do carregamento.
    /// </summary>
    private async Task<List<string>> SaidasEmbaladosAsync(CarregamentoVolumes c, List<ItemJson> itens)
    {
        var avisos = new List<string>();
        var origem = OrigemCarregamento(c.Id);
        db.VolumesControleLinhas.RemoveRange(await db.VolumesControleLinhas.Where(l => l.Origem == origem).ToListAsync());

        var saidas = SaidasEmbaladosDe(itens);
        if (saidas.Count == 0) { await db.SaveChangesAsync(); return avisos; }

        var ini = c.Data.AddDays(-1);
        var fim = c.Data.AddDays(1);
        // Saídas à mão perto do dia, em qualquer controle (as de 02/10 estavam no 329, não no 330)
        var perto = await db.VolumesControleLinhas.Include(l => l.Controle)
            .Where(l => l.Grupo == ControleVolumesLinha.Saida && l.Origem == null && l.Data >= ini && l.Data <= fim)
            .ToListAsync();
        // A da véspera ou do dia seguinte só é desta carga se nenhuma carga daquele dia levou o cliente
        var vizinhas = (await db.CarregamentosVolumes.AsNoTracking()
                .Where(x => x.Id != c.Id && x.Data >= ini && x.Data <= fim && x.Data != c.Data).ToListAsync())
            .SelectMany(x => SaidasEmbaladosDe(ItensDe(x)).Select(sx => (x.Data, sx.cliente))).ToList();

        // O caminhão leva o que foi embalado antes: vale o último controle de ANTES do dia
        // (como o Admin lançava: saídas de 02/10 no 329 de 30/09, de 09/10 no 331 de 07/10)
        var controle = await db.VolumesControles.Include(x => x.Linhas)
            .Where(x => x.Data < c.Data).OrderByDescending(x => x.Data).ThenByDescending(x => x.Numero).FirstOrDefaultAsync();
        // O controle das planilhas antigas já traz as saídas do período dele: só recebe as
        // de depois da última que ele tem (o 328 de 23/09 não tinha as de 28–29/09)
        var ultimaDaPlanilha = controle?.Origem == "planilha"
            ? controle.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida).Max(l => l.Data)
            : null;
        var ordem = controle?.Linhas.Select(l => l.Ordem).DefaultIfEmpty(0).Max() ?? 0;
        var lancadas = new List<string>();
        var semControle = new List<string>();
        foreach (var (cliente, ave, pvc) in saidas)
        {
            // Só pelo nome: comparar as caixas casava clientes diferentes com a mesma
            // quantidade (GUSTAVO de 12/10 tomado pelo IRINEU do controle 332)
            var aMao = perto
                .Where(l => MesmoCliente(l.Nome, cliente))
                .Where(l => l.Data == c.Data || !vizinhas.Any(v => v.Data == l.Data && MesmoCliente(l.Nome, v.cliente)))
                .OrderBy(l => Math.Abs(l.Data!.Value.DayNumber - c.Data.DayNumber))
                .FirstOrDefault();
            if (aMao != null)
            {
                perto.Remove(aMao);
                if (aMao.Controle?.Origem == "planilha") continue; // histórico das planilhas fica como está
                // A própria linha passa a ser do carregamento (mesmo lugar no controle)
                var mudou = aMao.Ave != ave || aMao.GrPvcVr != pvc;
                avisos.Add($"Embalados: {aMao.Nome} de {aMao.Data:dd/MM} lançado à mão no controle {aMao.Controle?.Numero} " +
                    (mudou ? $"foi trocado pelo do carregamento (AVE {aMao.Ave:0.##} → {ave:0.##}, AV PVC {aMao.GrPvcVr:0.##} → {pvc:0.##})."
                           : "agora vem do carregamento."));
                aMao.Nome = cliente; aMao.Placa = c.Placa; aMao.Data = c.Data; aMao.Origem = origem;
                aMao.Ave = ave; aMao.GrPvcVr = pvc;
                continue;
            }
            if (controle == null || (ultimaDaPlanilha is { } u && c.Data <= u.AddDays(1)))
            {
                semControle.Add(cliente);
                continue;
            }
            db.VolumesControleLinhas.Add(new ControleVolumesLinha
            {
                ControleId = controle.Id, Grupo = ControleVolumesLinha.Saida, Ordem = ++ordem, Nome = cliente,
                Placa = c.Placa, Data = c.Data, Origem = origem, Ave = ave, GrPvcVr = pvc,
            });
            lancadas.Add(cliente);
        }
        await db.SaveChangesAsync();
        if (lancadas.Count > 0)
            avisos.Add($"Embalados: saídas de AVE e AV PVC de {string.Join(", ", lancadas)} no controle {controle!.Numero}.");
        if (semControle.Count > 0)
            avisos.Add(controle == null
                ? $"Embalados: nenhum controle até {c.Data:dd/MM}; as saídas de AVE e AV PVC de {string.Join(", ", semControle)} não foram lançadas."
                : $"Embalados: {string.Join(", ", semControle)} de {c.Data:dd/MM} não entrou: o controle {controle.Numero} veio da planilha antiga e já cobre esse dia.");
        return avisos;
    }

    private static List<(string cliente, decimal ave, decimal pvc)> SaidasEmbaladosDe(List<ItemJson> itens)
    {
        static decimal Coluna(ItemJson i, string chave) =>
            (i.colunas ?? []).Where(kv => Normalizar(kv.Key).Replace(" ", "") == chave).Sum(kv => kv.Value);
        return itens.Select(i => (i.cliente, ave: Coluna(i, "AVE"), pvc: Coluna(i, "AVPVC")))
            .Where(s => s.ave > 0 || s.pvc > 0).ToList();
    }

    public static string OrigemCarregamento(int id) => "carregamento:" + id;

    // Placas da frota: veículos, o cadastro de placas, placa habitual dos motoristas e as das viagens
    private async Task<HashSet<string>> PlacasDaFrotaAsync()
    {
        var veiculos = await db.Veiculos.AsNoTracking().Select(v => v.Placa).ToListAsync();
        var cadastro = await db.PlacasFrota.AsNoTracking().Select(p => p.Placa).ToListAsync();
        var motoristas = await db.MotoristasFrota.AsNoTracking().Where(m => m.PlacaCavalo != null).Select(m => m.PlacaCavalo!).ToListAsync();
        var viagens = await db.ViagensCaminhao.AsNoTracking().Select(v => v.Placa).Distinct().ToListAsync();
        return veiculos.Concat(cadastro).Concat(motoristas).Concat(viagens).Select(NormalizarPlaca).Where(p => p.Length > 0).ToHashSet();
    }

    /// <summary>
    /// Só no controle de carregamento (Admin, 20/10/2026): a placa vem digitada à
    /// mão e às vezes com um caractere errado, a mais ou a menos ("XAB -4C58",
    /// "XAC-6D75"). Vale a placa da frota que difere em até 1 caractere, quando só
    /// uma casa; senão fica a lida. Abastecimentos guardam a placa do relatório.
    /// </summary>
    public static string CorrigirPlaca(string placa, IReadOnlyCollection<string> frota)
    {
        static string So(string p) => new(p.Where(char.IsLetterOrDigit).ToArray());
        var lida = So(placa);
        if (lida.Length == 0) return placa;
        var igual = frota.FirstOrDefault(f => So(f) == lida);
        if (igual != null) return igual;
        var parecidas = frota.Where(f => Distancia(So(f), lida) <= 1).Distinct().ToList();
        return parecidas.Count == 1 ? parecidas[0] : placa;
    }

    // "FAROL VOLUMES" no carregamento e "FAROL" no controle; "BOA NUTRI" e "BOANUTRI"
    private static bool MesmoCliente(string? a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return false;
        var x = Normalizar(a); var y = Normalizar(b);
        var xs = x.Replace(" ", ""); var ys = y.Replace(" ", "");
        var px = x.Split(' ')[0]; var py = y.Split(' ')[0];
        // "WESLEY" no carregamento e "WELINGTON" no controle: até 2 letras de diferença
        return xs.StartsWith(ys) || ys.StartsWith(xs) || px == py
            || (Math.Min(px.Length, py.Length) >= 5 && Distancia(px, py) <= 2);
    }

    private static int Distancia(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// A produtoC da BRZ no controle é a mesma do romaneio da nota (NFE-05): o
    /// romaneio guardado do mesmo dia (e da mesma placa) fica com a quantidade do
    /// controle, sempre com aviso quando era diferente (EST-07).
    /// </summary>
    private async Task<List<string>> RomaneioBrzAsync(CarregamentoVolumes c, List<ItemJson> itens)
    {
        var avisos = new List<string>();
        var tabela = await precos.TabelaAsync();
        var brz = itens.Where(i => ProdutosCBrz.EhBrz(tabela.AcharFrete(i.cliente), i.cliente)).ToList();
        if (brz.Count == 0) return avisos;
        var produtosC = brz.Sum(i => i.produtosC);
        var dia = c.Data.ToString("dd-MM-yyyy");
        var romaneios = (await db.PlanilhasCarga.Where(p => p.Nome.Contains(dia)).ToListAsync())
            .Where(p => ProdutosCBrz.RomaneioDoDia(p.Nome, c.Data)).OrderByDescending(p => p.CriadoEm).ToList();
        if (romaneios.Count == 0)
        {
            if (produtosC > 0) avisos.Add($"BRZ: ainda não há romaneio da nota de {c.Data:dd/MM}. Ao gerar, informe {produtosC:0.##} produtosC (as do controle).");
            return avisos;
        }
        var estoqueBrz = tabela.EstoqueDoCliente(brz[0].cliente);
        var tipoProdutoC = await db.TiposProdutoB.AsNoTracking().FirstOrDefaultAsync(t => t.Codigo == TipoProdutoB.CodigoProdutoC);
        var precoBrz = estoqueBrz?.PrecosProdutoB.FirstOrDefault(p => p.TipoProdutoBId == tipoProdutoC?.Id) is { Preco: > 0 } pr
            ? pr.Preco : (decimal)NFeImportService.PrecoProdutoCPadrao;
        var mexeu = false;
        foreach (var p in romaneios)
        {
            ProdutosCBrz.Linha? linha;
            try { linha = ProdutosCBrz.Ler(p.Conteudo); }
            catch (Exception) { avisos.Add($"{p.Nome}: não consegui abrir o romaneio para conferir as produtosC."); continue; }
            if (linha == null) { avisos.Add($"{p.Nome}: o romaneio não tem a linha ProdutosC; confira à mão ({produtosC:0.##} no controle)."); continue; }
            // Romaneio de outro caminhão no mesmo dia fica como está
            if (Placas(linha.Placa).FirstOrDefault() is { } placaRomaneio && placaRomaneio != c.Placa) continue;
            if (linha.Quantidade == produtosC) continue;
            var novo = ProdutosCBrz.Gravar(p.Conteudo, produtosC, precoBrz);
            if (novo == null) continue;
            p.Conteudo = novo;
            mexeu = true;
            avisos.Add($"{p.Nome}: o romaneio da BRZ tinha {linha.Quantidade:0.##} produtosC e o controle diz {produtosC:0.##}. O romaneio guardado ficou com {produtosC:0.##}; se já tinha mandado, mande de novo.");
        }
        if (mexeu) await db.SaveChangesAsync();
        return avisos;
    }

    /// <summary>ProdutosC da BRZ no controle de carregamento do dia (para conferir o romaneio gerado depois).</summary>
    public async Task<decimal?> ProdutosCBrzNoControleAsync(DateOnly dia)
    {
        var cargas = await db.CarregamentosVolumes.AsNoTracking().Where(c => c.Data == dia).ToListAsync();
        if (cargas.Count == 0) return null;
        var tabela = await precos.TabelaAsync();
        var brz = cargas.SelectMany(ItensDe).Where(i => ProdutosCBrz.EhBrz(tabela.AcharFrete(i.cliente), i.cliente)).ToList();
        return brz.Count == 0 ? null : brz.Sum(i => i.produtosC);
    }

    // VIA-09/VIA-25: viagens da placa (cavalo) com saída de ini − 60 a fim + 12 (a próxima saída fecha o período da
    // viagem sem chegada), os dias das anotações de carga VOLUMES e as que já têm controle ligado
    private record ViagemParaCarga(ViagemPeriodoLigador.Viagem V, List<DateOnly> DiasVolumes, string Status, bool TemVolumes);

    private async Task<(List<ViagemParaCarga> Viagens, Dictionary<int, int> ControleDaViagem)> ViagensParaCargaAsync(string? placa, DateOnly ini, DateOnly fim)
    {
        var chave = RetornoAnotacaoLigador.ChavePlaca(placa);
        var lista = (await db.ViagensCaminhao.AsNoTracking()
                .Where(v => v.DataSaida != null && v.DataSaida >= ini.AddDays(-60) && v.DataSaida <= fim.AddDays(ViagemPeriodoLigador.DiasSemChegada + 2))
                .Select(v => new
                {
                    v.Id, v.Placa, v.PlacaCarreta, v.DataSaida, v.DataChegada, v.MotoristaId, v.Status,
                    Dias = v.Cargas.Where(x => x.Tipo == "VOLUMES" && x.Data != null).Select(x => x.Data!.Value).ToList(),
                    TemVolumes = v.Volumes.Any(),
                })
                .ToListAsync())
            .Where(v => RetornoAnotacaoLigador.ChavePlaca(v.Placa) == chave)
            .Select(v => new ViagemParaCarga(new ViagemPeriodoLigador.Viagem(v.Id, v.Placa, v.PlacaCarreta, v.DataSaida, v.DataChegada, v.MotoristaId), v.Dias, v.Status, v.TemVolumes))
            .ToList();
        var ids = lista.Select(v => v.V.Id).ToList();
        var controles = await db.CarregamentosVolumes.AsNoTracking().Where(c => c.ViagemId != null && ids.Contains(c.ViagemId.Value))
            .Select(c => new { c.Id, ViagemId = c.ViagemId!.Value }).ToListAsync();
        return (lista, controles.GroupBy(c => c.ViagemId).ToDictionary(g => g.Key, g => g.First().Id));
    }

    private async Task<ViagemCaminhao?> ViagemDoCarregamentoAsync(CarregamentoVolumes c, List<string> avisos)
    {
        var (viagens, comControle) = await ViagensParaCargaAsync(c.Placa, c.Data, c.Data);
        var e = CarregamentoPeriodoLigador.EscolherViagem(new(c.Id, c.Placa, c.Data), viagens.Select(x => x.V).ToList(),
            id => viagens.First(x => x.V.Id == id).DiasVolumes,
            comControle.Where(k => k.Value != c.Id).Select(k => k.Key).ToHashSet());
        if (e.Aviso != null) avisos.Add(e.Aviso);
        return e.ViagemId is { } vid ? await db.ViagensCaminhao.FirstOrDefaultAsync(v => v.Id == vid) : null;
    }

    // Controle solto da janela da viagem (CarregamentoPeriodoLigador.EscolherCarga); nulo se a viagem já tem controle
    private async Task<CarregamentoVolumes?> CarregamentoDaViagemAsync(ViagemCaminhao v, List<string>? avisos)
    {
        if (v.DataSaida is not { } saida) return null;
        if (await db.CarregamentosVolumes.AnyAsync(c => c.ViagemId == v.Id)) return null; // um controle por viagem
        var (viagens, comControle) = await ViagensParaCargaAsync(v.Placa, saida, saida);
        var esta = new ViagemPeriodoLigador.Viagem(v.Id, v.Placa, v.PlacaCarreta, v.DataSaida, v.DataChegada, v.MotoristaId);
        var todas = viagens.Select(x => x.V).Where(x => x.Id != v.Id).Append(esta).ToList();
        if (CarregamentoPeriodoLigador.Janela(esta, todas) is not { } j) return null;
        var chave = RetornoAnotacaoLigador.ChavePlaca(v.Placa);
        var soltas = (await db.CarregamentosVolumes.Where(c => c.ViagemId == null && c.Data >= j.Ini && c.Data <= j.Fim).ToListAsync())
            .Where(c => RetornoAnotacaoLigador.ChavePlaca(c.Placa) == chave).ToList();
        if (soltas.Count == 0) return null;
        var dias = v.Cargas.Where(x => x.Tipo == "VOLUMES" && x.Data != null).Select(x => x.Data!.Value);
        var (cargaId, aviso) = CarregamentoPeriodoLigador.EscolherCarga(esta, todas, dias,
            soltas.Select(c => new CarregamentoPeriodoLigador.Carga(c.Id, c.Placa, c.Data)).ToList(), comControle.Keys.ToHashSet(),
            id => viagens.FirstOrDefault(x => x.V.Id == id)?.DiasVolumes ?? []);
        if (aviso != null) avisos?.Add(aviso);
        return cargaId is { } id ? soltas.First(c => c.Id == id) : null;
    }

    private async Task CasarCarregamentoAsync(ViagemCaminhao v)
    {
        if (v.DataSaida == null) return;
        if ((await DonosDasPlacas.CarregarAsync(db)).Dono(v.Placa) != DonosDasPlacas.Bento) return; // VIA-24
        var escolhido = await CarregamentoDaViagemAsync(v, null);
        if (escolhido != null) await AplicarCarregamentoAsync(v.Id, escolhido.Id);
    }

    /// <summary>
    /// VIA-25: controles soltos das placas do Bento com data entre ini e fim vão para a viagem da placa pela janela
    /// (CarregamentoPeriodoLigador). Só liga; viagem "lancada" sem volumes recebe os volumes do controle (como
    /// DepoisDeGravarAsync). Conferida ou fechada: só liga. Devolve quantos ligou; os avisos vão para a lista.
    /// </summary>
    public async Task<int> LigarCarregamentosPeloPeriodoAsync(DateOnly ini, DateOnly fim, List<string>? avisos = null)
    {
        var donos = await DonosDasPlacas.CarregarAsync(db);
        var soltas = (await db.CarregamentosVolumes.Where(c => c.ViagemId == null && c.Data >= ini && c.Data <= fim).OrderBy(c => c.Data).ToListAsync())
            .Where(c => donos.Dono(c.Placa) == DonosDasPlacas.Bento).ToList();
        var n = 0;
        foreach (var grupo in soltas.GroupBy(c => RetornoAnotacaoLigador.ChavePlaca(c.Placa)))
        {
            var (viagens, controleDaViagem) = await ViagensParaCargaAsync(grupo.First().Placa, ini, fim);
            var comControle = controleDaViagem.Keys.ToHashSet();
            var lista = viagens.Select(x => x.V).ToList();
            var escolhas = grupo.Select(c => (c, e: CarregamentoPeriodoLigador.EscolherViagem(new(c.Id, c.Placa, c.Data), lista,
                id => viagens.First(x => x.V.Id == id).DiasVolumes, comControle))).ToList();
            foreach (var (_, e) in escolhas.Where(x => x.e.Duvida)) avisos?.Add(e.Aviso!);
            foreach (var porViagem in escolhas.Where(x => x.e.ViagemId != null).GroupBy(x => x.e.ViagemId!.Value))
            {
                var vid = porViagem.Key;
                var v = viagens.First(x => x.V.Id == vid);
                // Dois controles para a mesma viagem: vale o do dia da saída ou da anotação; senão nenhum (como EscolherCarga)
                var c = porViagem.Count() == 1 ? porViagem.First().c
                    : porViagem.Where(x => x.c.Data == v.V.Saida || v.DiasVolumes.Contains(x.c.Data)).Select(x => x.c).ToList() is [var unico] ? unico : null;
                if (c == null)
                {
                    avisos?.Add($"{porViagem.Count()} controles possíveis para a viagem de {v.V.Saida:dd/MM} ({string.Join(", ", porViagem.Select(x => x.c.Data).Order().Select(d => d.ToString("dd/MM")))}): escolha na viagem.");
                    continue;
                }
                if (porViagem.First(x => x.c == c).e.Aviso is { } aviso) avisos?.Add(aviso);
                if (!v.TemVolumes && v.Status == "lancada")
                {
                    var aplicados = await AplicarCarregamentoAsync(vid, c.Id);
                    avisos?.AddRange(aplicados);
                }
                else
                {
                    c.ViagemId = vid;
                    await db.SaveChangesAsync();
                }
                comControle.Add(vid);
                n++;
            }
        }
        return n;
    }

    private async Task<object> CarregamentosCandidatosAsync(ViagemCaminhao v)
    {
        var lista = await db.CarregamentosVolumes.AsNoTracking()
            .Where(c => c.ViagemId == v.Id || (c.ViagemId == null && c.Placa == v.Placa && v.DataSaida != null
                && c.Data >= v.DataSaida.Value.AddDays(-5) && c.Data <= v.DataSaida.Value.AddDays(5)))
            .OrderBy(c => c.Data).ToListAsync();
        return lista.Select(c => new { c.Id, c.Data, c.Placa, c.Motorista, c.NomeArquivo, Aplicado = c.ViagemId == v.Id, Clientes = ItensDe(c).Count });
    }

    // Itens guardados no carregamento. Os lidos pela regra antiga não têm amarelas,
    // brancas e produtosC (só "produtosB"): reprocessar usa as colunas.
    public record ItemJson(string cliente, decimal volumes, decimal produtoE, decimal produtoD, decimal produtosB,
        decimal amarelas = 0, decimal brancas = 0, decimal produtosC = 0, Dictionary<string, decimal>? colunas = null);

    // Pela regra antiga as caixas não batiam com a coluna VOLUMES (total da linha) guardada
    private static bool LidoPelaRegraAntiga(List<ItemJson> itens)
    {
        foreach (var i in itens)
            foreach (var (k, v) in i.colunas ?? [])
                if (Normalizar(k).Replace(" ", "") == "VOLUMES" && v != i.volumes) return true;
        return false;
    }

    private static List<ItemJson> ItensDe(CarregamentoVolumes c)
    {
        try { return JsonSerializer.Deserialize<List<ItemJson>>(c.ItensJson) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Troca os volumes da viagem pelos clientes do carregamento, com os fretes da tabela no dia.</summary>
    public async Task<List<string>> AplicarCarregamentoAsync(int viagemId, int carregamentoId)
    {
        var v = await db.ViagensCaminhao.Include(x => x.Volumes).FirstOrDefaultAsync(x => x.Id == viagemId) ?? throw new ErroViagem("Viagem não encontrada.");
        if (v.Status == "fechada") throw new ErroViagem("Viagem fechada: reabra antes de mudar.");
        var c = await db.CarregamentosVolumes.FindAsync(carregamentoId) ?? throw new ErroViagem("Carregamento não encontrado.");
        var tabela = await precos.TabelaAsync();
        var avisos = new List<string>();
        db.ViagemCaminhaoVolumes.RemoveRange(v.Volumes);
        v.Volumes = ItensDe(c).Select((i, n) =>
        {
            var f = tabela.Fretes(i.cliente);
            if (f.Frete == null) avisos.Add($"{i.cliente}: cliente fora da tabela de frete, entrou com R$ 9,00 por caixa. Confira.");
            var cor = i.amarelas + i.brancas;
            return new ViagemCaminhaoVolumes
            {
                Ordem = n, Cliente = f.Frete?.Cliente ?? i.cliente, CaixasVolumes = i.volumes, CaixasProdutoE = i.produtoE, CaixasProdutoD = i.produtoD,
                ProdutosBAmarelas = i.amarelas, ProdutosBBrancas = i.brancas, ProdutosB = cor > 0 ? cor : i.produtosB, ProdutosC = i.produtosC,
                ValorCaixa = f.Caixa, ValorProdutoE = f.ProdutoE, ValorProdutoD = f.ProdutoD, ValorProdutoB = f.ProdutoB, ValorProdutoC = f.ProdutoC,
            };
        }).ToList();
        // Um carregamento por viagem: o que estava ligado antes se solta
        var antes = await db.CarregamentosVolumes.Where(x => x.ViagemId == viagemId && x.Id != carregamentoId).ToListAsync();
        foreach (var a in antes) a.ViagemId = null;
        c.ViagemId = viagemId;
        await db.SaveChangesAsync();
        return avisos;
    }

    public async Task<object> CarregamentosAsync(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1);
        var donos = await DonosDasPlacas.CarregarAsync(db);
        var lista = (await db.CarregamentosVolumes.AsNoTracking().Where(c => c.Data >= ini && c.Data < fim).OrderBy(c => c.Data).ThenBy(c => c.Placa).ToListAsync())
            .Where(c => donos.Dono(c.Placa) == DonosDasPlacas.Bento); // VIA-24: a aba mostra só as placas do Bento
        return lista.Select(c =>
        {
            var itens = ItensDe(c);
            return new
            {
                c.Id, c.Data, c.Placa, c.Motorista, c.Destino, c.Observacao, c.NomeArquivo, c.ViagemId,
                Itens = itens.Select(i => new { i.cliente, i.volumes, i.produtoE, i.produtoD, i.produtosB, i.amarelas, i.brancas, i.produtosC }),
                Volumes = itens.Sum(i => i.volumes), ProdutoE = itens.Sum(i => i.produtoE), ProdutoD = itens.Sum(i => i.produtoD),
                ProdutosB = itens.Sum(i => i.produtosB), Amarelas = itens.Sum(i => i.amarelas), Brancas = itens.Sum(i => i.brancas),
                ProdutosC = itens.Sum(i => i.produtosC),
                // Lido pela regra antiga (somava detalhe e totais): pede para ler de novo
                RegraAntiga = LidoPelaRegraAntiga(itens),
            };
        });
    }

    /// <summary>Apaga o carregamento e as saídas de produtosB e produtosC que nasceram dele (a viagem fica com os volumes).</summary>
    public async Task<List<string>> ExcluirCarregamentoAsync(int id)
    {
        var c = await db.CarregamentosVolumes.FindAsync(id) ?? throw new ErroViagem("Carregamento não encontrado.");
        var avisos = await vendas.ApagarDoCarregamentoAsync(id);
        var origem = OrigemCarregamento(id);
        db.VolumesControleLinhas.RemoveRange(await db.VolumesControleLinhas.Where(l => l.Origem == origem).ToListAsync());
        db.CarregamentosVolumes.Remove(c);
        await db.SaveChangesAsync();
        return avisos;
    }

    // ───────────────────────── Importar o acerto antigo ─────────────────────────

    /// <summary>
    /// Traz as abas VIAGEMn do acerto mensal do caminhão. Idempotente pela chave
    /// placa|ano-mês|aba (a viagem já trazida é refeita só com substituir). Os retornos
    /// da aba juntam com a linha da tela Retornos só com candidato único (VIA-32); sem par
    /// ou com vários, só aviso: o acerto nunca cria retorno.
    /// A sobra anterior da planilha entra como informada só quando não há viagem
    /// anterior do caminhão no sistema (começo da cadeia).
    /// </summary>
    /// <remarks>
    /// VIA-23 (Admin, 09/11/2026): viagem que já está no sistema (mesma chave, ou sem chave com a
    /// mesma placa e a mesma data de saída: lançada no painel ou no escritório) não é gravada de novo.
    /// Volta por viagem a situação IGUAL ou CONFIRMAR (com as diferenças sistema × arquivo) e só é
    /// trocada no lugar (mesmo Id, sem duplicar) quando a aba vem em <paramref name="abasSubstituir"/>
    /// (ou com <paramref name="substituir"/> sem lista = todas). Viagem nova entra direto.
    /// </remarks>
    public async Task<object> ImportarAcertoAsync(byte[] bytes, string nome, string? placaInformada, bool substituir,
        IReadOnlyCollection<string>? abasSubstituir = null)
    {
        var lido = AcertoCaminhaoLeitor.Ler(bytes, nome, placaInformada);
        var motoristas = await db.MotoristasFrota.AsNoTracking().ToListAsync();
        var tabela = await db.PrecosFreteCliente.AsNoTracking().ToListAsync();
        var avisos = new List<string>(lido.Avisos);
        var criadas = 0; var puladas = 0; var iguais = 0; var substituidas = 0; var completadas = 0;
        var conferencia = new List<object>(); var viagens = new List<object>();
        var trocar = abasSubstituir?.Select(NormalizarAba).ToHashSet();
        var placaArquivo = NormalizarPlaca(lido.Placa);
        var jaAchadas = new HashSet<int>();
        var motoristasDasAbas = new List<(string Aba, string Nome, MotoristaFrota? Mot)>();
        // VIA-25: o controle de carregamento liga depois de todas as abas gravadas (a viagem seguinte também conta)
        var ligarControle = new List<(ViagemCaminhao V, string Aba)>();

        // Antes: o que casa pela chave ou pela data fica reservado para a sua aba (a regra "provável"
        // de outra aba não pode tomar a viagem que é exatamente de uma aba deste arquivo).
        var reservadas = new Dictionary<string, int>();
        foreach (var a in lido.Viagens)
        {
            var (v0, g0, _) = await AcharViagemDoAcertoAsync(ChaveAcerto(lido, a.Aba), placaArquivo, a.Saida, a.Chegada, null, reservadas.Values.ToHashSet());
            if (v0 != null && g0 != ViagemCasamento.Provavel) reservadas[a.Aba] = v0.Id;
        }

        foreach (var a in lido.Viagens)
        {
            var chave = ChaveAcerto(lido, a.Aba);
            var mot = AcharMotorista(a.Motorista, motoristas);
            motoristasDasAbas.Add((a.Aba, a.Motorista, mot));
            var novo = MontarDoAcerto(a, lido, nome, mot, tabela, chave);
            // Diária que não fecha em meia (R$ 50): fica a quantidade quebrada e um aviso
            if (a.Diarias % 50 != 0) avisos.Add($"{a.Aba}: diárias de R$ {a.Diarias:N2} não fecham em meia diária.");

            var fora = jaAchadas.Concat(reservadas.Where(x => x.Key != a.Aba).Select(x => x.Value)).ToHashSet();
            var sinais = await SinaisDaAbaAsync(a, placaArquivo);
            var (existente, grau, motivo) = await AcharViagemDoAcertoAsync(chave, placaArquivo, a.Saida, a.Chegada, sinais, fora);
            if (motivo != null) avisos.Add($"{a.Aba}: {motivo}.");
            if (existente != null)
            {
                jaAchadas.Add(existente.Id);
                var retsSistema = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId == existente.Id).ToListAsync();
                // UI-22: o que a aba não traz fica como está no sistema (não entra na comparação nem na troca)
                ManterOQueAPlanilhaNaoTraz(novo, existente);
                var trocarRetornos = a.Retornos.Count > 0;
                var difs = CompararComAcerto(existente, retsSistema, novo, a);
                // VIA-32: o retorno não troca a viagem (vale a planilha de retornos); só junta com candidato único
                difs.RemoveAll(d => d.Campo == "Retornos");
                object Situacao(string s) => new
                {
                    aba = a.Aba, situacao = s, grau, viagemId = existente.Id, origem = existente.Origem, status = existente.Status,
                    saida = a.Saida,
                    diferencas = difs.Select(d => new { campo = d.Campo, sistema = d.Sistema, arquivo = d.Arquivo, completou = FaltavaNoSistema(d) }).ToList(),
                };
                if (grau == ViagemCasamento.Provavel)
                {
                    // Provável nunca grava sozinho: só com a aba escolhida na confirmação
                    difs.RemoveAll(d => d.Campo == "Saída");
                    difs.Insert(0, new("Data de saída", existente.DataSaida?.ToString("dd/MM/yyyy") ?? "(vazio)", a.Saida?.ToString("dd/MM/yyyy") ?? "(vazio)"));
                    if (trocar?.Contains(NormalizarAba(a.Aba)) != true) { puladas++; viagens.Add(Situacao("CONFIRMAR")); continue; }
                    await LiberarChaveAsync(chave, existente.Id, a.Aba, avisos);
                    await SubstituirPeloAcertoAsync(existente, novo, a, lido, avisos, trocarRetornos);
                    ligarControle.Add((existente, a.Aba));
                    substituidas++; viagens.Add(Situacao("SUBSTITUIDA"));
                    continue;
                }
                if (difs.Count == 0)
                {
                    // Casou pela data: grava a chave da aba (não muda valor; a próxima importação casa pela chave)
                    if (existente.ChaveImportacao != chave)
                    {
                        await LiberarChaveAsync(chave, existente.Id, a.Aba, avisos);
                        existente.ChaveImportacao = chave;
                        await db.SaveChangesAsync();
                    }
                    await LigarRetornosDoAcertoAsync(existente, a, lido, avisos);
                    iguais++; puladas++; viagens.Add(Situacao("IGUAL")); continue;
                }
                // UI-22 (Admin, 10/11/2026): casou pela chave ou pela data = preenche sozinho o que falta e
                // substitui sozinho o valor diferente, sem pedir confirmação; o resultado avisa cada troca.
                // PERGUNTA EM ABERTO ao Admin: viagem conferida/fechada no sistema também é substituída
                // (só com aviso). Se ele quiser proteger a fechada, é aqui que ela passa a pedir confirmação.
                var trocas = difs.Where(d => !FaltavaNoSistema(d)).ToList();
                foreach (var d in trocas)
                    avisos.Add($"{a.Aba}: já tinha {d.Campo} no sistema, substituído: {d.Sistema} → {d.Arquivo}.");
                if (trocas.Count > 0 && existente.Status is "fechada" or "conferida")
                    avisos.Add($"{a.Aba}: a viagem {existente.Id} estava {NomeSituacao(existente.Status)} no sistema e foi atualizada pela planilha.");
                await LiberarChaveAsync(chave, existente.Id, a.Aba, avisos);
                await SubstituirPeloAcertoAsync(existente, novo, a, lido, avisos, trocarRetornos);
                ligarControle.Add((existente, a.Aba));
                if (trocas.Count == 0) { completadas++; viagens.Add(Situacao("COMPLETADA")); }
                else { substituidas++; viagens.Add(Situacao("SUBSTITUIDA")); }
                continue;
            }

            // Começo da cadeia: sem viagem anterior do caminhão no sistema, vale a sobra da planilha
            if (!await TemAnteriorAsync(lido, a.Saida, null)) novo.SobraAnteriorInformada = a.SobraAnterior;
            await LiberarChaveAsync(chave, 0, a.Aba, avisos);
            db.ViagensCaminhao.Add(novo);
            await db.SaveChangesAsync();
            await LigarRetornosDoAcertoAsync(novo, a, lido, avisos);
            ligarControle.Add((novo, a.Aba));
            criadas++;
            jaAchadas.Add(novo.Id);
            viagens.Add(new { aba = a.Aba, situacao = "NOVA", grau = (string?)null, viagemId = novo.Id, origem = novo.Origem, status = novo.Status, saida = a.Saida, diferencas = new List<object>() });
        }

        // VIA-25: retornos livres do mês do acerto entram na viagem da placa pelo período
        var inicioAcerto = new DateOnly(lido.Ano, lido.Mes, 1);
        var ligadosPeriodo = await LigarRetornosPeloPeriodoAsync(inicioAcerto.AddDays(-7), inicioAcerto.AddMonths(1).AddDays(6));
        if (ligadosPeriodo > 0) avisos.Add($"{ligadosPeriodo} retorno(s) sem viagem entraram na viagem pela placa e pelo período.");
        // VIA-09/VIA-25: controle de carregamento de cada aba gravada; depois os soltos do mesmo intervalo
        foreach (var (vc, aba) in ligarControle) await LigarCarregamentoSemTrocarAsync(vc, aba, avisos);
        var avisosCarga = new List<string>();
        var ligadosCarga = await LigarCarregamentosPeloPeriodoAsync(inicioAcerto.AddDays(-7), inicioAcerto.AddMonths(1).AddDays(6), avisosCarga);
        // As dúvidas das viagens das abas já foram avisadas acima (com o nome da aba); as outras entram aqui
        var jaAvisadas = ligarControle.Where(x => x.V.DataSaida != null).Select(x => $"para a viagem de {x.V.DataSaida:dd/MM}").ToHashSet();
        avisos.AddRange(avisosCarga.Where(a => !a.Contains("controles possíveis") || !jaAvisadas.Any(a.Contains)));
        if (ligadosCarga > 0) avisos.Add($"{ligadosCarga} controle(s) de carregamento sem viagem entraram na viagem pela placa e pelo período.");

        // Confere: o que o sistema calcula contra os totais guardados na planilha
        var viagensMes = await ComTudo().AsNoTracking().Where(v => v.Placa == lido.Placa && v.AnoReferencia == lido.Ano && v.MesReferencia == lido.Mes).ToListAsync();
        var sobras = await SobrasAsync(viagensMes);
        var idsMes = viagensMes.Select(x => x.Id).ToList();
        var retsMes = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null && idsMes.Contains(r.ViagemId.Value)).ToListAsync();
        foreach (var a in lido.Viagens)
        {
            var chave = ChaveAcerto(lido, a.Aba);
            var v = viagensMes.FirstOrDefault(x => x.ChaveImportacao == chave);
            if (v == null) continue;
            var c = Calcular(v, retsMes.Where(r => r.ViagemId == v.Id), sobras.GetValueOrDefault(v.Id));
            var dif = new List<string>();
            void Ver(string campo, decimal sistema)
            {
                if (a.Conferencia.TryGetValue(campo, out var p) && p != null && Math.Abs(p.Value - sistema) > 0.01m)
                    dif.Add($"{campo}: planilha {p.Value:N2}, sistema {sistema:N2}");
            }
            Ver("TotalVolumes", c.TotalVolumes); Ver("TotalRetornos", c.TotalRetornos); Ver("ValorTotal", c.ValorTotal);
            Ver("TotalDespesas", c.TotalDespesas); Ver("Sobra", c.Sobra); Ver("Resultado", c.Resultado_); Ver("ComissaoPlanilha", c.ComissaoPlanilha);
            conferencia.Add(new
            {
                aba = a.Aba, viagemId = v.Id, confere = dif.Count == 0, diferencas = dif,
                comissao = c.Comissao, comissaoPlanilha = a.Conferencia.GetValueOrDefault("ComissaoPlanilha"), sobra = c.Sobra,
            });
        }
        var fechamento = await DespesasDoMesNoFechamentoAsync(lido, motoristasDasAbas, avisos);
        return new { arquivo = nome, placa = lido.Placa, mes = lido.Mes, ano = lido.Ano, criadas, puladas, iguais, substituidas, completadas, avisos, conferencia, viagens, fechamento };
    }

    /// <summary>
    /// VIA-29 (Admin, 10/11/2026): a aba DESPESAS DO MES entra sozinha no fechamento do motorista das
    /// abas VIAGEMn, no mês do acerto. Nenhum motorista reconhecido ou mais de um no arquivo: não entra
    /// e o resultado diz o motivo (lançar à mão). Erro ao gravar não desfaz as viagens: vira motivo.
    /// </summary>
    private async Task<object> DespesasDoMesNoFechamentoAsync(AcertoLido lido, List<(string Aba, string Nome, MotoristaFrota? Mot)> abas, List<string> avisos)
    {
        if (lido.DespesasDoMes is not { } dm)
            return FechamentoMensalService.DespesasNaoImportadas(null, null, lido.Ano, lido.Mes, "O arquivo não tem a aba DESPESAS DO MES.");
        if (dm.MesTexto != null && Normalizar(dm.MesTexto) != Normalizar(NomeMes(lido.Mes)) || dm.Ano != null && dm.Ano != lido.Ano)
            avisos.Add($"DESPESAS DO MES diz {dm.MesTexto} de {dm.Ano}; valeu o mês do acerto, {NomeMes(lido.Mes)} de {lido.Ano}.");

        var semNome = abas.Where(x => x.Mot == null).Select(x => $"{x.Aba} ({(x.Nome.Trim().Length > 0 ? x.Nome.Trim() : "sem nome")})").ToList();
        var mots = abas.Where(x => x.Mot != null).Select(x => x.Mot!).DistinctBy(m => m.Id).ToList();
        string? motivo = null;
        if (abas.Count == 0) motivo = "Nenhuma aba VIAGEM com data de saída: não sei de quem são as despesas do mês; lance no Fechamento do mês.";
        else if (semNome.Count > 0) motivo = $"Não reconheci o motorista de {string.Join(", ", semNome)}: as despesas do mês não entraram; lance no Fechamento do mês.";
        else if (mots.Count > 1) motivo = $"O arquivo tem viagens de {mots.Count} motoristas ({string.Join(", ", mots.Select(m => m.Nome))}): as despesas do mês não entraram sozinhas; lance no fechamento de cada um.";
        else if (!FechamentoMensalService.MotoristasDoAcerto.Contains(mots[0].Id)) motivo = $"{mots[0].Nome} não tem fechamento mensal: as despesas do mês não entraram.";
        var unico = mots.Count == 1 ? mots[0] : null;
        if (motivo != null)
            return FechamentoMensalService.DespesasNaoImportadas(unico?.Id, unico?.Nome, lido.Ano, lido.Mes, motivo);
        try
        {
            return await new FechamentoMensalService(db).ImportarDespesasDaPlanilhaAsync(unico!.Id, lido.Ano, lido.Mes, dm);
        }
        catch (Exception ex) when (ex is ErroViagem or InvalidOperationException or DbUpdateException)
        {
            return FechamentoMensalService.DespesasNaoImportadas(unico!.Id, unico.Nome, lido.Ano, lido.Mes, ex is ErroViagem ? "Erro ao gravar as despesas do mês: " + ex.Message
                    : "Não consegui gravar as despesas do mês no fechamento. Tente de novo ou lance pela tela do Fechamento.");
        }
    }

    private static string NormalizarAba(string aba) => aba.ToUpperInvariant().Replace(" ", "");
    private static string ChaveAcerto(AcertoLido lido, string aba) => $"{lido.Placa}|{lido.Ano}-{lido.Mes:00}|{NormalizarAba(aba)}";

    /// <summary>A viagem da aba como a importação grava (ainda fora do banco).</summary>
    private static ViagemCaminhao MontarDoAcerto(ViagemLida a, AcertoLido lido, string nome, MotoristaFrota? mot,
        List<PrecoFreteCliente> tabela, string chave) => new()
    {
        MotoristaId = mot?.Id, MotoristaNome = mot?.Nome ?? a.Motorista.Trim(), Placa = lido.Placa,
        PlacaCarreta = a.Carreta, Destino = a.Destino, DataSaida = a.Saida, DataChegada = a.Chegada,
        KmSaida = a.KmSaida, KmChegada = a.KmChegada, Litros = a.Litros, Levou = a.Levou, Devolveu = a.Devolveu,
        DiariasQuantidade = a.Diarias / ValorDiariaPadrao, ValorDiaria = ValorDiariaPadrao, PorcentagemComissao = a.Porcentagem,
        MesReferencia = lido.Mes, AnoReferencia = lido.Ano, Status = "fechada", Origem = "importacao",
        ChaveImportacao = chave, Observacoes = $"Importada de {nome} ({a.Aba}).",
        Volumes = a.Volumes.Select((o, i) =>
        {
            var p = AcharCliente(o.Cliente, tabela);
            return new ViagemCaminhaoVolumes
            {
                Ordem = i, Cliente = p?.Cliente ?? o.Cliente.Trim(), CaixasVolumes = o.Volumes, CaixasProdutoE = o.ProdutoE,
                CaixasProdutoD = o.ProdutoD, ProdutosB = o.ProdutosB, ValorCaixa = o.ValorCaixa, ValorProdutoD = o.ValorProdutoD, ValorProdutoB = 10,
            };
        }).ToList(),
        Abastecimentos = a.Abastecimentos.Select((x, i) => new ViagemCaminhaoAbastecimento { Ordem = i, Valor = x.Valor, Forma = x.Forma, Local = x.Observacao }).ToList(),
        Despesas = a.Despesas.Select((d, i) => new ViagemCaminhaoDespesa { Ordem = i, Tipo = d.Tipo, Descricao = d.Descricao, Valor = d.Valor, Observacao = d.Observacao }).ToList(),
    };

    /// <summary>
    /// UI-22 (Admin, 10/11/2026): campo vazio na planilha mantém o do sistema. Põe em <paramref name="novo"/>
    /// (a aba montada) o valor do sistema onde a aba não traz nada; assim a comparação e a troca só
    /// mexem no que a planilha de fato tem. Zero em levou/diárias/% conta como vazio (célula em branco).
    /// </summary>
    private static void ManterOQueAPlanilhaNaoTraz(ViagemCaminhao novo, ViagemCaminhao s)
    {
        if (novo.MotoristaId == null && string.IsNullOrWhiteSpace(novo.MotoristaNome)) { novo.MotoristaId = s.MotoristaId; novo.MotoristaNome = s.MotoristaNome; }
        if (string.IsNullOrWhiteSpace(novo.PlacaCarreta)) novo.PlacaCarreta = s.PlacaCarreta;
        if (string.IsNullOrWhiteSpace(novo.Destino)) novo.Destino = s.Destino;
        novo.DataChegada ??= s.DataChegada;
        novo.KmSaida ??= s.KmSaida;
        novo.KmChegada ??= s.KmChegada;
        novo.Litros ??= s.Litros;
        if (novo.Levou == 0) novo.Levou = s.Levou;
        if (novo.DiariasQuantidade == 0) { novo.DiariasQuantidade = s.DiariasQuantidade; novo.ValorDiaria = s.ValorDiaria; }
        if (novo.PorcentagemComissao == 0) novo.PorcentagemComissao = s.PorcentagemComissao;
        if (novo.Volumes.Count == 0)
            novo.Volumes = s.Volumes.Select(o => new ViagemCaminhaoVolumes
            {
                Ordem = o.Ordem, Cliente = o.Cliente, CaixasVolumes = o.CaixasVolumes, CaixasProdutoE = o.CaixasProdutoE, CaixasProdutoD = o.CaixasProdutoD,
                ProdutosB = o.ProdutosB, ProdutosBAmarelas = o.ProdutosBAmarelas, ProdutosBBrancas = o.ProdutosBBrancas, ProdutosC = o.ProdutosC,
                ValorCaixa = o.ValorCaixa, ValorProdutoE = o.ValorProdutoE, ValorProdutoD = o.ValorProdutoD, ValorProdutoB = o.ValorProdutoB, ValorProdutoC = o.ValorProdutoC,
            }).ToList();
        if (novo.Abastecimentos.Count == 0)
            novo.Abastecimentos = s.Abastecimentos.Select(x => new ViagemCaminhaoAbastecimento
            {
                Ordem = x.Ordem, Data = x.Data, Km = x.Km, Litros = x.Litros, Valor = x.Valor, Local = x.Local, Observacao = x.Observacao, Forma = x.Forma,
            }).ToList();
        if (novo.Despesas.Count == 0)
            novo.Despesas = s.Despesas.Select(d => new ViagemCaminhaoDespesa
            {
                Ordem = d.Ordem, Tipo = d.Tipo, Descricao = d.Descricao, Valor = d.Valor, Observacao = d.Observacao,
            }).ToList();
    }

    /// <summary>A diferença é "falta no sistema" (o sistema não tinha nada) e não um valor trocado.</summary>
    private static bool FaltavaNoSistema(DiferencaAcerto d) =>
        d.Sistema is "(vazio)" or "(não tem)" or "(nenhum)" || d.Sistema.StartsWith("R$ 0,00");

    /// <summary>
    /// Viagem do sistema que é a da aba, pela regra única de casamento (<see cref="ViagemCasamento"/>):
    /// candidatas = a da chave e as da placa com saída a até 7 dias da saída da aba.
    /// </summary>
    private async Task<(ViagemCaminhao? Viagem, string? Grau, string? Motivo)> AcharViagemDoAcertoAsync(string chave, string placa,
        DateOnly? saida, DateOnly? chegada, SinaisViagem? sinais, IReadOnlySet<int> jaAchadas)
    {
        var cands = await CandidatasDeCasamentoAsync(placa, saida, chave);
        return ViagemCasamento.Achar(placa, saida, chegada, chave, sinais, cands, jaAchadas);
    }

    /// <summary>
    /// A chave (placa|mês|aba) é única no banco. Quando outra viagem a tem mas não é a desta aba
    /// (a aba mudou de número na planilha: saída longe), ela perde a chave, com aviso, e a chave
    /// passa para a viagem que é de fato a da aba.
    /// </summary>
    private async Task LiberarChaveAsync(string chave, int dono, string aba, List<string> avisos)
    {
        var outras = await db.ViagensCaminhao.Where(v => v.ChaveImportacao == chave && v.Id != dono).ToListAsync();
        if (outras.Count == 0) return;
        foreach (var o in outras)
        {
            o.ChaveImportacao = null;
            avisos.Add($"{aba}: a viagem {o.Id} (saída {o.DataSaida:dd/MM}) tinha a chave desta aba e a perdeu; confira se ela é de outra aba.");
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Viagens da placa com saída a até 7 dias (mais a da chave, se houver).</summary>
    private async Task<List<ViagemCaminhao>> CandidatasDeCasamentoAsync(string placa, DateOnly? saida, string? chave)
    {
        var lista = new List<ViagemCaminhao>();
        if (saida != null)
        {
            var ini = saida.Value.AddDays(-7); var fim = saida.Value.AddDays(7);
            lista = (await ComTudo().Where(v => v.DataSaida >= ini && v.DataSaida <= fim).ToListAsync())
                .Where(v => NormalizarPlaca(v.Placa) == placa).ToList();
        }
        if (chave != null && lista.All(v => v.ChaveImportacao != chave)
            && await ComTudo().FirstOrDefaultAsync(v => v.ChaveImportacao == chave) is { } pc)
            lista.Add(pc);
        return lista;
    }

    /// <summary>Sinais da aba para a regra "provável": abastecimentos, caixas e viagens com controle do período.</summary>
    private async Task<SinaisViagem> SinaisDaAbaAsync(ViagemLida a, string placa)
    {
        var ligadas = new HashSet<int>();
        if (a.Saida is { } s)
        {
            var ini = s.AddDays(-1); var fim = a.Chegada ?? s.AddDays(1);
            ligadas = (await db.CarregamentosVolumes.AsNoTracking().Where(c => c.ViagemId != null && c.Data >= ini && c.Data <= fim)
                .Select(c => new { c.Placa, c.ViagemId }).ToListAsync())
                .Where(c => NormalizarPlaca(c.Placa) == placa).Select(c => c.ViagemId!.Value).ToHashSet();
        }
        return new SinaisViagem(a.Abastecimentos.Select(x => x.Valor).ToList(), a.Volumes.Sum(o => o.Volumes + o.ProdutoE + o.ProdutoD), ligadas);
    }

    /// <summary>
    /// VIA-09/VIA-24: a viagem que veio do acerto (nova ou substituída) e ainda não tem controle de
    /// carregamento ganha o controle solto da mesma placa (só placas do Bento) pela janela de
    /// CarregamentoPeriodoLigador (saída − 2 até o fim do período; VIA-25). Só LIGA (ViagemId): os volumes da
    /// planilha não mudam. Mais de um possível: não liga e avisa.
    /// </summary>
    private async Task LigarCarregamentoSemTrocarAsync(ViagemCaminhao v, string aba, List<string> avisos)
    {
        if (v.DataSaida == null) return;
        var donos = await DonosDasPlacas.CarregarAsync(db);
        if (donos.Dono(v.Placa) != DonosDasPlacas.Bento) return;
        var meus = new List<string>();
        var escolhido = await CarregamentoDaViagemAsync(v, meus);
        var trechoViagem = $"para a viagem de {v.DataSaida:dd/MM}";
        avisos.AddRange(meus.Select(a => a.Contains(trechoViagem) ? a.Replace(trechoViagem, $"para a {aba}") : $"{aba}: {a}"));
        if (escolhido == null) return;
        escolhido.ViagemId = v.Id;
        await db.SaveChangesAsync();
    }

    private async Task<bool> TemAnteriorAsync(AcertoLido lido, DateOnly? saida, int? fora) =>
        await db.ViagensCaminhao.AnyAsync(x => x.Placa == lido.Placa && x.Id != (fora ?? 0) &&
            (x.AnoReferencia < lido.Ano || x.AnoReferencia == lido.Ano && x.MesReferencia < lido.Mes
             || x.AnoReferencia == lido.Ano && x.MesReferencia == lido.Mes && x.DataSaida < saida));

    /// <summary>
    /// VIA-32: cada retorno da aba junta com a linha da planilha de retornos só com candidato único
    /// (<see cref="RetornoAcertoCasamento"/>); a linha que já está nesta viagem (inclusive a ligada à mão) fica.
    /// Dois ou mais candidatos ou nenhum: só aviso. O acerto nunca cria retorno; vale o valor da planilha.
    /// </summary>
    private async Task<HashSet<int>> LigarRetornosDoAcertoAsync(ViagemCaminhao v, ViagemLida a, AcertoLido lido, List<string> avisos)
    {
        var usados = new HashSet<int>();
        if (a.Retornos.Count == 0) return usados;
        // A aba não traz data do retorno: vale a saída da viagem
        var data = a.Saida ?? v.DataSaida ?? new DateOnly(lido.Ano, lido.Mes, 1);
        var ini = data.AddDays(-RetornoAcertoCasamento.Dias); var fim = data.AddDays(RetornoAcertoCasamento.Dias);
        var placa = v.Placa ?? lido.Placa;
        var linhas = await db.RetornosFrete.Where(x => x.ViagemId == v.Id || x.ViagemId == null && x.Data >= ini && x.Data <= fim).ToListAsync();
        var modelo = linhas.Select(x => new RetornoAcertoCasamento.Linha(x.Id, x.Placa, x.Transportadora, x.Data, x.ValorFrete, x.Adicional,
            x.FreteTotal, x.ViagemId, x.ViagemLigacao, x.ForaDoControle, x.ValorContrato)).ToList();
        foreach (var r in a.Retornos)
        {
            var res = RetornoAcertoCasamento.Escolher(r.Valor, r.Transportadora, data, placa, v.PlacaCarreta, v.Id, modelo, usados);
            if (res.Escolhida is { } e)
            {
                usados.Add(e.Id);
                var par = linhas.First(x => x.Id == e.Id);
                if (!res.JaNaViagem)
                {
                    par.ViagemId = v.Id;
                    par.ViagemLigacao = ViagemPeriodoLigador.Auto;
                    if (v.MotoristaId != null) par.MotoristaId = v.MotoristaId;
                    par.AtualizadoEm = DateTime.UtcNow;
                }
                if (r.PorcentagemIcms != 0) par.PorcentagemIcms = r.PorcentagemIcms;
            }
            else if (res.Candidatos.Count > 1)
                avisos.Add($"{a.Aba}: retorno de R$ {r.Valor:N2} cabe em {res.Candidatos.Count} retornos ({string.Join(", ", res.Candidatos.Select(c => $"{c.Data:dd/MM} R$ {c.ValorFrete + c.Adicional:N2}"))}): escolha na viagem.");
            else
                avisos.Add($"{a.Aba}: retorno de R$ {r.Valor:N2} da planilha do acerto não está nos Retornos.");
        }
        // Retorno criado por importação antiga do acerto (antes da VIA-32): fica, mas avisa para conferir a dobra
        foreach (var x in linhas.Where(x => x.ViagemId == v.Id && x.ForaDoControle && x.Origem == "importacao"))
            avisos.Add($"{a.Aba}: a viagem tem o retorno R$ {x.FreteTotal:N2} criado por importação antiga do acerto (fora do controle); se repete um da planilha de retornos, apague-o à mão.");
        await db.SaveChangesAsync();
        return usados;
    }

    /// <summary>
    /// Troca a viagem pela aba no lugar (mesmo Id): cabeçalho, volumes, abastecimentos, despesas e
    /// retornos. Ficam as anotações do motorista (cargas), o carregamento ligado, a situação e a
    /// origem. Retorno ligado a uma anotação do motorista continua na viagem mesmo sem par na aba.
    /// </summary>
    private async Task SubstituirPeloAcertoAsync(ViagemCaminhao v, ViagemCaminhao novo, ViagemLida a, AcertoLido lido, List<string> avisos,
        bool trocarRetornos = true)
    {
        if (novo.MotoristaId != null || v.MotoristaId == null) { v.MotoristaId = novo.MotoristaId; v.MotoristaNome = novo.MotoristaNome; }
        v.Placa = novo.Placa; v.PlacaCarreta = novo.PlacaCarreta; v.Destino = novo.Destino;
        v.DataSaida = novo.DataSaida; v.DataChegada = novo.DataChegada; v.KmSaida = novo.KmSaida; v.KmChegada = novo.KmChegada;
        v.Litros = novo.Litros; v.Levou = novo.Levou; v.Devolveu = novo.Devolveu;
        v.DiariasQuantidade = novo.DiariasQuantidade; v.ValorDiaria = novo.ValorDiaria; v.PorcentagemComissao = novo.PorcentagemComissao;
        v.MesReferencia = novo.MesReferencia; v.AnoReferencia = novo.AnoReferencia; v.ChaveImportacao = novo.ChaveImportacao;
        v.Observacoes = string.Join(" ", new[] { v.Observacoes, $"Substituída pela planilha em {DateTime.Now:dd/MM/yyyy}: {novo.Observacoes}" }.Where(x => !string.IsNullOrWhiteSpace(x)));
        v.AtualizadoEm = DateTime.UtcNow;
        if (!await TemAnteriorAsync(lido, a.Saida, v.Id)) v.SobraAnteriorInformada = a.SobraAnterior;

        db.RemoveRange(v.Volumes); v.Volumes.Clear(); v.Volumes.AddRange(novo.Volumes);
        db.RemoveRange(v.Abastecimentos); v.Abastecimentos.Clear(); v.Abastecimentos.AddRange(novo.Abastecimentos);
        db.RemoveRange(v.Despesas); v.Despesas.Clear(); v.Despesas.AddRange(novo.Despesas);
        // Aba sem retorno nenhum (UI-22): os retornos da viagem ficam como estão
        if (!trocarRetornos) { await db.SaveChangesAsync(); return; }

        // Retornos (VIA-32): os já ligados à viagem ficam (inclusive o escolhido à mão e o anotado pelo
        // motorista, VIA-22); a aba só junta a linha livre da planilha de retornos com candidato único.
        await db.SaveChangesAsync();
        var anotados = v.Cargas.Where(c => c.RetornoId != null).Select(c => c.RetornoId!.Value).ToHashSet();
        var ligados = await db.RetornosFrete.Where(r => r.ViagemId == v.Id).ToListAsync();
        var usados = await LigarRetornosDoAcertoAsync(v, a, lido, avisos);
        foreach (var id in anotados.Where(id => !usados.Contains(id)))
            if (ligados.FirstOrDefault(r => r.Id == id) is { } r)
                avisos.Add($"{a.Aba}: retorno {r.Transportadora} R$ {r.FreteTotal:N2} anotado pelo motorista não está na aba; continua na viagem.");
    }

    public record DiferencaAcerto(string Campo, string Sistema, string Arquivo);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Diferenças campo a campo entre a viagem do sistema e a aba do arquivo (VIA-23).</summary>
    public static List<DiferencaAcerto> CompararComAcerto(ViagemCaminhao s, List<RetornoFrete> retsSistema, ViagemCaminhao f, ViagemLida a)
    {
        var d = new List<DiferencaAcerto>();
        static string Tx(string? x) => string.IsNullOrWhiteSpace(x) ? "(vazio)" : x.Trim();
        static string Dt(DateOnly? x) => x?.ToString("dd/MM/yyyy") ?? "(vazio)";
        static string Rs(decimal x) => "R$ " + x.ToString("N2", PtBr);
        static string Nm(decimal x) => x.ToString(x % 1 == 0 ? "N0" : "N2", PtBr);
        static string Km(int? x) => x?.ToString("N0", PtBr) ?? "(vazio)";
        static bool Dif(decimal x, decimal y) => Math.Abs(x - y) > 0.005m;
        static string Chave(string? x) => (x ?? "").Trim().ToUpperInvariant();
        static string Tipo(string t) => PtBr.TextInfo.ToTitleCase(t.ToLowerInvariant().Replace('_', ' '));

        var mesmoMotorista = s.MotoristaId != null && f.MotoristaId != null ? s.MotoristaId == f.MotoristaId : Chave(s.MotoristaNome) == Chave(f.MotoristaNome);
        if (!mesmoMotorista) d.Add(new("Motorista", Tx(s.MotoristaNome), Tx(f.MotoristaNome)));
        if (NormalizarPlaca(s.PlacaCarreta) != NormalizarPlaca(f.PlacaCarreta)) d.Add(new("Carreta", Tx(s.PlacaCarreta), Tx(f.PlacaCarreta)));
        if (Chave(s.Destino) != Chave(f.Destino)) d.Add(new("Destino", Tx(s.Destino), Tx(f.Destino)));
        if (s.DataSaida != f.DataSaida) d.Add(new("Saída", Dt(s.DataSaida), Dt(f.DataSaida)));
        if (s.DataChegada != f.DataChegada) d.Add(new("Chegada", Dt(s.DataChegada), Dt(f.DataChegada)));
        if (s.KmSaida != f.KmSaida) d.Add(new("Km na saída", Km(s.KmSaida), Km(f.KmSaida)));
        if (s.KmChegada != f.KmChegada) d.Add(new("Km na chegada", Km(s.KmChegada), Km(f.KmChegada)));
        if (Dif(s.Levou, f.Levou)) d.Add(new("Dinheiro levado", Rs(s.Levou), Rs(f.Levou)));
        if (Dif(s.Devolveu, f.Devolveu)) d.Add(new("Devolveu", Rs(s.Devolveu), Rs(f.Devolveu)));
        if (Dif(ValorDiarias(s), ValorDiarias(f)))
            d.Add(new("Diárias", $"{Rs(ValorDiarias(s))} ({Nm(s.DiariasQuantidade)})", $"{Rs(ValorDiarias(f))} ({Nm(f.DiariasQuantidade)})"));
        if (Dif(s.PorcentagemComissao, f.PorcentagemComissao)) d.Add(new("Comissão (%)", Nm(s.PorcentagemComissao), Nm(f.PorcentagemComissao)));

        // Volumes por cliente
        static string Linha(List<ViagemCaminhaoVolumes> os)
        {
            if (os.Count == 0) return "(não tem)";
            var p = new List<string> { $"{Nm(os.Sum(o => o.CaixasVolumes))} caixas" };
            if (os.Sum(o => o.CaixasProdutoE) != 0) p.Add($"{Nm(os.Sum(o => o.CaixasProdutoE))} produtoE");
            if (os.Sum(o => o.CaixasProdutoD) != 0) p.Add($"{Nm(os.Sum(o => o.CaixasProdutoD))} produtoD");
            if (os.Sum(o => o.ProdutosB) != 0) p.Add($"{Nm(os.Sum(o => o.ProdutosB))} produtosB");
            if (os.Sum(o => o.ProdutosC) != 0) p.Add($"{Nm(os.Sum(o => o.ProdutosC))} produtosC");
            return string.Join(" · ", p);
        }
        var clientes = s.Volumes.Select(o => Chave(o.Cliente)).Concat(f.Volumes.Select(o => Chave(o.Cliente))).Distinct();
        foreach (var c in clientes)
        {
            var os = s.Volumes.Where(o => Chave(o.Cliente) == c).ToList();
            var of = f.Volumes.Where(o => Chave(o.Cliente) == c).ToList();
            var igual = !Dif(os.Sum(o => o.CaixasVolumes), of.Sum(o => o.CaixasVolumes)) && !Dif(os.Sum(o => o.CaixasProdutoE), of.Sum(o => o.CaixasProdutoE))
                && !Dif(os.Sum(o => o.CaixasProdutoD), of.Sum(o => o.CaixasProdutoD)) && !Dif(os.Sum(o => o.ProdutosB), of.Sum(o => o.ProdutosB))
                && !Dif(os.Sum(o => o.ProdutosC), of.Sum(o => o.ProdutosC));
            if (!igual)
                d.Add(new($"Volumes: {(os.FirstOrDefault() ?? of[0]).Cliente}", Linha(os), Linha(of)));
        }
        var freteS = s.Volumes.Sum(TotalVolumes); var freteF = f.Volumes.Sum(TotalVolumes);
        if (Dif(freteS, freteF)) d.Add(new("Frete dos volumes", Rs(freteS), Rs(freteF)));

        // Abastecimentos
        foreach (var (forma, rot) in new[] { ("NOTA", "Diesel na nota"), ("PAGO", "Diesel pago") })
        {
            var xs = s.Abastecimentos.Where(x => (x.Forma == "PAGO") == (forma == "PAGO")).ToList();
            var xf = f.Abastecimentos.Where(x => (x.Forma == "PAGO") == (forma == "PAGO")).ToList();
            if (Dif(xs.Sum(x => x.Valor), xf.Sum(x => x.Valor)) || xs.Count != xf.Count)
                d.Add(new(rot, $"{Rs(xs.Sum(x => x.Valor))} ({xs.Count} {(xs.Count == 1 ? "linha" : "linhas")})", $"{Rs(xf.Sum(x => x.Valor))} ({xf.Count} {(xf.Count == 1 ? "linha" : "linhas")})"));
        }

        // Despesas por tipo
        foreach (var t in s.Despesas.Select(x => x.Tipo).Concat(f.Despesas.Select(x => x.Tipo)).Distinct())
        {
            var vs = s.Despesas.Where(x => x.Tipo == t).Sum(x => x.Valor);
            var vf = f.Despesas.Where(x => x.Tipo == t).Sum(x => x.Valor);
            if (Dif(vs, vf)) d.Add(new($"Despesa: {Tipo(t)}", Rs(vs), Rs(vf)));
        }

        // Retornos (frete total de cada linha)
        var rs = retsSistema.Select(r => Math.Round(r.FreteTotal, 2)).OrderBy(x => x).ToList();
        var rf = a.Retornos.Select(r => Math.Round(r.Valor, 2)).OrderBy(x => x).ToList();
        if (!rs.SequenceEqual(rf))
        {
            static string Rets(List<decimal> xs) => xs.Count == 0 ? "(nenhum)" : $"{xs.Count} · {Rs(xs.Sum())} ({string.Join("; ", xs.Select(Rs))})";
            d.Add(new("Retornos", Rets(rs), Rets(rf)));
        }
        return d;
    }

    // ───────────────────────── Viagens duplicadas (juntar) ─────────────────────────

    private static int Nivel(string? status) => status switch { "fechada" => 2, "conferida" => 1, _ => 0 };
    private static bool DaPlanilha(ViagemCaminhao v) => v.Origem == "importacao";
    private static string? ArquivoDaChave(string? chave) => chave is { } c && c.LastIndexOf('|') is var i and > 0 ? c[..i] : null;
    private static string? AbaDaChave(string? chave) => chave is { } c && c.LastIndexOf('|') is var i and > 0 ? c[(i + 1)..] : null;
    private static int QuantoTem(ViagemCaminhao v) => v.Volumes.Count + v.Abastecimentos.Count + v.Despesas.Count + v.Cargas.Count
        + (v.DataChegada != null ? 1 : 0) + (v.Levou != 0 ? 1 : 0);

    /// <summary>
    /// S4 (docs/planos/importacoes-mesclar.md): vazia = sem volumes, sem retorno ligado, sem abastecimento,
    /// sem despesa e levou 0. Só sai pedida pelo id, com confirmação.
    /// </summary>
    private static bool Vazia(ViagemCaminhao v, IReadOnlySet<int> comRetorno) =>
        v.Volumes.Count == 0 && v.Abastecimentos.Count == 0 && v.Despesas.Count == 0 && v.Levou == 0 && !comRetorno.Contains(v.Id);

    /// <summary>
    /// Qual das duas fica (S1): a que NÃO veio da planilha (mantém Id, anotações, controle e origem).
    /// As duas da planilha ou nenhuma: fica a que tem mais dados (empate: a mais antiga).
    /// Valores: os da planilha; nenhuma (ou as duas) da planilha: os da que fica.
    /// </summary>
    private static (ViagemCaminhao Fica, ViagemCaminhao Sai, bool ValoresDaQueSai) QualFica(ViagemCaminhao a, ViagemCaminhao b)
    {
        if (DaPlanilha(a) != DaPlanilha(b)) return DaPlanilha(a) ? (b, a, true) : (a, b, true);
        var fica = QuantoTem(a) != QuantoTem(b) ? (QuantoTem(a) > QuantoTem(b) ? a : b) : (a.Id < b.Id ? a : b);
        return (fica, fica == a ? b : a, false);
    }

    /// <summary>
    /// Prévia (só leitura) das viagens repetidas já gravadas: pares por placa pela regra de junção
    /// (grau "data" = mesma saída; "provavel" = saída a até 3 dias ou períodos que se cruzam, uma só
    /// candidata e um sinal igual) e as viagens vazias (S4). Duas com chave do mesmo arquivo não formam
    /// par; vazia não entra em par (aparece só como vazia).
    /// </summary>
    public async Task<object> DuplicadasAsync()
    {
        var todas = await ComTudo().AsNoTracking().ToListAsync();
        var rets = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null).ToListAsync();
        var carregs = await db.CarregamentosVolumes.AsNoTracking().Where(c => c.ViagemId != null).ToListAsync();
        var comRetorno = rets.Select(r => r.ViagemId!.Value).ToHashSet();
        var vazias = todas.Where(v => Vazia(v, comRetorno)).ToList();
        var idsVazias = vazias.Select(v => v.Id).ToHashSet();

        var pares = new List<object>();
        foreach (var g in todas.Where(v => v.DataSaida != null && !idsVazias.Contains(v.Id)).GroupBy(v => NormalizarPlaca(v.Placa)))
        {
            var lista = g.OrderBy(v => v.DataSaida).ThenBy(v => v.Id).ToList();
            var usados = new HashSet<int>();
            bool PodeSerPar(ViagemCaminhao x, ViagemCaminhao y) => x.Id != y.Id
                && !(x.ChaveImportacao != null && y.ChaveImportacao != null && ArquivoDaChave(x.ChaveImportacao) == ArquivoDaChave(y.ChaveImportacao));
            var achados = new List<(ViagemCaminhao A, ViagemCaminhao B, string Grau, string? Sinal)>();

            // Grau "data": mesma saída
            foreach (var x in lista)
            {
                if (usados.Contains(x.Id)) continue;
                var y = lista.Where(o => !usados.Contains(o.Id) && o.DataSaida == x.DataSaida && PodeSerPar(x, o))
                    .OrderBy(o => DaPlanilha(o) != DaPlanilha(x) ? 0 : 1).ThenBy(o => o.Id).FirstOrDefault();
                if (y == null) continue;
                usados.Add(x.Id); usados.Add(y.Id);
                achados.Add((x, y, ViagemCasamento.Data, null));
            }

            // Grau "provavel": perto, uma só candidata dos dois lados e um sinal igual
            bool Perto(ViagemCaminhao x, ViagemCaminhao y) =>
                Math.Abs(x.DataSaida!.Value.DayNumber - y.DataSaida!.Value.DayNumber) <= ViagemCasamento.DiasPerto
                || x.DataSaida!.Value <= (y.DataChegada ?? y.DataSaida!.Value) && (x.DataChegada ?? x.DataSaida!.Value) >= y.DataSaida!.Value;
            List<ViagemCaminhao> Vizinhas(ViagemCaminhao x) => lista.Where(o => !usados.Contains(o.Id) && PodeSerPar(x, o) && Perto(x, o)).ToList();
            SinaisViagem Sinais(ViagemCaminhao x)
            {
                var ini = x.DataSaida!.Value.AddDays(-1); var fim = x.DataChegada ?? x.DataSaida!.Value.AddDays(1);
                var ligadas = carregs.Where(c => c.Data >= ini && c.Data <= fim && NormalizarPlaca(c.Placa) == g.Key).Select(c => c.ViagemId!.Value).ToHashSet();
                return new(x.Abastecimentos.Select(a => a.Valor).ToList(), x.Volumes.Sum(o => o.CaixasVolumes + o.CaixasProdutoE + o.CaixasProdutoD), ligadas);
            }
            foreach (var x in lista)
            {
                if (usados.Contains(x.Id)) continue;
                var viz = Vizinhas(x);
                if (viz.Count != 1) continue;
                var y = viz[0];
                if (Vizinhas(y).Count != 1) continue;
                var sinal = ViagemCasamento.SinalIgual(y, Sinais(x)) ?? ViagemCasamento.SinalIgual(x, Sinais(y));
                if (sinal == null) continue;
                usados.Add(x.Id); usados.Add(y.Id);
                achados.Add((x, y, ViagemCasamento.Provavel, sinal));
            }

            foreach (var (a, b, grau, sinal) in achados)
            {
                var (fica, sai, valoresDaQueSai) = QualFica(a, b);
                var retsFica = rets.Where(r => r.ViagemId == fica.Id).ToList();
                var retsSai = rets.Where(r => r.ViagemId == sai.Id).ToList();
                var cFica = carregs.Where(c => c.ViagemId == fica.Id).ToList();
                var cSai = carregs.Where(c => c.ViagemId == sai.Id).ToList();
                var difs = CompararViagens(fica, retsFica, sai, retsSai);
                pares.Add(new
                {
                    placa = g.Key, grau, sinal,
                    manterId = fica.Id, removerId = sai.Id,
                    valoresDe = valoresDaQueSai ? sai.Id : fica.Id,
                    motivo = valoresDaQueSai
                        ? $"Fica a viagem {fica.Id} ({NomeOrigem(fica.Origem)}) com os valores da planilha (viagem {sai.Id})."
                        : $"Nenhuma (ou as duas) da planilha: fica a viagem {fica.Id}, que tem mais dados, com os valores dela.",
                    situacaoFinal = Nivel(sai.Status) > Nivel(fica.Status) ? sai.Status : fica.Status,
                    fica = ResumoDuplicada(fica, retsFica, cFica),
                    sai = ResumoDuplicada(sai, retsSai, cSai),
                    diferencas = difs.Select(d => new { campo = d.Campo, fica = d.Sistema, sai = d.Arquivo }).ToList(),
                    mudamDeViagem = new
                    {
                        retornos = retsSai.Select(r => new
                        {
                            r.Id, r.Data, r.Transportadora, freteTotal = r.FreteTotal,
                            sai = r.ForaDoControle && retsFica.Any(f => Math.Abs(f.FreteTotal - r.FreteTotal) < 0.01m),
                        }).ToList(),
                        carregamento = cSai.Select(c => new { c.Id, c.Data, vaiPara = cFica.Count == 0 ? "fica" : "solto" }).ToList(),
                        anotacoes = sai.Cargas.Where(c => !fica.Cargas.Any(f => MesmaAnotacao(f, c)))
                            .Select(c => new { c.Id, c.Tipo, c.Data, c.Transportadora, c.Valor }).ToList(),
                    },
                });
            }
        }

        return new
        {
            pares,
            vazias = vazias.OrderBy(v => v.Placa).ThenBy(v => v.DataSaida).Select(v => new
            {
                v.Id, v.Placa, v.MotoristaNome, v.DataSaida, v.DataChegada, v.Origem, v.Status,
                anotacoes = v.Cargas.Count, carregamento = carregs.Any(c => c.ViagemId == v.Id),
            }).ToList(),
        };
    }

    private static string NomeOrigem(string o) => o switch { "motorista" => "do motorista", "importacao" => "da planilha", _ => "do escritório" };

    private static object ResumoDuplicada(ViagemCaminhao v, List<RetornoFrete> rets, List<CarregamentoVolumes> carregs) => new
    {
        v.Id, v.Placa, v.MotoristaNome, v.Origem, v.Status, v.DataSaida, v.DataChegada, v.Destino, v.ChaveImportacao,
        aba = AbaDaChave(v.ChaveImportacao), v.Levou,
        caixas = v.Volumes.Sum(o => o.CaixasVolumes + o.CaixasProdutoE + o.CaixasProdutoD),
        abastecimentos = v.Abastecimentos.Sum(a => a.Valor), despesas = v.Despesas.Sum(d => d.Valor),
        retornos = rets.Count, anotacoes = v.Cargas.Count, carregamentos = carregs.Select(c => c.Data).ToList(),
    };

    /// <summary>Diferenças campo a campo entre as duas viagens do par (formato de <see cref="DiferencaAcerto"/>).</summary>
    private static List<DiferencaAcerto> CompararViagens(ViagemCaminhao fica, List<RetornoFrete> retsFica, ViagemCaminhao sai, List<RetornoFrete> retsSai)
    {
        var lida = new ViagemLida("", 0, sai.MotoristaNome, null, sai.PlacaCarreta, sai.Destino, sai.DataSaida, sai.DataChegada, sai.KmSaida, sai.KmChegada,
            sai.Litros, 0, sai.Levou, sai.Devolveu, ValorDiarias(sai), sai.PorcentagemComissao, [], [], [],
            retsSai.Select(r => new RetornoDaViagem(r.Transportadora, r.FreteTotal, r.PorcentagemIcms, r.Destino)).ToList(), []);
        return CompararComAcerto(fica, retsFica, sai, lida);
    }

    /// <summary>
    /// Junta duas viagens que são a mesma (docs/planos/importacoes-mesclar.md, Parte 4), numa transação.
    /// A que fica recebe da que sai (quando a que sai é a da planilha) chave, mês/ano, destino, datas, km,
    /// litros, levou, devolveu, diárias, %, sobra anterior informada, volumes, abastecimentos e despesas;
    /// situação = a mais avançada. Retornos passam (o "fora do controle" repetido pelo mesmo valor sai);
    /// controle de carregamento: herda se a que fica não tem, senão o da que sai fica solto; anotações
    /// passam se não forem repetidas. Por fim remove a que sai (sem ExcluirAsync). Devolve os avisos.
    /// </summary>
    public async Task<List<string>> JuntarAsync(int manterId, int removerId)
    {
        if (manterId == removerId) throw new ErroViagem("Escolha duas viagens diferentes.");
        var fica = await ComTudo().FirstOrDefaultAsync(v => v.Id == manterId) ?? throw new ErroViagem($"Viagem {manterId} não encontrada.");
        var sai = await ComTudo().FirstOrDefaultAsync(v => v.Id == removerId) ?? throw new ErroViagem($"Viagem {removerId} não encontrada.");
        if (NormalizarPlaca(fica.Placa) != NormalizarPlaca(sai.Placa)) throw new ErroViagem("As duas viagens são de placas diferentes.");
        var avisos = new List<string>();
        var (_, _, valoresDaQueSai) = QualFica(fica, sai);
        var valoresDaPlanilha = valoresDaQueSai && DaPlanilha(sai);

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;

        // 1. O que muda de viagem: retornos, controle de carregamento, anotações
        var retsFica = await db.RetornosFrete.Where(r => r.ViagemId == fica.Id).ToListAsync();
        foreach (var r in await db.RetornosFrete.Where(r => r.ViagemId == sai.Id).ToListAsync())
        {
            if (r.ForaDoControle && retsFica.Any(f => Math.Abs(f.FreteTotal - r.FreteTotal) < 0.01m))
            {
                db.RetornosFrete.Remove(r);
                avisos.Add($"Retorno {r.Transportadora} R$ {r.FreteTotal:N2} (fora do controle) repetido na viagem {fica.Id}: saiu.");
                continue;
            }
            r.ViagemId = fica.Id; retsFica.Add(r);
        }
        var cFica = await db.CarregamentosVolumes.AnyAsync(c => c.ViagemId == fica.Id);
        foreach (var c in await db.CarregamentosVolumes.Where(c => c.ViagemId == sai.Id).ToListAsync())
        {
            if (!cFica) { c.ViagemId = fica.Id; cFica = true; continue; }
            c.ViagemId = null;
            avisos.Add($"As duas tinham controle de carregamento: o de {c.Data:dd/MM} (da viagem {sai.Id}) ficou solto.");
        }
        // Só a chave estrangeira muda (tirar da coleção apagaria o filho como órfão)
        var cargasFica = fica.Cargas.ToList();
        var ordem = cargasFica.Count;
        foreach (var c in sai.Cargas.ToList())
        {
            if (cargasFica.FirstOrDefault(f => MesmaAnotacao(f, c)) is { } igual)
            {
                // Repetida: fica a da viagem que fica; a ligação com o retorno passa se ela não tinha
                if (igual.RetornoId == null && c.RetornoId != null) { var rid = c.RetornoId; c.RetornoId = null; await db.SaveChangesAsync(); igual.RetornoId = rid; igual.Ligacao = c.Ligacao; }
                continue;
            }
            c.ViagemId = fica.Id; c.Ordem = ordem++;
        }
        var chave = sai.ChaveImportacao;
        sai.ChaveImportacao = null;   // a chave é única no banco: sai antes de passar para a que fica
        await db.SaveChangesAsync();

        // 2. Valores da planilha
        if (valoresDaPlanilha)
        {
            if (sai.MotoristaId != null || fica.MotoristaId == null) { fica.MotoristaId = sai.MotoristaId; fica.MotoristaNome = sai.MotoristaNome; }
            if (!string.IsNullOrWhiteSpace(sai.PlacaCarreta)) fica.PlacaCarreta = sai.PlacaCarreta;
            fica.ChaveImportacao = chave; fica.MesReferencia = sai.MesReferencia; fica.AnoReferencia = sai.AnoReferencia;
            fica.Destino = sai.Destino; fica.DataSaida = sai.DataSaida; fica.DataChegada = sai.DataChegada;
            fica.KmSaida = sai.KmSaida; fica.KmChegada = sai.KmChegada; fica.Litros = sai.Litros;
            fica.Levou = sai.Levou; fica.Devolveu = sai.Devolveu;
            fica.DiariasQuantidade = sai.DiariasQuantidade; fica.ValorDiaria = sai.ValorDiaria; fica.PorcentagemComissao = sai.PorcentagemComissao;
            fica.SobraAnteriorInformada = sai.SobraAnteriorInformada;
            db.RemoveRange(fica.Volumes); db.RemoveRange(fica.Abastecimentos); db.RemoveRange(fica.Despesas);
            await db.SaveChangesAsync();
            foreach (var o in sai.Volumes.ToList()) o.ViagemId = fica.Id;
            foreach (var a in sai.Abastecimentos.ToList()) a.ViagemId = fica.Id;
            foreach (var d in sai.Despesas.ToList()) d.ViagemId = fica.Id;
        }
        else if (fica.ChaveImportacao == null) fica.ChaveImportacao = chave;
        else if (chave != null) avisos.Add($"A viagem {sai.Id} tinha a chave {chave}; a {fica.Id} ficou com a dela.");

        if (Nivel(sai.Status) > Nivel(fica.Status))
        {
            fica.Status = sai.Status;
            fica.ConferidoEm ??= sai.ConferidoEm ?? DateTime.UtcNow;
        }
        var nota = DaPlanilha(sai) && AbaDaChave(chave) is { } aba
            ? $"Juntada com a viagem da planilha ({aba}) em {DateTime.Now:dd/MM/yyyy}."
            : $"Juntada com a viagem {sai.Id} em {DateTime.Now:dd/MM/yyyy}.";
        fica.Observacoes = string.Join(" ", new[] { fica.Observacoes, nota }.Where(x => !string.IsNullOrWhiteSpace(x)));
        fica.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        db.ViagensCaminhao.Remove(sai);
        await db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        avisos.Insert(0, $"Viagem {sai.Id} juntada na {fica.Id}" + (valoresDaPlanilha ? " com os valores da planilha." : "."));
        return avisos;
    }

    /// <summary>Apaga a viagem vazia (S4) pedida pelo id, pelo ExcluirAsync. Recusa a que tem dados.</summary>
    public async Task ApagarVaziaAsync(int id)
    {
        var v = await ComTudo().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw new ErroViagem($"Viagem {id} não encontrada.");
        var comRetorno = await db.RetornosFrete.AnyAsync(r => r.ViagemId == id) ? new HashSet<int> { id } : [];
        if (!Vazia(v, comRetorno)) throw new ErroViagem($"A viagem {id} não está vazia (tem volumes, retorno, abastecimento, despesa ou dinheiro levado).");
        await ExcluirAsync(id);
    }

    // ───────────────────────── Excel ─────────────────────────

    // Acerto do mês por motorista (VIA-21); sem motorista, pela placa (viagens antigas soltas)
    public async Task<(byte[] Conteudo, string Nome)> ExportarAcertoAsync(int? motoristaId, string? placa, int ano, int mes)
    {
        placa = NormalizarPlaca(placa);
        var q = ComTudo().AsNoTracking().Where(v => v.AnoReferencia == ano && v.MesReferencia == mes);
        q = motoristaId is { } mid ? q.Where(v => v.MotoristaId == mid) : q.Where(v => v.MotoristaId == null && v.Placa == placa);
        var viagens = await q.ToListAsync();
        if (viagens.Count == 0) throw new ErroViagem("Nenhuma viagem deste motorista no mês.");
        var quem = placa;
        if (motoristaId is { } m2)
        {
            var nome = await db.MotoristasFrota.AsNoTracking().Where(x => x.Id == m2).Select(x => x.Nome).FirstOrDefaultAsync() ?? "";
            quem = nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? $"motorista {m2}";
        }
        var ids = viagens.Select(v => v.Id).ToList();
        var rets = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null && ids.Contains(r.ViagemId.Value)).OrderBy(r => r.Data).ThenBy(r => r.Id).ToListAsync();
        var sobras = await SobrasAsync(viagens);
        var tabela = await db.PrecosFreteCliente.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Ordem).ThenBy(p => p.Cliente).ToListAsync();
        var lista = EmOrdem(viagens).Select(v => (v, rets.Where(r => r.ViagemId == v.Id).ToList(), sobras.GetValueOrDefault(v.Id))).ToList();
        var bytes = AcertoCaminhaoExcel.Gerar(lista, tabela);
        return (bytes, $"{mes}-V -{NomeMes(mes)}-{ano} {quem}.xlsx");
    }

    public async Task<(byte[] Conteudo, string Nome)> ExportarRetornosAsync(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        var lista = await db.RetornosFrete.AsNoTracking().Where(r => r.Data >= ini && r.Data < ini.AddMonths(1) && !r.ForaDoControle)
            .OrderBy(r => r.Data).ThenBy(r => r.Id).ToListAsync();
        return (AcertoCaminhaoExcel.GerarRetornos(lista, mes, ano), $"{mes:00}-R-{NomeMes(mes)} -{ano % 100:00}.xlsx");
    }

    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
