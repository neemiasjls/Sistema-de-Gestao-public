namespace TransportadoraSystem.Application.Services.Acesso;

/// <summary>
/// Catálogo das áreas do sistema (uma por tela do menu) e a regra que decide, por rota da API,
/// quem pode ler (GET/HEAD) e quem pode escrever (o resto). Sem banco e sem HTTP: quem chama
/// passa o perfil, o administrador, as permissões gravadas e se o login tem cadastro de motorista
/// ativo (USU-11). Fechado por padrão: rota sem regra só para o administrador; permissão nula =
/// nenhuma área.
/// </summary>
public static class AreasAcesso
{
    // Pseudoáreas (não são caixinhas)
    public const string Todos = "#todos";           // qualquer login ativo
    public const string Escritorio = "#escritorio"; // login de escritório ou do portal das produtosA
    public const string Admin = "#admin";           // só o administrador (Usuarios.Dono)
    public const string Anonimo = "#anonimo";       // rota pública ([AllowAnonymous]); a própria rota se protege

    public const string AcessoTotal = "*";
    public const string PainelMotorista = "painel-motorista";
    public const string PortalProdutosA = "portal-produtosA";

    public const string MensagemMotorista = "Acesso só ao painel do motorista.";
    public const string MensagemProdutosA = "Acesso só ao portal das produtosA.";
    public const string MensagemAdmin = "Só o administrador tem acesso a isto.";

    public sealed record Area(string Chave, string Nome, string? Grupo, string? Tela);

    public sealed record Regra(string Prefixo, string[] Leem, string[] Escrevem);

    public sealed record UsuarioAcesso(string? Perfil, bool Dono, string? Permissoes, bool TemCadastroMotoristaAtivo);

    public sealed record Decisao(bool Permitido, int Status, string? Mensagem, string? AreaFaltando)
    {
        public static readonly Decisao Liberado = new(true, 200, null, null);
    }

    public static readonly IReadOnlyList<Area> Areas =
    [
        new("inicio", "Início", null, "/"),
        new("viagens", "Viagens", "Dia a dia", "/frete"),
        new("acerto-viagens", "Acerto viagens", "Dia a dia", "/viagens-caminhoes"),
        new("retornos", "Retornos", "Dia a dia", "/retornos"),
        new("romaneios", "Romaneios", "Dia a dia", "/romaneios"),
        new("abastecimentos", "Abastecimentos", "Dia a dia", "/abastecimentos"),
        new("emails-clientes", "E-mails aos clientes", "Dia a dia", "/emails-clientes"),
        new("volumes-embalados", "Controle embalados", "Embalados", "/estoque/volumes"),
        new("embalados-quebras", "Quebras", "Embalados", "/embalados/quebras"),
        new("embalados-despesas", "Despesas embalados", "Embalados", "/embalados/despesas"),
        new("embalados-comissoes", "Comissões", "Embalados", "/embalados/comissoes"),
        new("embalados-salarios", "Salários", "Embalados", "/embalados/salarios"),
        new("barracao-comissoes", "Comissões", "Estoques", "/barracao/comissoes"),
        new("produtosB", "ProdutosB", "Estoques", "/estoque/produtosB"),
        new("produtosA", "ProdutosA", "Estoques", "/estoque/produtosA"),
        new("produtosC", "ProdutosC", "Estoques", "/estoque/produtosC"),
        new("barracao-despesas", "Despesas barracão", "Barracão", "/barracao/despesas"),
        new("boletos", "Boletos", "Financeiro", "/boletos"),
        new("acerto-alfa", "Acerto Alfa", "Financeiro", "/acerto-alfa"),
        new("financeiro", "Financeiro", "Financeiro", "/financeiro"),
        new("despesas-divididas", "Despesas divididas", "Financeiro", "/despesas-divididas"),
        new("saldos", "Saldos a receber", "Financeiro", "/saldos"),
        new("agrotrade-tip", "Agrotrade / TIP Bank", "Financeiro", "/agrotrade-tip"),
        new("seguro", "Seguro", "Fechamento do mês", "/cte-export"),
        new("semparar", "Sem Parar", "Fechamento do mês", "/semparar"),
        new("motoristas", "Motoristas", "Cadastros", "/cadastros/motoristas"),
        new("placas", "Placas", "Cadastros", "/cadastros/placas"),
        new("clientes", "Clientes", "Cadastros e arquivos", "/precos"),
        new("tipos-volume", "Tipos de volumes", "Cadastros e arquivos", "/tipos-volume"),
        new("notas", "Notas fiscais", "Cadastros e arquivos", "/notas"),
        new(PortalProdutosA, "Portal das produtosA", null, "/produtosA"),
        // Não é caixinha: vem do perfil motorista ou do cadastro de motorista ligado ao login.
        new(PainelMotorista, "Painel do motorista", null, "/controle-viagens"),
    ];

