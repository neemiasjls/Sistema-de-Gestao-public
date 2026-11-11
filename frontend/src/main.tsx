import React, { useSyncExternalStore } from 'react'
import ReactDOM from 'react-dom/client'
import { Toaster } from 'react-hot-toast'
import App from './App'
import { acordarServidor } from './services/api'
import { registrarSW } from './pwa/registrarSW'
import { acompanharAlturaTela } from './utils/alturaTela'
import './index.css'

// A API dorme na Render grátis: já pede para ela acordar enquanto a tela de
// login (ou a primeira tela) desenha (D-02).
acordarServidor()

// Toast no celular embaixo e no meio (em cima ele cobria o título da tela e o
// botão do menu); no computador, em cima à direita como sempre.
const celular = window.matchMedia('(max-width: 639px)')
const assinar = (f: () => void) => { celular.addEventListener('change', f); return () => celular.removeEventListener('change', f) }
const ehCelular = () => celular.matches

function Avisos() {
  const noCelular = useSyncExternalStore(assinar, ehCelular)
  return (
    <Toaster
      position={noCelular ? 'bottom-center' : 'top-right'}
      containerStyle={noCelular ? { bottom: 'max(16px, env(safe-area-inset-bottom))' } : undefined}
      toastOptions={{
        duration: 3500,
        // As mesmas variáveis das janelas: acompanha o tema claro e os escuros
        style: {
          background: 'rgb(var(--bg-white))',
          color: 'rgb(var(--text-gray-900))',
          border: '1px solid rgb(var(--border-gray-200))',
          fontFamily: 'Barlow, system-ui, sans-serif',
          maxWidth: 'min(420px, calc(100vw - 32px))',
        },
        error: { duration: 6000 },
      }}
    />
  )
}

acompanharAlturaTela()

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
    <Avisos />
  </React.StrictMode>
)

// App instalável: service worker (só no build), "Nova versão" e "Sem internet" (F-03, UI-20)
registrarSW()
