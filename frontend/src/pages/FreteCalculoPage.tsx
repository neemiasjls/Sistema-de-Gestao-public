import { useConfirmar } from '../components/ui/Confirmar'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Campo from '../components/ui/Campo'
import Aviso, { toastConfira } from '../components/ui/Aviso'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import AreaSoltar from '../components/ui/AreaSoltar'
import { Link } from 'react-router-dom'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import { Fragment, useCallback, useEffect, useMemo, useState, useRef } from 'react'
import ProdutoECampo from '../components/ProdutoECampo'
import ProdutosCBrzCampo, { produtosCSemResposta, faltaProdutosC, type ProdutosCBrz } from '../components/ProdutosCBrzCampo'
import ConflitosTipoVolume from '../components/ConflitosTipoVolume'
import ProdutosSemTipo, { type ProdutoSemTipo } from '../components/ProdutosSemTipo'
import { ArrowRight, ArrowUp, Calculator, Check, ChevronDown, Copy, Download, FileSpreadsheet, FileText, FileUp, MapPin, Plus, Save, Truck, X } from 'lucide-react'
import toast from 'react-hot-toast'
import api, { ctesApi, calculosFreteApi, downloadBlob, formatBRL, guardarArquivosNFe, nomeDoArquivo, type FreteCalculoResult, type ItemFreteCalculo, type NfDetalhe, type CalculoFreteSalvoResumo, type PlanilhaDaCarga, type ConflitoTipo, type CorrecaoTipo } from '../services/api'
import ClienteBadge from '../components/ClienteBadge'
import { recalcularRotasFrete } from '../services/freteRotas'
import CalculoFretePerfil from '../components/CalculoFretePerfil'
import { boletosContasApi } from '../services/boletosContasApi'
import { dataBr } from '../utils/formato'
import { calculoPerfilDoNome, linkBoletosPerfil } from '../utils/calculoPerfil'
import { Vazio } from '../components/ui/Estados'
import RotasCadastradas from '../components/frete/RotasCadastradas'
import CargasSalvas from '../components/frete/CargasSalvas'

// Cálculo do frete Perfil (BOL-17): o .txt "dd-MM-aaaa PLACA.txt" fica guardado na carga, não baixa.
const ehTxt = (nome: string) => /\.txt$/i.test(nome)

interface AnaliseNFe {
  totalXmls: number
  totalNfes: number
  clientesEncontrados: string[]
  numerosSugeridos: Record<string, number>
  temCedro: boolean
  temBrz?: boolean
  precoProdutoCPadrao?: number
  temFrete: boolean
  produtosENasNotas: number
  conflitosTipo?: ConflitoTipo[]
  produtosSemTipo?: ProdutoSemTipo[]
  tiposRomaneio?: string[]
}

interface ProdutoSemPeso {
  numerNF: string
  produto: string
  qtd: number
}

interface LinhaItem extends ItemFreteCalculo {
  id: number
  // Nome pelo qual este destinatário é cobrado, quando é outro. As linhas com
  // a mesma chaveGrupo viram uma só na hora de calcular.
  grupo?: string
  cidadeGrupo?: string
  ufGrupo?: string
  chaveGrupo?: string
  nfsDetalhadas?: NfDetalhe[]
  cidadeConflito?: boolean
  cidadesEncontradas?: string[]
  pesoIncompleto?: boolean
  produtosSemPeso?: ProdutoSemPeso[]
  // kg/cx informados pelo usuário: produto → kg
  pesosInformados?: Record<string, number>
}

interface Rota {
  id?: number
  origem: string
  ufOrigem: string
  destino: string
  uf: string
  km: number
}

let nextId = 1
const linhaVazia = (): LinhaItem => ({
  id: nextId++,
  numeroNF: '',
  emitente: '',
  cidadeEmitente: 'ALDEIA',
  destinatario: '',
  cidadeDestino: '',
  uf: '',
  pesoKg: 0,
})

// ─────────────────────────────────────────────────────────────────

// Mesma ordem em que o Bsoft TMS numera os CT-es: pela menor chave de acesso
// das NFs de cada grupo. A chave começa pelo CNPJ do emitente e termina no
// número da nota — por isso Aldeia (…0181) vem antes de Ribeira (…0262), e
// dentro de cada um a nota menor primeiro. Pelo número da NF sozinho, 22xxx
// passava na frente de 144xxx e a ordem desandava.
// Carga salva antes de guardar a chave (até 22/09/2026) cai na cidade de
// origem + número da NF: Aldeia antes de Ribeira, igual à chave.
// A nota do Beta (Cascata) vai sempre por último no Bsoft, independente da chave.
type LinhaOrdenavel = {
  numeroNF?: string; destinatario?: string; cidadeOrigem?: string; cidadeEmitente?: string
  emitente?: string
  nfsDetalhadas?: NfDetalhe[]
}
function ehBeta(i: LinhaOrdenavel) {
  if ((i.emitente ?? '').toUpperCase().includes('BETA')) return true
  return (i.nfsDetalhadas ?? []).some(n => {
    const c = n.chave ?? ''
    return c.length === 44 && c.substring(6, 20) === '44555666000181'
  })
}
// No Bsoft a nota do Epsilon (CNPJ 77888999000181) sai sempre em 1º e a do Delta
// (33444555000181) em 2º, antes das outras, seja qual for a chave (FRE-02)
const PRIMEIROS = ['77888999000181', '33444555000181']
function posicaoFixa(i: LinhaOrdenavel) {
  const cnpjs = (i.nfsDetalhadas ?? []).map(n => n.chave ?? '').filter(c => c.length === 44).map(c => c.substring(6, 20))
  const p = PRIMEIROS.findIndex(c => cnpjs.includes(c))
  return p < 0 ? PRIMEIROS.length : p
}
function chaveBsoft(i: LinhaOrdenavel) {
  const chaves = (i.nfsDetalhadas ?? []).map(n => n.chave ?? '').filter(c => c.length === 44).sort()
  if (chaves.length) return chaves[0]
  const nfs = (i.numeroNF ?? '').split(',').map(n => parseInt(n.trim(), 10)).filter(n => !isNaN(n))
  const origem = (i.cidadeOrigem || i.cidadeEmitente || '').toUpperCase()
  return '~' + origem + '|' + String(nfs.length ? Math.min(...nfs) : 999999999).padStart(9, '0')
}
// A carga vai num caminhão só: nota sem a informação nos dados adicionais não
// conta; as que têm precisam bater. Divergiu, o campo fica em branco e a tela
// mostra o que veio (Admin, 16/10/2026).
function doNotas(valores: (string | undefined)[]) {
  const achados = [...new Set(valores.map(v => (v ?? '').trim()).filter(Boolean))]
  return { valor: achados.length === 1 ? achados[0] : '', divergentes: achados.length > 1 ? achados : [] }
}

function compararBsoft(a: LinhaOrdenavel, b: LinhaOrdenavel) {
  return Number(ehBeta(a)) - Number(ehBeta(b)) || posicaoFixa(a) - posicaoFixa(b) ||
    chaveBsoft(a).localeCompare(chaveBsoft(b)) || (a.destinatario ?? '').localeCompare(b.destinatario ?? '')
}

