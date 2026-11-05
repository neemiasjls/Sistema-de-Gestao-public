using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Core.Entities;

public class MovimentoEstoque : BaseEntity
{
    public TipoProduto Produto { get; set; }
    public TipoMovimentoEstoque Tipo { get; set; }
    public decimal Quantidade { get; set; }
    public decimal? ValorUnitario { get; set; }
    public DateTime Data { get; set; }
    public string? Descricao { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
}

public class LoteVolumesEmbalados : BaseEntity
{
    public DateTime DataEntrada { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }

    public decimal QtdEntrada { get; set; }
    public decimal ValorCompraUnitario { get; set; }
    public decimal QtdSaida { get; set; }
    public decimal ValorVendaUnitario { get; set; }
    public decimal Quebras { get; set; }

    public decimal CustoTotal => QtdEntrada * ValorCompraUnitario;
    public decimal ReceitaTotal => QtdSaida * ValorVendaUnitario;
    public decimal Margem => ReceitaTotal - CustoTotal;

    public ICollection<DespesaBarracao> Despesas { get; set; } = [];
}

public class DespesaBarracao : BaseEntity
{
    public int? LoteId { get; set; }
    public LoteVolumesEmbalados? Lote { get; set; }

    public string Descricao { get; set; } = string.Empty;
    public decimal Valor { get; set; }
    public DateTime Data { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }

    public bool EhValter { get; set; } = false;
    public decimal? HorasTrabalhadas { get; set; }
    public decimal? ValorHora { get; set; }
}
