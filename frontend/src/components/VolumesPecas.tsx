import type { ReactNode } from 'react'
import { CircleAlert, CircleCheck, CircleDashed } from 'lucide-react'
import { TODOS_CRUS, caixasEProdutosC, type SobraAnterior, type Status } from '../services/volumesEmbaladosApi'

// Peças da tela de volumes embalados usadas em mais de uma página.

export type TipoVolume = 'branco' | 'medio' | 'vermelho'

/**
 * Situação do controle: bate, não bate ou incompleto. Ícone além da cor, para
 * ler de relance (e para quem não distingue verde de vermelho).
 */
export function Selo({ status, grande }: { status: Status; grande?: boolean }) {
  const cfg = {
    bate:       { t: 'Bate',       c: 'bg-emerald-50 text-emerald-800 border-emerald-200', I: CircleCheck },
    nao_bate:   { t: 'Não bate',   c: 'bg-red-50 text-red-800 border-red-200',             I: CircleAlert },
    incompleto: { t: 'Incompleto', c: 'bg-amber-50 text-amber-800 border-amber-200',       I: CircleDashed },
  }[status] ?? { t: status, c: 'bg-gray-100 text-gray-700 border-gray-200', I: CircleDashed }
  return (
    <span className={`inline-flex items-center gap-1 font-medium rounded-full border shrink-0 whitespace-nowrap
      ${grande ? 'text-sm px-2.5 py-1' : 'text-xs px-2 py-0.5'} ${cfg.c}`}>
      <cfg.I size={grande ? 15 : 13} strokeWidth={2.25} aria-hidden /> {cfg.t}
    </span>
  )
}

// Desenho do volume: branco, branco médio (menor) e vermelho
export function Volume({ tipo, pequeno }: { tipo: TipoVolume; pequeno?: boolean }) {
  const vermelho = tipo === 'vermelho'
  const escala = tipo === 'medio' ? 0.8 : 1
  const h = (pequeno ? 20 : 30) * escala
  return (
    <svg width={h * 0.78} height={h} viewBox="0 0 28 36" aria-hidden className="shrink-0">
      <path d="M14 1C7 1 1 12.5 1 21.5 1 29.5 6.8 35 14 35s13-5.5 13-13.5C27 12.5 21 1 14 1Z"
            fill={vermelho ? '#b8683f' : '#fbf6ea'}
            stroke={vermelho ? '#8f4a28' : '#b9ad95'} strokeWidth="1.5" />
      <ellipse cx="9.5" cy="12" rx="3" ry="4.5" fill="#fff" opacity={vermelho ? 0.22 : 0.9} />
    </svg>
  )
}

// Cru (caixa da fazenda) → como ele fala: "Vermelho fazenda"
const NOME_CRU: Record<string, { nome: string; tipo: TipoVolume }> = {
  ab:   { nome: 'Branco',       tipo: 'branco' },
  ab20: { nome: 'Branco de 20', tipo: 'branco' },
  bb:   { nome: 'Branco médio', tipo: 'medio' },
  av:   { nome: 'Vermelho',     tipo: 'vermelho' },
}

const diaMes = (iso: string | null) => (iso ? `${iso.slice(8, 10)}/${iso.slice(5, 7)}` : '')

/**
 * Sobra do controle anterior, que entra como entrada neste (VOLUME-05).
 * "Sobra do controle 328 · 23/09 — 4 produtosC vermelho fazenda".
 */
export function CartaoSobraAnterior({ sobra, manual, acao, carregando }: {
  sobra: SobraAnterior | null; manual: boolean; acao?: ReactNode; carregando?: boolean
}) {
  const itens = sobra ? TODOS_CRUS.filter(t => (sobra.valores[t.chave] ?? 0) !== 0) : []

  if (carregando) {
    return (
      <div role="status" className="flex items-center gap-3 px-1 min-h-[2.5rem]">
        <span className="sr-only">Buscando a sobra do controle anterior...</span>
        <div className="esqueleto h-3.5 w-56 max-w-full" aria-hidden />
      </div>
    )
  }

  if (itens.length === 0) {
    return (
      <div className="flex items-center justify-between gap-3 px-1 min-h-[2.5rem]">
        <p className="text-sm text-gray-500">
          {manual
            ? 'Sobra anterior digitada à mão: nada.'
            : sobra?.doControle
              ? `Sem sobra do controle anterior (${sobra.doControle}).`
              : 'Sem sobra do controle anterior.'}
        </p>
        {acao}
      </div>
    )
  }

  return (
    <div className="bg-gema-50 border border-gema-200 rounded-xl overflow-hidden">
      <div className="flex items-start justify-between gap-3 px-4 pt-3 pb-1">
        <div className="min-w-0">
          <h2 className="font-display text-base font-semibold text-gray-900">
            {manual
              ? 'Sobra anterior'
              : sobra?.doControle
                ? <>Sobra do controle {sobra.doControle}{sobra.doControleData && <span className="font-normal text-gray-500"> · {diaMes(sobra.doControleData)}</span>}</>
                : 'Sobra anterior'}
          </h2>
          <p className="text-sm text-gray-600">
            {manual ? 'Digitada à mão. ' : ''}Entra como entrada neste controle.
          </p>
        </div>
        {acao}
      </div>
      <ul className="px-4 pb-3 pt-1 space-y-1.5">
        {itens.map(t => {
          const info = NOME_CRU[t.chave]
          return (
            <li key={t.chave} className="flex items-center gap-3">
              <Volume tipo={info.tipo} pequeno />
              <span className="flex-1 min-w-0 text-sm text-gray-700">{info.nome} <span className="text-gray-500">fazenda</span></span>
              <span className="text-sm font-semibold tabular-nums text-gray-900 text-right">
                {caixasEProdutosC(sobra!.valores[t.chave])}
              </span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
