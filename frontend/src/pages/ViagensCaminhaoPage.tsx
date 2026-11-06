import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import {
  CalendarCheck, ChevronRight, ClipboardList, Download, FileSpreadsheet, Milestone, Package, Plus, RefreshCw, Trash2, Truck, Upload, Users,
} from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { brl, dataBr, num } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import {
  viagensApi,
  type ListaViagens, type ViagemResumo, type Carregamento,
  type ResultadoAcerto, type ResultadoCarregamento,
} from '../services/viagensCaminhaoApi'
import FechamentoMes from '../components/viagens/FechamentoMes'
import { NavegaMes, SeloStatus, Selo, ListaResultado, mesExtenso } from '../components/viagens/ViagensPecas'

// Viagens dos caminhões (escritório), no lugar do acerto mensal em planilha
// ("N-V -MÊS-AAAA.xlsx", uma aba por viagem). Por mês e caminhão; cada viagem
// abre como a aba VIAGEMn. As que o motorista enviou entram "lançadas" para
// conferir. A planilha sai no mesmo layout (Exportar Excel de cada caminhão).

type Aba = 'viagens' | 'carregamentos' | 'fechamento'

export default function ViagensCaminhaoPage() {
  const [params, setParams] = useSearchParams()
  const agora = new Date()
  const mes = Number(params.get('mes')) || agora.getMonth() + 1
  const ano = Number(params.get('ano')) || agora.getFullYear()
  const abaUrl = params.get('aba') as Aba | null
  // A aba Preços saiu (09/10/2026) e a Motoristas foi para Cadastros (26/10/2026): link antigo cai em Viagens
  const aba: Aba = abaUrl === 'carregamentos' || abaUrl === 'fechamento' ? abaUrl : 'viagens'
  const idAbas = useAbas('viagens-caminhoes')
  const mudar = (p: Record<string, string>) => setParams({ mes: String(mes), ano: String(ano), aba, ...p })
  const navigate = useNavigate()

  return (
    <div className="space-y-5 max-w-6xl">
      <CabecalhoTela
        descricao={<>O acerto é por motorista, com a placa que ele usou em cada viagem. Cada viagem é a aba <b>VIAGEMn</b> do acerto: carga de volumes (do controle de carregamento), retornos, abastecimentos e despesas, com total, comissão e a sobra que passa para a próxima viagem do mesmo motorista. As que o motorista envia pelo celular chegam aqui como <b>lançadas</b>.</>}
        acoes={<Botao variante="primario" icone={Plus} onClick={() => navigate(`/viagens-caminhoes/nova?mes=${mes}&ano=${ano}`)}>Nova viagem</Botao>}
      />
      <Abas id={idAbas} valor={aba} onChange={v => mudar({ aba: v })} rotulo="Partes"
            opcoes={[['viagens', 'Viagens', Milestone], ['carregamentos', 'Carregamentos', Package], ['fechamento', 'Fechamento do mês', CalendarCheck]] as const} />
      <PainelAba id={idAbas} valor={aba}>
        {aba === 'viagens' && <Viagens mes={mes} ano={ano} onMes={(m, a) => mudar({ mes: String(m), ano: String(a) })} />}
        {aba === 'carregamentos' && <Carregamentos mes={mes} ano={ano} onMes={(m, a) => mudar({ mes: String(m), ano: String(a) })} />}
        {aba === 'fechamento' && <FechamentoMes mes={mes} ano={ano} onMes={(m, a) => mudar({ mes: String(m), ano: String(a) })} />}
      </PainelAba>
    </div>
  )
}

// ───────────────────────────── Viagens do mês ─────────────────────────────

