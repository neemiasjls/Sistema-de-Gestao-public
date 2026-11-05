import { useSyncExternalStore } from 'react'

// Estado da conversa com a API, para o aviso "Ligando o servidor" (D-02).
//
// A API fica na Render grátis: dorme depois de 15 minutos parada e leva cerca de
// um minuto para acordar. Nesse tempo as requisições ficam presas, sem erro, e a
// tela parece travada. Aqui se conta o que está pendente e desde quando; o aviso
// aparece se alguma espera passar de ESPERA_AVISO.
//
// "acordando": nenhuma resposta ainda nesta visita, ou a última foi há mais de
// 14 minutos (a API provavelmente dormiu). Fora isso, é só uma resposta lenta.

export const ESPERA_AVISO = 4000
const DORME_EM = 14 * 60 * 1000

type Estado = { lento: boolean; acordando: boolean }

const pendentes = new Map<number, number>() // id -> quando começou
let proximoId = 1
let ultimaResposta = 0
let estado: Estado = { lento: false, acordando: false }
let relogio: number | undefined
const ouvintes = new Set<() => void>()

function recalcular() {
  const agora = Date.now()
  let maisAntiga = Infinity
  for (const t of pendentes.values()) maisAntiga = Math.min(maisAntiga, t)
  const lento = maisAntiga !== Infinity && agora - maisAntiga >= ESPERA_AVISO
  const acordando = lento && (ultimaResposta === 0 || maisAntiga - ultimaResposta > DORME_EM)
  if (lento !== estado.lento || acordando !== estado.acordando) {
    estado = { lento, acordando }
    ouvintes.forEach(f => f())
  }
  // Ainda há espera em curso: confere de novo quando ela completar o prazo
  window.clearTimeout(relogio)
  if (maisAntiga !== Infinity && !lento)
    relogio = window.setTimeout(recalcular, ESPERA_AVISO - (agora - maisAntiga) + 20)
}

/** Chamado pelo api.ts quando uma requisição sai. Devolve o id para fechar. */
export function inicioRequisicao(): number {
  const id = proximoId++
  pendentes.set(id, Date.now())
  recalcular()
  return id
}

/** Chamado quando a requisição termina (com resposta ou erro). */
export function fimRequisicao(id: number | undefined, respondeu: boolean) {
  if (id !== undefined) pendentes.delete(id)
  if (respondeu) ultimaResposta = Date.now()
  recalcular()
}

const assinar = (f: () => void) => { ouvintes.add(f); return () => { ouvintes.delete(f) } }
const ler = () => estado

export function useServidor(): Estado {
  return useSyncExternalStore(assinar, ler, ler)
}
