import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Busca única (F-02): GET /api/busca?q=. A API procura em todas as tabelas e
// devolve no máximo seis por grupo, cada um com o endereço da tela dele.

export type ItemBusca = {
  titulo: string
  detalhe: string | null
  link: string
  valor: number | null
  data: string | null
  /** Situação curta (Pago, Cancelado) ou o dono do caminhão (Bento, Caio, Dudu) */
  marca: string | null
}

export type GrupoBusca = { chave: string; titulo: string; itens: ItemBusca[]; mais: boolean }

export const MIN_BUSCA = 2

export async function buscar(q: string, signal?: AbortSignal): Promise<GrupoBusca[]> {
  const { data } = await api.get('/api/busca', { params: { q }, signal })
  const r = comoObjeto<{ grupos: GrupoBusca[] }>(data, ['grupos'], 'busca')
  return r.grupos.map(g => ({ ...g, itens: comoLista<ItemBusca>(g.itens) }))
}
