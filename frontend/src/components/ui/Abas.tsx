import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import type { LucideIcon } from 'lucide-react'

// Abas (D-22): visual de pílulas, com a marcação que o leitor de tela entende
// (qual está marcada, setas do teclado trocam). O conteúdo vai em <PainelAba>.
//
//   const abas = useAbas('boletos')
//   <Abas id={abas} valor={aba} onChange={setAba} opcoes={[['pagar','A pagar',Icone], ...]} />
//   <PainelAba id={abas} valor={aba}>...</PainelAba>

// O 4º item marca a aba discreta (configuração pouco usada): vai no fim, depois
// de um traço, com texto mais apagado.
export type OpcaoAba<T extends string> = readonly [T, string, LucideIcon?, 'discreta'?]

export const useAbas = (nome: string) => `${nome}-${useId().replace(/:/g, '')}`

// Faixa que passa da largura (celular, M-01): rola de lado dentro dela mesma,
// esmaece a borda do lado em que há mais e traz a opção marcada para a vista.
const FOLGA = 24
function useFaixa(marcada: number) {
  const ref = useRef<HTMLDivElement>(null)
  const [mais, setMais] = useState({ esq: false, dir: false })
  const medir = useCallback(() => {
    const el = ref.current
    if (!el) return
    const esq = el.scrollLeft > 1
    const dir = el.scrollLeft + el.clientWidth < el.scrollWidth - 1
    setMais(m => (m.esq === esq && m.dir === dir ? m : { esq, dir }))
  }, [])
  useEffect(() => {
    const el = ref.current
    const alvo = el?.children[marcada] as HTMLElement | undefined
    if (el && alvo && el.scrollWidth > el.clientWidth) {
      const ini = alvo.offsetLeft - FOLGA
      const fim = alvo.offsetLeft + alvo.offsetWidth + FOLGA - el.clientWidth
      if (el.scrollLeft > ini) el.scrollLeft = Math.max(0, ini)
      else if (el.scrollLeft < fim) el.scrollLeft = fim
    }
    medir()
  }, [marcada, medir])
  useEffect(() => {
    const el = ref.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const ro = new ResizeObserver(medir)
    ro.observe(el)
    return () => ro.disconnect()
  }, [medir])
  const mascara = mais.esq || mais.dir
    ? `linear-gradient(to right, ${mais.esq ? 'transparent' : '#000'}, #000 ${FOLGA}px, #000 calc(100% - ${FOLGA}px), ${mais.dir ? 'transparent' : '#000'})`
    : undefined
  return { ref, onScroll: medir, style: { maskImage: mascara, WebkitMaskImage: mascara } }
}

export function Abas<T extends string>({ id, valor, opcoes, onChange, rotulo }: {
  id: string
  valor: T
  opcoes: readonly OpcaoAba<T>[]
  onChange: (v: T) => void
  /** Nome do grupo para o leitor de tela */
  rotulo?: string
}) {
  const botoes = useRef<(HTMLButtonElement | null)[]>([])
  const faixa = useFaixa(opcoes.findIndex(([v]) => v === valor))
  const tecla = (e: KeyboardEvent, i: number) => {
    const ir = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0
    if (!ir && e.key !== 'Home' && e.key !== 'End') return
    e.preventDefault()
    const n = opcoes.length
    const alvo = e.key === 'Home' ? 0 : e.key === 'End' ? n - 1 : (i + ir + n) % n
    onChange(opcoes[alvo][0])
    botoes.current[alvo]?.focus()
  }
  return (
    <div className="inline-flex max-w-full bg-gray-100 border border-gray-200 rounded-lg">
    <div role="tablist" aria-label={rotulo} {...faixa} className="relative flex min-w-0 overflow-x-auto p-1 gap-1">
      {opcoes.map(([v, texto, Icone, jeito], i) => {
        const marcada = v === valor
        const discreta = jeito === 'discreta'
        return (
          <button
            key={v}
            ref={el => { botoes.current[i] = el }}
            type="button"
            role="tab"
            id={`${id}-aba-${v}`}
            aria-selected={marcada}
            aria-controls={`${id}-painel`}
            tabIndex={marcada ? 0 : -1}
            onClick={() => onChange(v)}
            onKeyDown={e => tecla(e, i)}
            className={`inline-flex items-center gap-1.5 min-h-10 sm:min-h-8 text-sm rounded-md whitespace-nowrap transition-colors
              ${discreta ? 'relative ml-2 px-3 before:absolute before:-left-1.5 before:top-1.5 before:bottom-1.5 before:w-px before:bg-gray-300' : 'px-4 font-medium'}
              ${marcada ? 'bg-white text-gray-900 shadow-sm' : discreta ? 'text-gray-500 hover:text-gray-900' : 'text-gray-600 hover:text-gray-900'}`}
          >
            {Icone && <Icone size={15} aria-hidden />} {texto}
          </button>
        )
      })}
    </div>
    </div>
  )
}

export function PainelAba<T extends string>({ id, valor, children }: { id: string; valor: T; children: ReactNode }) {
  return (
    <div role="tabpanel" id={`${id}-painel`} aria-labelledby={`${id}-aba-${valor}`} className="space-y-5">
      {children}
    </div>
  )
}

/** Filtro de poucas opções (Em aberto / Pagos / Todos): botões que dizem qual está ligado. */
export function Segmentado<T extends string>({ valor, opcoes, onChange, rotulo }: {
  valor: T
  opcoes: readonly (readonly [T, string])[]
  onChange: (v: T) => void
  rotulo?: string
}) {
  const faixa = useFaixa(opcoes.findIndex(([v]) => v === valor))
  return (
    <div className="inline-flex max-w-full bg-gray-100 rounded-lg">
    <div role="group" aria-label={rotulo} {...faixa} className="relative flex min-w-0 overflow-x-auto p-0.5 gap-0.5">
      {opcoes.map(([v, r]) => (
        <button key={v} type="button" onClick={() => onChange(v)} aria-pressed={valor === v}
                className={`px-3 min-h-10 sm:min-h-8 text-sm rounded-md whitespace-nowrap transition-colors
                  ${valor === v ? 'bg-white text-gray-900 shadow-sm font-medium' : 'text-gray-600 hover:text-gray-900'}`}>
          {r}
        </button>
      ))}
    </div>
    </div>
  )
}
