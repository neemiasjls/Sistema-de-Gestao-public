using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.FechamentoMensalCalculo;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

// Entrada (o que a tela manda)
public record DadosSalarioFechamento(decimal Salario, decimal HorasExtras, decimal Fgts, decimal Inss, decimal Irr, decimal Quinquenio);
public record DespesaFechamentoEntrada(DateOnly? Data, string? Referencia, decimal? Valor, string? Parcela, string? Observacao, string? Origem, bool Corrigida);
public record CreditoFechamentoEntrada(string? Referencia, decimal Valor);
public record ValeFechamentoEntrada(DateOnly? Data, decimal Valor, string? Referencia);
public record FechamentoEntrada(int MotoristaId, int Ano, int Mes, decimal? SaldoAnteriorDigitado, DadosSalarioFechamento Salario,
    string? Observacao, List<DespesaFechamentoEntrada>? Despesas, List<CreditoFechamentoEntrada>? Creditos, List<ValeFechamentoEntrada>? Vales);
public record MesFechamentoEntrada(int MotoristaId, int Ano, int Mes);
public record ConsorcioEntrada(string Placa, string Descricao, string? Cota, decimal ValorParcela, int TotalParcelas,
    int PrimeiroAno, int PrimeiroMes, bool Ativo);

// Saída (o que a tela mostra)
public record LinhaViagem(int Id, DateOnly? DataSaida, string? Destino, string Motorista, string Placa, string? PlacaCarreta, decimal Resultado,
    decimal FreteComissao, decimal PorcentagemComissao, decimal Comissao);
public record LinhaDespesa(DateOnly? Data, string Referencia, decimal? Valor, string? Parcela, string? Observacao,
    string Origem, bool Automatica, bool Corrigida, decimal? ValorAutomatico, string? DeOndeVem, string? Aviso);
public record LinhaCredito(string Referencia, decimal Valor);
public record LinhaVale(DateOnly? Data, decimal Valor, string? Referencia);
public record TotaisFechamento(decimal EntradaViagens, decimal Creditos, decimal EntradaTotal, decimal SaidaTotal,
    decimal Sobra, decimal SaldoAnterior, decimal Total, decimal ComissaoTotal, decimal TotalVales, decimal SalarioLiquido);
public record ModeloFechamento(int MotoristaId, string Motorista, List<string> Placas, int Ano, int Mes, bool Salvo, int? Id, bool Fechado, DateTime? FechadoEm,
    string? Observacao, List<LinhaViagem> Viagens, List<LinhaCredito> Creditos, List<LinhaDespesa> Despesas,
    List<LinhaVale> Vales, DadosSalarioFechamento Salario, decimal? SaldoAnteriorDigitado, string SaldoAnteriorOrigem,
    TotaisFechamento Totais, List<string> Avisos);

/// <summary>
/// Fechamento mensal por motorista (VIA-18, VIA-21 desde 02/11/2026; era por caminhão): junta as
/// viagens do motorista no mês (data de saída), com qualquer placa,
/// os créditos, as despesas (manuais + automáticas: ICMS/seguro da SG do mês anterior,
/// consórcio e as retenções do salário), a comissão e o salário. Aberto edita; fechado trava.
/// </summary>
public class FechamentoMensalService(AppDbContext db)
{
    // Os três do acerto (Admin, 02/11/2026): Bento, Renato, Paulo Henrique (MotoristasFrota.Id)
    public static readonly int[] MotoristasDoAcerto = [10, 12, 4];

    private async Task<(int Id, string Nome)> MotoristaValidoAsync(int motoristaId)
    {
        if (!MotoristasDoAcerto.Contains(motoristaId))
            throw new ErroViagem("O fechamento mensal é só do Bento, do Renato e do Paulo Henrique.");
        var nome = await db.MotoristasFrota.AsNoTracking().Where(m => m.Id == motoristaId).Select(m => m.Nome).FirstOrDefaultAsync()
            ?? throw new ErroViagem("Motorista não encontrado.");
        return (motoristaId, nome);
    }

