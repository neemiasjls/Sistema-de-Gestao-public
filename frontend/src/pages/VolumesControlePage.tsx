import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import Botao, { BotaoIcone, classesBotao } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, ChevronLeft, ChevronRight, Pencil, Plus, Trash2, ArrowLeftRight, CircleAlert, CircleCheck, Inbox, Truck,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import { dataBr, lerNum, num } from '../utils/formato'
import {
  volumesApi, TIPOS_CRUS, TIPOS_PRODUZIDOS, TODOS_CRUS, TODOS_PRODUZIDOS, PRODUTOSC_POR_CAIXA, ORIGEM_ANOTACAO,
  caixasEProdutosC, caixas, caixasProduzido, pacotes, romaneioDaOrigem,
  type Controle, type LinhaVolumes, type LinhaCru, type LinhaProduzido, type Quantidades, type Sugestoes, type RomaneioFazenda, type ItemLista,
} from '../services/volumesEmbaladosApi'
import { CartaoSobraAnterior, Selo } from '../components/VolumesPecas'
import { CartaoRomaneiosSemControle } from '../components/RomaneiosEmbalar'
import { classeCampo, classeNumero } from '../components/EstoquePecas'

// Um controle de volumes embalados, no lugar da planilha: o que embalou, as
// entradas da fazenda, as saídas por cliente e a conferência (tem de dar zero).

type Aba = 'embalou' | 'entradas' | 'saidas' | 'conferencia'

const porCaixaDe = (chave: string) => TODOS_PRODUZIDOS.find(t => t.chave === chave)?.porCaixa ?? 12

// Só os tipos que tiveram movimento (Bb e Ab-20 quase sempre ficam de fora)
const crusComMovimento = (c: Controle) => c.conferencia.crus.filter(x =>
  [x.entradas, x.sobraAnterior, x.pvcVinteEntrada, x.consumoProduzidos, x.usadoPvcVinte,
   x.saidasCruas, x.sobra, x.quebra].some(v => v !== 0))
const produzidosComMovimento = (c: Controle) => c.conferencia.produzidos.filter(p => p.embalou !== 0 || p.saidas !== 0)

