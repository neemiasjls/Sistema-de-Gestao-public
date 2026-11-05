using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Volumes embalados — controle do barracão, no lugar das planilhas
/// "VOLUMES EMBALADOS". Primeiro se anota o que embalou (a mensagem que hoje vai
/// no WhatsApp); depois entram as entradas da fazenda e as saídas por cliente.
/// A conferência (ConferenciaVolumes) tem de fechar em zero.
/// </summary>
[Authorize]
[ApiController]
[Route("api/volumes-embalados")]
public class VolumesEmbaladosController(AppDbContext db, RomaneiosEmbalarService embalar, EstoqueVendasService vendas) : ControllerBase
{
    // As produtosB usadas para embalar saem do estoque de produtosB (VOLUME-04, EST-02).
    // Falhar aqui não desfaz o controle: avisa e dá para acertar na tela de ProdutosB.
    private async Task<List<string>> SincronizarProdutosBAsync(int controleId)
    {
        try { return await vendas.SincronizarEmbaladosAsync(controleId); }
        catch (Exception)
        {
            db.ChangeTracker.Clear();
            return ["Não consegui lançar a saída das produtosB usadas. Em ProdutosB, use \"Acertar uso nos embalados\"."];
        }
    }

    private static readonly string[] GruposUnicos =
        [ControleVolumesLinha.Embalou, ControleVolumesLinha.Sobra, ControleVolumesLinha.Quebra, ControleVolumesLinha.SobraAnterior];
    private static readonly string[] GruposLista = [ControleVolumesLinha.Entrada, ControleVolumesLinha.Saida];

    // ── Lista ─────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Lista()
    {
        // Romaneio da fazenda sem funrural que chegou no acerto entra antes de mostrar
        await embalar.AtualizarAsync();
        var controles = await CarregarTodos();
        var itens = new List<object>();
        ControleVolumes? anterior = null;
        foreach (var c in controles)
        {
            var sobraAnt = SobraAnterior(c, anterior);
            var conf = ConferenciaVolumes.Calcular(c, sobraAnt);
            var embalou = ConferenciaVolumes.Somar(c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Embalou));
            itens.Add(new
            {
                c.Id,
                c.Numero,
                c.Data,
                conf.Status,
                embalou = ConferenciaVolumes.Produzidos
                    .Where(p => p.Ler(embalou) != 0)
                    .Select(p => new { p.Codigo, caixas = p.Ler(embalou) }),
                // Cru em produtosC; produzido (sobrou/faltou embalado) em caixas.
                diferencas = conf.Crus.Where(x => !x.Bate).Select(x => new { x.Nome, valor = x.Estoque, emProdutosC = true })
                    .Concat(conf.Produzidos.Where(x => !x.Bate).Select(x => new { x.Nome, valor = x.Estoque, emProdutosC = false })),
                entradas = c.Linhas.Count(l => l.Grupo == ControleVolumesLinha.Entrada),
                saidas = c.Linhas.Count(l => l.Grupo == ControleVolumesLinha.Saida),
            });
            anterior = c;
        }
        itens.Reverse();   // mais recente primeiro

