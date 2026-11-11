using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.FretePerfilCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Queda do volume da Girassol com banco (BOL-17): acha o .txt da carga (PlanilhasCarga), monta as linhas por tipo
/// pelos XML guardados (ArquivosNFe), aplica a queda lançada (FretePerfilAjustes), encadeia o saldo entre as
/// cargas e grava o bloco "QUEDA DO VOLUME" no fim do próprio .txt. O cálculo das notas nunca muda.
/// </summary>
public class FretePerfilService(AppDbContext db, TiposVolumeService tiposVolume)
{
    public const decimal QuedaMaxima = 999.99m;

    /// <summary>Fotografia do que saiu no boleto, para o e-mail repetir os mesmos números.</summary>
    public record Emitido(decimal FreteBruto, decimal DescontoTotal, decimal SaldoAbatido, decimal Valor,
        List<string> Tabela, DateTime Em, string Por);

    /// <summary>A carga com o cálculo, a queda guardada e a posição na cadeia do saldo.</summary>
    public record CalculoCarga(DateOnly Data, string Placa, string? Arquivo, RelatorioFreteLeitor.Relatorio? Relatorio,
        Calculo Calculo, TotaisCarga Totais, PosicaoCadeia? Posicao, bool TemBoleto,
        IReadOnlyDictionary<string, decimal> Descontos, Emitido? Emitido, DateTime? AtualizadoEm, string? AtualizadoPor,
        List<string> Avisos, bool? HouveQueda = null);

    // Uma carga = o .txt de frete mais recente da data + placa
    private record Carga(DateOnly Data, string Placa, int PlanilhaId, string Nome);
    private record Montada(Carga Carga, RelatorioFreteLeitor.Relatorio Rel, Calculo Calc);

    private List<Carga>? cargas;
    private List<PosicaoCadeia>? cadeia;
    private readonly Dictionary<(int PlanilhaId, bool ComTipos), Montada> montadas = new();
    private IReadOnlyDictionary<string, string>? escritas;
    private Dictionary<string, string>? nomesExtenso;

    // ── Leitura ─────────────────────────────────────────────────────────────

    /// <summary>
    /// O cálculo da carga: .txt mais recente da data e placa (como BoletoMontagemService.Montar), linhas por tipo
    /// pelos XML guardados, queda guardada aplicada e a posição na cadeia do saldo.
    /// </summary>
    public async Task<CalculoCarga> ObterAsync(DateOnly data, string? placa)
    {
        var pl = Norm(placa);
        var avisos = new List<string>();
        var (carga, avisoCarga) = await AcharCargaAsync(data, pl);
        if (avisoCarga != null) avisos.Add(avisoCarga);
        var ajuste = await db.FretePerfilAjustes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.DataCarga == data && a.Placa == (carga != null ? carga.Placa : pl));
        var descontos = LerDescontos(ajuste?.DescontosJson);
        var emitido = LerEmitido(ajuste?.EmitidoJson);

        if (carga == null)
        {
            var vazio = new Calculo(new(), new());
            return new CalculoCarga(data, pl, null, null, vazio, Totais(vazio), null, false, descontos, emitido,
                ajuste?.AtualizadoEm, ajuste?.AtualizadoPor, avisos, Marcacao(ajuste, descontos));
        }

