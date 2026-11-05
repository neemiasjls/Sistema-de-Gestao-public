import { useCallback, useEffect, useState } from 'react'
import { Plus, Save, Trash2, TriangleAlert, Users } from 'lucide-react'
import toast from 'react-hot-toast'
import api, { formatBRL, socioColor } from '../services/api'
import { comoObjeto } from '../utils/resposta'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Campo from '../components/ui/Campo'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { CampoNumero, NavegaMes } from '../components/viagens/ViagensPecas'
import { classeCampo } from '../components/EstoquePecas'
import { MESES } from '../services/viagensCaminhaoApi'

type Socio = 'Bento' | 'Caio' | 'Dudu'
type Linha = { id: number; descricao: string; valor: number; dividirPor: number; observacao: string | null; valorDividido: number }
type SocioMes = { socio: Socio; quantidade: number; placas: string | null; valor: number }
type Cavalo = { placa: string; responsavel: string | null }
type Mes = {
  ano: number; mes: number; modo: 'placas' | 'planilha'; linhas: Linha[]; totalDespesas: number; valorPorCaminhao: number
  nPlacas: number; socios: SocioMes[]; semResponsavel: string[]; placasMarcadas: string[]; sugestao: string[]; cavalos: Cavalo[]
}
type Resumo = {
  ano: number
  meses: { mes: number; modo: 'placas' | 'planilha'; valorPorCaminhao: number; socios: SocioMes[] }[]
  totais: { socio: Socio; quantidade: number; valor: number }[]
}

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao
const arred = (v: number) => Math.round((v + Number.EPSILON) * 100) / 100
const base = '/api/despesas-divididas'
const SOCIOS: Socio[] = ['Bento', 'Caio', 'Dudu']
const colunasLinha = (placas: boolean) => placas
  ? 'sm:grid-cols-[minmax(0,2fr)_8rem_7rem_minmax(0,2fr)_auto]'
  : 'sm:grid-cols-[minmax(0,2fr)_8rem_5rem_7rem_minmax(0,2fr)_auto]'

type Rascunho = { id: number | null; descricao: string; valor: number | null; dividirPor: number; observacao: string }
const doRegistro = (l: Linha): Rascunho => ({ id: l.id, descricao: l.descricao, valor: l.valor, dividirPor: l.dividirPor, observacao: l.observacao ?? '' })
const novo = (): Rascunho => ({ id: null, descricao: '', valor: null, dividirPor: 1, observacao: '' })

