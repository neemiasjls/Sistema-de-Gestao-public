namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Um romaneio da fazenda (PDF "BENTO ...") guardado num acerto Alfa, lido uma
/// vez com os produtos. Com DescFunrural é de cliente; sem, são volumes para embalar
/// no barracão e viram ENTRADA de um controle de volumes embalados: a linha de
/// entrada tem Origem = "romaneio:NUMERO" (uma só por romaneio). Qual controle
/// recebeu é a própria linha; aqui fica só o que não se deduz dela.
/// </summary>
public class RomaneioFazendaLido
{
    public int Id { get; set; }
    public int Numero { get; set; }                 // "CONTROLE" da fazenda (133423)
    public DateOnly Data { get; set; }              // emissão
    public int? AcertoId { get; set; }
    public int? ArquivoId { get; set; }             // PDF de onde foi lido
    public bool DescFunrural { get; set; }
    public int Caixas { get; set; }
    public decimal Valor { get; set; }
    public string ItensJson { get; set; } = "[]";   // RomaneioFazendaLeitor.Item[]
    // Tirado à mão do controle de volumes: não volta sozinho para nenhum controle.
    public bool Retirado { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
