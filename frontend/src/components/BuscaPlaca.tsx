import { useId, useMemo, useState } from 'react'

// Campo de placa com lista filtrável (VIA-20): o motorista digita parte da placa e
// escolhe da lista; só vale placa do cadastro. O texto livre fica no campo, mas a
// tela só aceita quando bate com uma placa da lista.

const chave = (s: string) => s.toUpperCase().replace(/[^A-Z0-9]/g, '')

export default function BuscaPlaca({ valor, onChange, opcoes, className = '', placeholder, rotulo }: {
  valor: string
  onChange: (placa: string) => void
  /** Placas ativas do tipo certo */
  opcoes: string[]
  className?: string
  placeholder?: string
  /** Nome do campo para o leitor de tela */
  rotulo: string
}) {
  const [aberto, setAberto] = useState(false)
  const [ativo, setAtivo] = useState(0)
  const idLista = useId()
  const filtradas = useMemo(() => {
    const k = chave(valor)
    return opcoes.filter(p => chave(p).includes(k)).slice(0, 40)
  }, [valor, opcoes])

  const escolher = (p: string) => { onChange(p); setAberto(false) }
  const fechar = () => {
    setAberto(false)
    // Digitou a placa inteira igual a uma do cadastro: guarda com o traço certo
    const igual = opcoes.find(p => chave(p) === chave(valor))
    if (igual && igual !== valor) onChange(igual)
  }

  return (
    <div className="relative">
      <input
        role="combobox" aria-expanded={aberto} aria-controls={idLista} aria-autocomplete="list" aria-label={rotulo}
        value={valor} placeholder={placeholder} className={className} autoCapitalize="characters" autoComplete="off" spellCheck={false}
        onFocus={() => { setAberto(true); setAtivo(0) }}
        onBlur={fechar}
        onChange={e => { onChange(e.target.value.toUpperCase()); setAberto(true); setAtivo(0) }}
        onKeyDown={e => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setAberto(true); setAtivo(a => Math.min(a + 1, filtradas.length - 1)) }
          else if (e.key === 'ArrowUp') { e.preventDefault(); setAtivo(a => Math.max(a - 1, 0)) }
          else if (e.key === 'Enter' && aberto && filtradas[ativo]) { e.preventDefault(); escolher(filtradas[ativo]) }
          else if (e.key === 'Escape') setAberto(false)
        }}
      />
      {aberto && (
        <ul id={idLista} role="listbox"
            className="absolute z-20 left-0 right-0 mt-1 max-h-56 overflow-y-auto bg-white border border-gray-300 rounded-lg shadow-lg">
          {filtradas.length === 0 && (
            <li className="px-3 py-3 text-sm text-gray-600">
              {opcoes.length === 0 ? 'Nenhuma placa cadastrada. Fale com o escritório.' : 'Esta placa não está no cadastro.'}
            </li>
          )}
          {filtradas.map((p, i) => (
            <li key={p} role="option" aria-selected={i === ativo}
                // mouseDown (não click): o campo perderia o foco antes e fecharia a lista
                onMouseDown={e => { e.preventDefault(); escolher(p) }}
                className={`px-3 min-h-11 flex items-center cursor-pointer text-base ${i === ativo ? 'bg-tinta-50 font-semibold' : ''}`}>
              {p}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
