// Confere o formato do que a API devolveu antes de a tela usar.
//
// A tela branca (D-01) vinha daqui: as telas faziam `dados.cobrancas.map(...)`
// direto no que chegava. Se chega outra coisa (o Pages devolve o index.html com
// 200 para qualquer caminho; um deploy no meio; uma rota mudada), o React
// quebrava e levava o menu junto. Com estas funções a resposta errada vira um
// erro comum, que cai no `catch` da tela ("Não consegui carregar..."), e as
// listas que vierem nulas viram lista vazia.

export class RespostaInesperada extends Error {
  constructor(oQue: string) {
    super(`O servidor respondeu algo inesperado (${oQue}).`)
    this.name = 'RespostaInesperada'
  }
}

const ehObjeto = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v)

/** Lista ou erro. `null` (lista vazia mal serializada) vira []. */
export function comoLista<T>(v: unknown, oQue = 'lista'): T[] {
  if (Array.isArray(v)) return v as T[]
  if (v === null || v === undefined || v === '') return []
  throw new RespostaInesperada(oQue)
}

/**
 * Objeto ou erro. Os campos de `listas` que vierem nulos ou de outro tipo
 * viram [] (a tela faz `.map` neles sem medo).
 */
export function comoObjeto<T>(v: unknown, listas: readonly string[] = [], oQue = 'dados'): T {
  if (!ehObjeto(v)) throw new RespostaInesperada(oQue)
  const o: Record<string, unknown> = { ...v }
  for (const k of listas) if (!Array.isArray(o[k])) o[k] = []
  return o as T
}

/** Troca o `data` da resposta pelo conferido, mantendo o resto (headers etc.). */
export function comDados<T, R extends { data: unknown }>(r: R, conferir: (d: unknown) => T): Omit<R, 'data'> & { data: T } {
  return { ...r, data: conferir(r.data) }
}