    public async Task<List<object>> MotoristasAsync()
    {
        var ms = await db.MotoristasFrota.AsNoTracking().Where(m => MotoristasDoAcerto.Contains(m.Id))
            .Select(m => new { m.Id, m.Nome }).ToListAsync();
        return [.. MotoristasDoAcerto.Select(id => ms.FirstOrDefault(m => m.Id == id)).Where(m => m != null).Cast<object>()];
    }

    private static string PlacaValida(string? placa)
    {
        var p = NormalizarPlaca(placa);
        if (p.Length == 0) throw new ErroViagem("Informe a placa do caminhão.");
        return p;
    }

    private static void MesValido(int ano, int mes)
    {
        if (mes is < 1 or > 12 || ano is < 2000 or > 2100) throw new ErroViagem("Mês inválido.");
    }

    private static string NomeMesAno(int mes, int ano) => $"{PlanilhaSeguroLeitor.NomeMes(mes)} de {ano}";

    // ───────────────────────── Montagem ─────────────────────────

    public async Task<ModeloFechamento> MontarAsync(int motoristaId, int ano, int mes)
    {
        var m = await MotoristaValidoAsync(motoristaId);
        MesValido(ano, mes);
        return await MontarModeloAsync(m, ano, mes, 0);
    }

    private async Task<ModeloFechamento> MontarModeloAsync((int Id, string Nome) mot, int ano, int mes, int profundidade)
    {
        var motoristaId = mot.Id;
        var avisos = new List<string>();
        var rec = await db.FechamentosMensais.AsNoTracking()
            .Include(f => f.Despesas).Include(f => f.Creditos).Include(f => f.Vales).AsSplitQuery()
            .FirstOrDefaultAsync(f => f.MotoristaId == motoristaId && f.Ano == ano && f.Mes == mes);
        var fechado = rec?.Fechado == true;
        var sal = rec == null ? new DadosSalarioFechamento(0, 0, 0, 0, 0, 0)
            : new DadosSalarioFechamento(rec.Salario, rec.HorasExtras, rec.Fgts, rec.Inss, rec.Irr, rec.Quinquenio);

        // Viagens do mês pela data de saída
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1);
        var viagens = await db.ViagensCaminhao.AsNoTracking()
            .Include(v => v.Volumes).Include(v => v.Abastecimentos).Include(v => v.Despesas).AsSplitQuery()
            .Where(v => v.MotoristaId == motoristaId && v.DataSaida >= ini && v.DataSaida < fim)
            .ToListAsync();
        var ids = viagens.Select(v => v.Id).ToList();
        var rets = await db.RetornosFrete.AsNoTracking()
            .Where(r => r.ViagemId != null && ids.Contains(r.ViagemId.Value)).ToListAsync();
        var linhasViagem = viagens.OrderBy(v => v.DataSaida).ThenBy(v => v.Id).Select(v =>
        {
            var c = Calcular(v, rets.Where(r => r.ViagemId == v.Id), 0);
            return new LinhaViagem(v.Id, v.DataSaida, v.Destino, v.MotoristaNome, v.Placa, v.PlacaCarreta, c.Resultado_, c.BaseComissao,
                v.PorcentagemComissao, c.Comissao);
        }).ToList();
        if (linhasViagem.Count == 0) avisos.Add($"Nenhuma viagem saiu em {NomeMesAno(mes, ano)}.");
        var semSaida = await db.ViagensCaminhao.CountAsync(v => v.MotoristaId == motoristaId && v.DataSaida == null
            && v.AnoReferencia == ano && v.MesReferencia == mes);
        if (semSaida > 0)
            avisos.Add($"{semSaida} viagem(ns) do acerto de {NomeMesAno(mes, ano)} sem data de saída ficaram de fora.");

