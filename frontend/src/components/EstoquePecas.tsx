import { useState, type ReactNode } from 'react'
import { ChevronDown, Download, Pencil, Trash2 } from 'lucide-react'
import Botao from './ui/Botao'
import Campo from './ui/Campo'
import { Modal } from './ModalBase'
import { brl, lerNum, num } from '../utils/formato'

// Peças das telas de ProdutosA e ProdutosB (Fase 2 do plano de design). As duas
// telas têm a mesma forma: escolhe a carga, vê o que sobrou e o que saiu, e
// lança saídas por cliente. O que muda é a unidade (produtoA e kg; pacote).

/** Campo de texto do estoque: 44 px no celular, borda que se vê. */
export const classeCampo =
  'w-full min-w-0 min-h-11 sm:min-h-10 rounded-lg border border-gray-300 bg-white px-3 text-sm text-gray-900 ' +
  'transition-colors hover:border-gray-400 focus:border-tinta-500'
/** Campo de número: algarismos da mesma largura, para conferir de olho. */
export const classeNumero = `${classeCampo} tabular-nums`

const ANO_ATUAL = new Date().getFullYear()

/** "20/09" no ano corrente; "20/09/2025" nos outros. */
export const diaCurto = (iso: string) => {
  if (!iso) return ''
  const [a, m, d] = iso.slice(0, 10).split('-')
  return Number(a) === ANO_ATUAL ? `${d}/${m}` : `${d}/${m}/${a}`
}

/** "1 produtoA", "12 produtosA" */
export const plural = (n: number, um: string, varios: string) => `${num(n, n % 1 ? 1 : 0)} ${Math.abs(n) === 1 ? um : varios}`

// ── Carga ─────────────────────────────────────────────────────────────────

/** Escolha da carga, com o rótulo ligado e a situação ao lado. */
export function SeletorCarga<T extends { id: number; encerrada: boolean }>({ cargas, valor, onChange, descrever }: {
  cargas: T[]
  valor: number | null
  onChange: (id: number) => void
  descrever: (c: T) => string
}) {
  return (
    <Campo rotulo="Carga" className="sm:max-w-md">
      <select value={valor ?? ''} onChange={e => onChange(Number(e.target.value))} className={classeCampo}>
        {cargas.map(c => (
          <option key={c.id} value={c.id}>{descrever(c)}{c.encerrada ? ' (encerrada)' : ''}</option>
        ))}
      </select>
    </Campo>
  )
}

/**
 * O resumo da carga num cartão só: o estoque grande, com a barra do que ainda
 * tem; embaixo, o dinheiro (a receber e comissão) e o custo que vale para a carga.
 */
export function PainelCarga({ estoque, total, unidade, casas = 0, saiu, aReceber, valorTotal, comissao, custo, encerrada, children }: {
  estoque: number
  total: number
  /** "produtosA", "pacotes" */
  unidade: string
  casas?: number
  /** Linha ao lado da barra: "12 saíram · 150,20 kg" */
  saiu: ReactNode
  aReceber: number
  valorTotal: number
  comissao: number
  /** "R$ 18,90 / kg", "R$ 73,45 por pacote" */
  custo: string
  encerrada: boolean
  /** Algo a mais embaixo (estoque por tipo) */
  children?: ReactNode
}) {
  const negativo = estoque < 0
  const fracao = total > 0 ? Math.min(1, Math.max(0, estoque / total)) : 0
  return (
    <section aria-label="Resumo da carga" className="bg-white border border-gray-200 rounded-xl overflow-hidden">
      <div className="px-4 pt-4 pb-3.5 sm:px-5">
        <div className="flex items-center justify-between gap-3">
          <p className="text-sm font-medium text-gray-600">Em estoque</p>
          <span className={`text-xs font-medium rounded-full px-2 py-0.5 border ${encerrada
            ? 'bg-gray-100 text-gray-600 border-gray-200'
            : 'bg-gema-50 text-gema-800 border-gema-200'}`}>
            {encerrada ? 'Encerrada' : 'Carga aberta'}
          </span>
        </div>
        <p className="mt-1 flex items-baseline gap-2 flex-wrap">
          <span className={`font-display text-4xl font-semibold leading-none tabular-nums ${negativo ? 'text-red-700' : 'text-gray-900'}`}>
            {num(estoque, casas)}
          </span>
          <span className="text-sm text-gray-500">de {num(total, 0)} {unidade}</span>
        </p>
        <div className="mt-3 h-2 rounded-full bg-gray-100 overflow-hidden" aria-hidden>
          <div className={`h-full rounded-full transition-[width] duration-500 ease-out ${negativo ? 'bg-red-600' : 'bg-gema-400'}`}
               style={{ width: `${negativo ? 100 : fracao * 100}%` }} />
        </div>
        <p className="mt-2 text-sm text-gray-600 tabular-nums">
          {negativo ? <span className="font-medium text-red-700">Estoque negativo: saiu mais do que entrou. </span> : null}
          {saiu}
        </p>
      </div>
      <dl className="grid grid-cols-2 sm:grid-cols-3 border-t border-gray-100 divide-x divide-gray-100">
        <div className="px-4 py-3 sm:px-5">
          <dt className="text-xs text-gray-500">A receber</dt>
          <dd className="mt-0.5 font-display text-lg font-semibold text-gray-900 tabular-nums leading-tight">{brl(aReceber)}</dd>
          <dd className="text-xs text-gray-500 tabular-nums">de {brl(valorTotal)}</dd>
        </div>
        <div className="px-4 py-3 sm:px-5">
          <dt className="text-xs text-gray-500">Comissão</dt>
          <dd className={`mt-0.5 font-display text-lg font-semibold tabular-nums leading-tight ${comissao < 0 ? 'text-red-700' : comissao > 0 ? 'text-emerald-700' : 'text-gray-900'}`}>{brl(comissao)}</dd>
        </div>
        <div className="col-span-2 sm:col-span-1 px-4 py-3 sm:px-5 border-t border-gray-100 sm:border-t-0 flex sm:block items-baseline justify-between gap-3">
          <dt className="text-xs text-gray-500">Custo da carga</dt>
          <dd className="sm:mt-0.5 text-sm font-semibold text-gray-800 tabular-nums">{custo}</dd>
        </div>
      </dl>
      {children}
    </section>
  )
}

