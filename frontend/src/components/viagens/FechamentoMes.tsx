import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { Lock, LockOpen, Plus, Save, Trash2, Landmark } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone } from '../ui/Botao'
import Aviso from '../ui/Aviso'
import Campo from '../ui/Campo'
import { Segmentado } from '../ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../ui/Estados'
import { useConfirmar } from '../ui/Confirmar'
import { Modal, BotoesModal } from '../ModalBase'
import { classeCampo } from '../EstoquePecas'
import { CampoNumero, NavegaMes, Selo, mesExtenso } from './ViagensPecas'
import { brl, dataBr, num } from '../../utils/formato'
import { erroDaApi } from '../../services/semPararApi'
import {
  fechamentosApi, PLACAS_ACERTO,
  type Consorcio, type ConsorcioEntrada, type CreditoFechamento, type DespesaFechamento, type Fechamento,
  type SalarioFechamento, type ValeFechamento,
} from '../../services/fechamentosApi'
import { MESES } from '../../services/viagensCaminhaoApi'

// Fechamento mensal por caminhão (VIA-18): as abas FECHAMENTO, DESPESAS DO MES e
// COMISSÃO do acerto. Aberto edita; "Fechar o mês" trava (e "Reabrir" destrava).

type Edicao = {
  saldoDigitado: number | null
  salario: SalarioFechamento
  observacao: string
  despesas: DespesaFechamento[]
  creditos: CreditoFechamento[]
  vales: ValeFechamento[]
}

const edicaoDe = (f: Fechamento): Edicao => ({
  saldoDigitado: f.saldoAnteriorDigitado,
  salario: { ...f.salario },
  observacao: f.observacao ?? '',
  despesas: f.despesas.map(d => ({ ...d })),
  creditos: f.creditos.map(c => ({ ...c })),
  vales: f.vales.map(v => ({ ...v })),
})

const CHAVE_SALARIO: Record<string, keyof SalarioFechamento> = {
  'salario:FGTS': 'fgts', 'salario:INSS': 'inss', 'salario:IRR': 'irr', 'salario:QUINQUENIO': 'quinquenio',
}

const despesaVazia = (): DespesaFechamento => ({
  data: null, referencia: '', valor: null, parcela: null, observacao: null, origem: 'manual',
  automatica: false, corrigida: false, valorAutomatico: null, deOndeVem: null, aviso: null,
})

