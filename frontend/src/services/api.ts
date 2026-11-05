import axios, { type InternalAxiosRequestConfig } from 'axios'
import { comDados, comoLista, comoObjeto } from '../utils/resposta'
import { fimRequisicao, inicioRequisicao } from './servidor'
import { RespostaInesperada } from '../utils/resposta'

const TOKEN_KEY = 'trx_token'

const api = axios.create({
  baseURL: import.meta.env.VITE_API_URL ?? '',
  headers: { 'Content-Type': 'application/json' },
})

// Cada requisição leva um número para o aviso "Ligando o servidor" (servidor.ts)
type ConfigComId = InternalAxiosRequestConfig & { _idEspera?: number }

// Interceptor: injeta Bearer token em todas as requisições
api.interceptors.request.use(config => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) config.headers['Authorization'] = `Bearer ${token}`
  ;(config as ConfigComId)._idEspera = inicioRequisicao()
  return config
})

// Página HTML onde se esperava JSON: é o index.html do site (o Pages devolve ele
// com 200 para qualquer caminho) ou uma página de erro do provedor. Vira erro,
// para a tela mostrar "não consegui carregar" em vez de quebrar.
const pareceHtml = (dados: unknown) =>
  typeof dados === 'string' && /^\s*</.test(dados)

// Interceptor: redireciona para /login em caso de 401. O próprio login fica de
// fora: senha errada também é 401, e recarregar a página apagava o aviso.
// Senha provisória (403 com trocarSenha) volta para o início, onde a tela de
// criar senha assume.
api.interceptors.response.use(
  res => {
    fimRequisicao((res.config as ConfigComId)._idEspera, true)
    const tipo = res.config.responseType
    if ((!tipo || tipo === 'json') && pareceHtml(res.data))
      return Promise.reject(new RespostaInesperada('página no lugar dos dados'))
    return res
  },
  err => {
    fimRequisicao((err.config as ConfigComId | undefined)?._idEspera, !!err.response)
    const url: string = err.config?.url ?? ''
    if (err.response?.status === 403 && err.response?.data?.trocarSenha) {
      window.location.href = '/'
    } else if (err.response?.status === 401 && !url.includes('/api/auth/login')) {
      localStorage.removeItem(TOKEN_KEY)
      delete axios.defaults.headers.common['Authorization']
      window.location.href = '/login'
    }
    return Promise.reject(err)
  }
)

/**
 * Acorda a API assim que o site abre (até na tela de login): enquanto a pessoa
 * digita, a Render já está ligando. Sem rota de login nem token; erro é ignorado.
 */
export function acordarServidor() {
  api.get('/health', { timeout: 90_000 }).catch(() => { /* só para acordar */ })
}

// ── Tipos ──────────────────────────────────────────────────────────
export type Socio = 'Bento' | 'Caio' | 'Dudu'
export type StatusCTe = 'Ativo' | 'Cancelado'
export type StatusBoleto = 'Pendente' | 'Pago' | 'Vencido'
export type TipoDespesa = 'Combustivel' | 'Pedagio' | 'Diaria' | 'Manutencao' | 'Pneu' | 'Lavagem' | 'Outros'

export interface Motorista {
  id: number
  nome: string
  apelido?: string
  cpf?: string
  telefone?: string
  ativo: boolean
  socioPrincipal?: Socio
}

export interface Veiculo {
  id: number
  placa: string
  socio: Socio
  socioId: number
  ativo: boolean
  motorista?: Pick<Motorista, 'id' | 'nome' | 'apelido'>
}

export interface CTe {
  id: number
  numeroCTe: string
  chaveAcesso: string
  dataEmissao: string
  remetente?: string
  destinatario?: string
  ufOrigem?: string
  ufDestino?: string
  cidadeDestino?: string
  valorNF: number
  valorFrete: number
  aliquotaICMS: number
  pesoKg: number
  status: StatusCTe
  numeroNF?: string
  clienteNome?: string
  placa: string
  socio: Socio
}

