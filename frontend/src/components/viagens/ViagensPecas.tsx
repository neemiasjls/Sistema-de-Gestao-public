import { useEffect, useState, type ChangeEvent, type InputHTMLAttributes, type ReactNode } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import toast from 'react-hot-toast'
import { BotaoIcone } from '../ui/Botao'
import Aviso from '../ui/Aviso'
import { Modal, Campo, BotoesModal } from '../ModalBase'
import { classeCampo, classeNumero } from '../EstoquePecas'
import { brl, hoje, lerNum, num } from '../../utils/formato'
import { erroDaApi } from '../../services/semPararApi'
import {
  MESES, NOME_STATUS, retornosApi, normalizarPlaca,
  type Retorno, type RetornoEntrada, type StatusViagem,
} from '../../services/viagensCaminhaoApi'

// Peças das telas Viagens dos caminhões e Retornos.

/** Mês anterior / próximo, com o nome no meio. */
export function NavegaMes({ mes, ano, onChange }: { mes: number; ano: number; onChange: (mes: number, ano: number) => void }) {
  const ir = (d: number) => {
    const t = new Date(ano, mes - 1 + d, 1)
    onChange(t.getMonth() + 1, t.getFullYear())
  }
  return (
    <div className="inline-flex items-center gap-1 bg-white border border-gray-200 rounded-xl p-1">
      <BotaoIcone icone={ChevronLeft} rotulo="Mês anterior" onClick={() => ir(-1)} />
      <p className="min-w-[10.5rem] text-center font-display text-lg font-semibold text-gray-900" aria-live="polite">
        {MESES[mes - 1]} de {ano}
      </p>
      <BotaoIcone icone={ChevronRight} rotulo="Próximo mês" onClick={() => ir(1)} />
    </div>
  )
}

const TONS_STATUS: Record<StatusViagem, string> = {
  lancada: 'bg-gema-100 text-gema-900 border-gema-200',
  conferida: 'bg-tinta-50 text-tinta-800 border-tinta-200',
  fechada: 'bg-gray-100 text-gray-700 border-gray-200',
}

export function SeloStatus({ status }: { status: StatusViagem }) {
  return <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded-md border whitespace-nowrap ${TONS_STATUS[status]}`}>{NOME_STATUS[status]}</span>
}

export function Selo({ children, tom = 'neutro' }: { children: ReactNode; tom?: 'neutro' | 'atencao' | 'info' }) {
  const t = tom === 'atencao' ? 'bg-amber-50 text-amber-900 border-amber-200'
    : tom === 'info' ? 'bg-tinta-50 text-tinta-800 border-tinta-100' : 'bg-gray-100 text-gray-700 border-gray-200'
  return <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded-md border whitespace-nowrap ${t}`}>{children}</span>
}

/**
 * Campo de número no jeito brasileiro (1.234,56). Enquanto digita guarda o texto;
 * ao sair do campo mostra formatado. Vazio = 0 (ou null com `vazioNulo`).
 */
export function CampoNumero({ valor, onChange, casas = 2, vazioNulo = false, className = '', ...resto }: {
  valor: number | null
  onChange: (v: number | null) => void
  casas?: number
  vazioNulo?: boolean
} & Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'>) {
  const fmt = (v: number | null) => v == null || (v === 0 && vazioNulo) ? '' : v === 0 ? '' : num(v, v % 1 === 0 && casas <= 2 ? 0 : casas)
  const [texto, setTexto] = useState(fmt(valor))
  const [focado, setFocado] = useState(false)
  useEffect(() => { if (!focado) setTexto(fmt(valor)) }, [valor, focado]) // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <input
      {...resto}
      inputMode="decimal"
      value={texto}
      onFocus={() => setFocado(true)}
      onBlur={() => { setFocado(false); setTexto(fmt(valor)) }}
      onChange={e => {
        const t = e.target.value.replace(/[^\d.,-]/g, '')
        setTexto(t)
        onChange(t.trim() === '' ? (vazioNulo ? null : 0) : lerNum(t))
      }}
      className={`${classeNumero} ${className}`}
    />
  )
}

// ───────────── Retorno (a linha da planilha CONTROLE MENSAL DE RETORNOS) ─────────────

export const retornoVazio = (): RetornoEntrada => ({
  data: hoje(), destino: null, transportadora: null, valorFrete: 0, pedagio: 0, adicional: 0, descontoSeguro: 0,
  porcentagemTip: null, descontoManual: null, motorista: null, placa: null, observacoes: null, formaPagamento: null,
  viagemId: null, porcentagemIcms: 0, foraDoControle: false, conferido: true,
})

