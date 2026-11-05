using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Guarda as planilhas SG (Seguro / ICMS) no sistema, uma por mês: a gerada
/// na tela de CT-es e as trazidas do Drive. Guardar o mesmo mês de novo troca
/// o que estava lá e avisa.
/// </summary>
public class PlanilhaSeguroService(AppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Os valores digitados na tela antes de gerar.</summary>
    public record ValoresInformados(decimal Das, decimal IofSeguros, decimal ServicoEscritorio, decimal TaxaBSoft);

    public record Guardada(PlanilhaSeguro Registro, PlanilhaSeguroLeitor.Resumo Resumo, bool Substituiu, string? OrigemAnterior);

    /// <summary>
    /// Lê a planilha e guarda. Mês e ano: os informados (a geração sabe de que
    /// mês é) ou os que o leitor achou (nome do arquivo, célula MÊS REFERENTE
    /// ou datas dos CT-es). Sem mês reconhecido, não guarda.
    /// </summary>
    public async Task<Guardada> GuardarAsync(byte[] xlsx, string nome, int? mes, int? ano, string origem,
        ValoresInformados? valores, CancellationToken ct = default)
    {
        var resumo = PlanilhaSeguroLeitor.Ler(xlsx, nome);
        int m = mes ?? resumo.Mes ?? 0, a = ano ?? resumo.Ano ?? 0;
        if (m is < 1 or > 12 || a is < 2000 or > 2100)
            throw new InvalidOperationException(
                "Não reconheci o mês da planilha: nem pelo nome (\"08-SG AGOSTO 2026.xlsx\"), nem pela célula MÊS REFERENTE, nem pelas datas dos CT-es.");
        if (mes != null && ano != null && (resumo.Mes != m || resumo.Ano != a))
        {
            resumo.Mes = m; resumo.Ano = a; resumo.OrigemMes = "geracao";
        }

        var atual = await db.PlanilhasSeguro.FirstOrDefaultAsync(p => p.Ano == a && p.Mes == m, ct);
        var origemAnterior = atual?.Origem;
        var reg = atual ?? new PlanilhaSeguro { Mes = m, Ano = a, CriadoEm = DateTime.UtcNow };
        Preencher(reg, xlsx, nome, origem, resumo, valores);
        if (atual == null) db.PlanilhasSeguro.Add(reg);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (atual == null)
        {
            // Outro pedido guardou o mesmo mês entre a busca e a gravação (o
            // índice único do mês barrou a segunda): troca o que ele guardou.
            db.Entry(reg).State = EntityState.Detached;
            var outro = await db.PlanilhasSeguro.FirstAsync(p => p.Ano == a && p.Mes == m, ct);
            origemAnterior = outro.Origem;
            Preencher(outro, xlsx, nome, origem, resumo, valores);
            await db.SaveChangesAsync(ct);
            return new Guardada(outro, resumo, true, origemAnterior);
        }

        return new Guardada(reg, resumo, atual != null, origemAnterior);
    }

    private static void Preencher(PlanilhaSeguro reg, byte[] xlsx, string nome, string origem,
        PlanilhaSeguroLeitor.Resumo resumo, ValoresInformados? valores)
    {
        reg.Nome = nome;
        reg.Conteudo = xlsx;
        reg.Origem = origem;
        reg.QuantidadeCtes = resumo.QuantidadeCtes;
        reg.TotalValorNF = resumo.TotalValorNF;
        reg.TotalFrete = resumo.TotalFrete;
        reg.TotalSeguro = resumo.TotalSeguro;
        reg.TotalIcms = resumo.TotalIcms;
        reg.TotalDespesas = resumo.TotalDespesas;
        reg.ResumoJson = JsonSerializer.Serialize(resumo, Json);
        // Importada: o que vale é o que está dentro da planilha, não sobra
        // valor digitado de uma geração anterior do mesmo mês.
        reg.Das = valores?.Das;
        reg.IofSeguros = valores?.IofSeguros;
        reg.ServicoEscritorio = valores?.ServicoEscritorio;
        reg.TaxaBSoft = valores?.TaxaBSoft;
        reg.AtualizadoEm = DateTime.UtcNow;
    }

    /// <summary>
    /// O resumo guardado. Se o JSON não abrir (gravado por uma versão antiga,
    /// por exemplo), lê de novo a própria planilha.
    /// </summary>
    public static PlanilhaSeguroLeitor.Resumo ResumoDe(PlanilhaSeguro reg)
    {
        try
        {
            var r = JsonSerializer.Deserialize<PlanilhaSeguroLeitor.Resumo>(reg.ResumoJson, Json);
            if (r != null) return r;
        }
        catch (JsonException) { /* lê da planilha abaixo */ }
        var lido = PlanilhaSeguroLeitor.Ler(reg.Conteudo, reg.Nome);
        lido.Mes = reg.Mes; lido.Ano = reg.Ano;
        return lido;
    }

    public static string MesAno(int mes, int ano) => $"{PlanilhaSeguroLeitor.NomeMes(mes)} de {ano}";
}
