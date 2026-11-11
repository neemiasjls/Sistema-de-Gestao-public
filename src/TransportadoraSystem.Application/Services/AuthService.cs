using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using TransportadoraSystem.Application.Services.Acesso;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

public class LoginResult
{
    public string?  Token   { get; set; }
    public UsuarioDto? Usuario { get; set; }
    public string?  Erro    { get; set; }
    public bool     Sucesso => Erro == null;
}

public class UsuarioDto
{
    public int    Id          { get; set; }
    public string Nome        { get; set; } = "";
    public string Email       { get; set; } = "";
    public bool   Ativo       { get; set; }
    public bool   Dono        { get; set; }
    public bool   TrocarSenha { get; set; }
    // "" = escritório; "motorista" = só o painel do motorista
    public string Perfil      { get; set; } = "";
    // Áreas que a pessoa tem de fato (AreasAcesso.Efetivas), na ordem do catálogo.
    // Acesso total e administrador = ["*"] (mais "painel-motorista" se o login tem
    // cadastro de motorista ativo, porque o "*" não dá o painel).
    public List<string> Areas { get; set; } = [];
}

// Resultado de criar usuário ou gerar senha nova. Quando o e-mail não sai, a
// senha volta aqui para o dono ver uma única vez e mandar por outro caminho —
// melhor que um usuário criado sem ninguém saber a senha.
public class SenhaProvisoriaResult
{
    public string? Erro          { get; set; }
    public bool    EmailEnviado  { get; set; }
    public string? AvisoEmail    { get; set; }
    public string? SenhaProvisoria { get; set; }
}

public class AuthService(AppDbContext db, IConfiguration config, EmailService email, PortalProdutosAHistoricoService historico)
{
    public const string ClaimDono        = "dono";
    public const string ClaimTrocarSenha = "trocar_senha";
    public const string ClaimPerfil      = "perfil";
    public const string PerfilMotorista  = "motorista";
    public const string PerfilProdutosA    = "produtosA";   // portal das produtosA, cadastro pelo QR (EST-09)

    private readonly PasswordHasher<Usuario> _hasher = new();

    // ── Login por e-mail ───────────────────────────────────────────
    public async Task<LoginResult> LoginAsync(string login, string senha)
    {
        var alvo = login.Trim().ToLower();
        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == alvo && u.Ativo);

        // Motorista entra com o usuário (joseferraz), sem @
        var erro = alvo.Contains('@') ? "E-mail ou senha incorretos." : "Usuário ou senha incorretos.";
        if (usuario == null)
            return new LoginResult { Erro = erro };

