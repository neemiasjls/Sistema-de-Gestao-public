namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// O controle de pedágio Sem Parar de um mês, no lugar da planilha
/// "N-SEM PARAR -MÊS-AA.xlsx": fatura por placa, vales-pedágio (VPO) e créditos
/// inseridos. Vem do relatório de VPO + fatura em PDF, de uma planilha pronta
/// ou é montado à mão; a planilha é sempre gerada a partir destes dados.
/// </summary>
public class SemPararControle
{
    public int Id { get; set; }
    public int Ano { get; set; }
    public int Mes { get; set; }
    // Placas na ordem das colunas da planilha, separadas por vírgula ("XAA-9457,XAD-5H46,...")
    public string Placas { get; set; } = "";
    public string Origem { get; set; } = "manual";   // gerado, planilha ou manual
    // Mudado pela tela depois de gerar/importar: reimportar só com confirmação
    public bool Editado { get; set; }
    public string AvisosJson { get; set; } = "[]";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    public List<SemPararLinha> Linhas { get; set; } = [];
    public List<SemPararArquivo> Arquivos { get; set; } = [];
}

/// <summary>
/// Uma linha do controle. FATURA: o valor da placa no resumo da fatura (uma por
/// placa). VPO: um vale-pedágio, com a data; linhas com a mesma Ordem saem na mesma
/// linha da planilha. CREDITO: um crédito inserido; Ordem é a posição na coluna da placa.
/// </summary>
public class SemPararLinha
{
    public int Id { get; set; }
    public int ControleId { get; set; }
    public SemPararControle? Controle { get; set; }
    public string Tipo { get; set; } = "VPO";
    public string Placa { get; set; } = "";
    public DateOnly? Data { get; set; }
    public decimal Valor { get; set; }
    public int Ordem { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Arquivo de origem do controle (planilha importada, relatório de VPO ou fatura), guardado como veio.</summary>
public class SemPararArquivo
{
    public int Id { get; set; }
    public int ControleId { get; set; }
    public SemPararControle? Controle { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "PLANILHA";   // PLANILHA, VPO ou FATURA
    public byte[] Conteudo { get; set; } = [];
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
}
