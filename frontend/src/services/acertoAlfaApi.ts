import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoLista } from '../utils/resposta'
import type { Conferencia } from '../components/ConferenciaSemana'
import type { RomaneioFazenda } from './volumesEmbaladosApi'

// Acertos com a Fazenda Alfa guardados no sistema, com a mesma cara da
// planilha: à esquerda o controle de descontos dos romaneios da fazenda, à
// direita os pagamentos e depósitos. A planilha sai sempre destes dados.

// Sugestões da coluna de observação: os clientes que pagam por boleto e o PIX de sempre
export const OBSERVACOES_SUGERIDAS = ['AURORA', 'BRZ', 'CD', 'DUNAS', 'LITORAL', 'V. M. ROCHA']

// Sugestões da coluna de condição, como aparecem nas planilhas
export const CONDICOES_SUGERIDAS = ['SALDO ANT.', 'PIX', 'BOLETO', 'DEPÓSITO', 'CHEQUE', 'CHEQUES', 'DINHEIRO', 'TED']

export type RomaneioAcerto = {
  numero: number              // 0 quando a linha leva texto no lugar do número
  rotulo: string | null       // "BOLETO", "CH DEV"...
  data: string | null         // aaaa-mm-dd
  valor: number
  caixas: number
  descontoCaixa: number | null // vazio = sem desconto (coluna E vazia)
}

export type PagamentoAcerto = {
  data: string | null         // aaaa-mm-dd; no boleto é o vencimento
  condicao: string            // SALDO ANT., PIX, BOLETO, DEPÓSITO, CHEQUE...
  valor: number | null        // vazio = lançado antes de o dinheiro (ou o boleto) chegar
  observacao: string          // cliente ou texto livre
  numero: string              // número do boleto (ou o que ela escrever)
}

// O pé da planilha: total dos romaneios, depósitos e pagamentos, créditos e débitos com Livia
export type TotaisAcerto = {
  valorRomaneios: number
  descontos: number
  aPagar: number
  caixas: number
  pagamentos: number
  saldo: number
}

export type ArquivoAcerto = {
  id: number
  nome: string
  tipo: 'BOLETO' | 'ROMANEIO' | 'PLANILHA' | 'OUTRO'
  tamanho: number
  criadoEm: string
}

export type AcertoResumo = {
  id: number
  numero: number
  semana: string
  totalRomaneios: number
  totalAPagar: number
  totalPagamentos: number
  saldo: number
  totalCaixas: number
  qtdRomaneios: number
  qtdBoletos: number
  qtdBoletosSemValor: number
  qtdArquivos: number
  criadoEm: string
  atualizadoEm: string
}

export type AcertoDetalhe = {
  id: number
  numero: number
  semana: string
  romaneios: RomaneioAcerto[]
  pagamentos: PagamentoAcerto[]
  notas: string[]
  totais: TotaisAcerto              // já com o SALDO ANT. automático
  // SALDO ANT.: a linha sem valor é automática e vale o saldo final do acerto de
  // número anterior (numero/semana/saldo, nulos se não há acerto antes no sistema)
  saldoAnterior: {
    temLinha: boolean
    automatico: boolean
    doAcertoId: number | null
    numero: number | null
    semana: string | null
    saldo: number | null
  }
  romaneiosEmbalar: RomaneioFazenda[] // romaneios da fazenda sem funrural: volumes para embalar
  avisos: string[]           // da leitura dos arquivos
  avisosPlanilha: string[]   // o que a geração da planilha avisou
  arquivos: ArquivoAcerto[]
  criadoEm: string
  atualizadoEm: string
}

export type AcertoEdicao = {
  numero: number
  semana: string
  romaneios: RomaneioAcerto[]
  pagamentos: PagamentoAcerto[]
  notas: string[]
  atualizadoEm?: string      // de quando a tela abriu; a API recusa se o acerto mudou depois
}

export type ResultadoAcrescentar = {
  acerto: AcertoDetalhe
  boletosNovos: number
  romaneiosNovos: number
  arquivosNovos: number
  avisos: string[]
}

export type ResultadoSemana = {
  acertos: {
    acertoId: number
    numero: number
    semana: string
    criado: boolean
    arquivosNovos: number
    boletosNovos: number
    romaneiosNovos: number
    avisos: string[]
  }[]
  avisos: string[]
  // O que cada arquivo era e para onde foi (destino null: ficou de fora ou já estava guardado)
  arquivos: ArquivoIdentificado[]
}

export type TipoArquivoIdentificado =
  | 'BOLETO_FAZENDA' | 'ROMANEIO_FAZENDA' | 'BOLETO_RECEBER' | 'NAO_RECONHECIDO' | 'ILEGIVEL' | 'FORA'

