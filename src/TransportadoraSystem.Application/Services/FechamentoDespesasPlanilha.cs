using System.Globalization;
using static TransportadoraSystem.Application.Services.ViagemCaminhaoCalculo;

namespace TransportadoraSystem.Application.Services;

/// <summary>
/// Mescla da aba DESPESAS DO MES do acerto com o fechamento do mês (VIA-29, UI-22): o que já está
/// no sistema é achado e atualizado, o novo é acrescentado, nada é duplicado nem apagado (fora a
/// linha manual "gêmea" de uma automática). Função pura: não lê nem grava banco.
/// Automáticas nunca viram manual: FGTS/INSS/IRR/QUINQUÊNIO gravam os campos do salário;
/// ICMS/SEGURO fica o do sistema (planilha SG) e só entra o da planilha quando o sistema não tem
/// valor; CONSÓRCIO diferente do cadastro vale o da planilha naquele mês (linha corrigida, pergunta 4
/// do plano, 11/11/2026). Mês fechado: nada a gravar, só as diferenças.
/// </summary>
public static class FechamentoDespesasPlanilha
{
    public enum Classe { Fgts, Inss, Irr, Quinquenio, Icms, Consorcio, Manual }

    public record LinhaManual(int Id, DateOnly? Data, string Referencia, decimal? Valor, string? Parcela, string? Observacao, int Ordem);
    /// <summary>Linha automática como o fechamento mostra agora (já com a correção, se houver).</summary>
    public record LinhaAutomatica(string Origem, string Referencia, decimal? Valor, string? Parcela, string? Observacao);
    public record CreditoGravado(int Id, string Referencia, decimal Valor);

    public record Estado(bool Fechado, List<LinhaManual> Manuais, decimal Fgts, decimal Inss, decimal Irr, decimal Quinquenio,
        List<LinhaAutomatica> Automaticas)
    {
        public List<CreditoGravado> Creditos { get; init; } = [];
    }

    public record Troca(string Campo, string Anterior, string Atual);
    public record Substituida(int Id, DespesaMesLida Lida, List<Troca> Trocas);
    public record CampoSalario(string Campo, decimal Anterior, decimal Atual);
    /// <summary>Automática que passa a ter o valor da planilha (gravada como corrigida).</summary>
    public record AutomaticaCorrigida(string Origem, string Referencia, decimal? Anterior, decimal Valor, string? Parcela, string? Observacao);
    public record CreditoSubstituido(int Id, string Referencia, decimal Anterior, decimal Atual);

    public record Plano(
        List<DespesaMesLida> Novas, List<Substituida> Substituidas, int Iguais, List<LinhaManual> SoNoSistema,
        List<CampoSalario> CamposSalario, List<AutomaticaCorrigida> AutomaticasCorrigidas, List<LinhaManual> GemeasARetirar,
        List<CreditoMesLido> CreditosNovos, List<CreditoSubstituido> CreditosSubstituidos, int CreditosIguais,
        List<CreditoGravado> CreditosSoNoSistema, List<string> Diferencas, List<string> Avisos)
    {
        public decimal? IcmsCorrigido => AutomaticasCorrigidas.FirstOrDefault(a => a.Origem == "icms")?.Valor;
        public bool NadaAGravar => Novas.Count == 0 && Substituidas.Count == 0 && CamposSalario.Count == 0
            && AutomaticasCorrigidas.Count == 0 && GemeasARetirar.Count == 0 && CreditosNovos.Count == 0 && CreditosSubstituidos.Count == 0;
    }

    public static Classe Classificar(string? referencia)
    {
        var n = Normalizar(referencia);
        if (n == "FGTS") return Classe.Fgts;
        if (n == "INSS") return Classe.Inss;
        if (n is "IRR" or "IRRF") return Classe.Irr;
        if (n.StartsWith("QUINQU")) return Classe.Quinquenio;
        if (n.StartsWith("ICMS") || n.Contains("SEGURO CARGA")) return Classe.Icms;
        if (n.Contains("CONSORCIO")) return Classe.Consorcio;
        return Classe.Manual;
    }

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    public static string Reais(decimal? v) => v == null ? "vazio" : "R$ " + v.Value.ToString("N2", Br);
    private static bool Igual(decimal? a, decimal? b) => a != null && b != null && Math.Abs(a.Value - b.Value) <= 0.01m;
    private static bool MesmoTexto(string? a, string? b) => Normalizar(a) == Normalizar(b);

