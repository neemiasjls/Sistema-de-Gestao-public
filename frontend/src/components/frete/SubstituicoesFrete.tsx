import { useCallback, useEffect, useState } from 'react'
import { Check, Pencil, Plus, Replace, Trash2, X } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone } from '../ui/Botao'
import Campo from '../ui/Campo'
import { Carregando, ErroCarga, Vazio } from '../ui/Estados'
import { useConfirmar } from '../ui/Confirmar'
import CityAutocomplete from '../CityAutocomplete'
import { substituicoesApi, type GrupoSubstituicao } from '../../services/api'
import { erroDe } from '../embalados/EmbaladosPecas'
import { separadorPerigo } from './BarraLista'

// Aba "Substituições" da tela Viagens (FRE-03): cliente da nota que, no cálculo
// do frete, é cobrado pelo nome do grupo. Quase nunca muda, então a tela é
// quieta: só a lista; criar grupo fica num botão discreto no fim.

const campo = 'w-full min-h-11 sm:min-h-10 border border-gray-300 rounded-lg px-2.5 py-1.5 text-sm'
type FormGrupo = { nome: string; cidade: string; uf: string }
const vazio: FormGrupo = { nome: '', cidade: '', uf: '' }

function CamposGrupo({ valor, onChange }: { valor: FormGrupo; onChange: (v: FormGrupo) => void }) {
  return (
    <div className="grid grid-cols-[1fr_5.5rem] sm:grid-cols-[2fr_1fr_5.5rem] gap-2 sm:gap-3 items-end">
      <Campo rotulo="Nome do grupo" className="col-span-2 sm:col-span-1">
        <input value={valor.nome} onChange={e => onChange({ ...valor, nome: e.target.value })}
               placeholder="Como sai na cobrança" className={campo} />
      </Campo>
      <Campo rotulo="Cidade">
        <CityAutocomplete value={valor.cidade} placeholder="Ex.: ANÁPOLIS"
          onSelect={(nome, uf) => onChange({ ...valor, cidade: nome, uf: uf || valor.uf })} className={campo} />
      </Campo>
      <Campo rotulo="UF">
        <input value={valor.uf} onChange={e => onChange({ ...valor, uf: e.target.value.toUpperCase() })}
               maxLength={2} placeholder="GO" className={`${campo} uppercase`} />
      </Campo>
    </div>
  )
}

