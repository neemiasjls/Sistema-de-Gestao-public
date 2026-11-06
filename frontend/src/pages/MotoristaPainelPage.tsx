import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { CheckCircle2, Fuel, LogOut, Minus, Package, Plus, Send, Trash2, Truck, Wallet, PencilLine, Receipt, NotebookPen } from 'lucide-react'
import toast from 'react-hot-toast'
import { useAuth } from '../contexts/AuthContext'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { BotaoTema } from '../components/EscolhaTema'
import BuscaPlaca from '../components/BuscaPlaca'
import Campo from '../components/ui/Campo'
import { Carregando, ErroCarga } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { brl, hoje, lerNum, num } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import {
  motoristaPainelApi, diariasPelasDatas, normalizarPlaca, placaValida,
  type InicioMotorista, type FolhaMotorista, type SobraAnterior, type PlacasPainel,
} from '../services/viagensCaminhaoApi'

// Painel do motorista (Admin, 06/10/2026): só a folha "RELATÓRIO DE VIAGEM" que
// ele preenchia no papel. Preenche, envia e cai no controle do escritório. Nada de
// lista de viagens, frete ou comissão. Pode corrigir o último envio enquanto o
// escritório não conferiu. O rascunho fica no aparelho. Mostra a sobra da viagem
// anterior do caminhão (Admin, 07/10/2026): ele leva esse dinheiro para esta.
//
// Celular primeiro: letra grande, campo de 48 px, um bloco por assunto, botão de
// enviar fixo embaixo. Números com vírgula (teclado decimal).

// Despesas da folha, na ordem do papel (o diesel pago vai nos abastecimentos)
const DESPESAS_FOLHA = [
  ['CARREGAMENTO', 'Carregamento'], ['GASOLINA', 'Gasolina'], ['BORRACHARIA', 'Borracharia'],
  ['AUTO_ELETRICO', 'Auto elétrico'], ['LAVAGEM', 'Lavagem'], ['LUBRIFICACAO', 'Lubrificação'],
  ['MECANICO', 'Mecânico'], ['DESCARGA', 'Descarga'], ['CRED_CELULAR', 'Crédito de celular'],
] as const

type Abast = { data: string; km: string; litros: string; valor: string; local: string; observacao: string; forma: 'NOTA' | 'PAGO' }
type Outra = { descricao: string; valor: string }
type Carga = { tipo: 'VOLUMES' | 'RETORNO'; transportadora: string; data: string; origem: string; destino: string; valor: string; retornoId: number | null }

type Form = {
  placa: string; placaCarreta: string; destino: string; dataSaida: string; dataChegada: string; kmSaida: string; kmChegada: string
  levou: string; dataDinheiro: string; diarias: number; diariasMexidas: boolean
  despesas: Record<string, string>; outras: Outra[]
  /** Caixinhas (Admin, 06/10/2026): cada uma com valor e de quem é (cliente ou lugar) */
  caixinhas: Outra[]; abastecimentos: Abast[]; cargas: Carga[]; observacoes: string
}

type Rascunho = { versao: 1; modo: 'novo' | 'corrigir'; id: number | null; form: Form; salvoEm: string }

const abastVazio = (): Abast => ({ data: hoje(), km: '', litros: '', valor: '', local: '', observacao: '', forma: 'NOTA' })
const cargaVazia = (tipo: Carga['tipo']): Carga => ({ tipo, transportadora: '', data: hoje(), origem: '', destino: '', valor: '', retornoId: null })

const formNovo = (_ini: InicioMotorista): Form => ({
  // Sem placa padrão (VIA-20): cavalo e carreta são escolhidos a cada viagem
  placa: '', placaCarreta: '', destino: '', dataSaida: hoje(), dataChegada: '',
  // A data do dinheiro abre em branco: só é pedida quando ele levou dinheiro (VIA-04)
  kmSaida: '', kmChegada: '', levou: '', dataDinheiro: '', diarias: 0, diariasMexidas: false,
  despesas: {}, outras: [], caixinhas: [], abastecimentos: [], cargas: [cargaVazia('VOLUMES')], observacoes: '',
})

const txt = (n: number | null | undefined) => (n == null || n === 0 ? '' : num(n, n % 1 === 0 ? 0 : 2))

function formDaFolha(f: FolhaMotorista): Form {
  const despesas: Record<string, string> = {}
  const outras: Outra[] = []
  const caixinhas: Outra[] = []
  for (const d of f.despesas) {
    if (d.tipo === 'OUTRA') outras.push({ descricao: d.descricao ?? '', valor: txt(d.valor) })
    else if (d.tipo === 'CAIXINHA') caixinhas.push({ descricao: d.descricao ?? '', valor: txt(d.valor) })
    else despesas[d.tipo] = txt(lerNum(despesas[d.tipo] ?? '') + d.valor)
  }
  return {
    placa: f.placa, placaCarreta: f.placaCarreta ?? '', destino: f.destino ?? '', dataSaida: f.dataSaida ?? '',
    dataChegada: f.dataChegada ?? '', kmSaida: f.kmSaida ? String(f.kmSaida) : '', kmChegada: f.kmChegada ? String(f.kmChegada) : '',
    levou: txt(f.levou), dataDinheiro: f.dataDinheiro ?? '', diarias: f.diariasQuantidade, diariasMexidas: true,
    despesas, outras, caixinhas, observacoes: f.observacoes ?? '',
    abastecimentos: f.abastecimentos.map(a => ({
      data: a.data ?? '', km: a.km ? String(a.km) : '', litros: txt(a.litros), valor: txt(a.valor),
      local: a.local ?? '', observacao: a.observacao ?? '', forma: a.forma,
    })),
    cargas: f.cargas.map(c => ({
      tipo: c.tipo, transportadora: c.transportadora ?? '', data: c.data ?? '', origem: c.origem ?? '',
      destino: c.destino ?? '', valor: txt(c.valor), retornoId: c.retornoId,
    })),
  }
}

