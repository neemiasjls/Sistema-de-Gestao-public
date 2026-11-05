import { Inbox, Plus, Search } from 'lucide-react'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Aviso from '../components/ui/Aviso'
import { Segmentado } from '../components/ui/Abas'
import Botao from '../components/ui/Botao'
import { classeCampo, classeNumero } from '../components/EstoquePecas'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { financeiroApi, veiculosApi, formatBRL, type SaldoReceber, type Veiculo } from '../services/api'
import { dataBr } from '../utils/formato'

const EMPRESAS_COMUNS = ['Logix Logística/Zico', 'Agrotrade', 'TransRota']
const TIPOS = ['Frete', 'Descarga', 'Diária', 'Outros']
const CARTOES = ['TipBank', 'PamCard']

interface SaldosData {
  totalPendente: number
  saldos: SaldoReceber[]
}

const data = (d: string | null | undefined) => (!d ? '' : /^\d{4}-\d{2}-\d{2}/.test(d) ? dataBr(d.slice(0, 10)) : d)
const hojeIso = () => new Date().toISOString().split('T')[0]

function Situacao({ s }: { s: SaldoReceber }) {
  return s.recebido
    ? <span className="inline-flex text-xs font-medium px-2 py-0.5 rounded-full bg-gray-100 text-gray-700 border border-gray-200 whitespace-nowrap tabular-nums">Recebido {data(s.dataRecebimento)}</span>
    : <span className="inline-flex text-xs font-medium px-2 py-0.5 rounded-full bg-gema-50 text-gema-900 border border-gema-200">Pendente</span>
}

