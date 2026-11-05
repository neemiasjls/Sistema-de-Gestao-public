import { useConfirmar } from '../components/ui/Confirmar'
import Botao, { BotaoIcone, classesBotao } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Carregando, ErroCarga } from '../components/ui/Estados'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Copy, Check, Minus, Plus, Pencil, Trash2, CloudCheck, History, RotateCcw } from 'lucide-react'
import toast from 'react-hot-toast'
import { lerNum, num } from '../utils/formato'
import {
  volumesApi, vazio, caixasEProdutosC, caixasEProdutosCDe, ORIGEM_ANOTACAO,
  type Controle, type Quantidades, type ChaveQuantidade, type LinhaVolumes, type SobraAnterior, type VendaEnvio, type RomaneioFazenda,
} from '../services/volumesEmbaladosApi'
import { Volume, CartaoSobraAnterior } from '../components/VolumesPecas'
import { LinhaRomaneio } from '../components/RomaneiosEmbalar'

// Anotação do dia de produtoB — o que hoje vai no WhatsApp. Feita para o
// celular, no barracão: escolhe o tipo de volume, a forma (embalado ou PVC),
// digita as caixas e adiciona na lista. Salvar sempre à mão no rodapé.
// Com :id na rota, edita a anotação de um controle que já existe.
//
// A tela guarda uma lista de combinações; na hora de salvar ela vira os mesmos
// campos de sempre (Abe, Ave, GrPvcBr, GrPvc20, GrPvcVr e o PVC de 20 feito
// com caixa de 30), ver VOLUME-03, VOLUME-06 e VOLUME-10. Sobras e quebras em caixas e
// produtosC (guardadas em produtosC); vendas avulsas viram saídas do controle
// (VOLUME-11). Branco médio (Bb, PVCMB) saiu de uso em 30/09/2026.

// ── Tipos, formas e combinações válidas ──────────────────────────────────

type Tipo = 'branco' | 'vermelho'
type Forma = 'embalado' | 'pvc' | 'pvc20'
type FormaVenda = Forma | 'fazenda'
type Origem = 'cx20' | 'cx30'

// Combinação anotada. PVC de 20 se divide pela caixa de origem: os dois somam
// no GrPvc20 e o de caixa de 30 também vai para pvcVinteComCaixaAb.
type Combo = 'abe' | 'ave' | 'grPvcBr' | 'grPvc20' | 'grPvc20Cx30' | 'grPvcVr'

const TIPOS: { id: Tipo; nome: string }[] = [
  { id: 'branco',   nome: 'Branco' },
  { id: 'vermelho', nome: 'Vermelho' },
]

const FORMAS: { id: Forma; nome: string }[] = [
  { id: 'embalado', nome: 'Embalado' },
  { id: 'pvc',      nome: 'PVC' },
  { id: 'pvc20',    nome: 'PVC de 20' },
]

// Só estas existem (VOLUME-10)
const COMBINACOES: Record<Tipo, Partial<Record<Forma, Combo>>> = {
  branco:   { embalado: 'abe', pvc: 'grPvcBr', pvc20: 'grPvc20' },
  vermelho: { embalado: 'ave', pvc: 'grPvcVr' },
}

const INFO: Record<Combo, { codigo: string; nome: string; tipo: Tipo; pvc: boolean }> = {
  abe:         { codigo: 'AB',      nome: 'Embalado branco',                 tipo: 'branco',   pvc: false },
  ave:         { codigo: 'AVE',     nome: 'Embalado vermelho',               tipo: 'vermelho', pvc: false },
  grPvcBr:     { codigo: 'PVCAB',   nome: 'PVC branco',                      tipo: 'branco',   pvc: true },
  grPvc20:     { codigo: 'PVCAB20', nome: 'PVC de 20 com caixa de 20',       tipo: 'branco',   pvc: true },
  grPvc20Cx30: { codigo: 'PVCAB20', nome: 'PVC de 20 com caixa de 30',       tipo: 'branco',   pvc: true },
  grPvcVr:     { codigo: 'PVCV',    nome: 'PVC vermelho',                    tipo: 'vermelho', pvc: true },
}

// Ordem da mensagem do WhatsApp
const ORDEM: Combo[] = ['abe', 'ave', 'grPvcBr', 'grPvc20', 'grPvc20Cx30', 'grPvcVr']

const comboDe = (tipo: Tipo | null, forma: Forma | null, origem: Origem): Combo | null => {
  if (!tipo || !forma) return null
  const c = COMBINACOES[tipo][forma]
  if (!c) return null
  return c === 'grPvc20' && origem === 'cx30' ? 'grPvc20Cx30' : c
}

// ── Vendas (saídas avulsas, sem preço) ───────────────────────────────────
// Cada tipo × forma cai numa coluna do controle. porCaixa: produtosC numa caixa
// daquela forma — embalado 12, PVC 10 (inferido, ver PEN-16), fazenda 12. Fazenda
// = volume como veio da fazenda, sem plástico nem produtoB: guarda em produtosC.

type ColunaVenda = { chave: ChaveQuantidade; porCaixa: number; fazenda: boolean }
const VENDA_COLUNA: Record<Tipo, Partial<Record<FormaVenda, ColunaVenda>>> = {
  branco: {
    embalado: { chave: 'abe',     porCaixa: 12, fazenda: false },
    pvc:      { chave: 'grPvcBr', porCaixa: 10, fazenda: false },
    pvc20:    { chave: 'grPvc20', porCaixa: 10, fazenda: false },
    fazenda:   { chave: 'ab',      porCaixa: 12, fazenda: true },
  },
  vermelho: {
    embalado: { chave: 'ave',     porCaixa: 12, fazenda: false },
    pvc:      { chave: 'grPvcVr', porCaixa: 10, fazenda: false },
    fazenda:   { chave: 'av',      porCaixa: 12, fazenda: true },
  },
}
const FORMAS_VENDA: { id: FormaVenda; nome: string; nota: string }[] = [
  { id: 'embalado', nome: 'Embalado',  nota: 'caixa de 12' },
  { id: 'pvc',      nome: 'PVC',       nota: 'caixa de 10' },
  { id: 'pvc20',    nome: 'PVC de 20', nota: 'caixa de 10' },
  { id: 'fazenda',   nome: 'Fazenda',    nota: 'sem produtoB' },
]
const NOME_FORMA: Record<FormaVenda, string> = { embalado: 'embalado', pvc: 'PVC', pvc20: 'PVC de 20', fazenda: 'fazenda' }

type Venda = {
  chave: number             // só da tela
  id?: number               // linha de saída já gravada
  tipo: Tipo
  forma: FormaVenda
  produtosC: number
  nome: string
  data: string | null       // null = dia da anotação
  extras?: Partial<Quantidades>  // colunas que a tela não conhece (lançadas na aba Saídas): mantidas
}

let proximaChave = 1

const quantidadesVenda = (v: Venda): Partial<Quantidades> => {
  const col = VENDA_COLUNA[v.tipo][v.forma]!
  const valor = col.fazenda ? v.produtosC : Math.round((v.produtosC / col.porCaixa) * 10000) / 10000
  return { ...(v.extras ?? {}), [col.chave]: valor }
}

// Linha de saída gravada → vendas da tela (uma por coluna com valor).
function vendasDaLinha(l: LinhaVolumes, dia: string): Venda[] {
  const r: Venda[] = []
  const conhecidas = new Set<string>()
  ;(Object.keys(VENDA_COLUNA) as Tipo[]).forEach(tipo => {
    (Object.entries(VENDA_COLUNA[tipo]) as [FormaVenda, ColunaVenda][]).forEach(([forma, col]) => {
      conhecidas.add(col.chave)
      const v = l[col.chave] ?? 0
      if (!v) return
      const produtosC = col.fazenda ? v : Math.round(v * col.porCaixa * 2) / 2
      r.push({
        chave: proximaChave++, id: r.length === 0 ? l.id : undefined, tipo, forma, produtosC,
        nome: l.nome ?? '', data: l.data && l.data !== dia ? l.data : null,
      })
    })
  })
  const extras: Partial<Quantidades> = {}
  ;(Object.keys(vazio()) as ChaveQuantidade[]).forEach(k => { if (!conhecidas.has(k) && l[k]) extras[k] = l[k] })
  if (Object.keys(extras).length > 0) {
    if (r.length > 0) r[0].extras = extras
  }
  return r
}

