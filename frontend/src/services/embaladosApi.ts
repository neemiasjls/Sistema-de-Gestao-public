import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Módulo Embalados (VOLUME-16..18): quebras, despesas das produtosB, comissões e
// salários — o que era a planilha COMISSÕES VOLUMES EMBALADOS.

export type Periodo = { de?: string; ate?: string }

export type Cor = 'BRANCO' | 'VERMELHO'

export type Quebra = {
  id: number | null
  data: string
  cor: Cor
  produtosC: number
  precoCaixa: number | null
  /** De onde veio o preço: planilha, digitado, calculado pelo romaneio ou faltando */
  dePreco: 'planilha' | 'manual' | 'romaneio' | 'falta'
  valor: number
  controleId: number | null
  controleNumero: number | null
  romaneio: number | null
  precoRomaneio: number | null
  descontoCaixa: number | null
  acertoNumero: number | null
  aviso: string | null
  observacao: string | null
}

export type Despesa = { id: number; data: string; descricao: string; valor: number | null; observacao: string | null; origem: string }

export type Comissao = {
  id: number; data: string; cliente: string; tipo: string | null; caixas: number
  precoCompra: number | null; precoVenda: number | null; frete: number
  fornecedor: string | null; observacao: string | null; origem: string
  porCaixa: number; total: number
}

export type Salario = {
  id: number; mes: string; referencia: string; valor: number | null; inss: number | null; fgts: number | null
  observacao: string | null; origem: string
}

export type Resumo = {
  comissoes: number; salarios: number; despesas: number; quebras: number
  totalDespesas: number; sobra: number; dividido: number; vendasSemCompra: number
}

export type Importacao = { quebras: number; despesas: number; comissoes: number; salarios: number; avisos: string[] }

const params = (p: Periodo) => ({ params: { de: p.de || undefined, ate: p.ate || undefined } })

export const embaladosApi = {
  resumo: (p: Periodo) => api.get('/api/embalados/resumo', params(p)).then(r => comoObjeto<Resumo>(r.data, [], 'resumo')),
  importar: (arquivo: File) => {
    const f = new FormData()
    f.append('arquivo', arquivo)
    return api.post('/api/embalados/importar', f).then(r => comoObjeto<Importacao>(r.data, ['avisos'], 'importação'))
  },

  quebras: (p: Periodo) => api.get('/api/embalados/quebras', params(p)).then(r => comoLista<Quebra>(r.data, 'quebras')),
  salvarQuebra: (q: { data: string; cor: Cor; produtosC: number | null; precoCaixa: number | null; observacao: string | null }) =>
    api.put('/api/embalados/quebras', q),
  excluirQuebra: (id: number) => api.delete(`/api/embalados/quebras/${id}`),

  despesas: (p: Periodo) => api.get('/api/embalados/despesas', params(p)).then(r => comoLista<Despesa>(r.data, 'despesas')),
  salvarDespesa: (id: number | null, d: Omit<Despesa, 'id' | 'origem'>) =>
    id == null ? api.post('/api/embalados/despesas', d) : api.put(`/api/embalados/despesas/${id}`, d),
  excluirDespesa: (id: number) => api.delete(`/api/embalados/despesas/${id}`),

  comissoes: (p: Periodo) => api.get('/api/embalados/comissoes', params(p)).then(r => comoLista<Comissao>(r.data, 'comissões')),
  salvarComissao: (id: number | null, c: Omit<Comissao, 'id' | 'origem' | 'porCaixa' | 'total'>) =>
    id == null ? api.post('/api/embalados/comissoes', c) : api.put(`/api/embalados/comissoes/${id}`, c),
  excluirComissao: (id: number) => api.delete(`/api/embalados/comissoes/${id}`),

  salarios: (p: Periodo) => api.get('/api/embalados/salarios', params(p)).then(r => comoLista<Salario>(r.data, 'salários')),
  salvarSalario: (id: number | null, s: Omit<Salario, 'id' | 'origem'>) =>
    id == null ? api.post('/api/embalados/salarios', s) : api.put(`/api/embalados/salarios/${id}`, s),
  excluirSalario: (id: number) => api.delete(`/api/embalados/salarios/${id}`),
}

/** "BRANCO" → "Branco" */
export const nomeCor = (c: Cor) => (c === 'BRANCO' ? 'Branco' : 'Vermelho')
