namespace TransportadoraSystem.Core.Entities;

// ── Volumes embalados (controle do barracão) ─────────────────────────────────
// Substitui as planilhas "VOLUMES EMBALADOS\328--(23-09-2026).xlsx". Cada controle
// é uma produtoB: o dia em que embalaram, numerado como as planilhas.
//
// Unidade: a caixa da fazenda tem 12 produtosC. Os tipos crus (caixas da fazenda)
// ficam guardados em PRODUTOSC, porque sobra, quebra e retirada são faladas em
// produtosC e 1/12 de caixa não fecha em decimal (a planilha usava 0,0833). Os
// tipos produzidos (embalado e PVC) ficam em caixas.

public class ControleVolumes : BaseEntity
{
    public int Numero { get; set; }
    public DateOnly Data { get; set; }            // dia em que embalaram
    public string? Observacao { get; set; }

    // Caixas de PVC de 20 (PVCAB20) montadas com caixa de 30 volumes (Ab): trocam
    // os volumes de produtoC. Cada uma gasta 200 volumes = 200/360 de caixa Ab.
    public decimal PvcVinteComCaixaAb { get; set; }

    // Sem marcar, a sobra anterior é a sobra do controle anterior (lida na hora).
    // Marcada, vale a linha "sobra_anterior" gravada neste controle.
    public bool SobraAnteriorManual { get; set; }

    public string Origem { get; set; } = "manual";   // manual | planilha

    public ICollection<ControleVolumesLinha> Linhas { get; set; } = [];
}

// Uma linha da planilha. O grupo diz o que ela é; as colunas são as mesmas em
// todas. Entradas e saídas podem ter várias linhas; os demais grupos, uma.
public class ControleVolumesLinha : BaseEntity
{
    public const string Embalou = "embalou";
    public const string Entrada = "entrada";
    public const string Saida = "saida";
    public const string Sobra = "sobra";
    public const string Quebra = "quebra";
    public const string SobraAnterior = "sobra_anterior";

    // Origem da linha: null = lançada na aba (ou importada da planilha);
    // "anotacao" = venda/saída avulsa anotada junto com a produtoB. A tela de
    // anotar só mexe nas suas.
    public const string OrigemAnotacao = "anotacao";
    // "romaneio:133423" = entrada que veio do romaneio da fazenda sem funrural
    // (volumes para embalar), guardado no acerto Alfa. Uma linha por romaneio.
    public const string PrefixoOrigemRomaneio = "romaneio:";
    public static string OrigemRomaneio(int numero) => PrefixoOrigemRomaneio + numero;

    public int ControleId { get; set; }
    public ControleVolumes? Controle { get; set; }
    public string Grupo { get; set; } = Saida;
    public int Ordem { get; set; }
    public string? Nome { get; set; }       // cliente, pessoa ou fornecedor ("PAULO")
    public string? Placa { get; set; }
    public DateOnly? Data { get; set; }
    public string? Origem { get; set; }

    // Crus, em produtosC (12 por caixa)
    public decimal Ab { get; set; }
    public decimal Ab20 { get; set; }
    public decimal Bb { get; set; }
    public decimal Av { get; set; }

    // Produzidos, em caixas
    public decimal Abe { get; set; }       // AB      embalado branco
    public decimal Ave { get; set; }       // AV      embalado vermelho
    public decimal GrPvcBr { get; set; }   // PVCAB
    public decimal MdPvcBr { get; set; }   // PVCMB
    public decimal GrPvc20 { get; set; }   // PVCAB20
    public decimal GrPvcVr { get; set; }   // PVCV
}