export default function VolumesControlePage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [c, setC] = useState<Controle | null>(null)
  const [aba, setAba] = useState<Aba | null>(null)
  const idAbas = useAbas('controle')
  const [sugestoes, setSugestoes] = useState<Sugestoes>({ clientes: [], fornecedores: [], placas: [] })
  const [linhaModal, setLinhaModal] = useState<{ grupo: 'entrada' | 'saida'; linha?: LinhaVolumes } | null>(null)
  const [modalSobraAnt, setModalSobraAnt] = useState(false)
  // Romaneios da fazenda sem funrural (volumes para embalar): os que estão sem
  // controle aparecem nas Entradas para puxar; o de cada linha, para trocar.
  const [romaneios, setRomaneios] = useState<RomaneioFazenda[]>([])
  const [puxando, setPuxando] = useState<number | null>(null)
  const [trocando, setTrocando] = useState<RomaneioFazenda | null>(null)
  const [falhou, setFalhou] = useState(false)
  const confirmar = useConfirmar()

  const carregar = useCallback(async () => {
    setFalhou(false)
    try {
      const d = await volumesApi.detalhe(Number(id))
      setC(d)
      volumesApi.romaneiosFazenda().then(setRomaneios).catch(() => setRomaneios([]))
      // Abre onde há trabalho: o que falta, ou a conferência.
      setAba(prev => prev ?? (d.entradas.length === 0 ? 'entradas' : d.saidas.length === 0 ? 'saidas' : 'conferencia'))
    } catch {
      setFalhou(true)
    }
  }, [id])

  useEffect(() => { setAba(null); setC(null); carregar() }, [carregar])
  useEffect(() => { volumesApi.sugestoes().then(setSugestoes).catch(() => {}) }, [])

  const excluir = async () => {
    if (!c) return
    if (!await confirmar({
      titulo: `Apagar o controle ${c.numero}?`,
      texto: <>O controle de {dataBr(c.data)} sai inteiro: o que embalou, {c.entradas.length} {c.entradas.length === 1 ? 'entrada' : 'entradas'} e {c.saidas.length} {c.saidas.length === 1 ? 'saída' : 'saídas'}.</>,
      confirmar: 'Apagar controle',
    })) return
    try {
      await volumesApi.excluir(c.id)
      toast.success('Controle apagado.')
      navigate('/estoque/volumes')
    } catch {
      toast.error('Não consegui apagar.')
    }
  }

  const excluirLinha = async (l: LinhaVolumes) => {
    const rom = romaneioDaOrigem(l.origem)
    if (!await confirmar(rom
      ? { titulo: `Tirar o romaneio ${rom} deste controle?`, texto: 'Ele não volta sozinho; dá para puxar de novo pelo cartão de romaneios nas Entradas.', confirmar: 'Tirar romaneio' }
      : { titulo: 'Apagar esta linha?', texto: <><b>{l.nome ?? 'Sem nome'}</b>{l.data ? `, ${dataBr(l.data)}` : ''}.</>, confirmar: 'Apagar linha' })) return
    try {
      if (rom) await volumesApi.retirarRomaneio(rom)
      else await volumesApi.excluirLinha(l.id)
      toast.success(rom ? 'Romaneio tirado deste controle.' : 'Linha apagada.')
      carregar()
    } catch {
      toast.error('Não consegui apagar.')
    }
  }

  const erroDe = (e: unknown, padrao: string) =>
    (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

  const puxarRomaneio = async (r: RomaneioFazenda) => {
    if (!c) return
    setPuxando(r.numero)
    try {
      await volumesApi.moverRomaneio(r.numero, c.id)
      toast.success(`Romaneio ${r.numero} entrou no controle ${c.numero}.`)
      carregar()
    } catch (e) {
      toast.error(erroDe(e, 'Não consegui puxar o romaneio.'))
    } finally {
      setPuxando(null)
    }
  }

  if (!c) return (
    <div className="max-w-3xl space-y-4">
      <Link to="/estoque/volumes" className={`${classesBotao('fantasma', 'sm')} -ml-3`}>
        <ArrowLeft size={16} aria-hidden /> Controles
      </Link>
      {falhou
        ? <ErroCarga mensagem="Não consegui carregar o controle." onTentar={carregar} />
        : <Carregando linhas={4} rotulo="Carregando o controle" />}
    </div>
  )

  const conf = c.conferencia
  const naoBatem = crusComMovimento(c).filter(x => !x.bate).length + produzidosComMovimento(c).filter(p => !p.bate).length
  const abaAtual: Aba = aba ?? 'conferencia'

  return (
    <div className="max-w-3xl space-y-4">
      <nav aria-label="Controles" className="flex items-center justify-between gap-2 -mt-1">
        <Link to="/estoque/volumes" className={`${classesBotao('fantasma', 'sm')} -ml-3`}>
          <ArrowLeft size={16} aria-hidden /> Controles
        </Link>
        <div className="flex items-center gap-1">
          <BotaoIcone icone={ChevronLeft} rotulo="Controle anterior" disabled={!c.anteriorId}
                      onClick={() => navigate(`/estoque/volumes/${c.anteriorId}`)} />
          <BotaoIcone icone={ChevronRight} rotulo="Próximo controle" disabled={!c.proximoId}
                      onClick={() => navigate(`/estoque/volumes/${c.proximoId}`)} />
        </div>
      </nav>

      <header className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <h2 className="font-display text-2xl font-semibold text-gray-900 leading-tight">Controle {c.numero}</h2>
        <span className="text-base text-gray-500 tabular-nums">{dataBr(c.data)}</span>
        <Selo status={conf.status} grande />
      </header>

      {conf.avisos.length > 0 && <Aviso tipo="atencao" itens={conf.avisos} />}

      {/* Quatro abas que cabem na largura do celular (sem rolar de lado) */}
      <div className="[&_[role=tablist]]:flex [&_[role=tablist]]:w-full sm:[&_[role=tablist]]:inline-flex sm:[&_[role=tablist]]:w-auto
                      [&_[role=tab]]:flex-1 [&_[role=tab]]:justify-center [&_[role=tab]]:px-1.5 sm:[&_[role=tab]]:px-4">
        <Abas id={idAbas} valor={abaAtual} onChange={setAba} rotulo="Partes do controle" opcoes={[
          ['embalou', 'Embalou'],
          ['entradas', `Entradas ${c.entradas.length}`],
          ['saidas', `Saídas ${c.saidas.length}`],
          ['conferencia', naoBatem ? `Conferência (${naoBatem})` : 'Conferência'],
        ]} />
      </div>

      <PainelAba id={idAbas} valor={abaAtual}>
        {abaAtual === 'embalou' && (
          <AbaEmbalou c={c} onEditarSobraAnterior={() => setModalSobraAnt(true)} onExcluir={excluir} />
        )}

        {abaAtual === 'entradas' && (
          <AbaLinhas c={c} grupo="entrada" linhas={c.entradas}
                     topo={<>
                       <CartaoRomaneiosSemControle romaneios={romaneios} onPuxar={puxarRomaneio} puxando={puxando} dataControle={c.data} />
                       {/* A sobra do controle anterior é a primeira entrada, e dá para corrigir aqui mesmo */}
                       <CartaoSobraAnterior sobra={c.sobraAnterior} manual={c.sobraAnteriorManual} acao={
                         <Botao variante="fantasma" tamanho="sm" icone={Pencil} onClick={() => setModalSobraAnt(true)} className="-my-1 -mr-2 shrink-0">
                           Alterar
                         </Botao>
                       } />
                     </>}
                     romaneios={romaneios}
                     onTrocarControle={setTrocando}
                     onNova={() => setLinhaModal({ grupo: 'entrada' })}
                     onEditar={l => setLinhaModal({ grupo: 'entrada', linha: l })}
                     onExcluir={excluirLinha} />
        )}

        {abaAtual === 'saidas' && (
          <AbaLinhas c={c} grupo="saida" linhas={c.saidas}
                     onNova={() => setLinhaModal({ grupo: 'saida' })}
                     onEditar={l => setLinhaModal({ grupo: 'saida', linha: l })}
                     onExcluir={excluirLinha} />
        )}

        {abaAtual === 'conferencia' && <AbaConferencia c={c} onSalvou={carregar} />}
      </PainelAba>

      {linhaModal && (
        <ModalLinha c={c} grupo={linhaModal.grupo} linha={linhaModal.linha} sugestoes={sugestoes}
                    onClose={() => setLinhaModal(null)}
                    onSalvou={() => { setLinhaModal(null); carregar(); volumesApi.sugestoes().then(setSugestoes).catch(() => {}) }} />
      )}

      {modalSobraAnt && (
        <ModalSobraAnterior c={c} onClose={() => setModalSobraAnt(false)}
                            onSalvou={() => { setModalSobraAnt(false); carregar() }} />
      )}

      {trocando && (
        <ModalTrocarControle romaneio={trocando} atual={c} onClose={() => setTrocando(null)}
                             onTrocou={novo => { setTrocando(null); navigate(`/estoque/volumes/${novo}`) }} />
      )}
    </div>
  )
}

// ── Peças ─────────────────────────────────────────────────────────────────

/** Cartão branco com título (h3) e ação opcional no canto. */
function Bloco({ titulo, nota, acao, children, className = '' }: {
  titulo: ReactNode; nota?: ReactNode; acao?: ReactNode; children: ReactNode; className?: string
}) {
  return (
    <section className={`bg-white border border-gray-200 rounded-xl overflow-hidden ${className}`}>
      <div className="flex items-center justify-between gap-3 pl-4 pr-2 min-h-12 border-b border-gray-100">
        <h3 className="font-display text-base font-semibold text-gray-900 py-2">
          {titulo} {nota && <span className="font-sans text-sm font-normal text-gray-500">{nota}</span>}
        </h3>
        {acao}
      </div>
      {children}
    </section>
  )
}