        // Despesas: manuais + automáticas
        List<LinhaDespesa> despesas;
        if (fechado)
        {
            despesas = rec!.Despesas.OrderBy(d => d.Ordem).Select(d => new LinhaDespesa(d.Data, d.Referencia, d.Valor,
                d.Parcela, d.Observacao, d.Origem, d.Origem != "manual", d.Corrigido, null, DeOndeVem(d.Origem, ano, mes),
                d.Origem == "icms" && d.Valor == null ? "Sem valor: faltava a planilha SG quando o mês foi fechado" : null)).ToList();
        }
        else
        {
            despesas = [];
            foreach (var auto in await AutomaticasAsync(motoristaId, ano, mes, sal))
            {
                var corrigida = rec?.Despesas.FirstOrDefault(d => d.Origem == auto.Origem && d.Corrigido);
                despesas.Add(corrigida == null ? auto : auto with
                {
                    Data = corrigida.Data, Valor = corrigida.Valor, Parcela = corrigida.Parcela,
                    Observacao = corrigida.Observacao, Corrigida = true, Aviso = null,
                });
            }
            despesas.AddRange((rec?.Despesas ?? []).Where(d => d.Origem == "manual").OrderBy(d => d.Ordem)
                .Select(d => new LinhaDespesa(d.Data, d.Referencia, d.Valor, d.Parcela, d.Observacao, "manual",
                    false, false, null, null, null)));
        }
        foreach (var d in despesas.Where(d => d.Aviso != null && !d.Corrigida)) avisos.Add(d.Aviso!);

        var creditos = (rec?.Creditos ?? []).OrderBy(c => c.Id).Select(c => new LinhaCredito(c.Referencia, c.Valor)).ToList();
        var vales = (rec?.Vales ?? []).OrderBy(v => v.Data ?? DateOnly.MaxValue).ThenBy(v => v.Id)
            .Select(v => new LinhaVale(v.Data, v.Valor, v.Referencia)).ToList();

        // Saldo anterior: TOTAL do mês anterior se ele está no sistema; senão o digitado
        var (aa, ma) = MesAnterior(ano, mes);
        decimal saldo;
        string origem;
        var anterior = await db.FechamentosMensais.AsNoTracking().FirstOrDefaultAsync(f => f.MotoristaId == motoristaId && f.Ano == aa && f.Mes == ma);
        if (anterior != null && profundidade < 36)
        {
            saldo = (await MontarModeloAsync(mot, aa, ma, profundidade + 1)).Totais.Total;
            origem = "fechamento";
            if (!anterior.Fechado)
                avisos.Add($"O fechamento de {NomeMesAno(ma, aa)} ainda está aberto: o saldo anterior pode mudar.");
        }
        else if (rec?.SaldoAnteriorDigitado is { } digitado)
        {
            saldo = digitado;
            origem = "digitado";
        }
        else
        {
            saldo = 0;
            origem = "vazio";
            avisos.Add($"Falta o saldo anterior: não há fechamento de {NomeMesAno(ma, aa)} no sistema. Digite o valor.");
        }

        var t = CalcularTotais(linhasViagem.Select(v => v.Resultado), creditos.Select(c => c.Valor), despesas.Select(d => d.Valor), saldo);
        var comissao = Math.Round(linhasViagem.Sum(v => v.Comissao), 2);
        var totalVales = Math.Round(vales.Sum(v => v.Valor), 2);
        var totais = new TotaisFechamento(t.EntradaViagens, t.Creditos, t.EntradaTotal, t.SaidaTotal, t.Sobra, t.SaldoAnterior, t.Total,
            comissao, totalVales, SalarioLiquido(sal.Salario, sal.HorasExtras, comissao, sal.Quinquenio, sal.Inss, sal.Irr, totalVales));

