namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Como o sistema emite o boleto a receber de cada cliente (uma linha por cliente: "CD" ou
/// "GIRASSOL"), editável em Boletos › Sicoob › Emissão de boletos (BOL-12). Sem a linha, valem
/// os valores do BOL-11 que estão no código (BoletoMontador.Padrao).
/// </summary>
public class ConfigEmissaoBoleto
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";
    public int DiasVencimento { get; set; }
    public string PagadorNome { get; set; } = "";
    public string PagadorCnpj { get; set; } = "";
    public string PagadorEndereco { get; set; } = "";
    public string PagadorBairro { get; set; } = "";
    public string PagadorCidade { get; set; } = "";
    public string PagadorUf { get; set; } = "";
    public string PagadorCep { get; set; } = "";
    public decimal PrecoCaixaProdutoD { get; set; }   // só a CD usa (valor = caixas × preço)
    public decimal MultaPct { get; set; }
    public decimal JurosMes { get; set; }
    public string Instrucao3 { get; set; } = "";
    public bool Pix { get; set; }
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
