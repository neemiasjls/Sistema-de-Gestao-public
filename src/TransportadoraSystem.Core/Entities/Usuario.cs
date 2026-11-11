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
}
