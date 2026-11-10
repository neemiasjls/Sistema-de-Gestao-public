namespace TransportadoraSystem.Core.Entities;

// Boletos da tela Boletos que não são dos postos: os que a transportadora
// emite para os clientes (a receber) e os avulsos que ela tem de pagar.

/// <summary>
/// Boleto emitido para um cliente: o das produtosD da CD e o do frete da Girassol.
/// O PDF fica guardado como veio; os dados são lidos dele e podem ser corrigidos.
/// Espelha a planilha de controle de cada cliente ("CONTROLE DE BOLETOS A
/// RECEBER"): data da carga, caminhão, número do boleto, valor, vencimento e se
/// já foi recebido.
/// </summary>
public class BoletoReceber
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";          // "CD" ou "GIRASSOL"
    public int? Sequencia { get; set; }                // o número que abre o nome do arquivo: "197-REF. ..."
    public string Numero { get; set; } = "";           // número do documento (no Sicoob é a data da carga: 29092026)
    public string NossoNumero { get; set; } = "";      // "668-8" — é o "Nº BOLETO" da planilha de controle
    public string LinhaDigitavel { get; set; } = "";   // devolvida pelo Sicoob quando o sistema emite (BOL-10)
    public string Pagador { get; set; } = "";
    public string Placa { get; set; } = "";
    public decimal? Valor { get; set; }
    public DateOnly? Vencimento { get; set; }
    public DateOnly? Emissao { get; set; }
    public DateOnly? DataCarga { get; set; }
    public DateOnly? RecebidoEm { get; set; }
    public string Observacao { get; set; } = "";
    public bool LidoDoPdf { get; set; }
    public string AvisosJson { get; set; } = "[]";
    public string NomeArquivo { get; set; } = "";
    public byte[]? Pdf { get; set; }                   // null no boleto lançado à mão (ou importado da planilha)
    public string? Hash { get; set; }                  // SHA-256 do PDF: subir o mesmo arquivo de novo não duplica
    // Quando o PDF foi trocado por outro (boleto cancelado e refeito): o e-mail da
    // carga enviado antes disso foi com o boleto velho e volta para enviar.
    public DateTime? SubstituidoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Boleto a pagar que não é de posto: lido do PDF quando dá, senão digitado.</summary>
public class BoletoPagarAvulso
{
    public int Id { get; set; }
    public string Descricao { get; set; } = "";
    public string Beneficiario { get; set; } = "";
    public string Numero { get; set; } = "";
    public decimal? Valor { get; set; }
    public DateOnly? Vencimento { get; set; }
    public DateOnly DataChegada { get; set; }
    public DateOnly? PagoEm { get; set; }
    public bool LidoDoPdf { get; set; }
    public string AvisosJson { get; set; } = "[]";
    public string NomeArquivo { get; set; } = "";
    public byte[]? Pdf { get; set; }
    public string? Hash { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
