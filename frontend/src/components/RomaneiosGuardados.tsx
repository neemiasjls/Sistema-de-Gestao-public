import { useEffect, useMemo, useState } from 'react'
import Aviso from './ui/Aviso'
import { Carregando, ErroCarga } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import Botao from './ui/Botao'
import { Modal } from './ModalBase'
import { AlertTriangle, Download, Eye, FileText, FileUp, Loader2, Search, Trash2, Truck } from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  dataBR, romaneiosClienteApi,
  type LinhaRomaneio, type ResumoRomaneio, type RomaneioGuardado,
} from '../services/romaneiosClienteApi'
import ClienteBadge from './ClienteBadge'

// Todos os romaneios de cliente que o sistema guardou, por cliente. "Ver" lê a
// planilha no servidor e monta o romaneio aqui, sem precisar do Excel; o
// arquivo original continua disponível para baixar.

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho',
  'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

// A data que vale para ordenar e agrupar: a do nome do arquivo; sem ela, o dia
// em que foi guardado.
const dataDe = (r: RomaneioGuardado) => (r.data ?? r.criadoEm).slice(0, 10)

const mesDe = (iso: string) => {
  const [a, m] = iso.split('-')
  return `${MESES[Number(m) - 1] ?? ''} de ${a}`
}

const caixas = (n: number) => n.toLocaleString('pt-BR', { maximumFractionDigits: 2 })

