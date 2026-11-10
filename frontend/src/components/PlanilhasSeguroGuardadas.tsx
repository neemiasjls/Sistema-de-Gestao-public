import { useEffect, useMemo, useRef, useState } from 'react'
import Aviso from './ui/Aviso'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import Botao, { BotaoIcone } from './ui/Botao'
import { Modal } from './ModalBase'
import {
  Download, Eye, FileSpreadsheet, FileUp, Minus, Trash2, TrendingDown, TrendingUp, TriangleAlert,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  nomeMesSeguro, planilhasSeguroApi,
  type DetalhePlanilhaSeguro, type PlanilhaSeguroGuardada, type ResumoSeguro,
} from '../services/planilhasSeguroApi'

// As planilhas SG guardadas no sistema, uma por mês: a que a tela gera e as
// trazidas do Drive. "Ver" mostra o resumo lido da planilha, sem precisar do
// Excel; o arquivo continua disponível para baixar.

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

const mesAno = (mes: number, ano: number) => `${nomeMesSeguro(mes)} de ${ano}`

const numero = (n: number) => n.toLocaleString('pt-BR')

export default function PlanilhasSeguroGuardadas({ atualizar }: { atualizar: number }) {
  const [lista, setLista] = useState<PlanilhaSeguroGuardada[]>([])
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const [aberto, setAberto] = useState<DetalhePlanilhaSeguro | null>(null)
  const [abrindo, setAbrindo] = useState<number | null>(null)
  const [baixando, setBaixando] = useState<number | null>(null)
  const [importando, setImportando] = useState(false)
  // O que a importação recusou ou substituiu fica na tela até ele fechar (D-07)
  const [avisos, setAvisos] = useState<string[]>([])
  const entrada = useRef<HTMLInputElement>(null)
  const confirmar = useConfirmar()

  const carregar = async () => {
    setCarregando(true)
    try {
      const { data } = await planilhasSeguroApi.listar()
      setLista(data)
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally { setCarregando(false) }
  }

  // Recarrega ao abrir a tela e depois de cada geração: a planilha que acabou
  // de sair já aparece na lista.
  useEffect(() => { carregar() }, [atualizar])

  const ver = async (p: PlanilhaSeguroGuardada) => {
    setAbrindo(p.id)
    try {
      const { data } = await planilhasSeguroApi.detalhe(p.id)
      setAberto(data)
    } catch (e) { toast.error(erroDe(e, 'Não consegui abrir a planilha.')) }
    finally { setAbrindo(null) }
  }

  const baixar = async (id: number, nome: string) => {
    setBaixando(id)
    try { await planilhasSeguroApi.baixar(id, nome) }
    catch { toast.error('Não consegui baixar a planilha.') }
    finally { setBaixando(null) }
  }

  const excluir = async (p: PlanilhaSeguroGuardada) => {
    if (!await confirmar({
      titulo: <span className="first-letter:uppercase">Excluir a planilha SG de {mesAno(p.mes, p.ano)}?</span>,
      texto: <><b className="break-all">{p.nome}</b>. Não dá para desfazer.</>,
      confirmar: 'Excluir planilha',
    })) return
    try {
      await planilhasSeguroApi.excluir(p.id)
      toast.success('Planilha excluída.')
      if (aberto?.id === p.id) setAberto(null)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  // Planilhas de fora do sistema (as do Drive): cada uma é lida antes de entrar
  const importar = async (arquivos: File[]) => {
    const planilhas = arquivos.filter(a => a.name.toLowerCase().endsWith('.xlsx') && !a.name.startsWith('~$'))
    if (!planilhas.length) { toast.error('Selecione as planilhas SG (.xlsx).'); return }
    setImportando(true)
    try {
      const { data } = await planilhasSeguroApi.importar(planilhas)
      const trocadas = data.guardadas.filter(g => g.substituiu)
      if (data.guardadas.length)
        toast.success(`${data.guardadas.length} planilha(s) guardada(s)${trocadas.length ? `, ${trocadas.length} no lugar da que já estava` : ''}.`)
      setAvisos([
        ...trocadas.map(g => `${mesAno(g.mes, g.ano)} já estava guardada${g.origemAnterior === 'gerada' ? ' (gerada pelo sistema)' : ''} e foi substituída por ${g.nome}.`),
        ...data.recusados,
      ])
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui importar as planilhas.')) }
    finally { setImportando(false) }
  }

  // O último mês guardado e o mês de calendário anterior a ele, se estiver guardado
  const comparacao = useMemo(() => {
    if (!lista.length) return null
    const ultimo = lista[0]
    const mesAnt = ultimo.mes === 1 ? 12 : ultimo.mes - 1
    const anoAnt = ultimo.mes === 1 ? ultimo.ano - 1 : ultimo.ano
    const anterior = lista.find(p => p.mes === mesAnt && p.ano === anoAnt) ?? null
    return { ultimo, anterior, mesAnt, anoAnt }
  }, [lista])

  return (
    <section aria-labelledby="sg-guardadas" className="space-y-4">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div className="min-w-0">
          <h2 id="sg-guardadas" className="text-lg font-semibold text-gray-900">Planilhas SG guardadas</h2>
          <p className="text-sm text-gray-600">
            Toda planilha gerada aqui fica guardada, uma por mês. Gerar o mesmo mês de novo troca a que estava.
          </p>
        </div>
        <Botao variante="secundario" icone={FileUp} carregando={importando} textoCarregando="Importando..."
               onClick={() => entrada.current?.click()} className="shrink-0"
               title="Traz para o sistema as planilhas SG feitas fora dele, como as do Drive">
          Importar planilhas SG
        </Botao>
        <input ref={entrada} type="file" accept=".xlsx" multiple hidden tabIndex={-1}
               onChange={e => { importar(Array.from(e.target.files ?? [])); e.target.value = '' }} />
      </div>

      {avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira a importação" itens={avisos} onFechar={() => setAvisos([])} />}

      {carregando && !lista.length ? (
        <Carregando linhas={3} rotulo="Carregando as planilhas SG" />
      ) : falhou && !lista.length ? (
        <ErroCarga mensagem="Não consegui carregar as planilhas SG guardadas." onTentar={carregar} />
      ) : !lista.length ? (
        <Vazio icone={FileSpreadsheet} titulo="Nenhuma planilha SG guardada ainda">
          As próximas geradas aqui ficam guardadas. As antigas, que estão no Drive, entram por Importar planilhas SG.
        </Vazio>
      ) : (
        <>
          {comparacao && <PainelUltimoMes {...comparacao} />}

          <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            <div aria-hidden className="hidden md:grid grid-cols-[1.4fr_0.5fr_1fr_1fr_1fr_0.8fr_auto] gap-3 px-4 py-2 text-xs font-medium text-gray-500 border-b border-gray-200 bg-gray-50">
              <span>Mês</span>
              <span className="text-right">CT-es</span>
              <span className="text-right">Frete</span>
              <span className="text-right">Seguro</span>
              <span className="text-right">ICMS</span>
              <span>Origem</span>
              <span className="w-[164px]" />
            </div>
            <ul className="divide-y divide-gray-100">
              {lista.map(p => (
                <li key={p.id}
                    className="px-4 py-3 grid grid-cols-2 md:grid-cols-[1.4fr_0.5fr_1fr_1fr_1fr_0.8fr_auto] gap-x-3 gap-y-2 items-center">
                  <div className="col-span-2 md:col-span-1 min-w-0">
                    <p className="font-semibold text-gray-900 first-letter:uppercase">{mesAno(p.mes, p.ano)}</p>
                    <p className="text-sm text-gray-500 flex items-center gap-1.5 min-w-0" title={p.nome}>
                      {p.avisos > 0 && (
                        <span className="inline-flex items-center gap-0.5 text-amber-800 font-medium shrink-0" title="A planilha tem avisos: abra em Ver">
                          <TriangleAlert size={13} aria-hidden /> {p.avisos}<span className="sr-only"> {p.avisos === 1 ? 'aviso' : 'avisos'}</span>
                        </span>
                      )}
                      <span className="truncate">{p.nome}</span>
                    </p>
                  </div>
                  <Valor rotulo="CT-es" valor={numero(p.quantidadeCtes)} />
                  <Valor rotulo="Frete" valor={formatBRL(p.totalFrete)} forte />
                  <Valor rotulo="Seguro" valor={formatBRL(p.totalSeguro)} />
                  <Valor rotulo="ICMS" valor={formatBRL(p.totalIcms)} />
                  <div><SeloOrigem origem={p.origem} /></div>
                  <div className="col-span-2 md:col-span-1 flex items-center gap-1.5 md:justify-end [&>button:not(:last-child)]:flex-1 md:[&>button]:!flex-none">
                    <Botao variante="secundario" tamanho="sm" icone={Eye} onClick={() => ver(p)} carregando={abrindo === p.id}>Ver</Botao>
                    <Botao variante="secundario" tamanho="sm" icone={Download} onClick={() => baixar(p.id, p.nome)} carregando={baixando === p.id}>Baixar</Botao>
                    <BotaoIcone icone={Trash2} perigo rotulo={`Excluir a planilha de ${mesAno(p.mes, p.ano)}`} onClick={() => excluir(p)} />
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </>
      )}

      {aberto && (
        <VisaoPlanilha detalhe={aberto} baixando={baixando === aberto.id}
                       onBaixar={() => baixar(aberto.id, aberto.nome)} onClose={() => setAberto(null)} />
      )}
    </section>
  )
}

// Na tela larga os números ficam nas colunas do cabeçalho; na estreita, cada
// um leva o próprio rótulo.
function Valor({ rotulo, valor, forte }: { rotulo: string; valor: string; forte?: boolean }) {
  return (
    <p className={`text-sm tabular-nums md:text-right ${forte ? 'font-semibold text-gray-900' : 'text-gray-800'}`}>
      <span className="md:hidden text-xs font-normal text-gray-500 mr-1.5">{rotulo}</span>{valor}
    </p>
  )
}

function SeloOrigem({ origem }: { origem: PlanilhaSeguroGuardada['origem'] }) {
  return origem === 'gerada'
    ? <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-tinta-50 text-tinta-800 border border-tinta-200">Gerada aqui</span>
    : <span className="inline-flex px-2 py-0.5 rounded-full text-xs font-medium bg-gema-50 text-gema-900 border border-gema-200">Importada</span>
}

// ───────────────────────── Último mês × anterior ─────────────────────────

function PainelUltimoMes({ ultimo, anterior, mesAnt, anoAnt }: {
  ultimo: PlanilhaSeguroGuardada; anterior: PlanilhaSeguroGuardada | null; mesAnt: number; anoAnt: number
}) {
  const itens: { rotulo: string; atual: number; antes: number | null }[] = [
    { rotulo: 'Frete', atual: ultimo.totalFrete, antes: anterior?.totalFrete ?? null },
    { rotulo: 'Seguro', atual: ultimo.totalSeguro, antes: anterior?.totalSeguro ?? null },
    { rotulo: 'ICMS', atual: ultimo.totalIcms, antes: anterior?.totalIcms ?? null },
  ]

  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4 space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-base font-semibold text-gray-900">
          Último mês guardado: <span className="first-letter:uppercase inline-block">{mesAno(ultimo.mes, ultimo.ano)}</span>
          <span className="font-sans text-sm font-normal text-gray-500 tabular-nums"> · {numero(ultimo.quantidadeCtes)} CT-es</span>
        </h3>
        <p className="text-sm text-gray-500">
          {anterior
            ? <>Comparado com {mesAno(mesAnt, anoAnt)} (<span className="tabular-nums">{numero(anterior.quantidadeCtes)}</span> CT-es)</>
            : <span className="text-amber-800">{mesAno(mesAnt, anoAnt)} não está guardado: sem comparação</span>}
        </p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-px bg-gray-200 rounded-lg border border-gray-200 overflow-hidden">
        {itens.map(i => <CartaoComparacao key={i.rotulo} {...i} />)}
      </div>
    </div>
  )
}

function CartaoComparacao({ rotulo, atual, antes }: { rotulo: string; atual: number; antes: number | null }) {
  const diferenca = antes == null ? null : atual - antes
  const pct = antes ? (diferenca! / antes) * 100 : null
  const Icone = diferenca == null || Math.abs(diferenca) < 0.005 ? Minus : diferenca > 0 ? TrendingUp : TrendingDown

  return (
    <div className="bg-white px-3.5 py-3">
      <p className="text-xs font-medium text-gray-500">{rotulo}</p>
      <p className="font-display text-xl font-semibold text-gray-900 tabular-nums leading-tight">{formatBRL(atual)}</p>
      {diferenca != null ? (
        <p className="text-sm text-gray-600 tabular-nums flex flex-wrap items-center gap-x-1">
          <Icone size={14} className="text-gray-500" aria-hidden />
          {diferenca >= 0 ? '+' : '−'} {formatBRL(Math.abs(diferenca))}
          {pct != null && <span className="text-gray-500">({pct >= 0 ? '+' : '−'}{Math.abs(pct).toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%)</span>}
        </p>
      ) : (
        <p className="text-sm text-gray-500">sem o mês anterior</p>
      )}
    </div>
  )
}

// ───────────────────────── Resumo do mês ─────────────────────────

function VisaoPlanilha({ detalhe, baixando, onBaixar, onClose }: {
  detalhe: DetalhePlanilhaSeguro; baixando: boolean; onBaixar: () => void; onClose: () => void
}) {
  const r = detalhe.resumo
  const v = detalhe.valoresInformados

  return (
    <Modal
      onClose={onClose}
      fecharFora
      largura="xl"
      fundoCinza
      classeCorpo="p-3 sm:p-5 space-y-4"
      titulo={<span className="first-letter:uppercase">Planilha SG · {mesAno(detalhe.mes, detalhe.ano)}</span>}
      descricao={<>{detalhe.nome} · {detalhe.origem === 'gerada' ? 'gerada aqui' : 'importada'} em {new Date(detalhe.atualizadoEm).toLocaleDateString('pt-BR')}</>}
      acoes={
        <Botao variante="secundario" tamanho="sm" icone={Download} onClick={onBaixar} carregando={baixando} aria-label="Baixar .xlsx">
          <span className="hidden sm:inline">Baixar .xlsx</span>
        </Botao>
      }
    >
      {r.avisos.length > 0 && <Aviso tipo="atencao" titulo="A planilha avisa" itens={r.avisos} />}

      {/* Totais do mês */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3">
        <Cartao rotulo="CT-es" valor={numero(r.quantidadeCtes)}
                detalhe={r.quantidadeCancelados ? `${r.quantidadeCancelados} cancelado(s) fora da conta` : undefined} />
        <Cartao rotulo="Valor das notas" valor={formatBRL(r.totalValorNF)} />
        <Cartao rotulo="Frete" valor={formatBRL(r.totalFrete)} />
        <Cartao rotulo="Seguro das cargas" valor={formatBRL(r.totalSeguro)} />
        <Cartao rotulo="ICMS" valor={formatBRL(r.totalIcms)} />
        <Cartao rotulo="Alíquota do ICMS" valor={r.aliquotaIcms != null
          ? `${r.aliquotaIcms.toLocaleString('pt-BR', { maximumFractionDigits: 4 })}%` : null} />
      </div>

      {/* Seguros e despesas */}
      <div className="grid sm:grid-cols-2 gap-3">
        <div className="bg-white rounded-xl border border-gray-200 p-4 text-sm space-y-1.5">
          <h3 className="text-sm font-semibold text-gray-700 mb-1">Seguros</h3>
          <Linha rotulo="Seguro das cargas" valor={r.totalSeguro} />
          <Linha rotulo="IOF seguros" valor={r.iofSeguros} />
          <Linha rotulo="Diferença seguros" valor={r.diferencaSeguros} vazio="sem diferença" />
          <Linha rotulo="Total dos seguros (Porto Seguros)" valor={r.portoSeguros ?? r.valorTotalSeguros} forte />
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-4 text-sm space-y-1.5">
          <h3 className="text-sm font-semibold text-gray-700 mb-1">Despesas do mês (aba RESUMO)</h3>
          <Linha rotulo="DAS referente ao mês" valor={r.dasReferente ?? r.totalIcms} />
          <Linha rotulo="Serviço escritório" valor={r.servicoEscritorio} />
          <Linha rotulo="Taxa B Soft" valor={r.taxaBSoft} />
          <Linha rotulo="Encargos por caminhão" valor={r.encargosPorCaminhao}
                 sufixo={r.quantidadeCaminhoes != null ? ` · ${r.quantidadeCaminhoes} caminhões` : undefined} />
          <Linha rotulo="Total de despesas" valor={r.totalDespesas} forte />
        </div>
      </div>

      {v && (
        <p className="text-sm text-gray-600 bg-white rounded-xl border border-gray-200 px-4 py-2.5 tabular-nums">
          <span className="font-semibold text-gray-800">Digitado na geração: </span>
          DAS {formatBRL(v.das ?? 0)} · IOF seguros {formatBRL(v.iofSeguros ?? 0)} ·
          Serviço escritório {formatBRL(v.servicoEscritorio ?? 0)} · Taxa B Soft {formatBRL(v.taxaBSoft ?? 0)}
          {v.faturaSeguro != null && <> · Fatura do seguro {formatBRL(v.faturaSeguro)}</>}
        </p>
      )}

      <TabelaPlacas r={r} />
    </Modal>
  )
}

function TabelaPlacas({ r }: { r: ResumoSeguro }) {
  const soma = (f: (p: ResumoSeguro['placas'][number]) => number | null) =>
    r.placas.reduce((s, p) => s + (f(p) ?? 0), 0)

  if (r.placas.length === 0) {
    return <div className="bg-white rounded-xl border border-gray-200"><Vazio compacto titulo="Nenhuma placa na planilha" /></div>
  }

  return (
    <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
      {/* Celular: um cartão por placa, o total do caminhão em destaque */}
      <ul className="sm:hidden divide-y divide-gray-100">
        {r.placas.map(p => (
          <li key={p.nome} className="px-4 py-3 space-y-1.5">
            <div className="flex items-baseline justify-between gap-3">
              <span className="font-semibold text-gray-900 tabular-nums">{p.nome}</span>
              <span className="text-right">
                {p.totalCaminhao != null
                  ? <span className="font-semibold text-gray-900 tabular-nums">{formatBRL(p.totalCaminhao)}</span>
                  : <span className="text-sm font-medium text-amber-800">fora do RESUMO</span>}
              </span>
            </div>
            <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm tabular-nums">
              <div className="flex justify-between gap-2"><dt className="text-gray-500">CT-es</dt><dd>{numero(p.quantidadeCtes)}</dd></div>
              <div className="flex justify-between gap-2"><dt className="text-gray-500">Frete</dt><dd>{formatBRL(p.frete)}</dd></div>
              <div className="flex justify-between gap-2"><dt className="text-gray-500">Seguro</dt><dd>{formatBRL(p.seguro)}</dd></div>
              <div className="flex justify-between gap-2"><dt className="text-gray-500">ICMS</dt><dd>{formatBRL(p.icms)}</dd></div>
            </dl>
          </li>
        ))}
        <li className="px-4 py-3 flex items-baseline justify-between gap-3 bg-gray-50 font-semibold text-gray-900 tabular-nums">
          <span>Total</span><span>{formatBRL(soma(p => p.totalCaminhao))}</span>
        </li>
      </ul>

      <div className="hidden sm:block overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="text-xs text-gray-500 border-b border-gray-200 bg-gray-50">
              <th className="text-left font-medium px-4 py-2">Placa</th>
              <th className="text-right font-medium px-3 py-2">CT-es</th>
              <th className="text-right font-medium px-3 py-2">Valor das notas</th>
              <th className="text-right font-medium px-3 py-2">Frete</th>
              <th className="text-right font-medium px-3 py-2">Seguro</th>
              <th className="text-right font-medium px-3 py-2">ICMS</th>
              <th className="text-right font-medium px-4 py-2" title="ICMS + seguro + encargos, como na linha do caminhão no RESUMO">
                Total do caminhão
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {r.placas.map(p => (
              <tr key={p.nome}>
                <td className="px-4 py-2 font-medium text-gray-900 tabular-nums">{p.nome}</td>
                <td className="px-3 py-2 text-right tabular-nums">{numero(p.quantidadeCtes)}</td>
                <td className="px-3 py-2 text-right tabular-nums text-gray-600">{formatBRL(p.valorNF)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatBRL(p.frete)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatBRL(p.seguro)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatBRL(p.icms)}</td>
                <td className="px-4 py-2 text-right tabular-nums font-semibold text-gray-900">
                  {p.totalCaminhao != null ? formatBRL(p.totalCaminhao) : <span className="text-amber-800 font-medium">fora do RESUMO</span>}
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot>
            <tr className="border-t-2 border-gray-200 font-semibold text-gray-900">
              <td className="px-4 py-2">Total</td>
              <td className="px-3 py-2 text-right tabular-nums">{numero(soma(p => p.quantidadeCtes))}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatBRL(soma(p => p.valorNF))}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatBRL(soma(p => p.frete))}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatBRL(soma(p => p.seguro))}</td>
              <td className="px-3 py-2 text-right tabular-nums">{formatBRL(soma(p => p.icms))}</td>
              <td className="px-4 py-2 text-right tabular-nums">{formatBRL(soma(p => p.totalCaminhao))}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>
  )
}

function Cartao({ rotulo, valor, detalhe }: { rotulo: string; valor: string | null; detalhe?: string }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 px-3 py-2.5 min-w-0">
      <p className="text-xs font-medium text-gray-500">{rotulo}</p>
      {valor != null
        ? <p className="font-semibold text-gray-900 tabular-nums break-words">{valor}</p>
        : <p className="text-sm font-medium text-amber-800">não está na planilha</p>}
      {detalhe && <p className="text-xs text-gray-500 mt-0.5">{detalhe}</p>}
    </div>
  )
}

// Valor que não veio da planilha aparece como aviso, não como campo vazio
function Linha({ rotulo, valor, forte, vazio, sufixo }: {
  rotulo: string; valor: number | null; forte?: boolean; vazio?: string; sufixo?: string
}) {
  return (
    <div className={`flex items-baseline justify-between gap-3 ${forte ? 'pt-1.5 mt-1 border-t border-gray-100' : ''}`}>
      <span className={forte ? 'font-semibold text-gray-900' : 'text-gray-600'}>{rotulo}</span>
      {valor != null ? (
        <span className={`tabular-nums text-right ${forte ? 'font-semibold text-gray-900' : 'text-gray-800'}`}>
          {formatBRL(valor)}{sufixo && <span className="text-xs text-gray-500">{sufixo}</span>}
        </span>
      ) : (
        <span className={vazio ? 'text-gray-500' : 'text-amber-800 font-medium'}>{vazio ?? 'não está na planilha'}</span>
      )}
    </div>
  )
}
