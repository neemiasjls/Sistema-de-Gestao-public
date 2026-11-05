using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Core.Entities;

public class CTe : BaseEntity
{
    public string NumeroCTe { get; set; } = string.Empty;
    public string ChaveAcesso { get; set; } = string.Empty;
    public DateTime DataEmissao { get; set; }
    public string? Remetente { get; set; }
    public string? Destinatario { get; set; }
    public string? UFOrigem { get; set; }
    public string? UFDestino { get; set; }
    public string? CidadeDestino { get; set; }
    public decimal ValorNF { get; set; }
    public decimal ValorFrete { get; set; }
    public decimal AliquotaICMS { get; set; }
    public decimal PesoKg { get; set; }
    public StatusCTe Status { get; set; } = StatusCTe.Ativo;
    public string? XmlOriginal { get; set; }

    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public int? ViagemId { get; set; }
    public Viagem? Viagem { get; set; }

    public string? NumeroNF { get; set; }
    public string? ClienteNome { get; set; }
}

public class Viagem : BaseEntity
{
    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public int MotoristaId { get; set; }
    public Motorista? Motorista { get; set; }

    public DateTime DataSaida { get; set; }
    public DateTime? DataRetorno { get; set; }
    public string? Destino { get; set; }

    public decimal FreteRecebido { get; set; }
    public decimal FreteRetorno { get; set; }
    public decimal Diarias { get; set; }
    public decimal Comissao { get; set; }
    public decimal Adiantamento { get; set; }
    public decimal SaldoAnterior { get; set; }

    public int Mes { get; set; }
    public int Ano { get; set; }

    public bool FechamentoRealizado { get; set; } = false;

    public ICollection<CTe> CTes { get; set; } = [];
    public ICollection<DespesaViagem> Despesas { get; set; } = [];

    public decimal SaldoFinal =>
        SaldoAnterior + FreteRecebido + FreteRetorno - Diarias - Comissao - Adiantamento
        - Despesas.Sum(d => d.Valor);
}

public class DespesaViagem : BaseEntity
{
    public int ViagemId { get; set; }
    public Viagem? Viagem { get; set; }

    public TipoDespesa Tipo { get; set; }
    public string? Descricao { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
}

public class AcertoCaminhao : BaseEntity
{
    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public int MotoristaId { get; set; }
    public Motorista? Motorista { get; set; }

    public int Mes { get; set; }
    public int Ano { get; set; }

    public decimal TotalFrete { get; set; }
    public decimal TotalDespesas { get; set; }
    public decimal TotalDiarias { get; set; }
    public decimal TotalComissao { get; set; }
    public decimal TotalAdiantamento { get; set; }
    public decimal SaldoFinal { get; set; }
}

public class FreteRetorno : BaseEntity
{
    public int ViagemId { get; set; }
    public Viagem? Viagem { get; set; }

    public string? Empresa { get; set; }
    public decimal Valor { get; set; }
    public string? NumeroDocumento { get; set; }
    public TipoCartaoFrete? TipoPagamento { get; set; }
}