function Viagens({ mes, ano, onMes }: { mes: number; ano: number; onMes: (m: number, a: number) => void }) {
  const [dados, setDados] = useState<ListaViagens | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [importar, setImportar] = useState(false)
  const [importando, setImportando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoAcerto[] | null>(null)
  const [exportando, setExportando] = useState<string | null>(null)

  const carregar = useCallback(() => {
    setFalhou(false); setDados(null)
    viagensApi.listar(ano, mes).then(setDados).catch(() => setFalhou(true))
  }, [ano, mes])
  useEffect(carregar, [carregar])

  const importarAcerto = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setImportando(true)
    try {
      const r = await viagensApi.importarAcerto(arquivos)
      setResultado(r)
      const n = r.reduce((s, x) => s + (x.criadas ?? 0), 0)
      toast.success(n > 0 ? `${n} viagens trazidas.` : 'Nada novo: essas abas já estavam aqui.')
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui importar.')) }
    finally { setImportando(false) }
  }

  const exportar = async (grupo: string, vs: ViagemResumo[]) => {
    setExportando(grupo)
    try { await viagensApi.exportar(vs[0].motoristaId != null ? { motoristaId: vs[0].motoristaId } : { placa: vs[0].placa }, ano, mes) }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui gerar a planilha.')) }
    finally { setExportando(null) }
  }

  // Um acerto por motorista (VIA-21); a API já manda em ordem de motorista e viagem
  const porMotorista = new Map<string, ViagemResumo[]>()
  for (const v of dados?.viagens ?? []) {
    const g = v.grupo ?? (v.motoristaId != null ? `M${v.motoristaId}` : `P${v.placa}`)
    porMotorista.set(g, [...(porMotorista.get(g) ?? []), v])
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
        <NavegaMes mes={mes} ano={ano} onChange={onMes} />
        <Botao variante="secundario" icone={Upload} onClick={() => setImportar(v => !v)}>Importar acerto antigo</Botao>
      </div>
      {importar && (
        <AreaSoltar aceita={['.xlsx', '.xls']} onArquivos={importarAcerto} carregando={importando} textoCarregando="Lendo as abas..."
                    titulo="Arraste a planilha do acerto do caminhão" icone={FileSpreadsheet}
                    dica="9-V -SETEMBRO-2026.xlsx. Importe antes os retornos do mês, para casar. Importar de novo não duplica." />
      )}
      {resultado && (
        <ListaResultado titulo="Acertos importados" onFechar={() => setResultado(null)} itens={resultado.map((r, i) => {
          const dif = (r.conferencia ?? []).filter(c => !c.confere)
          return {
            chave: `${r.arquivo}-${i}`, nome: `${r.arquivo}${r.placa ? ` · ${r.placa}` : ''}`, erro: r.erro, problema: dif.length > 0,
            avisos: [...(r.avisos ?? []), ...dif.map(c => `${c.aba}: ${c.diferencas.join('; ')}`)],
            linha: <>{r.mes && r.ano ? mesExtenso(r.mes, r.ano) : ''} · {r.criadas} viagens novas{(r.puladas ?? 0) > 0 && <> · {r.puladas} já estavam</>}
              {' · '}{(r.conferencia ?? []).length - dif.length} de {(r.conferencia ?? []).length} abas conferem com a planilha</>,
          }
        })} />
      )}
      {dados && dados.aConferir > 0 && (
        <Aviso tipo="atencao">{dados.aConferir} {dados.aConferir === 1 ? 'viagem enviada por motorista espera' : 'viagens enviadas por motoristas esperam'} conferência (marcadas como Lançada).</Aviso>
      )}

      {falhou ? <ErroCarga mensagem="Não consegui carregar as viagens." onTentar={carregar} />
        : !dados ? <Carregando linhas={5} rotulo="Carregando as viagens" />
        : porMotorista.size === 0 ? (
          <Vazio icone={Truck} titulo={`Nenhuma viagem em ${mesExtenso(mes, ano)}`}>
            Lance uma viagem nova, espere o motorista enviar pelo celular ou importe o acerto antigo.
          </Vazio>
        ) : [...porMotorista.entries()].map(([grupo, vs]) => (
          <section key={grupo} className="bg-white border border-gray-200 rounded-xl overflow-hidden" aria-labelledby={`acerto-${grupo}`}>
            <div className="px-4 py-3 border-b border-gray-100 flex flex-col sm:flex-row sm:items-center gap-2 justify-between">
              <div>
                <h2 id={`acerto-${grupo}`} className="font-display text-xl font-semibold text-gray-900">{vs[0].motoristaId != null ? vs[0].motorista : vs[0].placa}</h2>
                <p className="text-sm text-gray-600 tabular-nums">
                  {vs[0].motoristaId != null && <>{[...new Set(vs.map(v => v.placa))].join(', ')} · </>}
                  {vs.length} {vs.length === 1 ? 'viagem' : 'viagens'} · frete {brl(vs.reduce((s, v) => s + v.valorTotal, 0))} · comissão {brl(vs.reduce((s, v) => s + v.comissao, 0))} · sobra no fim {brl(vs[vs.length - 1].sobra)}
                </p>
              </div>
              <Botao variante="secundario" tamanho="sm" icone={Download} carregando={exportando === grupo} textoCarregando="Gerando..."
                     onClick={() => exportar(grupo, vs)}>Exportar Excel</Botao>
            </div>
            <ul className="divide-y divide-gray-100">
              {vs.map(v => (
                <li key={v.id}>
                  <Link to={`/viagens-caminhoes/${v.id}`} className="group flex items-center gap-3 px-4 py-3 hover:bg-gray-50">
                    <div className="w-16 shrink-0">
                      <p className="text-xs text-gray-500">Viagem</p>
                      <p className="font-display text-2xl font-semibold leading-none text-gray-900">{v.numero}</p>
                    </div>
                    <div className="flex-1 min-w-0 grid grid-cols-1 lg:grid-cols-[2.2fr_1fr_1fr_1fr_1fr] gap-x-4 gap-y-1">
                      <div className="min-w-0">
                        <p className="font-semibold text-gray-900 truncate tabular-nums">{v.placa}{v.placaCarreta ? ` / ${v.placaCarreta}` : ''}</p>
                        <p className="text-sm text-gray-600 truncate">
                          {v.dataSaida ? dataBr(v.dataSaida) : 'sem data'}{v.dataChegada ? ` a ${dataBr(v.dataChegada)}` : ''}{v.destino ? ` · ${v.destino}` : ''}
                        </p>
                        <div className="flex flex-wrap gap-1 mt-1">
                          <SeloStatus status={v.status} />
                          {v.origem === 'motorista' && <Selo tom="info">Enviada pelo motorista</Selo>}
                          {v.origem === 'importacao' && <Selo>Da planilha</Selo>}
                          {v.semVolumes && <Selo tom="atencao">Sem carga de volumes</Selo>}
                          {v.retornosAConferir > 0 && <Selo tom="atencao">{v.retornosAConferir} retorno a conferir</Selo>}
                        </div>
                      </div>
                      <Valor rotulo="Frete total" valor={v.valorTotal} />
                      <Valor rotulo="Despesas" valor={v.totalDespesas} />
                      <Valor rotulo="Comissão" valor={v.comissao} />
                      <Valor rotulo="Sobra" valor={v.sobra} />
                    </div>
                    <ChevronRight size={18} className="text-gray-400 group-hover:text-gray-700 shrink-0" aria-hidden />
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        ))}
    </div>
  )
}

function Valor({ rotulo, valor }: { rotulo: string; valor: number }) {
  return (
    <p className="text-sm flex lg:block justify-between gap-2">
      <span className="text-gray-500 lg:block lg:text-xs">{rotulo}</span>
      <span className="font-medium tabular-nums text-gray-900">{brl(valor)}</span>
    </p>
  )
}

// ───────────────────────────── Controle de carregamento ─────────────────────────────

type ItemResultado = { chave: string; nome: string; erro?: string | null; linha?: ReactNode; avisos?: string[]; problema?: boolean }

function Carregamentos({ mes, ano, onMes }: { mes: number; ano: number; onMes: (m: number, a: number) => void }) {
  const [lista, setLista] = useState<Carregamento[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [importando, setImportando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoCarregamento[] | null>(null)
  const [aberto, setAberto] = useState<number | null>(null)
  const confirmar = useConfirmar()

  const carregar = useCallback(() => {
    setFalhou(false); setLista(null)
    viagensApi.carregamentos(ano, mes).then(setLista).catch(() => setFalhou(true))
  }, [ano, mes])
  useEffect(carregar, [carregar])

  const importar = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setImportando(true)
    try { setResultado(await viagensApi.importarCarregamento(arquivos)); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui importar.')) }
    finally { setImportando(false) }
  }

  const excluir = async (c: Carregamento) => {
    if (!await confirmar({ titulo: 'Apagar o carregamento?', texto: `${c.placa} · ${dataBr(c.data)}. A viagem ligada continua com os volumes que já tem; as saídas de produtosB e produtosC que nasceram dele saem do estoque.` })) return
    try {
      const r = await viagensApi.excluirCarregamento(c.id)
      if (r.avisos.length) toast(r.avisos.join('\n'), { duration: 8000 })
      carregar()
    }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  // Lê de novo o carregamento guardado com a regra atual (VIA-09), sem a planilha
  const [relendo, setRelendo] = useState<number | null>(null)
  const reler = async (c: Carregamento) => {
    setRelendo(c.id)
    try {
      const r = await viagensApi.relerCarregamento(c.id)
      setResultado([{ arquivo: c.nomeArquivo ?? `${c.placa} ${dataBr(c.data)}`, cargas: [{
        aba: 'lido de novo', guardado: true, id: r.id, placa: r.placa, data: r.data, clientes: r.clientes, viagemId: r.viagemId,
        aplicado: r.aplicado, volumes: r.volumes, produtoE: r.produtoE, produtoD: r.produtoD, amarelas: r.amarelas, brancas: r.brancas,
        produtosC: r.produtosC, avisos: r.avisos,
      }] }])
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui ler de novo.')) }
    finally { setRelendo(null) }
  }

  return (
    <div className="space-y-4">
      <AreaSoltar aceita={['.xlsx', '.xls']} onArquivos={importar} carregando={importando} textoCarregando="Lendo..." compacta
                  titulo="Arraste o controle de carregamento" icone={ClipboardList}
                  dica="Resumo de carregamento: data, placa e as caixas por cliente. Casa com a viagem pela placa e pelo dia." />
      {resultado && (
        <ListaResultado titulo="Carregamentos importados" onFechar={() => setResultado(null)} itens={resultado.flatMap((r, i): ItemResultado[] =>
          r.erro ? [{ chave: `${i}`, nome: r.arquivo, erro: r.erro }] : (r.cargas ?? []).map((c, j) => ({
            chave: `${i}-${j}`, nome: `${r.arquivo} · ${c.aba}`, avisos: c.avisos, problema: !c.guardado || !c.viagemId,
            erro: c.guardado ? null : 'Não guardado: falta data, placa ou caixas.',
            linha: <>{c.placa} · {c.data ? dataBr(c.data) : ''} · {c.clientes} clientes{c.volumes != null && <> · {resumoCarga(c)}</>} · {c.aplicado ? 'volumes lançados na viagem' : c.viagemId ? 'já ligado a uma viagem' : 'sem viagem dessa placa e dia ainda (casa quando ela chegar)'}</>,
          })))} />
      )}
      <NavegaMes mes={mes} ano={ano} onChange={onMes} />
      {falhou ? <ErroCarga mensagem="Não consegui carregar." onTentar={carregar} />
        : !lista ? <Carregando linhas={3} />
        : lista.length === 0 ? <Vazio icone={Package} titulo={`Nenhum carregamento em ${mesExtenso(mes, ano)}`}>Importe a planilha do controle de carregamento.</Vazio>
        : (
          <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100 overflow-hidden">
            {lista.map(c => (
              <li key={c.id} className="px-4 py-3 space-y-2">
                <div className="flex items-center gap-3">
                  <button type="button" onClick={() => setAberto(aberto === c.id ? null : c.id)} aria-expanded={aberto === c.id}
                          className="flex-1 min-w-0 text-left">
                    <p className="font-semibold text-gray-900">{c.placa} · {dataBr(c.data)}{c.motorista ? ` · ${c.motorista}` : ''}{c.destino ? ` · ${c.destino}` : ''}</p>
                    <p className="text-sm text-gray-600 tabular-nums">{c.itens.length} clientes · {resumoCarga(c)}</p>
                  </button>
                  {c.viagemId ? <Link to={`/viagens-caminhoes/${c.viagemId}`} className="text-sm font-medium text-tinta-700 underline underline-offset-2 whitespace-nowrap">Abrir viagem</Link>
                    : <Selo tom="atencao">Sem viagem</Selo>}
                  <BotaoIcone icone={RefreshCw} rotulo="Ler de novo com a regra atual" onClick={() => reler(c)} disabled={relendo === c.id} />
                  <BotaoIcone icone={Trash2} rotulo="Apagar carregamento" perigo onClick={() => excluir(c)} />
                </div>
                {c.regraAntiga && (
                  <Aviso tipo="atencao" acao={<Botao variante="secundario" tamanho="sm" icone={RefreshCw} carregando={relendo === c.id} textoCarregando="Lendo..." onClick={() => reler(c)}>Ler de novo</Botao>}>
                    Lido pela regra antiga, que somava as colunas de detalhe com as de total e dobrava as caixas.
                  </Aviso>
                )}
                {c.observacao && <p className="text-sm text-gray-700"><span className="text-gray-500">Observação: </span>{c.observacao}</p>}
                {aberto === c.id && (
                  <ul className="text-sm text-gray-700 grid sm:grid-cols-2 gap-x-6 gap-y-0.5 animate-entrada">
                    {c.itens.map((i, k) => (
                      <li key={k} className="flex justify-between gap-2 tabular-nums"><span className="truncate">{i.cliente}</span>
                        <span className="text-right">{resumoCarga({ ...i, produtosB: i.produtosB })}</span></li>
                    ))}
                  </ul>
                )}
              </li>
            ))}
          </ul>
        )}
    </div>
  )
}

/** "60 caixas · 20 produtosD · 2 produtosB amarelas" (só o que tem) */
const plural = (n: number, um: string, varios: string) => `${num(n, n % 1 ? 1 : 0)} ${n === 1 ? um : varios}`
function resumoCarga(c: { volumes?: number; produtoE?: number; produtoD?: number; produtosB?: number; amarelas?: number; brancas?: number; produtosC?: number }) {
  const partes = [plural(c.volumes ?? 0, 'caixa', 'caixas')]
  if (c.produtoE) partes.push(plural(c.produtoE, 'produtoE', 'produtosE'))
  if (c.produtoD) partes.push(plural(c.produtoD, 'produtoD', 'produtosD'))
  if (c.amarelas) partes.push(plural(c.amarelas, 'produtoB amarela', 'produtosB amarelas'))
  if (c.brancas) partes.push(plural(c.brancas, 'produtoB branca', 'produtosB brancas'))
  if (!c.amarelas && !c.brancas && c.produtosB) partes.push(plural(c.produtosB, 'produtoB', 'produtosB'))
  if (c.produtosC) partes.push(plural(c.produtosC, 'produtoC', 'produtosC'))
  return partes.join(' · ')
}