function folhaDoForm(f: Form): FolhaMotorista {
  const inteiro = (s: string) => { const n = Math.round(lerNum(s)); return n > 0 ? n : null }
  return {
    placa: normalizarPlaca(f.placa), placaCarreta: f.placaCarreta.trim() ? normalizarPlaca(f.placaCarreta) : null,
    destino: f.destino.trim() || null, dataSaida: f.dataSaida || null, dataChegada: f.dataChegada || null,
    kmSaida: inteiro(f.kmSaida), kmChegada: inteiro(f.kmChegada), litros: null,
    levou: lerNum(f.levou), dataDinheiro: f.dataDinheiro || null, diariasQuantidade: f.diarias, observacoes: f.observacoes.trim() || null,
    abastecimentos: f.abastecimentos
      .filter(a => lerNum(a.valor) > 0 || lerNum(a.litros) > 0)
      .map(a => ({ data: a.data || null, km: inteiro(a.km), litros: lerNum(a.litros) || null, valor: lerNum(a.valor),
                   local: a.local.trim() || null, observacao: a.observacao.trim() || null, forma: a.forma })),
    despesas: [
      ...DESPESAS_FOLHA.filter(([t]) => lerNum(f.despesas[t] ?? '') > 0)
        .map(([t]) => ({ tipo: t as string, descricao: null, valor: lerNum(f.despesas[t]), observacao: null })),
      ...f.caixinhas.filter(o => lerNum(o.valor) > 0)
        .map(o => ({ tipo: 'CAIXINHA', descricao: o.descricao.trim() || null, valor: lerNum(o.valor), observacao: null })),
      ...f.outras.filter(o => lerNum(o.valor) > 0)
        .map(o => ({ tipo: 'OUTRA', descricao: o.descricao.trim() || null, valor: lerNum(o.valor), observacao: null })),
    ],
    cargas: f.cargas.map(c => ({
      tipo: c.tipo, transportadora: c.tipo === 'RETORNO' ? c.transportadora.trim() || null : null, data: c.data || null,
      origem: c.origem.trim() || null, destino: c.destino.trim() || null, valor: c.tipo === 'RETORNO' ? lerNum(c.valor) : 0,
      retornoId: c.retornoId,
    })).filter(c => c.tipo === 'RETORNO' || c.origem || c.destino || c.data),
  }
}

const naLista = (placa: string, lista: string[]) => lista.includes(normalizarPlaca(placa))

function problemas(f: Form, placas: PlacasPainel): string[] {
  const p: string[] = []
  if (!naLista(f.placa, placas.cavalos)) p.push('Cavalo: escolha uma placa da lista.')
  if (!naLista(f.placaCarreta, placas.carretas)) p.push('Carreta: escolha uma placa da lista.')
  if (!f.dataSaida) p.push('Data de saída.')
  if (f.dataChegada && f.dataSaida && f.dataChegada < f.dataSaida) p.push('A chegada está antes da saída.')
  if (lerNum(f.levou) > 0 && !f.dataDinheiro) p.push('Data em que pegou o dinheiro.')
  f.caixinhas.forEach((o, i) => { if (lerNum(o.valor) > 0 && !o.descricao.trim()) p.push(`Caixinha ${i + 1}: diga de quem é.`) })
  f.outras.forEach((o, i) => { if (lerNum(o.valor) > 0 && !o.descricao.trim()) p.push(`Outra despesa ${i + 1}: diga o que foi.`) })
  f.cargas.forEach((c, i) => {
    if (c.tipo === 'RETORNO' && lerNum(c.valor) <= 0) p.push(`Carga ${i + 1}: valor do frete.`)
    if (c.tipo === 'RETORNO' && !c.transportadora.trim()) p.push(`Carga ${i + 1}: transportadora.`)
  })
  return p
}

