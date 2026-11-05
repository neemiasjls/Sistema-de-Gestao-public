// Arquivos recebidos pelo "Compartilhar" do celular (F-05, UI-20).
//
// Caminho: o PDF/XML compartilhado do WhatsApp ou do e-mail chega por POST em
// /compartilhar-receber (manifest: share_target). O service worker (pwa/sw.js)
// guarda os arquivos aqui no IndexedDB e abre /compartilhar
// (public/compartilhar.html), que pergunta para qual tela vão e leva para
// "/tela?compartilhados=1".
//
// PRÓXIMO PASSO (ainda não ligado): a tela de destino ler os arquivos. O jeito
// mais curto é no AreaSoltar, uma vez, para todas as telas:
//
//   useEffect(() => {
//     if (!new URLSearchParams(location.search).has('compartilhados')) return
//     pegarCompartilhados(aceita).then(arquivos => { if (arquivos.length) onArquivos(arquivos) })
//   }, [])
//
// `pegarCompartilhados` entrega e apaga (não entra duas vezes). Tudo com
// try/catch: sem IndexedDB devolve lista vazia.

const BANCO = 'trx-compartilhados'
const LOJA = 'arquivos'

type Guardado = { id: number; nome: string; tipo: string; tamanho: number; recebidoEm: number; arquivo: Blob }

function abrir(): Promise<IDBDatabase> {
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

/** Lista o que está guardado, sem apagar. */
export async function listarCompartilhados(): Promise<Guardado[]> {
  try {
    const db = await abrir()
    const itens = await new Promise<Guardado[]>((ok, falha) => {
      const pedido = db.transaction(LOJA).objectStore(LOJA).getAll()
      pedido.onsuccess = () => ok(pedido.result as Guardado[])
      pedido.onerror = () => falha(pedido.error)
    })
    db.close()
    return itens
  } catch {
    return []
  }
}

/**
 * Entrega como File os arquivos guardados que a tela aceita (pela extensão,
 * ex.: ['.pdf']) e apaga do aparelho os que entregou.
 */
export async function pegarCompartilhados(aceita: string[]): Promise<File[]> {
  try {
    const itens = await listarCompartilhados()
    const servem = itens.filter(i => aceita.some(ext => i.nome.toLowerCase().endsWith(ext.toLowerCase())))
    if (!servem.length) return []
    const db = await abrir()
    await new Promise<void>((ok, falha) => {
      const tx = db.transaction(LOJA, 'readwrite')
      for (const i of servem) tx.objectStore(LOJA).delete(i.id)
      tx.oncomplete = () => ok()
      tx.onerror = () => falha(tx.error)
    })
    db.close()
    return servem.map(i => new File([i.arquivo], i.nome, { type: i.tipo, lastModified: i.recebidoEm }))
  } catch {
    return []
  }
}
