namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Histórico do portal das produtosA (EST-09), só o dono vê: cadastro pelo QR, cliente novo,
/// anotação criada/editada/excluída, senha esquecida, código do QR trocado. Sem FK de propósito:
/// a linha fica mesmo depois que a venda ou o usuário somem.
/// </summary>
public class PortalProdutosAHistorico
{
    public int Id { get; set; }
    public DateTime Quando { get; set; } = DateTime.UtcNow;
    public int? UsuarioId { get; set; }
    public string UsuarioNome { get; set; } = "";   // cópia do nome na hora
    public string Acao { get; set; } = "";
    public int? VendaProdutoAId { get; set; }
    public string Resumo { get; set; } = "";
    public string? Antes { get; set; }              // JSON em texto
    public string? Depois { get; set; }             // JSON em texto
    public string? Ip { get; set; }
}

/// <summary>Linha única: o código impresso no QR do barracão. Trocar invalida o QR antigo.</summary>
public class PortalProdutosAConfig
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public DateTime TrocadoEm { get; set; } = DateTime.UtcNow;
}
