import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Fechamento mensal por caminhão (VIA-18). As contas moram na API
// (FechamentoMensalCalculo); aqui só os tipos e as chamadas.

export const PLACAS_ACERTO = ['XAC-6D57', 'XAA-9457', 'XAB-4C58'] as const

export interface LinhaViagemFechamento {
  id: number
  dataSaida: string | null
  destino: string | null
  motorista: string
  resultado: number
  freteComissao: number
  porcentagemComissao: number
  comissao: number
}

export interface DespesaFechamento {
  data: string | null
  referencia: string
  valor: number | null
  parcela: string | null
  observacao: string | null
  /** "manual" ou a chave da automática: icms, consorcio:ID, salario:INSS */
  origem: string
  automatica: boolean
  corrigida: boolean
  valorAutomatico: number | null
  deOndeVem: string | null
  aviso: string | null
}

export interface CreditoFechamento { referencia: string; valor: number }
export interface ValeFechamento { data: string | null; valor: number; referencia: string | null }

export interface SalarioFechamento {
  salario: number
  horasExtras: number
  fgts: number
  inss: number
  irr: number
  quinquenio: number
}

export interface TotaisFechamento {
  entradaViagens: number
  creditos: number
  entradaTotal: number
  saidaTotal: number
  sobra: number
  saldoAnterior: number
  total: number
  comissaoTotal: number
  totalVales: number
  salarioLiquido: number
}

export interface Fechamento {
  placa: string
  ano: number
  mes: number
  salvo: boolean
  id: number | null
  fechado: boolean
  fechadoEm: string | null
  observacao: string | null
  viagens: LinhaViagemFechamento[]
  creditos: CreditoFechamento[]
  despesas: DespesaFechamento[]
  vales: ValeFechamento[]
  salario: SalarioFechamento
  saldoAnteriorDigitado: number | null
  saldoAnteriorOrigem: 'fechamento' | 'digitado' | 'vazio'
  totais: TotaisFechamento
  avisos: string[]
}

export interface FechamentoEntrada {
  placa: string
  ano: number
  mes: number
  saldoAnteriorDigitado: number | null
  salario: SalarioFechamento
  observacao: string | null
  despesas: { data: string | null; referencia: string; valor: number | null; parcela: string | null; observacao: string | null; origem: string; corrigida: boolean }[]
  creditos: CreditoFechamento[]
  vales: ValeFechamento[]
}

export interface Consorcio {
  id: number
  placa: string
  descricao: string
  cota: string | null
  valorParcela: number
  totalParcelas: number
  primeiroAno: number
  primeiroMes: number
  ativo: boolean
}
export type ConsorcioEntrada = Omit<Consorcio, 'id'>

const LISTAS = ['viagens', 'creditos', 'despesas', 'vales', 'avisos'] as const
const comoFechamento = (d: unknown) => comoObjeto<Fechamento>(d, LISTAS, 'fechamento')

export const fechamentosApi = {
  montar: (placa: string, ano: number, mes: number) =>
    api.get('/api/fechamentos', { params: { placa, ano, mes } }).then(r => comoFechamento(r.data)),
  salvar: (e: FechamentoEntrada) => api.put('/api/fechamentos', e).then(r => comoFechamento(r.data)),
  fechar: (placa: string, ano: number, mes: number) =>
    api.post('/api/fechamentos/fechar', { placa, ano, mes }).then(r => comoFechamento(r.data)),
  reabrir: (placa: string, ano: number, mes: number) =>
    api.post('/api/fechamentos/reabrir', { placa, ano, mes }).then(r => comoFechamento(r.data)),
  consorcios: (placa?: string) =>
    api.get('/api/fechamentos/consorcios', { params: { placa: placa || undefined } }).then(r => comoLista<Consorcio>(r.data, 'consórcios')),
  criarConsorcio: (e: ConsorcioEntrada) => api.post<Consorcio>('/api/fechamentos/consorcios', e).then(r => r.data),
  salvarConsorcio: (id: number, e: ConsorcioEntrada) => api.put<Consorcio>(`/api/fechamentos/consorcios/${id}`, e).then(r => r.data),
  excluirConsorcio: (id: number) => api.delete(`/api/fechamentos/consorcios/${id}`),
}
