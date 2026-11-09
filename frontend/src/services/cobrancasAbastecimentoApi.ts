import api, { downloadBlob, nomeDoArquivo } from './api'
import { comDados, comoObjeto } from '../utils/resposta'

// Cobranças dos postos guardadas no sistema: faturas, boletos e as planilhas de
// divisão. A conferência (fatura × abastecimentos × planilha × boleto) e o
// painel vêm prontos da API.

export type StatusCobranca = 'CONFERE' | 'DIVERGE' | 'FALTA_BOLETO' | 'FALTA_FATURA'

export type FaturaPosto = {
  id: number
  posto: string
  numero: string
  emissao: string | null       // aaaa-mm-dd
  vencimento: string | null
  valorFatura: number | null   // null quando o PDF não trouxe
  soma: number                 // soma dos abastecimentos lidos
  somaNaPlanilha: number       // o que foi para a planilha (todas as placas têm dono no cadastro)
  quantidade: number
  valorBento: number
  quantidadeBento: number
  // Como o Bento escreve no controle do banco: "POSTO 7 VILA NORTE (VOLUME-XAA)"
  descricaoBento?: string | null
  nomeArquivo: string
  criadoEm: string
}

// Linha pronta para o controle do banco do Bento: vencimento, valor e
// "POSTO X (PLACAS)". Separada por tabulação, cola em três colunas no Excel.
export const linhaBancoBento = (f: Pick<FaturaPosto, 'vencimento' | 'valorBento' | 'descricaoBento' | 'posto'>) => {
  const [a, m, d] = (f.vencimento ?? '').split('-')
  const data = d ? `${d}/${m}/${a}` : ''
  const valor = f.valorBento.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  return { data, valor, descricao: f.descricaoBento ?? `POSTO ${f.posto}`, texto: `${data}\t${valor}\t${f.descricaoBento ?? `POSTO ${f.posto}`}` }
}

export type BoletoPosto = {
  id: number
  posto: string
  numeroDocumento: string
  nossoNumero: string
  valor: number | null
  vencimento: string | null
  dataDocumento: string | null
  dataChegada: string
  pagoEm: string | null
  faturaId: number | null
  ligacaoManual: boolean
  lidoDoPdf: boolean
  avisos: string[]
  nomeArquivo: string
  temPdf: boolean
}

export type Cobranca = {
  chave: string
  status: StatusCobranca
  diferencas: string[]
  posto: string
  numero: string
  valor: number | null
  vencimento: string | null
  dataChegada: string
  paga: boolean
  fatura: FaturaPosto | null
  boletos: BoletoPosto[]
}

export type ProximoVencimento = {
  posto: string
  numero: string
  valor: number | null
  data: string
  status: StatusCobranca
}

export type ResumoCobrancas = {
  abertas: number
  totalAberto: number
  semBoleto: number
  semFatura: number
  divergentes: number
  totalAVencer: number
  vencidas: number
  totalVencido: number
  proximosVencimentos: ProximoVencimento[]
}

export type PlanilhaGuardada = {
  id: number
  nome: string
  faturas: string
  faturasIds: string
  soma: number
  quantidade: number
  criadoEm: string
}

export type ListaCobrancas = {
  hoje: string
  resumo: ResumoCobrancas
  cobrancas: Cobranca[]
  planilhas: PlanilhaGuardada[]
}

export type AbastecimentoLido = {
  data: string
  placa: string
  motorista: string
  valor: number
  dono: string | null
}

export type FaturaDetalhe = Omit<FaturaPosto, 'criadoEm'> & {
  abastecimentos: AbastecimentoLido[]
  avisos: string[]
  criadoEm: string
  atualizadoEm: string
}

export type ResultadoImportacao = {
  faturas: (Omit<FaturaPosto, 'nomeArquivo' | 'criadoEm'> & { atualizada: boolean })[]
  idsFaturas: number[]
  boletos: Pick<BoletoPosto, 'id' | 'posto' | 'numeroDocumento' | 'nossoNumero' | 'valor' | 'vencimento' | 'lidoDoPdf' | 'faturaId'>[]
  bento: { valor: number; quantidade: number }
  avisos: string[]
}

// O que a planilha gerada devolve nos cabeçalhos (mesma conferência da tela antiga)
export type ConferenciaPlanilha = {
  soma: number
  totalFatura: number | null
  qtde: number
  avisos: string[]
}

export type FaturaEdicao = {
  posto: string
  numero: string
  emissao: string | null
  vencimento: string | null
  valorFatura: number | null
}

export type BoletoEdicao = {
  posto: string
  numeroDocumento: string
  nossoNumero: string
  valor: number | null
  vencimento: string | null
  dataChegada: string
  pagoEm: string | null
  faturaId: number | null
  ligacaoAutomatica: boolean
}

