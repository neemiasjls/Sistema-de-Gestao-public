namespace TransportadoraSystem.Core.Entities;

// ── Estoque de produtosA e produtosB ────────────────────────────────────────
// A carga traz o CUSTO (R$/kg na produtoA, R$/pacote na produtoB) e muda a cada
// pedido. O cliente traz o PREÇO que ele paga. A diferença entre os dois é a
// comissão — é isso que a planilha de comissões faz na mão.
//
// Preço e custo são copiados para dentro da venda no momento em que ela é
// lançada. Sem isso, mudar o preço de um cliente reescreveria a comissão de
// todas as vendas passadas dele.

public class EstoqueCliente : BaseEntity
{
    public string Nome { get; set; } = string.Empty;
    public string? Cidade { get; set; }          // sai no romaneio
    public decimal PrecoProdutoAKg { get; set; }
    // Frete cobrado por pacote de produtoB. Zero para quem não paga frete.
    // É o mesmo valor que a viagem do caminhão cobra (painel de preços, VIA-10).
    public decimal FreteProdutoB { get; set; }
    // Frete por pacote de produtoC (100 produtosC); nulo = o da produtoB.
    public decimal? FreteProdutoC { get; set; }
    public bool Ativo { get; set; } = true;

    public ICollection<EstoqueClientePreco> PrecosProdutoB { get; set; } = [];

    public decimal FreteDaProdutoC => FreteProdutoC ?? FreteProdutoB;
}

/// <summary>Os dois produtos do controle de pacotes: produtosB e produtosC (EST-07).</summary>
public static class ProdutoPacote
{
    public const string ProdutoB = "PRODUTOB";
    public const string ProdutoC = "PRODUTOC";
    public static string Normalizar(string? p) => p?.Trim().ToUpperInvariant() == ProdutoC ? ProdutoC : ProdutoB;
}

public class TipoProdutoB : BaseEntity
{
    // Códigos que o sistema procura (controle de carregamento e volumes embalados)
    public const string CodigoAmarela = "AMARELA";
    public const string CodigoBranca = "BRANCA";
    public const string CodigoProdutoC = "PRODUTOC";

    public string Nome { get; set; } = string.Empty;
    public int UnidadesPorPacote { get; set; } = 150;
    public bool Ativo { get; set; } = true;
    public string? Codigo { get; set; }
    // PRODUTOB ou PRODUTOC: cada produto tem as suas cargas e saídas
    public string Produto { get; set; } = ProdutoPacote.ProdutoB;
}

// Preço do cliente para um tipo de produtoB (Gde Br, Gde Vr, Mdo Vr...)
public class EstoqueClientePreco : BaseEntity
{
    public int ClienteId { get; set; }
    public EstoqueCliente? Cliente { get; set; }
    public int TipoProdutoBId { get; set; }
    public TipoProdutoB? Tipo { get; set; }
    public decimal Preco { get; set; }
}

// ── ProdutosA ───────────────────────────────────────────────────────────────

public class CargaProdutoA : BaseEntity
{
    public DateOnly Data { get; set; }
    public string Fornecedor { get; set; } = string.Empty;
    public int Quantidade { get; set; }        // produtosA que vieram na carga
    public decimal ValorTotal { get; set; }    // valor do pedido, já com frete
    public decimal CustoKg { get; set; }       // R$/kg desta carga
    public string? Controle { get; set; }
    public string? Observacao { get; set; }
    public bool Encerrada { get; set; }

    public ICollection<VendaProdutoA> Vendas { get; set; } = [];
}

// Uma saída: várias produtosA do mesmo cliente, no mesmo dia
public class VendaProdutoA : BaseEntity
{
    public int CargaId { get; set; }
    public CargaProdutoA? Carga { get; set; }
    public int ClienteId { get; set; }
    public EstoqueCliente? Cliente { get; set; }
    public DateOnly Data { get; set; }
    public int? Espessura { get; set; }
    public int? Numero { get; set; }       // nº do romaneio, gravado ao exportar
    public decimal PrecoKg { get; set; }   // do cliente, congelado na venda
    public decimal CustoKg { get; set; }   // da carga, congelado na venda
    public string? Observacao { get; set; }

    // Portal das produtosA (EST-09): quem anotou no celular e quando. Nulo = lançada no sistema.
    public int? AnotadaPorUsuarioId { get; set; }
    public DateTime? AnotadaEm { get; set; }

    public ICollection<ProdutoAVendida> ProdutosA { get; set; } = [];
}

public class ProdutoAVendida : BaseEntity
{
    public int VendaId { get; set; }
    public VendaProdutoA? Venda { get; set; }
    public int PesoGramas { get; set; }    // como vem da balança: 12740
}

// ── ProdutosB ────────────────────────────────────────────────────────────

public class CargaProdutoB : BaseEntity
{
    public DateOnly Data { get; set; }
    // PRODUTOB ou PRODUTOC (EST-07: produtoC tem cargas e saídas próprias)
    public string Produto { get; set; } = ProdutoPacote.ProdutoB;
    public string Fornecedor { get; set; } = string.Empty;
    public int QuantidadePacotes { get; set; }   // pacotes comprados (define o custo)
    public decimal ValorProdutosB { get; set; }
    public decimal ValorFrete { get; set; }
    public string? Transportador { get; set; }
    public string? Observacao { get; set; }
    public bool Encerrada { get; set; }

    public ICollection<CargaProdutoBItem> Itens { get; set; } = [];
    public ICollection<VendaProdutoB> Vendas { get; set; } = [];
}

// Quanto entrou de cada tipo. Pode ser maior que a quantidade comprada, porque
// entra também a sobra do pedido anterior.
public class CargaProdutoBItem : BaseEntity
{
    public int CargaId { get; set; }
    public CargaProdutoB? Carga { get; set; }
    public int TipoProdutoBId { get; set; }
    public TipoProdutoB? Tipo { get; set; }
    public int Quantidade { get; set; }
}

public class VendaProdutoB : BaseEntity
{
    public int CargaId { get; set; }
    public CargaProdutoB? Carga { get; set; }
    public int ClienteId { get; set; }
    public EstoqueCliente? Cliente { get; set; }
    public DateOnly Data { get; set; }
    public int? Numero { get; set; }           // nº do romaneio, gravado ao exportar
    public decimal CustoPacote { get; set; }   // da carga, congelado na venda
    public decimal FretePacote { get; set; }   // do cliente, congelado na venda — não entra na comissão
    public string? Observacao { get; set; }
    // De onde veio a saída: controle de carregamento (por cliente) ou uso nos
    // volumes embalados (controle do barracão). As duas nulas = lançada à mão.
    public int? CarregamentoId { get; set; }
    public int? VolumesControleId { get; set; }
    // Já cobrada noutro documento (produtoC da BRZ no romaneio da nota, NFE-05):
    // nome do romaneio. A saída conta no estoque, mas não se cobra de novo.
    public string? CobradaNoRomaneio { get; set; }

    public ICollection<VendaProdutoBItem> Itens { get; set; } = [];
}

public class VendaProdutoBItem : BaseEntity
{
    public int VendaId { get; set; }
    public VendaProdutoB? Venda { get; set; }
    public int TipoProdutoBId { get; set; }
    public TipoProdutoB? Tipo { get; set; }
    public int Pacotes { get; set; }
    public int Unidades { get; set; }    // avulsas: 150 delas fecham um pacote
    public decimal Preco { get; set; }   // R$/pacote do cliente, congelado
}
