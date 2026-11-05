import { useState } from 'react'
import { Check, Monitor, Palette } from 'lucide-react'
import { Modal } from './ModalBase'
import { useTema, type Tema, type Tom } from '../utils/tema'

// Escolha do tema: no Perfil (aparência) e no painel do motorista, pelo ícone
// da barra (BotaoTema). As mesmas opções nos dois lugares (UI-06).

// Cada opção mostra um pedacinho da tela nela: fundo, menu, cartão e a gema.
// As cores do desenho são fixas (não seguem o tema atual), para comparar.
type Amostra = { fundo: string; menu: string; cartao: string; borda: string; texto: string }
type Opcao = { id: string; tema: Tema; tom?: Tom; rotulo: string; dica: string; amostra?: Amostra }

const opcoesTema: Opcao[] = [
  { id: 'claro',   tema: 'claro',  rotulo: 'Claro',       dica: 'O de sempre',
    amostra: { fundo: '#f4f6f8', menu: '#111f2c', cartao: '#ffffff', borda: '#dce2e8', texto: '#37424e' } },
  { id: 'azul',    tema: 'escuro', tom: 'azul',    rotulo: 'Azul noite',  dica: 'Escuro com a tinta',
    amostra: { fundo: '#0f151c', menu: '#111f2c', cartao: '#161e27', borda: '#2a3541', texto: '#cdd4dc' } },
  { id: 'preto',   tema: 'escuro', tom: 'preto',   rotulo: 'Preto total', dica: 'Fundo preto, bom em tela OLED',
    amostra: { fundo: '#000000', menu: '#000000', cartao: '#0e0f10', borda: '#2a2c30', texto: '#d0d2d6' } },
  { id: 'medio',   tema: 'escuro', tom: 'medio',   rotulo: 'Preto médio', dica: 'Cinza-carvão',
    amostra: { fundo: '#141516', menu: '#0c0d0e', cartao: '#1e1f21', borda: '#35383b', texto: '#d4d6d9' } },
  { id: 'grafite', tema: 'escuro', tom: 'grafite', rotulo: 'Preto claro', dica: 'Grafite, mais leve',
    amostra: { fundo: '#222428', menu: '#1a1c1f', cartao: '#2c2f33', borda: '#45494f', texto: '#d9dbde' } },
]

function Miniatura({ a }: { a: Amostra }) {
  return (
    <span aria-hidden className="block h-14 rounded-md overflow-hidden border flex" style={{ background: a.fundo, borderColor: a.borda }}>
      <span className="w-4 shrink-0 flex flex-col items-center pt-1.5 gap-1" style={{ background: a.menu }}>
        <span className="w-2 h-2 rounded-full bg-[#f6bd2c]" />
        <span className="w-2 h-0.5 rounded bg-[#ffffff40]" />
        <span className="w-2 h-0.5 rounded bg-[#ffffff40]" />
      </span>
      <span className="flex-1 p-1.5">
        <span className="block h-full rounded border p-1.5 space-y-1" style={{ background: a.cartao, borderColor: a.borda }}>
          <span className="block h-1 w-3/4 rounded" style={{ background: a.texto }} />
          <span className="block h-1 w-1/2 rounded opacity-50" style={{ background: a.texto }} />
        </span>
      </span>
    </span>
  )
}

export default function EscolhaTema() {
  const { tema, tom, escolher } = useTema()
  const ativo = (o: Opcao) => o.tema === tema && (o.tema !== 'escuro' || o.tom === tom)
  const nomeTom = opcoesTema.find(o => o.tom === tom)?.rotulo ?? 'Azul noite'

  return (
    <div className="space-y-3">
      <div role="radiogroup" aria-label="Tema" className="grid grid-cols-2 sm:grid-cols-3 gap-2.5">
        {opcoesTema.map(o => {
          const sel = ativo(o)
          return (
            <button key={o.id} role="radio" aria-checked={sel} onClick={() => escolher(o.tema, o.tom)}
                    className={`relative flex flex-col text-left rounded-xl border p-2 min-h-[7rem] transition-colors duration-200
                      ${sel ? 'border-tinta-500 ring-2 ring-tinta-400/40 bg-tinta-50' : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'}`}>
              <Miniatura a={o.amostra!} />
              <span className="flex items-center gap-1.5 mt-2 px-0.5">
                <span className="text-sm font-medium text-gray-900">{o.rotulo}</span>
                {sel && <Check size={15} className="text-tinta-600 shrink-0" />}
              </span>
              <span className="block text-xs text-gray-500 px-0.5 leading-snug">{o.dica}</span>
            </button>
          )
        })}
        <button role="radio" aria-checked={tema === 'sistema'} onClick={() => escolher('sistema')}
                className={`flex flex-col text-left rounded-xl border p-2 transition-all duration-200
                  ${tema === 'sistema' ? 'border-tinta-500 ring-2 ring-tinta-400/40 bg-tinta-50' : 'border-gray-200 hover:border-gray-300 hover:bg-gray-50'}`}>
          <span aria-hidden className="h-14 rounded-md border border-gray-200 overflow-hidden flex">
            <span className="flex-1 bg-[#f4f6f8]" />
            <span className="flex-1" style={{ background: opcoesTema.find(o => o.tom === tom)?.amostra?.fundo }} />
          </span>
          <span className="flex items-center gap-1.5 mt-2 px-0.5">
            <Monitor size={14} className="text-gray-500 shrink-0" />
            <span className="text-sm font-medium text-gray-900">Automático</span>
            {tema === 'sistema' && <Check size={15} className="text-tinta-600 shrink-0" />}
          </span>
          <span className="block text-xs text-gray-500 px-0.5 leading-snug">Segue o aparelho; à noite, {nomeTom}</span>
        </button>
      </div>
    </div>
  )
}

/** Ícone na barra escura de cima que abre a escolha do tema (painel do motorista). */
export function BotaoTema() {
  const [aberto, setAberto] = useState(false)
  return (
    <>
      <button type="button" onClick={() => setAberto(true)} aria-label="Mudar o tema" title="Mudar o tema"
              className="inline-flex items-center justify-center w-12 h-12 rounded-lg text-tinta-100 hover:bg-white/10 shrink-0">
        <Palette size={22} aria-hidden />
      </button>
      {aberto && (
        <Modal titulo="Tema" descricao="Toque para trocar; vale neste aparelho." onClose={() => setAberto(false)} fecharFora>
          <EscolhaTema />
        </Modal>
      )}
    </>
  )
}
