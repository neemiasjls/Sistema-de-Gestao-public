import { useState, type ReactNode } from 'react'
import { ChevronDown, type LucideIcon } from 'lucide-react'

// Seção que fica fechada numa linha só ("Mudar senha", "Planilha SG") e abre ao
// tocar, mostrando tudo. Fechada, o conteúdo sai da tela (e do teclado): o que
// foi digitado nos campos dela some, a não ser que o estado more na página.
// "lembrar": guarda aberto/fechado neste navegador, com essa chave.

const ler = (chave?: string) => {
  if (!chave) return null
  try { const v = localStorage.getItem(chave); return v === null ? null : v === '1' } catch { return null }
}

export default function SecaoRecolhivel({
  titulo, descricao, icone: Icone, children, inicial = false, lembrar, aberto: abertoFora, onAlternar, className = '',
}: {
  titulo: string
  descricao?: ReactNode
  icone?: LucideIcon
  children: ReactNode
  inicial?: boolean
  lembrar?: string
  aberto?: boolean
  onAlternar?: (aberto: boolean) => void
  className?: string
}) {
  const [abertoDentro, setAbertoDentro] = useState(() => ler(lembrar) ?? inicial)
  const aberto = abertoFora ?? abertoDentro

  const alternar = () => {
    const novo = !aberto
    setAbertoDentro(novo)
    onAlternar?.(novo)
    if (lembrar) try { localStorage.setItem(lembrar, novo ? '1' : '0') } catch { /* sem armazenamento */ }
  }

  return (
    <section className={`bg-white border border-gray-200 rounded-xl overflow-hidden ${className}`}>
      <button type="button" onClick={alternar} aria-expanded={aberto}
              className="w-full flex items-center gap-3 px-4 sm:px-5 py-3.5 text-left hover:bg-gray-50 transition-colors">
        {Icone && (
          <span className="w-9 h-9 rounded-lg bg-tinta-50 text-tinta-600 flex items-center justify-center shrink-0">
            <Icone size={18} strokeWidth={1.75} />
          </span>
        )}
        <span className="flex-1 min-w-0">
          <span className="block font-display text-lg font-semibold text-gray-900 leading-tight">{titulo}</span>
          {descricao && <span className="block text-sm text-gray-500 mt-0.5">{descricao}</span>}
        </span>
        <ChevronDown size={20} className={`text-gray-400 shrink-0 transition-transform duration-200 ${aberto ? 'rotate-180' : ''}`} />
      </button>
      {aberto && (
        <div className="px-4 sm:px-5 pb-5 pt-4 border-t border-gray-100 animate-entrada">
          {children}
        </div>
      )}
    </section>
  )
}
