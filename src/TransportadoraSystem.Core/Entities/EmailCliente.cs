namespace TransportadoraSystem.Core.Entities;

// E-mail semanal para os clientes que recebem o romaneio e os boletos por
// e-mail (Cedro Frutas e Dunas): os destinatários de cada um e o histórico do
// que já foi mandado.

/// <summary>Destinatários padrão de um cliente. Um registro por cliente.</summary>
public class EmailClienteConfig
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";          // "CEDRO FRUTAS" ou "DUNAS"
    public string Destinatarios { get; set; } = "";    // separados por ";"
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}

/// <summary>Um envio (ou tentativa) do e-mail de um cliente, com o que foi junto.</summary>
public class EmailClienteEnvio
{
    public int Id { get; set; }
    public string Cliente { get; set; } = "";
    public DateOnly DataCarga { get; set; }
    public string Assunto { get; set; } = "";
    public string Destinatarios { get; set; } = "";    // os do cliente, separados por ";"
    public string EnviadoPara { get; set; } = "";      // para onde foi de fato (no modo de teste, o endereço de teste)
    public string Anexos { get; set; } = "";           // nomes dos arquivos do .zip, um por linha
    public string NomeZip { get; set; } = "";
    public int TamanhoZip { get; set; }
    public bool ModoTeste { get; set; }
    public string? MessageId { get; set; }
    public string? Erro { get; set; }                  // null quando o Brevo aceitou
    public DateTime EnviadoEm { get; set; } = DateTime.UtcNow;
}
