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
    string? Observacoes, string? FormaPagamento, int? ViagemId, decimal PorcentagemIcms, bool ForaDoControle, bool? Conferido);

public class ErroViagem(string mensagem) : Exception(mensagem);

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

    /// <summary>Sobra anterior das viagens dos caminhões pedidos (cadeia inteira de cada um).</summary>
    public async Task<Dictionary<int, decimal>> SobrasAsync(IEnumerable<string> placas)
    {
        var lista = placas.Distinct().ToList();
        var viagens = await db.ViagensCaminhao.Where(v => lista.Contains(v.Placa))
            .Include(v => v.Abastecimentos).Include(v => v.Despesas).AsSplitQuery().AsNoTracking().ToListAsync();
        var mapa = new Dictionary<int, decimal>();
        foreach (var g in viagens.GroupBy(v => v.Placa))
            foreach (var kv in SobrasAnteriores(g)) mapa[kv.Key] = kv.Value;
        return mapa;
    }

    public record SobraAnteriorPainel(bool TemAnterior, decimal Sobra, DateOnly? DataViagemAnterior);

    /// <summary>
    /// Sobra que a viagem que o motorista está preenchendo recebe da anterior do mesmo
    /// caminhão (VIA-07), igual à conta do escritório: a folha entra na cadeia como uma
    /// viagem a mais (mês da saída, data de saída) e a sobra é a da viagem logo antes
    /// dela, em qualquer situação. Ao corrigir um envio, a própria viagem sai da cadeia
    /// e fica no mês de acerto que já tem. Só devolve sobra e data, nada de frete.
    /// </summary>
    public async Task<SobraAnteriorPainel> SobraAnteriorPainelAsync(string? placaInformada, DateOnly? dataSaida, int? viagemPropriaId)
    {
        var placa = NormalizarPlaca(placaInformada);
        if (placa.Length == 0) return new(false, 0, null);
        var propria = viagemPropriaId is { } pid ? await db.ViagensCaminhao.AsNoTracking().FirstOrDefaultAsync(v => v.Id == pid) : null;
        var excluir = propria?.Id ?? 0;
        var viagens = await db.ViagensCaminhao.AsNoTracking().Where(v => v.Placa == placa && v.Id != excluir)
            .Include(v => v.Abastecimentos).Include(v => v.Despesas).AsSplitQuery().ToListAsync();
        if (viagens.Count == 0) return new(false, 0, null);

        var dia = dataSaida ?? propria?.DataSaida;
        var refDia = dia ?? DateOnly.FromDateTime(DateTime.Today);
        var folha = new ViagemCaminhao
        {
            Id = propria?.Id ?? int.MaxValue, Placa = placa, DataSaida = dia,
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
        var sobras = await SobrasAsync(viagens.Select(v => v.Placa));
        var nomes = await db.MotoristasFrota.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nome);

        var itens = viagens.GroupBy(v => v.Placa).OrderBy(g => g.Key).SelectMany(g => EmOrdem(g).Select((v, i) =>
        {
            var rv = rets.Where(r => r.ViagemId == v.Id).ToList();
            var c = Calcular(v, rv, sobras.GetValueOrDefault(v.Id));
            return new
            {
                v.Id, v.Placa, v.PlacaCarreta, Numero = i + 1,
                Motorista = v.MotoristaId is { } mid && nomes.TryGetValue(mid, out var n) ? n : v.MotoristaNome,
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
        var sobras = await SobrasAsync([v.Placa]);
        var c = Calcular(v, rets, sobras.GetValueOrDefault(id));

        // Posição na cadeia do mês (VIAGEMn) e vizinhas
        var doMes = await db.ViagensCaminhao.AsNoTracking()
            .Where(x => x.Placa == v.Placa && x.AnoReferencia == v.AnoReferencia && x.MesReferencia == v.MesReferencia).ToListAsync();
        var ordem = EmOrdem(doMes).Select(x => x.Id).ToList();

        var sugeridos = await RetornosSugeridosAsync(v);
        var carregamentos = await CarregamentosCandidatosAsync(v);
        var motorista = v.MotoristaId is { } mid ? await db.MotoristasFrota.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mid) : null;

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
            Cargas = v.Cargas.OrderBy(x => x.Ordem).Select(x => new { x.Id, x.Tipo, x.Transportadora, x.Data, x.Origem, x.Destino, x.Valor, x.RetornoId }),
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

        await SincronizarCargasAsync(v, e.Cargas ?? [], doMotorista);
        await db.SaveChangesAsync();

        // Viagem sem volumes ainda: tenta o controle de carregamento da placa e do dia
        if (v.Volumes.Count == 0) await CasarCarregamentoAsync(v);
        return v;
    }

    /// <summary>
    /// As cargas "outra carga / retorno" viram linhas dos Retornos (ligadas à viagem).
    /// A do motorista entra como "a conferir"; a já conferida pelo escritório não muda
    /// mais pela folha do motorista.
    /// </summary>
    private async Task SincronizarCargasAsync(ViagemCaminhao v, List<CargaEntrada> cargas, bool doMotorista)
    {
        var antigas = v.Cargas.ToList();
        var retIds = antigas.Where(c => c.RetornoId != null).Select(c => c.RetornoId!.Value)
            .Concat(cargas.Where(c => c.RetornoId != null).Select(c => c.RetornoId!.Value)).Distinct().ToList();
        var retornos = await db.RetornosFrete.Where(r => retIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);

        var novas = new List<ViagemCaminhaoCarga>();
        var mantidos = new HashSet<int>();
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
                RetornoFrete? r = c.RetornoId is { } rid && retornos.TryGetValue(rid, out var achado) ? achado : null;
                // Só reaproveita retorno desta viagem (ou ainda sem viagem): id de outra viagem é ignorado
                // (o motorista só mexe nos retornos da própria viagem)
                if (r != null && r.ViagemId != null && r.ViagemId != v.Id) r = null;
                if (r != null && doMotorista && (v.Id == 0 || r.ViagemId != v.Id)) r = null;
                if (r == null)
                {
                    r = new RetornoFrete { Origem = doMotorista ? "motorista" : "escritorio", Conferido = !doMotorista };
                    db.RetornosFrete.Add(r);
                }
                var novo = r.Id == 0;
                if (novo || !r.Conferido) r.ValorFrete = c.Valor;
                if (!r.Conferido || !doMotorista)
                {
                    r.Data = c.Data ?? v.DataSaida ?? DateOnly.FromDateTime(DateTime.Today);
                    r.Transportadora = nova.Transportadora;
                    r.Observacoes = nova.Origem;
                    r.Destino = nova.Destino;
                    r.Placa = v.Placa;
                    r.Motorista = v.MotoristaNome;
                    r.MotoristaId = v.MotoristaId;
                    r.AtualizadoEm = DateTime.UtcNow;
                }
                r.Viagem = v;
                if (r.Id != 0) mantidos.Add(r.Id);
                nova.Valor = r.Conferido ? r.FreteTotal : c.Valor;
                nova.Retorno = r;
                novas.Add(nova);
            }
            else novas.Add(nova);
        }

        // Retorno que o motorista tirou da folha: some se ainda não foi conferido
        foreach (var velha in antigas.Where(a => a.RetornoId != null && !mantidos.Contains(a.RetornoId!.Value)))
            if (retornos.TryGetValue(velha.RetornoId!.Value, out var r) && r.Origem == "motorista" && !r.Conferido)
                db.RetornosFrete.Remove(r);

        db.ViagemCaminhaoCargas.RemoveRange(antigas);
        v.Cargas = novas;
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
        return new
        {
            ano, mes,
            retornos = lista.Select(r => new
            {
                Retorno = RetornoDto(r),
                Viagem = r.ViagemId is { } vid && viagens.TryGetValue(vid, out var vv) ? vv : null,
            }),
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
        if (e.ViagemId is { } vid)
        {
            if (!await db.ViagensCaminhao.AnyAsync(v => v.Id == vid)) throw new ErroViagem("Viagem não encontrada.");
            r.ViagemId = vid;
        }
        else r.ViagemId = null;
        if (r.ViagemId != null && r.MotoristaId == null)
            r.MotoristaId = await db.ViagensCaminhao.Where(v => v.Id == r.ViagemId).Select(v => v.MotoristaId).FirstOrDefaultAsync();
        await db.SaveChangesAsync();
        await AtualizarCargaDoRetornoAsync(r);
        return r;
    }

    // A carga declarada que aponta para o retorno mostra o frete total conferido
    private async Task AtualizarCargaDoRetornoAsync(RetornoFrete r)
    {
        var cargas = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId == r.Id).ToListAsync();
        foreach (var c in cargas)
        {
            if (r.ViagemId != c.ViagemId) c.RetornoId = null;
            else c.Valor = r.FreteTotal;
        }
        await db.SaveChangesAsync();
    }

    public async Task VincularRetornoAsync(int retornoId, int? viagemId)
    {
        var r = await db.RetornosFrete.FindAsync(retornoId) ?? throw new ErroViagem("Retorno não encontrado.");
        if (viagemId is { } vid && !await db.ViagensCaminhao.AnyAsync(v => v.Id == vid)) throw new ErroViagem("Viagem não encontrada.");
        r.ViagemId = viagemId;
        r.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await AtualizarCargaDoRetornoAsync(r);
    }

    public async Task ExcluirRetornoAsync(int id)
    {
        var r = await db.RetornosFrete.FindAsync(id) ?? throw new ErroViagem("Retorno não encontrado.");
        var cargas = await db.ViagemCaminhaoCargas.Where(c => c.RetornoId == id).ToListAsync();
        foreach (var c in cargas) c.RetornoId = null;
        db.RetornosFrete.Remove(r);
        await db.SaveChangesAsync();
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
        var chaves = lido.Linhas.Select(l => l.Chave).ToList();
        var existentes = await db.RetornosFrete.Where(r => r.ChaveImportacao != null && chaves.Contains(r.ChaveImportacao))
            .Select(r => r.ChaveImportacao!).ToListAsync();
        var motoristas = await db.MotoristasFrota.AsNoTracking().ToListAsync();
        var novos = 0;
        foreach (var l in lido.Linhas.Where(l => !existentes.Contains(l.Chave)))
        {
            db.RetornosFrete.Add(new RetornoFrete
            {
                Data = l.Data, Destino = l.Destino, Transportadora = l.Transportadora, ValorFrete = l.ValorFrete,
                Pedagio = l.Pedagio, Adicional = l.Adicional, DescontoSeguro = l.DescontoSeguro, PorcentagemTip = l.PorcentagemTip,
                DescontoManual = l.DescontoManual, Motorista = l.Motorista, Placa = l.Placa, Observacoes = l.Observacoes,
                FormaPagamento = l.FormaPagamento, Origem = "importacao", Conferido = true, ChaveImportacao = l.Chave,
                MotoristaId = AcharMotorista(l.Motorista, motoristas)?.Id,
            });
            novos++;
        }
        await db.SaveChangesAsync();
        var vinculados = await VincularRetornosSemViagemAsync(lido.Ano, lido.Mes);
        var total = lido.Linhas.Sum(l => l.FreteTotal);
        return new
        {
            arquivo = nome, mes = lido.Mes, ano = lido.Ano, linhas = lido.Linhas.Count, novos,
            jaExistiam = lido.Linhas.Count - novos, vinculados, total, totalPlanilha = lido.TotalPlanilha,
            confere = lido.TotalPlanilha == null || Math.Abs(lido.TotalPlanilha.Value - total) < 0.01m,
            avisos = lido.Avisos,
        };
    }

    /// <summary>Liga retornos sem viagem à viagem do mesmo caminhão que estava na estrada naquele dia (só quando há uma).</summary>
    public async Task<int> VincularRetornosSemViagemAsync(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1).AddDays(-7);
        var fim = new DateOnly(ano, mes, 1).AddMonths(1).AddDays(7);
        var soltos = await db.RetornosFrete.Where(r => r.ViagemId == null && !r.ForaDoControle && r.Data >= ini && r.Data < fim && r.Placa != null).ToListAsync();
        if (soltos.Count == 0) return 0;
        var placas = soltos.Select(r => r.Placa!).Distinct().ToList();
        var viagens = await db.ViagensCaminhao.AsNoTracking()
            .Where(v => placas.Contains(v.Placa) && v.DataSaida != null && v.DataSaida <= fim && (v.DataChegada ?? v.DataSaida) >= ini.AddDays(-20))
            .ToListAsync();
        var n = 0;
        foreach (var r in soltos)
        {
            var cand = viagens.Where(v => v.Placa == r.Placa && v.DataSaida <= r.Data && (v.DataChegada ?? v.DataSaida.Value.AddDays(3)) >= r.Data).ToList();
            if (cand.Count != 1) continue;
            r.ViagemId = cand[0].Id;
            n++;
        }
        await db.SaveChangesAsync();
        return n;
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
            // Mesma placa e dia: substitui (importar de novo não duplica)
            var c = await db.CarregamentosVolumes.FirstOrDefaultAsync(x => x.Placa == placa && x.Data == l.Data);
            if (c == null) { c = new CarregamentoVolumes { Placa = placa, Data = l.Data.Value }; db.CarregamentosVolumes.Add(c); }
            c.Motorista = l.Motorista;
            c.Destino = l.Destino;
            c.Observacao = l.Observacao;
            c.NomeArquivo = nome;
            c.ItensJson = ItensParaJson(l.Itens);
            await db.SaveChangesAsync();

            var (viagemId, aplicado) = await DepoisDeGravarAsync(c, l.Avisos);
            resultado.Add(new
            {
                aba = l.Aba, guardado = true, id = c.Id, placa, data = c.Data, clientes = l.Itens.Count, viagemId, aplicado,
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
        var (viagemId, aplicado) = await DepoisDeGravarAsync(c, avisos);
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
    private async Task<(int? ViagemId, bool Aplicado)> DepoisDeGravarAsync(CarregamentoVolumes c, List<string> avisos)
    {
        int? viagemId = c.ViagemId;
        var aplicado = false;
        if (viagemId == null)
        {
            var v = await ViagemDoCarregamentoAsync(c);
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

    private async Task<ViagemCaminhao?> ViagemDoCarregamentoAsync(CarregamentoVolumes c)
    {
        var cand = await db.ViagensCaminhao.Include(v => v.Cargas)
            // Saída perto do dia, ou o dia dentro da viagem (carga no meio: XAA de 03 a 13/09 carregou em 11/09)
            .Where(v => v.Placa == c.Placa && v.DataSaida != null && ((v.DataSaida >= c.Data.AddDays(-1) && v.DataSaida <= c.Data.AddDays(2))
                || (v.DataSaida <= c.Data && v.DataChegada != null && v.DataChegada >= c.Data)))
            .ToListAsync();
        var exatos = cand.Where(v => v.DataSaida == c.Data || v.Cargas.Any(x => x.Tipo == "VOLUMES" && x.Data == c.Data)).ToList();
        if (exatos.Count == 1) return exatos[0];
        return exatos.Count == 0 && cand.Count == 1 ? cand[0] : null;
    }

    private async Task CasarCarregamentoAsync(ViagemCaminhao v)
    {
        if (v.DataSaida == null) return;
        var cands = await db.CarregamentosVolumes
            .Where(c => c.ViagemId == null && c.Placa == v.Placa && c.Data >= v.DataSaida.Value.AddDays(-2)
                && c.Data <= (v.DataChegada > v.DataSaida.Value.AddDays(1) ? v.DataChegada.Value : v.DataSaida.Value.AddDays(1)))
            .ToListAsync();
        var datasVolumes = v.Cargas.Where(x => x.Tipo == "VOLUMES" && x.Data != null).Select(x => x.Data!.Value).ToList();
        var exatos = cands.Where(c => c.Data == v.DataSaida || datasVolumes.Contains(c.Data)).ToList();
        var escolhido = exatos.Count == 1 ? exatos[0] : exatos.Count == 0 && cands.Count == 1 ? cands[0] : null;
        if (escolhido != null) await AplicarCarregamentoAsync(v.Id, escolhido.Id);
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
        var lista = await db.CarregamentosVolumes.AsNoTracking().Where(c => c.Data >= ini && c.Data < fim).OrderBy(c => c.Data).ThenBy(c => c.Placa).ToListAsync();
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
    /// da aba casam com os da tela Retornos (mesma placa, mesmo frete total, perto das
    /// datas); sem par, entram como retorno "fora do controle" só desta viagem.
    /// A sobra anterior da planilha entra como informada só quando não há viagem
    /// anterior do caminhão no sistema (começo da cadeia).
    /// </summary>
    public async Task<object> ImportarAcertoAsync(byte[] bytes, string nome, string? placaInformada, bool substituir)
    {
        var lido = AcertoCaminhaoLeitor.Ler(bytes, nome, placaInformada);
        var motoristas = await db.MotoristasFrota.AsNoTracking().ToListAsync();
        var tabela = await db.PrecosFreteCliente.AsNoTracking().ToListAsync();
        var avisos = new List<string>(lido.Avisos);
        var criadas = 0; var puladas = 0; var conferencia = new List<object>();

        foreach (var a in lido.Viagens)
        {
            var chave = $"{lido.Placa}|{lido.Ano}-{lido.Mes:00}|{a.Aba.ToUpperInvariant().Replace(" ", "")}";
            var existente = await db.ViagensCaminhao.FirstOrDefaultAsync(v => v.ChaveImportacao == chave);
            if (existente != null)
            {
                if (!substituir) { puladas++; continue; }
                await ExcluirAsync(existente.Id);
            }

            var mot = AcharMotorista(a.Motorista, motoristas);
            var v = new ViagemCaminhao
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
            // Diária que não fecha em meia (R$ 50): fica a quantidade quebrada e um aviso
            if (a.Diarias % 50 != 0) avisos.Add($"{a.Aba}: diárias de R$ {a.Diarias:N2} não fecham em meia diária.");

            // Começo da cadeia: sem viagem anterior do caminhão no sistema, vale a sobra da planilha
            var temAnterior = await db.ViagensCaminhao.AnyAsync(x => x.Placa == lido.Placa &&
                (x.AnoReferencia < lido.Ano || x.AnoReferencia == lido.Ano && x.MesReferencia < lido.Mes
                 || x.AnoReferencia == lido.Ano && x.MesReferencia == lido.Mes && x.DataSaida < a.Saida));
            if (!temAnterior) v.SobraAnteriorInformada = a.SobraAnterior;
            db.ViagensCaminhao.Add(v);
            await db.SaveChangesAsync();

            // Retornos da aba (dois fretes iguais na mesma viagem casam com duas linhas diferentes)
            var usados = new HashSet<int>();
            foreach (var r in a.Retornos)
            {
                var ini = (a.Saida ?? DateOnly.MinValue).AddDays(-5);
                var fim = (a.Chegada ?? a.Saida ?? DateOnly.MaxValue.AddDays(-30)).AddDays(5);
                var cands = await db.RetornosFrete.Where(x => x.ViagemId == null && x.Placa == lido.Placa && x.Data >= ini && x.Data <= fim).ToListAsync();
                var par = cands.Where(x => x.ViagemId == null && !usados.Contains(x.Id) && Math.Abs(x.FreteTotal - r.Valor) < 0.01m)
                    .OrderBy(x => x.Data).FirstOrDefault();
                if (par != null)
                {
                    usados.Add(par.Id);
                    par.ViagemId = v.Id;
                    if (r.PorcentagemIcms != 0) par.PorcentagemIcms = r.PorcentagemIcms;
                }
                else
                {
                    var partes = System.Text.RegularExpressions.Regex.Split((r.OrigemDestino ?? "").Trim(), @"\s*/+\s*|\s+X\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                    db.RetornosFrete.Add(new RetornoFrete
                    {
                        Data = a.Saida ?? new DateOnly(lido.Ano, lido.Mes, 1), Transportadora = r.Transportadora, ValorFrete = r.Valor,
                        Observacoes = partes.Length > 0 && partes[0].Length > 0 ? partes[0] : null,
                        Destino = partes.Length > 1 ? partes[1] : r.OrigemDestino,
                        Placa = lido.Placa, Motorista = v.MotoristaNome, MotoristaId = v.MotoristaId, ViagemId = v.Id,
                        PorcentagemIcms = r.PorcentagemIcms, ForaDoControle = true, Origem = "importacao", Conferido = true,
                    });
                    avisos.Add($"{a.Aba}: retorno {r.Transportadora} R$ {r.Valor:N2} não está na tela Retornos; entrou só na viagem (fora do total mensal). Importe os retornos do mês antes para casar.");
                }
            }
            await db.SaveChangesAsync();
            criadas++;
        }

        // Confere: o que o sistema calcula contra os totais guardados na planilha
        var viagensMes = await ComTudo().AsNoTracking().Where(v => v.Placa == lido.Placa && v.AnoReferencia == lido.Ano && v.MesReferencia == lido.Mes).ToListAsync();
        var sobras = await SobrasAsync([lido.Placa]);
        var idsMes = viagensMes.Select(x => x.Id).ToList();
        var retsMes = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null && idsMes.Contains(r.ViagemId.Value)).ToListAsync();
        foreach (var a in lido.Viagens)
        {
            var chave = $"{lido.Placa}|{lido.Ano}-{lido.Mes:00}|{a.Aba.ToUpperInvariant().Replace(" ", "")}";
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
        return new { arquivo = nome, placa = lido.Placa, mes = lido.Mes, ano = lido.Ano, criadas, puladas, avisos, conferencia };
    }

    // ───────────────────────── Excel ─────────────────────────

    public async Task<(byte[] Conteudo, string Nome)> ExportarAcertoAsync(string placa, int ano, int mes)
    {
        placa = NormalizarPlaca(placa);
        var viagens = await ComTudo().AsNoTracking().Where(v => v.Placa == placa && v.AnoReferencia == ano && v.MesReferencia == mes).ToListAsync();
        if (viagens.Count == 0) throw new ErroViagem("Nenhuma viagem deste caminhão no mês.");
        var ids = viagens.Select(v => v.Id).ToList();
        var rets = await db.RetornosFrete.AsNoTracking().Where(r => r.ViagemId != null && ids.Contains(r.ViagemId.Value)).OrderBy(r => r.Data).ThenBy(r => r.Id).ToListAsync();
        var sobras = await SobrasAsync([placa]);
        var tabela = await db.PrecosFreteCliente.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Ordem).ThenBy(p => p.Cliente).ToListAsync();
        var lista = EmOrdem(viagens).Select(v => (v, rets.Where(r => r.ViagemId == v.Id).ToList(), sobras.GetValueOrDefault(v.Id))).ToList();
        var bytes = AcertoCaminhaoExcel.Gerar(lista, tabela);
        return (bytes, $"{mes}-V -{NomeMes(mes)}-{ano} {placa}.xlsx");
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