export default function FreteCalculoPage() {
  const [placa, setPlaca]               = useState('')
  const [motorista, setMotorista]       = useState('')
  // O que veio das notas na última leitura: o campo só é trocado por uma
  // leitura nova se ainda estiver vazio ou com o valor que as notas puseram.
  const placaDasNotas     = useRef('')
  const motoristaDasNotas = useRef('')
  // Notas com placas (ou motoristas) diferentes: o campo fica em branco e avisa
  const [placasDivergentes, setPlacasDivergentes]         = useState<string[]>([])
  const [motoristasDivergentes, setMotoristasDivergentes] = useState<string[]>([])
  // Avisos da leitura dos arquivos e da geração: ficam na tela até fechar (D-07)
  const [avisos, setAvisos]             = useState<string[]>([])
  const confirmar = useConfirmar()
  const [valorCarga, setValorCarga]     = useState('')
  const [valorPedagio, setValorPedagio] = useState('')
  const [itens, setItens]               = useState<LinhaItem[]>([linhaVazia()])
  const [resultado, setResultado]       = useState<FreteCalculoResult | null>(null)
  // Últimos valores, para quem espera o envio dos PDFs (salvar) não gravar a versão antiga
  const itensRef     = useRef(itens);     itensRef.current = itens
  const resultadoRef = useRef(resultado); resultadoRef.current = resultado
  const envioPdfs    = useRef<Promise<void> | null>(null)
  const [calculando, setCalculando]     = useState(false)
  const [importandoXml, setImportandoXml] = useState(false)
  const [copiadoIdx, setCopiadoIdx]     = useState<number | null>(null)
  // Sem NF na tela, a aba NFs mostra o vazio; 'Digitar a NF à mão' abre a lista mesmo assim
  const [digitarNfs, setDigitarNfs]     = useState(false)

  // ── Planilhas de romaneio, geradas dos mesmos XMLs ────────────
  // Os arquivos ficam guardados aqui porque o XML não volta do servidor: é
  // deles que sai a planilha, e é assim que dá pra gerar sem subir de novo.
  const [xmlFiles, setXmlFiles]         = useState<File[]>([])
  const [analise, setAnalise]           = useState<AnaliseNFe | null>(null)
  const [numerosRom, setNumerosRom]     = useState<Record<string, number>>({})
  const [qtdProdutosD, setQtdProdutosD]   = useState(0)
  const [caixasProdutoE, setCaixasProdutoE] = useState(0)
  const [produtosC, setProdutosC] = useState<ProdutosCBrz>(produtosCSemResposta())
  const [correcoes, setCorrecoes]       = useState<CorrecaoTipo[]>([])
  const [gerandoPlan, setGerandoPlan]   = useState(false)
  // Chave da última geração (notas, números, produtosD, produtoE, produtosC)
  const ultimaGeracao = useRef<string | null>(null)
  const [planilhasGeradas, setPlanilhasGeradas] = useState<{ nome: string; conteudo: string }[]>([])
  const [planilhasSalvas, setPlanilhasSalvas]   = useState<PlanilhaDaCarga[]>([])
  const [cargaAbertaId, setCargaAbertaId]       = useState<number | null>(null)
  const [verCalculoPerfil, setVerCalculoPerfil] = useState<{ data: string; placa: string } | null>(null)
  // Carga com queda do volume lançada em Boletos: calcular de novo apagaria a queda do .txt (Admin, 11/11/2026)
  const [quedaLancada, setQuedaLancada]         = useState<{ data: string; placa: string } | null>(null)
  // Enquanto não se sabe (conferindo) ou a conferência falhou, o Calcular fica travado: só libera com a certeza de que não há queda
  const [conferenciaQueda, setConferenciaQueda] = useState<'livre' | 'conferindo' | 'falhou'>('livre')
  const [tentativaQueda, setTentativaQueda]     = useState(0)

  // ── Cargas salvas ─────────────────────────────────────────────
  const [salvos, setSalvos]             = useState<CalculoFreteSalvoResumo[]>([])
  const [aba, setAba] = useState<'calculo' | 'nfs' | 'rotas' | 'cargas'>('calculo')
  const idAbas = useAbas('viagens')
  // Filtros das abas Rotas e Cargas: ficam aqui para não sumirem ao abrir uma carga e voltar
  const [buscaCarga, setBuscaCarga] = useState('')
  const [mesCarga, setMesCarga]     = useState('')
  const [buscaRota, setBuscaRota]   = useState('')
  const [salvandoCalc, setSalvandoCalc] = useState(false)

  // Copia o valor do pedágio de um CT-e (grupo de NFs) sem cifrão, no formato BR
  const copiarPedagio = async (valor: number, idx: number) => {
    const texto = valor.toFixed(2).replace('.', ',')
    try {
      await navigator.clipboard.writeText(texto)
      setCopiadoIdx(idx)
      toast.success(`Pedágio copiado: ${texto}`)
      setTimeout(() => setCopiadoIdx(c => (c === idx ? null : c)), 1500)
    } catch {
      toast.error('Não foi possível copiar.')
    }
  }

  const copiarTexto = async (texto: string, ok: string) => {
    try { await navigator.clipboard.writeText(texto); toast.success(ok) }
    catch { toast.error('Não foi possível copiar.') }
  }

  // Monta o resumo do frete e copia pra área de transferência.
  // O usuário cola onde quiser (WhatsApp, e-mail, bloco de notas).
  const copiarResumo = async () => {
    if (!resultado) return
    const hoje    = new Date().toLocaleDateString('pt-BR')
    // Valor REAL da carga = líquota arredondada × peso total das NFs
    const carga   = resultado.liquotaArredondada * resultado.pesoTotal
    const pedagio = parseFloat(valorPedagio.replace(',', '.')) || 0

    // Um bloco por CT-e (grupo de NFs) com todas as infos da tabela + origem
    const detalhe = resultado.itens.map((it, i) => {
      const nfs = (it.numeroNF ?? '').split(',').map(n => n.trim()).filter(Boolean).join(', ')
      return (
        `*${i + 1})* NF ${nfs || '-'}\n` +
        `Emitente: ${it.emitente ?? '-'}\n` +
        `Destinatário: ${it.destinatario ?? '-'}\n` +
        `${it.cidadeOrigem ?? '-'} → ${it.cidadeDestino}/${it.uf}`
      )
    }).join('\n\n')

    const msg =
      `*DATA:* ${hoje}\n` +
      `*PLACA:* ${placa || '-'}\n` +
      `*MOTORISTA:* ${motorista || '-'}\n\n` +
      `*RESUMO TOTAL*\n` +
      `Valor da carga: ${formatBRL(carga)}\n` +
      `Pedágio total: ${formatBRL(pedagio)}\n` +
      `Peso total: ${resultado.pesoTotal.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} kg\n` +
      `Líquota: ${resultado.liquotaArredondada.toFixed(2).replace('.', ',')}\n` +
      `Total de NFs: ${resultado.totalNFs}\n` +
      `Total de CT-es: ${resultado.itens.length}\n` +
      `------------------------------\n` +
      `*DETALHE POR CT-e:*\n\n` +
      detalhe

    try {
      await navigator.clipboard.writeText(msg)
      toast.success('Resumo copiado! Agora é só colar onde quiser.')
    } catch {
      toast.error('Não foi possível copiar o resumo.')
    }
  }

  // ── Salvar / carregar / abrir / excluir cálculos ──────────────
  const carregarSalvos = useCallback(async () => {
    try {
      const { data } = await calculosFreteApi.listar()
      setSalvos(data)
    } catch { /* silencioso */ }
  }, [])

  useEffect(() => { carregarSalvos() }, [carregarSalvos])

  // Carga aberta sem nada mudado: "Salvar alterações" fica apagado (Admin, 13/11/2026).
  // Retrato do que o salvar grava; ao abrir ou salvar, o retrato daquele momento vira o "salvo".
  const retrato = useMemo(() => JSON.stringify([placa.trim().toUpperCase(), motorista, valorCarga, valorPedagio,
    itens, resultado, planilhasGeradas.map(p => `${p.nome}:${p.conteudo.length}`)]),
    [placa, motorista, valorCarga, valorPedagio, itens, resultado, planilhasGeradas])
  const [retratoSalvo, setRetratoSalvo] = useState<string | null>(null)
  const [marcarSalvo, setMarcarSalvo] = useState(0)
  useEffect(() => { if (marcarSalvo) setRetratoSalvo(retrato) }, [marcarSalvo]) // eslint-disable-line react-hooks/exhaustive-deps
  const temAlteracao = cargaAbertaId == null || retrato !== retratoSalvo

  // Carga aberta: grava por cima (editar). comoNova = salvar outra carga com estes dados.
  const salvarCalculo = async (comoNova = false) => {
    if (!resultado) return
    if (!placa.trim()) { toast.error('Informe a placa para salvar o cálculo.'); return }
    setSalvandoCalc(true)
    try {
      // PDFs ainda subindo: espera, para a carga sair com a marca "tem PDF" certa
      if (envioPdfs.current) await envioPdfs.current
      const payloadJson = JSON.stringify({ placa, motorista,
        placaNotas: placaDasNotas.current, motoristaNotas: motoristaDasNotas.current, valorCarga, valorPedagio,
        itens: itensRef.current, resultado: resultadoRef.current ?? resultado })
      const carga   = parseFloat(valorCarga.replace(',', '.')) || 0
      const pedagio = parseFloat(valorPedagio.replace(',', '.')) || 0
      const editar = cargaAbertaId != null && !comoNova
      const enviar = editar
        ? (d: Parameters<typeof calculosFreteApi.salvar>[0]) => calculosFreteApi.atualizar(cargaAbertaId!, d)
        : calculosFreteApi.salvar
      const { data } = await enviar({
        placa: placa.trim().toUpperCase(),
        valorCarga: carga,
        valorPedagio: pedagio,
        pesoTotal: resultado.pesoTotal,
        liquotaBruta: resultado.liquotaBruta,
        liquotaArredondada: resultado.liquotaArredondada,
        totalNFs: resultado.totalNFs,
        totalCTes: resultado.itens.length,
        payloadJson,
        planilhas: planilhasGeradas,
      })

      // A carga passa a ser a recém-salva, então gerar de novo já guarda nela.
      setCargaAbertaId(data.id)
      if (data.planilhas > 0) {
        const { data: full } = await calculosFreteApi.obter(data.id)
        setPlanilhasSalvas(full.planilhas ?? [])
        setPlanilhasGeradas([])
      }

      toast.success(editar
        ? 'Carga atualizada.'
        : data.planilhas > 0 ? `Carga salva com ${data.planilhas} planilha(s)!` : 'Carga salva!')
      setMarcarSalvo(n => n + 1)
      await carregarSalvos()
    } catch { toast.error('Erro ao salvar cálculo.') }
    finally { setSalvandoCalc(false) }
  }

  const abrirSalvo = async (id: number) => {
    try {
      const { data } = await calculosFreteApi.obter(id)
      const p = JSON.parse(data.payloadJson) as {
        placa?: string; motorista?: string; placaNotas?: string; motoristaNotas?: string
        valorCarga?: string; valorPedagio?: string
        itens?: LinhaItem[]; resultado?: FreteCalculoResult
      }
      setPlaca(p.placa ?? data.placa)
      setMotorista(p.motorista ?? '')
      // "Importado das NFs" continua ao reabrir. Carga salva antes de guardar
      // isso: tira de novo das NFs que ficaram na carga.
      const nfsSalvas = (p.itens ?? []).flatMap(it => it.nfsDetalhadas ?? [])
      placaDasNotas.current = p.placaNotas ?? doNotas(nfsSalvas.map(n => n.placa)).valor
      motoristaDasNotas.current = p.motoristaNotas ?? doNotas(nfsSalvas.map(n => n.motorista)).valor
      setPlacasDivergentes([]); setMotoristasDivergentes([])
      setValorCarga(p.valorCarga ?? '')
      setValorPedagio(p.valorPedagio ?? '')
      if (p.itens) {
        setItens(p.itens)
        // evita colisão de id entre linhas restauradas e novas linhas
        const maxId = p.itens.reduce((m, it) => Math.max(m, it.id ?? 0), 0)
        if (maxId >= nextId) nextId = maxId + 1
      }
      // Carga salva antes da ordem do Bsoft volta na ordem de quando foi
      // calculada; reordena ao abrir. Cada linha leva seus próprios valores.
      const salvo = p.resultado ? { ...p.resultado, itens: [...p.resultado.itens].sort(compararBsoft) } : null
      if (salvo) setResultado(salvo)

      setCargaAbertaId(id)
      setPlanilhasSalvas(data.planilhas ?? [])
      setPlanilhasGeradas([])
      setAnalise(null)
      setXmlFiles([])
      xmlRef.current = []
      setMarcarSalvo(n => n + 1)

      setAba('calculo')
      // Quem rola é o <main> (a janela não rola): sem isto, abrir uma carga do fim da lista caía no meio do cálculo
      document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' })
      toast.success(`Carga de ${data.placa} aberta.`)
      // Restaura toda a carga antes de esperar pela consulta das rotas.
      if (salvo && salvo.rotasNaoCadastradas.length > 0)
        await atualizarResultadoDasRotas(salvo, p.valorCarga ?? String(data.valorCarga), p.valorPedagio ?? String(data.valorPedagio))
    } catch { toast.error('Erro ao abrir cálculo.') }
  }

  const excluirSalvo = async (c: CalculoFreteSalvoResumo) => {
    const dia = new Date(c.criadoEm).toLocaleDateString('pt-BR')
    if (!await confirmar({
      titulo: `Excluir a carga de ${c.placa}?`,
      texto: <>Carga de <b>{dia}</b>, {formatBRL(c.valorCarga)}.
        {(c.totalPlanilhas ?? 0) > 0 && <> Os romaneios dela também são excluídos, e os romaneios seguintes do mesmo cliente descem um número para não ficar buraco na sequência.</>}
        {' '}Não dá para desfazer.</>,
      confirmar: 'Excluir carga',
    })) return
    try {
      const { data } = await calculosFreteApi.excluir(c.id)
      const excluidos = data?.romaneiosExcluidos?.length ?? 0
      const renomeados = data?.renomeados?.length ?? 0
      toast.success(`Carga excluída${excluidos ? ` com ${excluidos} romaneio${excluidos === 1 ? '' : 's'}` : ''}` +
        `${renomeados ? `. ${renomeados} romaneio${renomeados === 1 ? '' : 's'} renumerado${renomeados === 1 ? '' : 's'}` : ''}.`)
      if (cargaAbertaId === c.id) { setCargaAbertaId(null); setPlanilhasSalvas([]); ultimaGeracao.current = null }
      await carregarSalvos()
    } catch { toast.error('Erro ao excluir a carga.') }
  }

  // ── Rotas cadastradas ─────────────────────────────────────────
  const [rotas, setRotas]               = useState<Rota[]>([])
  const [novaRota, setNovaRota]         = useState<Rota>({ origem: '', ufOrigem: '', destino: '', uf: '', km: 0 })
  const [salvandoRota, setSalvandoRota] = useState(false)
  const [editandoId, setEditandoId]     = useState<number | null>(null)
  const [editRota, setEditRota]         = useState<Rota>({ origem: '', ufOrigem: '', destino: '', uf: '', km: 0 })

  const carregarRotas = useCallback(async () => {
    try {
      const { data } = await ctesApi.listarDistancias()
      setRotas(data.sort((a, b) => a.origem.localeCompare(b.origem) || a.destino.localeCompare(b.destino)))
    } catch { /* silencioso */ }
  }, [])

  useEffect(() => { carregarRotas() }, [carregarRotas])

  const atualizarResultadoDasRotas = async (
    atual = resultado, carga = valorCarga, pedagio = valorPedagio,
  ) => {
    if (!atual) return
    try {
      setResultado(await recalcularRotasFrete(
        atual, parseFloat(carga.replace(',', '.')) || 0, parseFloat(pedagio.replace(',', '.')) || 0,
      ))
    } catch {
      // Não deixa o aviso e os valores antigos parecerem um cálculo atualizado.
      setResultado(null)
      toast.error('Não foi possível atualizar as distâncias. Clique em Calcular frete e pedágio para tentar novamente.')
    }
  }

  const salvarRota = async () => {
    if (!novaRota.origem || !novaRota.destino || !novaRota.uf || !novaRota.km) {
      toast.error('Preencha origem, destino, UF de destino e km.')
      return
    }
    setSalvandoRota(true)
    try {
      await ctesApi.adicionarDistancia({
        origem:   novaRota.origem,
        ufOrigem: novaRota.ufOrigem,
        destino:  novaRota.destino,
        uf:       novaRota.uf,
        km:       novaRota.km,
      })
      toast.success('Rota cadastrada!')
      setNovaRota({ origem: '', ufOrigem: '', destino: '', uf: '', km: 0 })
      await carregarRotas()
      await atualizarResultadoDasRotas()
    } catch { toast.error('Erro ao salvar rota.') }
    finally { setSalvandoRota(false) }
  }

  const removerRota = async (r: Rota) => {
    if (!await confirmar({ titulo: 'Remover a rota?', texto: <><b>{r.origem}/{r.ufOrigem}</b> até <b>{r.destino}/{r.uf}</b>, {r.km} km.</>, confirmar: 'Remover rota' })) return
    try {
      await ctesApi.removerDistancia({ origem: r.origem, destino: r.destino, uf: r.uf })
      toast.success('Rota removida.')
      await carregarRotas()
    } catch { toast.error('Erro ao remover rota.') }
  }

  const iniciarEdicao = (r: Rota) => {
    setEditandoId(r.id ?? null)
    setEditRota({ ...r })
  }

  const cancelarEdicao = () => {
    setEditandoId(null)
    setEditRota({ origem: '', ufOrigem: '', destino: '', uf: '', km: 0 })
  }

  const salvarEdicao = async () => {
    if (!editRota.origem || !editRota.destino || !editRota.uf || !editRota.km || !editandoId) {
      toast.error('Preencha todos os campos.')
      return
    }
    try {
      await ctesApi.atualizarDistancia(editandoId, {
        origem:   editRota.origem,
        ufOrigem: editRota.ufOrigem,
        destino:  editRota.destino,
        uf:       editRota.uf,
        km:       editRota.km,
      })
      toast.success('Rota atualizada!')
      cancelarEdicao()
      await carregarRotas()
      await atualizarResultadoDasRotas()
    } catch { toast.error('Erro ao atualizar rota.') }
  }

  // ── Import XML ────────────────────────────────────────────────

  // PDF solto junto com os XMLs vai para o arquivo de notas, onde encontra a
  // nota dele pelo número do arquivo.
  const guardarPdfs = async (pdfs: File[]) => {
    if (!pdfs.length) return
    // Salvar a carga espera este envio terminar, para gravar a marca "tem PDF" já certa
    const envio = (async () => {
      try {
        const r = await guardarArquivosNFe(pdfs)
        if (!r) return
        if (r.pdfs > 0) toast.success(`${r.pdfs} PDF(s) guardado(s) em Notas Fiscais.`)
        if (r.avisos.length) setAvisos(v => [...v, ...r.avisos])
        if (r.pdfs > 0) await marcarPdfsGuardados()
      } catch {
        toast.error('Não consegui guardar os PDFs.')
      }
    })()
    envioPdfs.current = envio
    try { await envio } finally { if (envioPdfs.current === envio) envioPdfs.current = null }
  }

  // A marca "tem PDF" de cada nota vem da leitura do XML, ANTES de os PDFs subirem.
  // A resposta do envio só diz QUANTOS PDFs entraram, não de quais notas; então,
  // depois do envio, confere no arquivo de notas (pela chave) as notas da tela
  // ainda sem PDF e marca só as que o servidor diz que agora têm. As outras
  // continuam com o aviso de PDF não guardado.
  const marcarPdfsGuardados = async () => {
    const chaves = [...new Set(itensRef.current.flatMap(it => (it.nfsDetalhadas ?? [])
      .filter(n => !n.temPdf && (n.chave ?? '').length === 44).map(n => n.chave!)))]
    const comPdf = new Set<string>()
    for (const chave of chaves) {
      try {
        const { data } = await api.get<{ itens?: { chaveAcesso: string; temPdf: boolean }[] }>('/api/nfe-arquivos', { params: { busca: chave } })
        if (data.itens?.some(a => a.chaveAcesso === chave && a.temPdf)) comPdf.add(chave)
      } catch { /* sem confirmação: a nota fica como estava */ }
    }
    if (!comPdf.size) return
    const marcar = <T extends { nfsDetalhadas?: NfDetalhe[] }>(lista: T[]): T[] => lista.map(it =>
      it.nfsDetalhadas?.some(n => n.chave && comPdf.has(n.chave))
        ? { ...it, nfsDetalhadas: it.nfsDetalhadas.map(n => n.chave && comPdf.has(n.chave) ? { ...n, temPdf: true } : n) }
        : it)
    itensRef.current = marcar(itensRef.current)
    if (resultadoRef.current) resultadoRef.current = { ...resultadoRef.current, itens: marcar(resultadoRef.current.itens) }
    setItens(marcar)
    setResultado(r => r ? { ...r, itens: marcar(r.itens) } : r)
  }

  // Notas da carga na tela (Admin, 15/10/2026: importar soma às que já estão, e ele tira
  // a que não quiser pelo ✕; antes cada importação trocava todas)
  const xmlRef = useRef<File[]>([])
  const planilhasSalvasRef = useRef<PlanilhaDaCarga[]>([])
  planilhasSalvasRef.current = planilhasSalvas

  const numerosDaCargaAberta = () => {
    const doSalvo: Record<string, number> = {}
    for (const pl of planilhasSalvasRef.current) {
      const m = /^(\d+)--(.+?)-\d{2}-\d{2}-\d{4}\.xlsx$/i.exec(pl.nome)
      if (m) doSalvo[m[2].toUpperCase()] = Number(m[1])
    }
    return doSalvo
  }

  // Lê de novo a carga inteira com estas notas: grupos, peso e romaneios
  const lerXmls = async (todos: File[]) => {
    if (!todos.length) {
      setXmlFiles([]); xmlRef.current = []; setItens([linhaVazia()]); setResultado(null); setAnalise(null)
      return
    }
    setImportandoXml(true)
    setXmlFiles(todos)
    xmlRef.current = todos
    try {
      const fd = new FormData()
      todos.forEach(f => fd.append('arquivos', f))
      const resp = await api.post<{
        numeroNF: string; emitente: string; cidadeEmitente: string
        destinatario: string; cidadeDestino: string; uf: string
        grupo?: string; cidadeGrupo?: string; ufGrupo?: string; chaveGrupo?: string
        pesoKg: number; nfs?: NfDetalhe[]
        cidadeConflito?: boolean; cidadesEncontradas?: string[]
        pesoIncompleto?: boolean; produtosSemPeso?: ProdutoSemPeso[]
      }[]>('/api/nfe/extrair-para-frete', fd, {
        headers: { 'Content-Type': 'multipart/form-data' }
      })
      const data = resp.data
      if (!data.length) { setAvisos(v => [...v, 'Nenhuma nota saiu desses XMLs: confira se são XMLs de NF-e.']); return }

      const conflitos    = data.filter(d => d.cidadeConflito).length
      const semPeso      = data.filter(d => d.pesoIncompleto).length
      if (conflitos > 0)
        toast(`${conflitos} grupo(s) com cidades conflitantes — escolha a cidade antes de calcular.`, { duration: 6000 })
      if (semPeso > 0)
        toast(`${semPeso} grupo(s) com produtos sem peso cadastrado — informe o kg/cx antes de calcular.`, { duration: 6000 })

      setItens(data.map(d => ({
        id:                 nextId++,
        numeroNF:           d.numeroNF,
        emitente:           d.emitente,
        cidadeEmitente:     d.cidadeEmitente || 'ALDEIA',
        destinatario:       d.destinatario,
        grupo:              d.grupo ?? '',
        cidadeGrupo:        d.cidadeGrupo ?? '',
        ufGrupo:            d.ufGrupo ?? '',
        chaveGrupo:         d.chaveGrupo ?? '',
        cidadeDestino:      d.cidadeDestino,
        uf:                 d.uf,
        pesoKg:             d.pesoKg,
        nfsDetalhadas:      d.nfs ?? [],
        cidadeConflito:     d.cidadeConflito ?? false,
        cidadesEncontradas: d.cidadesEncontradas ?? [],
        pesoIncompleto:     d.pesoIncompleto ?? false,
        produtosSemPeso:    d.produtosSemPeso ?? [],
        pesosInformados:    {},
      })))
      setResultado(null)
      toast.success(`${data.length} grupo(s) importado(s)`)

      // Placa e motorista da carga, pelas notas. Só troca o que o usuário não
      // digitou: campo vazio ou com o que a leitura anterior pôs.
      const nfs = data.flatMap(d => d.nfs ?? [])
      const preencher = (achado: ReturnType<typeof doNotas>, ref: { current: string },
                         set: (f: (atual: string) => string) => void, setDiv: (v: string[]) => void) => {
        const antes = ref.current
        if (achado.valor || achado.divergentes.length)
          set(atual => !atual.trim() || atual === antes ? achado.valor : atual)
        ref.current = achado.valor
        setDiv(achado.divergentes)
      }
      preencher(doNotas(nfs.map(n => n.placa)), placaDasNotas, setPlaca, setPlacasDivergentes)
      preencher(doNotas(nfs.map(n => n.motorista)), motoristaDasNotas, setMotorista, setMotoristasDivergentes)

      // Mesmos XMLs, segunda pergunta: dá pra tirar romaneio desta carga?
      try {
        const fd2 = new FormData()
        todos.forEach(f => fd2.append('arquivos', f))
        const an = await api.post<AnaliseNFe>('/api/nfe/analisar', fd2, {
          headers: { 'Content-Type': 'multipart/form-data' }
        })
        setAnalise(an.data)
        setCorrecoes([])
        setProdutosC(produtosCSemResposta(an.data.precoProdutoCPadrao))
        // Carga aberta: os romaneios mantêm os números que ela já tinha
        const doSalvo = numerosDaCargaAberta()
        setNumerosRom(Object.fromEntries(Object.entries(an.data.numerosSugeridos ?? {}).map(([ap, n]) => [ap, doSalvo[ap.toUpperCase()] ?? n])))
        setPlanilhasGeradas([])
        if (an.data.clientesEncontrados.length > 0)
          toast(`${an.data.clientesEncontrados.length} romaneio(s) podem ser gerados desta carga.`,
            { duration: 5000 })
      } catch {
        // Sem a análise a tela não tem os números: o servidor completa ao gerar, mas avisa
        setAvisos(v => [...v, 'Não consegui conferir os romaneios destas notas agora. Se faltar número ou romaneio, clique de novo em "Calcular frete e pedágio e gerar romaneios".'])
      }

    } catch { toast.error('Erro ao importar XMLs.') }
    finally { setImportandoXml(false) }
  }

  // Traz de volta os XMLs guardados das notas da carga aberta (pela chave de acesso)
  const trazerXmlsDaCarga = async (): Promise<File[]> => {
    const chaves = [...new Set(itens.flatMap(it => (it.nfsDetalhadas ?? []).map(n => n.chave ?? '')).filter(c => c.length === 44))]
    const arquivos: File[] = []
    let faltaram = 0
    for (const chave of chaves) {
      try {
        const r = await api.get(`/api/nfe-arquivos/chave/${chave}/xml`, { responseType: 'blob' })
        const nome = nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `${chave}.xml`)
        arquivos.push(new File([r.data as Blob], nome, { type: 'application/xml' }))
      } catch { faltaram++ }
    }
    if (faltaram) setAvisos(v => [...v, `${faltaram} nota(s) desta carga sem XML guardado: ponha os XMLs delas de novo.`])
    return arquivos
  }

  const [trazendoXmls, setTrazendoXmls] = useState(false)
  // As notas que estão na tela. Carga aberta ainda não tem os arquivos aqui:
  // eles voltam do arquivo de notas antes de tirar ou somar uma.
  const notasDaTela = async () => {
    if (xmlRef.current.length || !cargaAbertaId) return xmlRef.current
    setTrazendoXmls(true)
    try { return await trazerXmlsDaCarga() } finally { setTrazendoXmls(false) }
  }
  const inputNfRef = useRef<HTMLInputElement>(null)

  // Soma as notas novas às da tela (mesmo arquivo não entra duas vezes)
  const importarXmls = async (files: File[]) => {
    const novos = files.filter(f => f.name.toLowerCase().endsWith('.xml'))
    const pdfFiles = files.filter(f => f.name.toLowerCase().endsWith('.pdf'))
    if (!novos.length) {
      if (pdfFiles.length) { await guardarPdfs(pdfFiles); return }
      toast.error('Nenhum XML selecionado.')
      return
    }
    const base = await notasDaTela()
    const visto = new Set(base.map(f => `${f.name}:${f.size}`))
    const todos = [...base, ...novos.filter(f => !visto.has(`${f.name}:${f.size}`))]
    await lerXmls(todos)
    ultimaGeracao.current = null
    // Os XMLs o servidor já arquivou ao ler; faltam os PDFs.
    await guardarPdfs(pdfFiles)
  }

  // Tira uma NF da carga (a cancelada, a trocada) e lê de novo as que ficaram
  const tirarNf = async (det: NfDetalhe) => {
    const base = await notasDaTela()
    // Pelo nome do arquivo; o XML trazido de volta pode ter outro nome, aí vale a chave de acesso
    const daNota = await Promise.all(base.map(async f =>
      f.name === det.arquivo || (!!det.chave && (await f.text()).includes(`NFe${det.chave}"`))))
    const fica = base.filter((_, i) => !daNota[i])
    if (fica.length === base.length) { toast.error(`Não achei o XML da NF ${det.numero}.`); return }
    await lerXmls(fica)
    ultimaGeracao.current = null
    toast.success(`NF ${det.numero} tirada da carga.`)
  }

  // ── Gerar as planilhas dos mesmos XMLs ────────────────────────
  // Chama o mesmo endpoint da tela de Planilhas NF-e: mesma geração, mesmas
  // perguntas, mesmo contador de romaneio. Aqui muda só onde elas ficam.
  // O que define as planilhas: mudou, gera de novo; igual, o "Calcular" não baixa outra vez
  const chaveGeracao = () => JSON.stringify([xmlFiles.map(f => `${f.name}:${f.size}`), numerosRom, correcoes,
    qtdProdutosD, caixasProdutoE, analise?.temBrz ? produtosC : null])

  // automatico = chamado pelo "Calcular frete" (Admin, 14/10/2026): falta de dado vira
  // aviso na tela, não erro, e não repete se nada mudou desde a última geração.
  const gerarPlanilhas = async (automatico = false) => {
    if (!xmlFiles.length) { if (!automatico) toast.error('Importe os XMLs primeiro.'); return }
    if (automatico && ultimaGeracao.current === chaveGeracao()) return
    const falha = (msg: string) => {
      if (automatico) setAvisos(v => [...v, `Romaneios e Cálculo do frete Perfil não foram gerados: ${msg} Depois clique de novo em "Calcular frete e pedágio e gerar romaneios".`])
      else toast.error(msg)
    }
    if (analise?.temBrz) {
      const falta = faltaProdutosC(produtosC)
      if (falta) { falha(falta); return }
    }
    if (analise?.temCedro && qtdProdutosD <= 0) {
      falha('Informe a quantidade de caixas de produtosD da CEDRO FRUTAS.')
      return
    }
    if (analise?.produtosSemTipo?.length) {
      falha('Tem produto das notas sem nome no romaneio: escolha o nome antes de gerar.')
      return
    }
    setGerandoPlan(true)
    try {
      const fd = new FormData()
      xmlFiles.forEach(f => fd.append('arquivos', f))
      fd.append('numeros', JSON.stringify(numerosRom))
      fd.append('correcoes', JSON.stringify(correcoes))
      const resp = await api.post<{ arquivos: { nome: string; conteudo: string }[]; avisos?: string[] }>(
        `/api/nfe/gerar-planilhas?qtdProdutosDCedro=${qtdProdutosD}&caixasProdutoE=${caixasProdutoE}&produtosCBrz=${analise?.temBrz && produtosC.vai ? produtosC.quantidade : 0}&precoProdutoCBrz=${produtosC.preco}`,
        fd,
        { headers: { 'Content-Type': 'multipart/form-data' } }
      )

      // O .txt (Cálculo do frete Perfil) não baixa: fica guardado na carga (BOL-17)
      const paraBaixar = resp.data.arquivos.filter(a => !ehTxt(a.nome))
      for (const arq of paraBaixar) {
        const bin = atob(arq.conteudo)
        const bytes = new Uint8Array(bin.length)
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
        downloadBlob(new Blob([bytes]), arq.nome)
        await new Promise(r => setTimeout(r, 300))
      }
      const temTxt = paraBaixar.length < resp.data.arquivos.length
      toast.success(paraBaixar.length
        ? `${paraBaixar.length} romaneio(s) baixado(s)!${temTxt ? ' O Cálculo do frete Perfil fica guardado na carga.' : ''}`
        : 'Cálculo do frete Perfil pronto: fica guardado na carga.')
      if (resp.data.avisos?.length) setAvisos(v => [...v, ...resp.data.avisos!])

      setPlanilhasGeradas(resp.data.arquivos)
      ultimaGeracao.current = chaveGeracao()
      if (automatico && analise?.temFrete && !analise.produtosENasNotas && caixasProdutoE <= 0)
        setAvisos(v => [...v, 'O Cálculo do frete Perfil saiu com 0 caixas de produtoE (a nota do Beta não veio). Se teve produtoE, informe e clique de novo em "Calcular frete e pedágio e gerar romaneios".'])

      // Carga já salva: guarda agora. Ainda não salva: vai junto no salvar.
      if (cargaAbertaId) {
        await calculosFreteApi.anexarPlanilhas(cargaAbertaId, resp.data.arquivos)
        const { data: full } = await calculosFreteApi.obter(cargaAbertaId)
        setPlanilhasSalvas(full.planilhas ?? [])
        setPlanilhasGeradas([])
        await carregarSalvos()
      }
    } catch (e) { toast.error((e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? 'Erro ao gerar planilhas.') }
    finally { setGerandoPlan(false) }
  }

  // DANFE de uma NF da lista. A nota é achada pela chave de acesso, que veio
  // no proprio XML importado.
  const baixarDanfe = async (chave: string, numero: string) => {
    try {
      const r = await api.get(`/api/nfe-arquivos/chave/${chave}/pdf`, { responseType: 'blob' })
      downloadBlob(r.data as Blob,
        nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `NF-${numero}.pdf`))
    } catch {
      toast.error(`NF ${numero}: PDF não guardado. Suba o PDF em Notas Fiscais.`)
    }
  }

  // Carga aberta com Cálculo do frete Perfil: confere em Boletos se a queda do volume já foi lançada
  useEffect(() => {
    setQuedaLancada(null)
    setConferenciaQueda('livre')
    if (!cargaAbertaId) return
    const calculos = planilhasSalvas.map(pl => calculoPerfilDoNome(pl.nome)).filter((c): c is { data: string; placa: string } => !!c)
    if (!calculos.length) return
    let vivo = true
    setConferenciaQueda('conferindo')
    ;(async () => {
      try {
        for (const c of calculos) {
          const r = await boletosContasApi.fretePerfil(c.data, c.placa)
          if (!vivo) return
          if (r.data.comQueda) { setQuedaLancada(c); setConferenciaQueda('livre'); return }
        }
        if (vivo) setConferenciaQueda('livre')
      } catch {
        // Sem resposta não dá para saber se há queda: continua travado, com "Tentar de novo"
        if (vivo) setConferenciaQueda('falhou')
      }
    })()
    return () => { vivo = false }
  }, [cargaAbertaId, planilhasSalvas, tentativaQueda])

  const baixarPlanilhaSalva = async (pl: PlanilhaDaCarga) => {
    if (!cargaAbertaId) return
    try {
      const r = await calculosFreteApi.baixarPlanilha(cargaAbertaId, pl.id)
      downloadBlob(r.data as Blob,
        nomeDoArquivo(r.headers['content-disposition'] as string | undefined, pl.nome))
    } catch { toast.error('Não consegui baixar a planilha.') }
  }

  // ── Tabela ────────────────────────────────────────────────────
  const atualizarLinha = (id: number, campo: keyof ItemFreteCalculo, valor: string | number) =>
    setItens(prev => prev.map(it => it.id === id ? { ...it, [campo]: valor } : it))

  const adicionarLinha = () => setItens(prev => [...prev, linhaVazia()])
  const removerLinha   = (id: number) => { if (itens.length > 1) setItens(prev => prev.filter(it => it.id !== id)) }

  // Quando o usuário informa kg/cx de um produto sem peso,
  // recalcula pesoKg = pesoConhecido + Σ(qtd × kgInformado)
  const informarPesoProduto = (id: number, produto: string, kgCx: number) => {
    setItens(prev => prev.map(it => {
      if (it.id !== id) return it
      const novosInformados = { ...(it.pesosInformados ?? {}), [produto]: kgCx }
      const pesoExtras = (it.produtosSemPeso ?? []).reduce((sum, p) => {
        const kg = novosInformados[p.produto] ?? 0
        return sum + p.qtd * kg
      }, 0)
      // pesoKg base (produtos que JÁ tinham peso) + extras informados
      const pesoBase = it.nfsDetalhadas?.reduce((s, n) => s + n.pesoKg, 0) ?? it.pesoKg
      return { ...it, pesosInformados: novosInformados, pesoKg: pesoBase + pesoExtras }
    }))
  }

  // ── Calcular ─────────────────────────────────────────────────
  const calcular = async () => {
    if (quedaLancada) { toast.error('A queda do volume desta carga já foi lançada em Boletos: não dá para calcular de novo.'); return }
    if (conferenciaQueda !== 'livre') { toast.error('Ainda não deu para conferir se a queda do volume desta carga foi lançada em Boletos.'); return }
    const carga   = parseFloat(valorCarga.replace(',', '.'))
    const pedagio = parseFloat(valorPedagio.replace(',', '.'))
    if (!carga || !pedagio) { toast.error('Informe o valor da carga e do pedágio.'); return }
    const itensValidos = itens.filter(it => it.pesoKg > 0)
    if (!itensValidos.length) { toast.error('Informe ao menos um item com peso.'); return }

    // É aqui que as linhas se juntam. Até agora a tela mostrou o nome que veio
    // em cada nota; o cálculo é feito pelo nome de cobrança, com os pesos
    // somados e as NFs de todas as linhas do grupo.
    const porGrupo = new Map<string, ItemFreteCalculo>()
    for (const it of itensValidos) {
      const chave = it.chaveGrupo || `linha-${it.id}`
      const atual = porGrupo.get(chave)
      if (!atual) {
        porGrupo.set(chave, {
          numeroNF:       it.numeroNF ?? '',
          emitente:       it.emitente ?? '',
          cidadeEmitente: it.cidadeEmitente ?? '',
          destinatario:   it.grupo || it.destinatario || '',
          cidadeDestino:  it.cidadeGrupo || it.cidadeDestino || '',
          uf:             it.ufGrupo || it.uf || '',
          pesoKg:         Number(it.pesoKg) || 0,
          nfsDetalhadas:  [...(it.nfsDetalhadas ?? [])],
        })
      } else {
        atual.pesoKg += Number(it.pesoKg) || 0
        atual.numeroNF = [atual.numeroNF, it.numeroNF].filter(Boolean).join(', ')
        atual.nfsDetalhadas = [...(atual.nfsDetalhadas ?? []), ...(it.nfsDetalhadas ?? [])]
        // Grupo sem cidade própria fica com a primeira cidade que aparecer
        if (!atual.cidadeDestino && it.cidadeDestino) atual.cidadeDestino = it.cidadeDestino
        if (!atual.uf && it.uf) atual.uf = it.uf
      }
    }
    const itensParaCalculo = [...porGrupo.values()].sort(compararBsoft)

    setCalculando(true)
    try {
      const { data } = await ctesApi.calcularFrete({
        valorCarga: carga,
        valorPedagio: pedagio,
        itens: itensParaCalculo,
      })
      setResultado(data)
      if (data.rotasNaoCadastradas.length > 0)
        toastConfira(`${data.rotasNaoCadastradas.length} rota(s) não cadastrada(s). Veja embaixo do resultado.`)
      setCalculando(false)
      // Calculou: já baixa e guarda os romaneios e o .txt da Girassol (antes era outro botão)
      await gerarPlanilhas(true)
    } catch { toast.error('Erro ao calcular frete.') }
    finally { setCalculando(false) }
  }

  // ── Agrupamento visual das rotas ─────────────────────────────
  // Quantas linhas cada grupo tem: com mais de uma, a tela marca que elas vão
  // virar uma só no cálculo.
  const tamanhoDoGrupo = itens.reduce<Record<string, number>>((acc, it) => {
    if (it.chaveGrupo) acc[it.chaveGrupo] = (acc[it.chaveGrupo] ?? 0) + 1
    return acc
  }, {})

  // Resumo que aparece com a lista fechada
  const pesoDosItens = itens.reduce((soma, it) => soma + (Number(it.pesoKg) || 0), 0)
  const pesoResumo = pesoDosItens.toLocaleString('pt-BR', { maximumFractionDigits: 2 })
  const itensPendentes = itens.filter(it =>
    (it.cidadeConflito && (it.cidadesEncontradas?.length ?? 0) > 0) ||
    (it.pesoIncompleto && (it.produtosSemPeso?.length ?? 0) > 0))
  const itensComPendencia = itensPendentes.length

  // A lista completa mora na aba NFs. Nota com pendência aparece também no Cálculo,
  // com os campos de peso e de cidade, perto do botão de calcular (o pedido de peso
  // não pode passar batido, senão a nota fica fora da conta).
  const temNumeroNf = itens.some(it => (it.numeroNF ?? '').trim() !== '')
  const nfsVazias = !temNumeroNf && itens.every(it => !it.emitente && !it.destinatario && !it.pesoKg && !it.cidadeDestino)
  const irParaNfs = () => { setAba('nfs'); document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' }) }

  const rotasPorOrigem = rotas.reduce<Record<string, Rota[]>>((acc, r) => {
    ;(acc[`${r.origem}/${r.ufOrigem}`] ??= []).push(r)
    return acc
  }, {})

  // ── Peças de linha usadas na tabela (computador) e nos cartões (celular) ──

  // Botão do PDF da NF, à esquerda do número (FRE-06). Texto e não emoji: o
  // emoji não muda de cor, e os dois estados ficavam iguais.
  const totalNfsTela = itens.reduce((s, it) => s + (it.numeroNF ?? '').split(',').filter(n => n.trim()).length, 0)

  const chipsNf = (nfs: string[], detalhes: NfDetalhe[] | undefined, comPdf: boolean) => (
    <div className="flex flex-wrap gap-1.5">
      {[...nfs].sort((a, b) => (Number(a) - Number(b)) || String(a).localeCompare(String(b))).map(nf => {
        const det = detalhes?.find(d => d.numero === nf)
        return (
          <span key={nf} className="inline-flex items-center gap-1">
            {comPdf && det?.chave ? (
              <button
                type="button"
                onClick={() => baixarDanfe(det.chave!, nf)}
                aria-label={det.temPdf ? `Baixar o PDF da NF ${nf}` : `NF ${nf}: PDF ainda não guardado`}
                title={det.temPdf ? `Baixar o PDF da NF ${nf}` : `NF ${nf}: PDF ainda não guardado`}
                className={`inline-flex items-center gap-1 text-xs font-semibold px-2 min-h-10 sm:min-h-7 rounded-md border transition-colors ${
                  det.temPdf
                    ? 'bg-red-50 border-red-300 text-red-800 hover:bg-red-100'
                    : 'bg-gray-50 border-gray-200 text-gray-500 hover:bg-gray-100'}`}
              >
                <FileText size={13} aria-hidden /> PDF
              </button>
            ) : null}
            <span className="inline-flex items-center gap-0.5 bg-gray-100 text-gray-800 text-xs tabular-nums font-medium pl-2 pr-0.5 min-h-7 rounded-md border border-gray-200">
              {nf}
              {det?.arquivo ? (
                <button type="button" onClick={() => tirarNf(det)} disabled={importandoXml || trazendoXmls}
                        aria-label={`Tirar a NF ${nf} da carga`} title={`Tirar a NF ${nf} da carga`}
                        className="inline-flex items-center justify-center w-6 h-6 rounded text-gray-500 hover:text-red-700 hover:bg-red-50 disabled:opacity-40">
                  <X size={13} aria-hidden />
                </button>
              ) : <span className="w-1.5" />}
            </span>
          </span>
        )
      })}
    </div>
  )

  const escolherCidade = (it: LinhaItem) => (
    <div className="space-y-1.5">
      <p className="text-sm font-semibold text-amber-900">Cidades diferentes nas notas: escolha uma</p>
      <div className="flex flex-wrap gap-1.5" role="group" aria-label="Cidade de destino">
        {it.cidadesEncontradas!.map(c => {
          const [cidade, uf] = c.split('/')
          const ativo = it.cidadeDestino === cidade && it.uf === uf
          return (
            <button
              key={c}
              type="button"
              aria-pressed={ativo}
              onClick={() => {
                atualizarLinha(it.id, 'cidadeDestino', cidade)
                atualizarLinha(it.id, 'uf', uf ?? '')
              }}
              className={`inline-flex items-center gap-1 px-3 min-h-10 sm:min-h-8 rounded-lg text-sm font-medium border transition-colors ${ativo
                ? 'bg-tinta-700 text-white border-tinta-700'
                : 'bg-white text-amber-950 border-amber-300 hover:bg-amber-100'}`}
            >
              {ativo && <Check size={14} aria-hidden />}{c}
            </button>
          )
        })}
      </div>
    </div>
  )

  const pesosFaltando = (it: LinhaItem) => (
    <div className="space-y-1.5">
      <p className="text-sm font-semibold text-amber-900">Peso por caixa não cadastrado</p>
      {[...new Map((it.produtosSemPeso ?? []).map(p => [p.produto, p])).values()].map(p => (
        <label key={p.produto} className="flex items-center justify-between gap-2">
          <span className="text-sm text-amber-900 min-w-0 truncate" title={p.produto}>{p.produto}</span>
          <span className="flex items-center gap-1.5 shrink-0">
            <input
              inputMode="decimal"
              placeholder="0"
              value={(it.pesosInformados?.[p.produto]) || ''}
              onChange={e => informarPesoProduto(it.id, p.produto, parseFloat(e.target.value.replace(',', '.')) || 0)}
              className="w-20 min-h-11 sm:min-h-0 border border-amber-400 rounded-lg px-2 py-1.5 text-sm text-right tabular-nums bg-white"
            />
            <span className="text-sm text-amber-900">kg por caixa</span>
          </span>
        </label>
      ))}
    </div>
  )

  const campoLinha = 'w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-2.5 py-1.5 text-sm'
  // Campo dentro da tabela de NFs (computador): mesma altura em todas as colunas
  const campoNfTabela = 'min-h-9 border border-gray-300 rounded-lg px-2 py-1 text-sm bg-white'

  // ─────────────────────────────────────────────────────────────
  // Linhas das NFs (cartões no celular, tabela no computador). A aba NFs mostra todas;
  // a aba Cálculo mostra só as pendentes, com os mesmos campos (mesmo estado).
  const listaNfs = (lista: LinhaItem[], comTotal: boolean) => (<>
        {/* Celular e tablet: um cartão por linha, com os mesmos campos da tabela */}
        <ul className="lg:hidden grid sm:grid-cols-2 gap-3">
          {lista.map(it => {
            const nfs = (it.numeroNF ?? '').split(',').map(n => n.trim()).filter(Boolean)
            const conflito     = it.cidadeConflito && (it.cidadesEncontradas?.length ?? 0) > 0
            const semPesoLinha = it.pesoIncompleto && (it.produtosSemPeso?.length ?? 0) > 0
            const noGrupo      = (it.chaveGrupo && tamanhoDoGrupo[it.chaveGrupo] > 1) ? true : false
            const mostraGrupo  = Boolean(it.grupo) || noGrupo
            return (
              <li key={it.id} className={`rounded-xl border p-4 space-y-3 ${conflito || semPesoLinha ? 'bg-amber-50 border-amber-300' : noGrupo ? 'bg-tinta-50 border-tinta-200' : 'bg-white border-gray-200'}`}>
                <div className="flex items-start gap-2">
                  <div className="flex-1 min-w-0">
                    {nfs.length > 0 ? chipsNf(nfs, it.nfsDetalhadas, true) : (
                      <Campo rotulo="NF">
                        <input value={it.numeroNF ?? ''} onChange={e => atualizarLinha(it.id, 'numeroNF', e.target.value)}
                               className={`${campoLinha} tabular-nums`} />
                      </Campo>
                    )}
                  </div>
                  <BotaoIcone icone={X} perigo rotulo="Tirar esta linha" onClick={() => removerLinha(it.id)} disabled={itens.length <= 1} />
                </div>
                <Campo rotulo="Emitente">
                  <input value={it.emitente ?? ''} onChange={e => atualizarLinha(it.id, 'emitente', e.target.value)} className={campoLinha} />
                </Campo>
                <Campo rotulo="Destinatário">
                  <input value={it.destinatario ?? ''} onChange={e => atualizarLinha(it.id, 'destinatario', e.target.value)} className={campoLinha} />
                </Campo>
                {mostraGrupo && (
                  <p className="flex flex-wrap items-center gap-1.5 text-sm text-tinta-900 -mt-1">
                    <span className="text-tinta-800">CT-e feito como</span>
                    <span className="bg-tinta-100 border border-tinta-200 rounded-md px-1.5 py-0.5 font-semibold">{it.grupo || it.destinatario}</span>
                    {noGrupo && <span className="text-tinta-800">· junta {tamanhoDoGrupo[it.chaveGrupo!]} linhas</span>}
                  </p>
                )}
                <div className="grid grid-cols-2 gap-2">
                  <Campo rotulo="Origem">
                    <input value={it.cidadeEmitente ?? ''} onChange={e => atualizarLinha(it.id, 'cidadeEmitente', e.target.value.toUpperCase())}
                           className={`${campoLinha} uppercase`} />
                  </Campo>
                  <Campo rotulo="Peso (kg)">
                    <input inputMode="decimal" value={it.pesoKg || ''} placeholder="0"
                           onChange={e => atualizarLinha(it.id, 'pesoKg', parseFloat(e.target.value.replace(',', '.')) || 0)}
                           className={`${campoLinha} text-right tabular-nums ${semPesoLinha ? 'border-amber-400' : ''}`} />
                  </Campo>
                </div>
                {conflito ? escolherCidade(it) : (
                  <div className="grid grid-cols-[1fr_5.5rem] gap-2">
                    <Campo rotulo="Destino">
                      <input value={it.cidadeDestino ?? ''} onChange={e => atualizarLinha(it.id, 'cidadeDestino', e.target.value.toUpperCase())}
                             className={`${campoLinha} uppercase`} />
                    </Campo>
                    <Campo rotulo="UF">
                      <input value={it.uf ?? ''} maxLength={2} onChange={e => atualizarLinha(it.id, 'uf', e.target.value.toUpperCase())}
                             className={`${campoLinha} uppercase text-center`} />
                    </Campo>
                  </div>
                )}
                {semPesoLinha && pesosFaltando(it)}
              </li>
            )
          })}
        </ul>

        {/* Computador: tabela com total de peso no pé; rola de lado dentro dela quando não cabe (UI-05) */}
        <div className="hidden lg:block overflow-x-auto rounded-xl border border-gray-200 bg-white">
          <table className="w-full text-sm">
            <thead className="sticky -top-4 sm:-top-5 lg:-top-6 z-10 text-xs [&_th]:bg-gray-50 [&_th]:shadow-[inset_0_-1px_0] [&_th]:shadow-gray-200 text-gray-500 [&_th:first-child]:rounded-tl-xl [&_th:last-child]:rounded-tr-xl">
              <tr>
                <th className="pl-4 pr-2 py-2.5 text-left font-medium">NF(s)</th>
                <th className="px-2 py-2.5 text-left font-medium">Emitente</th>
                <th className="px-2 py-2.5 text-left font-medium">Origem</th>
                <th className="px-2 py-2.5 text-left font-medium">Destinatário</th>
                <th className="px-2 py-2.5 text-left font-medium">Destino</th>
                <th className="px-2 py-2.5 text-center font-medium">UF</th>
                <th className="px-2 py-2.5 text-right font-medium whitespace-nowrap">Peso (kg)</th>
                <th className="pl-2 pr-3 py-2.5 w-12"><span className="sr-only">Tirar</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {lista.map(it => {
                const nfs = (it.numeroNF ?? '').split(',').map(n => n.trim()).filter(Boolean)
                const conflito      = it.cidadeConflito && (it.cidadesEncontradas?.length ?? 0) > 0
                const semPesoLinha  = it.pesoIncompleto && (it.produtosSemPeso?.length ?? 0) > 0
                const noGrupo       = (it.chaveGrupo && tamanhoDoGrupo[it.chaveGrupo] > 1) ? true : false
                const nomeCobranca  = it.grupo || it.destinatario
                const mostraGrupo   = Boolean(it.grupo) || noGrupo
                // Sem faixa colorida na lateral: o tom do fundo já marca a linha
                const rowClass      = conflito || semPesoLinha ? 'bg-amber-50'
                                    : noGrupo      ? 'bg-tinta-50 hover:bg-tinta-100/70'
                                    : 'hover:bg-gray-50'
                return (
                <Fragment key={it.id}>
                <tr className={`align-top transition-colors ${rowClass}`}>
                  <td className="pl-4 pr-2 py-2.5 w-[150px]">
                    {nfs.length > 0 ? chipsNf(nfs, it.nfsDetalhadas, true) : (
                      <input
                        value={it.numeroNF ?? ''}
                        onChange={e => atualizarLinha(it.id, 'numeroNF', e.target.value)}
                        aria-label="Número da NF"
                        className={`w-24 ${campoNfTabela} tabular-nums`}
                        placeholder="NF"
                      />
                    )}
                  </td>
                  <td className="px-2 py-2.5">
                    <input
                      value={it.emitente ?? ''}
                      onChange={e => atualizarLinha(it.id, 'emitente', e.target.value)}
                      aria-label="Emitente"
                      className={`w-full min-w-[9rem] ${campoNfTabela}`}
                      placeholder="Emitente"
                    />
                  </td>
                  <td className="px-2 py-2.5">
                    <input
                      value={it.cidadeEmitente ?? ''}
                      onChange={e => atualizarLinha(it.id, 'cidadeEmitente', e.target.value.toUpperCase())}
                      aria-label="Cidade de origem"
                      className={`w-full min-w-[6rem] ${campoNfTabela} uppercase`}
                      placeholder="Cidade"
                    />
                  </td>
                  <td className="px-2 py-2.5">
                    <input
                      value={it.destinatario ?? ''}
                      onChange={e => atualizarLinha(it.id, 'destinatario', e.target.value)}
                      aria-label="Destinatário"
                      className={`w-full min-w-[12rem] ${campoNfTabela}`}
                      placeholder="Destinatário"
                    />
                    {/* O nome da nota fica; ao lado, por qual nome ele é cobrado */}
                    {mostraGrupo && (
                      <div className="mt-1.5 flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs text-tinta-900">
                        <span className="text-tinta-800 whitespace-nowrap">CT-e feito como</span>
                        <span className="bg-tinta-100 border border-tinta-200 rounded-md px-1.5 py-0.5 font-semibold">
                          {nomeCobranca}
                        </span>
                        {noGrupo && (
                          <span className="text-tinta-800 whitespace-nowrap">
                            · junta {tamanhoDoGrupo[it.chaveGrupo!]} linhas
                          </span>
                        )}
                      </div>
                    )}
                  </td>
                  <td className="px-2 py-2.5" colSpan={conflito ? 2 : 1}>
                    {conflito ? escolherCidade(it) : (
                      <input
                        value={it.cidadeDestino ?? ''}
                        onChange={e => atualizarLinha(it.id, 'cidadeDestino', e.target.value.toUpperCase())}
                        aria-label="Cidade de destino"
                        className={`w-full min-w-[7rem] ${campoNfTabela} uppercase`}
                        placeholder="Destino"
                      />
                    )}
                  </td>
                  {!conflito && (
                  <td className="px-2 py-2.5">
                    <input
                      value={it.uf ?? ''}
                      onChange={e => atualizarLinha(it.id, 'uf', e.target.value.toUpperCase())}
                      aria-label="UF de destino"
                      className={`w-12 ${campoNfTabela} uppercase text-center`}
                      placeholder="GO"
                      maxLength={2}
                    />
                  </td>
                  )}
                  <td className="px-2 py-2.5 text-right">
                    <input
                      inputMode="decimal"
                      value={it.pesoKg || ''}
                      onChange={e => atualizarLinha(it.id, 'pesoKg', parseFloat(e.target.value.replace(',', '.')) || 0)}
                      aria-label="Peso em kg"
                      className={`w-24 ${campoNfTabela} text-right tabular-nums ${semPesoLinha ? '!border-amber-400 bg-white' : ''}`}
                      placeholder="0"
                    />
                  </td>
                  <td className="pl-2 pr-3 py-2">
                    <BotaoIcone icone={X} perigo rotulo="Tirar esta linha" onClick={() => removerLinha(it.id)} disabled={itens.length <= 1} />
                  </td>
                </tr>
                {/* Peso que falta: linha inteira embaixo, para não espremer a coluna */}
                {semPesoLinha && (
                  <tr className="bg-amber-50">
                    <td colSpan={8} className="px-4 pb-3 pt-0">
                      <div className="max-w-lg">{pesosFaltando(it)}</div>
                    </td>
                  </tr>
                )}
                </Fragment>
                )}
              )}
            </tbody>
            {comTotal && <tfoot className="border-t-2 border-gray-200 bg-gray-50 text-gray-900 font-semibold tabular-nums">
              <tr>
                <td className="pl-4 pr-2 py-2.5 rounded-bl-xl" colSpan={6}>Total · {totalNfsTela} {totalNfsTela === 1 ? 'NF' : 'NFs'}</td>
                <td className="px-2 py-2.5 text-right whitespace-nowrap">{pesoResumo}</td>
                <td className="rounded-br-xl" />
              </tr>
            </tfoot>}
          </table>
        </div>
  </>)

  return (
    <div className="space-y-5 w-full">
      <CabecalhoTela descricao="Importe os XMLs da carga: o sistema agrupa por emitente e destinatário, calcula a líquota e tira os romaneios da mesma viagem." />

      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Partes da tela Viagens"
        opcoes={[['calculo', 'Cálculo do frete', Calculator], ['nfs', `NFs (${totalNfsTela})`, FileText], ['cargas', `Cargas (${salvos.length})`, Truck], ['rotas', `Rotas cadastradas (${rotas.length})`, MapPin]]} />
      <PainelAba id={idAbas} valor={aba}>

      {/* ── NFs da carga (aba) ── */}
      {aba === 'nfs' && (nfsVazias && !digitarNfs ? (
        <div className="rounded-xl border border-gray-200 bg-white">
          <Vazio compacto icone={FileText} titulo="Nenhuma NF na carga em tela"
            acao={<div className="flex flex-wrap justify-center gap-2">
              <Botao variante="primario" icone={FileUp} onClick={() => inputNfRef.current?.click()}
                     carregando={importandoXml || trazendoXmls} textoCarregando="Lendo as notas...">Importar NF</Botao>
              <Botao variante="secundario" onClick={() => setDigitarNfs(true)}>Digitar a NF à mão</Botao>
            </div>}>
            Importe os XMLs aqui ou na aba Cálculo do frete, ou abra uma carga salva na aba Cargas.
          </Vazio>
          <input ref={inputNfRef} type="file" accept=".xml,.pdf" multiple hidden
                 onChange={e => { const f = [...(e.target.files ?? [])]; e.target.value = ''; if (f.length) importarXmls(f) }} />
        </div>
      ) : (
        <div className="space-y-3">
          {/* Barra de cima, igual às abas Cargas e Rotas: resumo à esquerda, ações à direita */}
          <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
            <div className="space-y-1 min-w-0">
              <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-gray-600 tabular-nums">
                <span><span className="font-semibold text-gray-900">{totalNfsTela}</span> {totalNfsTela === 1 ? 'NF' : 'NFs'} · {pesoResumo} kg · linhas agrupadas por emitente e destinatário</span>
                {itensComPendencia > 0 && (
                  <span className="bg-amber-100 text-amber-900 border border-amber-300 text-xs font-semibold px-2 py-0.5 rounded-full whitespace-nowrap">
                    {itensComPendencia} a conferir
                  </span>
                )}
              </p>
              {/* Trocar uma NF: o ✕ ao lado do número tira; importar soma às que já estão */}
              <p className="text-sm text-gray-500">O ✕ ao lado da NF tira ela da carga; importar soma as novas às que já estão.</p>
            </div>
            <div className="flex flex-wrap gap-2 shrink-0">
              <Botao variante="secundario" icone={Plus} onClick={adicionarLinha}>Adicionar linha</Botao>
              <Botao variante="secundario" icone={FileUp} onClick={() => inputNfRef.current?.click()}
                     carregando={importandoXml || trazendoXmls} textoCarregando="Lendo as notas...">
                Importar NF
              </Botao>
              <input ref={inputNfRef} type="file" accept=".xml,.pdf" multiple hidden
                     onChange={e => { const f = [...(e.target.files ?? [])]; e.target.value = ''; if (f.length) importarXmls(f) }} />
            </div>
          </div>

        {listaNfs(itens, true)}
        </div>
      ))}


      {aba === 'rotas' && <RotasCadastradas rotas={rotas} busca={buscaRota} onBusca={setBuscaRota}
        novaRota={novaRota} setNovaRota={setNovaRota} salvandoRota={salvandoRota} onCadastrar={salvarRota}
        editandoId={editandoId} editRota={editRota} setEditRota={setEditRota} onEditar={iniciarEdicao}
        onSalvarEdicao={salvarEdicao} onCancelarEdicao={cancelarEdicao} onRemover={removerRota} />}


      {aba === 'cargas' && <CargasSalvas salvos={salvos} cargaAbertaId={cargaAbertaId}
        busca={buscaCarga} onBusca={setBuscaCarga} mes={mesCarga} onMes={setMesCarga}
        onAbrir={abrirSalvo} onExcluir={excluirSalvo} />}


      {aba === 'calculo' && <>

      {/* ── Importar XMLs ── */}
      <AreaSoltar
        aceita={['.xml', '.pdf']}
        onArquivos={importarXmls}
        icone={FileUp}
        titulo="Arraste aqui os XMLs e os PDFs das notas da carga"
        dica="O XML monta o cálculo; o PDF (DANFE) fica guardado em Notas Fiscais."
        textoBotao="Escolher arquivos"
        carregando={importandoXml}
        textoCarregando="Lendo as notas..."
      />

      {avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira" itens={avisos} onFechar={() => setAvisos([])} />}

      {/* ── Planilhas de romaneio desta carga ── */}
      {(analise !== null || planilhasSalvas.length > 0) && (
        <section aria-labelledby="via-planilhas" className="bg-white rounded-xl border border-gray-200 p-4 sm:p-5 space-y-4 animate-entrada">
          <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
            <h2 id="via-planilhas" className="text-base font-semibold text-gray-900">Romaneios da carga</h2>
            {cargaAbertaId && (
              <span className="text-sm text-gray-500">guardadas na carga aberta</span>
            )}
          </div>

          {analise && analise.clientesEncontrados.length === 0 && (
            <p className="text-sm text-gray-600">
              Nenhum cliente de romaneio nesses XMLs: essa carga não gera romaneio.
            </p>
          )}

          {analise && analise.clientesEncontrados.length > 0 && (
            <>
              <div className="flex flex-wrap gap-2">
                {analise.clientesEncontrados.map(c => <ClienteBadge key={c} apelido={c} />)}
              </div>

              {Object.keys(numerosRom).length > 0 && (
                <Campo grupo rotulo="Número do romaneio (sugerido, ajuste se precisar)">
                  <div className="flex flex-wrap gap-2 mt-0.5">
                    {Object.entries(numerosRom).map(([apelido, n]) => (
                      <label key={apelido} className="flex items-center gap-2 bg-gray-50 border border-gray-200 rounded-lg pl-3 pr-1.5 py-1.5">
                        <span className="text-sm font-semibold text-gray-800">{apelido}</span>
                        <input
                          type="number"
                          min={1}
                          value={n || ''}
                          onChange={e => setNumerosRom(prev => ({ ...prev, [apelido]: Math.max(0, parseInt(e.target.value) || 0) }))}
                          className="w-24 border border-gray-300 rounded-md px-2 py-1 text-sm text-center tabular-nums"
                        />
                      </label>
                    ))}
                  </div>
                </Campo>
              )}

              {analise.temBrz && <ProdutosCBrzCampo valor={produtosC} onChange={setProdutosC} />}

              {analise.temCedro && (
                <div className={`rounded-lg px-4 py-3 border ${qtdProdutosD > 0 ? 'bg-gray-50 border-gray-200' : 'bg-red-50 border-red-200'}`}>
                  <Campo
                    rotulo="Cedro Frutas: caixas de produtosD"
                    erro={qtdProdutosD <= 0 ? 'Obrigatório para gerar o romaneio da Cedro Frutas.' : undefined}
                  >
                    <input
                      type="number"
                      min={1}
                      value={qtdProdutosD || ''}
                      placeholder="0"
                      onChange={e => setQtdProdutosD(Math.max(0, parseInt(e.target.value) || 0))}
                      className="w-28 border border-gray-300 rounded-lg px-3 py-1.5 text-sm text-center tabular-nums"
                    />
                  </Campo>
                </div>
              )}

              <ConflitosTipoVolume conflitos={analise.conflitosTipo ?? []} tipos={analise.tiposRomaneio ?? []}
                                correcoes={correcoes} onChange={setCorrecoes} />
              <ProdutosSemTipo produtos={analise.produtosSemTipo ?? []}
                               onResolvido={p => setAnalise(a => a && { ...a, produtosSemTipo: (a.produtosSemTipo ?? []).filter(x => x.produto !== p) })} />

              {analise.temFrete && (
                <ProdutoECampo nasNotas={analise.produtosENasNotas} valor={caixasProdutoE} onChange={setCaixasProdutoE} />
              )}

            </>
          )}

          {planilhasSalvas.length > 0 && (
            <div className="space-y-2">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                <h3 className="text-sm font-semibold text-gray-800">Guardados nesta carga</h3>
                <Link to="/romaneios" className="text-sm font-medium text-tinta-700 hover:underline inline-flex items-center gap-1">
                  Para editar, abra Romaneios <ArrowRight size={14} aria-hidden />
                </Link>
              </div>
              <ul className="rounded-lg border border-gray-200 divide-y divide-gray-100">
                {planilhasSalvas.map(pl => {
                  if (ehTxt(pl.nome)) {
                    // Cálculo do frete Perfil: só ver aqui; a queda do volume se lança em Boletos (BOL-17)
                    const c = calculoPerfilDoNome(pl.nome)
                    return (
                      <li key={pl.id} className="pl-3 pr-1 py-2 space-y-1">
                        <div className="flex items-center justify-between gap-2">
                          <span className="flex items-center gap-2 min-w-0">
                            <FileText size={16} className="text-tinta-500 shrink-0" aria-hidden />
                            <span className="text-sm text-gray-800 truncate">
                              Cálculo do frete Perfil
                              {c ? <span className="text-gray-600"> · {dataBr(c.data)} · {c.placa}</span>
                                 : <span className="text-amber-800"> · sem data e placa no nome ({pl.nome})</span>}
                            </span>
                          </span>
                          {c && (
                            <Botao variante="secundario" tamanho="sm" onClick={() => setVerCalculoPerfil(c)}>Ver</Botao>
                          )}
                        </div>
                        {c && (
                          <p className="text-sm text-gray-600">
                            Para lançar a queda do volume:{' '}
                            <Link to={linkBoletosPerfil(c)} className="font-medium text-tinta-700 hover:underline">
                              Boletos → A receber → Perfil
                            </Link>
                          </p>
                        )}
                      </li>
                    )
                  }
                  return (
                    <li key={pl.id} className="flex items-center justify-between gap-2 pl-3 pr-1 py-1">
                      <span className="flex items-center gap-2 min-w-0">
                        <FileSpreadsheet size={16} className="text-tinta-500 shrink-0" aria-hidden />
                        <span className="text-sm text-gray-800 truncate" title={pl.nome}>{pl.nome}</span>
                      </span>
                      <span className="flex items-center shrink-0">
                        <BotaoIcone icone={Download} rotulo={`Baixar ${pl.nome}`} onClick={() => baixarPlanilhaSalva(pl)} />
                      </span>
                    </li>
                  )
                })}
              </ul>
              {verCalculoPerfil && (
                <CalculoFretePerfil data={verCalculoPerfil.data} placa={verCalculoPerfil.placa} somenteLeitura
                                    onClose={() => setVerCalculoPerfil(null)} />
              )}
            </div>
          )}
        </section>
      )}

      {/* ── Valores da viagem ── */}
      <section aria-label="Valores da viagem" className="bg-white rounded-xl border border-gray-200 p-4 sm:p-5 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <Campo rotulo="Placa" dica={placa
          ? placa === placaDasNotas.current ? <span className="text-[13px]">Importado das NFs</span> : undefined
          : placasDivergentes.length ? <span className="text-[13px] text-amber-700">Placas diferentes nas notas ({placasDivergentes.join(', ')}): informe</span> : undefined}>
          <input
            value={placa}
            onChange={e => setPlaca(e.target.value.toUpperCase())}
            placeholder="Ex.: XAL-5A77"
            autoComplete="off"
            className="w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-3 py-2 text-sm uppercase tracking-wide"
          />
        </Campo>
        <Campo rotulo="Motorista" dica={motorista
          ? motorista === motoristaDasNotas.current ? <span className="text-[13px]">Importado das NFs</span> : undefined
          : motoristasDivergentes.length ? <span className="text-[13px] text-amber-700">Motoristas diferentes nas notas ({motoristasDivergentes.join(', ')}): informe</span> : undefined}>
          <input
            value={motorista}
            onChange={e => setMotorista(e.target.value.toUpperCase())}
            placeholder="Ex.: MARCOS TEIXEIRA"
            autoComplete="off"
            className="w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-3 py-2 text-sm uppercase"
          />
        </Campo>
        <Campo rotulo="Valor total da carga (R$)">
          <input
            value={valorCarga}
            onChange={e => setValorCarga(e.target.value)}
            placeholder="Ex.: 12500,00"
            inputMode="decimal"
            className="w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-3 py-2 text-sm tabular-nums"
          />
        </Campo>
        <Campo rotulo="Valor total do pedágio (R$)">
          <input
            value={valorPedagio}
            onChange={e => setValorPedagio(e.target.value)}
            placeholder="Ex.: 350,00"
            inputMode="decimal"
            className="w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-3 py-2 text-sm tabular-nums"
          />
        </Campo>
      </section>

      {/* ── NFs: a lista completa mora na aba NFs. Nota com pendência (peso que falta, cidade que
          não bate) aparece aqui mesmo, com os campos, como sempre foi (Admin, 12/11/2026) ── */}
      {itensComPendencia > 0 && (
        <section aria-label="NFs a conferir" className="space-y-3">
          <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
            <span className="font-semibold text-gray-900">NFs</span>
            <span className="bg-amber-100 text-amber-900 border border-amber-300 text-xs font-semibold px-2 py-0.5 rounded-full whitespace-nowrap">
              {itensComPendencia} a conferir
            </span>
          </p>
          {listaNfs(itensPendentes, false)}
        </section>
      )}
      <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-gray-600 tabular-nums">
        <span><span className="font-semibold text-gray-900">{totalNfsTela}</span> {totalNfsTela === 1 ? 'NF' : 'NFs'} · {pesoResumo} kg</span>
        <button type="button" onClick={irParaNfs} className="font-medium text-tinta-700 hover:underline">Ver na aba NFs</button>
      </p>

      {quedaLancada && (
        <Aviso tipo="atencao" titulo="Esta carga não pode ser calculada de novo">
          A queda do volume da Girassol já foi lançada em Boletos. Calcular de novo apagaria a queda do Cálculo do frete Perfil.{' '}
          <Link to={linkBoletosPerfil(quedaLancada)} className="font-medium text-tinta-700 hover:underline">
            Ver em Boletos, A receber, Perfil
          </Link>
        </Aviso>
      )}

      {conferenciaQueda === 'falhou' && (
        <Aviso tipo="atencao" titulo="Não consegui conferir a queda do volume desta carga">
          Sem saber se a queda do volume da Girassol já foi lançada em Boletos, a carga não pode ser calculada de novo.{' '}
          <button type="button" onClick={() => setTentativaQueda(n => n + 1)} className="font-medium text-tinta-700 hover:underline">
            Tentar de novo
          </button>
        </Aviso>
      )}

      <Botao variante="primario" icone={Calculator} onClick={calcular} carregando={calculando} textoCarregando="Calculando..."
             disabled={!!quedaLancada || conferenciaQueda !== 'livre'} bloco className="sm:w-auto">
        {conferenciaQueda === 'conferindo' ? 'Conferindo a queda do volume...' : 'Calcular frete e pedágio e gerar romaneios'}
      </Botao>

      {/* ── Resultado ── */}
      {resultado && (() => {
        const liquota = resultado.liquotaArredondada.toFixed(2).replace('.', ',')
        const pedagioTotal = parseFloat(valorPedagio.replace(',', '.')) || 0
        return (
        <section aria-labelledby="via-resultado" className="space-y-4 animate-entrada">
          {/* A líquota é o número que vai para o Bsoft: ela manda; o resto confere */}
          <div className="bg-white border border-gray-200 rounded-2xl overflow-hidden grid sm:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
            <div className="bg-gema-50 border-b sm:border-b-0 sm:border-r border-gema-200 p-4 sm:p-5 flex flex-col gap-1">
              <h2 id="via-resultado" className="text-sm font-medium text-gray-700 font-sans tracking-normal">Líquota para o Bsoft</h2>
              <p className="font-display text-5xl font-semibold text-gray-900 tabular-nums leading-none tracking-tight">{liquota}</p>
              <p className="text-sm text-gray-700 tabular-nums">
                arredondada para cima · bruta {resultado.liquotaBruta.toFixed(5).replace('.', ',')}
              </p>
              <div className="pt-2">
                <Botao variante="secundario" tamanho="sm" icone={Copy} onClick={() => copiarTexto(liquota, `Líquota copiada: ${liquota}`)}>
                  Copiar a líquota
                </Botao>
              </div>
            </div>
            <dl className="grid grid-cols-2 lg:grid-cols-3">
              {([
                ['Valor da carga', formatBRL(resultado.liquotaArredondada * resultado.pesoTotal)],
                ['Pedágio total', formatBRL(pedagioTotal)],
                ['Peso total', `${resultado.pesoTotal.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} kg`],
                ['Notas fiscais', String(resultado.totalNFs)],
                ['CT-es', String(resultado.itens.length)],
              ] as const).map(([rotulo, valor]) => (
                <div key={rotulo} className="px-4 py-3 border-b border-r border-gray-100">
                  <dt className="text-xs text-gray-500">{rotulo}</dt>
                  <dd className="font-display text-lg sm:text-xl font-semibold text-gray-900 tabular-nums leading-tight mt-0.5">{valor}</dd>
                </div>
              ))}
            </dl>
          </div>

          {/* Ações: salvar cálculo + copiar resumo */}
          <div className="flex flex-col sm:flex-row gap-2 sm:gap-3">
            <Botao variante="primario" icone={Save} onClick={() => salvarCalculo()} carregando={salvandoCalc} textoCarregando="Salvando..."
              disabled={!temAlteracao} title={temAlteracao ? undefined : 'Nada mudou desde que a carga foi salva.'}>
              {!cargaAbertaId ? 'Salvar cálculo' : temAlteracao ? 'Salvar alterações na carga' : 'Carga salva, sem alterações'}
            </Botao>
            {cargaAbertaId && (
              <Botao variante="secundario" icone={Save} onClick={() => salvarCalculo(true)} disabled={salvandoCalc}>
                Salvar como nova carga
              </Botao>
            )}
            <Botao variante="secundario" icone={Copy} onClick={copiarResumo}>Copiar resumo</Botao>
          </div>

          {/* Rotas não cadastradas */}
          {resultado.rotasNaoCadastradas.length > 0 && (
            <Aviso
              tipo="atencao"
              titulo="Rotas não cadastradas: o pedágio dessas linhas ficou sem distância"
              itens={resultado.rotasNaoCadastradas}
              acao={
                <Botao variante="secundario" tamanho="sm" icone={ArrowUp}
                       // A página rola dentro do <main>, não da janela
                       onClick={() => { setAba('rotas'); document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' }) }}>
                  Ir para o painel de rotas
                </Botao>
              }
            />
          )}

          {/* Rateio do pedágio, na ordem do Bsoft (FRE-02) */}
          <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div className="px-4 sm:px-5 py-3 border-b border-gray-100 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
              <h3 className="font-semibold text-gray-900 text-base">Rateio do pedágio por CT-e</h3>
              <span className="text-sm text-gray-500 tabular-nums">
                na ordem do Bsoft · peso × km somados: {resultado.totalPesoXKm.toLocaleString('pt-BR', { maximumFractionDigits: 0 })}
              </span>
            </div>

            {/* Celular: um cartão por CT-e, com o pedágio e o copiar à mão */}
            <ol className="sm:hidden divide-y divide-gray-100">
              {resultado.itens.map((it, i) => {
                const nfsRes = (it.numeroNF ?? '').split(',').map(n => n.trim()).filter(Boolean)
                return (
                  <li key={i} className="p-4 space-y-2">
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-xs font-semibold text-gray-500 tabular-nums">CT-e {i + 1}</p>
                        <p className="font-semibold text-gray-900 break-words">{it.destinatario ?? '—'}</p>
                        <p className="text-sm text-gray-600 flex flex-wrap items-center gap-x-1">
                          <span>{it.cidadeOrigem ?? '—'}</span>
                          <ArrowRight size={13} className="text-gray-500" aria-label="até" />
                          <span>{it.cidadeDestino}/{it.uf}</span>
                        </p>
                      </div>
                      <div className="text-right shrink-0">
                        <p className="text-xs text-gray-500">Pedágio</p>
                        <p className="font-display text-lg font-semibold text-tinta-800 tabular-nums leading-tight">{formatBRL(it.valorPedagio)}</p>
                      </div>
                    </div>
                    {nfsRes.length > 0 && chipsNf(nfsRes, undefined, false)}
                    <p className="text-sm text-gray-600 truncate">{it.emitente ?? '—'}</p>
                    <div className="flex items-center justify-between gap-3">
                      <dl className="flex gap-4 text-sm tabular-nums">
                        <div><dt className="text-xs text-gray-500">Peso</dt><dd className="text-gray-900">{it.pesoKg.toLocaleString('pt-BR', { maximumFractionDigits: 2 })} kg</dd></div>
                        <div><dt className="text-xs text-gray-500">Distância</dt>
                          <dd className={it.distanciaKm != null ? 'text-gray-900' : 'text-red-700 font-semibold'}>{it.distanciaKm != null ? `${it.distanciaKm} km` : 'sem rota'}</dd></div>
                        <div><dt className="text-xs text-gray-500">Parte</dt><dd className="text-gray-900">{(it.percentualPedagio * 100).toFixed(2).replace('.', ',')}%</dd></div>
                      </dl>
                      <Botao variante="secundario" tamanho="sm" icone={copiadoIdx === i ? Check : Copy}
                             aria-label={`Copiar o pedágio do CT-e ${i + 1}`} onClick={() => copiarPedagio(it.valorPedagio, i)}>
                        {copiadoIdx === i ? 'Copiado' : 'Copiar'}
                      </Botao>
                    </div>
                  </li>
                )
              })}
            </ol>

            {/* Computador: tabela */}
            <div className="hidden sm:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-xs text-gray-500">
                  <tr>
                    <th className="pl-4 pr-2 py-2.5 text-right font-medium w-10 whitespace-nowrap">CT-e</th>
                    <th className="px-3 py-2.5 text-left font-medium">NF(s)</th>
                    <th className="px-3 py-2.5 text-left font-medium">Emitente</th>
                    <th className="px-3 py-2.5 text-left font-medium">Destinatário</th>
                    <th className="px-3 py-2.5 text-left font-medium">Trajeto</th>
                    <th className="px-3 py-2.5 text-right font-medium whitespace-nowrap">Peso (kg)</th>
                    <th className="px-3 py-2.5 text-right font-medium">km</th>
                    <th className="px-3 py-2.5 text-right font-medium">Parte</th>
                    <th className="px-4 py-2.5 text-right font-medium">Pedágio</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {resultado.itens.map((it, i) => {
                    const nfsRes = (it.numeroNF ?? '').split(',').map(n => n.trim()).filter(Boolean)
                    return (
                    <tr key={i} className="hover:bg-gray-50 transition-colors align-top">
                      <td className="pl-4 pr-2 py-2.5 text-right tabular-nums font-semibold text-gray-500">{i + 1}</td>
                      <td className="px-3 py-2.5 w-[10rem]">
                        {nfsRes.length > 0 ? chipsNf(nfsRes, undefined, false) : <span className="text-gray-500">—</span>}
                      </td>
                      <td className="px-3 py-2.5 text-gray-600 min-w-[8rem]">{it.emitente ?? '—'}</td>
                      <td className="px-3 py-2.5 text-gray-900 font-medium min-w-[10rem]">{it.destinatario ?? '—'}</td>
                      <td className="px-3 py-2.5 text-gray-600">
                        <span className="whitespace-nowrap">{it.cidadeOrigem ?? '—'}</span>
                        <ArrowRight size={13} className="inline mx-1 text-gray-500 align-[-2px]" aria-label="até" />
                        <span className="whitespace-nowrap">{it.cidadeDestino}/{it.uf}</span>
                      </td>
                      <td className="px-3 py-2.5 text-right tabular-nums">{it.pesoKg.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}</td>
                      <td className="px-3 py-2.5 text-right tabular-nums">
                        {it.distanciaKm != null
                          ? it.distanciaKm
                          : <span className="text-red-700 font-semibold whitespace-nowrap">sem rota</span>}
                      </td>
                      <td className="px-3 py-2.5 text-right tabular-nums text-gray-600">
                        {(it.percentualPedagio * 100).toFixed(2).replace('.', ',')}%
                      </td>
                      <td className="pl-3 pr-2 py-1.5 text-right">
                        <div className="flex items-center justify-end gap-1">
                          <span className="font-semibold text-tinta-800 tabular-nums">{formatBRL(it.valorPedagio)}</span>
                          <button
                            type="button"
                            onClick={() => copiarPedagio(it.valorPedagio, i)}
                            aria-label={`Copiar o pedágio do CT-e ${i + 1}`}
                            title="Copiar o pedágio deste CT-e"
                            className={`shrink-0 w-9 h-9 inline-flex items-center justify-center rounded-lg border transition-colors ${
                              copiadoIdx === i
                                ? 'bg-emerald-50 border-emerald-300 text-emerald-800'
                                : 'bg-white border-gray-300 text-gray-600 hover:bg-tinta-50 hover:text-tinta-800 hover:border-tinta-300'
                            }`}
                          >
                            {copiadoIdx === i ? <Check size={15} aria-hidden /> : <Copy size={15} aria-hidden />}
                          </button>
                        </div>
                      </td>
                    </tr>
                    )}
                  )}
                </tbody>
                <tfoot>
                  <tr className="border-t border-gray-200 font-semibold text-gray-900">
                    <td className="px-4 py-2.5" colSpan={5}>Total</td>
                    <td className="px-3 py-2.5 text-right tabular-nums">{resultado.pesoTotal.toLocaleString('pt-BR', { maximumFractionDigits: 2 })}</td>
                    <td colSpan={2} />
                    <td className="pl-3 pr-12 py-2.5 text-right tabular-nums text-tinta-800">{formatBRL(pedagioTotal)}</td>
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>
        </section>
        )
      })()}
      </>}
      </PainelAba>
    </div>
  )
}