        var placas = linhasViagem.Select(v => v.Placa).Where(p => p.Length > 0).Distinct().OrderBy(p => p).ToList();
        return new ModeloFechamento(motoristaId, mot.Nome, placas, ano, mes, rec != null, rec?.Id, fechado, rec?.FechadoEm, rec?.Observacao,
            linhasViagem, creditos, despesas, vales, sal, rec?.SaldoAnteriorDigitado, origem, totais, avisos);
    }

    private static string? DeOndeVem(string origem, int ano, int mes)
    {
        if (origem == "icms") { var (a, m) = MesAnterior(ano, mes); return $"Planilha SG de {NomeMesAno(m, a)}, placas com que ele viajou nesse mês"; }
        if (origem.StartsWith("consorcio:")) return "Cadastro de consórcio";
        if (origem.StartsWith("salario:")) return "Parte Salário";
        return null;
    }

    /// <summary>As linhas automáticas do mês, com o valor calculado agora.</summary>
    private async Task<List<LinhaDespesa>> AutomaticasAsync(int motoristaId, int ano, int mes, DadosSalarioFechamento sal)
    {
        var lista = new List<LinhaDespesa>();

        // ICMS/SEGURO CARGAS (Admin, 02/11/2026): soma, na SG do mês anterior, o total das placas
        // (cavalos) com que o motorista viajou naquele mês. Placa usada por outro motorista no mesmo
        // mês: avisa, para acertar à mão.
        var (aa, ma) = MesAnterior(ano, mes);
        var iniAnt = new DateOnly(aa, ma, 1);
        var fimAnt = iniAnt.AddMonths(1);
        var doMesAnterior = await db.ViagensCaminhao.AsNoTracking()
            .Where(v => v.DataSaida >= iniAnt && v.DataSaida < fimAnt && v.Placa != "")
            .Select(v => new { v.Placa, v.MotoristaId }).ToListAsync();
        var placasAnt = doMesAnterior.Where(v => v.MotoristaId == motoristaId).Select(v => v.Placa).Distinct().OrderBy(p => p).ToList();
        decimal? valor = null;
        var avisosIcms = new List<string>();
        if (placasAnt.Count == 0)
            avisosIcms.Add($"ICMS/SEGURO: ele não tem viagem em {NomeMesAno(ma, aa)}; digite o valor se houver");
        else
        {
            var resumo = await ResumoSgAsync(aa, ma);
            foreach (var placa in placasAnt)
            {
                var (v, aviso) = TotalSeguroDaPlaca(resumo, placa, ma, aa);
                if (v is { } x) valor = (valor ?? 0) + x;
                if (aviso != null) avisosIcms.Add(aviso);
                if (doMesAnterior.Any(o => o.Placa == placa && o.MotoristaId != motoristaId))
                    avisosIcms.Add($"ICMS/SEGURO: a {placa} também rodou com outro motorista em {NomeMesAno(ma, aa)}; o total dela entrou inteiro aqui, confira");
            }
        }
        lista.Add(new LinhaDespesa(null, "ICMS/SEGURO CARGAS", valor, null, placasAnt.Count > 0 ? string.Join(", ", placasAnt) : null,
            "icms", true, false, valor, DeOndeVem("icms", ano, mes), avisosIcms.Count > 0 ? string.Join(". ", avisosIcms) : null));

        // Consórcios dentro do prazo, das placas com que ele viajou no mês
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1);
        var placasMes = await db.ViagensCaminhao.AsNoTracking()
            .Where(v => v.MotoristaId == motoristaId && v.DataSaida >= ini && v.DataSaida < fim)
            .Select(v => v.Placa).Distinct().ToListAsync();
        var consorcios = await db.Consorcios.AsNoTracking().Where(c => placasMes.Contains(c.Placa) && c.Ativo).OrderBy(c => c.Id).ToListAsync();
        foreach (var c in consorcios)
        {
            if (NumeroParcela(c.PrimeiroAno, c.PrimeiroMes, c.TotalParcelas, ano, mes) is not { } n) continue;
            lista.Add(new LinhaDespesa(null, c.Descricao, c.ValorParcela, $"{n}/{c.TotalParcelas}", c.Cota,
                $"consorcio:{c.Id}", true, false, c.ValorParcela, DeOndeVem("consorcio:", ano, mes), null));
        }

        // Retenções do salário (D8..D11 = L5..L8)
        foreach (var (chave, nome, v) in new[]
        {
            ("FGTS", "FGTS", sal.Fgts), ("INSS", "INSS", sal.Inss), ("IRR", "IRR", sal.Irr), ("QUINQUENIO", "QUINQUÊNIO", sal.Quinquenio),
        })
            lista.Add(new LinhaDespesa(null, nome, v, null, null, $"salario:{chave}", true, false, v, DeOndeVem("salario:", ano, mes), null));
        return lista;
    }

    private async Task<PlanilhaSeguroLeitor.Resumo?> ResumoSgAsync(int ano, int mes)
    {
        var json = await db.PlanilhasSeguro.AsNoTracking().Where(p => p.Ano == ano && p.Mes == mes)
            .Select(p => p.ResumoJson).FirstOrDefaultAsync();
        if (json == null) return null;
        try
        {
            var r = JsonSerializer.Deserialize<PlanilhaSeguroLeitor.Resumo>(json, PlanilhaSeguroService.Json);
            if (r != null) return r;
        }
        catch (JsonException) { /* lê da própria planilha abaixo */ }
        var reg = await db.PlanilhasSeguro.AsNoTracking().FirstAsync(p => p.Ano == ano && p.Mes == mes);
        return PlanilhaSeguroService.ResumoDe(reg);
    }

    // ───────────────────────── Gravação ─────────────────────────

    private Task<FechamentoMensal?> CarregarAsync(int motoristaId, int ano, int mes) =>
        db.FechamentosMensais.Include(f => f.Despesas).Include(f => f.Creditos).Include(f => f.Vales).AsSplitQuery()
            .FirstOrDefaultAsync(f => f.MotoristaId == motoristaId && f.Ano == ano && f.Mes == mes);

    private static ErroViagem MesFechado() => new("O mês está fechado. Reabra para alterar.");

    public async Task SalvarAsync(FechamentoEntrada e)
    {
        var mot = await MotoristaValidoAsync(e.MotoristaId);
        MesValido(e.Ano, e.Mes);
        var rec = await CarregarAsync(mot.Id, e.Ano, e.Mes);
        if (rec?.Fechado == true) throw MesFechado();
        if (rec == null)
        {
            rec = new FechamentoMensal { MotoristaId = mot.Id, Placa = "", Ano = e.Ano, Mes = e.Mes };
            db.FechamentosMensais.Add(rec);
        }
        rec.SaldoAnteriorDigitado = e.SaldoAnteriorDigitado;
        rec.Salario = e.Salario.Salario; rec.HorasExtras = e.Salario.HorasExtras; rec.Fgts = e.Salario.Fgts;
        rec.Inss = e.Salario.Inss; rec.Irr = e.Salario.Irr; rec.Quinquenio = e.Salario.Quinquenio;
        rec.Observacao = string.IsNullOrWhiteSpace(e.Observacao) ? null : e.Observacao.Trim();

        db.FechamentoDespesas.RemoveRange(rec.Despesas); rec.Despesas.Clear();
        db.FechamentoCreditos.RemoveRange(rec.Creditos); rec.Creditos.Clear();
        db.FechamentoVales.RemoveRange(rec.Vales); rec.Vales.Clear();

        var ordem = 0;
        foreach (var d in e.Despesas ?? [])
        {
            var origem = d.Origem ?? "manual";
            var automatica = origem == "icms" || origem.StartsWith("consorcio:") || origem.StartsWith("salario:");
            // Automática só fica guardada quando corrigida à mão; senão é recalculada
            if (automatica && !d.Corrigida) continue;
            if (!automatica)
            {
                origem = "manual";
                if (string.IsNullOrWhiteSpace(d.Referencia) && d.Valor == null && d.Data == null) continue;
            }
            rec.Despesas.Add(new FechamentoDespesa
            {
                Data = d.Data, Referencia = (d.Referencia ?? "").Trim(), Valor = d.Valor,
                Parcela = Limpar(d.Parcela), Observacao = Limpar(d.Observacao),
                Origem = origem, Corrigido = automatica, Ordem = ordem++,
            });
        }
        foreach (var c in e.Creditos ?? [])
        {
            if (string.IsNullOrWhiteSpace(c.Referencia) && c.Valor == 0) continue;
            rec.Creditos.Add(new FechamentoCredito { Referencia = (c.Referencia ?? "").Trim(), Valor = c.Valor });
        }
        foreach (var v in e.Vales ?? [])
        {
            if (v.Valor == 0 && v.Data == null && string.IsNullOrWhiteSpace(v.Referencia)) continue;
            rec.Vales.Add(new FechamentoVale { Data = v.Data, Valor = v.Valor, Referencia = Limpar(v.Referencia) });
        }
        await db.SaveChangesAsync();
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Fecha o mês: grava o retrato das linhas automáticas e trava.</summary>
    public async Task FecharAsync(MesFechamentoEntrada e)
    {
        var mot = await MotoristaValidoAsync(e.MotoristaId);
        MesValido(e.Ano, e.Mes);
        var rec = await CarregarAsync(mot.Id, e.Ano, e.Mes);
        if (rec?.Fechado == true) throw new ErroViagem("O mês já está fechado.");
        if (rec == null)
        {
            rec = new FechamentoMensal { MotoristaId = mot.Id, Placa = "", Ano = e.Ano, Mes = e.Mes };
            db.FechamentosMensais.Add(rec);
            await db.SaveChangesAsync();
        }
        var modelo = await MontarModeloAsync(mot, e.Ano, e.Mes, 0);
        db.FechamentoDespesas.RemoveRange(rec.Despesas); rec.Despesas.Clear();
        var ordem = 0;
        foreach (var d in modelo.Despesas)
            rec.Despesas.Add(new FechamentoDespesa
            {
                Data = d.Data, Referencia = d.Referencia, Valor = d.Valor, Parcela = d.Parcela, Observacao = d.Observacao,
                Origem = d.Origem, Corrigido = d.Corrigida, Ordem = ordem++,
            });
        rec.Fechado = true;
        rec.FechadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Reabre: as automáticas voltam a ser recalculadas (as corrigidas à mão ficam).</summary>
    public async Task ReabrirAsync(MesFechamentoEntrada e)
    {
        var mot = await MotoristaValidoAsync(e.MotoristaId);
        MesValido(e.Ano, e.Mes);
        var rec = await CarregarAsync(mot.Id, e.Ano, e.Mes);
        if (rec == null || !rec.Fechado) throw new ErroViagem("O mês não está fechado.");
        db.FechamentoDespesas.RemoveRange(rec.Despesas.Where(d => d.Origem != "manual" && !d.Corrigido));
        rec.Fechado = false;
        rec.FechadoEm = null;
        await db.SaveChangesAsync();
    }

    // ───────────────────────── Despesas da planilha do acerto ─────────────────────────

    /// <summary>
    /// Aba DESPESAS DO MES do acerto entra sozinha no fechamento (VIA-29, UI-22): mescla com o que já
    /// está gravado (<see cref="FechamentoDespesasPlanilha.Mesclar"/>) e grava só a diferença, linha a
    /// linha (nunca o apagar-e-regravar do <see cref="SalvarAsync"/>). Mês fechado: não grava nada e
    /// devolve as diferenças. Não toca em vales, saldo anterior, salário, horas extras, observação
    /// nem no cadastro de consórcios.
    /// </summary>
    public async Task<object> ImportarDespesasDaPlanilhaAsync(int motoristaId, int ano, int mes, DespesasDoMesLidas lidas)
    {
        var mot = await MotoristaValidoAsync(motoristaId);
        MesValido(ano, mes);
        var rec = await CarregarAsync(mot.Id, ano, mes);
        var fechado = rec?.Fechado == true;
        var sal = rec == null ? new DadosSalarioFechamento(0, 0, 0, 0, 0, 0)
            : new DadosSalarioFechamento(rec.Salario, rec.HorasExtras, rec.Fgts, rec.Inss, rec.Irr, rec.Quinquenio);

        List<FechamentoDespesasPlanilha.LinhaAutomatica> automaticas;
        if (fechado)
            automaticas = [.. rec!.Despesas.Where(d => d.Origem != "manual").OrderBy(d => d.Ordem)
                .Select(d => new FechamentoDespesasPlanilha.LinhaAutomatica(d.Origem, d.Referencia, d.Valor, d.Parcela, d.Observacao))];
        else
            automaticas = [.. (await AutomaticasAsync(mot.Id, ano, mes, sal)).Select(a =>
            {
                // Como MontarModeloAsync: a linha corrigida vale no lugar da calculada
                var c = rec?.Despesas.FirstOrDefault(d => d.Origem == a.Origem && d.Corrigido);
                return c == null ? new FechamentoDespesasPlanilha.LinhaAutomatica(a.Origem, a.Referencia, a.Valor, a.Parcela, a.Observacao)
                    : new FechamentoDespesasPlanilha.LinhaAutomatica(a.Origem, a.Referencia, c.Valor, c.Parcela, c.Observacao);
            })];
        var estado = new FechamentoDespesasPlanilha.Estado(fechado,
            [.. (rec?.Despesas ?? []).Where(d => d.Origem == "manual")
                .Select(d => new FechamentoDespesasPlanilha.LinhaManual(d.Id, d.Data, d.Referencia, d.Valor, d.Parcela, d.Observacao, d.Ordem))],
            sal.Fgts, sal.Inss, sal.Irr, sal.Quinquenio, automaticas)
        {
            Creditos = [.. (rec?.Creditos ?? []).Select(c => new FechamentoDespesasPlanilha.CreditoGravado(c.Id, c.Referencia, c.Valor))],
        };
        var p = FechamentoDespesasPlanilha.Mesclar(estado, lidas.Linhas, lidas.Creditos);

        if (!fechado && !p.NadaAGravar)
        {
            if (rec == null)
            {
                rec = new FechamentoMensal { MotoristaId = mot.Id, Placa = "", Ano = ano, Mes = mes };
                db.FechamentosMensais.Add(rec);
            }
            var ordem = rec.Despesas.Count == 0 ? 0 : rec.Despesas.Max(d => d.Ordem) + 1;
            foreach (var l in p.Novas)
                rec.Despesas.Add(new FechamentoDespesa
                {
                    Data = l.Data, Referencia = l.Referencia, Valor = l.Valor, Parcela = l.Parcela, Observacao = l.Observacao,
                    Origem = "manual", Ordem = ordem++,
                });
            foreach (var s in p.Substituidas)
            {
                var d = rec.Despesas.First(x => x.Id == s.Id);
                d.Valor = s.Lida.Valor;
                if (s.Lida.Data != null) d.Data = s.Lida.Data;
                if (s.Lida.Parcela != null) d.Parcela = s.Lida.Parcela;
                if (s.Lida.Observacao != null) d.Observacao = s.Lida.Observacao;
            }
            foreach (var c in p.CamposSalario)
                switch (c.Campo)
                {
                    case "Fgts": rec.Fgts = c.Atual; break;
                    case "Inss": rec.Inss = c.Atual; break;
                    case "Irr": rec.Irr = c.Atual; break;
                    case "Quinquenio": rec.Quinquenio = c.Atual; break;
                }
            foreach (var a in p.AutomaticasCorrigidas)
            {
                var d = rec.Despesas.FirstOrDefault(x => x.Origem == a.Origem && x.Corrigido) ?? rec.Despesas.FirstOrDefault(x => x.Origem == a.Origem);
                if (d == null)
                    rec.Despesas.Add(new FechamentoDespesa
                    {
                        Referencia = a.Referencia, Valor = a.Valor, Parcela = a.Parcela, Observacao = a.Observacao,
                        Origem = a.Origem, Corrigido = true, Ordem = ordem++,
                    });
                else { d.Valor = a.Valor; d.Corrigido = true; }
            }
            foreach (var g in p.GemeasARetirar)
            {
                var d = rec.Despesas.First(x => x.Id == g.Id);
                rec.Despesas.Remove(d);
                db.FechamentoDespesas.Remove(d);
            }
            foreach (var c in p.CreditosNovos)
                rec.Creditos.Add(new FechamentoCredito { Referencia = c.Referencia, Valor = c.Valor });
            foreach (var c in p.CreditosSubstituidos)
                rec.Creditos.First(x => x.Id == c.Id).Valor = c.Atual;
            await db.SaveChangesAsync();
        }

        return new
        {
            motoristaId = (int?)mot.Id, motorista = (string?)mot.Nome, ano, mes, situacao = fechado ? "fechado" : "aberto", motivo = (string?)null,
            novas = p.Novas.Select(l => new { referencia = l.Referencia, valor = l.Valor, parcela = l.Parcela, observacao = l.Observacao }).ToList(),
            substituidas = p.Substituidas.Select(s => new
            {
                referencia = s.Lida.Referencia,
                trocas = s.Trocas.Select(t => new { campo = t.Campo, anterior = t.Anterior, atual = t.Atual }).ToList(),
            }).ToList(),
            iguais = p.Iguais,
            soNoSistema = p.SoNoSistema.Select(m => new { referencia = m.Referencia, valor = m.Valor }).ToList(),
            salario = p.CamposSalario.Select(c => new { campo = c.Campo, anterior = c.Anterior, atual = c.Atual }).ToList(),
            automaticas = p.AutomaticasCorrigidas.Select(a => new { referencia = a.Referencia, anterior = a.Anterior, atual = a.Valor }).ToList(),
            retiradas = p.GemeasARetirar.Select(m => new { referencia = m.Referencia, valor = m.Valor }).ToList(),
            creditosNovos = p.CreditosNovos.Select(c => new { referencia = c.Referencia, valor = c.Valor }).ToList(),
            creditosSubstituidos = p.CreditosSubstituidos.Select(c => new { referencia = c.Referencia, anterior = c.Anterior, atual = c.Atual }).ToList(),
            creditosIguais = p.CreditosIguais,
            diferencas = p.Diferencas,
            avisos = p.Avisos,
        };
    }

    /// <summary>Resultado quando as despesas da planilha não entram sozinhas (motivo sempre na tela, UI-04).</summary>
    public static object DespesasNaoImportadas(int? motoristaId, string? motorista, int ano, int mes, string motivo) => new
    {
        motoristaId, motorista, ano, mes, situacao = "nao_importado", motivo,
        novas = Array.Empty<object>(), substituidas = Array.Empty<object>(), iguais = 0, soNoSistema = Array.Empty<object>(),
        salario = Array.Empty<object>(), automaticas = Array.Empty<object>(), retiradas = Array.Empty<object>(),
        creditosNovos = Array.Empty<object>(), creditosSubstituidos = Array.Empty<object>(), creditosIguais = 0,
        diferencas = Array.Empty<string>(), avisos = Array.Empty<string>(),
    };

    // ───────────────────────── Consórcios ─────────────────────────

    public Task<List<Consorcio>> ConsorciosAsync(string? placa)
    {
        var q = db.Consorcios.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(placa)) { var p = NormalizarPlaca(placa); q = q.Where(c => c.Placa == p); }
        return q.OrderBy(c => c.Placa).ThenBy(c => c.Id).ToListAsync();
    }

    private static void Preencher(Consorcio c, ConsorcioEntrada e)
    {
        c.Placa = PlacaValida(e.Placa);
        c.Descricao = (e.Descricao ?? "").Trim();
        if (c.Descricao.Length == 0) throw new ErroViagem("Informe a descrição do consórcio.");
        if (e.ValorParcela <= 0) throw new ErroViagem("Informe o valor da parcela.");
        if (e.TotalParcelas < 1) throw new ErroViagem("Informe o total de parcelas.");
        MesValido(e.PrimeiroAno, e.PrimeiroMes);
        c.Cota = Limpar(e.Cota);
        c.ValorParcela = e.ValorParcela;
        c.TotalParcelas = e.TotalParcelas;
        c.PrimeiroAno = e.PrimeiroAno;
        c.PrimeiroMes = e.PrimeiroMes;
        c.Ativo = e.Ativo;
    }

    public async Task<Consorcio> SalvarConsorcioAsync(int? id, ConsorcioEntrada e)
    {
        var c = id == null ? new Consorcio() : await db.Consorcios.FindAsync(id.Value)
            ?? throw new ErroViagem("Consórcio não encontrado.");
        Preencher(c, e);
        if (id == null) db.Consorcios.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    public async Task ExcluirConsorcioAsync(int id)
    {
        var c = await db.Consorcios.FindAsync(id) ?? throw new ErroViagem("Consórcio não encontrado.");
        db.Consorcios.Remove(c);
        await db.SaveChangesAsync();
    }
}