export interface Viagem {
  id: number
  dataSaida: string
  dataRetorno?: string
  destino?: string
  freteRecebido: number
  freteRetorno: number
  diarias: number
  comissao: number
  adiantamento: number
  saldoAnterior: number
  saldoFinal: number
  totalDespesas: number
  qtdCTes: number
  mes: number
  ano: number
  fechamentoRealizado: boolean
  veiculo: { id: number; placa: string; socio: Socio }
  motorista: { id: number; nome: string; apelido?: string }
}

export interface Cliente {
  id: number
  nome: string
  nomeFantasia?: string
  cnpj?: string
  email?: string
  telefone?: string
  cidade?: string
  uf?: string
  ativo: boolean
}

export interface NfDetalhe {
  numero: string
  pesoKg: number
  arquivo?: string
  chave?: string     // chave de acesso, usada para baixar o DANFE
  temPdf?: boolean   // se o PDF dessa nota já está guardado no sistema
  placa?: string     // caminhão escrito na nota (infCpl ou veicTransp)
  motorista?: string // motorista escrito na nota, ou o da frota pela placa
}

export interface ItemFreteCalculo {
  numeroNF?: string
  emitente?: string
  cidadeEmitente?: string
  destinatario?: string
  cidadeDestino?: string
  uf?: string
  pesoKg: number
  nfsDetalhadas?: NfDetalhe[]   // NFs individuais do grupo (vem do import XML)
}

export interface FreteCalculoResult {
  pesoTotal: number
  liquotaBruta: number
  liquotaArredondada: number
  totalPesoXKm: number    // "Soma total dos índices" do script
  totalNFs: number        // "Quantidade de NFs processadas" do script
  itens: {
    numeroNF?: string
    emitente?: string
    destinatario?: string
    cidadeOrigem?: string
    cidadeDestino?: string
    uf?: string
    pesoKg: number
    distanciaKm?: number
    pesoXKm: number
    percentualPedagio: number
    valorPedagio: number
    nfsDetalhadas: NfDetalhe[]
  }[]
  rotasNaoCadastradas: string[]
}

// ── Veículos ───────────────────────────────────────────────────────
export const veiculosApi = {
  listar: () => api.get('/api/veiculo').then(r => comDados(r, d =>
    comoLista<{ socio: string; veiculos: Veiculo[] }>(d, 'veículos').map(g => comoObjeto<{ socio: string; veiculos: Veiculo[] }>(g, ['veiculos'])))),
  atualizar: (id: number, data: { motoristaAtualId?: number; ativo?: boolean }) =>
    api.put(`/api/veiculo/${id}`, data),
}

// ── Motoristas ────────────────────────────────────────────────────
export const motoristasApi = {
  listar: (params?: { ativo?: boolean; socio?: string }) =>
    api.get<Motorista[]>('/api/motorista', { params }),
  criar: (data: Omit<Motorista, 'id' | 'ativo'>) =>
    api.post<Motorista>('/api/motorista', data),
  atualizar: (id: number, data: Partial<Motorista>) =>
    api.put<Motorista>(`/api/motorista/${id}`, data),
  desativar: (id: number) => api.delete(`/api/motorista/${id}`),
}

// ── Clientes ──────────────────────────────────────────────────────
export const clientesApi = {
  listar: (params?: { ativo?: boolean; nome?: string }) =>
    api.get<Cliente[]>('/api/cliente', { params }),
  detalhe: (id: number) => api.get<Cliente>(`/api/cliente/${id}`),
  criar: (data: Omit<Cliente, 'id'>) => api.post<Cliente>('/api/cliente', data),
  atualizar: (id: number, data: Partial<Cliente>) =>
    api.put<Cliente>(`/api/cliente/${id}`, data),
}

