using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Despesas divididas entre os caminhões (VIA-19). Dois modos por mês:
/// "placas" (meses com placas marcadas: total ÷ placas = valor por placa; cada sócio paga
/// valor por placa × placas dele, pelo responsável da placa) e "planilha" (jan–set/2026:
/// linhas com "dividir por" + quantidade de caminhões por sócio, como na planilha antiga).
/// </summary>
[Authorize]
[ApiController]
[Route("api/despesas-divididas")]
public sealed class DespesasDivididasController(AppDbContext db) : ControllerBase
{
    private static readonly string[] Socios = ["Bento", "Caio", "Dudu"];

    public record LinhaDto(int Ano, int Mes, string Descricao, decimal Valor, int DividirPor, string? Observacao);
    public record SocioDto(string Socio, int Quantidade, string? Placas);
    public record SociosDto(int Ano, int Mes, List<SocioDto> Socios);
    public record PlacasMesDto(int Ano, int Mes, List<string>? Placas);
    public record SocioMesDto(string Socio, int Quantidade, string? Placas, decimal Valor);

    private static decimal Dividido(DespesaDivididaLinha l) => l.Valor / Math.Max(1, l.DividirPor);
    private static decimal PorCaminhao(IEnumerable<DespesaDivididaLinha> ls) =>
        Math.Round(ls.Sum(Dividido), 2, MidpointRounding.AwayFromZero);

    private static string Sigla(string placa) => placa.Length >= 3 ? placa[..3] : placa;

    /// <summary>Modo "placas": valor por placa e quanto cada sócio paga (placas marcadas × responsável da placa).</summary>
    private static (decimal PorPlaca, List<SocioMesDto> Socios, List<string> SemResp) CalcularPlacas(
        decimal total, List<string> marcadas, Dictionary<string, string?> responsavelDe)
    {
        var porPlaca = marcadas.Count == 0 ? 0 : Math.Round(total / marcadas.Count, 2, MidpointRounding.AwayFromZero);
        var socios = Socios.Select(n =>
        {
            var minhas = marcadas.Where(p => responsavelDe.GetValueOrDefault(p) == n).ToList();
            var siglas = minhas.Select(Sigla).OrderBy(x => x, StringComparer.Ordinal);
            return new SocioMesDto(n, minhas.Count, minhas.Count == 0 ? null : string.Join("-", siglas), porPlaca * minhas.Count);
        }).ToList();
        var semResp = marcadas.Where(p => !Socios.Contains(responsavelDe.GetValueOrDefault(p) ?? ""))
            .OrderBy(p => p, StringComparer.Ordinal).ToList();
        return (porPlaca, socios, semResp);
    }

    private async Task<Dictionary<string, string?>> Cavalos(bool soAtivos)
    {
        var q = db.PlacasFrota.AsNoTracking().Where(p => p.Tipo == PlacasController.Cavalo);
        if (soAtivos) q = q.Where(p => p.Ativo);
        return await q.ToDictionaryAsync(p => p.Placa, p => p.Responsavel);
    }

