import { useSyncExternalStore } from 'react'
import { createRoot } from 'react-dom/client'
import toast from 'react-hot-toast'
import { WifiOff } from 'lucide-react'

// App instalável (F-03, UI-20). Chamado uma vez pelo main.tsx.
//
//   - Registra o service worker (/sw.js, gerado no build a partir de pwa/sw.js).
//     No `npm run dev` não registra: ali o Vite troca os arquivos a toda hora.
//   - Versão nova publicada: o SW novo baixa o casco e espera. Aqui aparece
//     "Nova versão · Atualizar"; tocar troca e recarrega (se houver algo
//     digitado, o aviso de sair sem salvar da UI-17 pergunta antes). "Agora
//     não" deixa para a próxima vez que o app for aberto.
//   - Sem internet: um selo fixo "Sem internet" no alto, por cima de tudo, sem
//     empurrar a tela; some sozinho quando a conexão volta.

export function registrarSW() {
  montarAvisoConexao()

  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return

  const iniciar = async () => {
    let reg: ServiceWorkerRegistration
    try {
      reg = await navigator.serviceWorker.register('/sw.js', { scope: '/' })
    } catch {
      return // sem SW o site funciona igual, só não abre offline
    }

    // Só recarrega quando a pessoa pediu (na 1ª instalação o SW também assume a
    // página, e isso não pode recarregar sozinho no meio do uso)
    let pediuAtualizar = false
    navigator.serviceWorker.addEventListener('controllerchange', () => {
      if (pediuAtualizar) window.location.reload()
    })

    const avisar = (novo: ServiceWorker) => {
      toast(
        t => (
          <span className="flex flex-wrap items-center gap-x-3 gap-y-2">
            <span>Nova versão do sistema</span>
            <span className="flex gap-2">
              <button
                type="button"
                className="rounded-md bg-tinta-700 px-3 py-1.5 text-sm font-semibold text-white hover:bg-tinta-800 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-tinta-500"
                onClick={() => {
                  pediuAtualizar = true
                  toast.dismiss(t.id)
                  novo.postMessage({ tipo: 'PULAR_ESPERA' })
                }}
              >
                Atualizar
              </button>
              <button
                type="button"
                className="rounded-md px-2 py-1.5 text-sm text-gray-600 hover:bg-gray-100"
                onClick={() => toast.dismiss(t.id)}
              >
                Agora não
              </button>
            </span>
          </span>
        ),
        { id: 'nova-versao', duration: Infinity },
      )
    }

    // Já tinha uma esperando (baixada numa visita anterior)
    if (reg.waiting && navigator.serviceWorker.controller) avisar(reg.waiting)

    reg.addEventListener('updatefound', () => {
      const novo = reg.installing
      if (!novo) return
      novo.addEventListener('statechange', () => {
        // "installed" com um SW já no controle = é atualização, não 1ª instalação
        if (novo.state === 'installed' && navigator.serviceWorker.controller) avisar(novo)
      })
    })

    // O app instalado fica aberto dias: confere se há versão nova ao voltar para ele
    let ultimaConferida = Date.now()
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState !== 'visible' || Date.now() - ultimaConferida < 10 * 60 * 1000) return
      ultimaConferida = Date.now()
      reg.update().catch(() => { /* sem rede: confere da próxima vez */ })
    })
  }

  // Depois do 'load', para não disputar a rede com a primeira carga
  if (document.readyState === 'complete') iniciar()
  else window.addEventListener('load', iniciar, { once: true })
}

// ---- Sem internet -----------------------------------------------------------

const assinar = (f: () => void) => {
  window.addEventListener('online', f)
  window.addEventListener('offline', f)
  return () => {
    window.removeEventListener('online', f)
    window.removeEventListener('offline', f)
  }
}
const conectado = () => navigator.onLine

function AvisoConexao() {
  const online = useSyncExternalStore(assinar, conectado, () => true)
  if (online) return null
  return (
    <div
      role="status"
      aria-live="polite"
      className="pointer-events-none fixed inset-x-0 z-[60] flex justify-center px-4"
      style={{ top: 'calc(env(safe-area-inset-top) + 8px)' }}
    >
      <p className="flex items-center gap-2 rounded-full border border-gema-300 bg-gema-50 px-3 py-1.5 text-xs sm:text-sm text-tinta-900 shadow-md animate-entrada">
        <WifiOff className="h-4 w-4 flex-shrink-0" aria-hidden />
        <span className="font-semibold">Sem internet</span>
        <span className="hidden sm:inline text-tinta-800">os dados não carregam nem salvam até voltar</span>
      </p>
    </div>
  )
}

function montarAvisoConexao() {
  const lugar = document.createElement('div')
  lugar.id = 'aviso-conexao'
  document.body.appendChild(lugar)
  createRoot(lugar).render(<AvisoConexao />)

  window.addEventListener('online', () => toast.success('Conexão de volta', { id: 'conexao' }))
}
