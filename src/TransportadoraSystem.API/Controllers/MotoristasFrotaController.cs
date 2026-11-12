using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// CNH dos motoristas (VIA-20): lê a CNH digital (PDF, ou JPG/PNG exportado), mostra o que muda e só então grava,
/// criando o motorista ou atualizando os campos da CNH do que já existe (achado pelo CPF; sem
/// CPF, pelo nome sem acento). O cadastro em si (nome, login, comissão) segue em
/// ViagensCaminhaoController. Só escritório e dono: o perfil motorista recebe 403 no Program.cs.
/// </summary>
[Authorize]
[ApiController]
[Route("api/motoristas-frota")]
public class MotoristasFrotaController(AppDbContext db, AuthService auth, ILogger<MotoristasFrotaController> logger) : ControllerBase
{
    private const int MaxArquivo = 8 * 1024 * 1024;

    // Campos da CNH, na ordem da tela: (campo, rótulo, valor atual do cadastro, valor lido)
    private static readonly (string Campo, string Rotulo, Func<MotoristaFrota, string?> Atual, Func<CnhDados, string?> Novo)[] Campos =
    [
        ("cpf", "CPF", m => m.Cpf, d => d.Cpf),
        ("rg", "Documento de identidade", m => m.Rg, d => d.Rg),
        ("rgOrgaoUf", "Órgão emissor e estado", m => m.RgOrgaoUf, d => d.RgOrgaoUf),
        ("dataNascimento", "Data de nascimento", m => Data(m.DataNascimento), d => Data(d.DataNascimento)),
        ("cnhRegistro", "Número do registro", m => m.CnhRegistro, d => d.CnhRegistro),
        ("cnhCategoria", "Categoria", m => m.CnhCategoria, d => d.CnhCategoria),
        ("cnhEmissao", "Data de emissão", m => Data(m.CnhEmissao), d => Data(d.CnhEmissao)),
        ("cnhValidade", "Validade", m => Data(m.CnhValidade), d => Data(d.CnhValidade)),
        ("pai", "Pai", m => m.Pai, d => d.Pai),
        ("mae", "Mãe", m => m.Mae, d => d.Mae),
    ];

    public record DiferencaCnh(string Campo, string Rotulo, string? Atual, string? Novo);

    // ── Ajudas (usadas também pelo cadastro em ViagensCaminhaoController) ──

    /// <summary>Os motoristas sem o PDF da CNH (bytea): listas e comparações não carregam o arquivo.</summary>
    public static IQueryable<MotoristaFrota> SemPdf(IQueryable<MotoristaFrota> q) =>
        q.Select(m => new MotoristaFrota
        {
            Id = m.Id, Nome = m.Nome, Apelidos = m.Apelidos, Login = m.Login, PorcentagemComissao = m.PorcentagemComissao,
            PlacaCavalo = m.PlacaCavalo, PlacaCarreta = m.PlacaCarreta, Ativo = m.Ativo, UsuarioId = m.UsuarioId,
            Cpf = m.Cpf, Rg = m.Rg, RgOrgaoUf = m.RgOrgaoUf, DataNascimento = m.DataNascimento,
            CidadeNascimento = m.CidadeNascimento, UfNascimento = m.UfNascimento, CnhRegistro = m.CnhRegistro,
            CnhCategoria = m.CnhCategoria, CnhEmissao = m.CnhEmissao, CnhValidade = m.CnhValidade, Pai = m.Pai, Mae = m.Mae, Email = m.Email,
            CnhPdfNome = m.CnhPdfNome, CnhAtualizadaEm = m.CnhAtualizadaEm, CriadoEm = m.CriadoEm, AtualizadoEm = m.AtualizadoEm,
        });

    public static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();

    private static string Digitos(string? s) => Regex.Replace(s ?? "", @"\D", "");

