// Service worker do app instalado (F-03, UI-20).
//
// Este arquivo é um MODELO: o plugin "casco-pwa" do vite.config.ts troca
// __VERSAO__ e __CASCO__ no build e publica o resultado como /sw.js. No
// `npm run dev` não há service worker (o registro só acontece no build).
//
// Regras:
//   - Guarda só o "casco" do site (index.html, JS, CSS, ícones, manifest), para
//     abrir rápido e abrir mesmo sem internet.
//   - NUNCA guarda resposta da API nem dado: /api e /health passam direto, e
//     qualquer outro endereço (a API na Render, as fontes) nem é tocado.
//   - Página (navegação): rede primeiro; sem rede ou rede lenta, o index.html
//     guardado (o site é SPA, o React Router acha a tela; igual ao _redirects).
//   - Versão nova: instala e espera. O site mostra "Nova versão · Atualizar";
//     ao tocar, o site manda PULAR_ESPERA e recarrega. Fechar o app de vez
//     também troca. O cache da versão velha é apagado ao ativar a nova.
//   - Compartilhar do celular (F-05, Android): o POST do Web Share Target é
//     recebido aqui, os arquivos ficam no IndexedDB e a pessoa vai para
//     /compartilhar (public/compartilhar.html).

const VERSAO = '__VERSAO__'
const CASCO = __CASCO__
const CACHE = 'trx-casco-' + VERSAO
const PREFIXO = 'trx-casco-'
const ESPERA_REDE_MS = 5000

self.addEventListener('install', event => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE)
    await Promise.all(CASCO.map(async url => {
      // cache: 'reload' para não pegar cópia velha do cache HTTP do navegador
      const resposta = await fetch(new Request(url, { cache: 'reload' }))
      if (!resposta.ok) throw new Error('casco: ' + url + ' deu ' + resposta.status)
      // O Pages redireciona "/x.html" para "/x"; resposta redirecionada não pode
      // ser entregue a uma navegação, então guarda uma cópia limpa.
      const limpa = resposta.redirected
        ? new Response(await resposta.blob(), { status: 200, headers: resposta.headers })
        : resposta
      await cache.put(url, limpa)
    }))
  })())
})

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    const nomes = await caches.keys()
    await Promise.all(nomes.filter(n => n.startsWith(PREFIXO) && n !== CACHE).map(n => caches.delete(n)))
    await self.clients.claim()
  })())
})

self.addEventListener('message', event => {
  if (event.data && event.data.tipo === 'PULAR_ESPERA') self.skipWaiting()
  if (event.data && event.data.tipo === 'VERSAO' && event.ports[0]) event.ports[0].postMessage(VERSAO)
})

self.addEventListener('fetch', event => {
  const req = event.request
  const url = new URL(req.url)

  // Compartilhar do celular (manifest: share_target.action)
  if (req.method === 'POST' && url.origin === self.location.origin && url.pathname === '/compartilhar-receber') {
    event.respondWith(receberCompartilhado(req))
    return
  }

  // Só GET do próprio site. API (em outro endereço), fontes e tudo mais: rede, sem tocar.
  if (req.method !== 'GET' || url.origin !== self.location.origin) return
  // A API também pode estar no mesmo endereço (proxy): nunca guardar.
  if (url.pathname.startsWith('/api/') || url.pathname === '/api' || url.pathname.startsWith('/health')) return
  if (url.pathname === '/sw.js') return

  if (req.mode === 'navigate') {
    event.respondWith(pagina(req, url))
    return
  }

  // Arquivo do casco (JS/CSS com hash, ícones): do cache; o resto, rede.
  event.respondWith((async () => {
    // ignoreVary: o servidor manda "Vary: Origin" e o script com crossorigin leva
    // Origin; sem isso o arquivo guardado (pedido sem Origin) não casa offline.
    const guardado = await caches.match(req, { cacheName: CACHE, ignoreSearch: true, ignoreVary: true })
    return guardado || fetch(req)
  })())
})

async function pagina(req, url) {
  try {
    const resposta = await Promise.race([
      fetch(req),
      new Promise((_, falhar) => setTimeout(() => falhar(new Error('rede lenta')), ESPERA_REDE_MS)),
    ])
    return resposta
  } catch {
    const cache = await caches.open(CACHE)
    const op = { ignoreSearch: true, ignoreVary: true }
    return (await cache.match(url.pathname, op)) || (await cache.match('/', op)) || Response.error()
  }
}

// ---- Compartilhar (F-05) ----------------------------------------------------
// Mesmo banco que src/pwa/compartilhados.ts e public/compartilhar.html.

const BANCO = 'trx-compartilhados'
const LOJA = 'arquivos'
const VALIDADE_MS = 7 * 24 * 60 * 60 * 1000

function abrirBanco() {
  return new Promise((ok, falha) => {
    const pedido = indexedDB.open(BANCO, 1)
    pedido.onupgradeneeded = () => {
      const db = pedido.result
      if (!db.objectStoreNames.contains(LOJA)) db.createObjectStore(LOJA, { keyPath: 'id', autoIncrement: true })
    }
    pedido.onsuccess = () => ok(pedido.result)
    pedido.onerror = () => falha(pedido.error)
  })
}

async function receberCompartilhado(req) {
  try {
    const form = await req.formData()
    const arquivos = form.getAll('arquivos').filter(f => f && typeof f === 'object' && 'size' in f && f.size > 0)
    const db = await abrirBanco()
    const agora = Date.now()
    await new Promise((ok, falha) => {
      const tx = db.transaction(LOJA, 'readwrite')
      const loja = tx.objectStore(LOJA)
      // Limpa o que ficou esquecido há mais de 7 dias
      loja.openCursor().onsuccess = e => {
        const cursor = e.target.result
        if (!cursor) return
        if (agora - (cursor.value.recebidoEm || 0) > VALIDADE_MS) cursor.delete()
        cursor.continue()
      }
      for (const f of arquivos) {
        loja.add({ nome: f.name || 'arquivo', tipo: f.type || '', tamanho: f.size, recebidoEm: agora, arquivo: f })
      }
      tx.oncomplete = () => ok()
      tx.onerror = () => falha(tx.error)
    })
    db.close()
    return Response.redirect('/compartilhar?recebidos=' + arquivos.length, 303)
  } catch {
    return Response.redirect('/compartilhar?erro=1', 303)
  }
}
