import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Volumes embalados: controle do barracão (uma produtoB por controle).
// Tipos crus (caixa a granel) vêm em PRODUTOSC; produzidos, em caixas.

export type Quantidades = {
  ab: number; ab20: number; bb: number; av: number
  abe: number; ave: number; grPvcBr: number; mdPvcBr: number; grPvc20: number; grPvcVr: number
}
export type ChaveQuantidade = keyof Quantidades

export type LinhaVolumes = Quantidades & {
  id: number
  grupo: string
  nome: string | null
  placa: string | null
  data: string | null
  origem: string | null   // 'anotacao' = venda anotada junto com a produtoB
}

export const ORIGEM_ANOTACAO = 'anotacao'

export const PRODUTOSC_POR_CAIXA = 12

// Todos os tipos que o banco guarda. Bb e PVCMB (branco médio) saíram de uso em
// 30/09/2026: ficam aqui só para mostrar linha antiga que ainda tenha valor.
export const TODOS_CRUS: { chave: 'ab' | 'ab20' | 'bb' | 'av'; nome: string }[] = [
  { chave: 'ab',   nome: 'Ab' },
  { chave: 'ab20', nome: 'Ab-20' },
  { chave: 'bb',   nome: 'Bb' },
  { chave: 'av',   nome: 'Av' },
]

// Código do WhatsApp → nome da planilha. porCaixa: produtosC numa caixa do
// produzido (embalado 12; PVC 10 — VOLUME-03).
export const TODOS_PRODUZIDOS: {
  chave: 'abe' | 'ave' | 'grPvcBr' | 'mdPvcBr' | 'grPvc20' | 'grPvcVr'
  codigo: string; nome: string; descricao: string; pvc: boolean; porCaixa: number
}[] = [
  { chave: 'abe',     codigo: 'AB',      nome: 'Abe',      descricao: 'Embalado branco',   pvc: false, porCaixa: 12 },
  { chave: 'ave',     codigo: 'AVE',     nome: 'Ave',      descricao: 'Embalado vermelho', pvc: false, porCaixa: 12 },
  { chave: 'grPvcBr', codigo: 'PVCAB',   nome: 'Gr-PvcBr', descricao: 'PVC grande branco', pvc: true,  porCaixa: 10 },
  { chave: 'mdPvcBr', codigo: 'PVCMB',   nome: 'Md-PvcBr', descricao: 'PVC médio branco',  pvc: true,  porCaixa: 10 },
  { chave: 'grPvc20', codigo: 'PVCAB20', nome: 'GrPvc-20', descricao: 'PVC de 20 branco',  pvc: true,  porCaixa: 10 },
  { chave: 'grPvcVr', codigo: 'PVCV',    nome: 'Gr PvcVr', descricao: 'PVC vermelho',      pvc: true,  porCaixa: 10 },
]

// Fora de uso desde 30/09/2026 (Admin): branco médio (Bb e PVCMB).
const FORA_DE_USO = new Set<string>(['bb', 'mdPvcBr'])
// Os que as telas oferecem para digitar
export const TIPOS_CRUS = TODOS_CRUS.filter(t => !FORA_DE_USO.has(t.chave))
export const TIPOS_PRODUZIDOS = TODOS_PRODUZIDOS.filter(t => !FORA_DE_USO.has(t.chave))

export const vazio = (): Quantidades => ({
  ab: 0, ab20: 0, bb: 0, av: 0, abe: 0, ave: 0, grPvcBr: 0, mdPvcBr: 0, grPvc20: 0, grPvcVr: 0,
})

export type Status = 'bate' | 'nao_bate' | 'incompleto'

export type ItemLista = {
  id: number
  numero: number
  data: string
  status: Status
  embalou: { codigo: string; caixas: number }[]
  diferencas: { nome: string; valor: number; emProdutosC: boolean }[]
  entradas: number
  saidas: number
}

export type LinhaCru = {
  chave: string; nome: string; entradas: number; sobraAnterior: number; pvcVinteEntrada: number
  consumoProduzidos: number; usadoPvcVinte: number; saidasCruas: number; sobra: number
  quebra: number; estoque: number; bate: boolean
}

export type LinhaProduzido = {
  chave: string; nome: string; codigo: string; embalou: number; saidas: number; estoque: number; bate: boolean
}

