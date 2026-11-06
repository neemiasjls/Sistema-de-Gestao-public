namespace TransportadoraSystem.Core.Entities;

// Fechamento mensal por caminhão (VIA-18): as abas FECHAMENTO, DESPESAS DO MES e
// COMISSÃO do acerto. As viagens do mês não ficam aqui (vêm de ViagensCaminhao
// pela data de saída); aqui moram só as anotações do mês.

/// <summary>Um caminhão num mês: salário do motorista, saldo anterior digitado e a situação (aberto/fechado).</summary>
public class FechamentoMensal : BaseEntity
{
    // Por motorista desde 02/11/2026 (VIA-21); Placa ficou vazia, só para os antigos
    public int? MotoristaId { get; set; }
    public string Placa { get; set; } = "";
    public int Ano { get; set; }
    public int Mes { get; set; }
    // Só vale quando o mês anterior não está no sistema (senão o saldo anterior é o TOTAL dele)
    public decimal? SaldoAnteriorDigitado { get; set; }
    public decimal Salario { get; set; }
    public decimal HorasExtras { get; set; }
    public decimal Fgts { get; set; }
    public decimal Inss { get; set; }
    public decimal Irr { get; set; }
    public decimal Quinquenio { get; set; }
    public bool Fechado { get; set; }
    public DateTime? FechadoEm { get; set; }
    public string? Observacao { get; set; }

    public List<FechamentoDespesa> Despesas { get; set; } = [];
    public List<FechamentoCredito> Creditos { get; set; } = [];
    public List<FechamentoVale> Vales { get; set; } = [];
}

/// <summary>
/// Linha de DESPESAS DO MES. Origem "manual" ou a chave de uma linha automática
/// ("icms", "consorcio:ID", "salario:INSS"...). Automática só fica guardada quando
/// foi corrigida à mão (Corrigido) ou quando o mês foi fechado (retrato do dia).
/// </summary>
public class FechamentoDespesa
{
    public int Id { get; set; }
    public int FechamentoId { get; set; }
    public DateOnly? Data { get; set; }
    public string Referencia { get; set; } = "";
    public decimal? Valor { get; set; }
    // Texto livre: "1ª/3ª", "COTA 126"
    public string? Parcela { get; set; }
    public string? Observacao { get; set; }
    public string Origem { get; set; } = "manual";
    public bool Corrigido { get; set; }
    public int Ordem { get; set; }
}

/// <summary>CREDITOS RELATIVO AO PERIODO: lançados à mão.</summary>
public class FechamentoCredito
{
    public int Id { get; set; }
    public int FechamentoId { get; set; }
    public string Referencia { get; set; } = "";
    public decimal Valor { get; set; }
}

/// <summary>VALES MOTORISTAS do mês.</summary>
public class FechamentoVale
{
    public int Id { get; set; }
    public int FechamentoId { get; set; }
    public DateOnly? Data { get; set; }
    public decimal Valor { get; set; }
    public string? Referencia { get; set; }
}

/// <summary>
/// Consórcio do caminhão: uma parcela por mês, a partir do mês/ano da primeira,
/// até acabar o total de parcelas. Cai sozinho nas despesas do mês.
/// </summary>
public class Consorcio : BaseEntity
{
    public string Placa { get; set; } = "";
    public string Descricao { get; set; } = "";
    // "COTA 126": vai na observação da despesa
    public string? Cota { get; set; }
    public decimal ValorParcela { get; set; }
    public int TotalParcelas { get; set; }
    public int PrimeiroAno { get; set; }
    public int PrimeiroMes { get; set; }
    public bool Ativo { get; set; } = true;
}