// Em download a resposta de erro chega como Blob; o { erro } fica dentro dele
export const erroCobranca = async (e: unknown, padrao: string): Promise<string> => {
  const dados = (e as { response?: { data?: unknown } })?.response?.data
  if (dados instanceof Blob) {
    try { return JSON.parse(await dados.text()).erro ?? padrao } catch { return padrao }
  }
  return (dados as { erro?: string } | undefined)?.erro ?? padrao
}

// Placa da fatura que não está no cadastro de veículos (ABA-06): a API não
// guarda nem gera nada e devolve 409 com a lista, para a tela pedir o dono.
export type PlacaSemCadastro = {
  placa: string
  abastecimentos: number
  valor: number
  motoristas: string[]
}

export const placasSemCadastro = async (e: unknown): Promise<PlacaSemCadastro[] | null> => {
  const r = (e as { response?: { status?: number; data?: unknown } })?.response
  if (r?.status !== 409) return null
  let dados = r.data
  if (dados instanceof Blob) {
    try { dados = JSON.parse(await dados.text()) } catch { return null }
  }
  const lista = (dados as { placasSemCadastro?: unknown } | undefined)?.placasSemCadastro
  return Array.isArray(lista) && lista.length ? lista as PlacaSemCadastro[] : null
}

// Dono no cadastro de veículos: 1 = Bento, 2 = Caio, 3 = Davi (Dudu)
export type SocioDono = 1 | 2 | 3
export const cadastrarDonos = (lista: { placa: string; socio: SocioDono }[]) =>
  api.post('/api/veiculo/donos', lista)

const base = '/api/abastecimento/cobrancas'
const comObjetoCobrancas = (d: unknown) => comoObjeto<ListaCobrancas>(d, ['cobrancas', 'planilhas'], 'cobranças dos postos')

const baixar = async (url: string, padrao: string) => {
  const r = await api.get(url, { responseType: 'blob' })
  downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, padrao))
}

export const cobrancasAbastecimentoApi = {
  listar: () => api.get(base).then(r => comDados(r, d => {
    const l = comObjetoCobrancas(d)
    l.resumo = comoObjeto<ResumoCobrancas>(l.resumo ?? {}, ['proximosVencimentos'], 'resumo das cobranças')
    l.cobrancas = l.cobrancas.map(c => comoObjeto<Cobranca>(c, ['diferencas', 'boletos']))
    return l
  })),
  detalheFatura: (id: number) => api.get<FaturaDetalhe>(`${base}/faturas/${id}`),

  importar: (arquivos: File[]) => {
    const fd = new FormData()
    arquivos.forEach(f => fd.append('arquivos', f))
    return api.post<ResultadoImportacao>(`${base}/importar`, fd, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  },

  // Gera (e guarda) a planilha de divisão das faturas, baixa e devolve a conferência
  gerarPlanilha: async (faturaIds: number[]): Promise<ConferenciaPlanilha> => {
    const r = await api.post(`${base}/planilha`, { faturaIds }, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, 'ABASTECIMENTOS.xlsx'))
    const avisos = r.headers['x-avisos'] as string | undefined
    const total = r.headers['x-total-fatura'] as string | undefined
    return {
      soma: parseFloat((r.headers['x-soma'] as string) ?? '0'),
      totalFatura: total ? parseFloat(total) : null,
      qtde: parseInt((r.headers['x-qtde'] as string) ?? '0', 10),
      avisos: avisos ? decodeURIComponent(avisos).split(' | ') : [],
    }
  },

  editarFatura: (id: number, dados: FaturaEdicao) => api.put(`${base}/faturas/${id}`, dados),
  editarBoleto: (id: number, dados: BoletoEdicao) => api.put(`${base}/boletos/${id}`, dados),

  baixarFatura: (f: Pick<FaturaPosto, 'id' | 'nomeArquivo'>) =>
    baixar(`${base}/faturas/${f.id}/pdf`, f.nomeArquivo || 'FATURA.pdf'),
  baixarBoleto: (b: Pick<BoletoPosto, 'id' | 'nomeArquivo'>) =>
    baixar(`${base}/boletos/${b.id}/pdf`, b.nomeArquivo || 'BOLETO.pdf'),
  baixarPlanilha: (p: Pick<PlanilhaGuardada, 'id' | 'nome'>) =>
    baixar(`${base}/planilhas/${p.id}`, p.nome || 'ABASTECIMENTOS.xlsx'),

  excluirFatura: (id: number) => api.delete(`${base}/faturas/${id}`),
  excluirBoleto: (id: number) => api.delete(`${base}/boletos/${id}`),
  excluirPlanilha: (id: number) => api.delete(`${base}/planilhas/${id}`),
}
