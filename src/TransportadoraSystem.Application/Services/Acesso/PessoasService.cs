using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services.Acesso;

/// <summary>
/// Gestão das pessoas da tela "Usuários" (USU-11, Parte 6): lista única, permissões, administrador e as
/// classificações Usuário/Motorista. Só o administrador chega aqui (UsuarioController confere no banco).
/// A pessoa não tem tabela própria: é o login de escritório, o login restrito ou o cadastro de motorista,
/// juntados pela ligação MotoristasFrota.UsuarioId. Toda mudança vai para o log (nunca senha).
/// </summary>
public class PessoasService(AppDbContext db, AuthService auth, ILogger<PessoasService> logger)
{
    public const string Usuario = "usuario";
    public const string Motorista = "motorista";
    public const string Portal = "portal";

    public sealed record AreaCatalogo(string Chave, string Nome, string? Grupo);

    public sealed record LoginPessoa(int UsuarioId, string Login, string Perfil, bool Ativo, bool TrocarSenha);

    public sealed record Pessoa(
        string Chave, string Nome, List<string> Classificacoes, List<LoginPessoa> Logins, bool Ativo,
        bool Administrador, bool AcessoTotal, List<string> Areas, bool ControleViagens, bool ControleProdutosA,
        int? UsuarioId, int? MotoristaId, bool? MotoristaAtivo, string? Email, DateOnly? CnhValidade,
        int Anotacoes, List<string> Avisos);

    public sealed record Resultado(string? Erro, object? Dados = null);

    static Resultado Falha(string erro) => new(erro);

    // ── Catálogo ─────────────────────────────────────────────────────────
    /// <summary>Áreas na ordem do menu. O painel do motorista fica de fora: vem do cadastro, não se marca.</summary>
    public static List<AreaCatalogo> Catalogo() =>
        AreasAcesso.Areas.Where(a => a.Chave != AreasAcesso.PainelMotorista)
            .Select(a => new AreaCatalogo(a.Chave, a.Nome, a.Grupo)).ToList();

