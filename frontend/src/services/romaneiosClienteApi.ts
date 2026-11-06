import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoLista } from '../utils/resposta'

// Romaneios de cliente guardados no sistema: tudo o que sai da geração de
// planilhas (tela Planilhas NF-e ou Viagens), com ou sem carga.

export type TipoRomaneioGuardado = 'romaneio' | 'frete'

export interface RomaneioGuardado {
  id: number
  nome: string
  apelido: string
  data: string | null          // "2026-09-18T00:00:00", tirada do nome do arquivo
  numero: number | null
  tipo: TipoRomaneioGuardado
  calculoId: number | null
  placa: string | null
  criadoEm: string
  tamanho: number
  versoes: number
}

export interface LinhaRomaneio {
  tipo: string
  categoria: 'volume' | 'produtoD' | 'produtoB'
  caixas: number
  precoUnitario: number | null
  total: number
}

export interface LeituraRomaneio {
  data: string | null
  motorista: string | null
  placa: string | null
  destinatario: string | null
  fornecedor: string | null
  vendedor: string | null
  linhas: LinhaRomaneio[]
  totalCaixasVolumes: number
  totalCaixasProdutosD: number
  somaLinhas: number
  acrescimos: number
  descontos: number
  valorAPagar: number
  valorAPagarPlanilha: number | null
  funrural: string | null
  funruralPorNota: { nf: string; valor: number }[]
  boletos: string | null
  valoresBoletos: number[]
  avisos: string[]
}

export interface ResumoRomaneio {
  id: number
  nome: string
  apelido: string
  numero: number | null
  data: string | null
  tipo: TipoRomaneioGuardado
  calculoId: number | null
  placaCarga: string | null
  criadoEm: string
  texto: string | null
  romaneio: LeituraRomaneio | null
}

export const romaneiosClienteApi = {
  listar: () => api.get('/api/romaneios-cliente').then(r => comDados(r, d => comoLista<RomaneioGuardado>(d, 'romaneios'))),
  resumo: (id: number) => api.get<ResumoRomaneio>(`/api/romaneios-cliente/${id}/resumo`),
  excluir: (id: number) => api.delete(`/api/romaneios-cliente/${id}`),
  // Troca o número (só existe no nome do arquivo) e/ou a planilha acertada no Excel
  editar: (id: number, dados: { numero?: number; arquivo?: File | null }) => {
    const fd = new FormData()
    if (dados.numero != null) fd.append('numero', String(dados.numero))
    if (dados.arquivo) fd.append('arquivo', dados.arquivo)
    return api.put<{ id: number; nome: string }>(`/api/romaneios-cliente/${id}`, fd,
      { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  // Romaneios feitos fora do sistema (os do Drive). Volta o que entrou e o que ficou de fora.
  importar: (arquivos: File[]) => {
    const fd = new FormData()
    arquivos.forEach(a => fd.append('arquivos', a))
    return api.post<{ novos: number; atualizados: number; recusados: string[] }>(
      '/api/romaneios-cliente/importar', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  baixar: async (id: number, nomePadrao: string) => {
    const r = await api.get(`/api/romaneios-cliente/${id}/arquivo`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, nomePadrao))
  },
}

// A data vem sem fuso ("2026-09-18T00:00:00"): lê os números direto, para o
// navegador não empurrar o dia para trás ao converter.
export const dataBR = (iso: string | null | undefined) => {
  if (!iso) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  return m ? `${m[3]}/${m[2]}/${m[1]}` : ''
}
