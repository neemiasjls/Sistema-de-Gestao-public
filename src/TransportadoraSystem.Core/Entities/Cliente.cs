namespace TransportadoraSystem.Core.Entities;

public class Cliente : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string? NomeFantasia { get; set; }
    public string? CNPJ { get; set; }
    public string? Email { get; set; }
    public string? Telefone { get; set; }
    public string? Cidade { get; set; }
    public string? UF { get; set; }
    public bool Ativo { get; set; } = true;

    public ICollection<Romaneio> Romaneios { get; set; } = [];
    public ICollection<Boleto> Boletos { get; set; } = [];
}

public class Fazenda : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string? Contato { get; set; }
    public string? Telefone { get; set; }
    public string? Cidade { get; set; }
    public string? UF { get; set; }
    public bool Ativo { get; set; } = true;
}

public class Posto : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string? Cidade { get; set; }
    public string? UF { get; set; }
    public bool Ativo { get; set; } = true;
}
