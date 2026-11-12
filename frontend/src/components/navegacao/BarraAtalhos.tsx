import { useEffect, useState, type ReactNode } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { Menu, type LucideIcon } from 'lucide-react'
import { atalhos, casa } from './menu'
import { ID_MENU } from './MenuLateral'
import { useAuth } from '../../contexts/AuthContext'

// Barra de atalhos embaixo, só no celular (F-04, UI-16). Fica no fluxo da página
// (não é fixed): a área de conteúdo encolhe para caber, então nada da tela fica
// escondido atrás dela. Cada destino ocupa um quinto da largura e 56 px de altura;
// o ativo ganha a pílula de gema, como no menu. "Menu" abre a gaveta com o resto e
// acende quando a tela atual não é nenhum dos quatro atalhos, para dizer onde você está.
//
// Some enquanto um campo de texto está com o teclado aberto: no Android a tela
// encolhe e a barra subiria junto, roubando espaço do formulário.

const CAMPO_TEXTO = 'input:not([type=checkbox]):not([type=radio]):not([type=button]):not([type=submit]):not([type=file]):not([type=range]):not([type=color]), textarea, [contenteditable="true"]'

function useDigitando() {
  const [digitando, setDigitando] = useState(false)
  useEffect(() => {
    const ver = () => {
      const el = document.activeElement
      setDigitando(!!el && el instanceof HTMLElement && el.matches(CAMPO_TEXTO))
    }
    const depois = () => setTimeout(ver, 0)
    document.addEventListener('focusin', ver)
    document.addEventListener('focusout', depois)
    return () => { document.removeEventListener('focusin', ver); document.removeEventListener('focusout', depois) }
  }, [])
  return digitando
}

const caixaIcone = 'h-8 w-14 rounded-full flex items-center justify-center transition-colors duration-200'

function Conteudo({ icone: Icone, label, ativo }: { icone: LucideIcon; label: ReactNode; ativo: boolean }) {
  return (
    <>
      <span className={`${caixaIcone} ${ativo ? 'bg-gema-400 text-tinta-950' : 'text-tinta-200 group-hover:text-white'}`}>
        <Icone size={21} strokeWidth={ativo ? 2.1 : 1.75} aria-hidden />
      </span>
      <span className={`font-display text-xs min-[380px]:text-[13px] leading-none tracking-tight whitespace-nowrap ${ativo ? 'text-white font-semibold' : 'text-tinta-200 font-medium'}`}>
        {label}
      </span>
    </>
  )
}

const alvo = 'group w-full min-h-14 flex flex-col items-center justify-center gap-1 pt-1.5 pb-1 outline-offset-[-2px]'

export default function BarraAtalhos({ menuAberto, onAbrirMenu }: { menuAberto: boolean; onAbrirMenu: () => void }) {
    const { pathname } = useLocation()
    const digitando = useDigitando()
    const { pode } = useAuth()
    // Só os atalhos que a pessoa pode abrir (USU-11); o Menu fica sempre, no fim
    const meus = atalhos.filter(a => pode(a.area))
    const emAtalho = meus.some(a => casa(pathname, a.to, a.exact))

    return (
      <nav
        aria-label="Atalhos"
        className={`lg:hidden flex-shrink-0 bg-menu border-t border-white/[0.06] pb-[env(safe-area-inset-bottom)] ${digitando ? 'hidden' : ''}`}
      >
        <ul className="grid" style={{ gridTemplateColumns: `repeat(${meus.length + 1}, minmax(0, 1fr))` }}>
          {meus.map(a => (
            <li key={a.to} className="min-w-0">
              <NavLink to={a.to} end={a.exact} className={alvo}>
                {({ isActive }) => <Conteudo icone={a.icon} label={a.label} ativo={isActive} />}
              </NavLink>
            </li>
          ))}
          <li className="min-w-0">
            <button
              type="button"
              onClick={onAbrirMenu}
              aria-haspopup="dialog"
              aria-expanded={menuAberto}
              aria-controls={ID_MENU}
              className={alvo}
            >
              <Conteudo icone={Menu} label="Menu" ativo={menuAberto || !emAtalho} />
            </button>
          </li>
        </ul>
      </nav>
    )
}
