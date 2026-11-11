import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, History } from 'lucide-react'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Campo from '../components/ui/Campo'
import Aviso from '../components/ui/Aviso'
import Botao, { classesBotao } from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { classeCampo } from '../components/EstoquePecas'
import { portalProdutosADonoApi, type HistoricoPortalItem, type HistoricoPortalPagina } from '../services/api'
import { useAuth } from '../contexts/AuthContext'
import { comoObjeto } from '../utils/resposta'
import { dataBr, num } from '../utils/formato'

// Histórico do portal das produtosA (EST-09): só o dono vê. Tudo o que foi feito pelo celular
// (e a exclusão ou alteração pelo sistema de uma anotação do celular), com o antes → depois.

const ACOES: Record<string, string> = {
  anotacao_criada:              'Anotou',
  anotacao_editada:             'Alterou',
  anotacao_excluida:            'Excluiu',
  anotacao_excluida_no_sistema: 'Excluída no sistema',
  anotacao_editada_no_sistema:  'Alterada no sistema',
  usuario_cadastrado:           'Cadastro pelo QR',
  senha_esquecida:              'Esqueceu a senha',
  cliente_cadastrado:           'Cliente novo',
  codigo_trocado:               'Código do QR',
}

const FUSO = 'America/Sao_Paulo'
const diaDe = (iso: string) => new Date(iso).toLocaleDateString('en-CA', { timeZone: FUSO })   // aaaa-mm-dd
const horaDe = (iso: string) => new Date(iso).toLocaleTimeString('pt-BR', { timeZone: FUSO, hour: '2-digit', minute: '2-digit' })

const DIAS_SEMANA = ['domingo', 'segunda', 'terça', 'quarta', 'quinta', 'sexta', 'sábado']
function tituloDia(dia: string) {
  const hojeBr = diaDe(new Date().toISOString())
  const ontem = diaDe(new Date(Date.now() - 86_400_000).toISOString())
  if (dia === hojeBr) return `Hoje · ${dataBr(dia)}`
  if (dia === ontem) return `Ontem · ${dataBr(dia)}`
  const [a, m, d] = dia.split('-').map(Number)
  return `${DIAS_SEMANA[new Date(a, m - 1, d).getDay()]} · ${dataBr(dia)}`
}

// O que vem gravado em "antes"/"depois" (JSON em texto)
type Retrato = {
  data?: string
  cliente?: string | null
  cidade?: string | null
  pesos?: number[]
  nome?: string
  email?: string
  anotadaPor?: string | null
  numero?: number | string | null
  semLoteEmAberto?: boolean
  depoisDoRomaneio?: boolean
}

function ler(json: string | null): Retrato | null {
  if (!json) return null
  try {
    const v = JSON.parse(json)
    return v && typeof v === 'object' ? v as Retrato : null
  } catch { return null }
}

const kg = (g: number) => `${num(g / 1000, 3)} kg`
const produtosA = (n: number) => `${n} ${n === 1 ? 'produtoA' : 'produtosA'}`
const totalDe = (p: number[]) => `${produtosA(p.length)} · ${num(p.reduce((s, x) => s + x, 0) / 1000)} kg`

/** Pesos que saíram e que entraram, contando repetidos (12.740 duas vezes e uma ficou = uma saiu). */
function diferencaPesos(antes: number[], depois: number[]) {
  const sobra = [...depois]
  const retirados: number[] = []
  for (const p of antes) {
    const i = sobra.indexOf(p)
    if (i >= 0) sobra.splice(i, 1)
    else retirados.push(p)
  }
  return { retirados, incluidos: sobra }
}

function Pesos({ incluidos = [], retirados = [], mantidos = [] }: { incluidos?: number[]; retirados?: number[]; mantidos?: number[] }) {
  return (
    <ul className="flex flex-wrap gap-1.5" aria-label="Pesos">
      {mantidos.map((p, i) => (
        <li key={`m${i}`} className="text-sm tabular-nums rounded-md border border-gray-200 bg-white px-2 py-0.5 text-gray-800">{kg(p)}</li>
      ))}
      {incluidos.map((p, i) => (
        <li key={`i${i}`} className="text-sm tabular-nums rounded-md border border-tinta-300 bg-tinta-50 px-2 py-0.5 font-semibold text-tinta-900">
          <span className="sr-only">incluído: </span>+ {kg(p)}
        </li>
      ))}
      {retirados.map((p, i) => (
        <li key={`r${i}`} className="text-sm tabular-nums rounded-md border border-red-200 bg-red-50 px-2 py-0.5 text-red-800 line-through">
          <span className="sr-only">retirado: </span>{kg(p)}
        </li>
      ))}
    </ul>
  )
}

function Linha({ rotulo, children }: { rotulo: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[5.5rem_1fr] gap-2 items-baseline">
      <dt className="text-sm text-gray-600">{rotulo}</dt>
      <dd className="text-sm text-gray-900 min-w-0">{children}</dd>
    </div>
  )
}

