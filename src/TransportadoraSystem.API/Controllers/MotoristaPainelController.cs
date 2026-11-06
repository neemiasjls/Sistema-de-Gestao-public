using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Painel do motorista: só o formulário da folha "RELATÓRIO DE VIAGEM". Ele preenche,
/// envia e a viagem cai no controle do escritório (situação "lançada"). Pode corrigir
/// o último envio enquanto o escritório não conferiu. Nada de valores de frete,
/// comissão ou lista de viagens (Admin, 06/10/2026); só a sobra da viagem anterior
/// do caminhão, que ele leva para esta (Admin, 07/10/2026).
/// A conferência do perfil é no banco, não só no token.
/// </summary>
[Authorize]
[ApiController]
[Route("api/motorista-painel")]
public class MotoristaPainelController(ViagensCaminhaoService svc, AppDbContext db) : ControllerBase, IAsyncActionFilter
{
    private MotoristaFrota? _motorista;
    private int _usuarioId;

    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var id = AuthController.UsuarioId(User);
        var u = id == null ? null : await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (u?.Perfil != AuthService.PerfilMotorista)
        {
            ctx.Result = StatusCode(403, new { erro = "Este painel é só para motoristas." });
            return;
        }
        if (!u.Ativo)
        {
            ctx.Result = StatusCode(403, new { erro = "Seu acesso está desativado. Fale com o escritório.", semCadastro = true });
            return;
        }
        // Usuário de motorista criado à mão, sem o cadastro (ou cadastro desativado):
        // avisa claro em vez de quebrar. O escritório completa em Perfil → Motoristas.
        var cadastro = await db.MotoristasFrota.FirstOrDefaultAsync(m => m.UsuarioId == u.Id);
        if (cadastro == null || !cadastro.Ativo)
        {
            ctx.Result = StatusCode(403, new
            {
                erro = cadastro == null
                    ? "Seu usuário ainda não tem cadastro de motorista. Fale com o escritório."
                    : "Seu cadastro de motorista está desativado. Fale com o escritório.",
                semCadastro = true,
            });
            return;
        }
        _motorista = cadastro;
        _usuarioId = u!.Id;
        await next();
    }

    [HttpGet("inicio")]
    public async Task<IActionResult> Inicio()
    {
        var m = _motorista!;
        var ultima = await UltimaAsync(m.Id);
        object? ult = null;
        if (ultima != null)
        {
            var podeCorrigir = ultima.Status == "lancada";
            ult = new
            {
                ultima.Id, ultima.EnviadoEm, ultima.Placa, ultima.Destino, ultima.DataSaida, PodeCorrigir = podeCorrigir,
                // Só o que ele mesmo digitou, e só enquanto pode corrigir
                Folha = podeCorrigir ? Folha(ultima) : null,
            };
        }
        return Ok(new
        {
            m.Nome, PrimeiroNome = m.Nome.Split(' ')[0],
            ValorDiaria = ViagensCaminhaoService.ValorDiariaPadrao, Ultima = ult,
            SugestoesCaixinha = await SugestoesCaixinhaAsync(m.Id),
        });
    }

    // Placas para a busca da folha (VIA-20): só as ativas do cadastro, cavalo e carreta separados.
    // Sem placa padrão: o motorista escolhe as duas a cada viagem.
    [HttpGet("placas")]
    public async Task<IActionResult> Placas()
    {
        var lista = await db.PlacasFrota.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Placa)
            .Select(p => new { p.Placa, p.Tipo }).ToListAsync();
        return Ok(new
        {
            Cavalos = lista.Where(p => p.Tipo == PlacasController.Cavalo).Select(p => p.Placa),
            Carretas = lista.Where(p => p.Tipo == PlacasController.Carreta).Select(p => p.Placa),
        });
    }

    // Sobra da viagem anterior DELE (Admin, 07/10/2026; por motorista desde 02/11/2026, VIA-21):
    // ele leva para esta viagem, com qualquer placa. Ao corrigir, viagemId é o envio que
    // está corrigindo (só vale se for dele) e ele mesmo sai da conta.
    [HttpGet("sobra-anterior")]
    public async Task<IActionResult> SobraAnterior([FromQuery] string? placa, [FromQuery] DateOnly? dataSaida, [FromQuery] int? viagemId)
    {
        int? propria = null;
        if (viagemId is { } vid && await db.ViagensCaminhao.AnyAsync(v => v.Id == vid && v.MotoristaId == _motorista!.Id))
            propria = vid;
        var r = await svc.SobraAnteriorPainelAsync(_motorista!.Id, placa, dataSaida, propria);
        return Ok(new { r.TemAnterior, r.Sobra, r.DataViagemAnterior });
    }

    // "De quem é a caixinha": clientes da tabela de frete + nomes que ele já usou
    private async Task<List<string>> SugestoesCaixinhaAsync(int motoristaId)
    {
        var clientes = await db.PrecosFreteCliente.AsNoTracking().Where(p => p.Ativo).OrderBy(p => p.Ordem).Select(p => p.Cliente).ToListAsync();
        var usados = await db.ViagemCaminhaoDespesas.AsNoTracking()
            .Where(d => d.Tipo == ViagemCaminhaoCalculo.Caixinha && d.Descricao != null && d.Viagem!.MotoristaId == motoristaId)
            .Select(d => d.Descricao!).Distinct().ToListAsync();
        return usados.Concat(clientes).Select(x => x.Trim().ToUpperInvariant()).Where(x => x.Length > 0).Distinct().Take(200).ToList();
    }

    [HttpPost("viagens")]
    public async Task<IActionResult> Enviar([FromBody] ViagemEntrada e)
    {
        try
        {
            var v = await svc.SalvarAsync(null, e, "motorista", _usuarioId, _motorista);
            return Ok(new { id = v.Id, enviadoEm = v.EnviadoEm });
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPut("viagens/{id:int}")]
    public async Task<IActionResult> Corrigir(int id, [FromBody] ViagemEntrada e)
    {
        var ultima = await UltimaAsync(_motorista!.Id);
        if (ultima == null || ultima.Id != id)
            return BadRequest(new { erro = "Só dá para corrigir o último envio." });
        if (ultima.Status != "lancada")
            return BadRequest(new { erro = "O escritório já conferiu esta viagem. Fale com o escritório para corrigir." });
        try
        {
            var v = await svc.SalvarAsync(id, e, "motorista", _usuarioId, _motorista);
            return Ok(new { id = v.Id, enviadoEm = v.EnviadoEm });
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    private Task<ViagemCaminhao?> UltimaAsync(int motoristaId) =>
        db.ViagensCaminhao.AsNoTracking()
            .Include(v => v.Abastecimentos).Include(v => v.Despesas).Include(v => v.Cargas).AsSplitQuery()
            .Where(v => v.MotoristaId == motoristaId && v.Origem == "motorista")
            .OrderByDescending(v => v.EnviadoEm).ThenByDescending(v => v.Id)
            .FirstOrDefaultAsync();

    private static object Folha(ViagemCaminhao v) => new
    {
        v.Placa, v.PlacaCarreta, v.Destino, v.DataSaida, v.DataChegada, v.KmSaida, v.KmChegada, v.Litros,
        v.Levou, v.DataDinheiro, v.DiariasQuantidade, v.Observacoes,
        Abastecimentos = v.Abastecimentos.OrderBy(a => a.Ordem).Select(a => new { a.Data, a.Km, a.Litros, a.Valor, a.Local, a.Observacao, a.Forma }),
        Despesas = v.Despesas.OrderBy(d => d.Ordem).Select(d => new { d.Tipo, d.Descricao, d.Valor, d.Observacao }),
        Cargas = v.Cargas.OrderBy(c => c.Ordem).Select(c => new { c.Tipo, c.Transportadora, c.Data, c.Origem, c.Destino, c.Valor, c.RetornoId }),
    };
}
