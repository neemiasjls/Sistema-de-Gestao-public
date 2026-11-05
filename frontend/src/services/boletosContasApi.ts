import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoObjeto } from '../utils/resposta'

// Tela Boletos. "A pagar": boletos dos postos (os mesmos da tela
// Abastecimentos) mais os avulsos. "A receber": os que a transportadora emite
// para os clientes (produtosD da CD, frete da Girassol).

export interface BoletoAPagar {
  origem: 'POSTO' | 'AVULSO'
  id: number
  beneficiario: string
  descricao: string
  numero: string
  valor: number | null
  vencimento: string | null
  dataChegada: string
  pagoEm: string | null
  lidoDoPdf: boolean
  avisos: string[]
  nomeArquivo: string
  temPdf: boolean
}

export interface ListaAPagar {
  hoje: string
  resumo: {
    abertos: number
    totalAberto: number
    totalAVencer: number
    vencidos: number
    totalVencido: number
    semValor: number
    vencemEm7Dias: number
  }
  boletos: BoletoAPagar[]
}

export interface AvulsoEdicao {
  descricao: string
  beneficiario: string
  numero: string
  valor: number | null
  vencimento: string | null
  dataChegada: string | null
  pagoEm: string | null
}

export type ClienteReceber = 'CD' | 'GIRASSOL'

export interface BoletoReceber {
  id: number
  cliente: ClienteReceber
  sequencia: number | null
  numero: string
  nossoNumero: string
  pagador: string
  placa: string
  valor: number | null
  vencimento: string | null
  emissao: string | null
  dataCarga: string | null
  recebidoEm: string | null
  observacao: string
  lidoDoPdf: boolean
  avisos: string[]
  nomeArquivo: string
  temPdf: boolean
  criadoEm: string
}

export interface ResumoClienteReceber {
  chave: ClienteReceber
  rotulo: string
  quantidade: number
  totalEmitido: number
  totalRecebido: number
  abertos: number
  totalAReceber: number
  vencidos: number
  totalVencido: number
}

export interface ListaAReceber {
  hoje: string
  clientes: ResumoClienteReceber[]
  boletos: BoletoReceber[]
}

export interface ReceberEdicao {
  cliente: ClienteReceber
  sequencia: number | null
  numero: string
  nossoNumero: string
  pagador: string
  placa: string
  valor: number | null
  vencimento: string | null
  emissao: string | null
  dataCarga: string | null
  recebidoEm: string | null
  observacao: string
}

export interface ImportacaoReceber {
  novos: number
  completados: number
  repetidos: number
  substituidos?: number
  boletos: { id: number; cliente: ClienteReceber; nossoNumero: string; valor: number | null; dataCarga: string | null; nomeArquivo: string }[]
  avisos: string[]
  // Um por arquivo, na ordem em que foram lidos (os recusados no fim)
  itens?: ItemImportacaoReceber[]
}

export interface ItemImportacaoReceber {
  arquivo: string
  situacao: 'NOVO' | 'COMPLETADO' | 'SUBSTITUIDO' | 'REPETIDO' | 'RECUSADO'
  pagador: string | null
  cliente: ClienteReceber | null
  valor: number | null
  vencimento: string | null
  dataCarga: string | null
  placa: string | null
  nossoNumero: string | null
  numero?: string | null
  emissao?: string | null
  avisos: string[]
  // Só no REPETIDO: o que foi lido do PDF subido (os campos de cima são do guardado)
  novo?: { numero: string; nossoNumero: string; valor: number | null; emissao: string | null; vencimento: string | null; dataCarga: string | null } | null
}

const base = '/api/boletos-contas'

const baixar = async (url: string, padrao: string) => {
  const r = await api.get(url, { responseType: 'blob' })
  downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, padrao))
}

const pdfs = (arquivos: File[], extra?: Record<string, string>) => {
  const fd = new FormData()
  arquivos.forEach(a => fd.append('arquivos', a))
  Object.entries(extra ?? {}).forEach(([k, v]) => fd.append(k, v))
  return fd
}
const multipart = { headers: { 'Content-Type': 'multipart/form-data' } }

export const boletosContasApi = {
  // A pagar
  listarAPagar: () => api.get(`${base}/pagar`).then(r => comDados(r, d => {
    const l = comoObjeto<ListaAPagar>(d, ['boletos'], 'boletos a pagar')
    l.resumo = comoObjeto(l.resumo ?? {}, [], 'resumo a pagar')
    return l
  })),
  pagarPosto: (id: number, pagoEm: string | null) => api.put(`${base}/pagar/posto/${id}/pago`, { pagoEm }),
  pagarAvulso: (id: number, pagoEm: string | null) => api.put(`${base}/pagar/avulsos/${id}/pago`, { pagoEm }),
  importarAvulsos: (arquivos: File[], descricao: string) =>
    api.post<{ boletos: unknown[]; paraReceber?: number; avisos: string[] }>(`${base}/pagar/avulsos`, pdfs(arquivos, { descricao }), multipart),
  criarAvulso: (d: AvulsoEdicao) => api.post<{ id: number }>(`${base}/pagar/avulsos/manual`, d),
  editarAvulso: (id: number, d: AvulsoEdicao) => api.put(`${base}/pagar/avulsos/${id}`, d),
  excluirAvulso: (id: number) => api.delete(`${base}/pagar/avulsos/${id}`),
  baixarAvulso: (b: Pick<BoletoAPagar, 'id' | 'nomeArquivo'>) =>
    baixar(`${base}/pagar/avulsos/${b.id}/pdf`, b.nomeArquivo || 'BOLETO.pdf'),
  // O PDF do boleto de posto sai pela rota da tela Abastecimentos
  baixarPosto: (b: Pick<BoletoAPagar, 'id' | 'nomeArquivo'>) =>
    baixar(`/api/abastecimento/cobrancas/boletos/${b.id}/pdf`, b.nomeArquivo || 'BOLETO.pdf'),

  // A receber
  listarAReceber: () => api.get(`${base}/receber`).then(r => comDados(r, d => comoObjeto<ListaAReceber>(d, ['clientes', 'boletos'], 'boletos a receber'))),
  // substituir: o boleto que já existe é trocado pelo subido (a tela pergunta antes)
  importarAReceber: (arquivos: File[], cliente: ClienteReceber, substituir = false) =>
    api.post<ImportacaoReceber>(`${base}/receber/importar`, pdfs(arquivos), { ...multipart, params: { cliente, substituir } }),
  // Tela E-mails aos clientes: qualquer PDF arrastado; o cliente sai do pagador e
  // só entra boleto emitido pela transportadora (o da fazenda volta recusado)
  importarAReceberDaTransportadora: (arquivos: File[], substituir = false) =>
    api.post<ImportacaoReceber>(`${base}/receber/importar`, pdfs(arquivos), { ...multipart, params: { soDaTransportadora: true, substituir } }),
  criarAReceber: (d: ReceberEdicao) => api.post<{ id: number }>(`${base}/receber/manual`, d),
  editarAReceber: (id: number, d: ReceberEdicao) => api.put(`${base}/receber/${id}`, d),
  marcarRecebido: (id: number, recebidoEm: string | null) => api.put(`${base}/receber/${id}/recebido`, { recebidoEm }),
  excluirAReceber: (id: number) => api.delete(`${base}/receber/${id}`),
  baixarAReceber: (b: Pick<BoletoReceber, 'id' | 'nomeArquivo'>) =>
    baixar(`${base}/receber/${b.id}/pdf`, b.nomeArquivo || 'BOLETO.pdf'),
}