function LinhaEditavel({ inicial, ano, mes, nPlacas, onSalvou, onExcluir, onCancelar }: {
  inicial: Rascunho; ano: number; mes: number; nPlacas?: number; onSalvou: () => void; onExcluir?: () => void; onCancelar?: () => void
}) {
  const [r, setR] = useState(inicial)
  const [salvando, setSalvando] = useState(false)
  useEffect(() => setR(inicial), [inicial])
  const sujo = r.id == null || JSON.stringify(r) !== JSON.stringify(inicial)
  const dividido = !r.valor ? 0 : nPlacas ? r.valor / nPlacas : r.dividirPor >= 1 ? r.valor / r.dividirPor : 0
  const salvar = async () => {
    if (!r.descricao.trim()) return toast.error('Informe a referência.')
    if (!r.valor || r.valor <= 0) return toast.error('Informe o valor.')
    if (!nPlacas && r.dividirPor < 1) return toast.error('Dividir por precisa ser 1 ou mais.')
    setSalvando(true)
    try {
      const dados = { ano, mes, descricao: r.descricao, valor: r.valor, dividirPor: r.dividirPor, observacao: r.observacao || null }
      if (r.id == null) await api.post(base, dados); else await api.put(`${base}/${r.id}`, dados)
      if (r.id == null) setR(novo())
      onSalvou()
    } catch (e) { toast.error(erroDe(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }
  return (
    <div className={`grid grid-cols-2 ${colunasLinha(!!nPlacas)} gap-2 items-end rounded-xl border border-gray-200 bg-white p-3 sm:border-0 sm:border-b sm:rounded-none sm:p-2`}>
      <Campo rotulo="Referência" className="col-span-2 sm:col-span-1">
        <input className={classeCampo} value={r.descricao} onChange={e => setR({ ...r, descricao: e.target.value })} />
      </Campo>
      <Campo rotulo="Valor"><CampoNumero valor={r.valor} vazioNulo onChange={v => setR({ ...r, valor: v })} className={`${classeCampo} text-right`} /></Campo>
      {!nPlacas && <Campo rotulo="Div. por">
        <input type="number" min={1} step={1} className={`${classeCampo} text-right tabular-nums`} value={r.dividirPor}
          onChange={e => setR({ ...r, dividirPor: Math.max(1, Math.floor(Number(e.target.value) || 1)) })} />
      </Campo>}
      <div className="min-w-0">
        <p className="text-xs text-gray-500">{nPlacas ? `Valor por placa (÷ ${nPlacas})` : 'Valor dividido'}</p>
        <p className="min-h-11 sm:min-h-10 flex items-center justify-end font-semibold tabular-nums text-gray-900">{formatBRL(arred(dividido))}</p>
      </div>
      <Campo rotulo="Observações" className="col-span-2 sm:col-span-1">
        <input className={classeCampo} value={r.observacao} onChange={e => setR({ ...r, observacao: e.target.value })} />
      </Campo>
      <div className="col-span-2 sm:col-span-1 flex justify-end gap-1">
        {sujo && <Botao tamanho="sm" variante="primario" icone={r.id == null ? Plus : Save} onClick={salvar} disabled={salvando}>
          {r.id == null ? 'Incluir' : 'Salvar'}</Botao>}
        {onExcluir && <BotaoIcone icone={Trash2} rotulo="Excluir linha" perigo onClick={onExcluir} />}
        {onCancelar && <Botao tamanho="sm" onClick={onCancelar}>Cancelar</Botao>}
      </div>
    </div>
  )
}

export default function DespesasDivididasPage() {
  const confirmar = useConfirmar()
  const hojeData = new Date()
  const [mes, setMes] = useState(hojeData.getMonth() + 1)
  const [ano, setAno] = useState(hojeData.getFullYear())
  const [dados, setDados] = useState<Mes | null>(null)
  const [resumo, setResumo] = useState<Resumo | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [socios, setSocios] = useState<SocioMes[]>([])
  const [salvandoSocios, setSalvandoSocios] = useState(false)
  const [marcadas, setMarcadas] = useState<string[]>([])
  const [salvandoPlacas, setSalvandoPlacas] = useState(false)

  const carregar = useCallback(() => {
    setFalhou(false)
    api.get(base, { params: { ano, mes } }).then(r => {
      const d = comoObjeto<Mes>(r.data, ['linhas', 'socios', 'placasMarcadas', 'sugestao', 'cavalos', 'semResponsavel'])
      setDados(d); setSocios(d.socios.map(s => ({ ...s })))
      setMarcadas(d.placasMarcadas.length > 0 ? d.placasMarcadas : d.sugestao)
    }).catch(() => setFalhou(true))
    api.get(`${base}/resumo`, { params: { ano } }).then(r => setResumo(comoObjeto<Resumo>(r.data, ['meses', 'totais']))).catch(() => setResumo(null))
  }, [ano, mes])
  useEffect(() => { setDados(null); carregar() }, [carregar])

  const excluir = async (l: Linha) => {
    if (!await confirmar({ titulo: `Excluir "${l.descricao}"?`, confirmar: 'Excluir' })) return
    try { await api.delete(`${base}/${l.id}`); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const salvarSocios = async () => {
    setSalvandoSocios(true)
    try {
      await api.put(`${base}/socios`, { ano, mes, socios: socios.map(s => ({ socio: s.socio, quantidade: s.quantidade, placas: s.placas || null })) })
      toast.success('Divisão salva.'); carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui salvar a divisão.')) }
    finally { setSalvandoSocios(false) }
  }

  const salvarPlacas = async () => {
    setSalvandoPlacas(true)
    try {
      await api.put(`${base}/placas`, { ano, mes, placas: marcadas })
      toast.success(marcadas.length === 0 ? 'Marcação limpa: o mês volta para a planilha.' : 'Placas do mês salvas.'); carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui salvar as placas.')) }
    finally { setSalvandoPlacas(false) }
  }
  const alternarPlaca = (placa: string) =>
    setMarcadas(m => m.includes(placa) ? m.filter(x => x !== placa) : [...m, placa])

  const modoPlacas = dados?.modo === 'placas'
  const porCaminhao = dados?.valorPorCaminhao ?? 0
  const naoGravado = dados != null && dados.placasMarcadas.length === 0
  const placasSujas = dados != null && (naoGravado ? marcadas.length > 0
    : JSON.stringify([...marcadas].sort()) !== JSON.stringify([...dados.placasMarcadas].sort()))
  const sociosSujos = dados != null && JSON.stringify(socios.map(s => [s.quantidade, s.placas ?? ''])) !==
    JSON.stringify(dados.socios.map(s => [s.quantidade, s.placas ?? '']))

  return <div className="space-y-6 max-w-6xl">
    <CabecalhoTela descricao="Anote as despesas do mês e marque as placas que rodaram. O total dividido pelas placas é o valor por placa; cada sócio paga esse valor vezes as placas dele."
      acoes={<NavegaMes mes={mes} ano={ano} onChange={(m, a) => { setMes(m); setAno(a) }} />} />

    {falhou ? <ErroCarga mensagem="Não consegui carregar o mês." onTentar={carregar} />
      : dados == null ? <Carregando linhas={5} />
      : <>
        <section className="space-y-3" aria-label="Despesas do mês">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <h3 className="font-display text-lg font-semibold text-gray-900">Despesas de {MESES[mes - 1]} de {ano}</h3>
            <div className="rounded-xl bg-tinta-700 text-white px-4 py-3 text-right">
              <p className="text-xs opacity-80">{modoPlacas ? `Valor por placa (÷ ${dados.nPlacas} ${dados.nPlacas === 1 ? 'placa' : 'placas'})` : 'Valor por caminhão'}</p>
              <p className="font-display text-2xl font-semibold tabular-nums">{formatBRL(porCaminhao)}</p>
            </div>
          </div>
          <div className={`hidden sm:grid ${colunasLinha(modoPlacas)} gap-2 px-2 text-xs text-gray-500`} aria-hidden>
            <span>Referência</span><span className="text-right">Valor</span>
            {!modoPlacas && <span className="text-right">Div. por</span>}
            <span className="text-right">{modoPlacas ? `÷ ${dados.nPlacas} ${dados.nPlacas === 1 ? 'placa' : 'placas'}` : 'Valor dividido'}</span><span>Observações</span><span className="w-[7.5rem]" />
          </div>
          <div className="space-y-2 sm:space-y-0">
            {dados.linhas.length === 0 && <Vazio compacto titulo="Nenhuma despesa neste mês">Inclua a primeira na linha abaixo.</Vazio>}
            {dados.linhas.map(l => <LinhaEditavel key={l.id} inicial={doRegistro(l)} ano={ano} mes={mes} nPlacas={modoPlacas ? dados.nPlacas : undefined} onSalvou={carregar} onExcluir={() => excluir(l)} />)}
            <LinhaEditavel inicial={novo()} ano={ano} mes={mes} nPlacas={modoPlacas ? dados.nPlacas : undefined} onSalvou={carregar} />
          </div>
          <p className="text-right text-sm text-gray-700">Total das despesas {formatBRL(dados.totalDespesas)} · {modoPlacas ? 'valor por placa' : 'valor total dividido'} <strong className="tabular-nums">{formatBRL(porCaminhao)}</strong></p>
        </section>

        <section className="space-y-3" aria-label="Placas que rodaram no mês">
          <h3 className="font-display text-lg font-semibold text-gray-900">Placas que rodaram no mês</h3>
          {naoGravado && dados.sugestao.length > 0 &&
            <p className="text-sm text-gray-700">Sugerido pelas viagens do mês — confira e salve.</p>}
          {naoGravado && dados.sugestao.length === 0 && dados.cavalos.length > 0 &&
            <p className="text-sm text-gray-700">Nenhuma viagem encontrada neste mês. Marque as placas que rodaram.</p>}
          {dados.cavalos.length === 0
            ? <Vazio compacto icone={Users} titulo="Nenhum cavalo ativo cadastrado">Cadastre os cavalos em Placas.</Vazio>
            : <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {dados.cavalos.map(c => {
                const marcada = marcadas.includes(c.placa)
                const dono = SOCIOS.includes(c.responsavel as Socio) ? c.responsavel as Socio : null
                const cor = dono ? socioColor(dono) : null
                return <label key={c.placa} className={`flex items-center gap-3 min-h-11 rounded-xl border p-3 cursor-pointer ${marcada ? 'border-tinta-600 bg-white' : 'border-gray-200 bg-gray-50'}`}>
                  <input type="checkbox" className="w-5 h-5" checked={marcada} onChange={() => alternarPlaca(c.placa)} />
                  <span className="font-semibold tabular-nums text-gray-900">{c.placa}</span>
                  {cor ? <span className={`ml-auto inline-flex items-center gap-1.5 text-sm ${cor.text}`}><span className={`w-2.5 h-2.5 rounded-full ${cor.dot}`} />{dono}</span>
                    : <span className="ml-auto text-sm text-gray-500">Sem responsável</span>}
                </label>
              })}
            </div>}
          <div className="flex flex-wrap items-center justify-between gap-2">
            <p className="text-sm text-gray-700">{marcadas.length} {marcadas.length === 1 ? 'placa marcada' : 'placas marcadas'}</p>
            <Botao variante="primario" icone={Save} onClick={salvarPlacas} disabled={salvandoPlacas || !placasSujas}>Salvar placas</Botao>
          </div>
        </section>

        <section className="space-y-3" aria-label="Divisão por sócio">
          <h3 className="font-display text-lg font-semibold text-gray-900">Divisão por sócio</h3>
          {modoPlacas
            ? <>
              {dados.semResponsavel.length > 0 &&
                <p className="flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
                  <TriangleAlert className="w-4 h-4 mt-0.5 shrink-0" aria-hidden />
                  <span>Sem responsável: {dados.semResponsavel.join(', ')}. A placa conta na divisão, mas não cai para nenhum sócio.</span>
                </p>}
              <div className="grid gap-3 sm:grid-cols-3">
                {dados.socios.map(s => {
                  const cor = socioColor(s.socio)
                  return <div key={s.socio} className={`rounded-xl border ${cor.border} ${cor.bg} p-3 space-y-1`}>
                    <p className={`font-semibold ${cor.text} flex items-center gap-2`}><span className={`w-2.5 h-2.5 rounded-full ${cor.dot}`} />{s.socio}</p>
                    <p className="text-sm text-gray-700">{s.quantidade} {s.quantidade === 1 ? 'placa' : 'placas'}{s.placas ? ` · ${s.placas}` : ''}</p>
                    <p className="text-sm text-gray-700">Paga <strong className="tabular-nums text-gray-900">{formatBRL(s.valor)}</strong></p>
                  </div>
                })}
              </div>
            </>
            : <>
              <p className="text-sm text-gray-700">Mês lançado pela planilha antiga.</p>
              <div className="grid gap-3 sm:grid-cols-3">
                {socios.map((s, i) => {
                  const cor = socioColor(s.socio)
                  return <div key={s.socio} className={`rounded-xl border ${cor.border} ${cor.bg} p-3 space-y-2`}>
                    <p className={`font-semibold ${cor.text} flex items-center gap-2`}><span className={`w-2.5 h-2.5 rounded-full ${cor.dot}`} />{s.socio}</p>
                    <div className="grid grid-cols-[5rem_minmax(0,1fr)] gap-2">
                      <Campo rotulo="Caminhões">
                        <input type="number" min={0} step={1} className={`${classeCampo} text-right tabular-nums`} value={s.quantidade}
                          onChange={e => setSocios(socios.map((x, j) => j === i ? { ...x, quantidade: Math.max(0, Math.floor(Number(e.target.value) || 0)) } : x))} />
                      </Campo>
                      <Campo rotulo="Placas">
                        <input className={classeCampo} value={s.placas ?? ''} placeholder="Ex.: VOLUME-XAC"
                          onChange={e => setSocios(socios.map((x, j) => j === i ? { ...x, placas: e.target.value } : x))} />
                      </Campo>
                    </div>
                    <p className="text-sm text-gray-700">Paga <strong className="tabular-nums text-gray-900">{formatBRL(porCaminhao * s.quantidade)}</strong></p>
                  </div>
                })}
              </div>
              <div className="flex justify-end">
                <Botao variante="primario" icone={Save} onClick={salvarSocios} disabled={salvandoSocios || !sociosSujos}>Salvar divisão</Botao>
              </div>
            </>}
        </section>

        <section className="space-y-3" aria-label="Resumo do ano">
          <h3 className="font-display text-lg font-semibold text-gray-900">Resumo de {ano}</h3>
          {resumo == null ? <Carregando linhas={3} />
            : resumo.meses.length === 0 ? <Vazio compacto icone={Users} titulo={`Nada lançado em ${ano}`}>Os meses aparecem aqui depois de lançados.</Vazio>
            : <>
              <div className="hidden sm:block overflow-x-auto rounded-xl border border-gray-200 bg-white">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 text-gray-600">
                    <tr><th className="text-left p-2">Mês</th><th className="text-right p-2">Por caminhão ou placa</th>
                      {resumo.totais.map(t => <th key={t.socio} className={`text-left p-2 ${socioColor(t.socio).text}`}>{t.socio}</th>)}</tr>
                  </thead>
                  <tbody>
                    {resumo.meses.map(m => <tr key={m.mes} className="border-t border-gray-100">
                      <td className="p-2">{MESES[m.mes - 1]}</td>
                      <td className="p-2 text-right tabular-nums">{formatBRL(m.valorPorCaminhao)}</td>
                      {m.socios.map(s => <td key={s.socio} className="p-2">
                        <span className="tabular-nums">{s.quantidade} {m.modo === 'placas' ? (s.quantidade === 1 ? 'placa' : 'placas') : (s.quantidade === 1 ? 'caminhão' : 'caminhões')} · <strong>{formatBRL(s.valor)}</strong></span>
                        {s.placas && <span className="block text-xs text-gray-500">{s.placas}</span>}
                      </td>)}
                    </tr>)}
                  </tbody>
                  <tfoot className="border-t-2 border-gray-200 font-semibold">
                    <tr><td className="p-2" colSpan={2}>Total do ano</td>
                      {resumo.totais.map(t => <td key={t.socio} className="p-2 tabular-nums">{formatBRL(t.valor)}</td>)}</tr>
                  </tfoot>
                </table>
              </div>
              <div className="sm:hidden space-y-2">
                {resumo.meses.map(m => <div key={m.mes} className="rounded-xl border border-gray-200 bg-white p-3 space-y-1">
                  <p className="flex justify-between font-semibold text-gray-900"><span>{MESES[m.mes - 1]}</span><span className="tabular-nums">{formatBRL(m.valorPorCaminhao)} por {m.modo === 'placas' ? 'placa' : 'caminhão'}</span></p>
                  {m.socios.map(s => <p key={s.socio} className="flex justify-between text-sm">
                    <span className={socioColor(s.socio).text}>{s.socio} · {s.quantidade}{s.placas ? ` (${s.placas})` : ''}</span>
                    <span className="tabular-nums">{formatBRL(s.valor)}</span></p>)}
                </div>)}
                <div className="rounded-xl bg-gray-50 border border-gray-200 p-3 space-y-1">
                  <p className="font-semibold text-gray-900">Total do ano</p>
                  {resumo.totais.map(t => <p key={t.socio} className="flex justify-between text-sm">
                    <span className={socioColor(t.socio).text}>{t.socio}</span><strong className="tabular-nums">{formatBRL(t.valor)}</strong></p>)}
                </div>
              </div>
            </>}
        </section>
      </>}
  </div>
}
