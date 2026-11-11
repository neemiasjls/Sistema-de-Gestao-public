import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import {
  ArrowLeft, CheckCircle2, ChevronLeft, ChevronRight, Link2, Lock, LockOpen, Minus, Package, Pencil, Plus, RefreshCw, Save, Trash2, Unlink,
} from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import Campo from '../components/ui/Campo'
import { Carregando, ErroCarga } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { Card } from '../components/ModalBase'
import { classeCampo } from '../components/EstoquePecas'
import { brl, dataBr, hoje, num } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import {
  viagensApi, retornosApi, MESES, TIPOS_DESPESA, diariasPelasDatas, normalizarPlaca,
  type ViagemDetalhe, type ViagemEntrada, type MotoristaFrota, type PrecoCliente, type Volumes, type Abastecimento,
  type Despesa, type Carga, type Retorno,
} from '../services/viagensCaminhaoApi'
import { CampoNumero, SeloStatus, Selo, RetornoModal, retornoVazio, entradaDoRetorno } from '../components/viagens/ViagensPecas'

// Uma viagem do caminhão: a aba VIAGEMn do acerto mensal, para conferir e editar.
// As contas aparecem na hora (mesmas fórmulas da API e da planilha); salvar grava
// tudo de uma vez. Retornos são linhas da tela Retornos ligadas a esta viagem.

type Form = Omit<ViagemEntrada, 'volumes'> & { volumes: Volumes[] }

const vazio = (mes: number, ano: number): Form => ({
  motoristaId: null, motoristaNome: null, placa: '', placaCarreta: null, destino: null, dataSaida: hoje(), dataChegada: null,
  kmSaida: null, kmChegada: null, litros: null, levou: 0, dataDinheiro: null, devolveu: 0, sobraAnteriorInformada: null,
  diariasQuantidade: 0, porcentagemComissao: 6, mesReferencia: mes, anoReferencia: ano, observacoes: null,
  volumes: [], abastecimentos: [], despesas: [], cargas: [],
})

const doDetalhe = (d: ViagemDetalhe): Form => ({
  motoristaId: d.motoristaId, motoristaNome: d.motoristaNome, placa: d.placa, placaCarreta: d.placaCarreta, destino: d.destino,
  dataSaida: d.dataSaida, dataChegada: d.dataChegada, kmSaida: d.kmSaida, kmChegada: d.kmChegada, litros: d.litros,
  levou: d.levou, dataDinheiro: d.dataDinheiro, devolveu: d.devolveu, sobraAnteriorInformada: d.sobraAnteriorInformada,
  diariasQuantidade: d.diariasQuantidade, porcentagemComissao: d.porcentagemComissao, mesReferencia: d.mesReferencia,
  anoReferencia: d.anoReferencia, observacoes: d.observacoes,
  volumes: d.volumes.map(o => ({ ...o })), abastecimentos: d.abastecimentos.map(a => ({ ...a })),
  despesas: d.despesas.map(x => ({ tipo: x.tipo, descricao: x.descricao, valor: x.valor, observacao: x.observacao })),
  cargas: d.cargas.map(c => ({ ...c })),
})

// Frete do cliente (VIA-06): cada tipo com o valor gravado na linha (tabela de preços do dia)
const totalVolumes = (o: Volumes) => o.caixasVolumes * o.valorCaixa + o.caixasProdutoE * o.valorProdutoE + o.caixasProdutoD * o.valorProdutoD
  + o.produtosB * o.valorProdutoB + o.produtosC * o.valorProdutoC

/** ProdutosB da linha: amarelas + brancas; a linha antiga (planilha) só tinha o total, sem cor. */
const comProdutosB = (o: Volumes, p: Partial<Volumes>): Volumes => {
  const n = { ...o, ...p }
  const cor = n.produtosBAmarelas + n.produtosBBrancas
  return { ...n, produtosB: cor > 0 ? cor : (n.produtosBSemCor ?? 0) }
}

/** Fretes do cliente na tabela de preços (os que a API grava numa linha nova). */
const fretesDe = (p: PrecoCliente | undefined): Pick<Volumes, 'valorCaixa' | 'valorProdutoE' | 'valorProdutoD' | 'valorProdutoB' | 'valorProdutoC'> =>
  p ? { valorCaixa: p.valorCaixa, valorProdutoE: p.valorProdutoE, valorProdutoD: p.valorProdutoD, valorProdutoB: p.valorProdutoB, valorProdutoC: p.valorProdutoC }
    : { valorCaixa: 9, valorProdutoE: 4.5, valorProdutoD: 3, valorProdutoB: 10, valorProdutoC: 10 }

/** As contas da aba (ViagemCaminhaoCalculo na API), para mostrar na hora. */
function contas(f: Form, retornos: Retorno[], sobraAnteriorCadeia: number, valorDiaria: number) {
  const volumes = f.volumes.reduce((s, o) => s + totalVolumes(o), 0)
  const rets = retornos.reduce((s, r) => s + r.freteTotal, 0)
  const valorTotal = volumes + rets
  const nota = f.abastecimentos.filter(a => a.forma !== 'PAGO').reduce((s, a) => s + a.valor, 0)
  const pago = f.abastecimentos.filter(a => a.forma === 'PAGO').reduce((s, a) => s + a.valor, 0)
  const diarias = Math.round(f.diariasQuantidade * valorDiaria * 100) / 100
  const outras = f.despesas.reduce((s, d) => s + d.valor, 0)
  const totalDespesas = nota + pago + diarias + outras
  const sobraAnterior = f.sobraAnteriorInformada ?? sobraAnteriorCadeia
  const sobra = sobraAnterior + f.levou - f.devolveu - totalDespesas + nota
  const caixinha = f.despesas.filter(d => d.tipo === 'CAIXINHA').reduce((s, d) => s + d.valor, 0)
  const descarga = f.despesas.filter(d => d.tipo === 'DESCARGA').reduce((s, d) => s + d.valor, 0)
  const base = valorTotal - caixinha - descarga
  const comissao = Math.round(base / 100 * (f.porcentagemComissao ?? 0) * 100) / 100
  return { volumes, rets, valorTotal, nota, pago, diarias, outras, totalDespesas, sobraAnterior, sobra, caixinha, descarga, base, comissao, resultado: valorTotal - totalDespesas }
}

