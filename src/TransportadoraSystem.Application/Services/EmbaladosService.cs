using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Módulo Embalados (VOLUME-16..18): quebras, despesas, comissões e salários, que
/// eram as abas da planilha COMISSÕES VOLUMES EMBALADOS.
/// </summary>
public class EmbaladosService(AppDbContext db)
{
    public const string OrigemPlanilha = "planilha";
    public const string PrefixoOrigemProdutoA = "produtoA:";

    // ─────────────────── ProdutosA do embalado (VOLUME-20) ───────────────────

    /// <summary>Cliente de estoque que é o nosso embalado ("EMBALADO", "EMBALADOS").</summary>
    public static bool EhEmbalado(string? nome) => Normalizar(nome).Replace(" ", "").StartsWith("EMBALAD");

    /// <summary>Como a planilha escrevia: "PRODUTOSA - (25.200) - R$ 16,34".</summary>
    public static string DescricaoProdutoA(int gramas, decimal custoKg) =>
        $"PRODUTOSA - ({gramas.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))}) - R$ {Dinheiro(custoKg)}";

    /// <summary>
    /// A produtoA que sai para o embalado é negócio nosso: vale o custo (sem comissão)
    /// e vira despesa embalados de kg × custo por kg da carga (Admin, 20/10/2026).
    /// Deixa as despesas "produtoA:ID" iguais às saídas; a linha PRODUTOSA do mesmo dia
    /// que veio da planilha ou foi lançada à mão dá lugar à da saída.
    /// </summary>
    public async Task SincronizarProdutosAEmbaladoAsync()
    {
        // O custo por kg é de cada carga (cada controle de produtosA): vale o da carga de onde saiu
        var vendas = (await db.VendasProdutoA.Include(v => v.Cliente).Include(v => v.ProdutosA).Include(v => v.Carga).ToListAsync())
            .Where(v => EhEmbalado(v.Cliente?.Nome)).ToList();
        var daProdutoA = await db.DespesasEmbalados.Where(d => d.Origem.StartsWith(PrefixoOrigemProdutoA)).ToListAsync();
        var dias = vendas.Select(v => v.Data).ToHashSet();
        var outras = (await db.DespesasEmbalados.Where(d => !d.Origem.StartsWith(PrefixoOrigemProdutoA) && dias.Contains(d.Data)).ToListAsync())
            .Where(d => Normalizar(d.Descricao).StartsWith("PRODUTOA")).ToList();
        db.DespesasEmbalados.RemoveRange(outras);

        foreach (var v in vendas)
        {
            var custo = v.Carga?.CustoKg ?? v.CustoKg;
            if (v.CustoKg != custo || v.PrecoKg != custo) { v.CustoKg = custo; v.PrecoKg = custo; v.AtualizadoEm = DateTime.UtcNow; }
            var gramas = v.ProdutosA.Sum(b => b.PesoGramas);
            var origem = PrefixoOrigemProdutoA + v.Id;
            var d = daProdutoA.FirstOrDefault(x => x.Origem == origem);
            if (d == null) { d = new DespesaEmbalados { Origem = origem }; db.DespesasEmbalados.Add(d); }
            else daProdutoA.Remove(d);
            var valor = gramas / 1000m * v.CustoKg;
            var descricao = DescricaoProdutoA(gramas, v.CustoKg);
            if (d.Data != v.Data || d.Descricao != descricao || d.Valor != valor)
            {
                d.Data = v.Data; d.Descricao = descricao; d.Valor = valor; d.AtualizadoEm = DateTime.UtcNow;
            }
        }
        // Saída excluída: a despesa dela sai junto
        db.DespesasEmbalados.RemoveRange(daProdutoA);
        await db.SaveChangesAsync();
    }

    // ───────────────────────────── Quebras ─────────────────────────────

