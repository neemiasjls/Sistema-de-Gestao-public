import { useEffect, useRef } from 'react'
import { NavLink } from 'react-router-dom'
import { LogOut, X } from 'lucide-react'
import { useAuth } from '../../contexts/AuthContext'
import { secoesMenu } from './menu'

// Menu lateral (UI-16). No computador fica sempre à mostra; no celular é uma
// gaveta que entra pela esquerda (D-20): tem nome ("Menu"), prende o foco
// enquanto aberta, fecha com Esc, com o X, tocando fora e ao escolher uma tela,
// e devolve o foco ao botão que a abriu. Fechada, sai também do teclado
// (invisible), para o Tab não andar por itens que não aparecem. A visibilidade só
// anima ao fechar: ao abrir ela precisa valer na hora, senão o foco no X falha.

// A gema é o único ponto quente do sistema: marca onde você está.
// No celular o item é mais alto (dedo, 44 px); no computador, compacto.
const itemBase = 'flex items-center gap-3 px-3 min-h-11 lg:min-h-0 lg:py-[5px] lg:leading-5 rounded-lg text-[15px] transition-colors'
const itemAtivo = 'bg-gema-400 text-tinta-950 font-semibold'
const itemInativo = 'text-tinta-200 hover:bg-[rgb(255_255_255/0.06)] hover:text-white'

const FOCAVEIS = 'a[href], button:not([disabled])'

export const ID_MENU = 'menu-principal'

// Marca: uma gema vista de cima. Não existe logo da empresa, então nada de
// inventar um — só o ponto de cor que o sistema inteiro usa.
export function Marca() {
  return (
    <div className="flex items-center gap-3">
      <span className="relative w-8 h-8 rounded-full bg-[#fff] flex items-center justify-center shrink-0" aria-hidden>
        <span className="w-4 h-4 rounded-full bg-gema-400" />
      </span>
      <div className="leading-tight">
        <p className="font-display text-lg font-semibold tracking-tight text-white">TRX Transportes</p>
        <p className="text-xs text-tinta-300">Gestão operacional</p>
      </div>
    </div>
  )
}

