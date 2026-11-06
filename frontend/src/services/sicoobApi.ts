import api from './api'
import { comDados, comoObjeto } from '../utils/resposta'

// Extrato da conta PJ no Sicoob, lido pela API do banco (só consulta)

export interface BoletoSugerido {
  id: number
  cliente: 'CD' | 'GIRASSOL'
  rotulo: string
  nossoNumero: string
  valor: number
  vencimento: string | null
  dataCarga: string | null
  placa: string
}

export interface LancamentoSicoob {
  id: string
  data: string | null
  credito: boolean
  valor: number
  descricao: string
  documento: string
  cpfCnpj: string
  complemento: string
  jaMarcado: { id: number; cliente: string; rotulo: string; nossoNumero: string; dataCarga: string | null } | null
  sugestoes: BoletoSugerido[]
}

export interface ExtratoSicoob {
  configurado: boolean
  erro?: string
  inicio: string
  fim: string
  hoje?: string
  saldo?: number | null
  saldoLimite?: number | null
  saldoBloqueado?: number | null
  entradas?: number
  saidas?: number
  lancamentos: LancamentoSicoob[]
}

export const sicoobApi = {
  extrato: (inicio?: string, fim?: string) =>
    api.get('/api/sicoob/extrato', { params: { inicio, fim } }).then(r => comDados(r, d => {
      const e = comoObjeto<ExtratoSicoob>(d, ['lancamentos'], 'extrato do Sicoob')
      e.lancamentos = e.lancamentos.map(l => comoObjeto<LancamentoSicoob>(l, ['sugestoes'], 'lançamento'))
      return e
    })),
}