export default function FechamentoMes({ mes, ano, onMes }: { mes: number; ano: number; onMes: (m: number, a: number) => void }) {
  const confirmar = useConfirmar()
  const [placa, setPlaca] = useState<string>(PLACAS_ACERTO[0])
  const [dados, setDados] = useState<Fechamento | null>(null)
  const [ed, setEd] = useState<Edicao | null>(null)
  const [base, setBase] = useState('')
  const [falhou, setFalhou] = useState(false)
  const [salvando, setSalvando] = useState(false)
  const [consorcios, setConsorcios] = useState(false)

  const aplicar = useCallback((f: Fechamento) => {
    const e = edicaoDe(f)
    setDados(f); setEd(e); setBase(JSON.stringify(e))
  }, [])

  const carregar = useCallback(() => {
    setFalhou(false); setDados(null); setEd(null)
    fechamentosApi.montar(placa, ano, mes).then(aplicar).catch(() => setFalhou(true))
  }, [placa, ano, mes, aplicar])
  useEffect(carregar, [carregar])

  const sujo = ed != null && JSON.stringify(ed) !== base

  // Totais na hora, com as mesmas contas da API (FechamentoMensalCalculo); ao salvar vale o da API
  const t = useMemo(() => {
    if (!dados || !ed) return null
    const entradaViagens = dados.totais.entradaViagens
    const creditos = ed.creditos.reduce((s, c) => s + c.valor, 0)
    const saida = ed.despesas.reduce((s, d) => s + (d.valor ?? 0), 0)
    const entrada = entradaViagens + creditos
    const sobra = entrada - saida
    const saldo = dados.saldoAnteriorOrigem === 'fechamento' ? dados.totais.saldoAnterior : ed.saldoDigitado ?? 0
    const vales = ed.vales.reduce((s, v) => s + v.valor, 0)
    const s = ed.salario
    const liquido = s.salario + s.horasExtras + dados.totais.comissaoTotal + s.quinquenio - s.inss - s.irr - vales
    return { creditos, entrada, saida, sobra, saldo, total: sobra + saldo, vales, liquido }
  }, [dados, ed])

  const set = (p: Partial<Edicao>) => setEd(x => x && { ...x, ...p })

  const mudarSalario = (campo: keyof SalarioFechamento, v: number) => setEd(x => {
    if (!x) return x
    const salario = { ...x.salario, [campo]: v }
    // As retenções automáticas acompanham o salário, salvo as corrigidas à mão
    const despesas = x.despesas.map(d => {
      const alvo = CHAVE_SALARIO[d.origem]
      return alvo && !d.corrigida ? { ...d, valor: salario[alvo], valorAutomatico: salario[alvo] } : d
    })
    return { ...x, salario, despesas }
  })

  const mudarDespesa = (i: number, p: Partial<DespesaFechamento>) => setEd(x => x && {
    ...x, despesas: x.despesas.map((d, j) => j !== i ? d : { ...d, ...p }),
  })
  // Valor de linha automática mexido à mão vira manual (não é mais recalculado)
  const mudarValorDespesa = (i: number, valor: number | null) =>
    mudarDespesa(i, ed?.despesas[i]?.automatica ? { valor, corrigida: true, aviso: null } : { valor })
  const voltarAutomatico = (i: number) => setEd(x => x && {
    ...x, despesas: x.despesas.map((d, j) => j !== i ? d : { ...d, corrigida: false, valor: d.valorAutomatico }),
  })

  const entrada = () => ({
    placa, ano, mes,
    saldoAnteriorDigitado: ed!.saldoDigitado,
    salario: ed!.salario,
    observacao: ed!.observacao || null,
    despesas: ed!.despesas.map(d => ({
      data: d.data, referencia: d.referencia, valor: d.valor, parcela: d.parcela, observacao: d.observacao,
      origem: d.origem, corrigida: d.corrigida,
    })),
    creditos: ed!.creditos,
    vales: ed!.vales,
  })

  const salvar = async (aviso = true) => {
    if (!ed) return false
    setSalvando(true)
    try {
      aplicar(await fechamentosApi.salvar(entrada()))
      if (aviso) toast.success('Fechamento salvo.')
      return true
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui salvar o fechamento.'))
      return false
    } finally { setSalvando(false) }
  }

  const fechar = async () => {
    if (!dados) return
    const pendencias = dados.avisos.length
    if (!await confirmar({
      titulo: `Fechar ${mesExtenso(mes, ano)} de ${placa}?`, confirmar: 'Fechar o mês', perigo: false,
      texto: <>As linhas automáticas ficam gravadas como estão hoje e nada mais pode ser alterado até reabrir.
        {pendencias > 0 && <> Ainda há {pendencias} aviso(s) na tela.</>}</>,
    })) return
    if (sujo && !await salvar(false)) return
    setSalvando(true)
    try {
      aplicar(await fechamentosApi.fechar(placa, ano, mes))
      toast.success('Mês fechado.')
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui fechar o mês.')) }
    finally { setSalvando(false) }
  }

  const reabrir = async () => {
    if (!await confirmar({
      titulo: `Reabrir ${mesExtenso(mes, ano)} de ${placa}?`, confirmar: 'Reabrir', perigo: false,
      texto: 'As linhas automáticas voltam a ser recalculadas (as que você corrigiu à mão ficam como estão).',
    })) return
    setSalvando(true)
    try {
      aplicar(await fechamentosApi.reabrir(placa, ano, mes))
      toast.success('Mês reaberto.')
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui reabrir o mês.')) }
    finally { setSalvando(false) }
  }

  const fechado = dados?.fechado === true
  const cab = (
    <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
      <div className="flex flex-col sm:flex-row sm:items-center gap-3">
        <NavegaMes mes={mes} ano={ano} onChange={onMes} />
        <Segmentado valor={placa} onChange={setPlaca} rotulo="Caminhão" opcoes={PLACAS_ACERTO.map(p => [p, p] as const)} />
      </div>
      <Botao variante="secundario" icone={Landmark} onClick={() => setConsorcios(true)}>Consórcios</Botao>
    </div>
  )

  if (falhou) return <div className="space-y-4">{cab}<ErroCarga mensagem="Não consegui montar o fechamento." onTentar={carregar} /></div>
  if (!dados || !ed || !t) return <div className="space-y-4">{cab}<Carregando linhas={6} /></div>

  return (
    <div className="space-y-5">
      {cab}
      {consorcios && <ConsorciosModal placa={placa} onClose={() => { setConsorcios(false); if (!sujo) carregar() }} />}

      <div className="flex flex-wrap items-center gap-2">
        {fechado
          ? <Selo tom="info">Fechado{dados.fechadoEm ? ` em ${dataBr(dados.fechadoEm.slice(0, 10))}` : ''}</Selo>
          : <Selo tom="atencao">Aberto</Selo>}
        <span className="text-sm text-gray-600">{placa} · {MESES[mes - 1]} de {ano}</span>
      </div>

      {dados.avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira antes de fechar" itens={dados.avisos} />}
      {fechado && <Aviso tipo="info">Mês fechado: nada pode ser alterado. Use Reabrir para mexer.</Aviso>}

      <fieldset disabled={fechado || salvando} className="space-y-5 min-w-0 border-0 p-0 m-0">
        <Secao titulo="Viagens do mês" nota="Pela data de saída. Resultado = valor total da viagem − despesas.">
          {dados.viagens.length === 0
            ? <Vazio compacto titulo="Nenhuma viagem saiu neste mês" />
            : <ul className="divide-y divide-gray-100">
              {dados.viagens.map((v, i) => (
                <li key={v.id} className="py-2 flex flex-wrap items-baseline gap-x-4 gap-y-1 text-sm">
                  <span className="font-medium text-gray-900 w-24">{i + 1}ª · {v.dataSaida ? dataBr(v.dataSaida) : 'sem data'}</span>
                  <span className="flex-1 min-w-[8rem] text-gray-600">{v.motorista}{v.destino ? ` · ${v.destino}` : ''}</span>
                  <span className="tabular-nums font-semibold">{brl(v.resultado)}</span>
                </li>
              ))}
            </ul>}
        </Secao>

        <Secao titulo="Créditos do período" nota="Entram na entrada total."
               acao={<Botao tamanho="sm" icone={Plus} onClick={() => set({ creditos: [...ed.creditos, { referencia: '', valor: 0 }] })}>Adicionar crédito</Botao>}>
          {ed.creditos.length === 0 && <p className="text-sm text-gray-500">Nenhum crédito neste mês.</p>}
          {ed.creditos.map((c, i) => (
            <div key={i} className="grid grid-cols-[1fr_auto] sm:grid-cols-[1fr_10rem_auto] gap-2 items-end">
              <Campo rotulo="Referência" className="col-span-2 sm:col-span-1">
                <input value={c.referencia} onChange={e => set({ creditos: ed.creditos.map((x, j) => j === i ? { ...x, referencia: e.target.value } : x) })} className={classeCampo} />
              </Campo>
              <Campo rotulo="Valor">
                <CampoNumero valor={c.valor} onChange={v => set({ creditos: ed.creditos.map((x, j) => j === i ? { ...x, valor: v ?? 0 } : x) })} />
              </Campo>
              <BotaoIcone icone={Trash2} rotulo="Apagar crédito" perigo onClick={() => set({ creditos: ed.creditos.filter((_, j) => j !== i) })} />
            </div>
          ))}
        </Secao>

        <Secao titulo="Despesas do mês" nota="As linhas automáticas se recalculam enquanto o mês está aberto. Mexeu no valor, ela vira manual."
               acao={<Botao tamanho="sm" icone={Plus} onClick={() => set({ despesas: [...ed.despesas, despesaVazia()] })}>Adicionar despesa</Botao>}>
          {ed.despesas.map((d, i) => (
            <div key={i} className={`rounded-lg border p-2.5 space-y-2 ${d.automatica ? 'bg-tinta-50/50 border-tinta-100' : 'border-gray-200'}`}>
              {d.automatica && (
                <div className="flex flex-wrap items-center gap-2 text-xs text-gray-600">
                  <Selo tom="info">{d.corrigida ? 'Automático, corrigido à mão' : 'Automático'}</Selo>
                  {d.deOndeVem && <span>{d.deOndeVem}</span>}
                  {d.corrigida && d.valorAutomatico != null && !fechado && (
                    <button type="button" onClick={() => voltarAutomatico(i)} className="underline text-tinta-700 min-h-8">
                      Voltar ao valor automático ({brl(d.valorAutomatico)})
                    </button>
                  )}
                </div>
              )}
              {d.aviso && !d.corrigida && <p className="text-xs font-medium text-amber-800">{d.aviso}</p>}
              <div className="grid grid-cols-2 sm:grid-cols-12 gap-2 items-end">
                <Campo rotulo="Data" className="sm:col-span-2">
                  <input type="date" value={d.data ?? ''} onChange={e => mudarDespesa(i, { data: e.target.value || null })} className={classeCampo} />
                </Campo>
                <Campo rotulo="Referência" className="col-span-2 sm:col-span-4">
                  <input value={d.referencia} readOnly={d.automatica} onChange={e => mudarDespesa(i, { referencia: e.target.value })}
                         className={`${classeCampo} ${d.automatica ? 'bg-gray-50' : ''}`} />
                </Campo>
                <Campo rotulo="Valor" className="sm:col-span-2">
                  <CampoNumero valor={d.valor} vazioNulo onChange={v => mudarValorDespesa(i, v)} />
                </Campo>
                <Campo rotulo="Parcela" className="sm:col-span-1">
                  <input value={d.parcela ?? ''} placeholder="1ª/3ª" onChange={e => mudarDespesa(i, { parcela: e.target.value || null })} className={classeCampo} />
                </Campo>
                <Campo rotulo="Observação" className="col-span-2 sm:col-span-2">
                  <input value={d.observacao ?? ''} onChange={e => mudarDespesa(i, { observacao: e.target.value || null })} className={classeCampo} />
                </Campo>
                {!d.automatica && (
                  <BotaoIcone icone={Trash2} rotulo="Apagar despesa" perigo className="sm:col-span-1 justify-self-end"
                              onClick={() => set({ despesas: ed.despesas.filter((_, j) => j !== i) })} />
                )}
              </div>
            </div>
          ))}
        </Secao>

        <Secao titulo="Totais do mês">
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-8 gap-y-1.5 text-sm max-w-2xl">
            <Linha rotulo="Resultado das viagens" valor={dados.totais.entradaViagens} />
            <Linha rotulo="Créditos" valor={t.creditos} />
            <Linha rotulo="Entrada total do mês" valor={t.entrada} forte />
            <Linha rotulo="Saída total do mês" valor={t.saida} forte />
            <Linha rotulo="Sobra do mês" valor={t.sobra} forte />
            <div className="flex items-center justify-between gap-3">
              <dt className="text-gray-600">Saldo anterior</dt>
              <dd className="tabular-nums font-medium">
                {dados.saldoAnteriorOrigem === 'fechamento'
                  ? brl(dados.totais.saldoAnterior)
                  : <span className="inline-block w-36"><CampoNumero valor={ed.saldoDigitado} vazioNulo onChange={v => set({ saldoDigitado: v })} aria-label="Saldo anterior" className={`${classeCampo} text-right`} /></span>}
              </dd>
            </div>
            <Linha rotulo="Total" valor={t.total} forte />
          </dl>
          {dados.saldoAnteriorOrigem === 'fechamento' && <p className="text-xs text-gray-500 mt-2">Saldo anterior = total do fechamento do mês passado.</p>}
        </Secao>

        <Secao titulo="Comissão" nota="Frete para comissão = volumes + retornos, sem caixinha e descarga.">
          {dados.viagens.length === 0
            ? <p className="text-sm text-gray-500">Sem viagens, sem comissão.</p>
            : <ul className="divide-y divide-gray-100 text-sm">
              {dados.viagens.map((v, i) => (
                <li key={v.id} className="py-2 flex flex-wrap items-baseline gap-x-4 gap-y-1">
                  <span className="w-24 font-medium">{i + 1}ª · {v.dataSaida ? dataBr(v.dataSaida) : 'sem data'}</span>
                  <span className="flex-1 min-w-[7rem] text-gray-600">Frete {brl(v.freteComissao)} · {num(v.porcentagemComissao, 0)}%</span>
                  <span className="tabular-nums font-semibold">{brl(v.comissao)}</span>
                </li>
              ))}
            </ul>}
          <dl className="mt-2 max-w-sm"><Linha rotulo="Total da comissão" valor={dados.totais.comissaoTotal} forte /></dl>
        </Secao>

        <Secao titulo="Salário do motorista" nota="FGTS, INSS, IRR e quinquênio também caem nas despesas do mês.">
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
            {([['salario', 'Salário'], ['horasExtras', 'Horas extras'], ['fgts', 'FGTS'], ['inss', 'INSS'], ['irr', 'IRR'], ['quinquenio', 'Quinquênio']] as const).map(([k, r]) => (
              <Campo key={k} rotulo={r}><CampoNumero valor={ed.salario[k]} onChange={v => mudarSalario(k, v ?? 0)} /></Campo>
            ))}
          </div>
          <div className="mt-3 space-y-2">
            <div className="flex items-center justify-between">
              <h4 className="text-sm font-semibold text-gray-800">Vales</h4>
              <Botao tamanho="sm" icone={Plus} onClick={() => set({ vales: [...ed.vales, { data: null, valor: 0, referencia: null }] })}>Adicionar vale</Botao>
            </div>
            {ed.vales.length === 0 && <p className="text-sm text-gray-500">Nenhum vale neste mês.</p>}
            {ed.vales.map((v, i) => (
              <div key={i} className="grid grid-cols-2 sm:grid-cols-[10rem_10rem_1fr_auto] gap-2 items-end">
                <Campo rotulo="Data"><input type="date" value={v.data ?? ''} onChange={e => set({ vales: ed.vales.map((x, j) => j === i ? { ...x, data: e.target.value || null } : x) })} className={classeCampo} /></Campo>
                <Campo rotulo="Valor"><CampoNumero valor={v.valor} onChange={val => set({ vales: ed.vales.map((x, j) => j === i ? { ...x, valor: val ?? 0 } : x) })} /></Campo>
                <Campo rotulo="Referência" className="col-span-2 sm:col-span-1"><input value={v.referencia ?? ''} onChange={e => set({ vales: ed.vales.map((x, j) => j === i ? { ...x, referencia: e.target.value || null } : x) })} className={classeCampo} /></Campo>
                <BotaoIcone icone={Trash2} rotulo="Apagar vale" perigo onClick={() => set({ vales: ed.vales.filter((_, j) => j !== i) })} />
              </div>
            ))}
          </div>
          <dl className="mt-3 max-w-sm space-y-1.5">
            <Linha rotulo="Total de vales" valor={t.vales} />
            <Linha rotulo="Salário líquido" valor={t.liquido} forte />
          </dl>
          <p className="text-xs text-gray-500 mt-1">Líquido = salário + horas extras + comissão + quinquênio − INSS − IRR − vales.</p>
        </Secao>

        <Campo rotulo="Observação do mês">
          <textarea value={ed.observacao} onChange={e => set({ observacao: e.target.value })} rows={2} className={classeCampo} />
        </Campo>
      </fieldset>

      <div className="flex flex-col sm:flex-row gap-2 sm:items-center sticky bottom-0 bg-white/90 backdrop-blur py-2 border-t border-gray-200">
        {!fechado && <Botao variante="primario" icone={Save} onClick={() => salvar()} carregando={salvando} textoCarregando="Salvando..." disabled={!sujo}>Salvar</Botao>}
        {!fechado && <Botao variante="secundario" icone={Lock} onClick={fechar} disabled={salvando}>Fechar o mês</Botao>}
        {fechado && <Botao variante="secundario" icone={LockOpen} onClick={reabrir} disabled={salvando}>Reabrir</Botao>}
        {sujo && !fechado && <span className="text-sm text-amber-800">Há alterações sem salvar.</span>}
      </div>
    </div>
  )
}

function Secao({ titulo, nota, acao, children }: { titulo: string; nota?: string; acao?: ReactNode; children: ReactNode }) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl p-3 sm:p-4 space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-display text-lg font-semibold text-gray-900 leading-tight">{titulo}</h3>
          {nota && <p className="text-xs text-gray-500 mt-0.5">{nota}</p>}
        </div>
        {acao}
      </div>
      {children}
    </section>
  )
}

function Linha({ rotulo, valor, forte = false }: { rotulo: string; valor: number; forte?: boolean }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className={forte ? 'font-semibold text-gray-900' : 'text-gray-600'}>{rotulo}</dt>
      <dd className={`tabular-nums ${forte ? 'font-semibold' : 'font-medium'} ${valor < 0 ? 'text-red-700' : ''}`}>{brl(valor)}</dd>
    </div>
  )
}

