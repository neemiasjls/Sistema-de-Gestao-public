using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// E-mail semanal da Cedro Frutas e do Dunas, e o do frete da Girassol: a prévia de cada um para uma data
/// de carga (o que vai no .zip e o que falta), os destinatários padrão, o envio
/// e o histórico. Com Email:DestinoTeste configurado, tudo vai só para esse
/// endereço — a prévia diz isso para a tela mostrar.
/// </summary>
[Authorize]
[ApiController]
[Route("api/emails-cliente")]
public class EmailsClienteController(
    EmailClienteService svc, EmailService email, AppDbContext db, ILogger<EmailsClienteController> logger) : ControllerBase
{
    private static bool LerData(string? s, out DateOnly data) =>
        DateOnly.TryParseExact((s ?? "").Trim(), ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

    // A prévia dos dois e-mails. Sem data, vale a carga mais recente com
    // romaneio da Cedro Frutas ou do Dunas.
    [HttpGet("previa")]
    public async Task<IActionResult> Previa([FromQuery] string? data)
    {
        try
        {
            var datas = await svc.DatasComRomaneio();
            DateOnly dia;
            if (!string.IsNullOrWhiteSpace(data))
            {
                if (!LerData(data, out dia)) return BadRequest(new { erro = "Data inválida." });
            }
            else if (datas.Count > 0) dia = datas[0];
            else dia = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));

            var emails = new List<object>();
            foreach (var c in EmailClienteService.Clientes)
            {
                var m = await svc.Montar(c, dia);
                var envios = await db.EmailsClienteEnvios.AsNoTracking()
                    .Where(e => e.Cliente == c.Chave && e.DataCarga == dia)
                    .OrderByDescending(e => e.EnviadoEm)
                    .ToListAsync();
                DateTime? troca = (await svc.TrocasDeBoleto(c)).TryGetValue(dia, out var t) ? t : null;
                emails.Add(new
                {
                    Cliente = c.Chave,
                    c.Rotulo,
                    c.NomeZip,
                    Destinatarios = await svc.Destinatarios(c),
                    DestinatariosPadrao = c.DestinatariosPadrao,
                    Assunto = EmailClienteService.Assunto(m),
                    Corpo = svc.Corpo(m),
                    NumerosNF = m.NumerosNF,
                    m.Placa,
                    m.Motorista,
                    c.EhFrete,
                    Frete = m.Frete == null ? null : new
                    {
                        m.RelatorioId,
                        m.RelatorioNome,
                        m.Frete.Data,
                        m.Frete.CaixasNormais,
                        m.Frete.CaixasProdutoE,
                        m.Frete.TotalFrete,
                        m.Frete.NFs,
                    },
                    Arquivos = m.Pecas.Select(p => new { p.Chave, p.Nome, p.Tipo, p.Descricao, Tamanho = p.Conteudo.Length, Incluir = p.Padrao }),
                    m.Faltando,
                    m.Avisos,
                    // Perfil sem boleto (queda do volume passou do frete, BOL-17): envia sem anexo
                    SemBoleto = m.Queda?.SemBoleto == true,
                    Envios = envios.Select(Resumir),
                    // Boleto substituído depois do último envio: a tela volta o e-mail para enviar
                    BoletoTrocadoEm = troca != null && envios.Any(e => e.Erro == null && e.EnviadoEm < troca)
                        && !envios.Any(e => e.Erro == null && e.EnviadoEm >= troca) ? troca : null,
                });
            }

            // Falta enviar, por cliente (Admin, 15/10/2026: "uma opção por cliente onde eu
            // expando e vejo todos que faltam"). Dia com o documento do cliente (romaneio, ou o
            // .txt do frete na Girassol) e sem envio, a partir do primeiro envio dele pelo site
            // (antes disso era tudo à mão); sem envio nenhum ainda, os últimos 21 dias.
            var nomesDocs = await db.PlanilhasCarga.AsNoTracking().Select(p => p.Nome).ToListAsync();
            var idents = nomesDocs.Select(RomaneioClienteLeitor.LerNome).Where(i => i.Data != null).ToList();
            var todosEnvios = await db.EmailsClienteEnvios.AsNoTracking()
                .Select(e => new { e.Cliente, e.DataCarga, e.EnviadoEm }).ToListAsync();
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
            var faltaEnviar = new List<object>();
            foreach (var c in EmailClienteService.Clientes)
            {
                var trocas = await svc.TrocasDeBoleto(c);
                var envios = todosEnvios.Where(e => e.Cliente == c.Chave).Select(e => e.DataCarga).ToHashSet();
                var desde = envios.Count > 0 ? envios.Min() : hoje.AddDays(-21);
                // Boleto trocado depois do envio: o dia volta para enviar
                envios = todosEnvios.Where(e => e.Cliente == c.Chave
                        && !(trocas.TryGetValue(e.DataCarga, out var tr) && e.EnviadoEm < tr))
                    .Select(e => e.DataCarga).ToHashSet();
                var dias = idents
                    .Where(i => c.EhFrete ? i.Tipo == "frete" : i.Tipo == "romaneio" && i.Apelido == c.Chave)
                    .Select(i => DateOnly.FromDateTime(i.Data!.Value))
                    .Where(d => d >= desde && !envios.Contains(d))
                    .Distinct().OrderByDescending(d => d).Take(15).ToList();
                var lista = new List<object>();
                foreach (var d in dias)
                {
                    var m = await svc.Montar(c, d);
                    lista.Add(new { Data = d, Pronto = m.Faltando.Count == 0, m.Faltando });
                }
                faltaEnviar.Add(new { Cliente = c.Chave, c.Rotulo, Dias = lista });
            }

            return Ok(new
            {
                Data = dia,
                Datas = datas,
                FaltaEnviar = faltaEnviar,
                email.Configurado,
                Remetente = email.RemetenteEmail,
                email.RemetenteNome,
                email.ModoTeste,
                email.DestinoTeste,
                Emails = emails,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao montar a prévia dos e-mails aos clientes");
            return StatusCode(500, new { erro = $"Erro ao montar a prévia: {ex.Message}" });
        }
    }

    // Uma peça da prévia, para abrir e conferir antes de mandar
    [HttpGet("arquivo")]
    public async Task<IActionResult> Arquivo([FromQuery] string? cliente, [FromQuery] string? data, [FromQuery] string? chave)
    {
        var c = EmailClienteService.AcharCliente(cliente);
        if (c == null) return BadRequest(new { erro = "Cliente desconhecido." });
        if (!LerData(data, out var dia)) return BadRequest(new { erro = "Data inválida." });
        var m = await svc.Montar(c, dia);
        var p = m.Pecas.FirstOrDefault(x => x.Chave == chave);
        if (p == null) return NotFound(new { erro = "Arquivo não encontrado. Atualize a prévia." });
        return File(p.Conteudo, TipoDoArquivo(p.Nome), p.Nome);
    }

    public record ZipPedido(string? Cliente, string? Data, List<string>? Chaves);

    // O .zip exatamente como iria no e-mail
    [HttpPost("zip")]
    public async Task<IActionResult> Zip([FromBody] ZipPedido req)
    {
        var c = EmailClienteService.AcharCliente(req.Cliente);
        if (c == null) return BadRequest(new { erro = "Cliente desconhecido." });
        if (!LerData(req.Data, out var dia)) return BadRequest(new { erro = "Data inválida." });
        var m = await svc.Montar(c, dia);
        var chaves = (req.Chaves ?? []).ToHashSet();
        var pecas = m.Pecas.Where(p => chaves.Contains(p.Chave)).ToList();
        if (pecas.Count == 0) return BadRequest(new { erro = "Escolha pelo menos um arquivo." });
        var (bytes, _) = EmailClienteService.Zipar(pecas);
        return File(bytes, "application/zip", string.IsNullOrEmpty(c.NomeZip) ? $"{c.Chave}.zip" : c.NomeZip);
    }

    public record DestinatariosPedido(List<string>? Destinatarios);

    [HttpPut("config/{cliente}")]
    public async Task<IActionResult> SalvarDestinatarios(string cliente, [FromBody] DestinatariosPedido req)
    {
        var c = EmailClienteService.AcharCliente(cliente);
        if (c == null) return BadRequest(new { erro = "Cliente desconhecido." });
        var lista = (req.Destinatarios ?? []).SelectMany(EmailClienteService.SepararEnderecos)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var invalido = lista.FirstOrDefault(e => !EmailClienteService.EnderecoValido(e));
        if (invalido != null) return BadRequest(new { erro = $"Endereço inválido: {invalido}" });
        if (lista.Count == 0) return BadRequest(new { erro = "Informe pelo menos um destinatário." });
        await svc.SalvarDestinatarios(c, lista);
        return Ok(new { destinatarios = lista });
    }

    public record EnvioPedido(string? Cliente, string? Data, List<string>? Destinatarios, string? Assunto, string? Corpo,
        List<string>? Chaves, bool Reenviar);

    [HttpPost("enviar")]
    public async Task<IActionResult> Enviar([FromBody] EnvioPedido req)
    {
        var c = EmailClienteService.AcharCliente(req.Cliente);
        if (c == null) return BadRequest(new { erro = "Cliente desconhecido." });
        if (!LerData(req.Data, out var dia)) return BadRequest(new { erro = "Data inválida." });
        var destinatarios = (req.Destinatarios ?? []).SelectMany(EmailClienteService.SepararEnderecos)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (destinatarios.Count == 0) return BadRequest(new { erro = "Informe pelo menos um destinatário." });
        var invalido = destinatarios.FirstOrDefault(e => !EmailClienteService.EnderecoValido(e));
        if (invalido != null) return BadRequest(new { erro = $"Endereço inválido: {invalido}" });
        if (string.IsNullOrWhiteSpace(req.Assunto)) return BadRequest(new { erro = "Informe o assunto." });
        if (string.IsNullOrWhiteSpace(req.Corpo)) return BadRequest(new { erro = "Escreva o texto do e-mail." });
        if (req.Chaves == null || req.Chaves.Count == 0) return BadRequest(new { erro = "Escolha pelo menos um arquivo." });

        try
        {
            var r = await svc.Enviar(new EmailClienteService.Pedido(c, dia, destinatarios, req.Assunto, req.Corpo, req.Chaves, req.Reenviar));
            if (r.JaEnviado)
                return Conflict(new { erro = r.Erro, jaEnviado = true, envio = r.Envio == null ? null : Resumir(r.Envio) });
            if (!r.Enviado && r.Envio == null)
                return BadRequest(new { erro = r.Erro });
            return Ok(new
            {
                r.Enviado, r.Erro, r.ModoTeste, r.EnviadoPara, r.Anexos, r.Aviso,
                Envio = r.Envio == null ? null : Resumir(r.Envio),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao enviar o e-mail {Cliente} de {Data}", c.Chave, dia);
            return StatusCode(500, new { erro = $"Erro ao enviar: {ex.Message}" });
        }
    }

    // O relatório de frete da Girassol (.txt) que não foi gerado pelo sistema: fica
    // guardado como os gerados na tela Planilhas NF-e ("dd-MM-yyyy PLACA.txt", sem
    // carga), e o e-mail da Girassol passa a achá-lo pela data. Mesmo nome sem carga
    // troca o conteúdo, então mandar de novo não duplica.
    [HttpPost("relatorio-frete")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> SubirRelatorioFrete(IFormFile? arquivo)
    {
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { erro = "Escolha o relatório de frete (.txt)." });
        var nomeOriginal = Path.GetFileName(arquivo.FileName).Trim();
        if (!nomeOriginal.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { erro = "O relatório de frete é o arquivo .txt (\"29-09-2026 XAD-5H46.txt\")." });

        using var ms = new MemoryStream();
        await arquivo.CopyToAsync(ms);
        var bytes = ms.ToArray();
        var texto = RelatorioFreteLeitor.Texto(bytes);
        if (!RelatorioFreteLeitor.EhRelatorio(texto))
            return BadRequest(new { erro = $"{nomeOriginal} não é um relatório de frete (não achei o RELATORIO DE FRETE e o TOTAL FRETE)." });
        var rel = RelatorioFreteLeitor.Ler(texto, nomeOriginal);
        if (rel.Data is not DateOnly data)
            return BadRequest(new { erro = $"{nomeOriginal}: não achei a data da carga no relatório nem no nome." });

        // O nome é o que dá data e placa para o resto do sistema: sai no padrão
        var nome = rel.Placa == null ? $"{data:dd-MM-yyyy}.txt" : $"{data:dd-MM-yyyy} {rel.Placa}.txt";
        var atual = await db.PlanilhasCarga
            .Where(p => p.CalculoId == null && p.Nome == nome)
            .OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync();
        var novo = atual == null;
        if (atual == null)
        {
            atual = new Core.Entities.PlanilhaCarga { CalculoId = null, Nome = nome };
            db.PlanilhasCarga.Add(atual);
        }
        atual.Conteudo = bytes;
        atual.CriadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var avisos = new List<string>(rel.Avisos);
        if (!string.Equals(nome, nomeOriginal, StringComparison.OrdinalIgnoreCase))
            avisos.Add($"Guardado como {nome}.");
        return Ok(new { Nome = nome, Data = data, Novo = novo, Avisos = avisos });
    }

    // O histórico de todos os envios, do mais recente para trás
    [HttpGet("envios")]
    public async Task<IActionResult> Envios([FromQuery] int quantos = 60)
    {
        var lista = await db.EmailsClienteEnvios.AsNoTracking()
            .OrderByDescending(e => e.EnviadoEm)
            .Take(Math.Clamp(quantos, 1, 500))
            .ToListAsync();
        return Ok(lista.Select(Resumir));
    }

    private static object Resumir(Core.Entities.EmailClienteEnvio e) => new
    {
        e.Id,
        e.Cliente,
        Rotulo = EmailClienteService.AcharCliente(e.Cliente)?.Rotulo ?? e.Cliente,
        e.DataCarga,
        e.Assunto,
        Destinatarios = EmailClienteService.SepararEnderecos(e.Destinatarios),
        EnviadoPara = EmailClienteService.SepararEnderecos(e.EnviadoPara),
        Anexos = e.Anexos.Split('\n', StringSplitOptions.RemoveEmptyEntries),
        e.NomeZip,
        e.TamanhoZip,
        e.ModoTeste,
        e.Erro,
        e.EnviadoEm,
    };

    private static string TipoDoArquivo(string nome) => Path.GetExtension(nome).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".xml" => "application/xml",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };
}
