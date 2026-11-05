namespace TransportadoraSystem.Core.Enums;

public enum Socio
{
    Bento = 1,
    Caio = 2,
    Dudu = 3
}

public enum StatusCTe
{
    Ativo = 1,
    Cancelado = 2
}

public enum StatusBoleto
{
    Pendente = 1,
    Pago = 2,
    Vencido = 3
}

public enum TipoProduto
{
    VolumesProdutoCDuzia = 1,
    VolumesProdutoC30 = 2,
    VolumesProdutoC20 = 3,
    ProdutosD = 4,
    ProdutosA = 5,
    ProdutosBAvulso = 6,
    Frete = 7
}

public enum TipoDespesa
{
    Combustivel = 1,
    Pedagio = 2,
    Diaria = 3,
    Manutencao = 4,
    Pneu = 5,
    Lavagem = 6,
    Outros = 7
}

public enum TipoLancamento
{
    Entrada = 1,
    Saida = 2,
    Transferencia = 3
}

public enum TipoCartaoFrete
{
    TipBank = 1,
    PamCard = 2
}

public enum TipoMovimentoEstoque
{
    Entrada = 1,
    Saida = 2
}

public enum CanalEnvio
{
    Email = 1,
    WhatsApp = 2
}