export default function MenuLateral({ aberto, onFechar }: { aberto: boolean; onFechar: () => void }) {
  const { usuario, logout, pode } = useAuth()
  const caixa = useRef<HTMLElement>(null)
  const botaoFechar = useRef<HTMLButtonElement>(null)
  const fechar = useRef(onFechar)
  fechar.current = onFechar

  // Gaveta aberta (só existe no celular): foco no X, Tab preso, Esc fecha.
  useEffect(() => {
    if (!aberto) return
    const antes = document.activeElement as HTMLElement | null
    botaoFechar.current?.focus({ preventScroll: true })

    const tecla = (e: KeyboardEvent) => {
      // Uma janela (modal) aberta por cima responde primeiro
      if (document.querySelector('[aria-modal="true"][role="dialog"]:not(#' + ID_MENU + '), [role="alertdialog"]')) return
      if (e.key === 'Escape') { e.preventDefault(); fechar.current(); return }
      if (e.key !== 'Tab' || !caixa.current) return
      const itens = Array.from(caixa.current.querySelectorAll<HTMLElement>(FOCAVEIS)).filter(x => x.offsetParent !== null)
      if (!itens.length) return
      const primeiro = itens[0], ultimo = itens[itens.length - 1]
      const fora = !caixa.current.contains(document.activeElement)
      if (e.shiftKey && (document.activeElement === primeiro || fora)) { e.preventDefault(); ultimo.focus() }
      else if (!e.shiftKey && (document.activeElement === ultimo || fora)) { e.preventDefault(); primeiro.focus() }
    }
    document.addEventListener('keydown', tecla)

    // Virou computador com a gaveta aberta (girou o tablet): fecha, senão o
    // resto da tela ficaria travado para o teclado
    const largo = window.matchMedia('(min-width: 1024px)')
    const mudou = () => { if (largo.matches) fechar.current() }
    largo.addEventListener('change', mudou)

    return () => {
      document.removeEventListener('keydown', tecla)
      largo.removeEventListener('change', mudou)
      if (antes && document.contains(antes) && antes.offsetParent !== null) antes.focus({ preventScroll: true })
    }
  }, [aberto])

  return (
    <>
      {/* Fundo escurecido do celular: tocar fora fecha */}
      {aberto && (
        <div className="fixed inset-0 bg-tinta-950/60 z-30 lg:hidden animate-fundo" onClick={onFechar} aria-hidden />
      )}

      <aside
        ref={caixa}
        id={ID_MENU}
        aria-label="Menu"
        {...(aberto ? { role: 'dialog', 'aria-modal': true } : {})}
        className={`
          fixed inset-y-0 left-0 w-[19rem] max-w-[86vw] lg:w-64 bg-menu text-white z-30 border-r border-white/[0.06]
          flex flex-col shrink-0 duration-200 ease-out
          ${aberto ? 'translate-x-0 visible shadow-2xl transition-transform' : '-translate-x-full invisible transition-[transform,visibility]'}
          lg:relative lg:translate-x-0 lg:visible lg:shadow-none
        `}
      >
        <div className="pl-5 pr-2 pt-[max(1rem,env(safe-area-inset-top))] pb-3 lg:pb-2 flex items-center justify-between gap-2">
          <Marca />
          <button
            ref={botaoFechar}
            type="button"
            onClick={onFechar}
            className="lg:hidden w-11 h-11 rounded-lg flex items-center justify-center text-tinta-200 hover:text-white hover:bg-[rgb(255_255_255/0.08)]"
          >
            <X size={22} strokeWidth={1.75} aria-hidden />
            <span className="sr-only">Fechar menu</span>
          </button>
        </div>

        <nav aria-label="Telas" className="flex-1 px-3 pt-1 pb-4 lg:pb-2 space-y-5 lg:space-y-3 overflow-y-auto overscroll-contain scrollbar-thin">
          {secoesMenu.map((secao, i) => {
            const idTitulo = `menu-secao-${i}`
            // Só o que a pessoa pode abrir (USU-11); grupo vazio some inteiro
            const itens = secao.itens.filter(item => pode(item.area))
            if (!itens.length) return null
            return (
              <div key={secao.titulo ?? i} role={secao.titulo ? 'group' : undefined} aria-labelledby={secao.titulo ? idTitulo : undefined}>
                {secao.titulo && (
                  <p id={idTitulo} className="px-3 pb-1.5 lg:pb-1 font-display text-[13px] font-semibold tracking-wide text-tinta-300">
                    {secao.titulo}
                  </p>
                )}
                <ul className="space-y-0.5">
                  {itens.map(item => {
                    const Icone = item.icon
                    return (
                      <li key={item.to}>
                        <NavLink
                          to={item.to}
                          end={item.exact}
                          onClick={onFechar}
                          className={({ isActive }) => `${itemBase} ${isActive ? itemAtivo : itemInativo}`}
                        >
                          <Icone size={18} strokeWidth={1.75} className="shrink-0" aria-hidden />
                          {item.label}
                        </NavLink>
                      </li>
                    )
                  })}
                </ul>
              </div>
            )
          })}
        </nav>

        {/* Usuário logado + sair. Nome e foto levam ao perfil: senha, e-mail, tema e usuários */}
        <div className="px-3 pt-2 pb-[max(0.5rem,env(safe-area-inset-bottom))] border-t border-white/[0.07] flex items-center gap-1">
          {usuario && (
            <NavLink
              to="/perfil"
              onClick={onFechar}
              className={({ isActive }) => `flex-1 min-w-0 flex items-center gap-3 px-2 min-h-12 rounded-lg transition-colors
                ${isActive ? 'bg-[rgb(255_255_255/0.08)]' : 'hover:bg-[rgb(255_255_255/0.06)]'}`}
            >
              <span className="w-8 h-8 rounded-full bg-tinta-700 text-gema-300 flex items-center justify-center text-sm font-semibold shrink-0" aria-hidden>
                {usuario.nome.charAt(0).toUpperCase()}
              </span>
              <span className="min-w-0">
                <span className="block text-sm font-medium text-white truncate">{usuario.nome}</span>
                <span className="block text-xs text-tinta-300">Meu perfil</span>
              </span>
            </NavLink>
          )}
          <button
            type="button"
            onClick={logout}
            className="shrink-0 flex items-center gap-2 px-3 min-h-11 rounded-lg text-sm text-tinta-300 hover:bg-[rgb(255_255_255/0.06)] hover:text-white transition-colors"
          >
            <LogOut size={16} strokeWidth={1.75} aria-hidden />
            Sair
          </button>
        </div>
      </aside>
    </>
  )
}
