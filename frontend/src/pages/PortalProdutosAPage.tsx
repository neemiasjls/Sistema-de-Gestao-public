import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import axios from 'axios'
import toast from 'react-hot-toast'
import { ArrowLeft, CalendarDays, Check, ChevronRight, Cylinder, LogOut, Pencil, Plus, Search, Trash2, TriangleAlert, UserPlus, X } from 'lucide-react'
import { useAuth } from '../contexts/AuthContext'
import { portalProdutosAApi, type AnotacaoPortal, type ClientePortal } from '../services/api'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import ServidorAcordando from '../components/ui/ServidorAcordando'

// Portal das produtosA no celular (EST-09). Quem entra anota a retirada do barracão
// (data, cliente, pesos em gramas, EST-04) e vê/altera/exclui só as dele. Rapidez
// é o requisito: os pesos se digitam um atrás do outro, só com dígitos + o botão
// Adicionar (o teclado numérico do celular não tem Enter), sem fechar o teclado (EST-11).
// Nada de preço, valor, carga ou custo aqui.

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
  // Rascunho ainda sem cliente nem pesos abre com a data de hoje
  const [rascunho, setRascunho] = useState<Rascunho | null>(() => {
    const r = lerRascunho(chave)
    return r && r.editandoId === null && r.clienteId === null && r.pesos.length === 0 ? { ...r, data: diasAtras(0) } : r
  })

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
                  <li key={a.id} className="bg-white border border-gray-200 rounded-2xl p-3.5 shadow-sm">
                    <div className="flex items-center gap-3">
                      <Iniciais nome={a.cliente || '?'} />
                      <div className="flex-1 min-w-0">
                        <p className="font-semibold text-gray-900 truncate">{a.cliente || 'Cliente não informado'}</p>
                        <p className="text-sm text-gray-600">{dataCurta(a.data)} · {plural(a.quantidade)}</p>
                      </div>
                      <p className="font-display text-xl font-semibold text-gray-900 tabular-nums shrink-0">{kg(total)} <span className="text-sm font-normal text-gray-600">kg</span></p>
                    </div>
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
  const [aConfirmar, setAConfirmar] = useState<number | null>(null)   // peso fora do comum esperando o 2º toque
  const [novaId, setNovaId] = useState<number | null>(null)           // índice da última incluída (pisca de leve)
  const [erroPeso, setErroPeso] = useState<string | null>(null)
  const [corrigindo, setCorrigindo] = useState<number | null>(null)  // índice da produtoA em correção
  const [salvando, setSalvando] = useState(false)
  const [erroSalvar, setErroSalvar] = useState<{ texto: string; rede: boolean } | null>(null)
  const pesosRef = useRef<HTMLInputElement>(null)

  // Cada mudança fica no aparelho até salvar
  useEffect(() => { gravarRascunho(chave, r) }, [chave, r])

  const total = r.pesos.reduce((s, p) => s + p, 0)
  const hoje = diasAtras(0), limite = diasAtras(DIAS_PARA_TRAS)

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

  // Devolve os pesos com o digitado incluído, ou null se não incluiu (vazio, inválido ou pedindo confirmação)
  function confirmarPeso(): number[] | null {
    const texto = r.digitando
    if (!texto) return null
    const g = parseInt(texto, 10)
    if (g < PESO_MIN || g > PESO_MAX) { setErroPeso('Peso em gramas, entre 1.000 e 30.000 (ex.: 12740).'); return null }
    if (corrigindo === null && r.pesos.length >= MAX_PRODUTOSA) { setErroPeso(`No máximo ${MAX_PRODUTOSA} produtosA por anotação.`); return null }
    if (foraDoComum(g) && aConfirmar !== g) { setAConfirmar(g); return null }
    const pesos = corrigindo === null ? [...r.pesos, g] : r.pesos.map((p, i) => (i === corrigindo ? g : p))
    mudar({ pesos, digitando: '' })
    setNovaId(corrigindo ?? pesos.length - 1)
    setAConfirmar(null); setErroPeso(null); setCorrigindo(null)
    try { navigator.vibrate?.(30) } catch { /* aparelho sem vibração */ }
    return pesos
  }

  // Botão "+ Adicionar": inclui e devolve o foco ao campo no mesmo toque (o teclado numérico não fecha)
  function adicionarPeloBotao() {
    confirmarPeso()
    pesosRef.current?.focus()
  }

  function corrigir(i: number) {
    setCorrigindo(i); setAConfirmar(null); setErroPeso(null)
    mudar({ digitando: String(r.pesos[i]) })
    pesosRef.current?.focus()
  }

  function tirar(i: number) {
    mudar({ pesos: r.pesos.filter((_, j) => j !== i) })
    setNovaId(null)
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
    let pesosFinais = r.pesos
    if (r.digitando) {
      // Peso ainda no campo: entra sozinho se for válido e comum; senão pede para conferir
      const g = parseInt(r.digitando, 10)
      const incluido = g >= PESO_COMUM_MIN && g <= PESO_COMUM_MAX ? confirmarPeso() : null
      if (!incluido) { setErroSalvar({ texto: 'Confira o peso digitado: toque em Adicionar ou apague.', rede: false }); pesosRef.current?.focus(); return }
      pesosFinais = incluido
    }
    if (pesosFinais.length === 0) { setErroSalvar({ texto: 'Digite o peso de pelo menos uma produtoA.', rede: false }); pesosRef.current?.focus(); return }
    setSalvando(true); setErroSalvar(null)
    const dados = { data: r.data, clienteId: r.clienteId, pesos: pesosFinais }
    const totalFinal = pesosFinais.reduce((s, p) => s + p, 0)
    try {
      if (r.editandoId) await portalProdutosAApi.alterar(r.editandoId, dados)
      else await portalProdutosAApi.anotar(dados)
      apagarRascunho(chave)
      toast.success(`${r.editandoId ? 'Alterado' : 'Anotado'}: ${plural(pesosFinais.length)}, ${kg(totalFinal)} kg para ${r.clienteNome}`, { duration: 5000 })
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
        <div className="min-w-0">
          <h2 className="font-display text-2xl font-semibold text-gray-900 leading-tight">{r.editandoId ? 'Alterar anotação' : 'Nova anotação'}</h2>
          <p className="text-sm text-gray-600">Retirada de produtosA no barracão</p>
        </div>
        <Botao variante="fantasma" icone={X} onClick={cancelar} className="-mr-2 shrink-0">Cancelar</Botao>
      </div>

      <div className="space-y-3">
        {/* 1. Data */}
        <Etapa n={1} id="anotar-data" titulo="Data da retirada" feito
               resumo={<span className="inline-flex items-center gap-1.5 rounded-full bg-tinta-50 px-2.5 py-0.5 text-sm font-medium text-tinta-700">{dataCurta(r.data)}</span>}>
          <div className="relative">
            <CalendarDays size={18} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
            <input id="anotar-data-campo" type="date" value={r.data} min={limite} max={hoje} aria-labelledby="anotar-data"
                   onChange={e => mudar({ data: e.target.value && e.target.value >= limite && e.target.value <= hoje ? e.target.value : hoje })}
                   className={`${CAMPO} pl-10`} />
          </div>
          <p className="text-xs text-gray-600 mt-1.5">Toque para escolher outro dia (até {DIAS_PARA_TRAS} dias atrás).</p>
        </Etapa>

        {/* 2. Cliente */}
        <EscolhaCliente clienteId={r.clienteId} clienteNome={r.clienteNome} onEscolher={escolherCliente}
                        onTrocar={() => mudar({ clienteId: null, clienteNome: '' })} />

        {/* 3. Pesos */}
        <Etapa n={3} id="anotar-pesos" feito={r.pesos.length > 0}
               titulo={corrigindo === null ? 'Peso de cada produtoA' : `Corrigindo a produtoA ${corrigindo + 1}`}
               resumo={<span className="text-sm text-gray-600">em gramas</span>}>
          <div className="flex gap-2">
            <input
              ref={pesosRef}
              type="text" inputMode="numeric" pattern="[0-9]*" enterKeyHint="done" autoComplete="off"
              aria-labelledby="anotar-pesos" aria-describedby="anotar-pesos-dica"
              aria-invalid={erroPeso ? true : undefined}
              placeholder="Ex.: 12740"
              value={r.digitando}
              onChange={e => digitar(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); confirmarPeso() } }}
              className={`flex-1 min-w-0 min-h-14 rounded-xl border bg-white px-3 text-2xl font-semibold tabular-nums text-gray-900 placeholder:text-gray-400 placeholder:font-normal focus:outline-none focus:ring-2 focus:ring-tinta-500
                          ${erroPeso || aConfirmar !== null ? 'border-amber-500' : 'border-gray-300'}`}
            />
            {/* pointerdown sem efeito padrão: o toque não tira o foco do campo e o teclado numérico fica aberto */}
            <button type="button" onPointerDown={e => e.preventDefault()} onMouseDown={e => e.preventDefault()}
                    onClick={adicionarPeloBotao} disabled={!r.digitando}
                    className={`shrink-0 min-h-14 min-w-28 px-3 rounded-xl shadow-sm active:scale-[.97] transition-transform text-base font-semibold text-white inline-flex items-center justify-center gap-1.5
                                disabled:opacity-60 focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-tinta-500
                                ${aConfirmar !== null ? 'bg-amber-600 hover:bg-amber-700' : 'bg-tinta-700 hover:bg-tinta-800'}`}>
              {aConfirmar !== null ? 'Incluir assim mesmo' : corrigindo !== null ? 'Trocar' : <><Plus size={20} aria-hidden /> Adicionar</>}
            </button>
          </div>
          <div id="anotar-pesos-dica" className="mt-1 min-h-5 text-sm">
            {erroPeso ? <span className="text-red-700">{erroPeso}</span>
              : aConfirmar !== null ? (
                <span className="text-amber-800 font-medium">
                  {gramas(aConfirmar)} g ({kg(aConfirmar)} kg) está fora do comum. Confira e toque em Incluir assim mesmo.
                </span>
              ) : r.digitando ? <span className="text-gray-700 tabular-nums">{gramas(+r.digitando)} g · {kg(+r.digitando)} kg</span>
              : <span className="text-gray-600">Digite o peso e toque em Adicionar. O campo limpa para a próxima.</span>}
          </div>
          <p className="sr-only" aria-live="polite">
            {novaId !== null && r.pesos[novaId] !== undefined ? `ProdutoA ${novaId + 1}: ${gramas(r.pesos[novaId])} gramas` : ''}
          </p>

          {r.pesos.length > 0 && (
            <div className="mt-3 flex items-end justify-between gap-3 rounded-xl bg-tinta-50 px-3 py-2.5">
              <div>
                <p className="text-xs font-medium text-tinta-700">Total anotado</p>
                <p className="font-display text-2xl font-semibold text-gray-900 tabular-nums leading-tight">{kg(total)} kg</p>
              </div>
              <p className="text-sm font-medium text-gray-700 tabular-nums pb-0.5">{plural(r.pesos.length)}</p>
            </div>
          )}
          {r.pesos.length > 0 && (
            <ol className="mt-2 divide-y divide-gray-200 border border-gray-200 rounded-xl bg-white overflow-hidden">
              {r.pesos.map((p, i) => ({ p, i })).reverse().map(({ p, i }) => (
                <li key={i} className={`flex items-center ${corrigindo === i ? 'bg-tinta-50' : novaId === i ? 'animate-entrada' : ''}`}>
                  <button type="button" onClick={() => corrigir(i)}
                          aria-label={`Corrigir a produtoA ${i + 1}, ${gramas(p)} gramas`}
                          className="flex-1 min-w-0 flex items-center gap-3 px-3 min-h-14 text-left hover:bg-gray-50">
                    <span className="grid place-items-center size-8 shrink-0 rounded-full bg-gray-100 text-sm font-semibold text-gray-700 tabular-nums">{i + 1}</span>
                    <span className="min-w-0 flex-1">
                      <span className="block text-base font-semibold text-gray-900 tabular-nums">{gramas(p)} g
                        <span className="ml-1.5 text-sm font-normal text-gray-600">{kg(p)} kg</span>
                      </span>
                      {foraDoComum(p) && (
                        <span className="flex items-center gap-1 text-xs text-amber-800">
                          <TriangleAlert size={14} aria-hidden /> Fora de 8.000 a 16.000 g
                        </span>
                      )}
                    </span>
                    <Pencil size={15} className="text-gray-400 shrink-0" aria-hidden />
                  </button>
                  <BotaoIcone icone={X} rotulo={`Tirar a produtoA ${i + 1}`} onClick={() => tirar(i)} className="mr-1" />
                </li>
              ))}
            </ol>
          )}
        </Etapa>

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
    // EST-11: a lista só aparece depois de digitar
    return b ? clientes.filter(c => semAcento(c.nome).includes(b)).slice(0, 30) : []
  }, [clientes, busca])
  const [tocou, setTocou] = useState(false)

  const trocar = () => { setBusca(''); onTrocar() }

  if (clienteId) return (
    <Etapa n={2} id="anotar-cliente" titulo="Cliente" feito>
      <div className="flex items-center gap-3 rounded-xl border border-tinta-200 bg-tinta-50 pl-2.5 pr-1 min-h-14">
        <Iniciais nome={clienteNome} destaque />
        <span className="flex-1 min-w-0 text-base font-semibold text-gray-900 truncate">{clienteNome || 'Cliente sem nome'}</span>
        <Botao variante="fantasma" tamanho="sm" onClick={trocar}>Trocar</Botao>
      </div>
    </Etapa>
  )

  const termo = busca.trim()
  return (
    <Etapa n={2} id="anotar-cliente" titulo="Cliente">
      {novoAberto
        ? <ClienteNovo nomeInicial={busca} onCriado={c => { setNovoAberto(false); setClientes(l => l && !l.some(x => x.id === c.id) ? [c, ...l] : l); onEscolher(c) }}
                       onCancelar={() => setNovoAberto(false)} />
        : (
          <>
            <div className="relative">
              <Search size={18} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
              <input type="search" value={busca} onChange={e => setBusca(e.target.value)} onFocus={() => setTocou(true)}
                     placeholder="Toque e digite o nome"
                     aria-label="Buscar cliente" aria-controls="anotar-cliente-lista" autoComplete="off"
                     className={`${CAMPO} pl-10`} />
            </div>
            <div id="anotar-cliente-lista" aria-live="polite" className="mt-2">
              {erro ? <ErroCarga mensagem="Não consegui carregar os clientes." onTentar={carregar} />
                : !termo ? (tocou && (
                  <p className="flex items-center gap-2 text-sm text-gray-600 px-1 py-1.5">
                    <Search size={15} className="text-gray-400 shrink-0" aria-hidden /> Digite uma letra do nome para ver os clientes.
                  </p>
                ))
                : clientes === null ? <Carregando linhas={3} rotulo="Carregando os clientes" />
                : (
                  <div className="rounded-xl border border-gray-200 bg-white overflow-hidden animate-entrada">
                    <p className="px-3 pt-2.5 pb-1.5 text-xs font-medium text-gray-600">
                      {filtrados.length === 0 ? 'Nenhum cliente com esse nome'
                        : filtrados.length === 1 ? '1 cliente encontrado' : `${filtrados.length} clientes encontrados`}
                    </p>
                    {filtrados.length > 0 && (
                      <ul className="divide-y divide-gray-100 max-h-80 overflow-y-auto">
                        {filtrados.map(c => (
                          <li key={c.id}>
                            <button type="button" onClick={() => onEscolher(c)}
                                    className="w-full min-h-14 flex items-center gap-3 px-3 text-left hover:bg-tinta-50 active:bg-tinta-100 focus:outline-none focus-visible:bg-tinta-50">
                              <Iniciais nome={c.nome} />
                              <span className="flex-1 min-w-0 text-base text-gray-900 truncate"><Destacado texto={c.nome} termo={termo} /></span>
                              <ChevronRight size={18} className="text-gray-400 shrink-0" aria-hidden />
                            </button>
                          </li>
                        ))}
                      </ul>
                    )}
                    <button type="button" onClick={() => setNovoAberto(true)}
                            className="w-full min-h-12 flex items-center gap-3 px-3 border-t border-gray-100 text-left text-sm font-medium text-tinta-700 hover:bg-tinta-50">
                      <span className="grid place-items-center size-9 shrink-0 rounded-full border border-dashed border-tinta-300"><UserPlus size={17} aria-hidden /></span>
                      <span className="min-w-0 truncate">Cadastrar “{termo}” como cliente novo</span>
                    </button>
                  </div>
                )}
            </div>
            {!termo && (
              <button type="button" onClick={() => setNovoAberto(true)}
                      className="mt-2 inline-flex items-center gap-1.5 min-h-11 px-1 text-sm font-medium text-tinta-700 hover:text-tinta-900">
                <UserPlus size={17} aria-hidden /> Cadastrar cliente novo
              </button>
            )}
          </>
        )}
    </Etapa>
  )
}

