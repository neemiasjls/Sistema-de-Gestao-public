using TransportadoraSystem.Core.Enums;

namespace TransportadoraSystem.Core.Entities;

public class Veiculo : BaseEntity
{
    public string Placa { get; set; } = string.Empty;
    public Socio Socio { get; set; }
    public bool Ativo { get; set; } = true;

    public int? MotoristaAtualId { get; set; }
    public Motorista? MotoristaAtual { get; set; }

    public ICollection<Viagem> Viagens { get; set; } = [];
    public ICollection<CTe> CTes { get; set; } = [];
}

public class Motorista : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string? Apelido { get; set; }
    public string? CPF { get; set; }
    public string? Telefone { get; set; }
    public bool Ativo { get; set; } = true;

    public int? VeiculoPrincipalId { get; set; }
    public Socio? SocioPrincipal { get; set; }

    public ICollection<Veiculo> Veiculos { get; set; } = [];
}
