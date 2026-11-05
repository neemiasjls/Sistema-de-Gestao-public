import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Campo from '../components/ui/Campo'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { Segmentado } from '../components/ui/Abas'
import { classeCampo } from '../components/EstoquePecas'
import { useEffect, useMemo, useState } from 'react'
import { Egg, FileSearch, Pencil, Plus, Search, Trash2, TriangleAlert, X } from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL, tiposVolumeApi, type CorVolume, type ProdutoAuditado, type TipoVolume } from '../services/api'

// Os tipos de volumes e todas as formas como as notas os escrevem. É daqui que a
// leitura dos XMLs tira em que linha do romaneio cada produto cai; a auditoria
// mostra tudo o que já passou pelas notas guardadas e onde foi parar.
//
// Celular (D-06): cada tipo é um cartão (nome, escritas, cor e excluir em
// linhas próprias) e a auditoria vira cartões em vez da tabela de 5 colunas.

const CORES: { valor: CorVolume; rotulo: string; bolinha: string }[] = [
  { valor: 'BRANCO',   rotulo: 'Brancos',   bolinha: 'bg-white border border-gray-400' },
  { valor: 'VERMELHO', rotulo: 'Vermelhos', bolinha: 'bg-red-700' },
  { valor: 'OUTRO',    rotulo: 'Outros',    bolinha: 'bg-gray-400' },
]

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

// A escrita que vale cadastrar: o código antes do traço quando a nota usa um
// ("GV10X30-VOLUMES ..." → "GV10X30"), senão a descrição inteira.
const escritaSugerida = (produto: string) => {
  const codigo = produto.split('-')[0].trim()
  return produto.includes('-') && /^[A-Z0-9]{2,12}$/i.test(codigo) ? codigo : produto.trim()
}

const campoPequeno = 'min-h-11 sm:min-h-9 rounded-lg border border-gray-300 bg-white px-2.5 text-sm text-gray-900 transition-colors hover:border-gray-400 focus:border-tinta-500'