        return Ok(new
        {
            proximoNumero = controles.Count == 0 ? 1 : controles.Max(c => c.Numero) + 1,
            controles = itens,
        });
    }

    // ── Detalhe ───────────────────────────────────────────────────────────

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalhe(int id)
    {
        await embalar.AtualizarAsync();
        var controles = await CarregarTodos();
        var i = controles.FindIndex(c => c.Id == id);
        if (i < 0) return NotFound();

        var c = controles[i];
        var anterior = i > 0 ? controles[i - 1] : null;
        var sobraAnt = SobraAnterior(c, anterior);
        var conf = ConferenciaVolumes.Calcular(c, sobraAnt);

        object? Unica(string grupo)
        {
            var l = c.Linhas.FirstOrDefault(x => x.Grupo == grupo);
            return l == null ? null : Linha(l);
        }

        return Ok(new
        {
            c.Id,
            c.Numero,
            c.Data,
            c.Observacao,
            c.PvcVinteComCaixaAb,
            c.SobraAnteriorManual,
            c.Origem,
            embalou = Unica(ControleVolumesLinha.Embalou),
            sobra = Unica(ControleVolumesLinha.Sobra),
            quebra = Unica(ControleVolumesLinha.Quebra),
            sobraAnterior = new
            {
                valores = Linha(sobraAnt),
                doControle = c.SobraAnteriorManual ? null : anterior?.Numero,
                doControleData = c.SobraAnteriorManual ? null : anterior?.Data,
            },
            entradas = c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Entrada)
                .OrderBy(l => l.Ordem).ThenBy(l => l.Id).Select(Linha),
            saidas = c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida)
                .OrderBy(l => l.Ordem).ThenBy(l => l.Id).Select(Linha),
            conferencia = conf,
            anteriorId = anterior?.Id,
            proximoId = i + 1 < controles.Count ? controles[i + 1].Id : (int?)null,
        });
    }

    // Sobra que um controle novo vai receber como sobra anterior: a sobra do
    // último controle com número menor. Para o cartão da tela de anotar.
    [HttpGet("sobra-anterior")]
    public async Task<IActionResult> SobraParaNovo([FromQuery] int? numero)
    {
        var q = db.VolumesControles.Include(c => c.Linhas).AsNoTracking();
        if (numero is > 0) q = q.Where(c => c.Numero < numero);
        var anterior = await q.OrderByDescending(c => c.Numero).FirstOrDefaultAsync();
        var valores = anterior == null
            ? new ControleVolumesLinha()
            : ConferenciaVolumes.SobraFinal(anterior.Linhas);
        return Ok(new
        {
            valores = Linha(valores),
            doControle = anterior?.Numero,
            doControleData = anterior?.Data,
        });
    }

    // ── Anotar a produtoB (cria o controle) ──────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Anotar([FromBody] ControleVolumesDto dto)
    {
        if (dto.Data == null) return BadRequest(new { erro = "Informe o dia da produtoB." });

        var numero = dto.Numero ?? 0;
        if (numero <= 0)
            numero = (await db.VolumesControles.MaxAsync(c => (int?)c.Numero) ?? 0) + 1;
        if (await db.VolumesControles.AnyAsync(c => c.Numero == numero))
            return Conflict(new { erro = $"Já existe o controle {numero}." });

        var erroVendas = ValidarVendas(dto.Vendas);
        if (erroVendas != null) return BadRequest(new { erro = erroVendas });

        var c = new ControleVolumes { Numero = numero, Data = dto.Data.Value, Origem = "manual" };
        Aplicar(c, dto);
        SincronizarVendas(c, dto.Vendas);
        db.VolumesControles.Add(c);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            // Dois salvando ao mesmo tempo: a restrição de número único segura.
            return Conflict(new { erro = $"Já existe o controle {numero}." });
        }
        // O romaneio da fazenda que estava aguardando este controle entra agora
        var romaneios = 0;
        try { romaneios = await embalar.LigarPendentesAsync(); }
        catch (Exception) { db.ChangeTracker.Clear(); }
        var produtosB = await SincronizarProdutosBAsync(c.Id);
        return Created($"/api/volumes-embalados/{c.Id}", new { c.Id, c.Numero, romaneios, produtosB });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Atualizar(int id, [FromBody] ControleVolumesDto dto)
    {
        var c = await db.VolumesControles.Include(x => x.Linhas).FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();

        if (dto.Numero is > 0 && dto.Numero != c.Numero)
        {
            if (await db.VolumesControles.AnyAsync(x => x.Numero == dto.Numero && x.Id != id))
                return Conflict(new { erro = $"Já existe o controle {dto.Numero}." });
            c.Numero = dto.Numero.Value;
        }
        var erroVendas = ValidarVendas(dto.Vendas);
        if (erroVendas != null) return BadRequest(new { erro = erroVendas });

        if (dto.Data != null) c.Data = dto.Data.Value;
        Aplicar(c, dto);
        SincronizarVendas(c, dto.Vendas);
        c.AtualizadoEm = DateTime.UtcNow;
        // Um SaveChanges só: cabeçalho, embalou, sobra, quebra e vendas entram
        // (ou falham) juntos, numa transação.
        await db.SaveChangesAsync();
        // Mudou a data: romaneio da fazenda aguardando pode ter achado o controle
        if (dto.Data != null) await embalar.AtualizarAsync();
        var produtosB = await SincronizarProdutosBAsync(c.Id);
        return Ok(new { c.Id, produtosB });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var c = await db.VolumesControles.FindAsync(id);
        if (c == null) return NotFound();
        db.VolumesControles.Remove(c);   // linhas saem junto (cascade)
        await db.SaveChangesAsync();
        // Sem o controle, a saída das produtosB usadas nele sai junto
        await SincronizarProdutosBAsync(id);
        return NoContent();
    }

    // ── Entradas e saídas ─────────────────────────────────────────────────

    [HttpPost("{id:int}/linhas")]
    public async Task<IActionResult> CriarLinha(int id, [FromBody] LinhaVolumesDto dto)
    {
        if (!GruposLista.Contains(dto.Grupo)) return BadRequest(new { erro = "Grupo inválido." });
        if (!await db.VolumesControles.AnyAsync(c => c.Id == id)) return NotFound();

        var ordem = await db.VolumesControleLinhas.Where(l => l.ControleId == id && l.Grupo == dto.Grupo)
            .MaxAsync(l => (int?)l.Ordem) ?? 0;
        var linha = new ControleVolumesLinha { ControleId = id, Grupo = dto.Grupo, Ordem = ordem + 1 };
        PreencherLinha(linha, dto);
        if (!ConferenciaVolumes.TemAlgum(linha)) return BadRequest(new { erro = "Informe alguma quantidade." });

        db.VolumesControleLinhas.Add(linha);
        await db.SaveChangesAsync();
        return Ok(new { linha.Id });
    }

    [HttpPut("linhas/{linhaId:int}")]
    public async Task<IActionResult> AtualizarLinha(int linhaId, [FromBody] LinhaVolumesDto dto)
    {
        var linha = await db.VolumesControleLinhas.FindAsync(linhaId);
        if (linha == null || !GruposLista.Contains(linha.Grupo)) return NotFound();

        PreencherLinha(linha, dto);
        if (!ConferenciaVolumes.TemAlgum(linha)) return BadRequest(new { erro = "Informe alguma quantidade." });
        linha.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { linha.Id });
    }

    [HttpDelete("linhas/{linhaId:int}")]
    public async Task<IActionResult> ExcluirLinha(int linhaId)
    {
        var linha = await db.VolumesControleLinhas.FindAsync(linhaId);
        if (linha == null || !GruposLista.Contains(linha.Grupo)) return NotFound();
        db.VolumesControleLinhas.Remove(linha);
        // Entrada que veio do romaneio da fazenda: apagada à mão, não volta sozinha
        await embalar.MarcarRetiradoAsync(linha.Origem);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Romaneios da fazenda sem funrural (volumes para embalar) ──────────────

    // Todos, com a situação: lançado (em qual controle), aguardando controle,
    // tirado à mão, histórico (até o último controle das planilhas) ou sem tipo cru.
    [HttpGet("romaneios-fazenda")]
    public async Task<IActionResult> RomaneiosFazenda()
    {
        await embalar.AtualizarAsync();
        return Ok(await embalar.ListarAsync());
    }

    public record MoverRomaneioDto(int ControleId);

    // Põe o romaneio neste controle (ou troca de controle), à mão
    [HttpPut("romaneios-fazenda/{numero:int}/controle")]
    public async Task<IActionResult> MoverRomaneio(int numero, [FromBody] MoverRomaneioDto dto)
    {
        try
        {
            var erro = await embalar.MoverAsync(numero, dto.ControleId);
            return erro == null ? Ok(new { numero, dto.ControleId }) : BadRequest(new { erro });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { erro = "O romaneio foi lançado por outra tela ao mesmo tempo. Abra de novo." });
        }
    }

    // Tira o romaneio dos volumes: a entrada sai e ele não volta sozinho
    [HttpDelete("romaneios-fazenda/{numero:int}/controle")]
    public async Task<IActionResult> RetirarRomaneio(int numero) =>
        await embalar.RetirarAsync(numero) ? NoContent() : NotFound(new { erro = "Romaneio da fazenda não encontrado." });

    // Nomes e placas já usados, para completar a digitação no celular.
    [HttpGet("sugestoes")]
    public async Task<IActionResult> Sugestoes()
    {
        var linhas = await db.VolumesControleLinhas
            .Where(l => l.Grupo == ControleVolumesLinha.Entrada || l.Grupo == ControleVolumesLinha.Saida)
            .Select(l => new { l.Grupo, l.Nome, l.Placa })
            .ToListAsync();
        var placasFrota = await db.Veiculos.Where(v => v.Ativo).Select(v => v.Placa).ToListAsync();

        static List<string> Distintos(IEnumerable<string?> v) =>
            v.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim().ToUpperInvariant())
             .Distinct().OrderBy(s => s).ToList();

        return Ok(new
        {
            clientes = Distintos(linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida).Select(l => l.Nome)),
            fornecedores = Distintos(linhas.Where(l => l.Grupo == ControleVolumesLinha.Entrada).Select(l => l.Nome)
                .Append("PAULO").Append("EMBALADOS FAZENDA").Append("EMBALADOS DUDU")),
            placas = Distintos(linhas.Select(l => l.Placa).Concat(placasFrota)),
        });
    }

    // ── Apoio ─────────────────────────────────────────────────────────────

    private Task<List<ControleVolumes>> CarregarTodos() =>
        db.VolumesControles.Include(c => c.Linhas).AsNoTracking()
            .OrderBy(c => c.Numero).ToListAsync();

    // Sobra anterior: a digitada neste controle, ou a sobra do controle anterior.
    private static ControleVolumesLinha SobraAnterior(ControleVolumes c, ControleVolumes? anterior)
    {
        if (c.SobraAnteriorManual)
            return ConferenciaVolumes.Somar(c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.SobraAnterior));
        if (anterior == null) return new ControleVolumesLinha();
        return ConferenciaVolumes.SobraFinal(anterior.Linhas);
    }

    // Vendas/saídas avulsas anotadas junto com a produtoB ("3 produtosC
    // vermelho fazenda Helena"). Sem preço: só controle. Viram linhas de saída
    // com Origem = anotacao; as saídas lançadas na aba Saídas não são tocadas.
    private static string? ValidarVendas(List<VendaVolumesDto>? vendas)
    {
        if (vendas == null) return null;
        foreach (var v in vendas)
        {
            if (string.IsNullOrWhiteSpace(v.Nome)) return "Toda venda precisa do nome de quem levou.";
            var l = new ControleVolumesLinha();
            Quantidades(l, v.Quantidades ?? new QuantidadesVolumesDto());
            if (!ConferenciaVolumes.TemAlgum(l)) return $"Venda de {v.Nome.Trim()} sem quantidade.";
        }
        return null;
    }

    // Lista nula = não mexe. Lista (mesmo vazia) = o estado final das vendas da
    // anotação: com Id atualiza, sem Id cria, a que sumiu da lista sai.
    private void SincronizarVendas(ControleVolumes c, List<VendaVolumesDto>? vendas)
    {
        if (vendas == null) return;
        var atuais = c.Linhas
            .Where(l => l.Grupo == ControleVolumesLinha.Saida && l.Origem == ControleVolumesLinha.OrigemAnotacao)
            .ToList();
        var ids = vendas.Where(v => v.Id is > 0).Select(v => v.Id!.Value).ToHashSet();

        foreach (var l in atuais.Where(l => !ids.Contains(l.Id)))
        {
            c.Linhas.Remove(l);
            if (l.Id != 0) db.VolumesControleLinhas.Remove(l);
        }

        var ordem = c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.Saida).Select(l => l.Ordem).DefaultIfEmpty(0).Max();
        foreach (var v in vendas)
        {
            // Id que não é venda da anotação deste controle (apagada na aba
            // Saídas enquanto a tela estava aberta): vira venda nova.
            var linha = v.Id is > 0 ? atuais.FirstOrDefault(l => l.Id == v.Id) : null;
            if (linha == null)
            {
                linha = new ControleVolumesLinha
                {
                    Grupo = ControleVolumesLinha.Saida, Origem = ControleVolumesLinha.OrigemAnotacao, Ordem = ++ordem,
                };
                c.Linhas.Add(linha);
            }
            // A placa não vem da anotação: mantém a que foi posta na aba Saídas.
            PreencherLinha(linha, new LinhaVolumesDto(ControleVolumesLinha.Saida, v.Nome, linha.Placa, v.Data ?? c.Data, v.Quantidades));
            linha.AtualizadoEm = DateTime.UtcNow;
        }
    }

    // Cabeçalho + as linhas únicas (embalou, sobra, quebra, sobra anterior).
    // Campo que não veio no corpo fica como está.
    private void Aplicar(ControleVolumes c, ControleVolumesDto dto)
    {
        if (dto.Observacao != null) c.Observacao = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim();
        if (dto.PvcVinteComCaixaAb != null) c.PvcVinteComCaixaAb = Math.Max(0, dto.PvcVinteComCaixaAb.Value);

        GravarUnica(c, ControleVolumesLinha.Embalou, dto.Embalou);
        GravarUnica(c, ControleVolumesLinha.Sobra, dto.Sobra);
        GravarUnica(c, ControleVolumesLinha.Quebra, dto.Quebra);

        if (dto.SobraAnteriorManual != null)
        {
            c.SobraAnteriorManual = dto.SobraAnteriorManual.Value;
            GravarUnica(c, ControleVolumesLinha.SobraAnterior,
                c.SobraAnteriorManual ? dto.SobraAnterior ?? new QuantidadesVolumesDto() : null, apagarSeVazio: !c.SobraAnteriorManual);
        }
    }

    private void GravarUnica(ControleVolumes c, string grupo, QuantidadesVolumesDto? q, bool apagarSeVazio = false)
    {
        var existente = c.Linhas.FirstOrDefault(l => l.Grupo == grupo);
        if (q == null)
        {
            if (apagarSeVazio && existente != null)
            {
                c.Linhas.Remove(existente);
                if (existente.Id != 0) db.VolumesControleLinhas.Remove(existente);
            }
            return;
        }
        if (existente == null)
        {
            existente = new ControleVolumesLinha { Grupo = grupo };
            c.Linhas.Add(existente);
        }
        Quantidades(existente, q);
        existente.AtualizadoEm = DateTime.UtcNow;
    }

    private static void PreencherLinha(ControleVolumesLinha l, LinhaVolumesDto dto)
    {
        l.Nome = string.IsNullOrWhiteSpace(dto.Nome) ? null : dto.Nome.Trim().ToUpperInvariant();
        l.Placa = string.IsNullOrWhiteSpace(dto.Placa) ? null : dto.Placa.Trim().ToUpperInvariant();
        l.Data = dto.Data;
        Quantidades(l, dto.Quantidades ?? new QuantidadesVolumesDto());
    }

    private static void Quantidades(ControleVolumesLinha l, QuantidadesVolumesDto q)
    {
        static decimal N(decimal? v) => Math.Max(0, v ?? 0);
        l.Ab = N(q.Ab); l.Ab20 = N(q.Ab20); l.Bb = N(q.Bb); l.Av = N(q.Av);
        l.Abe = N(q.Abe); l.Ave = N(q.Ave); l.GrPvcBr = N(q.GrPvcBr); l.MdPvcBr = N(q.MdPvcBr);
        l.GrPvc20 = N(q.GrPvc20); l.GrPvcVr = N(q.GrPvcVr);
    }

    private static object Linha(ControleVolumesLinha l) => new
    {
        l.Id, l.Grupo, l.Nome, l.Placa, l.Data, l.Origem,
        l.Ab, l.Ab20, l.Bb, l.Av,
        l.Abe, l.Ave, l.GrPvcBr, l.MdPvcBr, l.GrPvc20, l.GrPvcVr,
    };
}

// Crus em produtosC; produzidos em caixas.
public record QuantidadesVolumesDto(
    decimal? Ab = null, decimal? Ab20 = null, decimal? Bb = null, decimal? Av = null,
    decimal? Abe = null, decimal? Ave = null, decimal? GrPvcBr = null, decimal? MdPvcBr = null,
    decimal? GrPvc20 = null, decimal? GrPvcVr = null);

public record ControleVolumesDto(
    int? Numero, DateOnly? Data, QuantidadesVolumesDto? Embalou, decimal? PvcVinteComCaixaAb,
    QuantidadesVolumesDto? Sobra, QuantidadesVolumesDto? Quebra, string? Observacao,
    bool? SobraAnteriorManual, QuantidadesVolumesDto? SobraAnterior,
    List<VendaVolumesDto>? Vendas = null);

// Venda/saída avulsa da anotação. Id da linha quando já existe; Data nula = dia da produtoB.
public record VendaVolumesDto(int? Id, string? Nome, DateOnly? Data, QuantidadesVolumesDto? Quantidades);

public record LinhaVolumesDto(string Grupo, string? Nome, string? Placa, DateOnly? Data, QuantidadesVolumesDto? Quantidades);