export default function SubstituicoesFrete() {
  const confirmar = useConfirmar()
  const [grupos, setGrupos] = useState<GrupoSubstituicao[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [novoCliente, setNovoCliente] = useState<Record<number, string>>({})
  const [editando, setEditando] = useState<{ id: number; form: FormGrupo } | null>(null)
  const [novoGrupo, setNovoGrupo] = useState<FormGrupo | null>(null)
  const [salvando, setSalvando] = useState(false)

  const carregar = useCallback(async () => {
    setFalhou(false)
    try {
      const { data } = await substituicoesApi.listar()
      setGrupos(data)
    } catch { setFalhou(true) }
  }, [])
  useEffect(() => { carregar() }, [carregar])

  const formOk = (f: FormGrupo) => {
    if (!f.nome.trim() || !f.cidade.trim() || f.uf.trim().length !== 2) {
      toast.error('Preencha o nome do grupo, a cidade e a UF.')
      return false
    }
    return true
  }

  const criarGrupo = async () => {
    if (!novoGrupo || !formOk(novoGrupo)) return
    setSalvando(true)
    try {
      await substituicoesApi.criarGrupo(novoGrupo)
      toast.success('Grupo criado.')
      setNovoGrupo(null)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui criar o grupo.')) }
    finally { setSalvando(false) }
  }

  const salvarGrupo = async () => {
    if (!editando || !formOk(editando.form)) return
    try {
      await substituicoesApi.editarGrupo(editando.id, editando.form)
      setEditando(null)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui salvar o grupo.')) }
  }

  const excluirGrupo = async (g: GrupoSubstituicao) => {
    if (!await confirmar({
      titulo: `Excluir o grupo ${g.nome}?`,
      texto: g.clientes.length
        ? `Vão junto os ${g.clientes.length} cliente(s) dele: no frete, eles voltam a ser cobrados pelo nome da nota.`
        : 'O grupo não tem clientes.',
      confirmar: 'Excluir grupo',
    })) return
    try { await substituicoesApi.excluirGrupo(g.id); toast.success('Grupo excluído.'); await carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir o grupo.')) }
  }

  const adicionarCliente = async (g: GrupoSubstituicao) => {
    const nome = (novoCliente[g.id] ?? '').trim()
    if (!nome) { toast.error('Escreva o nome do cliente como vem na nota.'); return }
    try {
      await substituicoesApi.adicionarCliente(g.id, nome)
      setNovoCliente(p => ({ ...p, [g.id]: '' }))
      toast.success(`Cliente adicionado ao grupo ${g.nome}.`)
      await carregar()
    } catch (e) { toast.error(erroDe(e, 'Não consegui adicionar o cliente.')) }
  }

  const excluirCliente = async (g: GrupoSubstituicao, c: { id: number; nome: string }) => {
    if (!await confirmar({
      titulo: `Tirar ${c.nome} do grupo?`,
      texto: `No frete, ele deixa de ser cobrado como ${g.nome} e volta ao nome da nota.`,
      confirmar: 'Tirar do grupo',
    })) return
    try { await substituicoesApi.excluirCliente(c.id); await carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui tirar o cliente.')) }
  }

  if (falhou) return <ErroCarga mensagem="Não consegui carregar as substituições." onTentar={carregar} />
  if (!grupos) return <Carregando rotulo="Carregando as substituições" />

  return (
    <div className="space-y-4 max-w-6xl">
      <p className="text-sm text-gray-600">
        Cliente da nota que, no cálculo do frete, é cobrado pelo nome do grupo. O nome da nota não muda.
        Vale para as notas importadas depois da mudança.
      </p>

      {grupos.length === 0 ? (
        <div className="rounded-xl border border-gray-200 bg-white">
          <Vazio compacto icone={Replace} titulo="Nenhuma substituição">Todo cliente é cobrado pelo nome da nota.</Vazio>
        </div>
      ) : (
        <ul className="grid gap-3 lg:grid-cols-2 items-start">
          {grupos.map(g => (
            <li key={g.id} className="rounded-xl border border-gray-200 bg-white">
              {editando?.id === g.id ? (
                <div className="p-4 space-y-3 bg-tinta-50 rounded-t-xl">
                  <CamposGrupo valor={editando.form} onChange={form => setEditando({ id: g.id, form })} />
                  <div className="flex gap-2">
                    <Botao variante="primario" tamanho="sm" icone={Check} onClick={salvarGrupo}>Salvar o grupo</Botao>
                    <Botao variante="secundario" tamanho="sm" onClick={() => setEditando(null)}>Cancelar</Botao>
                  </div>
                </div>
              ) : (
                <div className="flex items-start gap-2 pl-4 pr-1 pt-2.5 pb-2">
                  <div className="flex-1 min-w-0 pt-1">
                    <p className="text-sm font-medium text-gray-900 break-words">{g.nome}</p>
                    <p className="text-sm text-gray-500">{g.cidade || 'Cidade não informada'} / {g.uf || 'UF não informada'}</p>
                  </div>
                  <div className="flex items-center">
                    <BotaoIcone icone={Pencil} rotulo={`Editar o grupo ${g.nome}`}
                      onClick={() => setEditando({ id: g.id, form: { nome: g.nome, cidade: g.cidade, uf: g.uf } })} />
                    <span className={separadorPerigo}>
                      <BotaoIcone icone={Trash2} perigo rotulo={`Excluir o grupo ${g.nome}`} onClick={() => excluirGrupo(g)} />
                    </span>
                  </div>
                </div>
              )}

              <div className="border-t border-gray-100 px-4 pt-2 pb-3 space-y-2">
                <p className="text-xs text-gray-500">
                  {g.clientes.length === 0 ? 'Nenhum cliente vira este grupo.'
                    : g.clientes.length === 1 ? '1 cliente vira este grupo' : `${g.clientes.length} clientes viram este grupo`}
                </p>
                {g.clientes.length > 0 && (
                  <ul className="divide-y divide-gray-100">
                    {g.clientes.map(c => (
                      <li key={c.id} className="flex items-center gap-2 py-0.5">
                        <span className="flex-1 min-w-0 text-sm text-gray-800 break-words">{c.nome}</span>
                        <BotaoIcone icone={X} perigo rotulo={`Tirar ${c.nome} do grupo`} onClick={() => excluirCliente(g, c)} />
                      </li>
                    ))}
                  </ul>
                )}
                <form className="flex gap-2" onSubmit={e => { e.preventDefault(); adicionarCliente(g) }}>
                  <input value={novoCliente[g.id] ?? ''} onChange={e => setNovoCliente(p => ({ ...p, [g.id]: e.target.value }))}
                         aria-label={`Cliente novo do grupo ${g.nome}, como vem na nota`}
                         placeholder="Nome do cliente como vem na nota" className={`${campo} flex-1 min-w-0`} />
                  <Botao type="submit" variante="secundario" icone={Plus}>Adicionar</Botao>
                </form>
              </div>
            </li>
          ))}
        </ul>
      )}

      {novoGrupo ? (
        <section aria-labelledby="titulo-novo-grupo" className="rounded-xl border border-gray-200 bg-white p-4 space-y-3">
          <h3 id="titulo-novo-grupo" className="text-sm font-semibold text-gray-900">Novo grupo</h3>
          <CamposGrupo valor={novoGrupo} onChange={setNovoGrupo} />
          <div className="flex gap-2">
            <Botao variante="primario" icone={Check} onClick={criarGrupo} carregando={salvando} textoCarregando="Salvando...">Criar grupo</Botao>
            <Botao variante="secundario" onClick={() => setNovoGrupo(null)}>Cancelar</Botao>
          </div>
        </section>
      ) : (
        <Botao variante="fantasma" icone={Plus} onClick={() => setNovoGrupo(vazio)}>Novo grupo</Botao>
      )}
    </div>
  )
}
