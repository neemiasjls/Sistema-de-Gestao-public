import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, CornerDownLeft, LoaderCircle, Search, SearchX, X } from 'lucide-react'
import { buscar, MIN_BUSCA, type GrupoBusca, type ItemBusca } from '../../services/buscaApi'
import { formatBRL, socioColor, type Socio } from '../../services/api'

// Busca única (F-02, UI-19). Um campo que acha nota, CT-e, romaneio, placa,
// cliente, boleto (número, nosso número ou valor), controle de volumes, acerto e
// fatura de posto, e abre a tela onde a coisa mora.
//
// Abre pelo botão da barra de cima ou Ctrl+K (Cmd+K no Mac). É um combobox: o
// foco fica no campo, as setas andam pela lista (aria-activedescendant), Enter
// abre, Esc fecha e o foco volta ao botão que abriu. No celular ocupa a tela toda
// (com o teclado aberto, uma janela que sobe de baixo ficaria atrás dele); no
// computador fica no alto, onde o olho já está.

const ESPERA_MS = 250
const DONOS: Socio[] = ['Bento', 'Caio', 'Dudu']

type Estado =
  | { tipo: 'inicio' }
  | { tipo: 'buscando' }
  | { tipo: 'pronto'; grupos: GrupoBusca[]; q: string }
  | { tipo: 'erro' }

