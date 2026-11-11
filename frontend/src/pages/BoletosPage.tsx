import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import AreaSoltar from '../components/ui/AreaSoltar'
import { useConfirmar } from '../components/ui/Confirmar'
import ComparaBoletos from '../components/ComparaBoletos'
import ExtratoSicoob from '../components/ExtratoSicoob'
import ConfigEmissaoBoletos from '../components/ConfigEmissaoBoletos'
import MontarBoleto from '../components/MontarBoleto'
import ParaEmitirBoletos from '../components/ParaEmitirBoletos'
import ProdutosCAReceber from '../components/ProdutosCAReceber'
import { Abas, PainelAba, Segmentado, useAbas } from '../components/ui/Abas'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Aviso from '../components/ui/Aviso'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { ArrowDownToLine, ArrowUpFromLine, Ban, Calculator, CalendarClock, ChevronRight, ClipboardCheck, Landmark, Download, FileUp, Inbox, Pencil, Plus, RotateCcw, Trash2, TriangleAlert } from 'lucide-react'
import { boletosApi, clientesApi, formatBRL, type Boleto, type Cliente } from '../services/api'
import {
  boletosContasApi as api,
  type AvulsoEdicao, type BoletoAPagar, type BoletoReceber, type ClienteReceber, type ListaAPagar, type ListaAReceber, type ReceberEdicao,
} from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { dataBr, lerNum, num } from '../utils/formato'
import { useAuth } from '../contexts/AuthContext'
import { sicoobApi } from '../services/sicoobApi'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import CalculoFretePerfil from '../components/CalculoFretePerfil'
import { useSearchParams } from 'react-router-dom'

// Boletos em duas abas. "A pagar": os dos postos (os mesmos da tela
// Abastecimentos) e os avulsos subidos aqui. "A receber": os que a
// transportadora emite para os clientes, cada cliente com a sua lista, como na
// planilha de controle dele.

// Sicoob (BOL-12): a configuração da emissão e o extrato. A lista "Para emitir" fica em A receber,
// junto com o Montar boleto que emite (a confirmação a cada boleto continua lá).
type AbaSicoob = 'emissao' | 'extrato'
function Sicoob() {
  const [aba, setAba] = useState<AbaSicoob>('extrato')
  return (
    <div className="space-y-4">
      <Segmentado valor={aba} onChange={setAba} rotulo="Sicoob"
                  opcoes={[['extrato', 'Extrato'], ['emissao', 'Emissão de boletos']]} />
      {aba === 'emissao' ? <ConfigEmissaoBoletos /> : <ExtratoSicoob />}
    </div>
  )
}

// O dia daqui, não o de Greenwich: à noite o toISOString já seria amanhã
const hojeLocal = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const valorOuTraco = (v: number | null) => (v == null ? '—' : formatBRL(v))
const inputCls = 'w-full min-h-11 sm:min-h-0 text-sm border border-gray-300 rounded-lg px-3 py-2'

// Dias entre hoje e o vencimento (negativo = já venceu). As duas datas são
// aaaa-mm-dd; ao meio-dia, para o horário de verão não comer um dia.
const diasAte = (venc: string, hoje: string) =>
  Math.round((Date.parse(`${venc}T12:00:00`) - Date.parse(`${hoje}T12:00:00`)) / 86_400_000)

type Aba = 'pagar' | 'receber' | 'sicoob'

// Link de Viagens (BOL-17): /boletos?aba=receber&cliente=GIRASSOL&carga=aaaa-mm-dd&placa=XXX abre o cálculo do frete
type CargaDoLink = { cliente: ClienteReceber; carga: { data: string; placa: string } | null }
function lerLink(p: URLSearchParams): CargaDoLink | null {
  if (p.get('aba') !== 'receber') return null
  const cliente: ClienteReceber = p.get('cliente')?.toUpperCase() === 'GIRASSOL' ? 'GIRASSOL' : 'CD'
  const data = p.get('carga') ?? '', placa = (p.get('placa') ?? '').trim().toUpperCase()
  const carga = cliente === 'GIRASSOL' && /^\d{4}-\d{2}-\d{2}$/.test(data) && /^[A-Z0-9-]{1,10}$/.test(placa) ? { data, placa } : null
  return { cliente, carga }
}

export default function BoletosPage() {
  const [params] = useSearchParams()
  const [link] = useState(() => lerLink(params))
  const [aba, setAba] = useState<Aba>(link ? 'receber' : 'pagar')
  const idAbas = useAbas('boletos')
  return (
    <div className="space-y-5">
      <CabecalhoTela descricao="O que a transportadora tem a pagar (postos e outros) e o que tem a receber dos clientes." />

      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Tipo de boleto"
            opcoes={[['pagar', 'A pagar', ArrowUpFromLine], ['receber', 'A receber', ArrowDownToLine], ['sicoob', 'Sicoob', Landmark]]} />

      <PainelAba id={idAbas} valor={aba}>
        {aba === 'pagar' ? <APagar /> : aba === 'receber' ? <AReceber link={link} /> : <Sicoob />}
      </PainelAba>
    </div>
  )
}

// ───────────────────────── Peças comuns ─────────────────────────

function Avisos({ avisos, onFechar }: { avisos: string[]; onFechar: () => void }) {
  if (avisos.length === 0) return null
  return <Aviso tipo="atencao" titulo="Confira" itens={avisos} onFechar={onFechar} />
}

function Filtro<T extends string>({ valor, opcoes, onChange }: { valor: T; opcoes: [T, string][]; onChange: (v: T) => void }) {
  return <Segmentado valor={valor} opcoes={opcoes} onChange={onChange} rotulo="Mostrar" />
}

const SELO = 'inline-block text-xs font-medium px-2 py-0.5 rounded-full border whitespace-nowrap tabular-nums'

function Situacao({ quitado, rotuloQuitado, vencimento, hoje }: { quitado: string | null; rotuloQuitado: string; vencimento: string | null; hoje: string }) {
  if (quitado) return <span className={`${SELO} bg-emerald-50 text-emerald-800 border-emerald-200`}>{rotuloQuitado} {dataBr(quitado)}</span>
  if (!vencimento) return <span className={`${SELO} bg-amber-50 text-amber-900 border-amber-200`}>Sem vencimento</span>
  const dias = diasAte(vencimento, hoje)
  if (dias < 0) return <span className={`${SELO} bg-red-700 text-white border-red-700`}>Vencido há {-dias} dia{dias === -1 ? '' : 's'}</span>
  if (dias === 0) return <span className={`${SELO} bg-red-50 text-red-800 border-red-300`}>Vence hoje</span>
  if (dias <= 7) return <span className={`${SELO} bg-amber-50 text-amber-900 border-amber-200`}>Vence em {dias} dia{dias === 1 ? '' : 's'}</span>
  return <span className={`${SELO} bg-gray-50 text-gray-700 border-gray-200`}>Em aberto</span>
}

// Linha de aviso do boleto (o que não veio do PDF): à vista, não escondida numa dica
function AvisosBoleto({ avisos }: { avisos: string[] }) {
  if (!avisos.length) return null
  return (
    <p className="flex items-start gap-1.5 text-sm text-amber-800 mt-1">
      <TriangleAlert size={15} className="shrink-0 mt-0.5" aria-hidden />
      <span className="min-w-0">{avisos[0]}{avisos.length > 1 && <span className="text-amber-700"> (e mais {avisos.length - 1})</span>}</span>
    </p>
  )
}