export type Conferencia = {
  crus: LinhaCru[]
  produzidos: LinhaProduzido[]
  produtosB: { unidadesAmarelas: number; unidadesBrancas: number }
  status: Status
  avisos: string[]
}

export type Controle = {
  id: number
  numero: number
  data: string
  observacao: string | null
  pvcVinteComCaixaAb: number
  sobraAnteriorManual: boolean
  origem: string
  embalou: LinhaVolumes | null
  sobra: LinhaVolumes | null
  quebra: LinhaVolumes | null
  sobraAnterior: SobraAnterior
  entradas: LinhaVolumes[]
  saidas: LinhaVolumes[]
  conferencia: Conferencia
  anteriorId: number | null
  proximoId: number | null
}

export type SobraAnterior = { valores: LinhaVolumes; doControle: number | null; doControleData: string | null }

export type VendaEnvio = { id?: number; nome: string; data: string | null; quantidades: Partial<Quantidades> }

export type ControleEnvio = {
  numero?: number
  data?: string
  embalou?: Partial<Quantidades>
  pvcVinteComCaixaAb?: number
  sobra?: Partial<Quantidades>
  quebra?: Partial<Quantidades>
  observacao?: string
  sobraAnteriorManual?: boolean
  sobraAnterior?: Partial<Quantidades>
  // Vendas da anotação: a lista inteira (a API cria, atualiza e apaga as que sumiram).
  vendas?: VendaEnvio[]
}

export type LinhaEnvio = {
  grupo: 'entrada' | 'saida'
  nome: string
  placa: string
  data: string | null
  quantidades: Partial<Quantidades>
}

export type Sugestoes = { clientes: string[]; fornecedores: string[]; placas: string[] }

// Romaneio da fazenda SEM funrural (volumes que pegam na fazenda para embalar), lido
// do PDF guardado no acerto Alfa. Vira a entrada "PAULO" de um controle, na
// linha com origem "romaneio:NUMERO".
export type SituacaoRomaneio = 'lancado' | 'aguardando' | 'retirado' | 'historico' | 'sem_tipo'

export type RomaneioFazenda = {
  numero: number
  data: string
  caixas: number
  itens: { produto: string; descricao: string; caixas: number; cru: string | null; cruNome: string | null; produtosC: number }[]
  avisos: string[]
  situacao: SituacaoRomaneio
  controleId: number | null
  controleNumero: number | null
  controleData: string | null
  linhaId: number | null
  acertoId: number | null
  acertoNumero: number | null
}

export const PREFIXO_ORIGEM_ROMANEIO = 'romaneio:'

/** "romaneio:133423" → 133423; outra origem → null. */
export const romaneioDaOrigem = (origem: string | null) =>
  origem?.startsWith(PREFIXO_ORIGEM_ROMANEIO) ? Number(origem.slice(PREFIXO_ORIGEM_ROMANEIO.length)) || null : null

/** "170 caixas de vermelho" (só o que vira caixa para embalar). */
export const descreverRomaneio = (r: RomaneioFazenda) =>
  r.itens.filter(i => i.cru).map(i => `${i.caixas.toLocaleString('pt-BR')} ${i.caixas === 1 ? 'caixa' : 'caixas'} de ${(i.cruNome ?? '').toLowerCase()}`).join(' e ')

