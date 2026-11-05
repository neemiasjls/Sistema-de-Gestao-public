using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Guarda em BoletosReceber os boletos que a transportadora emite (CD e Girassol).
/// É o mesmo caminho para a tela Boletos (aba A receber) e para o upload único do
/// Acerto Alfa, que reconhece esses boletos no meio dos da fazenda e manda para cá.
///
/// O cliente vem do pagador impresso; se o PDF não disser, vale o da aba. O mesmo
/// PDF (pelo hash) não entra duas vezes; boleto de mesmo nosso número já lançado
/// sem PDF (vindo da planilha de controle) ganha o PDF e os dados lidos. Não grava:
/// quem chama dá o SaveChanges, para tudo sair numa transação só.
/// </summary>
public class BoletoReceberImportacao(AppDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Resultado de um arquivo. Situacao: NOVO, COMPLETADO, SUBSTITUIDO, REPETIDO ou RECUSADO.</summary>
    /// Novo: o que foi lido do PDF subido, quando ele bateu com um boleto já guardado
    /// (REPETIDO): a tela mostra os dois lado a lado antes de perguntar se substitui.
    public record Item(string Arquivo, string Situacao, BoletoReceber? Boleto, string? Pagador, List<string> Avisos,
        BoletoReceberLeitor.Lido? Novo = null);

    public record Resultado(List<Item> Itens, List<BoletoReceber> Guardados)
    {
        public int Novos => Itens.Count(i => i.Situacao == "NOVO");
        public int Completados => Itens.Count(i => i.Situacao == "COMPLETADO");
        public int Repetidos => Itens.Count(i => i.Situacao == "REPETIDO");
        public int Substituidos => Itens.Count(i => i.Situacao == "SUBSTITUIDO");
        public List<string> Avisos => Itens.SelectMany(i => i.Avisos).ToList();
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <param name="substituir">Boleto que já existe (mesmo PDF ou mesmo nosso número) é
    /// trocado pelo subido agora, em vez de ficar de fora. A tela pergunta antes.</param>
    public async Task<Resultado> Importar(IEnumerable<(string Nome, byte[] Bytes)> arquivos, string? clienteAba = null,
        bool substituir = false)
    {
        clienteAba = string.IsNullOrWhiteSpace(clienteAba) ? null : clienteAba.Trim().ToUpperInvariant();
        var placas = await db.Veiculos.AsNoTracking().Select(v => v.Placa).ToListAsync();
        var itens = new List<Item>();
        var guardados = new List<BoletoReceber>();
        var agora = DateTime.UtcNow;

        foreach (var (nome, bytes) in arquivos)
        {
            if (!nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            { itens.Add(new(nome, "RECUSADO", null, null, [$"{nome}: não é PDF (ficou de fora)."])); continue; }

            var hash = Hash(bytes);
            var mesmoArquivo = guardados.FirstOrDefault(b => b.Hash == hash)
                ?? await db.BoletosReceber.FirstOrDefaultAsync(b => b.Hash == hash);
            if (mesmoArquivo != null && !(substituir && !guardados.Contains(mesmoArquivo)))
            { itens.Add(new(nome, "REPETIDO", mesmoArquivo, mesmoArquivo.Pagador, [$"{nome}: esse boleto já estava guardado e ficou de fora."],
                BoletoReceberLeitor.Ler(bytes, nome, placas))); continue; }

            var lido = BoletoReceberLeitor.Ler(bytes, nome, placas);
            var avisosBoleto = new List<string>(lido.Avisos);
            var doCliente = lido.Cliente ?? clienteAba;
            if (doCliente == null)
            {
                itens.Add(new(nome, "RECUSADO", null, lido.Pagador,
                    [$"{nome}: não reconheci o pagador ({(lido.Pagador == "" ? "não lido" : lido.Pagador)}). Suba em Boletos, na aba do cliente."]));
                continue;
            }
            if (lido.Cliente != null && clienteAba != null && lido.Cliente != clienteAba)
                avisosBoleto.Add($"{nome}: subido na aba {BoletoReceberLeitor.RotuloCliente(clienteAba)}, mas o pagador é {lido.Pagador}; ficou em {BoletoReceberLeitor.RotuloCliente(lido.Cliente)}.");

            // Mesmo nosso número do mesmo cliente: se já tem PDF é repetido (ou é trocado,
            // quando pediram para substituir); sem PDF, completa
            BoletoReceber? existente = mesmoArquivo;
            if (existente == null && lido.NossoNumero != "")
                existente = guardados.FirstOrDefault(b => b.Cliente == doCliente && b.NossoNumero == lido.NossoNumero)
                    ?? await db.BoletosReceber.FirstOrDefaultAsync(b => b.Cliente == doCliente && b.NossoNumero == lido.NossoNumero);
            var trocar = existente != null && existente.Pdf != null;
            if (trocar && (!substituir || guardados.Contains(existente!)))
            {
                itens.Add(new(nome, "REPETIDO", existente, lido.Pagador,
                    [$"{nome}: o boleto {lido.NossoNumero} já estava guardado (arquivo {existente!.NomeArquivo}) e ficou de fora."], lido));
                continue;
            }

            var b = existente ?? new BoletoReceber { CriadoEm = agora };
            b.Cliente = doCliente;
            b.Sequencia = lido.Sequencia ?? b.Sequencia;
            if (lido.Numero != "") b.Numero = lido.Numero;
            if (lido.NossoNumero != "") b.NossoNumero = lido.NossoNumero;
            if (lido.Pagador != "") b.Pagador = lido.Pagador;
            if (lido.Placa != "") b.Placa = lido.Placa;
            b.Valor = lido.Valor ?? b.Valor;
            b.Vencimento = lido.Vencimento ?? b.Vencimento;
            b.Emissao = lido.Emissao ?? b.Emissao;
            b.DataCarga = lido.DataCarga ?? b.DataCarga;
            b.LidoDoPdf = lido.LidoDoPdf;
            b.AvisosJson = JsonSerializer.Serialize(avisosBoleto, Json);
            b.NomeArquivo = nome;
            b.Pdf = bytes;
            b.Hash = hash;
            b.AtualizadoEm = agora;
            if (trocar) b.SubstituidoEm = agora;
            if (existente == null) db.BoletosReceber.Add(b);
            guardados.Add(b);
            itens.Add(new(nome, existente == null ? "NOVO" : trocar ? "SUBSTITUIDO" : "COMPLETADO", b, lido.Pagador, avisosBoleto));
        }

        return new Resultado(itens, guardados);
    }
}
