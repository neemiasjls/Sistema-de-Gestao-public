import { forwardRef, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { LoaderCircle, type LucideIcon } from 'lucide-react'

// Botão do sistema (D-09). Cinco jeitos e dois tamanhos, iguais em toda tela:
//   primario   tinta: a ação principal da tela (uma por área)
//   secundario contorno: ações de apoio (Clientes, Importar planilha, Cancelar)
//   perigo     vermelho escuro: o "Apagar" da confirmação
//   perigoLeve contorno vermelho: o botão que abre a confirmação (Excluir acerto)
//   fantasma   só texto: ações discretas dentro de cartões e listas
// No celular a altura é 44 px (dedo); no computador, 40 (md) ou 32 (sm).
// Verde não é cor de botão: verde é do Caio (EMP-02), azul do Bento e laranja
// do Davi — botão nessas cores parece "coisa do dono".

export type VarianteBotao = 'primario' | 'secundario' | 'perigo' | 'perigoLeve' | 'fantasma'
export type TamanhoBotao = 'md' | 'sm'

const VARIANTES: Record<VarianteBotao, string> = {
  primario:   'bg-tinta-700 text-white hover:bg-tinta-800 shadow-sm',
  secundario: 'bg-white text-gray-800 border border-gray-300 hover:bg-gray-50 hover:border-gray-400',
  perigo:     'bg-red-700 text-white hover:bg-red-800 shadow-sm',
  perigoLeve: 'bg-white text-red-700 border border-red-200 hover:bg-red-50 hover:border-red-300',
  fantasma:   'text-gray-700 hover:bg-gray-100 hover:text-gray-900',
}

const TAMANHOS: Record<TamanhoBotao, string> = {
  md: 'min-h-11 sm:min-h-10 px-4 text-sm gap-2',
  sm: 'min-h-10 sm:min-h-8 px-3 text-sm gap-1.5',
}

export function classesBotao(variante: VarianteBotao = 'secundario', tamanho: TamanhoBotao = 'md', bloco = false) {
  return [
    'inline-flex items-center justify-center rounded-lg font-semibold transition-colors select-none',
    'disabled:cursor-not-allowed disabled:opacity-60',
    VARIANTES[variante], TAMANHOS[tamanho], bloco ? 'w-full' : '',
  ].join(' ')
}

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  variante?: VarianteBotao
  tamanho?: TamanhoBotao
  /** Ícone lucide à esquerda do texto */
  icone?: LucideIcon
  /** Mostra o giro no lugar do ícone e trava o botão */
  carregando?: boolean
  /** Texto enquanto carrega ("Salvando..."); sem ele fica o texto de sempre */
  textoCarregando?: ReactNode
  /** Ocupa a largura toda (útil no celular: `bloco` + `sm:w-auto` no className) */
  bloco?: boolean
}

const Botao = forwardRef<HTMLButtonElement, Props>(function Botao(
  { variante = 'secundario', tamanho = 'md', icone: Icone, carregando = false, textoCarregando, bloco = false,
    className = '', children, disabled, type = 'button', ...resto },
  ref,
) {
  const tam = tamanho === 'sm' ? 16 : 18
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || carregando}
      aria-busy={carregando || undefined}
      className={`${classesBotao(variante, tamanho, bloco)} ${className}`}
      {...resto}
    >
      {carregando
        ? <LoaderCircle size={tam} className="animate-spin shrink-0" aria-hidden />
        : Icone && <Icone size={tam} strokeWidth={2} className="shrink-0" aria-hidden />}
      {carregando && textoCarregando ? textoCarregando : children}
    </button>
  )
})

export default Botao

/** Botão só de ícone (fechar, apagar linha). O nome vai para o leitor de tela e para a dica. */
export const BotaoIcone = forwardRef<HTMLButtonElement, ButtonHTMLAttributes<HTMLButtonElement> & {
  icone: LucideIcon
  rotulo: string
  perigo?: boolean
}>(function BotaoIcone({ icone: Icone, rotulo, perigo = false, className = '', type = 'button', ...resto }, ref) {
  return (
    <button
      ref={ref}
      type={type}
      aria-label={rotulo}
      title={rotulo}
      className={`inline-flex items-center justify-center w-11 h-11 sm:w-9 sm:h-9 rounded-lg shrink-0 transition-colors
        disabled:cursor-not-allowed disabled:opacity-60
        ${perigo ? 'text-gray-500 hover:text-red-700 hover:bg-red-50' : 'text-gray-500 hover:text-gray-900 hover:bg-gray-100'} ${className}`}
      {...resto}
    >
      <Icone size={18} strokeWidth={2} aria-hidden />
    </button>
  )
})
