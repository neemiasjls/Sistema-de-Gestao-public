namespace TransportadoraSystem.Core.Entities;

public class RotaDistancia : BaseEntity
{
    public string Origem  { get; set; } = "";
    public string UFOrigem { get; set; } = "";   // UF da cidade de origem
    public string Destino { get; set; } = "";
    public string UF      { get; set; } = "";    // UF da cidade de destino
    public int    Km      { get; set; }
}
