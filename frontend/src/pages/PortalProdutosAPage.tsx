import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import axios from 'axios'
import toast from 'react-hot-toast'
import { ArrowLeft, Cylinder, LogOut, Pencil, Plus, Search, Trash2, TriangleAlert, X } from 'lucide-react'
import { useAuth } from '../contexts/AuthContext'
import { portalProdutosAApi, type AnotacaoPortal, type ClientePortal } from '../services/api'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Segmentado } from '../components/ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import ServidorAcordando from '../components/ui/ServidorAcordando'

// Portal das produtosA no celular (EST-09). Quem entra anota a retirada do barracão
// (data, cliente, pesos em gramas, EST-04) e vê/altera/exclui só as dele. Rapidez
// é o requisito: os pesos se digitam um atrás do outro, só com dígitos + Enter,
// sem fechar o teclado. Nada de preço, valor, carga ou custo aqui.

const PESO_MIN = 1000, PESO_MAX = 30000          // o que a API aceita
const PESO_COMUM_MIN = 8000, PESO_COMUM_MAX = 16000 // fora disso, pede confirmação (pega "1274" ou "127400")
const MAX_PRODUTOSA = 60
const DIAS_PARA_TRAS = 30

type Rascunho = {
  editandoId: number | null
  data: string
  clienteId: number | null
  clienteNome: string
  pesos: number[]
  digitando: string
}

// ── Formatos ────────────────────────────────────────────────────────────
const pad = (n: number) => String(n).padStart(2, '0')
const isoLocal = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const diasAtras = (n: number) => { const d = new Date(); d.setDate(d.getDate() - n); return isoLocal(d) }
const gramas = (g: number) => g.toLocaleString('pt-BR')
const kg = (g: number) => (g / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 3 })
const foraDoComum = (g: number) => g < PESO_COMUM_MIN || g > PESO_COMUM_MAX
const plural = (n: number) => `${n} ${n === 1 ? 'produtoA' : 'produtosA'}`

function dataCurta(iso: string) {
  if (iso === diasAtras(0)) return 'Hoje'
  if (iso === diasAtras(1)) return 'Ontem'
  const [a, m, d] = iso.split('-').map(Number)
  const sem = new Date(a, m - 1, d).toLocaleDateString('pt-BR', { weekday: 'short' }).replace('.', '')
  return `${sem}, ${pad(d)}/${pad(m)}/${a}`
}

function erroDa(err: unknown, padrao: string) {
  if (axios.isAxiosError(err)) {
    if (!err.response) return null // sem conexão: quem chama decide a mensagem
    return (err.response.data as { erro?: string } | undefined)?.erro ?? padrao
  }
  return padrao
}

// ── Rascunho no aparelho (localStorage pode faltar: try/catch) ──────────
const chaveRascunho = (usuarioId: number) => `trx_produtosA_rascunho_${usuarioId}`
function lerRascunho(chave: string): Rascunho | null {
  try {
    const r = JSON.parse(localStorage.getItem(chave) ?? 'null') as Rascunho | null
    if (!r || typeof r.data !== 'string' || !Array.isArray(r.pesos)) return null
    return { editandoId: r.editandoId ?? null, data: r.data, clienteId: r.clienteId ?? null,
             clienteNome: r.clienteNome ?? '', pesos: r.pesos.filter(p => Number.isInteger(p)), digitando: r.digitando ?? '' }
  } catch { return null }
}
const gravarRascunho = (chave: string, r: Rascunho) => { try { localStorage.setItem(chave, JSON.stringify(r)) } catch { /* sem armazenamento */ } }
const apagarRascunho = (chave: string) => { try { localStorage.removeItem(chave) } catch { /* sem armazenamento */ } }

const novo = (): Rascunho => ({ editandoId: null, data: diasAtras(0), clienteId: null, clienteNome: '', pesos: [], digitando: '' })