export default function TiposVolumePage() {
  const [tipos, setTipos] = useState<TipoVolume[]>([])
  const [linhas, setLinhas] = useState<string[]>([])
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const confirmar = useConfirmar()
  const [novoNome, setNovoNome] = useState('')
  const [novoExtenso, setNovoExtenso] = useState('')
  const [novaCor, setNovaCor] = useState<CorVolume>('BRANCO')
  const [criando, setCriando] = useState(false)
  const [escritaNova, setEscritaNova] = useState<Record<number, string>>({})

  const [auditoria, setAuditoria] = useState<ProdutoAuditado[] | null>(null)
  const [auditando, setAuditando] = useState(false)
  const [falhouAuditar, setFalhouAuditar] = useState(false)
  const [filtroAud, setFiltroAud] = useState<'todos' | 'sem'>('todos')
  const [busca, setBusca] = useState('')

  const carregar = async () => {
    try {
      const { data } = await tiposVolumeApi.listar()
      setTipos(data.tipos)
      setLinhas(data.linhasRomaneio)
      setFalhou(false)
    } catch { setFalhou(true) }
    finally { setCarregando(false) }
  }

  const auditar = async () => {
    setAuditando(true)
    try {
      const { data } = await tiposVolumeApi.auditoria()
      setAuditoria(data)
      setFalhouAuditar(false)
    } catch { setFalhouAuditar(true); toast.error('Não consegui ler as notas guardadas.') }
    finally { setAuditando(false) }
  }

  useEffect(() => { carregar() }, [])

  const criar = async () => {
    if (!novoNome.trim()) return
    setCriando(true)
    try {
      await tiposVolumeApi.criar({ nome: novoNome.trim(), cor: novaCor, nomeExtenso: novoExtenso.trim() || null })
      setNovoNome('')
      setNovoExtenso('')
      toast.success('Tipo cadastrado.')
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui cadastrar.')) }
    finally { setCriando(false) }
  }

  const trocarCor = async (t: TipoVolume, cor: CorVolume) => {
    try { await tiposVolumeApi.editar(t.id, { nome: t.nome, cor, nomeExtenso: t.nomeExtenso }); await carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui salvar.')) }
  }

  // O nome por extenso é só para ler a tela: trocar não mexe em regra nenhuma.
  const renomear = async (t: TipoVolume, nomeExtenso: string) => {
    const novo = nomeExtenso.trim() || null
    if (novo === (t.nomeExtenso ?? null)) return
    try { await tiposVolumeApi.editar(t.id, { nome: t.nome, cor: t.cor, nomeExtenso: novo }); await carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui salvar o nome.')) }
  }

  // Na auditoria o tipo vem pelo nome do sistema; na tela aparece o por extenso.
  const exibir = (nome: string) => tipos.find(t => t.nome === nome)?.nomeExtenso || nome

  const excluir = async (t: TipoVolume) => {
    if (!await confirmar({
      titulo: `Excluir ${t.nomeExtenso || t.nome}?`,
      texto: `Vão junto as ${t.escritas.length} escrita(s) dele. As notas com essas escritas deixam de cair no romaneio.`,
      confirmar: 'Excluir tipo',
    })) return
    try { await tiposVolumeApi.excluir(t.id); await carregar(); if (auditoria) await auditar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const adicionarEscrita = async (tipoId: number, texto: string) => {
    if (!texto.trim()) return
    try {
      await tiposVolumeApi.adicionarEscrita(tipoId, texto.trim())
      setEscritaNova(p => ({ ...p, [tipoId]: '' }))
      await carregar()
      if (auditoria) await auditar()
      return true
    } catch (e) { toast.error(erroDe(e, 'Não consegui adicionar.')) }
  }

  const excluirEscrita = async (id: number, texto: string) => {
    if (!await confirmar({
      titulo: `Remover a escrita ${texto}?`,
      texto: 'As notas escritas assim deixam de cair neste tipo no romaneio.',
      confirmar: 'Remover escrita',
    })) return
    try { await tiposVolumeApi.excluirEscrita(id); await carregar(); if (auditoria) await auditar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui remover.')) }
  }

  const auditoriaFiltrada = useMemo(() => {
    const b = busca.trim().toUpperCase()
    return (auditoria ?? [])
      .filter(p => filtroAud === 'todos' || !p.tipo)
      .filter(p => !b || p.produto.toUpperCase().includes(b) || (p.tipo ?? '').toUpperCase().includes(b)
        || p.emitente.toUpperCase().includes(b))
  }, [auditoria, filtroAud, busca])

  const semTipo = (auditoria ?? []).filter(p => !p.tipo).length

  // Produto da nota sem tipo: escolher o tipo cadastra a escrita sugerida
  const escolherTipo = (p: ProdutoAuditado) => (
    <select defaultValue="" aria-label={`Tipo para ${p.produto}`} title={`Cadastra "${escritaSugerida(p.produto)}" no tipo escolhido`}
            onChange={async e => {
              const id = Number(e.target.value)
              if (id && await adicionarEscrita(id, escritaSugerida(p.produto)))
                toast.success(`"${escritaSugerida(p.produto)}" cadastrada.`)
            }}
            className={`${campoPequeno} w-full sm:w-auto border-amber-400 bg-amber-50 text-amber-950 font-medium`}>
      <option value="">Sem tipo: escolher...</option>
      {tipos.map(t => <option key={t.id} value={t.id}>{t.nomeExtenso || t.nome}</option>)}
    </select>
  )

  return (
    <div className="space-y-6 max-w-5xl">
      <CabecalhoTela descricao="Cada tipo e as formas como as notas o escrevem. É por aqui que o sistema sabe em que linha do romaneio cada produto da nota cai." />

      {/* Novo tipo */}
      <section aria-labelledby="tipo-novo" className="bg-white rounded-xl border border-gray-200 p-4 space-y-3">
        <h2 id="tipo-novo" className="text-base font-semibold text-gray-900">Novo tipo</h2>
        <form onSubmit={e => { e.preventDefault(); criar() }}
              className="grid grid-cols-1 sm:grid-cols-[1fr_1fr_9rem_auto] gap-3 sm:items-end">
          <Campo rotulo="Nome por extenso">
            <input value={novoExtenso} onChange={e => setNovoExtenso(e.target.value)}
                   placeholder="ex.: Grande Vermelho PVC" className={classeCampo} />
          </Campo>
          <Campo rotulo="Nome no romaneio (usado pelo sistema)">
            <input list="linhas-romaneio" value={novoNome} onChange={e => setNovoNome(e.target.value)}
                   placeholder="ex.: Grande Vr Pvc" className={classeCampo} />
          </Campo>
          <datalist id="linhas-romaneio">
            {linhas.filter(l => !tipos.some(t => t.nome === l)).map(l => <option key={l} value={l} />)}
          </datalist>
          <Campo rotulo="Cor">
            <select value={novaCor} onChange={e => setNovaCor(e.target.value as CorVolume)} className={classeCampo}>
              <option value="BRANCO">Branco</option>
              <option value="VERMELHO">Vermelho</option>
              <option value="OUTRO">Outro</option>
            </select>
          </Campo>
          <Botao type="submit" variante="primario" icone={Plus} disabled={!novoNome.trim()} carregando={criando}>Cadastrar</Botao>
        </form>
      </section>

      {/* Tipos por cor */}
      {carregando ? <Carregando linhas={4} rotulo="Carregando os tipos de volumes" />
        : falhou ? <ErroCarga mensagem="Não consegui carregar os tipos de volumes." onTentar={() => { setCarregando(true); carregar() }} />
        : tipos.length === 0 ? <Vazio icone={Egg} titulo="Nenhum tipo cadastrado">Cadastre o primeiro acima, com o nome da linha do romaneio.</Vazio>
        : CORES.map(cor => {
        const daCor = tipos.filter(t => t.cor === cor.valor)
        if (!daCor.length) return null
        return (
          <section key={cor.valor} aria-labelledby={`cor-${cor.valor}`} className="space-y-2">
            <h2 id={`cor-${cor.valor}`} className="flex items-center gap-2 text-base font-semibold text-gray-900">
              <span className={`w-3 h-3 rounded-full ${cor.bolinha}`} aria-hidden /> {cor.rotulo}
              <span className="font-sans text-sm font-normal text-gray-500 tabular-nums">{daCor.length}</span>
            </h2>
            <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100">
              {daCor.map(t => (
                <li key={t.id} className="px-4 py-3 grid grid-cols-[1fr_auto] md:grid-cols-[14rem_1fr_auto] gap-x-4 gap-y-2 md:items-start">
                  <div className="min-w-0">
                    <NomeExtenso tipo={t} onSalvar={v => renomear(t, v)} />
                    {!t.naPlanilha && (
                      <p className="flex items-center gap-1 text-sm font-medium text-amber-800">
                        <TriangleAlert size={14} aria-hidden /> sem linha no romaneio
                      </p>
                    )}
                  </div>

                  <div className="flex items-center gap-1 md:order-3">
                    <select value={t.cor} onChange={e => trocarCor(t, e.target.value as CorVolume)} aria-label={`Cor de ${t.nomeExtenso || t.nome}`}
                            className={campoPequeno}>
                      <option value="BRANCO">Branco</option>
                      <option value="VERMELHO">Vermelho</option>
                      <option value="OUTRO">Outro</option>
                    </select>
                    <BotaoIcone icone={Trash2} perigo rotulo={`Excluir ${t.nomeExtenso || t.nome}`} onClick={() => excluir(t)} />
                  </div>

                  <div className="col-span-2 md:col-span-1 md:order-2 flex flex-wrap items-center gap-1.5 min-w-0">
                    {/* O nome da linha é o próprio tipo: o × dele exclui o tipo (com confirmação) */}
                    <span title="Nome da linha no romaneio. É por ele que o sistema preenche a planilha."
                          className="inline-flex items-center max-w-full min-h-8 bg-tinta-50 text-tinta-800 border border-tinta-200 text-sm font-medium rounded-md pl-2">
                      <span className="break-all py-1">{t.nome}</span>
                      <button type="button" onClick={() => excluir(t)} aria-label={`Excluir o tipo ${t.nomeExtenso || t.nome}`} title="Excluir este tipo"
                              className="shrink-0 inline-flex items-center justify-center w-10 h-10 sm:w-8 sm:h-8 -my-1 rounded-md text-tinta-600 hover:text-red-700 hover:bg-red-50">
                        <X size={14} aria-hidden />
                      </button>
                    </span>
                    {t.escritas.map(e => (
                      <span key={e.id} className="inline-flex items-center max-w-full min-h-8 bg-gray-100 text-gray-800 text-sm rounded-md pl-2 border border-gray-200">
                        <span className="break-all py-1">{e.texto}</span>
                        <button type="button" onClick={() => excluirEscrita(e.id, e.texto)} aria-label={`Remover a escrita ${e.texto}`} title="Remover esta escrita"
                                className="shrink-0 inline-flex items-center justify-center w-10 h-10 sm:w-8 sm:h-8 -my-1 rounded-md text-gray-500 hover:text-red-700 hover:bg-red-50">
                          <X size={14} aria-hidden />
                        </button>
                      </span>
                    ))}
                    <form onSubmit={e => { e.preventDefault(); adicionarEscrita(t.id, escritaNova[t.id] ?? '') }} className="inline-flex">
                      <input value={escritaNova[t.id] ?? ''}
                             onChange={e => setEscritaNova(p => ({ ...p, [t.id]: e.target.value }))}
                             placeholder="Nova escrita"
                             aria-label={`Nova escrita para ${t.nomeExtenso || t.nome} (Enter adiciona)`}
                             className={`${campoPequeno} w-40 border-dashed`} />
                    </form>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        )
      })}

      {/* Auditoria */}
      <section aria-labelledby="auditoria" className="space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
          <div className="min-w-0">
            <h2 id="auditoria" className="text-lg font-semibold text-gray-900">Auditoria das notas</h2>
            <p className="text-sm text-gray-600">
              Cada descrição de produto que já veio nas notas guardadas e em qual tipo ela cai hoje.
            </p>
          </div>
          <Botao variante="secundario" icone={FileSearch} onClick={auditar} carregando={auditando} textoCarregando="Lendo as notas..." className="shrink-0">
            {auditoria ? 'Ler de novo' : 'Ler as notas guardadas'}
          </Botao>
        </div>

        {auditando && !auditoria ? <Carregando linhas={4} rotulo="Lendo as notas guardadas" />
          : falhouAuditar && !auditoria ? <ErroCarga mensagem="Não consegui ler as notas guardadas." onTentar={auditar} />
          : auditoria && (
          <>
            <div className="flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="relative sm:w-64">
                <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
                <input value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar produto, tipo ou emitente" type="search"
                       aria-label="Buscar na auditoria" className={`${classeCampo} pl-9`} />
              </div>
              <Segmentado rotulo="Mostrar" valor={filtroAud} onChange={setFiltroAud}
                          opcoes={[['todos', 'Todos'], ['sem', `Só os sem tipo (${semTipo})`]]} />
              <span className="text-sm text-gray-500 tabular-nums sm:ml-auto">{auditoria.length} descrições diferentes</span>
            </div>

            <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
              {!auditoriaFiltrada.length ? (
                <Vazio compacto titulo={filtroAud === 'sem' && !busca ? 'Todas as descrições têm tipo' : 'Nada com essa busca'} />
              ) : (
                <>
                  <ul className="md:hidden divide-y divide-gray-100">
                    {auditoriaFiltrada.map(p => (
                      <li key={p.produto} className={`px-4 py-3 space-y-1.5 ${!p.tipo ? 'bg-amber-50/50' : ''}`}>
                        <p className="text-sm font-medium text-gray-900 break-words">{p.produto}</p>
                        <p className="text-sm text-gray-600 tabular-nums">
                          {p.emitente} · {p.vezes} {p.vezes === 1 ? 'nota' : 'notas'} · última NF {p.ultimaNF}
                        </p>
                        <p className="text-sm text-gray-800 tabular-nums">
                          {p.precoMin === p.precoMax ? formatBRL(p.precoMin) : `${formatBRL(p.precoMin)} a ${formatBRL(p.precoMax)}`}
                        </p>
                        {p.tipo ? (
                          <p className={`text-sm ${p.naPlanilha ? 'text-gray-800' : 'text-amber-800 font-medium'}`}>
                            <span className="text-gray-500">Cai em </span>{exibir(p.tipo)}{!p.naPlanilha && ' (sem linha no romaneio)'}
                          </p>
                        ) : escolherTipo(p)}
                      </li>
                    ))}
                  </ul>
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead>
                        <tr className="text-left text-xs text-gray-500 bg-gray-50 border-b border-gray-200">
                          <th className="px-4 py-2 font-medium">Como está na nota</th>
                          <th className="px-4 py-2 font-medium">Emitente</th>
                          <th className="px-4 py-2 font-medium text-right">Notas</th>
                          <th className="px-4 py-2 font-medium text-right">Preço</th>
                          <th className="px-4 py-2 font-medium">Cai em</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-gray-100">
                        {auditoriaFiltrada.map(p => (
                          <tr key={p.produto} className={!p.tipo ? 'bg-amber-50/50' : ''}>
                            <td className="px-4 py-2.5 text-gray-900">
                              {p.produto}
                              <span className="block text-sm text-gray-500 tabular-nums">última: NF {p.ultimaNF}</span>
                            </td>
                            <td className="px-4 py-2.5 text-gray-600">{p.emitente}</td>
                            <td className="px-4 py-2.5 text-right tabular-nums">{p.vezes}</td>
                            <td className="px-4 py-2.5 text-right tabular-nums whitespace-nowrap">
                              {p.precoMin === p.precoMax ? formatBRL(p.precoMin)
                                : `${formatBRL(p.precoMin)} a ${formatBRL(p.precoMax)}`}
                            </td>
                            <td className="px-4 py-2.5">
                              {p.tipo ? (
                                <span className={p.naPlanilha ? 'text-gray-800' : 'text-amber-800 font-medium'}>
                                  {exibir(p.tipo)}{!p.naPlanilha && ' (sem linha no romaneio)'}
                                </span>
                              ) : escolherTipo(p)}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </>
              )}
            </div>
          </>
        )}
      </section>
    </div>
  )
}

// Nome por extenso editável no lugar: clica, escreve, Enter ou sair do campo
// salva; Esc desiste. Sem nome, mostra o convite para dar um nome.
function NomeExtenso({ tipo, onSalvar }: { tipo: TipoVolume; onSalvar: (v: string) => void }) {
  const [editando, setEditando] = useState(false)
  const [valor, setValor] = useState(tipo.nomeExtenso ?? '')

  if (editando) return (
    <input autoFocus value={valor} onChange={e => setValor(e.target.value)}
           onBlur={() => { setEditando(false); onSalvar(valor) }}
           onKeyDown={e => {
             if (e.key === 'Enter') (e.target as HTMLInputElement).blur()
             if (e.key === 'Escape') { setValor(tipo.nomeExtenso ?? ''); setEditando(false) }
           }}
           aria-label={`Nome por extenso de ${tipo.nome}`}
           placeholder="Nome por extenso"
           className={classeCampo} />
  )

  return (
    <button type="button" onClick={() => { setValor(tipo.nomeExtenso ?? ''); setEditando(true) }}
            title="Renomear (só muda como aparece aqui)"
            className="group flex items-center gap-1.5 min-h-11 sm:min-h-8 text-left font-semibold">
      {tipo.nomeExtenso
        ? <span className="text-gray-900">{tipo.nomeExtenso}</span>
        : <span className="text-gray-500 font-normal italic">Dar um nome...</span>}
      <Pencil size={14} className="text-gray-400 group-hover:text-gray-700 shrink-0" aria-hidden />
      <span className="sr-only">(renomear)</span>
    </button>
  )
}
