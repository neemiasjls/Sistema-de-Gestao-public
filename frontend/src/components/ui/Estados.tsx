import type { ReactNode } from 'react'
import { CloudOff, RotateCcw, type LucideIcon } from 'lucide-react'
import Botao from './Botao'

// Os três estados de toda lista (D-11, D-12): carregando, erro e vazio. Nunca
// mostrar "Nenhum..." quando a carga falhou: parece que não há nada, e há.

/** Linhas cinza no formato do que vai chegar. `forma`: linhas de lista ou cartões de resumo. */
export function Carregando({ linhas = 3, forma = 'lista', rotulo = 'Carregando' }: {
  linhas?: number
  forma?: 'lista' | 'cartoes'
  rotulo?: string
}) {
  return (
    <div role="status" aria-live="polite" className="animate-entrada">
      <span className="sr-only">{rotulo}...</span>
      {forma === 'cartoes' ? (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3" aria-hidden>
          {Array.from({ length: linhas }, (_, i) => (
            <div key={i} className="rounded-xl border border-gray-200 bg-white p-3 space-y-2.5">
              <div className="esqueleto h-3 w-1/2" />
              <div className="esqueleto h-6 w-3/4" />
            </div>
          ))}
        </div>
      ) : (
        <div className="rounded-xl border border-gray-200 bg-white divide-y divide-gray-100 overflow-hidden" aria-hidden>
          {Array.from({ length: linhas }, (_, i) => (
            <div key={i} className="flex items-center gap-3 px-4 py-3.5">
              <div className="flex-1 space-y-2">
                <div className="esqueleto h-3.5" style={{ width: `${62 - i * 9}%` }} />
                <div className="esqueleto h-3 w-1/3" />
              </div>
              <div className="esqueleto h-4 w-16 shrink-0" />
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/** Carga que falhou: diz o que não veio e oferece tentar de novo. */
export function ErroCarga({ mensagem = 'Não consegui carregar.', onTentar, detalhe }: {
  mensagem?: ReactNode
  onTentar?: () => void
  /** Uma linha a mais (ex.: "O resto da tela funciona.") */
  detalhe?: ReactNode
}) {
  return (
    <div role="alert" className="rounded-xl border border-red-200 bg-red-50 px-4 py-5 sm:px-5 flex flex-col sm:flex-row sm:items-center gap-3 animate-entrada">
      <CloudOff size={22} strokeWidth={1.75} className="text-red-700 shrink-0" aria-hidden />
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-red-900">{mensagem}</p>
        <p className="text-sm text-red-800 mt-0.5">
          {detalhe ?? 'Pode ser a internet ou o servidor ligando. Tente de novo em alguns segundos.'}
        </p>
      </div>
      {onTentar && (
        <Botao variante="secundario" icone={RotateCcw} onClick={onTentar} className="shrink-0">
          Tentar de novo
        </Botao>
      )}
    </div>
  )
}

/** Lista vazia de verdade: o que é, por que está vazia e como começar. */
export function Vazio({ icone: Icone, titulo, children, acao, compacto = false }: {
  icone?: LucideIcon
  titulo: ReactNode
  children?: ReactNode
  acao?: ReactNode
  /** Dentro de um cartão: sem borda tracejada, menos espaço */
  compacto?: boolean
}) {
  return (
    <div className={compacto
      ? 'px-4 py-8 text-center'
      : 'border-2 border-dashed border-gray-300 rounded-xl px-5 py-10 text-center'}>
      {Icone && <Icone size={30} strokeWidth={1.5} className="mx-auto mb-2.5 text-tinta-400" aria-hidden />}
      <p className="text-sm font-semibold text-gray-800">{titulo}</p>
      {children && <p className="text-sm text-gray-500 mt-1 max-w-sm mx-auto">{children}</p>}
      {acao && <div className="mt-4 flex justify-center">{acao}</div>}
    </div>
  )
}
