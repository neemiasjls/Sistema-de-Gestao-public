using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// O e-mail semanal da Cedro Frutas (CD) e do Dunas: um .zip com o romaneio do
/// cliente, os boletos da Fazenda Alfa daquele cliente e, conforme o cliente, o
/// boleto das produtosD (CD) ou o XML e o DANFE da nota (Dunas).
///
/// Nada é montado à mão: cada peça vem de onde o sistema já guarda — romaneio
/// em PlanilhasCarga, nota em ArquivosNFe, boletos da fazenda nos PDFs dos
/// acertos (AcertoAlfaArquivos), boleto das produtosD em BoletosReceber. O que
/// não foi achado volta na lista do que falta, com o lugar onde subir.
///
/// O e-mail da Girassol (Horacio) é outro formato: só o boleto do frete que a
/// transportadora emitiu (BoletosReceber, cliente GIRASSOL), em PDF direto, sem .zip;
/// assunto e texto saem do relatório de frete .txt da carga (FRE-05), guardado em
/// PlanilhasCarga. Ver BOL-05.
/// </summary>
public class EmailClienteService(AppDbContext db, EmailService email)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public record Cliente(
        string Chave,                 // "CEDRO FRUTAS" — o apelido do romaneio e a chave da configuração
        string Rotulo,                // como aparece na tela
        string NomeAssunto,           // como vai no assunto: "CEDRO FRUTAS", "DISTRIBUIDORA DUNAS"
        string PagadorFazenda,         // rótulo do pagador nos boletos da fazenda (AcertoAlfaService)
        string SiglaBoleto,           // "BOLETO 1 CD 29-09.pdf", "BOLETO 1 VERD 29-09.pdf"
        string NomeZip,
        string DestinatarioNF,        // pedaço do destinatário da nota, sem acento nem pontuação
        bool NotaNoZip,               // XML e DANFE vão junto por padrão
        string? ClienteBoletoReceber, // boleto emitido pela transportadora (produtosD da CD, frete da Girassol)
        string[] DestinatariosPadrao,
        bool EhFrete = false);        // Perfil: boleto do frete, sem romaneio nem .zip (NomeZip vazio)

    public static readonly Cliente[] Clientes =
    [
        new("CEDRO FRUTAS", "Cedro Frutas (CD)", "CEDRO FRUTAS", "CD", "CD", "CD.zip",
            "COMERCIO DE HORTIFRUTI", true, BoletoReceberLeitor.Cd,
            ["faturamento@cedro.exemplo.com.br", "compras@cedro.exemplo.com.br", "contato@dunas.exemplo.com.br", "contabilidade@exemplo.com.br", "socio@transportadora.exemplo.com.br"]),
        new("DUNAS", "Dunas", "DISTRIBUIDORA DUNAS", "DUNAS", "VERD", "DUNAS.zip",
            "DUNAS", true, null,
            ["financeiro@dunas.exemplo.com.br", "escritorio@dunas.exemplo.com.br", "contabilidade@exemplo.com.br", "socio@transportadora.exemplo.com.br"]),
        new("GIRASSOL", "Girassol (Horacio)", "VOLUMES GIRASSOL", "", "", "",
            "", false, BoletoReceberLeitor.Perfil,
            ["frete@girassol.exemplo.com.br", "adm@girassol.exemplo.com.br", "socio@transportadora.exemplo.com.br"], EhFrete: true),
    ];

    public static Cliente? AcharCliente(string? chave) =>
        Clientes.FirstOrDefault(c => string.Equals(c.Chave, (chave ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    public const string TipoRomaneio = "ROMANEIO";
    public const string TipoBoletoFazenda = "BOLETO_FAZENDA";
    public const string TipoBoletoProdutosD = "BOLETO_PRODUTOSD";
    public const string TipoXml = "NF_XML";
    public const string TipoDanfe = "NF_DANFE";
    public const string TipoBoletoFrete = "BOLETO_FRETE";

    /// <summary>Uma peça que pode ir no .zip. A chave identifica a origem ("fazenda-12").</summary>
    public record Peca(string Chave, string Nome, string Tipo, string Descricao, byte[] Conteudo, bool Padrao);

    public record Montagem(Cliente Cliente, DateOnly Data, List<Peca> Pecas, List<string> Faltando, List<string> Avisos,
        List<string> NumerosNF, string Placa, string? Motorista,
        RelatorioFreteLeitor.Relatorio? Frete = null, int? RelatorioId = null, string? RelatorioNome = null);

    // ───────────────────────── Datas ─────────────────────────

    /// <summary>
    /// Dias de carga com romaneio da Cedro Frutas ou do Dunas, ou com relatório de
    /// frete da Girassol, do mais recente para trás.
    /// </summary>
    public async Task<List<DateOnly>> DatasComRomaneio(int quantas = 30)
    {
        var nomes = await db.PlanilhasCarga.AsNoTracking().Select(p => p.Nome).ToListAsync();
        return nomes
            .Select(RomaneioClienteLeitor.LerNome)
            .Where(i => i.Data != null && (i.Tipo == "frete"
                || (i.Tipo == "romaneio" && Clientes.Any(c => !c.EhFrete && c.Chave == i.Apelido))))
            .Select(i => DateOnly.FromDateTime(i.Data!.Value))
            .Distinct()
            .OrderByDescending(d => d)
            .Take(quantas)
            .ToList();
    }

    // ───────────────────────── Montagem ─────────────────────────

    public async Task<Montagem> Montar(Cliente c, DateOnly data)
    {
        if (c.EhFrete) return await MontarFrete(c, data);
        var pecas = new List<Peca>();
        var faltando = new List<string>();
        var avisos = new List<string>();
        var dataBr = data.ToString("dd/MM/yyyy");

        // ── Romaneio do cliente ──
        string placaRomaneio = "";
        string? motorista = null;
        RomaneioClienteLeitor.Resumo? resumo = null;
        var registros = await db.PlanilhasCarga.AsNoTracking()
            .Select(p => new { p.Id, p.Nome, p.CriadoEm })
            .ToListAsync();
        var doDia = registros
            .Select(p => new { p.Id, p.Nome, p.CriadoEm, Ident = RomaneioClienteLeitor.LerNome(p.Nome) })
            .Where(p => p.Ident.Tipo == "romaneio" && p.Ident.Apelido == c.Chave && p.Ident.Data?.Date == data.ToDateTime(TimeOnly.MinValue))
            .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
            .ToList();
        if (doDia.Count == 0)
            faltando.Add($"Romaneio {c.Rotulo} de {dataBr} não encontrado — gere na tela Planilhas NF-e ou importe na tela Romaneios.");
        else
        {
            var escolhido = doDia[0];
            var nomesDiferentes = doDia.Select(p => p.Nome.Trim().ToUpperInvariant()).Distinct().Count();
            if (nomesDiferentes > 1)
                avisos.Add($"Há {nomesDiferentes} romaneios {c.Rotulo} com a data de {dataBr} ({string.Join(", ", doDia.Select(p => p.Nome).Distinct())}); vai o mais recente, {escolhido.Nome}.");
            var conteudo = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Id == escolhido.Id).Select(p => p.Conteudo).FirstAsync();
            try
            {
                resumo = RomaneioClienteLeitor.Ler(conteudo);
                placaRomaneio = Placa(resumo.Placa);
                motorista = resumo.Motorista;
            }
            catch (Exception)
            {
                avisos.Add($"Não consegui ler o romaneio {escolhido.Nome}; ele vai assim mesmo, mas confira no Excel.");
            }
            pecas.Add(new Peca($"romaneio-{escolhido.Id}", escolhido.Nome.Trim(), TipoRomaneio, "Romaneio do cliente", conteudo, true));
        }

        // ── Nota fiscal do dia ──
        var numerosNF = new List<string>();
        string placaNota = "";
        var notas = (await db.ArquivosNFe.AsNoTracking()
                .Where(a => a.DataEmissao == data)
                .Select(a => new { a.Id, a.NumeroNF, a.Destinatario, a.NomeXml, a.Xml, a.NomePdf, a.Pdf })
                .ToListAsync())
            .Where(a => Normalizar(a.Destinatario).Contains(c.DestinatarioNF))
            .OrderBy(a => a.NumeroNF.Length).ThenBy(a => a.NumeroNF)
            .ToList();
        if (notas.Count == 0)
        {
            if (c.NotaNoZip)
                faltando.Add($"Nota fiscal {c.Rotulo} de {dataBr} não encontrada — suba o XML e o DANFE na tela Notas Fiscais.");
            else
                avisos.Add($"Nota fiscal {c.Rotulo} de {dataBr} não encontrada: o assunto sai sem o número da nota. Suba o XML na tela Notas Fiscais.");
        }
        foreach (var n in notas)
        {
            numerosNF.Add(n.NumeroNF);
            try
            {
                var dados = NFeImportService.ParseXml(Encoding.UTF8.GetString(n.Xml));
                if (placaNota == "") placaNota = Placa(dados.Placa);
            }
            catch (Exception) { }

            pecas.Add(new Peca($"xml-{n.Id}", NomeOuPadrao(n.NomeXml, $"NF {n.NumeroNF}.xml"), TipoXml,
                $"XML da NF {n.NumeroNF}", n.Xml, c.NotaNoZip));
            if (n.Pdf is { Length: > 0 })
                pecas.Add(new Peca($"danfe-{n.Id}", NomeOuPadrao(n.NomePdf, $"DANFE_{n.NumeroNF}.pdf"), TipoDanfe,
                    $"DANFE da NF {n.NumeroNF}", n.Pdf, c.NotaNoZip));
            else if (c.NotaNoZip)
                faltando.Add($"DANFE da NF {n.NumeroNF} não está guardado — suba o PDF na tela Notas Fiscais.");
        }
        if (notas.Count > 1)
            avisos.Add($"{notas.Count} notas {c.Rotulo} com data de {dataBr}: {string.Join(", ", numerosNF)}.");

        var placa = placaNota != "" ? placaNota : placaRomaneio;
        if (placaNota != "" && placaRomaneio != "" && placaNota != placaRomaneio)
            avisos.Add($"A nota diz placa {placaNota} e o romaneio diz {placaRomaneio}; o assunto vai com a da nota.");
        if (placa == "")
            avisos.Add("Não achei a placa na nota nem no romaneio: o assunto sai sem a placa.");

        // ── Boletos da fazenda para o cliente, com data do documento no dia da carga ──
        var segunda = data.AddDays(-(((int)data.DayOfWeek + 6) % 7));
        var arquivos = await db.AcertoAlfaArquivos.AsNoTracking()
            .Where(a => a.Tipo == "BOLETO" && a.Acerto!.Semana >= segunda.AddDays(-7) && a.Acerto.Semana <= segunda.AddDays(7))
            .OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.Nome, a.Conteudo })
            .ToListAsync();
        var daFazenda = arquivos
            .Select(a => new { a.Id, a.Conteudo, Boleto = AcertoAlfaService.LerBoletoGuardado(a.Conteudo) })
            .Where(a => a.Boleto != null && a.Boleto.Cliente == c.PagadorFazenda && DateOnly.FromDateTime(a.Boleto.DataDoc) == data)
            .GroupBy(a => a.Boleto!.Numero)                // o mesmo boleto subido duas vezes vai uma vez só
            .Select(g => g.First())
            .OrderBy(a => a.Boleto!.Vencimento).ThenBy(a => a.Boleto!.Numero)
            .ToList();
        if (daFazenda.Count == 0)
            faltando.Add($"Boletos da Fazenda Alfa para {c.Rotulo} de {dataBr} não encontrados — suba os PDFs dos boletos na tela Acerto Alfa.");
        for (int i = 0; i < daFazenda.Count; i++)
        {
            var b = daFazenda[i].Boleto!;
            pecas.Add(new Peca($"fazenda-{daFazenda[i].Id}", $"BOLETO {i + 1} {c.SiglaBoleto} {data:dd-MM}.pdf", TipoBoletoFazenda,
                $"Boleto da fazenda {b.Numero}: {((decimal)b.Valor).ToString("C", PtBr)}, vence {b.Vencimento:dd/MM/yyyy}",
                daFazenda[i].Conteudo, true));
        }

        // ── Boleto emitido pela transportadora (produtosD da CD) ──
        var valoresEmitidos = new List<decimal>();
        if (c.ClienteBoletoReceber is string cr)
        {
            var emitidos = await db.BoletosReceber.AsNoTracking()
                .Where(b => b.Cliente == cr && b.DataCarga == data && b.Pdf != null)
                .OrderBy(b => b.Id)
                .Select(b => new { b.Id, b.NomeArquivo, b.Pdf, b.Valor, b.Vencimento, b.NossoNumero })
                .ToListAsync();
            if (emitidos.Count == 0)
                faltando.Add("Boleto das produtosD não encontrado — suba em Boletos → A receber.");
            if (emitidos.Count > 1)
                avisos.Add($"{emitidos.Count} boletos das produtosD com a carga de {dataBr}; confira quais vão.");
            foreach (var b in emitidos)
            {
                if (b.Valor is decimal valorEmitido) valoresEmitidos.Add(Math.Round(valorEmitido, 2));
                var desc = "Boleto das produtosD" +
                    (b.Valor is decimal v ? $": {v.ToString("C", PtBr)}" : "") +
                    (b.Vencimento is DateOnly ve ? $", vence {ve:dd/MM/yyyy}" : "");
                pecas.Add(new Peca($"produtosD-{b.Id}", NomeOuPadrao(b.NomeArquivo, $"BOLETO PRODUTOSD {data:dd-MM}.pdf"),
                    TipoBoletoProdutosD, desc, b.Pdf!, true));
            }
        }

        // O romaneio diz quanto os boletos somam (na CD o das produtosD entra
        // junto): se o que foi achado não bate, falta ou sobra algum
        if (resumo != null && resumo.ValoresBoletos.Count > 0 && daFazenda.Count > 0)
        {
            var achados = daFazenda.Select(a => Math.Round((decimal)a.Boleto!.Valor, 2)).Concat(valoresEmitidos).ToList();
            var somaAchados = achados.Sum();
            var somaRomaneio = resumo.ValoresBoletos.Sum();
            if (Math.Abs(somaAchados - somaRomaneio) > 0.05m)
                avisos.Add($"Os boletos achados somam {somaAchados.ToString("C", PtBr)}, mas o romaneio fala em {somaRomaneio.ToString("C", PtBr)} ({resumo.ValoresBoletos.Count} boleto(s)). Confira se falta algum.");
            else if (achados.Count != resumo.ValoresBoletos.Count)
                avisos.Add($"O romaneio fala em {resumo.ValoresBoletos.Count} boleto(s) e foram achados {achados.Count}.");
        }

        return new Montagem(c, data, Ordenar(pecas), faltando, avisos, numerosNF, placa, motorista);
    }

    // ───────────────────────── Perfil: boleto do frete ─────────────────────────

    // Boleto de outra carga só entra como sugestão se for de até tantos dias antes ou depois
    private const int DiasSugestao = 10;

    private record BoletoFrete(int Id, string NomeArquivo, byte[]? Pdf, decimal? Valor, DateOnly? Vencimento,
        string NossoNumero, string Placa, DateOnly? DataCarga, int? Sequencia);

    private async Task<Montagem> MontarFrete(Cliente c, DateOnly data)
    {
        var pecas = new List<Peca>();
        var faltando = new List<string>();
        var avisos = new List<string>();
        var dataBr = data.ToString("dd/MM/yyyy");

        // ── Relatório de frete (.txt) da carga ──
        var registros = (await db.PlanilhasCarga.AsNoTracking()
                .Select(p => new { p.Id, p.Nome, p.CriadoEm })
                .ToListAsync())
            .Select(p => new { p.Id, p.Nome, p.CriadoEm, Ident = RomaneioClienteLeitor.LerNome(p.Nome) })
            .Where(p => p.Ident.Tipo == "frete" && p.Ident.Data != null)
            .ToList();
        // Dias que têm relatório próprio: o boleto de um deles não é sugerido para outro
        var diasComRelatorio = registros.Select(p => DateOnly.FromDateTime(p.Ident.Data!.Value)).ToHashSet();
        var doDia = registros
            .Where(p => DateOnly.FromDateTime(p.Ident.Data!.Value) == data)
            .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
            .ToList();

        RelatorioFreteLeitor.Relatorio? rel = null;
        int? relId = null;
        string? relNome = null;
        if (doDia.Count == 0)
            faltando.Add($"Relatório de frete da Girassol de {dataBr} não encontrado — gere na tela Planilhas NF-e ou suba o .txt aqui.");
        else
        {
            var escolhido = doDia[0];
            var nomes = doDia.Select(p => p.Nome.Trim().ToUpperInvariant()).Distinct().ToList();
            if (nomes.Count > 1)
                avisos.Add($"Há {nomes.Count} relatórios de frete com a data de {dataBr} ({string.Join(", ", doDia.Select(p => p.Nome).Distinct())}); vale o mais recente, {escolhido.Nome}.");
            var conteudo = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Id == escolhido.Id).Select(p => p.Conteudo).FirstAsync();
            var texto = RelatorioFreteLeitor.Texto(conteudo);
            if (!RelatorioFreteLeitor.EhRelatorio(texto))
                avisos.Add($"{escolhido.Nome} não parece um relatório de frete; confira.");
            rel = RelatorioFreteLeitor.Ler(texto, escolhido.Nome);
            avisos.AddRange(rel.Avisos);
            relId = escolhido.Id;
            relNome = escolhido.Nome;
        }
        var placa = rel?.Placa ?? "";

        // ── Boleto do frete emitido para a Girassol ──
        var emitidos = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Cliente == c.ClienteBoletoReceber && b.Pdf != null && b.DataCarga != null
                        && b.DataCarga >= data.AddDays(-DiasSugestao) && b.DataCarga <= data.AddDays(DiasSugestao))
            .OrderBy(b => b.DataCarga).ThenBy(b => b.Id)
            .Select(b => new BoletoFrete(b.Id, b.NomeArquivo, b.Pdf, b.Valor, b.Vencimento, b.NossoNumero, b.Placa, b.DataCarga, b.Sequencia))
            .ToListAsync();

        string Descricao(BoletoFrete b) =>
            $"Boleto do frete {(b.Sequencia is int s ? s + " " : "")}({b.NossoNumero}), carga de {b.DataCarga:dd/MM/yyyy}" +
            (string.IsNullOrWhiteSpace(b.Placa) ? "" : $" ({b.Placa})") +
            (b.Valor is decimal v ? $": {v.ToString("C", PtBr)}" : "") +
            (b.Vencimento is DateOnly ve ? $", vence {ve:dd/MM/yyyy}" : "");

        var exatos = emitidos.Where(b => b.DataCarga == data).ToList();
        // Mais de um boleto no dia: o da placa do relatório vai; os outros ficam para marcar
        var daPlaca = placa == "" ? exatos : exatos.Where(b => Placa(b.Placa) == placa).ToList();
        if (daPlaca.Count == 0) daPlaca = exatos;
        if (exatos.Count > 1)
            avisos.Add($"{exatos.Count} boletos da Girassol com a carga de {dataBr}; confira qual vai.");

        foreach (var b in exatos)
        {
            var vai = daPlaca.Contains(b) && (daPlaca.Count == 1 || b == daPlaca[0]);
            pecas.Add(new Peca($"frete-{b.Id}", NomeOuPadrao(b.NomeArquivo, $"BOLETO FRETE {data:dd-MM}.pdf"), TipoBoletoFrete,
                Descricao(b), b.Pdf!, vai));
            if (!vai) continue;
            if (placa != "" && !string.IsNullOrWhiteSpace(b.Placa) && Placa(b.Placa) != placa)
                avisos.Add($"O boleto {b.NomeArquivo} diz caminhão {Placa(b.Placa)}, mas o relatório de frete é do {placa}.");
            if (rel?.TotalFrete is decimal total && b.Valor is decimal valor && Math.Abs(valor - total) > 0.005m)
                avisos.Add($"O boleto {b.NomeArquivo} é de {valor.ToString("C", PtBr)}, mas o relatório de frete dá {total.ToString("C", PtBr)}. Confira antes de enviar.");
            if (placa == "" && !string.IsNullOrWhiteSpace(b.Placa)) placa = Placa(b.Placa);
        }

        if (exatos.Count == 0)
        {
            faltando.Add($"Boleto da Girassol da carga de {dataBr}{(placa != "" ? $" ({placa})" : "")} não encontrado — suba em Boletos → A receber ou solte no Acerto Alfa.");
            // Os de cargas vizinhas sem relatório próprio podem ser este com a data trocada
            foreach (var b in emitidos.Where(b => !diasComRelatorio.Contains(b.DataCarga!.Value)))
            {
                pecas.Add(new Peca($"frete-{b.Id}", NomeOuPadrao(b.NomeArquivo, $"BOLETO FRETE {b.DataCarga:dd-MM}.pdf"), TipoBoletoFrete,
                    Descricao(b), b.Pdf!, false));
                var difere = new List<string>();
                if (placa != "" && !string.IsNullOrWhiteSpace(b.Placa) && Placa(b.Placa) != placa) difere.Add($"caminhão {Placa(b.Placa)}");
                if (rel?.TotalFrete is decimal total && b.Valor is decimal valor && Math.Abs(valor - total) > 0.005m)
                    difere.Add($"valor {valor.ToString("C", PtBr)} contra {total.ToString("C", PtBr)} do relatório");
                avisos.Add($"O boleto {b.NomeArquivo} é da carga de {b.DataCarga:dd/MM/yyyy}, não desta" +
                    (difere.Count > 0 ? $" ({string.Join("; ", difere)})" : "") + ". Só marque se for mesmo o desta carga.");
            }
        }

        if (placa == "") avisos.Add("Não achei a placa no nome do relatório nem no boleto: o assunto sai sem a placa.");
        return new Montagem(c, data, pecas, faltando, avisos, [], placa, null, rel, relId, relNome);
    }

    // A ordem do .zip de sempre: romaneio, boletos da fazenda, produtosD, nota
    private static List<Peca> Ordenar(List<Peca> pecas)
    {
        string[] ordem = [TipoRomaneio, TipoBoletoFazenda, TipoBoletoProdutosD, TipoBoletoFrete, TipoXml, TipoDanfe];
        return pecas.OrderBy(p => Array.IndexOf(ordem, p.Tipo)).ToList();
    }

    // ───────────────────────── Texto do e-mail ─────────────────────────

    public static string Assunto(Montagem m)
    {
        if (m.Cliente.EhFrete)
        {
            // "FRETE VOLUMES GIRASSOL REF. 29/09/2026 - 1247 CAIXAS - 30 PRODUTOSE (XAD-5H46)"; sem produtoE, sem essa parte
            var f = $"FRETE {m.Cliente.NomeAssunto} REF. {m.Data:dd/MM/yyyy}";
            if (m.Frete?.CaixasNormais is double n) f += $" - {RelatorioFreteLeitor.Caixas(n)} CAIXAS";
            if (m.Frete?.CaixasProdutoE is double cp && cp > 0) f += $" - {RelatorioFreteLeitor.Caixas(cp)} PRODUTOSE";
            if (m.Placa != "") f += $" ({m.Placa})";
            return f;
        }
        var s = $"ROMANEIO E BOLETOS {m.Cliente.NomeAssunto} REF. {m.Data:dd/MM/yyyy}";
        if (m.NumerosNF.Count > 0) s += $" - NF {string.Join("/", m.NumerosNF)}";
        if (m.Placa != "") s += $" ({m.Placa})";
        return s;
    }

    public string Corpo(Montagem m, DateTime? agoraUtc = null)
    {
        var hora = (agoraUtc ?? DateTime.UtcNow).AddHours(-3).Hour;
        var saudacao = hora < 12 ? "Bom dia" : hora < 18 ? "Boa tarde" : "Boa noite";
        if (m.Cliente.EhFrete)
        {
            var partes = new List<string>();
            if (m.Frete?.CaixasNormais is double n)
                partes.Add($"{RelatorioFreteLeitor.Caixas(n)} {(n == 1 ? "caixa" : "caixas")}");
            if (m.Frete?.CaixasProdutoE is double cp && cp > 0)
                partes.Add($"{RelatorioFreteLeitor.Caixas(cp)} {(cp == 1 ? "caixa" : "caixas")} de produtoE");
            var detalhe = partes.Count > 0 ? $" ({string.Join(" e ", partes)})" : "";
            return $"""
                {saudacao},

                Segue em anexo o boleto do frete da carga de {m.Data:dd/MM/yyyy}{detalhe}.

                Qualquer dúvida, estamos à disposição.

                Atenciosamente,
                {email.RemetenteNome}
                """;
        }
        var nota = m.NumerosNF.Count > 0 ? $" (NF {string.Join("/", m.NumerosNF)})" : "";
        var conteudo = m.Cliente.NotaNoZip
            ? "o romaneio, os boletos e a nota fiscal"
            : "o romaneio e os boletos";
        return $"""
            {saudacao},

            Segue em anexo {conteudo} da carga de {m.Data:dd/MM/yyyy}{nota}.

            Qualquer dúvida, estamos à disposição.

            Atenciosamente,
            {email.RemetenteNome}
            """;
    }

    // ───────────────────────── Destinatários ─────────────────────────

    public async Task<List<string>> Destinatarios(Cliente c)
    {
        var cfg = await db.EmailsClienteConfig.AsNoTracking().FirstOrDefaultAsync(x => x.Cliente == c.Chave);
        var lista = cfg == null ? [.. c.DestinatariosPadrao] : SepararEnderecos(cfg.Destinatarios);
        return lista;
    }

    private static readonly Regex EmailRx = new(@"^[^@\s;,]+@[^@\s;,]+\.[^@\s;,]+$", RegexOptions.Compiled);
    public static bool EnderecoValido(string e) => EmailRx.IsMatch(e.Trim());

    public static List<string> SepararEnderecos(string? texto) =>
        (texto ?? "").Split([';', ',', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public async Task SalvarDestinatarios(Cliente c, List<string> enderecos)
    {
        var cfg = await db.EmailsClienteConfig.FirstOrDefaultAsync(x => x.Cliente == c.Chave);
        if (cfg == null)
        {
            cfg = new EmailClienteConfig { Cliente = c.Chave };
            db.EmailsClienteConfig.Add(cfg);
        }
        cfg.Destinatarios = string.Join(";", enderecos);
        cfg.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    // ───────────────────────── .zip ─────────────────────────

    public static (byte[] Bytes, List<string> Nomes) Zipar(IEnumerable<Peca> pecas)
    {
        var nomes = new List<string>();
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pecas)
            {
                var nome = NomeUnico(p.Nome, usados);
                var entrada = zip.CreateEntry(nome, CompressionLevel.Optimal);
                using var s = entrada.Open();
                s.Write(p.Conteudo);
                nomes.Add(nome);
            }
        }
        return (ms.ToArray(), nomes);
    }

    private static string NomeUnico(string nome, HashSet<string> usados)
    {
        nome = string.Join("_", nome.Split(Path.GetInvalidFileNameChars())).Trim();
        if (nome == "") nome = "arquivo";
        var candidato = nome;
        for (int i = 2; !usados.Add(candidato); i++)
            candidato = $"{Path.GetFileNameWithoutExtension(nome)} ({i}){Path.GetExtension(nome)}";
        return candidato;
    }

    // ───────────────────────── Envio ─────────────────────────

    public record Pedido(Cliente Cliente, DateOnly Data, List<string> Destinatarios, string Assunto, string Corpo, List<string> Chaves, bool Reenviar);

    public record Resultado(bool Enviado, bool JaEnviado, string? Erro, EmailClienteEnvio? Envio, List<string> Anexos, bool ModoTeste,
        IReadOnlyList<string> EnviadoPara, string? Aviso);

    // Dois cliques em "Enviar" (ou as duas pessoas ao mesmo tempo) não mandam dois e-mails
    private static readonly SemaphoreSlim Um = new(1, 1);

    /// <summary>
    /// Última troca de boleto (Substituir) das cargas do cliente, por data da carga. Envio
    /// feito antes da troca foi com o boleto velho: não conta como enviado.
    /// </summary>
    public async Task<Dictionary<DateOnly, DateTime>> TrocasDeBoleto(Cliente c)
    {
        if (c.ClienteBoletoReceber is not string cr) return [];
        var trocas = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Cliente == cr && b.SubstituidoEm != null && b.DataCarga != null)
            .Select(b => new { Data = b.DataCarga!.Value, Em = b.SubstituidoEm!.Value })
            .ToListAsync();
        return trocas.GroupBy(t => t.Data).ToDictionary(g => g.Key, g => g.Max(t => t.Em));
    }

    public async Task<Resultado> Enviar(Pedido p)
    {
        await Um.WaitAsync();
        try
        {
            var modoTeste = email.ModoTeste;
            // O envio de teste não impede o de verdade, e vice-versa
            var anterior = await db.EmailsClienteEnvios.AsNoTracking()
                .Where(e => e.Cliente == p.Cliente.Chave && e.DataCarga == p.Data && e.Erro == null && e.ModoTeste == modoTeste)
                .OrderByDescending(e => e.EnviadoEm)
                .FirstOrDefaultAsync();
            // Boleto trocado depois do envio: o envio foi com o boleto velho, manda sem perguntar
            if (anterior != null && (await TrocasDeBoleto(p.Cliente)).TryGetValue(p.Data, out var troca) && anterior.EnviadoEm < troca)
                anterior = null;
            if (anterior != null && !p.Reenviar)
                return new Resultado(false, true,
                    $"O e-mail {p.Cliente.Rotulo} de {p.Data:dd/MM/yyyy} já foi enviado em {anterior.EnviadoEm.AddHours(-3):dd/MM/yyyy 'às' HH:mm}{(modoTeste ? " (teste)" : "")}. Confirme para mandar de novo.",
                    anterior, [], modoTeste, [], null);

            var montagem = await Montar(p.Cliente, p.Data);
            var escolhidas = new List<Peca>();
            foreach (var chave in p.Chaves.Distinct())
            {
                var peca = montagem.Pecas.FirstOrDefault(x => x.Chave == chave);
                if (peca == null)
                    return new Resultado(false, false, "Algum arquivo da prévia não existe mais. Atualize a prévia e confira de novo.",
                        null, [], modoTeste, [], null);
                escolhidas.Add(peca);
            }
            escolhidas = Ordenar(escolhidas);
            if (escolhidas.Count == 0)
                return new Resultado(false, false, "Escolha pelo menos um arquivo.", null, [], modoTeste, [], null);

            List<EmailService.Anexo> anexos;
            List<string> nomes;
            int tamanho;
            if (string.IsNullOrEmpty(p.Cliente.NomeZip))
            {
                var usados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                anexos = escolhidas.Select(x => new EmailService.Anexo(NomeUnico(x.Nome, usados), x.Conteudo)).ToList();
                nomes = anexos.Select(a => a.Nome).ToList();
                tamanho = anexos.Sum(a => a.Conteudo.Length);
            }
            else
            {
                var (zip, nomesZip) = Zipar(escolhidas);
                anexos = [new EmailService.Anexo(p.Cliente.NomeZip, zip)];
                nomes = nomesZip;
                tamanho = zip.Length;
            }
            var preparado = email.Preparar(new EmailService.EmailComAnexos(
                p.Destinatarios, p.Assunto.Trim(), p.Corpo.Trim(), anexos));
            var res = await email.EnviarComAnexosAsync(preparado);

            var envio = new EmailClienteEnvio
            {
                Cliente = p.Cliente.Chave,
                DataCarga = p.Data,
                Assunto = preparado.Assunto,
                Destinatarios = string.Join(";", preparado.DestinatariosReais),
                EnviadoPara = string.Join(";", preparado.Para),
                Anexos = string.Join("\n", nomes),
                NomeZip = p.Cliente.NomeZip,
                TamanhoZip = tamanho,
                ModoTeste = preparado.ModoTeste,
                MessageId = res.MessageId,
                Erro = res.Enviado ? null : res.Erro ?? "Falha desconhecida.",
                EnviadoEm = DateTime.UtcNow,
            };

            string? aviso = null;
            try
            {
                db.EmailsClienteEnvios.Add(envio);
                await db.SaveChangesAsync();
            }
            catch (Exception)
            {
                db.ChangeTracker.Clear();
                // O e-mail já saiu (ou já falhou): não dá para desfazer, só avisar
                aviso = "O envio aconteceu, mas não consegui guardar no histórico.";
            }

            return new Resultado(res.Enviado, false, res.Enviado ? null : envio.Erro, envio, nomes, preparado.ModoTeste, preparado.Para, aviso);
        }
        finally { Um.Release(); }
    }

    // ───────────────────────── Apoio ─────────────────────────

    private static string NomeOuPadrao(string? nome, string padrao) =>
        string.IsNullOrWhiteSpace(nome) ? padrao : Path.GetFileName(nome.Trim());

    // Placa no formato da frota: "XAD 5H46", "xad5h46" → "XAD-5H46"
    public static string Placa(string? p)
    {
        var t = Regex.Replace((p ?? "").ToUpperInvariant(), @"[^A-Z0-9]", "");
        return t.Length == 7 ? $"{t[..3]}-{t[3..]}" : (p ?? "").Trim().ToUpperInvariant();
    }

    // Maiúsculas, sem acento e sem pontuação: "C.D. COMERCIO DE HORTIFRUTI" → "R D COMERCIO DE HORTIFRUTI"
    private static string Normalizar(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return Regex.Replace(Regex.Replace(sb.ToString().ToUpperInvariant(), @"[^A-Z0-9]", " "), @"\s+", " ").Trim();
    }
}
