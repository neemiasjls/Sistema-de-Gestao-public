namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Placa do cadastro central (VIA-20): só placa, tipo (Cavalo ou Carreta) e quem cuida dela.
/// O painel do motorista só aceita placa daqui. Placa normalizada (XAC-6D57), única.
/// </summary>
public class PlacaFrota : BaseEntity
{
    public string Placa { get; set; } = "";
    public string Tipo { get; set; } = "Cavalo";
    // Quem cuida da placa (texto livre; não é o dono)
    public string? Responsavel { get; set; }
    public bool Ativo { get; set; } = true;
}
