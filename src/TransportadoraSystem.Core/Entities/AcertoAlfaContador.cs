namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Guarda o último número usado no "ACERTO G. ALFA" para sugerir o próximo (+1).
/// Tabela de uma linha só.
/// </summary>
public class AcertoAlfaContador
{
    public int Id { get; set; }
    public int Ultimo { get; set; }
}