export default function RomaneiosGuardados() {
  const [lista, setLista] = useState<RomaneioGuardado[]>([])
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const [cliente, setCliente] = useState<string>('TODOS')
  const [busca, setBusca] = useState('')
  const [aberto, setAberto] = useState<ResumoRomaneio | null>(null)
  const [abrindo, setAbrindo] = useState<number | null>(null)
  const [baixando, setBaixando] = useState<number | null>(null)
  const [importando, setImportando] = useState(false)
  // Romaneios recusados na importação ficam na tela até ele fechar (D-07)
  const [recusados, setRecusados] = useState<string[]>([])
  const confirmar = useConfirmar()

  const carregar = async () => {
    setCarregando(true)
    try {
      const { data } = await romaneiosClienteApi.listar()
      setLista(data)
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally { setCarregando(false) }
  }

  // Monta ao abrir a aba: o que acabou de ser gerado já aparece.
  useEffect(() => { carregar() }, [])

  const porCliente = useMemo(() => {
    const m = new Map<string, number>()
    for (const r of lista) m.set(r.apelido, (m.get(r.apelido) ?? 0) + 1)
    return [...m.entries()].sort((a, b) => a[0].localeCompare(b[0]))
  }, [lista])

  const filtrada = useMemo(() => {
    const b = busca.trim().replace(/^n[º°o]?\s*/i, '')
    return lista
      .filter(r => cliente === 'TODOS' || r.apelido === cliente)
      .filter(r => !b || String(r.numero ?? '').includes(b) || r.nome.toUpperCase().includes(b.toUpperCase()))
      .sort((a, b) => dataDe(b).localeCompare(dataDe(a))
        || (b.numero ?? 0) - (a.numero ?? 0)
        || b.criadoEm.localeCompare(a.criadoEm))
  }, [lista, cliente, busca])

  // Agrupa por mês para a lista longa não virar uma parede
  const meses = useMemo(() => {
    const grupos: { mes: string; itens: RomaneioGuardado[] }[] = []
    for (const r of filtrada) {
      const mes = mesDe(dataDe(r))
      const ultimo = grupos[grupos.length - 1]
      if (ultimo?.mes === mes) ultimo.itens.push(r)
      else grupos.push({ mes, itens: [r] })
    }
    return grupos
  }, [filtrada])

  const ver = async (r: RomaneioGuardado) => {
    setAbrindo(r.id)
    try {
      const { data } = await romaneiosClienteApi.resumo(r.id)
      setAberto(data)
    } catch (e) { toast.error(erroDe(e, 'Não consegui abrir o romaneio.')) }
    finally { setAbrindo(null) }
  }

  const baixar = async (id: number, nome: string) => {
    setBaixando(id)
    try { await romaneiosClienteApi.baixar(id, nome) }
    catch { toast.error('Não consegui baixar o arquivo.') }
    finally { setBaixando(null) }
  }

  // Romaneios de fora do sistema (os do Drive): cada um é lido antes de entrar
  const importar = async (arquivos: File[]) => {
    const planilhas = arquivos.filter(a => a.name.toLowerCase().endsWith('.xlsx') && !a.name.startsWith('~$'))
    if (!planilhas.length) { toast.error('Selecione as planilhas dos romaneios (.xlsx).'); return }
    setImportando(true)
    try {
      const { data } = await romaneiosClienteApi.importar(planilhas)
      const entraram = data.novos + data.atualizados
      if (entraram) toast.success(`${entraram} romaneio(s) guardado(s)${data.atualizados ? ` (${data.atualizados} atualizado(s))` : ''}.`)
      setRecusados(data.recusados)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui importar os romaneios.')) }
    finally { setImportando(false) }
  }

  const botaoImportar = (
    <label className={`inline-flex items-center gap-2 px-3 py-2 border border-gray-300 text-sm font-medium text-gray-700 rounded-lg cursor-pointer hover:bg-gray-50 ${importando ? 'opacity-50 pointer-events-none' : ''}`}
           title="Traz para o sistema romaneios feitos fora dele, como os do Drive">
      {importando ? <Loader2 size={15} className="animate-spin" /> : <FileUp size={15} />}
      {importando ? 'Importando…' : 'Importar romaneios'}
      <input type="file" accept=".xlsx" multiple hidden disabled={importando}
             onChange={e => { importar(Array.from(e.target.files ?? [])); e.target.value = '' }} />
    </label>
  )

  const excluir = async (r: RomaneioGuardado) => {
    let msg = 'Não dá para desfazer.'
    if (r.calculoId) msg += ' Ele também sai da carga em Viagens.'
    if (r.versoes > 1) msg += ` As outras ${r.versoes - 1} versão(ões) com o mesmo nome continuam guardadas.`
    if (!await confirmar({ titulo: 'Excluir o romaneio?', texto: <><b className="break-all">{r.nome}</b>. {msg}</>, confirmar: 'Excluir romaneio' })) return
    try {
      await romaneiosClienteApi.excluir(r.id)
      toast.success('Romaneio excluído.')
      if (aberto?.id === r.id) setAberto(null)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const avisoRecusados = recusados.length > 0 && (
    <Aviso tipo="atencao" titulo="Ficaram de fora" itens={recusados} onFechar={() => setRecusados([])} />
  )

  if (carregando && !lista.length)
    return <Carregando linhas={4} rotulo="Carregando os romaneios guardados" />

  if (falhou && !lista.length)
    return <ErroCarga mensagem="Não consegui carregar os romaneios guardados." onTentar={carregar} />

  if (!lista.length)
    return (
      <div className="space-y-3">
      {avisoRecusados}
      <div className="bg-white rounded-xl border border-gray-200 p-8 text-center">
        <FileText size={28} strokeWidth={1.5} className="mx-auto mb-2 text-gray-300" />
        <p className="font-medium text-gray-700">Nenhum romaneio guardado ainda</p>
        <p className="text-sm text-gray-500 mt-1">Tudo o que for gerado daqui em diante fica guardado aqui, separado por cliente.</p>
        <div className="mt-4">{botaoImportar}</div>
      </div>
      </div>
    )

  return (
    <div className="space-y-4">
      {avisoRecusados}
      {/* Clientes */}
      <div className="flex flex-wrap gap-2">
        <FiltroCliente ativo={cliente === 'TODOS'} onClick={() => setCliente('TODOS')} contagem={lista.length}>
          <span className="inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold border bg-white text-gray-700 border-gray-300">
            Todos
          </span>
        </FiltroCliente>
        {porCliente.map(([apelido, n]) => (
          <FiltroCliente key={apelido} ativo={cliente === apelido} onClick={() => setCliente(apelido)} contagem={n}>
            <ClienteBadge apelido={apelido} />
          </FiltroCliente>
        ))}
      </div>

      {/* Busca */}
      <div className="flex flex-wrap items-center justify-between gap-3">
      <label className="relative block w-full max-w-xs">
        <Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
        <input value={busca} onChange={e => setBusca(e.target.value)} inputMode="numeric"
               placeholder="Buscar pelo número do romaneio"
               className="w-full border border-gray-300 rounded-lg pl-9 pr-3 py-2 text-sm" />
      </label>
      {botaoImportar}
      </div>

      {!filtrada.length ? (
        <p className="text-sm text-gray-500 bg-white rounded-xl border border-gray-200 px-4 py-6 text-center">
          Nenhum romaneio {busca.trim() ? `com o número "${busca.trim()}"` : 'deste cliente'}.
        </p>
      ) : meses.map(g => (
        <section key={g.mes} className="space-y-2">
          <h2 className="text-xs font-semibold text-gray-500 first-letter:uppercase">
            {g.mes} <span className="font-normal text-gray-400">· {g.itens.length}</span>
          </h2>
          <div className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
            {g.itens.map(r => (
              <div key={r.id} className="px-4 py-3 flex flex-wrap items-center gap-x-4 gap-y-2">
                <div className="w-20 shrink-0">
                  {r.tipo === 'frete' ? (
                    <span className="text-xs font-medium text-gray-500">Frete</span>
                  ) : r.numero != null ? (
                    <span className="block leading-tight">
                      <span className="block text-[11px] text-gray-400">romaneio</span>
                      <span className="font-mono text-lg font-semibold text-gray-900 tabular-nums">{r.numero}</span>
                    </span>
                  ) : (
                    <span className="text-xs text-gray-400">sem número</span>
                  )}
                </div>

                <div className="flex-1 min-w-[180px] space-y-1">
                  <div className="flex flex-wrap items-center gap-2">
                    {cliente === 'TODOS' && <span className="scale-90 origin-left"><ClienteBadge apelido={r.apelido} /></span>}
                    <span className="text-sm font-medium text-gray-800 tabular-nums">{dataBR(r.data) || 'sem data'}</span>
                  </div>
                  <p className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-xs text-gray-500">
                    {r.calculoId ? (
                      <span className="inline-flex items-center gap-1 text-tinta-700">
                        <Truck size={12} /> Carga {r.placa ?? ''}
                      </span>
                    ) : r.placa ? (
                      <span className="inline-flex items-center gap-1"><Truck size={12} /> {r.placa}</span>
                    ) : (
                      <span>Sem carga</span>
                    )}
                    {r.versoes > 1 && (
                      <span title="O mesmo arquivo foi guardado mais de uma vez; aqui aparece o mais recente.">
                        {r.versoes} versões
                      </span>
                    )}
                    <span className="truncate max-w-[260px]" title={r.nome}>{r.nome}</span>
                  </p>
                </div>

                <div className="flex items-center gap-1.5">
                  <button onClick={() => ver(r)} disabled={abrindo === r.id}
                          className="inline-flex items-center gap-1.5 px-3 py-1.5 bg-tinta-600 text-white text-sm font-medium rounded-lg hover:bg-tinta-700 disabled:opacity-50">
                    {abrindo === r.id ? <Loader2 size={14} className="animate-spin" /> : <Eye size={14} />} Ver
                  </button>
                  <button onClick={() => baixar(r.id, r.nome)} disabled={baixando === r.id}
                          className="inline-flex items-center gap-1.5 px-3 py-1.5 border border-gray-300 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-50 disabled:opacity-50">
                    {baixando === r.id ? <Loader2 size={14} className="animate-spin" /> : <Download size={14} />} Baixar
                  </button>
                  <button onClick={() => excluir(r)} title="Excluir"
                          className="p-2 text-gray-400 hover:text-red-600 rounded-lg hover:bg-red-50">
                    <Trash2 size={15} />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </section>
      ))}

      {aberto && (
        <VisaoRomaneio resumo={aberto} baixando={baixando === aberto.id}
                       onBaixar={() => baixar(aberto.id, aberto.nome)} onClose={() => setAberto(null)} />
      )}
    </div>
  )
}

function FiltroCliente({ ativo, onClick, contagem, children }: {
  ativo: boolean; onClick: () => void; contagem: number; children: React.ReactNode
}) {
  return (
    <button onClick={onClick} aria-pressed={ativo}
            className={`inline-flex items-center gap-1.5 rounded-full pr-2.5 transition-opacity
              ${ativo ? 'ring-2 ring-tinta-500 ring-offset-1 bg-white' : 'opacity-60 hover:opacity-100'}`}>
      {children}
      <span className="text-xs font-semibold text-gray-600 tabular-nums">{contagem}</span>
    </button>
  )
}

// ───────────────────────── Visão do romaneio ─────────────────────────

function VisaoRomaneio({ resumo, baixando, onBaixar, onClose }: {
  resumo: ResumoRomaneio; baixando: boolean; onBaixar: () => void; onClose: () => void
}) {
  const titulo = resumo.tipo === 'frete'
    ? `Relatório de frete · ${dataBR(resumo.data)}`
    : `${resumo.numero != null ? `Romaneio ${resumo.numero}` : 'Romaneio'} · ${resumo.apelido}`

  return (
    <Modal
      onClose={onClose}
      fecharFora
      largura="lg"
      fundoCinza
      classeCorpo="p-3 sm:p-5"
      titulo={titulo}
      descricao={<>{resumo.nome}{resumo.calculoId ? ` · carga ${resumo.placaCarga ?? ''}` : ' · sem carga'}</>}
      acoes={
        <Botao variante="secundario" tamanho="sm" icone={Download} onClick={onBaixar} carregando={baixando}
               aria-label={`Baixar ${resumo.tipo === 'frete' ? '.txt' : '.xlsx'}`}>
          <span className="hidden sm:inline">Baixar {resumo.tipo === 'frete' ? '.txt' : '.xlsx'}</span>
        </Botao>
      }
    >
          {resumo.texto != null ? (
            <pre className="bg-white border border-gray-200 rounded-xl p-4 text-xs sm:text-sm font-mono text-gray-800 whitespace-pre overflow-x-auto">
              {resumo.texto}
            </pre>
          ) : resumo.romaneio ? (
            <Documento r={resumo.romaneio} numero={resumo.numero} />
          ) : (
            <p className="text-sm text-gray-500">Não há nada para mostrar neste arquivo. Baixe e abra no Excel.</p>
          )}
    </Modal>
  )
}

function Documento({ r, numero }: { r: NonNullable<ResumoRomaneio['romaneio']>; numero: number | null }) {
  const temProdutosD = r.totalCaixasProdutosD > 0

  return (
    <div className="space-y-3">
      {r.avisos.length > 0 && (
        <div className="bg-amber-50 border border-amber-200 rounded-xl px-4 py-3 space-y-1">
          {r.avisos.map((a, i) => (
            <p key={i} className="flex items-start gap-2 text-sm text-amber-800">
              <AlertTriangle size={14} className="mt-0.5 flex-shrink-0" /> {a}
            </p>
          ))}
        </div>
      )}

      <article className="bg-white border border-gray-200 rounded-xl shadow-sm">
        {/* Cabeçalho */}
        <header className="px-5 pt-5 pb-4 border-b border-gray-200">
          <div className="flex items-start justify-between gap-4">
            <div>
              <p className="text-[11px] font-semibold tracking-widest text-gray-500 uppercase">Remessa de mercadorias · volumes</p>
              <p className="font-display text-xl font-semibold text-gray-900 mt-0.5">{r.destinatario ?? 'Destinatário não informado'}</p>
            </div>
            <div className="text-right">
              {numero != null && <p className="font-mono text-lg font-semibold text-gray-900">Romaneio {numero}</p>}
              <p className="text-sm text-gray-600 tabular-nums">{dataBR(r.data) || 'sem data'}</p>
            </div>
          </div>
          <dl className="grid grid-cols-2 sm:grid-cols-4 gap-x-4 gap-y-2 mt-4 text-sm">
            <Campo rotulo="Motorista" valor={r.motorista} />
            <Campo rotulo="Placa" valor={r.placa} mono />
            <Campo rotulo="Fornecedor" valor={r.fornecedor} />
            <Campo rotulo="Vendedor" valor={r.vendedor} />
          </dl>
        </header>

        {/* Tipos */}
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-xs text-gray-500 border-b border-gray-200">
                <th className="text-left font-medium px-5 py-2">Tipo</th>
                <th className="text-right font-medium px-3 py-2">Caixas</th>
                <th className="text-right font-medium px-3 py-2">Preço unitário</th>
                <th className="text-right font-medium px-5 py-2">Total</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {r.linhas.length === 0 && (
                <tr><td colSpan={4} className="px-5 py-4 text-center text-gray-400">Nenhum tipo preenchido.</td></tr>
              )}
              {r.linhas.map((l, i) => <LinhaTipo key={`${l.tipo}-${i}`} l={l} />)}
            </tbody>
          </table>
        </div>

        {/* Totais */}
        <div className="border-t border-gray-200 px-5 py-4 grid sm:grid-cols-2 gap-4">
          <div className="text-sm space-y-1">
            <Par rotulo="Caixas de volumes" valor={caixas(r.totalCaixasVolumes)} />
            {temProdutosD && <Par rotulo="Caixas de produtosD" valor={caixas(r.totalCaixasProdutosD)} />}
          </div>
          <div className="text-sm space-y-1 sm:border-l sm:border-gray-100 sm:pl-4">
            <Par rotulo="Soma dos produtos" valor={formatBRL(r.somaLinhas)} />
            {r.acrescimos !== 0 && <Par rotulo="Acréscimos" valor={`+ ${formatBRL(r.acrescimos)}`} />}
            <Par rotulo="Descontos (funrural)" valor={r.descontos ? `− ${formatBRL(r.descontos)}` : formatBRL(0)} />
            <div className="flex items-baseline justify-between gap-3 pt-2 mt-1 border-t border-gray-200">
              <span className="font-semibold text-gray-900">Valor a pagar</span>
              <span className="font-display text-2xl font-semibold text-gray-900 tabular-nums">{formatBRL(r.valorAPagar)}</span>
            </div>
          </div>
        </div>

        {/* Funrural e boletos */}
        <div className="border-t border-gray-200 px-5 py-4 grid sm:grid-cols-2 gap-4 text-sm">
          <div>
            <p className="text-xs font-semibold text-gray-500 mb-1.5">Funrural por nota</p>
            {r.funruralPorNota.length > 0 ? (
              <ul className="space-y-0.5">
                {r.funruralPorNota.map((f, i) => (
                  <li key={`${f.nf}-${i}`} className="flex justify-between gap-3 tabular-nums">
                    <span className="text-gray-600">NF {f.nf}</span>
                    <span className="text-gray-800">{formatBRL(f.valor)}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="text-gray-400">{r.funrural ?? 'Sem funrural na planilha.'}</p>
            )}
          </div>
          <div>
            <p className="text-xs font-semibold text-gray-500 mb-1.5">Boletos</p>
            {r.valoresBoletos.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {r.valoresBoletos.map((v, i) => (
                  <span key={i} className="inline-flex items-center bg-gema-50 text-gema-800 border border-gema-200 rounded-md px-2 py-1 text-sm font-medium tabular-nums">
                    {formatBRL(v)}
                  </span>
                ))}
              </div>
            ) : (
              <p className="text-gray-400">{r.boletos ?? 'Sem boletos na planilha.'}</p>
            )}
          </div>
        </div>
      </article>
    </div>
  )
}

function LinhaTipo({ l }: { l: LinhaRomaneio }) {
  const extra = l.categoria === 'produtoD' ? 'produtoD' : l.categoria === 'produtoB' ? 'produtoB' : null
  return (
    <tr>
      <td className="px-5 py-2 text-gray-800">
        {l.tipo}
        {extra && <span className="ml-2 text-[11px] text-gray-400">{extra}</span>}
      </td>
      <td className="px-3 py-2 text-right tabular-nums text-gray-800">{caixas(l.caixas)}</td>
      <td className="px-3 py-2 text-right tabular-nums text-gray-600">
        {l.precoUnitario != null && l.precoUnitario !== 0 ? formatBRL(l.precoUnitario)
          : <span className="text-amber-700">sem preço</span>}
      </td>
      <td className="px-5 py-2 text-right tabular-nums font-medium text-gray-900">{formatBRL(l.total)}</td>
    </tr>
  )
}

function Campo({ rotulo, valor, mono }: { rotulo: string; valor: string | null; mono?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-gray-500">{rotulo}</dt>
      <dd className={`font-medium ${valor ? 'text-gray-800' : 'text-amber-700'} ${mono ? 'font-mono' : ''}`}>
        {valor ?? 'não informado'}
      </dd>
    </div>
  )
}

function Par({ rotulo, valor }: { rotulo: string; valor: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <span className="text-gray-600">{rotulo}</span>
      <span className="tabular-nums text-gray-800">{valor}</span>
    </div>
  )
}
