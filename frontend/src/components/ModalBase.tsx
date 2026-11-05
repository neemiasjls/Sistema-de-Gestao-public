import { createContext, useCallback, useContext, useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import Botao, { BotaoIcone } from './ui/Botao'
import { useConfirmar } from './ui/Confirmar'

// Janela (modal) do sistema. No celular sobe de baixo e ocupa a largura toda; no
// computador fica centralizada. Fecha com Esc e com o X (tocar fora só se pedir); prende o
// foco dentro dela enquanto aberta e devolve o foco ao botão que a abriu.
//
// Vai para o <body> por portal: assim nenhum pai com transform (a animação de
// entrada da tela, UI-03) muda onde ela abre, e a rolagem do fundo não anda.
//
// Janelas empilhadas (a confirmação por cima de outra janela): só a de cima
// responde ao Esc e prende o foco.
//
// Dados digitados (D-17, UI-17): com `sujo` (a tela diz) ou `confirmarAoFechar` (a
// janela percebe sozinha qualquer digitação dentro dela), fechar pelo X, Esc, toque
// fora ou Cancelar do BotoesModal pergunta antes "Sair sem salvar?". Fechar depois de
// salvar não pergunta: a tela fecha pelo próprio estado, sem passar por aqui.
// Campo que não conta como dado (busca, filtro) leva data-nao-suja.

/** Fechar "pedindo licença": pergunta antes se houver dados digitados */
const ContextoFechar = createContext<((depois?: () => void) => void) | null>(null)
export const usePedirFechar = () => useContext(ContextoFechar)

const pilha: number[] = []
let proximo = 1

const FOCAVEIS = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

export type LarguraModal = 'md' | 'lg' | 'xl' | '2xl'
const LARGURAS: Record<LarguraModal, string> = {
  md: 'sm:max-w-lg', lg: 'sm:max-w-2xl', xl: 'sm:max-w-4xl', '2xl': 'sm:max-w-6xl',
}

export function Modal({
  titulo, children, onClose, largura = 'md', descricao, alerta = false, rodape, fecharFora = false, acoes, fundoCinza = false, classeCorpo,
  sujo = false, confirmarAoFechar = false,
}: {
  titulo: ReactNode
  children: ReactNode
  onClose: () => void
  largura?: LarguraModal
  /** Uma linha abaixo do título */
  descricao?: ReactNode
  /** Confirmação: o leitor de tela anuncia como alerta */
  alerta?: boolean
  /** Botões fixos embaixo (não rolam com o conteúdo) */
  rodape?: ReactNode
  /** Tocar fora fecha. Desligado por padrão: num formulário, um toque fora perdia o que foi digitado (D-17). */
  fecharFora?: boolean
  /** Botões no topo, ao lado do X (ex.: Baixar) */
  acoes?: ReactNode
  /** Corpo em cinza claro, para cartões brancos dentro (visualizações) */
  fundoCinza?: boolean
  /** Troca o espaçamento do corpo (padrão: p-4 space-y-3) */
  classeCorpo?: string
  /** Há dados não salvos: fechar pergunta antes (D-17) */
  sujo?: boolean
  /** Pergunta antes de fechar se algo foi digitado dentro da janela (detecta sozinha) */
  confirmarAoFechar?: boolean
}) {
  const idTitulo = useId()
  const idDescricao = useId()
  const caixa = useRef<HTMLDivElement>(null)
  const fechar = useRef(onClose)
  fechar.current = onClose
  const confirmar = useConfirmar()

  // Sujo = a tela disse, ou (com confirmarAoFechar) alguém digitou aqui dentro
  const digitou = useRef(false)
  const sujoAgora = useRef(sujo)
  sujoAgora.current = sujo || (confirmarAoFechar && digitou.current)
  const perguntando = useRef(false)

  const pedirFechar = useCallback(async (depois?: () => void) => {
    const sair = depois ?? fechar.current
    if (!sujoAgora.current) { sair(); return }
    if (perguntando.current) return
    perguntando.current = true
    const ok = await confirmar({
      titulo: 'Sair sem salvar?',
      texto: 'O que você digitou nesta janela vai ser perdido.',
      confirmar: 'Sair sem salvar',
      cancelar: 'Continuar editando',
    })
    perguntando.current = false
    if (ok) sair()
  }, [confirmar])
  const pedir = useRef(pedirFechar)
  pedir.current = pedirFechar

  // Detecta a digitação (só com confirmarAoFechar) e segura o recarregar/fechar a aba
  useEffect(() => {
    const el = caixa.current
    if (!el) return
    const marcou = (e: Event) => {
      if (!confirmarAoFechar) return
      const alvo = e.target as HTMLElement | null
      if (alvo?.closest('[data-nao-suja]')) return
      digitou.current = true
      sujoAgora.current = true
    }
    const saindo = (e: BeforeUnloadEvent) => { if (sujoAgora.current) { e.preventDefault(); e.returnValue = '' } }
    el.addEventListener('input', marcou)
    el.addEventListener('change', marcou)
    window.addEventListener('beforeunload', saindo)
    return () => {
      el.removeEventListener('input', marcou)
      el.removeEventListener('change', marcou)
      window.removeEventListener('beforeunload', saindo)
    }
  }, [confirmarAoFechar])

  useEffect(() => {
    const eu = proximo++
    pilha.push(eu)
    const antes = document.activeElement as HTMLElement | null
    const noTopo = () => pilha[pilha.length - 1] === eu

    // Foco inicial na própria janela (o leitor de tela lê o título). Não vai
    // direto ao primeiro campo: no celular isso abria o teclado por cima do
    // formulário. Quem quiser um campo já focado marca com data-foco-inicial.
    const el = caixa.current
    ;(el?.querySelector<HTMLElement>('[data-foco-inicial]') ?? el)?.focus({ preventScroll: true })

    const tecla = (e: KeyboardEvent) => {
      if (!noTopo() || !el) return
      if (e.key === 'Escape') { e.stopPropagation(); pedir.current(); return }
      if (e.key !== 'Tab') return
      const itens = Array.from(el.querySelectorAll<HTMLElement>(FOCAVEIS)).filter(x => x.offsetParent !== null)
      if (!itens.length) { e.preventDefault(); return }
      const primeiro = itens[0], ultimo = itens[itens.length - 1]
      if (e.shiftKey && (document.activeElement === primeiro || document.activeElement === el)) { e.preventDefault(); ultimo.focus() }
      else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primeiro.focus() }
    }
    document.addEventListener('keydown', tecla)

    const overflowAntes = document.body.style.overflow
    document.body.style.overflow = 'hidden'

    return () => {
      document.removeEventListener('keydown', tecla)
      pilha.splice(pilha.indexOf(eu), 1)
      if (!pilha.length) document.body.style.overflow = overflowAntes
      // Devolve o foco a quem abriu, se ele ainda estiver na tela
      if (antes && document.contains(antes)) antes.focus({ preventScroll: true })
    }
  }, [])

  return createPortal(
    <div
      className="fixed inset-0 bg-tinta-950/50 z-40 flex items-end sm:items-center justify-center p-0 sm:p-4 animate-fundo"
      onMouseDown={e => { if (fecharFora && e.target === e.currentTarget) pedirFechar() }}
    >
      <div
        ref={caixa}
        role={alerta ? 'alertdialog' : 'dialog'}
        aria-modal="true"
        aria-labelledby={idTitulo}
        aria-describedby={descricao ? idDescricao : undefined}
        tabIndex={-1}
        className={`${fundoCinza ? 'bg-gray-50' : 'bg-white'} w-full ${LARGURAS[largura]} sm:rounded-xl rounded-t-2xl max-h-[92dvh] flex flex-col shadow-xl animate-janela outline-none overflow-hidden`}
      >
        <div className="bg-white pl-4 pr-2 py-2 border-b border-gray-200 flex items-center justify-between gap-2 flex-shrink-0">
          <div className="min-w-0 py-1">
            <h2 id={idTitulo} className="font-display text-lg font-semibold text-gray-900 leading-tight">{titulo}</h2>
            {descricao && <p id={idDescricao} className="text-sm text-gray-500 mt-0.5 break-words">{descricao}</p>}
          </div>
          <div className="flex items-center gap-1.5 shrink-0">
            {acoes}
            <BotaoIcone icone={X} rotulo="Fechar" onClick={() => pedirFechar()} />
          </div>
        </div>
        <ContextoFechar.Provider value={pedirFechar}>
        <div className={`flex-1 overflow-y-auto overscroll-contain ${classeCorpo ?? 'p-4 space-y-3'} ${rodape ? '' : 'pb-seguro'}`}>{children}</div>
        {rodape && (
          <div className="px-4 pt-3 pb-seguro border-t border-gray-200 flex-shrink-0">{rodape}</div>
        )}
        </ContextoFechar.Provider>
      </div>
    </div>,
    document.body,
  )
}

