import type { ReactNode } from 'react'
import toast from 'react-hot-toast'
import { CircleCheck, CircleX, Info, TriangleAlert, X, type LucideIcon } from 'lucide-react'

// Faixa de aviso que fica na tela até a pessoa fechar (D-07, UI-04). É o lugar de
// "nota sem peso", "boleto recusado", "mês substituído": o que não pode passar
// batido. O toast fica só para "feito" e "erro de rede".
//
// Sem emoji: o ícone é do lucide. Sem faixa colorida na lateral: o tom vem do
// fundo e da borda inteira.

export type TipoAviso = 'info' | 'atencao' | 'erro' | 'sucesso'

const TONS: Record<TipoAviso, { caixa: string; icone: string; Icone: LucideIcon }> = {
  info:    { caixa: 'bg-tinta-50 border-tinta-200 text-tinta-900',     icone: 'text-tinta-600',   Icone: Info },
  atencao: { caixa: 'bg-amber-50 border-amber-200 text-amber-900',     icone: 'text-amber-700',   Icone: TriangleAlert },
  erro:    { caixa: 'bg-red-50 border-red-200 text-red-900',           icone: 'text-red-700',     Icone: CircleX },
  sucesso: { caixa: 'bg-emerald-50 border-emerald-200 text-emerald-900', icone: 'text-emerald-700', Icone: CircleCheck },
}

export default function Aviso({ tipo = 'info', titulo, children, itens, onFechar, acao, className = '' }: {
  tipo?: TipoAviso
  titulo?: ReactNode
  children?: ReactNode
  /** Lista de avisos (um por linha). Mais de 4: mostra 4 e "ver mais". */
  itens?: ReactNode[]
  /** Com isto aparece o X; sem isto o aviso fica até a tela mudar */
  onFechar?: () => void
  /** Botão ou link no fim (ex.: "Tentar de novo") */
  acao?: ReactNode
  className?: string
}) {
  const t = TONS[tipo]
  const lista = itens?.filter(Boolean) ?? []
  const visiveis = lista.slice(0, 4)
  const resto = lista.slice(4)
  return (
    <div
      role={tipo === 'erro' ? 'alert' : 'status'}
      className={`flex gap-3 rounded-xl border px-3.5 py-3 text-sm animate-entrada ${t.caixa} ${className}`}
    >
      <t.Icone size={18} strokeWidth={2} className={`shrink-0 mt-0.5 ${t.icone}`} aria-hidden />
      <div className="flex-1 min-w-0 space-y-1">
        {titulo && <p className="font-semibold leading-snug">{titulo}</p>}
        {children && <div className="leading-snug [&_b]:font-semibold">{children}</div>}
        {visiveis.length > 0 && (
          <ul className="space-y-1 leading-snug">
            {visiveis.map((a, i) => <li key={i} className="break-words">{a}</li>)}
          </ul>
        )}
        {resto.length > 0 && (
          <details className="group">
            <summary className="cursor-pointer font-medium underline underline-offset-2 list-none">
              <span className="group-open:hidden">Ver mais {resto.length}</span>
              <span className="hidden group-open:inline">Ver menos</span>
            </summary>
            <ul className="space-y-1 leading-snug mt-1">
              {resto.map((a, i) => <li key={i} className="break-words">{a}</li>)}
            </ul>
          </details>
        )}
        {acao && <div className="pt-1.5">{acao}</div>}
      </div>
      {onFechar && (
        <button
          type="button"
          onClick={onFechar}
          aria-label="Fechar aviso"
          title="Fechar aviso"
          className="-mr-1.5 -mt-1.5 w-10 h-10 sm:w-8 sm:h-8 shrink-0 inline-flex items-center justify-center rounded-lg opacity-70 hover:opacity-100"
        >
          <X size={16} aria-hidden />
        </button>
      )}
    </div>
  )
}

/**
 * Toast de "terminou, mas olhe o aviso" (sem emoji). O aviso em si tem de estar
 * na tela, num <Aviso>: o toast só chama a atenção para ele.
 */
export function toastConfira(mensagem = 'Pronto. Confira o aviso na tela.') {
  toast(mensagem, {
    icon: <TriangleAlert size={18} strokeWidth={2} className="text-amber-700 shrink-0" aria-hidden />,
    duration: 6000,
  })
}
