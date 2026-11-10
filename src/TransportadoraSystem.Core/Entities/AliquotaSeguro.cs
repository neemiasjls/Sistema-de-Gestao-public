namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Alíquota do seguro de carga por UF do destino (em %, como na planilha SG:
/// 0,069 = 0,069% do valor da nota). Muda na tela Seguro › Alíquotas e vale
/// para as próximas planilhas; as guardadas ficam como saíram (SEG-06).
/// </summary>
public class AliquotaSeguro
{
    public int Id { get; set; }
    public string Uf { get; set; } = "";
    public decimal Aliquota { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
