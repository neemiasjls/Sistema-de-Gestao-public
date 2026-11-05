namespace TransportadoraSystem.Core.Entities;

public class SeguroMensal : BaseEntity
{
    public int Mes { get; set; }
    public int Ano { get; set; }
    public decimal ValorTotal { get; set; }
    public string? Seguradora { get; set; }
    public string? NumeroApolice { get; set; }

    public ICollection<SeguroPlaca> Placas { get; set; } = [];
}

public class SeguroPlaca : BaseEntity
{
    public int SeguroMensalId { get; set; }
    public SeguroMensal? SeguroMensal { get; set; }

    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public decimal Valor { get; set; }
    public int NumeroViagens { get; set; }
}

public class SemPararMensal : BaseEntity
{
    public int Mes { get; set; }
    public int Ano { get; set; }
    public decimal ValorTotal { get; set; }

    public ICollection<SemPararPlaca> Placas { get; set; } = [];
}

public class SemPararPlaca : BaseEntity
{
    public int SemPararMensalId { get; set; }
    public SemPararMensal? SemPararMensal { get; set; }

    public int VeiculoId { get; set; }
    public Veiculo? Veiculo { get; set; }

    public decimal Valor { get; set; }
    public int NumeroPassagens { get; set; }
}