export const entradaDoRetorno = (r: Retorno): RetornoEntrada => ({
  data: r.data, destino: r.destino, transportadora: r.transportadora, valorFrete: r.valorFrete, pedagio: r.pedagio,
  adicional: r.adicional, descontoSeguro: r.descontoSeguro, porcentagemTip: r.porcentagemTip, descontoManual: r.descontoManual,
  motorista: r.motorista, placa: r.placa, observacoes: r.observacoes, formaPagamento: r.formaPagamento, viagemId: r.viagemId,
  porcentagemIcms: r.porcentagemIcms, foraDoControle: r.foraDoControle, conferido: r.conferido,
})

/** DESCONT (H) e FRETE TOTAL (J), iguais às fórmulas da planilha. */
export function contaRetorno(e: Pick<RetornoEntrada, 'valorFrete' | 'pedagio' | 'adicional' | 'descontoSeguro' | 'porcentagemTip' | 'descontoManual'>) {
  const desconto = e.descontoManual ?? Math.round((e.valorFrete + e.adicional - e.pedagio) / 100 * (e.porcentagemTip ?? 0) * 10000) / 10000
  return { desconto, total: e.valorFrete + e.adicional - e.pedagio - desconto - e.descontoSeguro }
}

export function RetornoModal({ id, inicial, onClose, onSalvou, titulo }: {
  id: number | null
  inicial: RetornoEntrada
  onClose: () => void
  onSalvou: () => void
  titulo?: string
}) {
  const [e, setE] = useState<RetornoEntrada>(inicial)
  const [salvando, setSalvando] = useState(false)
  const [manual, setManual] = useState(inicial.descontoManual != null)
  const set = (p: Partial<RetornoEntrada>) => setE(x => ({ ...x, ...p }))
  const c = contaRetorno({ ...e, descontoManual: manual ? e.descontoManual ?? 0 : null })

  const salvar = async () => {
    if (!e.data) { toast.error('Informe a data do frete.'); return }
    if (e.valorFrete <= 0) { toast.error('Informe o valor do frete.'); return }
    setSalvando(true)
    try {
      const dto = { ...e, placa: e.placa ? normalizarPlaca(e.placa) : null, descontoManual: manual ? e.descontoManual ?? 0 : null }
      if (id) await retornosApi.salvar(id, dto)
      else await retornosApi.criar(dto)
      toast.success('Retorno salvo.')
      onSalvou()
    } catch (err) {
      toast.error(await erroDaApi(err, 'Não consegui salvar o retorno.'))
    } finally { setSalvando(false) }
  }

  const txt = (k: keyof RetornoEntrada) => (ev: ChangeEvent<HTMLInputElement>) => set({ [k]: ev.target.value || null } as Partial<RetornoEntrada>)
  return (
    <Modal titulo={titulo ?? (id ? 'Editar retorno' : 'Novo retorno')} onClose={onClose} largura="lg" confirmarAoFechar
           descricao="Frete total = valor do frete + adicional − pedágio − desconto − desconto do seguro.">
      {!e.conferido && <Aviso tipo="atencao">Lançado pelo motorista: confira os valores e salve marcado como conferido.</Aviso>}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Data do frete"><input type="date" value={e.data ?? ''} onChange={ev => set({ data: ev.target.value || null })} className={classeCampo} /></Campo>
        <Campo rotulo="Transportadora"><input value={e.transportadora ?? ''} onChange={txt('transportadora')} className={classeCampo} placeholder="AGROTRADE TRANSPORTES" /></Campo>
        <Campo rotulo="Origem" dica="Coluna OBSERVAÇÕES da planilha"><input value={e.observacoes ?? ''} onChange={txt('observacoes')} className={classeCampo} /></Campo>
        <Campo rotulo="Destino"><input value={e.destino ?? ''} onChange={txt('destino')} className={classeCampo} /></Campo>
        <Campo rotulo="Motorista"><input value={e.motorista ?? ''} onChange={txt('motorista')} className={classeCampo} /></Campo>
        <Campo rotulo="Placa"><input value={e.placa ?? ''} onChange={ev => set({ placa: ev.target.value.toUpperCase() || null })} className={classeCampo} placeholder="XAA-9457" /></Campo>
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <Campo rotulo="Valor do frete"><CampoNumero valor={e.valorFrete} onChange={v => set({ valorFrete: v ?? 0 })} /></Campo>
        <Campo rotulo="Pedágio"><CampoNumero valor={e.pedagio} onChange={v => set({ pedagio: v ?? 0 })} /></Campo>
        <Campo rotulo="Adicional"><CampoNumero valor={e.adicional} onChange={v => set({ adicional: v ?? 0 })} /></Campo>
        <Campo rotulo="Desconto do seguro"><CampoNumero valor={e.descontoSeguro} onChange={v => set({ descontoSeguro: v ?? 0 })} /></Campo>
        <Campo rotulo="Porcentagem TIP"><CampoNumero valor={e.porcentagemTip} vazioNulo onChange={v => set({ porcentagemTip: v })} /></Campo>
        <Campo rotulo="Desconto" dica={manual ? 'Digitado à mão' : 'Calculado pela porcentagem'}>
          {manual
            ? <CampoNumero valor={e.descontoManual ?? 0} onChange={v => set({ descontoManual: v ?? 0 })} />
            : <input readOnly value={num(c.desconto)} className={`${classeNumero} bg-gray-50`} />}
        </Campo>
        <Campo rotulo="Forma de pagamento">
          <select value={e.formaPagamento ?? ''} onChange={ev => set({ formaPagamento: ev.target.value || null })} className={classeCampo}>
            <option value="">Não informada</option>
            {['CARTÃO', 'DEPÓSITO', 'PALETES'].concat(e.formaPagamento && !['CARTÃO', 'DEPÓSITO', 'PALETES'].includes(e.formaPagamento) ? [e.formaPagamento] : [])
              .map(f => <option key={f} value={f}>{f.charAt(0) + f.slice(1).toLowerCase()}</option>)}
          </select>
        </Campo>
        <Campo rotulo="ICMS do frete (%)" dica="Coluna PORCENT. da viagem"><CampoNumero valor={e.porcentagemIcms} onChange={v => set({ porcentagemIcms: v ?? 0 })} /></Campo>
      </div>
      <div className="flex flex-col sm:flex-row sm:flex-wrap gap-x-5 gap-y-1 text-sm">
        <label className="inline-flex items-center gap-2 min-h-11"><input type="checkbox" checked={manual} onChange={ev => setManual(ev.target.checked)} className="w-5 h-5" /> Desconto digitado à mão</label>
        <label className="inline-flex items-center gap-2 min-h-11"><input type="checkbox" checked={e.conferido ?? true} onChange={ev => set({ conferido: ev.target.checked })} className="w-5 h-5" /> Conferido</label>
        <label className="inline-flex items-center gap-2 min-h-11"><input type="checkbox" checked={e.foraDoControle} onChange={ev => set({ foraDoControle: ev.target.checked })} className="w-5 h-5" /> Fora do controle mensal (só da viagem)</label>
      </div>
      <div className="rounded-xl bg-gema-50 border border-gema-200 px-3 py-2 flex items-center justify-between">
        <span className="text-sm text-gray-700">Frete total</span>
        <span className="font-display text-2xl font-semibold tabular-nums">{brl(c.total)}</span>
      </div>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

/** Lista de resultado de importação (uma linha por arquivo). */
export function ListaResultado({ titulo, itens, onFechar }: {
  titulo: string
  itens: { chave: string; nome: string; erro?: string | null; linha?: ReactNode; avisos?: string[]; problema?: boolean }[]
  onFechar: () => void
}) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl overflow-hidden animate-entrada">
      <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
        <h3 className="font-semibold text-gray-900">{titulo}</h3>
        <button type="button" onClick={onFechar} className="text-sm font-medium text-gray-600 hover:text-gray-900 min-h-10 px-3">Fechar</button>
      </div>
      <ul className="divide-y divide-gray-100">
        {itens.map(r => (
          <li key={r.chave} className={`px-4 py-3 text-sm space-y-0.5 ${r.erro ? 'bg-red-50/60' : r.problema ? 'bg-amber-50/50' : ''}`}>
            <p className="font-medium text-gray-900 break-words">{r.nome}</p>
            {r.erro ? <p className="text-red-800">{r.erro}</p> : r.linha && <div className="text-gray-700 tabular-nums">{r.linha}</div>}
            {(r.avisos ?? []).map((a, i) => <p key={i} className="text-amber-800">{a}</p>)}
          </li>
        ))}
      </ul>
    </section>
  )
}

/** Diz o mês por extenso (para títulos). */
export const mesExtenso = (mes: number, ano: number) => `${MESES[mes - 1]} de ${ano}`