// O rótulo e a mensagem de erro do campo agora moram em ui/Campo (D-04).
// Ficam exportados daqui também, para as telas que já importavam deste arquivo.
export { default as Campo } from './ui/Campo'

export function BotoesModal({ salvando, onSalvar, onClose, rotulo = 'Salvar' }: {
  salvando: boolean; onSalvar: () => void; onClose: () => void; rotulo?: string
}) {
  // Dentro de uma janela, o Cancelar também pergunta se havia algo digitado (D-17)
  const pedirFechar = usePedirFechar()
  return (
    <div className="flex gap-2 pt-1">
      <Botao variante="primario" onClick={onSalvar} carregando={salvando} textoCarregando="Salvando..." className="flex-1">
        {rotulo}
      </Botao>
      <Botao variante="fantasma" onClick={() => (pedirFechar ? pedirFechar(onClose) : onClose())}>Cancelar</Botao>
    </div>
  )
}

export function Card({ titulo, children, destaque }: {
  titulo: string; children: ReactNode; destaque?: boolean
}) {
  return (
    <div className={`rounded-xl border p-3 ${destaque ? 'bg-gema-50 border-gema-200' : 'bg-white border-gray-200'}`}>
      <p className="text-xs text-gray-500">{titulo}</p>
      <p className="font-display text-2xl font-semibold text-gray-900 mt-0.5 leading-tight tabular-nums">{children}</p>
    </div>
  )
}

export function Info({ rotulo, valor }: { rotulo: string; valor: string }) {
  return (
    <div>
      <p className="text-gray-500">{rotulo}</p>
      <p className="font-medium text-gray-800">{valor}</p>
    </div>
  )
}