// ── Saídas ────────────────────────────────────────────────────────────────

/** Título da lista de saídas com a contagem e o botão de lançar. */
export function CabecalhoSaidas({ quantidade, acao }: { quantidade: number; acao: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-3 pt-1">
      <h2 className="font-display text-lg font-semibold text-gray-900">
        Saídas <span className="font-sans text-sm font-normal text-gray-500 tabular-nums">{quantidade}</span>
      </h2>
      {acao}
    </div>
  )
}

/**
 * Uma saída: cliente, o que levou e o valor. Tocar abre o detalhe ali mesmo
 * (preço, custo, romaneio e apagar).
 */
export function CartaoSaida({ id, cliente, data, resumo, valor, nota, comissao, aberta, onAlternar, children }: {
  id: number
  cliente: string
  data: string
  /** O que levou, numa linha: "12 produtosA · 150,20 kg" */
  resumo: ReactNode
  valor: number
  /** Embaixo do valor (ex.: "com R$ 49,50 de frete") */
  nota?: ReactNode
  comissao: number
  aberta: boolean
  onAlternar: () => void
  children: ReactNode
}) {
  const idDetalhe = `saida-${id}-detalhe`
  return (
    <li className={`bg-white border rounded-xl overflow-hidden transition-colors ${aberta ? 'border-gray-300 shadow-sm' : 'border-gray-200'}`}>
      <button
        type="button"
        onClick={onAlternar}
        aria-expanded={aberta}
        aria-controls={idDetalhe}
        className="w-full text-left pl-4 pr-3 py-3 sm:pl-5 flex items-start gap-3 hover:bg-gray-50 transition-colors"
      >
        <div className="flex-1 min-w-0">
          <p className="flex items-baseline gap-2 min-w-0">
            <span className="font-semibold text-gray-900 truncate">{cliente}</span>
            <span className="text-sm text-gray-500 tabular-nums shrink-0">{diaCurto(data)}</span>
          </p>
          <div className="mt-0.5 text-sm text-gray-600 tabular-nums">{resumo}</div>
        </div>
        <div className="text-right shrink-0">
          <p className="font-semibold text-gray-900 tabular-nums">{brl(valor)}</p>
          {nota && <p className="text-xs text-gray-500 tabular-nums max-w-[8.5rem] sm:max-w-none">{nota}</p>}
          <p className={`text-sm tabular-nums ${comissao < 0 ? 'text-red-700' : 'text-emerald-700'}`}>
            <span className="sr-only">Comissão </span>{comissao < 0 ? `−${brl(-comissao)}` : `+${brl(comissao)}`}
          </p>
        </div>
        <ChevronDown size={18} aria-hidden
                     className={`mt-0.5 text-gray-500 shrink-0 transition-transform duration-200 ${aberta ? 'rotate-180' : ''}`} />
      </button>
      {aberta && (
        <div id={idDetalhe} className="border-t border-gray-100 bg-gray-50 px-4 py-4 sm:px-5 space-y-4 animate-entrada">
          {children}
        </div>
      )}
    </li>
  )
}

