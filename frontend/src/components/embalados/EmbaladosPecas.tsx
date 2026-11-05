import { useEffect, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { Pencil, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone } from '../ui/Botao'
import Campo from '../ui/Campo'
import ImportarLancamentos from '../ImportarLancamentos'
import Aviso from '../ui/Aviso'
import { Modal } from '../ModalBase'
import { classeCampo } from '../EstoquePecas'
import { brl, lerNum, num } from '../../utils/formato'
import { embaladosApi, type Periodo, type Resumo } from '../../services/embaladosApi'

// Peças das telas do módulo Embalados (VOLUME-16..18): período, resumo, lista
// (tabela no computador, cartões no celular — UI-05) e o formulário.

export const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
export const nomeMes = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1] ?? ''} de ${iso.slice(0, 4)}`

const CHAVE_PERIODO = 'trx_embalados_periodo'

/** Ano e mês escolhidos (mês vazio = o ano todo); lembrado neste navegador. */
export function usePeriodo() {
  const agora = new Date()
  const [ano, setAno] = useState(() => {
    try { const v = JSON.parse(localStorage.getItem(CHAVE_PERIODO) ?? 'null'); if (v?.ano) return Number(v.ano) } catch { /* sem armazenamento */ }
    return agora.getFullYear()
  })
  const [mes, setMes] = useState<number | null>(() => {
    try { const v = JSON.parse(localStorage.getItem(CHAVE_PERIODO) ?? 'null'); if (v && 'mes' in v) return v.mes } catch { /* sem armazenamento */ }
    return agora.getMonth() + 1
  })
  useEffect(() => {
    try { localStorage.setItem(CHAVE_PERIODO, JSON.stringify({ ano, mes })) } catch { /* sem armazenamento */ }
  }, [ano, mes])

  const p2 = (n: number) => String(n).padStart(2, '0')
  const periodo: Periodo = mes
    ? { de: `${ano}-${p2(mes)}-01`, ate: `${ano}-${p2(mes)}-${p2(new Date(ano, mes, 0).getDate())}` }
    : { de: `${ano}-01-01`, ate: `${ano}-12-31` }
  const rotulo = mes ? `${MESES[mes - 1]} de ${ano}` : `${ano} inteiro`

  const filtro = (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Período">
      <select value={mes ?? ''} onChange={e => setMes(e.target.value ? Number(e.target.value) : null)}
              aria-label="Mês" className={`${classeCampo} w-auto`}>
        <option value="">Ano todo</option>
        {MESES.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
      </select>
      <select value={ano} onChange={e => setAno(Number(e.target.value))} aria-label="Ano" className={`${classeCampo} w-auto`}>
        {Array.from({ length: agora.getFullYear() - 2024 + 1 }, (_, i) => agora.getFullYear() - i).map(a => <option key={a} value={a}>{a}</option>)}
      </select>
    </div>
  )
  return { periodo, rotulo, filtro }
}

/** Comissões − despesas = sobra, dividida por 3 (como o fim da aba COMISSÕES). */
export function ResumoEmbalados({ periodo, rotulo, versao }: { periodo: Periodo; rotulo: string; versao: number }) {
  const [r, setR] = useState<Resumo | null>(null)
  const [falhou, setFalhou] = useState(false)
  useEffect(() => {
    let cancelado = false
    setFalhou(false)
    embaladosApi.resumo(periodo).then(x => { if (!cancelado) setR(x) }).catch(() => { if (!cancelado) setFalhou(true) })
    return () => { cancelado = true }
  }, [periodo.de, periodo.ate, versao])

  if (falhou) return <p className="text-sm text-red-700">Não consegui calcular o resumo.</p>
  const linha = (rotuloLinha: ReactNode, valor: number | undefined, forte = false, sinal = '') => (
    <div className={`flex items-baseline justify-between gap-3 py-1.5 ${forte ? 'font-semibold text-gray-900' : 'text-gray-700'}`}>
      <dt>{rotuloLinha}</dt>
      <dd className="tabular-nums">{valor == null ? '…' : `${sinal}${brl(valor)}`}</dd>
    </div>
  )
  return (
    <section aria-label={`Resumo de ${rotulo}`} className="bg-white border border-gray-200 rounded-xl p-4">
      <h2 className="text-base font-semibold text-gray-900">Resumo · {rotulo}</h2>
      <dl className="mt-2 divide-y divide-gray-100 text-sm">
        {linha(<Link to="/embalados/comissoes" className="hover:underline">Comissões</Link>, r?.comissoes, true)}
        {linha(<Link to="/embalados/salarios" className="hover:underline">Salários, INSS e FGTS</Link>, r?.salarios, false, '− ')}
        {linha(<Link to="/embalados/despesas" className="hover:underline">Despesas das produtosB</Link>, r?.despesas, false, '− ')}
        {linha(<Link to="/embalados/quebras" className="hover:underline">Quebras</Link>, r?.quebras, false, '− ')}
        {linha('Sobra', r?.sobra, true)}
        {linha('Para cada um (÷ 3)', r?.dividido, true)}
      </dl>
      {r && r.vendasSemCompra > 0 && (
        <p className="mt-2 text-sm font-medium text-amber-800">
          {r.vendasSemCompra} {r.vendasSemCompra === 1 ? 'venda está' : 'vendas estão'} sem preço de compra: a comissão delas está inflada.
        </p>
      )}
    </section>
  )
}

/** Compara a planilha de 2026 antes de atualizar os lançamentos. */
export function ImportarPlanilhaEmbalados({ onPronto }: { onPronto: () => void }) {
  return <ImportarLancamentos caminho="/api/embalados" titulo="volumes embalados" onPronto={onPronto} />
}
// ───────────────────────────── Lista ─────────────────────────────

export type Coluna<T> = {
  rotulo: string
  valor: (item: T) => ReactNode
  /** Números à direita */
  direita?: boolean
  /** Some no cartão do celular (já está no título dele) */
  soTabela?: boolean
}

export function ListaLancamentos<T extends { id: number | null }>({ itens, colunas, titulo, onEditar, onExcluir, chave, rodape }: {
  itens: T[]
  colunas: Coluna<T>[]
  /** Linha de cima do cartão no celular */
  titulo: (item: T) => ReactNode
  onEditar: (item: T) => void
  onExcluir?: (item: T) => void
  chave: (item: T, i: number) => string | number
  rodape?: ReactNode
}) {
  const acoes = (item: T) => (
    <div className="flex items-center justify-end gap-0.5">
      <BotaoIcone icone={Pencil} rotulo="Corrigir" onClick={() => onEditar(item)} />
      {onExcluir && item.id != null && <BotaoIcone icone={Trash2} perigo rotulo="Excluir" onClick={() => onExcluir(item)} />}
    </div>
  )
  return (
    <>
      {/* Celular: um cartão por lançamento */}
      <ul className="sm:hidden space-y-2">
        {itens.map((it, i) => (
          <li key={chave(it, i)} className="bg-white border border-gray-200 rounded-xl p-3">
            <div className="flex items-start gap-2">
              <div className="flex-1 min-w-0 font-semibold text-gray-900">{titulo(it)}</div>
              {acoes(it)}
            </div>
            <dl className="mt-1 grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
              {colunas.filter(c => !c.soTabela).map(c => (
                <div key={c.rotulo} className="min-w-0">
                  <dt className="text-gray-500">{c.rotulo}</dt>
                  <dd className="text-gray-900 tabular-nums break-words">{c.valor(it)}</dd>
                </div>
              ))}
            </dl>
          </li>
        ))}
        {rodape && <li className="px-1 text-sm">{rodape}</li>}
      </ul>
      {/* Computador: tabela */}
      <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-gray-600 border-b border-gray-200">
            <tr>
              {colunas.map(c => <th key={c.rotulo} scope="col" className={`px-3 py-2 font-medium ${c.direita ? 'text-right' : 'text-left'}`}>{c.rotulo}</th>)}
              <th scope="col"><span className="sr-only">Ações</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {itens.map((it, i) => (
              <tr key={chave(it, i)} className="align-top">
                {colunas.map(c => <td key={c.rotulo} className={`px-3 py-2 ${c.direita ? 'text-right tabular-nums' : ''}`}>{c.valor(it)}</td>)}
                <td className="px-1 py-1 w-px whitespace-nowrap">{acoes(it)}</td>
              </tr>
            ))}
          </tbody>
          {rodape && <tfoot><tr className="border-t border-gray-200"><td colSpan={colunas.length + 1} className="px-3 py-2">{rodape}</td></tr></tfoot>}
        </table>
      </div>
    </>
  )
}

/** Valor em reais ou o aviso de que não veio (UI-04). */
export const dinheiroOuFalta = (v: number | null | undefined, falta = 'sem valor') =>
  v == null ? <span className="text-amber-800 font-medium">{falta}</span> : brl(v)

// ───────────────────────────── Formulário ─────────────────────────────

export type CampoForm = {
  chave: string
  rotulo: string
  tipo: 'data' | 'mes' | 'texto' | 'dinheiro' | 'numero' | 'cor'
  obrigatorio?: boolean
  dica?: ReactNode
  /** Sugestões (datalist) */
  sugestoes?: string[]
  /** Ocupa a linha toda */
  largo?: boolean
}

export type Valores = Record<string, string>

/** Número digitado, ou null se vazio. */
export const numOuNulo = (v: string | undefined) => (v ?? '').trim() === '' ? null : lerNum(v!)
/** Número para o campo de texto (vírgula). */
export const paraCampo = (v: number | null | undefined) => v == null ? '' : num(v, v % 1 === 0 ? 0 : 2)

export function FormLancamento({ titulo, campos, inicial, onSalvar, onClose, antes }: {
  titulo: string
  campos: CampoForm[]
  inicial: Valores
  onSalvar: (v: Valores) => Promise<void>
  onClose: () => void
  antes?: ReactNode
}) {
  const [v, setV] = useState<Valores>(inicial)
  const [tentou, setTentou] = useState(false)
  const [salvando, setSalvando] = useState(false)
  const faltando = campos.filter(c => c.obrigatorio && !(v[c.chave] ?? '').trim())

  const salvar = async () => {
    setTentou(true)
    if (faltando.length) return
    setSalvando(true)
    try { await onSalvar(v); onClose() }
    catch (e) { toast.error(erroDe(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo={titulo} onClose={onClose} confirmarAoFechar
           rodape={<div className="flex justify-end gap-2">
             <Botao onClick={onClose}>Cancelar</Botao>
             <Botao variante="primario" carregando={salvando} onClick={salvar}>Salvar</Botao>
           </div>}>
      {antes}
      <form onSubmit={e => { e.preventDefault(); salvar() }} className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        {campos.map(c => {
          const erro = tentou && c.obrigatorio && !(v[c.chave] ?? '').trim() ? 'Preencha este campo.' : undefined
          const muda = (x: string) => setV(p => ({ ...p, [c.chave]: x }))
          const listaId = c.sugestoes ? `sug-${c.chave}` : undefined
          return (
            <Campo key={c.chave} rotulo={c.rotulo} dica={c.dica} erro={erro} className={c.largo ? 'sm:col-span-2' : ''}>
              {c.tipo === 'cor' ? (
                <select value={v[c.chave] ?? ''} onChange={e => muda(e.target.value)} className={classeCampo}>
                  <option value="VERMELHO">Vermelho</option>
                  <option value="BRANCO">Branco</option>
                </select>
              ) : (
                <input value={v[c.chave] ?? ''} onChange={e => muda(e.target.value)} list={listaId}
                       type={c.tipo === 'data' ? 'date' : c.tipo === 'mes' ? 'month' : 'text'}
                       inputMode={c.tipo === 'dinheiro' || c.tipo === 'numero' ? 'decimal' : undefined}
                       placeholder={c.tipo === 'dinheiro' ? '0,00' : undefined}
                       className={`${classeCampo} ${c.tipo === 'dinheiro' || c.tipo === 'numero' ? 'tabular-nums' : ''}`} />
              )}
              {listaId && <datalist id={listaId}>{c.sugestoes!.map(s => <option key={s} value={s} />)}</datalist>}
            </Campo>
          )
        })}
        <button type="submit" className="hidden" aria-hidden tabIndex={-1} />
      </form>
    </Modal>
  )
}
