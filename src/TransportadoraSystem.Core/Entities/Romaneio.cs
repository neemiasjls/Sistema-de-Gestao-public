using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Core.Entities;

public class Romaneio : BaseEntity
{
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public DateTime DataRomaneio { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }
    public bool Enviado { get; set; } = false;
    public DateTime? DataEnvio { get; set; }
    public CanalEnvio? CanalEnvio { get; set; }

    public ICollection<ItemRomaneio> Itens { get; set; } = [];
    public ICollection<Boleto> Boletos { get; set; } = [];

    public decimal Total => Itens.Sum(i => i.ValorTotal);
}

public class ItemRomaneio : BaseEntity
{
    public int RomaneioId { get; set; }
    public Romaneio? Romaneio { get; set; }

    public TipoProduto Produto { get; set; }
    public string? Descricao { get; set; }
    public decimal Quantidade { get; set; }
    public string? Unidade { get; set; }
    public decimal ValorUnitario { get; set; }

    public decimal ValorTotal => Quantidade * ValorUnitario;
}

public class Boleto : BaseEntity
{
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public int? RomaneioId { get; set; }
    public Romaneio? Romaneio { get; set; }

    public decimal Valor { get; set; }
    public DateTime DataEmissao { get; set; }
    public DateTime DataVencimento { get; set; }
    public StatusBoleto Status { get; set; } = StatusBoleto.Pendente;
    public DateTime? DataPagamento { get; set; }
    public string? Descricao { get; set; }
    public string? NossoNumero { get; set; }

    public TipoProduto? TipoProduto { get; set; }

    public int? VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }
}