    /// <summary>Cavalos ativos com movimento no mês: viagem, carregamento de volumes ou linha do Sem Parar.</summary>
    private async Task<List<string>> Sugestao(int ano, int mes, IEnumerable<string> ativos)
    {
        var ini = new DateOnly(ano, mes, 1);
        var fim = ini.AddMonths(1);
        var viagens = await db.ViagensCaminhao.AsNoTracking()
            .Where(v => (v.DataSaida != null && v.DataSaida >= ini && v.DataSaida < fim)
                     || (v.DataSaida == null && v.DataChegada != null && v.DataChegada >= ini && v.DataChegada < fim))
            .Select(v => v.Placa).Distinct().ToListAsync();
        var carregs = await db.CarregamentosVolumes.AsNoTracking()
            .Where(c => c.Data >= ini && c.Data < fim).Select(c => c.Placa).Distinct().ToListAsync();
        var passagens = await db.SemPararLinhas.AsNoTracking()
            .Where(l => l.Data != null && l.Data >= ini && l.Data < fim).Select(l => l.Placa).Distinct().ToListAsync();
        var usadas = viagens.Concat(carregs).Concat(passagens).Select(ViagemCaminhaoCalculo.NormalizarPlaca).ToHashSet();
        return ativos.Where(usadas.Contains).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    [HttpGet]
    public async Task<IActionResult> Mes([FromQuery] int ano, [FromQuery] int mes)
    {
        if (ano < 2000 || mes < 1 || mes > 12) return BadRequest(new { erro = "Informe ano e mês válidos." });
        var linhas = await db.DespesasDivididasLinhas.AsNoTracking()
            .Where(l => l.Ano == ano && l.Mes == mes).OrderBy(l => l.Id).ToListAsync();
        var marcadas = await db.DespesasDivididasPlacas.AsNoTracking()
            .Where(x => x.Ano == ano && x.Mes == mes).Select(x => x.Placa).ToListAsync();
        marcadas = marcadas.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var todos = await Cavalos(false);
        var ativos = (await Cavalos(true)).Keys.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var cavalos = ativos.Select(p => new { placa = p, responsavel = todos[p] }).ToList();
        var sugestao = await Sugestao(ano, mes, ativos);
        var total = linhas.Sum(l => l.Valor);

        if (marcadas.Count > 0)
        {
            var (porPlaca, sociosP, semResp) = CalcularPlacas(total, marcadas, todos);
            var n = marcadas.Count;
            return Ok(new
            {
                ano, mes, modo = "placas",
                linhas = linhas.Select(l => new { l.Id, l.Descricao, l.Valor, l.DividirPor, l.Observacao, valorDividido = l.Valor / n }),
                totalDespesas = total,
                valorPorCaminhao = porPlaca, nPlacas = n,
                socios = sociosP.Select(s => new { socio = s.Socio, quantidade = s.Quantidade, placas = s.Placas, valor = s.Valor }),
                semResponsavel = semResp, placasMarcadas = marcadas, sugestao, cavalos,
            });
        }

        var regs = await db.DespesasDivididasSocios.AsNoTracking().Where(s => s.Ano == ano && s.Mes == mes).ToListAsync();
        var porCaminhao = PorCaminhao(linhas);
        var socios = Socios.Select(n =>
        {
            var r = regs.FirstOrDefault(x => x.Socio == n);
            var q = r?.Quantidade ?? 0;
            return new { socio = n, quantidade = q, placas = r?.Placas, valor = porCaminhao * q };
        }).ToList();
        return Ok(new
        {
            ano, mes, modo = "planilha",
            linhas = linhas.Select(l => new { l.Id, l.Descricao, l.Valor, l.DividirPor, l.Observacao, valorDividido = Dividido(l) }),
            totalDespesas = total,
            valorPorCaminhao = porCaminhao,
            nPlacas = 0,
            socios,
            semResponsavel = new List<string>(), placasMarcadas = marcadas, sugestao, cavalos,
        });
    }

    // Grava a marcação do mês (substitui a lista); lista vazia volta o mês para o modo planilha
    [HttpPut("placas")]
    public async Task<IActionResult> SalvarPlacas(PlacasMesDto dto)
    {
        if (dto.Ano < 2000 || dto.Mes < 1 || dto.Mes > 12) return BadRequest(new { erro = "Informe ano e mês válidos." });
        var novas = (dto.Placas ?? []).Select(ViagemCaminhaoCalculo.NormalizarPlaca).Distinct().ToList();
        var cavalos = await Cavalos(false);
        var fora = novas.Where(p => !cavalos.ContainsKey(p)).ToList();
        if (fora.Count > 0) return BadRequest(new { erro = $"Placa não cadastrada como cavalo: {string.Join(", ", fora)}." });
        var atuais = await db.DespesasDivididasPlacas.Where(x => x.Ano == dto.Ano && x.Mes == dto.Mes).ToListAsync();
        db.DespesasDivididasPlacas.RemoveRange(atuais.Where(x => !novas.Contains(x.Placa)));
        foreach (var p in novas.Where(p => atuais.All(x => x.Placa != p)))
            db.DespesasDivididasPlacas.Add(new DespesaDivididaPlaca { Ano = dto.Ano, Mes = dto.Mes, Placa = p });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost]
    public Task<IActionResult> Criar(LinhaDto dto) => SalvarLinha(null, dto);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Alterar(int id, LinhaDto dto) => SalvarLinha(id, dto);

    private async Task<IActionResult> SalvarLinha(int? id, LinhaDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Descricao)) return BadRequest(new { erro = "Informe a referência." });
        if (dto.Valor <= 0) return BadRequest(new { erro = "O valor precisa ser maior que zero." });
        if (dto.DividirPor < 1) return BadRequest(new { erro = "Dividir por precisa ser 1 ou mais." });
        if (dto.Ano < 2000 || dto.Mes < 1 || dto.Mes > 12) return BadRequest(new { erro = "Informe ano e mês válidos." });
        var l = id == null ? new DespesaDivididaLinha() : await db.DespesasDivididasLinhas.FindAsync(id.Value);
        if (l == null) return NotFound(new { erro = "Linha não encontrada." });
        if (id == null) db.DespesasDivididasLinhas.Add(l);
        l.Ano = dto.Ano; l.Mes = dto.Mes; l.Descricao = dto.Descricao.Trim();
        l.Valor = Math.Round(dto.Valor, 2); l.DividirPor = dto.DividirPor;
        l.Observacao = string.IsNullOrWhiteSpace(dto.Observacao) ? null : dto.Observacao.Trim();
        l.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { l.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        var l = await db.DespesasDivididasLinhas.FindAsync(id);
        if (l == null) return NotFound();
        db.DespesasDivididasLinhas.Remove(l); await db.SaveChangesAsync(); return NoContent();
    }

