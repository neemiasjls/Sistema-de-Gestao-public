namespace TransportadoraSystem.Core.Entities;

/// <summary>Uma linha da planilha de despesas divididas do mês: valor dividido = Valor / DividirPor.</summary>
public class DespesaDivididaLinha : BaseEntity
{
    public int Ano { get; set; }
    public int Mes { get; set; }
    public string Descricao { get; set; } = "";
    public decimal Valor { get; set; }
    public int DividirPor { get; set; } = 1;
    public string? Observacao { get; set; }
}

/// <summary>Quantos caminhões cada sócio tem no mês (e as placas, em texto).</summary>
public class DespesaDivididaSocio : BaseEntity
{
    public int Ano { get; set; }
    public int Mes { get; set; }
    public string Socio { get; set; } = "";
    public int Quantidade { get; set; }
    public string? Placas { get; set; }
}

/// <summary>Placa (cavalo) que rodou no mês: o total das despesas é dividido entre as placas marcadas.</summary>
public class DespesaDivididaPlaca : BaseEntity
{
    public int Ano { get; set; }
    public int Mes { get; set; }
    public string Placa { get; set; } = "";
}