// ── CT-es ─────────────────────────────────────────────────────────
export const ctesApi = {
  listar: (params?: { mes?: number; ano?: number; veiculoId?: number; status?: string }) =>
    api.get('/api/cte', { params }).then(r => comDados(r, d => comoLista<CTe>(d, 'CT-es'))),
  porPlaca: (params?: { mes?: number; ano?: number }) =>
    api.get('/api/cte/por-placa', { params }),
  importar: (file: File) => {
    const form = new FormData()
    form.append('arquivo', file)
    return api.post('/api/cte/importar', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  },
  importarMultiplos: (files: File[]) => {
    const form = new FormData()
    files.forEach(f => form.append('arquivos', f))
    return api.post('/api/cte/importar-multiplos', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
  },
  calcularFrete: (data: { valorCarga: number; valorPedagio: number; itens: ItemFreteCalculo[] }) =>
    api.post<FreteCalculoResult>('/api/cte/calcular-frete', data),
  listarDistancias: () =>
    api.get<{ id: number; origem: string; ufOrigem: string; destino: string; uf: string; km: number }[]>('/api/cte/distancias'),
  adicionarDistancia: (data: { origem: string; ufOrigem: string; destino: string; uf: string; km: number }) =>
    api.post('/api/cte/distancias', data),
  atualizarDistancia: (id: number, data: { origem: string; ufOrigem: string; destino: string; uf: string; km: number }) =>
    api.put(`/api/cte/distancias/${id}`, data),
  removerDistancia: (data: { origem: string; destino: string; uf: string }) =>
    api.delete('/api/cte/distancias', { data }),
}

// ── Cálculos de frete salvos ──────────────────────────────────────
export interface PlanilhaDaCarga {
  id: number
  nome: string
  criadoEm: string
  tamanho: number
}

export interface CalculoFreteSalvoResumo {
  id: number
  placa: string
  criadoEm: string
  valorCarga: number
  valorPedagio: number
  pesoTotal: number
  liquotaArredondada: number
  totalNFs: number
  totalCTes: number
  totalPlanilhas?: number
}

export interface CalculoFreteSalvoFull extends CalculoFreteSalvoResumo {
  liquotaBruta: number
  payloadJson: string
  planilhas?: PlanilhaDaCarga[]
}

// Guarda XML e PDF no arquivo de notas. Os XMLs que passam pelas telas de
// importação já entram sozinhos pelo servidor; este é o caminho dos PDFs, que
// podem ser soltos junto com os XMLs em qualquer uma dessas telas.
export async function guardarArquivosNFe(files: File[]) {
  if (!files.length) return null
  const fd = new FormData()
  files.forEach(f => fd.append('arquivos', f))
  const r = await api.post<{ novos: number; atualizados: number; pdfs: number; avisos: string[] }>(
    '/api/nfe-arquivos', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
  return r.data
}

export const calculosFreteApi = {
  listar: () => api.get<CalculoFreteSalvoResumo[]>('/api/calculo-frete'),
  obter:  (id: number) => api.get<CalculoFreteSalvoFull>(`/api/calculo-frete/${id}`),
  salvar: (data: {
    placa: string; valorCarga: number; valorPedagio: number
    pesoTotal: number; liquotaBruta: number; liquotaArredondada: number
    totalNFs: number; totalCTes: number; payloadJson: string
    planilhas?: { nome: string; conteudo: string }[]
  }) => api.post<{ id: number; planilhas: number }>('/api/calculo-frete', data),
  /** Grava por cima a carga aberta (editar a carga, em vez de excluir e fazer outra) */
  atualizar: (id: number, data: {
    placa: string; valorCarga: number; valorPedagio: number
    pesoTotal: number; liquotaBruta: number; liquotaArredondada: number
    totalNFs: number; totalCTes: number; payloadJson: string
    planilhas?: { nome: string; conteudo: string }[]
  }) => api.put<{ id: number; planilhas: number }>(`/api/calculo-frete/${id}`, data),
  anexarPlanilhas: (id: number, planilhas: { nome: string; conteudo: string }[]) =>
    api.post<{ guardadas: number }>(`/api/calculo-frete/${id}/planilhas`, planilhas),
  baixarPlanilha: (id: number, planilhaId: number) =>
    api.get(`/api/calculo-frete/${id}/planilha/${planilhaId}`, { responseType: 'blob' }),
  excluir: (id: number) => api.delete(`/api/calculo-frete/${id}`),
}

// ── Viagens ───────────────────────────────────────────────────────
export const viagensApi = {
  listar: (params?: { mes?: number; ano?: number; veiculoId?: number; socio?: string }) =>
    api.get('/api/viagem', { params }).then(r => comDados(r, d => comoLista<Viagem>(d, 'viagens'))),
  detalhe: (id: number) => api.get<Viagem>(`/api/viagem/${id}`),
  criar: (data: object) => api.post<Viagem>('/api/viagem', data),
  atualizar: (id: number, data: object) => api.put<Viagem>(`/api/viagem/${id}`, data),
  adicionarDespesa: (id: number, data: object) =>
    api.post(`/api/viagem/${id}/despesas`, data),
  removerDespesa: (id: number, despesaId: number) =>
    api.delete(`/api/viagem/${id}/despesas/${despesaId}`),
  vincularCTes: (id: number, cteIds: number[]) =>
    api.post(`/api/viagem/${id}/vincular-ctes`, { cTeIds: cteIds }),
  fecharMes: (data: { veiculoId: number; mes: number; ano: number }) =>
    api.post('/api/viagem/fechar-mes', data),
}

// ── Romaneios ────────────────────────────────────────────────────
export interface ItemRomaneio {
  id: number
  produto: string
  descricao?: string
  quantidade: number
  unidade?: string
  valorUnitario: number
  valorTotal: number
}

export interface Romaneio {
  id: number
  dataRomaneio: string
  mes: number
  ano: number
  enviado: boolean
  dataEnvio?: string
  canalEnvio?: string
  total: number
  qtdItens: number
  boletosPendentes: number
  cliente: { id: number; nome: string; nomeFantasia?: string }
}

export interface Boleto {
  id: number
  valor: number
  dataEmissao: string
  dataVencimento: string
  status: string
  dataPagamento?: string
  descricao?: string
  nossoNumero?: string
  tipoProduto?: string
  diasVencimento: number
  cliente?: { id: number; nome: string }
  veiculo?: { id: number; placa: string }
}

export interface LancamentoBanco {
  id: number
  socio: string
  tipo: string
  descricao?: string
  valor: number
  data: string
  mes: number
  ano: number
  placa?: string
}

// /api/financeiro/lancamentos devolve o resumo junto com a lista
export interface LancamentosResponse {
  resumo: { entradas: number; saidas: number; saldo: number }
  lancamentos: LancamentoBanco[]
}

// /api/financeiro/saldos devolve o total pendente junto com a lista
export interface SaldosResponse {
  totalPendente: number
  saldos: SaldoReceber[]
}

export interface SaldoReceber {
  id: number
  empresa: string
  tipo?: string
  valor: number
  dataLancamento: string
  recebido: boolean
  dataRecebimento?: string
  numeroDocumento?: string
  cartaoFrete?: string
  placa?: string
  veiculo?: { id: number; placa: string }
}

export const romaneiosApi = {
  listar: (params?: { clienteId?: number; mes?: number; ano?: number; enviado?: boolean }) =>
    api.get<Romaneio[]>('/api/romaneio', { params }),
  detalhe: (id: number) => api.get<Romaneio & { itens: ItemRomaneio[] }>(`/api/romaneio/${id}`),
  criar: (data: { clienteId: number; dataRomaneio?: string }) =>
    api.post<{ id: number }>('/api/romaneio', data),
  adicionarItem: (id: number, data: object) =>
    api.post(`/api/romaneio/${id}/itens`, data),
  atualizarItem: (id: number, itemId: number, data: object) =>
    api.put(`/api/romaneio/${id}/itens/${itemId}`, data),
  removerItem: (id: number, itemId: number) =>
    api.delete(`/api/romaneio/${id}/itens/${itemId}`),
  marcarEnviado: (id: number, canal: 'email' | 'whatsapp') =>
    api.post(`/api/romaneio/${id}/marcar-enviado?canal=${canal}`),
  copiar: (id: number) => api.post<{ id: number }>(`/api/romaneio/${id}/copiar`),
  exportar: (id: number) =>
    api.get(`/api/export/romaneio/${id}`, { responseType: 'blob' }),
}

export const boletosApi = {
  listar: (params?: { clienteId?: number; status?: string; mes?: number; ano?: number }) =>
    api.get<Boleto[]>('/api/boleto', { params }),
  pendentes: () => api.get('/api/boleto/pendentes'),
  vencendo: (dias?: number) => api.get('/api/boleto/vencendo', { params: { dias } }),
  criar: (data: object) => api.post<Boleto>('/api/boleto', data),
  pagar: (id: number, dataPagamento?: string) =>
    api.put(`/api/boleto/${id}/pagar`, { dataPagamento }),
  excluir: (id: number) => api.delete(`/api/boleto/${id}`),
}

export const financeiroApi = {
  listarLancamentos: (params?: { socio?: string; mes?: number; ano?: number }) =>
    api.get('/api/financeiro/lancamentos', { params }).then(r => comDados(r, d => {
      const l = comoObjeto<LancamentosResponse>(d, ['lancamentos'], 'lançamentos')
      l.resumo = comoObjeto(l.resumo ?? { entradas: 0, saidas: 0, saldo: 0 }, [], 'resumo')
      return l
    })),
  criarLancamento: (data: object) =>
    api.post('/api/financeiro/lancamentos', data),
  listarSaldos: (params?: { recebido?: boolean; empresa?: string }) =>
    api.get('/api/financeiro/saldos', { params }).then(r => comDados(r, d => comoObjeto<SaldosResponse>(d, ['saldos'], 'saldos'))),
  criarSaldo: (data: object) => api.post('/api/financeiro/saldos', data),
  receberSaldo: (id: number, data: object) =>
    api.put(`/api/financeiro/saldos/${id}/receber`, data),
}

export const exportApi = {
  cteGeral: (mes: number, ano: number) =>
    api.get(`/api/export/cte-geral?mes=${mes}&ano=${ano}`, { responseType: 'blob' }),
  ctePorPlaca: (mes: number, ano: number) =>
    api.get(`/api/export/cte-por-placa?mes=${mes}&ano=${ano}`, { responseType: 'blob' }),
  abastecimento: (mes: number, ano: number) =>
    api.get(`/api/export/abastecimento?mes=${mes}&ano=${ano}`, { responseType: 'blob' }),
  seguro: (mes: number, ano: number) =>
    api.get(`/api/export/seguro?mes=${mes}&ano=${ano}`, { responseType: 'blob' }),
}

// ── Auth ──────────────────────────────────────────────────────────
export interface UsuarioResumo {
  id: number
  nome: string
  email: string
  ativo: boolean
  dono: boolean
  trocarSenha: boolean
  /** '' = escritório; 'motorista' = só o painel do motorista */
  perfil?: string
}

// Quando o e-mail não sai, a senha provisória volta para o dono ver uma vez.
export interface SenhaProvisoria {
  emailEnviado: boolean
  avisoEmail: string | null
  senhaProvisoria: string | null
}

export const authApi = {
  login: (email: string, senha: string) =>
    api.post<{ token: string; usuario: UsuarioResumo }>('/api/auth/login', { email, senha }),
  me: () => api.get<UsuarioResumo>('/api/auth/me'),
  trocarSenha: (senhaAtual: string, novaSenha: string) =>
    api.post<{ token: string; usuario: UsuarioResumo }>('/api/auth/trocar-senha', { senhaAtual, novaSenha }),
  atualizarPerfil: (data: { nome: string; email: string; senhaAtual: string }) =>
    api.put<{ token: string; usuario: UsuarioResumo }>('/api/auth/perfil', data),
}

export const usuariosApi = {
  listar: () => api.get<UsuarioResumo[]>('/api/usuario'),
  criar: (data: { nome: string; email: string }) =>
    api.post<SenhaProvisoria>('/api/usuario', data),
  editar: (id: number, data: { nome: string; email: string }) =>
    api.put(`/api/usuario/${id}`, data),
  novaSenha: (id: number) => api.post<SenhaProvisoria>(`/api/usuario/${id}/nova-senha`, {}),
  desativar: (id: number) => api.delete(`/api/usuario/${id}`),
  reativar: (id: number) => api.put(`/api/usuario/${id}/reativar`, {}),
}

// ── Tipos de volumes ───────────────────────────────────────────────────────
// Como cada nota escreve cada tipo, e os conflitos que isso gera no romaneio.

export type CorVolume = 'BRANCO' | 'VERMELHO' | 'OUTRO'

export interface TipoVolume {
  id: number
  nome: string              // o nome que o sistema usa (linha do romaneio)
  nomeExtenso: string | null // só para leitura na tela
  cor: CorVolume
  naPlanilha: boolean      // existe linha com esse nome no romaneio
  par: string | null       // o mesmo tamanho na outra cor
  escritas: { id: number; texto: string }[]
}

export interface ProdutoAuditado {
  produto: string
  tipo: string | null
  emitente: string
  ultimaNF: string
  ultimaData: string | null
  vezes: number
  precoMin: number
  precoMax: number
  naPlanilha: boolean
}

export interface OcorrenciaTipo {
  nf: string
  produtoNota: string
  tipo: string
  quantidade: number
  preco: number
}

export interface ConflitoTipo {
  cliente: string
  tipo: string
  motivo: string
  sugestao: string | null
  sugestaoNF: string | null
  sugestaoProduto: string | null
  sugestaoPreco: number | null
  ocorrencias: OcorrenciaTipo[]
}

export interface CorrecaoTipo {
  nf: string
  produtoNota: string
  preco: number
  novoTipo: string
}

export const tiposVolumeApi = {
  listar: () => api.get('/api/tipos-volume').then(r => comDados(r, d => {
    const l = comoObjeto<{ tipos: TipoVolume[]; linhasRomaneio: string[] }>(d, ['tipos', 'linhasRomaneio'], 'tipos de volumes')
    l.tipos = l.tipos.map(t => comoObjeto<TipoVolume>(t, ['escritas']))
    return l
  })),
  criar: (data: { nome: string; cor: CorVolume; nomeExtenso?: string | null }) => api.post<{ id: number }>('/api/tipos-volume', data),
  editar: (id: number, data: { nome: string; cor: CorVolume; nomeExtenso?: string | null }) => api.put(`/api/tipos-volume/${id}`, data),
  excluir: (id: number) => api.delete(`/api/tipos-volume/${id}`),
  adicionarEscrita: (id: number, texto: string) => api.post(`/api/tipos-volume/${id}/escritas`, { texto }),
  excluirEscrita: (id: number) => api.delete(`/api/tipos-volume/escritas/${id}`),
  auditoria: () => api.get('/api/tipos-volume/auditoria').then(r => comDados(r, d => comoLista<ProdutoAuditado>(d, 'auditoria'))),
}

// O ASP.NET manda o nome do arquivo DUAS vezes no Content-Disposition:
//   filename=PRAT_O.XLSX          → versão só-ASCII, acento virou "_"
//   filename*=UTF-8''ESTRADA.XLSX  → o nome de verdade, percent-encoded
// Tem que ler o filename* primeiro, senão nome com acento chega estropiado.
export const nomeDoArquivo = (contentDisposition: string | undefined, padrao: string) => {
  if (!contentDisposition) return padrao
  const star = /filename\*=\s*UTF-8''([^;]+)/i.exec(contentDisposition)
  if (star) {
    try { return decodeURIComponent(star[1].trim()) } catch { /* malformado: usa o simples */ }
  }
  const simples = /filename=\s*"?([^";]+)"?/i.exec(contentDisposition)
  return simples ? simples[1].trim() : padrao
}

export const downloadBlob = (blob: Blob, filename: string) => {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

// ── Utilitários ────────────────────────────────────────────────────
export const socioColor = (socio: Socio) => ({
  Bento: { bg: 'bg-blue-50', text: 'text-blue-700', border: 'border-blue-200', dot: 'bg-blue-600' },
  Caio:   { bg: 'bg-green-50', text: 'text-green-700', border: 'border-green-200', dot: 'bg-green-600' },
  Dudu:  { bg: 'bg-orange-50', text: 'text-orange-700', border: 'border-orange-200', dot: 'bg-orange-600' },
}[socio])

export const formatBRL = (v: number) =>
  new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' }).format(v)

export const formatDate = (d: string) =>
  new Date(d).toLocaleDateString('pt-BR')

export const mesNome = (m: number) =>
  ['Jan','Fev','Mar','Abr','Mai','Jun','Jul','Ago','Set','Out','Nov','Dez'][m - 1] ?? ''

export default api
