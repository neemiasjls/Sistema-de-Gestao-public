import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoLista } from '../utils/resposta'

// Planilhas SG (Seguro / ICMS) guardadas no sistema, uma por mês: as geradas
// na tela de CT-es e as trazidas do Drive.

export type OrigemPlanilhaSeguro = 'gerada' | 'importada'

export interface PlanilhaSeguroGuardada {
  id: number
  mes: number
  ano: number
  nome: string
  origem: OrigemPlanilhaSeguro
  quantidadeCtes: number
  totalValorNF: number
  totalFrete: number
  totalSeguro: number
  totalIcms: number
  totalDespesas: number | null
  avisos: number
  criadoEm: string
  atualizadoEm: string
  tamanho: number
}

export interface PlacaSeguro {
  nome: string
  quantidadeCtes: number
  valorNF: number
  frete: number
  seguro: number
  icms: number
  totalCaminhao: number | null   // a linha "CAMINHÃO X" do RESUMO
}

export interface ResumoSeguro {
  mes: number | null
  ano: number | null
  mesReferente: string | null
  origemMes: string | null
  quantidadeCtes: number
  quantidadeCancelados: number
  totalValorNF: number
  totalFrete: number
  totalSeguro: number
  totalIcms: number
  totalValorNFPlanilha: number | null
  totalFretePlanilha: number | null
  totalSeguroPlanilha: number | null
  totalIcmsPlanilha: number | null
  aliquotaIcms: number | null
  valorTotalSeguros: number | null
  iofSeguros: number | null
  diferencaSeguros: number | null
  servicoEscritorio: number | null
  taxaBSoft: number | null
  quantidadeCaminhoes: number | null
  encargosPorCaminhao: number | null
  portoSeguros: number | null
  dasReferente: number | null
  totalDespesas: number | null
  placas: PlacaSeguro[]
  avisos: string[]
}

export interface DetalhePlanilhaSeguro {
  id: number
  mes: number
  ano: number
  nome: string
  origem: OrigemPlanilhaSeguro
  criadoEm: string
  atualizadoEm: string
  valoresInformados: {
    das: number | null
    iofSeguros: number | null
    servicoEscritorio: number | null
    taxaBSoft: number | null
  } | null
  resumo: ResumoSeguro
}

export interface ResultadoImportacaoSeguro {
  guardadas: {
    nome: string
    id: number
    mes: number
    ano: number
    substituiu: boolean
    origemAnterior: OrigemPlanilhaSeguro | null
    avisos: string[]
  }[]
  recusados: string[]
}

export const planilhasSeguroApi = {
  listar: () => api.get('/api/planilhas-seguro').then(r => comDados(r, d => comoLista<PlanilhaSeguroGuardada>(d, 'planilhas SG'))),
  detalhe: (id: number) => api.get<DetalhePlanilhaSeguro>(`/api/planilhas-seguro/${id}`),
  excluir: (id: number) => api.delete(`/api/planilhas-seguro/${id}`),
  // Planilhas feitas fora do sistema (as do Drive). O mês sai do nome
  // ("08-SG AGOSTO 2026.xlsx") ou, sem ele, da própria planilha.
  importar: (arquivos: File[]) => {
    const fd = new FormData()
    arquivos.forEach(a => fd.append('arquivos', a))
    return api.post<ResultadoImportacaoSeguro>(
      '/api/planilhas-seguro/importar', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  baixar: async (id: number, nomePadrao: string) => {
    const r = await api.get(`/api/planilhas-seguro/${id}/arquivo`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, nomePadrao))
  },
}

const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho',
  'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

export const nomeMesSeguro = (mes: number) => MESES[mes - 1] ?? ''
