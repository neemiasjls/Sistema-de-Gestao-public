import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react'
import type { LucideIcon } from 'lucide-react'

// Abas (D-22): visual de pílulas, com a marcação que o leitor de tela entende
// (qual está marcada, setas do teclado trocam). O conteúdo vai em <PainelAba>.
//
//   const abas = useAbas('boletos')
//   <Abas id={abas} valor={aba} onChange={setAba} opcoes={[['pagar','A pagar',Icone], ...]} />
//   <PainelAba id={abas} valor={aba}>...</PainelAba>

export type OpcaoAba<T extends string> = readonly [T, string, LucideIcon?]

export const useAbas = (nome: string) => `${nome}-${useId().replace(/:/g, '')}`

export function Abas<T extends string>({ id, valor, opcoes, onChange, rotulo }: {
  id: string
  valor: T
  opcoes: readonly OpcaoAba<T>[]
  onChange: (v: T) => void
  /** Nome do grupo para o leitor de tela */
  rotulo?: string
}) {
  const botoes = useRef<(HTMLButtonElement | null)[]>([])
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
    <div role="tablist" aria-label={rotulo} className="inline-flex max-w-full overflow-x-auto bg-gray-100 border border-gray-200 rounded-lg p-1 gap-1">
      {opcoes.map(([v, texto, Icone], i) => {
        const marcada = v === valor
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
            className={`inline-flex items-center gap-1.5 px-4 min-h-10 sm:min-h-8 text-sm font-medium rounded-md whitespace-nowrap transition-colors
              ${marcada ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-600 hover:text-gray-900'}`}
          >
            {Icone && <Icone size={15} aria-hidden />} {texto}
          </button>
        )
      })}
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
  return (
    <div role="group" aria-label={rotulo} className="inline-flex max-w-full overflow-x-auto bg-gray-100 rounded-lg p-0.5 gap-0.5">
      {opcoes.map(([v, r]) => (
        <button key={v} type="button" onClick={() => onChange(v)} aria-pressed={valor === v}
                className={`px-3 min-h-10 sm:min-h-8 text-sm rounded-md whitespace-nowrap transition-colors
                  ${valor === v ? 'bg-white text-gray-900 shadow-sm font-medium' : 'text-gray-600 hover:text-gray-900'}`}>
          {r}
        </button>
      ))}
    </div>
  )
}