export default function BuscaGeral({ onFechar }: { onFechar: () => void }) {
  const navigate = useNavigate()
  const [texto, setTexto] = useState('')
  const [estado, setEstado] = useState<Estado>({ tipo: 'inicio' })
  const [ativo, setAtivo] = useState(0)
  const [tentativa, setTentativa] = useState(0)
  const campo = useRef<HTMLInputElement>(null)
  const caixa = useRef<HTMLDivElement>(null)
  const lista = useRef<HTMLDivElement>(null)
  const fechar = useRef(onFechar)
  fechar.current = onFechar
  const base = useId().replace(/:/g, '')
  const idLista = `${base}-lista`
  const idTitulo = `${base}-titulo`

  // Busca com espera curta e cancelando a anterior: digitar rápido não enfileira pedidos
  useEffect(() => {
    const q = texto.trim()
    if (q.length < MIN_BUSCA) { setEstado({ tipo: 'inicio' }); return }
    setEstado(e => e.tipo === 'pronto' ? e : { tipo: 'buscando' })
    const ctl = new AbortController()
    const t = setTimeout(() => {
      setEstado(e => e.tipo === 'pronto' ? e : { tipo: 'buscando' })
      buscar(q, ctl.signal)
        .then(grupos => { setEstado({ tipo: 'pronto', grupos, q }); setAtivo(0) })
        .catch(err => { if (!ctl.signal.aborted && err?.name !== 'CanceledError') setEstado({ tipo: 'erro' }) })
    }, ESPERA_MS)
    return () => { clearTimeout(t); ctl.abort() }
  }, [texto, tentativa])

  const itens = useMemo(() => estado.tipo === 'pronto' ? estado.grupos.flatMap(g => g.itens) : [], [estado])
  const carregandoNovo = estado.tipo === 'pronto' && estado.q !== texto.trim() && texto.trim().length >= MIN_BUSCA

  // Foco no campo, Tab preso, fundo travado, foco devolvido ao fechar
  useEffect(() => {
    const antes = document.activeElement as HTMLElement | null
    campo.current?.focus({ preventScroll: true })
    const tecla = (e: globalThis.KeyboardEvent) => {
      if (e.key !== 'Tab' || !caixa.current) return
      const focaveis = Array.from(caixa.current.querySelectorAll<HTMLElement>('input, button:not([disabled])')).filter(x => x.offsetParent !== null)
      if (!focaveis.length) return
      const primeiro = focaveis[0], ultimo = focaveis[focaveis.length - 1]
      if (e.shiftKey && document.activeElement === primeiro) { e.preventDefault(); ultimo.focus() }
      else if (!e.shiftKey && document.activeElement === ultimo) { e.preventDefault(); primeiro.focus() }
    }
    document.addEventListener('keydown', tecla)
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', tecla)
      document.body.style.overflow = overflow
      if (antes && document.contains(antes)) antes.focus({ preventScroll: true })
    }
  }, [])

  // A opção ativa sempre à vista ao andar com as setas
  useEffect(() => {
    lista.current?.querySelector<HTMLElement>(`#${base}-op-${ativo}`)?.scrollIntoView({ block: 'nearest' })
  }, [ativo, base])

  const abrir = (item: ItemBusca) => { fechar.current(); navigate(item.link) }

  const teclas = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); fechar.current(); return }
    if (!itens.length) return
    if (e.key === 'ArrowDown') { e.preventDefault(); setAtivo(a => (a + 1) % itens.length) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setAtivo(a => (a - 1 + itens.length) % itens.length) }
    else if (e.key === 'Home' && e.ctrlKey) { e.preventDefault(); setAtivo(0) }
    else if (e.key === 'End' && e.ctrlKey) { e.preventDefault(); setAtivo(itens.length - 1) }
    else if (e.key === 'Enter') { e.preventDefault(); const it = itens[ativo]; if (it) abrir(it) }
  }

  const temLista = itens.length > 0
  const aviso = estado.tipo === 'pronto' && !temLista
    ? `Nada encontrado para ${estado.q}.`
    : estado.tipo === 'pronto' ? `${itens.length} resultado${itens.length === 1 ? '' : 's'}.` : ''

  let indice = -1
  return createPortal(
    <div className="fixed inset-0 z-50 flex justify-center sm:items-start sm:pt-[10vh] sm:px-4 bg-tinta-950/50 animate-fundo"
         onMouseDown={e => { if (e.target === e.currentTarget) fechar.current() }}>
      <div ref={caixa} role="dialog" aria-modal="true" aria-labelledby={idTitulo}
           className="bg-white w-full h-tela sm:h-auto sm:max-h-[75vh] sm:max-w-2xl sm:rounded-xl shadow-xl flex flex-col overflow-hidden animate-janela">
        <h2 id={idTitulo} className="sr-only">Buscar no sistema</h2>

        {/* Campo */}
        <div className="flex items-center gap-1 pl-1 pr-2 sm:pl-4 border-b border-gray-200 flex-shrink-0 pt-[env(safe-area-inset-top)] sm:pt-0">
          <button type="button" onClick={() => fechar.current()} aria-label="Fechar a busca"
                  className="sm:hidden w-11 h-11 flex items-center justify-center rounded-lg text-gray-600 hover:bg-gray-100 shrink-0">
            <ArrowLeft size={20} aria-hidden />
          </button>
          {estado.tipo === 'buscando' || carregandoNovo
            ? <LoaderCircle size={20} className="hidden sm:block text-tinta-500 animate-spin shrink-0" aria-hidden />
            : <Search size={20} className="hidden sm:block text-gray-400 shrink-0" aria-hidden />}
          <input
            ref={campo}
            type="search"
            role="combobox"
            aria-expanded={temLista}
            aria-controls={idLista}
            aria-autocomplete="list"
            aria-activedescendant={temLista ? `${base}-op-${ativo}` : undefined}
            aria-label="Buscar nota, CT-e, romaneio, placa, cliente ou boleto"
            placeholder="Nota, CT-e, romaneio, placa, boleto..."
            autoComplete="off" autoCorrect="off" autoCapitalize="characters" spellCheck={false}
            enterKeyHint="search"
            data-nao-suja
            value={texto}
            onChange={e => setTexto(e.target.value)}
            onKeyDown={teclas}
            className="flex-1 min-w-0 h-14 bg-transparent border-0 px-2 sm:px-3 text-base sm:text-lg text-gray-900 placeholder:text-gray-400 focus:outline-none focus-visible:ring-0 [&::-webkit-search-cancel-button]:hidden"
          />
          {texto && (
            <button type="button" onClick={() => { setTexto(''); campo.current?.focus() }} aria-label="Limpar a busca"
                    className="w-11 h-11 sm:w-9 sm:h-9 flex items-center justify-center rounded-lg text-gray-500 hover:bg-gray-100 shrink-0">
              <X size={18} aria-hidden />
            </button>
          )}
          <button type="button" onClick={() => fechar.current()}
                  className="hidden sm:inline-flex items-center h-8 px-2 rounded-md border border-gray-200 text-xs font-medium text-gray-500 hover:bg-gray-50 shrink-0">
            Esc
          </button>
        </div>

        <p className="sr-only" aria-live="polite">{aviso}</p>

        {/* Resultados */}
        <div ref={lista} className="flex-1 overflow-y-auto overscroll-contain">
          {estado.tipo === 'inicio' && <Dicas onExemplo={t => { setTexto(t); campo.current?.focus() }} />}
          {estado.tipo === 'buscando' && (
            <div className="p-4 space-y-3" aria-hidden>
              {[70, 55, 62].map(w => <div key={w} className="space-y-2"><div className="esqueleto h-3.5" style={{ width: `${w}%` }} /><div className="esqueleto h-3 w-1/3" /></div>)}
            </div>
          )}
          {estado.tipo === 'erro' && (
            <div className="px-5 py-10 text-center" role="alert">
              <p className="text-sm font-semibold text-gray-800">Não consegui buscar agora.</p>
              <p className="text-sm text-gray-500 mt-1">Pode ser a internet ou o servidor ligando.</p>
              <button type="button" onClick={() => setTentativa(n => n + 1)}
                      className="mt-3 min-h-11 sm:min-h-9 px-4 rounded-lg border border-gray-300 text-sm font-semibold text-gray-800 hover:bg-gray-50">
                Tentar de novo
              </button>
            </div>
          )}
          {estado.tipo === 'pronto' && !temLista && (
            <div className="px-5 py-10 text-center">
              <SearchX size={28} strokeWidth={1.5} className="mx-auto mb-2 text-tinta-400" aria-hidden />
              <p className="text-sm font-semibold text-gray-800">Nada com “{estado.q}”.</p>
              <p className="text-sm text-gray-500 mt-1 max-w-xs mx-auto">Tente só o número (sem pontos), a placa sem traço ou o nome do cliente.</p>
            </div>
          )}
          {temLista && estado.tipo === 'pronto' && (
            <div id={idLista} role="listbox" aria-label="Resultados da busca" className={`pb-2 ${carregandoNovo ? 'opacity-60' : ''}`}>
              {estado.grupos.map(g => (
                <div key={g.chave} role="group" aria-labelledby={`${base}-g-${g.chave}`}>
                  <div id={`${base}-g-${g.chave}`} role="presentation"
                       className="sticky top-0 z-10 bg-white/95 backdrop-blur px-4 sm:px-5 pt-3 pb-1.5 text-sm font-semibold text-gray-500 font-display">
                    {g.titulo}
                  </div>
                  {g.itens.map(item => {
                    indice++
                    const i = indice
                    return (
                      <Opcao key={i} id={`${base}-op-${i}`} item={item} ativo={i === ativo}
                             onEscolher={() => abrir(item)} onApontar={() => setAtivo(i)} />
                    )
                  })}
                  {g.mais && <p className="px-4 sm:px-5 pb-1 text-xs text-gray-500" role="presentation">Tem mais: escreva um pouco mais para filtrar.</p>}
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Rodapé com as teclas, só no computador */}
        <div className="hidden sm:flex items-center gap-4 px-5 py-2.5 border-t border-gray-100 text-xs text-gray-500 flex-shrink-0" aria-hidden>
          <span className="inline-flex items-center gap-1.5"><Tecla>↑</Tecla><Tecla>↓</Tecla> andar</span>
          <span className="inline-flex items-center gap-1.5"><Tecla><CornerDownLeft size={12} /></Tecla> abrir</span>
          <span className="inline-flex items-center gap-1.5"><Tecla>Esc</Tecla> fechar</span>
        </div>
      </div>
    </div>,
    document.body,
  )
}

function Opcao({ id, item, ativo, onEscolher, onApontar }: {
  id: string; item: ItemBusca; ativo: boolean; onEscolher: () => void; onApontar: () => void
}) {
  const dono = item.marca && (DONOS as string[]).includes(item.marca) ? socioColor(item.marca as Socio) : null
  return (
    <div id={id} role="option" aria-selected={ativo}
         onMouseDown={e => e.preventDefault()} onClick={onEscolher} onMouseMove={onApontar}
         className={`mx-2 px-3 py-2.5 min-h-14 sm:min-h-12 rounded-lg flex items-center gap-3 cursor-pointer select-none
                     ${ativo ? 'bg-tinta-50 ring-1 ring-inset ring-tinta-200' : ''}`}>
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-medium text-gray-900 truncate">{item.titulo}</span>
          {item.marca && (dono
            ? <span className={`inline-flex items-center gap-1 px-1.5 py-px rounded text-xs font-semibold border ${dono.bg} ${dono.text} ${dono.border}`}>
                <span className={`w-1.5 h-1.5 rounded-full ${dono.dot}`} aria-hidden />{item.marca === 'Dudu' ? 'Davi' : item.marca}
              </span>
            : <span className={`px-1.5 py-px rounded text-xs font-medium border shrink-0
                ${item.marca === 'Cancelado' ? 'bg-red-50 text-red-700 border-red-200'
                  : item.marca === 'Em aberto' ? 'bg-amber-50 text-amber-800 border-amber-200'
                  : 'bg-gray-50 text-gray-600 border-gray-200'}`}>{item.marca}</span>)}
        </div>
        {(item.detalhe || item.data) && (
          <p className="text-sm text-gray-500 truncate mt-0.5">
            {[item.data && dataCurta(item.data), item.detalhe].filter(Boolean).join(' · ')}
          </p>
        )}
      </div>
      {item.valor !== null && item.valor !== undefined && (
        <span className="text-sm font-semibold text-gray-800 tabular-nums whitespace-nowrap">{formatBRL(item.valor)}</span>
      )}
    </div>
  )
}

const dataCurta = (iso: string) => {
  const [a, m, d] = iso.slice(0, 10).split('-')
  return `${d}/${m}/${a}`
}

function Tecla({ children }: { children: ReactNode }) {
  return <kbd className="inline-flex items-center justify-center min-w-[1.375rem] h-[1.375rem] px-1 rounded border border-gray-200 bg-gray-50 font-sans text-[11px] text-gray-600">{children}</kbd>
}

function Dicas({ onExemplo }: { onExemplo: (t: string) => void }) {
  const exemplos: [string, string][] = [
    ['144827', 'número de nota ou CT-e'],
    ['XAC4D56', 'placa, com ou sem traço'],
    ['Dunas', 'cliente ou posto'],
    ['1.250,00', 'valor de boleto'],
  ]
  return (
    <div className="px-4 sm:px-5 py-5">
      <p className="text-sm text-gray-600">Procura em notas, CT-es, romaneios, caminhões, clientes, boletos, controles de volumes, acertos e faturas de posto.</p>
      <ul className="mt-3 grid sm:grid-cols-2 gap-1.5">
        {exemplos.map(([t, d]) => (
          <li key={t}>
            <button type="button" onClick={() => onExemplo(t)}
                    className="w-full text-left min-h-11 px-3 py-2 rounded-lg border border-gray-200 hover:bg-gray-50 hover:border-gray-300">
              <span className="font-mono text-sm text-gray-800">{t}</span>
              <span className="block text-xs text-gray-500">{d}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
