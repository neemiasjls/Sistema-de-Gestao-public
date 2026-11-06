using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Todos os romaneios de cliente guardados no sistema — os gerados soltos na
/// tela Planilhas NF-e e os que ficaram numa carga —, para listar por
/// cliente, abrir uma visão lida pela aplicação e baixar de novo.
/// </summary>
[Authorize]
[ApiController]
[Route("api/romaneios-cliente")]
public class RomaneiosClienteController(AppDbContext db, ILogger<RomaneiosClienteController> logger) : ControllerBase
{
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static bool EhTexto(string nome) => nome.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    // Sem o conteúdo: a lista só precisa do nome, e o arquivo vem no download.
    // O mesmo nome em cargas diferentes é o mesmo romaneio gerado de novo:
    // mostra o mais recente e conta as versões.
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var registros = await db.PlanilhasCarga
            .Select(p => new
            {
                p.Id, p.Nome, p.CalculoId, p.CriadoEm,
                tamanho = p.Conteudo.Length,
                placaCarga = p.Calculo != null ? p.Calculo.Placa : null,
            })
            .ToListAsync();

        var lista = registros
            .GroupBy(p => p.Nome.Trim().ToUpperInvariant())
            .Select(g =>
            {
                var p = g.OrderByDescending(x => x.CriadoEm).ThenByDescending(x => x.Id).First();
                var id = RomaneioClienteLeitor.LerNome(p.Nome);
                return new
                {
                    p.Id,
                    p.Nome,
                    apelido = id.Apelido,
                    data = id.Data,
                    numero = id.Numero,
                    tipo = id.Tipo,
                    p.CalculoId,
                    // A placa da carga vale mais que a do nome do arquivo
                    placa = p.placaCarga ?? id.Placa,
                    p.CriadoEm,
                    p.tamanho,
                    versoes = g.Count(),
                };
            })
            .OrderByDescending(x => x.data ?? x.CriadoEm.Date)
            .ThenByDescending(x => x.numero ?? 0)
            .ThenByDescending(x => x.CriadoEm)
            .ToList();