export default function SaldosPage() {
  const [dados, setDados] = useState<SaldosData>({ totalPendente: 0, saldos: [] })
  const [veiculos, setVeiculos] = useState<Veiculo[]>([])
  const [filtroRecebido, setFiltroRecebido] = useState<'false' | 'true' | ''>('false')
  const [filtroEmpresa, setFiltroEmpresa] = useState('')
  const [loading, setLoading] = useState(false)
  const [carregou, setCarregou] = useState(false)
  const [falhou, setFalhou] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [recebendo, setRecebendo] = useState<SaldoReceber | null>(null)

  const carregar = useCallback(async () => {
    setLoading(true)
    try {
      const [{ data: s }, { data: grupos }] = await Promise.all([
        financeiroApi.listarSaldos({
          recebido: filtroRecebido === '' ? undefined : filtroRecebido === 'true',
          empresa: filtroEmpresa || undefined,
        }),
        veiculosApi.listar(),
      ])
      setDados(s)
      setVeiculos(grupos.flatMap((g: { veiculos: Veiculo[] }) => g.veiculos))
      setFalhou(false)
      setCarregou(true)
    } catch {
      setFalhou(true)
    } finally { setLoading(false) }
  }, [filtroRecebido, filtroEmpresa])

  // A busca por empresa espera a pessoa parar de digitar (antes pedia o botão Filtrar)
  useEffect(() => {
    const t = setTimeout(carregar, 300)
    return () => clearTimeout(t)
  }, [carregar])

  // Agrupar por empresa
  const porEmpresa = dados.saldos
    .filter(s => !s.recebido)
    .reduce<Record<string, { total: number; saldos: SaldoReceber[] }>>((acc, s) => {
      if (!acc[s.empresa]) acc[s.empresa] = { total: 0, saldos: [] }
      acc[s.empresa].total += s.valor
      acc[s.empresa].saldos.push(s)
      return acc
    }, {})

  const botaoReceber = (s: SaldoReceber) => !s.recebido && (
    <Botao variante="secundario" tamanho="sm" onClick={() => setRecebendo(s)}>Receber</Botao>
  )

  return (
    <div className="space-y-5">
      <CabecalhoTela descricao="O que cada cliente ainda tem a pagar."
                     acoes={<Botao variante="primario" icone={Plus} onClick={() => setShowForm(true)}>Novo saldo</Botao>} />

      {/* Total em destaque, com o que cada empresa deve logo embaixo */}
      {!carregou && loading ? <Carregando forma="cartoes" linhas={2} rotulo="Carregando o total" /> : carregou && (
        <section aria-label="Total pendente" className="bg-white border border-gray-200 rounded-xl overflow-hidden">
          <div className="h-1 bg-gema-400" aria-hidden />
          <div className="p-4 sm:p-5 flex flex-col sm:flex-row sm:items-end gap-4">
            <div className="sm:w-64 shrink-0">
              <p className="text-sm font-medium text-gray-600">Total pendente a receber</p>
              <p className="font-display text-3xl font-semibold text-gray-900 tabular-nums leading-tight">{formatBRL(dados.totalPendente)}</p>
            </div>
            {Object.keys(porEmpresa).length > 0 && (
              <ul className="flex-1 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-x-5 gap-y-2 sm:border-l sm:border-gray-200 sm:pl-5">
                {Object.entries(porEmpresa).map(([empresa, info]) => (
                  <li key={empresa} className="min-w-0 flex sm:block items-baseline justify-between gap-3">
                    <p className="text-sm text-gray-700 truncate">{empresa} <span className="text-gray-500 tabular-nums">· {info.saldos.length}</span></p>
                    <p className="font-semibold text-gray-900 tabular-nums">{formatBRL(info.total)}</p>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
      )}

      {/* Filtros */}
      <div className="flex flex-col sm:flex-row sm:items-end gap-3">
        <Segmentado rotulo="Situação" valor={filtroRecebido} onChange={setFiltroRecebido}
                    opcoes={[['false', 'Pendentes'], ['true', 'Recebidos'], ['', 'Todos']]} />
        <div className="relative sm:w-64">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
          <input value={filtroEmpresa} onChange={e => setFiltroEmpresa(e.target.value)} type="search"
                 placeholder="Buscar empresa" aria-label="Buscar empresa" className={`${classeCampo} pl-9`} />
        </div>
      </div>

      {/* Lista */}
      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
        {loading && !carregou ? (
          <div className="p-3"><Carregando linhas={3} rotulo="Carregando os saldos" /></div>
        ) : falhou ? (
          <div className="p-3"><ErroCarga mensagem="Não consegui carregar os saldos." onTentar={carregar} /></div>
        ) : dados.saldos.length === 0 ? (
          <Vazio compacto icone={Inbox} titulo={filtroEmpresa ? 'Nenhum saldo com esse nome' : filtroRecebido === 'false' ? 'Nada pendente' : 'Nenhum saldo'}>
            {filtroEmpresa ? 'Tente outro nome ou limpe a busca.' : filtroRecebido === 'false' ? 'Todos os saldos foram recebidos.' : 'Os saldos lançados aparecem aqui.'}
          </Vazio>
        ) : (
          <>
          {/* Celular: cartões */}
          <ul className={`sm:hidden divide-y divide-gray-100 transition-opacity ${loading ? 'opacity-60' : ''}`}>
            {dados.saldos.map(s => (
              <li key={s.id} className="px-4 py-3 space-y-1.5">
                <div className="flex items-start justify-between gap-3">
                  <p className="font-medium text-gray-900 min-w-0 break-words">{s.empresa}</p>
                  <p className="font-display text-lg font-semibold text-gray-900 tabular-nums whitespace-nowrap leading-tight">{formatBRL(s.valor)}</p>
                </div>
                <p className="text-sm text-gray-600 tabular-nums">
                  {[s.tipo, s.cartaoFrete, s.veiculo?.placa, data(s.dataLancamento)].filter(Boolean).join(' · ')}
                </p>
                <div className="flex items-center justify-between gap-2">
                  <Situacao s={s} />
                  {botaoReceber(s)}
                </div>
              </li>
            ))}
          </ul>
          <div className={`hidden sm:block overflow-x-auto transition-opacity ${loading ? 'opacity-60' : ''}`}>
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-4 py-2.5 text-left font-medium">Empresa</th>
                  <th className="px-4 py-2.5 text-left font-medium">Tipo</th>
                  <th className="px-4 py-2.5 text-left font-medium">Cartão</th>
                  <th className="px-4 py-2.5 text-left font-medium">Placa</th>
                  <th className="px-4 py-2.5 text-left font-medium">Data</th>
                  <th className="px-4 py-2.5 text-right font-medium">Valor</th>
                  <th className="px-4 py-2.5 text-left font-medium">Situação</th>
                  <th className="px-4 py-2.5"><span className="sr-only">Ações</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {dados.saldos.map(s => (
                  <tr key={s.id} className="hover:bg-gray-50">
                    <td className="px-4 py-2.5 font-medium text-gray-900">{s.empresa}</td>
                    <td className="px-4 py-2.5 text-gray-700">{s.tipo || '—'}</td>
                    <td className="px-4 py-2.5 text-gray-700">{s.cartaoFrete || '—'}</td>
                    <td className="px-4 py-2.5 text-gray-700 tabular-nums">{s.veiculo?.placa || '—'}</td>
                    <td className="px-4 py-2.5 text-gray-600 whitespace-nowrap tabular-nums">{data(s.dataLancamento)}</td>
                    <td className="px-4 py-2.5 text-right font-semibold text-gray-900 tabular-nums">{formatBRL(s.valor)}</td>
                    <td className="px-4 py-2.5"><Situacao s={s} /></td>
                    <td className="px-3 py-1.5 text-right">{botaoReceber(s)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          </>
        )}
      </div>

      {showForm && (
        <ModalNovoSaldo veiculos={veiculos} onClose={() => setShowForm(false)} onSalvo={() => { setShowForm(false); carregar() }} />
      )}
      {recebendo && (
        <ModalReceber saldo={recebendo} onClose={() => setRecebendo(null)} onSalvo={() => { setRecebendo(null); carregar() }} />
      )}
    </div>
  )
}

function ModalNovoSaldo({ veiculos, onClose, onSalvo }: { veiculos: Veiculo[]; onClose: () => void; onSalvo: () => void }) {
  const [salvando, setSalvando] = useState(false)
  const [form, setForm] = useState({
    empresa: '', tipo: 'Frete', valor: '', dataLancamento: hojeIso(), cartaoFrete: '', veiculoId: '',
  })

  const criar = async () => {
    if (!form.empresa || !form.valor) { toast.error('Preencha empresa e valor.'); return }
    setSalvando(true)
    try {
      await financeiroApi.criarSaldo({
        empresa: form.empresa,
        tipo: form.tipo,
        valor: parseFloat(form.valor.replace(',', '.')),
        dataLancamento: form.dataLancamento,
        cartaoFrete: form.cartaoFrete || null,
        veiculoId: form.veiculoId ? Number(form.veiculoId) : null,
      })
      toast.success('Saldo lançado.')
      onSalvo()
    } catch { toast.error('Não consegui salvar.') }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo="Lançar saldo a receber" onClose={onClose} confirmarAoFechar>
      <Campo rotulo="Empresa">
        <input value={form.empresa} onChange={e => setForm(p => ({ ...p, empresa: e.target.value }))}
               list="empresas-comuns" placeholder="Ex: Logix Logística" className={classeCampo} />
      </Campo>
      <datalist id="empresas-comuns">{EMPRESAS_COMUNS.map(e => <option key={e} value={e} />)}</datalist>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Valor (R$)">
          <input value={form.valor} onChange={e => setForm(p => ({ ...p, valor: e.target.value }))}
                 inputMode="decimal" placeholder="0,00" className={classeNumero} />
        </Campo>
        <Campo rotulo="Data">
          <input type="date" value={form.dataLancamento} onChange={e => setForm(p => ({ ...p, dataLancamento: e.target.value }))} className={classeCampo} />
        </Campo>
        <Campo rotulo="Tipo">
          <select value={form.tipo} onChange={e => setForm(p => ({ ...p, tipo: e.target.value }))} className={classeCampo}>
            {TIPOS.map(t => <option key={t}>{t}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Cartão de frete">
          <select value={form.cartaoFrete} onChange={e => setForm(p => ({ ...p, cartaoFrete: e.target.value }))} className={classeCampo}>
            <option value="">Nenhum</option>
            {CARTOES.map(c => <option key={c}>{c}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Veículo" className="sm:col-span-2">
          <select value={form.veiculoId} onChange={e => setForm(p => ({ ...p, veiculoId: e.target.value }))} className={classeCampo}>
            <option value="">Nenhum</option>
            {veiculos.map(v => <option key={v.id} value={v.id}>{v.placa} ({v.socio})</option>)}
          </select>
        </Campo>
      </div>
      <BotoesModal salvando={salvando} onSalvar={criar} onClose={onClose} rotulo="Lançar" />
    </Modal>
  )
}

function ModalReceber({ saldo, onClose, onSalvo }: { saldo: SaldoReceber; onClose: () => void; onSalvo: () => void }) {
  const [form, setForm] = useState({ dataRecebimento: hojeIso(), numeroDocumento: '' })
  const [salvando, setSalvando] = useState(false)

  const confirmarReceber = async () => {
    setSalvando(true)
    try {
      await financeiroApi.receberSaldo(saldo.id, form)
      toast.success('Saldo marcado como recebido. Lançamento bancário gerado.')
      onSalvo()
    } catch { toast.error('Não consegui confirmar o recebimento.') }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo="Confirmar recebimento" onClose={onClose} confirmarAoFechar
           descricao={<>{saldo.empresa} · <span className="tabular-nums">{formatBRL(saldo.valor)}</span></>}>
      <Campo rotulo="Data do recebimento">
        <input type="date" value={form.dataRecebimento} onChange={e => setForm(p => ({ ...p, dataRecebimento: e.target.value }))} className={classeCampo} />
      </Campo>
      <Campo rotulo="Número do documento / TIP BANK">
        <input value={form.numeroDocumento} onChange={e => setForm(p => ({ ...p, numeroDocumento: e.target.value }))}
               placeholder="Ex: TIP-12345" className={classeCampo} />
      </Campo>
      <Aviso tipo="info">Se for TIP BANK, um lançamento bancário é gerado sozinho para o Bento.</Aviso>
      <BotoesModal salvando={salvando} onSalvar={confirmarReceber} onClose={onClose} rotulo="Confirmar recebimento" />
    </Modal>
  )
}