// ── Sobras e quebras: crus Ab e Av, em caixas + produtosC, guardados em produtosC

type Grupo = 'sobra' | 'quebra'
type Cru = 'ab' | 'av'
type ChaveCru = `${Grupo}.${Cru}`
const CRUS: { id: Cru; nome: string; tipo: Tipo }[] = [
  { id: 'ab', nome: 'Branco', tipo: 'branco' },
  { id: 'av', nome: 'Vermelho', tipo: 'vermelho' },
]
const PRODUTOSC_CAIXA = 12

type Itens = Partial<Record<Combo, number>>
type Crus = Partial<Record<ChaveCru, number>>

// Dia de hoje no horário do celular (o toISOString vira o dia às 21h)
const hojeLocal = () => {
  const d = new Date()
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 10)
}

const fmtNum = (v: number) => num(v, v % 1 ? 1 : 0)
const caixasTxt = (v: number) => `${fmtNum(v)} ${v === 1 ? 'caixa' : 'caixas'}`
const produtosCTxt = (v: number) => `${fmtNum(v)} ${v === 1 ? 'produtoC' : 'produtosC'}`
const paraTexto = (v: number) => (v ? String(v).replace('.', ',') : '')
const diaMes = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`
const capitalizar = (s: string) => s.toLowerCase().replace(/(^|\s)\S/g, x => x.toUpperCase())

// ProdutosC → "1 cx e 2 band" (texto do WhatsApp, que já usava "band")
const qtdZap = (produtosC: number, porCaixa: number) => {
  const cx = Math.trunc(produtosC / porCaixa)
  const resto = Math.round((produtosC - cx * porCaixa) * 2) / 2
  const partes: string[] = []
  if (cx > 0) partes.push(`${num(cx, 0)} cx`)
  if (resto > 0) partes.push(`${num(resto, resto % 1 ? 1 : 0)} band`)
  return partes.join(' e ')
}

// Lista → campos que a API recebe
function montar(itens: Itens, crus: Crus) {
  const v = (c: Combo) => itens[c] ?? 0
  const embalou: Partial<Quantidades> = {
    abe: v('abe'), ave: v('ave'), grPvcBr: v('grPvcBr'),
    grPvc20: v('grPvc20') + v('grPvc20Cx30'), grPvcVr: v('grPvcVr'),
  }
  return {
    embalou,
    pvcVinte: v('grPvc20Cx30'),
    sobra: { ab: crus['sobra.ab'] ?? 0, av: crus['sobra.av'] ?? 0 },
    quebra: { ab: crus['quebra.ab'] ?? 0, av: crus['quebra.av'] ?? 0 },
  }
}

// ── Rascunho no aparelho (D-17) ──────────────────────────────────────────
// Enquanto digita, a anotação fica guardada neste navegador. Fechou sem querer
// (bateria, aba, voltar do celular)? Ao abrir de novo ela volta. Some ao salvar.
// Anotação nova: volta sozinha, com aviso e "Começar do zero". Edição de controle
// que já existe: pergunta antes (o controle pode ter mudado por outro caminho).

type Formularios = {
  tipo: Tipo | null; forma: Forma | null; origem: Origem; qtd: string
  grupo: Grupo; cru: Cru | null; cruCx: string; band: string
  vTipo: Tipo | null; vForma: FormaVenda | null; vCx: string; vBand: string; vNome: string; vData: string | null
}
type EstadoAnotacao = {
  data: string; numero: string; itens: Itens; crus: Crus; vendas: Venda[]; observacao: string; form: Formularios
}
type Rascunho = { versao: 1; salvoEm: string; estado: EstadoAnotacao }

const FORM_VAZIO: Formularios = {
  tipo: null, forma: null, origem: 'cx20', qtd: '', grupo: 'sobra', cru: null, cruCx: '', band: '',
  vTipo: null, vForma: null, vCx: '', vBand: '', vNome: '', vData: null,
}
const RASCUNHO_VALIDADE = 14 * 86400000
const chaveRascunho = (id: number | null) => `trx_rascunho_anotar_${id ?? 'novo'}`

// Zeros não contam: adicionar e remover volta ao "igual ao começo"
const semZeros = <T extends Record<string, number | undefined>>(o: T) =>
  Object.fromEntries(Object.entries(o).filter(([, v]) => v))
const assinatura = (e: EstadoAnotacao) => JSON.stringify({
  ...e, itens: semZeros(e.itens), crus: semZeros(e.crus),
  vendas: e.vendas.map(({ chave: _chave, ...v }) => v),
  form: { ...e.form, tipo: e.form.qtd ? e.form.tipo : null, forma: e.form.qtd ? e.form.forma : null,
          cru: e.form.cruCx || e.form.band ? e.form.cru : null, grupo: 'sobra', origem: 'cx20',
          vTipo: e.form.vCx || e.form.vBand ? e.form.vTipo : null, vForma: e.form.vCx || e.form.vBand ? e.form.vForma : null },
})

function lerRascunho(id: number | null): Rascunho | null {
  try {
    const bruto = localStorage.getItem(chaveRascunho(id))
    if (!bruto) return null
    const r = JSON.parse(bruto) as Rascunho
    if (r?.versao !== 1 || !r.estado || !Array.isArray(r.estado.vendas)) return null
    if (Date.now() - new Date(r.salvoEm).getTime() > RASCUNHO_VALIDADE) { apagarRascunho(id); return null }
    return r
  } catch {
    return null
  }
}
function gravarRascunho(id: number | null, estado: EstadoAnotacao): string | null {
  try {
    const salvoEm = new Date().toISOString()
    localStorage.setItem(chaveRascunho(id), JSON.stringify({ versao: 1, salvoEm, estado } satisfies Rascunho))
    return salvoEm
  } catch {
    return null   // aba anônima, armazenamento cheio: segue sem rascunho
  }
}
function apagarRascunho(id: number | null) {
  try { localStorage.removeItem(chaveRascunho(id)) } catch { /* sem armazenamento */ }
}
const quando = (iso: string) => {
  const d = new Date(iso)
  const hora = d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
  return d.toDateString() === new Date().toDateString()
    ? `hoje às ${hora}` : `${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${hora}`
}

// ── Página ────────────────────────────────────────────────────────────────

export default function VolumesAnotarPage() {
  const { id } = useParams()
  const editando = id ? Number(id) : null
  const navigate = useNavigate()

  const [original, setOriginal] = useState<Controle | null>(null)
  const [numero, setNumero] = useState('')
  const [data, setData] = useState(hojeLocal())
  const [itens, setItens] = useState<Itens>({})
  const [crus, setCrus] = useState<Crus>({})
  const [vendas, setVendas] = useState<Venda[]>([])
  const [observacao, setObservacao] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [copiado, setCopiado] = useState(false)
  const [carregando, setCarregando] = useState(true)
  // Edição que não carregou: nada de formulário vazio (salvar zeraria o controle)
  const [falhouCarga, setFalhouCarga] = useState(false)
  const [tentativa, setTentativa] = useState(0)
  const [nomes, setNomes] = useState<string[]>([])

  // Sobra do controle anterior (cartão do topo)
  const [sobraAnt, setSobraAnt] = useState<SobraAnterior | null>(null)
  const [buscandoSobra, setBuscandoSobra] = useState(!editando)

  // Formulário "o que embalou"
  const [tipo, setTipo] = useState<Tipo | null>(null)
  const [forma, setForma] = useState<Forma | null>(null)
  const [origem, setOrigem] = useState<Origem>('cx20')
  const [qtd, setQtd] = useState('')
  const [corrigindo, setCorrigindo] = useState<Combo | null>(null)
  const formRef = useRef<HTMLDivElement>(null)
  const qtdRef = useRef<HTMLInputElement>(null)

  // Formulário "sobras e quebras"
  const [grupo, setGrupo] = useState<Grupo>('sobra')
  const [cru, setCru] = useState<Cru | null>(null)
  const [cruCx, setCruCx] = useState('')
  const [band, setBand] = useState('')
  const [corrigindoCru, setCorrigindoCru] = useState<ChaveCru | null>(null)
  const cruRef = useRef<HTMLDivElement>(null)
  const cruCxRef = useRef<HTMLInputElement>(null)
  const bandRef = useRef<HTMLInputElement>(null)

  // Formulário "vendas"
  const [vTipo, setVTipo] = useState<Tipo | null>(null)
  const [vForma, setVForma] = useState<FormaVenda | null>(null)
  const [vCx, setVCx] = useState('')
  const [vBand, setVBand] = useState('')
  const [vNome, setVNome] = useState('')
  const [vData, setVData] = useState<string | null>(null)
  const [corrigindoVenda, setCorrigindoVenda] = useState<number | null>(null)
  const vendaRef = useRef<HTMLDivElement>(null)
  const vCxRef = useRef<HTMLInputElement>(null)
  const vBandRef = useRef<HTMLInputElement>(null)
  const vNomeRef = useRef<HTMLInputElement>(null)
  const confirmar = useConfirmar()

  // Rascunho: `base` é a anotação como abriu (sem nada digitado); diferente dela, guarda
  const base = useRef<string | null>(null)
  const [pendente, setPendente] = useState<Rascunho | null>(null)    // edição: espera ele decidir
  const [recuperado, setRecuperado] = useState<string | null>(null)  // nova: voltou sozinho
  const [guardadoEm, setGuardadoEm] = useState<string | null>(null)

  const aplicar = (e: EstadoAnotacao, comNumero: boolean) => {
    setData(e.data)
    if (comNumero && e.numero) setNumero(e.numero)
    setItens(e.itens ?? {})
    setCrus(e.crus ?? {})
    const vs = (e.vendas ?? []).map(v => ({ ...v, chave: proximaChave++ }))
    setVendas(vs)
    setObservacao(e.observacao ?? '')
    const f = { ...FORM_VAZIO, ...e.form }
    setTipo(f.tipo); setForma(f.forma); setOrigem(f.origem); setQtd(f.qtd)
    setGrupo(f.grupo); setCru(f.cru); setCruCx(f.cruCx); setBand(f.band)
    setVTipo(f.vTipo); setVForma(f.vForma); setVCx(f.vCx); setVBand(f.vBand); setVNome(f.vNome); setVData(f.vData)
  }

  useEffect(() => {
    volumesApi.sugestoes().then(s => setNomes(s.clientes)).catch(() => {})
    setCarregando(true)
    setFalhouCarga(false)
    base.current = null
    if (editando) {
      volumesApi.detalhe(editando).then(c => {
        setOriginal(c)
        setNumero(String(c.numero))
        setData(c.data)
        const e = c.embalou
        const cx30 = c.pvcVinteComCaixaAb ?? 0
        const its: Itens = {
          abe: e?.abe ?? 0, ave: e?.ave ?? 0, grPvcBr: e?.grPvcBr ?? 0,
          grPvc20: Math.max(0, (e?.grPvc20 ?? 0) - cx30), grPvc20Cx30: cx30, grPvcVr: e?.grPvcVr ?? 0,
        }
        const crs: Crus = {
          'sobra.ab': c.sobra?.ab ?? 0, 'sobra.av': c.sobra?.av ?? 0,
          'quebra.ab': c.quebra?.ab ?? 0, 'quebra.av': c.quebra?.av ?? 0,
        }
        const vs = c.saidas.filter(s => s.origem === ORIGEM_ANOTACAO).flatMap(s => vendasDaLinha(s, c.data))
        setItens(its)
        setCrus(crs)
        setVendas(vs)
        setObservacao(c.observacao ?? '')
        setSobraAnt(c.sobraAnterior)
        base.current = assinatura({ data: c.data, numero: String(c.numero), itens: its, crus: crs, vendas: vs, observacao: c.observacao ?? '', form: FORM_VAZIO })
        const r = lerRascunho(editando)
        if (r && assinatura(r.estado) !== base.current) setPendente(r)
        else if (r) apagarRascunho(editando)
        setCarregando(false)
      }).catch(() => { setFalhouCarga(true); setCarregando(false) })
    } else {
      base.current = assinatura({ data: hojeLocal(), numero: '', itens: {}, crus: {}, vendas: [], observacao: '', form: FORM_VAZIO })
      const r = lerRascunho(null)
      if (r && assinatura({ ...r.estado, numero: '' }) !== base.current) {
        aplicar(r.estado, false)
        setRecuperado(r.salvoEm)
        setGuardadoEm(r.salvoEm)
      }
      volumesApi.lista().then(r => setNumero(String(r.proximoNumero)))
        .catch(() => toast.error('Não consegui buscar o próximo número. Confira o número do controle.'))
        .finally(() => setCarregando(false))
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editando, tentativa])

  // Controle novo: a sobra anterior é a do último controle antes deste número.
  const numeroInt = Math.round(lerNum(numero))
  // Romaneios da fazenda sem funrural aguardando controle: os da janela deste dia
  // (romaneio até 3 dias antes da produtoB) entram sozinhos quando salvar
  const [romaneiosAguardando, setRomaneiosAguardando] = useState<RomaneioFazenda[]>([])
  useEffect(() => {
    if (editando) return
    volumesApi.romaneiosFazenda().then(l => setRomaneiosAguardando(l.filter(r => r.situacao === 'aguardando'))).catch(() => {})
  }, [editando])
  const romaneiosDoDia = romaneiosAguardando.filter(r => {
    const dias = (new Date(data + 'T00:00:00').getTime() - new Date(r.data + 'T00:00:00').getTime()) / 86400000
    return dias >= 0 && dias <= 3
  })

  useEffect(() => {
    if (editando || !numeroInt) return
    setBuscandoSobra(true)
    const t = setTimeout(() => {
      volumesApi.sobraParaNovo(numeroInt).then(setSobraAnt)
        .catch(() => setSobraAnt(null))
        .finally(() => setBuscandoSobra(false))
    }, 350)
    return () => clearTimeout(t)
  }, [editando, numeroInt])

  const combo = comboDe(tipo, forma, origem)
  const qtdNum = lerNum(qtd)
  const jaTem = combo && !corrigindo ? (itens[combo] ?? 0) : 0

  const chaveCru: ChaveCru | null = cru ? `${grupo}.${cru}` : null
  const bandNum = Math.round(lerNum(cruCx)) * PRODUTOSC_CAIXA + lerNum(band)
  const jaTemCru = chaveCru && !corrigindoCru ? (crus[chaveCru] ?? 0) : 0

  const colVenda = vTipo && vForma ? VENDA_COLUNA[vTipo][vForma] ?? null : null
  const vPorCaixa = colVenda?.porCaixa ?? PRODUTOSC_CAIXA
  const vTotal = Math.round(lerNum(vCx)) * vPorCaixa + lerNum(vBand)

  const valores = useMemo(() => montar(itens, crus), [itens, crus])
  const totalEmbalado = Object.values(valores.embalou).reduce((s, v) => s + (v ?? 0), 0)

  // O que o controle já tem e esta tela não mostra (Ab-20, Bb, PVCMB): avisa,
  // em vez de deixar escondido (UI-04). Fica como está ao salvar.
  const outros = useMemo(() => {
    const l: string[] = []
    const n = { ab20: 'Ab-20', bb: 'Bb' } as const
    ;(['sobra', 'quebra'] as const).forEach(g => {
      ;(['ab20', 'bb'] as const).forEach(k => {
        const v = original?.[g]?.[k] ?? 0
        if (v) l.push(`${g === 'sobra' ? 'sobra' : 'quebra'} de ${n[k]}: ${caixasEProdutosC(v)}`)
      })
    })
    const md = original?.embalou?.mdPvcBr ?? 0
    if (md) l.push(`PVCMB embalado: ${caixasTxt(md)}`)
    return l
  }, [original])

  // Guarda a cada mudança. Na edição com rascunho antigo esperando decisão, não
  // grava por cima dele.
  const estadoAtual: EstadoAnotacao = {
    data, numero: editando ? numero : '', itens, crus, vendas, observacao,
    form: { tipo, forma, origem, qtd, grupo, cru, cruCx, band, vTipo, vForma, vCx, vBand, vNome, vData },
  }
  const assinaturaAtual = assinatura(estadoAtual)
  const salvoComSucesso = useRef(false)
  useEffect(() => {
    if (carregando || falhouCarga || pendente || base.current === null || salvoComSucesso.current) return
    if (assinaturaAtual === base.current) { apagarRascunho(editando); setGuardadoEm(null); return }
    setGuardadoEm(gravarRascunho(editando, estadoAtual))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [assinaturaAtual, carregando, falhouCarga, pendente, editando])

  const recuperarPendente = () => {
    if (!pendente) return
    aplicar(pendente.estado, true)
    setGuardadoEm(pendente.salvoEm)
    setPendente(null)
    toast.success('Rascunho recuperado.')
  }
  const descartarPendente = () => { apagarRascunho(editando); setPendente(null) }
  const comecarDoZero = async () => {
    if (!await confirmar({ titulo: 'Começar do zero?', texto: 'O rascunho guardado neste aparelho vai ser apagado.', confirmar: 'Apagar rascunho' })) return
    const n = numero
    aplicar({ data: hojeLocal(), numero: '', itens: {}, crus: {}, vendas: [], observacao: '', form: FORM_VAZIO }, false)
    setNumero(n)
    setCorrigindo(null); setCorrigindoCru(null); setCorrigindoVenda(null)
    apagarRascunho(editando)
    setRecuperado(null)
    setGuardadoEm(null)
  }

  // ── Ações da lista de embalados ─────────────────────────────────────────

  const escolherTipo = (t: Tipo) => {
    setTipo(t)
    // Forma que não existe para o tipo sai; se só há uma, já vem marcada
    const validas = Object.keys(COMBINACOES[t]) as Forma[]
    setForma(f => (f && validas.includes(f) ? f : validas.length === 1 ? validas[0] : null))
  }

  const limparForm = () => {
    setForma(null)
    setOrigem('cx20')
    setQtd('')
    setCorrigindo(null)
  }

  const adicionar = () => {
    if (!combo) return
    if (qtdNum <= 0 && !corrigindo) { qtdRef.current?.focus(); return }
    setItens(it => ({ ...it, [combo]: corrigindo ? qtdNum : (it[combo] ?? 0) + qtdNum }))
    if (corrigindo && corrigindo !== combo) {
      // Trocou o tipo ou a forma ao corrigir: sai da combinação antiga
      setItens(it => ({ ...it, [corrigindo]: 0 }))
    }
    limparForm()
  }

  const corrigir = (c: Combo) => {
    const info = INFO[c]
    setTipo(info.tipo)
    setForma(c === 'abe' || c === 'ave' ? 'embalado' : c === 'grPvc20' || c === 'grPvc20Cx30' ? 'pvc20' : 'pvc')
    setOrigem(c === 'grPvc20Cx30' ? 'cx30' : 'cx20')
    setQtd(paraTexto(itens[c] ?? 0))
    setCorrigindo(c)
    formRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    setTimeout(() => qtdRef.current?.focus(), 300)
  }

  const remover = (c: Combo) => {
    const v = itens[c] ?? 0
    setItens(it => ({ ...it, [c]: 0 }))
    if (corrigindo === c) limparForm()
    toast(t => (
      <span className="flex items-center gap-3">
        {INFO[c].codigo} {caixasTxt(v)} removido.
        <button className="font-semibold text-tinta-700 underline"
                onClick={() => { setItens(it => ({ ...it, [c]: v })); toast.dismiss(t.id) }}>
          Desfazer
        </button>
      </span>
    ))
  }

  // ── Ações de sobras e quebras ───────────────────────────────────────────

  const limparCru = () => { setCruCx(''); setBand(''); setCorrigindoCru(null) }

  const adicionarCru = () => {
    if (!chaveCru) return
    if (bandNum <= 0 && !corrigindoCru) { cruCxRef.current?.focus(); return }
    setCrus(c => ({ ...c, [chaveCru]: corrigindoCru ? bandNum : (c[chaveCru] ?? 0) + bandNum }))
    if (corrigindoCru && corrigindoCru !== chaveCru) setCrus(c => ({ ...c, [corrigindoCru]: 0 }))
    limparCru()
  }

  const corrigirCru = (k: ChaveCru) => {
    const [g, c] = k.split('.') as [Grupo, Cru]
    const v = crus[k] ?? 0
    const cx = Math.floor(v / PRODUTOSC_CAIXA)
    setGrupo(g)
    setCru(c)
    setCruCx(cx ? String(cx) : '')
    setBand(paraTexto(v - cx * PRODUTOSC_CAIXA))
    setCorrigindoCru(k)
    cruRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    setTimeout(() => (cx ? cruCxRef : bandRef).current?.focus(), 300)
  }

  const removerCru = (k: ChaveCru) => {
    const v = crus[k] ?? 0
    setCrus(c => ({ ...c, [k]: 0 }))
    if (corrigindoCru === k) limparCru()
    toast(t => (
      <span className="flex items-center gap-3">
        Removido.
        <button className="font-semibold text-tinta-700 underline"
                onClick={() => { setCrus(c => ({ ...c, [k]: v })); toast.dismiss(t.id) }}>
          Desfazer
        </button>
      </span>
    ))
  }

  // ── Ações de vendas ─────────────────────────────────────────────────────

  const escolherTipoVenda = (t: Tipo) => {
    setVTipo(t)
    setVForma(f => (f && VENDA_COLUNA[t][f] ? f : null))
  }

  const limparVenda = () => {
    setVForma(null); setVCx(''); setVBand(''); setVNome(''); setVData(null); setCorrigindoVenda(null)
  }

  // Monta a venda do formulário; null (e foca o campo) se falta algo.
  const vendaDoForm = (avisar: boolean): Venda | null => {
    if (!vTipo || !vForma) return null
    if (vTotal <= 0) { if (avisar) vCxRef.current?.focus(); return null }
    if (!vNome.trim()) {
      if (avisar) { toast.error('Quem levou? Digite o nome.'); vNomeRef.current?.focus() }
      return null
    }
    const antiga = corrigindoVenda != null ? vendas.find(v => v.chave === corrigindoVenda) : undefined
    return {
      chave: antiga?.chave ?? proximaChave++, id: antiga?.id, extras: antiga?.extras,
      tipo: vTipo, forma: vForma, produtosC: vTotal, nome: vNome.trim(), data: vData && vData !== data ? vData : null,
    }
  }

  const juntarVenda = (lista: Venda[], v: Venda) =>
    lista.some(x => x.chave === v.chave) ? lista.map(x => (x.chave === v.chave ? v : x)) : [...lista, v]

  const adicionarVenda = () => {
    const v = vendaDoForm(true)
    if (!v) return
    setVendas(l => juntarVenda(l, v))
    toast.success(corrigindoVenda != null ? 'Venda corrigida.' : `Venda de ${capitalizar(v.nome)} anotada.`)
    limparVenda()
  }

  const corrigirVenda = (v: Venda) => {
    const porCaixa = VENDA_COLUNA[v.tipo][v.forma]!.porCaixa
    const cx = Math.floor(v.produtosC / porCaixa)
    setVTipo(v.tipo)
    setVForma(v.forma)
    setVCx(cx ? String(cx) : '')
    setVBand(paraTexto(v.produtosC - cx * porCaixa))
    setVNome(v.nome)
    setVData(v.data)
    setCorrigindoVenda(v.chave)
    vendaRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  const removerVenda = (v: Venda) => {
    const pos = vendas.findIndex(x => x.chave === v.chave)
    setVendas(l => l.filter(x => x.chave !== v.chave))
    if (corrigindoVenda === v.chave) limparVenda()
    toast(t => (
      <span className="flex items-center gap-3">
        Venda de {capitalizar(v.nome)} removida.
        <button className="font-semibold text-tinta-700 underline"
                onClick={() => {
                  setVendas(l => { const n = [...l]; n.splice(Math.max(0, pos), 0, v); return n })
                  toast.dismiss(t.id)
                }}>
          Desfazer
        </button>
      </span>
    ))
  }

  // Digitou e não apertou "Adicionar": pergunta antes de salvar/copiar,
  // para não perder a última linha. null = parar (falta o nome da venda).
  const comPendentes = async (): Promise<{ itens: Itens; crus: Crus; vendas: Venda[] } | null> => {
    let it = itens
    let cr = crus
    let vs = vendas
    if (combo && qtdNum > 0) {
      const txt = `${INFO[combo].codigo} ${caixasTxt(qtdNum)}`
      if (await confirmar({ titulo: 'Adicionar o que ficou digitado?', texto: <>Você digitou <b>{txt}</b> e não tocou em adicionar.</>, confirmar: 'Adicionar', cancelar: 'Deixar de fora', perigo: false })) {
        it = { ...it, [combo]: corrigindo ? qtdNum : (it[combo] ?? 0) + qtdNum }
        if (corrigindo && corrigindo !== combo) it = { ...it, [corrigindo]: 0 }
        setItens(it)
        limparForm()
      }
    }
    if (chaveCru && bandNum > 0) {
      const txt = `${grupo === 'sobra' ? 'sobra' : 'quebra'} de ${caixasEProdutosC(bandNum)}`
      if (await confirmar({ titulo: 'Adicionar o que ficou digitado?', texto: <>Você digitou <b>{txt}</b> e não tocou em adicionar.</>, confirmar: 'Adicionar', cancelar: 'Deixar de fora', perigo: false })) {
        cr = { ...cr, [chaveCru]: corrigindoCru ? bandNum : (cr[chaveCru] ?? 0) + bandNum }
        if (corrigindoCru && corrigindoCru !== chaveCru) cr = { ...cr, [corrigindoCru]: 0 }
        setCrus(cr)
        limparCru()
      }
    }
    if (colVenda && vTotal > 0) {
      if (await confirmar({ titulo: 'Adicionar a venda que ficou digitada?', texto: 'Você digitou uma venda e não tocou em adicionar.', confirmar: 'Adicionar', cancelar: 'Deixar de fora', perigo: false })) {
        const v = vendaDoForm(true)
        if (!v) return null
        vs = juntarVenda(vs, v)
        setVendas(vs)
        limparVenda()
      }
    }
    return { itens: it, crus: cr, vendas: vs }
  }

  const salvar = async () => {
    if (!data) { toast.error('Informe o dia.'); return }
    const atual = await comPendentes()
    if (!atual) return
    const v = montar(atual.itens, atual.crus)
    const total = Object.values(v.embalou).reduce((s, x) => s + (x ?? 0), 0)
    if (total <= 0 && !await confirmar({ titulo: 'Salvar sem nada embalado?', texto: 'Nenhuma caixa foi anotada como embalada neste controle.', confirmar: 'Salvar assim mesmo', perigo: false })) return
    setSalvando(true)
    // Na edição, o que esta tela não mostra (sobra/quebra de Ab-20 e Bb,
    // PVCMB embalado) fica como estava.
    const dto = {
      numero: Math.round(lerNum(numero)) || undefined,
      data,
      embalou: { ...v.embalou, mdPvcBr: original?.embalou?.mdPvcBr ?? 0 },
      pvcVinteComCaixaAb: v.pvcVinte,
      sobra: { ...(original?.sobra ?? vazio()), ...v.sobra },
      quebra: { ...(original?.quebra ?? vazio()), ...v.quebra },
      observacao,
      vendas: atual.vendas.map((x): VendaEnvio => ({
        id: x.id, nome: x.nome, data: x.data, quantidades: quantidadesVenda(x),
      })),
    }
    try {
      if (editando) {
        await volumesApi.atualizar(editando, dto)
        salvoComSucesso.current = true
        apagarRascunho(editando)
        toast.success('Controle atualizado.')
        navigate(`/estoque/volumes/${editando}`)
      } else {
        const r = await volumesApi.anotar(dto)
        salvoComSucesso.current = true
        apagarRascunho(null)
        toast.success(r.romaneios
          ? `Controle ${r.numero} anotado, com ${r.romaneios === 1 ? 'o romaneio da fazenda' : `${r.romaneios} romaneios da fazenda`} nas entradas.`
          : `Controle ${r.numero} anotado.`)
        navigate(`/estoque/volumes/${r.id}`)
      }
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro
      toast.error(msg ?? 'Não consegui salvar.')
    } finally {
      setSalvando(false)
    }
  }

  // Mesmo formato da mensagem que ele manda no WhatsApp.
  const textoWhatsApp = (v: ReturnType<typeof montar>, vs: Venda[]) => {
    const [a, m, d] = data.split('-')
    const q = (x: number | undefined) => (x ? num(x, 0) : '')
    const b = (x: number) => (x ? qtdZap(x, PRODUTOSC_CAIXA) : '')
    const pvc20 = v.embalou.grPvc20
      ? q(v.embalou.grPvc20) + (v.pvcVinte ? ` (${num(v.pvcVinte, 0)} com caixa de 30)` : '')
      : ''
    const linhasVendas = vs.map(x => {
      const porCaixa = VENDA_COLUNA[x.tipo][x.forma]!.porCaixa
      const dia = x.data ? ` (${diaMes(x.data)})` : ''
      return `${capitalizar(x.nome)} - ${qtdZap(x.produtosC, porCaixa)} ${x.tipo} ${NOME_FORMA[x.forma]}${dia}`
    })
    return [
      `Dia: ${d}/${m}/${a.slice(2)}`,
      'Embalados:',
      `AB: ${q(v.embalou.abe)}`,
      `AVE: ${q(v.embalou.ave)}`,
      'PVC:',
      `PVCAB: ${q(v.embalou.grPvcBr)}`,
      `PVCAB20: ${pvc20}`,
      `PVCV: ${q(v.embalou.grPvcVr)}`,
      'SOBRAS:',
      `AB: ${b(v.sobra.ab)}`,
      `AV: ${b(v.sobra.av)}`,
      'QUEBRAS:',
      `AB: ${b(v.quebra.ab)}`,
      `AV: ${b(v.quebra.av)}`,
      ...(linhasVendas.length ? ['VENDAS:', ...linhasVendas] : []),
      `OBSERVAÇÃO: ${observacao.trim()}`,
    ].join('\n')
  }

  const copiar = async () => {
    const atual = await comPendentes()
    if (!atual) return
    const t = textoWhatsApp(montar(atual.itens, atual.crus), atual.vendas)
    try {
      await navigator.clipboard.writeText(t)
    } catch {
      // Sem permissão de área de transferência (http, navegador antigo)
      const el = document.createElement('textarea')
      el.value = t
      document.body.appendChild(el)
      el.select()
      document.execCommand('copy')
      el.remove()
    }
    setCopiado(true)
    toast.success('Copiado. É só colar no WhatsApp.')
    setTimeout(() => setCopiado(false), 2500)
  }

  if (carregando) return (
    <div className="max-w-lg mx-auto space-y-4">
      <div className="esqueleto h-8 w-40" aria-hidden />
      <Carregando linhas={4} rotulo={editando ? 'Abrindo o controle' : 'Preparando a anotação'} />
    </div>
  )
  if (falhouCarga) return (
    <div className="max-w-lg mx-auto space-y-4">
      <Link to={editando ? `/estoque/volumes/${editando}` : '/estoque/volumes'} className={`${classesBotao('fantasma', 'sm')} -ml-3`}>
        <ArrowLeft size={16} aria-hidden /> Voltar
      </Link>
      <ErroCarga mensagem="Não consegui abrir o controle para editar." onTentar={() => setTentativa(t => t + 1)} />
    </div>
  )

  const formasValidas = tipo ? (Object.keys(COMBINACOES[tipo]) as Forma[]) : []
  const formasVenda = vTipo ? FORMAS_VENDA.filter(f => VENDA_COLUNA[vTipo][f.id]) : []
  const embalados = ORDEM.filter(c => !INFO[c].pvc && (itens[c] ?? 0) > 0)
  const pvcs = ORDEM.filter(c => INFO[c].pvc && (itens[c] ?? 0) > 0)
  const listaCrus = (['sobra', 'quebra'] as Grupo[]).flatMap(g =>
    CRUS.map(c => `${g}.${c.id}` as ChaveCru)).filter(k => (crus[k] ?? 0) > 0)

  const textoBotao = !combo
    ? (tipo ? 'Escolha a forma' : 'Escolha o tipo')
    : corrigindo
      ? `Corrigir ${INFO[combo].codigo}`
      : `Adicionar ${INFO[combo].codigo}`

  const textoBotaoVenda = !vTipo ? 'Escolha o tipo'
    : !vForma ? 'Escolha a forma'
    : corrigindoVenda != null ? 'Corrigir venda' : 'Adicionar venda'

  return (
    <div className="max-w-lg mx-auto pb-28">
      <Link to={editando ? `/estoque/volumes/${editando}` : '/estoque/volumes'} className={`${classesBotao('fantasma', 'sm')} -ml-3 -mt-1`}>
        <ArrowLeft size={16} aria-hidden /> {editando ? 'Voltar ao controle' : 'Controles'}
      </Link>

      <div className="mt-2 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h2 className="font-display text-2xl font-semibold text-gray-900 leading-tight">
          {editando ? `Controle ${numero}: anotação` : 'Anotar produtoB'}
        </h2>
        {guardadoEm && !pendente && (
          <p className="inline-flex items-center gap-1.5 text-sm text-gray-500" aria-live="polite">
            <CloudCheck size={16} aria-hidden className="text-emerald-700" />
            Rascunho guardado {quando(guardadoEm).replace('hoje às', 'às')}
          </p>
        )}
      </div>

      {pendente && (
        <Aviso tipo="atencao" className="mt-4" titulo="Há um rascunho desta anotação que não foi salvo"
               acao={<div className="flex flex-wrap gap-2">
                 <Botao variante="primario" tamanho="sm" icone={History} onClick={recuperarPendente}>Recuperar rascunho</Botao>
                 <Botao variante="secundario" tamanho="sm" onClick={descartarPendente}>Descartar</Botao>
               </div>}>
          Guardado neste aparelho {quando(pendente.salvoEm)}. Recuperar troca o que está na tela pelo rascunho.
        </Aviso>
      )}
      {recuperado && (
        <Aviso tipo="info" className="mt-4" titulo="Rascunho recuperado" onFechar={() => setRecuperado(null)}
               acao={<Botao variante="secundario" tamanho="sm" icone={RotateCcw} onClick={comecarDoZero}>Começar do zero</Botao>}>
          Você começou esta anotação {quando(recuperado)} e não salvou. Está tudo de volta.
        </Aviso>
      )}

      <div className="grid grid-cols-[1fr_7.5rem] gap-3 mt-4">
        <label className="block min-w-0">
          <span className="block text-sm font-medium text-gray-700 mb-1">Dia</span>
          <input type="date" value={data} onChange={e => setData(e.target.value)}
                 className="w-full min-w-0 min-h-12 border border-gray-300 rounded-lg px-3 text-base bg-white" />
        </label>
        <label className="block">
          <span className="block text-sm font-medium text-gray-700 mb-1">Controle</span>
          <input value={numero} onChange={e => setNumero(e.target.value)} inputMode="numeric"
                 className="w-full min-h-12 border border-gray-300 rounded-lg px-3 text-base font-semibold tabular-nums bg-white" />
        </label>
      </div>

      {/* ── Sobra do controle anterior (entra como entrada) ─────────────── */}
      <div className="mt-4">
        <CartaoSobraAnterior sobra={sobraAnt} manual={original?.sobraAnteriorManual ?? false}
                             carregando={buscandoSobra && !sobraAnt} />
      </div>

      {/* ── Romaneio da fazenda (volumes para embalar) que entra neste controle ─ */}
      {!editando && romaneiosDoDia.length > 0 && (
        <div className="mt-3 bg-white border border-gray-200 rounded-xl px-4 py-3 space-y-1.5">
          <p className="text-sm font-medium text-gray-700">Entra nas entradas ao salvar</p>
          {romaneiosDoDia.map(r => <div key={r.numero}><LinhaRomaneio r={r} /></div>)}
        </div>
      )}

      {/* ── O que embalou ──────────────────────────────────────────────── */}
      <section ref={formRef} className="mt-6 scroll-mt-4">
        <Titulo texto="O que embalou" />
        <div className="bg-white border border-gray-200 rounded-xl p-3 sm:p-4 space-y-4">
          <div>
            <Rotulo texto="Tipo de volume" />
            <div className="grid grid-cols-2 gap-2">
              {TIPOS.map(t => (
                <Opcao key={t.id} ativo={tipo === t.id} onClick={() => escolherTipo(t.id)}>
                  <Volume tipo={t.id} />
                  <span className="text-base font-semibold leading-tight text-center">{t.nome}</span>
                </Opcao>
              ))}
            </div>
          </div>

          {tipo && (
            <div className="animate-entrada">
              <Rotulo texto="Forma" />
              <div className={`grid gap-2 ${formasValidas.length === 3 ? 'grid-cols-3' : 'grid-cols-2'}`}>
                {FORMAS.filter(f => formasValidas.includes(f.id)).map(f => {
                  const c = COMBINACOES[tipo][f.id]!
                  return (
                    <Opcao key={f.id} ativo={forma === f.id} onClick={() => setForma(f.id)} baixo>
                      <span className="text-base font-semibold leading-tight">{f.nome}</span>
                      <span className="text-xs text-gray-600 tabular-nums tracking-wide">{INFO[c].codigo}</span>
                    </Opcao>
                  )
                })}
              </div>
            </div>
          )}

          {forma === 'pvc20' && (
            <div className="animate-entrada">
              <Rotulo texto="Montado com" />
              <div className="grid grid-cols-2 gap-2">
                <Opcao ativo={origem === 'cx20'} onClick={() => setOrigem('cx20')} baixo>
                  <span className="text-base font-semibold">Caixa de 20</span>
                </Opcao>
                <Opcao ativo={origem === 'cx30'} onClick={() => setOrigem('cx30')} baixo>
                  <span className="text-base font-semibold">Caixa de 30</span>
                </Opcao>
              </div>
              {origem === 'cx30' && (
                <p className="text-sm text-gray-600 mt-1.5">
                  Cada caixa gasta 200 volumes de Ab (6,67 produtosC).
                </p>
              )}
            </div>
          )}

          <div>
            <Rotulo texto="Quantas caixas" />
            <Contador valor={qtd} onChange={setQtd} inputRef={qtdRef} passo={1}
                      onEnter={adicionar} rotulo="Caixas" />
            {jaTem > 0 && qtdNum > 0 && combo && (
              <p className="text-sm text-gray-600 mt-1.5">
                Já tem {caixasTxt(jaTem)} de {INFO[combo].codigo}: vai somar e ficar{' '}
                <strong className="text-gray-900">{caixasTxt(jaTem + qtdNum)}</strong>.
              </p>
            )}
          </div>

          <div className="flex gap-2">
            {corrigindo && (
              <Botao variante="secundario" onClick={limparForm} className="min-h-12">Cancelar</Botao>
            )}
            <Botao variante="primario" onClick={adicionar} disabled={!combo} icone={combo && !corrigindo ? Plus : undefined}
                   className="flex-1 min-h-12 text-base">
              {textoBotao}
            </Botao>
          </div>
        </div>
      </section>

      {/* ── Lista do dia ───────────────────────────────────────────────── */}
      <section className="mt-6">
        <div className="flex items-baseline justify-between">
          <Titulo texto="Anotado" />
          {totalEmbalado > 0 && (
            <span className="text-sm font-medium text-gray-700 tabular-nums">{caixasTxt(totalEmbalado)} embaladas</span>
          )}
        </div>
        {embalados.length + pvcs.length + vendas.length === 0 ? (
          <p className="text-sm text-gray-600 bg-white border border-dashed border-gray-300 rounded-xl px-4 py-5 text-center">
            Nada anotado ainda. Escolha o tipo, a forma e as caixas acima.
          </p>
        ) : (
          <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
            {[{ nome: 'Embalados', lista: embalados }, { nome: 'PVC', lista: pvcs }]
              .filter(g => g.lista.length > 0).map(g => (
                <div key={g.nome}>
                  <h3 className="px-4 pt-3 pb-1 font-sans text-sm font-semibold text-gray-600">{g.nome}</h3>
                  <ul className="divide-y divide-gray-100">
                    {g.lista.map(c => (
                      <Linha key={c} tipo={INFO[c].tipo} codigo={INFO[c].codigo} nome={INFO[c].nome}
                             valor={caixasTxt(itens[c] ?? 0)} ativo={corrigindo === c}
                             onEditar={() => corrigir(c)} onRemover={() => remover(c)} />
                    ))}
                  </ul>
                </div>
              ))}
            {vendas.length > 0 && (
              <div>
                <h3 className="px-4 pt-3 pb-1 font-sans text-sm font-semibold text-gray-600">Vendas</h3>
                <ul className="divide-y divide-gray-100">
                  {vendas.map(v => (
                    <Linha key={v.chave} tipo={v.tipo} codigo={capitalizar(v.nome)} mono={false}
                           nome={`${v.tipo === 'branco' ? 'Branco' : 'Vermelho'} ${NOME_FORMA[v.forma]}${v.data ? ` · ${diaMes(v.data)}` : ''}`}
                           valor={caixasEProdutosCDe(v.produtosC, VENDA_COLUNA[v.tipo][v.forma]!.porCaixa)}
                           ativo={corrigindoVenda === v.chave}
                           onEditar={() => corrigirVenda(v)} onRemover={() => removerVenda(v)} />
                  ))}
                </ul>
              </div>
            )}
          </div>
        )}
      </section>

      {/* ── Sobras e quebras ───────────────────────────────────────────── */}
      <section ref={cruRef} className="mt-6 scroll-mt-4">
        <Titulo texto="Sobras e quebras" nota="caixa da fazenda" />
        <div className="bg-white border border-gray-200 rounded-xl p-3 sm:p-4 space-y-4">
          <div className="grid grid-cols-2 gap-1 p-1 bg-gray-100 rounded-lg" role="group" aria-label="Sobra ou quebra">
            {(['sobra', 'quebra'] as Grupo[]).map(g => (
              <button key={g} type="button" onClick={() => setGrupo(g)} aria-pressed={grupo === g}
                      className={`min-h-11 rounded-md text-base font-semibold transition-colors ${
                        grupo === g ? 'bg-white text-gray-900 shadow-sm' : 'text-gray-500 hover:text-gray-800'}`}>
                {g === 'sobra' ? 'Sobra' : 'Quebra'}
              </button>
            ))}
          </div>

          <div className="grid grid-cols-2 gap-2">
            {CRUS.map(c => (
              <Opcao key={c.id} ativo={cru === c.id} onClick={() => setCru(c.id)} baixo linha>
                <Volume tipo={c.tipo} pequeno />
                <span className="text-base font-semibold">{c.nome}</span>
              </Opcao>
            ))}
          </div>

          <div className="space-y-2">
            <CampoQtd rotulo="Caixas">
              <Contador valor={cruCx} onChange={setCruCx} inputRef={cruCxRef} passo={1}
                        onEnter={() => bandRef.current?.focus()} rotulo="Caixas" />
            </CampoQtd>
            <CampoQtd rotulo="ProdutosC">
              <Contador valor={band} onChange={setBand} inputRef={bandRef} passo={0.5} decimal
                        onEnter={adicionarCru} rotulo="ProdutosC" />
            </CampoQtd>
            <p className="text-sm text-gray-600 min-h-[1.25rem]">
              {bandNum > 0 && <>= {produtosCTxt(bandNum)}{bandNum >= PRODUTOSC_CAIXA && <> ({caixasEProdutosC(bandNum)})</>}</>}
              {jaTemCru > 0 && bandNum > 0 && (
                <> · já tem {caixasEProdutosC(jaTemCru)}, vai ficar{' '}
                  <strong className="text-gray-900">{caixasEProdutosC(jaTemCru + bandNum)}</strong></>
              )}
            </p>
          </div>

          <div className="flex gap-2">
            {corrigindoCru && (
              <Botao variante="secundario" onClick={limparCru} className="min-h-12">Cancelar</Botao>
            )}
            <Botao variante="secundario" onClick={adicionarCru} disabled={!cru} icone={cru && !corrigindoCru ? Plus : undefined}
                   className="flex-1 min-h-12 text-base border-tinta-300 text-tinta-800 hover:bg-tinta-50 hover:border-tinta-400">
              {!cru ? 'Escolha o tipo' : corrigindoCru ? `Corrigir ${grupo}` : `Adicionar ${grupo}`}
            </Botao>
          </div>

          {listaCrus.length > 0 && (
            <ul className="divide-y divide-gray-100 border-t border-gray-100 -mx-3 sm:-mx-4 -mb-3 sm:-mb-4">
              {listaCrus.map(k => {
                const [g, c] = k.split('.') as [Grupo, Cru]
                const info = CRUS.find(x => x.id === c)!
                const v = crus[k] ?? 0
                return (
                  <Linha key={k} tipo={info.tipo} codigo={`${g === 'sobra' ? 'Sobra' : 'Quebra'} ${c.toUpperCase()}`}
                         nome={v >= PRODUTOSC_CAIXA ? `${info.nome} · ${produtosCTxt(v)}` : info.nome}
                         valor={caixasEProdutosC(v)}
                         ativo={corrigindoCru === k}
                         onEditar={() => corrigirCru(k)} onRemover={() => removerCru(k)} />
                )
              })}
            </ul>
          )}
        </div>
        {outros.length > 0 && (
          <p className="text-sm text-gray-600 mt-2">
            Este controle também tem {outros.join('; ')}. Fica como está.
          </p>
        )}
      </section>

      {/* ── Vendas (saídas avulsas, sem preço) ─────────────────────────── */}
      <section ref={vendaRef} className="mt-6 scroll-mt-4">
        <Titulo texto="Vendas" nota="quem levou volume, sem preço" />
        <div className="bg-white border border-gray-200 rounded-xl p-3 sm:p-4 space-y-4">
          <div>
            <Rotulo texto="Tipo de volume" />
            <div className="grid grid-cols-2 gap-2">
              {TIPOS.map(t => (
                <Opcao key={t.id} ativo={vTipo === t.id} onClick={() => escolherTipoVenda(t.id)} baixo linha>
                  <Volume tipo={t.id} pequeno />
                  <span className="text-base font-semibold">{t.nome}</span>
                </Opcao>
              ))}
            </div>
          </div>

          {vTipo && (
            <div className="animate-entrada">
              <Rotulo texto="Forma" />
              <div className={`grid gap-2 ${formasVenda.length === 3 ? 'grid-cols-3' : 'grid-cols-2'}`}>
                {formasVenda.map(f => (
                  <Opcao key={f.id} ativo={vForma === f.id} onClick={() => setVForma(f.id)} baixo>
                    <span className="text-base font-semibold leading-tight">{f.nome}</span>
                    <span className="text-xs text-gray-600">{f.nota}</span>
                  </Opcao>
                ))}
              </div>
            </div>
          )}

          <div className="space-y-2">
            <CampoQtd rotulo="Caixas">
              <Contador valor={vCx} onChange={setVCx} inputRef={vCxRef} passo={1}
                        onEnter={() => vBandRef.current?.focus()} rotulo="Caixas da venda" />
            </CampoQtd>
            <CampoQtd rotulo="ProdutosC">
              <Contador valor={vBand} onChange={setVBand} inputRef={vBandRef} passo={0.5} decimal
                        onEnter={() => vNomeRef.current?.focus()} rotulo="ProdutosC da venda" />
            </CampoQtd>
            {vTotal > 0 && lerNum(vCx) > 0 && (
              <p className="text-sm text-gray-600">
                = {produtosCTxt(vTotal)}
                {colVenda && !colVenda.fazenda && colVenda.porCaixa !== PRODUTOSC_CAIXA && <> (caixa de PVC com {colVenda.porCaixa})</>}
              </p>
            )}
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-[1fr_11rem] gap-3">
            <label className="block min-w-0">
              <Rotulo texto="Quem levou" />
              <input ref={vNomeRef} value={vNome} onChange={e => setVNome(e.target.value)} list="volumes-vendas-nomes"
                     placeholder="Helena, Beatriz..." autoCapitalize="words"
                     onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); adicionarVenda() } }}
                     className="w-full min-w-0 min-h-12 border border-gray-300 rounded-lg px-3 text-base bg-white" />
              <datalist id="volumes-vendas-nomes">{nomes.map(n => <option key={n} value={n} />)}</datalist>
            </label>
            <label className="block min-w-0">
              <Rotulo texto="Dia da venda" />
              <input type="date" value={vData ?? data} onChange={e => setVData(e.target.value || null)}
                     className="w-full min-w-0 min-h-12 border border-gray-300 rounded-lg px-3 text-base bg-white" />
            </label>
          </div>

          <div className="flex gap-2">
            {corrigindoVenda != null && (
              <Botao variante="secundario" onClick={limparVenda} className="min-h-12">Cancelar</Botao>
            )}
            <Botao variante="secundario" onClick={adicionarVenda} disabled={!colVenda} icone={colVenda && corrigindoVenda == null ? Plus : undefined}
                   className="flex-1 min-h-12 text-base border-tinta-300 text-tinta-800 hover:bg-tinta-50 hover:border-tinta-400">
              {textoBotaoVenda}
            </Botao>
          </div>
          <p className="text-sm text-gray-600 -mt-1">
            Vira uma saída do controle. As saídas para clientes com placa continuam na aba Saídas.
          </p>
        </div>
      </section>

      <label className="block mt-6">
        <Titulo texto="Observação" />
        <textarea value={observacao} onChange={e => setObservacao(e.target.value)} rows={2}
                  placeholder="Algum recado sobre a produtoB"
                  className="w-full border border-gray-300 rounded-lg px-3 py-2.5 text-base bg-white transition-colors hover:border-gray-400 focus:border-tinta-500" />
      </label>

      {/* Rodapé fixo: no celular o botão fica sempre à mão */}
      <div className="fixed bottom-0 inset-x-0 lg:left-64 bg-white/95 backdrop-blur border-t border-gray-200 px-3 pt-3 pb-seguro z-10 shadow-[0_-4px_12px_-6px_rgb(0_0_0/0.12)]">
        <div className="max-w-lg mx-auto flex gap-2">
          <Botao variante="secundario" onClick={copiar} icone={copiado ? Check : Copy} className="min-h-12"
                 aria-label="Copiar para o WhatsApp">
            <span className="hidden sm:inline">Copiar para o WhatsApp</span>
            <span className="sm:hidden">{copiado ? 'Copiado' : 'WhatsApp'}</span>
          </Botao>
          <Botao variante="primario" onClick={salvar} carregando={salvando} textoCarregando="Salvando..."
                 className="flex-1 min-h-12 text-base">
            {editando ? 'Salvar' : `Salvar controle ${numero}`}
          </Botao>
        </div>
      </div>
    </div>
  )
}

// ── Peças ─────────────────────────────────────────────────────────────────

function Titulo({ texto, nota }: { texto: string; nota?: string }) {
  return (
    <h2 className="font-display text-lg font-semibold text-gray-900 mb-2">
      {texto} {nota && <span className="font-sans font-normal text-sm text-gray-500">{nota}</span>}
    </h2>
  )
}

function Rotulo({ texto }: { texto: string }) {
  return <span className="block text-sm font-medium text-gray-700 mb-1.5">{texto}</span>
}

// Rótulo à esquerda + contador (caixas / produtosC)
function CampoQtd({ rotulo, children }: { rotulo: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[4.75rem_1fr] items-center gap-2">
      <span className="text-sm font-medium text-gray-700">{rotulo}</span>
      {children}
    </div>
  )
}

// Botão grande de escolha (tipo, forma, origem)
function Opcao({ ativo, onClick, children, baixo, linha }: {
  ativo: boolean; onClick: () => void; children: React.ReactNode; baixo?: boolean; linha?: boolean
}) {
  return (
    <button type="button" onClick={onClick} aria-pressed={ativo}
            className={`min-w-0 rounded-lg border-2 px-2 flex items-center justify-center transition-colors ${
              linha ? 'flex-row gap-2' : 'flex-col gap-1'} ${
              baixo ? 'min-h-[52px] py-1.5' : 'min-h-[84px] py-2'} ${
              ativo
                ? 'border-gema-500 bg-gema-50 text-gray-900 shadow-sm'
                : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300 hover:bg-gray-50'}`}>
      {children}
    </button>
  )
}