// ── Página ──────────────────────────────────────────────────────────────
export default function PortalProdutosAPage({ dentroDoSistema = false }: { dentroDoSistema?: boolean }) {
  const { usuario, logout } = useAuth()
  const navigate = useNavigate()
  const chave = chaveRascunho(usuario?.id ?? 0)
  // Recarregou no meio da anotação: volta direto para ela
  const [rascunho, setRascunho] = useState<Rascunho | null>(() => lerRascunho(chave))

  function sair() {
    navigate('/produtosA', { replace: true })
    logout()
  }

  return (
    <div className={dentroDoSistema ? '' : 'h-tela overflow-y-auto overflow-x-hidden bg-gray-50'}>
      {!dentroDoSistema && (
        <header className="sticky top-0 z-20 bg-menu text-white">
          <div className="max-w-lg mx-auto flex items-center gap-3 px-4 min-h-14">
            <div className="min-w-0 flex-1">
              <p className="font-display text-xl font-semibold leading-tight">ProdutosA</p>
              <p className="text-sm text-tinta-200 truncate">{usuario?.nome}</p>
            </div>
            {!rascunho && (
              <button type="button" onClick={sair}
                      className="inline-flex items-center gap-2 min-h-11 px-3 -mr-2 rounded-lg text-sm font-medium text-tinta-100 hover:bg-white/10 hover:text-white">
                <LogOut size={18} aria-hidden /> Sair
              </button>
            )}
          </div>
          <ServidorAcordando />
        </header>
      )}

      <div className={dentroDoSistema ? 'max-w-lg mx-auto' : 'max-w-lg mx-auto px-4 pt-4 pb-10'}>
        {dentroDoSistema && !rascunho && (
          <div className="flex items-center justify-between gap-3 mb-4">
            <Link to="/" className="inline-flex items-center gap-1.5 min-h-11 text-sm font-medium text-tinta-700 hover:text-tinta-900">
              <ArrowLeft size={18} aria-hidden /> Voltar ao sistema
            </Link>
            <span className="text-sm text-gray-600 truncate">Anotando como {usuario?.nome}</span>
          </div>
        )}
        {rascunho
          ? <Anotar key={rascunho.editandoId ?? 'novo'} inicial={rascunho} chave={chave} dentroDoSistema={dentroDoSistema}
                    onFechar={() => setRascunho(null)} />
          : <MinhasAnotacoes onNova={() => setRascunho(novo())}
                             onAlterar={a => setRascunho({ editandoId: a.id, data: a.data, clienteId: a.clienteId,
                                                           clienteNome: a.cliente, pesos: [...a.pesos], digitando: '' })} />}
      </div>
    </div>
  )
}

