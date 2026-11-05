using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

public class CTeImportResult
{
    public bool Sucesso { get; set; }
    public string? Erro { get; set; }
    public CTe? CTe { get; set; }
    public bool JaExistia { get; set; }
}

// Detalhe de uma NF individual dentro de um grupo
public class NfDetalhe
{
    public string Numero { get; set; } = "";
    public double PesoKg { get; set; }
    public string Arquivo { get; set; } = "";

    // Chave de acesso da NF-e: é por ela que a tela acha o PDF guardado, sem
    // depender do número, que pode se repetir entre emitentes.
    public string Chave  { get; set; } = "";
    public bool   TemPdf { get; set; }

    // Caminhão e motorista escritos na nota: a tela de frete preenche a placa
    // e o motorista da carga quando as notas que trazem a informação batem.
    public string Placa     { get; set; } = "";
    public string Motorista { get; set; } = "";
}

public class FreteCalculoInput
{
    public decimal ValorCarga { get; set; }
    public decimal ValorPedagio { get; set; }
    public List<ItemFreteCalculo> Itens { get; set; } = [];
}

public class ItemFreteCalculo
{
    public string? NumeroNF { get; set; }
    public string? Emitente { get; set; }
    public string? CidadeEmitente { get; set; }
    public string? Destinatario { get; set; }
    public string? CidadeDestino { get; set; }
    public string? UF { get; set; }
    public decimal PesoKg { get; set; }
    // NFs individuais do grupo (vem do import XML)
    public List<NfDetalhe>? NfsDetalhadas { get; set; }
}

public class FreteCalculoResult
{
    public decimal PesoTotal { get; set; }
    public decimal LiquotaBruta { get; set; }
    public decimal LiquotaArredondada { get; set; }
    public decimal TotalPesoXKm { get; set; }   // "Soma total dos índices" do script
    public int TotalNFs { get; set; }            // "Quantidade de NFs processadas" do script
    public List<ItemFreteResultado> Itens { get; set; } = [];
    public List<string> RotasNaoCadastradas { get; set; } = [];
}

public class ItemFreteResultado
{
    public string? NumeroNF { get; set; }
    public string? Emitente { get; set; }
    public string? Destinatario { get; set; }
    public string? CidadeOrigem { get; set; }
    public string? CidadeDestino { get; set; }
    public string? UF { get; set; }
    public decimal PesoKg { get; set; }
    public int? DistanciaKm { get; set; }
    public decimal PesoXKm { get; set; }
    public decimal PercentualPedagio { get; set; }
    public decimal ValorPedagio { get; set; }
    public List<NfDetalhe> NfsDetalhadas { get; set; } = [];
}