// ───────────────────────────── Consórcios ─────────────────────────────

const consorcioVazio = (placa: string): ConsorcioEntrada => {
  const h = new Date()
  return { placa, descricao: 'CONSÓRCIO VOLVO', cota: null, valorParcela: 0, totalParcelas: 0, primeiroAno: h.getFullYear(), primeiroMes: h.getMonth() + 1, ativo: true }
}

function ConsorciosModal({ placa, onClose }: { placa: string; onClose: () => void }) {
  const confirmar = useConfirmar()
  const [lista, setLista] = useState<Consorcio[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [form, setForm] = useState<{ id: number | null; e: ConsorcioEntrada } | null>(null)
  const [salvando, setSalvando] = useState(false)

  const carregar = useCallback(() => {
    setFalhou(false)
    fechamentosApi.consorcios().then(setLista).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const salvar = async () => {
    if (!form) return
    setSalvando(true)
    try {
      if (form.id) await fechamentosApi.salvarConsorcio(form.id, form.e)
      else await fechamentosApi.criarConsorcio(form.e)
      toast.success('Consórcio salvo.')
      setForm(null); carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui salvar o consórcio.')) }
    finally { setSalvando(false) }
  }

  const excluir = async (c: Consorcio) => {
    if (!await confirmar({ titulo: 'Apagar o consórcio?', texto: `${c.placa} · ${c.descricao}${c.cota ? ` · ${c.cota}` : ''}. Meses já fechados não mudam.` })) return
    try { await fechamentosApi.excluirConsorcio(c.id); toast.success('Consórcio apagado.'); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  const set = (p: Partial<ConsorcioEntrada>) => setForm(f => f && { ...f, e: { ...f.e, ...p } })

  return (
    <Modal titulo="Consórcios" onClose={onClose} largura="lg" sujo={form != null}
           descricao="Cada mês, dentro do prazo, cai uma parcela nas despesas do caminhão.">
      {falhou && <ErroCarga mensagem="Não consegui carregar os consórcios." onTentar={carregar} />}
      {!falhou && lista == null && <Carregando linhas={2} />}
      {lista && !form && (
        <>
          {lista.length === 0
            ? <Vazio compacto titulo="Nenhum consórcio cadastrado" />
            : <ul className="divide-y divide-gray-100">
              {lista.map(c => (
                <li key={c.id} className="py-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                  <div className="flex-1 min-w-[10rem]">
                    <p className="font-medium text-gray-900">{c.descricao}{c.cota ? ` · ${c.cota}` : ''} {!c.ativo && <Selo>Inativo</Selo>}</p>
                    <p className="text-gray-600">{c.placa} · {brl(c.valorParcela)} × {c.totalParcelas} · começa em {MESES[c.primeiroMes - 1]} de {c.primeiroAno}</p>
                  </div>
                  <Botao tamanho="sm" variante="secundario" onClick={() => setForm({ id: c.id, e: { ...c } })}>Editar</Botao>
                  <BotaoIcone icone={Trash2} rotulo="Apagar consórcio" perigo onClick={() => excluir(c)} />
                </li>
              ))}
            </ul>}
          <Botao variante="primario" icone={Plus} onClick={() => setForm({ id: null, e: consorcioVazio(placa) })}>Novo consórcio</Botao>
        </>
      )}
      {form && (
        <div className="space-y-3">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Campo rotulo="Caminhão">
              <select value={form.e.placa} onChange={e => set({ placa: e.target.value })} className={classeCampo}>
                {PLACAS_ACERTO.map(p => <option key={p} value={p}>{p}</option>)}
              </select>
            </Campo>
            <Campo rotulo="Descrição"><input value={form.e.descricao} onChange={e => set({ descricao: e.target.value })} className={classeCampo} /></Campo>
            <Campo rotulo="Cota" dica="Vai na observação da despesa"><input value={form.e.cota ?? ''} placeholder="COTA 126" onChange={e => set({ cota: e.target.value || null })} className={classeCampo} /></Campo>
            <Campo rotulo="Valor da parcela"><CampoNumero valor={form.e.valorParcela} onChange={v => set({ valorParcela: v ?? 0 })} /></Campo>
            <Campo rotulo="Total de parcelas"><input type="number" min={1} value={form.e.totalParcelas || ''} onChange={e => set({ totalParcelas: Number(e.target.value) })} className={classeCampo} /></Campo>
            <Campo rotulo="Mês da primeira parcela">
              <div className="grid grid-cols-2 gap-2">
                <select value={form.e.primeiroMes} onChange={e => set({ primeiroMes: Number(e.target.value) })} className={classeCampo} aria-label="Mês">
                  {MESES.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
                </select>
                <input type="number" value={form.e.primeiroAno} onChange={e => set({ primeiroAno: Number(e.target.value) })} className={classeCampo} aria-label="Ano" />
              </div>
            </Campo>
          </div>
          <label className="inline-flex items-center gap-2 min-h-11 text-sm"><input type="checkbox" checked={form.e.ativo} onChange={e => set({ ativo: e.target.checked })} className="w-5 h-5" /> Ativo</label>
          <BotoesModal salvando={salvando} onSalvar={salvar} onClose={() => setForm(null)} />
        </div>
      )}
    </Modal>
  )
}
