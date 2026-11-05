namespace TransportadoraSystem.Core.Entities;

// Módulo Embalados (VOLUME-16): o que era a planilha COMISSÕES VOLUMES EMBALADOS.
// Origem "planilha" = trazido da planilha (importar de novo troca só essas);
// "manual" = lançado na tela.

/// <summary>
/// Quebra guardada: a da planilha antiga ou a lançada à mão. Nos dias com
/// controle de embalados, as produtosC vêm do controle e daqui só vale o preço
/// (quando digitado). Uma linha por dia e cor (VOLUME-17).
/// </summary>
public class QuebraVolumes : BaseEntity
{
    public const string Branco = "BRANCO";
    public const string Vermelho = "VERMELHO";

    public DateOnly Data { get; set; }
    public string Cor { get; set; } = Vermelho;
    public decimal? ProdutosC { get; set; }
    public decimal? PrecoCaixa { get; set; }
    public string Origem { get; set; } = "manual";
    public string? Observacao { get; set; }
}

public class DespesaEmbalados : BaseEntity
{
    public DateOnly Data { get; set; }
    public string Descricao { get; set; } = "";
    public decimal? Valor { get; set; }
    public string? Observacao { get; set; }
    public string Origem { get; set; } = "manual";
}

/// <summary>Comissão de uma venda: por caixa = venda − compra − frete (VOLUME-18).</summary>
public class ComissaoEmbalados : BaseEntity
{
    public DateOnly Data { get; set; }
    public string Cliente { get; set; } = "";
    public string? Tipo { get; set; }
    public decimal Caixas { get; set; }
    public decimal? PrecoCompra { get; set; }
    public decimal? PrecoVenda { get; set; }
    public decimal Frete { get; set; }
    public string? Fornecedor { get; set; }
    public string? Observacao { get; set; }
    public int Ordem { get; set; }
    public string Origem { get; set; } = "manual";
}

/// <summary>Pagamento do mês: salário ou diárias, com INSS e FGTS (VOLUME-18).</summary>
public class SalarioEmbalados : BaseEntity
{
    /// <summary>Primeiro dia do mês de referência</summary>
    public DateOnly Mes { get; set; }
    public string Referencia { get; set; } = "";
    public decimal? Valor { get; set; }
    public decimal? Inss { get; set; }
    public decimal? Fgts { get; set; }
    public string? Observacao { get; set; }
    public int Ordem { get; set; }
    public string Origem { get; set; } = "manual";
}

// Barracão (VOLUME-19): o que era a planilha COMISSÕES PRODUTOSB E PRODUTOSA.

/// <summary>
/// Comissão de uma venda de produtoB ou produtoA: total = quantidade × valor
/// (pacotes ou kg; o valor já é a comissão por unidade).
/// </summary>
public class ComissaoBarracao : BaseEntity
{
    public DateOnly Data { get; set; }
    public string Cliente { get; set; } = "";
    /// <summary>Amarelas, Brancas, ProdutosB ou ProdutosA (coluna observação da planilha)</summary>
    public string? Produto { get; set; }
    public decimal Quantidade { get; set; }
    public decimal? Valor { get; set; }
    public string? Fornecedor { get; set; }
    public string? Observacao { get; set; }
    public int Ordem { get; set; }
    public string Origem { get; set; } = "manual";
}

public class DespesaDoBarracao : BaseEntity
{
    public DateOnly Data { get; set; }
    public string Descricao { get; set; } = "";
    public decimal? Valor { get; set; }
    public string? Observacao { get; set; }
    public string Origem { get; set; } = "manual";
}
