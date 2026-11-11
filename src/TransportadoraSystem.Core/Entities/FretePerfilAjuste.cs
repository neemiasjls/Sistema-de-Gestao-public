namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Queda do volume lançada numa carga da Girassol (BOL-17), uma linha por carga (data + placa com hífen).
/// O cálculo continua sendo o .txt das notas (PlanilhasCarga); aqui fica a queda por caixa de cada tipo,
/// a fotografia do que foi emitido (para o e-mail repetir os mesmos números) e quem lançou.
/// </summary>
public class FretePerfilAjuste
{
    public int Id { get; set; }
    public DateOnly DataCarga { get; set; }
    public string Placa { get; set; } = "";             // "XAH-2I15" (EmailClienteService.Placa); vazia = carga sem placa
    public string? DescontosJson { get; set; }          // tipo → queda por caixa (só tipos normais); nulo = sem queda
    public string? EmitidoJson { get; set; }            // o que saiu no boleto (FretePerfilService.Emitido)
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
    public string AtualizadoPor { get; set; } = "";
}