        var result = _hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha);
        if (result == PasswordVerificationResult.Failed)
            return new LoginResult { Erro = erro };

        return new LoginResult { Token = GerarToken(usuario), Usuario = await ToDtoAsync(usuario) };
    }

    public async Task<UsuarioDto?> ObterAsync(int id)
    {
        var u = await db.Usuarios.FindAsync(id);
        return u == null ? null : await ToDtoAsync(u);
    }

    public async Task<bool> EhDonoAsync(int id) =>
        await db.Usuarios.AnyAsync(u => u.Id == id && u.Dono && u.Ativo);

    // ── Gerar token JWT ────────────────────────────────────────────
    private string GerarToken(Usuario usuario)
    {
        var secret = config["JWT:Secret"]
            ?? throw new InvalidOperationException("JWT:Secret não configurado.");

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(ClaimTypes.Name,               usuario.Nome),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };
        if (usuario.Dono)        claims.Add(new(ClaimDono, "true"));
        if (usuario.TrocarSenha) claims.Add(new(ClaimTrocarSenha, "true"));
        if (!string.IsNullOrEmpty(usuario.Perfil)) claims.Add(new(ClaimPerfil, usuario.Perfil));

        var token = new JwtSecurityToken(
            claims:             claims,
            // Portal das produtosA no celular: 30 dias (a API confere a cada pedido se a conta segue ativa)
            expires:            usuario.Perfil == PerfilProdutosA ? DateTime.UtcNow.AddDays(30) : DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── Senha ──────────────────────────────────────────────────────

    // 16 caracteres de quatro grupos, sorteados com gerador criptográfico.
    // Sem 0/O, 1/l/I: a pessoa pode ter que digitar olhando o celular.
    public static string GerarSenhaForte()
    {
        const string maiusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string minusculas = "abcdefghijkmnopqrstuvwxyz";
        const string numeros    = "23456789";
        const string simbolos   = "!@#$%&*?";
        var todos = maiusculas + minusculas + numeros + simbolos;

        var chars = new List<char>
        {
            maiusculas[RandomNumberGenerator.GetInt32(maiusculas.Length)],
            minusculas[RandomNumberGenerator.GetInt32(minusculas.Length)],
            numeros[RandomNumberGenerator.GetInt32(numeros.Length)],
            simbolos[RandomNumberGenerator.GetInt32(simbolos.Length)],
        };
        while (chars.Count < 16)
            chars.Add(todos[RandomNumberGenerator.GetInt32(todos.Length)]);

        // Embaralha para os quatro garantidos não ficarem sempre no começo
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());
    }

    // Regra da senha que a pessoa cria: 8 ou mais, com letra e número.
    public static string? ValidarSenhaNova(string senha)
    {
        if (senha.Length < 8)                return "A senha precisa ter pelo menos 8 caracteres.";
        if (!senha.Any(char.IsLetter))       return "A senha precisa ter pelo menos uma letra.";
        if (!senha.Any(char.IsDigit))        return "A senha precisa ter pelo menos um número.";
        return null;
    }

    public static bool EmailValido(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Contains(' ')) return false;
        try { return new MailAddress(email).Address == email && email.Contains('.'); }
        catch { return false; }
    }

    // Troca a própria senha. Serve para o primeiro acesso e para depois.
    public async Task<LoginResult> TrocarSenhaAsync(int id, string senhaAtual, string novaSenha)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario == null || !usuario.Ativo) return new LoginResult { Erro = "Usuário não encontrado." };

        if (_hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senhaAtual) == PasswordVerificationResult.Failed)
            return new LoginResult { Erro = "A senha atual não confere." };

        if (ValidarSenhaNova(novaSenha) is { } problema) return new LoginResult { Erro = problema };
        if (novaSenha == senhaAtual) return new LoginResult { Erro = "A senha nova precisa ser diferente da atual." };

        usuario.SenhaHash   = _hasher.HashPassword(usuario, novaSenha);
        usuario.TrocarSenha = false;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Token novo, já sem a marca de senha provisória
        return new LoginResult { Token = GerarToken(usuario), Usuario = await ToDtoAsync(usuario) };
    }

    // A pessoa muda o próprio nome e e-mail. O e-mail é o login, então pede a
    // senha atual — sessão esquecida aberta não vira troca de dono da conta.
    public async Task<LoginResult> AtualizarPerfilAsync(int id, string nome, string emailInformado, string senhaAtual)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario == null || !usuario.Ativo) return new LoginResult { Erro = "Usuário não encontrado." };

        if (_hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senhaAtual) == PasswordVerificationResult.Failed)
            return new LoginResult { Erro = "A senha atual não confere." };

        var emailNorm = emailInformado.Trim().ToLower();
        nome = nome.Trim();
        if (nome.Length == 0)        return new LoginResult { Erro = "Informe o nome." };
        if (!EmailValido(emailNorm)) return new LoginResult { Erro = "Esse e-mail não parece válido." };
        if (await db.Usuarios.AnyAsync(u => u.Id != id && u.Email.ToLower() == emailNorm))
            return new LoginResult { Erro = "Este e-mail já é de outro usuário." };

        usuario.Nome  = nome;
        usuario.Email = emailNorm;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Token novo: o antigo ainda carrega o e-mail velho
        return new LoginResult { Token = GerarToken(usuario), Usuario = await ToDtoAsync(usuario) };
    }

    // ── Usuários (só o dono chega aqui) ────────────────────────────
    // Os motoristas têm o próprio cadastro (Viagens dos caminhões) e as contas do portal das
    // produtosA têm seção própria: não entram aqui.
    public async Task<List<UsuarioDto>> ListarAsync() =>
        await db.Usuarios
            .Where(u => u.Perfil != PerfilMotorista && u.Perfil != PerfilProdutosA)
            .OrderByDescending(u => u.Dono).ThenBy(u => u.Nome)
            .Select(u => new UsuarioDto
            {
                Id = u.Id, Nome = u.Nome, Email = u.Email, Ativo = u.Ativo,
                Dono = u.Dono, TrocarSenha = u.TrocarSenha,
            })
            .ToListAsync();

    // Usuário novo de escritório nasce só com as áreas marcadas (USU-11); sem nenhuma = sem acesso.
    public async Task<SenhaProvisoriaResult> CriarAsync(string nome, string emailInformado, IEnumerable<string>? areas = null)
    {
        var emailNorm = emailInformado.Trim().ToLower();
        nome = nome.Trim();

        if (nome.Length == 0)          return new() { Erro = "Informe o nome." };
        if (!EmailValido(emailNorm))   return new() { Erro = "Esse e-mail não parece válido." };
        var permissoes = AreasAcesso.Validar(areas);
        if (!permissoes.Ok)            return new() { Erro = permissoes.Erro };
        if (await db.Usuarios.AnyAsync(u => u.Email.ToLower() == emailNorm))
            return new() { Erro = "Este e-mail já está cadastrado." };

        var senha = GerarSenhaForte();
        var usuario = new Usuario
        {
            Nome = nome, Email = emailNorm, Ativo = true, TrocarSenha = true, Permissoes = permissoes.Valor,
        };
        usuario.SenhaHash = _hasher.HashPassword(usuario, senha);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        return await EntregarSenhaAsync(usuario, senha, primeiroAcesso: true);
    }

    // Senha esquecida, ou usuário antigo que ganhou e-mail agora: sorteia outra
    // provisória e manda de novo. A senha anterior deixa de valer.
    public async Task<SenhaProvisoriaResult> GerarNovaSenhaAsync(int id)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario == null) return new() { Erro = "Usuário não encontrado." };
        if (!EmailValido(usuario.Email))
            return new() { Erro = "Cadastre um e-mail válido para este usuário antes de mandar a senha." };

        var senha = GerarSenhaForte();
        usuario.SenhaHash   = _hasher.HashPassword(usuario, senha);
        usuario.TrocarSenha = true;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return await EntregarSenhaAsync(usuario, senha, primeiroAcesso: false);
    }

    private async Task<SenhaProvisoriaResult> EntregarSenhaAsync(Usuario usuario, string senha, bool primeiroAcesso)
    {
        var falha = await email.EnviarSenhaProvisoriaAsync(usuario.Nome, usuario.Email, senha, primeiroAcesso);
        return falha == null
            ? new() { EmailEnviado = true }
            : new() { EmailEnviado = false, AvisoEmail = falha, SenhaProvisoria = senha };
    }

    public async Task<string?> EditarAsync(int id, string nome, string emailInformado)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario == null) return "Usuário não encontrado.";

        var emailNorm = emailInformado.Trim().ToLower();
        nome = nome.Trim();
        if (nome.Length == 0)        return "Informe o nome.";
        if (!EmailValido(emailNorm)) return "Esse e-mail não parece válido.";
        if (await db.Usuarios.AnyAsync(u => u.Id != id && u.Email.ToLower() == emailNorm))
            return "Este e-mail já é de outro usuário.";

        usuario.Nome  = nome;
        usuario.Email = emailNorm;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    public async Task<string?> DefinirAtivoAsync(int id, bool ativo)
    {
        var usuario = await db.Usuarios.FindAsync(id);
        if (usuario == null) return "Usuário não encontrado.";
        if (usuario.Dono && !ativo) return "O dono não pode ser desativado.";

        usuario.Ativo = ativo;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    // ── Seed do usuário de acesso ──────────────────────────────────
    // Usuário e senha vêm da config (Seed:AdminUser / Seed:AdminPassword) —
    // nunca ficam escritos no código, que vai pro GitHub.
    // ── Portal das produtosA: cadastro pelo QR e senha esquecida (EST-09, USU-05) ──
    // Exceção ao USU-01: a própria pessoa cria a conta, com o código do QR do barracão.
    // Sem liberação: o login já funciona. Perfil e dono são fixos aqui, nunca vêm do corpo.

    public record CadastroProdutosAResult(int Status, string? Erro, LoginResult? Login);

    public const string ErroQrInvalido = "QR inválido. Peça o QR novo ao Admin.";

    /// <summary>Confere o código do QR com o gravado, em tempo constante. Sem config ou vazio: inválido.</summary>
    public async Task<bool> CodigoProdutosAValidoAsync(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return false;
        var gravado = await db.PortalProdutosAConfig.OrderBy(c => c.Id).Select(c => c.Codigo).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(gravado)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(codigo.Trim()), Encoding.UTF8.GetBytes(gravado.Trim()));
    }

    public async Task<CadastroProdutosAResult> CadastrarProdutosAAsync(string? codigo, string? nome, string? emailInformado,
        string? senha, string? ip)
    {
        if (!await CodigoProdutosAValidoAsync(codigo)) return new(403, ErroQrInvalido, null);

        nome = (nome ?? "").Trim();
        var emailNorm = (emailInformado ?? "").Trim().ToLower();
        senha ??= "";

        if (nome.Length < 2 || nome.Length > 80) return new(400, "Informe o seu nome (de 2 a 80 letras).", null);
        if (emailNorm.Length > 254 || !EmailValido(emailNorm)) return new(400, "Esse e-mail não parece válido.", null);
        if (ValidarSenhaNova(senha) is { } erroSenha) return new(400, erroSenha, null);
        if (await db.Usuarios.AnyAsync(u => u.Email.ToLower() == emailNorm))
            return new(400, "Não deu para criar a conta com este e-mail. Fale com o Admin.", null);

        var usuario = new Usuario
        {
            Nome = nome, Email = emailNorm, Ativo = true, Dono = false, TrocarSenha = false, Perfil = PerfilProdutosA,
            Permissoes = AreasAcesso.PortalProdutosA,
        };
        usuario.SenhaHash = _hasher.HashPassword(usuario, senha);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();

        await historico.RegistrarAsync(usuario.Id, usuario.Nome, PortalProdutosAHistoricoService.UsuarioCadastrado,
            $"Cadastro pelo QR: {usuario.Nome} ({usuario.Email})",
            depois: new { usuario.Nome, usuario.Email }, ip: ip);

        return new(201, null, new LoginResult { Token = GerarToken(usuario), Usuario = await ToDtoAsync(usuario) });
    }

    /// <summary>
    /// "Esqueci a senha" do portal: só conta ativa de perfil produtosA. Sorteia a provisória e manda
    /// pelo mesmo e-mail do GerarNovaSenhaAsync. Quem chama responde sempre a mesma coisa, achando
    /// ou não a conta (não revela quem está cadastrado).
    /// </summary>
    public async Task EsqueciSenhaProdutosAAsync(string? emailInformado, string? ip)
    {
        var emailNorm = (emailInformado ?? "").Trim().ToLower();
        if (emailNorm.Length == 0 || emailNorm.Length > 254) return;

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
            u.Email.ToLower() == emailNorm && u.Ativo && u.Perfil == PerfilProdutosA);
        if (usuario == null || !EmailValido(usuario.Email)) return;

        // Manda antes de gravar: se o e-mail falhar, a senha atual continua valendo (a pessoa não fica
        // trancada do lado de fora). A falha fica no log (o EmailService registra) e no histórico.
        var senha = GerarSenhaForte();
        var falha = await email.EnviarSenhaProvisoriaAsync(usuario.Nome, usuario.Email, senha, primeiroAcesso: false);
        if (falha == null)
        {
            usuario.SenhaHash    = _hasher.HashPassword(usuario, senha);
            usuario.TrocarSenha  = true;
            usuario.AtualizadoEm = DateTime.UtcNow;
        }
        historico.Adicionar(usuario.Id, usuario.Nome, PortalProdutosAHistoricoService.SenhaEsquecida,
            falha == null
                ? $"Senha esquecida: provisória mandada para {usuario.Email}"
                : $"Senha esquecida: o e-mail para {usuario.Email} não saiu ({falha}); a senha não mudou",
            ip: ip);
        await db.SaveChangesAsync();
    }

    public async Task SeedAdminAsync()
    {
        var login = config["Seed:AdminUser"];
        var senha = config["Seed:AdminPassword"];

        // Só cria com o banco vazio. Antes procurava pelo login do seed: quando o
        // dono trocou o login pelo e-mail real, a subida seguinte recriou o
        // usuário antigo como dono, com a senha da configuração.
        if (!string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(senha))
        {
            var existe = await db.Usuarios.AnyAsync(u => u.Perfil != PerfilMotorista && u.Perfil != PerfilProdutosA);
            if (!existe)
            {
                var novo = new Usuario
                {
                    Nome = login, Email = login.ToLower(), Ativo = true, Dono = true, Permissoes = AreasAcesso.AcessoTotal,
                };
                novo.SenhaHash = _hasher.HashPassword(novo, senha);
                db.Usuarios.Add(novo);
                await db.SaveChangesAsync();
            }
        }

        // Sempre existe um dono. Se ninguém estiver marcado, vira dono o
        // usuário do seed ou, sem ele, o mais antigo ainda ativo.
        if (await db.Usuarios.AnyAsync(u => u.Dono)) return;

        var dono = (login == null ? null
                       : await db.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == login.ToLower()))
                   ?? await db.Usuarios.Where(u => u.Ativo && u.Perfil != PerfilMotorista && u.Perfil != PerfilProdutosA).OrderBy(u => u.Id).FirstOrDefaultAsync();
        if (dono == null) return;

        dono.Dono = true;
        await db.SaveChangesAsync();
    }

    // Serve o login e o /api/auth/me: leva as áreas efetivas (USU-11).
    private async Task<UsuarioDto> ToDtoAsync(Usuario u)
    {
        var temCadastro = string.IsNullOrEmpty(u.Perfil) && u.Id != 0
            && await db.MotoristasFrota.AnyAsync(m => m.UsuarioId == u.Id && m.Ativo);
        return new()
        {
            Id = u.Id, Nome = u.Nome, Email = u.Email, Ativo = u.Ativo,
            Dono = u.Dono, TrocarSenha = u.TrocarSenha, Perfil = u.Perfil,
            Areas = AreasDoDto(u.Perfil, u.Dono, u.Permissoes, temCadastro),
        };
    }

    /// <summary>Lista de áreas para o site: ["*"] para acesso total e administrador; senão as chaves, na ordem do catálogo.</summary>
    public static List<string> AreasDoDto(string? perfil, bool dono, string? permissoes, bool temCadastroMotoristaAtivo)
    {
        var efetivas = AreasAcesso.Efetivas(perfil, dono, permissoes, temCadastroMotoristaAtivo);
        var total = string.IsNullOrEmpty(perfil)
            && (dono || (permissoes ?? "").Split(',', StringSplitOptions.TrimEntries).Contains(AreasAcesso.AcessoTotal));
        if (total)
        {
            List<string> r = [AreasAcesso.AcessoTotal];
            if (efetivas.Contains(AreasAcesso.PainelMotorista)) r.Add(AreasAcesso.PainelMotorista);
            return r;
        }
        return AreasAcesso.Areas.Select(a => a.Chave).Where(efetivas.Contains).ToList();
    }

    // ── Acesso dos motoristas ──────────────────────────────────────
    // Senha padrão = login + "123", fixa e sem troca obrigatória (decisão do
    // Admin, 06/10/2026). Ela nasce da regra, não fica escrita em lugar nenhum.
    public static string SenhaPadraoMotorista(string login) => login.Trim().ToLowerInvariant() + "123";

    public static bool LoginMotoristaValido(string login) =>
        login.Length >= 3 && login.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');

    /// <summary>
    /// Garante o usuário de acesso (perfil motorista) de um motorista. Cria com a
    /// senha padrão se ainda não tem; com redefinirSenha volta para a padrão.
    /// Nunca mexe num usuário do escritório: se o login já é de outro (um dono que
    /// entrasse com "bento", por exemplo), devolve o erro e não cria nada.
    /// Com soLigar (subida da API): usuário de motorista que já existia sem cadastro
    /// (criado à mão) só é ligado, sem mudar nome, senha nem situação.
    /// </summary>
    public async Task<string?> GarantirUsuarioMotoristaAsync(MotoristaFrota m, bool redefinirSenha = false, bool soLigar = false)
    {
        // Pessoa com um login só (USU-11): o cadastro aponta para o login do escritório. Esse login
        // não é tocado (nome, e-mail, senha, situação) e a ligação fica; nada de login de motorista.
        if (await LoginEscritorioDoCadastroAsync(m) is { } escritorio)
        {
            if (await db.MotoristasFrota.AnyAsync(x => x.UsuarioId == escritorio.Id && x.Id != m.Id))
                return $"O login de \"{escritorio.Nome}\" já está ligado a outro cadastro de motorista.";
            return redefinirSenha ? MensagemLoginDoSistema : null;
        }

        var login = m.Login.Trim().ToLowerInvariant();
        if (!LoginMotoristaValido(login)) return $"Login \"{m.Login}\" inválido: só letras minúsculas e números, sem espaço.";

        Usuario? usuario = m.UsuarioId is { } uid ? await db.Usuarios.FindAsync(uid) : null;
        if (usuario != null && usuario.Perfil != PerfilMotorista) usuario = null;

        var outro = await db.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == login && (usuario == null || u.Id != usuario.Id));
        if (outro != null)
        {
            if (outro.Perfil != PerfilMotorista)
                return $"O login \"{login}\" já é de um usuário do escritório. Escolha outro login para o motorista.";
            // Usuário de motorista que já existia com esse login (cadastro refeito ou
            // criado à mão): reaproveita, se não for de outro cadastro
            if (usuario != null || await db.MotoristasFrota.AnyAsync(x => x.UsuarioId == outro.Id && x.Id != m.Id))
                return $"O login \"{login}\" já é de outro motorista.";
            usuario = outro;
            if (soLigar)
            {
                m.UsuarioId = usuario.Id;
                await db.SaveChangesAsync();
                return null;
            }
        }

        if (usuario == null)
        {
            usuario = new Usuario
            {
                Nome = m.Nome, Email = login, Ativo = m.Ativo, Perfil = PerfilMotorista,
                Permissoes = AreasAcesso.PainelMotorista,
            };
            usuario.SenhaHash = _hasher.HashPassword(usuario, SenhaPadraoMotorista(login));
            db.Usuarios.Add(usuario);
        }
        else
        {
            var loginMudou = usuario.Email != login;
            usuario.Nome = m.Nome;
            usuario.Email = login;
            usuario.Ativo = m.Ativo;
            usuario.TrocarSenha = false;
            if (redefinirSenha || loginMudou) usuario.SenhaHash = _hasher.HashPassword(usuario, SenhaPadraoMotorista(login));
            usuario.AtualizadoEm = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        if (m.UsuarioId != usuario.Id)
        {
            m.UsuarioId = usuario.Id;
            await db.SaveChangesAsync();
        }
        return null;
    }

    public const string MensagemLoginDoSistema = "Esta pessoa entra com o login do sistema; a senha se troca no perfil dela.";

    /// <summary>Login de escritório (perfil vazio) para o qual o cadastro aponta, ou null.</summary>
    public async Task<Usuario?> LoginEscritorioDoCadastroAsync(MotoristaFrota m)
    {
        if (m.UsuarioId is not { } uid) return null;
        var u = await db.Usuarios.FindAsync(uid);
        return u != null && string.IsNullOrEmpty(u.Perfil) ? u : null;
    }

    /// <summary>Na subida: cria o acesso de quem ainda não tem (idempotente).</summary>
    public async Task<List<string>> GarantirUsuariosMotoristasAsync()
    {
        var avisos = new List<string>();
        var semAcesso = await db.MotoristasFrota.Where(m => m.UsuarioId == null).ToListAsync();
        foreach (var m in semAcesso)
            if (await GarantirUsuarioMotoristaAsync(m, soLigar: true) is { } erro) avisos.Add(erro);
        return avisos;
    }

    /// <summary>
    /// O dono/escritório digita a senha nova do motorista (Admin, 07/10/2026). Mesmo
    /// hash do sistema; sem troca obrigatória, como a padrão. Sem usuário ainda, cria antes.
    /// </summary>
    public async Task<string?> DefinirSenhaMotoristaAsync(MotoristaFrota m, string senha)
    {
        if (await LoginEscritorioDoCadastroAsync(m) != null) return MensagemLoginDoSistema;
        if (ValidarSenhaNova(senha) is { } problema) return problema;
        Usuario? usuario = m.UsuarioId is { } uid ? await db.Usuarios.FindAsync(uid) : null;
        if (usuario == null || usuario.Perfil != PerfilMotorista)
        {
            if (await GarantirUsuarioMotoristaAsync(m) is { } erro) return erro;
            usuario = await db.Usuarios.FindAsync(m.UsuarioId!.Value);
            if (usuario == null) return "Não consegui criar o acesso do motorista.";
        }
        usuario.SenhaHash = _hasher.HashPassword(usuario, senha);
        usuario.TrocarSenha = false;
        usuario.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    public record UsuarioMotoristaSemCadastro(int Id, string Nome, string Login, bool Ativo);

    /// <summary>Usuários de perfil motorista que nenhum cadastro de motorista aponta (criados à mão).</summary>
    public async Task<List<UsuarioMotoristaSemCadastro>> UsuariosMotoristaSemCadastroAsync()
    {
        var ligados = db.MotoristasFrota.Where(m => m.UsuarioId != null).Select(m => m.UsuarioId!.Value);
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Perfil == PerfilMotorista && !ligados.Contains(u.Id))
            .OrderBy(u => u.Nome)
            .Select(u => new UsuarioMotoristaSemCadastro(u.Id, u.Nome, u.Email, u.Ativo))
            .ToListAsync();
    }
}