// Quantidade: − campo + (teclado numérico)
function Contador({ valor, onChange, inputRef, passo, decimal, onEnter, rotulo }: {
  valor: string; onChange: (v: string) => void; inputRef: React.RefObject<HTMLInputElement>
  passo: number; decimal?: boolean; onEnter: () => void; rotulo: string
}) {
  const soma = (d: number) => {
    const v = Math.max(0, lerNum(valor) + d)
    onChange(v ? String(v).replace('.', ',') : '')
  }
  const botao = 'w-14 shrink-0 min-h-[56px] rounded-lg border border-gray-300 bg-white text-gray-700 hover:bg-gray-50 hover:border-gray-400 flex items-center justify-center transition-colors disabled:opacity-60 disabled:cursor-not-allowed'
  return (
    <div className="flex gap-2 min-w-0">
      <button type="button" onClick={() => soma(-passo)} className={botao}
              disabled={lerNum(valor) <= 0} aria-label={`Menos ${passo === 1 ? 'uma' : 'meia'}`}>
        <Minus size={22} aria-hidden />
      </button>
      <input ref={inputRef} value={valor} onChange={e => onChange(e.target.value)}
             inputMode={decimal ? 'decimal' : 'numeric'} placeholder="0" aria-label={rotulo}
             onFocus={e => e.target.select()}
             onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); onEnter() } }}
             className="flex-1 min-w-0 w-full min-h-[56px] border border-gray-300 rounded-lg px-2 font-display !text-2xl font-semibold tabular-nums text-center bg-white transition-colors hover:border-gray-400 focus:border-tinta-500" />
      <button type="button" onClick={() => soma(passo)} className={botao}
              aria-label={`Mais ${passo === 1 ? 'uma' : 'meia'}`}>
        <Plus size={22} aria-hidden />
      </button>
    </div>
  )
}

