namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Cálculo de frete/pedágio salvo pelo usuário na tela de Frete/Líquota.
/// Identificado por placa + dia. O PayloadJson guarda tudo (inputs + resultado)
/// para reabrir o cálculo completo depois.
/// </summary>
public class CalculoFreteSalvo
{
    public int Id { get; set; }
    public string Placa { get; set; } = "";
    public decimal ValorCarga { get; set; }
    public decimal ValorPedagio { get; set; }
    public decimal PesoTotal { get; set; }
    public decimal LiquotaBruta { get; set; }
    public decimal LiquotaArredondada { get; set; }
    public int TotalNFs { get; set; }
    public int TotalCTes { get; set; }
    public string PayloadJson { get; set; } = "";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Planilha de romaneio gerada a partir dos XMLs e guardada no sistema, para
/// poder abrir e baixar de novo sem ter os arquivos em mãos. Fica ligada à
/// carga quando foi gerada numa; gerada solta (tela Planilhas NF-e), ou com a
/// carga excluída, fica sem carga mas continua guardada.
/// </summary>
public class PlanilhaCarga
{
    public int Id { get; set; }
    public int? CalculoId { get; set; }
    public CalculoFreteSalvo? Calculo { get; set; }
    public string Nome { get; set; } = "";
    public byte[] Conteudo { get; set; } = [];
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Arquivos de uma NF-e guardados no sistema: o XML e o DANFE em PDF. A chave
/// de acesso é a identidade — subir de novo atualiza, não duplica.
/// </summary>
public class ArquivoNFe
{
    public int Id { get; set; }
    public string ChaveAcesso { get; set; } = "";
    public string NumeroNF { get; set; } = "";
    public DateOnly? DataEmissao { get; set; }
    public string Emitente { get; set; } = "";
    public string CnpjEmit { get; set; } = "";
    public string Destinatario { get; set; } = "";
    public string CidadeDestino { get; set; } = "";
    public string Uf { get; set; } = "";
    public decimal ValorNF { get; set; }

    public string NomeXml { get; set; } = "";
    public byte[] Xml { get; set; } = [];
    public string? NomePdf { get; set; }
    public byte[]? Pdf { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