export default function MotoristaPainelPage() {
  const { usuario, logout } = useAuth()
  const [ini, setIni] = useState<InicioMotorista | null>(null)
  const [placas, setPlacas] = useState<PlacasPainel>({ cavalos: [], carretas: [] })
  const [falhou, setFalhou] = useState(false)
  // Usuário de motorista sem cadastro (ou cadastro desativado): aviso do escritório
  const [semCadastro, setSemCadastro] = useState<string | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [modo, setModo] = useState<{ modo: 'novo' | 'corrigir'; id: number | null }>({ modo: 'novo', id: null })
  const [enviado, setEnviado] = useState<{ id: number; enviadoEm: string } | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [tentou, setTentou] = useState(false)
  const [restaurado, setRestaurado] = useState(false)
  const confirmar = useConfirmar()
  const chave = `trx_folha_motorista_${usuario?.id ?? 0}`

  const carregar = useCallback(async () => {
    setFalhou(false)
    try {
      const [r, pl] = await Promise.all([motoristaPainelApi.inicio(), motoristaPainelApi.placas()])
      setIni(r)
      setPlacas(pl)
      // Rascunho no aparelho (vale 14 dias): volta onde parou
      let rasc: Rascunho | null = null
      try {
        const s = localStorage.getItem(chave)
        if (s) rasc = JSON.parse(s) as Rascunho
        if (rasc && (rasc.versao !== 1 || Date.now() - Date.parse(rasc.salvoEm) > 14 * 86_400_000)) rasc = null
      } catch { rasc = null }
      // Rascunho de correção só vale se aquele envio ainda pode ser corrigido
      // Rascunho de antes da caixinha com nome: o valor único vira uma caixinha sem nome
      if (rasc && !Array.isArray(rasc.form.caixinhas)) {
        const { CAIXINHA, ...resto } = rasc.form.despesas ?? {}
        rasc.form = { ...rasc.form, despesas: resto, caixinhas: CAIXINHA ? [{ descricao: '', valor: CAIXINHA }] : [] }
      }
      if (rasc?.modo === 'corrigir' && !(r.ultima?.podeCorrigir && r.ultima.id === rasc.id)) rasc = null
      if (rasc) { setForm(rasc.form); setModo({ modo: rasc.modo, id: rasc.id }); setRestaurado(true) }
      else setForm(formNovo(r))
      setSemCadastro(null)
    } catch (e) {
      const resp = (e as { response?: { status?: number; data?: { semCadastro?: boolean; erro?: string } } })?.response
      if (resp?.status === 403 && resp.data?.semCadastro) setSemCadastro(resp.data.erro ?? 'Seu usuário ainda não tem cadastro de motorista. Fale com o escritório.')
      else setFalhou(true)
    }
  }, [chave])
  useEffect(() => { carregar() }, [carregar])

  // Guarda o rascunho a cada mudança (sem travar a digitação)
  const timer = useRef<number>()
  useEffect(() => {
    if (!form || enviado) return
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => {
      try { localStorage.setItem(chave, JSON.stringify({ versao: 1, modo: modo.modo, id: modo.id, form, salvoEm: new Date().toISOString() } satisfies Rascunho)) }
      catch { /* sem armazenamento: segue sem rascunho */ }
    }, 400)
    return () => window.clearTimeout(timer.current)
  }, [form, modo, chave, enviado])

  const apagarRascunho = () => { try { localStorage.removeItem(chave) } catch { /* nada */ } }

  // Sobra da viagem anterior DELE (VIA-21, 02/11/2026: cadeia por motorista, com qualquer
  // placa; era pela placa do cavalo). Busca de novo quando muda a data de saída; ao
  // corrigir, o próprio envio sai da conta. Só leitura.
  const [sobraAnt, setSobraAnt] = useState<{ estado: 'semPlaca' | 'carregando' | 'erro' } | { estado: 'ok'; dados: SobraAnterior }>({ estado: 'semPlaca' })
  const [tentarSobra, setTentarSobra] = useState(0)
  const placaSobra = form && placaValida(form.placa) ? normalizarPlaca(form.placa) : ''
  const dataSobra = form?.dataSaida || null
  const idSobra = modo.modo === 'corrigir' ? modo.id : null
  const temIni = !!ini
  useEffect(() => {
    if (!temIni) { setSobraAnt({ estado: 'semPlaca' }); return }
    setSobraAnt({ estado: 'carregando' })
    const ctrl = new AbortController()
    const t = window.setTimeout(() => {
      motoristaPainelApi.sobraAnterior(placaSobra, dataSobra, idSobra, ctrl.signal)
        .then(d => setSobraAnt({ estado: 'ok', dados: d }))
        .catch(() => { if (!ctrl.signal.aborted) setSobraAnt({ estado: 'erro' }) })
    }, 350)
    return () => { window.clearTimeout(t); ctrl.abort() }
  }, [placaSobra, dataSobra, idSobra, temIni, tentarSobra])

  const muda = <K extends keyof Form>(k: K, v: Form[K]) => setForm(f => f && ({ ...f, [k]: v }))

  // Diárias pelas datas enquanto ele não mexer à mão
  useEffect(() => {
    if (!form || form.diariasMexidas) return
    const d = diariasPelasDatas(form.dataSaida, form.dataChegada)
    if (d !== form.diarias) setForm(f => f && ({ ...f, diarias: d }))
  }, [form?.dataSaida, form?.dataChegada, form?.diariasMexidas]) // eslint-disable-line react-hooks/exhaustive-deps

  const contas = useMemo(() => {
    if (!form || !ini) return null
    const dieselPago = form.abastecimentos.filter(a => a.forma === 'PAGO').reduce((s, a) => s + lerNum(a.valor), 0)
    const dieselNota = form.abastecimentos.filter(a => a.forma === 'NOTA').reduce((s, a) => s + lerNum(a.valor), 0)
    const diarias = form.diarias * ini.valorDiaria
    const outras = DESPESAS_FOLHA.reduce((s, [t]) => s + lerNum(form.despesas[t] ?? ''), 0) + form.outras.reduce((s, o) => s + lerNum(o.valor), 0)
      + form.caixinhas.reduce((s, o) => s + lerNum(o.valor), 0)
    const pagas = dieselPago + diarias + outras
    const levou = lerNum(form.levou)
    // Sem viagem anterior (ou ainda sem placa) a sobra anterior conta 0, igual ao escritório
    const anterior = sobraAnt.estado === 'ok' ? sobraAnt.dados.sobra : 0
    const dinheiro = anterior + levou
    return { dieselPago, dieselNota, diarias, outras, pagas, levou, anterior, dinheiro, sobra: dinheiro - pagas }
  }, [form, ini, sobraAnt])

  const textoSobraAnt = sobraAnt.estado === 'ok'
    ? sobraAnt.dados.temAnterior
      ? `${brl(sobraAnt.dados.sobra)}${sobraAnt.dados.dataViagemAnterior ? ` (viagem de ${diaMes(sobraAnt.dados.dataViagemAnterior)})` : ''}`
      : 'Sem viagem anterior'
    : sobraAnt.estado === 'carregando' ? 'Buscando...'
    : sobraAnt.estado === 'erro' ? 'Não consegui buscar'
    : 'Informe a placa'

  const enviar = async () => {
    if (!form) return
    setTentou(true)
    const p = problemas(form, placas)
    if (p.length > 0) { window.scrollTo({ top: 0, behavior: 'smooth' }); return }
    if (!await confirmar({
      titulo: modo.modo === 'corrigir' ? 'Mandar a correção para o escritório?' : 'Enviar a viagem para o escritório?',
      texto: `${normalizarPlaca(form.placa)}${form.destino ? ` · ${form.destino}` : ''}`,
      confirmar: modo.modo === 'corrigir' ? 'Mandar correção' : 'Enviar', perigo: false,
    })) return
    setEnviando(true)
    try {
      const folha = folhaDoForm(form)
      const r = modo.modo === 'corrigir' && modo.id
        ? await motoristaPainelApi.corrigir(modo.id, folha)
        : await motoristaPainelApi.enviar(folha)
      apagarRascunho()
      setEnviado(r)
      setTentou(false)
      window.scrollTo({ top: 0 })
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui enviar. Confira a internet e tente de novo; o que você digitou continua guardado.'))
    } finally {
      setEnviando(false)
    }
  }

  const novaViagem = async () => {
    const r = await motoristaPainelApi.inicio().catch(() => ini)
    if (r) { setIni(r); setForm(formNovo(r)) }
    setModo({ modo: 'novo', id: null }); setEnviado(null); setRestaurado(false); setTentou(false)
    window.scrollTo({ top: 0 })
  }

  const corrigirUltimo = async () => {
    try {
      const r = await motoristaPainelApi.inicio()
      setIni(r)
      if (!r.ultima?.podeCorrigir || !r.ultima.folha) { toast.error('O escritório já conferiu esta viagem. Fale com o escritório para corrigir.'); return }
      setForm(formDaFolha(r.ultima.folha)); setModo({ modo: 'corrigir', id: r.ultima.id }); setEnviado(null); setRestaurado(false)
      window.scrollTo({ top: 0 })
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui abrir o último envio.')) }
  }

  const descartar = async () => {
    if (!ini) return
    if (!await confirmar({ titulo: 'Apagar o que foi digitado?', texto: 'A folha volta em branco.', confirmar: 'Apagar' })) return
    apagarRascunho(); setForm(formNovo(ini)); setModo({ modo: 'novo', id: null }); setRestaurado(false); setTentou(false)
  }

  const lista = form ? problemas(form, placas) : []

  return (
    <div className="min-h-tela bg-gray-50 text-gray-900 text-[17px]">
      <header className="bg-menu text-white">
        <div className="max-w-xl mx-auto px-4 h-16 flex items-center gap-3">
          <span className="w-9 h-9 rounded-full bg-[#fff] flex items-center justify-center shrink-0" aria-hidden>
            <span className="w-[18px] h-[18px] rounded-full bg-gema-400" />
          </span>
          <div className="flex-1 min-w-0">
            <p className="font-display text-lg font-semibold leading-tight truncate">Relatório de viagem</p>
            <p className="text-sm text-tinta-200 truncate">{ini ? ini.nome : 'TRX Transportes'}</p>
          </div>
          <BotaoTema />
          <button type="button" onClick={logout}
                  className="inline-flex items-center gap-1.5 min-h-12 px-3 rounded-lg text-tinta-100 hover:bg-white/10 font-medium">
            <LogOut size={20} aria-hidden /> Sair
          </button>
        </div>
      </header>

      <main className="max-w-xl mx-auto px-4 pt-4 pb-36 space-y-4">
        {semCadastro ? (
          <Aviso tipo="atencao" titulo="Falta o seu cadastro">{semCadastro}</Aviso>
        ) : falhou ? (
          <ErroCarga mensagem="Não consegui abrir a folha." onTentar={carregar} />
        ) : !ini || !form ? (
          <Carregando linhas={4} rotulo="Abrindo a folha" />
        ) : enviado ? (
          <section className="bg-white border border-gray-200 rounded-2xl p-6 text-center space-y-4 animate-entrada" aria-live="polite">
            <CheckCircle2 size={56} strokeWidth={1.75} className="mx-auto text-emerald-700" aria-hidden />
            <div>
              <h1 className="font-display text-3xl font-semibold">Enviado</h1>
              <p className="text-gray-600 mt-1">
                O escritório já recebeu{enviado.enviadoEm ? ` (${new Date(enviado.enviadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })})` : ''}.
                Enquanto não for conferida, dá para corrigir.
              </p>
            </div>
            <div className="flex flex-col gap-2">
              <Botao variante="primario" icone={Plus} onClick={novaViagem} className="min-h-14 text-lg">Começar nova viagem</Botao>
              <Botao variante="secundario" icone={PencilLine} onClick={corrigirUltimo} className="min-h-14 text-lg">Corrigir este envio</Botao>
            </div>
          </section>
        ) : (
          <>
            {modo.modo === 'corrigir' && (
              <Aviso tipo="info" titulo="Corrigindo o último envio">
                Mude o que precisar e toque em <b>Mandar correção</b>. Ela troca o envio anterior.
              </Aviso>
            )}
            {modo.modo === 'novo' && ini.ultima?.podeCorrigir && !restaurado && (
              <Aviso tipo="info" titulo="Seu último envio ainda não foi conferido"
                     acao={<Botao variante="secundario" tamanho="sm" icone={PencilLine} onClick={corrigirUltimo}>Corrigir o último envio</Botao>}>
                {ini.ultima.placa}{ini.ultima.destino ? ` · ${ini.ultima.destino}` : ''}. Para uma viagem nova, é só preencher abaixo.
              </Aviso>
            )}
            {restaurado && (
              <Aviso tipo="info" onFechar={() => setRestaurado(false)}>Voltei com o que você tinha digitado neste aparelho.</Aviso>
            )}
            {tentou && lista.length > 0 && (
              <Aviso tipo="erro" titulo="Falta preencher" itens={lista} />
            )}

            <Bloco icone={Truck} titulo="Caminhão e viagem">
              <div className="grid grid-cols-2 gap-3">
                <CampoG rotulo="Cavalo" erro={tentou && !naLista(form.placa, placas.cavalos)}>
                  <BuscaPlaca rotulo="Placa do cavalo" valor={form.placa} onChange={v => muda('placa', v)} opcoes={placas.cavalos}
                              placeholder="Buscar placa" className={entrada} />
                </CampoG>
                <CampoG rotulo="Carreta" erro={tentou && !naLista(form.placaCarreta, placas.carretas)}>
                  <BuscaPlaca rotulo="Placa da carreta" valor={form.placaCarreta} onChange={v => muda('placaCarreta', v)} opcoes={placas.carretas}
                              placeholder="Buscar placa" className={entrada} />
                </CampoG>
              </div>
              <CampoG rotulo="Destino">
                <input value={form.destino} onChange={e => muda('destino', e.target.value)} placeholder="Aldeia / Goiânia" className={entrada} />
              </CampoG>
              <div className="grid grid-cols-2 gap-3">
                <CampoG rotulo="Data de saída" erro={tentou && !form.dataSaida}>
                  <input type="date" value={form.dataSaida} onChange={e => muda('dataSaida', e.target.value)} className={entrada} />
                </CampoG>
                <CampoG rotulo="Data de chegada">
                  <input type="date" value={form.dataChegada} min={form.dataSaida || undefined} onChange={e => muda('dataChegada', e.target.value)} className={entrada} />
                </CampoG>
                <CampoG rotulo="Km na saída" dica="Se tiver">
                  <input inputMode="numeric" value={form.kmSaida} onChange={e => muda('kmSaida', e.target.value.replace(/\D/g, ''))} className={entrada} />
                </CampoG>
                <CampoG rotulo="Km na chegada" dica="Se tiver">
                  <input inputMode="numeric" value={form.kmChegada} onChange={e => muda('kmChegada', e.target.value.replace(/\D/g, ''))} className={entrada} />
                </CampoG>
              </div>
            </Bloco>

            <Bloco icone={Wallet} titulo="Dinheiro da viagem">
              <div className="rounded-xl bg-gray-50 border border-gray-200 px-3 py-2.5" aria-live="polite">
                <div className="flex items-start justify-between gap-3">
                  <p className="text-gray-700">Sobra da viagem anterior</p>
                  <p className={`text-right font-medium ${sobraAnt.estado === 'ok' && sobraAnt.dados.temAnterior ? 'tabular-nums' : 'text-gray-600'}`}>
                    {textoSobraAnt}
                  </p>
                </div>
                {sobraAnt.estado === 'ok' && sobraAnt.dados.temAnterior && (
                  <p className="text-sm text-gray-500">Do caminhão {placaSobra}. Não dá para mudar aqui.</p>
                )}
                {sobraAnt.estado === 'ok' && !sobraAnt.dados.temAnterior && (
                  <p className="text-sm text-gray-500">O caminhão {placaSobra} não tem viagem antes desta: conta R$ 0,00.</p>
                )}
                {sobraAnt.estado === 'erro' && (
                  <p className="text-sm text-gray-600">
                    A conta segue sem ela por enquanto.{' '}
                    <button type="button" onClick={() => setTentarSobra(n => n + 1)} className="font-medium text-tinta-700 underline underline-offset-2 min-h-11">Tentar de novo</button>
                  </p>
                )}
              </div>
              <div className="grid grid-cols-2 gap-3">
                <CampoG rotulo="Dinheiro que levou">
                  <Dinheiro valor={form.levou} onChange={v => muda('levou', v)} />
                </CampoG>
                <CampoG rotulo="Data em que pegou" erro={tentou && lerNum(form.levou) > 0 && !form.dataDinheiro}>
                  <input type="date" value={form.dataDinheiro} onChange={e => muda('dataDinheiro', e.target.value)} className={entrada} />
                </CampoG>
              </div>
              {contas && (
                <div className="flex items-start justify-between gap-3 border-t border-gray-200 pt-2">
                  <div className="min-w-0">
                    <p className="font-semibold">Dinheiro para esta viagem</p>
                    <p className="text-sm text-gray-500">Sobra anterior + o que levou</p>
                  </div>
                  <p className="tabular-nums whitespace-nowrap font-display text-xl font-semibold">{brl(contas.dinheiro)}</p>
                </div>
              )}
            </Bloco>

            <Bloco icone={Fuel} titulo="Abastecimentos">
              {form.abastecimentos.length === 0 && <p className="text-gray-600">Nenhum abastecimento ainda.</p>}
              {form.abastecimentos.map((a, i) => {
                const set = (p: Partial<Abast>) => muda('abastecimentos', form.abastecimentos.map((x, j) => j === i ? { ...x, ...p } : x))
                return (
                  <fieldset key={i} className="rounded-xl border border-gray-200 p-3 space-y-3">
                    <legend className="sr-only">Abastecimento {i + 1}</legend>
                    <div className="flex items-center justify-between">
                      <p className="font-semibold">Abastecimento {i + 1}</p>
                      <BotaoIcone icone={Trash2} rotulo={`Tirar abastecimento ${i + 1}`} perigo
                                  onClick={() => muda('abastecimentos', form.abastecimentos.filter((_, j) => j !== i))} />
                    </div>
                    <div role="radiogroup" aria-label="Quem pagou" className="grid grid-cols-2 gap-2">
                      <Opcao ativa={a.forma === 'NOTA'} onClick={() => set({ forma: 'NOTA' })} titulo="Nota / pix" sub="A empresa paga" />
                      <Opcao ativa={a.forma === 'PAGO'} onClick={() => set({ forma: 'PAGO' })} titulo="Paguei" sub="Com o dinheiro da viagem" />
                    </div>
                    <div className="grid grid-cols-2 gap-3">
                      <CampoG rotulo="Data"><input type="date" value={a.data} onChange={e => set({ data: e.target.value })} className={entrada} /></CampoG>
                      <CampoG rotulo="Km"><input inputMode="numeric" value={a.km} onChange={e => set({ km: e.target.value.replace(/\D/g, '') })} className={entrada} /></CampoG>
                      <CampoG rotulo="Litros"><input inputMode="decimal" value={a.litros} onChange={e => set({ litros: e.target.value })} className={entrada} /></CampoG>
                      <CampoG rotulo="Valor"><Dinheiro valor={a.valor} onChange={v => set({ valor: v })} /></CampoG>
                    </div>
                    <CampoG rotulo="Posto / cidade"><input value={a.local} onChange={e => set({ local: e.target.value })} placeholder="Posto 7, Itumbiara" className={entrada} /></CampoG>
                    <CampoG rotulo="Observação"><input value={a.observacao} onChange={e => set({ observacao: e.target.value })} className={entrada} /></CampoG>
                  </fieldset>
                )
              })}
              <Botao variante="secundario" icone={Plus} onClick={() => muda('abastecimentos', [...form.abastecimentos, abastVazio()])} className="min-h-12 w-full">
                Adicionar abastecimento
              </Botao>
            </Bloco>

            <Bloco icone={Receipt} titulo="Despesas">
              <div className="rounded-xl border border-gray-200 p-3">
                <p className="font-semibold">Diárias</p>
                <p className="text-sm text-gray-600">R$ {num(ini.valorDiaria, 0)} por dia, meia diária R$ {num(ini.valorDiaria / 2, 0)}. Sai pelas datas; ajuste se precisar.</p>
                <div className="flex items-center gap-3 mt-3">
                  <BotaoIcone icone={Minus} rotulo="Meia diária a menos" className="!w-14 !h-14 border border-gray-300"
                              onClick={() => setForm(f => f && ({ ...f, diarias: Math.max(0, f.diarias - 0.5), diariasMexidas: true }))} />
                  <div className="flex-1 text-center" aria-live="polite">
                    <p className="font-display text-3xl font-semibold tabular-nums">{num(form.diarias, form.diarias % 1 ? 1 : 0)}</p>
                    <p className="text-sm text-gray-600 tabular-nums">{brl(form.diarias * ini.valorDiaria)}</p>
                  </div>
                  <BotaoIcone icone={Plus} rotulo="Meia diária a mais" className="!w-14 !h-14 border border-gray-300"
                              onClick={() => setForm(f => f && ({ ...f, diarias: f.diarias + 0.5, diariasMexidas: true }))} />
                </div>
                {form.diariasMexidas && (
                  <button type="button" className="mt-2 text-sm font-medium text-tinta-700 underline underline-offset-2 min-h-11"
                          onClick={() => setForm(f => f && ({ ...f, diariasMexidas: false }))}>
                    Voltar para o cálculo pelas datas ({num(diariasPelasDatas(form.dataSaida, form.dataChegada), 0)})
                  </button>
                )}
              </div>
              <div className="grid grid-cols-2 gap-3">
                {DESPESAS_FOLHA.map(([t, nome]) => (
                  <CampoG key={t} rotulo={nome}>
                    <Dinheiro valor={form.despesas[t] ?? ''} onChange={v => muda('despesas', { ...form.despesas, [t]: v })} />
                  </CampoG>
                ))}
              </div>
              <div className="rounded-xl border border-gray-200 p-3 space-y-3">
                <div>
                  <p className="font-semibold">Caixinha</p>
                  <p className="text-sm text-gray-600">Uma para cada pessoa ou lugar: o valor e de quem é.</p>
                </div>
                <datalist id="caixinha-quem">{(ini.sugestoesCaixinha ?? []).map(n => <option key={n} value={n} />)}</datalist>
                {form.caixinhas.map((o, i) => (
                  <div key={i} className="space-y-2 border-t border-gray-100 pt-3 first-of-type:border-0 first-of-type:pt-0">
                    <CampoG rotulo={`Caixinha ${i + 1}: de quem`} erro={tentou && lerNum(o.valor) > 0 && !o.descricao.trim()}>
                      <input list="caixinha-quem" value={o.descricao} placeholder="Cliente ou lugar" autoCapitalize="characters"
                             onChange={e => muda('caixinhas', form.caixinhas.map((x, j) => j === i ? { ...x, descricao: e.target.value.toUpperCase() } : x))} className={entrada} />
                    </CampoG>
                    <div className="grid grid-cols-[1fr_auto] gap-2 items-end">
                      <CampoG rotulo="Valor"><Dinheiro valor={o.valor} onChange={v => muda('caixinhas', form.caixinhas.map((x, j) => j === i ? { ...x, valor: v } : x))} /></CampoG>
                      <BotaoIcone icone={Trash2} rotulo={`Tirar caixinha ${i + 1}`} perigo className="mb-0.5"
                                  onClick={() => muda('caixinhas', form.caixinhas.filter((_, j) => j !== i))} />
                    </div>
                  </div>
                ))}
                <Botao variante="secundario" icone={Plus} onClick={() => muda('caixinhas', [...form.caixinhas, { descricao: '', valor: '' }])} className="min-h-12 w-full">
                  Adicionar caixinha
                </Botao>
              </div>
              {form.outras.map((o, i) => (
                <div key={i} className="rounded-xl border border-gray-200 p-3 space-y-3">
                  <CampoG rotulo={`Outra despesa ${i + 1}`} erro={tentou && lerNum(o.valor) > 0 && !o.descricao.trim()}>
                    <input value={o.descricao} placeholder="O que foi (estacionamento, peça...)" onChange={e => muda('outras', form.outras.map((x, j) => j === i ? { ...x, descricao: e.target.value } : x))} className={entrada} />
                  </CampoG>
                  <div className="grid grid-cols-[1fr_auto] gap-2 items-end">
                    <CampoG rotulo="Valor"><Dinheiro valor={o.valor} onChange={v => muda('outras', form.outras.map((x, j) => j === i ? { ...x, valor: v } : x))} /></CampoG>
                    <BotaoIcone icone={Trash2} rotulo={`Tirar outra despesa ${i + 1}`} perigo className="mb-0.5"
                                onClick={() => muda('outras', form.outras.filter((_, j) => j !== i))} />
                  </div>
                </div>
              ))}
              <Botao variante="secundario" icone={Plus} onClick={() => muda('outras', [...form.outras, { descricao: '', valor: '' }])} className="min-h-12 w-full">
                Outra despesa
              </Botao>
            </Bloco>

            <Bloco icone={Package} titulo="Cargas e retornos">
              {form.cargas.length === 0 && <p className="text-gray-600">Nenhuma carga anotada.</p>}
              {form.cargas.map((c, i) => {
                const set = (p: Partial<Carga>) => muda('cargas', form.cargas.map((x, j) => j === i ? { ...x, ...p } : x))
                return (
                  <fieldset key={i} className="rounded-xl border border-gray-200 p-3 space-y-3">
                    <legend className="sr-only">Carga {i + 1}</legend>
                    <div className="flex items-center justify-between">
                      <p className="font-semibold">Carga {i + 1}</p>
                      <BotaoIcone icone={Trash2} rotulo={`Tirar carga ${i + 1}`} perigo onClick={() => muda('cargas', form.cargas.filter((_, j) => j !== i))} />
                    </div>
                    <div role="radiogroup" aria-label="Tipo de carga" className="grid grid-cols-2 gap-2">
                      <Opcao ativa={c.tipo === 'VOLUMES'} onClick={() => set({ tipo: 'VOLUMES' })} titulo="Volumes" sub="Carga da fazenda" />
                      <Opcao ativa={c.tipo === 'RETORNO'} onClick={() => set({ tipo: 'RETORNO' })} titulo="Outra carga" sub="Retorno, frete" />
                    </div>
                    {c.tipo === 'RETORNO' && (
                      <CampoG rotulo="Transportadora" erro={tentou && !c.transportadora.trim()}>
                        <input value={c.transportadora} onChange={e => set({ transportadora: e.target.value })} placeholder="Logix Logística" className={entrada} />
                      </CampoG>
                    )}
                    <div className="grid grid-cols-2 gap-3">
                      <CampoG rotulo="Data do carregamento"><input type="date" value={c.data} onChange={e => set({ data: e.target.value })} className={entrada} /></CampoG>
                      {c.tipo === 'RETORNO' && (
                        <CampoG rotulo="Valor do frete" erro={tentou && lerNum(c.valor) <= 0}><Dinheiro valor={c.valor} onChange={v => set({ valor: v })} /></CampoG>
                      )}
                      <CampoG rotulo="Origem"><input value={c.origem} onChange={e => set({ origem: e.target.value })} className={entrada} /></CampoG>
                      <CampoG rotulo="Destino"><input value={c.destino} onChange={e => set({ destino: e.target.value })} className={entrada} /></CampoG>
                    </div>
                  </fieldset>
                )
              })}
              {/* O tipo se escolhe no cartão; a primeira costuma ser a de volumes */}
              <Botao variante="secundario" icone={Plus} className="min-h-12 w-full"
                     onClick={() => muda('cargas', [...form.cargas, cargaVazia(form.cargas.some(c => c.tipo === 'VOLUMES') ? 'RETORNO' : 'VOLUMES')])}>
                Adicionar carga
              </Botao>
            </Bloco>

            <Bloco icone={NotebookPen} titulo="Observações">
              <textarea value={form.observacoes} onChange={e => muda('observacoes', e.target.value)} rows={3}
                        aria-label="Observações" placeholder="Algo que o escritório precisa saber"
                        className="w-full rounded-xl border border-gray-300 bg-white px-3 py-3 text-[17px]" />
            </Bloco>

            {contas && (
              <section aria-labelledby="mot-totais" className="bg-white border border-gray-200 rounded-2xl p-4 space-y-2">
                <h2 id="mot-totais" className="font-display text-xl font-semibold">Totais da folha</h2>
                <Linha rotulo="Sobra da viagem anterior" valor={sobraAnt.estado === 'ok' && sobraAnt.dados.temAnterior ? brl(contas.anterior) : textoSobraAnt}
                       detalhe={sobraAnt.estado === 'erro' ? 'Conta sem ela até conseguir buscar' : undefined} />
                <Linha rotulo="Dinheiro que levou" valor={brl(contas.levou)} />
                <Linha rotulo="Dinheiro para esta viagem" valor={brl(contas.dinheiro)} />
                <Linha rotulo="Despesas pagas com o dinheiro" valor={brl(contas.pagas)}
                       detalhe={`Diesel pago ${brl(contas.dieselPago)} · diárias ${brl(contas.diarias)} · outras ${brl(contas.outras)}`} />
                {contas.dieselNota > 0 && <Linha rotulo="Diesel na nota / pix (empresa)" valor={brl(contas.dieselNota)} detalhe="Não sai do seu dinheiro" />}
                <div className="border-t border-gray-200 pt-2">
                  <Linha rotulo="Sobra da viagem" valor={brl(contas.sobra)} forte />
                </div>
              </section>
            )}

            <button type="button" onClick={descartar} className="w-full min-h-12 text-gray-600 font-medium underline underline-offset-2">
              Apagar o que digitei e começar de novo
            </button>
          </>
        )}
      </main>

      {form && ini && !enviado && (
        <div className="fixed bottom-0 inset-x-0 bg-white/95 backdrop-blur border-t border-gray-200 pb-[env(safe-area-inset-bottom)]">
          <div className="max-w-xl mx-auto px-4 py-3">
            <Botao variante="primario" icone={Send} onClick={enviar} carregando={enviando} textoCarregando="Enviando..." bloco className="min-h-14 text-lg">
              {modo.modo === 'corrigir' ? 'Mandar correção' : 'Enviar para o escritório'}
            </Botao>
            <p className="text-center text-xs text-gray-500 mt-1.5">Rascunho guardado neste aparelho até enviar.</p>
          </div>
        </div>
      )}
    </div>
  )
}