/** Fatos da saída em grade (rótulo pequeno em cima, número embaixo). */
export function Fatos({ itens }: { itens: [string, ReactNode][] }) {
  return (
    <dl className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-3">
      {itens.map(([r, v]) => (
        <div key={r} className="min-w-0">
          <dt className="text-xs text-gray-500">{r}</dt>
          <dd className="text-sm font-medium text-gray-900 tabular-nums">{v}</dd>
        </div>
      ))}
    </dl>
  )
}

/** Baixar o romaneio e apagar a saída. No celular um embaixo do outro. */
export function AcoesSaida({ numero, onExportar, onEditar, onApagar }: {
  numero?: number | null; onExportar: () => void; onEditar?: () => void; onApagar: () => void
}) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-center gap-2 pt-1">
      <Botao variante="secundario" icone={Download} onClick={onExportar} className="w-full sm:w-auto">
        {numero ? `Baixar romaneio ${numero}` : 'Baixar romaneio'}
      </Botao>
      {onEditar && (
        <Botao variante="secundario" icone={Pencil} onClick={onEditar} className="w-full sm:w-auto sm:ml-auto">
          Editar esta saída
        </Botao>
      )}
      <Botao variante="perigoLeve" icone={Trash2} onClick={onApagar} className={`w-full sm:w-auto ${onEditar ? "" : "sm:ml-auto"}`}>
        Apagar esta saída
      </Botao>
    </div>
  )
}

/**
 * Número do romaneio antes de baixar (no lugar do prompt do navegador). Vem
 * com o número que já tem ou o próximo; vazio sai sem número.
 */
export function ModalRomaneio({ cliente, sugestao, jaTem, onClose, baixar }: {
  cliente: string
  sugestao: number | null | undefined
  /** A saída já tem número gravado */
  jaTem: boolean
  onClose: () => void
  /** Baixa com o número (0 = sem número). Lança erro se falhar. */
  baixar: (numero: number) => Promise<void>
}) {
  const [numero, setNumero] = useState(sugestao ? String(sugestao) : '')
  const [baixando, setBaixando] = useState(false)
  const ir = async () => {
    setBaixando(true)
    try {
      await baixar(numero.trim() ? Math.round(lerNum(numero)) : 0)
    } finally {
      setBaixando(false)
    }
  }
  return (
    <Modal titulo="Baixar romaneio" descricao={cliente} onClose={onClose}
           rodape={
             <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
               <Botao variante="secundario" onClick={onClose}>Cancelar</Botao>
               <Botao variante="primario" icone={Download} onClick={ir} carregando={baixando} textoCarregando="Gerando...">
                 Baixar romaneio
               </Botao>
             </div>
           }>
      <Campo rotulo="Número do romaneio"
             dica={jaTem ? 'Já gravado nesta saída: baixar de novo sai igual.' : 'Sugestão: o próximo número. Deixe vazio para sair sem número.'}>
        <input value={numero} onChange={e => setNumero(e.target.value)} inputMode="numeric" data-foco-inicial
               onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); ir() } }}
               className={`${classeNumero} sm:max-w-[12rem] text-base`} />
      </Campo>
    </Modal>
  )
}

/** A conta da saída no rodapé do formulário: o valor e a comissão em destaque. */
export function ContaSaida({ linhas, valor, comissao, children }: {
  linhas: [ReactNode, ReactNode][]
  valor: number
  comissao: number
  children?: ReactNode
}) {
  return (
    <div className="rounded-xl border border-gray-200 bg-gray-50 px-4 py-3 text-sm" aria-live="polite">
      <dl className="space-y-1.5">
        {linhas.map(([r, v], i) => (
          <div key={i} className="flex items-baseline justify-between gap-3">
            <dt className="text-gray-600">{r}</dt>
            <dd className="text-gray-900 tabular-nums text-right">{v}</dd>
          </div>
        ))}
        <div className="flex items-baseline justify-between gap-3 border-t border-gray-200 pt-2 mt-2">
          <dt className="font-medium text-gray-800">Valor da saída</dt>
          <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums">{brl(valor)}</dd>
        </div>
        <div className="flex items-baseline justify-between gap-3">
          <dt className="text-gray-600">Comissão</dt>
          <dd className={`font-semibold tabular-nums ${comissao >= 0 ? 'text-emerald-700' : 'text-red-700'}`}>{brl(comissao)}</dd>
        </div>
      </dl>
      {children}
    </div>
  )
}
