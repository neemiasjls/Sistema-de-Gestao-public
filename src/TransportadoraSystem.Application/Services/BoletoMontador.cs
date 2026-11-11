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
        string SeuNumero, Pagador Pagador, List<string> Instrucoes, List<Campo> Campos, List<string> Avisos,
        Config Regras);

    /// <summary>
    /// O que é configurável na emissão (BOL-12, tabela ConfigEmissaoBoleto). Sem a linha no banco
    /// vale <see cref="Padrao"/>, que são os valores do BOL-11.
    /// </summary>
    public record Config(string Cliente, int DiasVencimento, Pagador Pagador, decimal PrecoCaixaProdutoD,
        decimal MultaPct, decimal JurosMes, string Instrucao3, bool Pix);

    public const string Instrucao3Padrao = "Não conceder desconto.";

    public static Config Padrao(string cliente) => new(cliente, DiasVencimento(cliente), PagadorDe(cliente),
        NFeImportService.PrecoCaixaProdutoDCedro, MultaPct, JurosMes, Instrucao3Padrao, false);

    /// <summary>O que impede gravar a configuração (a tela mostra antes de salvar).</summary>
    public static List<string> Validar(Config c)
    {
        var l = new List<string>();
        if (c.DiasVencimento is < 1 or > 180) l.Add("Dias para o vencimento: de 1 a 180.");
        var p = c.Pagador;
        if (string.IsNullOrWhiteSpace(p.Nome)) l.Add("Informe o nome do pagador.");
        if (Digitos(p.Cnpj).Length != 14) l.Add("O CNPJ do pagador tem 14 números.");
        if (new[] { p.Endereco, p.Bairro, p.Cidade }.Any(string.IsNullOrWhiteSpace)) l.Add("Informe endereço, bairro e cidade do pagador.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(p.Uf ?? "", "^[A-Z]{2}$")) l.Add("A UF do pagador tem duas letras, ex.: GO.");
        if (Digitos(p.Cep).Length != 8) l.Add("O CEP do pagador tem 8 números.");
        if (c.Cliente != BoletoReceberLeitor.Perfil && c.PrecoCaixaProdutoD is <= 0 or > 10000) l.Add("Preço da caixa de produtoD: maior que zero.");
        if (c.MultaPct is <= 0 or > 20) l.Add("Multa: maior que zero e até 20%.");
        if (c.JurosMes is <= 0 or > 10) l.Add("Juros: maior que zero e até 10% ao mês.");
        if (string.IsNullOrWhiteSpace(c.Instrucao3)) l.Add("Informe a instrução 3.");
        else if (c.Instrucao3.Trim().Length > 40) l.Add("A instrução 3 tem no máximo 40 letras (limite do Sicoob).");
        return l;
    }

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

    /// <param name="regras">A configuração do cliente; nula = <see cref="Padrao"/> (BOL-11).</param>
    public static Boleto Montar(string cliente, DateOnly emissao, decimal? valor, string? placa, Config? regras = null)
    {
        var r = regras ?? Padrao(cliente);
        var avisos = new List<string>();
        var venc = emissao.AddDays(r.DiasVencimento);
        var encargos = venc.AddDays(1);
        var pag = r.Pagador;
        placa = string.IsNullOrWhiteSpace(placa) ? null : EmailClienteService.Placa(placa);
        var jurosDia = Math.Round(r.JurosMes / 30m, 2);   // 2% a.m. → 0,07%/dia, como o Sicoob imprime

        var instrucoes = new List<string>
        {
            $"A partir {encargos:dd/MM/yyyy} Juros {jurosDia.ToString("0.00", PtBr)}%/dia.",
            $"A partir {encargos:dd/MM/yyyy} Multa de {r.MultaPct.ToString("0.##", PtBr)}%.",
            r.Instrucao3.Trim(),
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
            new(G3, "Juros", $"{r.JurosMes.ToString("0.00", PtBr)}% ao mês (percentual), a partir de {encargos:dd/MM/yyyy}"),
            new(G3, "Multa", $"{r.MultaPct.ToString("0.00", PtBr)}% (percentual), a partir de {encargos:dd/MM/yyyy}"),
            new(G3, "Desconto", "Sem condições de desconto"),
        };
        for (int i = 0; i < instrucoes.Count; i++)
            campos.Add(new(G4, $"Instrução {i + 1}", instrucoes[i], i == 3 && placa == null ? "Placa não encontrada" : null));
        campos.Add(new(G5, "Beneficiário final", $"{BeneficiarioFinalCnpj} {BeneficiarioFinalNome}"));
        campos.Add(new(G5, "Impressão e distribuição", "Cedente"));
        campos.Add(new(G5, "Formulário", "A4 sem envelopamento"));
        campos.Add(new(G5, "QR Code Pix", r.Pix ? "Com QR Code Pix" : "Sem QR Code Pix"));

        return new Boleto(cliente, emissao, venc, encargos, valor, placa, emissao.ToString("ddMMyyyy"), pag,
            instrucoes, campos, avisos, r);
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
            ["valorMulta"] = b.Regras.MultaPct,
            ["tipoJurosMora"] = 2,                          // taxa mensal
            ["dataJurosMora"] = D(b.DataEncargos),
            ["valorJurosMora"] = b.Regras.JurosMes,
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
            ["codigoCadastrarPIX"] = b.Regras.Pix ? 1 : 2,  // 1 com · 2 sem QR Code Pix (524 e 202: sem)
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
        int Sequencia, string NomeArquivo, List<string> ImpedeEmitir,
        decimal? Caixas = null, decimal? CaixasRomaneio = null, bool JaTemBoleto = false);

    /// <summary>Carga candidata da lista "Para emitir" (já montada).</summary>
    public record Candidata(string Cliente, DateOnly Data, string? Placa, string? Arquivo, Resultado Montagem);

    /// <summary>
    /// A configuração da emissão do cliente (tabela ConfigEmissaoBoleto, BOL-12). Sem a linha, ou sem
    /// conseguir ler a tabela, valem os valores do código (BOL-11) e volta o aviso para a tela.
    /// </summary>
    public async Task<(BoletoMontador.Config Regras, string? Aviso)> Regras(string cliente)
    {
        try
        {
            var c = await db.ConfigEmissaoBoleto.AsNoTracking().FirstOrDefaultAsync(x => x.Cliente == cliente);
            if (c != null) return (ParaConfig(c), null);
            return (BoletoMontador.Padrao(cliente), null);
        }
        catch (Exception)
        {
            return (BoletoMontador.Padrao(cliente),
                "Não consegui ler a configuração da emissão (Boletos → Sicoob → Emissão de boletos): valeram os valores padrão.");
        }
    }

    public static BoletoMontador.Config ParaConfig(Core.Entities.ConfigEmissaoBoleto c) => new(c.Cliente, c.DiasVencimento,
        new(c.PagadorNome, c.PagadorCnpj, c.PagadorEndereco, c.PagadorBairro, c.PagadorCidade, c.PagadorUf, c.PagadorCep),
        c.PrecoCaixaProdutoD, c.MultaPct, c.JurosMes, c.Instrucao3, c.Pix);

    // 1ª linha do .txt da Girassol: "524-GIRASSOL. 05-11-2026-XAI" (o número que o boleto vai ter, NFeController)
    private static readonly System.Text.RegularExpressions.Regex LinhaBoleto =
        new(@"^\s*(\d+)-GIRASSOL\.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Cargas dos últimos dias ainda sem boleto: Perfil = cada .txt de frete (data + placa do nome);
    /// CD = cada dia com romaneio da Cedro Frutas com caixas de produtoD. Cada uma já montada.
    /// </summary>
    public async Task<List<Candidata>> ParaEmitir(string cliente, DateOnly hoje, int dias = 30)
    {
        var desde = hoje.AddDays(-dias);
        var tipo = cliente == BoletoReceberLeitor.Perfil ? "frete" : "romaneio";
        var nomes = (await db.PlanilhasCarga.AsNoTracking()
                .Select(p => new { p.Id, p.Nome, p.CriadoEm })
                .ToListAsync())
            .Select(p => new { p.Nome, p.CriadoEm, p.Id, Ident = RomaneioClienteLeitor.LerNome(p.Nome) })
            .Where(p => p.Ident.Data != null && p.Ident.Tipo == tipo
                && (tipo == "frete" || p.Ident.Apelido == "CEDRO FRUTAS"))
            .Select(p => new { p.Nome, p.CriadoEm, p.Id, Data = DateOnly.FromDateTime(p.Ident.Data!.Value), p.Ident.Placa })
            .Where(p => p.Data >= desde && p.Data <= hoje)
            .ToList();

        // Perfil: uma carga por data + placa; CD: uma por data (a montagem usa o romaneio mais recente do dia)
        var cargas = nomes
            .GroupBy(p => (p.Data, Placa: tipo == "frete" && !string.IsNullOrWhiteSpace(p.Placa) ? EmailClienteService.Placa(p.Placa) : null))
            .Select(g => g.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Id).First())
            .OrderByDescending(p => p.Data)
            .ToList();

        var lista = new List<Candidata>();
        foreach (var c in cargas)
        {
            var placa = tipo == "frete" && !string.IsNullOrWhiteSpace(c.Placa) ? c.Placa : null;
            var m = await Montar(cliente, c.Data, placa);
            if (m.JaTemBoleto) continue;
            if (tipo == "romaneio" && m.CaixasRomaneio is decimal cx && cx <= 0) continue;   // sem produtoD, sem boleto
            lista.Add(new(cliente, c.Data, m.Boleto.Placa ?? placa, c.Nome, m));
        }
        return lista;
    }

    /// <param name="caixasPedidas">CD: caixas de produtoD digitadas (corrigem as do romaneio); nulo = as do romaneio.</param>
    public async Task<Resultado> Montar(string cliente, DateOnly data, string? placaPedida = null, decimal? caixasPedidas = null)
    {
        decimal? caixasUsadas = null, caixasRomaneio = null;
        decimal? valor = null;
        string? placa = null;
        int? sequenciaTxt = null;
        var fontes = new List<Fonte>();
        var faltando = new List<string>();
        var dataBr = data.ToString("dd/MM/yyyy");
        var (regras, avisoRegras) = await Regras(cliente);
        if (avisoRegras != null) faltando.Add(avisoRegras);

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
                    caixasRomaneio = (decimal)r.TotalCaixasProdutosD;
                    var digitadas = caixasPedidas is > 0 && caixasPedidas != caixasRomaneio;
                    var caixas = digitadas ? caixasPedidas!.Value : caixasRomaneio.Value;
                    caixasUsadas = caixas;
                    var preco = regras.PrecoCaixaProdutoD.ToString("N2", new CultureInfo("pt-BR"));
                    var cxTexto = caixas.ToString("0.##", new CultureInfo("pt-BR"));
                    if (caixas <= 0) faltando.Add($"O romaneio {esc.Nome} não tem caixas de produtoD: sem boleto das produtosD nesta carga.");
                    else valor = Math.Round(caixas * regras.PrecoCaixaProdutoD, 2);
                    placa = string.IsNullOrWhiteSpace(r.Placa) ? null : r.Placa;
                    fontes.Add(new(esc.Nome, digitadas
                        ? $"Valor = {cxTexto} caixas de produtoD digitadas (o romaneio tem {caixasRomaneio.Value.ToString("0.##", new CultureInfo("pt-BR"))}) × R$ {preco}; placa do romaneio"
                        : $"Valor = {cxTexto} caixas de produtoD × R$ {preco}; placa do romaneio"));
                }
                catch (Exception)
                {
                    faltando.Add($"Não consegui ler o romaneio {esc.Nome}.");
                }
            }
        }
        if (placa == null && !string.IsNullOrWhiteSpace(placaPedida)) placa = placaPedida;

        var boleto = BoletoMontador.Montar(cliente, data, valor, placa, regras);

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
        // Placa errada nas NFs (Admin, 11/11/2026: .txt VOLUME, boleto emitido certo com XAC): boleto do dia cuja placa não é
        // de nenhuma carga do dia vale para a única carga do dia que ficou sem boleto
        if (mesmo == null && feitos.Count > 0 && boleto.Placa != null && cliente == BoletoReceberLeitor.Perfil)
        {
            var placasDoDia = (await db.PlanilhasCarga.AsNoTracking().Select(p => p.Nome).ToListAsync())
                .Select(RomaneioClienteLeitor.LerNome)
                .Where(i => i.Tipo == "frete" && i.Data is { } d && DateOnly.FromDateTime(d) == data && !string.IsNullOrWhiteSpace(i.Placa))
                .Select(i => EmailClienteService.Placa(i.Placa!)).ToHashSet();
            var cargasSemBoleto = placasDoDia.Where(p => !feitos.Any(f => EmailClienteService.Placa(f.Placa) == p)).ToList();
            var boletosSemCarga = feitos.Where(f => !string.IsNullOrWhiteSpace(f.Placa) && !placasDoDia.Contains(EmailClienteService.Placa(f.Placa))).ToList();
            if (cargasSemBoleto is [var unica] && unica == boleto.Placa && boletosSemCarga is [var outro])
            {
                mesmo = outro;
                faltando.Add($"O boleto {outro.Sequencia} desta data saiu com a placa {outro.Placa} e a carga está com {boleto.Placa}: conta como o boleto desta carga.");
            }
        }
        if (mesmo != null)
            impede.Insert(0, $"Esta carga já tem boleto: {(string.IsNullOrWhiteSpace(mesmo.NomeArquivo) ? $"Boleto {mesmo.Sequencia}" : mesmo.NomeArquivo)}"
                + (string.IsNullOrWhiteSpace(mesmo.NossoNumero) ? "." : $" (nosso número {mesmo.NossoNumero})."));

        return new Resultado(boleto, fontes, faltando, feito?.Id,
            feito == null ? null : (string.IsNullOrWhiteSpace(feito.NomeArquivo) ? $"Boleto {feito.Sequencia}" : feito.NomeArquivo),
            feito?.NossoNumero, comp, sequencia, nomeArquivo, impede, caixasUsadas, caixasRomaneio, mesmo != null);
    }
}