function Mudou({ antes, depois }: { antes: string; depois: string }) {
  if (antes === depois) return <>{depois}</>
  return <><span className="line-through text-gray-600">{antes}</span> → <b>{depois}</b></>
}

/** O que mudou, conforme a ação. */
function Detalhe({ item }: { item: HistoricoPortalItem }) {
  const antes = ler(item.antes)
  const depois = ler(item.depois)
  const marcas: string[] = []
  if (depois?.semLoteEmAberto) marcas.push('Sem lote em aberto: entrou no último lote, já fechado')
  if (depois?.depoisDoRomaneio) marcas.push('Alterada depois do romaneio')
  if (antes?.numero != null && antes.numero !== '') marcas.push(`Já estava no romaneio ${antes.numero}`)

  let corpo: React.ReactNode = null
  if ((item.acao === 'anotacao_editada' || item.acao === 'anotacao_editada_no_sistema') && antes && depois) {
    const pa = antes.pesos ?? [], pd = depois.pesos ?? []
    const { retirados, incluidos } = diferencaPesos(pa, pd)
    const mantidos = diferencaPesos(retirados, pa).incluidos   // pa sem os retirados
    corpo = (
      <dl className="space-y-1.5">
        <Linha rotulo="Data"><Mudou antes={antes.data ? dataBr(antes.data) : '?'} depois={depois.data ? dataBr(depois.data) : '?'} /></Linha>
        <Linha rotulo="Cliente"><Mudou antes={antes.cliente ?? '?'} depois={depois.cliente ?? '?'} /></Linha>
        <Linha rotulo="Pesos">
          {retirados.length === 0 && incluidos.length === 0
            ? <span className="text-gray-600">sem mudança</span>
            : <Pesos mantidos={mantidos} incluidos={incluidos} retirados={retirados} />}
        </Linha>
        <Linha rotulo="Total"><Mudou antes={totalDe(pa)} depois={totalDe(pd)} /></Linha>
      </dl>
    )
  } else if (item.acao === 'anotacao_criada' && depois) {
    const p = depois.pesos ?? []
    corpo = (
      <dl className="space-y-1.5">
        <Linha rotulo="Data">{depois.data ? dataBr(depois.data) : '?'}</Linha>
        <Linha rotulo="Cliente">{depois.cliente ?? '?'}</Linha>
        <Linha rotulo="Pesos"><Pesos incluidos={p} /></Linha>
        <Linha rotulo="Total"><b>{totalDe(p)}</b></Linha>
      </dl>
    )
  } else if ((item.acao === 'anotacao_excluida' || item.acao === 'anotacao_excluida_no_sistema') && antes) {
    const p = antes.pesos ?? []
    corpo = (
      <dl className="space-y-1.5">
        <Linha rotulo="Data">{antes.data ? dataBr(antes.data) : '?'}</Linha>
        <Linha rotulo="Cliente">{antes.cliente ?? '?'}</Linha>
        {antes.anotadaPor && <Linha rotulo="Anotada por">{antes.anotadaPor}</Linha>}
        <Linha rotulo="Pesos"><Pesos retirados={p} /></Linha>
        <Linha rotulo="Total"><span className="line-through">{totalDe(p)}</span></Linha>
      </dl>
    )
  } else if (item.acao === 'usuario_cadastrado' && depois?.email) {
    corpo = <dl><Linha rotulo="E-mail"><span className="break-all">{depois.email}</span></Linha></dl>
  } else if (item.acao === 'cliente_cadastrado' && depois) {
    corpo = (
      <dl className="space-y-1.5">
        <Linha rotulo="Cliente">{depois.cliente ?? '?'}</Linha>
        <Linha rotulo="Cidade">{depois.cidade || <span className="text-gray-600">não informada</span>}</Linha>
      </dl>
    )
  }

  if (!corpo && marcas.length === 0) return null
  return (
    <div className="mt-2 space-y-2">
      {marcas.length > 0 && (
        <ul className="flex flex-wrap gap-1.5">
          {marcas.map(m => (
            <li key={m} className="text-xs font-medium bg-gema-50 text-gema-900 border border-gema-200 rounded-full px-2 py-0.5">{m}</li>
          ))}
        </ul>
      )}
      {corpo}
    </div>
  )
}