// ── Embalou ───────────────────────────────────────────────────────────────

function AbaEmbalou({ c, onEditarSobraAnterior, onExcluir }: {
  c: Controle; onEditarSobraAnterior: () => void; onExcluir: () => void
}) {
  // Mostra tudo que tiver valor, inclusive tipo fora de uso (Bb) em controle antigo
  const crusComValor = (l: LinhaVolumes | null) => TODOS_CRUS.filter(t => (l?.[t.chave] ?? 0) !== 0)
  const produzidos = TODOS_PRODUZIDOS.filter(t => TIPOS_PRODUZIDOS.includes(t) || (c.embalou?.[t.chave] ?? 0) !== 0)

  return (
    <div className="space-y-4">
      <Bloco titulo="O que embalou" acao={
        <Link to={`/estoque/volumes/${c.id}/editar`} className={classesBotao('fantasma', 'sm')}>
          <Pencil size={16} aria-hidden /> Editar
        </Link>
      }>
        <dl className="divide-y divide-gray-100 text-sm">
          {produzidos.map(t => {
            const v = c.embalou?.[t.chave] ?? 0
            return (
              <div key={t.chave} className="flex items-baseline justify-between gap-3 px-4 py-2.5">
                <dt className="min-w-0">
                  <span className="font-medium text-gray-900">{t.codigo}</span>
                  <span className="text-gray-500"> · {t.descricao}</span>
                </dt>
                <dd className={`tabular-nums text-right ${v ? 'font-semibold text-gray-900' : 'text-gray-500'}`}>
                  {v ? caixasProduzido(v, t.porCaixa) : '—'}
                </dd>
              </div>
            )
          })}
          {c.pvcVinteComCaixaAb > 0 && (
            <div className="flex items-baseline justify-between gap-3 px-4 py-2.5">
              <dt className="text-gray-600">PVC de 20 feitos com caixa de 30</dt>
              <dd className="tabular-nums font-semibold text-gray-900">{caixas(c.pvcVinteComCaixaAb)}</dd>
            </div>
          )}
          <LinhaProdutosC rotulo="Sobra" linha={c.sobra} tipos={crusComValor(c.sobra)} />
          <LinhaProdutosC rotulo="Quebra" linha={c.quebra} tipos={crusComValor(c.quebra)} />
        </dl>
        {c.observacao && (
          <p className="px-4 py-3 border-t border-gray-100 text-sm text-gray-800">
            <span className="text-gray-500">Observação: </span>{c.observacao}
          </p>
        )}
      </Bloco>

      <CartaoSobraAnterior sobra={c.sobraAnterior} manual={c.sobraAnteriorManual} acao={
        <Botao variante="fantasma" tamanho="sm" icone={Pencil} onClick={onEditarSobraAnterior} className="-my-1 -mr-2 shrink-0">
          Alterar
        </Botao>
      } />

      <div className="pt-2">
        <Botao variante="perigoLeve" icone={Trash2} onClick={onExcluir} className="w-full sm:w-auto">
          Apagar este controle
        </Botao>
      </div>
    </div>
  )
}

function LinhaProdutosC({ rotulo, linha, tipos }: {
  rotulo: string; linha: LinhaVolumes | null; tipos: typeof TIPOS_CRUS
}) {
  return (
    <div className="flex items-baseline justify-between gap-3 px-4 py-2.5">
      <dt className="font-medium text-gray-900">{rotulo}</dt>
      <dd className="text-right tabular-nums">
        {tipos.length === 0 ? <span className="text-gray-500">—</span> : tipos.map(t => (
          <span key={t.chave} className="block text-gray-900">
            <span className="text-gray-500">{t.nome}</span> <span className="font-semibold">{caixasEProdutosC(linha![t.chave])}</span>
          </span>
        ))}
      </dd>
    </div>
  )
}

// ── Entradas e saídas ─────────────────────────────────────────────────────

/** Cada quantidade da linha: [tipo, quanto] (produzido em caixas; cru em caixas e produtosC). */
function quantidadesDaLinha(l: LinhaVolumes): [string, string][] {
  const partes: [string, string][] = []
  TODOS_PRODUZIDOS.forEach(t => { if (l[t.chave]) partes.push([t.codigo, caixasProduzido(l[t.chave], t.porCaixa)]) })
  TODOS_CRUS.forEach(t => { if (l[t.chave]) partes.push([t.nome, caixasEProdutosC(l[t.chave])]) })
  return partes
}