    /// <summary>"12345678901" → "123.456.789-01"; nulo se não tiver 11 números.</summary>
    public static string? FormatarCpf(string? s)
    {
        var d = Digitos(s);
        return d.Length == 11 ? $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}" : null;
    }

    private static string? Data(DateOnly? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static async Task<byte[]> Bytes(IFormFile f)
    {
        using var ms = new MemoryStream();
        await f.CopyToAsync(ms);
        return ms.ToArray();
    }

    // PDF, JPG ou PNG pelo conteúdo (há arquivo sem extensão); nulo = recusa
    private static string? TipoAceito(byte[] b) => CnhLeitor.TipoArquivo(b);

    private static string Extensao(string? tipo) => tipo switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".pdf" };

    // ── Ler ──

    [HttpPost("cnh/ler")]
    public async Task<IActionResult> Ler(IFormFile? arquivo)
    {
        if (arquivo == null || arquivo.Length == 0) return BadRequest(new { erro = "Escolha o arquivo da CNH." });
        if (arquivo.Length > MaxArquivo) return BadRequest(new { erro = "O arquivo é grande demais (limite de 8 MB)." });
        var bytes = await Bytes(arquivo);
        if (TipoAceito(bytes) == null) return BadRequest(new { erro = "Este arquivo não é PDF, JPG nem PNG." });

        CnhLeitura leitura;
        try { leitura = CnhLeitor.Ler(bytes); }
        catch (ErroCnh ex) { return BadRequest(new { erro = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao ler a CNH");
            return BadRequest(new { erro = "Não consegui ler este arquivo. Preencha à mão." });
        }

        var d = leitura.Dados;
        var motoristas = await SemPdf(db.MotoristasFrota.AsNoTracking()).ToListAsync();
        MotoristaFrota? achado = null;
        string? achadoPor = null;
        if (Digitos(d.Cpf) is { Length: 11 } cpf)
        {
            achado = motoristas.FirstOrDefault(m => Digitos(m.Cpf) == cpf);
            if (achado != null) achadoPor = "cpf";
        }
        if (achado == null && Normalizar(d.Nome) is { Length: > 0 } nome)
        {
            achado = motoristas.FirstOrDefault(m => Normalizar(m.Nome) == nome);
            if (achado != null) achadoPor = "nome";
        }
        // Cadastro só com o primeiro nome ou apelido ("RENATO"): vale se for um só e sem outro CPF
        if (achado == null)
        {
            var cpfCnh = Digitos(d.Cpf);
            var parecidos = motoristas.Where(m => (Digitos(m.Cpf) is not { Length: 11 } c || c == cpfCnh)
                && CnhLeitor.ComecaComONome(d.Nome, m.Nome, m.Apelidos)).ToList();
            if (parecidos.Count == 1) { achado = parecidos[0]; achadoPor = "nome"; }
        }

        var diferencas = new List<DiferencaCnh>();
        // O nome completo da CNH substitui o do cadastro (ao gravar, o antigo vira apelido)
        if (achado != null && Texto(d.Nome) is { } nomeCnh && Normalizar(nomeCnh) != Normalizar(achado.Nome))
            diferencas.Add(new DiferencaCnh("nome", "Nome", achado.Nome, nomeCnh));
        var iguais = 0;
        foreach (var (campo, rotulo, atual, novo) in Campos)
        {
            var n = novo(d);
            if (n == null) continue;
            var a = achado == null ? null : atual(achado);
            if (a != null && string.Equals(a.Trim(), n.Trim(), StringComparison.OrdinalIgnoreCase)) iguais++;
            else diferencas.Add(new DiferencaCnh(campo, rotulo, a, n));
        }

        return Ok(new
        {
            dados = d,
            naoAchou = leitura.NaoAchou,
            tipo = leitura.Tipo,
            reconhecida = leitura.Reconhecida,
            motorista = achado == null ? null : new { achado.Id, achado.Nome, achado.Ativo, TemCnhPdf = achado.CnhPdfNome != null },
            achadoPor,
            diferencas,
            iguais,
        });
    }

    // ── Aplicar ──

    public class AplicarCnhForm
    {
        public int? MotoristaId { get; set; }
        /// <summary>Os dados lidos (JSON), como a leitura devolveu.</summary>
        public string? Dados { get; set; }
        /// <summary>Só para motorista novo: o usuário para entrar (padrão: primeiro nome, ou com sobrenome se já existir).</summary>
        public string? Login { get; set; }
        public IFormFile? Arquivo { get; set; }
    }

    [HttpPost("cnh/aplicar")]
    public async Task<IActionResult> Aplicar([FromForm] AplicarCnhForm f)
    {
        if (f.Arquivo == null || f.Arquivo.Length == 0) return BadRequest(new { erro = "Falta o arquivo da CNH." });
        if (f.Arquivo.Length > MaxArquivo) return BadRequest(new { erro = "O arquivo é grande demais (limite de 8 MB)." });
        var bytes = await Bytes(f.Arquivo);
        var tipoArquivo = TipoAceito(bytes);
        if (tipoArquivo == null) return BadRequest(new { erro = "Este arquivo não é PDF, JPG nem PNG." });

        CnhDados? d;
        try { d = JsonSerializer.Deserialize<CnhDados>(f.Dados ?? "", new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { d = null; }
        if (d == null) return BadRequest(new { erro = "Não entendi os dados da CNH. Leia o arquivo de novo." });
        if (d.Cpf != null && FormatarCpf(d.Cpf) == null) return BadRequest(new { erro = "CPF inválido: são 11 números." });

        MotoristaFrota m;
        var novo = f.MotoristaId == null;
        if (f.MotoristaId is { } mid)
        {
            var achado = await db.MotoristasFrota.FindAsync(mid);
            if (achado == null) return NotFound(new { erro = "Motorista não encontrado." });
            // Motorista escolhido na tela com outro CPF: não grava a CNH de uma pessoa no cadastro de outra
            if (Digitos(d.Cpf) is { Length: 11 } cpfLido && Digitos(achado.Cpf) is { Length: 11 } cpfCad && cpfLido != cpfCad)
                return BadRequest(new { erro = $"O CPF desta CNH não é o de {achado.Nome}. Escolha o motorista certo." });
            m = achado;
        }
        else
        {
            var nome = Texto(d.Nome);
            if (nome == null) return BadRequest(new { erro = "A CNH não trouxe o nome. Cadastre o motorista à mão e importe de novo." });
            var existentes = await db.MotoristasFrota.AsNoTracking().Select(x => x.Nome).ToListAsync();
            if (existentes.Any(x => Normalizar(x) == Normalizar(nome)))
                return BadRequest(new { erro = $"Já existe um motorista chamado {nome}. Importe a CNH escolhendo ele." });
            var login = await LoginParaAsync(nome, f.Login);
            if (login.erro != null) return BadRequest(new { erro = login.erro });
            m = new MotoristaFrota { Nome = nome, Login = login.login!, PorcentagemComissao = 6, Ativo = true };
            db.MotoristasFrota.Add(m);
        }

        // Nome do cadastro diferente do da CNH (ex.: só "RENATO"): fica o da CNH e o antigo vira
        // apelido, para a planilha continuar achando o motorista
        var nomeMudou = false;
        if (!novo && Texto(d.Nome) is { } nomeCnh && Normalizar(nomeCnh) != Normalizar(m.Nome))
        {
            var apelidos = (m.Apelidos ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (!apelidos.Any(a => Normalizar(a) == Normalizar(m.Nome))) apelidos.Insert(0, m.Nome);
            m.Apelidos = string.Join(",", apelidos);
            m.Nome = nomeCnh;
            nomeMudou = true;
        }

        // Só o que a CNH trouxe: o que não foi lido não apaga o que já está no cadastro
        if (FormatarCpf(d.Cpf) is { } cpf) m.Cpf = cpf;
        if (Texto(d.Rg) is { } rg) m.Rg = rg;
        if (Texto(d.RgOrgaoUf) is { } ou) m.RgOrgaoUf = ou;
        if (d.DataNascimento != null) m.DataNascimento = d.DataNascimento;
        if (Texto(d.CnhRegistro) is { } reg) m.CnhRegistro = reg;
        if (Texto(d.CnhCategoria) is { } cat) m.CnhCategoria = cat;
        if (d.CnhEmissao != null) m.CnhEmissao = d.CnhEmissao;
        if (d.CnhValidade != null) m.CnhValidade = d.CnhValidade;
        if (Texto(d.Pai) is { } pai) m.Pai = pai;
        if (Texto(d.Mae) is { } mae) m.Mae = mae;
        m.CnhPdfNome = Path.GetFileName(f.Arquivo.FileName) is { Length: > 0 } fn ? fn : "cnh" + Extensao(tipoArquivo);
        m.CnhAtualizadaEm = DateTime.UtcNow;
        m.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        // O arquivo vai depois do salvar: motorista novo só tem Id aqui
        var pdf = await db.MotoristasCnhPdf.FindAsync(m.Id);
        if (pdf == null) db.MotoristasCnhPdf.Add(new MotoristaCnhPdf { MotoristaId = m.Id, Pdf = bytes, Tipo = tipoArquivo });
        else { pdf.Pdf = bytes; pdf.Tipo = tipoArquivo; }
        await db.SaveChangesAsync();

        // Motorista novo já ganha o usuário de acesso, como no cadastro (senha padrão: usuário + 123);
        // nome trocado vai também para o usuário (como na edição do cadastro)
        var aviso = novo || nomeMudou ? await auth.GarantirUsuarioMotoristaAsync(m) : null;
        return Ok(new { id = m.Id, criado = novo, login = novo ? m.Login : null, aviso });
    }

    // Primeiro nome sem acento; se já é de outro, junta os sobrenomes até ficar livre (USU-03)
    private async Task<(string? login, string? erro)> LoginParaAsync(string nome, string? pedido)
    {
        async Task<bool> Livre(string l) =>
            !await db.MotoristasFrota.AnyAsync(x => x.Login == l) && !await db.Usuarios.AnyAsync(u => u.Email.ToLower() == l);

        if (!string.IsNullOrWhiteSpace(pedido))
        {
            var p = pedido.Trim().ToLowerInvariant();
            if (!AuthService.LoginMotoristaValido(p)) return (null, "Usuário: só letras minúsculas e números, sem espaço nem acento.");
            return await Livre(p) ? (p, null) : (null, "Este usuário já existe. Escolha outro.");
        }
        var partes = Normalizar(nome).ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, "[^a-z0-9]", "")).Where(x => x.Length > 0).ToList();
        if (partes.Count == 0) return (null, "Não consegui montar o usuário a partir do nome.");
        var tentativa = "";
        foreach (var parte in partes)
        {
            tentativa += parte;
            if (AuthService.LoginMotoristaValido(tentativa) && await Livre(tentativa)) return (tentativa, null);
        }
        for (var n = 2; n < 100; n++)
            if (AuthService.LoginMotoristaValido(tentativa + n) && await Livre(tentativa + n)) return (tentativa + n, null);
        return (null, "Não consegui achar um usuário livre. Informe um.");
    }

    // ── Arquivo guardado (PDF, JPG ou PNG) ──

    [HttpGet("{id:int}/cnh")]
    public async Task<IActionResult> Pdf(int id)
    {
        var pdf = await db.MotoristasCnhPdf.AsNoTracking().FirstOrDefaultAsync(x => x.MotoristaId == id);
        if (pdf == null) return NotFound(new { erro = "Este motorista não tem o arquivo da CNH guardado." });
        var nome = await db.MotoristasFrota.AsNoTracking().Where(x => x.Id == id).Select(x => x.CnhPdfNome).FirstOrDefaultAsync();
        // Tipo pelo conteúdo, não pelo que foi gravado: arquivo antigo (Tipo nulo) é PDF
        var tipo = CnhLeitor.TipoArquivo(pdf.Pdf) ?? "application/octet-stream";
        // Arquivo sem extensão (ex.: foto salva do WhatsApp) baixa com a extensão do tipo
        var nomeArq = nome is { Length: > 0 } n ? (Path.HasExtension(n) ? n : n + Extensao(tipo)) : "cnh" + Extensao(tipo);
        return File(pdf.Pdf, tipo, nomeArq);
    }
}
