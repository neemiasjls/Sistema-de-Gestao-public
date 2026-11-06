namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Viagem da Agrotrade (base da aba VIAGENS da planilha AGROTRADE ou lançada à mão).
/// Não é viagem de volumes: tabela própria (AgrotradeViagens). Chave = Nf (número antes da barra).
/// Nome difere do record AgrotradeViagem do leitor para não dar ambiguidade.
/// </summary>
public class ViagemAgrotrade : BaseEntity
{
    public DateOnly Data { get; set; }
    public string? Motorista { get; set; }
    public string? Placa { get; set; }
    public string? Destino { get; set; }
    public string? Unidade { get; set; }
    public string? Nf { get; set; }
    public string? NfOriginal { get; set; }
    public decimal? Frete { get; set; }
    public decimal? AdiantDescarga { get; set; }
    public decimal? Descontos { get; set; }
    public decimal? ValorDescarga { get; set; }
    public decimal? FreteTotal { get; set; }
    public string? Contrato { get; set; }
    public decimal? DifDescarga { get; set; }
    public string? ProtocoloDescarga { get; set; }
    public DateOnly? DataOcorrenciaDescarga { get; set; }
    public bool DescargaRecebidaPlanilha { get; set; }
    public DateOnly? DataDescargaRecebidaPlanilha { get; set; }
    public string? ProtocoloDiaria { get; set; }
    public DateOnly? DataDiaria { get; set; }
    public decimal? ValorDiaria { get; set; }
    public string? Observacao { get; set; }
    public string Origem { get; set; } = "planilha";   // planilha ou manual
}

/// <summary>Fatura (transferência) da Strada: uma por número, do .txt de repasse ou do detalhamento.</summary>
public class StradaFatura : BaseEntity
{
    public string Numero { get; set; } = "";
    public DateOnly? DataGeracao { get; set; }
    public DateOnly? DataPagamento { get; set; }
    public decimal Total { get; set; }
    public decimal Taxa { get; set; }
    public decimal Repasse { get; set; }
    public bool Incompleta { get; set; }
    public string Origem { get; set; } = "txt";        // txt ou detalhamento
    public string? Avisos { get; set; }
    public bool CaiuNoBanco { get; set; }
    public DateOnly? DataCreditoBanco { get; set; }
    public string? LancamentoBancoId { get; set; }
    public List<StradaContrato> Contratos { get; set; } = [];
}

/// <summary>
/// Contrato pago pela Strada. FaturaId nulo = histórico da aba CONTRATOS TIP (origem "planilha").
/// Único por fatura + contrato; sem fatura, único pelo contrato.
/// </summary>
public class StradaContrato : BaseEntity
{
    public int? FaturaId { get; set; }
    public StradaFatura? Fatura { get; set; }
    public string Contrato { get; set; } = "";
    public decimal Valor { get; set; }
    public decimal? Adiantamento { get; set; }
    public decimal? Saldo { get; set; }
    public string Tipo { get; set; } = "indefinido";   // frete, reembolso ou indefinido
    public int? ViagemId { get; set; }
    public ViagemAgrotrade? Viagem { get; set; }
    public string? Placa { get; set; }
    public string? Socio { get; set; }
    public decimal? ValorEsperado { get; set; }
    public decimal? Diferenca { get; set; }
    public string Situacao { get; set; } = "sem viagem"; // ligado, sem viagem, sugestao ou manual
    public string? Nf { get; set; }
    public string? NfSugerida { get; set; }
    public string? Motorista { get; set; }
    public decimal? ValorContratoPdf { get; set; }
    public decimal? Seguro { get; set; }
    public DateOnly? DataTransferencia { get; set; }
    public string Origem { get; set; } = "fatura";     // fatura, planilha ou pdf
}

/// <summary>Arquivo original guardado como veio (txt, detalhamento, pdf ou agrotrade).</summary>
public class StradaArquivo
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "txt";
    public string Nome { get; set; } = "";
    public byte[] Conteudo { get; set; } = [];
    public string? Referencia { get; set; }            // nº da fatura ou do contrato
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
