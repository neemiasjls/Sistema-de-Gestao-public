namespace TransportadoraSystem.Core.Entities;

public class Usuario : BaseEntity
{
    public string Nome      { get; set; } = "";
    public string Email     { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public bool   Ativo     { get; set; } = true;

    // Só o dono cria, edita e desativa usuários.
    public bool   Dono        { get; set; }

    // Senha provisória, mandada por e-mail: enquanto for true, a pessoa só
    // consegue trocar a senha — o resto da API responde 403.
    public bool   TrocarSenha { get; set; }

    // "" = escritório (acesso normal); "motorista" = só o painel do motorista;
    // "produtosA" = só o portal das produtosA (cadastro pelo QR, EST-09/USU-05).
    // O login do motorista fica no campo Email, sem @ (joseferraz).
    public string Perfil      { get; set; } = "";

    // Áreas liberadas (USU-11): chaves do catálogo AreasAcesso separadas por vírgula,
    // "*" = acesso total, null = ainda não definido = sem acesso. Só entra pelas rotas próprias.
    public string? Permissoes { get; set; }

    // Senha provisória paralela (motorista pelo e-mail): fica ao lado da senha atual,
    // que só muda quando a pessoa cria a dela. Só o hash; vale até SenhaProvisoriaExpiraEm.
    public string?   SenhaProvisoriaHash     { get; set; }
    public DateTime? SenhaProvisoriaExpiraEm { get; set; }
    public DateTime? SenhaProvisoriaEm       { get; set; }
    public DateTime? SenhaAlteradaEm         { get; set; }
}