    public record QuebraLinha(
        int? Id, DateOnly Data, string Cor, decimal ProdutosC, decimal? PrecoCaixa,
        string DePreco,            // "planilha" | "manual" | "romaneio" | "falta"
        decimal Valor, int? ControleId, int? ControleNumero, int? Romaneio, decimal? PrecoRomaneio,
        decimal? DescontoCaixa, int? AcertoNumero, string? Aviso, string? Observacao);

    private record PrecoDoRomaneio(int Numero, DateOnly Data, decimal PrecoCaixa, decimal? Desconto, int? AcertoNumero);

    /// <summary>
    /// As quebras do período (VOLUME-17). Dia com controle de embalados: as produtosC
    /// vêm da linha de quebra do controle (Ab, Ab-20 e Bb = branco; Av = vermelho);
    /// o preço digitado (ou o da planilha) vale, senão sai do romaneio de embalar:
    /// valor por caixa − desconto por caixa do acerto. Dia sem controle: a linha
    /// guardada (planilha ou lançada à mão).
    /// </summary>
    public async Task<List<QuebraLinha>> QuebrasAsync(DateOnly? de, DateOnly? ate)
    {
        var guardadas = await db.QuebrasVolumes.AsNoTracking()
            .Where(q => (de == null || q.Data >= de) && (ate == null || q.Data <= ate)).ToListAsync();
        var controles = await db.VolumesControles.AsNoTracking().Include(c => c.Linhas)
            .Where(c => (de == null || c.Data >= de) && (ate == null || c.Data <= ate)).ToListAsync();
        var precos = await PrecosDosRomaneiosAsync();
        var doAcerto = await RomaneiosDosAcertosAsync();

        var lista = new List<QuebraLinha>();
        var usadas = new HashSet<int>();
        foreach (var c in controles)
        {
            var q = c.Linhas.FirstOrDefault(l => l.Grupo == ControleVolumesLinha.Quebra);
            if (q == null) continue;   // sem quebra anotada: vale a linha guardada (controles 319 e 321)
            var entradas = c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Entrada)
                .Select(l => RomaneiosEmbalarService.NumeroDaOrigem(l.Origem)).OfType<int>().ToHashSet();
            // Entrada sem o número do romaneio (controles de set/2026 vindos da planilha): acha a linha do
            // acerto Alfa com as mesmas caixas e data do dia ou até 3 dias antes (VOLUME-17)
            foreach (var e in c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Entrada && l.Origem == null && l.Data != null))
                foreach (var (corE, band) in new[] { (QuebraVolumes.Branco, e.Ab + e.Ab20 + e.Bb), (QuebraVolumes.Vermelho, e.Av) })
                {
                    if (band <= 0 || band % 12 != 0) continue;
                    var cx = band / 12;
                    var achados = doAcerto.Where(r => r.Caixas == cx && r.Data <= e.Data!.Value && r.Data >= e.Data.Value.AddDays(-3)).ToList();
                    if (achados.Count == 0) continue;
                    var maisPerto = achados.Max(r => r.Data);
                    achados = achados.Where(r => r.Data == maisPerto).ToList();
                    if (achados.Count != 1) continue;
                    var r = achados[0];
                    var daCorE = precos.TryGetValue(corE, out var l0) ? l0 : precos[corE] = [];
                    if (!daCorE.Any(x => x.Numero == r.Numero))
                        daCorE.Add(new PrecoDoRomaneio(r.Numero, r.Data, Math.Round(r.Valor / r.Caixas, 2), r.Desconto, r.AcertoNumero));
                    entradas.Add(r.Numero);
                }
            foreach (var (cor, produtosC) in new[] { (QuebraVolumes.Branco, q.Ab + q.Ab20 + q.Bb), (QuebraVolumes.Vermelho, q.Av) })
            {
                var g = guardadas.FirstOrDefault(x => x.Data == c.Data && x.Cor == cor);
                if (produtosC <= 0 && g?.ProdutosC is not > 0) continue;
                if (g != null) usadas.Add(g.Id);
                if (produtosC <= 0) continue;   // o controle diz zero: a linha guardada não conta
                lista.Add(Montar(g?.Id, c.Data, cor, produtosC, g, precos, entradas, c.Id, c.Numero));
            }
        }
        foreach (var g in guardadas.Where(g => !usadas.Contains(g.Id) && g.ProdutosC is > 0))
            lista.Add(Montar(g.Id, g.Data, g.Cor, g.ProdutosC!.Value, g, precos, [], null, null));
        return lista.OrderByDescending(l => l.Data).ThenBy(l => l.Cor).ToList();
    }

    private static QuebraLinha Montar(int? id, DateOnly data, string cor, decimal produtosC, QuebraVolumes? g,
        Dictionary<string, List<PrecoDoRomaneio>> precos, HashSet<int> doControle, int? controleId, int? controleNumero)
    {
        // Romaneio do próprio controle; senão o último de embalar da mesma cor até o dia
        var daCor = precos.GetValueOrDefault(cor) ?? [];
        var rom = daCor.Where(r => doControle.Contains(r.Numero)).OrderByDescending(r => r.Data).FirstOrDefault()
            ?? daCor.Where(r => r.Data <= data).OrderByDescending(r => r.Data).ThenByDescending(r => r.Numero).FirstOrDefault();

        decimal? preco; string de; string? aviso = null;
        if (g?.PrecoCaixa is > 0)
        {
            preco = g.PrecoCaixa;
            de = g.Origem == OrigemPlanilha ? "planilha" : "manual";
        }
        else if (rom != null)
        {
            preco = rom.PrecoCaixa - (rom.Desconto ?? 0);
            de = "romaneio";
            if (rom.Desconto == null)
                aviso = $"Falta o desconto por caixa do romaneio {rom.Numero} no acerto{(rom.AcertoNumero is { } n ? $" {n}" : "")}: o preço está sem desconto.";
        }
        else
        {
            preco = null;
            de = "falta";
            aviso = "Sem romaneio de embalar lido até este dia: digite o preço da caixa.";
        }
        var valor = Math.Round((preco ?? 0) / 12m * produtosC, 2);
        return new QuebraLinha(id, data, cor, produtosC, preco, de, valor, controleId, controleNumero,
            de == "romaneio" ? rom!.Numero : null, rom?.PrecoCaixa, rom?.Desconto, rom?.AcertoNumero, aviso, g?.Observacao);
    }

    // Romaneios da fazenda sem funrural (os de embalar), por cor, com o valor por caixa
    // daquela cor e o desconto por caixa anotado no acerto
    private async Task<Dictionary<string, List<PrecoDoRomaneio>>> PrecosDosRomaneiosAsync()
    {
        var roms = await db.RomaneiosFazendaLidos.AsNoTracking().Where(r => !r.DescFunrural).ToListAsync();
        var acertoIds = roms.Select(r => r.AcertoId).OfType<int>().Distinct().ToList();
        var acertos = await db.AcertosAlfa.AsNoTracking().Where(a => acertoIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Numero, a.DadosJson }).ToListAsync();
        var descontos = new Dictionary<int, (decimal? Desconto, int Acerto)>();
        foreach (var a in acertos)
            foreach (var (numero, desconto) in DescontosDoAcerto(a.DadosJson))
                descontos[numero] = (desconto, a.Numero);

        var porCor = new Dictionary<string, List<PrecoDoRomaneio>>();
        foreach (var r in roms)
        {
            List<RomaneioFazendaLeitor.Item> itens;
            try { itens = JsonSerializer.Deserialize<List<RomaneioFazendaLeitor.Item>>(r.ItensJson, Json) ?? []; }
            catch (JsonException) { continue; }
            foreach (var grupo in itens.GroupBy(i => CorDoProduto(i.Produto)).Where(g => g.Key != null))
            {
                var qtd = grupo.Sum(i => i.Quantidade);
                if (qtd <= 0) continue;
                var (desc, acerto) = descontos.TryGetValue(r.Numero, out var d) ? d : (null, (int?)null ?? 0);
                (porCor.TryGetValue(grupo.Key!, out var l) ? l : porCor[grupo.Key!] = [])
                    .Add(new PrecoDoRomaneio(r.Numero, r.Data, Math.Round(grupo.Sum(i => i.Valor) / qtd, 2), desc, acerto == 0 ? null : acerto));
            }
        }
        return porCor;
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string? CorDoProduto(string produto) => RomaneioFazendaLeitor.CruDoProduto(produto)?.Campo switch
    {
        "Av" => QuebraVolumes.Vermelho,
        "Ab" or "Bb" => QuebraVolumes.Branco,
        _ => null,
    };

    private record RomaneioDoAcerto(int Numero, DateOnly Data, decimal Valor, decimal Caixas, decimal? Desconto, int AcertoNumero);

    /// <summary>Linhas de romaneio dos acertos Alfa (número, data, valor, caixas, desconto por caixa).</summary>
    private async Task<List<RomaneioDoAcerto>> RomaneiosDosAcertosAsync()
    {
        var acertos = await db.AcertosAlfa.AsNoTracking().Select(a => new { a.Numero, a.DadosJson }).ToListAsync();
        var lista = new List<RomaneioDoAcerto>();
        foreach (var a in acertos)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(a.DadosJson); }
            catch (JsonException) { continue; }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("romaneios", out var roms) || roms.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in roms.EnumerateArray())
                {
                    if (!r.TryGetProperty("numero", out var n) || !n.TryGetInt32(out var numero) || numero <= 0) continue;
                    if (!r.TryGetProperty("data", out var d) || !DateOnly.TryParse(d.GetString(), CultureInfo.InvariantCulture, out var data)) continue;
                    if (!r.TryGetProperty("valor", out var v) || v.ValueKind != JsonValueKind.Number) continue;
                    if (!r.TryGetProperty("caixas", out var c) || c.ValueKind != JsonValueKind.Number || c.GetDecimal() <= 0) continue;
                    decimal? desc = r.TryGetProperty("descontoCaixa", out var dc) && dc.ValueKind == JsonValueKind.Number ? dc.GetDecimal() : null;
                    lista.Add(new RomaneioDoAcerto(numero, data, v.GetDecimal(), c.GetDecimal(), desc, a.Numero));
                }
            }
        }
        return lista;
    }

    private static IEnumerable<(int Numero, decimal? Desconto)> DescontosDoAcerto(string dadosJson)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(dadosJson); }
        catch (JsonException) { yield break; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("romaneios", out var roms) || roms.ValueKind != JsonValueKind.Array) yield break;
            foreach (var r in roms.EnumerateArray())
            {
                if (!r.TryGetProperty("numero", out var n) || !n.TryGetInt32(out var numero)) continue;
                decimal? desc = r.TryGetProperty("descontoCaixa", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDecimal() : null;
                yield return (numero, desc);
            }
        }
    }

    // ───────────────────────────── Resumo ─────────────────────────────

    public record Resumo(decimal Comissoes, decimal Salarios, decimal Despesas, decimal Quebras,
        decimal TotalDespesas, decimal Sobra, decimal Dividido, int VendasSemCompra);

    /// <summary>Comissões − (salários + INSS + FGTS + despesas + quebras) = sobra, dividida por 3 (VOLUME-18).</summary>
    public async Task<Resumo> ResumoAsync(DateOnly? de, DateOnly? ate)
    {
        var com = await db.ComissoesEmbalados.AsNoTracking()
            .Where(c => (de == null || c.Data >= de) && (ate == null || c.Data <= ate)).ToListAsync();
        var sal = await db.SalariosEmbalados.AsNoTracking()
            .Where(s => (de == null || s.Mes >= new DateOnly(de.Value.Year, de.Value.Month, 1)) && (ate == null || s.Mes <= ate)).ToListAsync();
        var desp = await db.DespesasEmbalados.AsNoTracking()
            .Where(d => (de == null || d.Data >= de) && (ate == null || d.Data <= ate)).SumAsync(d => d.Valor ?? 0);
        var quebras = (await QuebrasAsync(de, ate)).Sum(q => q.Valor);
        var comissoes = com.Sum(TotalComissao);
        var salarios = sal.Sum(s => (s.Valor ?? 0) + (s.Inss ?? 0) + (s.Fgts ?? 0));
        var total = salarios + desp + quebras;
        var sobra = comissoes - total;
        return new Resumo(Math.Round(comissoes, 2), Math.Round(salarios, 2), Math.Round(desp, 2), Math.Round(quebras, 2),
            Math.Round(total, 2), Math.Round(sobra, 2), Math.Round(sobra / 3, 2),
            com.Count(c => c.PrecoCompra == null && c.Tipo != null));
    }

    public static decimal PorCaixa(ComissaoEmbalados c) => (c.PrecoVenda ?? 0) - (c.PrecoCompra ?? 0) - c.Frete;
    public static decimal TotalComissao(ComissaoEmbalados c) => Math.Round(PorCaixa(c) * c.Caixas, 2);

    // ───────────────────────────── Planilha ─────────────────────────────

    public record PlanilhaLida(List<QuebraVolumes> Quebras, List<DespesaEmbalados> Despesas,
        List<ComissaoEmbalados> Comissoes, List<SalarioEmbalados> Salarios, List<string> Avisos);

    /// <summary>
    /// Lê a planilha COMISSÕES VOLUMES EMBALADOS: abas QUEBRAS, COMISSÕES, PGTOS
    /// SALÀRIOS e DESP. EMBAL. Os valores são os guardados das fórmulas.
    /// </summary>
    public static PlanilhaLida LerPlanilha(byte[] bytes)
    {
        var abas = AbaPlanilha.Ler(bytes);
        var avisos = new List<string>();
        AbaPlanilha? Aba(string comeco) => abas.FirstOrDefault(a => Normalizar(a.Nome).Replace(" ", "").StartsWith(comeco));
        static string? Nulo(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        static decimal? NaoZero(decimal? v) => v is null or 0 ? null : v;

        // Quebras: DATA (só na 1ª linha do dia) | TIPO | PREÇO CX | QDE BAND. | VALOR
        var quebras = new List<QuebraVolumes>();
        if (Aba("QUEBRA") is { } aq)
        {
            DateOnly? dia = null;
            for (var r = 3; r <= aq.TotalLinhas; r++)
            {
                if (Normalizar(aq.Texto(r, 3)).StartsWith("TOTAL")) break;
                dia = aq.Data(r, 1) ?? dia;
                var tipo = Normalizar(aq.Texto(r, 2));
                var cor = tipo.Contains("VERMELHO") ? QuebraVolumes.Vermelho : tipo.Contains("BRANCO") ? QuebraVolumes.Branco : null;
                var produtosC = aq.Numero(r, 4);
                if (cor == null || dia?.Year != 2026 || produtosC is null or <= 0) continue;
                quebras.Add(new QuebraVolumes { Data = dia.Value, Cor = cor, ProdutosC = produtosC, PrecoCaixa = NaoZero(aq.Numero(r, 3)), Origem = OrigemPlanilha });
            }
            var semPreco = quebras.Count(q => q.PrecoCaixa == null);
            if (semPreco > 0) avisos.Add($"Quebras: {semPreco} linha(s) sem preço da caixa na planilha; o sistema calcula pelo romaneio de embalar quando houver.");
        }
        else avisos.Add("Não achei a aba QUEBRAS.");

        // Comissões: Data | Cliente | Tipos | Qde cx | Pço Cpra | Pço Venda | V.Frete | V.Unitário | Valor total | Fornecedor | Observação
        var comissoes = new List<ComissaoEmbalados>();
        if (Aba("COMISS") is { } ac)
        {
            var semCaixas = 0; var semCompra = new List<string>();
            for (var r = 4; r <= ac.TotalLinhas; r++)
            {
                if (Normalizar(ac.Texto(r, 8)).StartsWith("SALARIO")) break;
                var data = ac.Data(r, 1);
                var cliente = Nulo(ac.Texto(r, 2));
                if (data?.Year != 2026 || cliente == null) continue;
                var c = new ComissaoEmbalados
                {
                    Data = data.Value, Cliente = cliente, Tipo = Nulo(ac.Texto(r, 3)), Caixas = ac.N(r, 4),
                    PrecoCompra = ac.Numero(r, 5), PrecoVenda = ac.Numero(r, 6), Frete = ac.N(r, 7),
                    Fornecedor = Nulo(ac.Texto(r, 10)), Observacao = Nulo(ac.Texto(r, 11)), Ordem = r, Origem = OrigemPlanilha,
                };
                if (c.Caixas == 0) semCaixas++;
                if (c.PrecoCompra == null && c.Tipo != null) semCompra.Add($"{c.Data:dd/MM} {c.Cliente}");
                comissoes.Add(c);
            }
            if (semCaixas > 0) avisos.Add($"Comissões: {semCaixas} venda(s) sem quantidade de caixas (comissão zero). Confira.");
            if (semCompra.Count > 0)
                avisos.Add($"Comissões: {semCompra.Count} venda(s) sem preço de compra (a comissão fica com o preço de venda inteiro): " +
                    string.Join(", ", semCompra.Distinct().Take(8)) + (semCompra.Count > 8 ? "…" : "") + ".");
        }
        else avisos.Add("Não achei a aba COMISSÕES.");

        // Salários: DATA (o mês, só na 1ª linha) | REFERÊNCIA | VALOR | INSS | FGTS | (anotações em F e G)
        var salarios = new List<SalarioEmbalados>();
        if (Aba("PGTOS") is { } asl)
        {
            DateOnly? mes = null;
            for (var r = 3; r <= asl.TotalLinhas; r++)
            {
                var referencia = Nulo(asl.Texto(r, 2));
                if (referencia != null && Normalizar(referencia).StartsWith("VALOR TOTAL")) break;
                if (asl.Data(r, 1) is { } d) mes = new DateOnly(d.Year, d.Month, 1);
                if (referencia == null || mes?.Year != 2026) continue;
                var obs = string.Join(" · ", new[] { asl.Texto(r, 6), asl.Texto(r, 7) }.Where(t => t.Trim().Length > 0));
                salarios.Add(new SalarioEmbalados
                {
                    Mes = mes.Value, Referencia = referencia, Valor = asl.Numero(r, 3), Inss = asl.Numero(r, 4), Fgts = asl.Numero(r, 5),
                    Observacao = Nulo(obs), Ordem = r, Origem = OrigemPlanilha,
                });
            }
        }
        else avisos.Add("Não achei a aba PGTOS SALÁRIOS.");

        // Despesas: DATA | REFERÊNCIA | VALOR | (observação)
        var despesas = new List<DespesaEmbalados>();
        if (Aba("DESP") is { } ad)
        {
            for (var r = 3; r <= ad.TotalLinhas; r++)
            {
                var descricao = Nulo(ad.Texto(r, 2));
                if (descricao != null && Normalizar(descricao).StartsWith("SOMA TOTAL")) break;
                var data = ad.Data(r, 1);
                if (data?.Year != 2026 || descricao == null) continue;
                var obs = Nulo(ad.Texto(r, 4));
                var valor = ad.Numero(r, 3);
                // "NÃO ANOTOU O VALOR" com zero: fica sem valor, para a tela avisar
                if (valor == 0 && obs != null) valor = null;
                despesas.Add(new DespesaEmbalados { Data = data.Value, Descricao = descricao, Valor = valor, Observacao = obs, Origem = OrigemPlanilha });
            }
        }
        else avisos.Add("Não achei a aba DESP. EMBAL.");

        return new PlanilhaLida(quebras, despesas, comissoes, salarios, avisos);
    }

    /// <summary>Prévia e atualização de 2026: mantém IDs, linhas manuais e o que só está no site.</summary>
    public async Task<object> ImportarAsync(byte[] bytes, bool aplicar = true)
    {
        var p = LerPlanilha(bytes);
        if (p.Quebras.Count + p.Despesas.Count + p.Comissoes.Count + p.Salarios.Count == 0)
            throw new InvalidOperationException("A planilha não tem lançamentos de 2026 nas abas esperadas.");
        var z = new ImportacaoLancamentos(db, aplicar);
        var ini = new DateOnly(2026, 1, 1); var fim = new DateOnly(2026, 12, 31);
        z.Comparar("Quebras", p.Quebras,
            await db.QuebrasVolumes.Where(q => q.Data >= ini && q.Data <= fim).ToListAsync(),
            q => $"{q.Data}|{q.Cor}", q => $"{q.Data:dd/MM/yyyy} · {q.Cor}",
            q => $"{q.ProdutosC:0.####} produtosC · caixa {q.PrecoCaixa:0.####} · {q.Observacao}",
            q => q.Origem == OrigemPlanilha, db.QuebrasVolumes);
        z.Comparar("Despesas embalados", p.Despesas,
            await db.DespesasEmbalados.Where(d => d.Data >= ini && d.Data <= fim).ToListAsync(),
            d => $"{d.Data}|{Normalizar(d.Descricao)}", d => $"{d.Data:dd/MM/yyyy} · {d.Descricao}",
            d => $"{d.Valor:0.####} · {d.Observacao}", d => d.Origem == OrigemPlanilha, db.DespesasEmbalados);
        z.Comparar("Comissões embalados", p.Comissoes,
            await db.ComissoesEmbalados.Where(c => c.Data >= ini && c.Data <= fim).ToListAsync(),
            c => $"{c.Data}|{Normalizar(c.Cliente)}|{Normalizar(c.Tipo)}|{Normalizar(c.Fornecedor)}",
            c => $"{c.Data:dd/MM/yyyy} · {c.Cliente} · {c.Tipo}",
            c => $"{c.Caixas:0.####} caixas · compra {c.PrecoCompra:0.####} · venda {c.PrecoVenda:0.####} · frete {c.Frete:0.####} · {c.Observacao}",
            c => c.Origem == OrigemPlanilha, db.ComissoesEmbalados);
        z.Comparar("Salários", p.Salarios,
            await db.SalariosEmbalados.Where(s => s.Mes >= ini && s.Mes <= fim).ToListAsync(),
            s => $"{s.Mes}|{Normalizar(s.Referencia)}", s => $"{s.Mes:MM/yyyy} · {s.Referencia}",
            s => $"{s.Valor:0.####} · INSS {s.Inss:0.####} · FGTS {s.Fgts:0.####} · {s.Observacao}",
            s => s.Origem == OrigemPlanilha, db.SalariosEmbalados);
        if (aplicar)
        {
            await db.SaveChangesAsync();
            // A linha PRODUTOSA do dia em que houve saída de produtoA para o embalado vem da saída (VOLUME-20)
            await SincronizarProdutosAEmbaladoAsync();
        }
        return new
        {
            quebras = p.Quebras.Count, despesas = p.Despesas.Count, comissoes = p.Comissoes.Count, salarios = p.Salarios.Count,
            avisos = p.Avisos, novas = z.Novas, mudadas = z.Mudadas, mantidas = z.Mantidas,
            soNoSite = z.SoNoSite, iguais = z.Iguais, ano = 2026,
        };
    }
    public static string Dinheiro(decimal v) => v.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"));
}
