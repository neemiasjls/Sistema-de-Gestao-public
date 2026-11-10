using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Monta o boleto a receber da CD ou da Girassol com as regras do BOL-11, para conferir com o que o
/// Admin faz à mão no Sicoob e, com a confirmação dele, emitir pela API de Cobrança (CorpoSicoob).
/// </summary>
public static class BoletoMontador
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public const string BeneficiarioFinalNome = "CAIO JOSE MOREIRA E CIA LTDA";
    public const string BeneficiarioFinalCnpj = "XX.XXX.XXX/0001-XX";
    public const decimal JurosMes = 2.00m;     // % ao mês, percentual
    public const decimal MultaPct = 5.00m;     // %, percentual

    public record Pagador(string Nome, string Cnpj, string? Endereco, string? Bairro, string? Cidade, string? Uf, string? Cep);

    public record Campo(string Grupo, string Rotulo, string? Valor, string? Aviso = null);

    public record Boleto(
        string Cliente, DateOnly Emissao, DateOnly Vencimento, DateOnly DataEncargos, decimal? Valor, string? Placa,
        string SeuNumero, Pagador Pagador, List<string> Instrucoes, List<Campo> Campos, List<string> Avisos);

    public static Pagador PagadorDe(string cliente) => cliente switch
    {
        BoletoReceberLeitor.Perfil => new("G.F. DE FONTES FILHO", "XX.XXX.XXX/0002-XX",
            "RODOVIA BR-153, S/N G2,BOX 7E8 KM 05", "FAZENDA RETIRO", "GOIÂNIA", "GO", "74620-430"),
        // Endereço da CD como o cadastro do Sicoob imprime no boleto 202 (bairro cortado em 30 letras pelo banco)
        _ => new("C.D. COMERCIO DE HORTIFRUTI LTDA", "XX.XXX.XXX/0003-XX",
            "AVENIDA PERIMETRAL", "PARQUE INDUSTRIAL ÁLVARO MOREI", "PARAÍSO DO TOCANTINS", "TO", "77546-098"),
    };

    /// <summary>Perfil +25 dias corridos; CD (Cedro Frutas) +28.</summary>
    public static int DiasVencimento(string cliente) => cliente == BoletoReceberLeitor.Perfil ? 25 : 28;

    public static Boleto Montar(string cliente, DateOnly emissao, decimal? valor, string? placa)
    {
        var avisos = new List<string>();
        var venc = emissao.AddDays(DiasVencimento(cliente));
        var encargos = venc.AddDays(1);
        var pag = PagadorDe(cliente);
        placa = string.IsNullOrWhiteSpace(placa) ? null : EmailClienteService.Placa(placa);
        var jurosDia = Math.Round(JurosMes / 30m, 2);   // 2% a.m. → 0,07%/dia, como o Sicoob imprime

        var instrucoes = new List<string>
        {
            $"A partir {encargos:dd/MM/yyyy} Juros {jurosDia.ToString("0.00", PtBr)}%/dia.",
            $"A partir {encargos:dd/MM/yyyy} Multa de {MultaPct.ToString("0", PtBr)}%.",
            "Não conceder desconto.",
            placa != null ? $"Caminhão ({placa})" : "Caminhão (placa não encontrada)",
        };
        if (valor == null) avisos.Add("Valor não calculado: falta o dado da carga (veja abaixo).");
        if (placa == null) avisos.Add("Placa do caminhão não encontrada: a instrução 4 precisa dela.");

        const string G1 = "Dados do boleto", G2 = "Pagador", G3 = "Encargos", G4 = "Instruções", G5 = "Beneficiário final e impressão";
        var campos = new List<Campo>
        {
            new(G1, "Espécie", "Duplicata Mercantil (DM)"),
            new(G1, "Modalidade", "Simples com registro"),
            new(G1, "Seu número", emissao.ToString("ddMMyyyy")),
            new(G1, "Data de emissão", emissao.ToString("dd/MM/yyyy")),
            new(G1, "Vencimento", venc.ToString("dd/MM/yyyy")),
            new(G1, "Valor", valor?.ToString("N2", PtBr), valor == null ? "Não calculado" : null),
            new(G1, "Aceite", "Não"),
            new(G1, "Apontamento (protesto)", "Sem apontamento"),
            new(G2, "CNPJ", pag.Cnpj),
            new(G2, "Nome", pag.Nome),
            new(G2, "Endereço", pag.Endereco, pag.Endereco == null ? "O Sicoob preenche ao digitar o CNPJ" : null),
            new(G2, "Bairro", pag.Bairro, pag.Bairro == null ? "O Sicoob preenche ao digitar o CNPJ" : null),
            new(G2, "Cidade / UF", pag.Cidade != null ? $"{pag.Cidade} - {pag.Uf}" : null, pag.Cidade == null ? "O Sicoob preenche ao digitar o CNPJ" : null),
            new(G2, "CEP", pag.Cep, pag.Cep == null ? "O Sicoob preenche ao digitar o CNPJ" : null),
            new(G3, "Juros", $"{JurosMes.ToString("0.00", PtBr)}% ao mês (percentual), a partir de {encargos:dd/MM/yyyy}"),
            new(G3, "Multa", $"{MultaPct.ToString("0.00", PtBr)}% (percentual), a partir de {encargos:dd/MM/yyyy}"),
            new(G3, "Desconto", "Sem condições de desconto"),
        };
        for (int i = 0; i < instrucoes.Count; i++)
            campos.Add(new(G4, $"Instrução {i + 1}", instrucoes[i], i == 3 && placa == null ? "Placa não encontrada" : null));
        campos.Add(new(G5, "Beneficiário final", $"{BeneficiarioFinalCnpj} {BeneficiarioFinalNome}"));
        campos.Add(new(G5, "Impressão e distribuição", "Cedente"));
        campos.Add(new(G5, "Formulário", "A4 sem envelopamento"));

        return new Boleto(cliente, emissao, venc, encargos, valor, placa, emissao.ToString("ddMMyyyy"), pag,
            instrucoes, campos, avisos);
    }

    /// <summary>O que impede emitir no Sicoob: sem valor, sem placa ou sem o endereço do pagador.</summary>
    public static List<string> ImpedeEmitir(Boleto b)
    {
        var l = new List<string>();
        if (b.Valor is not > 0) l.Add("Sem o valor calculado.");
        if (b.Placa == null) l.Add("Sem a placa do caminhão.");
        var p = b.Pagador;
        if (new[] { p.Endereco, p.Bairro, p.Cidade, p.Uf, p.Cep }.Any(string.IsNullOrWhiteSpace))
            l.Add("Sem o endereço completo do pagador.");
        return l;
    }

    // O Sicoob recusa "/" em texto do pedido ("S/N" → 400 "Invalid string syntax"); só nos campos do pagador.
    private static string SemBarra(string? s) =>
        string.Join(' ', (s ?? "").Replace('/', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Digitos(string? s) => new((s ?? "").Where(char.IsDigit).ToArray());
    private static string Corta(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>
    /// Corpo do POST /cobranca-bancaria/v3/boletos (um boleto, objeto único), mapa do BOL-10/BOL-11.
    /// Sem e-mail do pagador: quem manda o boleto ao cliente é o sistema (BOL-03/05), não o banco.
    /// </summary>
    public static Dictionary<string, object?> CorpoSicoob(Boleto b, long numeroCliente, long numeroContaCorrente)
    {
        static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
        var p = b.Pagador;
        return new()
        {
            ["numeroCliente"] = numeroCliente,
            ["codigoModalidade"] = 1,                       // simples com registro
            ["numeroContaCorrente"] = numeroContaCorrente,
            ["codigoEspecieDocumento"] = "DM",
            ["dataEmissao"] = D(b.Emissao),
            ["seuNumero"] = b.SeuNumero,                   // DDMMAAAA
            ["identificacaoEmissaoBoleto"] = 2,             // impressão: cedente (cliente)
            ["identificacaoDistribuicaoBoleto"] = 2,        // distribuição: cedente (cliente)
            ["valor"] = b.Valor,
            ["dataVencimento"] = D(b.Vencimento),
            ["tipoDesconto"] = 0,
            ["tipoMulta"] = 2,                              // percentual
            ["dataMulta"] = D(b.DataEncargos),
            ["valorMulta"] = MultaPct,
            ["tipoJurosMora"] = 2,                          // taxa mensal
            ["dataJurosMora"] = D(b.DataEncargos),
            ["valorJurosMora"] = JurosMes,
            ["numeroParcela"] = 1,
            ["aceite"] = false,
            ["codigoNegativacao"] = 3,                      // não negativar
            ["codigoProtesto"] = 3,                         // sem apontamento
            ["pagador"] = new Dictionary<string, object?>
            {
                ["numeroCpfCnpj"] = Digitos(p.Cnpj),
                ["nome"] = Corta(SemBarra(p.Nome), 50),
                ["endereco"] = Corta(SemBarra(p.Endereco), 40),
                ["bairro"] = Corta(SemBarra(p.Bairro), 30),
                ["cidade"] = Corta(SemBarra(p.Cidade), 40),
                ["cep"] = Digitos(p.Cep),
                ["uf"] = p.Uf,
            },
            ["beneficiarioFinal"] = new Dictionary<string, object?>
            {
                ["numeroCpfCnpj"] = Digitos(BeneficiarioFinalCnpj),
                ["nome"] = BeneficiarioFinalNome,
            },
            ["mensagensInstrucao"] = b.Instrucoes.Take(5).Select(i => Corta(i, 40)).ToList(),
            ["gerarPdf"] = true,
            ["codigoCadastrarPIX"] = 2,                     // sem QR Code Pix, como os boletos 524 e 202
        };
    }

    public record Diferenca(string Campo, string Montado, string Feito, bool Bate);

    /// <summary>Compara o montado com o boleto já feito (lido do PDF ou digitado).</summary>
    public static List<Diferenca> Comparar(Boleto m, string? numero, decimal? valor, DateOnly? vencimento,
        DateOnly? emissao, string? placa)
    {
        string D(DateOnly? d) => d?.ToString("dd/MM/yyyy") ?? "—";
        string V(decimal? v) => v?.ToString("N2", PtBr) ?? "—";
        var placaFeito = string.IsNullOrWhiteSpace(placa) ? null : EmailClienteService.Placa(placa);
        return
        [
            new("Seu número", m.SeuNumero, string.IsNullOrWhiteSpace(numero) ? "—" : numero.Trim(), numero?.Trim() == m.SeuNumero),
            new("Data de emissão", D(m.Emissao), D(emissao), emissao == m.Emissao),
            new("Vencimento", D(m.Vencimento), D(vencimento), vencimento == m.Vencimento),
            new("Valor", V(m.Valor), V(valor), m.Valor != null && valor != null && Math.Abs(m.Valor.Value - valor.Value) < 0.005m),
            new("Placa", m.Placa ?? "—", placaFeito ?? "—", m.Placa != null && m.Placa == placaFeito),
        ];
    }
}

/// <summary>Junta os dados da carga (relatório .txt da Girassol, romaneio da CD) e o boleto já feito.</summary>
public class BoletoMontagemService(AppDbContext db)
{
    public record Fonte(string Nome, string Descricao);

    public record Resultado(BoletoMontador.Boleto Boleto, List<Fonte> Fontes, List<string> Faltando,
        int? FeitoId, string? FeitoNome, string? FeitoNossoNumero, List<BoletoMontador.Diferenca>? Comparacao,
        int Sequencia, string NomeArquivo, List<string> ImpedeEmitir);

    // 1ª linha do .txt da Girassol: "524-GIRASSOL. 05-11-2026-XAI" (o número que o boleto vai ter, NFeController)
    private static readonly System.Text.RegularExpressions.Regex LinhaBoleto =
        new(@"^\s*(\d+)-GIRASSOL\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public async Task<Resultado> Montar(string cliente, DateOnly data, string? placaPedida = null)
    {
        decimal? valor = null;
        string? placa = null;
        int? sequenciaTxt = null;
        var fontes = new List<Fonte>();
        var faltando = new List<string>();
        var dataBr = data.ToString("dd/MM/yyyy");

        var registros = (await db.PlanilhasCarga.AsNoTracking()
                .Select(p => new { p.Id, p.Nome, p.CriadoEm })
                .ToListAsync())
            .Select(p => new { p.Id, p.Nome, p.CriadoEm, Ident = RomaneioClienteLeitor.LerNome(p.Nome) })
            .Where(p => p.Ident.Data != null && DateOnly.FromDateTime(p.Ident.Data.Value) == data)
            .OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id)
            .ToList();

        if (cliente == BoletoReceberLeitor.Perfil)
        {
            var doDia = registros.Where(p => p.Ident.Tipo == "frete").ToList();
            if (!string.IsNullOrWhiteSpace(placaPedida))
            {
                var pp = EmailClienteService.Placa(placaPedida);
                var daPlaca = doDia.Where(p => EmailClienteService.Placa(p.Ident.Placa) == pp).ToList();
                if (daPlaca.Count > 0) doDia = daPlaca;
            }
            if (doDia.Count == 0)
                faltando.Add($"Relatório de frete da Girassol de {dataBr} não encontrado — gere no Calcular frete ou suba o .txt em E-mails aos clientes.");
            else
            {
                var esc = doDia[0];
                var conteudo = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Id == esc.Id).Select(p => p.Conteudo).FirstAsync();
                var rel = RelatorioFreteLeitor.Ler(conteudo, esc.Nome);
                var primeira = System.Text.Encoding.UTF8.GetString(conteudo).Split('\n').FirstOrDefault() ?? "";
                var mt = LinhaBoleto.Match(primeira.TrimStart((char)0xFEFF));
                if (mt.Success) sequenciaTxt = int.Parse(mt.Groups[1].Value);
                valor = rel.TotalFrete;
                placa = rel.Placa;
                faltando.AddRange(rel.Avisos);
                fontes.Add(new(esc.Nome, "Valor = TOTAL FRETE do relatório; placa = nome do arquivo"));
                if (doDia.Count > 1)
                    faltando.Add($"Há {doDia.Count} relatórios de frete em {dataBr}; valeu o mais recente, {esc.Nome}.");
            }
        }
        else
        {
            var doDia = registros.Where(p => p.Ident.Tipo == "romaneio" && p.Ident.Apelido == "CEDRO FRUTAS").ToList();
            if (doDia.Count == 0)
                faltando.Add($"Romaneio da Cedro Frutas de {dataBr} não encontrado — gere em Romaneios.");
            else
            {
                var esc = doDia[0];
                var conteudo = await db.PlanilhasCarga.AsNoTracking().Where(p => p.Id == esc.Id).Select(p => p.Conteudo).FirstAsync();
                try
                {
                    var r = RomaneioClienteLeitor.Ler(conteudo);
                    var caixas = (decimal)r.TotalCaixasProdutosD;
                    if (caixas <= 0) faltando.Add($"O romaneio {esc.Nome} não tem caixas de produtoD: sem boleto das produtosD nesta carga.");
                    else valor = Math.Round(caixas * NFeImportService.PrecoCaixaProdutoDCedro, 2);
                    placa = string.IsNullOrWhiteSpace(r.Placa) ? null : r.Placa;
                    fontes.Add(new(esc.Nome, $"Valor = {caixas.ToString("0.##", new CultureInfo("pt-BR"))} caixas de produtoD × R$ 120,00; placa do romaneio"));
                }
                catch (Exception)
                {
                    faltando.Add($"Não consegui ler o romaneio {esc.Nome}.");
                }
            }
        }
        if (placa == null && !string.IsNullOrWhiteSpace(placaPedida)) placa = placaPedida;

        var boleto = BoletoMontador.Montar(cliente, data, valor, placa);

        // Boleto já feito para esta carga (o seu número do Sicoob é a data da carga)
        var feitos = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.Cliente == cliente && b.DataCarga == data)
            .OrderByDescending(b => b.Id)
            .Select(b => new { b.Id, b.NomeArquivo, b.NossoNumero, b.Numero, b.Valor, b.Vencimento, b.Emissao, b.Placa, b.Sequencia })
            .ToListAsync();
        var feito = boleto.Placa == null ? feitos.FirstOrDefault()
            : feitos.FirstOrDefault(b => EmailClienteService.Placa(b.Placa) == boleto.Placa) ?? feitos.FirstOrDefault();
        List<BoletoMontador.Diferenca>? comp = null;
        if (feito != null)
            comp = BoletoMontador.Comparar(boleto, feito.Numero, feito.Valor, feito.Vencimento, feito.Emissao, feito.Placa);

        // Número do arquivo: o do .txt da Girassol; senão o maior do cliente mais um ("202-CD. 03-11-2026-XAY.pdf")
        var sequencia = sequenciaTxt ?? (await db.BoletosReceber.Where(b => b.Cliente == cliente).MaxAsync(b => (int?)b.Sequencia) ?? 0) + 1;
        var nomeArquivo = $"{sequencia}-{cliente}. {data:dd-MM-yyyy}-{(boleto.Placa ?? "XXX")[..3]}.pdf";

        // Não emitir duas vezes: já há boleto desta carga (mesma placa, ou um dos dois sem placa)
        var impede = BoletoMontador.ImpedeEmitir(boleto);
        var mesmo = feitos.FirstOrDefault(b => boleto.Placa == null || string.IsNullOrWhiteSpace(b.Placa)
            || EmailClienteService.Placa(b.Placa) == boleto.Placa);
        if (mesmo != null)
            impede.Insert(0, $"Esta carga já tem boleto: {(string.IsNullOrWhiteSpace(mesmo.NomeArquivo) ? $"Boleto {mesmo.Sequencia}" : mesmo.NomeArquivo)}"
                + (string.IsNullOrWhiteSpace(mesmo.NossoNumero) ? "." : $" (nosso número {mesmo.NossoNumero})."));

        return new Resultado(boleto, fontes, faltando, feito?.Id,
            feito == null ? null : (string.IsNullOrWhiteSpace(feito.NomeArquivo) ? $"Boleto {feito.Sequencia}" : feito.NomeArquivo),
            feito?.NossoNumero, comp, sequencia, nomeArquivo, impede);
    }
}
