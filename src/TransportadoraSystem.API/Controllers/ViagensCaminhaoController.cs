using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Controle de viagens dos caminhões (escritório): viagens por caminhão e mês, como
/// as abas do acerto mensal; motoristas; tabela de frete por cliente; controle de
/// carregamento; importação das planilhas antigas e exportação no mesmo layout.
/// O perfil motorista não chega aqui (Program.cs barra tudo fora do painel dele).
/// </summary>
[Authorize]
[ApiController]
[Route("api/viagens-caminhao")]
public class ViagensCaminhaoController(ViagensCaminhaoService svc, AuthService auth, AppDbContext db,
    PrecosClienteService precos, ILogger<ViagensCaminhaoController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int ano, [FromQuery] int mes, [FromQuery] string? placa, [FromQuery] int? motoristaId)
    {
        if (mes is < 1 or > 12 || ano < 2000) return BadRequest(new { erro = "Mês inválido." });
        return Ok(await svc.ListarAsync(ano, mes, placa, motoristaId));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detalhe(int id) =>
        await svc.DetalheAsync(id) is { } d ? Ok(d) : NotFound(new { erro = "Viagem não encontrada." });

    [HttpPost]
    public Task<IActionResult> Criar([FromBody] ViagemEntrada e) => Salvar(null, e);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Atualizar(int id, [FromBody] ViagemEntrada e) => Salvar(id, e);

    private async Task<IActionResult> Salvar(int? id, ViagemEntrada e)
    {
        try
        {
            var v = await svc.SalvarAsync(id, e, "escritorio", AuthController.UsuarioId(User));
            return Ok(new { id = v.Id });
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        try { await svc.ExcluirAsync(id); return Ok(new { mensagem = "Viagem apagada." }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    public record StatusDto(string Status);

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> Status(int id, [FromBody] StatusDto dto)
    {
        try { await svc.MudarStatusAsync(id, dto.Status); return Ok(new { mensagem = "Situação atualizada." }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPost("{id:int}/carregamento/{carregamentoId:int}")]
    public async Task<IActionResult> AplicarCarregamento(int id, int carregamentoId)
    {
        try { return Ok(new { avisos = await svc.AplicarCarregamentoAsync(id, carregamentoId) }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpGet("excel")]
    public async Task<IActionResult> Excel([FromQuery] string placa, [FromQuery] int ano, [FromQuery] int mes)
    {
        try
        {
            var (bytes, nome) = await svc.ExportarAcertoAsync(placa, ano, mes);
            return File(bytes, Xlsx, nome);
        }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // ── Importações ──

    [HttpPost("importar-acerto")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> ImportarAcerto([FromForm] List<IFormFile> arquivos, [FromQuery] string? placa, [FromQuery] bool substituir = false)
    {
        if (arquivos.Count == 0) return BadRequest(new { erro = "Envie a planilha do acerto (.xlsx ou .xls)." });
        var resultados = new List<object>();
        foreach (var f in arquivos)
        {
            try { resultados.Add(await svc.ImportarAcertoAsync(await Bytes(f), f.FileName, placa, substituir)); }
            catch (Exception ex) when (ex is InvalidOperationException or ErroViagem or ExcelDataReader.Exceptions.HeaderException)
            { resultados.Add(new { arquivo = f.FileName, erro = ex.Message }); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao importar o acerto {Arquivo}", f.FileName);
                resultados.Add(new { arquivo = f.FileName, erro = "Não consegui ler esta planilha." });
            }
        }
        return Ok(resultados);
    }

    [HttpPost("importar-carregamento")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> ImportarCarregamento([FromForm] List<IFormFile> arquivos)
    {
        if (arquivos.Count == 0) return BadRequest(new { erro = "Envie o controle de carregamento (.xlsx ou .xls)." });
        var resultados = new List<object>();
        foreach (var f in arquivos)
        {
            try { resultados.Add(await svc.ImportarCarregamentoAsync(await Bytes(f), f.FileName)); }
            catch (Exception ex) when (ex is InvalidOperationException or ErroViagem or ExcelDataReader.Exceptions.HeaderException)
            { resultados.Add(new { arquivo = f.FileName, erro = ex.Message }); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao importar o carregamento {Arquivo}", f.FileName);
                resultados.Add(new { arquivo = f.FileName, erro = "Não consegui ler esta planilha." });
            }
        }
        return Ok(resultados);
    }

    [HttpGet("carregamentos")]
    public async Task<IActionResult> Carregamentos([FromQuery] int ano, [FromQuery] int mes)
    {
        if (mes is < 1 or > 12 || ano < 2000) return BadRequest(new { erro = "Mês inválido." });
        return Ok(await svc.CarregamentosAsync(ano, mes));
    }

    // Lê de novo o carregamento guardado com a regra atual (VIA-09), sem precisar da planilha
    [HttpPost("carregamentos/{id:int}/reprocessar")]
    public async Task<IActionResult> ReprocessarCarregamento(int id)
    {
        try { return Ok(await svc.ReprocessarCarregamentoAsync(id)); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpDelete("carregamentos/{id:int}")]
    public async Task<IActionResult> ExcluirCarregamento(int id)
    {
        try { return Ok(new { mensagem = "Carregamento apagado.", avisos = await svc.ExcluirCarregamentoAsync(id) }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    // ── Motoristas ──
    // Cadastro (Perfil → Motoristas e a aba Motoristas daqui): só escritório e dono;
    // o perfil motorista recebe 403 no Program.cs antes de chegar aqui.

    public record MotoristaDto(string? Nome, string? Apelidos, string? Login, decimal? PorcentagemComissao,
        string? PlacaCavalo, string? PlacaCarreta, bool? Ativo, int? UsuarioId = null,
        string? Cpf = null, string? Rg = null, string? RgOrgaoUf = null, DateOnly? DataNascimento = null,
        string? CidadeNascimento = null, string? UfNascimento = null, string? CnhRegistro = null, string? CnhCategoria = null,
        DateOnly? CnhEmissao = null, DateOnly? CnhValidade = null, string? Pai = null, string? Mae = null);
    public record SenhaMotoristaDto(string? Senha);
    public record AtivoDto(bool Ativo);

    [HttpGet("motoristas")]
    public async Task<IActionResult> Motoristas()
    {
        // Sem o PDF da CNH (bytea): a lista não carrega os arquivos
        var lista = await MotoristasFrotaController.SemPdf(db.MotoristasFrota.AsNoTracking())
            .OrderByDescending(m => m.Ativo).ThenBy(m => m.Nome).ToListAsync();
        var ids = lista.Where(m => m.UsuarioId != null).Select(m => m.UsuarioId!.Value).ToList();
        var usuarios = await db.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Perfil == AuthService.PerfilMotorista)
            .ToDictionaryAsync(u => u.Id, u => u.Ativo);
        return Ok(lista.Select(m => new
        {
            m.Id, m.Nome, m.Apelidos, m.Login, m.PorcentagemComissao, m.PlacaCavalo, m.PlacaCarreta, m.Ativo, m.UsuarioId,
            m.Cpf, m.Rg, m.RgOrgaoUf, m.DataNascimento, m.CidadeNascimento, m.UfNascimento, m.CnhRegistro, m.CnhCategoria,
            m.CnhEmissao, m.CnhValidade, m.Pai, m.Mae, m.CnhAtualizadaEm, TemCnhPdf = m.CnhPdfNome != null,
            TemUsuario = m.UsuarioId is { } u1 && usuarios.ContainsKey(u1),
            Acesso = m.UsuarioId is { } u2 && usuarios.TryGetValue(u2, out var ativo) && ativo,
        }));
    }

    // Usuário de motorista criado à mão (SQL Editor) sem cadastro: aparece para completar
    [HttpGet("motoristas/usuarios-sem-cadastro")]
    public async Task<IActionResult> UsuariosSemCadastro() => Ok(await auth.UsuariosMotoristaSemCadastroAsync());

    [HttpPost("motoristas")]
    public Task<IActionResult> CriarMotorista([FromBody] MotoristaDto dto) => SalvarMotorista(null, dto);

    [HttpPut("motoristas/{id:int}")]
    public Task<IActionResult> AtualizarMotorista(int id, [FromBody] MotoristaDto dto) => SalvarMotorista(id, dto);

    private async Task<IActionResult> SalvarMotorista(int? id, MotoristaDto dto)
    {
        var nome = (dto.Nome ?? "").Trim().ToUpperInvariant();
        var login = (dto.Login ?? "").Trim().ToLowerInvariant();
        if (nome.Length == 0) return BadRequest(new { erro = "Informe o nome." });
        if (!AuthService.LoginMotoristaValido(login)) return BadRequest(new { erro = "Login: só letras minúsculas e números, sem espaço nem acento (ex.: joseferraz)." });
        if (dto.PorcentagemComissao is < 0 or > 100) return BadRequest(new { erro = "% de comissão inválida." });
        if (!string.IsNullOrWhiteSpace(dto.Cpf) && MotoristasFrotaController.FormatarCpf(dto.Cpf) == null)
            return BadRequest(new { erro = "CPF inválido: são 11 números." });
        if (await db.MotoristasFrota.AnyAsync(m => m.Login == login && m.Id != (id ?? 0)))
            return BadRequest(new { erro = "Este login já é de outro motorista." });
        if (await db.Usuarios.AnyAsync(u => u.Email.ToLower() == login && u.Perfil != AuthService.PerfilMotorista))
            return BadRequest(new { erro = $"O login \"{login}\" já é de um usuário do escritório. Escolha outro." });

        MotoristaFrota m;
        if (id is { } mid)
        {
            var achado = await db.MotoristasFrota.FindAsync(mid);
            if (achado == null) return NotFound(new { erro = "Motorista não encontrado." });
            m = achado;
        }
        else { m = new MotoristaFrota(); db.MotoristasFrota.Add(m); }

        // Completar o cadastro de um usuário de motorista que já existe
        if (id == null && dto.UsuarioId is { } uid)
        {
            var u = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
            if (u == null || u.Perfil != AuthService.PerfilMotorista)
                return BadRequest(new { erro = "Usuário de motorista não encontrado." });
            if (await db.MotoristasFrota.AnyAsync(x => x.UsuarioId == uid))
                return BadRequest(new { erro = "Este usuário já tem cadastro de motorista." });
            m.UsuarioId = uid;
        }

        m.Nome = nome;
        m.Apelidos = string.IsNullOrWhiteSpace(dto.Apelidos) ? null : dto.Apelidos.Trim().ToUpperInvariant();
        m.Login = login;
        m.PorcentagemComissao = dto.PorcentagemComissao ?? 6;
        // Placa habitual (PlacaCavalo/PlacaCarreta) saiu do formulário (VIA-20): fica como está no banco
        m.Cpf = string.IsNullOrWhiteSpace(dto.Cpf) ? null : MotoristasFrotaController.FormatarCpf(dto.Cpf);
        m.Rg = MotoristasFrotaController.Texto(dto.Rg);
        m.RgOrgaoUf = MotoristasFrotaController.Texto(dto.RgOrgaoUf);
        m.DataNascimento = dto.DataNascimento;
        m.CidadeNascimento = MotoristasFrotaController.Texto(dto.CidadeNascimento);
        m.UfNascimento = MotoristasFrotaController.Texto(dto.UfNascimento);
        m.CnhRegistro = MotoristasFrotaController.Texto(dto.CnhRegistro);
        m.CnhCategoria = MotoristasFrotaController.Texto(dto.CnhCategoria);
        m.CnhEmissao = dto.CnhEmissao;
        m.CnhValidade = dto.CnhValidade;
        m.Pai = MotoristasFrotaController.Texto(dto.Pai);
        m.Mae = MotoristasFrotaController.Texto(dto.Mae);
        m.Ativo = dto.Ativo ?? true;
        m.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var erroAcesso = await auth.GarantirUsuarioMotoristaAsync(m);
        return Ok(new { id = m.Id, aviso = erroAcesso });
    }

    // Ativar/desativar: desativado não entra (o usuário dele fica inativo junto)
    [HttpPut("motoristas/{id:int}/ativo")]
    public async Task<IActionResult> AtivoMotorista(int id, [FromBody] AtivoDto dto)
    {
        var m = await db.MotoristasFrota.FindAsync(id);
        if (m == null) return NotFound(new { erro = "Motorista não encontrado." });
        m.Ativo = dto.Ativo;
        m.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var erro = await auth.GarantirUsuarioMotoristaAsync(m);
        return Ok(new { aviso = erro });
    }

    // O dono/escritório digita a senha nova do motorista
    [HttpPut("motoristas/{id:int}/senha")]
    public async Task<IActionResult> SenhaMotorista(int id, [FromBody] SenhaMotoristaDto dto)
    {
        var m = await db.MotoristasFrota.FindAsync(id);
        if (m == null) return NotFound(new { erro = "Motorista não encontrado." });
        var erro = await auth.DefinirSenhaMotoristaAsync(m, dto.Senha ?? "");
        return erro == null ? Ok(new { mensagem = "Senha trocada." }) : BadRequest(new { erro });
    }

    // Senha esquecida: volta para a padrão (login + 123)
    [HttpPost("motoristas/{id:int}/senha-padrao")]
    public async Task<IActionResult> SenhaPadrao(int id)
    {
        var m = await db.MotoristasFrota.FindAsync(id);
        if (m == null) return NotFound(new { erro = "Motorista não encontrado." });
        var erro = await auth.GarantirUsuarioMotoristaAsync(m, redefinirSenha: true);
        return erro == null ? Ok(new { mensagem = "Senha voltou para a padrão." }) : BadRequest(new { erro });
    }

    // ── Tabela de frete por cliente ──
    // O painel de preços (PrecosController) é a tela; aqui a viagem lê os fretes do dia.

    public record PrecoDto(string? Cliente, string? Apelidos, decimal ValorCaixa, decimal ValorProdutoD, decimal ValorProdutoB, int? Ordem, bool? Ativo,
        decimal? ValorProdutoE = null);

    /// <summary>Fretes de cada cliente de volumes (caixa, produtoE, produtoD, produtoB e produtoC), como a viagem grava.</summary>
    [HttpGet("precos")]
    public async Task<IActionResult> Precos()
    {
        var t = await precos.TabelaAsync();
        return Ok(t.Frete.Select(p =>
        {
            var e = t.EstoqueDe(p);
            return new
            {
                p.Id, p.Cliente, p.Apelidos, p.ValorCaixa, ValorProdutoE = p.ProdutoE, p.ValorProdutoD,
                ValorProdutoB = e?.FreteProdutoB ?? TabelaPrecos.PadraoProdutoB,
                ValorProdutoC = e?.FreteDaProdutoC ?? TabelaPrecos.PadraoProdutoC,
                p.EstoqueClienteId, p.Ordem, p.Ativo,
            };
        }));
    }

    [HttpPost("precos")]
    public Task<IActionResult> CriarPreco([FromBody] PrecoDto dto) => SalvarPreco(null, dto);

    [HttpPut("precos/{id:int}")]
    public Task<IActionResult> AtualizarPreco(int id, [FromBody] PrecoDto dto) => SalvarPreco(id, dto);

    private async Task<IActionResult> SalvarPreco(int? id, PrecoDto dto)
    {
        var cliente = (dto.Cliente ?? "").Trim().ToUpperInvariant();
        if (cliente.Length == 0) return BadRequest(new { erro = "Informe o cliente." });
        if (dto.ValorCaixa < 0 || dto.ValorProdutoD < 0 || dto.ValorProdutoB < 0) return BadRequest(new { erro = "Valor negativo." });
        PrecoFreteCliente p;
        if (id is { } pid) p = await db.PrecosFreteCliente.FindAsync(pid) ?? throw new InvalidOperationException();
        else
        {
            p = new PrecoFreteCliente { Ordem = (await db.PrecosFreteCliente.MaxAsync(x => (int?)x.Ordem) ?? 0) + 1 };
            db.PrecosFreteCliente.Add(p);
        }
        p.Cliente = cliente;
        p.Apelidos = string.IsNullOrWhiteSpace(dto.Apelidos) ? null : dto.Apelidos.Trim().ToUpperInvariant();
        p.ValorCaixa = dto.ValorCaixa;
        p.ValorProdutoE = dto.ValorProdutoE ?? p.ValorProdutoE ?? Math.Round(dto.ValorCaixa / 2m, 2);
        p.ValorProdutoD = dto.ValorProdutoD;
        p.ValorProdutoB = dto.ValorProdutoB;
        if (dto.Ordem is { } o) p.Ordem = o;
        p.Ativo = dto.Ativo ?? true;
        p.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { id = p.Id });
    }

    [HttpDelete("precos/{id:int}")]
    public async Task<IActionResult> ExcluirPreco(int id)
    {
        var p = await db.PrecosFreteCliente.FindAsync(id);
        if (p == null) return NotFound(new { erro = "Cliente não encontrado." });
        db.PrecosFreteCliente.Remove(p);
        await db.SaveChangesAsync();
        return Ok(new { mensagem = "Cliente tirado da tabela." });
    }

    private static async Task<byte[]> Bytes(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }
}

/// <summary>Retornos (a planilha "CONTROLE MENSAL DE RETORNOS" como tela).</summary>
[Authorize]
[ApiController]
[Route("api/retornos")]
public class RetornosController(ViagensCaminhaoService svc, ILogger<RetornosController> logger) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] int ano, [FromQuery] int mes)
    {
        if (mes is < 1 or > 12 || ano < 2000) return BadRequest(new { erro = "Mês inválido." });
        return Ok(await svc.RetornosDoMesAsync(ano, mes));
    }

    [HttpPost]
    public Task<IActionResult> Criar([FromBody] RetornoEntrada e) => Salvar(null, e);

    [HttpPut("{id:int}")]
    public Task<IActionResult> Atualizar(int id, [FromBody] RetornoEntrada e) => Salvar(id, e);

    private async Task<IActionResult> Salvar(int? id, RetornoEntrada e)
    {
        try { var r = await svc.SalvarRetornoAsync(id, e); return Ok(new { id = r.Id }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Excluir(int id)
    {
        try { await svc.ExcluirRetornoAsync(id); return Ok(new { mensagem = "Retorno apagado." }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    public record VinculoDto(int? ViagemId);

    [HttpPut("{id:int}/viagem")]
    public async Task<IActionResult> Vincular(int id, [FromBody] VinculoDto dto)
    {
        try { await svc.VincularRetornoAsync(id, dto.ViagemId); return Ok(new { mensagem = dto.ViagemId == null ? "Retorno solto da viagem." : "Retorno ligado à viagem." }); }
        catch (ErroViagem ex) { return BadRequest(new { erro = ex.Message }); }
    }

    [HttpPost("importar")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Importar([FromForm] List<IFormFile> arquivos)
    {
        if (arquivos.Count == 0) return BadRequest(new { erro = "Envie as planilhas de retornos (.xlsx ou .xls)." });
        var resultados = new List<object>();
        foreach (var f in arquivos.Where(f => !f.FileName.StartsWith("~$")))
        {
            try
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms);
                resultados.Add(await svc.ImportarRetornosAsync(ms.ToArray(), f.FileName));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ErroViagem or ExcelDataReader.Exceptions.HeaderException)
            { resultados.Add(new { arquivo = f.FileName, erro = ex.Message }); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao importar retornos {Arquivo}", f.FileName);
                resultados.Add(new { arquivo = f.FileName, erro = "Não consegui ler esta planilha." });
            }
        }
        return Ok(resultados);
    }

    [HttpGet("excel")]
    public async Task<IActionResult> Excel([FromQuery] int ano, [FromQuery] int mes)
    {
        if (mes is < 1 or > 12 || ano < 2000) return BadRequest(new { erro = "Mês inválido." });
        var (bytes, nome) = await svc.ExportarRetornosAsync(ano, mes);
        return File(bytes, Xlsx, nome);
    }
}
