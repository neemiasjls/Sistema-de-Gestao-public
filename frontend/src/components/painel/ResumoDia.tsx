import type { MouseEvent, ReactNode } from 'react'
import { Banknote, HandCoins, ListChecks, TriangleAlert, type LucideIcon } from 'lucide-react'
import { formatBRL } from '../../services/api'
import type { GrupoBoletos } from '../../services/painelApi'
import type { Alerta } from './PainelPecas'

// Números do dia no topo do Início (UI-18): quatro fichas que resumem o que vem
// embaixo, na mesma ordem de urgência — o que pede atenção, as tarefas de hoje e
// os boletos dos próximos 7 dias. Nenhuma conta nova: cada número é o mesmo que o
// bloco correspondente mostra. Tocar numa ficha leva ao bloco (útil no celular,
// onde ele fica lá embaixo). Cor só no que pede atenção (vermelho/âmbar).

type Tom = 'vermelho' | 'ambar' | 'neutro'

const TOM: Record<Tom, { faixa: string; icone: string; nota: string }> = {
  vermelho: { faixa: 'before:bg-red-500', icone: 'text-red-700', nota: 'text-red-700 font-semibold' },
  ambar:    { faixa: 'before:bg-amber-400', icone: 'text-amber-700', nota: 'text-amber-800 font-medium' },
  neutro:   { faixa: 'before:bg-transparent', icone: 'text-tinta-500', nota: 'text-gray-500' },
}

const plural = (n: number, um: string, varios: string) => `${n} ${n === 1 ? um : varios}`

/** Desce até o bloco e põe o foco no título dele (leitor de tela e teclado seguem junto) */
function irPara(e: MouseEvent<HTMLAnchorElement>, alvo: string) {
  const titulo = document.getElementById(alvo)
  if (!titulo) return
  e.preventDefault()
  const bloco = titulo.closest('section') ?? titulo
  const reduzido = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
  bloco.scrollIntoView({ behavior: reduzido ? 'auto' : 'smooth', block: 'start' })
  if (!titulo.hasAttribute('tabindex')) titulo.setAttribute('tabindex', '-1')
  titulo.focus({ preventScroll: true })
}

function Ficha({ alvo, rotulo, icone: Icone, valor, nota, tom, atraso }: {
  alvo: string; rotulo: string; icone: LucideIcon; valor: ReactNode; nota: ReactNode; tom: Tom; atraso: number
}) {
  const t = TOM[tom]
  return (
    <li className="min-w-0 animate-entrada" style={{ animationDelay: `${atraso}ms` }}>
      <a href={`#${alvo}`} onClick={e => irPara(e, alvo)}
         className={`group relative flex h-full flex-col gap-1 overflow-hidden rounded-xl border border-gray-200 bg-white px-4 py-3 sm:px-5 sm:py-4
                     transition-colors hover:border-gray-300 hover:bg-gray-50
                     before:absolute before:inset-y-0 before:left-0 before:w-1 ${t.faixa}`}>
        <span className="flex items-start gap-2 text-sm text-gray-600 leading-snug">
          <Icone size={16} strokeWidth={1.9} className={`shrink-0 mt-0.5 ${t.icone}`} aria-hidden />
          <span className="min-w-0">{rotulo}</span>
        </span>
        {/* Valor nunca quebra no meio (R$ 1.234.567,89 cabe em meia tela de 360 px no tamanho menor) */}
        <span className="font-display text-lg min-[400px]:text-xl sm:text-2xl font-semibold text-gray-900 leading-tight tabular-nums whitespace-nowrap">{valor}</span>
        <span className={`text-sm leading-snug ${t.nota}`}>{nota}</span>
      </a>
    </li>
  )
}

function fichaBoletos(g: GrupoBoletos) {
  const { vencidos: v, aVencer: a } = g
  // Total com boleto sem valor avisa (UI-04): o número sozinho enganaria
  const semValor = (n: number) => (n > 0 ? ` · ${n} sem valor` : '')
  const proximos = a.quantidade > 0 ? `${plural(a.quantidade, 'boleto', 'boletos')}${semValor(a.semValor)}` : 'Nada vencendo'
  return {
    valor: formatBRL(a.total),
    nota: v.quantidade > 0 ? (
      <>
        <span className="block">{plural(v.quantidade, 'vencido', 'vencidos')} · {formatBRL(v.total)}{semValor(v.semValor)}</span>
        <span className="block text-gray-500 font-normal">{proximos}</span>
      </>
    ) : proximos,
    tom: (v.quantidade > 0 ? 'vermelho' : g.hojeQuantidade > 0 ? 'ambar' : 'neutro') as Tom,
  }
}

export default function ResumoDia({ alertas, prontas, tarefas, faltam, pagar, receber }: {
  alertas: Alerta[]
  prontas: number
  tarefas: number
  faltam: number
  pagar: GrupoBoletos
  receber: GrupoBoletos
}) {
  const grave = alertas.some(a => a.tom === 'vermelho')
  const p = fichaBoletos(pagar)
  const r = fichaBoletos(receber)
  return (
    <ul aria-label="Números do dia" className="grid grid-cols-2 lg:grid-cols-4 gap-3 lg:gap-4">
      <Ficha alvo="painel-atencao" rotulo="Pede atenção" icone={TriangleAlert} atraso={0}
             valor={alertas.length === 0 ? 'Nada' : alertas.length}
             nota={alertas.length === 0 ? 'Tudo em dia' : alertas.length === 1 ? alertas[0].titulo : `${alertas[0].titulo} e mais ${alertas.length - 1}`}
             tom={alertas.length === 0 ? 'neutro' : grave ? 'vermelho' : 'ambar'} />
      <Ficha alvo="painel-rotina" rotulo="Tarefas de hoje" icone={ListChecks} atraso={40}
             valor={tarefas === 0 ? 'Nenhuma' : `${prontas} de ${tarefas}`}
             nota={tarefas === 0 ? 'Nada marcado para hoje' : faltam > 0 ? (faltam === 1 ? '1 falta' : `${faltam} faltam`) : prontas === tarefas ? 'Tudo pronto' : 'Nada atrasado'}
             tom={faltam > 0 ? 'ambar' : 'neutro'} />
      <Ficha alvo="painel-boletos" rotulo="A pagar em 7 dias" icone={Banknote} atraso={80} {...p} />
      <Ficha alvo="painel-boletos" rotulo="A receber em 7 dias" icone={HandCoins} atraso={120} {...r} />
    </ul>
  )
}
