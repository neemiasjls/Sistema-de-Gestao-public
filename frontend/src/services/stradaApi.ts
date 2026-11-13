import api, { downloadBlob, nomeDoArquivo } from './api'
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
  /** Número da transferência no banco (nome da planilha); null enquanto não numerada. */
  numeroTransferencia: number | null
  /** Vencimento da fatura. */
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
  /** VIA-27: retornos da mesma placa com reembolso de descarga em aberto (só dica). */
  retornos?: RetornoDica[]
}

/** Retorno sugerido para um reembolso de descarga sem viagem (VIA-27). */
export interface RetornoDica {
  id: number; data: string; placa: string | null; motorista: string | null; destino: string | null
  contrato: string | null; reembolsoDescarga: number | null
}

export type SituacaoDescarga = 'a receber' | 'recebido com diferença' | 'aguardando PDF do contrato' | 'descarga não lançada' | 'recebido' | 'nada a receber'

/** GET api/strada/descargas: por retorno, quanto esperar no TIP e o que já caiu. */
export interface Descargas {
  ano: number
  mes: number
  socios: { socio: string | null; nome: string; esperado: number; recebido: number; aReceber: number; retornos: number }[]
  retornos: {
    retornoId: number; data: string; placa: string | null; socio: string | null; nomeSocio: string; motorista: string | null
    destino: string | null; transportadora: string | null; contrato: string | null; nf: string | null
    freteContratado: number; valorContrato: number | null; adiantamentoDescarga: number | null; descarga: number | null
    reembolsoEsperado: number | null; recebido: number; diferenca: number | null; situacao: SituacaoDescarga; aviso: string | null
    reembolsos: { id: number; contrato: string; valor: number; situacao: string; fatura: string | null; vencimento: string | null; caiuNoBanco: boolean; dataCredito: string | null }[]
  }[]
  contratosSemRetorno: {
    id: number; contrato: string; valor: number; placa: string | null; socio: string | null; nomeSocio: string; nf: string | null
    dataContrato: string | null; fatura: string | null; vencimento: string | null; dicas: RetornoDica[]
  }[]
}

/** Bloco de um sócio na planilha da transferência (sempre Bento, Caio e Dudu). */
export interface BlocoSocio {
  socio: string
  nome: string
  linhas: { contrato: string; placa: string | null; valor: number }[]
  total: number
  taxa: number
  saldo: number
}

export interface ContratoSemSocio { id: number; contrato: string; valor: number; placa: string | null; tipo: string; situacao: string }

export interface FaturaDetalhe {
  id: number
  numero: string
  numeroTransferencia: number | null
  dataGeracao: string | null
  dataPagamento: string | null
  dataCreditoBanco: string | null
  total: number
  taxa: number
  repasse: number
  incompleta: boolean
  avisos: string[]
  observacoes: string[]
  porSocio: ParteSocio[]
  contratos: ContratoFatura[]
  blocos: BlocoSocio[]
  totalTransferido: number
  semSocio: ContratoSemSocio[]
  /** Por que a planilha não sai (contrato sem sócio, sem número); null quando sai. */
  planilhaErro: string | null
}

export type Agrupar = 'vencimento' | 'caiu'

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
  aReceber: { viagemId: number | null; retornoId?: number | null; data: string; nf: string | null; placa: string | null; motorista: string | null; contrato: string | null; socio: string | null; nomeSocio: string; tipo: string; valor: number; motivo: string }[]
  contratosSemFatura: { id: number; contrato: string; valor: number; placa: string | null; nf: string | null; situacao: string; nfSugerida: string | null }[]
}

// Envio de arquivo: o padrão do api é JSON, que o servidor recusa (415)
const multipart = { headers: { 'Content-Type': 'multipart/form-data' } }