public class CTeImportService(AppDbContext db)
{
    // Alíquotas ICMS por UF destino — idêntico ao ALIQUOTAS dos scripts Python
    private static readonly Dictionary<string, decimal> AliquotasICMS = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GO"] = 6.90m,
        ["DF"] = 6.90m,
        ["MG"] = 6.90m,
        ["TO"] = 12.10m,
        ["PA"] = 14.70m,
    };

    // Rotas agora persistidas no banco (tabela RotasDistancia).
    // O dict estático foi removido — use os métodos async abaixo.

    // Substituições de destinatários — chaves NORMALIZADAS, idêntico ao FRETE.py
    // (bug anterior: chaves não normalizadas nunca davam match)
    private static readonly Dictionary<string, string> SubstituicoesDestinatarios;
    private static readonly Dictionary<string, (string cidade, string uf)> SubstituicoesCidadeUF;

    static CTeImportService()
    {
        SubstituicoesDestinatarios = new Dictionary<string, string>
        {
            [NormalizarTextoFrete("MARIELA R DE C PEREIRA EIRELI")]        = "A. IPANEMA COMERCIAL DE HORTIFRUTIGRANJEIROS LTDA",
            [NormalizarTextoFrete("DISTRIBUIDORA DE ALIMENTOS CAMPESTRE LTDA")]  = "A. IPANEMA COMERCIAL DE HORTIFRUTIGRANJEIROS LTDA",
            [NormalizarTextoFrete("LX9 EMPREENDIMENTOS LTDA")]                 = "A. IPANEMA COMERCIAL DE HORTIFRUTIGRANJEIROS LTDA",
            [NormalizarTextoFrete("P.Q. R. SANTOS COMERCIO DE ALIMENTOS LTD")] = "IRINEU FERRAZ DE PRADO DISTRIBUIDORA PRADO",
            [NormalizarTextoFrete("VINHEDO DIST.DE HORTIFRUTIGRANJEIROS LTD")] = "MF AMARAL TRANSPORTE LTDA",
            [NormalizarTextoFrete("R. DE ALMEIDA LIMA COMERCIO LTDA")]       = "IRINEU FERRAZ DE PRADO DISTRIBUIDORA PRADO",
            [NormalizarTextoFrete("C.D. COMERCIO DE HORTIFRUTI LTDA")]   = "DISTRIBUIDORA DE HORTIGRANJEIROS VALE VERDE LTDA",
            [NormalizarTextoFrete("VERDURAO DUNAS LTDA ME")]                  = "DISTRIBUIDORA DE HORTIGRANJEIROS VALE VERDE LTDA",
            [NormalizarTextoFrete("BOX FRUIT COMERCIO LTDA")]                  = "IRINEU FERRAZ DE PRADO DISTRIBUIDORA PRADO",
        };
        SubstituicoesCidadeUF = new Dictionary<string, (string, string)>
        {
            [NormalizarTextoFrete("MF AMARAL TRANSPORTE LTDA")]                                   = ("ANAPOLIS", "GO"),
            [NormalizarTextoFrete("A. IPANEMA COMERCIAL DE HORTIFRUTIGRANJEIROS LTDA")]              = ("ANAPOLIS", "GO"),
            [NormalizarTextoFrete("IRINEU FERRAZ DE PRADO DISTRIBUIDORA PRADO")]               = ("ANAPOLIS", "GO"),
            [NormalizarTextoFrete("DISTRIBUIDORA DE HORTIGRANJEIROS VALE VERDE LTDA")]         = ("GOIANIA",  "GO"),
        };
    }

    // Placas Bento e Caio — permitidas para boleto Girassol/Horacio (Dudu NÃO)
    private static readonly HashSet<string> PlacasBento = new(StringComparer.OrdinalIgnoreCase)
        { "XAA-9457", "XAB-4C58", "XAC-6D57" };

    private static readonly HashSet<string> PlacasCaio = new(StringComparer.OrdinalIgnoreCase)
        { "XAD-5H46", "XAE-4D55", "XAF-2J52", "XAG-2G58", "XAH-2I15" };

    public static bool PlacaPermiteBoletoHoracio(string placa) =>
        PlacasBento.Contains(placa) || PlacasCaio.Contains(placa);

    // ──────────────────────────────────────────────────────────────
    // IMPORTAÇÃO DE CT-e (substitui cte_geral.py e cte_placas.py)
    // ──────────────────────────────────────────────────────────────

    public async Task<CTeImportResult> ImportarXmlAsync(string xmlContent)
    {
        try
        {
            var doc = XDocument.Parse(xmlContent);

            // CT-e CANCELADO: tpEvento = 110111
            var tpEvento = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "tpEvento")?.Value;

            if (tpEvento == "110111")
            {
                var chCanc = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "chCTe")?.Value ?? "";

                // Extrai número do CT-e da chave (posição 25-34), igual ao script Python
                int? numCancelado = null;
                if (chCanc.Length >= 34 && int.TryParse(chCanc[25..34], out var n))
                    numCancelado = n;

                var cteCanc = await db.CTes.FirstOrDefaultAsync(c => c.ChaveAcesso == chCanc);
                if (cteCanc != null)
                {
                    cteCanc.Status = StatusCTe.Cancelado;
                    await db.SaveChangesAsync();
                    return new CTeImportResult { Sucesso = true, CTe = cteCanc };
                }

                // CT-e de cancelamento sem CT-e correspondente registrado → registrar como cancelado
                if (numCancelado.HasValue)
                {
                    var cteAvulso = new CTe
                    {
                        NumeroCTe = numCancelado.Value.ToString(),
                        ChaveAcesso = chCanc,
                        DataEmissao = DateTime.UtcNow,
                        Status = StatusCTe.Cancelado,
                        VeiculoId = 0,
                    };
                    return new CTeImportResult { Sucesso = true, CTe = cteAvulso };
                }

                return new CTeImportResult { Sucesso = false, Erro = "CT-e de cancelamento: chave original não encontrada." };
            }

            // Obter infCte
            var infCte = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "infCte");
            if (infCte == null)
                return new CTeImportResult { Sucesso = false, Erro = "Elemento infCte não encontrado no XML." };

            // Chave de acesso — atributo Id do infCte ou elemento chCTe
            var chaveAcesso = infCte.Attribute("Id")?.Value?.Replace("CTe", "")
                           ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "chCTe")?.Value ?? "";

            if (string.IsNullOrEmpty(chaveAcesso))
                return new CTeImportResult { Sucesso = false, Erro = "Chave de acesso não encontrada no XML." };

            // Verificar duplicata
            var duplicata = await db.CTes.FirstOrDefaultAsync(c => c.ChaveAcesso == chaveAcesso);
            if (duplicata != null)
                return new CTeImportResult { Sucesso = true, CTe = duplicata, JaExistia = true };

            // Número do CT-e
            var numeroCTe = GetText(infCte, "nCT");

            // Número da NF: chave da NF em infDoc/chave, posição 25-34 — igual ao script Python
            var chaveNF = GetText(infCte, "chave");
            string? numeroNF = null;
            if (chaveNF.Length >= 34 && int.TryParse(chaveNF[25..34], out var nfNum))
                numeroNF = nfNum.ToString();

            // Data emissão
            var dhEmi = GetText(infCte, "dhEmi");
            DateTime.TryParse(dhEmi.Length >= 10 ? dhEmi[..10] : dhEmi, out var dataEmissao);

            // UF e município — mesma lógica do script
            var ufCliente = GetText(infCte, "UF", "dest");       // dest/enderDest/UF
            var ufFornecedor = GetText(infCte, "UF", "rem");     // rem/enderReme/UF
            var cidadeDestino = GetText(infCte, "xMun", "dest"); // dest/enderDest/xMun

            // Remetente / Destinatário
            var remetente = GetText(infCte, "xNome", "rem") ?? GetText(infCte, "xNome", "emit");
            var destinatario = GetText(infCte, "xNome", "dest");

            // Valores
            decimal.TryParse(GetText(infCte, "vCarga"),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var valorNF);
            decimal.TryParse(GetText(infCte, "vTPrest"),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var valorFrete);
            decimal.TryParse(GetText(infCte, "qCarga"),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var peso);

            // Placa: veicTracao/placa → rodo/veic/placa → regex em xObs
            var placa = GetText(infCte, "placa", "veicTracao");
            if (string.IsNullOrEmpty(placa))
                placa = GetText(infCte, "placa", "veic");
            if (string.IsNullOrEmpty(placa))
            {
                var xObs = GetText(infCte, "xObs").ToUpper().Replace("\n", " ");
                var match = Regex.Match(xObs, @"([A-Z]{3}\s*-?\s*\d[A-Z0-9]\d{2})", RegexOptions.IgnoreCase);
                if (match.Success)
                    placa = match.Groups[1].Value.Replace(" ", "");
            }

            if (string.IsNullOrEmpty(placa))
                return new CTeImportResult { Sucesso = false, Erro = "Placa não encontrada no XML." };

            placa = NormalizarPlaca(placa);

            var veiculo = await db.Veiculos.FirstOrDefaultAsync(v => v.Placa == placa);
            if (veiculo == null)
                return new CTeImportResult { Sucesso = false, Erro = $"Veículo com placa {placa} não cadastrado no sistema." };

            AliquotasICMS.TryGetValue(ufCliente, out var aliquota);

            var cte = new CTe
            {
                NumeroCTe = numeroCTe,
                ChaveAcesso = chaveAcesso,
                DataEmissao = dataEmissao == default ? DateTime.UtcNow : dataEmissao,
                UFOrigem = ufFornecedor,
                UFDestino = ufCliente,
                CidadeDestino = cidadeDestino,
                Remetente = remetente,
                Destinatario = destinatario,
                ValorNF = valorNF,
                ValorFrete = valorFrete,
                AliquotaICMS = aliquota,
                PesoKg = peso,
                Status = StatusCTe.Ativo,
                VeiculoId = veiculo.Id,
                NumeroNF = numeroNF,
                ClienteNome = destinatario,
                XmlOriginal = xmlContent,
            };

            db.CTes.Add(cte);
            await db.SaveChangesAsync();

            return new CTeImportResult { Sucesso = true, CTe = cte };
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.InnerException?.Message
                   ?? ex.InnerException?.Message
                   ?? ex.Message;
            return new CTeImportResult { Sucesso = false, Erro = $"Erro ao processar XML: {msg}" };
        }
    }

    // ──────────────────────────────────────────────────────────────
    // CÁLCULO DE FRETE E LÍQUOTA (substitui FRETE.py)
    // ──────────────────────────────────────────────────────────────

    public async Task<FreteCalculoResult> CalcularFreteAsync(FreteCalculoInput input)
    {
        // Carrega todas as rotas do banco de uma vez (evita N+1 queries)
        var todasRotas = await db.RotasDistancia.ToListAsync();
        var distancias = todasRotas.ToDictionary(
            r => (r.Origem, r.Destino, r.UF),
            r => r.Km);

        var pesoTotal = input.Itens.Sum(i => i.PesoKg);

        // Líquota: valorCarga / pesoTotal — arredonda SEMPRE para cima (igual ao FRETE.py)
        var liquotaBruta      = pesoTotal > 0 ? input.ValorCarga / pesoTotal : 0;
        var liquotaArredondada = Math.Ceiling(liquotaBruta * 100) / 100;

        var itensResultado      = new List<ItemFreteResultado>();
        var rotasNaoCadastradas = new List<string>();

        foreach (var item in input.Itens)
        {
            var destNorm  = NormalizarTextoFrete(item.Destinatario);
            var destFinal = item.Destinatario ?? "";
            var cidadeFinal = NormalizarTextoFrete(item.CidadeDestino);
            var ufFinal     = NormalizarTextoFrete(item.UF);

            // Substituições de destinatário
            if (SubstituicoesDestinatarios.TryGetValue(destNorm, out var destSubst))
            {
                destFinal = destSubst;
                destNorm  = NormalizarTextoFrete(destSubst);
            }
            if (SubstituicoesCidadeUF.TryGetValue(destNorm, out var cidadeSubst))
            {
                cidadeFinal = cidadeSubst.cidade;
                ufFinal     = cidadeSubst.uf;
            }

            var origemNorm = NormalizarTextoFrete(item.CidadeEmitente);
            int? km = distancias.TryGetValue((origemNorm, cidadeFinal, ufFinal), out var d) ? d : null;
            if (km == null)
                rotasNaoCadastradas.Add($"{item.CidadeEmitente} → {cidadeFinal}/{ufFinal}");

            itensResultado.Add(new ItemFreteResultado
            {
                NumeroNF      = item.NumeroNF,
                Emitente      = item.Emitente,
                Destinatario  = destFinal,
                CidadeOrigem  = item.CidadeEmitente,
                CidadeDestino = cidadeFinal,
                UF            = ufFinal,
                PesoKg        = item.PesoKg,
                DistanciaKm   = km,
                PesoXKm       = km.HasValue ? item.PesoKg * km.Value : 0,
                NfsDetalhadas = item.NfsDetalhadas ?? [],
            });
        }

        // Rateio pedágio proporcional a peso × km (igual ao script)
        var totalPesoXKm = itensResultado.Sum(i => i.PesoXKm);
        foreach (var item in itensResultado)
        {
            item.PercentualPedagio = totalPesoXKm > 0 ? item.PesoXKm / totalPesoXKm : 0;
            item.ValorPedagio      = item.PercentualPedagio * input.ValorPedagio;
        }

        int totalNFs = itensResultado.Sum(i => i.NfsDetalhadas.Count > 0 ? i.NfsDetalhadas.Count : 1);

        return new FreteCalculoResult
        {
            PesoTotal           = pesoTotal,
            LiquotaBruta        = liquotaBruta,
            LiquotaArredondada  = liquotaArredondada,
            TotalPesoXKm        = totalPesoXKm,
            TotalNFs            = totalNFs,
            Itens               = itensResultado,
            RotasNaoCadastradas = rotasNaoCadastradas,
        };
    }

    // ──────────────────────────────────────────────────────────────
    // CRUD DE DISTÂNCIAS — persiste no banco de dados
    // ──────────────────────────────────────────────────────────────

    public async Task<List<RotaDistancia>> ListarDistanciasAsync() =>
        await db.RotasDistancia
            .OrderBy(r => r.Origem).ThenBy(r => r.Destino)
            .ToListAsync();

    public async Task AdicionarDistanciaAsync(string origem, string ufOrigem, string destino, string uf, int km)
    {
        var o  = NormalizarTextoFrete(origem).ToUpperInvariant();
        var uo = (ufOrigem ?? "").Trim().ToUpperInvariant();
        var d  = NormalizarTextoFrete(destino).ToUpperInvariant();
        var u  = (uf ?? "").Trim().ToUpperInvariant();

        var existing = await db.RotasDistancia
            .FirstOrDefaultAsync(r => r.Origem == o && r.Destino == d && r.UF == u);

        if (existing != null)
        {
            existing.Km       = km;
            existing.UFOrigem = uo;
        }
        else
        {
            db.RotasDistancia.Add(new RotaDistancia
            {
                Origem   = o,
                UFOrigem = uo,
                Destino  = d,
                UF       = u,
                Km       = km,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<bool> AtualizarDistanciaAsync(int id, string origem, string ufOrigem, string destino, string uf, int km)
    {
        var rota = await db.RotasDistancia.FindAsync(id);
        if (rota == null) return false;

        rota.Origem   = NormalizarTextoFrete(origem).ToUpperInvariant();
        rota.UFOrigem = (ufOrigem ?? "").Trim().ToUpperInvariant();
        rota.Destino  = NormalizarTextoFrete(destino).ToUpperInvariant();
        rota.UF       = (uf ?? "").Trim().ToUpperInvariant();
        rota.Km       = km;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoverDistanciaAsync(string origem, string destino, string uf)
    {
        var o = NormalizarTextoFrete(origem);
        var d = NormalizarTextoFrete(destino);
        var u = NormalizarTextoFrete(uf);

        var rota = await db.RotasDistancia
            .FirstOrDefaultAsync(r => r.Origem == o && r.Destino == d && r.UF == u);

        if (rota == null) return false;
        db.RotasDistancia.Remove(rota);
        await db.SaveChangesAsync();
        return true;
    }

    // ──────────────────────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────────────────────

    private static string GetText(XElement root, string localName, string? parentLocalName = null)
    {
        IEnumerable<XElement> candidates = root.Descendants()
            .Where(e => e.Name.LocalName == localName);

        if (parentLocalName != null)
            candidates = candidates.Where(e =>
                e.Ancestors().Any(a => a.Name.LocalName == parentLocalName));

        return candidates.FirstOrDefault()?.Value?.Trim() ?? "";
    }

    // Normalização de texto igual ao normalize_text() do FRETE.py
    public static string NormalizarTextoFrete(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        var normalized = texto.Normalize(System.Text.NormalizationForm.FormKD);
        var ascii = new string(normalized.Where(c => c < 128).ToArray());
        ascii = ascii.ToUpperInvariant();

        foreach (var r in new[] { " LTDA", " LT", " ME", ".", ",", "-", "/", "  " })
            ascii = ascii.Replace(r, "");

        return string.Join(" ", ascii.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string NormalizarPlaca(string placa)
    {
        placa = placa.ToUpper().Replace(" ", "");
        if (placa.Length == 7 && !placa.Contains('-'))
            placa = placa[..3] + "-" + placa[3..];
        return placa;
    }
}
