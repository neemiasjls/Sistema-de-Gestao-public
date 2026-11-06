import api from './api'
import { comDados, comoLista, comoObjeto } from '../utils/resposta'

// Agrotrade / TIP Bank (PEN-39): base de viagens, faturas da Strada, PDFs dos contratos e painel por sócio

export type Modo = 'manter' | 'substituir'

export interface ResultadoArquivo {
  arquivo: string
  status: 'criado' | 'atualizado' | 'substituido' | 'igual' | 'mantido' | 'confirmar' | 'erro'
  referencia: string | null
  mensagem: string | null
  novas: number
  alteradas: number
  iguais: number
  mudancas: { chave: string; descricao: string }[]
  avisos: string[]
}

export interface ParteSocio { socio: string | null; nome: string; total: number; taxa: number; repasse: number }

export interface FaturaResumo {
  id: number
  numero: string
  dataPagamento: string | null
  total: number
  taxa: number
  repasse: number
  incompleta: boolean
  caiuNoBanco: boolean
  dataCreditoBanco: string | null
  conferidaSemCredito: boolean
  avisos: string[]
  contratos: number
  pendencias: number
  porSocio: ParteSocio[]
}

export interface ViagemResumo {
  id: number
  data: string
  nf: string | null
  nfOriginal: string | null
  placa: string | null
  motorista: string | null
  destino: string | null
  contrato: string | null
  freteEsperado: number | null
  difDescarga: number | null
  valorDiaria: number | null
}

export interface ContratoFatura {
  id: number
  contrato: string
  valor: number
  adiantamento: number | null
  saldo: number | null
  tipo: 'frete' | 'reembolso' | 'indefinido'
  placa: string | null
  socio: string | null
  nomeSocio: string
  valorEsperado: number | null
  diferenca: number | null
  situacao: 'ligado' | 'sem viagem' | 'sugestao' | 'manual'
  nf: string | null
  nfSugerida: string | null
  motorista: string | null
  valorContratoPdf: number | null
  seguro: number | null
  temPdf: boolean
  viagem: ViagemResumo | null
  candidatos: ViagemResumo[]
}

export interface FaturaDetalhe {
  id: number
  numero: string
  dataGeracao: string | null
  dataPagamento: string | null
  total: number
  taxa: number
  repasse: number
  incompleta: boolean
  avisos: string[]
  observacoes: string[]
  porSocio: ParteSocio[]
  contratos: ContratoFatura[]
}

export interface ViagemBase {
  id: number
  data: string
  motorista: string | null
  placa: string | null
  nomeSocio: string
  destino: string | null
  nf: string | null
  nfOriginal: string | null
  frete: number | null
  descontos: number | null
  freteEsperado: number | null
  contrato: string | null
  difDescarga: number | null
  valorDiaria: number | null
  contratos: { viagemId: number; tipo: string; contrato: string; valor: number; dataTransferencia: string | null; situacao: string }[]
}

export interface Divergencia {
  tipo: string
  fatura: string | null
  contratoId: number | null
  contrato: string | null
  texto: string
}

export interface Painel {
  ano: number
  mes: number
  socios: { socio: string | null; nome: string; recebido: number; taxa: number; repasse: number; aReceber: number; viagensAReceber: number }[]
  totais: { recebido: number; taxa: number; repasse: number; aReceber: number }
  incompletas: { numero: string; dataPagamento: string | null; total: number; taxa: number; repasse: number }[]
  divergencias: Divergencia[]
  aReceber: { viagemId: number; data: string; nf: string | null; placa: string | null; motorista: string | null; contrato: string | null; socio: string | null; nomeSocio: string; tipo: string; valor: number; motivo: string }[]
  contratosSemFatura: { id: number; contrato: string; valor: number; placa: string | null; nf: string | null; situacao: string; nfSugerida: string | null }[]
}

const enviar = (rota: string, arquivos: File[], modo?: Modo) => {
  const fd = new FormData()
  arquivos.forEach(a => fd.append('arquivos', a))
  return api.post(rota, fd, { params: { modo } })
    .then(r => comDados(r, d => comoLista<ResultadoArquivo>(d, 'resultado da importação')))
}

export interface ConferenciaBanco {
  fatura: string
  dataPagamento: string | null
  repasse: number
  situacao: 'caiu' | 'duvida' | 'nao caiu' | 'aguardando'
  dataCredito: string | null
  mensagem: string | null
}

export const stradaApi = {
  conferirBanco: () => api.post('/api/strada/faturas/conferir-banco')
    .then(r => comDados(r, d => comoLista<ConferenciaBanco>(d, 'conferência com o banco'))),
  importarBase: (arquivo: File, modo?: Modo) => {
    const fd = new FormData()
    fd.append('arquivo', arquivo)
    return api.post('/api/strada/base/importar', fd, { params: { modo } })
      .then(r => comDados(r, d => comoObjeto<ResultadoArquivo>(d, ['mudancas', 'avisos'], 'resultado da importação')))
  },
  importarFaturas: (arquivos: File[], modo?: Modo) => enviar('/api/strada/faturas/importar', arquivos, modo),
  importarPdfs: (arquivos: File[], modo?: Modo) => enviar('/api/strada/contratos-pdf/importar', arquivos, modo),
  faturas: (ano: number, mes: number) =>
    api.get('/api/strada/faturas', { params: { ano, mes } }).then(r => comDados(r, d => comoLista<FaturaResumo>(d, 'faturas'))),
  fatura: (numero: string) =>
    api.get(`/api/strada/faturas/${encodeURIComponent(numero)}`)
      .then(r => comDados(r, d => comoObjeto<FaturaDetalhe>(d, ['contratos', 'porSocio', 'avisos', 'observacoes'], 'fatura'))),
  viagens: (params: { ano?: number; mes?: number; busca?: string }) =>
    api.get('/api/strada/viagens', { params }).then(r => comDados(r, d => comoLista<ViagemBase>(d, 'viagens'))),
  painel: (ano: number, mes: number) =>
    api.get('/api/strada/painel', { params: { ano, mes } })
      .then(r => comDados(r, d => comoObjeto<Painel>(d, ['socios', 'incompletas', 'divergencias', 'aReceber', 'contratosSemFatura'], 'painel'))),
  ligar: (id: number, pedido: { viagemId?: number; placa?: string }) => api.put(`/api/strada/contratos/${id}/ligar`, pedido),
  desligar: (id: number) => api.delete(`/api/strada/contratos/${id}/ligar`),
  confirmar: (id: number) => api.put(`/api/strada/contratos/${id}/confirmar`),
}