function AbaLinhas({ c, grupo, linhas, onNova, onEditar, onExcluir, topo, romaneios = [], onTrocarControle }: {
  c: Controle; grupo: 'entrada' | 'saida'; linhas: LinhaVolumes[]
  onNova: () => void; onEditar: (l: LinhaVolumes) => void; onExcluir: (l: LinhaVolumes) => void
  topo?: ReactNode
  romaneios?: RomaneioFazenda[]
  onTrocarControle?: (r: RomaneioFazenda) => void
}) {
  const entrada = grupo === 'entrada'
  return (
    <div className="space-y-3">
      {topo}
      <Botao variante="primario" icone={Plus} onClick={onNova} className="w-full sm:w-auto">
        {entrada ? 'Nova entrada da fazenda' : 'Nova saída'}
      </Botao>

      {linhas.length === 0 ? (
        entrada
          ? <Aviso tipo="atencao" titulo="Nenhuma entrada lançada">Sem ela a conferência não fecha.</Aviso>
          : <Vazio icone={Truck} titulo="Nenhuma saída lançada ainda">Lance o que saiu para cada cliente, com a placa e o dia.</Vazio>
      ) : (
        <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
          {linhas.map(l => {
            // Entrada que veio do romaneio da fazenda sem funrural (acerto Alfa)
            const numRom = romaneioDaOrigem(l.origem)
            const rom = numRom ? romaneios.find(r => r.numero === numRom) : undefined
            const nome = l.nome ?? 'Sem nome'
            const atrasada = !!l.data && l.data < c.data && !entrada
            return (
              <li key={l.id} className="pl-4 pr-1.5 py-2.5">
                <div className="flex items-start gap-2">
                  <div className="flex-1 min-w-0 pt-1">
                    <p className="flex flex-wrap items-center gap-x-2 gap-y-1">
                      <span className={`font-semibold ${l.nome ? 'text-gray-900' : 'text-amber-800'}`}>{nome}</span>
                      {l.placa && <span className="text-sm text-gray-500 tabular-nums">{l.placa}</span>}
                      {l.origem === ORIGEM_ANOTACAO && (
                        <span className="text-xs font-medium text-gema-900 bg-gema-100 rounded-md px-1.5 py-0.5">venda da anotação</span>
                      )}
                      {l.origem?.startsWith('carregamento:') && (
                        <span className="text-xs font-medium text-tinta-800 bg-tinta-50 rounded-md px-1.5 py-0.5">do controle de carregamento</span>
                      )}
                    </p>
                    <p className="text-sm mt-0.5 tabular-nums">
                      {l.data
                        ? <span className={atrasada ? 'text-amber-800' : 'text-gray-500'}>{dataBr(l.data)}{atrasada && ' · antes do controle'}</span>
                        : <span className="text-amber-800">Sem data</span>}
                      {numRom && (
                        <span className="text-tinta-700"> · romaneio da fazenda {numRom}
                          {rom?.acertoNumero ? <span className="text-gray-500"> · acerto {rom.acertoNumero}</span> : null}
                        </span>
                      )}
                    </p>
                  </div>
                  <div className="flex shrink-0">
                    {rom && onTrocarControle && (
                      <BotaoIcone icone={ArrowLeftRight} rotulo={`Trocar o romaneio ${numRom} de controle`} onClick={() => onTrocarControle(rom)} />
                    )}
                    <BotaoIcone icone={Pencil} rotulo={`Editar ${nome}`} onClick={() => onEditar(l)} />
                    <BotaoIcone icone={Trash2} perigo rotulo={numRom ? `Tirar o romaneio ${numRom} deste controle` : `Apagar ${nome}`} onClick={() => onExcluir(l)} />
                  </div>
                </div>
                <ul className="mt-1.5 mb-1 flex flex-wrap gap-1.5 pr-2.5">
                  {quantidadesDaLinha(l).map(([tipo, quanto]) => (
                    <li key={tipo} className="text-sm tabular-nums bg-gray-50 border border-gray-200 rounded-md px-2 py-0.5">
                      <span className="text-gray-600">{tipo}</span> <span className="font-semibold text-gray-900">{quanto}</span>
                    </li>
                  ))}
                </ul>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

// Cru: caixas + produtosC avulsas. Produzido: caixas.
function ModalLinha({ c, grupo, linha, sugestoes, onClose, onSalvou }: {
  c: Controle; grupo: 'entrada' | 'saida'; linha?: LinhaVolumes; sugestoes: Sugestoes
  onClose: () => void; onSalvou: () => void
}) {
  const entrada = grupo === 'entrada'
  const ultimaData = [...c.saidas].reverse().find(s => s.data)?.data
  const [nome, setNome] = useState(linha?.nome ?? (entrada ? 'PAULO' : ''))
  const [placa, setPlaca] = useState(linha?.placa ?? (!entrada ? [...c.saidas].reverse().find(s => s.placa)?.placa ?? '' : ''))
  const [data, setData] = useState(linha?.data ?? (entrada ? c.data : ultimaData ?? c.data))
  const inicial = (k: keyof Quantidades) => (linha?.[k] ? String(linha[k]).replace('.', ',') : '')
  const [prod, setProd] = useState<Record<string, string>>(
    Object.fromEntries(TIPOS_PRODUZIDOS.map(t => [t.chave, inicial(t.chave)])))
  const [cruCx, setCruCx] = useState<Record<string, string>>(Object.fromEntries(TIPOS_CRUS.map(t => {
    const b = linha?.[t.chave] ?? 0
    const cx = Math.floor(b / PRODUTOSC_POR_CAIXA)
    return [t.chave, cx ? String(cx) : '']
  })))
  const [cruBd, setCruBd] = useState<Record<string, string>>(Object.fromEntries(TIPOS_CRUS.map(t => {
    const b = linha?.[t.chave] ?? 0
    const resto = b - Math.floor(b / PRODUTOSC_POR_CAIXA) * PRODUTOSC_POR_CAIXA
    return [t.chave, resto ? String(resto).replace('.', ',') : '']
  })))
  const [mostrarCrus, setMostrarCrus] = useState(entrada || TIPOS_CRUS.some(t => (linha?.[t.chave] ?? 0) > 0))
  const [salvando, setSalvando] = useState(false)
  // Mexeu em algum campo: fechar pergunta antes (D-17, UI-17)
  const [mexeu, setMexeu] = useState(false)
  const sujo = mexeu

  const quantidades = (): Partial<Quantidades> => {
    // Tipos fora de uso (Bb, PVCMB) não aparecem aqui: mantém o que a linha já tinha.
    const q: Partial<Quantidades> = { bb: linha?.bb ?? 0, mdPvcBr: linha?.mdPvcBr ?? 0 }
    TIPOS_CRUS.forEach(t => {
      q[t.chave] = lerNum(cruCx[t.chave] ?? '') * PRODUTOSC_POR_CAIXA + lerNum(cruBd[t.chave] ?? '')
    })
    if (!entrada) TIPOS_PRODUZIDOS.forEach(t => { q[t.chave] = lerNum(prod[t.chave] ?? '') })
    return q
  }

  const salvar = async () => {
    const q = quantidades()
    if (!Object.values(q).some(v => (v ?? 0) > 0)) { toast.error('Informe alguma quantidade.'); return }
    if (!nome.trim()) { toast.error(entrada ? 'Informe de onde veio.' : 'Informe o cliente.'); return }
    setSalvando(true)
    const dto = { grupo, nome, placa, data: data || null, quantidades: q }
    try {
      if (linha) await volumesApi.atualizarLinha(linha.id, dto)
      else await volumesApi.criarLinha(c.id, dto)
      toast.success(entrada ? 'Entrada salva.' : 'Saída salva.')
      onSalvou()
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro
      toast.error(msg ?? 'Não consegui salvar.')
    } finally {
      setSalvando(false)
    }
  }

  const listaNomes = entrada ? sugestoes.fornecedores : sugestoes.clientes
  const mudou = <T,>(f: (v: T) => void) => (v: T) => { setMexeu(true); f(v) }

  return (
    <Modal titulo={`${linha ? 'Editar' : 'Nova'} ${entrada ? 'entrada da fazenda' : 'saída'}`} onClose={onClose} sujo={sujo}
           descricao={`Controle ${c.numero} · ${dataBr(c.data)}`}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />}>
      <Campo rotulo={entrada ? 'De onde veio' : 'Cliente ou pessoa'}>
        <input value={nome} onChange={e => mudou(setNome)(e.target.value)} list="volumes-nomes"
               placeholder={entrada ? 'PAULO, EMBALADOS FAZENDA...' : 'GIRASSOL, FAROL, ADMIN...'}
               className={`${classeCampo} uppercase`} />
        <datalist id="volumes-nomes">{listaNomes.map(n => <option key={n} value={n} />)}</datalist>
      </Campo>

      <div className={`grid gap-3 ${entrada ? 'grid-cols-1' : 'grid-cols-2'}`}>
        <Campo rotulo="Data">
          <input type="date" value={data ?? ''} onChange={e => mudou(setData)(e.target.value)} className={classeCampo} />
        </Campo>
        {!entrada && (
          <Campo rotulo="Placa">
            <input value={placa} onChange={e => mudou(setPlaca)(e.target.value)} list="volumes-placas"
                   placeholder="Opcional" className={`${classeCampo} uppercase tabular-nums`} />
            <datalist id="volumes-placas">{sugestoes.placas.map(p => <option key={p} value={p} />)}</datalist>
          </Campo>
        )}
      </div>

      {!entrada && (
        <fieldset>
          <legend className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">Embalados e PVC, em caixas</legend>
          <div className="grid grid-cols-3 gap-2">
            {TIPOS_PRODUZIDOS.map(t => (
              <Campo key={t.chave} rotulo={<span className="font-normal text-gray-600">{t.codigo}</span>}>
                <input value={prod[t.chave] ?? ''} onChange={e => mudou(setProd)({ ...prod, [t.chave]: e.target.value })}
                       inputMode="decimal" placeholder="0" onFocus={e => e.target.select()} className={`${classeNumero} text-base`} />
              </Campo>
            ))}
          </div>
        </fieldset>
      )}

      {!mostrarCrus ? (
        <Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={() => setMostrarCrus(true)} className="-ml-3 text-tinta-700">
          Saiu caixa a granel sem embalar (ou produtoC avulsa)?
        </Botao>
      ) : (
        <fieldset>
          <legend className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">
            {entrada ? 'Caixas a granel' : 'Caixa a granel sem embalar'}
          </legend>
          <div className="space-y-2">
            {TIPOS_CRUS.map(t => (
              <div key={t.chave} className="grid grid-cols-[3.5rem_1fr_1fr] items-center gap-2">
                <span className="text-sm font-semibold text-gray-800">{t.nome}</span>
                <input value={cruCx[t.chave] ?? ''} onChange={e => mudou(setCruCx)({ ...cruCx, [t.chave]: e.target.value })}
                       inputMode="decimal" placeholder="caixas" aria-label={`${t.nome}: caixas`}
                       onFocus={e => e.target.select()} className={`${classeNumero} text-base`} />
                <input value={cruBd[t.chave] ?? ''} onChange={e => mudou(setCruBd)({ ...cruBd, [t.chave]: e.target.value })}
                       inputMode="decimal" placeholder="produtosC" aria-label={`${t.nome}: produtosC`}
                       onFocus={e => e.target.select()} className={`${classeNumero} text-base`} />
              </div>
            ))}
          </div>
          <p className="text-xs text-gray-500 mt-1.5">Caixa de 12 produtosC. Pode usar só as produtosC (ex.: 1 produtoC).</p>
        </fieldset>
      )}
    </Modal>
  )
}

// Passa a entrada do romaneio da fazenda para outro controle (os mais perto da data primeiro)
function ModalTrocarControle({ romaneio, atual, onClose, onTrocou }: {
  romaneio: RomaneioFazenda; atual: Controle; onClose: () => void; onTrocou: (controleId: number) => void
}) {
  const [controles, setControles] = useState<ItemLista[] | 'erro' | null>(null)
  const [escolhido, setEscolhido] = useState<number | null>(null)
  const [salvando, setSalvando] = useState(false)

  const buscar = useCallback(() => {
    setControles(null)
    volumesApi.lista().then(r => {
      const dia = (s: string) => new Date(s + 'T00:00:00').getTime()
      const alvo = dia(romaneio.data)
      setControles([...r.controles].sort((a, b) => Math.abs(dia(a.data) - alvo) - Math.abs(dia(b.data) - alvo)).slice(0, 8))
    }).catch(() => setControles('erro'))
  }, [romaneio.data])
  useEffect(buscar, [buscar])

  const salvar = async () => {
    if (!escolhido) { toast.error('Escolha o controle.'); return }
    setSalvando(true)
    try {
      await volumesApi.moverRomaneio(romaneio.numero, escolhido)
      toast.success('Romaneio trocado de controle.')
      onTrocou(escolhido)
    } catch (e: unknown) {
      toast.error((e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? 'Não consegui trocar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo={`Romaneio da fazenda ${romaneio.numero}`} onClose={onClose}
           descricao={`De ${dataBr(romaneio.data)}. Hoje está no controle ${atual.numero}.`}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Trocar de controle" />}>
      {controles == null ? (
        <Carregando linhas={3} rotulo="Carregando os controles" />
      ) : controles === 'erro' ? (
        <ErroCarga mensagem="Não consegui carregar os controles." onTentar={buscar} />
      ) : (
        <fieldset className="space-y-1.5">
          <legend className="text-sm text-gray-700 mb-2">Para qual controle ele vai?</legend>
          {controles.map(x => {
            const eAtual = x.id === atual.id
            return (
              <label key={x.id} className={`flex items-center gap-3 px-3 min-h-12 border rounded-lg text-sm transition-colors
                ${eAtual ? 'border-gray-200 bg-gray-50 cursor-not-allowed' : escolhido === x.id ? 'border-tinta-400 bg-tinta-50 cursor-pointer' : 'border-gray-200 hover:border-gray-300 cursor-pointer'}`}>
                <input type="radio" name="controle" checked={escolhido === x.id} onChange={() => setEscolhido(x.id)}
                       disabled={eAtual} className="w-4 h-4 accent-tinta-700" />
                <span className="font-display text-lg font-semibold tabular-nums text-gray-900">{x.numero}</span>
                <span className="text-gray-600 tabular-nums">{dataBr(x.data)}</span>
                {eAtual && <span className="ml-auto text-xs text-gray-500">atual</span>}
              </label>
            )
          })}
        </fieldset>
      )}
    </Modal>
  )
}

// Sobra que entra neste controle: a do anterior (automática) ou digitada à mão,
// em caixas + produtosC como as entradas (Admin, 13/10/2026). "Zerar" deixa a
// mão com tudo em zero (às vezes a sobra de lá não veio para cá).
function ModalSobraAnterior({ c, onClose, onSalvou }: { c: Controle; onClose: () => void; onSalvou: () => void }) {
  const [manual, setManual] = useState(c.sobraAnteriorManual)
  const separar = (b: number) => {
    const cx = Math.floor(b / PRODUTOSC_POR_CAIXA)
    const resto = Math.round((b - cx * PRODUTOSC_POR_CAIXA) * 10) / 10
    return { cx: cx ? String(cx) : '', bd: resto ? String(resto).replace('.', ',') : '' }
  }
  const [cx, setCx] = useState<Record<string, string>>(Object.fromEntries(TIPOS_CRUS.map(t => [t.chave, separar(c.sobraAnterior.valores[t.chave] ?? 0).cx])))
  const [bd, setBd] = useState<Record<string, string>>(Object.fromEntries(TIPOS_CRUS.map(t => [t.chave, separar(c.sobraAnterior.valores[t.chave] ?? 0).bd])))
  const [salvando, setSalvando] = useState(false)

  const gravar = async (paraManual: boolean, zerar = false) => {
    setSalvando(true)
    try {
      const sobraAnterior: Partial<Quantidades> = { bb: zerar ? 0 : c.sobraAnterior.valores.bb ?? 0 }
      TIPOS_CRUS.forEach(t => {
        sobraAnterior[t.chave] = zerar ? 0 : lerNum(cx[t.chave] ?? '') * PRODUTOSC_POR_CAIXA + lerNum(bd[t.chave] ?? '')
      })
      await volumesApi.atualizar(c.id, { sobraAnteriorManual: paraManual, sobraAnterior: paraManual ? sobraAnterior : undefined })
      toast.success(zerar ? 'Sobra anterior zerada.' : 'Sobra anterior salva.')
      onSalvou()
    } catch {
      toast.error('Não consegui salvar.')
    } finally {
      setSalvando(false)
    }
  }

  const opcao = (ativo: boolean) => `flex items-start gap-3 rounded-lg border px-3 py-3 cursor-pointer transition-colors
    ${ativo ? 'border-tinta-400 bg-tinta-50' : 'border-gray-200 hover:border-gray-300'}`

  return (
    <Modal titulo="Sobra anterior" onClose={onClose} confirmarAoFechar
           descricao={`Entra como entrada no controle ${c.numero}`}
           rodape={<div className="flex flex-wrap justify-between gap-2">
             <Botao variante="perigoLeve" onClick={() => gravar(true, true)} disabled={salvando}>Zerar</Botao>
             <div className="flex gap-2">
               <Botao onClick={onClose}>Cancelar</Botao>
               <Botao variante="primario" carregando={salvando} onClick={() => gravar(manual)}>Salvar</Botao>
             </div>
           </div>}>
      <fieldset className="space-y-2">
        <legend className="sr-only">De onde vem a sobra anterior</legend>
        <label className={opcao(!manual)}>
          <input type="radio" checked={!manual} onChange={() => setManual(false)} className="mt-0.5 w-4 h-4 accent-tinta-700" />
          <span className="text-sm">
            <span className="block font-medium text-gray-900">Usar a sobra do controle anterior</span>
            <span className="block text-gray-600">Muda sozinha se a sobra de lá for corrigida.</span>
          </span>
        </label>
        <label className={opcao(manual)}>
          <input type="radio" checked={manual} onChange={() => setManual(true)} className="mt-0.5 w-4 h-4 accent-tinta-700" />
          <span className="text-sm">
            <span className="block font-medium text-gray-900">Digitar à mão</span>
            <span className="block text-gray-600">Em caixas e produtosC (caixa = 12 produtosC).</span>
          </span>
        </label>
      </fieldset>
      {manual && (
        <div className="space-y-2 animate-entrada">
          {TIPOS_CRUS.map(t => (
            <div key={t.chave} className="grid grid-cols-[3.5rem_1fr_1fr] items-center gap-2">
              <span className="text-sm font-semibold text-gray-800">{t.nome}</span>
              <input value={cx[t.chave] ?? ''} onChange={e => setCx({ ...cx, [t.chave]: e.target.value })}
                     inputMode="decimal" placeholder="caixas" aria-label={`${t.nome}: caixas`}
                     onFocus={e => e.target.select()} className={`${classeNumero} text-base`} />
              <input value={bd[t.chave] ?? ''} onChange={e => setBd({ ...bd, [t.chave]: e.target.value })}
                     inputMode="decimal" placeholder="produtosC" aria-label={`${t.nome}: produtosC`}
                     onFocus={e => e.target.select()} className={`${classeNumero} text-base`} />
            </div>
          ))}
          <p className="text-xs text-gray-500">Pode usar só as produtosC (ex.: 9 produtosC) ou caixas e produtosC.</p>
        </div>
      )}
    </Modal>
  )
}

// ── Conferência ───────────────────────────────────────────────────────────

function AbaConferencia({ c, onSalvou }: { c: Controle; onSalvou: () => void }) {
  const conf = c.conferencia
  const [salvandoQuebra, setSalvandoQuebra] = useState<string | null>(null)

  // A quebra é estimada (boa parte vai para o lixo): a que fecha a conta substitui a
  // anotada só deste tipo, as outras ficam como estão (Admin, 14/10/2026)
  const usarQuebra = async (chave: string, produtosC: number) => {
    setSalvandoQuebra(chave)
    try {
      const quebra: Partial<Quantidades> = {}
      TODOS_CRUS.forEach(t => { quebra[t.chave] = c.quebra?.[t.chave] ?? 0 })
      quebra[chave as keyof Quantidades] = produtosC
      await volumesApi.atualizar(c.id, { quebra })
      toast.success('Quebra ajustada pela conta.')
      onSalvou()
    } catch {
      toast.error('Não consegui salvar a quebra.')
    } finally {
      setSalvandoQuebra(null)
    }
  }
  const crus = crusComMovimento(c)
  const produzidos = produzidosComMovimento(c)
  const { unidadesAmarelas: am, unidadesBrancas: br } = conf.produtosB ?? { unidadesAmarelas: 0, unidadesBrancas: 0 }

  const cx = (v: number, chave: string) =>
    Math.abs(v - Math.round(v)) < 0.001 ? num(v, 0) : caixasProduzido(v, porCaixaDe(chave))

  // O que não bate, numa olhada, antes das contas
  const problemas = [
    ...crus.filter(x => !x.bate).map(x => ({ k: x.chave, nome: x.nome, tipo: 'caixa a granel', sobrou: x.estoque > 0, quanto: caixasEProdutosC(Math.abs(x.estoque)) })),
    ...produzidos.filter(p => !p.bate).map(p => ({ k: p.chave, nome: p.codigo, tipo: 'embalado', sobrou: p.estoque > 0, quanto: caixasProduzido(Math.abs(p.estoque), porCaixaDe(p.chave)) })),
  ]
  const temAlgo = crus.length > 0 || produzidos.length > 0

  return (
    <div className="space-y-4">
      {!temAlgo ? (
        <Vazio icone={Inbox} titulo="Nada lançado ainda para conferir">
          A conta aparece quando houver o que embalou, as entradas e as saídas.
        </Vazio>
      ) : problemas.length === 0 ? (
        <div className="flex items-center gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3.5">
          <CircleCheck size={24} strokeWidth={2} className="text-emerald-700 shrink-0" aria-hidden />
          <div>
            <p className="font-display text-lg font-semibold text-emerald-900 leading-tight">Tudo bate</p>
            <p className="text-sm text-emerald-800">Cada tipo fechou em zero.</p>
          </div>
        </div>
      ) : (
        <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3.5" role="status">
          <p className="flex items-center gap-2 font-display text-lg font-semibold text-red-900 leading-tight">
            <CircleAlert size={22} strokeWidth={2} className="text-red-700 shrink-0" aria-hidden />
            {problemas.length === 1 ? 'Um tipo não bate' : `${problemas.length} tipos não batem`}
          </p>
          <ul className="mt-2 space-y-1">
            {problemas.map(p => (
              <li key={p.k} className="flex items-baseline justify-between gap-3 text-sm">
                <span className="font-semibold text-red-900">{p.nome} <span className="font-normal text-red-800">{p.tipo}</span></span>
                <span className="text-red-800 tabular-nums text-right">{p.sobrou ? 'sobrou' : 'faltou'} <b className="font-semibold">{p.quanto}</b></span>
              </li>
            ))}
          </ul>
        </div>
      )}

      <CartaoSobraAnterior sobra={c.sobraAnterior} manual={c.sobraAnteriorManual} />

      {crus.length > 0 && (
        <div className="grid gap-3 sm:grid-cols-2">
          {crus.map(x => <CartaoCru key={x.chave} x={x} salvando={salvandoQuebra === x.chave} onUsarQuebra={b => usarQuebra(x.chave, b)} />)}
        </div>
      )}

      {produzidos.length > 0 && (
        <Bloco titulo="Estoque embalado" nota="em caixas">
          {/* Celular: uma linha por tipo, o estoque em destaque */}
          <ul className="sm:hidden divide-y divide-gray-100">
            {produzidos.map(p => <LinhaProduzidoCelular key={p.chave} p={p} cx={cx} />)}
          </ul>
          <table className="hidden sm:table w-full text-sm">
            <thead>
              <tr className="text-xs text-gray-500">
                <th scope="col" className="text-left font-medium px-4 py-2">Tipo</th>
                <th scope="col" className="text-right font-medium px-3 py-2">Embalou</th>
                <th scope="col" className="text-right font-medium px-3 py-2">Saiu</th>
                <th scope="col" className="text-right font-medium px-4 py-2">Estoque</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {produzidos.map(p => (
                <tr key={p.chave}>
                  <th scope="row" className="text-left font-normal px-4 py-2.5">
                    <span className="font-medium text-gray-900">{p.codigo}</span> <span className="text-gray-500">· {p.nome}</span>
                  </th>
                  <td className="text-right px-3 tabular-nums text-gray-800">{cx(p.embalou, p.chave)}</td>
                  <td className="text-right px-3 tabular-nums text-gray-800">{cx(p.saidas, p.chave)}</td>
                  <td className={`text-right px-4 tabular-nums font-semibold ${p.bate ? 'text-emerald-700' : 'text-red-700'}`}>
                    <span className="inline-flex items-center gap-1.5 justify-end">
                      {p.bate ? <CircleCheck size={15} aria-hidden /> : <CircleAlert size={15} aria-hidden />}
                      {cx(p.estoque, p.chave)}
                      <span className="sr-only">{p.bate ? ' (bate)' : ' (não bate)'}</span>
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Bloco>
      )}

      {(am > 0 || br > 0) && (
        <Bloco titulo="ProdutosB usadas">
          <dl className="divide-y divide-gray-100 text-sm">
            {am > 0 && (
              <div className="flex items-baseline justify-between gap-3 px-4 py-2.5">
                <dt className="text-gray-700">Amarelas <span className="text-gray-500">(volume branco)</span></dt>
                <dd className="font-semibold text-gray-900 tabular-nums text-right">{pacotes(am)}</dd>
              </div>
            )}
            {br > 0 && (
              <div className="flex items-baseline justify-between gap-3 px-4 py-2.5">
                <dt className="text-gray-700">Brancas <span className="text-gray-500">(volume vermelho)</span></dt>
                <dd className="font-semibold text-gray-900 tabular-nums text-right">{pacotes(br)}</dd>
              </div>
            )}
          </dl>
        </Bloco>
      )}
    </div>
  )
}

function LinhaProduzidoCelular({ p, cx }: { p: LinhaProduzido; cx: (v: number, chave: string) => string }) {
  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="min-w-0">
        <p><span className="font-semibold text-gray-900">{p.codigo}</span> <span className="text-sm text-gray-500">· {p.nome}</span></p>
        <p className="text-sm text-gray-600 tabular-nums">embalou {cx(p.embalou, p.chave)} · saiu {cx(p.saidas, p.chave)}</p>
      </div>
      <div className={`text-right shrink-0 ${p.bate ? 'text-emerald-700' : 'text-red-700'}`}>
        <p className="text-xs font-medium flex items-center justify-end gap-1">
          {p.bate ? <CircleCheck size={13} aria-hidden /> : <CircleAlert size={13} aria-hidden />}
          {p.bate ? 'Bate' : p.estoque > 0 ? 'Sobrou' : 'Faltou'}
        </p>
        <p className="font-display text-xl font-semibold tabular-nums leading-tight">{cx(p.estoque, p.chave)}</p>
      </div>
    </li>
  )
}

function CartaoCru({ x, onUsarQuebra, salvando }: { x: LinhaCru; onUsarQuebra: (produtosC: number) => void; salvando: boolean }) {
  // Quebra que zera a conta: a anotada mais o que sobrou (ou menos o que faltou), de meia em meia produtoC
  const quebraConta = Math.round((x.quebra + x.estoque) * 2) / 2
  const linhas: [string, number, 1 | -1][] = [
    ['Entradas da fazenda', x.entradas, 1],
    ['Sobra anterior', x.sobraAnterior, 1],
    ['PVC de 20 feito com caixa de 30', x.pvcVinteEntrada, 1],
    ['Embalados e PVC que saíram', x.consumoProduzidos, -1],
    ['Usado para PVC de 20', x.usadoPvcVinte, -1],
    ['Saiu sem embalar', x.saidasCruas, -1],
    ['Sobra', x.sobra, -1],
    ['Quebra', x.quebra, -1],
  ]
  const tom = x.bate
    ? { borda: 'border-gray-200', faixa: 'bg-emerald-50 border-emerald-100', texto: 'text-emerald-800', I: CircleCheck }
    : { borda: 'border-red-300', faixa: 'bg-red-50 border-red-100', texto: 'text-red-800', I: CircleAlert }
  return (
    <section className={`bg-white border rounded-xl overflow-hidden flex flex-col ${tom.borda}`}>
      <h3 className="px-4 py-2.5 border-b border-gray-100 font-display text-base font-semibold text-gray-900">
        {x.nome} <span className="font-sans text-sm font-normal text-gray-500">caixa a granel</span>
      </h3>
      <dl className="text-sm px-4 py-2.5 space-y-1.5 flex-1">
        {linhas.filter(([, v]) => v !== 0).map(([rot, v, s]) => (
          <div key={rot} className="flex items-baseline justify-between gap-3">
            <dt className="text-gray-600">{rot}</dt>
            <dd className="tabular-nums text-gray-900 text-right whitespace-nowrap">
              <span className={`inline-block w-3 ${s < 0 ? 'text-red-700' : 'text-emerald-700'}`} aria-hidden>{s < 0 ? '−' : '+'}</span>
              <span className="sr-only">{s < 0 ? 'menos ' : 'mais '}</span>
              {caixasEProdutosC(v)}
            </dd>
          </div>
        ))}
      </dl>
      <div className={`px-4 py-3 border-t flex items-center justify-between gap-3 ${tom.faixa}`}>
        <span className={`inline-flex items-center gap-1.5 text-sm font-semibold ${tom.texto}`}>
          <tom.I size={16} strokeWidth={2.25} aria-hidden />
          {x.bate ? 'Bate' : x.estoque > 0 ? 'Sobrou' : 'Faltou'}
        </span>
        <span className={`font-display text-xl font-semibold tabular-nums text-right ${tom.texto}`}>
          {caixasEProdutosC(Math.abs(x.estoque))}
        </span>
      </div>
      {!x.bate && (
        <div className="px-4 py-3 border-t border-gray-100 text-sm space-y-2">
          {quebraConta >= 0 ? (
            <>
              <p className="text-gray-700">
                Pela conta, a quebra foi <b className="tabular-nums text-gray-900">{caixasEProdutosC(quebraConta)}</b>
                {' '}(anotada: <span className="tabular-nums">{caixasEProdutosC(x.quebra)}</span>).
              </p>
              <Botao variante="secundario" tamanho="sm" carregando={salvando} onClick={() => onUsarQuebra(quebraConta)}>
                Usar esta quebra
              </Botao>
            </>
          ) : (
            <p className="text-amber-800">
              Mesmo sem quebra falta {caixasEProdutosC(-quebraConta)}: confira as entradas e as saídas.
            </p>
          )}
        </div>
      )}
    </section>
  )
}