        var m = await MontarAsync(carga, comTipos: true);
        var calc = AplicarDescontos(m.Calc, descontos);
        var pos = await PosicaoAsync(carga, calc);
        avisos.AddRange(m.Rel.Avisos);
        avisos.AddRange(calc.Avisos);
        avisos.AddRange(pos.Avisos);
        return new CalculoCarga(data, carga.Placa, carga.Nome, m.Rel, calc, Totais(calc), pos, pos.TemBoleto,
            descontos, emitido, ajuste?.AtualizadoEm, ajuste?.AtualizadoPor, avisos.Distinct().ToList(),
            Marcacao(ajuste, descontos));
    }

    // "Houve queda do volume?": queda lançada = sim; senão o que foi marcado (nulo = ainda não marcado)
    private static bool? Marcacao(FretePerfilAjuste? ajuste, IReadOnlyDictionary<string, decimal> descontos) =>
        descontos.Count > 0 ? true : ajuste?.HouveQueda;

    private List<FretePerfilAjuste>? marcacoes;

    /// <summary>A marcação "Houve queda do volume?" da carga, sem montar o cálculo (lista Para emitir).</summary>
    public async Task<bool?> HouveQuedaAsync(DateOnly data, string? placa)
    {
        marcacoes ??= await db.FretePerfilAjustes.AsNoTracking().ToListAsync();
        var pl = Norm(placa);
        var a = marcacoes.FirstOrDefault(x => x.DataCarga == data && x.Placa == pl);
        return Marcacao(a, LerDescontos(a?.DescontosJson));
    }

    /// <summary>
    /// Cadeia do saldo de todas as cargas da Girassol a partir da primeira com queda lançada, com os boletos GIRASSOL
    /// casados pela mesma regra do Montar (mesma placa, um dos dois sem placa, placa errada do BOL-16).
    /// Calculada uma vez por requisição. Sem queda lançada: lista vazia (cada carga vale sozinha).
    /// </summary>
    public async Task<List<PosicaoCadeia>> CadeiaAsync()
    {
        if (cadeia != null) return cadeia;
        var ajustes = (await db.FretePerfilAjustes.AsNoTracking()
                .Where(a => a.DescontosJson != null).ToListAsync())
            .Where(a => LerDescontos(a.DescontosJson).Count > 0).ToList();
        if (ajustes.Count == 0) return cadeia = new();

        var desde = ajustes.Min(a => a.DataCarga);
        var todas = await CargasAsync();
        var lista = new List<CargaCadeia>();
        var boletos = await BoletosAsync(desde);
        foreach (var c in todas.Where(c => c.Data >= desde))
        {
            var desc = LerDescontos(ajustes.FirstOrDefault(a => a.DataCarga == c.Data && a.Placa == c.Placa)?.DescontosJson);
            var m = await MontarAsync(c, comTipos: desc.Count > 0);
            var tot = Totais(AplicarDescontos(m.Calc, desc));
            var bol = BoletoDaCarga(c, boletos, todas);
            lista.Add(new CargaCadeia(c.Data, c.Placa, tot.Devido, tot.DescontoTotal, bol == null ? null : bol.Valor ?? tot.Devido));
        }
        return cadeia = Encadear(lista);
    }

    // ── Gravação ────────────────────────────────────────────────────────────

    /// <summary>
    /// Guarda a queda por caixa de cada tipo normal (vazio ou zero = sem queda; produtoE, produtoD e tipo fora da
    /// carga ficam de fora) e regrava o .txt da carga e das seguintes sem boleto com o bloco "QUEDA DO VOLUME"
    /// (sem queda e sem saldo abatido: o .txt volta sem o bloco). Queda fora de 0 a 999,99: ArgumentException.
    /// </summary>
    public async Task<CalculoCarga> SalvarAsync(DateOnly data, string? placa, IReadOnlyDictionary<string, decimal>? descontos,
        string usuario)
    {
        var pl = Norm(placa);
        var (carga, aviso) = await AcharCargaAsync(data, pl);
        if (carga == null) throw new InvalidOperationException(aviso ?? "Carga da Girassol não encontrada.");

        foreach (var (tipo, v) in descontos ?? new Dictionary<string, decimal>())
            if (v < 0 || v > QuedaMaxima || decimal.Round(v, 2) != v)
                throw new ArgumentException($"Queda por caixa de \"{tipo}\" inválida: de R$ 0,00 a R$ 999,99, com até duas casas.");

        // Só o que entra na conta: tipo normal desta carga, valor > 0
        var m = await MontarAsync(carga, comTipos: true);
        var validos = (descontos ?? new Dictionary<string, decimal>())
            .Where(d => d.Value > 0)
            .Select(d => (Linha: m.Calc.Linhas.FirstOrDefault(l => Mesmo(l.Tipo, d.Key)), d.Value))
            .Where(x => x.Linha != null && !SemQueda(x.Linha))
            .GroupBy(x => x.Linha!.Tipo)
            .ToDictionary(g => g.Key, g => g.Last().Value);

        var ajuste = await db.FretePerfilAjustes.FirstOrDefaultAsync(a => a.DataCarga == carga.Data && a.Placa == carga.Placa);
        if (ajuste == null) db.FretePerfilAjustes.Add(ajuste = new FretePerfilAjuste { DataCarga = carga.Data, Placa = carga.Placa });
        ajuste.DescontosJson = validos.Count == 0 ? null : JsonSerializer.Serialize(validos);
        ajuste.HouveQueda = validos.Count > 0;   // salvar sem queda = marcou "não houve"
        ajuste.AtualizadoEm = DateTime.UtcNow;
        ajuste.AtualizadoPor = usuario ?? "";
        await db.SaveChangesAsync();

        cadeia = null;
        marcacoes = null;
        await RegravarTxtAsync(carga);
        var res = await ObterAsync(carga.Data, carga.Placa);
        // Os avisos de tipo ignorado só aparecem aqui (o que foi guardado já vem limpo)
        var ignorados = AplicarDescontos(m.Calc, descontos?.Where(d => d.Value > 0).ToDictionary(d => d.Key, d => d.Value)).Avisos
            .Except(m.Calc.Avisos);
        return res with { Avisos = res.Avisos.Concat(ignorados).Distinct().ToList() };
    }

    /// <summary>Guarda a fotografia do boleto emitido (valor e tabelinha da queda) para o e-mail repetir os números.</summary>
    public async Task<Emitido> GravarEmitidoAsync(DateOnly data, string? placa, decimal valorEmitido, string usuario)
    {
        var c = await ObterAsync(data, placa);
        var abatido = c.Posicao?.SaldoAbatido ?? 0m;
        var em = new Emitido(c.Totais.FreteBruto, c.Totais.DescontoTotal, abatido, valorEmitido,
            Tabela(c.Calculo, abatido), DateTime.UtcNow, usuario ?? "");
        var ajuste = await db.FretePerfilAjustes.FirstOrDefaultAsync(a => a.DataCarga == data && a.Placa == c.Placa);
        if (ajuste == null) db.FretePerfilAjustes.Add(ajuste = new FretePerfilAjuste { DataCarga = data, Placa = c.Placa });
        ajuste.EmitidoJson = JsonSerializer.Serialize(em);
        ajuste.AtualizadoEm = DateTime.UtcNow;
        ajuste.AtualizadoPor = usuario ?? "";
        await db.SaveChangesAsync();
        return em;
    }

    /// <summary>
    /// Bloco da queda no .txt desta carga e das seguintes ainda sem boleto (o saldo abatido delas muda junto).
    /// Carga seguinte já com boleto é fechada: não mexe.
    /// </summary>
    private async Task RegravarTxtAsync(Carga desta)
    {
        var ajustes = await db.FretePerfilAjustes.AsNoTracking().Where(a => a.DataCarga >= desta.Data).ToListAsync();
        foreach (var c in (await CargasAsync()).Where(c => c.Data > desta.Data || c == desta
                     || (c.Data == desta.Data && string.CompareOrdinal(c.Placa, desta.Placa) > 0)))
        {
            var desc = LerDescontos(ajustes.FirstOrDefault(a => a.DataCarga == c.Data && a.Placa == c.Placa)?.DescontosJson);
            var m = await MontarAsync(c, comTipos: desc.Count > 0);
            var calc = AplicarDescontos(m.Calc, desc);
            var pos = await PosicaoAsync(c, calc);
            if (pos.TemBoleto && c != desta) continue;

            string? bloco = null;
            if (desc.Count > 0 || pos.SaldoAbatido > 0)
            {
                var devido = Totais(calc).Devido;
                // Já emitido: vale o emitido, a não ser que tenha saído sem a queda (aí o certo é o devido)
                var valor = pos.TemBoleto && pos.ValorBoleto is decimal v && v > devido ? devido : pos.ValorBoleto;
                bloco = BlocoQueda(calc, pos.SaldoAbatido, valor, pos.SaldoALevar);
            }
            var reg = await db.PlanilhasCarga.FirstAsync(p => p.Id == c.PlanilhaId);
            var texto = Encoding.UTF8.GetString(reg.Conteudo);
            var bom = texto.StartsWith('﻿');
            var novo = ComQueda(bom ? texto[1..] : texto, bloco);
            if (bom) novo = '﻿' + novo;
            if (novo == texto) continue;
            reg.Conteudo = Encoding.UTF8.GetBytes(novo);
            await db.SaveChangesAsync();
        }
    }

    // ── Peças ───────────────────────────────────────────────────────────────

    private async Task<PosicaoCadeia> PosicaoAsync(Carga c, Calculo calc)
    {
        var p = (await CadeiaAsync()).FirstOrDefault(x => x.Data == c.Data && x.Placa == c.Placa);
        if (p != null) return p;
        // Fora da cadeia (antes da primeira queda, ou nenhuma lançada): vale sozinha
        var tot = Totais(calc);
        var todas = await CargasAsync();
        var bol = BoletoDaCarga(c, await BoletosAsync(c.Data, c.Data), todas);
        return Encadear([new CargaCadeia(c.Data, c.Placa, tot.Devido, tot.DescontoTotal, bol == null ? null : bol.Valor ?? tot.Devido)])[0];
    }

    private record BoletoDia(int Id, DateOnly Data, string? Placa, decimal? Valor);

    private async Task<List<BoletoDia>> BoletosAsync(DateOnly desde, DateOnly? ate = null) =>
        (await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Cliente == BoletoReceberLeitor.Perfil && b.DataCarga != null && b.DataCarga >= desde
                && (ate == null || b.DataCarga <= ate))
            .OrderByDescending(b => b.Id)
            .Select(b => new { b.Id, b.DataCarga, b.Placa, b.Valor })
            .ToListAsync())
        .Select(b => new BoletoDia(b.Id, b.DataCarga!.Value, b.Placa, b.Valor)).ToList();

    /// <summary>Boleto desta carga com a regra do Montar (BoletoMontagemService.BoletoMesmaPlaca / BoletoPlacaErrada).</summary>
    private static BoletoDia? BoletoDaCarga(Carga c, List<BoletoDia> boletos, List<Carga> todas)
    {
        var doDia = boletos.Where(b => b.Data == c.Data).ToList();
        if (doDia.Count == 0) return null;
        var placa = c.Placa.Length == 0 ? null : c.Placa;
        var mesmo = BoletoMontagemService.BoletoMesmaPlaca(doDia, b => b.Placa, placa);
        if (mesmo != null || placa == null) return mesmo;
        var placasDoDia = todas.Where(x => x.Data == c.Data && x.Placa.Length > 0).Select(x => x.Placa).ToHashSet();
        return BoletoMontagemService.BoletoPlacaErrada(doDia, b => b.Placa, placa, placasDoDia);
    }

    /// <summary>Todas as cargas da Girassol: um .txt de frete por data + placa, o mais recente (como o ParaEmitir).</summary>
    private async Task<List<Carga>> CargasAsync()
    {
        if (cargas != null) return cargas;
        var nomes = await db.PlanilhasCarga.AsNoTracking().Select(p => new { p.Id, p.Nome, p.CriadoEm }).ToListAsync();
        return cargas = nomes
            .Select(p => new { p.Id, p.Nome, p.CriadoEm, Ident = RomaneioClienteLeitor.LerNome(p.Nome) })
            .Where(p => p.Ident.Tipo == "frete" && p.Ident.Data != null)
            .GroupBy(p => (Data: DateOnly.FromDateTime(p.Ident.Data!.Value), Placa: Norm(p.Ident.Placa)))
            .Select(g =>
            {
                var e = g.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id).First();
                return new Carga(g.Key.Data, g.Key.Placa, e.Id, e.Nome);
            })
            .OrderBy(c => c.Data).ThenBy(c => c.Placa, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A carga pedida. Com placa: só a daquela placa (nunca cai em outro caminhão, a queda grava no .txt).
    /// Sem placa: a mais recente do dia, avisando quando há mais de uma.
    /// </summary>
    private async Task<(Carga? Carga, string? Aviso)> AcharCargaAsync(DateOnly data, string placa)
    {
        var doDia = (await CargasAsync()).Where(c => c.Data == data).ToList();
        var dataBr = data.ToString("dd/MM/yyyy");
        if (placa.Length > 0)
        {
            var c = doDia.FirstOrDefault(x => x.Placa == placa);
            return c != null ? (c, null)
                : (null, $"Relatório de frete da Girassol de {dataBr} ({placa}) não encontrado — gere no Calcular frete.");
        }
        if (doDia.Count == 0) return (null, $"Relatório de frete da Girassol de {dataBr} não encontrado — gere no Calcular frete.");
        var nomes = await db.PlanilhasCarga.AsNoTracking().Where(p => doDia.Select(d => d.PlanilhaId).Contains(p.Id))
            .Select(p => new { p.Id, p.CriadoEm }).ToListAsync();
        var esc = doDia.OrderByDescending(d => nomes.First(n => n.Id == d.PlanilhaId).CriadoEm).ThenByDescending(d => d.PlanilhaId).First();
        return (esc, doDia.Count > 1 ? $"Há {doDia.Count} relatórios de frete em {dataBr}; valeu o mais recente, {esc.Nome}." : null);
    }

    /// <summary>Lê o .txt e monta as linhas; com tipos, pelos XML guardados das notas normais.</summary>
    private async Task<Montada> MontarAsync(Carga c, bool comTipos)
    {
        if (montadas.TryGetValue((c.PlanilhaId, comTipos), out var ja)) return ja;
        var conteudo = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Id == c.PlanilhaId).Select(p => p.Conteudo).FirstAsync();
        var rel = RelatorioFreteLeitor.Ler(conteudo, c.Nome);
        var tipos = comTipos ? await TiposPorNotaAsync(rel, c.Data) : null;
        var calc = MontarLinhas(rel, tipos?.Tipos);
        if (tipos != null && tipos.Value.Avisos.Count > 0) calc = calc with { Avisos = calc.Avisos.Concat(tipos.Value.Avisos).ToList() };
        return montadas[(c.PlanilhaId, comTipos)] = new Montada(c, rel, calc);
    }

    /// <summary>
    /// Tipos de volume de cada nota normal do .txt pelos XML guardados (número da NF, emissão perto da data da carga,
    /// mesmo emitente quando há mais de um). Nome mostrado = nome por extenso do tipo, senão o do sistema.
    /// </summary>
    private async Task<(Dictionary<string, IReadOnlyList<TipoNota>> Tipos, List<string> Avisos)?> TiposPorNotaAsync(
        RelatorioFreteLeitor.Relatorio rel, DateOnly data)
    {
        var normais = (rel.Linhas ?? new()).Where(l => !l.ProdutoE && l.NF != null).ToList();
        if (normais.Count == 0) return null;
        var nfs = normais.Select(l => l.NF!).Distinct().ToList();
        var de = data.AddDays(-7);
        var ate = data.AddDays(1);
        var achados = await db.ArquivosNFe.AsNoTracking()
            .Where(a => nfs.Contains(a.NumeroNF) && a.DataEmissao != null && a.DataEmissao >= de && a.DataEmissao <= ate)
            .Select(a => new { a.Id, a.NumeroNF, a.DataEmissao, a.Emitente })
            .ToListAsync();

        escritas ??= await tiposVolume.EscritasAsync();
        nomesExtenso ??= (await db.TiposVolume.AsNoTracking().Select(t => new { t.Nome, t.NomeExtenso }).ToListAsync())
            .GroupBy(t => t.Nome).ToDictionary(g => g.Key, g => g.First().NomeExtenso is { Length: > 0 } e ? e.Trim() : g.Key);

        var tipos = new Dictionary<string, IReadOnlyList<TipoNota>>();
        var avisos = new List<string>();
        foreach (var l in normais)
        {
            var esc = achados.Where(a => a.NumeroNF == l.NF)
                .OrderByDescending(a => Mesmo(a.Emitente, l.Emitente))
                .ThenBy(a => Math.Abs(a.DataEmissao!.Value.DayNumber - data.DayNumber))
                .FirstOrDefault();
            if (esc == null) continue;   // MontarLinhas avisa a nota sem XML
            var xml = await db.ArquivosNFe.AsNoTracking().Where(a => a.Id == esc.Id).Select(a => a.Xml).FirstAsync();
            NFeData nota;
            try { nota = NFeImportService.ParseXml(Encoding.UTF8.GetString(xml).TrimStart('﻿')); }
            catch (Exception) { avisos.Add($"Não consegui ler o XML da NF {l.NF}."); continue; }
            var porTipo = new List<TipoNota>();
            foreach (var p in nota.Produtos)
            {
                var tipo = NFeImportService.TipoDaEscrita(p.Original, escritas);
                if (tipo == null) { avisos.Add($"NF {l.NF}: \"{p.Original}\" sem tipo de volume cadastrado (Tipos de volumes)."); continue; }
                var nome = nomesExtenso.TryGetValue(tipo, out var n) ? n : tipo;
                var i = porTipo.FindIndex(t => t.Tipo == nome);
                if (i < 0) porTipo.Add(new TipoNota(nome, p.QCom));
                else porTipo[i] = porTipo[i] with { Caixas = porTipo[i].Caixas + p.QCom };
            }
            tipos[l.NF!] = porTipo;
        }
        return (tipos, avisos);
    }

    // ── Formatos ────────────────────────────────────────────────────────────

    private static string Norm(string? placa) =>
        string.IsNullOrWhiteSpace(placa) ? "" : EmailClienteService.Placa(placa);

    private static bool Mesmo(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, decimal> LerDescontos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)?.Where(d => d.Value > 0).ToDictionary(d => d.Key, d => d.Value) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static Emitido? LerEmitido(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Emitido>(json); }
        catch (JsonException) { return null; }
    }
}