const entrada = 'w-full min-w-0 min-h-12 rounded-xl border border-gray-300 bg-white px-3 text-[17px] text-gray-900 tabular-nums focus:border-tinta-500'

function Bloco({ icone: Icone, titulo, children }: { icone: typeof Truck; titulo: string; children: ReactNode }) {
  return (
    <section className="bg-white border border-gray-200 rounded-2xl p-4 space-y-3">
      <h2 className="font-display text-xl font-semibold flex items-center gap-2">
        <Icone size={22} className="text-tinta-600" aria-hidden /> {titulo}
      </h2>
      {children}
    </section>
  )
}

// Campo da peça base (rótulo ligado ao campo, erro lido pelo leitor de tela), com letra maior
function CampoG({ rotulo, dica, erro, children }: { rotulo: string; dica?: string; erro?: boolean; children: ReactNode }) {
  return (
    <Campo rotulo={rotulo} dica={dica} erro={erro ? 'Falta preencher' : undefined}
           className="min-w-0 [&>label]:!text-[15px] [&>label]:!mb-1.5">
      {children}
    </Campo>
  )
}

function Dinheiro({ valor, onChange }: { valor: string; onChange: (v: string) => void }) {
  return (
    <span className="relative block">
      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden>R$</span>
      <input inputMode="decimal" value={valor} placeholder="0,00" onChange={e => onChange(e.target.value.replace(/[^\d.,]/g, ''))}
             className={`${entrada} pl-10`} />
    </span>
  )
}