const enviar = (rota: string, arquivos: File[], modo?: Modo) => {
  const fd = new FormData()
  arquivos.forEach(a => fd.append('arquivos', a))
  return api.post(rota, fd, { params: { modo }, ...multipart })
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
    return api.post('/api/strada/base/importar', fd, { params: { modo }, ...multipart })
      .then(r => comDados(r, d => comoObjeto<ResultadoArquivo>(d, ['mudancas', 'avisos'], 'resultado da importação')))
  },
  importarFaturas: (arquivos: File[], modo?: Modo) => enviar('/api/strada/faturas/importar', arquivos, modo),
  importarPdfs: (arquivos: File[], modo?: Modo) => enviar('/api/strada/contratos-pdf/importar', arquivos, modo),
  faturas: (ano: number, mes: number, agrupar?: Agrupar) =>
    api.get('/api/strada/faturas', { params: { ano, mes, agrupar } }).then(r => comDados(r, d => comoLista<FaturaResumo>(d, 'faturas'))),
  fatura: (numero: string) =>
    api.get(`/api/strada/faturas/${encodeURIComponent(numero)}`)
      .then(r => comDados(r, d => comoObjeto<FaturaDetalhe>(d, ['contratos', 'porSocio', 'avisos', 'observacoes', 'blocos', 'semSocio'], 'fatura'))),
  /** Planilha da transferência (.xlsx). 409 traz { erro } dentro do Blob: ler com erroStrada. */
  baixarPlanilha: async (numero: string) => {
    const r = await api.get(`/api/strada/faturas/${encodeURIComponent(numero)}/planilha`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `Transferencia fatura ${numero}.xlsx`))
  },
  /** Todas as planilhas do mês (vencimento) num .zip. */
  baixarPlanilhas: async (ano: number, mes: number) => {
    const r = await api.get('/api/strada/faturas/planilhas', { params: { ano, mes }, responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `Transferencias ${String(mes).padStart(2, '0')}-${ano}.zip`))
  },
  /** Troca o número da transferência; 400 { erro } se o número já é de outra. */
  mudarNumero: (numero: string, novo: number) => api.put(`/api/strada/faturas/${encodeURIComponent(numero)}/numero`, { numero: novo }),
  viagens: (params: { ano?: number; mes?: number; busca?: string }) =>
    api.get('/api/strada/viagens', { params }).then(r => comDados(r, d => comoLista<ViagemBase>(d, 'viagens'))),
  painel: (ano: number, mes: number) =>
    api.get('/api/strada/painel', { params: { ano, mes } })
      .then(r => comDados(r, d => comoObjeto<Painel>(d, ['socios', 'incompletas', 'divergencias', 'aReceber', 'contratosSemFatura'], 'painel'))),
  ligar: (id: number, pedido: { viagemId?: number; placa?: string }) => api.put(`/api/strada/contratos/${id}/ligar`, pedido),
  desligar: (id: number) => api.delete(`/api/strada/contratos/${id}/ligar`),
  confirmar: (id: number) => api.put(`/api/strada/contratos/${id}/confirmar`),
  descargas: (ano: number, mes: number) =>
    api.get('/api/strada/descargas', { params: { ano, mes } })
      .then(r => comDados(r, d => comoObjeto<Descargas>(d, ['socios', 'retornos', 'contratosSemRetorno'], 'descargas'))),
  /** Reembolso de descarga ligado à mão ao retorno; null solta. */
  ligarRetorno: (id: number, retornoId: number | null) => api.put(`/api/strada/contratos/${id}/retorno`, { retornoId }),
}

// Em download a resposta de erro chega como Blob; o { erro } fica dentro dele
export const erroStrada = async (e: unknown, padrao: string): Promise<string> => {
  const dados = (e as { response?: { data?: unknown } })?.response?.data
  if (dados instanceof Blob) {
    try { return JSON.parse(await dados.text()).erro ?? padrao } catch { return padrao }
  }
  return (dados as { erro?: string } | undefined)?.erro ?? padrao
}
