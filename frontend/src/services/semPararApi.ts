import api, { downloadBlob, nomeDoArquivo } from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Controle de pedágio Sem Parar de cada mês, guardado como dados (no lugar da
// planilha "N-SEM PARAR -MÊS-AA.xlsx"): fatura por placa, vales-pedágio (VPO) e
// créditos inseridos. A planilha é gerada destes dados no botão Exportar Excel.

export const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho',
  'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']

export const nomeMes = (mes: number, ano: number) => `${MESES[mes - 1] ?? mes} de ${ano}`

export type TipoLinha = 'FATURA' | 'VPO' | 'CREDITO'
export type Origem = 'gerado' | 'planilha' | 'manual'

export type ControleResumo = {
  id: number
  ano: number
  mes: number
  origem: Origem
  editado: boolean
  atualizadoEm: string
  arquivos: number
  nome: string
  fatura: number
  creditos: number
  vpo: number
  valor: number
  qtdVpo: number
  qtdCreditos: number
  qtdPlacas: number
}

export type LinhaSemParar = {
  id: number
  tipo: TipoLinha
  placa: string
  data: string | null   // aaaa-mm-dd, só no VPO
  valor: number
  ordem: number
}

export type TotalPlaca = { placa: string; fatura: number; creditos: number; vpo: number; valor: number }

export type ControleDetalhe = {
  id: number
  ano: number
  mes: number
  origem: Origem
  editado: boolean
  criadoEm: string
  atualizadoEm: string
  nome: string
  placas: string[]
  avisos: string[]
  linhas: LinhaSemParar[]
  totais: { fatura: number; creditos: number; vpo: number; valor: number; porPlaca: TotalPlaca[] }
  arquivos: { id: number; nome: string; tipo: 'PLANILHA' | 'VPO' | 'FATURA'; tamanho: number; criadoEm: string }[]
  anteriorId: number | null
  proximoId: number | null
}

export type ResultadoImportacao = {
  nome: string
  status: 'criado' | 'atualizado' | 'editado' | 'erro'
  id: number | null
  mes: number | null
  ano: number | null
  linhas: number
  avisos: string[]
  erro: string | null
}

export type LinhaDto = { tipo: TipoLinha; placa: string; data: string | null; valor: number }

const multipart = { headers: { 'Content-Type': 'multipart/form-data' } }

export const semPararApi = {
  listar: () => api.get('/api/semparar/controles').then(r => comoLista<ControleResumo>(r.data, 'controles do Sem Parar')),
  detalhe: (id: number) => api.get(`/api/semparar/controles/${id}`).then(r => {
    const c = comoObjeto<ControleDetalhe>(r.data, ['placas', 'avisos', 'linhas', 'arquivos'], 'controle do Sem Parar')
    c.totais = comoObjeto(c.totais ?? { fatura: 0, creditos: 0, vpo: 0, valor: 0 }, ['porPlaca'], 'totais')
    return c
  }),
  criar: (mes: number, ano: number) => api.post<{ id: number }>('/api/semparar/controles', { mes, ano }).then(r => r.data),
  excluir: (id: number) => api.delete(`/api/semparar/controles/${id}`),
  placas: (id: number, placas: string[]) => api.put(`/api/semparar/controles/${id}/placas`, { placas }),

  criarLinha: (id: number, dto: LinhaDto) => api.post<{ id: number }>(`/api/semparar/controles/${id}/linhas`, dto),
  atualizarLinha: (linhaId: number, dto: LinhaDto) => api.put(`/api/semparar/linhas/${linhaId}`, dto),
  excluirLinha: (linhaId: number) => api.delete(`/api/semparar/linhas/${linhaId}`),

  // Lê o relatório de VPO + fatura e guarda como o controle do mês
  gerar: (vpo: File, fatura: File, mes: number, ano: number, substituir = false) => {
    const fd = new FormData()
    fd.append('vpo', vpo)
    fd.append('fatura', fatura)
    return api.post<{ id: number; criado: boolean; avisos: string[]; linhas: number }>(
      `/api/semparar/gerar?mes=${mes}&ano=${ano}${substituir ? '&substituir=true' : ''}`, fd, multipart).then(r => r.data)
  },

  importarPlanilhas: (arquivos: File[], substituir = false) => {
    const fd = new FormData()
    arquivos.forEach(a => fd.append('arquivos', a))
    return api.post<ResultadoImportacao[]>(
      `/api/semparar/importar-planilhas${substituir ? '?substituir=true' : ''}`, fd, multipart).then(r => r.data)
  },

  exportarExcel: async (c: { id: number; nome: string }) => {
    const r = await api.get(`/api/semparar/controles/${c.id}/excel`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, c.nome))
  },

  baixarArquivo: async (a: { id: number; nome: string }) => {
    const r = await api.get(`/api/semparar/arquivos/${a.id}`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, a.nome)
  },
}

/** Mensagem de erro da API (também quando a resposta veio como blob). */
export async function erroDaApi(e: unknown, padrao: string): Promise<string> {
  const data = (e as { response?: { data?: unknown } })?.response?.data
  if (data instanceof Blob) {
    try { return (JSON.parse(await data.text()) as { erro?: string }).erro ?? padrao } catch { return padrao }
  }
  return (data as { erro?: string } | undefined)?.erro ?? padrao
}

export const ehConflito = (e: unknown) =>
  (e as { response?: { status?: number; data?: { conflito?: boolean } } })?.response?.status === 409 &&
  (e as { response?: { data?: { conflito?: boolean } } })?.response?.data?.conflito === true
