import { useEffect, useState } from 'react'

// Tema da tela: claro, um dos quatro escuros, ou o do aparelho. A escolha fica
// neste navegador; o index.html já aplica antes da página desenhar, para não
// piscar branco (a mesma lógica de lá: mexeu aqui, mexa lá).
//
// Guardado em duas chaves: "trx_tema" (claro | escuro; sem nada = automático,
// como sempre foi) e "trx_tom" (qual escuro). Assim o automático também usa o
// escuro escolhido quando o aparelho escurece.

export type Tema = 'claro' | 'escuro' | 'sistema'
export type Tom = 'azul' | 'preto' | 'medio' | 'grafite'

const CHAVE = 'trx_tema'
const CHAVE_TOM = 'trx_tom'
const TONS: Tom[] = ['azul', 'preto', 'medio', 'grafite']

// Cor da barra do navegador no celular (meta theme-color): a da barra de cima
// do site (o cartão de cada tema), para a barra e o topo virarem uma faixa só.
// Antes era a do menu, que no celular fica escondido na gaveta (UI-14).
const COR_BARRA: Record<Tom | 'claro', string> = {
  claro: '#ffffff', azul: '#161e27', preto: '#0e0f10', medio: '#1e1f21', grafite: '#2c2f33',
}

const escuroNoSistema = () => window.matchMedia('(prefers-color-scheme: dark)').matches

function ler<T extends string>(chave: string, validos: readonly T[], padrao: T): T {
  try {
    const v = localStorage.getItem(chave) as T | null
    return v && validos.includes(v) ? v : padrao
  } catch {
    return padrao
  }
}

function gravar(chave: string, valor: string | null) {
  try {
    if (valor === null) localStorage.removeItem(chave)
    else localStorage.setItem(chave, valor)
  } catch { /* navegador sem armazenamento: vale só nesta visita */ }
}

function aplicar(tema: Tema, tom: Tom, suave: boolean) {
  const raiz = document.documentElement
  // Troca de tema com as cores deslizando, só quando a pessoa clica
  if (suave) {
    raiz.classList.add('trocando-tema')
    window.setTimeout(() => raiz.classList.remove('trocando-tema'), 300)
  }
  const escuro = tema === 'escuro' || (tema === 'sistema' && escuroNoSistema())
  raiz.classList.toggle('dark', escuro)
  for (const t of TONS) raiz.classList.toggle(`tema-${t}`, escuro && t === tom && t !== 'azul')
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', COR_BARRA[escuro ? tom : 'claro'])
}

export function useTema() {
  const [tema, setTemaEstado] = useState<Tema>(() => ler(CHAVE, ['claro', 'escuro'] as Tema[], 'sistema'))
  const [tom, setTomEstado] = useState<Tom>(() => ler(CHAVE_TOM, TONS, 'azul'))

  // No modo "sistema", acompanha o aparelho se ele mudar (anoitecer, por ex.)
  useEffect(() => {
    if (tema !== 'sistema') return
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const mudou = () => aplicar('sistema', tom, true)
    mq.addEventListener('change', mudou)
    return () => mq.removeEventListener('change', mudou)
  }, [tema, tom])

  /** Claro, automático ou um escuro (com o tom). */
  function escolher(novo: Tema, novoTom: Tom = tom) {
    gravar(CHAVE, novo === 'sistema' ? null : novo)
    gravar(CHAVE_TOM, novoTom === 'azul' ? null : novoTom)
    aplicar(novo, novoTom, true)
    setTemaEstado(novo)
    setTomEstado(novoTom)
  }

  return { tema, tom, escolher }
}
