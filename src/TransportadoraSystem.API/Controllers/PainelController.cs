using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportadoraSystem.Application.Services;
using TransportadoraSystem.Core.Entities;
using TransportadoraSystem.Core.Enums;
using TransportadoraSystem.Infrastructure.Data;

namespace TransportadoraSystem.API.Controllers;

/// <summary>
/// Painel do dia (F-01): a tela inicial numa chamada só. Junta o que já existe nas
/// outras telas — boletos que vencem, a rotina da semana (rotina.md, ROT-01 a 05)
/// com o que dá para conferir sozinho, e-mails ainda não enviados, controles de
/// volumes que não fecham, romaneios da fazenda sem controle, o acerto da semana e um
/// resumo curto da semana. Só lê; a única escrita é a mesma que a lista de volumes já
/// faz ao abrir (ligar romaneio da fazenda ao controle), para os dois mostrarem igual.
/// </summary>
[Authorize]
[ApiController]
[Route("api/painel")]
public class PainelController(AppDbContext db, RomaneiosEmbalarService embalar, ILogger<PainelController> logger) : ControllerBase
{
    // O dia no Brasil: o servidor roda em UTC e à noite já seria amanhã
    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3));
    private static DateOnly Segunda(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    // Meia-noite de Brasília em UTC, para comparar com as colunas timestamptz
    private static DateTime InicioUtc(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue).AddHours(3), DateTimeKind.Utc);

    private const int DiasAFrente = 7;
    private const int MaxLista = 8;

    // Situação de cada tarefa: feito e pendente o sistema conferiu; andamento é
    // começado; lembrete é o que ele faz fora do sistema (não há como conferir).
    public record Tarefa(string Chave, string Titulo, string? Detalhe, string Link, string Situacao);
    public record DiaRotina(int Dia, string Nome, List<Tarefa> Tarefas);

    [HttpGet]
    public async Task<IActionResult> Painel()
    {
        var hoje = Hoje();
        var segunda = Segunda(hoje);
        var domingo = segunda.AddDays(6);

        var boletos = await Boletos(hoje);
        var romaneiosSemana = await RomaneiosDaSemana(segunda, domingo);
        var emails = await Emails(hoje);
        var acerto = await Acerto(segunda);
        var volumes = await Volumes(hoje);
        var semana = await Semana(segunda, domingo);
        var (rotina, mensal) = await Rotina(hoje, segunda, domingo, romaneiosSemana, emails, acerto);

        // 1 = segunda ... 5 = sexta; sábado e domingo são o "fim de semana" (6)
        var diaHoje = hoje.DayOfWeek switch { DayOfWeek.Saturday or DayOfWeek.Sunday => 6, var d => (int)d };

        return Ok(new
        {
            Hoje = hoje,
            Segunda = segunda,
            Domingo = domingo,
            DiaHoje = diaHoje,
            Boletos = boletos,
            Rotina = rotina,
            Mensal = mensal,
            Emails = emails.Pendentes,
            Volumes = volumes,
            Acerto = acerto,
            Semana = semana,
            Cnh = await CnhVencendo(hoje),
        });
    }

    // CNH vencida ou que vence em até 30 dias (VIA-20): motoristas ativos, a mais urgente primeiro
    private async Task<object> CnhVencendo(DateOnly hoje)
    {
        var limite = hoje.AddDays(30);
        var lista = await db.MotoristasFrota.AsNoTracking()
            .Where(m => m.Ativo && m.CnhValidade != null && m.CnhValidade <= limite)
            .OrderBy(m => m.CnhValidade)
            .Select(m => new { m.Id, m.Nome, Validade = m.CnhValidade!.Value })
            .ToListAsync();
        return lista.Select(m => new { m.Id, m.Nome, m.Validade, Dias = m.Validade.DayNumber - hoje.DayNumber });
    }

    // ───────────────────────── Boletos ─────────────────────────

    public record BoletoPainel(string Origem, int Id, string Quem, string? Descricao, string Numero, decimal? Valor,
        DateOnly Vencimento, int Dias);

    private async Task<object> Boletos(DateOnly hoje)
    {
        var limite = hoje.AddDays(DiasAFrente);

        var postos = await db.BoletosPosto.AsNoTracking()
            .Where(b => b.PagoEm == null && b.Vencimento != null && b.Vencimento <= limite)
            .Select(b => new { b.Id, b.Posto, b.NumeroDocumento, b.NossoNumero, b.Valor, Vencimento = b.Vencimento!.Value })
            .ToListAsync();
        var avulsos = await db.BoletosPagarAvulsos.AsNoTracking()
            .Where(b => b.PagoEm == null && b.Vencimento != null && b.Vencimento <= limite)
            .Select(b => new { b.Id, b.Beneficiario, b.Descricao, b.Numero, b.Valor, Vencimento = b.Vencimento!.Value })
            .ToListAsync();
        var pagarSemVencimento =
            await db.BoletosPosto.CountAsync(b => b.PagoEm == null && b.Vencimento == null) +
            await db.BoletosPagarAvulsos.CountAsync(b => b.PagoEm == null && b.Vencimento == null);

        var pagar = postos
            .Select(b => new BoletoPainel("POSTO", b.Id, NomeOuTraco(b.Posto), "Posto de combustível",
                b.NumeroDocumento != "" ? b.NumeroDocumento : b.NossoNumero, b.Valor, b.Vencimento, b.Vencimento.DayNumber - hoje.DayNumber))
            .Concat(avulsos.Select(b => new BoletoPainel("AVULSO", b.Id, NomeOuTraco(b.Beneficiario),
                b.Descricao == "" ? null : b.Descricao, b.Numero, b.Valor, b.Vencimento, b.Vencimento.DayNumber - hoje.DayNumber)))
            .OrderBy(b => b.Vencimento).ThenByDescending(b => b.Valor)
            .ToList();

        var receberLista = await db.BoletosReceber.AsNoTracking()
            .Where(b => b.RecebidoEm == null && b.Vencimento != null && b.Vencimento <= limite)
            .Select(b => new { b.Id, b.Cliente, b.Pagador, b.NossoNumero, b.Numero, b.Placa, b.Valor, Vencimento = b.Vencimento!.Value })
            .ToListAsync();
        var receberSemVencimento = await db.BoletosReceber.CountAsync(b => b.RecebidoEm == null && b.Vencimento == null);

        var receber = receberLista
            .Select(b => new BoletoPainel(b.Cliente, b.Id, BoletoReceberLeitor.RotuloCliente(b.Cliente),
                b.Placa == "" ? null : $"Placa {b.Placa}", b.NossoNumero != "" ? b.NossoNumero : b.Numero, b.Valor,
                b.Vencimento, b.Vencimento.DayNumber - hoje.DayNumber))
            .OrderBy(b => b.Vencimento).ThenByDescending(b => b.Valor)
            .ToList();

        return new
        {
            Pagar = Grupo(pagar, pagarSemVencimento),
            Receber = Grupo(receber, receberSemVencimento),
        };
    }

    private static object Grupo(List<BoletoPainel> lista, int semVencimento)
    {
        var vencidos = lista.Where(b => b.Dias < 0).ToList();
        var aVencer = lista.Where(b => b.Dias >= 0).ToList();
        return new
        {
            Vencidos = new { Quantidade = vencidos.Count, Total = vencidos.Sum(b => b.Valor ?? 0), SemValor = vencidos.Count(b => b.Valor == null) },
            AVencer = new { Quantidade = aVencer.Count, Total = aVencer.Sum(b => b.Valor ?? 0), SemValor = aVencer.Count(b => b.Valor == null) },
            HojeQuantidade = aVencer.Count(b => b.Dias == 0),
            SemVencimento = semVencimento,
            // Vencidos primeiro (o mais velho no topo), depois os que vencem
            Itens = lista.Take(MaxLista),
            Mais = Math.Max(0, lista.Count - MaxLista),
        };
    }

    private static string NomeOuTraco(string s) => string.IsNullOrWhiteSpace(s) ? "Sem nome" : s.Trim();

    // ───────────────────────── Romaneios e e-mails ─────────────────────────

    public record RomaneioGuardado(string Apelido, string Tipo, DateOnly Data, int? Numero, string Nome);

    // Os romaneios de cliente guardados (PlanilhasCarga) com data nesta semana,
    // pelo nome do arquivo ("164--AURORA-29-09-2026.xlsx", "29-09-2026 XAG-2G58.txt")
    private async Task<List<RomaneioGuardado>> RomaneiosDaSemana(DateOnly de, DateOnly ate)
    {
        var nomes = await db.PlanilhasCarga.AsNoTracking().Select(p => p.Nome).ToListAsync();
        return Identificar(nomes).Where(r => r.Data >= de && r.Data <= ate).ToList();
    }

    private static IEnumerable<RomaneioGuardado> Identificar(IEnumerable<string> nomes) =>
        nomes.Select(n => (Nome: n, Id: RomaneioClienteLeitor.LerNome(n)))
            .Where(x => x.Id.Data != null)
            .Select(x => new RomaneioGuardado(x.Id.Apelido, x.Id.Tipo, DateOnly.FromDateTime(x.Id.Data!.Value), x.Id.Numero, x.Nome));

    public record EmailPendente(string Cliente, string Rotulo, DateOnly DataCarga, string Situacao, string? Detalhe);
    private record EmailsInfo(List<EmailPendente> Pendentes, List<(string Cliente, DateOnly Data)> Enviados, List<(string Cliente, DateOnly Data)> Cargas);

    // E-mails das cargas dos últimos 7 dias (Cedro Frutas, Dunas e o frete da
    // Perfil) sem envio de verdade. Envio no modo de teste não conta: foi só para
    // o Admin, o cliente não recebeu (BOL-04).
    private async Task<EmailsInfo> Emails(DateOnly hoje)
    {
        var de = hoje.AddDays(-7);
        var nomes = await db.PlanilhasCarga.AsNoTracking().Select(p => p.Nome).ToListAsync();
        var cargas = Identificar(nomes)
            .Where(r => r.Data >= de && r.Data <= hoje)
            .SelectMany(r => EmailClienteService.Clientes
                .Where(c => c.EhFrete ? r.Tipo == "frete" : r.Tipo == "romaneio" && c.Chave == r.Apelido)
                .Select(c => (Cliente: c, r.Data)))
            .DistinctBy(x => (x.Cliente.Chave, x.Data))
            .OrderBy(x => x.Data).ThenBy(x => x.Cliente.Chave)
            .ToList();

        var envios = await db.EmailsClienteEnvios.AsNoTracking()
            .Where(e => e.DataCarga >= de)
            .Select(e => new { e.Cliente, e.DataCarga, e.ModoTeste, e.Erro, e.EnviadoEm })
            .ToListAsync();

        var pendentes = new List<EmailPendente>();
        var enviados = new List<(string, DateOnly)>();
        foreach (var (c, data) in cargas)
        {
            var doDia = envios.Where(e => e.Cliente == c.Chave && e.DataCarga == data).OrderByDescending(e => e.EnviadoEm).ToList();
            if (doDia.Any(e => e.Erro == null && !e.ModoTeste)) { enviados.Add((c.Chave, data)); continue; }
            var ultimo = doDia.FirstOrDefault();
            var (situacao, detalhe) = ultimo switch
            {
                null => ("pendente", (string?)null),
                { Erro: not null } => ("erro", "A última tentativa deu erro."),
                _ => ("teste", "Foi só no modo de teste: o cliente não recebeu."),
            };
            pendentes.Add(new EmailPendente(c.Chave, c.Rotulo, data, situacao, detalhe));
        }
        return new EmailsInfo(pendentes, enviados, cargas.Select(x => (x.Cliente.Chave, x.Data)).ToList());
    }

    // ───────────────────────── Acerto Alfa ─────────────────────────

    public record AcertoPainel(int Id, int Numero, DateOnly Semana, int Romaneios, int Boletos, int BoletosSemValor,
        int Caixas, double APagar, double Pagamentos, double Saldo, int Arquivos);
    public record AcertoSemana(DateOnly Semana, AcertoPainel? Atual, AcertoPainel? Anterior);

    private async Task<AcertoSemana> Acerto(DateOnly segunda)
    {
        // O saldo anterior vem em cadeia do acerto de número anterior (ACE-08):
        // precisa de todos para o saldo sair igual ao da tela do acerto.
        var linhas = await db.AcertosAlfa.AsNoTracking()
            .Select(a => new { a.Id, a.Numero, a.Semana, a.DadosJson, Arquivos = a.Arquivos.Count })
            .ToListAsync();
        if (linhas.Count == 0) return new AcertoSemana(segunda, null, null);

        var cadeia = AcertoAlfaService.Encadear(linhas.Select(a => (a.Numero, a.Semana, AcertoAlfaService.Desserializar(a.DadosJson, a.Semana))))
            .ToDictionary(e => e.Numero);

        AcertoPainel Resumo(int id, int numero, DateOnly semana, int arquivos)
        {
            var e = cadeia[numero];
            var d = e.Valendo;
            var t = e.Totais;
            return new AcertoPainel(id, numero, semana, d.Romaneios.Count,
                d.Pagamentos.Count(p => AcertoAlfaService.EhBoleto(p) && p.Valor != null),
                d.Pagamentos.Count(p => AcertoAlfaService.EhBoleto(p) && p.Valor == null),
                t.Caixas, Math.Round(t.APagar, 2), Math.Round(t.Pagamentos, 2), Math.Round(t.Saldo, 2), arquivos);
        }

        var atual = linhas.Where(a => a.Semana == segunda).OrderByDescending(a => a.Numero).FirstOrDefault();
        var anterior = linhas.Where(a => a.Semana < segunda).OrderByDescending(a => a.Semana).ThenByDescending(a => a.Numero).FirstOrDefault();
        return new AcertoSemana(segunda,
            atual == null ? null : Resumo(atual.Id, atual.Numero, atual.Semana, atual.Arquivos),
            anterior == null ? null : Resumo(anterior.Id, anterior.Numero, anterior.Semana, anterior.Arquivos));
    }

    // ───────────────────────── Volumes embalados ─────────────────────────

    private const int ControlesRecentes = 12;
    private const int DiasVolumes = 45;

    private async Task<object> Volumes(DateOnly hoje)
    {
        // Mesmo passo da lista de volumes ao abrir: romaneio da fazenda novo entra no
        // controle certo antes de contar o que está sem controle.
        await embalar.AtualizarAsync();

        // Os mais recentes e mais um antes, para a sobra anterior do mais velho
        var controles = await db.VolumesControles.Include(c => c.Linhas).AsNoTracking()
            .OrderByDescending(c => c.Numero).Take(ControlesRecentes + 1)
            .ToListAsync();
        controles.Reverse();

        var problemas = new List<object>();
        int conferidos = 0, batem = 0;
        for (var i = 0; i < controles.Count; i++)
        {
            // O primeiro só serve de sobra anterior quando veio a mais
            if (i == 0 && controles.Count > ControlesRecentes) continue;
            var c = controles[i];
            var anterior = i > 0 ? controles[i - 1] : null;
            if (c.Data < hoje.AddDays(-DiasVolumes)) continue;

            var sobraAnt = c.SobraAnteriorManual
                ? ConferenciaVolumes.Somar(c.Linhas.Where(l => l.Grupo == ControleVolumesLinha.SobraAnterior))
                : anterior == null ? new ControleVolumesLinha() : ConferenciaVolumes.SobraFinal(anterior.Linhas);
            var conf = ConferenciaVolumes.Calcular(c, sobraAnt);
            conferidos++;
            if (conf.Status == "bate") { batem++; continue; }

            var diferencas = conf.Crus.Where(x => !x.Bate).Select(x => new { x.Nome, Valor = x.Estoque, EmProdutosC = true })
                .Concat(conf.Produzidos.Where(x => !x.Bate).Select(x => new { x.Nome, Valor = x.Estoque, EmProdutosC = false }))
                .ToList();
            problemas.Add(new
            {
                c.Id, c.Numero, c.Data, conf.Status,
                Faltando = conf.Avisos.Where(a => a.StartsWith("Falta") || a.StartsWith("Não tem")).ToList(),
                Diferencas = diferencas,
            });
        }
        problemas.Reverse();   // mais recente primeiro

        List<object> semControle = [];
        try
        {
            semControle = (await embalar.ListarAsync())
                .Where(r => r.Situacao == "aguardando")
                .OrderBy(r => r.Data).ThenBy(r => r.Numero)
                .Select(r => (object)new { r.Numero, r.Data, r.Caixas, r.AcertoId, r.AcertoNumero })
                .ToList();
        }
        catch (Exception ex)
        {
            // Sem os romaneios o resto do painel serve: a tela avisa que não veio
            logger.LogError(ex, "Painel: não consegui listar os romaneios da fazenda");
            return new { Controles = problemas, Conferidos = conferidos, Batem = batem, RomaneiosSemControle = (object?)null };
        }

        return new { Controles = problemas, Conferidos = conferidos, Batem = batem, RomaneiosSemControle = semControle };
    }

    // ───────────────────────── Semana ─────────────────────────

    private async Task<object> Semana(DateOnly segunda, DateOnly domingo)
    {
        var de = InicioUtc(segunda);
        var ate = InicioUtc(domingo.AddDays(1));

        var cargas = await db.CalculosFreteSalvos.AsNoTracking()
            .Where(c => c.CriadoEm >= de && c.CriadoEm < ate)
            .Select(c => new { c.Placa, c.ValorCarga, c.TotalNFs })
            .ToListAsync();
        var ctes = await db.CTes.AsNoTracking()
            .Where(c => c.DataEmissao >= de && c.DataEmissao < ate)
            .Select(c => new { c.Status, c.ValorFrete })
            .ToListAsync();
        var romaneios = (await RomaneiosDaSemana(segunda, domingo)).Count(r => r.Tipo == "romaneio");

        return new
        {
            Cargas = cargas.Count,
            Placas = cargas.Select(c => c.Placa).Where(p => p != "").Distinct().Count(),
            ValorCargas = cargas.Sum(c => c.ValorCarga),
            Notas = cargas.Sum(c => c.TotalNFs),
            Ctes = ctes.Count(c => c.Status == StatusCTe.Ativo),
            CtesCancelados = ctes.Count(c => c.Status == StatusCTe.Cancelado),
            Frete = ctes.Where(c => c.Status == StatusCTe.Ativo).Sum(c => c.ValorFrete),
            Romaneios = romaneios,
        };
    }

    // ───────────────────────── Rotina ─────────────────────────

    private async Task<(List<DiaRotina> Rotina, List<Tarefa> Mensal)> Rotina(DateOnly hoje, DateOnly segunda, DateOnly domingo,
        List<RomaneioGuardado> romaneios, EmailsInfo emails, AcertoSemana acerto)
    {
        Tarefa Romaneio(string apelido, string nome)
        {
            var feitos = romaneios.Where(r => r.Tipo == "romaneio" && r.Apelido == apelido).OrderByDescending(r => r.Data).ToList();
            if (feitos.Count == 0)
                return new Tarefa($"romaneio-{apelido}", $"Romaneio {nome}", "Ainda não foi gerado nesta semana.", "/frete", "pendente");
            var r = feitos[0];
            var numero = r.Numero is { } n ? $"Romaneio {n}" : "Gerado";
            return new Tarefa($"romaneio-{apelido}", $"Romaneio {nome}", $"{numero}, carga de {r.Data:dd/MM}.", "/romaneios", "feito");
        }

        Tarefa Email(string chave, string titulo)
        {
            var cargas = emails.Cargas.Where(c => c.Cliente == chave && c.Data >= segunda && c.Data <= domingo).ToList();
            if (cargas.Count == 0)
                return new Tarefa($"email-{chave}", titulo, "Ainda sem carga nesta semana para mandar.", "/emails-clientes", "pendente");
            var pend = emails.Pendentes.Where(p => p.Cliente == chave && p.DataCarga >= segunda).ToList();
            if (pend.Count == 0)
                return new Tarefa($"email-{chave}", titulo, $"Enviado (carga de {cargas.Max(c => c.Data):dd/MM}).", "/emails-clientes", "feito");
            return new Tarefa($"email-{chave}", titulo,
                pend.Any(p => p.Situacao == "teste") ? "Só foi no modo de teste." : $"Carga de {pend[0].DataCarga:dd/MM} ainda não enviada.",
                "/emails-clientes", "pendente");
        }

        // Boleto emitido para o cliente (Sicoob) com carga, emissão ou vencimento nesta semana
        var receberSemana = await db.BoletosReceber.AsNoTracking()
            .Where(b => (b.DataCarga >= segunda && b.DataCarga <= domingo) || (b.Emissao >= segunda && b.Emissao <= domingo))
            .Select(b => b.Cliente).ToListAsync();
        Tarefa BoletoCliente(string chave, string cliente, string titulo, string? detalheFalta, string situacaoFalta)
        {
            var n = receberSemana.Count(c => c == cliente);
            return n > 0
                ? new Tarefa(chave, titulo, n == 1 ? "Boleto desta semana já está em Boletos." : $"{n} boletos desta semana em Boletos.", "/boletos", "feito")
                : new Tarefa(chave, titulo, detalheFalta, "/boletos", situacaoFalta);
        }

        Tarefa AcertoTarefa()
        {
            const string titulo = "Acerto da fazenda Alfa";
            if (acerto.Atual is not { } a)
                return new Tarefa("acerto", titulo, "Ainda não chegou nenhum PDF desta semana.", "/acerto-alfa", "pendente");
            var partes = new List<string> { $"Acerto {a.Numero}", Contar(a.Romaneios, "romaneio", "romaneios"), Contar(a.Boletos, "boleto", "boletos") };
            if (a.BoletosSemValor > 0) partes.Add($"{Contar(a.BoletosSemValor, "boleto", "boletos")} sem valor");
            var completo = a.Romaneios > 0 && a.Boletos > 0 && a.BoletosSemValor == 0;
            return new Tarefa("acerto", titulo, string.Join(" · ", partes), "/acerto-alfa", completo ? "feito" : "andamento");
        }

        Tarefa Lembrete(string chave, string titulo, string link, string? detalhe = null) => new(chave, titulo, detalhe, link, "lembrete");

        var rotina = new List<DiaRotina>
        {
            // ROT-01
            new(1, "Segunda", [
                Romaneio("AURORA", "do Aurora"),
                Romaneio("BRZ", "da BRZ"),
                Lembrete("alfa-boletos", "Boletos da Alfa com o Igor e conferência com os romaneios", "/acerto-alfa"),
                BoletoCliente("frete-horacio-seg", BoletoReceberLeitor.Perfil, "Boleto de frete do Horacio (Perfil)",
                    "Só se a placa for do Bento ou do Caio.", "lembrete"),
                Lembrete("sicoob", "Anotar o banco Sicoob quando houver placa do Bento", "/abastecimentos"),
                Lembrete("tip-bank", "Conferir as faturas TIP BANK", "/abastecimentos"),
            ]),
            // ROT-02
            new(2, "Terça", [
                Romaneio("CEDRO FRUTAS", "da Cedro Frutas"),
                Romaneio("DUNAS", "do Dunas"),
                BoletoCliente("produtosD", BoletoReceberLeitor.Cd, "Boleto das produtosD da Cedro", "Ainda não está em Boletos.", "pendente"),
                Email("CEDRO FRUTAS", "E-mail da Cedro Frutas"),
                Email("DUNAS", "E-mail do Dunas"),
            ]),
            // ROT-03
            new(3, "Quarta", [
                Lembrete("preco-volume", "Verificar mudança de preço do volume", "/tipos-volume"),
                Email("GIRASSOL", "E-mail do boleto de frete do Horacio"),
            ]),
            new(4, "Quinta", [
                Lembrete("ctes-horacio", "Anotar os CT-es da carga do Horacio", "/frete"),
                BoletoCliente("frete-horacio-qui", BoletoReceberLeitor.Perfil, "Boleto do frete do Horacio",
                    "Só se a placa for do Bento ou do Caio.", "lembrete"),
            ]),
            // ROT-04
            new(5, "Sexta", [
                Romaneio("FAROL", "do Farol"),
                AcertoTarefa(),
                Lembrete("conferir-volumes", "Conferir os volumes embalados", "/estoque/volumes"),
                Lembrete("produtosA", "Romaneio de produtosA e produtosB", "/estoque/produtosA"),
                Lembrete("acerto-caio", "Acerto do Caio (Novo Dia e Bem Servido) e comissões do barracão", "/estoque/volumes"),
                Lembrete("boletos-pagos", "Boletos pagos (Jatobá, Girassol, Cedro)", "/boletos"),
            ]),
            new(6, "Fim de semana", [
                AcertoTarefa(),
            ]),
        };

        // ROT-05: o fechamento do mês anterior
        var mesAnt = hoje.AddMonths(-1);
        var semParar = await db.SemPararControles.AsNoTracking().AnyAsync(s => s.Ano == mesAnt.Year && s.Mes == mesAnt.Month);
        var seguro = await db.PlanilhasSeguro.AsNoTracking().AnyAsync(s => s.Ano == mesAnt.Year && s.Mes == mesAnt.Month);
        var nomeMes = Mes(mesAnt.Month);
        var mensal = new List<Tarefa>
        {
            new("sem-parar", $"Sem Parar de {nomeMes}", semParar ? "Controle do mês já está guardado." : "Pedágio por placa ainda não fechado.",
                "/semparar", semParar ? "feito" : "pendente"),
            new("seguro", $"Planilha SG de {nomeMes}", seguro ? "Planilha do mês já está guardada." : "Divisão do seguro por placa ainda não feita.",
                "/cte-export", seguro ? "feito" : "pendente"),
            Lembrete("despesas", "Despesas divididas por caminhão", "/despesas-divididas", "Valor por caminhão e divisão por sócio."),
        };

        return (rotina, mensal);
    }

    private static string Contar(int n, string um, string varios) => n == 1 ? $"1 {um}" : $"{n} {varios}";

    private static string Mes(int m) => m switch
    {
        1 => "janeiro", 2 => "fevereiro", 3 => "março", 4 => "abril", 5 => "maio", 6 => "junho",
        7 => "julho", 8 => "agosto", 9 => "setembro", 10 => "outubro", 11 => "novembro", _ => "dezembro",
    };
}