    // ── Lista única ──────────────────────────────────────────────────────
    public async Task<List<Pessoa>> ListarAsync()
    {
        var usuarios = await db.Usuarios.AsNoTracking().ToListAsync();
        var cadastros = await db.MotoristasFrota.AsNoTracking().ToListAsync();
        var anotacoes = (await db.VendasProdutoA.AsNoTracking()
                .Where(v => v.AnotadaPorUsuarioId != null)
                .GroupBy(v => v.AnotadaPorUsuarioId!.Value)
                .Select(g => new { Id = g.Key, N = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.Id, x => x.N);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
        var porId = usuarios.ToDictionary(u => u.Id);
        var usados = new HashSet<int>();
        var r = new List<Pessoa>();

        foreach (var m in cadastros)
        {
            Usuario? u = m.UsuarioId is { } uid && porId.TryGetValue(uid, out var achado) ? achado : null;
            if (u != null && u.Perfil == AuthService.PerfilProdutosA) u = null; // ligação estranha: não junta
            if (u != null) usados.Add(u.Id);
            r.Add(Montar(u, m, anotacoes, hoje));
        }
        foreach (var u in usuarios.Where(u => !usados.Contains(u.Id)))
            r.Add(Montar(u, null, anotacoes, hoje));

        return r.OrderByDescending(p => p.Administrador).ThenByDescending(p => p.Ativo)
            .ThenBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static Pessoa Montar(Usuario? u, MotoristaFrota? m, Dictionary<int, int> anotacoes, DateOnly hoje)
    {
        var classes = new List<string>();
        var avisos = new List<string>();
        var perfil = u?.Perfil;
        var escritorio = u != null && string.IsNullOrEmpty(perfil);
        if (escritorio) classes.Add(Usuario);
        if (m != null || perfil == AuthService.PerfilMotorista) classes.Add(Motorista);

        var temCadastroAtivo = escritorio && m is { Ativo: true };
        var areas = u == null ? [] : AuthService.AreasDoDto(perfil, u.Dono, u.Permissoes, temCadastroAtivo);
        var efetivas = u == null ? [] : AreasAcesso.Efetivas(perfil, u.Dono, u.Permissoes, temCadastroAtivo);
        var produtosA = efetivas.Contains(AreasAcesso.PortalProdutosA);
        if (perfil == AuthService.PerfilProdutosA || (perfil == AuthService.PerfilMotorista && produtosA)) classes.Add(Portal);

        if (u == null && m != null) avisos.Add("Sem login de acesso.");
        if (perfil == AuthService.PerfilMotorista && m == null) avisos.Add("Falta o cadastro de motorista.");
        if (m is { Ativo: true, CnhValidade: { } validade })
        {
            var dias = validade.DayNumber - hoje.DayNumber;
            if (dias < 0) avisos.Add($"CNH vencida há {-dias} dia(s).");
            else if (dias <= 30) avisos.Add($"CNH vence em {dias} dia(s).");
        }
        var nAnot = u != null && anotacoes.TryGetValue(u.Id, out var n) ? n : 0;
        if (perfil == AuthService.PerfilProdutosA && nAnot > 0) avisos.Add($"{nAnot} anotação(ões) no portal das produtosA.");
        if (escritorio && u!.Permissoes == null && !u.Dono) avisos.Add("Permissões ainda não definidas (sem acesso).");

        var logins = u == null ? [] : new List<LoginPessoa> { new(u.Id, u.Email, u.Perfil ?? "", u.Ativo, u.TrocarSenha) };
        var ativo = u?.Ativo ?? m!.Ativo;
        return new Pessoa(
            Chave: u != null ? $"u{u.Id}" : $"m{m!.Id}",
            Nome: u?.Nome is { Length: > 0 } nome ? nome : m?.Nome ?? "",
            Classificacoes: classes, Logins: logins, Ativo: ativo,
            Administrador: escritorio && u!.Dono, AcessoTotal: areas.Contains(AreasAcesso.AcessoTotal),
            Areas: areas,
            ControleViagens: efetivas.Contains(AreasAcesso.PainelMotorista),
            ControleProdutosA: produtosA,
            UsuarioId: u?.Id, MotoristaId: m?.Id, MotoristaAtivo: m?.Ativo,
            Email: escritorio || perfil == AuthService.PerfilProdutosA ? u!.Email : m?.Email,
            CnhValidade: m?.CnhValidade, Anotacoes: nAnot, Avisos: avisos);
    }

    // ── Permissões ───────────────────────────────────────────────────────
    /// <summary>
    /// Escritório: "*" só por <paramref name="total"/>, chaves pelo catálogo. Motorista: só liga/desliga o
    /// portal das produtosA (Caio). Administrador não se edita (sempre total); portal das produtosA é fixo.
    /// </summary>
    public async Task<Resultado> DefinirPermissoesAsync(int id, bool total, IEnumerable<string>? areas, int quemFez)
    {
        var u = await db.Usuarios.FindAsync(id);
        if (u == null) return Falha("Usuário não encontrado.");
        var escritorio = string.IsNullOrEmpty(u.Perfil);
        if (!escritorio && u.Perfil != AuthService.PerfilMotorista)
            return Falha("As áreas desta conta são fixas (portal das produtosA).");
        if (escritorio && u.Dono) return Falha("O administrador tem sempre acesso total.");

        var v = ValidarEntrada(total, areas, u.Perfil);
        if (!v.Ok) return Falha(v.Erro!);

        var antes = u.Permissoes;
        u.Permissoes = v.Valor;
        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Permissões: usuário {Quem} mudou as áreas do usuário {Alvo}: {Antes} -> {Depois}",
            quemFez, id, antes ?? "(nulo)", v.Valor);
        return new(null, new { permissoes = v.Valor });
    }

    /// <summary>"*" nunca vem dentro da lista: só pela marcação "total" (e só no escritório).</summary>
    public static (bool Ok, string? Erro, string Valor) ValidarEntrada(bool total, IEnumerable<string>? areas, string? perfil)
    {
        var lista = (areas ?? []).ToList();
        if (lista.Any(a => (a ?? "").Trim() == AreasAcesso.AcessoTotal))
            return (false, "Acesso total se marca à parte, não na lista de áreas.", "");
        if (total)
            return string.IsNullOrEmpty(perfil)
                ? (true, null, AreasAcesso.AcessoTotal)
                : (false, "Acesso total só para login do escritório.", "");
        return AreasAcesso.Validar(lista, perfil ?? "");
    }

    // ── Administrador ────────────────────────────────────────────────────
    /// <summary>
    /// Contagem e gravação numa transação Serializable: dois administradores se tirando ao mesmo tempo
    /// fariam cada um ver o outro ainda ativo. O Postgres derruba um deles (40001); tenta de novo uma vez
    /// (aí a contagem já vê o outro) e, se cair de novo, pede para repetir. Banco em memória: sem transação.
    /// </summary>
    public async Task<Resultado> DefinirAdminAsync(int id, bool admin, int quemFez)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                await using var tx = db.Database.IsRelational()
                    ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
                var r = await DefinirAdminUmaVezAsync(id, admin, quemFez);
                if (r.Erro != null) return r;
                if (tx != null) await tx.CommitAsync();
                if (r.Dados == null) return new(null, new { admin });
                // Log só depois de confirmado: tentativa derrubada não deixa registro falso.
                logger.LogInformation("Administrador: usuário {Quem} {Acao} o usuário {Alvo}",
                    quemFez, admin ? "fez administrador" : "tirou de administrador", id);
                return new(null, new { admin });
            }
            catch (Exception e) when (FalhaDeSerializacao(e))
            {
                db.ChangeTracker.Clear();
                if (tentativa >= 2)
                    return Falha("Outra mudança de administrador aconteceu ao mesmo tempo. Tente de novo.");
            }
        }
    }

    /// <summary>Postgres 40001 (serialization_failure), em qualquer nível da exceção.</summary>
    public static bool FalhaDeSerializacao(Exception? e)
    {
        for (; e != null; e = e.InnerException)
            if (e is System.Data.Common.DbException { SqlState: "40001" }) return true;
        return false;
    }

    async Task<Resultado> DefinirAdminUmaVezAsync(int id, bool admin, int quemFez)
    {
        var u = await db.Usuarios.FindAsync(id);
        if (u == null) return Falha("Usuário não encontrado.");
        if (!string.IsNullOrEmpty(u.Perfil)) return Falha("Só login do escritório pode ser administrador.");
        if (!u.Ativo) return Falha("Reative o usuário antes.");
        if (u.Dono == admin) return new(null); // nada a mudar
        if (!admin)
        {
            if (id == quemFez) return Falha("Você não pode tirar o seu próprio acesso de administrador.");
            var outros = await db.Usuarios.CountAsync(x => x.Id != id && x.Dono && x.Ativo && (x.Perfil == null || x.Perfil == ""));
            if (outros == 0) return Falha("Precisa sobrar ao menos um administrador ativo.");
        }
        u.Dono = admin;
        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new(null, new { admin, mudou = true });
    }

    /// <summary>Desativar: ninguém desativa a si mesmo; administrador continua barrado no AuthService.</summary>
    public async Task<string?> DesativarAsync(int id, int quemFez)
    {
        if (id == quemFez) return "Você não pode desativar o seu próprio usuário.";
        var erro = await auth.DefinirAtivoAsync(id, false);
        if (erro == null) logger.LogInformation("Usuário {Quem} desativou o usuário {Alvo}", quemFez, id);
        return erro;
    }

    // ── Classificação Motorista para quem é Usuário ──────────────────────
    public sealed record NovoCadastro(string? Nome, string? Login, decimal? PorcentagemComissao);

    /// <summary>
    /// Liga o login de escritório a um cadastro de motorista (que já existe ou novo). Se o cadastro tinha
    /// login de motorista próprio, as referências passam para o login de escritório e o antigo é excluído.
    /// </summary>
    public async Task<Resultado> DarMotoristaAsync(int id, int? motoristaId, NovoCadastro? novo, int quemFez)
    {
        var u = await db.Usuarios.FindAsync(id);
        if (u == null) return Falha("Usuário não encontrado.");
        if (!string.IsNullOrEmpty(u.Perfil)) return Falha("Só login do escritório recebe a classificação Motorista.");
        if (!u.Ativo) return Falha("Reative o usuário antes.");
        if (await db.MotoristasFrota.AnyAsync(m => m.UsuarioId == id))
            return Falha("Este login já está ligado a um cadastro de motorista.");

        if (motoristaId is { } mid)
        {
            var m = await db.MotoristasFrota.FindAsync(mid);
            if (m == null) return Falha("Cadastro de motorista não encontrado.");
            return await JuntarAsync(u, m, quemFez);
        }

        var login = (novo?.Login ?? "").Trim().ToLowerInvariant();
        if (!AuthService.LoginMotoristaValido(login))
            return Falha("Informe o login do motorista: só letras minúsculas e números, sem espaço (3 ou mais).");
        if (await db.MotoristasFrota.AnyAsync(m => m.Login == login) || await db.Usuarios.AnyAsync(x => x.Email.ToLower() == login))
            return Falha($"O login \"{login}\" já está em uso.");
        var nome = (novo?.Nome ?? "").Trim();
        var cadastro = new MotoristaFrota
        {
            Nome = nome.Length > 0 ? nome : u.Nome, Login = login, UsuarioId = u.Id,
            PorcentagemComissao = novo?.PorcentagemComissao ?? 6,
        };
        db.MotoristasFrota.Add(cadastro);
        await db.SaveChangesAsync();
        logger.LogInformation("Classificação: usuário {Quem} deu Motorista ao usuário {Alvo} (cadastro novo {Cadastro})",
            quemFez, id, cadastro.Id);
        return new(null, new { motoristaId = cadastro.Id, repassadas = new Dictionary<string, int>() });
    }

    // ── Classificação Usuário para quem é só Motorista ───────────────────
    public async Task<Resultado> DarUsuarioAsync(int motoristaId, string? email, bool total, IEnumerable<string>? areas, int quemFez)
    {
        var m = await db.MotoristasFrota.FindAsync(motoristaId);
        if (m == null) return Falha("Cadastro de motorista não encontrado.");
        if (await auth.LoginEscritorioDoCadastroAsync(m) != null) return Falha("Esta pessoa já tem a classificação Usuário.");
        var v = ValidarEntrada(total, areas, "");
        if (!v.Ok) return Falha(v.Erro!);
        if (await ColunaSemRepasseAsync() is { } faltando) return Falha(faltando);

        // Login novo + junção numa transação só: a junção falhou = nenhum login novo sobra. A senha só
        // vai por e-mail depois de tudo gravado. Banco em memória (sem transação): apaga o login criado.
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        var (u, senha, erro) = await auth.CriarLoginAsync(m.Nome, email ?? "", v.Valor == "" ? [] : v.Valor.Split(','));
        if (erro != null) return Falha(erro);

        Resultado junta;
        try { junta = await JuntarAsync(u!, m, quemFez); }
        catch
        {
            if (tx == null) await DesfazerLoginAsync(u!);
            throw;
        }
        if (junta.Erro != null)
        {
            if (tx != null) await tx.RollbackAsync(); else await DesfazerLoginAsync(u!);
            return junta;
        }
        if (tx != null) await tx.CommitAsync();
        logger.LogInformation("Classificação: usuário {Quem} criou o login de escritório {Alvo} para o motorista {Cadastro}, áreas {Areas}",
            quemFez, u!.Id, m.Id, v.Valor);

        var criado = await auth.EntregarSenhaPrimeiroAcessoAsync(u, senha!);
        return new(null, new { usuarioId = u.Id, senha = criado, juncao = junta.Dados });
    }

    /// <summary>Só no banco em memória (sem transação): tira o login recém-criado quando a junção falha.</summary>
    async Task DesfazerLoginAsync(Usuario u)
    {
        db.Usuarios.Remove(u);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Junta o cadastro ao login de escritório numa transação: toda coluna *UsuarioId que aponta para o
    /// login de motorista antigo passa para o de escritório e o antigo é excluído. Devolve as contagens.
    /// </summary>
    async Task<Resultado> JuntarAsync(Usuario escritorio, MotoristaFrota m, int quemFez)
    {
        Usuario? antigo = m.UsuarioId is { } uid && uid != escritorio.Id ? await db.Usuarios.FindAsync(uid) : null;
        if (antigo != null && antigo.Perfil != AuthService.PerfilMotorista)
            return Falha(string.IsNullOrEmpty(antigo.Perfil)
                ? $"Este cadastro de motorista já é de \"{antigo.Nome}\"."
                : "Este cadastro aponta para uma conta do portal das produtosA; corrija antes.");
        if (antigo != null && await ColunaSemRepasseAsync() is { } faltando) return Falha(faltando);

        var viagens = await db.ViagensCaminhao.CountAsync(v => v.MotoristaId == m.Id);
        var retornos = await db.RetornosFrete.CountAsync(x => x.MotoristaId == m.Id);
        var carregamentos = await db.CarregamentosVolumes.CountAsync(c =>
            c.ViagemId != null && db.ViagensCaminhao.Any(v => v.Id == c.ViagemId && v.MotoristaId == m.Id));

        // Dentro do DarUsuarioAsync já há transação aberta: usa a dela (quem abriu confirma).
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync() : null;
        var repassadas = new Dictionary<string, int>();
        if (antigo != null)
        {
            repassadas = await RepassarReferenciasAsync(antigo.Id, escritorio.Id);
            await db.SaveChangesAsync();
            db.Usuarios.Remove(antigo);
        }
        m.UsuarioId = escritorio.Id;
        m.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();

        logger.LogInformation(
            "Junção: usuário {Quem} ligou o cadastro de motorista {Cadastro} ao login {Alvo}; login antigo {Antigo} {Excluido}; repassadas {Repassadas}",
            quemFez, m.Id, escritorio.Id, antigo?.Id, antigo != null ? "excluído" : "(nenhum)",
            string.Join(", ", repassadas.Select(kv => $"{kv.Key}={kv.Value}")));
        return new(null, new
        {
            motoristaId = m.Id, loginExcluido = antigo?.Id, repassadas,
            viagens, retornos, carregamentos,
        });
    }

    // ── Tirar classificações ─────────────────────────────────────────────
    /// <summary>
    /// Tira "Usuário" de quem tem as duas: o login de escritório é desativado, a ligação se solta e o cadastro
    /// volta a ter login próprio de motorista (senha padrão). Ninguém tira a própria; administrador antes deixa de ser.
    /// </summary>
    public async Task<Resultado> TirarUsuarioAsync(int id, int quemFez)
    {
        if (id == quemFez) return Falha("Você não pode tirar a sua própria classificação Usuário.");
        var u = await db.Usuarios.FindAsync(id);
        if (u == null) return Falha("Usuário não encontrado.");
        if (!string.IsNullOrEmpty(u.Perfil)) return Falha("Este login não é do escritório.");
        if (u.Dono) return Falha("Tire o acesso de administrador antes.");
        var m = await db.MotoristasFrota.FirstOrDefaultAsync(x => x.UsuarioId == id);
        if (m == null) return Falha("Esta pessoa não é motorista: para tirar o acesso, desative o usuário.");

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        m.UsuarioId = null;
        if (await auth.GarantirUsuarioMotoristaAsync(m) is { } erro)
        {
            m.UsuarioId = id;
            return Falha(erro);
        }
        u.Ativo = false;
        u.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        logger.LogInformation("Classificação: usuário {Quem} tirou Usuário de {Alvo}; cadastro {Cadastro} voltou ao login {Novo}",
            quemFez, id, m.Id, m.UsuarioId);
        return new(null, new { motoristaId = m.Id, loginMotorista = m.UsuarioId });
    }

    /// <summary>Tira "Motorista" de quem é Usuário: desativa o cadastro (nunca apaga); a ligação fica.</summary>
    public async Task<Resultado> TirarMotoristaAsync(int id, int quemFez)
    {
        var u = await db.Usuarios.FindAsync(id);
        if (u == null) return Falha("Usuário não encontrado.");
        if (!string.IsNullOrEmpty(u.Perfil)) return Falha("Este login não é do escritório.");
        var m = await db.MotoristasFrota.FirstOrDefaultAsync(x => x.UsuarioId == id);
        if (m == null) return Falha("Esta pessoa não tem a classificação Motorista.");
        m.Ativo = false;
        m.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("Classificação: usuário {Quem} tirou Motorista de {Alvo} (cadastro {Cadastro} desativado)",
            quemFez, id, m.Id);
        return new(null, new { motoristaId = m.Id });
    }

    // ── Repasse das referências ao login ─────────────────────────────────
    /// <summary>Colunas do modelo que apontam para Usuarios.Id (nome terminado em UsuarioId).</summary>
    public static List<(IEntityType Tipo, IProperty Prop)> ColunasDeUsuario(DbContext ctx) =>
        ctx.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && !t.HasSharedClrType)
            .SelectMany(t => t.GetProperties()
                .Where(p => p.Name.EndsWith("UsuarioId", StringComparison.Ordinal)
                            && (p.ClrType == typeof(int) || p.ClrType == typeof(int?)))
                .Select(p => (t, p)))
            .ToList();

    /// <summary>
    /// No Postgres: toda coluna que aponta para Usuarios (por chave estrangeira ou pelo nome *UsuarioId) tem de
    /// estar no modelo, senão o repasse deixaria linha órfã. Devolve o erro ou null. Fora do Postgres: null.
    /// </summary>
    async Task<string?> ColunaSemRepasseAsync()
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) != true) return null;
        var noModelo = ColunasDeUsuario(db)
            .Select(c => $"{c.Tipo.GetTableName()}.{c.Prop.GetColumnName()}")
            .ToHashSet(StringComparer.Ordinal);
        var noBanco = await db.Database.SqlQueryRaw<string>("""
            SELECT cl.relname || '.' || a.attname AS "Value"
            FROM pg_constraint c
            JOIN pg_class cl ON cl.oid = c.conrelid
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
            WHERE c.contype = 'f' AND c.confrelid = '"Usuarios"'::regclass
            UNION
            SELECT table_name || '.' || column_name AS "Value"
            FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%UsuarioId'
            """).ToListAsync();
        var fora = noBanco.Where(c => !noModelo.Contains(c)).ToList();
        return fora.Count == 0 ? null
            : $"Coluna que aponta para usuário sem repasse: {string.Join(", ", fora)}. Nada foi mudado.";
    }

    async Task<Dictionary<string, int>> RepassarReferenciasAsync(int de, int para)
    {
        var r = new Dictionary<string, int>();
        var metodo = typeof(PessoasService).GetMethod(nameof(RepassarAsync),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var (tipo, prop) in ColunasDeUsuario(db))
        {
            var tarefa = (Task<int>)metodo.MakeGenericMethod(tipo.ClrType)
                .Invoke(this, [prop.Name, prop.ClrType == typeof(int?), de, para])!;
            var n = await tarefa;
            if (n > 0) r[$"{tipo.GetTableName()}.{prop.GetColumnName()}"] = n;
        }
        return r;
    }

    async Task<int> RepassarAsync<T>(string prop, bool nulavel, int de, int para) where T : class
    {
        var linhas = nulavel
            ? await db.Set<T>().Where(e => EF.Property<int?>(e, prop) == de).ToListAsync()
            : await db.Set<T>().Where(e => EF.Property<int>(e, prop) == de).ToListAsync();
        foreach (var l in linhas)
            db.Entry(l).Property(prop).CurrentValue = nulavel ? (int?)para : para;
        return linhas.Count;
    }
}