// Total do topo. Vencido e "vence logo" pedem atenção; o resto fica calmo.
function Total({ rotulo, valor, detalhe, tom = 'normal' }: {
  rotulo: string; valor: string; detalhe?: string; tom?: 'normal' | 'perigo' | 'atencao' | 'gema'
}) {
  const caixa = { normal: 'bg-white border-gray-200', perigo: 'bg-red-50 border-red-300', atencao: 'bg-amber-50 border-amber-200', gema: 'bg-gema-50 border-gema-200' }[tom]
  const texto = { normal: 'text-gray-900', perigo: 'text-red-800', atencao: 'text-amber-900', gema: 'text-gray-900' }[tom]
  const apoio = { normal: 'text-gray-500', perigo: 'text-red-800', atencao: 'text-amber-800', gema: 'text-gray-700' }[tom]
  return (
    <div className={`rounded-xl border px-3.5 py-3 ${caixa}`}>
      <p className={`text-xs font-medium ${apoio}`}>{rotulo}</p>
      <p className={`font-display text-xl sm:text-2xl font-semibold leading-tight tabular-nums mt-0.5 ${texto}`}>{valor}</p>
      {detalhe && <p className={`text-xs tabular-nums mt-0.5 ${apoio}`}>{detalhe}</p>}
    </div>
  )
}

const qtd = (n: number, um: string, varios: string) => `${n} ${n === 1 ? um : varios}`

// Data do pagamento (ou do recebimento): hoje por padrão, dá para trocar
function DataModal({ titulo, rotulo, inicial, onSalvar, onClose }: {
  titulo: string; rotulo: string; inicial: string | null; onSalvar: (d: string) => Promise<void>; onClose: () => void
}) {
  const [data, setData] = useState(inicial ?? hojeLocal())
  const [salvando, setSalvando] = useState(false)
  return (
    <Modal titulo={titulo} onClose={onClose} confirmarAoFechar>
      <Campo rotulo={rotulo}>
        <input type="date" value={data} onChange={e => setData(e.target.value)} className={inputCls} />
      </Campo>
      <BotoesModal salvando={salvando} onClose={onClose} onSalvar={async () => {
        if (!data) { toast.error(`Informe a ${rotulo.toLowerCase()}.`); return }
        setSalvando(true)
        try { await onSalvar(data); onClose() } finally { setSalvando(false) }
      }} />
    </Modal>
  )
}

// ═════════════════════════ A pagar ═════════════════════════

type FiltroPagar = 'abertos' | 'pagos' | 'todos'

