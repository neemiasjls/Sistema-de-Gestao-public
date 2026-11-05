namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Último número de romaneio usado por cliente (ex: AURORA=164 → próximo 165).
/// Usado para nomear os arquivos gerados: "164--AURORA-13-07-2026.xlsx".
/// </summary>
public class RomaneioContador
{
    public int Id { get; set; }
    public string Apelido { get; set; } = "";
    public int Ultimo { get; set; }
}
