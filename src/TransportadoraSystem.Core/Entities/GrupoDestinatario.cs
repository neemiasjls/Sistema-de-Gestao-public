namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Nome pelo qual um conjunto de clientes é cobrado no frete (ex.: "A. IPANEMA"),
/// com a cidade/UF que vale para a distância. O nome da nota não muda: o grupo
/// só entra no cálculo do frete.
/// </summary>
public class GrupoDestinatario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cidade { get; set; } = "";
    public string Uf { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<ClienteSubstituido> Clientes { get; set; } = [];
}

/// <summary>Destinatário da nota que, no frete, vira o grupo.</summary>
public class ClienteSubstituido
{
    public int Id { get; set; }
    public int GrupoDestinatarioId { get; set; }
    public GrupoDestinatario? GrupoDestinatario { get; set; }
    public string Nome { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
