namespace TransportadoraSystem.Core.Entities;

/// <summary>
/// Planilha SG (Seguro / ICMS) de um mês, guardada como saiu: a gerada na tela
/// de CT-es ou a trazida do Drive. Um registro por mês/ano — gerar ou importar
/// o mesmo mês de novo troca o que estava guardado.
/// </summary>
public class PlanilhaSeguro
{
    public int Id { get; set; }
    public int Mes { get; set; }
    public int Ano { get; set; }
    public string Nome { get; set; } = "";              // "08-SG AGOSTO 2026.xlsx"
    public byte[] Conteudo { get; set; } = [];
    public string Origem { get; set; } = "gerada";      // "gerada" (tela de CT-es) ou "importada"

    // Totais lidos da planilha, em coluna para a lista não precisar abrir o JSON
    public int QuantidadeCtes { get; set; }
    public decimal TotalValorNF { get; set; }
    public decimal TotalFrete { get; set; }
    public decimal TotalSeguro { get; set; }
    public decimal TotalIcms { get; set; }
    public decimal? TotalDespesas { get; set; }         // o TOTAL DESPESAS da aba RESUMO

    // O resumo inteiro lido da planilha (totais, aba RESUMO, por placa, avisos)
    public string ResumoJson { get; set; } = "";

    // Valores digitados na tela quando a planilha foi gerada pelo sistema.
    // Na importada ficam vazios: o que vale é o que está dentro da planilha.
    public decimal? Das { get; set; }
    public decimal? IofSeguros { get; set; }
    public decimal? ServicoEscritorio { get; set; }
    public decimal? TaxaBSoft { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;
}