export default function HistoricoPortalProdutosAPage() {
  const { usuario } = useAuth()
  const [pessoa, setPessoa] = useState('')
  const [acao, setAcao] = useState('')
  const [de, setDe] = useState('')
  const [ate, setAte] = useState('')
  const [pagina, setPagina] = useState(1)

  const [dados, setDados] = useState<HistoricoPortalPagina | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)

  const carregar = useCallback(async () => {
    setCarregando(true)
    try {
      const r = await portalProdutosADonoApi.historico({
        de: de || undefined, ate: ate || undefined,
        usuarioId: pessoa ? Number(pessoa) : undefined,
        acao: acao || undefined, pagina,
      })
      setDados(comoObjeto<HistoricoPortalPagina>(r.data, ['itens', 'pessoas']))
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally {
      setCarregando(false)
    }
  }, [de, ate, pessoa, acao, pagina])

  useEffect(() => { if (usuario?.dono) carregar() }, [carregar, usuario?.dono])

  // Filtro mudou: volta para a primeira página
  function filtrar(f: () => void) { f(); setPagina(1) }

  const voltar = (
    <Link to="/estoque/produtosA" className={`${classesBotao('fantasma', 'sm')} -ml-3`}>
      <ArrowLeft className="w-4 h-4" aria-hidden="true" /> ProdutosA
    </Link>
  )

  if (!usuario?.dono) {
    return (
      <div className="space-y-4 max-w-3xl">
        {voltar}
        <Aviso tipo="info" titulo="Só o dono vê o histórico do portal" />
      </div>
    )
  }

  // Cartões por dia, mais novo em cima (a API já manda nessa ordem)
  const dias: { dia: string; itens: HistoricoPortalItem[] }[] = []
  for (const it of dados?.itens ?? []) {
    const d = diaDe(it.quando)
    const grupo = dias[dias.length - 1]
    if (grupo?.dia === d) grupo.itens.push(it)
    else dias.push({ dia: d, itens: [it] })
  }
  const totalPaginas = dados ? Math.max(1, Math.ceil(dados.total / dados.porPagina)) : 1
  const temFiltro = !!(pessoa || acao || de || ate)

  return (
    <div className="space-y-5 max-w-3xl">
      {voltar}
      <CabecalhoTela descricao="Tudo o que foi feito no portal das produtosA pelo celular: quem, quando e o que mudou. Não dá para alterar nem apagar." />

      <div className="bg-white rounded-xl border border-gray-200 p-4 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        <Campo rotulo="Pessoa">
          <select value={pessoa} onChange={e => filtrar(() => setPessoa(e.target.value))} className={classeCampo}>
            <option value="">Todas</option>
            {(dados?.pessoas ?? []).map(p => <option key={p.id} value={p.id}>{p.nome}</option>)}
          </select>
        </Campo>
        <Campo rotulo="O que foi feito">
          <select value={acao} onChange={e => filtrar(() => setAcao(e.target.value))} className={classeCampo}>
            <option value="">Tudo</option>
            {Object.entries(ACOES).map(([v, l]) => <option key={v} value={v}>{l}</option>)}
          </select>
        </Campo>
        <Campo rotulo="De">
          <input type="date" value={de} onChange={e => filtrar(() => setDe(e.target.value))} className={classeCampo} />
        </Campo>
        <Campo rotulo="Até">
          <input type="date" value={ate} onChange={e => filtrar(() => setAte(e.target.value))} className={classeCampo} />
        </Campo>
        {temFiltro && (
          <div className="sm:col-span-2 lg:col-span-4">
            <Botao variante="fantasma" tamanho="sm" onClick={() => filtrar(() => { setPessoa(''); setAcao(''); setDe(''); setAte('') })}>
              Limpar filtros
            </Botao>
          </div>
        )}
      </div>

      {carregando && !dados ? (
        <Carregando linhas={4} rotulo="Carregando o histórico" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar o histórico." onTentar={carregar} />
      ) : !dados || dados.itens.length === 0 ? (
        <Vazio icone={History} titulo={temFiltro ? 'Nada com esses filtros' : 'Nada no histórico ainda'}>
          {temFiltro ? 'Mude ou limpe os filtros.' : 'O que for feito no portal aparece aqui.'}
        </Vazio>
      ) : (
        <div className={`space-y-5 ${carregando ? 'opacity-60' : ''}`} aria-busy={carregando}>
          {dias.map(g => (
            <section key={g.dia} aria-label={tituloDia(g.dia)}>
              <h2 className="text-sm font-semibold text-gray-700 mb-1.5 first-letter:uppercase">{tituloDia(g.dia)}</h2>
              <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100 overflow-hidden">
                {g.itens.map(it => (
                  <li key={it.id} className="px-4 sm:px-5 py-3.5">
                    <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                      <p className="text-sm text-gray-900 min-w-0">
                        <span className="tabular-nums text-gray-600">{horaDe(it.quando)}</span>
                        {' · '}<b>{it.usuarioNome || 'Sem nome'}</b>
                        {' · '}{it.resumo}
                      </p>
                      <span className="text-xs font-medium bg-tinta-50 text-tinta-800 border border-tinta-200 rounded-full px-2 py-0.5 shrink-0">
                        {ACOES[it.acao] ?? it.acao}
                      </span>
                    </div>
                    <Detalhe item={it} />
                  </li>
                ))}
              </ul>
            </section>
          ))}

          {totalPaginas > 1 && (
            <nav aria-label="Páginas" className="flex items-center justify-between gap-3">
              <Botao variante="secundario" disabled={pagina <= 1 || carregando} onClick={() => setPagina(p => p - 1)}>Mais novos</Botao>
              <span className="text-sm text-gray-600">Página {pagina} de {totalPaginas}</span>
              <Botao variante="secundario" disabled={pagina >= totalPaginas || carregando} onClick={() => setPagina(p => p + 1)}>Mais antigos</Botao>
            </nav>
          )}
        </div>
      )}
    </div>
  )
}