// ── Lista: minhas anotações ─────────────────────────────────────────────
function MinhasAnotacoes({ onNova, onAlterar }: { onNova: () => void; onAlterar: (a: AnotacaoPortal) => void }) {
  const confirmar = useConfirmar()
  const [lista, setLista] = useState<AnotacaoPortal[] | null>(null)
  const [erro, setErro] = useState(false)
  const [excluindo, setExcluindo] = useState<number | null>(null)

  const carregar = useCallback(() => {
    setErro(false); setLista(null)
    portalProdutosAApi.anotacoes()
      .then(r => setLista(Array.isArray(r.data) ? r.data : []))
      .catch(() => setErro(true))
  }, [])
  useEffect(carregar, [carregar])

  async function excluir(a: AnotacaoPortal) {
    const ok = await confirmar({
      titulo: 'Excluir esta anotação?',
      texto: `${a.cliente} · ${dataCurta(a.data)} · ${plural(a.quantidade)}, ${kg(a.pesos.reduce((s, p) => s + p, 0))} kg`,
      confirmar: 'Excluir',
    })
    if (!ok) return
    setExcluindo(a.id)
    try {
      await portalProdutosAApi.excluir(a.id)
      setLista(l => l?.filter(x => x.id !== a.id) ?? l)
      toast.success('Anotação excluída.')
    } catch (err) {
      toast.error(erroDa(err, 'Não deu para excluir.') ?? 'Sem conexão. Não excluiu; tente de novo.')
    } finally {
      setExcluindo(null)
    }
  }

  return (
    <div className="space-y-5">
      <Botao variante="primario" bloco icone={Plus} onClick={onNova} className="min-h-14 text-lg">
        Nova anotação
      </Botao>

      <section aria-labelledby="minhas-titulo">
        <h2 id="minhas-titulo" className="font-display text-lg font-semibold text-gray-900 mb-2">Minhas anotações</h2>
        {erro ? <ErroCarga mensagem="Não consegui carregar as suas anotações." onTentar={carregar} />
          : lista === null ? <Carregando linhas={3} rotulo="Carregando as anotações" />
          : lista.length === 0 ? (
            <Vazio icone={Cylinder} titulo="Nenhuma anotação ainda" compacto>
              As retiradas que você anotar aparecem aqui.
            </Vazio>
          ) : (
            <ul className="space-y-2">
              {lista.map(a => {
                const total = a.pesos.reduce((s, p) => s + p, 0)
                return (
                  <li key={a.id} className="bg-white border border-gray-200 rounded-xl p-3">
                    <div className="flex items-baseline justify-between gap-3">
                      <p className="font-semibold text-gray-900 truncate">{a.cliente || 'Cliente não informado'}</p>
                      <p className="text-sm text-gray-600 shrink-0">{dataCurta(a.data)}</p>
                    </div>
                    <p className="text-sm text-gray-700 mt-0.5 tabular-nums">{plural(a.quantidade)} · {kg(total)} kg</p>
                    {a.podeAlterar && (
                      <div className="flex gap-2 mt-3">
                        <Botao icone={Pencil} onClick={() => onAlterar(a)} className="flex-1">Alterar</Botao>
                        <Botao variante="perigoLeve" icone={Trash2} onClick={() => excluir(a)}
                               carregando={excluindo === a.id} className="flex-1">Excluir</Botao>
                      </div>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        {lista && lista.length > 0 && (
          <p className="text-xs text-gray-600 mt-3">Aparecem as anotações dos últimos 60 dias.</p>
        )}
      </section>
    </div>
  )
}

// ── Nova anotação / alterar ─────────────────────────────────────────────
function Anotar({ inicial, chave, dentroDoSistema, onFechar }: {
  inicial: Rascunho; chave: string; dentroDoSistema: boolean; onFechar: () => void
}) {
  const confirmar = useConfirmar()
  const [r, setR] = useState<Rascunho>(inicial)
  const [outraData, setOutraData] = useState(() => inicial.data !== diasAtras(0) && inicial.data !== diasAtras(1))
  const [aConfirmar, setAConfirmar] = useState<number | null>(null)   // peso fora do comum esperando o 2º Enter
  const [erroPeso, setErroPeso] = useState<string | null>(null)
  const [corrigindo, setCorrigindo] = useState<number | null>(null)  // índice da produtoA em correção
  const [salvando, setSalvando] = useState(false)
  const [erroSalvar, setErroSalvar] = useState<{ texto: string; rede: boolean } | null>(null)
  const pesosRef = useRef<HTMLInputElement>(null)

  // Cada mudança fica no aparelho até salvar
  useEffect(() => { gravarRascunho(chave, r) }, [chave, r])

  const total = r.pesos.reduce((s, p) => s + p, 0)
  const hoje = diasAtras(0), ontem = diasAtras(1), limite = diasAtras(DIAS_PARA_TRAS)
  const escolhaData = outraData ? 'outra' : r.data === hoje ? 'hoje' : r.data === ontem ? 'ontem' : 'outra'

  function mudar(p: Partial<Rascunho>) { setR(x => ({ ...x, ...p })); setErroSalvar(null) }

  function escolherCliente(c: ClientePortal) {
    mudar({ clienteId: c.id, clienteNome: c.nome })
    // Foco direto nos pesos, ainda dentro do toque (o celular só abre o teclado assim)
    pesosRef.current?.focus()
  }

  function digitar(v: string) {
    mudar({ digitando: v.replace(/\D/g, '').slice(0, 6) })
    setErroPeso(null); setAConfirmar(null)
  }

  function confirmarPeso() {
    const texto = r.digitando
    if (!texto) return
    const g = parseInt(texto, 10)
    if (g < PESO_MIN || g > PESO_MAX) { setErroPeso('Peso em gramas, entre 1.000 e 30.000 (ex.: 12740).'); return }
    if (corrigindo === null && r.pesos.length >= MAX_PRODUTOSA) { setErroPeso(`No máximo ${MAX_PRODUTOSA} produtosA por anotação.`); return }
    if (foraDoComum(g) && aConfirmar !== g) { setAConfirmar(g); return }
    const pesos = corrigindo === null ? [...r.pesos, g] : r.pesos.map((p, i) => (i === corrigindo ? g : p))
    mudar({ pesos, digitando: '' })
    setAConfirmar(null); setErroPeso(null); setCorrigindo(null)
  }

  function corrigir(i: number) {
    setCorrigindo(i); setAConfirmar(null); setErroPeso(null)
    mudar({ digitando: String(r.pesos[i]) })
    pesosRef.current?.focus()
  }

  function tirar(i: number) {
    mudar({ pesos: r.pesos.filter((_, j) => j !== i) })
    if (corrigindo === i) { setCorrigindo(null); mudar({ digitando: '' }) }
    else if (corrigindo !== null && corrigindo > i) setCorrigindo(corrigindo - 1)
  }

  async function cancelar() {
    const mexeu = r.pesos.length > 0 || r.digitando !== '' || (r.editandoId === null && r.clienteId !== null)
    if (mexeu && !(await confirmar({
      titulo: r.editandoId ? 'Sair sem salvar a alteração?' : 'Descartar esta anotação?',
      texto: r.editandoId ? 'A anotação fica como estava.' : 'Os pesos digitados se perdem.',
      confirmar: r.editandoId ? 'Sair sem salvar' : 'Descartar',
    }))) return
    apagarRascunho(chave)
    onFechar()
  }

  async function salvar() {
    if (salvando) return
    if (!r.clienteId) { setErroSalvar({ texto: 'Escolha o cliente.', rede: false }); return }
    if (r.digitando) { setErroSalvar({ texto: 'Tem um peso digitado sem Enter. Aperte Enter para incluir ou apague.', rede: false }); pesosRef.current?.focus(); return }
    if (r.pesos.length === 0) { setErroSalvar({ texto: 'Digite o peso de pelo menos uma produtoA.', rede: false }); pesosRef.current?.focus(); return }
    setSalvando(true); setErroSalvar(null)
    const dados = { data: r.data, clienteId: r.clienteId, pesos: r.pesos }
    try {
      if (r.editandoId) await portalProdutosAApi.alterar(r.editandoId, dados)
      else await portalProdutosAApi.anotar(dados)
      apagarRascunho(chave)
      toast.success(`${r.editandoId ? 'Alterado' : 'Anotado'}: ${plural(r.pesos.length)}, ${kg(total)} kg para ${r.clienteNome}`, { duration: 5000 })
      onFechar()
    } catch (err) {
      const texto = erroDa(err, 'Não salvou.')
      setErroSalvar(texto === null ? { texto: 'Não salvou: sem conexão. Está tudo guardado no aparelho.', rede: true } : { texto, rede: false })
      setSalvando(false)
    }
  }

  return (
    <div className="pb-28">
      <div className="flex items-center justify-between gap-3 mb-4">
        <h2 className="font-display text-xl font-semibold text-gray-900">{r.editandoId ? 'Alterar anotação' : 'Nova anotação'}</h2>
        <Botao variante="fantasma" icone={X} onClick={cancelar} className="-mr-2">Cancelar</Botao>
      </div>

      <div className="space-y-6">
        {/* 1. Data */}
        <section aria-labelledby="anotar-data">
          <h3 id="anotar-data" className="text-sm font-semibold text-gray-800 mb-2">Data</h3>
          <Segmentado valor={escolhaData} rotulo="Data da retirada"
                      opcoes={[['hoje', 'Hoje'], ['ontem', 'Ontem'], ['outra', 'Outro dia']] as const}
                      onChange={v => {
                        if (v === 'hoje') { setOutraData(false); mudar({ data: hoje }) }
                        else if (v === 'ontem') { setOutraData(false); mudar({ data: ontem }) }
                        else setOutraData(true)
                      }} />
          {escolhaData === 'outra' && (
            <div className="mt-2">
              <label htmlFor="anotar-outra-data" className="sr-only">Dia da retirada</label>
              <input id="anotar-outra-data" type="date" value={r.data} min={limite} max={hoje}
                     onChange={e => e.target.value && mudar({ data: e.target.value })}
                     className="appearance-none w-full min-h-12 rounded-lg border border-gray-300 bg-white px-3 text-base text-gray-900 focus:outline-none focus:ring-2 focus:ring-tinta-500" />
              <p className="text-xs text-gray-600 mt-1">Até {DIAS_PARA_TRAS} dias atrás.</p>
            </div>
          )}
        </section>

        {/* 2. Cliente */}
        <EscolhaCliente clienteId={r.clienteId} clienteNome={r.clienteNome} onEscolher={escolherCliente}
                        onTrocar={() => mudar({ clienteId: null, clienteNome: '' })} />

        {/* 3. Pesos */}
        <section aria-labelledby="anotar-pesos">
          <h3 id="anotar-pesos" className="text-sm font-semibold text-gray-800 mb-2">
            {corrigindo === null ? 'Peso de cada produtoA, em gramas' : `Corrigindo a produtoA ${corrigindo + 1}`}
          </h3>
          <input
            ref={pesosRef}
            type="text" inputMode="numeric" pattern="[0-9]*" enterKeyHint="next" autoComplete="off"
            aria-labelledby="anotar-pesos" aria-describedby="anotar-pesos-dica"
            aria-invalid={erroPeso ? true : undefined}
            placeholder="Ex.: 12740"
            value={r.digitando}
            onChange={e => digitar(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); confirmarPeso() } }}
            className={`w-full min-h-14 rounded-lg border bg-white px-3 text-2xl font-semibold tabular-nums text-gray-900 focus:outline-none focus:ring-2 focus:ring-tinta-500
                        ${erroPeso || aConfirmar !== null ? 'border-amber-500' : 'border-gray-300'}`}
          />
          <div id="anotar-pesos-dica" aria-live="polite" className="mt-1 min-h-5 text-sm">
            {erroPeso ? <span className="text-red-700">{erroPeso}</span>
              : aConfirmar !== null ? (
                <span className="text-amber-800 font-medium">
                  {gramas(aConfirmar)} g ({kg(aConfirmar)} kg) está fora do comum. Confira e aperte Enter de novo para incluir.
                </span>
              ) : r.digitando ? <span className="text-gray-700 tabular-nums">{gramas(+r.digitando)} g · {kg(+r.digitando)} kg — aperte Enter</span>
              : <span className="text-gray-600">Digite o peso e aperte Enter (ou Próximo). O campo limpa para a próxima.</span>}
          </div>

          {r.pesos.length > 0 && (
            <ol className="mt-3 divide-y divide-gray-200 border border-gray-200 rounded-xl bg-white overflow-hidden">
              {r.pesos.map((p, i) => (
                <li key={i} className={`flex items-center ${corrigindo === i ? 'bg-tinta-50' : ''}`}>
                  <button type="button" onClick={() => corrigir(i)}
                          aria-label={`Corrigir a produtoA ${i + 1}, ${gramas(p)} gramas`}
                          className="flex-1 min-w-0 flex items-center gap-3 px-3 min-h-12 text-left hover:bg-gray-50">
                    <span className="w-7 text-sm text-gray-600 tabular-nums text-right shrink-0">{i + 1}</span>
                    <span className="min-w-0">
                      <span className="block text-base font-semibold text-gray-900 tabular-nums">{gramas(p)} g · {kg(p)} kg</span>
                      {foraDoComum(p) && (
                        <span className="flex items-center gap-1 text-xs text-amber-800">
                          <TriangleAlert size={14} aria-hidden /> Fora de 8.000 a 16.000 g
                        </span>
                      )}
                    </span>
                  </button>
                  <BotaoIcone icone={X} rotulo={`Tirar a produtoA ${i + 1}`} onClick={() => tirar(i)} className="mr-1" />
                </li>
              ))}
            </ol>
          )}
        </section>

        {erroSalvar && (
          <Aviso tipo="erro" acao={erroSalvar.rede ? <Botao tamanho="sm" onClick={salvar}>Tentar de novo</Botao> : undefined}>
            {erroSalvar.texto}
          </Aviso>
        )}
      </div>

      {/* Rodapé fixo: total ao vivo e o Salvar sempre à mão */}
      <div className={`fixed bottom-0 inset-x-0 ${dentroDoSistema ? 'lg:left-64' : ''} bg-white/95 backdrop-blur border-t border-gray-200 px-4 pt-3 pb-seguro z-20 shadow-[0_-4px_12px_-6px_rgb(0_0_0/0.12)]`}>
        <div className="max-w-lg mx-auto flex items-center gap-3">
          <p className="flex-1 min-w-0 text-base font-semibold text-gray-900 tabular-nums" aria-live="polite">
            {plural(r.pesos.length)} · {kg(total)} kg
          </p>
          <Botao variante="primario" onClick={salvar} carregando={salvando} textoCarregando="Salvando..."
                 className="min-h-12 px-6 text-base">
            Salvar
          </Botao>
        </div>
      </div>
    </div>
  )
}

// ── Cliente: os mais usados em cima, busca e cadastro rápido ────────────
function EscolhaCliente({ clienteId, clienteNome, onEscolher, onTrocar }: {
  clienteId: number | null; clienteNome: string; onEscolher: (c: ClientePortal) => void; onTrocar: () => void
}) {
  const [clientes, setClientes] = useState<ClientePortal[] | null>(null)
  const [erro, setErro] = useState(false)
  const [busca, setBusca] = useState('')
  const [novoAberto, setNovoAberto] = useState(false)

  const carregar = useCallback(() => {
    setErro(false)
    portalProdutosAApi.clientes()
      .then(r => setClientes(Array.isArray(r.data) ? r.data : []))
      .catch(() => setErro(true))
  }, [])
  useEffect(carregar, [carregar])

  const semAcento = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
  const filtrados = useMemo(() => {
    if (!clientes) return []
    const b = semAcento(busca.trim())
    return b ? clientes.filter(c => semAcento(c.nome).includes(b)).slice(0, 30) : clientes.slice(0, 6)
  }, [clientes, busca])

  if (clienteId) return (
    <section aria-labelledby="anotar-cliente">
      <h3 id="anotar-cliente" className="text-sm font-semibold text-gray-800 mb-2">Cliente</h3>
      <div className="flex items-center gap-3 bg-white border border-gray-300 rounded-lg pl-3 pr-1 min-h-12">
        <span className="flex-1 min-w-0 font-semibold text-gray-900 truncate">{clienteNome || 'Cliente sem nome'}</span>
        <Botao variante="fantasma" tamanho="sm" onClick={() => { setBusca(''); onTrocar() }}>Trocar</Botao>
      </div>
    </section>
  )

  return (
    <section aria-labelledby="anotar-cliente">
      <h3 id="anotar-cliente" className="text-sm font-semibold text-gray-800 mb-2">Cliente</h3>
      {novoAberto
        ? <ClienteNovo nomeInicial={busca} onCriado={c => { setNovoAberto(false); setClientes(l => l && !l.some(x => x.id === c.id) ? [c, ...l] : l); onEscolher(c) }}
                       onCancelar={() => setNovoAberto(false)} />
        : (
          <>
            <div className="relative">
              <Search size={18} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
              <input type="search" value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar cliente"
                     aria-label="Buscar cliente" autoComplete="off"
                     className="w-full min-h-12 rounded-lg border border-gray-300 bg-white pl-10 pr-3 text-base text-gray-900 focus:outline-none focus:ring-2 focus:ring-tinta-500" />
            </div>
            <div className="mt-2">
              {erro ? <ErroCarga mensagem="Não consegui carregar os clientes." onTentar={carregar} />
                : clientes === null ? <Carregando linhas={3} rotulo="Carregando os clientes" />
                : filtrados.length === 0 ? (
                  <p className="text-sm text-gray-600 py-2">{busca ? 'Nenhum cliente com esse nome.' : 'Nenhum cliente cadastrado.'}</p>
                ) : (
                  <ul className="grid grid-cols-1 gap-2">
                    {filtrados.map(c => (
                      <li key={c.id}>
                        <button type="button" onClick={() => onEscolher(c)}
                                className="w-full min-h-12 px-3 rounded-lg border border-gray-200 bg-white text-left text-base font-medium text-gray-900 hover:border-tinta-500 hover:bg-tinta-50">
                          {c.nome}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              {!busca && clientes && clientes.length > 6 && (
                <p className="text-xs text-gray-600 mt-2">Os que você mais usa ficam em cima. Busque para ver os outros.</p>
              )}
            </div>
            <Botao variante="secundario" bloco icone={Plus} onClick={() => setNovoAberto(true)} className="mt-3">
              Cadastrar cliente novo
            </Botao>
          </>
        )}
    </section>
  )
}

function ClienteNovo({ nomeInicial, onCriado, onCancelar }: {
  nomeInicial: string; onCriado: (c: ClientePortal) => void; onCancelar: () => void
}) {
  const [nome, setNome] = useState(nomeInicial)
  const [cidade, setCidade] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function criar() {
    if (nome.trim().length < 2) { setErro('Escreva o nome do cliente.'); return }
    setSalvando(true); setErro(null)
    try {
      const r = await portalProdutosAApi.criarCliente({ nome: nome.trim(), cidade: cidade.trim() || null })
      if (r.data.jaExistia) toast(`${r.data.nome} já estava cadastrado. Escolhido.`)
      onCriado({ id: r.data.id, nome: r.data.nome })
    } catch (err) {
      setErro(erroDa(err, 'Não deu para cadastrar.') ?? 'Sem conexão. Tente de novo.')
      setSalvando(false)
    }
  }

  const campo = 'w-full min-h-12 rounded-lg border border-gray-300 bg-white px-3 text-base text-gray-900 focus:outline-none focus:ring-2 focus:ring-tinta-500'
  return (
    <div className="bg-white border border-gray-200 rounded-xl p-3 space-y-3">
      <p className="text-sm font-semibold text-gray-900">Cliente novo</p>
      <div>
        <label htmlFor="cliente-novo-nome" className="block text-sm font-medium text-gray-700 mb-1">Nome</label>
        <input id="cliente-novo-nome" type="text" value={nome} onChange={e => setNome(e.target.value)} autoCapitalize="characters"
               autoFocus className={campo} />
      </div>
      <div>
        <label htmlFor="cliente-novo-cidade" className="block text-sm font-medium text-gray-700 mb-1">Cidade (se souber)</label>
        <input id="cliente-novo-cidade" type="text" value={cidade} onChange={e => setCidade(e.target.value)} autoCapitalize="characters"
               onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); criar() } }} className={campo} />
      </div>
      {erro && <Aviso tipo="erro">{erro}</Aviso>}
      <div className="flex gap-2">
        <Botao onClick={onCancelar} className="flex-1">Voltar</Botao>
        <Botao variante="primario" onClick={criar} carregando={salvando} textoCarregando="Cadastrando..." className="flex-1">
          Cadastrar
        </Botao>
      </div>
    </div>
  )
}
