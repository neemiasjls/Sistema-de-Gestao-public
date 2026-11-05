namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Um tipo de volume como ele aparece no romaneio ("Grande Vr Pvc"). O nome é o
/// rótulo da linha do modelo de planilha: é por ele que a quantidade e o preço
/// da nota caem na linha certa.
/// </summary>
public class TipoVolume
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    // Só para leitura na tela ("Grande Branco Embalado"). O sistema continua
    // usando o Nome e as escritas; este aqui não entra em regra nenhuma.
    public string? NomeExtenso { get; set; }
    public string Cor { get; set; } = "OUTRO";   // BRANCO, VERMELHO ou OUTRO
    public int Ordem { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public List<EscritaVolume> Escritas { get; set; } = [];
}

/// <summary>
/// Um jeito de a nota escrever aquele tipo. Pode ser o código antes do traço
/// ("GV10X30") ou a descrição inteira, para fazenda que não usa código.
/// </summary>
public class EscritaVolume
{
    public int Id { get; set; }
    public int TipoVolumeId { get; set; }
    public TipoVolume? TipoVolume { get; set; }
    public string Texto { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