export const volumesApi = {
  lista: () => api.get('/api/volumes-embalados')
    .then(r => comoObjeto<{ proximoNumero: number; controles: ItemLista[] }>(r.data, ['controles'], 'controles de volumes')),
  detalhe: (id: number) => api.get(`/api/volumes-embalados/${id}`).then(r => {
    const c = comoObjeto<Controle>(r.data, ['entradas', 'saidas'], 'controle de volumes')
    c.conferencia = comoObjeto<Conferencia>(c.conferencia ?? {}, ['crus', 'produzidos', 'avisos'], 'conferência')
    return c
  }),
  anotar: (dto: ControleEnvio) =>
    api.post<{ id: number; numero: number; romaneios?: number }>('/api/volumes-embalados', dto).then(r => r.data),
  atualizar: (id: number, dto: ControleEnvio) => api.put(`/api/volumes-embalados/${id}`, dto),
  excluir: (id: number) => api.delete(`/api/volumes-embalados/${id}`),
  criarLinha: (id: number, dto: LinhaEnvio) => api.post(`/api/volumes-embalados/${id}/linhas`, dto),
  atualizarLinha: (linhaId: number, dto: LinhaEnvio) => api.put(`/api/volumes-embalados/linhas/${linhaId}`, dto),
  excluirLinha: (linhaId: number) => api.delete(`/api/volumes-embalados/linhas/${linhaId}`),
  sobraParaNovo: (numero: number) =>
    api.get('/api/volumes-embalados/sobra-anterior', { params: { numero } }).then(r => comoObjeto<SobraAnterior>(r.data, [], 'sobra anterior')),
  sugestoes: () => api.get('/api/volumes-embalados/sugestoes')
    .then(r => comoObjeto<Sugestoes>(r.data, ['clientes', 'fornecedores', 'placas'], 'sugestões')),
  romaneiosFazenda: () => api.get('/api/volumes-embalados/romaneios-fazenda')
    .then(r => comoLista<RomaneioFazenda>(r.data, 'romaneios da fazenda').map(x => comoObjeto<RomaneioFazenda>(x, ['itens', 'avisos']))),
  // Põe (ou troca) o romaneio da fazenda num controle, à mão
  moverRomaneio: (numero: number, controleId: number) =>
    api.put(`/api/volumes-embalados/romaneios-fazenda/${numero}/controle`, { controleId }),
  // Tira dos volumes: a entrada sai e não volta sozinha
  retirarRomaneio: (numero: number) => api.delete(`/api/volumes-embalados/romaneios-fazenda/${numero}/controle`),
}

// ── Formatação ────────────────────────────────────────────────────────────

const fmt = (v: number, casas = 2) =>
  v.toLocaleString('pt-BR', { minimumFractionDigits: 0, maximumFractionDigits: casas })

/** ProdutosC → "15 caixas e 7 produtosC". Arredonda para 0,5 produtoC; negativo leva o sinal na frente; zero é "0". */
export function caixasEProdutosC(produtosC: number): string {
  const total = Math.round(Math.abs(produtosC) * 2) / 2
  if (total === 0) return '0'
  const cx = Math.trunc(total / PRODUTOSC_POR_CAIXA)
  const resto = total - cx * PRODUTOSC_POR_CAIXA
  const partes: string[] = []
  if (cx > 0) partes.push(`${fmt(cx)} ${cx === 1 ? 'caixa' : 'caixas'}`)
  if (resto > 0) partes.push(`${fmt(resto, 1)} ${resto === 1 ? 'produtoC' : 'produtosC'}`)
  return (produtosC < 0 ? '−' : '') + partes.join(' e ')
}

/** ProdutosC → "2 caixas e 3 produtosC" com caixa de `porCaixa` produtosC (PVC = 10). */
export function caixasEProdutosCDe(produtosC: number, porCaixa: number): string {
  const total = Math.round(Math.abs(produtosC) * 2) / 2
  if (total === 0) return '0'
  const cx = Math.trunc(total / porCaixa)
  const resto = total - cx * porCaixa
  const partes: string[] = []
  if (cx > 0) partes.push(`${fmt(cx)} ${cx === 1 ? 'caixa' : 'caixas'}`)
  if (resto > 0) partes.push(`${fmt(resto, 1)} ${resto === 1 ? 'produtoC' : 'produtosC'}`)
  return (produtosC < 0 ? '−' : '') + partes.join(' e ')
}

/** Caixas de produzido. Inteiro: "3 caixas"; com fração (venda de produtoC): "2 caixas e 3 produtosC". */
export function caixasProduzido(v: number, porCaixa: number): string {
  if (Math.abs(v - Math.round(v)) < 0.001) return caixas(Math.round(v))
  return caixasEProdutosCDe(v * porCaixa, porCaixa)
}

export const caixas = (v: number) => `${fmt(v)} ${Math.abs(v) === 1 ? 'caixa' : 'caixas'}`

/** Unidades de produtoB → "21 pacotes e 30 unidades" (pacote de 150). */
export function pacotes(unidades: number): string {
  const p = Math.floor(unidades / 150)
  const u = Math.round(unidades - p * 150)
  const partes: string[] = []
  if (p > 0 || u === 0) partes.push(`${p} ${p === 1 ? 'pacote' : 'pacotes'}`)
  if (u > 0) partes.push(`${u} unidades`)
  return partes.join(' e ')
}