    [HttpPut("socios")]
    public async Task<IActionResult> SalvarSocios(SociosDto dto)
    {
        if (dto.Ano < 2000 || dto.Mes < 1 || dto.Mes > 12) return BadRequest(new { erro = "Informe ano e mês válidos." });
        if (dto.Socios.Any(s => !Socios.Contains(s.Socio) || s.Quantidade < 0))
            return BadRequest(new { erro = "Sócio ou quantidade inválidos." });
        var regs = await db.DespesasDivididasSocios.Where(s => s.Ano == dto.Ano && s.Mes == dto.Mes).ToListAsync();
        foreach (var s in dto.Socios)
        {
            var r = regs.FirstOrDefault(x => x.Socio == s.Socio);
            if (r == null)
            {
                r = new DespesaDivididaSocio { Ano = dto.Ano, Mes = dto.Mes, Socio = s.Socio };
                db.DespesasDivididasSocios.Add(r); regs.Add(r);
            }
            r.Quantidade = s.Quantidade;
            r.Placas = string.IsNullOrWhiteSpace(s.Placas) ? null : s.Placas.Trim();
            r.AtualizadoEm = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] int ano)
    {
        var linhas = await db.DespesasDivididasLinhas.AsNoTracking().Where(l => l.Ano == ano).ToListAsync();
        var regs = await db.DespesasDivididasSocios.AsNoTracking().Where(s => s.Ano == ano).ToListAsync();
        var placas = await db.DespesasDivididasPlacas.AsNoTracking().Where(x => x.Ano == ano).ToListAsync();
        var todos = await Cavalos(false);
        var meses = Enumerable.Range(1, 12)
            .Where(m => linhas.Any(l => l.Mes == m) || regs.Any(r => r.Mes == m && r.Quantidade > 0) || placas.Any(x => x.Mes == m))
            .Select(m =>
            {
                var marcadas = placas.Where(x => x.Mes == m).Select(x => x.Placa).ToList();
                if (marcadas.Count > 0)
                {
                    var (porPlaca, sociosP, _) = CalcularPlacas(linhas.Where(l => l.Mes == m).Sum(l => l.Valor), marcadas, todos);
                    return new { mes = m, modo = "placas", valorPorCaminhao = porPlaca, socios = sociosP };
                }
                var porCaminhao = PorCaminhao(linhas.Where(l => l.Mes == m));
                return new
                {
                    mes = m, modo = "planilha", valorPorCaminhao = porCaminhao,
                    socios = Socios.Select(n =>
                    {
                        var r = regs.FirstOrDefault(x => x.Mes == m && x.Socio == n);
                        var q = r?.Quantidade ?? 0;
                        return new SocioMesDto(n, q, r?.Placas, porCaminhao * q);
                    }).ToList(),
                };
            }).ToList();
        var totais = Socios.Select(n => new
        {
            socio = n,
            quantidade = meses.Sum(m => m.socios.First(s => s.Socio == n).Quantidade),
            valor = meses.Sum(m => m.socios.First(s => s.Socio == n).Valor),
        }).ToList();
        return Ok(new
        {
            ano,
            meses = meses.Select(m => new
            {
                m.mes, m.modo, m.valorPorCaminhao,
                socios = m.socios.Select(s => new { socio = s.Socio, quantidade = s.Quantidade, placas = s.Placas, valor = s.Valor }),
            }),
            totais,
        });
    }
}
