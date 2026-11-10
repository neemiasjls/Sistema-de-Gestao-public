import api from './api'
import { comoLista } from '../utils/resposta'

// Alíquotas do seguro de carga por UF do destino (SEG-06), em %:
// 0,069 = 0,069% do valor da nota. Valem para as próximas planilhas SG.

export interface AliquotaSeguro {
  id: number
  uf: string
  aliquota: number
  atualizadoEm: string
}

export const aliquotasSeguroApi = {
  listar: () =>
    api.get('/api/cte/aliquotas-seguro').then(r => comoLista<AliquotaSeguro>(r.data, 'alíquotas')),
  gravar: (lista: { uf: string; aliquota: number }[]) =>
    api.put('/api/cte/aliquotas-seguro', lista).then(r => comoLista<AliquotaSeguro>(r.data, 'alíquotas')),
}
