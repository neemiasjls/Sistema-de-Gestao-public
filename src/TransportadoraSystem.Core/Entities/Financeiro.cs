using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Core.Entities;

public class LancamentoBanco : BaseEntity
{
    public Socio Socio { get; set; }
    public TipoLancamento Tipo { get; set; }
    public string? Descricao { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }

    public int? VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }
}

public class SaldoReceber : BaseEntity
{
    public string Empresa { get; set; } = string.Empty;
    public string? Tipo { get; set; }
    public decimal Valor { get; set; }
    public DateTime DataLancamento { get; set; }
    public bool Recebido { get; set; } = false;
    public DateTime? DataRecebimento { get; set; }
    public string? NumeroDocumento { get; set; }
    public TipoCartaoFrete? CartaoFrete { get; set; }

    public int? VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }
}

public class CartaoFreteTransacao : BaseEntity
{
    public TipoCartaoFrete Tipo { get; set; }
    public string? Empresa { get; set; }
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
    public string? NumeroDocumento { get; set; }
    public bool Conciliado { get; set; } = false;

    public int? VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }
}

public class Abastecimento : BaseEntity
{
    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public int PostoId { get; set; }
    public Posto? Posto { get; set; }

    public DateTime Data { get; set; }
    public decimal Litros { get; set; }
    public decimal ValorLitro { get; set; }
    public decimal ValorTotal { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }
}

public class DespesaDividida : BaseEntity
{
    public string Descricao { get; set; } = string.Empty;
    public decimal ValorTotal { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }

    public ICollection<RateioDespesa> Rateios { get; set; } = [];
}

public class RateioDespesa : BaseEntity
{
    public int DespesaDivididaId { get; set; }
    public DespesaDividida? DespesaDividida { get; set; }

    public Socio Socio { get; set; }
    public decimal Percentual { get; set; }
    public decimal Valor { get; set; }
}
