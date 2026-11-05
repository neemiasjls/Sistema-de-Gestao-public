import { Download, Plus, Wallet } from 'lucide-react'
import { comoLista } from '../utils/resposta'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import Campo from '../components/ui/Campo'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Modal, BotoesModal } from '../components/ModalBase'
import { classeCampo, classeNumero } from '../components/EstoquePecas'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import api, { financeiroApi, veiculosApi, exportApi, downloadBlob, formatBRL, socioColor, type LancamentoBanco, type Veiculo, type Socio } from '../services/api'
import { dataBr } from '../utils/formato'

const SOCIOS: Socio[] = ['Bento', 'Caio', 'Dudu']
const TIPOS_LANCAMENTO = ['Entrada', 'Saida', 'Transferencia']
const nomeTipo = (t: string) => ({ Saida: 'Saída', Transferencia: 'Transferência' }[t] ?? t)

interface Resumo { entradas: number; saidas: number; saldo: number }
interface ResumoSocio { socio: string; entradas: number; saidas: number; saldo: number }

// Data que vem como aaaa-mm-dd (ou com hora) vira dd/mm/aaaa; outra forma fica como veio
const data = (d: string) => (/^\d{4}-\d{2}-\d{2}/.test(d) ? dataBr(d.slice(0, 10)) : d)

// Entrada sem cor de dono (o verde é do Caio, EMP-02): o sinal diz o sentido;
// só a saída fica em vermelho
const valorAssinado = (l: LancamentoBanco) => (
  <span className={`tabular-nums whitespace-nowrap font-semibold ${l.tipo === 'Saida' ? 'text-red-700' : 'text-gray-900'}`}>
    {l.tipo === 'Saida' ? '− ' : l.tipo === 'Entrada' ? '+ ' : ''}{formatBRL(l.valor)}
  </span>
)

