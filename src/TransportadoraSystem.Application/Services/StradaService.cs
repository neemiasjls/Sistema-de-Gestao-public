using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Fretes Agrotrade pela Strada / TIP Bank (PEN-39), com o banco: importa a base AGROTRADE.xlsx, os .txt de
/// repasse e os PDFs "Baixar contrato", guarda os arquivos e refaz a ligação dos contratos às viagens
/// (regra pura em StradaConciliacao). Ligação feita à mão (situação "manual") nunca é refeita sozinha.
/// Arquivo que já existe e mudou: devolve o resumo e só grava com modo "substituir" (UI-21).
/// </summary>
public sealed class StradaService(AppDbContext db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    public const string Ligado = "ligado", SemViagem = "sem viagem", Sugestao = "sugestao", Manual = "manual";

    public record Mudanca(string Chave, string Descricao);

    public record ResultadoArquivo(string Arquivo, string Status, string? Referencia, string? Mensagem,
        int Novas, int Alteradas, int Iguais, IReadOnlyList<Mudanca> Mudancas, IReadOnlyList<string> Avisos);

    public static string NomeSocio(string? socio) => socio switch
    {
        null or "" => "Sem sócio",
        nameof(Socio.Dudu) => "Davi",
        _ => socio,
    };

    private static string Dinheiro(decimal? v) => v is null ? "vazio" : v.Value.ToString("N2", PtBr);
    private static string Data(DateOnly? d) => d is null ? "vazio" : d.Value.ToString("dd/MM/yyyy");
    private static string Texto(string? s) => string.IsNullOrWhiteSpace(s) ? "vazio" : s.Trim();

    // ───────────────────────── Base AGROTRADE.xlsx ─────────────────────────

    public async Task<ResultadoArquivo> ImportarBaseAsync(string nome, byte[] bytes, string? modo, CancellationToken ct = default)
    {
        AgrotradePlanilhaResultado leitura;
        try { leitura = AgrotradePlanilhaLeitor.Ler(bytes, AgrotradePlanilhaLeitor.InicioBase); }
        catch (Exception ex)
        {
            return new(nome, "erro", null, $"Não consegui ler a planilha: {ex.Message}", 0, 0, 0, [], []);
        }
        if (leitura.Viagens.Count == 0 && leitura.ContratosTip.Count == 0)
            return new(nome, "erro", null, "A planilha não tem a aba VIAGENS nem a CONTRATOS TIP com dados.", 0, 0, 0, [], leitura.Avisos);

        var avisos = new List<string>();
        var agora = DateTime.UtcNow;

        // Viagens: chave = NF. NF repetida na planilha: só a primeira entra.
        var existentes = await db.AgrotradeViagens.Where(v => v.Nf != null).ToDictionaryAsync(v => v.Nf!, ct);
        var vistas = new Dictionary<string, int>();
        var novasViagens = new List<ViagemAgrotrade>();
        var alteradasViagens = new List<(ViagemAgrotrade Atual, AgrotradeViagem Lida)>();
        var mudancas = new List<Mudanca>();
        var iguais = 0;
        foreach (var v in leitura.Viagens)
        {
            if (v.Nf is null) { avisos.Add($"VIAGENS, linha {v.Linha}: sem NF, não importada."); continue; }
            if (vistas.TryGetValue(v.Nf, out var outra))
            {
                avisos.Add($"NF repetida na planilha (linhas {outra} e {v.Linha}): confira; só a linha {outra} entrou.");
                continue;
            }
            vistas[v.Nf] = v.Linha;
            if (!existentes.TryGetValue(v.Nf, out var atual)) { novasViagens.Add(NovaViagem(v, agora)); continue; }
            var dif = DiferencasViagem(atual, v);
            if (dif.Count == 0) { iguais++; continue; }
            alteradasViagens.Add((atual, v));
            mudancas.Add(new($"NF {v.Nf}", string.Join("; ", dif)));
        }

        // Histórico da aba CONTRATOS TIP: contrato sem fatura, origem "planilha".
        var historico = await db.StradaContratos.Where(c => c.FaturaId == null).ToDictionaryAsync(c => c.Contrato, ct);
        var novosTip = new List<StradaContrato>();
        var alteradosTip = new List<(StradaContrato Atual, AgrotradeContratoTip Lida)>();
        var contratosVistos = new HashSet<string>();
        foreach (var c in leitura.ContratosTip)
        {
            if (c.Contrato is null || !contratosVistos.Add(c.Contrato)) continue;
            if (!historico.TryGetValue(c.Contrato, out var atual)) { novosTip.Add(NovoTip(c, agora)); continue; }
            if (atual.Origem != "planilha") continue;   // veio do PDF: o PDF completa, a planilha não troca
            var dif = DiferencasTip(atual, c);
            if (dif.Count == 0) { iguais++; continue; }
            alteradosTip.Add((atual, c));
            mudancas.Add(new($"Contrato {c.Contrato}", string.Join("; ", dif)));
        }

        var novas = novasViagens.Count + novosTip.Count;
        var alteradas = alteradasViagens.Count + alteradosTip.Count;
        avisos.AddRange(leitura.Avisos);

        if (alteradas > 0 && modo is not ("manter" or "substituir"))
            return new(nome, "confirmar", null,
                $"A base já está no sistema: {novas} novas, {alteradas} com mudança, {iguais} iguais. Manter como está ou substituir?",
                novas, alteradas, iguais, mudancas, avisos);

        db.AgrotradeViagens.AddRange(novasViagens);
        db.StradaContratos.AddRange(novosTip);
        if (modo == "substituir")
        {
            foreach (var (atual, lida) in alteradasViagens) CopiarViagem(lida, atual, agora);
            foreach (var (atual, lida) in alteradosTip) CopiarTip(lida, atual, agora);
        }
        await GuardarArquivoAsync("agrotrade", nome, bytes, null, ct);
        await db.SaveChangesAsync(ct);
        await ReconciliarAsync(ct);

        var status = modo == "manter" ? "mantido" : (existentes.Count == 0 && historico.Count == 0 ? "criado" : "atualizado");
        return new(nome, status, null, null, novas, modo == "substituir" ? alteradas : 0, iguais, mudancas, avisos);
    }

    private static ViagemAgrotrade NovaViagem(AgrotradeViagem v, DateTime agora)
    {
        var n = new ViagemAgrotrade { Origem = "planilha", CriadoEm = agora };
        CopiarViagem(v, n, agora);
        return n;
    }

    private static void CopiarViagem(AgrotradeViagem v, ViagemAgrotrade e, DateTime agora)
    {
        e.Data = v.Data; e.Motorista = v.Motorista; e.Placa = v.Placa; e.Destino = v.Destino; e.Unidade = v.Unidade;
        e.Nf = v.Nf; e.NfOriginal = v.NfOriginal; e.Frete = v.Frete; e.AdiantDescarga = v.AdiantDescarga;
        e.Descontos = v.Descontos; e.ValorDescarga = v.ValorDescarga; e.FreteTotal = v.FreteTotal;
        e.Contrato = v.Contrato; e.DifDescarga = v.DifDescarga; e.ProtocoloDescarga = v.ProtocoloDescarga;
        e.DataOcorrenciaDescarga = v.DataOcorrenciaDescarga; e.DescargaRecebidaPlanilha = !string.IsNullOrWhiteSpace(v.Recebido);
        e.DataDescargaRecebidaPlanilha = v.DataDescargaRecebidaPlanilha; e.ProtocoloDiaria = v.ProtocoloDiaria;
        e.DataDiaria = v.DataDiaria; e.ValorDiaria = v.ValorDiaria; e.Observacao = v.Observacao;
        e.AtualizadoEm = agora;
    }

    private static List<string> DiferencasViagem(ViagemAgrotrade e, AgrotradeViagem v)
    {
        var d = new List<string>();
        void T(string campo, string a, string b) { if (a != b) d.Add($"{campo}: {a} → {b}"); }
        T("data", Data(e.Data), Data(v.Data));
        T("placa", Texto(e.Placa), Texto(v.Placa));
        T("motorista", Texto(e.Motorista), Texto(v.Motorista));
        T("destino", Texto(e.Destino), Texto(v.Destino));
        T("frete", Dinheiro(e.Frete), Dinheiro(v.Frete));
        T("adiantamento da descarga", Dinheiro(e.AdiantDescarga), Dinheiro(v.AdiantDescarga));
        T("descontos", Dinheiro(e.Descontos), Dinheiro(v.Descontos));
        T("contrato", Texto(e.Contrato), Texto(v.Contrato));
        T("diferença da descarga", Dinheiro(e.DifDescarga), Dinheiro(v.DifDescarga));
        T("protocolo da descarga", Texto(e.ProtocoloDescarga), Texto(v.ProtocoloDescarga));
        T("descarga recebida", e.DescargaRecebidaPlanilha ? "sim" : "não", string.IsNullOrWhiteSpace(v.Recebido) ? "não" : "sim");
        T("protocolo da diária", Texto(e.ProtocoloDiaria), Texto(v.ProtocoloDiaria));
        T("valor da diária", Dinheiro(e.ValorDiaria), Dinheiro(v.ValorDiaria));
        return d;
    }

    private static StradaContrato NovoTip(AgrotradeContratoTip c, DateTime agora)
    {
        var n = new StradaContrato { Contrato = c.Contrato!, Origem = "planilha", CriadoEm = agora };
        CopiarTip(c, n, agora);
        return n;
    }

    private static void CopiarTip(AgrotradeContratoTip c, StradaContrato e, DateTime agora)
    {
        e.Valor = c.ValorAReceber ?? Math.Round((c.Frete ?? 0) - (c.Desconto ?? 0), 2);
        e.Placa = c.Placa;
        e.Tipo = (c.Frete ?? 0) > 0 ? "frete" : "indefinido";
        e.DataTransferencia = string.IsNullOrWhiteSpace(c.Transferido) ? null : c.DataTransferencia;
        e.AtualizadoEm = agora;
    }

    private static List<string> DiferencasTip(StradaContrato e, AgrotradeContratoTip c)
    {
        var d = new List<string>();
        var valor = c.ValorAReceber ?? Math.Round((c.Frete ?? 0) - (c.Desconto ?? 0), 2);
        if (e.Valor != valor) d.Add($"valor: {Dinheiro(e.Valor)} → {Dinheiro(valor)}");
        if (Texto(e.Placa) != Texto(c.Placa)) d.Add($"placa: {Texto(e.Placa)} → {Texto(c.Placa)}");
        var transf = string.IsNullOrWhiteSpace(c.Transferido) ? null : c.DataTransferencia;
        if (e.DataTransferencia != transf) d.Add($"transferência: {Data(e.DataTransferencia)} → {Data(transf)}");
        return d;
    }

    // ───────────────────────── Faturas (.txt de repasse) ─────────────────────────

    public async Task<List<ResultadoArquivo>> ImportarFaturasAsync(List<(string Nome, byte[] Bytes)> arquivos, string? modo, CancellationToken ct = default)
    {
        var resultado = new List<ResultadoArquivo>();
        var vistas = new HashSet<string>();
        var agora = DateTime.UtcNow;
        foreach (var (nome, bytes) in arquivos)
        {
            var lido = StradaRepasseLeitor.Ler(bytes);
            if (!lido.Ok) { resultado.Add(new(nome, "erro", null, $"Não é arquivo de repasse da Strada: {lido.Erro}", 0, 0, 0, [], [])); continue; }
            var f = lido.Fatura!;
            if (!vistas.Add(f.Numero))
            {
                resultado.Add(new(nome, "erro", f.Numero, $"Outro arquivo deste envio já é a fatura {f.Numero}.", 0, 0, 0, [], f.Avisos));
                continue;
            }

            var atual = await db.StradaFaturas.Include(x => x.Contratos).FirstOrDefaultAsync(x => x.Numero == f.Numero, ct);
            // .txt só com o cabeçalho de uma fatura que o detalhamento já completou: nada a trocar
            if (atual is { Origem: "detalhamento" } && f.Incompleta && f.Contratos.Count == 0
                && Math.Abs(atual.Total - f.TotalCabecalho) <= 0.01m && atual.DataPagamento == f.DataPagamento)
            {
                await GuardarArquivoAsync("txt", nome, bytes, f.Numero, ct);
                await db.SaveChangesAsync(ct);
                resultado.Add(new(nome, "igual", f.Numero, "Fatura já completada pelo detalhamento.", 0, 0, atual.Contratos.Count, [], f.Avisos));
                continue;
            }
            if (atual != null)
            {
                var mudancas = DiferencasFatura(atual, f);
                if (mudancas.Count == 0)
                {
                    await GuardarArquivoAsync("txt", nome, bytes, f.Numero, ct);
                    await db.SaveChangesAsync(ct);
                    resultado.Add(new(nome, "igual", f.Numero, "Fatura já estava no sistema, igual.", 0, 0, f.Contratos.Count, [], f.Avisos));
                    continue;
                }
                if (modo == "manter")
                {
                    resultado.Add(new(nome, "mantido", f.Numero, "Mantida como estava.", 0, 0, 0, mudancas, f.Avisos));
                    continue;
                }
                if (modo != "substituir")
                {
                    resultado.Add(new(nome, "confirmar", f.Numero,
                        $"A fatura {f.Numero} já está no sistema e o arquivo traz mudanças. Manter como está ou substituir?",
                        0, mudancas.Count, 0, mudancas, f.Avisos));
                    continue;
                }
            }

            var fatura = atual ?? new StradaFatura { Numero = f.Numero, CriadoEm = agora };
            if (atual == null) db.StradaFaturas.Add(fatura);
            fatura.DataGeracao = f.DataGeracao; fatura.DataPagamento = f.DataPagamento;
            fatura.Total = f.Total; fatura.Taxa = f.Taxa; fatura.Repasse = f.Repasse;
            if (f.Incompleta && f.Contratos.Count == 0)
            {
                // Só o cabeçalho: guarda o valor dele até o "Exportar detalhamento" trazer os contratos.
                fatura.Total = f.TotalCabecalho;
                fatura.Taxa = StradaRepasseLeitor.CalcularTaxa(f.TotalCabecalho);
                fatura.Repasse = fatura.Total - fatura.Taxa;
            }
            fatura.Incompleta = f.Incompleta; fatura.Origem = "txt";
            fatura.Avisos = f.Avisos.Count == 0 ? null : string.Join("\n", f.Avisos);
            fatura.AtualizadoEm = agora;

            // Contratos: mantém os que continuam (com a ligação manual e os dados do PDF), troca o resto.
            var porNumero = fatura.Contratos.ToDictionary(c => c.Contrato);
            var lidos = f.Contratos.Select(c => c.Numero).ToHashSet();
            foreach (var sobra in fatura.Contratos.Where(c => !lidos.Contains(c.Contrato)).ToList())
            {
                fatura.Contratos.Remove(sobra);
                db.StradaContratos.Remove(sobra);
            }
            foreach (var c in f.Contratos)
            {
                if (!porNumero.TryGetValue(c.Numero, out var contrato))
                {
                    contrato = new StradaContrato { Contrato = c.Numero, Origem = "fatura", CriadoEm = agora };
                    fatura.Contratos.Add(contrato);
                    // PDF que subiu antes da fatura: passa os dados para o contrato da fatura.
                    var soPdf = await db.StradaContratos.FirstOrDefaultAsync(x => x.FaturaId == null && x.Origem == "pdf" && x.Contrato == c.Numero, ct);
                    if (soPdf != null)
                    {
                        CopiarPdf(soPdf, contrato);
                        db.StradaContratos.Remove(soPdf);
                    }
                }
                contrato.Valor = c.Valor;
                contrato.Adiantamento = Soma(c.Linhas.Where(l => l.Sequencia == 1));
                contrato.Saldo = Soma(c.Linhas.Where(l => l.Sequencia != 1));
                contrato.DataTransferencia = f.DataPagamento;
                contrato.AtualizadoEm = agora;
            }
            await GuardarArquivoAsync("txt", nome, bytes, f.Numero, ct);
            await db.SaveChangesAsync(ct);
            resultado.Add(new(nome, atual == null ? "criado" : "substituido", f.Numero, null,
                atual == null ? f.Contratos.Count : 0, atual == null ? 0 : f.Contratos.Count, 0, [], f.Avisos));
        }
        if (resultado.Any(r => r.Status is "criado" or "substituido")) await ReconciliarAsync(ct);
        return resultado;
    }

    // ─────────────── "Exportar detalhamento" (.xlsx): completa a fatura que veio só com o cabeçalho ───────────────

    public async Task<ResultadoArquivo> ImportarDetalhamentoAsync(string nome, byte[] bytes, string? modo, CancellationToken ct = default)
    {
        var lido = StradaDetalhamentoLeitor.Ler(bytes);
        if (lido.Detalhamento == null) return new(nome, "erro", null, $"Não é o \"Exportar detalhamento\" da Strada: {lido.Erro}", 0, 0, 0, [], []);
        var d = lido.Detalhamento;
        var avisos = new List<string>();
        var taxaCerta = StradaRepasseLeitor.CalcularTaxa(d.Total);
        if (Math.Abs(d.Taxa - taxaCerta) > 0.02m) avisos.Add($"Taxa no arquivo {Dinheiro(d.Taxa)}, esperado 1% = {Dinheiro(taxaCerta)}.");

        // O arquivo não traz o número da fatura: acha pelo total (a incompleta) ou pelos mesmos contratos (já completa)
        var numeros = d.Contratos.Select(c => c.Numero).ToHashSet();
        var candidatas = await db.StradaFaturas.Include(f => f.Contratos)
            .Where(f => f.Total >= d.Total - 0.01m && f.Total <= d.Total + 0.01m).ToListAsync(ct);
        var completa = candidatas.FirstOrDefault(f => f.Contratos.Count > 0 && f.Contratos.Select(c => c.Contrato).ToHashSet().SetEquals(numeros));
        if (completa != null)
        {
            var mudancas = new List<Mudanca>();
            var valores = completa.Contratos.ToDictionary(c => c.Contrato, c => c.Valor);
            foreach (var c in d.Contratos.Where(c => valores[c.Numero] != c.Valor))
                mudancas.Add(new($"Contrato {c.Numero}", $"{Dinheiro(valores[c.Numero])} → {Dinheiro(c.Valor)}"));
            if (d.Taxa > 0 && completa.Taxa != d.Taxa) mudancas.Add(new("Taxa", $"{Dinheiro(completa.Taxa)} → {Dinheiro(d.Taxa)}"));
            if (d.Repasse > 0 && completa.Repasse != d.Repasse) mudancas.Add(new("Repasse", $"{Dinheiro(completa.Repasse)} → {Dinheiro(d.Repasse)}"));
            if (mudancas.Count == 0)
            {
                await GuardarArquivoAsync("xlsx", nome, bytes, completa.Numero, ct);
                await db.SaveChangesAsync(ct);
                return new(nome, "igual", completa.Numero, $"A fatura {completa.Numero} já tem estes contratos.", 0, 0, d.Contratos.Count, [], avisos);
            }
            if (modo == "manter") return new(nome, "mantido", completa.Numero, "Mantida como estava.", 0, 0, 0, mudancas, avisos);
            if (modo != "substituir")
                return new(nome, "confirmar", completa.Numero,
                    $"A fatura {completa.Numero} já está no sistema e o detalhamento traz mudanças. Manter como está ou substituir?",
                    0, mudancas.Count, 0, mudancas, avisos);
            var agoraS = DateTime.UtcNow;
            foreach (var c in completa.Contratos) { c.Valor = d.Contratos.First(x => x.Numero == c.Contrato).Valor; c.AtualizadoEm = agoraS; }
            if (d.Taxa > 0) completa.Taxa = d.Taxa;
            if (d.Repasse > 0) completa.Repasse = d.Repasse;
            completa.AtualizadoEm = agoraS;
            await GuardarArquivoAsync("xlsx", nome, bytes, completa.Numero, ct);
            await db.SaveChangesAsync(ct);
            await ReconciliarAsync(ct);
            return new(nome, "substituido", completa.Numero, null, 0, mudancas.Count, 0, [], avisos);
        }
        var incompletas = candidatas.Where(f => f.Incompleta && f.Contratos.Count == 0).ToList();
        if (incompletas.Count == 0)
            return new(nome, "erro", null, $"Não achei fatura só com o cabeçalho de {Dinheiro(d.Total)}. Suba antes o .txt de repasse dela.", 0, 0, 0, [], avisos);
        if (incompletas.Count > 1)
            return new(nome, "erro", null, $"Mais de uma fatura incompleta de {Dinheiro(d.Total)} ({string.Join(", ", incompletas.Select(f => f.Numero))}): não sei qual completar.", 0, 0, 0, [], avisos);

        var fatura = incompletas[0];
        var agora = DateTime.UtcNow;
        foreach (var c in d.Contratos)
        {
            var contrato = new StradaContrato { Contrato = c.Numero, Origem = "fatura", CriadoEm = agora };
            fatura.Contratos.Add(contrato);
            var soPdf = await db.StradaContratos.FirstOrDefaultAsync(x => x.FaturaId == null && x.Origem == "pdf" && x.Contrato == c.Numero, ct);
            if (soPdf != null)
            {
                CopiarPdf(soPdf, contrato);
                db.StradaContratos.Remove(soPdf);
            }
            contrato.Valor = c.Valor;
            contrato.DataTransferencia = fatura.DataPagamento;
            contrato.AtualizadoEm = agora;
        }
        fatura.Total = d.Total;
        fatura.Taxa = d.Taxa > 0 ? d.Taxa : taxaCerta;
        fatura.Repasse = d.Repasse > 0 ? d.Repasse : d.Total - fatura.Taxa;
        fatura.Incompleta = false; fatura.Origem = "detalhamento";
        fatura.Avisos = avisos.Count == 0 ? null : string.Join("\n", avisos);
        fatura.AtualizadoEm = agora;
        await GuardarArquivoAsync("xlsx", nome, bytes, fatura.Numero, ct);
        await db.SaveChangesAsync(ct);
        await ReconciliarAsync(ct);
        return new(nome, "atualizado", fatura.Numero, $"Fatura {fatura.Numero} completada com {d.Contratos.Count} contrato(s).", d.Contratos.Count, 0, 0, [], avisos);
    }

    private static decimal? Soma(IEnumerable<StradaRepasseLeitor.Linha> linhas)
    {
        var l = linhas.ToList();
        return l.Count == 0 ? null : l.Sum(x => x.Valor);
    }

    private static void CopiarPdf(StradaContrato de, StradaContrato para)
    {
        para.Placa = de.Placa; para.Nf = de.Nf; para.Motorista = de.Motorista;
        para.ValorContratoPdf = de.ValorContratoPdf; para.Seguro = de.Seguro; para.DataContrato = de.DataContrato;
    }

    private static List<Mudanca> DiferencasFatura(StradaFatura e, StradaRepasseLeitor.Fatura f)
    {
        var d = new List<Mudanca>();
        // Fatura só com o cabeçalho guarda o total dele (o Total lido vem 0, sem contratos)
        var total = f.Incompleta && f.Contratos.Count == 0 ? f.TotalCabecalho : f.Total;
        if (e.Total != total) d.Add(new("Total", $"{Dinheiro(e.Total)} → {Dinheiro(total)}"));
        if (e.DataPagamento != f.DataPagamento) d.Add(new("Data de pagamento", $"{Data(e.DataPagamento)} → {Data(f.DataPagamento)}"));
        var antes = e.Contratos.ToDictionary(c => c.Contrato, c => c.Valor);
        var depois = f.Contratos.ToDictionary(c => c.Numero, c => c.Valor);
        foreach (var (k, v) in depois)
            if (!antes.TryGetValue(k, out var a)) d.Add(new($"Contrato {k}", $"entra com {Dinheiro(v)}"));
            else if (a != v) d.Add(new($"Contrato {k}", $"{Dinheiro(a)} → {Dinheiro(v)}"));
        foreach (var k in antes.Keys.Where(k => !depois.ContainsKey(k)))
            d.Add(new($"Contrato {k}", "sai da fatura"));
        return d;
    }

    // ───────────────────────── PDFs "Baixar contrato" ─────────────────────────

    public async Task<List<ResultadoArquivo>> ImportarPdfsAsync(List<(string Nome, byte[] Bytes)> arquivos, string? modo, CancellationToken ct = default)
    {
        var resultado = new List<ResultadoArquivo>();
        var vistos = new HashSet<string>();
        var agora = DateTime.UtcNow;
        foreach (var (nome, bytes) in arquivos)
        {
            var p = StradaContratoPdfLeitor.Ler(bytes);
            if (!p.Aceito) { resultado.Add(new(nome, "erro", null, p.Recusa, 0, 0, 0, [], [])); continue; }
            if (p.Contrato is null) { resultado.Add(new(nome, "erro", null, "O PDF não traz o número do contrato.", 0, 0, 0, [], p.Avisos)); continue; }
            if (!vistos.Add(p.Contrato))
            {
                resultado.Add(new(nome, "erro", p.Contrato, $"Outro PDF deste envio já é o contrato {p.Contrato}.", 0, 0, 0, [], p.Avisos));
                continue;
            }
            var nfs = p.Notas.Count == 0 ? null : string.Join(",", p.Notas.Select(n => n.Nf));

            var alvos = await db.StradaContratos.Where(c => c.Contrato == p.Contrato).ToListAsync(ct);
            var comPdf = alvos.FirstOrDefault(c => c.ValorContratoPdf != null);
            if (comPdf != null)
            {
                var mud = new List<Mudanca>();
                if (comPdf.ValorContratoPdf != p.ValorContrato) mud.Add(new("Valor do contrato", $"{Dinheiro(comPdf.ValorContratoPdf)} → {Dinheiro(p.ValorContrato)}"));
                if (Texto(comPdf.Nf) != Texto(nfs)) mud.Add(new("NF", $"{Texto(comPdf.Nf)} → {Texto(nfs)}"));
                if (Texto(comPdf.Placa) != Texto(p.Placa)) mud.Add(new("Placa", $"{Texto(comPdf.Placa)} → {Texto(p.Placa)}"));
                if (comPdf.Seguro != p.Seguro) mud.Add(new("Seguro", $"{Dinheiro(comPdf.Seguro)} → {Dinheiro(p.Seguro)}"));
                if (mud.Count == 0)
                {
                    await GuardarArquivoAsync("pdf", nome, bytes, p.Contrato, ct);
                    await db.SaveChangesAsync(ct);
                    resultado.Add(new(nome, "igual", p.Contrato, "Contrato já estava no sistema, igual.", 0, 0, 1, [], p.Avisos));
                    continue;
                }
                if (modo == "manter") { resultado.Add(new(nome, "mantido", p.Contrato, "Mantido como estava.", 0, 0, 0, mud, p.Avisos)); continue; }
                if (modo != "substituir")
                {
                    resultado.Add(new(nome, "confirmar", p.Contrato,
                        $"O contrato {p.Contrato} já tem PDF no sistema e este traz mudanças. Manter como está ou substituir?",
                        0, mud.Count, 0, mud, p.Avisos));
                    continue;
                }
            }

            if (alvos.Count == 0)
            {
                var novo = new StradaContrato
                {
                    Contrato = p.Contrato, Origem = "pdf", CriadoEm = agora,
                    Valor = Math.Round((p.ValorContrato ?? 0) - (p.Seguro ?? 0), 2),
                    Tipo = "indefinido", Situacao = SemViagem,
                };
                db.StradaContratos.Add(novo);
                alvos.Add(novo);
            }
            foreach (var c in alvos)
            {
                c.Placa = p.Placa ?? c.Placa; c.Nf = nfs; c.Motorista = p.Motorista;
                c.ValorContratoPdf = p.ValorContrato; c.Seguro = p.Seguro; c.DataContrato = p.DataContrato; c.AtualizadoEm = agora;
            }
            await GuardarArquivoAsync("pdf", nome, bytes, p.Contrato, ct);
            await db.SaveChangesAsync(ct);
            var semFatura = alvos.All(c => c.FaturaId == null && c.Origem == "pdf");
            resultado.Add(new(nome, comPdf == null ? "criado" : "substituido", p.Contrato,
                semFatura ? "A fatura deste contrato ainda não subiu: o contrato fica guardado e entra nela quando subir." : null,
                comPdf == null ? 1 : 0, comPdf == null ? 0 : 1, 0, [], p.Avisos));
        }
        if (resultado.Any(r => r.Status is "criado" or "substituido")) await ReconciliarAsync(ct);
        else await StradaRetornos.SincronizarAsync(db, ct);   // VIA-27: Reconciliar já sincroniza no fim
        return resultado;
    }

    // ───────────────────────── Caiu no banco (extrato do Sicoob) ─────────────────────────

    public record ConferenciaBanco(string Fatura, DateOnly? DataPagamento, decimal Repasse, string Situacao,
        DateOnly? DataCredito, string? Mensagem);

    /// <summary>
    /// Para cada fatura ainda não conferida: crédito no extrato com o valor do repasse, data do pagamento
    /// até 5 dias depois, e "STRADA" na descrição/complemento (o Pix vem de "STRADA PAY"). Um lançamento
    /// liga a uma fatura só. Dois candidatos → não marca. Sem crédito com a data já passada → marca
    /// "conferida sem crédito" (LancamentoBancoId vazio) e o painel avisa "não caiu no banco".
    /// </summary>
    public async Task<List<ConferenciaBanco>> ConferirBancoAsync(IReadOnlyList<SicoobService.Lancamento> lancamentos,
        DateOnly inicio, DateOnly fim, DateOnly hoje, CancellationToken ct = default)
    {
        var faturas = await db.StradaFaturas.Where(f => !f.CaiuNoBanco && f.DataPagamento >= inicio && f.DataPagamento <= fim)
            .OrderBy(f => f.DataPagamento).ToListAsync(ct);
        var usados = (await db.StradaFaturas.Where(f => f.LancamentoBancoId != null && f.LancamentoBancoId != "")
            .Select(f => f.LancamentoBancoId!).ToListAsync(ct)).ToHashSet();
        var strada = lancamentos.Where(l => l.Credito && l.Data != null &&
            (l.Descricao + " " + l.Complemento).Contains("STRADA", StringComparison.OrdinalIgnoreCase)).ToList();
        var resultado = new List<ConferenciaBanco>();
        foreach (var f in faturas)
        {
            var pago = f.DataPagamento!.Value;
            var cand = strada.Where(l => !usados.Contains(l.Id) && l.Valor == f.Repasse
                && l.Data >= pago && l.Data!.Value.DayNumber - pago.DayNumber <= 5).ToList();
            if (cand.Count == 1)
            {
                var l = cand[0];
                f.CaiuNoBanco = true; f.DataCreditoBanco = l.Data; f.LancamentoBancoId = l.Id; f.AtualizadoEm = DateTime.UtcNow;
                usados.Add(l.Id);
                resultado.Add(new(f.Numero, pago, f.Repasse, "caiu", l.Data, null));
            }
            else if (cand.Count > 1)
                resultado.Add(new(f.Numero, pago, f.Repasse, "duvida", null,
                    $"{cand.Count} créditos da Strada de {Dinheiro(f.Repasse)} entre {Data(pago)} e 5 dias depois: confira no extrato."));
            else if (pago.AddDays(5) < hoje && fim >= pago.AddDays(5))
            {
                f.LancamentoBancoId = ""; f.AtualizadoEm = DateTime.UtcNow;
                resultado.Add(new(f.Numero, pago, f.Repasse, "nao caiu", null,
                    $"Nenhum crédito da Strada de {Dinheiro(f.Repasse)} até 5 dias depois de {Data(pago)}."));
            }
            else resultado.Add(new(f.Numero, pago, f.Repasse, "aguardando", null, "Ainda dentro do prazo de 5 dias."));
        }
        await db.SaveChangesAsync(ct);
        return resultado;
    }

    // ───────────────────────── Ligação à mão ─────────────────────────

    public async Task<string?> LigarAsync(int contratoId, int? viagemId, string? placa, CancellationToken ct = default)
    {
        var c = await db.StradaContratos.FindAsync([contratoId], ct);
        if (c == null) return "Contrato não encontrado.";
        var socios = await SociosPorPlacaAsync(ct);
        // Contrato com PDF guarda a placa do PDF (desfazer a ligação volta a ela); a ligação só muda o sócio.
        var temPdf = c.ValorContratoPdf != null;
        string? placaLigada;
        if (viagemId is int vid)
        {
            var v = await db.AgrotradeViagens.FindAsync([vid], ct);
            if (v == null) return "Viagem não encontrada.";
            var (campo, esperado) = StradaConciliacao.ValorAnotado(ParaRegistro(v), c.Contrato, c.Valor);
            c.ViagemId = v.Id;
            placaLigada = v.Placa;
            c.Tipo = campo == "frete" ? "frete" : campo is null ? "indefinido" : "reembolso";
            c.ValorEsperado = esperado;
            c.Diferenca = esperado is null ? null : Math.Round(c.Valor - esperado.Value, 2);
        }
        else if (!string.IsNullOrWhiteSpace(placa))
        {
            c.ViagemId = null;
            placaLigada = ViagemCaminhaoCalculo.NormalizarPlaca(placa);
            c.ValorEsperado = null; c.Diferenca = null;
        }
        else return "Escolha a viagem ou a placa.";
        if (!temPdf || c.Placa == null) c.Placa = placaLigada;
        c.Socio = SocioDe(placaLigada, socios);
        c.Situacao = Manual;
        c.NfSugerida = null;
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Confirma a sugestão de NF quase igual: a ligação vira definitiva (não é refeita).</summary>
    public async Task<string?> ConfirmarAsync(int contratoId, CancellationToken ct = default)
    {
        var c = await db.StradaContratos.FindAsync([contratoId], ct);
        if (c == null) return "Contrato não encontrado.";
        if (c.Situacao != Sugestao || c.ViagemId == null) return "Este contrato não tem sugestão para confirmar.";
        c.Situacao = Manual;
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> DesligarAsync(int contratoId, CancellationToken ct = default)
    {
        var c = await db.StradaContratos.FindAsync([contratoId], ct);
        if (c == null) return "Contrato não encontrado.";
        c.Situacao = SemViagem;
        c.ViagemId = null;
        c.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await ReconciliarAsync(ct);
        return null;
    }

    // ───────────────────────── Refaz as ligações ─────────────────────────

    /// <summary>
    /// Liga de novo todos os contratos que não foram ligados à mão: 1) coluna L da VIAGENS (frete);
    /// 2) aba CONTRATOS TIP (placa dali); 3) NF do PDF (reembolso; NF quase igual = sugestão);
    /// senão "sem viagem". Sócio pela placa (Veiculos).
    /// </summary>
    public async Task ReconciliarAsync(CancellationToken ct = default)
    {
        var viagensDb = await db.AgrotradeViagens.AsNoTracking().ToListAsync(ct);
        var viagens = viagensDb.Select(ParaRegistro).ToList();
        var porId = viagensDb.ToDictionary(v => v.Id);
        var socios = await SociosPorPlacaAsync(ct);
        var contratos = await db.StradaContratos.Include(c => c.Fatura).ToListAsync(ct);

        var tip = contratos.Where(c => c.FaturaId == null && c.Origem == "planilha")
            .Select(c => new AgrotradeContratoTip(c.Id, null, c.Placa, c.Contrato, c.Tipo == "frete" ? c.Valor : null, null,
                c.Valor, c.DataTransferencia is null ? null : "SIM", c.DataTransferencia, false, []))
            .ToList();
        var faturas = contratos.Where(c => c.Fatura != null).GroupBy(c => c.Fatura!)
            .Select(g => new StradaRepasseLeitor.Fatura(g.Key.Numero, "", "", g.Key.DataGeracao ?? default,
                g.Key.DataPagamento ?? default, g.Key.Total, g.Key.Total, g.Key.Taxa, g.Key.Repasse, g.Key.Incompleta,
                g.Select(c => new StradaRepasseLeitor.Contrato(c.Contrato, c.Valor, [])).ToList(), [], []))
            .ToList();
        var conciliado = StradaConciliacao.Conciliar(faturas, viagens, tip, socios.ToDictionary(p => p.Key, p => Enum.Parse<Socio>(p.Value)));
        var porChave = conciliado.Faturas.SelectMany(f => f.Contratos).ToDictionary(c => (c.Fatura, c.Contrato));
        var agora = DateTime.UtcNow;

        foreach (var c in contratos)
        {
            if (c.Situacao == Manual) continue;   // ligado à mão: não refaz
            var temPdf = c.ValorContratoPdf != null;
            c.NfSugerida = null;

            StradaConciliacao.ContratoConciliado? k = null;
            if (c.Fatura != null) porChave.TryGetValue((c.Fatura.Numero, c.Contrato), out k);
            var viagemL = k?.Viagem ?? (c.Fatura == null ? viagens.FirstOrDefault(v => v.Contrato?.Trim() == c.Contrato) : null);

            if (viagemL != null)
            {
                var total = k?.ValorTotalContrato ?? c.Valor;
                var (_, esperado) = StradaConciliacao.ValorAnotado(viagemL, c.Contrato, total);
                c.Tipo = "frete";
                c.ViagemId = viagemL.Linha;
                c.ValorEsperado = esperado;
                c.Diferenca = esperado is null ? null : Math.Round(total - esperado.Value, 2);
                if (!temPdf) { c.Placa = viagemL.Placa; c.Nf = viagemL.Nf; c.Motorista = viagemL.Motorista; }
                c.Socio = SocioDe(viagemL.Placa, socios);
                c.Situacao = Ligado;
            }
            else if (k?.Origem == StradaConciliacao.Origem.ContratosTip || (c.Fatura == null && c.Origem == "planilha"))
            {
                var linha = k?.LinhaTip;
                c.Tipo = linha == null ? c.Tipo : (linha.Frete ?? 0) > 0 ? "frete" : "indefinido";
                c.ViagemId = null; c.ValorEsperado = null; c.Diferenca = null;
                if (!temPdf && linha != null) c.Placa = linha.Placa;
                c.Socio = SocioDe(c.Placa, socios);
                c.Situacao = Ligado;
                if (temPdf) LigarPeloPdf(c, viagens, socios);
            }
            else if (temPdf) LigarPeloPdf(c, viagens, socios);
            else
            {
                c.Tipo = "indefinido"; c.ViagemId = null; c.Placa = null; c.Socio = null;
                c.ValorEsperado = null; c.Diferenca = null; c.Situacao = SemViagem;
            }
            if (c.ViagemId is int id && !porId.ContainsKey(id)) c.ViagemId = null;
            c.AtualizadoEm = agora;
        }
        await db.SaveChangesAsync(ct);
        // VIA-27: o contrato que paga um retorno lançado à mão volta a ficar ligado a ele
        await StradaRetornos.SincronizarAsync(db, ct);
    }

    private static void LigarPeloPdf(StradaContrato c, List<AgrotradeViagem> viagens, Dictionary<string, string> socios)
    {
        var nfs = (c.Nf ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var l = StradaConciliacao.LigarPorNf(c.Contrato, nfs, c.Placa, c.Valor, viagens);
        c.Socio = SocioDe(c.Placa ?? l.Viagem?.Placa, socios);
        if (l.Situacao == StradaConciliacao.SituacaoNf.Nenhuma)
        {
            if (c.Situacao != Ligado) { c.Situacao = SemViagem; c.ViagemId = null; }
            c.ValorEsperado = null; c.Diferenca = null;
            if (c.Tipo == "indefinido") c.Tipo = "reembolso";
            return;
        }
        c.ViagemId = l.Viagem!.Linha;
        c.Tipo = l.Campo == "frete" ? "frete" : "reembolso";
        c.ValorEsperado = l.Esperado;
        c.Diferenca = l.Diferenca;
        if (l.Situacao == StradaConciliacao.SituacaoNf.Sugestao) { c.Situacao = Sugestao; c.NfSugerida = l.NfViagem; }
        else c.Situacao = Ligado;
    }

    // ───────────────────────── Consultas ─────────────────────────

    public async Task<object> ListarFaturasAsync(int? ano, int? mes, CancellationToken ct = default)
    {
        var q = db.StradaFaturas.AsNoTracking().Include(f => f.Contratos).AsQueryable();
        if (ano is int a && mes is int m)
        {
            var (de, ate) = Mes(a, m);
            q = q.Where(f => f.DataPagamento >= de && f.DataPagamento <= ate);
        }
        var faturas = await q.OrderByDescending(f => f.DataPagamento).ThenByDescending(f => f.Numero).ToListAsync(ct);
        return faturas.Select(f => new
        {
            f.Id, f.Numero, f.DataPagamento, f.Total, f.Taxa, f.Repasse, f.Incompleta,
            f.CaiuNoBanco, f.DataCreditoBanco, ConferidaSemCredito = !f.CaiuNoBanco && f.LancamentoBancoId == "",
            Avisos = Linhas(f.Avisos),
            Contratos = f.Contratos.Count,
            Pendencias = f.Contratos.Count(c => c.Situacao is SemViagem or Sugestao || Diverge(c) || (c.Placa != null && c.Socio == null)),
            PorSocio = PorSocio(f.Contratos),
        }).ToList();
    }

    public async Task<object?> FaturaAsync(string numero, CancellationToken ct = default)
    {
        var f = await db.StradaFaturas.AsNoTracking().Include(x => x.Contratos).FirstOrDefaultAsync(x => x.Numero == numero, ct);
        if (f == null) return null;
        var ids = f.Contratos.Where(c => c.ViagemId != null).Select(c => c.ViagemId!.Value).ToList();
        var viagens = await db.AgrotradeViagens.AsNoTracking().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var precisaCandidato = f.Contratos.Any(c => c.Situacao == SemViagem);
        var base_ = precisaCandidato
            ? await db.AgrotradeViagens.AsNoTracking().Where(v => v.Data <= (f.DataPagamento ?? DateOnly.MaxValue)).ToListAsync(ct)
            : new List<ViagemAgrotrade>();

        var observacoes = new List<string>();
        var porSocio = PorSocio(f.Contratos);
        if (f.Incompleta && f.Contratos.Count == 0)
            observacoes.Add("Fatura sem contratos no arquivo: suba o \"Exportar detalhamento\" desta fatura para dividir por sócio.");
        else
        {
            var soma = porSocio.Sum(s => s.Taxa);
            if (Math.Abs(soma - f.Taxa) == 0.01m)
                observacoes.Add($"A soma das taxas por sócio ({soma.ToString("N2", PtBr)}) difere 1 centavo da taxa da fatura ({f.Taxa.ToString("N2", PtBr)}).");
        }

        return new
        {
            f.Id, f.Numero, f.DataGeracao, f.DataPagamento, f.Total, f.Taxa, f.Repasse, f.Incompleta,
            Avisos = Linhas(f.Avisos), Observacoes = observacoes, PorSocio = porSocio,
            Contratos = f.Contratos.OrderBy(c => c.Contrato).Select(c => new
            {
                c.Id, c.Contrato, c.Valor, c.Adiantamento, c.Saldo, c.Tipo, c.Placa, c.Socio, NomeSocio = NomeSocio(c.Socio),
                c.ValorEsperado, c.Diferenca, c.Situacao, c.Nf, c.NfSugerida, c.Motorista, c.ValorContratoPdf, c.Seguro,
                TemPdf = c.ValorContratoPdf != null,
                Viagem = c.ViagemId is int id && viagens.TryGetValue(id, out var v) ? ResumoViagem(v) : null,
                Candidatos = c.Situacao == SemViagem
                    ? base_.Where(v => Igual(v.DifDescarga, c.Valor) || Igual(v.ValorDiaria, c.Valor)).Take(5).Select(ResumoViagem).ToList()
                    : new List<object>(),
            }).ToList(),
        };
    }

    public async Task<object> ListarViagensAsync(int? ano, int? mes, string? busca, CancellationToken ct = default)
    {
        var q = db.AgrotradeViagens.AsNoTracking().AsQueryable();
        if (ano is int a && mes is int m)
        {
            var (de, ate) = Mes(a, m);
            q = q.Where(v => v.Data >= de && v.Data <= ate);
        }
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim().ToUpperInvariant();
            var placa = ViagemCaminhaoCalculo.NormalizarPlaca(b);
            q = q.Where(v => (v.Nf != null && v.Nf.Contains(b)) || (v.Contrato != null && v.Contrato.Contains(b))
                || (v.Placa != null && v.Placa == placa) || (v.Motorista != null && v.Motorista.ToUpper().Contains(b)));
        }
        var viagens = await q.OrderByDescending(v => v.Data).ThenByDescending(v => v.Nf).Take(500).ToListAsync(ct);
        var ids = viagens.Select(v => v.Id).ToList();
        var ligados = await db.StradaContratos.AsNoTracking().Where(c => c.ViagemId != null && ids.Contains(c.ViagemId.Value))
            .Select(c => new { c.ViagemId, c.Tipo, c.Contrato, c.Valor, c.DataTransferencia, c.Situacao }).ToListAsync(ct);
        var socios = await SociosPorPlacaAsync(ct);
        return viagens.Select(v => new
        {
            v.Id, v.Data, v.Motorista, v.Placa, NomeSocio = NomeSocio(SocioDe(v.Placa, socios)), v.Destino, v.Nf, v.NfOriginal,
            v.Frete, v.Descontos, FreteEsperado = Esperado(v), v.Contrato, v.DifDescarga, v.ValorDiaria,
            Contratos = ligados.Where(c => c.ViagemId == v.Id).ToList(),
        }).ToList();
    }

    /// <summary>Viagens que cabem num contrato sem viagem (para ligar à mão): busca por NF, placa ou contrato.</summary>
    public Task<object> BuscarViagensAsync(string busca, CancellationToken ct = default) => ListarViagensAsync(null, null, busca, ct);

    /// <summary>
    /// Painel do mês: por sócio, recebido (faturas pagas no mês; histórico da CONTRATOS TIP que não
    /// está em fatura, pela data da transferência) e a receber (viagens do mês sem frete recebido e
    /// reembolsos anotados sem contrato). Divergências no alto.
    /// </summary>
    public async Task<object> PainelAsync(int ano, int mes, CancellationToken ct = default)
    {
        var (de, ate) = Mes(ano, mes);
        var socios = await SociosPorPlacaAsync(ct);
        var faturas = await db.StradaFaturas.AsNoTracking().Include(f => f.Contratos)
            .Where(f => f.DataPagamento >= de && f.DataPagamento <= ate).ToListAsync(ct);
        var emFatura = await db.StradaContratos.AsNoTracking().Where(c => c.FaturaId != null).Select(c => c.Contrato).Distinct().ToListAsync(ct);
        var emFaturaSet = emFatura.ToHashSet();
        var historico = (await db.StradaContratos.AsNoTracking()
                .Where(c => c.FaturaId == null && c.Origem == "planilha" && c.DataTransferencia >= de && c.DataTransferencia <= ate)
                .ToListAsync(ct))
            .Where(c => !emFaturaSet.Contains(c.Contrato)).ToList();

        var recebidos = faturas.SelectMany(f => f.Contratos).Concat(historico).ToList();
        var recebido = recebidos.GroupBy(c => c.Socio ?? "").Select(g =>
        {
            var total = g.Sum(c => c.Valor);
            var taxa = StradaRepasseLeitor.CalcularTaxa(total);
            return new { Socio = g.Key, Total = total, Taxa = taxa, Repasse = total - taxa };
        }).ToDictionary(x => x.Socio);
        // Fatura incompleta (só cabeçalho): mostrada à parte até subir o detalhamento.
        var incompletas = faturas.Where(f => f.Incompleta && f.Contratos.Count == 0).ToList();

        // A receber: viagens do mês (desde o início do controle).
        var inicio = de < AgrotradePlanilhaLeitor.Inicio ? AgrotradePlanilhaLeitor.Inicio : de;
        var viagens = await db.AgrotradeViagens.AsNoTracking().Where(v => v.Data >= inicio && v.Data <= ate).ToListAsync(ct);
        var ids = viagens.Select(v => v.Id).ToList();
        var contratosViagem = await db.StradaContratos.AsNoTracking()
            .Where(c => c.ViagemId != null && ids.Contains(c.ViagemId.Value)).ToListAsync(ct);
        var pagos = await db.StradaContratos.AsNoTracking()
            .Where(c => c.FaturaId != null || c.DataTransferencia != null).Select(c => c.Contrato).Distinct().ToListAsync(ct);
        var pagosSet = pagos.ToHashSet();

        var aReceber = new List<(string Socio, string Tipo, ViagemAgrotrade V, decimal Valor, string Motivo)>();
        foreach (var v in viagens)
        {
            var socio = SocioDe(v.Placa, socios) ?? "";
            var ligados = contratosViagem.Where(c => c.ViagemId == v.Id).ToList();
            var fretePago = ligados.Any(c => c.Tipo == "frete" && (c.FaturaId != null || c.DataTransferencia != null))
                || (v.Contrato != null && pagosSet.Contains(v.Contrato.Trim()));
            if (!fretePago && Esperado(v) is decimal esperado && esperado > 0)
                aReceber.Add((socio, "frete", v, esperado, v.Contrato == null ? "sem contrato" : "contrato ainda não pago"));
            var reembolsoPago = ligados.Any(c => c.Tipo == "reembolso" && (c.FaturaId != null || c.DataTransferencia != null));
            if (!reembolsoPago && !v.DescargaRecebidaPlanilha && v.DifDescarga is > 0)
                aReceber.Add((socio, "descarga", v, v.DifDescarga.Value, "reembolso de descarga não recebido"));
            if (!reembolsoPago && v.ValorDiaria is > 0)
                aReceber.Add((socio, "diária", v, v.ValorDiaria.Value, "diária não recebida"));
        }

        string[] ordem = [nameof(Socio.Bento), nameof(Socio.Caio), nameof(Socio.Dudu), ""];
        var linhas = ordem.Select(s => new
        {
            Socio = s == "" ? null : s,
            Nome = NomeSocio(s),
            Recebido = recebido.TryGetValue(s, out var r) ? r.Total : 0m,
            Taxa = recebido.TryGetValue(s, out var r2) ? r2.Taxa : 0m,
            Repasse = recebido.TryGetValue(s, out var r3) ? r3.Repasse : 0m,
            AReceber = aReceber.Where(x => x.Socio == s).Sum(x => x.Valor),
            ViagensAReceber = aReceber.Count(x => x.Socio == s),
        }).Where(l => l.Socio != null || l.Recebido != 0 || l.AReceber != 0).ToList();

        // Divergências (cada uma aponta a fatura/contrato ou a viagem).
        var divergencias = new List<object>();
        foreach (var f in faturas.OrderBy(f => f.DataPagamento))
        {
            if (!f.CaiuNoBanco && f.LancamentoBancoId == "")
                divergencias.Add(new { Tipo = "fatura não caiu no banco", Fatura = f.Numero, ContratoId = (int?)null, Contrato = (string?)null,
                    Texto = $"Fatura {f.Numero}: repasse de {f.Repasse.ToString("N2", PtBr)} pago em {Data(f.DataPagamento)} não apareceu no extrato do Sicoob." });
            if (f.Incompleta && f.Contratos.Count == 0)
                divergencias.Add(new { Tipo = "fatura incompleta", Fatura = f.Numero, ContratoId = (int?)null, Contrato = (string?)null,
                    Texto = $"Fatura {f.Numero} ({f.Total.ToString("N2", PtBr)}): só o cabeçalho; suba o \"Exportar detalhamento\"." });
            foreach (var c in f.Contratos.OrderBy(c => c.Contrato))
            {
                string? t = c.Situacao switch
                {
                    SemViagem => "contrato sem viagem",
                    Sugestao => "NF quase igual a confirmar",
                    _ => null,
                };
                if (t != null)
                    divergencias.Add(new { Tipo = t, Fatura = f.Numero, ContratoId = (int?)c.Id, Contrato = (string?)c.Contrato,
                        Texto = t == SemViagemTexto ? $"Contrato {c.Contrato} ({c.Valor.ToString("N2", PtBr)}) sem viagem: ligar pela NF ou à mão."
                            : $"Contrato {c.Contrato}: NF {c.Nf} quase igual à da viagem {c.NfSugerida} ({c.Placa}). Confirmar?" });
                if (Diverge(c))
                    divergencias.Add(new { Tipo = "valor diferente do esperado", Fatura = f.Numero, ContratoId = (int?)c.Id, Contrato = (string?)c.Contrato,
                        Texto = $"Contrato {c.Contrato}: recebido {c.Valor.ToString("N2", PtBr)}, esperado {Dinheiro(c.ValorEsperado)} (diferença {Dinheiro(c.Diferenca)})." });
                if (c.Placa != null && c.Socio == null)
                    divergencias.Add(new { Tipo = "placa sem sócio", Fatura = f.Numero, ContratoId = (int?)c.Id, Contrato = (string?)c.Contrato,
                        Texto = $"Contrato {c.Contrato}: placa {c.Placa} fora do cadastro de veículos." });
            }
        }
        // Data suspeita: transferência da CONTRATOS TIP antes da viagem.
        var viagensHist = historico.Where(c => c.ViagemId != null).Select(c => c.ViagemId!.Value).ToList();
        var datas = await db.AgrotradeViagens.AsNoTracking().Where(v => viagensHist.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Data, ct);
        foreach (var c in historico.Where(c => c.ViagemId != null && datas.TryGetValue(c.ViagemId.Value, out var d) && c.DataTransferencia < d))
            divergencias.Add(new { Tipo = "data suspeita", Fatura = (string?)null, ContratoId = (int?)c.Id, Contrato = (string?)c.Contrato,
                Texto = $"Contrato {c.Contrato}: transferência em {Data(c.DataTransferencia)} anterior à viagem em {Data(datas[c.ViagemId!.Value])} (CONTRATOS TIP)." });

        return new
        {
            Ano = ano, Mes = mes,
            Socios = linhas,
            Totais = new
            {
                // Fatura incompleta fica fora: o valor dela ainda não tem sócio (lista "Incompletas").
                Recebido = linhas.Sum(l => l.Recebido),
                Taxa = linhas.Sum(l => l.Taxa),
                Repasse = linhas.Sum(l => l.Repasse),
                AReceber = linhas.Sum(l => l.AReceber),
            },
            Incompletas = incompletas.Select(f => new { f.Numero, f.DataPagamento, f.Total, f.Taxa, f.Repasse }).ToList(),
            Divergencias = divergencias,
            AReceber = aReceber.OrderBy(x => x.V.Data).Select(x => new
            {
                ViagemId = x.V.Id, x.V.Data, x.V.Nf, x.V.Placa, x.V.Motorista, x.V.Contrato,
                Socio = x.Socio == "" ? null : x.Socio, NomeSocio = NomeSocio(x.Socio), x.Tipo, x.Valor, x.Motivo,
            }).ToList(),
            ContratosSemFatura = await db.StradaContratos.AsNoTracking().Where(c => c.FaturaId == null && c.Origem == "pdf")
                .OrderBy(c => c.Contrato)
                .Select(c => new { c.Id, c.Contrato, c.Valor, c.Placa, c.Nf, c.Situacao, c.NfSugerida }).ToListAsync(ct),
        };
    }

    private const string SemViagemTexto = "contrato sem viagem";

    // ───────────────────────── Auxiliares ─────────────────────────

    private static (DateOnly De, DateOnly Ate) Mes(int ano, int mes)
    {
        var de = new DateOnly(ano, mes, 1);
        return (de, de.AddMonths(1).AddDays(-1));
    }

    private static List<string> Linhas(string? s) =>
        string.IsNullOrWhiteSpace(s) ? [] : s.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool Igual(decimal? a, decimal b) => a is decimal x && x > 0 && Math.Abs(x - b) < 0.01m;

    private static bool Diverge(StradaContrato c) => c.Diferenca is decimal d && Math.Abs(d) >= 0.01m;

    private static decimal? Esperado(ViagemAgrotrade v) =>
        v.Frete is null ? null : Math.Round((v.Frete ?? 0) - (v.Descontos ?? 0), 2);

    private record SocioParte(string? Socio, string Nome, decimal Total, decimal Taxa, decimal Repasse);

    private static List<SocioParte> PorSocio(IEnumerable<StradaContrato> contratos) =>
        contratos.GroupBy(c => c.Socio).OrderBy(g => g.Key is null ? 9 : Array.IndexOf(Enum.GetNames<Socio>(), g.Key))
            .Select(g =>
            {
                var total = g.Sum(c => c.Valor);
                var taxa = StradaRepasseLeitor.CalcularTaxa(total);
                return new SocioParte(g.Key, NomeSocio(g.Key), total, taxa, total - taxa);
            }).ToList();

    private static object ResumoViagem(ViagemAgrotrade v) => new
    {
        v.Id, v.Data, v.Nf, v.NfOriginal, v.Placa, v.Motorista, v.Destino, v.Contrato,
        FreteEsperado = Esperado(v), v.DifDescarga, v.ValorDiaria,
    };

    private static AgrotradeViagem ParaRegistro(ViagemAgrotrade v) => new(
        v.Id, v.Data, v.Motorista, v.Placa, v.Destino, v.Unidade, v.Nf, v.NfOriginal, v.Frete, v.AdiantDescarga,
        v.Descontos, v.ValorDescarga, v.FreteTotal, v.Contrato, v.DifDescarga, v.ProtocoloDescarga,
        v.DataOcorrenciaDescarga, v.DescargaRecebidaPlanilha ? "SIM" : null, v.DataDescargaRecebidaPlanilha,
        v.ProtocoloDiaria, v.DataDiaria, v.ValorDiaria, v.Observacao, []);

    private Task<Dictionary<string, string>> SociosPorPlacaAsync(CancellationToken ct) => SociosPorPlacaAsync(db, ct);

    internal static async Task<Dictionary<string, string>> SociosPorPlacaAsync(AppDbContext db, CancellationToken ct)
    {
        var veiculos = await db.Veiculos.AsNoTracking().Select(v => new { v.Placa, v.Socio }).ToListAsync(ct);
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in veiculos.Where(v => !string.IsNullOrWhiteSpace(v.Placa)))
            mapa.TryAdd(ViagemCaminhaoCalculo.NormalizarPlaca(v.Placa), v.Socio.ToString());
        return mapa;
    }

    internal static string? SocioDe(string? placa, Dictionary<string, string> socios) =>
        placa != null && socios.TryGetValue(ViagemCaminhaoCalculo.NormalizarPlaca(placa), out var s) ? s : null;

    // Guarda o arquivo de origem, se ainda não guardado (mesmo nome e tamanho = o mesmo). Não salva.
    private async Task GuardarArquivoAsync(string tipo, string nome, byte[] bytes, string? referencia, CancellationToken ct)
    {
        var tamanho = bytes.Length;
        var existe = await db.StradaArquivos.AnyAsync(a => a.Tipo == tipo && a.Nome == nome && a.Conteudo.Length == tamanho, ct);
        if (!existe)
            db.StradaArquivos.Add(new StradaArquivo { Tipo = tipo, Nome = nome, Conteudo = bytes, Referencia = referencia, CriadoEm = DateTime.UtcNow });
    }
}