export default function ViagemCaminhaoPage() {
  const { id: idTexto } = useParams()
  const [params] = useSearchParams()
  const id = idTexto ? Number(idTexto) : null
  const navigate = useNavigate()
  const confirmar = useConfirmar()
  const agora = new Date()
  const [det, setDet] = useState<ViagemDetalhe | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [mexido, setMexido] = useState(false)
  const [falhou, setFalhou] = useState(false)
  const [motoristas, setMotoristas] = useState<MotoristaFrota[]>([])
  const [precos, setPrecos] = useState<PrecoCliente[]>([])
  const [salvando, setSalvando] = useState(false)
  const [retornoEdit, setRetornoEdit] = useState<{ id: number | null; inicial: ReturnType<typeof retornoVazio> } | null>(null)
  const [informarSobra, setInformarSobra] = useState(false)

  const carregar = useCallback(async () => {
    setFalhou(false)
    try {
      const [ms, ps] = await Promise.all([viagensApi.motoristas(), viagensApi.precos()])
      setMotoristas(ms); setPrecos(ps)
      if (id) {
        const d = await viagensApi.detalhe(id)
        setDet(d); setForm(doDetalhe(d)); setInformarSobra(d.sobraAnteriorInformada != null)
      } else {
        setDet(null)
        setForm(vazio(Number(params.get('mes')) || agora.getMonth() + 1, Number(params.get('ano')) || agora.getFullYear()))
      }
      setMexido(false)
    } catch { setFalhou(true) }
  }, [id]) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { carregar() }, [carregar])

  // Sair da página com algo não salvo: o navegador pergunta (UI-17)
  useEffect(() => {
    if (!mexido) return
    const aviso = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', aviso)
    return () => window.removeEventListener('beforeunload', aviso)
  }, [mexido])

  const set = (p: Partial<Form>) => { setForm(f => f && ({ ...f, ...p })); setMexido(true) }
  const fechada = det?.status === 'fechada'
  const retornos = det?.retornos ?? []
  const c = useMemo(() => form ? contas(form, retornos, det?.calculo.sobraAnterior ?? 0, det?.valorDiaria ?? 100) : null,
    [form, retornos, det]) // eslint-disable-line react-hooks/exhaustive-deps

  const salvar = async (): Promise<number | null> => {
    if (!form) return null
    if (!form.placa.trim()) { toast.error('Informe a placa.'); return null }
    if (!form.motoristaId && !form.motoristaNome?.trim()) { toast.error('Escolha o motorista.'); return null }
    setSalvando(true)
    try {
      const dto: ViagemEntrada = {
        ...form, placa: normalizarPlaca(form.placa), placaCarreta: form.placaCarreta ? normalizarPlaca(form.placaCarreta) : null,
        sobraAnteriorInformada: informarSobra ? form.sobraAnteriorInformada ?? 0 : null,
      }
      const r = id ? await viagensApi.salvar(id, dto) : await viagensApi.criar(dto)
      toast.success('Viagem salva.')
      setMexido(false)
      if (!id) { navigate(`/viagens-caminhoes/${r.id}`, { replace: true }); return r.id }
      await carregar()
      return r.id
    } catch (e) {
      const msg = await erroDaApi(e, 'Não consegui salvar a viagem.')
      // Viagem desta placa e saída já existe (importada do acerto ou lançada antes): oferece abrir aquela
      const existente = (e as { response?: { data?: { viagemId?: number } } })?.response?.data?.viagemId
      if (existente) {
        toast.error(t => (
          <span>
            {msg}{' '}
            <Link to={`/viagens-caminhoes/${existente}`} className="font-semibold underline" onClick={() => { toast.dismiss(t.id); setMexido(false) }}>
              Abrir a viagem
            </Link>
          </span>
        ), { duration: 10000 })
      } else toast.error(msg)
      return null
    } finally { setSalvando(false) }
  }

  const mudarStatus = async (status: 'lancada' | 'conferida' | 'fechada') => {
    if (!id) return
    if (mexido) { const ok = await salvar(); if (!ok) return }
    try {
      await viagensApi.status(id, status)
      toast.success(status === 'conferida' ? 'Viagem conferida.' : status === 'fechada' ? 'Viagem fechada.' : 'Viagem reaberta.')
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui mudar a situação.')) }
  }

  const excluir = async () => {
    if (!id || !det) return
    if (!await confirmar({ titulo: 'Apagar a viagem?', texto: `${det.placa} · ${det.dataSaida ? dataBr(det.dataSaida) : ''} · ${det.motorista}. Os retornos ligados continuam na tela Retornos (os do motorista ainda não conferidos vão junto).` })) return
    try { await viagensApi.excluir(id); toast.success('Viagem apagada.'); setMexido(false); navigate(`/viagens-caminhoes?mes=${det.mesReferencia}&ano=${det.anoReferencia}`) }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  const aplicarCarregamento = async (carregamentoId: number) => {
    if (!id) return
    if (form && form.volumes.length > 0 && !await confirmar({ titulo: 'Trocar os volumes pelos do carregamento?', texto: 'Os clientes e caixas desta viagem são trocados pelos do controle de carregamento.', confirmar: 'Trocar', perigo: false })) return
    if (mexido) { const ok = await salvar(); if (!ok) return }
    try {
      const r = await viagensApi.aplicarCarregamento(id, carregamentoId)
      if (r.avisos.length) toast(r.avisos.join('\n'), { duration: 8000 })
      else toast.success('Volumes lançados do carregamento.')
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui aplicar.')) }
  }

  // A viagem guarda os fretes do dia (VIA-10); trocar pelos de hoje é escolha de quem confere
  const atualizarFretes = async () => {
    if (!form) return
    if (!await confirmar({ titulo: 'Usar os fretes de hoje?', texto: 'Os clientes desta viagem passam a usar os fretes do painel de preços de hoje. Salve a viagem para gravar.', confirmar: 'Usar os de hoje', perigo: false })) return
    set({ volumes: form.volumes.map(o => ({ ...o, id: undefined, ...fretesDe(precos.find(p => p.cliente === o.cliente)) })) })
  }

  const vincular = async (r: Retorno, ligar: boolean) => {
    if (!id) return
    if (mexido) { const ok = await salvar(); if (!ok) return }
    try { await retornosApi.vincular(r.id, ligar ? id : null); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui mudar o retorno.')) }
  }

  const escolherMotorista = (valor: string) => {
    const m = motoristas.find(x => String(x.id) === valor)
    if (!m) { set({ motoristaId: null }); return }
    set({
      motoristaId: m.id, motoristaNome: m.nome, porcentagemComissao: m.porcentagemComissao,
      placa: form?.placa || m.placaCavalo || '', placaCarreta: form?.placaCarreta || m.placaCarreta,
    })
  }

  if (falhou) return <ErroCarga mensagem="Não consegui abrir a viagem." onTentar={carregar} />
  if (!form || !c) return <Carregando linhas={6} rotulo="Abrindo a viagem" />

  const voltar = `/viagens-caminhoes?mes=${form.mesReferencia}&ano=${form.anoReferencia}`
  const diariasDatas = diariasPelasDatas(form.dataSaida, form.dataChegada)

  return (
    <div className="space-y-5 max-w-6xl">
      <Link to={voltar} className="inline-flex items-center gap-1 text-sm font-medium text-gray-600 hover:text-gray-900 min-h-11 -mt-2">
        <ArrowLeft size={16} aria-hidden /> Viagens de {MESES[(form.mesReferencia ?? 1) - 1]}
      </Link>
      <CabecalhoTela
        titulo={det ? `Viagem ${det.numero} · ${det.placa}` : 'Nova viagem'}
        subtitulo={det ? <span className="inline-flex flex-wrap items-center gap-1.5"><SeloStatus status={det.status} />
          {det.origem === 'motorista' && <Selo tom="info">Enviada pelo motorista</Selo>}{det.origem === 'importacao' && <Selo>Da planilha</Selo>}</span> : undefined}
        acoes={<>
          {det && det.status === 'lancada' && <Botao variante="secundario" icone={CheckCircle2} onClick={() => mudarStatus('conferida')}>Marcar conferida</Botao>}
          {det && det.status === 'conferida' && <Botao variante="secundario" icone={Lock} onClick={() => mudarStatus('fechada')}>Fechar</Botao>}
          {det && det.status === 'fechada' && <Botao variante="secundario" icone={LockOpen} onClick={() => mudarStatus('conferida')}>Reabrir</Botao>}
          {det && det.status === 'conferida' && <Botao variante="fantasma" onClick={() => mudarStatus('lancada')}>Voltar para lançada</Botao>}
          {det && <Botao variante="perigoLeve" icone={Trash2} onClick={excluir}>Apagar</Botao>}
        </>}
      >
        {det && (det.anteriorId || det.proximaId) && (
          <div className="flex gap-2">
            {det.anteriorId && <Link to={`/viagens-caminhoes/${det.anteriorId}`} className="inline-flex items-center gap-1 text-sm font-medium text-tinta-700 min-h-11"><ChevronLeft size={16} aria-hidden /> Viagem anterior</Link>}
            {det.proximaId && <Link to={`/viagens-caminhoes/${det.proximaId}`} className="inline-flex items-center gap-1 text-sm font-medium text-tinta-700 min-h-11">Próxima viagem <ChevronRight size={16} aria-hidden /></Link>}
          </div>
        )}
      </CabecalhoTela>

      {det?.origem === 'motorista' && det.status === 'lancada' && (
        <Aviso tipo="atencao" titulo="Enviada pelo motorista">
          {det.enviadoEm ? `Chegou em ${new Date(det.enviadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}. ` : ''}
          Confira a carga de volumes, os retornos e as despesas e marque como conferida. Enquanto estiver lançada, o motorista ainda pode corrigir.
        </Aviso>
      )}
      {fechada && <Aviso tipo="info">Viagem fechada: reabra para mudar.</Aviso>}
      {det && det.volumes.length === 0 && <Aviso tipo="atencao">Sem carga de volumes: importe o controle de carregamento ou lance os clientes abaixo.</Aviso>}

      {/* Resumo: as contas da aba */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <Card titulo="Frete total (volumes + retornos)" destaque>{brl(c.valorTotal)}</Card>
        <Card titulo="Total de despesas">{brl(c.totalDespesas)}</Card>
        <Card titulo={`Comissão (${num(form.porcentagemComissao ?? 0, (form.porcentagemComissao ?? 0) % 1 ? 2 : 0)}%)`}>{brl(c.comissao)}</Card>
        <Card titulo="Sobra da viagem">{brl(c.sobra)}</Card>
      </div>
      <details className="bg-white border border-gray-200 rounded-xl group">
        <summary className="cursor-pointer px-4 min-h-11 flex items-center text-sm font-medium text-gray-700">Ver as contas</summary>
        <div className="px-4 pb-4 grid sm:grid-cols-2 gap-x-8 gap-y-1 text-sm tabular-nums">
          <Conta r="Total de volumes" v={c.volumes} /><Conta r="Total de retornos" v={c.rets} />
          <Conta r="Diesel na nota (empresa)" v={c.nota} /><Conta r="Diesel pago com o dinheiro" v={c.pago} />
          <Conta r="Diárias" v={c.diarias} /><Conta r="Outras despesas" v={c.outras} />
          <Conta r="Sobra anterior" v={c.sobraAnterior} /><Conta r="Dinheiro da viagem (sobra anterior + levou)" v={c.sobraAnterior + form.levou} />
          <Conta r="Base da comissão (frete − caixinha − descarga)" v={c.base} /><Conta r="Resultado (frete − despesas)" v={c.resultado} />
          {det && Math.abs(det.calculo.comissaoPlanilha - c.comissao) > 0.005 && !mexido && (
            <p className="sm:col-span-2 text-gray-500 pt-1">Pela fórmula antiga da planilha (que também tirava carregamento, gasolina e ICMS) daria {brl(det.calculo.comissaoPlanilha)}.</p>
          )}
        </div>
      </details>

      <fieldset disabled={fechada} className="min-w-0 space-y-5 disabled:opacity-90">
        <Secao titulo="Viagem">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            <Campo rotulo="Motorista">
              <select value={form.motoristaId ?? ''} onChange={e => escolherMotorista(e.target.value)} className={classeCampo}>
                <option value="">{form.motoristaNome && !form.motoristaId ? `${form.motoristaNome} (sem cadastro)` : 'Escolha'}</option>
                {motoristas.filter(m => m.ativo || m.id === form.motoristaId).map(m => <option key={m.id} value={m.id}>{m.nome}</option>)}
              </select>
            </Campo>
            <Campo rotulo="Placa do caminhão"><input value={form.placa} onChange={e => set({ placa: e.target.value.toUpperCase() })} className={classeCampo} /></Campo>
            <Campo rotulo="Carreta"><input value={form.placaCarreta ?? ''} onChange={e => set({ placaCarreta: e.target.value.toUpperCase() || null })} className={classeCampo} /></Campo>
            <Campo rotulo="Destino"><input value={form.destino ?? ''} onChange={e => set({ destino: e.target.value || null })} className={classeCampo} /></Campo>
            <Campo rotulo="Saída"><input type="date" value={form.dataSaida ?? ''} onChange={e => set({ dataSaida: e.target.value || null })} className={classeCampo} /></Campo>
            <Campo rotulo="Chegada"><input type="date" value={form.dataChegada ?? ''} onChange={e => set({ dataChegada: e.target.value || null })} className={classeCampo} /></Campo>
            <Campo rotulo="Km na saída"><CampoNumero valor={form.kmSaida} vazioNulo casas={0} onChange={v => set({ kmSaida: v })} /></Campo>
            <Campo rotulo="Km na chegada" dica={c && form.kmSaida && form.kmChegada ? `${num(form.kmChegada - form.kmSaida, 0)} km` : undefined}>
              <CampoNumero valor={form.kmChegada} vazioNulo casas={0} onChange={v => set({ kmChegada: v })} />
            </Campo>
            <Campo rotulo="Litros"><CampoNumero valor={form.litros} vazioNulo onChange={v => set({ litros: v })} /></Campo>
            <Campo rotulo="Comissão (%)"><CampoNumero valor={form.porcentagemComissao} onChange={v => set({ porcentagemComissao: v ?? 0 })} /></Campo>
            <Campo rotulo="Entra no acerto de" grupo>
              <div className="grid grid-cols-[1fr_6rem] gap-2">
                <select aria-label="Mês do acerto" value={form.mesReferencia ?? 1} onChange={e => set({ mesReferencia: Number(e.target.value) })} className={classeCampo}>
                  {MESES.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
                </select>
                <input aria-label="Ano do acerto" inputMode="numeric" value={form.anoReferencia ?? ''} onChange={e => set({ anoReferencia: Number(e.target.value.replace(/\D/g, '')) || null })} className={classeCampo} />
              </div>
            </Campo>
          </div>
        </Secao>

        <Secao titulo="Dinheiro">
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            <Campo rotulo="Sobra anterior" dica={informarSobra ? 'Digitada à mão' : 'Da viagem anterior deste caminhão'}>
              {informarSobra
                ? <CampoNumero valor={form.sobraAnteriorInformada ?? 0} onChange={v => set({ sobraAnteriorInformada: v ?? 0 })} />
                : <input readOnly value={num(det?.calculo.sobraAnterior ?? 0)} className={`${classeCampo} bg-gray-50 tabular-nums`} />}
            </Campo>
            <Campo rotulo="Levou"><CampoNumero valor={form.levou} onChange={v => set({ levou: v ?? 0 })} /></Campo>
            <Campo rotulo="Data do dinheiro"><input type="date" value={form.dataDinheiro ?? ''} onChange={e => set({ dataDinheiro: e.target.value || null })} className={classeCampo} /></Campo>
            {/* VIA-14: a sobra fica com o motorista e passa para a próxima viagem; não se digita */}
            <Campo rotulo="Sobra da viagem" dica="Fica com o motorista e passa para a próxima viagem">
              <input readOnly value={num(c.sobra)} className={`${classeCampo} bg-gray-50 tabular-nums`} />
            </Campo>
          </div>
          {form.devolveu !== 0 && (
            <p className="text-sm text-gray-600">Na planilha antiga o motorista devolveu {brl(form.devolveu)} no fim desta viagem; a conta da sobra desconta.</p>
          )}
          <label className="inline-flex items-center gap-2 min-h-11 text-sm">
            <input type="checkbox" checked={informarSobra} className="w-5 h-5"
                   onChange={e => { setInformarSobra(e.target.checked); set({ sobraAnteriorInformada: e.target.checked ? (det?.calculo.sobraAnterior ?? 0) : null }) }} />
            Digitar a sobra anterior à mão (começo do controle ou acerto feito por fora)
          </label>
        </Secao>

        <Secao titulo="Carga de volumes" extra={<span className="text-sm text-gray-600 tabular-nums">{brl(c.volumes)}</span>}>
          {det && det.carregamentos.length > 0 && (
            <div className="flex flex-wrap gap-2 items-center text-sm">
              <span className="text-gray-600">Controle de carregamento:</span>
              {det.carregamentos.map(cg => cg.aplicado
                ? <span key={cg.id} className="inline-flex flex-wrap items-center gap-2">
                    <Selo tom="info">{dataBr(cg.data)} · {cg.clientes} clientes · lançado</Selo>
                    <Botao variante="fantasma" tamanho="sm" icone={Package} onClick={() => aplicarCarregamento(cg.id)}>Usar de novo</Botao>
                  </span>
                : <Botao key={cg.id} variante="secundario" tamanho="sm" icone={Package} onClick={() => aplicarCarregamento(cg.id)}>
                    Usar o de {dataBr(cg.data)} ({cg.clientes} clientes)
                  </Botao>)}
            </div>
          )}
          <datalist id="clientes-frete">{precos.filter(p => p.ativo).map(p => <option key={p.id} value={p.cliente} />)}</datalist>
          <TabelaLinhas
            cabecalho={['Cliente', 'Caixas de volumes', 'ProdutoE', 'ProdutoD', 'ProdutosB amarelas', 'ProdutosB brancas', 'ProdutosC', 'Total']}
            vazio="Nenhum cliente nesta viagem."
            linhas={form.volumes.map((o, i) => {
              const mudar = (p: Partial<Volumes>) => set({ volumes: form.volumes.map((x, j) => j === i ? comProdutosB(x, p) : x) })
              const semCor = o.produtosBSemCor ?? 0
              return {
                chave: `o${i}`,
                celulas: [
                  <input key="c" list="clientes-frete" aria-label="Cliente" value={o.cliente} className={`${classeCampo} min-w-[11rem]`}
                         onChange={e => {
                           // Outro cliente: a linha vira nova e pega os fretes dele na tabela (a API grava os mesmos)
                           const nome = e.target.value.toUpperCase()
                           mudar({ cliente: nome, id: undefined, ...fretesDe(precos.find(x => x.cliente === nome)) })
                         }} />,
                  <CampoNumero key="o" aria-label="Caixas de volumes" valor={o.caixasVolumes} onChange={v => mudar({ caixasVolumes: v ?? 0 })} className="w-24" />,
                  <CampoNumero key="p" aria-label="Caixas de produtoE" valor={o.caixasProdutoE} onChange={v => mudar({ caixasProdutoE: v ?? 0 })} className="w-20" />,
                  <CampoNumero key="d" aria-label="Caixas de produtoD" valor={o.caixasProdutoD} onChange={v => mudar({ caixasProdutoD: v ?? 0 })} className="w-20" />,
                  <CampoNumero key="a" aria-label="Pacotes de produtoB amarela" valor={o.produtosBAmarelas} onChange={v => mudar({ produtosBAmarelas: v ?? 0 })} className="w-20" />,
                  <CampoNumero key="b" aria-label="Pacotes de produtoB branca" valor={o.produtosBBrancas} onChange={v => mudar({ produtosBBrancas: v ?? 0 })} className="w-20" />,
                  <CampoNumero key="j" aria-label="Pacotes de produtoC" valor={o.produtosC} onChange={v => mudar({ produtosC: v ?? 0 })} className="w-20" />,
                  <span key="t" className="inline-flex flex-col items-end gap-0.5">
                    <span className="tabular-nums font-medium whitespace-nowrap">{brl(totalVolumes(o))}</span>
                    {o.cliente && !precos.some(p => p.cliente === o.cliente) && <Selo tom="atencao">Fora da tabela de frete</Selo>}
                    {semCor > 0 && o.produtosBAmarelas + o.produtosBBrancas === 0 && (
                      <Selo>{num(semCor, semCor % 1 ? 1 : 0)} produtosB sem cor (planilha antiga)</Selo>
                    )}
                  </span>,
                ],
                remover: () => set({ volumes: form.volumes.filter((_, j) => j !== i) }),
              }
            })}
            rodape={form.volumes.length > 0 && <span className="tabular-nums">{num(form.volumes.reduce((s, o) => s + o.caixasVolumes, 0), 0)} caixas · total {brl(c.volumes)}</span>}
          />
          <div className="flex flex-wrap gap-2">
            <Botao variante="fantasma" tamanho="sm" icone={Plus}
                   onClick={() => set({ volumes: [...form.volumes, { cliente: '', caixasVolumes: 0, caixasProdutoE: 0, caixasProdutoD: 0, produtosB: 0, produtosBAmarelas: 0, produtosBBrancas: 0, produtosC: 0, ...fretesDe(undefined) }] })}>
              Cliente
            </Botao>
            {form.volumes.length > 0 && !fechada && (
              <Botao variante="fantasma" tamanho="sm" icone={RefreshCw} onClick={atualizarFretes}>Atualizar fretes pela tabela</Botao>
            )}
          </div>
          <p className="text-xs text-gray-500">Os fretes (caixa, produtoE, produtoD, produtoB e produtoC) vêm do painel de preços no dia em que o cliente entrou na viagem; mudar o painel não mexe aqui.</p>
        </Secao>

        <Secao titulo="Retornos e outras cargas" extra={<span className="text-sm text-gray-600 tabular-nums">{brl(c.rets)}</span>}>
          {!det ? <p className="text-sm text-gray-600">Salve a viagem para ligar os retornos.</p> : (
            <>
              {retornos.length === 0 && <p className="text-sm text-gray-600">Nenhum retorno ligado.</p>}
              <ul className="divide-y divide-gray-100 border border-gray-200 rounded-xl overflow-hidden empty:hidden">
                {retornos.map(r => (
                  <li key={r.id} className={`px-3 py-2 flex items-center gap-2 text-sm ${r.conferido ? '' : 'bg-amber-50/60'}`}>
                    <div className="flex-1 min-w-0">
                      <p className="font-medium text-gray-900">{r.transportadora ?? 'Sem transportadora'} · {dataBr(r.data)}</p>
                      <p className="text-gray-600 truncate">{[r.observacoes, r.destino].filter(Boolean).join(' / ')}</p>
                      <div className="flex flex-wrap gap-1">{!r.conferido && <Selo tom="atencao">A conferir</Selo>}{r.foraDoControle && <Selo>Fora do controle mensal</Selo>}</div>
                    </div>
                    <span className="tabular-nums font-semibold whitespace-nowrap">{brl(r.freteTotal)}</span>
                    <BotaoIcone icone={Pencil} rotulo="Editar retorno" onClick={() => setRetornoEdit({ id: r.id, inicial: entradaDoRetorno(r) })} />
                    <BotaoIcone icone={Unlink} rotulo="Soltar da viagem" onClick={() => vincular(r, false)} />
                  </li>
                ))}
              </ul>
              {det.retornosSugeridos.length > 0 && (
                <div className="space-y-1">
                  <p className="text-sm text-gray-600">Retornos desta placa nestas datas, ainda sem viagem:</p>
                  {det.retornosSugeridos.map(r => (
                    <div key={r.id} className="flex items-center gap-2 text-sm bg-gray-50 rounded-lg px-3 py-1.5">
                      <span className="flex-1 min-w-0 truncate">{dataBr(r.data)} · {r.transportadora} · {[r.observacoes, r.destino].filter(Boolean).join(' / ')}</span>
                      <span className="tabular-nums">{brl(r.freteTotal)}</span>
                      <Botao variante="secundario" tamanho="sm" icone={Link2} onClick={() => vincular(r, true)}>Ligar</Botao>
                    </div>
                  ))}
                </div>
              )}
              <Botao variante="fantasma" tamanho="sm" icone={Plus}
                     onClick={() => setRetornoEdit({ id: null, inicial: { ...retornoVazio(), data: form.dataSaida, placa: form.placa, motorista: form.motoristaNome, viagemId: det.id } })}>
                Novo retorno nesta viagem
              </Botao>
            </>
          )}
          {form.cargas.length > 0 && (
            <div className="text-sm space-y-1 pt-1">
              <p className="font-medium text-gray-700">O que o motorista anotou:</p>
              <ul className="text-gray-600 space-y-0.5">
                {form.cargas.map((cg, i) => (
                  <li key={i}>{cg.tipo === 'VOLUMES' ? 'Carga de volumes' : `Outra carga · ${cg.transportadora ?? 'sem transportadora'} · ${brl(cg.valor)}`}
                    {cg.data ? ` · ${dataBr(cg.data)}` : ''}{cg.origem || cg.destino ? ` · ${[cg.origem, cg.destino].filter(Boolean).join(' / ')}` : ''}
                    {cg.tipo === 'RETORNO' && (
                      <span className="flex flex-wrap items-center gap-1 mt-0.5">
                        {cg.retornoLigado
                          ? <Selo tom="info">Ligada ao retorno importado de {dataBr(cg.retornoLigado.data)} · {brl(cg.retornoLigado.freteTotal)}{cg.ligacao === 'manual' ? ' (ligada no escritório)' : ''}</Selo>
                          : cg.ligacao === 'conferir' ? <Selo tom="atencao">Para conferir</Selo>
                          : <Selo tom="atencao">Sem retorno importado</Selo>}
                        {cg.ligacaoAviso && <span className="text-xs text-gray-600">{cg.ligacaoAviso}</span>}
                        {!cg.retornoLigado && <Link to={`/retornos?mes=${Number((cg.data ?? form.dataSaida ?? '').slice(5, 7)) || ''}&ano=${(cg.data ?? form.dataSaida ?? '').slice(0, 4)}`} className="text-xs font-medium text-tinta-700 underline">Resolver em Retornos</Link>}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </Secao>

        <Secao titulo="Abastecimentos" extra={<span className="text-sm text-gray-600 tabular-nums">nota {brl(c.nota)} · pago {brl(c.pago)}</span>}>
          <TabelaLinhas
            cabecalho={['Quem pagou', 'Data', 'Km', 'Litros', 'Valor', 'Posto / cidade']}
            vazio="Nenhum abastecimento."
            linhas={form.abastecimentos.map((a, i) => {
              const mudar = (p: Partial<Abastecimento>) => set({ abastecimentos: form.abastecimentos.map((x, j) => j === i ? { ...x, ...p } : x) })
              return {
                chave: `a${i}`,
                celulas: [
                  <select key="f" aria-label="Quem pagou" value={a.forma} onChange={e => mudar({ forma: e.target.value as 'NOTA' | 'PAGO' })} className={`${classeCampo} min-w-[9rem]`}>
                    <option value="NOTA">Nota / pix (empresa)</option><option value="PAGO">Pago com o dinheiro</option>
                  </select>,
                  <input key="d" type="date" aria-label="Data" value={a.data ?? ''} onChange={e => mudar({ data: e.target.value || null })} className={classeCampo} />,
                  <CampoNumero key="k" aria-label="Km" casas={0} vazioNulo valor={a.km} onChange={v => mudar({ km: v })} className="w-24" />,
                  <CampoNumero key="l" aria-label="Litros" vazioNulo valor={a.litros} onChange={v => mudar({ litros: v })} className="w-24" />,
                  <CampoNumero key="v" aria-label="Valor" valor={a.valor} onChange={v => mudar({ valor: v ?? 0 })} className="w-28" />,
                  <input key="p" aria-label="Posto ou cidade" value={a.local ?? ''} onChange={e => mudar({ local: e.target.value || null })} className={`${classeCampo} min-w-[10rem]`} />,
                ],
                remover: () => set({ abastecimentos: form.abastecimentos.filter((_, j) => j !== i) }),
              }
            })}
          />
          <Botao variante="fantasma" tamanho="sm" icone={Plus}
                 onClick={() => set({ abastecimentos: [...form.abastecimentos, { data: form.dataSaida, km: null, litros: null, valor: 0, local: null, observacao: null, forma: 'NOTA' }] })}>
            Abastecimento
          </Botao>
        </Secao>

        <Secao titulo="Despesas" extra={<span className="text-sm text-gray-600 tabular-nums">{brl(c.diarias + c.outras)}</span>}>
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-sm font-medium text-gray-700">Diárias</span>
            <BotaoIcone icone={Minus} rotulo="Meia diária a menos" className="border border-gray-300" onClick={() => set({ diariasQuantidade: Math.max(0, form.diariasQuantidade - 0.5) })} />
            <span className="font-display text-xl font-semibold tabular-nums min-w-[3ch] text-center" aria-live="polite">{num(form.diariasQuantidade, form.diariasQuantidade % 1 ? 1 : 0)}</span>
            <BotaoIcone icone={Plus} rotulo="Meia diária a mais" className="border border-gray-300" onClick={() => set({ diariasQuantidade: form.diariasQuantidade + 0.5 })} />
            <span className="text-sm text-gray-600 tabular-nums">{brl(c.diarias)}</span>
            {diariasDatas !== form.diariasQuantidade && diariasDatas > 0 && (
              <Botao variante="fantasma" tamanho="sm" onClick={() => set({ diariasQuantidade: diariasDatas })}>Usar pelas datas ({diariasDatas})</Botao>
            )}
          </div>
          <TabelaLinhas
            cabecalho={['Despesa', 'O que foi / de quem', 'Valor', 'Observação']}
            vazio="Nenhuma outra despesa."
            linhas={form.despesas.map((d, i) => {
              const mudar = (p: Partial<Despesa>) => set({ despesas: form.despesas.map((x, j) => j === i ? { ...x, ...p } : x) })
              return {
                chave: `d${i}`,
                celulas: [
                  <select key="t" aria-label="Tipo de despesa" value={d.tipo} onChange={e => mudar({ tipo: e.target.value })} className={`${classeCampo} min-w-[10rem]`}>
                    {TIPOS_DESPESA.map(([t, n]) => <option key={t} value={t}>{n}</option>)}
                  </select>,
                  <span key="s" className="flex flex-col gap-0.5">
                    <input aria-label={d.tipo === 'CAIXINHA' ? 'De quem é a caixinha' : 'O que foi'} value={d.descricao ?? ''}
                           disabled={d.tipo !== 'OUTRA' && d.tipo !== 'CAIXINHA'} list={d.tipo === 'CAIXINHA' ? 'caixinha-quem' : undefined}
                           placeholder={d.tipo === 'OUTRA' ? 'Estacionamento, peça...' : d.tipo === 'CAIXINHA' ? 'De quem (cliente ou lugar)' : ''}
                           onChange={e => mudar({ descricao: (d.tipo === 'CAIXINHA' ? e.target.value.toUpperCase() : e.target.value) || null })} className={`${classeCampo} min-w-[10rem]`} />
                    {d.tipo === 'CAIXINHA' && d.valor > 0 && !d.descricao?.trim() && <Selo tom="atencao">Sem dizer de quem</Selo>}
                  </span>,
                  <CampoNumero key="v" aria-label="Valor" valor={d.valor} onChange={v => mudar({ valor: v ?? 0 })} className="w-28" />,
                  <input key="o" aria-label="Observação" value={d.observacao ?? ''} onChange={e => mudar({ observacao: e.target.value || null })} className={`${classeCampo} min-w-[8rem]`} />,
                ],
                remover: () => set({ despesas: form.despesas.filter((_, j) => j !== i) }),
              }
            })}
          />
          <Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={() => set({ despesas: [...form.despesas, { tipo: 'CARREGAMENTO', descricao: null, valor: 0, observacao: null }] })}>
            Despesa
          </Botao>
          <datalist id="caixinha-quem">
            {[...new Set([...form.despesas.filter(d => d.tipo === 'CAIXINHA' && d.descricao).map(d => d.descricao!), ...precos.filter(p => p.ativo).map(p => p.cliente)])]
              .map(n => <option key={n} value={n} />)}
          </datalist>
          {form.despesas.some(d => d.tipo === 'CAIXINHA' && d.valor) && (
            <div className="rounded-lg bg-gray-50 border border-gray-200 px-3 py-2 text-sm">
              <p className="font-medium text-gray-800">Caixinhas · {brl(c.caixinha)} <span className="font-normal text-gray-600">(sai da base da comissão)</span></p>
              <ul className="text-gray-700 tabular-nums">
                {form.despesas.filter(d => d.tipo === 'CAIXINHA' && d.valor).map((d, i) => (
                  <li key={i} className="flex justify-between gap-3"><span>{d.descricao?.trim() || 'Sem dizer de quem'}</span><span>{brl(d.valor)}</span></li>
                ))}
              </ul>
            </div>
          )}
          <p className="text-xs text-gray-500">Para a comissão só saem a caixinha e a descarga; as outras despesas não mudam a comissão.</p>
        </Secao>

        <Secao titulo="Observações">
          <textarea aria-label="Observações" rows={2} value={form.observacoes ?? ''} onChange={e => set({ observacoes: e.target.value || null })}
                    className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm" />
        </Secao>
      </fieldset>

      {/* Salvar preso embaixo da área que rola (sem cobrir a barra de atalhos do celular) */}
      {!fechada && (
        <div className="sticky bottom-3 z-20 flex justify-end pointer-events-none">
          <div className="pointer-events-auto bg-white/95 backdrop-blur border border-gray-200 rounded-xl shadow-lg px-3 py-2 flex items-center gap-3">
            {mexido && <span className="text-sm text-amber-800 font-medium">Mudanças não salvas</span>}
            <Botao variante="primario" icone={Save} onClick={salvar} carregando={salvando} textoCarregando="Salvando...">Salvar viagem</Botao>
          </div>
        </div>
      )}

      {retornoEdit && (
        <RetornoModal id={retornoEdit.id} inicial={retornoEdit.inicial} onClose={() => setRetornoEdit(null)}
                      onSalvou={() => { setRetornoEdit(null); carregar() }} />
      )}
    </div>
  )
}

function Secao({ titulo, extra, children }: { titulo: string; extra?: ReactNode; children: ReactNode }) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
      <div className="flex items-baseline justify-between gap-3">
        <h2 className="font-display text-lg font-semibold text-gray-900">{titulo}</h2>
        {extra}
      </div>
      {children}
    </section>
  )
}

function Conta({ r, v }: { r: string; v: number }) {
  return <p className="flex justify-between gap-3 border-b border-gray-100 py-1"><span className="text-gray-600">{r}</span><span className="font-medium">{brl(v)}</span></p>
}

/**
 * Linhas editáveis: no computador uma tabela; no celular cada linha vira um cartão
 * com os campos um embaixo do outro (sem rolagem lateral, UI-05).
 */
function TabelaLinhas({ cabecalho, linhas, vazio, rodape }: {
  cabecalho: string[]
  linhas: { chave: string; celulas: ReactNode[]; remover: () => void }[]
  vazio: string
  rodape?: ReactNode
}) {
  if (linhas.length === 0) return <p className="text-sm text-gray-600">{vazio}</p>
  return (
    <>
      <ul className="sm:hidden space-y-2">
        {linhas.map(l => (
          <li key={l.chave} className="border border-gray-200 rounded-xl p-3 space-y-2">
            {l.celulas.map((c, i) => (
              <div key={i} className="grid grid-cols-[7.5rem_1fr] items-center gap-2 [&_input]:w-full [&_select]:w-full">
                <span className="text-sm text-gray-600">{cabecalho[i]}</span>{c}
              </div>
            ))}
            <div className="flex justify-end"><BotaoIcone icone={Trash2} rotulo="Tirar linha" perigo onClick={l.remover} /></div>
          </li>
        ))}
        {rodape && <li className="text-sm text-gray-700 px-1">{rodape}</li>}
      </ul>
      <div className="hidden sm:block overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-left text-gray-600">
            <tr>{cabecalho.map(h => <th key={h} className="px-1.5 py-1 font-medium whitespace-nowrap">{h}</th>)}<th><span className="sr-only">Tirar</span></th></tr>
          </thead>
          <tbody>
            {linhas.map(l => (
              <tr key={l.chave}>
                {l.celulas.map((c, i) => <td key={i} className="px-1.5 py-1 align-middle">{c}</td>)}
                <td className="px-1 py-1"><BotaoIcone icone={Trash2} rotulo="Tirar linha" perigo onClick={l.remover} /></td>
              </tr>
            ))}
          </tbody>
          {rodape && <tfoot><tr><td colSpan={cabecalho.length + 1} className="px-1.5 pt-2 text-right text-gray-700">{rodape}</td></tr></tfoot>}
        </table>
      </div>
    </>
  )
}