function APagar() {
  const [lista, setLista] = useState<ListaAPagar | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [filtro, setFiltro] = useState<FiltroPagar>('abertos')
  const [avisos, setAvisos] = useState<string[]>([])
  const [descricao, setDescricao] = useState('')
  const [subindo, setSubindo] = useState(false)
  const [pagando, setPagando] = useState<BoletoAPagar | null>(null)
  const [agendando, setAgendando] = useState<BoletoAPagar | null>(null)
  const [editando, setEditando] = useState<BoletoAPagar | 'novo' | null>(null)
  const [falhou, setFalhou] = useState(false)
  const confirmar = useConfirmar()

  const carregar = useCallback(async () => {
    setCarregando(true)
    try { setLista((await api.listarAPagar()).data); setFalhou(false) }
    catch { setFalhou(true) }
    finally { setCarregando(false) }
  }, [])
  useEffect(() => { carregar() }, [carregar])

  const tentar = async (acao: () => Promise<unknown>, erro: string, ok?: string) => {
    try { await acao(); if (ok) toast.success(ok); await carregar() }
    catch (e) { toast.error(await erroApi(e, erro)) }
  }

  const subir = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setSubindo(true)
    try {
      const { data } = await api.importarAvulsos(arquivos, descricao)
      const rec = data.paraReceber ?? 0
      toast.success([
        data.boletos.length > 0 ? `${data.boletos.length} boleto(s) a pagar guardado(s).` : '',
        rec > 0 ? `${rec} boleto(s) da TRX foram para A receber.` : '',
      ].filter(Boolean).join(' ') || 'Nada novo para guardar.')
      setAvisos(data.avisos)
      setDescricao('')
      await carregar()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui subir os boletos.')) }
    finally { setSubindo(false) }
  }

  const marcarPago = (b: BoletoAPagar, data: string | null) =>
    b.origem === 'POSTO' ? api.pagarPosto(b.id, data) : api.pagarAvulso(b.id, data)

  const hoje = lista?.hoje ?? hojeLocal()
  const boletos = (lista?.boletos ?? []).filter(b =>
    filtro === 'todos' ? true : filtro === 'pagos' ? b.pagoEm != null : b.pagoEm == null)
  const r = lista?.resumo

  // Mesmos botões na tabela (desktop) e no cartão (celular)
  const acoes = (b: BoletoAPagar) => {
    const nome = b.beneficiario || b.descricao || 'boleto'
    return (
      <>
        {b.pagoEm ? (
          <Botao variante="fantasma" tamanho="sm" icone={RotateCcw}
                 onClick={() => tentar(() => marcarPago(b, null), 'Não consegui desfazer o pagamento.')}>Desfazer</Botao>
        ) : (
          <>
            <Botao tamanho="sm" className="whitespace-nowrap" onClick={() => setPagando(b)}>Marcar pago</Botao>
            {/* Posto: pagamento agendado no banco, marcado à mão; vira pago quando o débito cai no extrato (ABA-07) */}
            {b.origem === 'POSTO' && (b.agendadoPara ? (
              <Botao variante="fantasma" tamanho="sm" className="whitespace-nowrap"
                     onClick={() => tentar(() => api.agendarPosto(b.id, null), 'Não consegui tirar o agendado.', 'Agendado retirado.')}>
                Tirar agendado
              </Botao>
            ) : (
              <Botao variante="fantasma" tamanho="sm" icone={CalendarClock} className="whitespace-nowrap" onClick={() => setAgendando(b)}>
                Agendado para…
              </Botao>
            ))}
          </>
        )}
        {b.temPdf && (
          <BotaoIcone icone={Download} rotulo={`Baixar o PDF de ${nome}`}
                      onClick={() => (b.origem === 'POSTO' ? api.baixarPosto(b) : api.baixarAvulso(b))
                        .catch(async e => toast.error(await erroApi(e, 'Não consegui baixar o PDF.')))} />
        )}
        {b.origem === 'AVULSO' && (
          <>
            <BotaoIcone icone={Pencil} rotulo={`Editar ${nome}`} onClick={() => setEditando(b)} />
            <BotaoIcone icone={Trash2} rotulo={`Excluir ${nome}`} perigo
                        onClick={async () => await confirmar({
                          titulo: 'Excluir o boleto?',
                          texto: <><b>{b.beneficiario}</b>{b.numero ? ` · ${b.numero}` : ''}{b.valor != null ? ` · ${formatBRL(b.valor)}` : ''}{b.vencimento ? `, vence ${dataBr(b.vencimento)}` : ''}.</>,
                          confirmar: 'Excluir boleto',
                        }) && tentar(() => api.excluirAvulso(b.id), 'Não consegui excluir.', 'Boleto excluído.')} />
          </>
        )}
      </>
    )
  }
  // Posto em tinta (não em azul: azul é a cor do Bento, EMP-02)
  const origemEtiqueta = (b: BoletoAPagar) => (
    <span className={`inline-block text-xs font-medium px-1.5 py-0.5 rounded-md align-middle ${b.origem === 'POSTO' ? 'bg-tinta-50 text-tinta-800 border border-tinta-200' : 'bg-gray-100 text-gray-700 border border-gray-200'}`}>
      {b.origem === 'POSTO' ? 'Posto' : 'Avulso'}
    </span>
  )
  const notaPosto = (b: BoletoAPagar) => b.origem !== 'POSTO' ? null
    : b.pagoEm && b.peloExtrato ? <p className="text-sm font-normal text-emerald-800 mt-0.5">Identificado no extrato do Sicoob em {dataBr(b.pagoEm).slice(0, 5)}</p>
    : !b.pagoEm && b.agendadoPara ? <p className="text-sm font-normal text-tinta-800 mt-0.5">Agendado para {dataBr(b.agendadoPara).slice(0, 5)}</p>
    : null
  const vencido = (b: BoletoAPagar) => !b.pagoEm && !!b.vencimento && b.vencimento < hoje

  return (
    <div className="space-y-4">
      {/* Totais: o que já venceu e o que vence logo vêm primeiro e em destaque */}
      {r ? (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          <Total rotulo="Vencidos" valor={formatBRL(r.totalVencido)} tom={r.vencidos ? 'perigo' : 'normal'}
                 detalhe={r.vencidos ? qtd(r.vencidos, 'boleto', 'boletos') : 'Nenhum vencido'} />
          <Total rotulo="Vencem nos próximos 7 dias" valor={formatBRL(r.vencemEm7Dias)} tom={r.vencemEm7Dias ? 'atencao' : 'normal'} />
          <Total rotulo="A vencer" valor={formatBRL(r.totalAVencer)} />
          <Total rotulo="Em aberto" valor={formatBRL(r.totalAberto)} detalhe={qtd(r.abertos, 'boleto', 'boletos')} />
        </div>
      ) : !falhou && <Carregando forma="cartoes" linhas={4} rotulo="Carregando os totais" />}
      {r && r.semValor > 0 && (
        <Aviso tipo="atencao">
          {qtd(r.semValor, 'boleto em aberto está', 'boletos em aberto estão')} sem valor e fora dos totais. Preencha em Editar.
        </Aviso>
      )}

      <section aria-labelledby="pagar-subir" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
        <div>
          <h2 id="pagar-subir" className="text-base font-semibold text-gray-900">Outros boletos a pagar</h2>
          <p className="text-sm text-gray-600 max-w-[68ch]">
            Os boletos dos postos entram pela tela Abastecimentos. Aqui sobem os outros: o PDF é lido quando dá, senão
            você preenche os dados.
          </p>
        </div>
        <Campo rotulo="Descrição (opcional, vale para os PDFs desta vez)" className="max-w-md">
          <input value={descricao} onChange={e => setDescricao(e.target.value)} placeholder="Ex.: seguro, manutenção"
                 className={inputCls} />
        </Campo>
        <div className="flex flex-col sm:flex-row sm:items-start gap-3">
          <AreaSoltar compacta className="flex-1 min-w-0" aceita={['.pdf']} onArquivos={subir} icone={FileUp}
                      titulo="Arraste os PDFs dos boletos aqui" textoBotao="Escolher PDFs"
                      carregando={subindo} textoCarregando="Lendo os boletos..." />
          <Botao variante="secundario" icone={Plus} onClick={() => setEditando('novo')} className="sm:mt-1">Lançar à mão</Botao>
        </div>
      </section>

      <Avisos avisos={avisos} onFechar={() => setAvisos([])} />

      <Filtro valor={filtro} onChange={setFiltro} opcoes={[['abertos', 'Em aberto'], ['pagos', 'Pagos'], ['todos', 'Todos']]} />

      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
        {falhou && !lista ? (
          <div className="p-3"><ErroCarga mensagem="Não consegui carregar os boletos a pagar." onTentar={carregar} /></div>
        ) : carregando && !lista ? (
          <div className="p-3"><Carregando linhas={4} rotulo="Carregando os boletos a pagar" /></div>
        ) : boletos.length === 0 ? (
          <Vazio compacto icone={Inbox} titulo={filtro === 'abertos' ? 'Nenhum boleto em aberto' : filtro === 'pagos' ? 'Nenhum boleto pago' : 'Nenhum boleto guardado'}>
            {filtro === 'abertos' ? 'Tudo pago por aqui.' : 'Os boletos que subirem aparecem nesta lista.'}
          </Vazio>
        ) : (
          <>
          {/* Celular: um cartão por boleto, sem rolar para o lado */}
          <ul className="sm:hidden divide-y divide-gray-100">
            {boletos.map(b => (
              <li key={`${b.origem}-${b.id}`} className={`px-4 py-3.5 space-y-2 ${vencido(b) ? 'bg-red-50/60' : ''}`}>
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold text-gray-900 break-words">{b.beneficiario || 'Sem beneficiário'}</p>
                    {b.descricao && <p className="text-sm text-gray-600 break-words">{b.descricao}</p>}
                  </div>
                  <p className="font-display text-lg font-semibold text-gray-900 tabular-nums whitespace-nowrap leading-tight">{valorOuTraco(b.valor)}</p>
                </div>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-gray-600">
                  <Situacao quitado={b.pagoEm} rotuloQuitado="Pago em" vencimento={b.vencimento} hoje={hoje} />
                  <span className="tabular-nums">Vence {b.vencimento ? dataBr(b.vencimento) : '—'}</span>
                  {origemEtiqueta(b)}
                </div>
                <p className="text-sm text-gray-500 tabular-nums">
                  {b.numero && <>Número {b.numero} · </>}Chegou {dataBr(b.dataChegada)}
                </p>
                {notaPosto(b)}
                <AvisosBoleto avisos={b.avisos} />
                <div className="flex flex-wrap items-center gap-1 -ml-1 pt-0.5">{acoes(b)}</div>
              </li>
            ))}
          </ul>
          <div className="hidden sm:block overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Vencimento</th>
                  <th className="px-3 py-2 text-left font-medium">Situação</th>
                  <th className="px-3 py-2 text-left font-medium">Beneficiário</th>
                  <th className="px-3 py-2 text-left font-medium">Número</th>
                  <th className="px-3 py-2 text-right font-medium">Valor</th>
                  <th className="px-3 py-2 text-left font-medium">Chegada</th>
                  <th className="px-3 py-2 text-right font-medium"><span className="sr-only">Ações</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {boletos.map(b => (
                  <tr key={`${b.origem}-${b.id}`} className={`align-top transition-colors ${vencido(b) ? 'bg-red-50/60' : 'hover:bg-gray-50'}`}>
                    <td className={`px-3 py-2.5 whitespace-nowrap tabular-nums ${vencido(b) ? 'text-red-800 font-semibold' : 'text-gray-900'}`}>{b.vencimento ? dataBr(b.vencimento) : '—'}</td>
                    <td className="px-3 py-2.5"><Situacao quitado={b.pagoEm} rotuloQuitado="Pago em" vencimento={b.vencimento} hoje={hoje} /></td>
                    <td className="px-3 py-2.5 font-medium text-gray-900">
                      <span className="mr-1.5">{b.beneficiario || '—'}</span>{origemEtiqueta(b)}
                      {b.descricao && <p className="text-sm font-normal text-gray-600 mt-0.5">{b.descricao}</p>}
                      {notaPosto(b)}
                      <AvisosBoleto avisos={b.avisos} />
                    </td>
                    <td className="px-3 py-2.5 text-gray-600 tabular-nums">{b.numero || '—'}</td>
                    <td className="px-3 py-2.5 text-right font-semibold tabular-nums text-gray-900">{valorOuTraco(b.valor)}</td>
                    <td className="px-3 py-2.5 whitespace-nowrap tabular-nums text-gray-600">{dataBr(b.dataChegada)}</td>
                    <td className="px-2 py-1.5">
                      <div className="flex gap-0.5 justify-end items-center">{acoes(b)}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          </>
        )}
      </div>
      <p className="text-sm text-gray-500">Os dados dos boletos de posto (valor, vencimento, número) se corrigem na tela Abastecimentos.</p>

      {pagando && (
        <DataModal titulo={`Pagamento: ${pagando.beneficiario || pagando.descricao}`} rotulo="Data do pagamento" inicial={null}
                   onClose={() => setPagando(null)}
                   onSalvar={d => tentar(() => marcarPago(pagando, d), 'Não consegui marcar o pagamento.', 'Pagamento marcado.')} />
      )}
      {agendando && (
        <DataModal titulo={`Agendado: ${agendando.beneficiario || agendando.descricao}`} rotulo="Data do agendamento no banco"
                   inicial={agendando.vencimento && agendando.vencimento >= hoje ? agendando.vencimento : null}
                   onClose={() => setAgendando(null)}
                   onSalvar={d => tentar(() => api.agendarPosto(agendando.id, d), 'Não consegui marcar o agendado.', 'Agendado marcado.')} />
      )}
      {editando && (
        <AvulsoModal boleto={editando === 'novo' ? null : editando} onClose={() => setEditando(null)}
                     onSalvo={() => { setEditando(null); carregar() }} />
      )}
    </div>
  )
}

function AvulsoModal({ boleto, onClose, onSalvo }: { boleto: BoletoAPagar | null; onClose: () => void; onSalvo: () => void }) {
  const [f, setF] = useState({
    descricao: boleto?.descricao ?? '', beneficiario: boleto?.beneficiario ?? '', numero: boleto?.numero ?? '',
    valor: boleto?.valor != null ? num(boleto.valor) : '', vencimento: boleto?.vencimento ?? '',
    dataChegada: boleto?.dataChegada ?? hojeLocal(), pagoEm: boleto?.pagoEm ?? '',
  })
  const [salvando, setSalvando] = useState(false)
  const campo = (k: keyof typeof f) => ({ value: f[k], onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF(p => ({ ...p, [k]: e.target.value })) })

  const salvar = async () => {
    if (!f.descricao.trim() && !f.beneficiario.trim()) { toast.error('Informe a descrição ou o beneficiário.'); return }
    if (f.valor.trim() && lerNum(f.valor) < 0) { toast.error('O valor não pode ser negativo.'); return }
    const d: AvulsoEdicao = {
      descricao: f.descricao, beneficiario: f.beneficiario, numero: f.numero,
      valor: f.valor.trim() ? lerNum(f.valor) : null, vencimento: f.vencimento || null,
      dataChegada: f.dataChegada || null, pagoEm: f.pagoEm || null,
    }
    setSalvando(true)
    try {
      if (boleto) await api.editarAvulso(boleto.id, d)
      else await api.criarAvulso(d)
      toast.success('Boleto guardado.')
      onSalvo()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui guardar o boleto.')) }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo={boleto ? 'Corrigir boleto a pagar' : 'Novo boleto a pagar'} onClose={onClose} confirmarAoFechar>
      {boleto && boleto.avisos.length > 0 && <Aviso tipo="atencao" itens={boleto.avisos} />}
      <Campo rotulo="Descrição"><input {...campo('descricao')} className={inputCls} /></Campo>
      <Campo rotulo="Beneficiário"><input {...campo('beneficiario')} className={inputCls} /></Campo>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Número"><input {...campo('numero')} className={inputCls} /></Campo>
        <Campo rotulo="Valor (R$)"><input {...campo('valor')} placeholder="0,00" inputMode="decimal" className={inputCls} /></Campo>
        <Campo rotulo="Vencimento"><input type="date" {...campo('vencimento')} className={inputCls} /></Campo>
        <Campo rotulo="Data de chegada"><input type="date" {...campo('dataChegada')} className={inputCls} /></Campo>
        <Campo rotulo="Pago em (vazio se em aberto)"><input type="date" {...campo('pagoEm')} className={inputCls} /></Campo>
      </div>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

// ═════════════════════════ A receber ═════════════════════════

type FiltroReceber = 'abertos' | 'recebidos' | 'todos'

function AReceber({ link }: { link?: CargaDoLink | null }) {
  const [lista, setLista] = useState<ListaAReceber | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [cliente, setCliente] = useState<ClienteReceber>(link?.cliente ?? 'CD')
  // Perfil: cálculo do frete e queda do volume da carga (BOL-17)
  const [calculando, setCalculando] = useState<{ data: string; placa: string } | null>(link?.carga ?? null)
  // BRZ (produtosC) é outro cliente, separado da CD (Cedro Frutas): não tem boleto, só as produtosC a receber
  const [verProdutosC, setVerProdutosC] = useState(false)
  const [filtro, setFiltro] = useState<FiltroReceber>('todos')
  const [avisos, setAvisos] = useState<string[]>([])
  const [subindo, setSubindo] = useState(false)
  const [recebendo, setRecebendo] = useState<BoletoReceber | null>(null)
  const [editando, setEditando] = useState<BoletoReceber | 'novo' | null>(null)
  const [antigosAbertos, setAntigosAbertos] = useState(false)
  const [montando, setMontando] = useState<{ data: string; placa: string | null } | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [versaoParaEmitir, setVersaoParaEmitir] = useState(0)
  const confirmar = useConfirmar()
  const dono = useAuth().usuario?.dono === true

  const carregar = useCallback(async () => {
    setCarregando(true)
    try { setLista((await api.listarAReceber()).data); setFalhou(false) }
    catch { setFalhou(true) }
    finally { setCarregando(false) }
  }, [])
  useEffect(() => { carregar() }, [carregar])

  const tentar = async (acao: () => Promise<unknown>, erro: string, ok?: string) => {
    try { await acao(); if (ok) toast.success(ok); await carregar() }
    catch (e) { toast.error(await erroApi(e, erro)) }
  }

  const subir = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setSubindo(true)
    try {
      const { data } = await api.importarAReceber(arquivos, cliente)
      let avisosFinais = data.avisos
      let substituidos = 0, repetidos = data.repetidos
      // Boleto que já existe: pergunta se troca pelo subido agora ou deixa como está
      const jaExistem = (data.itens ?? []).filter(i => i.situacao === 'REPETIDO')
      if (jaExistem.length > 0) {
        const nomes = new Set(jaExistem.map(i => i.arquivo))
        const trocar = await confirmar({
          titulo: jaExistem.length === 1 ? 'Esse boleto já existe' : `${jaExistem.length} boletos já existem`,
          texto: <>
            <ComparaBoletos itens={jaExistem} />
            <p className="mt-3">Substituir troca o boleto anterior pelo que você subiu agora. Cancelar deixa o anterior como está.</p>
          </>,
          confirmar: 'Substituir',
          cancelar: 'Cancelar',
          perigo: false,
        })
        if (trocar) {
          const { data: d2 } = await api.importarAReceber(arquivos.filter(f => nomes.has(f.name)), cliente, true)
          const avisosDosRepetidos = new Set(jaExistem.flatMap(i => i.avisos))
          avisosFinais = [...data.avisos.filter(a => !avisosDosRepetidos.has(a)), ...d2.avisos]
          substituidos = d2.substituidos ?? 0
          repetidos = d2.repetidos
        }
      }
      const partes = [`${data.novos} novo(s)`]
      if (data.completados) partes.push(`${data.completados} completado(s) com o PDF`)
      if (substituidos) partes.push(`${substituidos} substituído(s)`)
      if (repetidos) partes.push(`${repetidos} já estava(m) guardado(s)`)
      toast.success(partes.join(', ') + '.')
      setAvisos(avisosFinais)
      await carregar()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui subir os boletos.')) }
    finally { setSubindo(false) }
  }

  const hoje = lista?.hoje ?? hojeLocal()
  const resumo = lista?.clientes.find(c => c.chave === cliente)
  const boletos = (lista?.boletos ?? []).filter(b => b.cliente === cliente).filter(b =>
    filtro === 'todos' ? true : filtro === 'recebidos' ? b.recebidoEm != null : b.recebidoEm == null)

  // Mesmos botões na tabela (desktop) e no cartão (celular)
  const acoes = (b: BoletoReceber) => {
    const nome = `o boleto ${b.nossoNumero || b.numero || b.nomeArquivo || ''}`.trim()
    return (
      <>
        {b.recebidoEm ? (
          <Botao variante="fantasma" tamanho="sm" icone={RotateCcw}
                 onClick={() => tentar(() => api.marcarRecebido(b.id, null), 'Não consegui desfazer.')}>Desfazer</Botao>
        ) : (
          <Botao tamanho="sm" className="whitespace-nowrap" onClick={() => setRecebendo(b)}>Marcar recebido</Botao>
        )}
        {b.temPdf && (
          <BotaoIcone icone={Download} rotulo={`Baixar o PDF d${nome}`}
                      onClick={() => api.baixarAReceber(b).catch(async e => toast.error(await erroApi(e, 'Não consegui baixar o PDF.')))} />
        )}
        {b.cliente === 'GIRASSOL' && b.dataCarga && b.placa && (
          <BotaoIcone icone={Calculator} rotulo={`Cálculo do frete da carga de ${dataBr(b.dataCarga)}`}
                      onClick={() => setCalculando({ data: b.dataCarga!, placa: b.placa! })} />
        )}
        {b.dataCarga && (
          <BotaoIcone icone={ClipboardCheck} rotulo={`Montar e comparar com ${nome}`}
                      onClick={() => setMontando({ data: b.dataCarga!, placa: b.placa || null })} />
        )}
        <BotaoIcone icone={Pencil} rotulo={`Editar ${nome}`} onClick={() => setEditando(b)} />
        {/* Boleto emitido errado: baixa no Sicoob e apaga do sistema (ação real no banco, só o dono) */}
        {dono && !b.recebidoEm && b.nossoNumero && (
          <BotaoIcone icone={Ban} rotulo="Dar baixa no boleto" perigo
                      onClick={async () => await confirmar({
                        titulo: 'Dar baixa no boleto?',
                        texto: <>
                          <p><b>{b.nossoNumero}</b> · {b.valor != null ? formatBRL(b.valor) : 'valor sem informar'} · vence {b.vencimento ? dataBr(b.vencimento) : 'data sem informar'}</p>
                          <p className="mt-2">Dá baixa no Sicoob e apaga do sistema; não dá para desfazer.</p>
                        </>,
                        confirmar: 'Dar baixa',
                      }) && tentar(async () => { const { data } = await sicoobApi.baixarEExcluir(b.id); toast.success(data.mensagem) },
                                   'O Sicoob não baixou o boleto.')} />
        )}
        <BotaoIcone icone={Trash2} rotulo={`Excluir ${nome}`} perigo
                    onClick={async () => await confirmar({
                      titulo: 'Excluir o boleto?',
                      texto: <><b>{b.nossoNumero || b.nomeArquivo}</b>{b.valor != null ? ` · ${formatBRL(b.valor)}` : ''}, carga de {b.dataCarga ? dataBr(b.dataCarga) : 'data sem informar'}.</>,
                      confirmar: 'Excluir boleto',
                    }) && tentar(() => api.excluirAReceber(b.id), 'Não consegui excluir.', 'Boleto excluído.')} />
      </>
    )
  }
  const vencido = (b: BoletoReceber) => !b.recebidoEm && !!b.vencimento && b.vencimento < hoje
  const opcoesCliente = (lista?.clientes ?? [{ chave: 'CD' as const, rotulo: 'CD (Cedro Frutas)' }, { chave: 'GIRASSOL' as const, rotulo: 'Girassol (Horacio)' }])
    .map(c => [c.chave, c.rotulo] as const)
  const opcoesTela = [...opcoesCliente, ['BRZ', 'BRZ (produtosC)'] as const]
  const escolherCliente = (v: string) => {
    if (v === 'BRZ') { setVerProdutosC(true); return }
    setVerProdutosC(false); setCliente(v as ClienteReceber)
  }

  return (
    <div className="space-y-4">
      {/* Subir boleto emitido fora do sistema é exceção (o normal é emitir em "Para emitir"):
          fica pequeno no canto de cima, à direita, e a lista sobe (BOL-17) */}
      <div className="flex flex-col gap-3 xl:flex-row xl:items-start xl:justify-between">
        <Segmentado valor={verProdutosC ? 'BRZ' : cliente} onChange={escolherCliente} rotulo="Cliente" opcoes={opcoesTela} />
        {!verProdutosC && (
          <div role="group" aria-label={`Subir, montar ou lançar boleto de ${resumo?.rotulo ?? cliente}`} className="min-w-0">
            <div className="flex flex-wrap gap-2 xl:justify-end">
              <AreaSoltar discreta className="flex-1 sm:flex-none" aceita={['.pdf']} onArquivos={subir} icone={FileUp}
                          titulo={`Arraste aqui ou escolha os boletos de ${resumo?.rotulo ?? cliente} emitidos fora do sistema`}
                          textoBotao="Subir boletos" carregando={subindo} textoCarregando="Lendo os boletos..." />
              <Botao variante="secundario" icone={ClipboardCheck} className="flex-1 sm:flex-none whitespace-nowrap"
                     onClick={() => setMontando({ data: hoje, placa: null })}>Montar boleto</Botao>
              <Botao variante="secundario" icone={Plus} className="flex-1 sm:flex-none whitespace-nowrap"
                     onClick={() => setEditando('novo')}>Lançar à mão</Botao>
            </div>
            <p className="text-xs text-gray-500 mt-1.5 xl:text-right">
              Subir é para boleto emitido fora do sistema, em PDF. Vale a data do boleto, não a do nome do arquivo.
            </p>
          </div>
        )}
      </div>

      {verProdutosC ? <ProdutosCAReceber modo="brz" /> : <>

      {resumo ? (
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
          <Total rotulo="Vencidos" valor={formatBRL(resumo.totalVencido)} tom={resumo.vencidos ? 'perigo' : 'normal'}
                 detalhe={resumo.vencidos ? qtd(resumo.vencidos, 'boleto', 'boletos') : 'Nenhum vencido'} />
          <Total rotulo="A receber" valor={formatBRL(resumo.totalAReceber)} tom="gema" detalhe={qtd(resumo.abertos, 'boleto em aberto', 'boletos em aberto')} />
          <Total rotulo="Recebido" valor={formatBRL(resumo.totalRecebido)} />
          <Total rotulo="Emitido" valor={formatBRL(resumo.totalEmitido)} detalhe={qtd(resumo.quantidade, 'boleto', 'boletos')} />
        </div>
      ) : !lista && !falhou && <Carregando forma="cartoes" linhas={4} rotulo="Carregando os totais" />}

      <ParaEmitirBoletos cliente={cliente} rotuloCliente={resumo?.rotulo ?? cliente} versao={versaoParaEmitir}
                         onEmitir={(data, placa) => setMontando({ data, placa })}
                         onCalculo={(data, placa) => setCalculando({ data, placa })} />

      <Avisos avisos={avisos} onFechar={() => setAvisos([])} />

      <Filtro valor={filtro} onChange={setFiltro} opcoes={[['todos', 'Todos'], ['abertos', 'Em aberto'], ['recebidos', 'Recebidos']]} />

      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
        {falhou && !lista ? (
          <div className="p-3"><ErroCarga mensagem="Não consegui carregar os boletos a receber." onTentar={carregar} /></div>
        ) : carregando && !lista ? (
          <div className="p-3"><Carregando linhas={4} rotulo="Carregando os boletos a receber" /></div>
        ) : boletos.length === 0 ? (
          <Vazio compacto icone={Inbox} titulo="Nenhum boleto nesta lista">
            {filtro === 'abertos' ? 'Nada em aberto para este cliente.' : 'Os boletos que subirem para este cliente aparecem aqui.'}
          </Vazio>
        ) : (
          <>
          {/* Celular: um cartão por boleto */}
          <ul className="sm:hidden divide-y divide-gray-100">
            {boletos.map(b => (
              <li key={b.id} className={`px-4 py-3.5 space-y-2 ${vencido(b) ? 'bg-red-50/60' : ''}`}>
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold text-gray-900 tabular-nums">Carga de {b.dataCarga ? dataBr(b.dataCarga) : '—'}</p>
                    <p className="text-sm text-gray-600 tabular-nums">
                      {b.placa || 'Sem placa'} · Boleto {b.nossoNumero || b.numero || '—'}
                    </p>
                  </div>
                  <p className="font-display text-lg font-semibold text-gray-900 tabular-nums whitespace-nowrap leading-tight">{valorOuTraco(b.valor)}</p>
                </div>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-gray-600">
                  <Situacao quitado={b.recebidoEm} rotuloQuitado="Recebido em" vencimento={b.vencimento} hoje={hoje} />
                  <span className="tabular-nums">Vence {b.vencimento ? dataBr(b.vencimento) : '—'}</span>
                </div>
                <AvisosBoleto avisos={b.avisos} />
                <div className="flex items-center gap-1 -ml-1 pt-0.5">{acoes(b)}</div>
              </li>
            ))}
          </ul>
          <div className="hidden sm:block overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-3 py-2 text-left font-medium">Data da carga</th>
                  <th className="px-3 py-2 text-left font-medium">Caminhão</th>
                  <th className="px-3 py-2 text-left font-medium">Número do boleto</th>
                  <th className="px-3 py-2 text-right font-medium">Valor</th>
                  <th className="px-3 py-2 text-left font-medium">Vencimento</th>
                  <th className="px-3 py-2 text-left font-medium">Situação</th>
                  <th className="px-3 py-2 text-left font-medium">Arquivo</th>
                  <th className="px-3 py-2 text-right font-medium"><span className="sr-only">Ações</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {boletos.map(b => (
                  <tr key={b.id} className={`align-top transition-colors ${vencido(b) ? 'bg-red-50/60' : 'hover:bg-gray-50'}`}>
                    <td className="px-3 py-2.5 whitespace-nowrap font-medium text-gray-900 tabular-nums">{b.dataCarga ? dataBr(b.dataCarga) : '—'}</td>
                    <td className="px-3 py-2.5 whitespace-nowrap tabular-nums">{b.placa || '—'}</td>
                    <td className="px-3 py-2.5 whitespace-nowrap tabular-nums">{b.nossoNumero || b.numero || '—'}</td>
                    <td className="px-3 py-2.5 text-right font-semibold tabular-nums text-gray-900">{valorOuTraco(b.valor)}</td>
                    <td className={`px-3 py-2.5 whitespace-nowrap tabular-nums ${vencido(b) ? 'text-red-800 font-semibold' : ''}`}>{b.vencimento ? dataBr(b.vencimento) : '—'}</td>
                    <td className="px-3 py-2.5"><Situacao quitado={b.recebidoEm} rotuloQuitado="Recebido em" vencimento={b.vencimento} hoje={hoje} /></td>
                    <td className="px-3 py-2.5 text-sm text-gray-500 max-w-[220px]">
                      <span className="break-all">{b.nomeArquivo || (b.temPdf ? 'PDF' : 'Sem PDF')}</span>
                      <AvisosBoleto avisos={b.avisos} />
                    </td>
                    <td className="px-2 py-1.5">
                      <div className="flex gap-0.5 justify-end items-center">{acoes(b)}</div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          </>
        )}
      </div>

      {/* A lista antiga (boletos digitados por cliente do cadastro) continua
          aqui até ser trocada de vez pela de cima */}

      <div className="bg-white border border-gray-200 rounded-xl">
        <button onClick={() => setAntigosAbertos(a => !a)} aria-expanded={antigosAbertos}
                className="w-full flex items-center gap-2 px-4 min-h-12 text-left text-sm font-semibold text-gray-800 rounded-xl hover:bg-gray-50">
          <ChevronRight size={16} aria-hidden className={`shrink-0 text-gray-500 transition-transform duration-200 ${antigosAbertos ? 'rotate-90' : ''}`} />
          Outros boletos lançados à mão (lista antiga, por cliente do cadastro)
        </button>
        {antigosAbertos && <div className="px-4 pb-4"><BoletosAntigos /></div>}
      </div>
      </>}

      {recebendo && (
        <DataModal titulo={`Recebimento do boleto ${recebendo.nossoNumero || ''}`.trim()} rotulo="Data do recebimento" inicial={null}
                   onClose={() => setRecebendo(null)}
                   onSalvar={d => tentar(() => api.marcarRecebido(recebendo.id, d), 'Não consegui marcar o recebimento.', 'Recebimento marcado.')} />
      )}
      {montando && (
        <MontarBoleto cliente={cliente} rotuloCliente={resumo?.rotulo ?? cliente} dataInicial={montando.data}
                      placaInicial={montando.placa} onClose={() => setMontando(null)}
                      onEmitido={() => { carregar(); setVersaoParaEmitir(v => v + 1) }} />
      )}
      {calculando && (
        <CalculoFretePerfil data={calculando.data} placa={calculando.placa} onClose={() => setCalculando(null)}
                            onSalvo={() => { carregar(); setVersaoParaEmitir(v => v + 1) }} />
      )}
      {editando && (
        <ReceberModal boleto={editando === 'novo' ? null : editando} cliente={cliente} onClose={() => setEditando(null)}
                      onSalvo={() => { setEditando(null); carregar() }} />
      )}
    </div>
  )
}

function ReceberModal({ boleto, cliente, onClose, onSalvo }: {
  boleto: BoletoReceber | null; cliente: ClienteReceber; onClose: () => void; onSalvo: () => void
}) {
  const [f, setF] = useState({
    cliente: boleto?.cliente ?? cliente, sequencia: boleto?.sequencia != null ? String(boleto.sequencia) : '',
    numero: boleto?.numero ?? '', nossoNumero: boleto?.nossoNumero ?? '', pagador: boleto?.pagador ?? '',
    placa: boleto?.placa ?? '', valor: boleto?.valor != null ? num(boleto.valor) : '',
    vencimento: boleto?.vencimento ?? '', emissao: boleto?.emissao ?? '', dataCarga: boleto?.dataCarga ?? '',
    recebidoEm: boleto?.recebidoEm ?? '', observacao: boleto?.observacao ?? '',
  })
  const [salvando, setSalvando] = useState(false)
  const campo = (k: keyof typeof f) => ({ value: f[k], onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF(p => ({ ...p, [k]: e.target.value })) })

  const salvar = async () => {
    if (!f.dataCarga) { toast.error('Informe a data da carga.'); return }
    if (f.valor.trim() && lerNum(f.valor) < 0) { toast.error('O valor não pode ser negativo.'); return }
    const seq = f.sequencia.trim() ? parseInt(f.sequencia, 10) : null
    if (f.sequencia.trim() && (seq == null || isNaN(seq))) { toast.error('A sequência precisa ser um número.'); return }
    const d: ReceberEdicao = {
      cliente: f.cliente as ClienteReceber, sequencia: seq, numero: f.numero, nossoNumero: f.nossoNumero,
      pagador: f.pagador, placa: f.placa, valor: f.valor.trim() ? lerNum(f.valor) : null,
      vencimento: f.vencimento || null, emissao: f.emissao || null, dataCarga: f.dataCarga || null,
      recebidoEm: f.recebidoEm || null, observacao: f.observacao,
    }
    setSalvando(true)
    try {
      if (boleto) await api.editarAReceber(boleto.id, d)
      else await api.criarAReceber(d)
      toast.success('Boleto guardado.')
      onSalvo()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui guardar o boleto.')) }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo={boleto ? 'Corrigir boleto a receber' : 'Novo boleto a receber'} onClose={onClose} confirmarAoFechar>
      {boleto && boleto.avisos.length > 0 && <Aviso tipo="atencao" itens={boleto.avisos} />}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Cliente">
          <select value={f.cliente} onChange={e => setF(p => ({ ...p, cliente: e.target.value as ClienteReceber }))} className={inputCls}>
            <option value="CD">CD (Cedro Frutas)</option>
            <option value="GIRASSOL">Girassol (Horacio)</option>
          </select>
        </Campo>
        <Campo rotulo="Data da carga"><input type="date" {...campo('dataCarga')} className={inputCls} /></Campo>
        <Campo rotulo="Caminhão (placa)"><input {...campo('placa')} placeholder="XAC-6D57" className={inputCls} /></Campo>
        <Campo rotulo="Número do boleto (nosso número)"><input {...campo('nossoNumero')} placeholder="668-8" className={inputCls} /></Campo>
        <Campo rotulo="Valor (R$)"><input {...campo('valor')} placeholder="0,00" inputMode="decimal" className={inputCls} /></Campo>
        <Campo rotulo="Vencimento"><input type="date" {...campo('vencimento')} className={inputCls} /></Campo>
        <Campo rotulo="Número do documento"><input {...campo('numero')} className={inputCls} /></Campo>
        <Campo rotulo="Sequência do arquivo"><input {...campo('sequencia')} inputMode="numeric" className={inputCls} /></Campo>
        <Campo rotulo="Emissão"><input type="date" {...campo('emissao')} className={inputCls} /></Campo>
        <Campo rotulo="Recebido em (vazio se em aberto)"><input type="date" {...campo('recebidoEm')} className={inputCls} /></Campo>
      </div>
      <Campo rotulo="Pagador"><input {...campo('pagador')} className={inputCls} /></Campo>
      <Campo rotulo="Observação"><input {...campo('observacao')} className={inputCls} /></Campo>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

// ───────────────────────── Lista antiga ─────────────────────────
// É a tela Boletos de antes (tabela Boletos, por cliente do cadastro), sem
// mudança de comportamento: filtro por mês, criar, pagar e excluir.

function StatusBadge({ status, dias }: { status: string; dias: number }) {
  if (status === 'Pago')
    return <span className={`${SELO} bg-emerald-50 text-emerald-800 border-emerald-200`}>Pago</span>
  if (status === 'Vencido' || (status === 'Pendente' && dias < 0))
    return <span className={`${SELO} bg-red-700 text-white border-red-700`}>Vencido há {Math.abs(dias)} dia{Math.abs(dias) === 1 ? '' : 's'}</span>
  if (status === 'Pendente' && dias <= 3)
    return <span className={`${SELO} bg-amber-50 text-amber-900 border-amber-200`}>{dias === 0 ? 'Vence hoje' : `Vence em ${dias} dia${dias === 1 ? '' : 's'}`}</span>
  return <span className={`${SELO} bg-gray-50 text-gray-700 border-gray-200`}>Pendente</span>
}

function BoletosAntigos() {
  const agora = new Date()
  const [mes, setMes] = useState(agora.getMonth() + 1)
  const [ano, setAno] = useState(agora.getFullYear())
  const [filtroStatus, setFiltroStatus] = useState('')
  const [boletos, setBoletos] = useState<Boleto[]>([])
  const [clientes, setClientes] = useState<Cliente[]>([])
  const [loading, setLoading] = useState(false)
  const [falhou, setFalhou] = useState(false)
  const [showForm, setShowForm] = useState(false)
  const [salvando, setSalvando] = useState(false)
  const confirmar = useConfirmar()

  const [form, setForm] = useState({
    clienteId: '', valor: '', dataEmissao: hojeLocal(),
    dataVencimento: '', descricao: '', tipoProduto: 'Frete',
  })

  const carregar = useCallback(async () => {
    setLoading(true)
    try {
      const [{ data: b }, { data: c }] = await Promise.all([
        boletosApi.listar({ mes, ano, status: filtroStatus || undefined }),
        clientesApi.listar({ ativo: true }),
      ])
      setBoletos(b)
      setClientes(c)
      setFalhou(false)
    } catch { setFalhou(true) }
    finally { setLoading(false) }
  }, [mes, ano, filtroStatus])

  useEffect(() => { carregar() }, [carregar])

  const pagar = async (id: number) => {
    try { await boletosApi.pagar(id); toast.success('Boleto marcado como pago.') }
    catch { toast.error('Não consegui marcar o pagamento.') }
    await carregar()
  }

  const excluir = async (id: number) => {
    const b = boletos.find(x => x.id === id)
    if (!await confirmar({
      titulo: 'Excluir o boleto?',
      texto: b ? <><b>{b.cliente?.nome ?? 'Sem cliente'}</b> · {formatBRL(b.valor)}, vence {dataBr(b.dataVencimento.slice(0, 10))}.</> : undefined,
      confirmar: 'Excluir boleto',
    })) return
    try { await boletosApi.excluir(id); toast.success('Boleto excluído.') }
    catch { toast.error('Não consegui excluir o boleto.') }
    await carregar()
  }

  const criar = async () => {
    if (!form.clienteId || !form.valor || !form.dataVencimento) {
      toast.error('Preencha cliente, valor e vencimento.'); return
    }
    setSalvando(true)
    try {
      await boletosApi.criar({
        clienteId: Number(form.clienteId),
        valor: lerNum(form.valor),
        dataEmissao: form.dataEmissao,
        dataVencimento: form.dataVencimento,
        descricao: form.descricao || null,
        tipoProduto: form.tipoProduto,
      })
      toast.success('Boleto criado.')
      setShowForm(false)
      setForm({ clienteId: '', valor: '', dataEmissao: hojeLocal(), dataVencimento: '', descricao: '', tipoProduto: 'Frete' })
      await carregar()
    } catch { toast.error('Erro ao criar boleto.') }
    finally { setSalvando(false) }
  }

  const totalPendente = boletos.filter(b => b.status !== 'Pago').reduce((s, b) => s + b.valor, 0)
  const totalPago = boletos.filter(b => b.status === 'Pago').reduce((s, b) => s + b.valor, 0)

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2 sm:gap-3 items-center">
        <select value={mes} onChange={e => setMes(Number(e.target.value))} aria-label="Mês" className="text-sm border border-gray-300 rounded-lg px-3 py-2">
          {Array.from({ length: 12 }, (_, i) => (
            <option key={i + 1} value={i + 1}>{new Date(2024, i).toLocaleString('pt-BR', { month: 'long' })}</option>
          ))}
        </select>
        <input type="number" value={ano} onChange={e => setAno(Number(e.target.value))} aria-label="Ano"
               className="text-sm border border-gray-300 rounded-lg px-3 py-2 w-24" />
        <select value={filtroStatus} onChange={e => setFiltroStatus(e.target.value)} aria-label="Situação" className="text-sm border border-gray-300 rounded-lg px-3 py-2">
          <option value="">Todos</option>
          <option value="Pendente">Pendente</option>
          <option value="Pago">Pago</option>
          <option value="Vencido">Vencido</option>
        </select>
        <span className="text-sm text-gray-600 tabular-nums">Pendente: <b className="text-gray-900">{formatBRL(totalPendente)}</b> · Pago no mês: <b className="text-emerald-800">{formatBRL(totalPago)}</b></span>
        <div className="flex-1" />
        <Botao variante="secundario" icone={Plus} onClick={() => setShowForm(true)}>Novo boleto</Botao>
      </div>

      {showForm && (
        <div className="border border-gray-200 rounded-xl p-4 space-y-3">
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <Campo rotulo="Cliente">
              <select value={form.clienteId} onChange={e => setForm(p => ({ ...p, clienteId: e.target.value }))} className={inputCls}>
                <option value="">Selecionar...</option>
                {clientes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
              </select>
            </Campo>
            <Campo rotulo="Tipo">
              <select value={form.tipoProduto} onChange={e => setForm(p => ({ ...p, tipoProduto: e.target.value }))} className={inputCls}>
                <option>Frete</option>
                <option>VolumesProdutoCDuzia</option>
                <option>VolumesProdutoC30</option>
                <option>ProdutosD</option>
                <option>ProdutosA</option>
                <option>ProdutosBAvulso</option>
              </select>
            </Campo>
            <Campo rotulo="Valor (R$)">
              <input value={form.valor} onChange={e => setForm(p => ({ ...p, valor: e.target.value }))} placeholder="0,00" className={inputCls} />
            </Campo>
            <Campo rotulo="Emissão">
              <input type="date" value={form.dataEmissao} onChange={e => setForm(p => ({ ...p, dataEmissao: e.target.value }))} className={inputCls} />
            </Campo>
            <Campo rotulo="Vencimento">
              <input type="date" value={form.dataVencimento} onChange={e => setForm(p => ({ ...p, dataVencimento: e.target.value }))} className={inputCls} />
            </Campo>
            <Campo rotulo="Descrição">
              <input value={form.descricao} onChange={e => setForm(p => ({ ...p, descricao: e.target.value }))} placeholder="Opcional" className={inputCls} />
            </Campo>
          </div>
          <div className="flex gap-3">
            <Botao variante="primario" onClick={criar} carregando={salvando} textoCarregando="Salvando...">Criar boleto</Botao>
            <Botao variante="fantasma" onClick={() => setShowForm(false)}>Cancelar</Botao>
          </div>
        </div>
      )}

      <div className="border border-gray-200 rounded-xl overflow-hidden">
        {falhou ? (
          <div className="p-3"><ErroCarga mensagem="Não consegui carregar a lista antiga." onTentar={carregar} /></div>
        ) : loading ? (
          <div className="p-3"><Carregando linhas={3} rotulo="Carregando os boletos" /></div>
        ) : boletos.length === 0 ? (
          <Vazio compacto titulo="Nenhum boleto no período" />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-3 py-2 text-left">Cliente</th>
                  <th className="px-3 py-2 text-left">Tipo</th>
                  <th className="px-3 py-2 text-left">Descrição</th>
                  <th className="px-3 py-2 text-right">Valor</th>
                  <th className="px-3 py-2 text-left">Emissão</th>
                  <th className="px-3 py-2 text-left">Vencimento</th>
                  <th className="px-3 py-2 text-center">Situação</th>
                  <th className="px-3 py-2 text-center"><span className="sr-only">Ações</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {boletos.map(b => (
                  <tr key={b.id}>
                    <td className="px-3 py-2 font-medium text-gray-800">{b.cliente?.nome ?? '—'}</td>
                    <td className="px-3 py-2 text-gray-500">{b.tipoProduto ?? '—'}</td>
                    <td className="px-3 py-2 text-gray-600 max-w-[200px] truncate">{b.descricao ?? '—'}</td>
                    <td className="px-3 py-2 text-right font-semibold tabular-nums">{formatBRL(b.valor)}</td>
                    <td className="px-3 py-2 text-gray-500 tabular-nums">{new Date(b.dataEmissao).toLocaleDateString('pt-BR')}</td>
                    <td className="px-3 py-2 text-gray-700 tabular-nums">{new Date(b.dataVencimento).toLocaleDateString('pt-BR')}</td>
                    <td className="px-3 py-2 text-center"><StatusBadge status={b.status} dias={b.diasVencimento} /></td>
                    <td className="px-3 py-2">
                      <div className="flex gap-1 justify-center items-center">
                        {b.status !== 'Pago' && (
                          <Botao tamanho="sm" onClick={() => pagar(b.id)}>Pagar</Botao>
                        )}
                        <BotaoIcone icone={Trash2} perigo rotulo={`Excluir o boleto de ${b.cliente?.nome ?? 'cliente sem nome'}`} onClick={() => excluir(b.id)} />
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