function ClienteNovo({ nomeInicial, onCriado, onCancelar }: {
  nomeInicial: string; onCriado: (c: ClientePortal) => void; onCancelar: () => void
}) {
  const [nome, setNome] = useState(nomeInicial)
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  async function criar() {
    if (nome.trim().length < 2) { setErro('Escreva o nome do cliente.'); return }
    setSalvando(true); setErro(null)
    try {
      const r = await portalProdutosAApi.criarCliente({ nome: nome.trim(), cidade: null })
      if (r.data.jaExistia) toast(`${r.data.nome} já estava cadastrado. Escolhido.`)
      onCriado({ id: r.data.id, nome: r.data.nome })
    } catch (err) {
      setErro(erroDa(err, 'Não deu para cadastrar.') ?? 'Sem conexão. Tente de novo.')
      setSalvando(false)
    }
  }

  return (
    <div className="rounded-xl border border-tinta-200 bg-tinta-50 p-3 space-y-3 animate-entrada">
      <div className="flex items-center gap-2.5">
        <span className="grid place-items-center size-9 shrink-0 rounded-full bg-tinta-700 text-white"><UserPlus size={17} aria-hidden /></span>
        <div>
          <p className="text-base font-semibold text-gray-900 leading-tight">Cliente novo</p>
          <p className="text-xs text-gray-600">Só o nome. Ele já fica escolhido.</p>
        </div>
      </div>
      <div>
        <label htmlFor="cliente-novo-nome" className="block text-sm font-medium text-gray-700 mb-1">Nome do cliente</label>
        <input id="cliente-novo-nome" type="text" value={nome} onChange={e => setNome(e.target.value)} autoCapitalize="characters"
               enterKeyHint="done" onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); criar() } }}
               autoFocus className={CAMPO} />
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

