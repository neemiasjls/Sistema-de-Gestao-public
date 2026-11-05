import { useState, type ReactNode } from 'react'
import { ChevronDown } from 'lucide-react'

// Topo de cada tela (D-03). O nome da tela fica só na barra de cima (o mesmo do
// menu); aqui vêm a explicação curta e os botões da tela.
//
// No celular a explicação começa fechada atrás de "Como funciona": antes ela
// empurrava o conteúdo um quarto de tela para baixo em toda visita. No
// computador fica aberta, numa linha de leitura confortável.
//
// `titulo` só para sub-telas que têm nome próprio ("Controle 330", "Setembro de
// 2026"): aparece aqui, porque a barra de cima mostra o nome da seção.

export default function CabecalhoTela({ titulo, subtitulo, descricao, acoes, children }: {
  titulo?: ReactNode
  /** Ao lado do título, menor (data, situação) */
  subtitulo?: ReactNode
  descricao?: ReactNode
  /** Botões da tela; no celular descem para baixo da explicação */
  acoes?: ReactNode
  /** Algo a mais embaixo (filtros, voltar) */
  children?: ReactNode
}) {
  const [aberta, setAberta] = useState(false)
  return (
    <div className="space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-start gap-3 sm:gap-6">
        <div className="flex-1 min-w-0">
          {titulo && (
            <h2 className="font-display text-xl sm:text-2xl font-semibold text-gray-900 leading-tight flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="min-w-0">{titulo}</span>
              {subtitulo && <span className="font-sans text-base font-normal text-gray-500">{subtitulo}</span>}
            </h2>
          )}
          {descricao && (
            <>
              <button
                type="button"
                onClick={() => setAberta(a => !a)}
                aria-expanded={aberta}
                className={`sm:hidden inline-flex items-center gap-1 -ml-1 px-1 min-h-11 text-sm font-medium text-gray-600 ${titulo ? 'mt-0.5' : '-mt-2'}`}
              >
                Como funciona
                <ChevronDown size={16} className={`transition-transform duration-200 ${aberta ? 'rotate-180' : ''}`} aria-hidden />
              </button>
              <div className={`${aberta ? 'block animate-entrada' : 'hidden'} sm:block text-sm text-gray-600 leading-relaxed max-w-[68ch] ${titulo ? 'sm:mt-1' : ''} [&_b]:font-semibold [&_b]:text-gray-800`}>
                {descricao}
              </div>
            </>
          )}
        </div>
        {/* No celular os botões dividem a linha por igual (o último, o principal, ocupa a
            linha dele quando não cabe); no computador ficam do tamanho do texto. */}
        {acoes && <div className="flex flex-wrap gap-2 sm:shrink-0 sm:justify-end [&>*]:flex-1 [&>*]:basis-auto sm:[&>*]:flex-none">{acoes}</div>}
      </div>
      {children}
    </div>
  )
}