        return Ok(lista);
    }

    [HttpGet("{id:int}/arquivo")]
    public async Task<IActionResult> Baixar(int id)
    {
        var p = await db.PlanilhasCarga.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { erro = "Romaneio não encontrado." });
        return File(p.Conteudo, EhTexto(p.Nome) ? "text/plain; charset=utf-8" : TipoXlsx, p.Nome);
    }

    // O romaneio lido pela aplicação, para a tela montar sem o Excel
    [HttpGet("{id:int}/resumo")]
    public async Task<IActionResult> Resumo(int id)
    {
        var p = await db.PlanilhasCarga.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Nome, x.Conteudo, x.CalculoId, x.CriadoEm,
                               placaCarga = x.Calculo != null ? x.Calculo.Placa : null })
            .FirstOrDefaultAsync();
        if (p == null) return NotFound(new { erro = "Romaneio não encontrado." });

        var ident = RomaneioClienteLeitor.LerNome(p.Nome);
        string? texto = null;
        RomaneioClienteLeitor.Resumo? romaneio = null;

        if (EhTexto(p.Nome))
            texto = System.Text.Encoding.UTF8.GetString(p.Conteudo).TrimStart('﻿');
        else
        {
            try { romaneio = RomaneioClienteLeitor.Ler(p.Conteudo); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Não consegui ler o romaneio {Nome}", p.Nome);
                return UnprocessableEntity(new { erro = $"Não consegui ler a planilha {p.Nome}. Baixe e abra no Excel." });
            }
        }

        return Ok(new
        {
            p.Id, p.Nome,
            apelido = ident.Apelido, numero = ident.Numero, data = ident.Data, tipo = ident.Tipo,
            p.CalculoId, placaCarga = p.placaCarga ?? ident.Placa, p.CriadoEm,
            texto, romaneio,
        });
    }

    // Traz para o sistema romaneios feitos fora dele (os do Drive, acertados à
    // mão no Excel). Cada arquivo é lido antes de guardar: o que não abre como
    // romaneio fica de fora e volta no aviso, em vez de entrar quebrado na lista.
    // Mesmo nome sem carga troca o conteúdo; o que está numa carga não é tocado.
    [HttpPost("importar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Importar(IFormFileCollection arquivos)
    {
        if (arquivos == null || arquivos.Count == 0)
            return BadRequest(new { erro = "Selecione os romaneios (.xlsx)." });

        int novos = 0, atualizados = 0;
        var recusados = new List<string>();
        var nomes = arquivos.Select(a => a.FileName.Trim()).ToList();
        var soltos = await db.PlanilhasCarga
            .Where(p => p.CalculoId == null && nomes.Contains(p.Nome))
            .ToListAsync();

        foreach (var arq in arquivos)
        {
            var nome = arq.FileName.Trim();
            if (nome.StartsWith("~$")) continue;   // arquivo temporário do Excel aberto
            if (!nome.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            { recusados.Add($"{nome}: não é planilha .xlsx."); continue; }

            using var ms = new MemoryStream();
            await arq.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var ident = RomaneioClienteLeitor.LerNome(nome);
            if (string.IsNullOrWhiteSpace(ident.Apelido) || ident.Data == null)
            { recusados.Add($"{nome}: não reconheci cliente e data no nome do arquivo."); continue; }
            try { RomaneioClienteLeitor.Ler(bytes); }
            catch { recusados.Add($"{nome}: não abriu como romaneio."); continue; }

            var atual = soltos.FirstOrDefault(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));
            if (atual != null) { atual.Conteudo = bytes; atual.CriadoEm = DateTime.UtcNow; atualizados++; }
            else
            {
                var novo = new Core.Entities.PlanilhaCarga { CalculoId = null, Nome = nome, Conteudo = bytes };
                db.PlanilhasCarga.Add(novo);
                soltos.Add(novo);
                novos++;
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { novos, atualizados, recusados });
    }

    // Editar o romaneio guardado (Admin, 02/11/2026: o 183 do Aurora e o 174 da BRZ
    // de 02/11 eram 181 e 172). O número só existe no nome do arquivo; troca em todas as
    // versões de mesmo nome. Se era o último número do cliente, o contador acompanha,
    // para o próximo sair em sequência. Planilha nova (acertada no Excel) troca o conteúdo.
    [HttpPut("{id:int}")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Editar(int id, [FromForm] int? numero, IFormFile? arquivo)
    {
        var p = await db.PlanilhasCarga.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) return NotFound(new { erro = "Romaneio não encontrado." });

        var ident = RomaneioClienteLeitor.LerNome(p.Nome);
        var nomeNovo = p.Nome;
        if (numero != null)
        {
            if (numero <= 0) return BadRequest(new { erro = "Número do romaneio inválido." });
            if (EhTexto(p.Nome) || string.IsNullOrWhiteSpace(ident.Apelido) || ident.Data == null)
                return BadRequest(new { erro = "Este arquivo não tem número de romaneio no nome." });
            nomeNovo = $"{numero}--{ident.Apelido}-{ident.Data:dd-MM-yyyy}.xlsx";
            if (!string.Equals(nomeNovo, p.Nome, StringComparison.OrdinalIgnoreCase)
                && await db.PlanilhasCarga.AnyAsync(x => x.Nome.ToUpper() == nomeNovo.ToUpper()))
                return Conflict(new { erro = $"Já existe o romaneio {nomeNovo}." });
        }

        byte[]? conteudo = null;
        if (arquivo != null && arquivo.Length > 0)
        {
            if (EhTexto(p.Nome) != arquivo.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { erro = EhTexto(p.Nome) ? "Envie o arquivo .txt." : "Envie a planilha .xlsx." });
            using var ms = new MemoryStream();
            await arquivo.CopyToAsync(ms);
            conteudo = ms.ToArray();
            if (!EhTexto(p.Nome))
            {
                try { RomaneioClienteLeitor.Ler(conteudo); }
                catch { return BadRequest(new { erro = $"{arquivo.FileName} não abriu como romaneio." }); }
            }
        }

        var nomeAntigo = p.Nome;
        var mesmoNome = await db.PlanilhasCarga
            .Where(x => x.Nome.ToUpper() == nomeAntigo.ToUpper())
            .ToListAsync();
        foreach (var x in mesmoNome) x.Nome = nomeNovo;
        if (conteudo != null) { p.Conteudo = conteudo; p.CriadoEm = DateTime.UtcNow; }

        if (numero != null && ident.Numero != null && ident.Numero != numero)
        {
            var contador = await db.RomaneioContadores.FirstOrDefaultAsync(c => c.Apelido == ident.Apelido);
            if (contador != null && contador.Ultimo == ident.Numero) contador.Ultimo = numero.Value;
        }

        await db.SaveChangesAsync();
        return Ok(new { p.Id, nome = nomeNovo });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var p = await db.PlanilhasCarga.FindAsync(id);
        if (p == null) return NotFound(new { erro = "Romaneio não encontrado." });
        // Sem outra versão de mesmo nome, os seguintes do cliente descem um número (NFE-12)
        var renomeados = await RomaneiosNumeracao.ExcluirERenumerarAsync(db, [p]);
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Romaneio excluído.", renomeados });
    }
}