// ── Peças visuais ───────────────────────────────────────────────────────
const CAMPO = 'appearance-none w-full min-h-12 rounded-xl border border-gray-300 bg-white px-3 text-base text-gray-900 placeholder:text-gray-500 focus:outline-none focus:ring-2 focus:ring-tinta-500 focus:border-tinta-500'

// Cartão de cada passo: número (vira um visto quando está preenchido), título e um resumo à direita
function Etapa({ n, id, titulo, feito = false, resumo, children }: {
  n: number; id: string; titulo: string; feito?: boolean; resumo?: ReactNode; children: ReactNode
}) {
  return (
    <section aria-labelledby={id} className="rounded-2xl border border-gray-200 bg-white p-3.5 shadow-sm">
      <div className="flex items-center gap-2.5 mb-3">
        <span className={`grid place-items-center size-6 shrink-0 rounded-full text-xs font-bold transition-colors
                          ${feito ? 'bg-tinta-700 text-white' : 'bg-gray-100 text-gray-700'}`} aria-hidden>
          {feito ? <Check size={14} strokeWidth={3} /> : n}
        </span>
        <h3 id={id} className="flex-1 min-w-0 text-base font-semibold text-gray-900 truncate">{titulo}</h3>
        {resumo}
      </div>
      {children}
    </section>
  )
}

// Círculo com as iniciais do cliente (até duas letras)
function Iniciais({ nome, destaque = false }: { nome: string; destaque?: boolean }) {
  const letras = nome.trim().split(/\s+/).filter(p => p.length > 2).slice(0, 2).map(p => p[0]).join('')
    || nome.trim().slice(0, 1) || '?'
  return (
    <span aria-hidden className={`grid place-items-center size-9 shrink-0 rounded-full text-sm font-semibold uppercase
                                  ${destaque ? 'bg-tinta-700 text-white' : 'bg-tinta-100 text-tinta-800'}`}>
      {letras}
    </span>
  )
}

// Nome com a parte buscada em negrito (ignora acento e maiúscula)
function Destacado({ texto, termo }: { texto: string; termo: string }) {
  // letra a letra, para o índice valer no texto original
  const norm = (s: string) => Array.from(s, ch => ch.normalize('NFD')[0].toLowerCase()).join('')
  const i = norm(texto).indexOf(norm(termo))
  if (!termo || i < 0) return <>{texto}</>
  return <>{texto.slice(0, i)}<strong className="font-semibold text-tinta-800">{texto.slice(i, i + termo.length)}</strong>{texto.slice(i + termo.length)}</>
}
