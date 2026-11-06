import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Fechamento mensal por motorista (VIA-18, VIA-21 desde 02/11/2026; era por caminhão). As contas moram na API
// (FechamentoMensalCalculo); aqui só os tipos e as chamadas.

export interface MotoristaFechamento { id: number; nome: string }

export interface LinhaViagemFechamento {
  id: number
  dataSaida: string | null
  destino: string | null
  motorista: string
  placa: string
  placaCarreta: string | null
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
  motoristaId: number
  motorista: string
  /** Placas com que ele viajou no mês */
  placas: string[]
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
  motoristaId: number
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

const LISTAS = ['viagens', 'creditos', 'despesas', 'vales', 'avisos', 'placas'] as const
const comoFechamento = (d: unknown) => comoObjeto<Fechamento>(d, LISTAS, 'fechamento')

export const fechamentosApi = {
  motoristas: () => api.get('/api/fechamentos/motoristas').then(r => comoLista<MotoristaFechamento>(r.data, 'motoristas')),
  montar: (motoristaId: number, ano: number, mes: number) =>
    api.get('/api/fechamentos', { params: { motoristaId, ano, mes } }).then(r => comoFechamento(r.data)),
  salvar: (e: FechamentoEntrada) => api.put('/api/fechamentos', e).then(r => comoFechamento(r.data)),
  fechar: (motoristaId: number, ano: number, mes: number) =>
    api.post('/api/fechamentos/fechar', { motoristaId, ano, mes }).then(r => comoFechamento(r.data)),
  reabrir: (motoristaId: number, ano: number, mes: number) =>
    api.post('/api/fechamentos/reabrir', { motoristaId, ano, mes }).then(r => comoFechamento(r.data)),
  consorcios: (placa?: string) =>
    api.get('/api/fechamentos/consorcios', { params: { placa: placa || undefined } }).then(r => comoLista<Consorcio>(r.data, 'consórcios')),
  criarConsorcio: (e: ConsorcioEntrada) => api.post<Consorcio>('/api/fechamentos/consorcios', e).then(r => r.data),
  salvarConsorcio: (id: number, e: ConsorcioEntrada) => api.put<Consorcio>(`/api/fechamentos/consorcios/${id}`, e).then(r => r.data),
  excluirConsorcio: (id: number) => api.delete(`/api/fechamentos/consorcios/${id}`),
}