    static readonly Dictionary<string, Area> PorChave = Areas.ToDictionary(a => a.Chave, StringComparer.Ordinal);

    /// <summary>Áreas que o "*" e o administrador abrem (todas menos o painel do motorista).</summary>
    public static readonly IReadOnlyList<string> AreasDeEscritorio =
        Areas.Where(a => a.Chave != PainelMotorista).Select(a => a.Chave).ToList();

    static string[] L(params string[] areas) => areas;
    static readonly string[] SoAdmin = [Admin];

    /// <summary>
    /// Regras por prefixo de rota, montadas da matriz tela → rotas (docs/planos/usuarios-permissoes-matriz.md),
    /// com a tela "dona" de cada rota. Vale o prefixo MAIS LONGO que casa por segmento inteiro;
    /// "*" num segmento casa qualquer segmento (id), e no empate ganha o prefixo com mais segmentos fixos.
    /// </summary>
    public static readonly IReadOnlyList<Regra> Regras =
    [
        // Entrada pública e sessão
        new("/health", [Anonimo], [Anonimo]),
        new("/api/auth/login", [Anonimo], [Anonimo]),
        new("/api/auth/produtosA", [Anonimo], [Anonimo]),
        new("/api/auth/me", [Todos], [Todos]),
        new("/api/auth/perfil", [Escritorio], [Escritorio]),
        new("/api/auth/trocar-senha", [Escritorio], [Escritorio]),
        new("/api/usuario", SoAdmin, SoAdmin),
        new("/api/painel", L("inicio"), L("inicio")),
        new("/api/busca", L("inicio"), L("inicio")),

        // Perfis restritos
        new("/api/motorista-painel", L(PainelMotorista), L(PainelMotorista)),
        new("/api/produtosA-portal", L(PortalProdutosA), L(PortalProdutosA)),

        // Dia a dia
        new("/api/calculo-frete", L("viagens"), L("viagens")),
        new("/api/nfe", L("viagens", "romaneios"), L("viagens", "romaneios")),
        new("/api/nfe/extrair-para-frete", L("viagens"), L("viagens")),
        new("/api/nfe-arquivos", L("viagens", "romaneios", "notas"), L("viagens", "romaneios", "notas")),
        new("/api/nfe-arquivos/*", SoAdmin, SoAdmin),
        new("/api/nfe-arquivos/*/pdf", L("notas"), SoAdmin),
        new("/api/nfe-arquivos/*/xml", L("notas"), SoAdmin),
        new("/api/nfe-arquivos/chave", L("viagens"), SoAdmin),
        new("/api/cte", L("inicio", "seguro"), SoAdmin),
        new("/api/cte/*", SoAdmin, SoAdmin),
        new("/api/cte/aliquotas-seguro", L("seguro"), L("seguro")),
        new("/api/cte/exportar-planilha", L("seguro"), L("seguro")),
        new("/api/cte/importar-multiplos", L("seguro"), L("seguro")),
        new("/api/cte/calcular-frete", L("viagens"), L("viagens")),
        new("/api/cte/distancias", L("viagens"), L("viagens")),
        new("/api/viagens-caminhao", L("acerto-viagens"), L("acerto-viagens")),
        new("/api/viagens-caminhao/precos", L("acerto-viagens"), SoAdmin),
        new("/api/viagens-caminhao/motoristas", L("acerto-viagens", "motoristas"), L("motoristas")),
        new("/api/fechamentos", L("acerto-viagens"), L("acerto-viagens")),
        new("/api/retornos", L("retornos", "acerto-viagens"), L("retornos", "acerto-viagens")),
        new("/api/romaneios-cliente", L("romaneios"), L("romaneios")),
        new("/api/romaneios-cliente/*/arquivo", L("romaneios", "emails-clientes"), L("romaneios")),
        new("/api/acerto-alfa", L("acerto-alfa"), L("acerto-alfa")),
        new("/api/acerto-alfa/gerar", SoAdmin, SoAdmin),
        new("/api/acerto-alfa/conferencia", SoAdmin, SoAdmin),
        new("/api/acerto-alfa/romaneios-fazenda", L("romaneios"), L("romaneios")),
        new("/api/acerto-alfa/acertos/*/arquivos/*", L("acerto-alfa", "romaneios"), L("acerto-alfa")),
        new("/api/abastecimento", L("abastecimentos"), L("abastecimentos")),
        new("/api/abastecimento/gerar", SoAdmin, SoAdmin),
        new("/api/abastecimento/cobrancas/boletos/*/pdf", L("abastecimentos", "boletos"), SoAdmin),
        new("/api/emails-cliente", L("emails-clientes"), L("emails-clientes")),

        // Embalados
        new("/api/volumes-embalados", L("volumes-embalados"), L("volumes-embalados")),
        new("/api/embalados",
            L("embalados-quebras", "embalados-despesas", "embalados-comissoes", "embalados-salarios"),
            L("embalados-quebras", "embalados-despesas", "embalados-comissoes", "embalados-salarios")),
        new("/api/embalados/quebras", L("embalados-quebras"), L("embalados-quebras")),
        new("/api/embalados/despesas", L("embalados-despesas"), L("embalados-despesas")),
        new("/api/embalados/comissoes", L("embalados-comissoes"), L("embalados-comissoes")),
        new("/api/embalados/salarios", L("embalados-salarios"), L("embalados-salarios")),

        // Estoques e barracão (/api/estoque sem sub-área = só administrador: tipos e comissões sem tela)
        new("/api/estoque", SoAdmin, SoAdmin),
        new("/api/estoque/tipos", L("produtosB", "produtosA", "produtosC", "boletos"), SoAdmin),
        new("/api/estoque/clientes", L("produtosB", "produtosA", "produtosC", "boletos"), SoAdmin),
        new("/api/estoque/produtosA", L("produtosA"), L("produtosA")),
        new("/api/estoque/produtosA/cargas/*", SoAdmin, SoAdmin),
        new("/api/estoque/produtosB", L("produtosB"), L("produtosB")),
        new("/api/estoque/produtosB/cargas/*", SoAdmin, SoAdmin),
        new("/api/estoque/produtosB/vendas", L("produtosB"), L("produtosB", "produtosC", "boletos")),
        new("/api/estoque/produtosC", L("produtosC", "boletos"), L("produtosC", "boletos")),
        new("/api/estoque/produtosC/preencher-historico", SoAdmin, SoAdmin),
        new("/api/estoque/produtosA-portal", SoAdmin, SoAdmin),
        new("/api/barracao", SoAdmin, SoAdmin),
        new("/api/barracao/comissoes", L("barracao-comissoes", "barracao-despesas"), L("barracao-comissoes", "barracao-despesas")),
        new("/api/barracao/despesas", L("barracao-comissoes", "barracao-despesas"), L("barracao-comissoes", "barracao-despesas")),
        new("/api/barracao/importar", L("barracao-comissoes", "barracao-despesas"), L("barracao-comissoes", "barracao-despesas")),

        // Financeiro
        new("/api/boleto", L("boletos"), L("boletos")),
        new("/api/boleto/pendentes", SoAdmin, SoAdmin),
        new("/api/boleto/vencendo", SoAdmin, SoAdmin),
        new("/api/boletos-contas", L("boletos"), L("boletos")),
        new("/api/boletos-contas/receber/importar", L("boletos", "emails-clientes"), L("boletos", "emails-clientes")),
        new("/api/sicoob", L("boletos"), L("boletos")),
        new("/api/sicoob/conferir-agendado", [Anonimo], [Anonimo]), // chave no cabeçalho, conferida na rota
        new("/api/cliente", L("boletos"), SoAdmin),
        new("/api/cliente/*", SoAdmin, SoAdmin),
        new("/api/financeiro", L("financeiro"), L("financeiro")),
        new("/api/financeiro/saldos", L("saldos"), L("saldos")),
        new("/api/financeiro/abastecimentos", SoAdmin, SoAdmin),
        new("/api/veiculo", L("financeiro", "saldos"), SoAdmin),
        new("/api/veiculo/*", SoAdmin, SoAdmin),
        new("/api/veiculo/donos", L("abastecimentos"), L("abastecimentos")),
        new("/api/export", SoAdmin, SoAdmin),
        new("/api/export/abastecimento", L("financeiro"), SoAdmin),
        new("/api/export/cte-geral", L("inicio", "seguro"), SoAdmin),
        new("/api/export/cte-por-placa", L("inicio", "seguro"), SoAdmin),
        new("/api/despesas-divididas", L("despesas-divididas"), L("despesas-divididas")),
        new("/api/strada", L("agrotrade-tip"), L("agrotrade-tip")),

        // Fechamento do mês
        new("/api/planilhas-seguro", L("seguro"), L("seguro")),
        new("/api/semparar", L("semparar"), L("semparar")),

        // Cadastros
        new("/api/motoristas-frota", L("motoristas"), L("motoristas")),
        new("/api/placas", L("acerto-viagens", "seguro", "placas"), L("placas")),
        new("/api/precos", L("clientes"), L("clientes")),
        new("/api/tipos-volume", L("viagens", "romaneios", "tipos-volume"), L("viagens", "romaneios", "tipos-volume")),
        new("/api/tipos-volume/*", L("tipos-volume"), L("tipos-volume")),
        new("/api/tipos-volume/*/escritas", L("tipos-volume"), L("viagens", "romaneios", "tipos-volume")),

        // Rotas antigas que nenhuma tela chama (VIA-02)
        new("/api/motorista", SoAdmin, SoAdmin),
        new("/api/romaneio", SoAdmin, SoAdmin),
        new("/api/viagem", SoAdmin, SoAdmin),
    ];

