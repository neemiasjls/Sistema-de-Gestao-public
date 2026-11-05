namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Um acerto semanal com a Fazenda Alfa. Os dados, com a cara da planilha
/// (romaneios com desconto por caixa, pagamentos e depósitos, observações), ficam
/// em DadosJson e são editáveis pela tela; vêm dos PDFs que chegam ao longo da
/// semana ou de uma planilha feita no Excel. A planilha do acerto é gerada a
/// partir deles, então o Excel é só a saída.
/// </summary>
public class AcertoAlfa
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public DateOnly Semana { get; set; }           // segunda-feira da semana
    public string DadosJson { get; set; } = "{}";  // { romaneios, pagamentos, notas } (ver AcertoAlfaService)
    public string AvisosJson { get; set; } = "[]"; // avisos da leitura dos PDFs
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public List<AcertoAlfaArquivo> Arquivos { get; set; } = [];
}

/// <summary>Um arquivo enviado para o acerto (PDF ou planilha importada), guardado como veio.</summary>
public class AcertoAlfaArquivo
{
    public int Id { get; set; }
    public int AcertoId { get; set; }
    public AcertoAlfa? Acerto { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "OUTRO";    // BOLETO, ROMANEIO, PLANILHA ou OUTRO
    public byte[] Conteudo { get; set; } = [];
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    // Quando os romaneios deste PDF foram lidos com os produtos (RomaneiosFazendaLidos);
    // nulo = ainda não. Só vale para Tipo = ROMANEIO.
    public DateTime? RomaneiosLidosEm { get; set; }
}
