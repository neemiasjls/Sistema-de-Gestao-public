using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Guarda os arquivos de cada NF-e — o XML e o DANFE em PDF — para poder baixar
/// depois sem depender da pasta de onde vieram.
///
/// A chave de acesso é a identidade da nota: subir o mesmo arquivo de novo
/// atualiza o que já está guardado em vez de duplicar.
/// </summary>
public class ArquivoNFeService(AppDbContext db)
{
    public record Resultado(int XmlsNovos, int XmlsAtualizados, int PdfsVinculados,
        List<string> Avisos);

    // O PDF não traz a chave no conteúdo de forma confiável, então o vínculo sai
    // do nome do arquivo: DANFE_144490_1.pdf casa com a NF 144490. Alguns
    // sistemas nomeiam pela chave inteira, que também é aceita.
    private static readonly Regex Numeros = new(@"\d{2,}", RegexOptions.Compiled);

    public async Task<Resultado> GuardarAsync(IEnumerable<(string Nome, byte[] Conteudo)> arquivos)
    {
        var avisos = new List<string>();
        var lista = arquivos.ToList();

        var xmls = lista.Where(a => a.Nome.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();
        var pdfs = lista.Where(a => a.Nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).ToList();

        int novos = 0, atualizados = 0;

        foreach (var (nome, conteudo) in xmls)
        {
            NFeData dados;
            string chave;
            try
            {
                var texto = System.Text.Encoding.UTF8.GetString(conteudo);
                dados = NFeImportService.ParseXml(texto);
                chave = ExtrairChave(texto);
            }
            catch
            {
                avisos.Add($"{nome}: não consegui ler como NF-e (pulei).");
                continue;
            }

            if (chave.Length != 44)
            {
                avisos.Add($"{nome}: sem chave de acesso no XML (pulei).");
                continue;
            }

            var existente = await db.ArquivosNFe.FirstOrDefaultAsync(a => a.ChaveAcesso == chave);
            if (existente == null)
            {
                db.ArquivosNFe.Add(Preencher(new ArquivoNFe { ChaveAcesso = chave }, dados, nome, conteudo));
                novos++;
            }
            else
            {
                Preencher(existente, dados, nome, conteudo);
                existente.AtualizadoEm = DateTime.UtcNow;
                atualizados++;
            }
        }

        if (novos > 0 || atualizados > 0) await db.SaveChangesAsync();

        int vinculados = 0;
        foreach (var (nome, conteudo) in pdfs)
        {
            var alvo = await AcharNota(nome, avisos);
            if (alvo == null) continue;

            alvo.NomePdf = nome;
            alvo.Pdf = conteudo;
            alvo.AtualizadoEm = DateTime.UtcNow;
            vinculados++;
        }

        if (vinculados > 0) await db.SaveChangesAsync();

        return new Resultado(novos, atualizados, vinculados, avisos);
    }

    private async Task<ArquivoNFe?> AcharNota(string nomePdf, List<string> avisos)
    {
        var pedacos = Numeros.Matches(nomePdf).Select(m => m.Value).ToList();

        // Nome com a chave inteira resolve sozinho.
        var chave = pedacos.FirstOrDefault(p => p.Length == 44);
        if (chave != null)
        {
            var porChave = await db.ArquivosNFe.FirstOrDefaultAsync(a => a.ChaveAcesso == chave);
            if (porChave != null) return porChave;
        }

        // Senão, o número da nota. Dois emitentes podem ter a mesma numeração,
        // então só vincula quando a resposta é uma só.
        foreach (var numero in pedacos.Where(p => p.Length is >= 3 and <= 9).OrderByDescending(p => p.Length))
        {
            var candidatos = await db.ArquivosNFe
                .Where(a => a.NumeroNF == numero)
                .Take(2)
                .ToListAsync();

            if (candidatos.Count == 1) return candidatos[0];
            if (candidatos.Count > 1)
            {
                avisos.Add($"{nomePdf}: mais de uma NF com o número {numero} — não vinculei.");
                return null;
            }
        }

        avisos.Add($"{nomePdf}: não achei a NF-e correspondente. Suba o XML dela antes.");
        return null;
    }

    private static ArquivoNFe Preencher(ArquivoNFe a, NFeData d,
        string nome, byte[] conteudo)
    {
        a.NumeroNF = d.NumeroNF;
        a.DataEmissao = d.DataEmissao.HasValue ? DateOnly.FromDateTime(d.DataEmissao.Value) : null;
        a.Emitente = d.EmitOriginal;
        a.CnpjEmit = d.CnpjEmit;
        a.Destinatario = d.DestOriginal;
        a.CidadeDestino = d.CidadeDestino;
        a.Uf = d.Uf;
        a.ValorNF = d.ValorNF ?? 0;
        a.NomeXml = nome;
        a.Xml = conteudo;
        return a;
    }

    private static string ExtrairChave(string xml)
    {
        var doc = XDocument.Parse(xml);
        var inf = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infNFe");
        var id = inf?.Attribute("Id")?.Value ?? "";
        return new string(id.Where(char.IsDigit).ToArray());
    }
}