    static readonly (Regra Regra, string[] Segmentos, int Fixos)[] RegrasPreparadas =
        Regras.Select(r =>
        {
            var s = Segmentos(r.Prefixo);
            return (r, s, s.Count(x => x != "*"));
        }).ToArray();

    static string[] Segmentos(string caminho) =>
        caminho.ToLowerInvariant().Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A regra do prefixo mais longo que casa por segmento inteiro, ou nulo.</summary>
    public static Regra? Achar(string caminho)
    {
        var seg = Segmentos(caminho ?? "");
        Regra? melhor = null;
        int melhorTam = -1, melhorFixos = -1;
        foreach (var (regra, pre, fixos) in RegrasPreparadas)
        {
            if (pre.Length > seg.Length) continue;
            var casa = true;
            for (var i = 0; i < pre.Length && casa; i++)
                casa = pre[i] == "*" || pre[i] == seg[i];
            if (!casa) continue;
            if (pre.Length > melhorTam || (pre.Length == melhorTam && fixos > melhorFixos))
                (melhor, melhorTam, melhorFixos) = (regra, pre.Length, fixos);
        }
        return melhor;
    }

    public static bool EhLeitura(string metodo) =>
        string.Equals(metodo, "GET", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(metodo, "HEAD", StringComparison.OrdinalIgnoreCase);

    static bool EhEscritorio(string? perfil) => string.IsNullOrEmpty(perfil);

    /// <summary>
    /// Áreas que o usuário tem de fato. Motorista: só o painel. Portal: só a página das produtosA
    /// (a coluna não alarga perfil restrito). Escritório: administrador = tudo, senão a coluna;
    /// mais o painel do motorista se o login tem cadastro de motorista ativo. O "*" nunca dá o painel.
    /// Perfil desconhecido = nada.
    /// </summary>
    public static HashSet<string> Efetivas(string? perfil, bool dono, string? permissoes, bool temCadastroMotoristaAtivo)
    {
        var r = new HashSet<string>(StringComparer.Ordinal);
        if (perfil == AuthService.PerfilMotorista) { r.Add(PainelMotorista); return r; }
        if (perfil == AuthService.PerfilProdutosA) { r.Add(PortalProdutosA); return r; }
        if (!EhEscritorio(perfil)) return r;

        if (dono) r.UnionWith(AreasDeEscritorio);
        else
            foreach (var chave in Partes(permissoes))
            {
                if (chave == AcessoTotal) r.UnionWith(AreasDeEscritorio);
                else if (chave != PainelMotorista && PorChave.ContainsKey(chave)) r.Add(chave);
            }
        if (temCadastroMotoristaAtivo) r.Add(PainelMotorista);
        return r;
    }

    static IEnumerable<string> Partes(string? permissoes) =>
        (permissoes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToLowerInvariant());

    /// <summary>
    /// Confere as chaves que a tela manda gravar: só chaves do catálogo (ou "*"), nunca pseudoárea
    /// nem o painel do motorista. Devolve as chaves arrumadas ("*" sozinho, ou as chaves sem repetir)
    /// ou o erro.
    /// </summary>
    public static (bool Ok, string? Erro, string Valor) Validar(IEnumerable<string>? chaves)
    {
        var lista = (chaves ?? []).Select(c => (c ?? "").Trim().ToLowerInvariant()).Where(c => c != "").Distinct().ToList();
        var ruins = lista.Where(c => c != AcessoTotal && (c == PainelMotorista || !PorChave.ContainsKey(c))).ToList();
        if (ruins.Count > 0) return (false, $"Área desconhecida: {string.Join(", ", ruins)}.", "");
        if (lista.Contains(AcessoTotal)) return (true, null, AcessoTotal);
        var ordem = AreasDeEscritorio.Where(lista.Contains);
        return (true, null, string.Join(",", ordem));
    }

    /// <summary>Decide se o pedido passa. Rota sem regra: só o administrador.</summary>
    public static Decisao Decidir(UsuarioAcesso u, string metodo, string caminho)
    {
        var regra = Achar(caminho);
        var lista = regra == null ? null : (EhLeitura(metodo) ? regra.Leem : regra.Escrevem);
        if (lista != null && lista.Contains(Anonimo)) return Decisao.Liberado;

        var perfil = u.Perfil ?? "";
        var escritorio = EhEscritorio(perfil);
        var restrito = perfil == AuthService.PerfilMotorista || perfil == AuthService.PerfilProdutosA;
        if (!escritorio && !restrito) return new(false, 403, "Perfil sem acesso.", null);

        var dono = escritorio && u.Dono;
        var efetivas = Efetivas(perfil, dono, u.Permissoes, u.TemCadastroMotoristaAtivo);

        if (lista != null)
        {
            if (lista.Contains(Todos)) return Decisao.Liberado;
            if (lista.Contains(Escritorio) && (escritorio || perfil == AuthService.PerfilProdutosA)) return Decisao.Liberado;
            if (lista.Contains(Admin) && dono) return Decisao.Liberado;
            if (lista.Any(efetivas.Contains)) return Decisao.Liberado;
        }
        else if (dono) return Decisao.Liberado;

        if (perfil == AuthService.PerfilMotorista) return new(false, 403, MensagemMotorista, PainelMotorista);
        if (perfil == AuthService.PerfilProdutosA) return new(false, 403, MensagemProdutosA, PortalProdutosA);

        var falta = lista?.FirstOrDefault(a => PorChave.ContainsKey(a));
        if (falta == null) return new(false, 403, MensagemAdmin, Admin);
        return new(false, 403, $"Sem acesso a esta área ({PorChave[falta].Nome}).", falta);
    }
}