function SeloSocio({ socio }: { socio: string }) {
  const c = socioColor(socio as Socio)
  if (!c) return <span className="text-xs text-gray-600">{socio}</span>
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full font-medium border ${c.bg} ${c.text} ${c.border}`}>{socio}</span>
}

export default function FinanceiroPage() {
  const agora = new Date()
  const [mes, setMes] = useState(agora.getMonth() + 1)
  const [ano, setAno] = useState(agora.getFullYear())
  const [filtroSocio, setFiltroSocio] = useState('')
  const [lancamentos, setLancamentos] = useState<LancamentoBanco[]>([])
  const [resumo, setResumo] = useState<Resumo>({ entradas: 0, saidas: 0, saldo: 0 })
  const [resumoSocios, setResumoSocios] = useState<ResumoSocio[]>([])
  const [veiculos, setVeiculos] = useState<Veiculo[]>([])
  const [loading, setLoading] = useState(false)
  const [carregou, setCarregou] = useState(false)
  const [falhou, setFalhou] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [exportando, setExportando] = useState(false)

  const carregar = useCallback(async () => {
    setLoading(true)
    try {
      const [{ data: r }, { data: rs }, { data: grupos }] = await Promise.all([
        financeiroApi.listarLancamentos({ mes, ano, socio: filtroSocio as Socio | undefined }),
        // Antes ia por fetch() cru, sem o endereço da API e sem o token: no ar caía
        // no index.html do site e derrubava a carga toda
        api.get('/api/financeiro/resumo-mensal', { params: { mes, ano } }).then(r => ({ data: comoLista<ResumoSocio>(r.data, 'resumo por sócio') })),
        veiculosApi.listar(),
      ])
      setLancamentos(r.lancamentos)
      setResumo(r.resumo)
      setResumoSocios(rs)
      setVeiculos(grupos.flatMap((g: { veiculos: Veiculo[] }) => g.veiculos))
      setFalhou(false)
      setCarregou(true)
    } catch {
      setFalhou(true)
    } finally { setLoading(false) }
  }, [mes, ano, filtroSocio])

  useEffect(() => { carregar() }, [carregar])

  const exportarAbastecimento = async () => {
    setExportando(true)
    try {
      const { data } = await exportApi.abastecimento(mes, ano)
      downloadBlob(data, `abastecimento_${ano}_${String(mes).padStart(2, '0')}.xlsx`)
    } catch { toast.error('Não consegui exportar.') }
    finally { setExportando(false) }
  }

  const nomeMes = new Date(2024, mes - 1).toLocaleString('pt-BR', { month: 'long' })

  return (
    <div className="space-y-5">
      <CabecalhoTela
        descricao="Movimentação por sócio."
        acoes={<>
          <Botao variante="secundario" icone={Download} onClick={exportarAbastecimento} carregando={exportando}>Abastecimento Excel</Botao>
          <Botao variante="primario" icone={Plus} onClick={() => setShowForm(true)}>Novo lançamento</Botao>
        </>}
      />

      {/* Período e sócio: tudo embaixo é deste filtro */}
      <div className="grid grid-cols-2 sm:flex sm:flex-wrap sm:items-end gap-3">
        <Campo rotulo="Mês" className="sm:w-40">
          <select value={mes} onChange={e => setMes(Number(e.target.value))} className={classeCampo}>
            {Array.from({ length: 12 }, (_, i) => (
              <option key={i + 1} value={i + 1}>{new Date(2024, i).toLocaleString('pt-BR', { month: 'long' })}</option>
            ))}
          </select>
        </Campo>
        <Campo rotulo="Ano" className="sm:w-24">
          <input type="number" inputMode="numeric" value={ano} onChange={e => setAno(Number(e.target.value))} className={classeNumero} />
        </Campo>
        <Campo rotulo="Sócio" className="col-span-2 sm:w-48">
          <select value={filtroSocio} onChange={e => setFiltroSocio(e.target.value)} className={classeCampo}>
            <option value="">Todos os sócios</option>
            {SOCIOS.map(s => <option key={s}>{s}</option>)}
          </select>
        </Campo>
      </div>

      {/* Resumo do período: o saldo é o número que importa */}
      {!carregou && loading ? (
        <Carregando forma="cartoes" linhas={3} rotulo="Carregando o resumo" />
      ) : carregou && (
        <>
          <dl className="grid grid-cols-3 gap-px bg-gray-200 rounded-xl border border-gray-200 overflow-hidden [&>div]:bg-white [&>div]:px-3.5 [&>div]:py-3 [&>div]:min-w-0">
            <div>
              <dt className="text-xs font-medium text-gray-500">Entradas</dt>
              <dd className="font-display text-lg sm:text-xl font-semibold text-gray-900 tabular-nums break-words">{formatBRL(resumo.entradas)}</dd>
            </div>
            <div>
              <dt className="text-xs font-medium text-gray-500">Saídas</dt>
              <dd className="font-display text-lg sm:text-xl font-semibold text-red-700 tabular-nums break-words">{formatBRL(resumo.saidas)}</dd>
            </div>
            <div className="!bg-gray-50">
              <dt className="text-xs font-medium text-gray-500">Saldo de <span className="first-letter:uppercase">{nomeMes}</span></dt>
              <dd className={`font-display text-lg sm:text-2xl font-semibold tabular-nums break-words ${resumo.saldo >= 0 ? 'text-gray-900' : 'text-red-700'}`}>{formatBRL(resumo.saldo)}</dd>
            </div>
          </dl>

          {resumoSocios.length > 0 && (
            <section aria-labelledby="fin-socios" className="space-y-2">
              <h2 id="fin-socios" className="text-base font-semibold text-gray-900">Por sócio</h2>
              <ul className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                {resumoSocios.map(rs => {
                  const c = socioColor(rs.socio as Socio)
                  return (
                    <li key={rs.socio} className="bg-white rounded-xl border border-gray-200 p-4">
                      <p className="flex items-center gap-2">
                        <span className={`w-2.5 h-2.5 rounded-full ${c?.dot ?? 'bg-gray-400'}`} aria-hidden />
                        <span className="font-semibold text-gray-900">{rs.socio}</span>
                      </p>
                      <p className={`font-display text-xl font-semibold tabular-nums mt-1 ${rs.saldo >= 0 ? 'text-gray-900' : 'text-red-700'}`}>{formatBRL(rs.saldo)}</p>
                      <p className="text-sm text-gray-600 tabular-nums flex flex-wrap gap-x-3">
                        <span>+ {formatBRL(rs.entradas)}</span>
                        <span className="text-red-700">− {formatBRL(rs.saidas)}</span>
                      </p>
                    </li>
                  )
                })}
              </ul>
            </section>
          )}
        </>
      )}

      {/* Lançamentos */}
      <section aria-labelledby="fin-lancamentos" className="space-y-2">
        <h2 id="fin-lancamentos" className="text-base font-semibold text-gray-900">Lançamentos</h2>
        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
          {loading && !carregou ? (
            <div className="p-3"><Carregando linhas={3} rotulo="Carregando os lançamentos" /></div>
          ) : falhou ? (
            <div className="p-3"><ErroCarga mensagem="Não consegui carregar os lançamentos." onTentar={carregar} /></div>
          ) : lancamentos.length === 0 ? (
            <Vazio compacto icone={Wallet} titulo="Nenhum lançamento no período"
                   acao={<Botao variante="secundario" icone={Plus} onClick={() => setShowForm(true)}>Novo lançamento</Botao>}>
              Troque o mês ou lance o primeiro.
            </Vazio>
          ) : (
            <>
            {/* Celular: cartões */}
            <ul className={`sm:hidden divide-y divide-gray-100 transition-opacity ${loading ? 'opacity-60' : ''}`}>
              {lancamentos.map(l => (
                <li key={l.id} className="px-4 py-3 space-y-1.5">
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm font-medium text-gray-900 min-w-0 break-words">{l.descricao || 'Sem descrição'}</p>
                    {valorAssinado(l)}
                  </div>
                  <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-sm text-gray-600 tabular-nums">
                    <span>{data(l.data)}</span>
                    <SeloSocio socio={l.socio} />
                    <span>{nomeTipo(l.tipo)}</span>
                    {l.placa && <span>{l.placa}</span>}
                  </div>
                </li>
              ))}
            </ul>
            <div className={`hidden sm:block overflow-x-auto transition-opacity ${loading ? 'opacity-60' : ''}`}>
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-xs text-gray-500">
                  <tr>
                    <th className="px-4 py-2.5 text-left font-medium">Data</th>
                    <th className="px-4 py-2.5 text-left font-medium">Sócio</th>
                    <th className="px-4 py-2.5 text-left font-medium">Tipo</th>
                    <th className="px-4 py-2.5 text-left font-medium">Descrição</th>
                    <th className="px-4 py-2.5 text-left font-medium">Placa</th>
                    <th className="px-4 py-2.5 text-right font-medium">Valor</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {lancamentos.map(l => (
                    <tr key={l.id} className="hover:bg-gray-50">
                      <td className="px-4 py-2.5 text-gray-600 whitespace-nowrap tabular-nums">{data(l.data)}</td>
                      <td className="px-4 py-2.5"><SeloSocio socio={l.socio} /></td>
                      <td className="px-4 py-2.5 text-gray-700">{nomeTipo(l.tipo)}</td>
                      <td className="px-4 py-2.5 text-gray-800 max-w-xs truncate">{l.descricao || '—'}</td>
                      <td className="px-4 py-2.5 text-gray-700 tabular-nums">{l.placa || '—'}</td>
                      <td className="px-4 py-2.5 text-right">{valorAssinado(l)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            </>
          )}
        </div>
      </section>

      {showForm && (
        <ModalLancamento veiculos={veiculos} onClose={() => setShowForm(false)}
                         onSalvo={() => { setShowForm(false); carregar() }} />
      )}
    </div>
  )
}

function ModalLancamento({ veiculos, onClose, onSalvo }: { veiculos: Veiculo[]; onClose: () => void; onSalvo: () => void }) {
  const [salvando, setSalvando] = useState(false)
  const [form, setForm] = useState({
    socio: 'Bento' as Socio,
    tipo: 'Entrada',
    descricao: '',
    valor: '',
    data: new Date().toISOString().split('T')[0],
    veiculoId: '',
  })

  const criar = async () => {
    if (!form.valor || !form.data) { toast.error('Preencha valor e data.'); return }
    setSalvando(true)
    try {
      await financeiroApi.criarLancamento({
        socio: form.socio,
        tipo: form.tipo,
        descricao: form.descricao || null,
        valor: parseFloat(form.valor.replace(',', '.')),
        data: form.data,
        veiculoId: form.veiculoId ? Number(form.veiculoId) : null,
      })
      toast.success('Lançamento registrado.')
      onSalvo()
    } catch { toast.error('Não consegui salvar.') }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo="Novo lançamento" onClose={onClose} confirmarAoFechar>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Sócio">
          <select value={form.socio} onChange={e => setForm(p => ({ ...p, socio: e.target.value as Socio }))} className={classeCampo}>
            {SOCIOS.map(s => <option key={s}>{s}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Tipo">
          <select value={form.tipo} onChange={e => setForm(p => ({ ...p, tipo: e.target.value }))} className={classeCampo}>
            {TIPOS_LANCAMENTO.map(t => <option key={t} value={t}>{nomeTipo(t)}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Valor (R$)">
          <input value={form.valor} onChange={e => setForm(p => ({ ...p, valor: e.target.value }))}
                 inputMode="decimal" placeholder="0,00" className={classeNumero} />
        </Campo>
        <Campo rotulo="Data">
          <input type="date" value={form.data} onChange={e => setForm(p => ({ ...p, data: e.target.value }))} className={classeCampo} />
        </Campo>
        <Campo rotulo="Veículo (opcional)">
          <select value={form.veiculoId} onChange={e => setForm(p => ({ ...p, veiculoId: e.target.value }))} className={classeCampo}>
            <option value="">Nenhum</option>
            {veiculos.map(v => <option key={v.id} value={v.id}>{v.placa} ({v.socio})</option>)}
          </select>
        </Campo>
        <Campo rotulo="Descrição">
          <input value={form.descricao} onChange={e => setForm(p => ({ ...p, descricao: e.target.value }))}
                 placeholder="Ex: TIP BANK — LOGIX" className={classeCampo} />
        </Campo>
      </div>
      <BotoesModal salvando={salvando} onSalvar={criar} onClose={onClose} />
    </Modal>
  )
}
