using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.AcertoAlfaService;

namespace TransportadoraSystem.API.Controllers;

[Authorize]
[ApiController]
[Route("api/acerto-alfa")]
public class AcertoAlfaController(AcertoAlfaService svc, AppDbContext db, RomaneiosEmbalarService embalar,
    ILogger<AcertoAlfaController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string TipoPlanilha = "PLANILHA";

    // Sugere o próximo número (último + 1)
    [HttpGet("proximo-numero")]
    public async Task<IActionResult> ProximoNumero() => Ok(new { proximo = await ProximoNumeroLivre() });

    // Confere os PDFs da semana (boletos + romaneios da fazenda) contra os
    // romaneios de cliente que o sistema gerou e guardou nas cargas. Os romaneios
    // de cliente não são enviados: são achados pela semana dos PDFs.
    [HttpPost("conferencia")]
    public async Task<IActionResult> Conferencia(IFormFileCollection boletos, [FromServices] ConferenciaSemanaService conf)
    {
        try
        {
            if (boletos == null || boletos.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs dos boletos e dos romaneios da fazenda." });

            var arquivos = await LerArquivos(boletos);
            return Ok(await ConferirPdfs(arquivos.Select(a => a.Bytes).ToList(), conf));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro na conferência da semana");
            return StatusCode(500, new { erro = $"Erro ao conferir: {ex.Message}" });
        }
    }

    // Gera a planilha a partir dos boletos e romaneios + número
    // (o campo do formulário continua se chamando "boletos"; o serviço separa os
    //  dois tipos pelo conteúdo de cada PDF). Além de devolver a planilha, guarda
    // o acerto com os dados lidos e os PDFs, para ver e editar depois pela tela.
    // A tela passou a usar "semana/arquivos"; esta rota fica para quem ainda chama.
    [HttpPost("gerar")]
    public async Task<IActionResult> Gerar(IFormFileCollection boletos, [FromQuery] int numero)
    {
        try
        {
            if (boletos == null || boletos.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs dos boletos ou dos romaneios." });
            if (numero <= 0)
                return BadRequest(new { erro = "Número do acerto inválido." });

            var arquivos = await LerArquivos(boletos);
            var lido = svc.Ler(arquivos.Select(a => a.Bytes));
            // Semana antes do Gerar: ela é tirada da ordem em que os PDFs chegaram
            var semana = DateOnly.FromDateTime(Semana(lido.Boletos, lido.Romaneios));
            var cadeiaAntes = await Cadeia(numero - 1);
            var res = svc.Gerar(lido, numero, OrigemDepois(cadeiaAntes));

            // guarda o número usado (pra sugerir +1 na próxima)
            var contador = await db.AcertoAlfaContadores.OrderBy(c => c.Id).FirstOrDefaultAsync();
            if (contador == null) db.AcertoAlfaContadores.Add(new() { Ultimo = numero });
            else contador.Ultimo = numero;
            await db.SaveChangesAsync();

            // Guardar é um passo a mais: se falhar, a planilha sai do mesmo jeito
            // e o aviso conta que o acerto não ficou guardado.
            var avisos = new List<string>(res.Avisos);
            try
            {
                await Guardar(numero, semana, lido, arquivos);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao guardar o acerto Alfa {Numero}", numero);
                db.ChangeTracker.Clear();
                avisos.Add("A planilha foi gerada, mas não consegui guardar o acerto no sistema.");
            }

            if (avisos.Count > 0)
                Response.Headers["X-Avisos"] = Uri.EscapeDataString(string.Join(" | ", avisos));
            // A liberação desses cabeçalhos fica na política de CORS (Program.cs) — setar
            // Access-Control-Expose-Headers aqui não funciona, o middleware sobrescreve depois.

            return File(res.Bytes, Xlsx, res.NomeArquivo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar acerto Alfa");
            return StatusCode(500, new { erro = $"Erro ao gerar planilha: {ex.Message}" });
        }
    }

    // Gerar de novo o mesmo número é refazer o acerto: os dados passam a ser os
    // da leitura nova e os PDFs se somam aos que já estavam (o mesmo arquivo,
    // pelo nome e tamanho, não entra duas vezes).
    private async Task Guardar(int numero, DateOnly semana, Leitura lido, List<Arquivo> arquivos)
    {
        var agora = DateTime.UtcNow;
        var acerto = await db.AcertosAlfa.FirstOrDefaultAsync(a => a.Numero == numero);
        if (acerto == null)
        {
            acerto = new AcertoAlfa { Numero = numero, CriadoEm = agora };
            db.AcertosAlfa.Add(acerto);
        }

        acerto.Semana = semana;
        acerto.DadosJson = Serializar(ParaDados(lido.Boletos, lido.Romaneios, semana.ToDateTime(TimeOnly.MinValue)));
        acerto.AvisosJson = JsonSerializer.Serialize(lido.Avisos, AcertoAlfaService.Json);
        acerto.AtualizadoEm = agora;

        await AnexarArquivos(acerto, arquivos, lido.Tipos, agora);
        await db.SaveChangesAsync();
    }

    // Acrescenta os arquivos que o acerto ainda não tem. Devolve quantos entraram.
    private async Task<int> AnexarArquivos(AcertoAlfa acerto, List<Arquivo> arquivos, List<string> tipos, DateTime agora)
    {
        var jaTem = acerto.Id == 0
            ? []
            : await db.AcertoAlfaArquivos
                .Where(x => x.AcertoId == acerto.Id)
                .Select(x => new { x.Nome, Tamanho = x.Conteudo.Length })
                .ToListAsync();
        var chaves = jaTem.Select(x => (x.Nome, x.Tamanho)).ToHashSet();

        int novos = 0;
        for (int i = 0; i < arquivos.Count; i++)
        {
            var a = arquivos[i];
            if (!chaves.Add((a.Nome, a.Bytes.Length))) continue;
            acerto.Arquivos.Add(new AcertoAlfaArquivo
            {
                Nome = a.Nome,
                Tipo = i < tipos.Count ? tipos[i] : "OUTRO",
                Conteudo = a.Bytes,
                CriadoEm = agora,
            });
            novos++;
        }
        return novos;
    }

    // ───────────────────── Acerto da semana, aos poucos ─────────────────────

    public record ResultadoSemana(int AcertoId, int Numero, DateOnly Semana, bool Criado,
        int ArquivosNovos, int BoletosNovos, int RomaneiosNovos, List<string> Avisos);

    /// <summary>
    /// O que cada arquivo enviado era e para onde foi, para a tela mostrar um por um
    /// (UI-04: o que não foi reconhecido aparece, não some). Tipo: BOLETO_FAZENDA,
    /// ROMANEIO_FAZENDA, BOLETO_RECEBER (CD ou Girassol, emitido pela transportadora),
    /// NAO_RECONHECIDO, ILEGIVEL ou FORA (não é PDF). Destino é onde ficou guardado;
    /// null quando ficou de fora (ou já estava guardado).
    /// </summary>
    public record ArquivoIdentificado(string Nome, string Tipo, string? Destino, int? AcertoId, string? Cliente,
        decimal? Valor, DateOnly? Vencimento, string? Numero, DateOnly? Data, int? Caixas, string? Placa, List<string> Avisos,
        List<RomaneiosEmbalarService.Info>? Embalar = null, string? Situacao = null, List<Diferenca>? Diferencas = null);

    // Um upload só para tudo o que chega da semana, um arquivo ou vários: boletos e
    // romaneios da fazenda entram no acerto da semana deles (pela data do documento),
    // criado com o próximo número se ainda não existe; os boletos que a
    // transportadora emite (CD e Girassol) vão para Boletos > A receber. Quem decide é
    // o conteúdo do PDF, não o nome. O que já está no acerto, inclusive o que foi
    // editado na tela, não é tocado: os boletos ocupam as linhas vazias e os
    // romaneios se somam.
    [HttpPost("semana/arquivos")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ArquivosDaSemana(IFormFileCollection arquivos, [FromServices] BoletoReceberImportacao importacao,
        [FromQuery] bool substituir = false)
    {
        try
        {
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs dos boletos ou dos romaneios." });

            var todos = await LerArquivos(arquivos);
            var itens = new List<ArquivoIdentificado>();
            foreach (var a in todos.Where(a => !EhPdf(a.Nome)))
                itens.Add(Fora(a.Nome, "FORA", "Não é PDF. Planilhas de acerto entram por \"Importar acertos (planilhas)\"."));
            var pdfs = todos.Where(a => EhPdf(a.Nome)).ToList();

            // Boletos da transportadora (CD e Girassol) vão para A receber
            var (daTransportadora, daFazenda) = Separar(pdfs);
            if (daTransportadora.Count > 0)
            {
                var r = await importacao.Importar(daTransportadora.Select(a => (a.Nome, a.Bytes)));
                try { await db.SaveChangesAsync(); }
                catch (DbUpdateException ex)
                {
                    logger.LogError(ex, "Erro ao guardar boletos a receber pelo acerto");
                    db.ChangeTracker.Clear();
                    return Conflict(new { erro = "Não consegui guardar os boletos da CD ou da Girassol: o mesmo boleto pode ter sido subido ao mesmo tempo. Envie de novo." });
                }
                itens.AddRange(r.Itens.Select(ParaItem));
            }

            // Cada PDF da fazenda sozinho, para saber o que é e de que semana. Documento que
            // já está num acerto (mesmo número) vai para aquele acerto, para ser comparado lá.
            var porSemana = new Dictionary<DateOnly, List<Arquivo>>();
            var semSemana = new List<Arquivo>();
            var lidos = new Dictionary<Arquivo, Leitura>();
            var (emRomaneios, emBoletos) = daFazenda.Count > 0
                ? await OndeEstaoOsDocumentos()
                : (new Dictionary<int, Onde>(), new Dictionary<string, Onde>());
            foreach (var pdf in daFazenda)
            {
                Leitura lido;
                try { lido = svc.Ler([pdf.Bytes]); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "PDF ilegível no acerto da semana: {Nome}", pdf.Nome);
                    itens.Add(Fora(pdf.Nome, "ILEGIVEL", "Não consegui ler o PDF; ficou de fora."));
                    continue;
                }
                lidos[pdf] = lido;
                if (lido.Boletos.Count == 0 && lido.Romaneios.Count == 0) { semSemana.Add(pdf); continue; }
                var existente = lido.Romaneios.Select(r => emRomaneios.GetValueOrDefault(r.Numero))
                    .Concat(lido.Boletos.Select(b => emBoletos.GetValueOrDefault(ChaveBoleto(b.Numero))))
                    .FirstOrDefault(o => o != null);
                var semana = existente?.Semana ?? DateOnly.FromDateTime(Semana(lido.Boletos, lido.Romaneios));
                if (!porSemana.TryGetValue(semana, out var lista)) porSemana[semana] = lista = [];
                lista.Add(pdf);
            }

            // O que não foi reconhecido só tem para onde ir se o envio é de uma semana só
            var semLeituraGuardados = new HashSet<Arquivo>();
            if (semSemana.Count > 0)
            {
                if (porSemana.Count == 1)
                {
                    porSemana.Values.First().AddRange(semSemana);
                    semLeituraGuardados.UnionWith(semSemana);
                }
                else
                    foreach (var a in semSemana)
                        itens.Add(Fora(a.Nome, "NAO_RECONHECIDO", "Não reconheci boleto nem romaneio neste PDF; ficou de fora.", lidos[a].Avisos));
            }

            var resultados = new List<ResultadoSemana>();
            foreach (var (semana, grupo) in porSemana.OrderBy(x => x.Key))
            {
                var acerto = await db.AcertosAlfa
                    .Where(a => a.Semana == semana)
                    .OrderByDescending(a => a.Numero)
                    .FirstOrDefaultAsync();
                var criado = acerto == null;
                if (acerto == null)
                {
                    var numero = await ProximoNumeroLivre();
                    acerto = new AcertoAlfa
                    {
                        Numero = numero, Semana = semana, DadosJson = Serializar(Novo(semana)), CriadoEm = DateTime.UtcNow,
                    };
                    db.AcertosAlfa.Add(acerto);
                    await GuardarContador(numero);
                }

                var r = await AcrescentarPdfs(acerto, grupo, substituir, lidos);
                resultados.Add(new ResultadoSemana(acerto.Id, acerto.Numero, acerto.Semana, criado,
                    r.ArquivosNovos, r.BoletosNovos, r.RomaneiosNovos, r.Avisos));

                var destino = $"Acerto {acerto.Numero} (semana de {acerto.Semana:dd/MM/yyyy})";
                foreach (var a in grupo)
                {
                    var res = r.PorArquivo.GetValueOrDefault(a);
                    var situacao = res?.Situacao;
                    // Só tem destino o que foi gravado agora
                    var gravado = situacao is "NOVO" or "SUBSTITUIDO" or "SO_ARQUIVO";
                    var lido = lidos[a];
                    if (semLeituraGuardados.Contains(a))
                    {
                        itens.Add(new ArquivoIdentificado(a.Nome, "NAO_RECONHECIDO", gravado ? destino : null, acerto.Id,
                            null, null, null, null, null, null, null,
                            [gravado ? "Não reconheci boleto nem romaneio: ficou guardado no acerto só como arquivo, sem lançar nada." : "Já estava guardado neste acerto.",
                             .. lido.Avisos], Situacao: situacao));
                        continue;
                    }
                    itens.Add(Identificado(a.Nome, lido, gravado ? destino : null, acerto.Id, (res?.Avisos ?? []).Except(lido.Avisos).ToList())
                        with { Situacao = situacao, Diferencas = res?.Diferencas });
                }
            }

            // Romaneio da fazenda sem funrural = volumes para embalar: entra no controle
            // de volumes da regra (ou fica aguardando), e a tela mostra para onde foi
            if (itens.Any(i => i.Tipo == "ROMANEIO_FAZENDA"))
            {
                await embalar.AtualizarAsync();
                itens = await ComEmbalar(itens);
            }

            // Na ordem em que foram enviados
            var ordem = todos.Select((a, i) => (a.Nome, i)).GroupBy(x => x.Nome).ToDictionary(g => g.Key, g => g.First().i);
            itens = itens.OrderBy(i => ordem.GetValueOrDefault(i.Nome, int.MaxValue)).ToList();

            if (porSemana.Count == 0 && daTransportadora.Count == 0)
                return BadRequest(new { erro = "Nenhum boleto ou romaneio reconhecido nos arquivos.", avisos = new List<string>(), arquivos = itens });

            return Ok(new { Acertos = resultados, Avisos = new List<string>(), Arquivos = itens });
        }
        catch (DbUpdateException ex)
        {
            // O número único segura dois envios ao mesmo tempo criando o mesmo acerto
            logger.LogError(ex, "Conflito ao guardar o acerto da semana");
            return Conflict(new { erro = "Outro envio guardou um acerto ao mesmo tempo. Envie os PDFs de novo." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao guardar os PDFs no acerto da semana");
            return StatusCode(500, new { erro = $"Erro ao ler os PDFs: {ex.Message}" });
        }
    }

    // Boleto emitido pela transportadora (beneficiária) à parte dos PDFs da fazenda
    private static (List<Arquivo> DaTransportadora, List<Arquivo> DaFazenda) Separar(List<Arquivo> pdfs)
    {
        var dela = pdfs.Where(a => BoletoReceberLeitor.EmitidoPelaTransportadora(a.Bytes)).ToList();
        return (dela, pdfs.Where(a => !dela.Contains(a)).ToList());
    }

    private static ArquivoIdentificado Fora(string nome, string tipo, string aviso, IEnumerable<string>? mais = null) =>
        new(nome, tipo, null, null, null, null, null, null, null, null, null, [aviso, .. mais ?? []]);

    private static ArquivoIdentificado ParaItem(BoletoReceberImportacao.Item i)
    {
        var b = i.Boleto;
        var destino = i.Situacao is "NOVO" or "COMPLETADO" && b != null
            ? $"Boletos a receber: {BoletoReceberLeitor.RotuloCliente(b.Cliente)}" : null;
        return new ArquivoIdentificado(i.Arquivo, i.Situacao == "RECUSADO" ? "NAO_RECONHECIDO" : "BOLETO_RECEBER", destino, null,
            b != null ? BoletoReceberLeitor.RotuloCliente(b.Cliente) : i.Pagador, b?.Valor, b?.Vencimento,
            b?.NossoNumero, b?.DataCarga, null, b?.Placa, i.Avisos);
    }

    // O que um PDF da fazenda trouxe. Um PDF pode ter mais de um romaneio: os números
    // saem juntos e caixas e valor somados.
    private static ArquivoIdentificado Identificado(string nome, Leitura l, string? destino, int acertoId, List<string> avisos)
    {
        if (l.Boletos.Count > 0)
        {
            var b = l.Boletos[0];
            return new ArquivoIdentificado(nome, "BOLETO_FAZENDA", destino, acertoId, b.Cliente, Math.Round((decimal)b.Valor, 2),
                DateOnly.FromDateTime(b.Vencimento), b.Numero, DateOnly.FromDateTime(b.DataDoc), null, null, [.. avisos, .. l.Avisos]);
        }
        var rs = l.Romaneios;
        return new ArquivoIdentificado(nome, "ROMANEIO_FAZENDA", destino, acertoId, null, Math.Round((decimal)rs.Sum(r => r.Valor), 2), null,
            string.Join(", ", rs.Select(r => r.Numero)), DateOnly.FromDateTime(rs.Min(r => r.Data)), rs.Sum(r => r.Caixas), null,
            [.. avisos, .. l.Avisos]);
    }

    // ───────────────────── Planilhas de acerto feitas à mão ─────────────────────

    public record ResultadoImportacao(string Arquivo, int? AcertoId, int? Numero, DateOnly? Semana, bool Criado,
        Totais? Totais, TotaisGravados? Gravados, List<string> Avisos, string? Erro);

    // As planilhas "NNN-ACERTO G. ALFA-dd-MM-aaaa.xlsx" do Excel viram acertos:
    // número e semana pelo nome, os dois blocos pelas colunas. Acerto de mesmo
    // número tem os dados trocados pelos da planilha (com aviso); a planilha fica
    // guardada junto como arquivo do acerto. Cada arquivo é gravado sozinho, então
    // um com problema não segura os outros, e mandar de novo não duplica nada.
    [HttpPost("importar-planilhas")]
    public async Task<IActionResult> ImportarPlanilhas(IFormFileCollection arquivos)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Envie as planilhas de acerto (.xlsx)." });

        var resultados = new List<ResultadoImportacao>();
        foreach (var a in await LerArquivos(arquivos))
        {
            ResultadoImportacao Erro(string erro) => new(a.Nome, null, null, null, false, null, null, [], erro);

            if (a.Nome.StartsWith("~$")) { resultados.Add(Erro("Arquivo temporário do Excel (começa com ~$), ficou de fora.")); continue; }
            if (!a.Nome.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) { resultados.Add(Erro("Não é planilha .xlsx.")); continue; }
            if (LerNomePlanilha(a.Nome) is not { } nome)
            {
                resultados.Add(Erro("O nome precisa ser como \"194-ACERTO G. ALFA-21-09-2026.xlsx\" (número e semana saem dele)."));
                continue;
            }

            PlanilhaLida lida;
            try { lida = svc.LerPlanilha(a.Bytes); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Planilha de acerto ilegível: {Nome}", a.Nome);
                resultados.Add(Erro(ex is InvalidDataException ? ex.Message : "Não consegui abrir a planilha."));
                continue;
            }

            var avisos = new List<string>();
            var semana = Segunda(nome.Semana);
            if (semana != nome.Semana)
                avisos.Add($"A data do nome ({nome.Semana:dd/MM/yyyy}) não é segunda-feira; ficou a semana de {semana:dd/MM/yyyy}.");

            try
            {
                var agora = DateTime.UtcNow;
                var acerto = await db.AcertosAlfa.FirstOrDefaultAsync(x => x.Numero == nome.Numero);
                var criado = acerto == null;
                if (acerto == null)
                {
                    acerto = new AcertoAlfa { Numero = nome.Numero, CriadoEm = agora };
                    db.AcertosAlfa.Add(acerto);
                }
                else avisos.Add($"O acerto {nome.Numero} já existia: os dados foram trocados pelos da planilha. Os arquivos guardados nele continuam.");
                avisos.AddRange(lida.Avisos);

                acerto.Semana = semana;
                acerto.DadosJson = Serializar(lida.Dados);
                acerto.AvisosJson = JsonSerializer.Serialize(lida.Avisos, AcertoAlfaService.Json);
                acerto.AtualizadoEm = agora;
                await AnexarArquivos(acerto, [a], [TipoPlanilha], agora);
                await GuardarContador(nome.Numero);
                await db.SaveChangesAsync();

                resultados.Add(new ResultadoImportacao(a.Nome, acerto.Id, acerto.Numero, semana, criado,
                    Totalizar(lida.Dados), lida.Gravados, avisos, null));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao importar a planilha de acerto {Nome}", a.Nome);
                db.ChangeTracker.Clear();
                resultados.Add(Erro("Não consegui guardar o acerto desta planilha."));
            }
        }

        try { await NormalizarSaldoAnterior(resultados); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao conferir o saldo anterior das planilhas importadas");
            db.ChangeTracker.Clear();
        }
        return Ok(resultados);
    }

    // ───────────────────────── Acertos guardados ─────────────────────────

    // Do mais novo para o mais velho, com os totais para a lista da tela
    [HttpGet("acertos")]
    public async Task<IActionResult> Listar()
    {
        var linhas = await db.AcertosAlfa
            .OrderByDescending(a => a.Semana).ThenByDescending(a => a.Numero)
            .Select(a => new
            {
                a.Id, a.Numero, a.Semana, a.DadosJson, a.CriadoEm, a.AtualizadoEm,
                Arquivos = a.Arquivos.Count,
            })
            .ToListAsync();

        // O saldo de cada um depende do SALDO ANT. automático, que vem do anterior
        var cadeia = Encadear(linhas.Select(a => (a.Numero, a.Semana, Desserializar(a.DadosJson, a.Semana))))
            .ToDictionary(e => e.Numero);

        return Ok(linhas.Select(a =>
        {
            var e = cadeia[a.Numero];
            var d = e.Valendo;
            var t = e.Totais;
            return new
            {
                a.Id, a.Numero, a.Semana,
                TotalRomaneios = t.ValorRomaneios,
                TotalAPagar = t.APagar,
                TotalPagamentos = t.Pagamentos,
                t.Saldo,
                TotalCaixas = t.Caixas,
                QtdRomaneios = d.Romaneios.Count,
                QtdBoletos = d.Pagamentos.Count(p => EhBoleto(p) && p.Valor != null),
                QtdBoletosSemValor = d.Pagamentos.Count(p => EhBoleto(p) && p.Valor == null),
                QtdArquivos = a.Arquivos,
                a.CriadoEm, a.AtualizadoEm,
            };
        }));
    }

    [HttpGet("acertos/{id:int}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        var acerto = await db.AcertosAlfa.FirstOrDefaultAsync(a => a.Id == id);
        if (acerto == null) return NotFound(new { erro = "Acerto não encontrado." });
        // Acerto gravado antes da marca de origem: decide uma vez e guarda (sem mexer em AtualizadoEm)
        if (await CompletarOrigem(acerto)) await db.SaveChangesAsync();
        return Ok(await Detalhar(acerto));
    }

    // ACE-11: linhas gravadas antes da marca DoArquivo ganham a origem pelos PDFs guardados no
    // acerto (romaneio ou boleto cujo número está num deles veio do arquivo). Só lê os PDFs se
    // alguma linha ainda está sem marca; não salva (quem chama salva). Devolve se mudou.
    private async Task<bool> CompletarOrigem(AcertoAlfa acerto)
    {
        var dados = Desserializar(acerto.DadosJson, acerto.Semana);
        if (!OrigemPendente(dados)) return false;

        var romaneios = new HashSet<int>();
        var boletos = new HashSet<string>();
        if (acerto.Id != 0)
        {
            var pdfs = await db.AcertoAlfaArquivos.AsNoTracking()
                .Where(x => x.AcertoId == acerto.Id && (x.Tipo == "BOLETO" || x.Tipo == "ROMANEIO"))
                .Select(x => x.Conteudo)
                .ToListAsync();
            foreach (var pdf in pdfs)
            {
                try
                {
                    var l = svc.Ler([pdf]);
                    romaneios.UnionWith(l.Romaneios.Select(r => r.Numero));
                    boletos.UnionWith(l.Boletos.Select(b => ChaveBoleto(b.Numero)));
                }
                catch (Exception ex) { logger.LogWarning(ex, "PDF ilegível ao marcar a origem do acerto {Id}", acerto.Id); }
            }
        }
        acerto.DadosJson = Serializar(MarcarOrigem(dados, romaneios, boletos));
        return true;
    }

    public record RomaneioEdicao(int Numero, string? Rotulo, string? Data, double Valor, int Caixas, double? DescontoCaixa);
    public record PagamentoEdicao(string? Data, string? Condicao, double? Valor, string? Observacao, string? Numero);
    // Lista que não vem (null) fica como está guardada. AtualizadoEm é o do acerto
    // quando a tela abriu: se ele mudou depois (chegou PDF, outra pessoa salvou),
    // salvar por cima apagaria o que entrou, então a edição é recusada.
    public record AcertoEdicao(int Numero, string? Semana, List<RomaneioEdicao>? Romaneios, List<PagamentoEdicao>? Pagamentos,
        List<string>? Notas, DateTime? AtualizadoEm = null);

    // Salva o que foi editado na tela. As datas chegam como texto para o erro sair
    // em português e apontando a linha, em vez do 400 genérico do ASP.NET.
    [HttpPut("acertos/{id:int}")]
    public async Task<IActionResult> Editar(int id, [FromBody] AcertoEdicao req)
    {
        var acerto = await db.AcertosAlfa.FirstOrDefaultAsync(a => a.Id == id);
        if (acerto == null) return NotFound(new { erro = "Acerto não encontrado." });

        // O banco guarda microssegundos; a tela pode ter o valor com mais casas
        if (req.AtualizadoEm is { } visto && (acerto.AtualizadoEm - visto).Duration() > TimeSpan.FromMilliseconds(1))
            return Conflict(new { erro = "O acerto mudou desde que foi aberto (chegaram PDFs ou alguém salvou). Anote o que alterou, descarte e abra o acerto de novo." });

        if (req.Numero <= 0) return BadRequest(new { erro = "Número do acerto inválido." });
        if (await db.AcertosAlfa.AnyAsync(a => a.Numero == req.Numero && a.Id != id))
            return Conflict(new { erro = $"Já existe outro acerto com o número {req.Numero}." });

        if (LerData(req.Semana) is not { } semanaDia)
            return BadRequest(new { erro = "Semana com data inválida." });

        await CompletarOrigem(acerto);
        var guardado = Desserializar(acerto.DadosJson, acerto.Semana);

        // ACE-11: romaneio e boleto que vieram de arquivo não se editam, só saem. Linha da
        // tela com o número de um deles fica como está guardada (no romaneio, só o desconto
        // por caixa muda: ele não está no documento). Vale também para quem chamar a API direto.
        var romaneiosDoArquivo = guardado.Romaneios.Where(r => r.DoArquivo == true && r.Numero > 0)
            .GroupBy(r => r.Numero).ToDictionary(g => g.Key, g => g.First());
        var boletosDoArquivo = guardado.Pagamentos.Where(p => p.DoArquivo == true && BoletoComNumero(p))
            .GroupBy(p => ChaveBoleto(p.Numero)).ToDictionary(g => g.Key, g => g.First());

        var romaneios = guardado.Romaneios;
        if (req.Romaneios != null)
        {
            romaneios = [];
            var i = 0;
            foreach (var r in req.Romaneios)
            {
                i++;
                if (r.Numero > 0 && romaneiosDoArquivo.TryGetValue(r.Numero, out var doArquivo))
                {
                    if (r.DescontoCaixa is { } dca && (!double.IsFinite(dca) || dca < 0 || dca > 1000))
                        return BadRequest(new { erro = $"Romaneio {r.Numero}: desconto por caixa inválido." });
                    romaneios.Add(doArquivo with { DescontoCaixa = r.DescontoCaixa is { } x ? Math.Round(x, 2) : null });
                    continue;
                }
                var rotulo = string.IsNullOrWhiteSpace(r.Rotulo) ? null : r.Rotulo.Trim().ToUpperInvariant();
                var nome = r.Numero > 0 ? $"Romaneio {r.Numero}" : rotulo != null ? $"Linha {i} ({rotulo})" : $"Romaneio {i}";
                if (r.Numero < 0 || (r.Numero == 0 && rotulo == null))
                    return BadRequest(new { erro = $"Romaneio {i}: informe o número (ou um texto como BOLETO ou CH DEV)." });
                if (rotulo is { Length: > 30 })
                    return BadRequest(new { erro = $"{nome}: o texto no lugar do número passa de 30 letras." });
                if (LerData(r.Data) is not { } data)
                    return BadRequest(new { erro = $"{nome}: data inválida." });
                if (!double.IsFinite(r.Valor) || r.Valor < 0)
                    return BadRequest(new { erro = $"{nome}: valor não pode ser negativo." });
                if (r.Caixas < 0)
                    return BadRequest(new { erro = $"{nome}: caixas não pode ser negativo." });
                if (r.DescontoCaixa is { } dc && (!double.IsFinite(dc) || dc < 0 || dc > 1000))
                    return BadRequest(new { erro = $"{nome}: desconto por caixa inválido." });
                romaneios.Add(new RomaneioDados(r.Numero > 0 ? r.Numero : 0, data, Math.Round(r.Valor, 2), r.Caixas,
                    r.DescontoCaixa is { } d ? Math.Round(d, 2) : null, r.Numero > 0 ? null : rotulo, false));
            }
            var romaneioRepetido = romaneios.Where(r => r.Numero > 0).GroupBy(r => r.Numero).FirstOrDefault(g => g.Count() > 1);
            if (romaneioRepetido != null)
                return BadRequest(new { erro = $"O romaneio {romaneioRepetido.Key} aparece mais de uma vez." });
        }

        var pagamentos = guardado.Pagamentos;
        if (req.Pagamentos != null)
        {
            pagamentos = [];
            var i = 0;
            foreach (var p in req.Pagamentos)
            {
                i++;
                if (boletosDoArquivo.TryGetValue(ChaveBoleto(p.Numero), out var boletoDoArquivo))
                {
                    pagamentos.Add(boletoDoArquivo);
                    continue;
                }
                var condicao = (p.Condicao ?? "").Trim().ToUpperInvariant();
                var observacao = (p.Observacao ?? "").Trim();
                var numero = (p.Numero ?? "").Trim();
                // Linha toda em branco some, como na planilha
                if (condicao == "" && observacao == "" && numero == "" && p.Valor == null && string.IsNullOrWhiteSpace(p.Data)) continue;
                if (condicao == "")
                    return BadRequest(new { erro = $"Pagamento {i}: informe a condição (PIX, BOLETO, DEPÓSITO...)." });
                if (condicao.Length > 30 || observacao.Length > 100 || numero.Length > 40)
                    return BadRequest(new { erro = $"Pagamento {i}: texto comprido demais." });
                DateOnly? data = null;
                if (!string.IsNullOrWhiteSpace(p.Data))
                {
                    if (LerData(p.Data) is not { } d) return BadRequest(new { erro = $"Pagamento {i}: data inválida." });
                    data = d;
                }
                if (p.Valor is { } v && !double.IsFinite(v))
                    return BadRequest(new { erro = $"Pagamento {i}: valor inválido." });
                // Lançada à mão: SALDO ANT., PIX, depósito, boleto sem PDF
                pagamentos.Add(new PagamentoDados(data, condicao, p.Valor is { } vv ? Math.Round(vv, 2) : null, observacao, numero, false));
            }
            var boletoRepetido = pagamentos.Where(p => EhBoleto(p) && p.Numero != "")
                .GroupBy(p => p.Numero, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (boletoRepetido != null)
                return BadRequest(new { erro = $"O boleto {boletoRepetido.Key} aparece mais de uma vez." });
        }

        var notas = req.Notas == null
            ? guardado.Notas
            : req.Notas.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (notas.Any(n => n.Length > 300)) return BadRequest(new { erro = "Observação comprida demais (até 300 letras)." });

        acerto.Numero = req.Numero;
        acerto.Semana = Segunda(semanaDia);
        acerto.DadosJson = Serializar(new Dados(romaneios, pagamentos, notas));
        acerto.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(await Detalhar(acerto));
    }

    // A planilha sai sempre dos dados guardados — é o Excel como saída, não fonte
    [HttpGet("acertos/{id:int}/planilha")]
    public async Task<IActionResult> Planilha(int id)
    {
        try
        {
            var acerto = await db.AcertosAlfa.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (acerto == null) return NotFound(new { erro = "Acerto não encontrado." });

            var res = GerarDoAcerto(acerto, await Valendo(acerto));
            if (res.Avisos.Count > 0)
                Response.Headers["X-Avisos"] = Uri.EscapeDataString(string.Join(" | ", res.Avisos));
            return File(res.Bytes, Xlsx, res.NomeArquivo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar a planilha do acerto {Id}", id);
            return StatusCode(500, new { erro = $"Erro ao gerar planilha: {ex.Message}" });
        }
    }

    /// <summary>
    /// Todos os romaneios da fazenda, de todos os acertos (tela Romaneios, parte da
    /// fazenda). O que veio em PDF diz se é de cliente (desconto de funrural) ou de
    /// embalar e traz o arquivo para baixar; o importado das planilhas antigas, não.
    /// </summary>
    [HttpGet("romaneios-fazenda")]
    public async Task<IActionResult> RomaneiosFazenda()
    {
        var acertos = await db.AcertosAlfa.AsNoTracking().Select(a => new { a.Id, a.Numero, a.DadosJson }).ToListAsync();
        var lidos = (await db.RomaneiosFazendaLidos.AsNoTracking()
            .Select(r => new { r.Numero, r.DescFunrural, r.ArquivoId, r.AcertoId }).ToListAsync())
            .GroupBy(r => r.Numero).ToDictionary(g => g.Key, g => g.First());
        var lista = new List<object>();
        foreach (var a in acertos)
        {
            System.Text.Json.JsonDocument doc;
            try { doc = System.Text.Json.JsonDocument.Parse(a.DadosJson); } catch (System.Text.Json.JsonException) { continue; }
            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("romaneios", out var roms) || roms.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                foreach (var r in roms.EnumerateArray())
                {
                    int? numero = r.TryGetProperty("numero", out var n) && n.TryGetInt32(out var x) && x > 0 ? x : null;
                    DateOnly? data = r.TryGetProperty("data", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.String
                        && DateOnly.TryParse(d.GetString(), out var dd) ? dd : null;
                    decimal? Num(string campo) => r.TryGetProperty(campo, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDecimal() : null;
                    var lido = numero is { } nn ? lidos.GetValueOrDefault(nn) : null;
                    lista.Add(new
                    {
                        numero, data, caixas = Num("caixas"), valor = Num("valor"), descontoCaixa = Num("descontoCaixa"),
                        acertoId = a.Id, acertoNumero = a.Numero,
                        tipo = lido == null ? null : lido.DescFunrural ? "cliente" : "embalar",
                        arquivoId = lido?.AcertoId == a.Id ? lido.ArquivoId : null,
                    });
                }
            }
        }
        return Ok(lista);
    }

    [HttpGet("acertos/{id:int}/arquivos/{arquivoId:int}")]
    public async Task<IActionResult> BaixarArquivo(int id, int arquivoId)
    {
        var arq = await db.AcertoAlfaArquivos.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == arquivoId && x.AcertoId == id);
        if (arq == null) return NotFound(new { erro = "Arquivo não encontrado." });
        var tipo = arq.Tipo == TipoPlanilha || arq.Nome.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? Xlsx : "application/pdf";
        return File(arq.Conteudo, tipo, arq.Nome);
    }

    // Mais PDFs para um acerto que já existe: o que for lido se SOMA ao que já
    // está (e às edições feitas na tela). Boleto e romaneio de número que o
    // acerto já tem ficam de fora, e o PDF igual a um guardado nem é lido.
    [HttpPost("acertos/{id:int}/arquivos")]
    public async Task<IActionResult> AcrescentarArquivos(int id, IFormFileCollection arquivos, [FromServices] BoletoReceberImportacao importacao,
        [FromQuery] bool substituir = false)
    {
        try
        {
            var acerto = await db.AcertosAlfa.FirstOrDefaultAsync(a => a.Id == id);
            if (acerto == null) return NotFound(new { erro = "Acerto não encontrado." });
            if (arquivos == null || arquivos.Count == 0)
                return BadRequest(new { erro = "Envie os PDFs." });

            var todos = await LerArquivos(arquivos);
            var foraPdf = todos.Where(a => !EhPdf(a.Nome)).Select(a => a.Nome).ToList();
            // Boleto da CD ou da Girassol solto aqui vai para A receber, não para o acerto
            var (daTransportadora, daFazenda) = Separar(todos.Where(a => EhPdf(a.Nome)).ToList());
            var avisosReceber = new List<string>();
            if (daTransportadora.Count > 0)
            {
                var ri = await importacao.Importar(daTransportadora.Select(a => (a.Nome, a.Bytes)));
                await db.SaveChangesAsync();
                foreach (var i in ri.Itens.Where(i => i.Situacao is "NOVO" or "COMPLETADO"))
                    avisosReceber.Add($"{i.Arquivo}: é boleto da transportadora; foi para Boletos a receber ({BoletoReceberLeitor.RotuloCliente(i.Boleto!.Cliente)}), não para o acerto.");
                avisosReceber.AddRange(ri.Avisos);
            }
            var r = await AcrescentarPdfs(acerto, daFazenda, substituir);
            if (r.ArquivosNovos > 0) await embalar.AtualizarAsync();
            r.Avisos.InsertRange(0, avisosReceber);
            if (foraPdf.Count > 0) r.Avisos.Insert(0, $"Só PDF entra aqui; ficou de fora: {string.Join(", ", foraPdf)}.");
            // Os avisos de cada PDF que não pede escolha (os que pedem aparecem na janela)
            foreach (var p in r.PorArquivo.Values.Where(p => p.Situacao != "CONFIRMAR"))
                r.Avisos.AddRange(p.Avisos.Select(x => $"{p.Arquivo}: {x}"));

            return Ok(new
            {
                Acerto = await Detalhar(acerto),
                r.BoletosNovos,
                r.RomaneiosNovos,
                r.ArquivosNovos,
                r.Substituidos,
                r.Avisos,
                Arquivos = r.PorArquivo.Values.ToList(),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao acrescentar PDFs ao acerto {Id}", id);
            return StatusCode(500, new { erro = $"Erro ao ler os PDFs: {ex.Message}" });
        }
    }

    /// <summary>
    /// O que aconteceu com um PDF da fazenda (ACE-11). Situacao: NOVO (entrou algo), IGUAL (o
    /// arquivo ou os documentos dele já estavam, iguais), CONFIRMAR (documento de mesmo número
    /// com diferença: nada deste arquivo foi gravado, espera "Substituir"), SUBSTITUIDO,
    /// SO_ARQUIVO (sem boleto nem romaneio: guardado só como arquivo), FORA (os documentos estão
    /// em outro acerto) ou ILEGIVEL.
    /// </summary>
    public record ResultadoPdf(string Arquivo, string Situacao, List<Diferenca> Diferencas, List<string> Avisos);

    private record Acrescimo(int ArquivosNovos, int BoletosNovos, int RomaneiosNovos, int Substituidos, List<string> Avisos,
        Dictionary<Arquivo, ResultadoPdf> PorArquivo);

    // Onde está cada documento (romaneio pelo número de controle, boleto pelo nosso número),
    // em todos os acertos: é a chave do "mesmo documento" (ACE-11).
    private record Onde(int AcertoId, int Numero, DateOnly Semana);
    private async Task<(Dictionary<int, Onde> Romaneios, Dictionary<string, Onde> Boletos)> OndeEstaoOsDocumentos()
    {
        var romaneios = new Dictionary<int, Onde>();
        var boletos = new Dictionary<string, Onde>();
        var acertos = await db.AcertosAlfa.AsNoTracking().OrderBy(a => a.Numero)
            .Select(a => new { a.Id, a.Numero, a.Semana, a.DadosJson }).ToListAsync();
        foreach (var a in acertos)
        {
            Dados d;
            try { d = Desserializar(a.DadosJson, a.Semana); } catch (JsonException) { continue; }
            var onde = new Onde(a.Id, a.Numero, a.Semana);
            foreach (var r in d.Romaneios.Where(r => r.Numero > 0)) romaneios.TryAdd(r.Numero, onde);
            foreach (var p in d.Pagamentos.Where(BoletoComNumero)) boletos.TryAdd(ChaveBoleto(p.Numero), onde);
        }
        return (romaneios, boletos);
    }

    // Lê os PDFs que o acerto ainda não tem e soma o que vier ao que está guardado,
    // sem mexer em linha preenchida (ver AcertoAlfaService.Acrescentar). Documento de
    // número que já está no acerto é comparado campo a campo: igual, avisa e não grava;
    // diferente, o arquivo inteiro espera a escolha (CONFIRMAR) e só entra com substituir,
    // que troca o documento inteiro e tira o PDF antigo dele. Nunca troca nem ignora calado.
    // Grava tudo de uma vez só, inclusive o acerto se ele for novo; repetir não duplica.
    private async Task<Acrescimo> AcrescentarPdfs(AcertoAlfa acerto, List<Arquivo> todos, bool substituir,
        IReadOnlyDictionary<Arquivo, Leitura>? jaLidos = null)
    {
        var porArquivo = new Dictionary<Arquivo, ResultadoPdf>();
        var avisos = new List<string>();

        // O mesmo arquivo (byte a byte) já guardado no acerto, ou repetido no envio
        var tamanhos = todos.Select(a => a.Bytes.Length).Distinct().ToList();
        var vistos = acerto.Id == 0
            ? []
            : await db.AcertoAlfaArquivos.AsNoTracking()
                .Where(x => x.AcertoId == acerto.Id && tamanhos.Contains(x.Conteudo.Length))
                .Select(x => x.Conteudo)
                .ToListAsync();
        var guardadosAntes = vistos.Count;
        var novos = new List<Arquivo>();
        foreach (var a in todos)
        {
            var i = vistos.FindIndex(b => b.AsSpan().SequenceEqual(a.Bytes));
            if (i >= 0)
            {
                porArquivo[a] = new(a.Nome, "IGUAL", [],
                    [i < guardadosAntes ? "Já estava guardado neste acerto, igual." : "Arquivo repetido neste envio; entrou uma vez só."]);
                continue;
            }
            vistos.Add(a.Bytes);
            novos.Add(a);
        }
        if (novos.Count < todos.Count)
            avisos.Add($"{todos.Count - novos.Count} PDF(s) já estavam guardados, iguais, e ficaram de fora.");

        var origemMudou = await CompletarOrigem(acerto);
        var dados = Desserializar(acerto.DadosJson, acerto.Semana);
        var (emRomaneios, emBoletos) = novos.Count > 0
            ? await OndeEstaoOsDocumentos()
            : (new Dictionary<int, Onde>(), new Dictionary<string, Onde>());
        bool EmOutro(Onde? o) => o != null && o.AcertoId != acerto.Id;

        int boletosNovos = 0, romaneiosNovos = 0, substituidos = 0;
        var anexar = new List<(Arquivo Arquivo, string Tipo, Leitura Lido)>();
        var trocadosRomaneio = new HashSet<int>();
        var trocadosBoleto = new HashSet<string>();
        foreach (var a in novos)
        {
            Leitura lido;
            if (jaLidos != null && jaLidos.TryGetValue(a, out var jl)) lido = jl;
            else
            {
                try { lido = svc.Ler([a.Bytes]); }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "PDF ilegível no acerto {Numero}: {Nome}", acerto.Numero, a.Nome);
                    porArquivo[a] = new(a.Nome, "ILEGIVEL", [], ["Não consegui ler o PDF; ficou de fora."]);
                    continue;
                }
            }
            var tipo = lido.Tipos.FirstOrDefault() ?? "OUTRO";
            var av = new List<string>(lido.Avisos);
            if (lido.Boletos.Count == 0 && lido.Romaneios.Count == 0)
            {
                anexar.Add((a, tipo, lido));
                porArquivo[a] = new(a.Nome, "SO_ARQUIVO", [], av);
                continue;
            }

            // Separa o que é novo, o que já está igual e o que está com diferença
            var difs = new List<Diferenca>();
            var romNovos = new List<AcertoAlfaService.Romaneio>();
            var romTrocar = new List<AcertoAlfaService.Romaneio>();
            var bolNovos = new List<AcertoAlfaService.Boleto>();
            var bolTrocar = new List<AcertoAlfaService.Boleto>();
            int iguais = 0, fora = 0;
            foreach (var r in lido.Romaneios)
            {
                if (EmOutro(emRomaneios.GetValueOrDefault(r.Numero)))
                {
                    av.Add($"O romaneio {r.Numero} já está no acerto {emRomaneios[r.Numero].Numero}; ficou de fora aqui.");
                    fora++;
                    continue;
                }
                var atual = dados.Romaneios.FirstOrDefault(x => x.Numero == r.Numero);
                if (atual == null) { romNovos.Add(r); continue; }
                var d = CompararRomaneio(atual, r);
                if (d.Count == 0) iguais++;
                else { difs.AddRange(d); romTrocar.Add(r); }
            }
            foreach (var b in lido.Boletos)
            {
                var chave = ChaveBoleto(b.Numero);
                if (chave != "" && EmOutro(emBoletos.GetValueOrDefault(chave)))
                {
                    av.Add($"O boleto {b.Numero} já está no acerto {emBoletos[chave].Numero}; ficou de fora aqui.");
                    fora++;
                    continue;
                }
                var atual = chave == "" ? null : dados.Pagamentos.FirstOrDefault(p => ChaveBoleto(p.Numero) == chave);
                if (atual == null) { bolNovos.Add(b); continue; }
                var d = CompararBoleto(atual, b);
                if (d.Count == 0) iguais++;
                else { difs.AddRange(d); bolTrocar.Add(b); }
            }

            if (difs.Count > 0 && !substituir)
            {
                porArquivo[a] = new(a.Nome, "CONFIRMAR", difs, av);
                continue;
            }

            var entrou = romNovos.Count + bolNovos.Count + romTrocar.Count + bolTrocar.Count;
            // Acerto que estava vazio ainda não tinha semana de verdade (era a de
            // quando foi criado): passa a valer a dos documentos, e o SALDO ANT. anda junto
            if (entrou > 0 && Vazio(dados))
            {
                var semana = DateOnly.FromDateTime(Semana(lido.Boletos, lido.Romaneios));
                if (semana != acerto.Semana)
                {
                    var saldoAntigo = acerto.Semana.AddDays(-7);
                    dados = dados with
                    {
                        Pagamentos = dados.Pagamentos
                            .Select(p => p.Condicao == "SALDO ANT." && p.Data == saldoAntigo ? p with { Data = semana.AddDays(-7) } : p)
                            .ToList(),
                    };
                    acerto.Semana = semana;
                }
            }

            var (somado, bn, rn, repetidos) = AcertoAlfaService.Acrescentar(dados, bolNovos, romNovos);
            av.AddRange(repetidos);
            foreach (var r in romTrocar) { somado = SubstituirRomaneio(somado, r); trocadosRomaneio.Add(r.Numero); }
            foreach (var b in bolTrocar) { somado = SubstituirBoleto(somado, b); trocadosBoleto.Add(ChaveBoleto(b.Numero)); }
            dados = somado;
            boletosNovos += bn;
            romaneiosNovos += rn;
            substituidos += romTrocar.Count + bolTrocar.Count;

            string situacao;
            if (romTrocar.Count + bolTrocar.Count > 0) situacao = "SUBSTITUIDO";
            else if (entrou > 0) situacao = "NOVO";
            else if (iguais > 0) { situacao = "IGUAL"; av.Insert(0, "Já estava no sistema, igual; nada mudou."); }
            else situacao = "FORA";
            if (entrou > 0) anexar.Add((a, tipo, lido));
            porArquivo[a] = new(a.Nome, situacao, difs, av);
        }

        var agora = DateTime.UtcNow;
        if (anexar.Count > 0)
        {
            acerto.DadosJson = Serializar(dados);
            acerto.AvisosJson = JsonSerializer.Serialize(
                LerAvisos(acerto.AvisosJson).Concat(anexar.SelectMany(x => x.Lido.Avisos)).Distinct().ToList(), AcertoAlfaService.Json);
            // Substituído: o PDF antigo com documento trocado sai quando tudo o que ele traz
            // veio nos PDFs novos (trocado ou igual); senão a conferência da semana leria
            // os dois e ficaria com o antigo
            if (trocadosRomaneio.Count + trocadosBoleto.Count > 0)
                await TirarPdfsSubstituidos(acerto, trocadosRomaneio, trocadosBoleto,
                    anexar.SelectMany(x => x.Lido.Romaneios.Select(r => r.Numero)).ToHashSet(),
                    anexar.SelectMany(x => x.Lido.Boletos.Select(b => ChaveBoleto(b.Numero))).ToHashSet(), avisos);
            foreach (var (a, tipo, _) in anexar)
                acerto.Arquivos.Add(new AcertoAlfaArquivo { Nome = a.Nome, Tipo = tipo, Conteudo = a.Bytes, CriadoEm = agora });
            acerto.AtualizadoEm = agora;
        }
        if (anexar.Count > 0 || origemMudou || acerto.Id == 0)
            await db.SaveChangesAsync();

        return new Acrescimo(anexar.Count, boletosNovos, romaneiosNovos, substituidos, avisos, porArquivo);
    }

    private async Task TirarPdfsSubstituidos(AcertoAlfa acerto, HashSet<int> romaneios, HashSet<string> boletos,
        HashSet<int> romaneiosNovos, HashSet<string> boletosNovos, List<string> avisos)
    {
        if (acerto.Id == 0) return;
        var guardados = await db.AcertoAlfaArquivos
            .Where(x => x.AcertoId == acerto.Id && (x.Tipo == "BOLETO" || x.Tipo == "ROMANEIO"))
            .ToListAsync();
        foreach (var g in guardados)
        {
            Leitura l;
            try { l = svc.Ler([g.Conteudo]); } catch (Exception) { continue; }
            var rs = l.Romaneios.Select(r => r.Numero).ToList();
            var bs = l.Boletos.Select(b => ChaveBoleto(b.Numero)).ToList();
            // Só o PDF que traz algum documento trocado
            if (!rs.Any(romaneios.Contains) && !bs.Any(boletos.Contains)) continue;
            if (rs.All(romaneiosNovos.Contains) && bs.All(boletosNovos.Contains))
            {
                db.AcertoAlfaArquivos.Remove(g);
                avisos.Add($"O PDF antigo {g.Nome} foi trocado pelo novo.");
            }
            else
                avisos.Add($"O PDF antigo {g.Nome} ficou guardado porque traz documentos que não vieram no novo; " +
                    "ele ainda tem os valores antigos do que foi substituído.");
        }
    }

    // Tira só o arquivo: o que foi lido dele continua nos dados, que se editam à parte
    [HttpDelete("acertos/{id:int}/arquivos/{arquivoId:int}")]
    public async Task<IActionResult> ExcluirArquivo(int id, int arquivoId)
    {
        var n = await db.AcertoAlfaArquivos.Where(x => x.Id == arquivoId && x.AcertoId == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Arquivo não encontrado." });
        await db.AcertosAlfa.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AtualizadoEm, DateTime.UtcNow));
        return NoContent();
    }

    // Os arquivos vão junto pelo ON DELETE CASCADE da tabela
    [HttpDelete("acertos/{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var n = await db.AcertosAlfa.Where(a => a.Id == id).ExecuteDeleteAsync();
        if (n == 0) return NotFound(new { erro = "Acerto não encontrado." });
        return NoContent();
    }

    // A mesma conferência do POST, com os PDFs guardados no acerto (a planilha
    // importada não é PDF e fica de fora)
    [HttpGet("acertos/{id:int}/conferencia")]
    public async Task<IActionResult> ConferenciaGuardada(int id, [FromServices] ConferenciaSemanaService conf)
    {
        try
        {
            if (!await db.AcertosAlfa.AnyAsync(a => a.Id == id))
                return NotFound(new { erro = "Acerto não encontrado." });
            var pdfs = await db.AcertoAlfaArquivos
                .Where(x => x.AcertoId == id && x.Tipo != TipoPlanilha)
                .OrderBy(x => x.Id)
                .Select(x => x.Conteudo)
                .ToListAsync();
            return Ok(await ConferirPdfs(pdfs, conf));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro na conferência do acerto {Id}", id);
            return StatusCode(500, new { erro = $"Erro ao conferir: {ex.Message}" });
        }
    }

    // ───────────────────────── Auxiliares ─────────────────────────

    private record Arquivo(string Nome, byte[] Bytes);

    private static bool EhPdf(string nome) => nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<Arquivo>> LerArquivos(IFormFileCollection arquivos)
    {
        var lista = new List<Arquivo>();
        foreach (var arq in arquivos)
        {
            using var ms = new MemoryStream();
            await arq.CopyToAsync(ms);
            lista.Add(new Arquivo(Path.GetFileName(arq.FileName), ms.ToArray()));
        }
        return lista;
    }

    // Maior entre o contador e o maior número guardado, mais um: acerto importado
    // de planilha também conta
    private async Task<int> ProximoNumeroLivre()
    {
        var contador = await db.AcertoAlfaContadores.OrderBy(c => c.Id).FirstOrDefaultAsync();
        var maior = await db.AcertosAlfa.MaxAsync(a => (int?)a.Numero) ?? 0;
        return Math.Max(contador?.Ultimo ?? 184, maior) + 1;
    }

    // Só sobe: importar um acerto velho não faz o próximo número voltar
    private async Task GuardarContador(int numero)
    {
        var contador = await db.AcertoAlfaContadores.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (contador == null) db.AcertoAlfaContadores.Add(new() { Ultimo = numero });
        else if (numero > contador.Ultimo) contador.Ultimo = numero;
    }

    private async Task<ConferenciaSemanaService.Resultado> ConferirPdfs(List<byte[]> pdfs, ConferenciaSemanaService conf)
    {
        var (fazenda, bols, avisos) = conf.LerPdfs(pdfs);
        var datas = fazenda.Select(g => g.Data.Date).Concat(bols.Select(b => b.DataDoc.Date)).ToList();
        if (datas.Count == 0)
            return conf.Conferir(fazenda, bols, [], avisos);

        // A semana inteira de cada documento, de segunda a domingo
        var inicio = Segunda(datas.Min());
        var fim = Segunda(datas.Max()).AddDays(6);

        var nomes = await db.PlanilhasCarga
            .Where(p => p.Nome.EndsWith(".xlsx"))
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync();
        var ids = nomes
            .Where(p => ConferenciaSemanaService.LerNomePlanilha(p.Nome) is { } x && x.Data >= inicio && x.Data <= fim)
            .Select(p => p.Id)
            .ToList();

        var planilhas = await db.PlanilhasCarga
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ConferenciaSemanaService.RomaneioClienteArquivo(p.Id, p.Nome, p.Conteudo))
            .ToListAsync();

        return conf.Conferir(fazenda, bols, planilhas, avisos);
    }

    // O Excel sai dos dados valendo: SALDO ANT. automático já com o saldo do acerto anterior
    private AcertoResult GerarDoAcerto(AcertoAlfa acerto, Dados valendo) =>
        svc.GerarDeDados(valendo, acerto.Numero, acerto.Semana.ToDateTime(TimeOnly.MinValue));

    // ───────────────────── Saldo anterior em cadeia ─────────────────────

    /// <summary>Os acertos até o número dado (todos, sem número), com o SALDO ANT. resolvido.</summary>
    private async Task<List<(int Id, Encadeado E)>> Cadeia(int? ateNumero = null)
    {
        var q = db.AcertosAlfa.AsNoTracking();
        if (ateNumero != null) q = q.Where(a => a.Numero <= ateNumero);
        var linhas = await q.OrderBy(a => a.Numero).Select(a => new { a.Id, a.Numero, a.Semana, a.DadosJson }).ToListAsync();
        var ids = linhas.ToDictionary(a => a.Numero, a => a.Id);
        return Encadear(linhas.Select(a => (a.Numero, a.Semana, Desserializar(a.DadosJson, a.Semana))))
            .Select(e => (ids[e.Numero], e)).ToList();
    }

    // O saldo com que o último da cadeia fechou: é a origem do acerto seguinte
    private static SaldoOrigem? OrigemDepois(List<(int Id, Encadeado E)> cadeia) =>
        cadeia.Count == 0 ? null : new SaldoOrigem(cadeia[^1].E.Numero, cadeia[^1].E.Semana, cadeia[^1].E.Totais.Saldo);

    private async Task<Dados> Valendo(AcertoAlfa acerto)
    {
        var dados = Desserializar(acerto.DadosJson, acerto.Semana);
        return ComSaldoAnterior(dados, OrigemDepois(await Cadeia(acerto.Numero - 1)));
    }

    // Planilha importada com o SALDO ANT. igual ao saldo do acerto anterior: fica
    // automático (corrigir o anterior passa a corrigir este). Diferente, fica o da
    // planilha, como digitado à mão, e o aviso mostra os dois valores.
    private async Task NormalizarSaldoAnterior(List<ResultadoImportacao> resultados)
    {
        var importados = resultados.Where(r => r.AcertoId != null).ToDictionary(r => r.AcertoId!.Value);
        if (importados.Count == 0) return;

        var mudou = false;
        foreach (var (id, e) in await Cadeia())
        {
            if (!importados.TryGetValue(id, out var res)) continue;
            var i = IndiceSaldoAnterior(e.Guardado);
            if (i < 0 || e.Guardado.Pagamentos[i].Valor is not { } valor) continue;
            if (e.Origem == null)
            {
                res.Avisos.Add($"Saldo anterior: não há acerto anterior no sistema; ficou o da planilha ({valor.ToString("N2", PtBr)}).");
                continue;
            }
            if (Math.Abs(valor - e.Origem.Saldo) < 0.005)
            {
                var acerto = await db.AcertosAlfa.FirstAsync(a => a.Id == id);
                var pagamentos = e.Guardado.Pagamentos.ToList();
                pagamentos[i] = pagamentos[i] with { Valor = null };
                acerto.DadosJson = Serializar(e.Guardado with { Pagamentos = pagamentos });
                mudou = true;
                res.Avisos.Add($"Saldo anterior igual ao saldo final do acerto {e.Origem.Numero}: ficou automático.");
            }
            else
                res.Avisos.Add($"Saldo anterior da planilha ({valor.ToString("N2", PtBr)}) diferente do saldo final do acerto {e.Origem.Numero} ({e.Origem.Saldo.ToString("N2", PtBr)}): ficou o da planilha, como digitado à mão.");
        }
        if (mudou) await db.SaveChangesAsync();
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Acrescenta a cada romaneio da fazenda lido o destino nos volumes embalados
    private async Task<List<ArquivoIdentificado>> ComEmbalar(List<ArquivoIdentificado> itens)
    {
        static List<int> Numeros(string? s) =>
            (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => int.TryParse(t, out var n) ? n : 0).Where(n => n > 0).ToList();

        var todos = itens.Where(i => i.Tipo == "ROMANEIO_FAZENDA").SelectMany(i => Numeros(i.Numero)).Distinct().ToList();
        if (todos.Count == 0) return itens;
        var infos = (await embalar.ListarAsync(numeros: todos)).ToDictionary(x => x.Numero);
        return itens.Select(i =>
        {
            if (i.Tipo != "ROMANEIO_FAZENDA") return i;
            var lista = Numeros(i.Numero).Where(infos.ContainsKey).Select(n => infos[n]).ToList();
            return lista.Count == 0 ? i : i with { Embalar = lista };
        }).ToList();
    }

    // Detalhe do acerto para a tela, com os totais do pé da planilha e o saldo
    // com que o acerto anterior fechou (é o SALDO ANT. deste). Os avisos da
    // planilha saem junto para aparecerem antes de alguém baixar.
    private async Task<object> Detalhar(AcertoAlfa acerto)
    {
        var dados = Desserializar(acerto.DadosJson, acerto.Semana);
        var cadeiaAntes = await Cadeia(acerto.Numero - 1);
        var origem = OrigemDepois(cadeiaAntes);
        var valendo = ComSaldoAnterior(dados, origem);
        var arquivos = await db.AcertoAlfaArquivos.AsNoTracking()
            .Where(x => x.AcertoId == acerto.Id)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Nome, x.Tipo, Tamanho = x.Conteudo.Length, x.CriadoEm })
            .ToListAsync();

        List<string> avisosPlanilha;
        try { avisosPlanilha = GerarDoAcerto(acerto, valendo).Avisos; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao montar a planilha do acerto {Id}", acerto.Id);
            avisosPlanilha = ["Não consegui montar a planilha com estes dados."];
        }

        return new
        {
            acerto.Id, acerto.Numero, acerto.Semana,
            dados.Romaneios, dados.Pagamentos, dados.Notas,
            Totais = Totalizar(valendo),
            // SALDO ANT.: automático (linha sem valor guardado) = saldo final do
            // acerto de número anterior; à mão, vale o digitado. Saldo é sempre o
            // do anterior, para a tela mostrar e poder voltar ao automático.
            SaldoAnterior = new
            {
                TemLinha = IndiceSaldoAnterior(dados) >= 0,
                Automatico = SaldoAnteriorAutomatico(dados),
                DoAcertoId = cadeiaAntes.Count > 0 ? cadeiaAntes[^1].Id : (int?)null,
                Numero = origem?.Numero,
                Semana = origem?.Semana,
                Saldo = origem?.Saldo,
            },
            // Romaneios da fazenda sem funrural deste acerto: volumes para embalar
            RomaneiosEmbalar = await embalar.ListarAsync(acertoId: acerto.Id),
            Avisos = LerAvisos(acerto.AvisosJson),
            AvisosPlanilha = avisosPlanilha,
            Arquivos = arquivos,
            acerto.CriadoEm, acerto.AtualizadoEm,
        };
    }

    private static List<string> LerAvisos(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json, AcertoAlfaService.Json) ?? []; }
        catch (JsonException) { return []; }
    }

    // Data da tela: "aaaa-mm-dd" (input date) ou "dd/mm/aaaa". Ano fora de
    // 2000..2100 é erro de digitação, não acerto de verdade.
    private static DateOnly? LerData(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateOnly.TryParseExact(s.Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return null;
        return d.Year is >= 2000 and <= 2100 ? d : null;
    }
}
