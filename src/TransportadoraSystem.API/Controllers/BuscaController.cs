using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Busca única (F-02): um texto só procura em notas, CT-es, romaneios, placas,
/// clientes, boletos (número, nosso número ou valor), controles de volumes, acertos e
/// faturas de posto. Devolve no máximo alguns por grupo, cada um com o endereço da
/// tela onde ele mora. Só lê.
/// </summary>
[Authorize]
[ApiController]
[Route("api/busca")]
public partial class BuscaController(AppDbContext db) : ControllerBase
{
    private const int PorGrupo = 6;
    private const int MinCaracteres = 2;
    private const int MaxCaracteres = 80;

    public record Item(string Titulo, string? Detalhe, string Link, decimal? Valor = null, DateOnly? Data = null, string? Marca = null);
    public record Grupo(string Chave, string Titulo, List<Item> Itens, bool Mais);

    [GeneratedRegex(@"[^0-9]")] private static partial Regex NaoDigito();
    [GeneratedRegex(@"[^A-Z0-9]")] private static partial Regex NaoPlaca();
    [GeneratedRegex(@"^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$")] private static partial Regex FormatoPlaca();

    [HttpGet]
    public async Task<IActionResult> Buscar([FromQuery] string? q)
    {
        var texto = (q ?? "").Trim();
        if (texto.Length > MaxCaracteres) texto = texto[..MaxCaracteres];
        if (texto.Length < MinCaracteres) return Ok(new { Q = texto, Grupos = Array.Empty<Grupo>() });

        var digitos = NaoDigito().Replace(texto, "");
        // Só número (com ponto, traço ou espaço no meio): "144.827", "668-8"
        var soNumero = digitos.Length > 0 && texto.All(ch => char.IsDigit(ch) || ch is '.' or '-' or ' ' or '/');
        var placa = NaoPlaca().Replace(texto.ToUpperInvariant(), "");
        var ehPlaca = FormatoPlaca().IsMatch(placa);
        var valor = Valor(texto);
        var like = $"%{Escapar(texto)}%";
        var likeDigitos = $"%{Escapar(digitos)}%";
        var likePlaca = $"%{Escapar(placa)}%";
        // Texto curto demais para "contém" em número: "12" acharia metade das notas
        var numeroLongo = digitos.Length >= 3;

        var grupos = new List<Grupo>();
        void Add(string chave, string titulo, List<Item> itens)
        {
            if (itens.Count == 0) return;
            grupos.Add(new Grupo(chave, titulo, itens.Take(PorGrupo).ToList(), itens.Count > PorGrupo));
        }

        // ── Controle de volumes: pelo número ("330") ─────────────────────
        if (soNumero && int.TryParse(digitos, out var numeroInt))
        {
            var volumes = await db.VolumesControles.AsNoTracking()
                .Where(c => c.Numero == numeroInt)
                .OrderByDescending(c => c.Data).Take(PorGrupo + 1)
                .Select(c => new { c.Id, c.Numero, c.Data })
                .ToListAsync();
            Add("volumes", "Controles de volumes", volumes.Select(c => new Item($"Controle {c.Numero}", "Volumes embalados", $"/estoque/volumes/{c.Id}", Data: c.Data)).ToList());

            var acertos = await db.AcertosAlfa.AsNoTracking()
                .Where(a => a.Numero == numeroInt)
                .Take(PorGrupo + 1)
                .Select(a => new { a.Numero, a.Semana })
                .ToListAsync();
            Add("acertos", "Acertos Alfa", acertos.Select(a => new Item($"Acerto {a.Numero}", $"Semana de {a.Semana:dd/MM/yyyy}", "/acerto-alfa", Data: a.Semana)).ToList());

            var fazenda = await db.RomaneiosFazendaLidos.AsNoTracking()
                .Where(r => r.Numero == numeroInt)
                .Take(PorGrupo + 1)
                .Select(r => new { r.Numero, r.Data, r.Caixas, r.Valor, r.DescFunrural })
                .ToListAsync();
            Add("fazenda", "Romaneios da fazenda", fazenda.Select(r => new Item($"Romaneio da fazenda {r.Numero}",
                r.DescFunrural ? $"{r.Caixas} caixas · de cliente" : $"{r.Caixas} caixas · para embalar",
                r.DescFunrural ? "/acerto-alfa" : "/estoque/volumes", r.Valor, r.Data)).ToList());
        }

        // ── Notas fiscais: número, chave ou destinatário/emitente ─────
        {
            var notas = db.ArquivosNFe.AsNoTracking();
            notas = soNumero && digitos.Length > 0
                ? (numeroLongo
                    ? notas.Where(n => EF.Functions.ILike(n.NumeroNF, likeDigitos) || EF.Functions.ILike(n.ChaveAcesso, likeDigitos))
                    : notas.Where(n => n.NumeroNF == digitos))
                : notas.Where(n => EF.Functions.ILike(n.Destinatario, like) || EF.Functions.ILike(n.Emitente, like) || EF.Functions.ILike(n.CidadeDestino, like));
            var lista = await notas.OrderByDescending(n => n.DataEmissao).Take(PorGrupo + 1)
                .Select(n => new { n.NumeroNF, n.Destinatario, n.CidadeDestino, n.Uf, n.ValorNF, n.DataEmissao })
                .ToListAsync();
            Add("notas", "Notas fiscais", lista.Select(n => new Item($"Nota {n.NumeroNF}",
                Juntar(n.Destinatario, Juntar(n.CidadeDestino, n.Uf, "/"), " · "), "/notas", n.ValorNF, n.DataEmissao)).ToList());
        }

        // ── CT-es: número, nota, chave ou destinatário ────────────────
        {
            var ctes = db.CTes.AsNoTracking();
            ctes = soNumero && digitos.Length > 0
                ? (numeroLongo
                    ? ctes.Where(c => EF.Functions.ILike(c.NumeroCTe, likeDigitos) || EF.Functions.ILike(c.NumeroNF ?? "", likeDigitos) || EF.Functions.ILike(c.ChaveAcesso, likeDigitos))
                    : ctes.Where(c => c.NumeroCTe == digitos))
                : ehPlaca
                    ? ctes.Where(c => c.Veiculo != null && EF.Functions.ILike(c.Veiculo.Placa.Replace("-", "").Replace(" ", ""), likePlaca))
                    : ctes.Where(c => EF.Functions.ILike(c.Destinatario ?? "", like) || EF.Functions.ILike(c.ClienteNome ?? "", like) || EF.Functions.ILike(c.CidadeDestino ?? "", like));
            var lista = await ctes.OrderByDescending(c => c.DataEmissao).Take(PorGrupo + 1)
                .Select(c => new { c.NumeroCTe, c.NumeroNF, c.Destinatario, c.UFDestino, c.ValorFrete, c.DataEmissao, c.Status, Placa = c.Veiculo != null ? c.Veiculo.Placa : null })
                .ToListAsync();
            Add("ctes", "CT-es", lista.Select(c => new Item($"CT-e {c.NumeroCTe}",
                Juntar(Juntar(c.Destinatario, c.UFDestino, " · "), c.NumeroNF is { Length: > 0 } nf ? $"nota {nf}" : null, " · "),
                "/cte-export", c.ValorFrete, DateOnly.FromDateTime(c.DataEmissao.AddHours(-3)),
                c.Status == StatusCTe.Cancelado ? "Cancelado" : c.Placa)).ToList());
        }

        // ── Romaneios de cliente guardados: pelo nome do arquivo ──────
        // ("164--AURORA-29-09-2026.xlsx": número, cliente, data ou placa do .txt)
        {
            var termoNome = soNumero ? digitos : texto;
            var planilhas = await db.PlanilhasCarga.AsNoTracking()
                .Where(p => EF.Functions.ILike(p.Nome, $"%{Escapar(termoNome)}%")
                         || (ehPlaca && EF.Functions.ILike(p.Nome.Replace("-", ""), likePlaca)))
                .OrderByDescending(p => p.CriadoEm).Take(60)
                .Select(p => new { p.Nome, p.CriadoEm })
                .ToListAsync();
            var achados = planilhas
                .Select(p => (p.Nome, Id: RomaneioClienteLeitor.LerNome(p.Nome)))
                // Número curto: só o número do romaneio, não pedaço da data
                .Where(x => !soNumero || numeroLongo || x.Id.Numero?.ToString() == digitos)
                .OrderByDescending(x => x.Id.Data ?? DateTime.MinValue)
                .Select(x => new Item(
                    x.Id.Tipo == "frete" ? "Relatório de frete da Girassol" : x.Id.Numero is { } n ? $"Romaneio {n}" : "Romaneio",
                    x.Id.Tipo == "frete" ? Juntar(x.Id.Placa, null, "") : NomeCliente(x.Id.Apelido),
                    "/romaneios", Data: x.Id.Data is { } d ? DateOnly.FromDateTime(d) : null))
                .ToList();
            Add("romaneios", "Romaneios", achados);
        }

        // ── Placas: frota e cargas ────────────────────────────────────
        if (!soNumero && placa.Length >= 3)
        {
            var veiculos = await db.Veiculos.AsNoTracking()
                .Where(v => EF.Functions.ILike(v.Placa.Replace("-", "").Replace(" ", ""), likePlaca))
                .OrderBy(v => v.Placa).Take(PorGrupo + 1)
                .Select(v => new { v.Placa, v.Socio, v.Ativo })
                .ToListAsync();
            Add("placas", "Caminhões", veiculos.Select(v => new Item(v.Placa, $"Caminhão do {NomeDono(v.Socio)}{(v.Ativo ? "" : " · fora da frota")}",
                "/frete", Marca: v.Socio.ToString())).ToList());

            var cargas = await db.CalculosFreteSalvos.AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Placa.Replace("-", "").Replace(" ", ""), likePlaca))
                .OrderByDescending(c => c.CriadoEm).Take(PorGrupo + 1)
                .Select(c => new { c.Placa, c.CriadoEm, c.ValorCarga, c.TotalNFs })
                .ToListAsync();
            Add("cargas", "Cargas (Viagens)", cargas.Select(c => new Item($"Carga {c.Placa}",
                c.TotalNFs == 1 ? "1 nota" : $"{c.TotalNFs} notas", "/frete", c.ValorCarga, DateOnly.FromDateTime(c.CriadoEm.AddHours(-3)))).ToList());
        }

        // ── Clientes ──────────────────────────────────────────────────
        if (!soNumero)
        {
            var clientes = await db.Clientes.AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Nome, like) || EF.Functions.ILike(c.NomeFantasia ?? "", like))
                .OrderBy(c => c.Nome).Take(PorGrupo + 1)
                .Select(c => new { c.Nome, c.NomeFantasia, c.Cidade, c.UF })
                .ToListAsync();
            var estoque = await db.EstoqueClientes.AsNoTracking()
                .Where(c => EF.Functions.ILike(c.Nome, like))
                .OrderBy(c => c.Nome).Take(PorGrupo + 1)
                .Select(c => new { c.Nome, c.Cidade })
                .ToListAsync();
            Add("clientes", "Clientes",
                clientes.Select(c => new Item(c.NomeFantasia is { Length: > 0 } f ? f : c.Nome, Juntar(c.Cidade, c.UF, "/"), "/romaneios"))
                .Concat(estoque.Select(c => new Item(c.Nome, Juntar("ProdutosA e produtosB", c.Cidade, " · "), "/estoque/produtosA")))
                .ToList());
        }

        // ── Boletos: número, nosso número, quem, placa ou valor ───────
        {
            var termo = soNumero ? likeDigitos : like;
            var curto = soNumero && !numeroLongo;
            var postos = await db.BoletosPosto.AsNoTracking()
                .Where(b => (curto ? b.NumeroDocumento == digitos || b.NossoNumero == digitos
                                   : EF.Functions.ILike(b.NumeroDocumento, termo) || EF.Functions.ILike(b.NossoNumero, termo)
                                     || (!soNumero && EF.Functions.ILike(b.Posto, like)))
                         || (valor != null && b.Valor == valor))
                .OrderByDescending(b => b.Vencimento).Take(PorGrupo + 1)
                .Select(b => new { b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, b.Vencimento, b.PagoEm })
                .ToListAsync();
            var avulsos = await db.BoletosPagarAvulsos.AsNoTracking()
                .Where(b => (curto ? b.Numero == digitos
                                   : EF.Functions.ILike(b.Numero, termo)
                                     || (!soNumero && (EF.Functions.ILike(b.Beneficiario, like) || EF.Functions.ILike(b.Descricao, like))))
                         || (valor != null && b.Valor == valor))
                .OrderByDescending(b => b.Vencimento).Take(PorGrupo + 1)
                .Select(b => new { b.Beneficiario, b.Numero, b.Valor, b.Vencimento, b.PagoEm })
                .ToListAsync();
            var receber = await db.BoletosReceber.AsNoTracking()
                .Where(b => (curto ? b.NossoNumero == texto || b.Numero == digitos
                                   : EF.Functions.ILike(b.NossoNumero.Replace("-", ""), termo) || EF.Functions.ILike(b.Numero, termo)
                                     || (!soNumero && (EF.Functions.ILike(b.Pagador, like) || EF.Functions.ILike(b.Cliente, like)
                                         || (placa.Length >= 3 && EF.Functions.ILike(b.Placa.Replace("-", ""), likePlaca)))))
                         || (valor != null && b.Valor == valor))
                .OrderByDescending(b => b.Vencimento).Take(PorGrupo + 1)
                .Select(b => new { b.Cliente, b.NossoNumero, b.Numero, b.Placa, b.Valor, b.Vencimento, b.RecebidoEm })
                .ToListAsync();

            var itens = postos.Select(b => (Venc: b.Vencimento, Item: new Item(
                    $"A pagar · {b.Posto}", Juntar(b.NumeroDocumento != "" ? $"Documento {b.NumeroDocumento}" : null, Pago(b.PagoEm, "pago"), " · "),
                    "/boletos", b.Valor, b.Vencimento, b.PagoEm == null ? "Em aberto" : "Pago")))
                .Concat(avulsos.Select(b => (Venc: b.Vencimento, Item: new Item(
                    $"A pagar · {(b.Beneficiario == "" ? "Boleto avulso" : b.Beneficiario)}", Juntar(b.Numero != "" ? $"Número {b.Numero}" : null, Pago(b.PagoEm, "pago"), " · "),
                    "/boletos", b.Valor, b.Vencimento, b.PagoEm == null ? "Em aberto" : "Pago"))))
                .Concat(receber.Select(b => (Venc: b.Vencimento, Item: new Item(
                    $"A receber · {BoletoReceberLeitor.RotuloCliente(b.Cliente)}",
                    Juntar(b.NossoNumero != "" ? $"Nosso número {b.NossoNumero}" : null, b.Placa != "" ? $"placa {b.Placa}" : null, " · "),
                    "/boletos", b.Valor, b.Vencimento, b.RecebidoEm == null ? "Em aberto" : "Recebido"))))
                .OrderByDescending(x => x.Venc ?? DateOnly.MinValue)
                .Select(x => x.Item)
                .ToList();
            Add("boletos", "Boletos", itens);
        }

        // ── Faturas de posto ──────────────────────────────────────────
        {
            var faturas = await db.FaturasPosto.AsNoTracking()
                .Where(f => (soNumero ? (numeroLongo ? EF.Functions.ILike(f.Numero, likeDigitos) : f.Numero == digitos)
                                      : EF.Functions.ILike(f.Posto, like))
                         || (valor != null && f.ValorFatura == valor))
                .OrderByDescending(f => f.Emissao).Take(PorGrupo + 1)
                .Select(f => new { f.Posto, f.Numero, f.ValorFatura, f.Emissao, f.Quantidade })
                .ToListAsync();
            Add("faturas", "Faturas de posto", faturas.Select(f => new Item(
                f.Numero != "" ? $"Fatura {f.Numero}" : "Fatura sem número",
                Juntar(f.Posto, f.Quantidade == 1 ? "1 abastecimento" : $"{f.Quantidade} abastecimentos", " · "),
                "/abastecimentos", f.ValorFatura, f.Emissao)).ToList());
        }

        return Ok(new { Q = texto, Grupos = grupos });
    }

    // ── Apoio ─────────────────────────────────────────────────────────

    // "%" e "_" do texto valem como letra, não como curinga do LIKE
    private static string Escapar(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // "1.234,56", "1234,56", "R$ 250", "250.00": vira valor para procurar boleto e fatura.
    // Só com vírgula ou ponto de centavos, ou com R$: número inteiro solto é mais
    // provável ser nota, CT-e ou romaneio.
    private static decimal? Valor(string texto)
    {
        var t = texto.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Trim();
        var temMoeda = texto.Contains("R$", StringComparison.OrdinalIgnoreCase);
        if (t.Length == 0 || !t.All(ch => char.IsDigit(ch) || ch is '.' or ','))
            return null;
        string normal;
        if (Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})*,\d{1,2}$") || Regex.IsMatch(t, @"^\d+,\d{1,2}$"))
            normal = t.Replace(".", "").Replace(",", ".");
        else if (Regex.IsMatch(t, @"^\d+\.\d{2}$"))
            normal = t;
        else if (temMoeda && Regex.IsMatch(t, @"^\d{1,3}(\.\d{3})*$|^\d+$"))
            normal = t.Replace(".", "");
        else return null;
        return decimal.TryParse(normal, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    private static string? Juntar(string? a, string? b, string sep)
    {
        a = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
        b = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
        return a == null ? b : b == null ? a : $"{a}{sep}{b}";
    }

    private static string? Pago(DateOnly? em, string verbo) => em is { } d ? $"{verbo} em {d:dd/MM}" : null;

    private static string NomeDono(Socio s) => s switch
    {
        Socio.Bento => "Bento",
        Socio.Caio => "Caio",
        Socio.Dudu => "Davi",
        _ => s.ToString(),
    };

    private static string NomeCliente(string apelido) => apelido switch
    {
        "CEDRO FRUTAS" => "Cedro Frutas",
        "DUNAS" => "Dunas",
        "AURORA" => "Aurora",
        "FAROL" => "Farol",
        "BRZ" => "BRZ",
        _ => apelido,
    };
}
