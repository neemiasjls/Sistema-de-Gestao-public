import { useState } from 'react'
import { ChevronRight, Copy, Download, FileSpreadsheet, FileText, Fuel, Pencil, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  cobrancasAbastecimentoApi as cobApi, erroCobranca, linhaBancoBento,
  type BoletoPosto, type Cobranca, type FaturaDetalhe, type FaturaPosto, type ListaCobrancas, type StatusCobranca,
} from '../services/cobrancasAbastecimentoApi'
import { dataBr, lerNum, num } from '../utils/formato'
import { Modal, Campo, BotoesModal } from './ModalBase'
import { useConfirmar } from './ui/Confirmar'
import { Carregando, Vazio } from './ui/Estados'
import Botao, { BotaoIcone } from './ui/Botao'
import Aviso from './ui/Aviso'

// Peças da tela Abastecimentos que mostram as cobranças guardadas: o painel de
// resumo, a lista no formato da planilha "BOLETOS POSTOS" e as planilhas de
// divisão geradas.

// Os nomes que o usuário usa na planilha dos boletos
export const POSTOS = ['7 VILA NORTE', 'ESTRADA', 'RODOVIA', 'BOA VIAGEM', 'TREVO', 'ZT']

const STATUS: Record<StatusCobranca, { rotulo: string; classe: string }> = {
  CONFERE:      { rotulo: 'Confere',      classe: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
  DIVERGE:      { rotulo: 'Diverge',      classe: 'bg-red-50 text-red-700 border-red-200' },
  FALTA_BOLETO: { rotulo: 'Falta boleto', classe: 'bg-amber-50 text-amber-800 border-amber-200' },
  FALTA_FATURA: { rotulo: 'Falta fatura', classe: 'bg-amber-50 text-amber-800 border-amber-200' },
}

export function SeloStatus({ status }: { status: StatusCobranca }) {
  const s = STATUS[status]
  return <span className={`inline-block text-xs font-medium border rounded-full px-2 py-0.5 whitespace-nowrap ${s.classe}`}>{s.rotulo}</span>
}

const SeloPaga = () => (
  <span className="inline-block text-xs font-medium border border-gray-200 bg-gray-100 text-gray-700 rounded-full px-2 py-0.5">Paga</span>
)

// Dono do caminhão com a cor dele (EMP-02): é dado, não enfeite
const COR_DONO: Record<string, string> = { BENTO: 'bg-bento', CAIO: 'bg-caio', DAVI: 'bg-dudu', DUDU: 'bg-dudu' }
function Dono({ dono }: { dono: string | null }) {
  if (!dono) return <span className="text-amber-800 font-medium">sem dono</span>
  const cor = COR_DONO[dono.toUpperCase()]
  return (
    <span className="inline-flex items-center gap-1.5">
      {cor && <span className={`w-2 h-2 rounded-full shrink-0 ${cor}`} aria-hidden />}
      {dono.charAt(0) + dono.slice(1).toLowerCase()}
    </span>
  )
}

const valorOuTraco = (v: number | null | undefined) => (v === null || v === undefined ? '—' : formatBRL(v))
const dataOuTraco = (d: string | null | undefined) => (d ? dataBr(d.slice(0, 10)) : '—')

// ───────────────────────── Painel ─────────────────────────

function Numero({ rotulo, valor, detalhe, estado }: {
  rotulo: string; valor: string; detalhe?: string; estado?: 'alerta' | 'erro'
}) {
  const cor = estado === 'erro' ? 'bg-red-50 border-red-200' : estado === 'alerta' ? 'bg-amber-50 border-amber-200' : 'bg-white border-gray-200'
  const texto = estado === 'erro' ? 'text-red-900' : estado === 'alerta' ? 'text-amber-900' : 'text-gray-900'
  const apoio = estado === 'erro' ? 'text-red-800' : estado === 'alerta' ? 'text-amber-800' : 'text-gray-500'
  return (
    <div className={`rounded-xl border px-3 py-2.5 ${cor}`}>
      <p className={`text-xs ${apoio}`}>{rotulo}</p>
      <p className={`font-display text-xl font-semibold tabular-nums leading-tight mt-0.5 ${texto}`}>{valor}</p>
      {detalhe && <p className={`text-xs tabular-nums ${apoio}`}>{detalhe}</p>}
    </div>
  )
}

export function PainelCobrancas({ dados }: { dados: ListaCobrancas | null }) {
  if (!dados) return <Carregando forma="cartoes" linhas={4} rotulo="Carregando o resumo dos postos" />
  const r = dados.resumo
  return (
    <div className="space-y-2">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-2">
        <Numero rotulo="Em aberto" valor={String(r.abertas)} detalhe={formatBRL(r.totalAberto)} />
        <Numero rotulo="A vencer" valor={formatBRL(r.totalAVencer)} />
        <Numero rotulo="Vencidas" valor={String(r.vencidas)} detalhe={r.vencidas ? formatBRL(r.totalVencido) : undefined}
                estado={r.vencidas ? 'erro' : undefined} />
        <Numero rotulo="Sem boleto" valor={String(r.semBoleto)} estado={r.semBoleto ? 'alerta' : undefined} />
        <Numero rotulo="Sem fatura" valor={String(r.semFatura)} estado={r.semFatura ? 'alerta' : undefined} />
        <Numero rotulo="Divergentes" valor={String(r.divergentes)} estado={r.divergentes ? 'erro' : undefined} />
      </div>
      {r.proximosVencimentos.length > 0 && (
        <div className="bg-white rounded-xl border border-gray-200 px-3 py-2.5 flex flex-col sm:flex-row sm:flex-wrap sm:items-center gap-x-5 gap-y-1.5 text-sm">
          <span className="text-xs font-medium text-gray-500">Próximos vencimentos</span>
          {r.proximosVencimentos.map((p, i) => (
            <span key={i} className="flex flex-wrap items-center gap-x-2 gap-y-0.5 min-w-0 text-gray-700">
              <b className="tabular-nums text-gray-900">{dataBr(p.data)}</b>
              <span>{p.posto}</span>
              <span className="tabular-nums text-gray-600">{valorOuTraco(p.valor)}</span>
              {p.status !== 'CONFERE' && <SeloStatus status={p.status} />}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

// ───────────────────────── Lista ─────────────────────────

export function ListaCobrancasPostos({ dados, recarregar, onGerarPlanilha }: {
  dados: ListaCobrancas | null
  recarregar: () => Promise<void>
  onGerarPlanilha: (faturas: { id: number; posto: string }[]) => Promise<void>
}) {
  const [aberta, setAberta] = useState<string | null>(null)
  const [selecionadas, setSelecionadas] = useState<number[]>([])
  const [editBoleto, setEditBoleto] = useState<BoletoPosto | null>(null)
  const [editFatura, setEditFatura] = useState<FaturaPosto | null>(null)
  const [gerando, setGerando] = useState(false)

  if (!dados) return <Carregando linhas={3} rotulo="Carregando os boletos dos postos" />

  const faturas = dados.cobrancas.flatMap(c => (c.fatura ? [c.fatura] : []))
  const alternar = (id: number) =>
    setSelecionadas(s => (s.includes(id) ? s.filter(x => x !== id) : [...s, id]))

  const gerar = async () => {
    setGerando(true)
    try { await onGerarPlanilha(faturas.filter(f => selecionadas.includes(f.id))); setSelecionadas([]) }
    finally { setGerando(false) }
  }

  return (
    <section className="space-y-2">
      <div className="flex flex-wrap items-end justify-between gap-x-4 gap-y-2 pt-2">
        <div className="min-w-0 max-w-[68ch]">
          <h2 className="text-lg font-semibold text-gray-900">Boletos dos postos</h2>
          <p className="text-sm text-gray-600">
            Cada linha confere a fatura com a soma dos abastecimentos, com o que foi para a planilha de divisão e com o boleto.
            Marque faturas para gerar a planilha de novo.
          </p>
        </div>
        {selecionadas.length > 0 && (
          <Botao variante="primario" icone={FileSpreadsheet} onClick={gerar} carregando={gerando} textoCarregando="Gerando..."
                 className="w-full sm:w-auto animate-entrada">
            Gerar planilha de {selecionadas.length} fatura{selecionadas.length > 1 ? 's' : ''}
          </Botao>
        )}
      </div>

      {dados.cobrancas.length === 0 ? (
        <Vazio icone={Fuel} titulo="Nenhuma fatura ou boleto guardado ainda">
          Solte os PDFs dos postos na área acima: as faturas e os boletos aparecem aqui, já conferidos.
        </Vazio>
      ) : <>
      {/* Celular: um cartão por cobrança; toca para abrir a conferência */}
      <ul className="sm:hidden bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        {dados.cobrancas.map(c => {
          const vencida = !c.paga && c.vencimento !== null && c.vencimento < dados.hoje
          const estaAberta = aberta === c.chave
          return (
            <li key={c.chave} className={estaAberta ? 'bg-gray-50' : ''}>
              <div className="flex items-start gap-1">
                {c.fatura ? (
                  <label className="w-11 shrink-0 flex justify-center pt-3 pb-3 cursor-pointer" title="Escolher para gerar a planilha de divisão">
                    <input type="checkbox" checked={selecionadas.includes(c.fatura.id)} onChange={() => c.fatura && alternar(c.fatura.id)}
                           aria-label={`Escolher a fatura ${c.posto} ${c.numero} para gerar a planilha de divisão`} className="w-5 h-5 mt-0.5 rounded border-gray-300" />
                  </label>
                ) : <span className="w-11 shrink-0" />}
                <button type="button" onClick={() => setAberta(a => (a === c.chave ? null : c.chave))} aria-expanded={estaAberta}
                        className="flex-1 min-w-0 text-left py-3 pr-3 space-y-1">
                  <span className="flex items-start justify-between gap-2">
                    <span className="font-semibold text-gray-900 min-w-0">{c.posto || <span className="text-amber-800">sem posto</span>}</span>
                    <span className="tabular-nums font-semibold text-gray-900 whitespace-nowrap">{valorOuTraco(c.valor)}</span>
                  </span>
                  <span className="block text-sm tabular-nums text-gray-600">
                    <span className={vencida ? 'text-red-700 font-semibold' : ''}>Vence {dataOuTraco(c.vencimento)}{vencida && ' (vencida)'}</span>
                    <span className="text-gray-500"> · Número {c.numero || 'sem número'} · Chegou {dataBr(c.dataChegada)}</span>
                  </span>
                  <span className="flex flex-wrap items-center gap-1">
                    <SeloStatus status={c.status} />
                    {c.paga && <SeloPaga />}
                    <ChevronRight size={18} aria-hidden className={`ml-auto text-gray-500 transition-transform duration-200 ${estaAberta ? 'rotate-90' : ''}`} />
                  </span>
                </button>
              </div>
              {estaAberta && (
                <div className="px-3 pb-3 animate-entrada">
                  <DetalheCobranca c={c} onEditarBoleto={setEditBoleto} onEditarFatura={setEditFatura} recarregar={recarregar} />
                </div>
              )}
            </li>
          )
        })}
      </ul>

      <div className="hidden sm:block bg-white rounded-xl border border-gray-200 overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs text-gray-500 border-b border-gray-100">
              <th className="pl-3 pr-1 py-2 w-8"><span className="sr-only">Escolher para gerar a planilha</span></th>
              <th className="px-3 py-2 font-medium">Chegou em</th>
              <th className="px-3 py-2 font-medium">Posto</th>
              <th className="px-3 py-2 font-medium">Número</th>
              <th className="px-3 py-2 font-medium text-right">Valor</th>
              <th className="px-3 py-2 font-medium">Vencimento</th>
              <th className="px-3 py-2 font-medium">Conferência</th>
              <th className="px-3 py-2 w-8"><span className="sr-only">Abrir</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {dados.cobrancas.map(c => {
              const vencida = !c.paga && c.vencimento !== null && c.vencimento < dados.hoje
              return (
                <LinhaCobranca key={c.chave} c={c} aberta={aberta === c.chave} vencida={vencida}
                               selecionada={c.fatura ? selecionadas.includes(c.fatura.id) : false}
                               onSelecionar={() => c.fatura && alternar(c.fatura.id)}
                               onAbrir={() => setAberta(a => (a === c.chave ? null : c.chave))}
                               onEditarBoleto={setEditBoleto} onEditarFatura={setEditFatura}
                               recarregar={recarregar} />
              )
            })}
          </tbody>
        </table>
      </div>
      </>}

      {editBoleto && (
        <EditarBoletoModal boleto={editBoleto} faturas={faturas} onClose={() => setEditBoleto(null)}
                           onSalvo={async () => { setEditBoleto(null); await recarregar() }} />
      )}
      {editFatura && (
        <EditarFaturaModal fatura={editFatura} onClose={() => setEditFatura(null)}
                           onSalvo={async () => { setEditFatura(null); await recarregar() }} />
      )}
    </section>
  )
}

function LinhaCobranca({ c, aberta, vencida, selecionada, onSelecionar, onAbrir, onEditarBoleto, onEditarFatura, recarregar }: {
  c: Cobranca; aberta: boolean; vencida: boolean; selecionada: boolean
  onSelecionar: () => void; onAbrir: () => void
  onEditarBoleto: (b: BoletoPosto) => void; onEditarFatura: (f: FaturaPosto) => void
  recarregar: () => Promise<void>
}) {
  return (
    <>
      <tr className={`cursor-pointer transition-colors hover:bg-gray-50 ${aberta ? 'bg-gray-50' : ''}`} onClick={onAbrir}>
        <td className="pl-3 pr-1 py-2" onClick={e => e.stopPropagation()}>
          {c.fatura && (
            <input type="checkbox" checked={selecionada} onChange={onSelecionar}
                   aria-label={`Escolher a fatura ${c.posto} ${c.numero} para gerar a planilha de divisão`}
                   title="Escolher para gerar a planilha de divisão" className="w-4 h-4 rounded border-gray-300 align-middle" />
          )}
        </td>
        <td className="px-3 py-2 tabular-nums text-gray-700">{dataBr(c.dataChegada)}</td>
        <td className="px-3 py-2 font-medium text-gray-900">{c.posto || <span className="text-amber-800">sem posto</span>}</td>
        <td className="px-3 py-2 tabular-nums text-gray-800">{c.numero || <span className="text-amber-800">sem número</span>}</td>
        <td className="px-3 py-2 text-right tabular-nums font-semibold text-gray-900">{valorOuTraco(c.valor)}</td>
        <td className={`px-3 py-2 tabular-nums ${vencida ? 'text-red-700 font-semibold' : 'text-gray-700'}`}>
          {dataOuTraco(c.vencimento)}{vencida && ' (vencida)'}
        </td>
        <td className="px-3 py-2">
          <span className="flex flex-wrap items-center gap-1">
            <SeloStatus status={c.status} />
            {c.paga && <SeloPaga />}
          </span>
        </td>
        <td className="px-3 py-2 text-gray-500">
          {/* O teclado abre pela seta; o clique na linha toda continua valendo */}
          <button type="button" onClick={e => { e.stopPropagation(); onAbrir() }} aria-expanded={aberta}
                  aria-label={`${aberta ? 'Fechar' : 'Abrir'} a conferência de ${c.posto} ${c.numero}`}
                  className="w-8 h-8 -my-1 inline-flex items-center justify-center rounded-lg hover:bg-gray-100 hover:text-gray-900">
            <ChevronRight size={16} aria-hidden className={`transition-transform duration-200 ${aberta ? 'rotate-90' : ''}`} />
          </button>
        </td>
      </tr>
      {aberta && (
        <tr>
          <td colSpan={8} className="px-3 pb-3 pt-1 bg-gray-50 animate-entrada">
            <DetalheCobranca c={c} onEditarBoleto={onEditarBoleto} onEditarFatura={onEditarFatura} recarregar={recarregar} />
          </td>
        </tr>
      )}
    </>
  )
}

function DetalheCobranca({ c, onEditarBoleto, onEditarFatura, recarregar }: {
  c: Cobranca
  onEditarBoleto: (b: BoletoPosto) => void; onEditarFatura: (f: FaturaPosto) => void
  recarregar: () => Promise<void>
}) {
  const [detalhe, setDetalhe] = useState<FaturaDetalhe | null>(null)
  const f = c.fatura
  const confirmar = useConfirmar()

  const tentar = async (acao: () => Promise<unknown>, erro: string) => {
    try { await acao() } catch (e) { toast.error(await erroCobranca(e, erro)) }
  }

  const verAbastecimentos = () => f && tentar(async () => {
    setDetalhe(detalhe ? null : (await cobApi.detalheFatura(f.id)).data)
  }, 'Não consegui abrir a fatura.')

  const excluirFatura = async () => f && await confirmar({
    titulo: 'Excluir a fatura?', texto: <><b>{f.posto} {f.numero}</b>. O boleto dela continua guardado.</>, confirmar: 'Excluir fatura',
  }) && tentar(async () => { await cobApi.excluirFatura(f.id); toast.success('Fatura excluída.'); await recarregar() }, 'Não consegui excluir a fatura.')

  const excluirBoleto = async (b: BoletoPosto) => await confirmar({
    titulo: 'Excluir o boleto?', texto: <><b>{b.posto} {b.numeroDocumento}</b>{b.valor != null && <>, {formatBRL(b.valor)}</>}.</>, confirmar: 'Excluir boleto',
  }) && tentar(async () => { await cobApi.excluirBoleto(b.id); toast.success('Boleto excluído.'); await recarregar() }, 'Não consegui excluir o boleto.')

  const marcarPago = (b: BoletoPosto, pago: boolean) => tentar(async () => {
    await cobApi.editarBoleto(b.id, {
      posto: b.posto, numeroDocumento: b.numeroDocumento, nossoNumero: b.nossoNumero, valor: b.valor,
      vencimento: b.vencimento, dataChegada: b.dataChegada, faturaId: b.faturaId, ligacaoAutomatica: !b.ligacaoManual,
      pagoEm: pago ? new Date().toLocaleDateString('sv-SE') : null,
    })
    await recarregar()
  }, 'Não consegui marcar o pagamento.')

  return (
    <div className="space-y-3 pt-1">
      {c.diferencas.length > 0 && (
        <ul className={`text-sm rounded-lg border px-3 py-2 space-y-0.5 ${
          c.status === 'DIVERGE' ? 'bg-red-50 border-red-200 text-red-800' : 'bg-amber-50 border-amber-200 text-amber-900'
        }`}>
          {c.diferencas.map((d, i) => <li key={i}>{d}</li>)}
        </ul>
      )}

      <div className="grid gap-3 md:grid-cols-2">
        {/* Fatura */}
        <div className="bg-white rounded-lg border border-gray-200 p-3 space-y-2">
          <h3 className="text-sm font-semibold text-gray-800">Fatura</h3>
          {f ? (
            <>
              <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
                <dt className="text-gray-500">Valor da fatura</dt><dd className="text-right tabular-nums">{valorOuTraco(f.valorFatura)}</dd>
                <dt className="text-gray-500">Soma dos abastecimentos</dt><dd className="text-right tabular-nums">{formatBRL(f.soma)} <span className="text-gray-500">({f.quantidade})</span></dd>
                <dt className="text-gray-500">Na planilha de divisão</dt><dd className="text-right tabular-nums">{formatBRL(f.somaNaPlanilha)}</dd>
                <dt className="text-gray-500">Emissão / vencimento</dt><dd className="text-right tabular-nums">{dataOuTraco(f.emissao)} / {dataOuTraco(f.vencimento)}</dd>
                <dt className="text-gray-500">Bento</dt>
                <dd className="text-right tabular-nums">
                  {f.quantidadeBento > 0 ? <>{formatBRL(f.valorBento)} <span className="text-gray-500">({f.quantidadeBento})</span></> : '—'}
                </dd>
              </dl>
              {f.quantidadeBento > 0 && (() => {
                const l = linhaBancoBento(f)
                return (
                  <div className="flex items-center justify-between gap-2 rounded-lg bg-blue-50 border border-blue-200 pl-3 pr-1 py-1 text-sm text-blue-900">
                    <span className="tabular-nums min-w-0">{l.data || 'sem vencimento'} · {formatBRL(f.valorBento)} · {l.descricao}</span>
                    <button title="Copia as três colunas para colar na planilha do banco"
                            onClick={() => navigator.clipboard.writeText(l.texto)
                              .then(() => toast.success('Linha do Bento copiada.'))
                              .catch(() => toast.error('Não consegui copiar.'))}
                            className="inline-flex items-center gap-1.5 shrink-0 font-semibold min-h-10 sm:min-h-8 px-2 -mr-1 rounded-lg hover:bg-blue-100 hover:text-blue-950">
                      <Copy size={14} aria-hidden /> Copiar
                    </button>
                  </div>
                )
              })()}
              <div className="flex flex-wrap gap-2 pt-1">
                <Botao tamanho="sm" icone={Download} onClick={() => tentar(() => cobApi.baixarFatura(f), 'Não consegui baixar a fatura.')}>Fatura (PDF)</Botao>
                <Botao tamanho="sm" icone={FileText} onClick={verAbastecimentos} aria-expanded={!!detalhe}>{detalhe ? 'Esconder' : 'Ver'} abastecimentos</Botao>
                <Botao tamanho="sm" icone={Pencil} onClick={() => onEditarFatura(f)}>Corrigir dados</Botao>
                <Botao tamanho="sm" variante="perigoLeve" icone={Trash2} onClick={excluirFatura}>Excluir</Botao>
              </div>
            </>
          ) : (
            <p className="text-sm text-amber-800">
              A fatura deste boleto ainda não foi importada. Quando chegar, ela se liga sozinha pelo número e pelo posto.
            </p>
          )}
        </div>

        {/* Boletos */}
        <div className="bg-white rounded-lg border border-gray-200 p-3 space-y-2">
          <h3 className="text-sm font-semibold text-gray-800">Boleto{c.boletos.length > 1 ? 's' : ''}</h3>
          {c.boletos.length === 0 && (
            <p className="text-sm text-amber-800">O boleto desta fatura ainda não foi importado.</p>
          )}
          {c.boletos.map(b => (
            <div key={b.id} className="space-y-2">
              {!b.lidoDoPdf && (
                <p className="text-xs text-amber-800 bg-amber-50 border border-amber-200 rounded-md px-2 py-1">
                  O PDF deste boleto não pôde ser lido: confira e preencha os dados.
                </p>
              )}
              {b.lidoDoPdf && b.avisos.map((a, i) => (
                <p key={i} className="text-xs text-amber-800">{a}</p>
              ))}
              <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
                <dt className="text-gray-500">Valor</dt><dd className="text-right tabular-nums">{valorOuTraco(b.valor)}</dd>
                <dt className="text-gray-500">Vencimento</dt><dd className="text-right tabular-nums">{dataOuTraco(b.vencimento)}</dd>
                <dt className="text-gray-500">Número do documento</dt><dd className="text-right tabular-nums">{b.numeroDocumento || '—'}</dd>
                <dt className="text-gray-500">Nosso número</dt><dd className="text-right tabular-nums">{b.nossoNumero || '—'}</dd>
                <dt className="text-gray-500">Chegou em</dt><dd className="text-right tabular-nums">{dataBr(b.dataChegada)}</dd>
                <dt className="text-gray-500">Ligação com a fatura</dt><dd className="text-right">{b.ligacaoManual ? 'feita à mão' : 'automática'}</dd>
              </dl>
              <div className="flex flex-wrap items-center gap-2 pt-1">
                <label className="inline-flex items-center gap-2 text-sm text-gray-800 mr-1 min-h-10 sm:min-h-8 cursor-pointer">
                  <input type="checkbox" checked={!!b.pagoEm} onChange={e => marcarPago(b, e.target.checked)} className="w-5 h-5 sm:w-4 sm:h-4 rounded border-gray-300" />
                  {b.pagoEm ? <>Pago em <span className="tabular-nums">{dataBr(b.pagoEm)}</span></> : 'Pago'}
                </label>
                {b.temPdf && <Botao tamanho="sm" icone={Download} onClick={() => tentar(() => cobApi.baixarBoleto(b), 'Não consegui baixar o boleto.')}>Boleto (PDF)</Botao>}
                <Botao tamanho="sm" icone={Pencil} onClick={() => onEditarBoleto(b)}>Editar</Botao>
                <Botao tamanho="sm" variante="perigoLeve" icone={Trash2} onClick={() => excluirBoleto(b)}>Excluir</Botao>
              </div>
            </div>
          ))}
        </div>
      </div>

      {detalhe && (
        <div className="space-y-2 animate-entrada">
          <h3 className="text-sm font-semibold text-gray-800">
            Abastecimentos da fatura <span className="font-normal text-gray-500 tabular-nums">({detalhe.abastecimentos.length})</span>
          </h3>
          {/* Celular: cartões (data, placa e valor na mesma linha); computador: tabela */}
          <ul className="sm:hidden bg-white rounded-lg border border-gray-200 divide-y divide-gray-100">
            {detalhe.abastecimentos.map((a, i) => (
              <li key={i} className="px-3 py-2.5 space-y-0.5">
                <div className="flex items-baseline justify-between gap-3">
                  <span className="text-sm text-gray-800">
                    <span className="tabular-nums">{dataBr(a.data)}</span>
                    <span className="text-gray-500"> · </span>
                    <span className="font-semibold tabular-nums">{a.placa}</span>
                  </span>
                  <span className="font-semibold text-gray-900 tabular-nums whitespace-nowrap">{formatBRL(a.valor)}</span>
                </div>
                <div className="flex items-baseline justify-between gap-3 text-sm text-gray-600">
                  <span className="min-w-0 truncate">{a.motorista || 'Motorista não veio no PDF'}</span>
                  <Dono dono={a.dono} />
                </div>
              </li>
            ))}
          </ul>
          <div className="hidden sm:block bg-white rounded-lg border border-gray-200 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-gray-500 border-b border-gray-100">
                  <th className="px-3 py-2 font-medium">Data</th>
                  <th className="px-3 py-2 font-medium">Placa</th>
                  <th className="px-3 py-2 font-medium">Motorista</th>
                  <th className="px-3 py-2 font-medium">Dono</th>
                  <th className="px-3 py-2 font-medium text-right">Valor</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {detalhe.abastecimentos.map((a, i) => (
                  <tr key={i} className="hover:bg-gray-50">
                    <td className="px-3 py-1.5 tabular-nums">{dataBr(a.data)}</td>
                    <td className="px-3 py-1.5 tabular-nums font-medium">{a.placa}</td>
                    <td className="px-3 py-1.5 text-gray-600">{a.motorista || '—'}</td>
                    <td className="px-3 py-1.5"><Dono dono={a.dono} /></td>
                    <td className="px-3 py-1.5 text-right tabular-nums">{formatBRL(a.valor)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-gray-200 font-semibold text-gray-900">
                  <td className="px-3 py-2" colSpan={4}>Soma</td>
                  <td className="px-3 py-2 text-right tabular-nums">{formatBRL(detalhe.soma)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}

// ───────────────────────── Edição ─────────────────────────

const campo = 'w-full min-h-11 sm:min-h-0 border border-gray-300 rounded-lg px-3 py-2 text-sm'

function EditarBoletoModal({ boleto, faturas, onClose, onSalvo }: {
  boleto: BoletoPosto; faturas: FaturaPosto[]; onClose: () => void; onSalvo: () => Promise<void>
}) {
  const [posto, setPosto] = useState(boleto.posto)
  const [numero, setNumero] = useState(boleto.numeroDocumento)
  const [nosso, setNosso] = useState(boleto.nossoNumero)
  const [valor, setValor] = useState(boleto.valor === null ? '' : num(boleto.valor))
  const [vencimento, setVencimento] = useState(boleto.vencimento ?? '')
  const [chegada, setChegada] = useState(boleto.dataChegada)
  const [pagoEm, setPagoEm] = useState(boleto.pagoEm ?? '')
  // 'auto' liga pelo número e posto; 'nenhuma' deixa sem fatura; ou o id escolhido
  const [ligacao, setLigacao] = useState(boleto.ligacaoManual ? (boleto.faturaId ? String(boleto.faturaId) : 'nenhuma') : 'auto')
  const [salvando, setSalvando] = useState(false)

  const salvar = async () => {
    if (!chegada) { toast.error('Informe a data de chegada.'); return }
    if (valor.trim() && lerNum(valor) < 0) { toast.error('O valor não pode ser negativo.'); return }
    setSalvando(true)
    try {
      await cobApi.editarBoleto(boleto.id, {
        posto: posto.trim(), numeroDocumento: numero.trim(), nossoNumero: nosso.trim(),
        valor: valor.trim() ? Math.round(lerNum(valor) * 100) / 100 : null,
        vencimento: vencimento || null, dataChegada: chegada, pagoEm: pagoEm || null,
        ligacaoAutomatica: ligacao === 'auto',
        faturaId: ligacao === 'auto' || ligacao === 'nenhuma' ? null : Number(ligacao),
      })
      toast.success('Boleto salvo.')
      await onSalvo()
    } catch (e) {
      toast.error(await erroCobranca(e, 'Não consegui salvar o boleto.'))
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo="Boleto do posto" onClose={onClose} confirmarAoFechar>
      {!boleto.lidoDoPdf && (
        <Aviso tipo="atencao">
          O PDF não pôde ser lido (tem senha ou está num formato que o sistema não conhece). Preencha com os dados impressos no boleto.
        </Aviso>
      )}
      <Campo rotulo="Posto">
        <input value={posto} onChange={e => setPosto(e.target.value.toUpperCase())} list="postos-cobranca" className={campo} />
        <datalist id="postos-cobranca">{POSTOS.map(p => <option key={p} value={p} />)}</datalist>
      </Campo>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Número do documento (número da fatura)">
          <input value={numero} onChange={e => setNumero(e.target.value)} className={`${campo} font-mono`} />
        </Campo>
        <Campo rotulo="Nosso número">
          <input value={nosso} onChange={e => setNosso(e.target.value)} className={`${campo} font-mono`} />
        </Campo>
        <Campo rotulo="Valor">
          <input value={valor} inputMode="decimal" placeholder="0,00" onChange={e => setValor(e.target.value)}
                 onBlur={() => valor.trim() && setValor(num(lerNum(valor)))} className={`${campo} tabular-nums`} />
        </Campo>
        <Campo rotulo="Vencimento">
          <input type="date" value={vencimento} onChange={e => setVencimento(e.target.value)} className={campo} />
        </Campo>
        <Campo rotulo="Chegou em">
          <input type="date" value={chegada} onChange={e => setChegada(e.target.value)} className={campo} />
        </Campo>
        <Campo rotulo="Pago em">
          <input type="date" value={pagoEm} onChange={e => setPagoEm(e.target.value)} className={campo} />
        </Campo>
      </div>
      <Campo rotulo="Fatura deste boleto">
        <select value={ligacao} onChange={e => setLigacao(e.target.value)} className={campo}>
          <option value="auto">Automática (pelo número do documento e pelo posto)</option>
          <option value="nenhuma">Nenhuma</option>
          {faturas.map(f => (
            <option key={f.id} value={String(f.id)}>
              {f.posto} {f.numero || '(sem número)'} — {valorOuTraco(f.valorFatura)} — vence {dataOuTraco(f.vencimento)}
            </option>
          ))}
        </select>
      </Campo>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

function EditarFaturaModal({ fatura, onClose, onSalvo }: {
  fatura: FaturaPosto; onClose: () => void; onSalvo: () => Promise<void>
}) {
  const [posto, setPosto] = useState(fatura.posto)
  const [numero, setNumero] = useState(fatura.numero)
  const [emissao, setEmissao] = useState(fatura.emissao ?? '')
  const [vencimento, setVencimento] = useState(fatura.vencimento ?? '')
  const [valor, setValor] = useState(fatura.valorFatura === null ? '' : num(fatura.valorFatura))
  const [salvando, setSalvando] = useState(false)

  const salvar = async () => {
    if (!posto.trim()) { toast.error('Informe o posto.'); return }
    setSalvando(true)
    try {
      await cobApi.editarFatura(fatura.id, {
        posto: posto.trim(), numero: numero.trim(), emissao: emissao || null, vencimento: vencimento || null,
        valorFatura: valor.trim() ? Math.round(lerNum(valor) * 100) / 100 : null,
      })
      toast.success('Fatura salva.')
      await onSalvo()
    } catch (e) {
      toast.error(await erroCobranca(e, 'Não consegui salvar a fatura.'))
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo="Dados da fatura" onClose={onClose} confirmarAoFechar>
      <p className="text-sm text-gray-500">
        Para corrigir o que não veio do PDF. Os abastecimentos continuam os do PDF (são eles que vão para a planilha).
      </p>
      <Campo rotulo="Posto">
        <input value={posto} onChange={e => setPosto(e.target.value.toUpperCase())} list="postos-fatura" className={campo} />
        <datalist id="postos-fatura">{POSTOS.map(p => <option key={p} value={p} />)}</datalist>
      </Campo>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Número da fatura">
          <input value={numero} onChange={e => setNumero(e.target.value)} className={`${campo} font-mono`} />
        </Campo>
        <Campo rotulo="Valor da fatura">
          <input value={valor} inputMode="decimal" placeholder="0,00" onChange={e => setValor(e.target.value)}
                 onBlur={() => valor.trim() && setValor(num(lerNum(valor)))} className={`${campo} tabular-nums`} />
        </Campo>
        <Campo rotulo="Emissão">
          <input type="date" value={emissao} onChange={e => setEmissao(e.target.value)} className={campo} />
        </Campo>
        <Campo rotulo="Vencimento">
          <input type="date" value={vencimento} onChange={e => setVencimento(e.target.value)} className={campo} />
        </Campo>
      </div>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

// ───────────────────────── Planilhas ─────────────────────────

export function PlanilhasGuardadas({ dados, recarregar }: { dados: ListaCobrancas | null; recarregar: () => Promise<void> }) {
  const confirmar = useConfirmar()
  if (!dados || dados.planilhas.length === 0) return null

  const baixar = async (id: number, nome: string) => {
    try { await cobApi.baixarPlanilha({ id, nome }) }
    catch (e) { toast.error(await erroCobranca(e, 'Não consegui baixar a planilha.')) }
  }
  const excluir = async (id: number, nome: string) => {
    if (!await confirmar({ titulo: 'Excluir a planilha?', texto: <b className="break-all">{nome}</b>, confirmar: 'Excluir planilha' })) return
    try { await cobApi.excluirPlanilha(id); toast.success('Planilha excluída.'); await recarregar() }
    catch (e) { toast.error(await erroCobranca(e, 'Não consegui excluir a planilha.')) }
  }

  return (
    <section className="space-y-2">
      <h2 className="text-lg font-semibold text-gray-900 pt-2">Planilhas de divisão geradas</h2>
      <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
        {dados.planilhas.map(p => (
          <li key={p.id} className="pl-4 pr-2 py-2.5 flex items-center gap-3 text-sm">
            <FileSpreadsheet size={18} className="text-tinta-500 shrink-0" aria-hidden />
            <div className="flex-1 min-w-0 sm:flex sm:items-center sm:gap-4">
              <div className="min-w-0 sm:flex-1">
                <p className="font-medium text-gray-900 break-all">{p.nome}</p>
                <p className="text-sm text-gray-500 tabular-nums">
                  {p.faturas ? `Fatura ${p.faturas}` : 'Sem número de fatura'}
                  <span className="sm:hidden"> · {formatBRL(p.soma)} em {p.quantidade} abastecimento{p.quantidade === 1 ? "" : "s"}</span>
                </p>
              </div>
              <span className="hidden sm:inline tabular-nums text-gray-800 whitespace-nowrap">
                {formatBRL(p.soma)} <span className="text-gray-500">({p.quantidade})</span>
              </span>
              <span className="block text-xs text-gray-500 tabular-nums whitespace-nowrap">
                {new Date(p.criadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}
              </span>
            </div>
            <span className="flex items-center shrink-0">
              <BotaoIcone icone={Download} rotulo={`Baixar ${p.nome}`} onClick={() => baixar(p.id, p.nome)} />
              <BotaoIcone icone={Trash2} rotulo={`Excluir ${p.nome}`} perigo onClick={() => excluir(p.id, p.nome)} />
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