    private static readonly (Classe Classe, string Campo, string Nome)[] Salario =
    [
        (Classe.Fgts, "Fgts", "FGTS"), (Classe.Inss, "Inss", "INSS"), (Classe.Irr, "Irr", "IRR"), (Classe.Quinquenio, "Quinquenio", "QUINQUÊNIO"),
    ];

    private static decimal CampoAtual(Estado e, Classe c) => c switch
    {
        Classe.Fgts => e.Fgts, Classe.Inss => e.Inss, Classe.Irr => e.Irr, _ => e.Quinquenio,
    };

    public static Plano Mesclar(Estado atual, IReadOnlyList<DespesaMesLida> lidas, IReadOnlyList<CreditoMesLido>? creditosLidos = null)
    {
        var novas = new List<DespesaMesLida>();
        var substituidas = new List<Substituida>();
        var iguais = 0;
        var campos = new List<CampoSalario>();
        var corrigidas = new List<AutomaticaCorrigida>();
        var gemeas = new List<LinhaManual>();
        var diferencas = new List<string>();
        var avisos = new List<string>();

        // ── Retenções do salário: gravam o campo (a linha "salario:X" continua uma só) ──
        var camposDepois = new Dictionary<Classe, decimal>();
        foreach (var (classe, campo, nome) in Salario)
        {
            var antes = CampoAtual(atual, classe);
            camposDepois[classe] = antes;
            var linhas = lidas.Where(l => Classificar(l.Referencia) == classe).ToList();
            if (linhas.Count == 0) continue;
            var valor = linhas.Sum(l => l.Valor);
            if (Igual(antes, valor)) { iguais++; continue; }
            campos.Add(new CampoSalario(campo, antes, valor));
            camposDepois[classe] = valor;
            if (antes != 0) avisos.Add($"{nome}: já tinha {Reais(antes)} no sistema, substituído: {Reais(antes)} → {Reais(valor)}");
        }

        // ── ICMS/SEGURO CARGAS: vale o do sistema; sem valor no sistema, entra o da planilha ──
        var icmsLidas = lidas.Where(l => Classificar(l.Referencia) == Classe.Icms).ToList();
        var icms = atual.Automaticas.FirstOrDefault(a => a.Origem == "icms");
        var icmsTemValor = icms?.Valor != null;
        if (icmsLidas.Count > 0)
        {
            var valor = icmsLidas.Sum(l => l.Valor);
            var referencia = icms?.Referencia ?? "ICMS/SEGURO CARGAS";
            if (icms?.Valor == null)
            {
                corrigidas.Add(new AutomaticaCorrigida("icms", referencia, null, valor, icms?.Parcela, icms?.Observacao));
                avisos.Add($"{referencia}: o sistema não tinha valor (falta a planilha SG); entrou o da planilha, {Reais(valor)}");
                icmsTemValor = true;
            }
            else if (Igual(icms.Valor, valor)) iguais++;
            else diferencas.Add($"{referencia}: planilha {Reais(valor)} × sistema {Reais(icms.Valor)} (fica o do sistema, da planilha SG)");
        }

        // ── CONSÓRCIO: casa com as linhas do cadastro; diferente, vale o da planilha naquele mês ──
        var consAuto = atual.Automaticas.Where(a => a.Origem.StartsWith("consorcio:")).ToList();
        var consLidas = lidas.Where(l => Classificar(l.Referencia) == Classe.Consorcio).ToList();
        var consSemCadastro = new List<DespesaMesLida>();
        {
            var livres = consAuto.ToList();
            var pares = new List<(DespesaMesLida L, LinhaAutomatica A)>();
            foreach (var l in consLidas.ToList())
                if (livres.FirstOrDefault(a => Igual(a.Valor, l.Valor)) is { } a) { pares.Add((l, a)); livres.Remove(a); consLidas.Remove(l); }
            foreach (var l in consLidas)
                if (livres.FirstOrDefault() is { } a) { pares.Add((l, a)); livres.Remove(a); }
                else consSemCadastro.Add(l);
            foreach (var (l, a) in pares)
            {
                if (Igual(a.Valor, l.Valor)) { iguais++; continue; }
                corrigidas.Add(new AutomaticaCorrigida(a.Origem, a.Referencia, a.Valor, l.Valor, a.Parcela, a.Observacao));
                avisos.Add($"{a.Referencia}: já tinha {Reais(a.Valor)} no sistema, substituído: {Reais(a.Valor)} → {Reais(l.Valor)}");
            }
            if (consAuto.Count == 0)
                foreach (var l in consSemCadastro)
                    avisos.Add($"{l.Referencia}: consórcio sem cadastro, entrou como despesa manual; cadastre para os próximos meses");
        }

        // ── Gêmeas: linha manual de classe automática quando a automática existe com valor ──
        var manuais = new List<LinhaManual>();
        foreach (var m in atual.Manuais)
        {
            var classe = Classificar(m.Referencia);
            var deOnde = classe switch
            {
                Classe.Consorcio when consAuto.Count > 0 => "já vem do cadastro de consórcio",
                Classe.Icms when icmsTemValor => "já vem da planilha SG",
                Classe.Fgts or Classe.Inss or Classe.Irr or Classe.Quinquenio when camposDepois[classe] > 0 => "já vem da parte Salário",
                _ => null,
            };
            if (deOnde == null) { manuais.Add(m); continue; }
            gemeas.Add(m);
            avisos.Add($"linha manual {m.Referencia} ({Reais(m.Valor)}) retirada: {deOnde}");
        }

        // ── Manuais: pela referência normalizada; repetida casa primeiro as de valor igual, depois em ordem ──
        var pool = lidas.Where(l => Classificar(l.Referencia) == Classe.Manual).Concat(consSemCadastro).OrderBy(l => l.Linha).ToList();
        var usadas = new HashSet<int>();
        var pareadas = new Dictionary<DespesaMesLida, LinhaManual>();
        var porChave = manuais.OrderBy(m => m.Ordem).ThenBy(m => m.Id).GroupBy(m => Normalizar(m.Referencia)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (Func<DespesaMesLida, LinhaManual, bool> criterio in new Func<DespesaMesLida, LinhaManual, bool>[]
        {
            (l, m) => Igual(l.Valor, m.Valor) && MesmoTexto(l.Parcela, m.Parcela),
            (l, m) => Igual(l.Valor, m.Valor),
            (_, _) => true,
        })
            foreach (var l in pool.Where(l => !pareadas.ContainsKey(l)))
                if (porChave.TryGetValue(Normalizar(l.Referencia), out var cands)
                    && cands.FirstOrDefault(m => !usadas.Contains(m.Id) && criterio(l, m)) is { } m)
                { pareadas[l] = m; usadas.Add(m.Id); }

        foreach (var l in pool)
        {
            if (!pareadas.TryGetValue(l, out var m)) { novas.Add(l); continue; }
            var trocas = new List<Troca>();
            if (!Igual(l.Valor, m.Valor)) trocas.Add(new Troca("Valor", Reais(m.Valor), Reais(l.Valor)));
            if (l.Data != null && l.Data != m.Data) trocas.Add(new Troca("Data", m.Data?.ToString("dd/MM/yyyy") ?? "vazio", l.Data.Value.ToString("dd/MM/yyyy")));
            if (l.Parcela != null && !MesmoTexto(l.Parcela, m.Parcela)) trocas.Add(new Troca("Parcela", m.Parcela ?? "vazio", l.Parcela));
            if (l.Observacao != null && !MesmoTexto(l.Observacao, m.Observacao)) trocas.Add(new Troca("Observação", m.Observacao ?? "vazio", l.Observacao));
            if (trocas.Count == 0) { iguais++; continue; }
            substituidas.Add(new Substituida(m.Id, l, trocas));
            foreach (var t in trocas.Where(t => t.Anterior != "vazio"))
                avisos.Add(t.Campo == "Valor"
                    ? $"{l.Referencia}: já tinha {t.Anterior} no sistema, substituído: {t.Anterior} → {t.Atual}"
                    : $"{l.Referencia}: já tinha {t.Campo.ToLowerInvariant()} {t.Anterior} no sistema, substituído: {t.Anterior} → {t.Atual}");
        }
        var soNoSistema = manuais.Where(m => !usadas.Contains(m.Id)).ToList();

        // ── Créditos do período: referência + valor, mesma regra ──
        var credNovos = new List<CreditoMesLido>();
        var credSubst = new List<CreditoSubstituido>();
        var credIguais = 0;
        var credUsados = new HashSet<int>();
        var credPares = new Dictionary<CreditoMesLido, CreditoGravado>();
        var credLidos = creditosLidos ?? [];
        foreach (var exato in new[] { true, false })
            foreach (var l in credLidos.Where(l => !credPares.ContainsKey(l)))
                if (atual.Creditos.Where(c => !credUsados.Contains(c.Id) && MesmoTexto(c.Referencia, l.Referencia))
                        .OrderBy(c => c.Id).FirstOrDefault(c => !exato || Igual(c.Valor, l.Valor)) is { } c)
                { credPares[l] = c; credUsados.Add(c.Id); }
        foreach (var l in credLidos)
        {
            if (!credPares.TryGetValue(l, out var c)) { credNovos.Add(l); continue; }
            if (Igual(c.Valor, l.Valor)) { credIguais++; continue; }
            credSubst.Add(new CreditoSubstituido(c.Id, c.Referencia, c.Valor, l.Valor));
            avisos.Add($"Crédito {l.Referencia}: já tinha {Reais(c.Valor)} no sistema, substituído: {Reais(c.Valor)} → {Reais(l.Valor)}");
        }
        var credSo = atual.Creditos.Where(c => !credUsados.Contains(c.Id)).ToList();

        if (!atual.Fechado)
            return new Plano(novas, substituidas, iguais, soNoSistema, campos, corrigidas, gemeas,
                credNovos, credSubst, credIguais, credSo, diferencas, avisos);

        // ── Mês fechado: nada é gravado; o que seria gravado vira diferença planilha × fechamento ──
        var difs = new List<string>();
        foreach (var l in novas) difs.Add($"{l.Referencia} {Reais(l.Valor)}: está na planilha e não no fechamento");
        foreach (var s in substituidas)
            foreach (var t in s.Trocas)
                difs.Add($"{s.Lida.Referencia}{(t.Campo == "Valor" ? "" : $" ({t.Campo.ToLowerInvariant()})")}: planilha {t.Atual} × fechamento {t.Anterior}");
        foreach (var c in campos)
            difs.Add($"{Salario.First(x => x.Campo == c.Campo).Nome}: planilha {Reais(c.Atual)} × fechamento {Reais(c.Anterior)}");
        foreach (var a in corrigidas) difs.Add($"{a.Referencia}: planilha {Reais(a.Valor)} × fechamento {Reais(a.Anterior)}");
        foreach (var d in diferencas) difs.Add(d.Replace("× sistema", "× fechamento").Replace(" (fica o do sistema, da planilha SG)", ""));
        foreach (var l in credNovos) difs.Add($"Crédito {l.Referencia} {Reais(l.Valor)}: está na planilha e não no fechamento");
        foreach (var c in credSubst) difs.Add($"Crédito {c.Referencia}: planilha {Reais(c.Atual)} × fechamento {Reais(c.Anterior)}");
        return new Plano([], [], iguais, soNoSistema, [], [], [], [], [], credIguais, credSo, difs,
            [difs.Count == 0 ? "Mês fechado: a planilha confere com o fechamento." : "Mês fechado: nada foi gravado; veja as diferenças planilha × fechamento."]);
    }
}