export type ArquivoIdentificado = {
  nome: string
  tipo: TipoArquivoIdentificado
  destino: string | null
  acertoId: number | null
  cliente: string | null     // pagador do boleto (AURORA, CD...) ou cliente do boleto a receber
  valor: number | null
  vencimento: string | null
  numero: string | null      // número do boleto ou do(s) romaneio(s)
  data: string | null        // data do documento (boleto), do romaneio ou da carga
  caixas: number | null
  placa: string | null
  avisos: string[]
  embalar?: RomaneioFazenda[] | null  // romaneio sem funrural: para qual controle de volumes foi
}

export type ResultadoImportacao = {
  arquivo: string
  acertoId: number | null
  numero: number | null
  semana: string | null
  criado: boolean
  totais: TotaisAcerto | null
  gravados: { aPagar: number | null; pagamentos: number | null; saldo: number | null } | null
  avisos: string[]
  erro: string | null
}

// Em download a resposta de erro chega como Blob; o { erro } fica dentro dele
export const erroAcerto = async (e: unknown, padrao: string): Promise<string> => {
  const dados = (e as { response?: { data?: unknown } })?.response?.data
  if (dados instanceof Blob) {
    try { return JSON.parse(await dados.text()).erro ?? padrao } catch { return padrao }
  }
  return (dados as { erro?: string } | undefined)?.erro ?? padrao
}

export type RomaneioDaFazenda = {
  numero: number | null; data: string | null; caixas: number | null; valor: number | null; descontoCaixa: number | null
  acertoId: number; acertoNumero: number
  /** Só o que veio em PDF: de cliente (com funrural) ou de embalar */
  tipo: 'cliente' | 'embalar' | null
  arquivoId: number | null
}

const raiz = '/api/acerto-alfa'
const base = `${raiz}/acertos`

const formulario = (arquivos: File[]) => {
  const fd = new FormData()
  arquivos.forEach(f => fd.append('arquivos', f))
  return fd
}
const multipart = { headers: { 'Content-Type': 'multipart/form-data' } }

export const acertoAlfaApi = {
  listar: () => api.get(base).then(r => comDados(r, d => comoLista<AcertoResumo>(d, 'acertos'))),
  detalhe: (id: number) => api.get<AcertoDetalhe>(`${base}/${id}`),
  salvar: (id: number, dados: AcertoEdicao) => api.put<AcertoDetalhe>(`${base}/${id}`, dados),
  excluir: (id: number) => api.delete(`${base}/${id}`),
  conferencia: (id: number) => api.get<Conferencia>(`${base}/${id}/conferencia`),
  proximoNumero: () => api.get<{ proximo: number }>(`${raiz}/proximo-numero`),

  // PDFs soltos ao longo da semana: cada um vai para o acerto da semana dele
  enviarDaSemana: (arquivos: File[]) =>
    api.post<ResultadoSemana>(`${raiz}/semana/arquivos`, formulario(arquivos), multipart),

  // Planilhas de acerto feitas no Excel ("194-ACERTO G. ALFA-21-09-2026.xlsx")
  importarPlanilhas: (arquivos: File[]) =>
    api.post<ResultadoImportacao[]>(`${raiz}/importar-planilhas`, formulario(arquivos), multipart),

  acrescentarArquivos: (id: number, arquivos: File[]) =>
    api.post<ResultadoAcrescentar>(`${base}/${id}/arquivos`, formulario(arquivos), multipart),
  excluirArquivo: (id: number, arquivoId: number) => api.delete(`${base}/${id}/arquivos/${arquivoId}`),

  // Baixa e devolve os avisos da geração (cabeçalho X-Avisos), se houver
  baixarPlanilha: async (id: number, padrao: string) => {
    const r = await api.get(`${base}/${id}/planilha`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, padrao))
    const avisos = r.headers['x-avisos'] as string | undefined
    return avisos ? decodeURIComponent(avisos) : null
  },
  /** Todos os romaneios da fazenda, de todos os acertos (tela Romaneios, parte Fazenda) */
  romaneiosFazenda: () => api.get(`${raiz}/romaneios-fazenda`).then(r => comoLista<RomaneioDaFazenda>(r.data, 'romaneios da fazenda')),
  baixarPdf: async (acertoId: number, arquivoId: number, nome: string) => {
    const r = await api.get(`${base}/${acertoId}/arquivos/${arquivoId}`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, nome))
  },
  baixarArquivo: async (id: number, arquivo: ArquivoAcerto) => {
    const r = await api.get(`${base}/${id}/arquivos/${arquivo.id}`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, arquivo.nome))
  },
}

// Segunda-feira da semana de uma data, em aaaa-mm-dd (hora local)
export const segundaDaSemana = (d = new Date()) => {
  const x = new Date(d.getFullYear(), d.getMonth(), d.getDate())
  x.setDate(x.getDate() - ((x.getDay() + 6) % 7))
  return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`
}
