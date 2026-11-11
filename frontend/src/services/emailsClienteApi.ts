import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoLista, comoObjeto } from '../utils/resposta'

// E-mail semanal da Cedro Frutas e do Dunas (.zip) e o do frete da Girassol (boleto
// em PDF direto): prévia de cada um, destinatários, envio e histórico.

export interface ArquivoEmail {
  chave: string          // "romaneio-12", "fazenda-40", "produtosD-3", "xml-8", "danfe-8"
  nome: string
  tipo: 'ROMANEIO' | 'BOLETO_FAZENDA' | 'BOLETO_PRODUTOSD' | 'BOLETO_FRETE' | 'NF_XML' | 'NF_DANFE'
  descricao: string
  tamanho: number
  incluir: boolean       // se vai por padrão
}

export interface EnvioEmail {
  id: number
  cliente: string
  rotulo: string
  dataCarga: string      // "2026-09-29"
  assunto: string
  destinatarios: string[]
  enviadoPara: string[]
  anexos: string[]
  nomeZip: string         // vazio no e-mail da Girassol (anexo direto)
  tamanhoZip: number
  modoTeste: boolean
  erro: string | null
  enviadoEm: string
}

export interface PreviaEmail {
  cliente: string
  rotulo: string
  nomeZip: string
  destinatarios: string[]
  destinatariosPadrao: string[]
  assunto: string
  corpo: string
  numerosNF: string[]
  placa: string
  motorista: string | null
  ehFrete: boolean       // Perfil: boleto do frete em PDF direto, sem .zip
  frete: FreteEmail | null
  arquivos: ArquivoEmail[]
  faltando: string[]
  avisos: string[]
  semBoleto?: boolean    // Perfil sem boleto (queda do volume passou do frete): envia sem anexo
  envios: EnvioEmail[]
  /** Boleto substituído depois do último envio: o e-mail volta para enviar */
  boletoTrocadoEm?: string | null
}

// O relatório de frete (.txt) da carga, de onde saem assunto e texto do e-mail da Girassol
export interface FreteEmail {
  relatorioId: number | null
  relatorioNome: string | null
  data: string | null
  caixasNormais: number | null
  caixasProdutoE: number | null
  totalFrete: number | null
  nFs: string[]
}

export interface PreviaEmails {
  data: string
  datas: string[]
  /** Por cliente, os dias com documento e ainda sem envio (prontos ou faltando algo) */
  faltaEnviar?: { cliente: string; rotulo: string; dias: { data: string; pronto: boolean; faltando: string[] }[] }[]
  configurado: boolean
  remetente: string
  remetenteNome: string
  modoTeste: boolean
  destinoTeste: string | null
  emails: PreviaEmail[]
}

export interface ResultadoEnvioEmail {
  enviado: boolean
  erro: string | null
  modoTeste: boolean
  enviadoPara: string[]
  anexos: string[]
  aviso: string | null
  envio: EnvioEmail | null
}

export interface PedidoEnvioEmail {
  cliente: string
  data: string
  destinatarios: string[]
  assunto: string
  corpo: string
  chaves: string[]
  reenviar: boolean
}

export const erroEmail = async (e: unknown, padrao: string): Promise<string> => {
  const dados = (e as { response?: { data?: unknown } })?.response?.data
  if (dados instanceof Blob) {
    try { return JSON.parse(await dados.text()).erro ?? padrao } catch { return padrao }
  }
  return (dados as { erro?: string } | undefined)?.erro ?? padrao
}

// Conflito (409) quer dizer "já foi enviado": a tela pergunta antes de repetir
export const jaEnviado = (e: unknown) =>
  (e as { response?: { status?: number; data?: { jaEnviado?: boolean } } })?.response?.data?.jaEnviado === true

const base = '/api/emails-cliente'

export const emailsClienteApi = {
  previa: (data?: string) => api.get(`${base}/previa`, { params: data ? { data } : undefined }).then(r => comDados(r, d => {
    const p = comoObjeto<PreviaEmails>(d, ['datas', 'emails'], 'prévia dos e-mails')
    p.emails = p.emails.map(e => comoObjeto<PreviaEmail>(e, ['destinatarios', 'destinatariosPadrao', 'numerosNF', 'arquivos', 'faltando', 'avisos', 'envios']))
    return p
  })),
  envios: () => api.get(`${base}/envios`).then(r => comDados(r, d => comoLista<EnvioEmail>(d, 'envios'))),
  salvarDestinatarios: (cliente: string, destinatarios: string[]) =>
    api.put<{ destinatarios: string[] }>(`${base}/config/${encodeURIComponent(cliente)}`, { destinatarios }),
  enviar: (p: PedidoEnvioEmail) => api.post<ResultadoEnvioEmail>(`${base}/enviar`, p),
  baixarArquivo: async (cliente: string, data: string, a: Pick<ArquivoEmail, 'chave' | 'nome'>) => {
    const r = await api.get(`${base}/arquivo`, { params: { cliente, data, chave: a.chave }, responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, a.nome))
  },
  subirRelatorioFrete: (arquivo: File) => {
    const f = new FormData()
    f.append('arquivo', arquivo)
    return api.post<{ nome: string; data: string; novo: boolean; avisos: string[] }>(`${base}/relatorio-frete`, f,
      { headers: { 'Content-Type': 'multipart/form-data' } })
  },
  baixarRelatorio: async (id: number, nome: string) => {
    const r = await api.get(`/api/romaneios-cliente/${id}/arquivo`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, nome))
  },
  baixarZip: async (cliente: string, data: string, chaves: string[], nomeZip: string) => {
    const r = await api.post(`${base}/zip`, { cliente, data, chaves }, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, nomeZip))
  },
}
