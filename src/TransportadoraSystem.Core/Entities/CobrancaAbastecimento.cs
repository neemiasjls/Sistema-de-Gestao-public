namespace TransportadoraSystem.Core.Entities;

// Cobranças dos postos de combustível: a fatura (a lista dos abastecimentos),
// o boleto que o posto manda para pagar essa fatura e as planilhas de divisão
// geradas a partir das faturas. Fatura e boleto chegam separados, em qualquer
// ordem: o boleto se liga à fatura pelo número do documento (= número da
// fatura) e pelo posto quando o par aparece.

/// <summary>Fatura de um posto, com o que foi lido do PDF e o próprio PDF.</summary>
public class FaturaPosto
{
    public int Id { get; set; }
    public string Posto { get; set; } = "";            // nome como na planilha dos boletos: "7 VILA NORTE", "ESTRADA"
    public string Numero { get; set; } = "";           // número da fatura; vazio quando o PDF não traz
    public DateOnly? Emissao { get; set; }
    public DateOnly? Vencimento { get; set; }
    public decimal? ValorFatura { get; set; }          // o total impresso na fatura
    public decimal SomaAbastecimentos { get; set; }    // soma de tudo o que foi lido
    public decimal SomaNaPlanilha { get; set; }        // só as placas que têm dono na divisão
    public int Quantidade { get; set; }
    public decimal ValorBento { get; set; }
    public int QuantidadeBento { get; set; }
    public string AbastecimentosJson { get; set; } = "[]";
    public string AvisosJson { get; set; } = "[]";
    public string NomeArquivo { get; set; } = "";
    public byte[] Pdf { get; set; } = [];
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Boleto de um posto. Os dados vêm do PDF ou são digitados na tela.</summary>
public class BoletoPosto
{
    public int Id { get; set; }
    public string Posto { get; set; } = "";
    public string NumeroDocumento { get; set; } = "";  // no boleto do posto é o número da fatura
    public string NossoNumero { get; set; } = "";
    public decimal? Valor { get; set; }
    public DateOnly? Vencimento { get; set; }
    public DateOnly? DataDocumento { get; set; }
    public DateOnly DataChegada { get; set; }          // o dia em que foi importado; editável
    public DateOnly? PagoEm { get; set; }
    public string PagoExtratoId { get; set; } = "";    // débito do extrato do Sicoob que marcou o pago (vazio = marcado à mão), ABA-07
    public string ExtratoRecusados { get; set; } = ""; // débitos que ele desfez ("não é este"), separados por ";"
    public DateOnly? AgendadoPara { get; set; }        // pagamento agendado no banco; some quando vira pago
    public bool AgendadoPeloBanco { get; set; }        // true = o agendado veio do DDA do Sicoob; false = marcado à mão (ABA-07)
    public string CodigoBarras { get; set; } = "";     // 44 dígitos, tirado da linha digitável do PDF; vazio = não lido (DDA casa por ele)
    public int? FaturaId { get; set; }
    public FaturaPosto? Fatura { get; set; }
    public bool LigacaoManual { get; set; }            // ligado (ou desligado) à mão: a ligação automática não mexe
    public bool LidoDoPdf { get; set; }                // false quando o PDF não abriu e os dados são digitados
    public string AvisosJson { get; set; } = "[]";
    public string NomeArquivo { get; set; } = "";
    public byte[]? Pdf { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Planilha de divisão gerada, guardada como saiu.</summary>
public class PlanilhaAbastecimento
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public byte[] Conteudo { get; set; } = [];
    public string FaturasIds { get; set; } = "";       // ids das faturas que entraram, separados por vírgula
    public string Faturas { get; set; } = "";          // "ESTRADA 187298, 7 VILA NORTE 83421" — continua legível se a fatura for excluída
    public decimal Soma { get; set; }
    public int Quantidade { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