function Opcao({ ativa, onClick, titulo, sub }: { ativa: boolean; onClick: () => void; titulo: string; sub: string }) {
  return (
    <button type="button" role="radio" aria-checked={ativa} onClick={onClick} aria-label={`${titulo}: ${sub}`}
            className={`min-h-14 rounded-xl border-2 px-3 py-2 text-left transition-colors
              ${ativa ? 'border-tinta-600 bg-tinta-50 text-tinta-900' : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300'}`}>
      <span className="block font-semibold">{titulo}</span>
      <span className="block text-sm opacity-80">{sub}</span>
    </button>
  )
}

function Linha({ rotulo, valor, detalhe, forte }: { rotulo: string; valor: string; detalhe?: string; forte?: boolean }) {
  return (
    <div className="flex items-start justify-between gap-3">
      <div className="min-w-0">
        <p className={forte ? 'font-semibold' : 'text-gray-700'}>{rotulo}</p>
        {detalhe && <p className="text-sm text-gray-500">{detalhe}</p>}
      </div>
      <p className={`tabular-nums whitespace-nowrap ${forte ? 'font-display text-2xl font-semibold' : 'font-medium'}`}>{valor}</p>
    </div>
  )
}

// "2026-10-03" → "03/10"
function diaMes(iso: string) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso)
  return m ? `${m[3]}/${m[2]}` : iso
}