function Linha({ tipo, codigo, nome, valor, ativo, onEditar, onRemover, mono = true }: {
  tipo: Tipo; codigo: string; nome: string; valor: string; ativo: boolean
  onEditar: () => void; onRemover: () => void; mono?: boolean
}) {
  return (
    <li className={`flex items-center gap-3 pl-4 pr-1 py-1 transition-colors ${ativo ? 'bg-gema-50' : ''}`}>
      <Volume tipo={tipo} pequeno />
      <button type="button" onClick={onEditar} className="flex-1 min-w-0 text-left py-2">
        <span className={`block font-semibold text-gray-900 text-base truncate ${mono ? 'tracking-wide' : ''}`}>{codigo}</span>
        <span className="block text-sm text-gray-600 truncate">{nome}</span>
      </button>
      <span className="font-display text-lg font-semibold tabular-nums text-gray-900 text-right leading-tight max-w-[7rem] sm:max-w-none">{valor}</span>
      <div className="flex">
        <BotaoIcone icone={Pencil} rotulo={`Corrigir ${codigo}`} onClick={onEditar} className="sm:w-11 sm:h-11" />
        <BotaoIcone icone={Trash2} perigo rotulo={`Remover ${codigo}`} onClick={onRemover} className="sm:w-11 sm:h-11" />
      </div>
    </li>
  )
}
