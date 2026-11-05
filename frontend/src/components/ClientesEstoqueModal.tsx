import Botao from './ui/Botao'
import Campo from './ui/Campo'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { Modal } from './ModalBase'
import { Pencil, Plus, Search, Users } from 'lucide-react'
import { useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import api from '../services/api'
import { comoLista } from '../utils/resposta'
import { brl, lerNum } from '../utils/formato'
import { classeCampo, classeNumero } from './EstoquePecas'

export type TipoProdutoB = { id: number; nome: string; unidadesPorPacote: number; codigo?: string | null; produto?: 'PRODUTOB' | 'PRODUTOC' }
export type ClienteEstoque = {
  id: number
  nome: string
  cidade?: string | null
  precoProdutoAKg: number
  freteProdutoB?: number
  /** Frete por pacote de produtoC (100 produtosC) */
  freteProdutoC?: number
  ativo: boolean
  precos: { tipoProdutoBId: number; preco: number }[]
}

type Props = {
  aberto: boolean
  // Qual preço vem primeiro no cartão: a tela de ProdutosA mostra o R$ por kg em
  // destaque; a de ProdutosB, os preços por pacote
  foco?: 'produtoA' | 'produtoB'
  // De qual produto é o frete por pacote do formulário (produtoB ou produtoC)
  produto?: 'PRODUTOB' | 'PRODUTOC'
  onClose: () => void
  tipos: TipoProdutoB[]
  onSalvou: () => void
}

/**
 * Cadastro de clientes do estoque: o preço que cada um paga por kg de produtoA e
 * por pacote de cada tipo de produtoB. É desse preço, menos o custo da carga,
 * que sai a comissão.
 */
// Preço da produtoA por kg: guardado com até 4 casas (numeric 18,4); mostra 2 a 4
export const brlKg = (v: number) =>
  `${v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: 2, maximumFractionDigits: 4 })} / kg`

export default function ClientesEstoqueModal({ aberto, onClose, tipos, onSalvou, foco = 'produtoB', produto = 'PRODUTOB' }: Props) {
  const ehProdutoC = produto === 'PRODUTOC'
  const freteDe = (c: ClienteEstoque) => (ehProdutoC ? c.freteProdutoC ?? c.freteProdutoB : c.freteProdutoB) ?? 0
  const [clientes, setClientes] = useState<ClienteEstoque[]>([])
  // Três estados da lista (UI-08): falhou não vira "nenhum cliente"
  const [estado, setEstado] = useState<'carregando' | 'erro' | 'ok'>('carregando')
  const [editando, setEditando] = useState<number | 'novo' | null>(null)
  const [busca, setBusca] = useState('')
  const [salvando, setSalvando] = useState(false)

  const [nome, setNome] = useState('')
  const [cidade, setCidade] = useState('')
  const [precoBob, setPrecoBob] = useState('')
  const [frete, setFrete] = useState('')
  const [precosTipo, setPrecosTipo] = useState<Record<number, string>>({})

  const carregar = (quieto = false) => {
    if (!quieto) setEstado('carregando')
    api.get('/api/estoque/clientes')
      .then(r => { setClientes(comoLista<ClienteEstoque>(r.data, 'clientes').map(c => ({ ...c, precos: c.precos ?? [] }))); setEstado('ok') })
      .catch(() => { if (quieto) toast.error('Não consegui atualizar a lista.'); else setEstado('erro') })
  }

  useEffect(() => { if (aberto) { setEditando(null); setBusca(''); carregar() } }, [aberto])

  // Fechar com um cliente aberto para editar pergunta antes (D-17, UI-17)
  const sujo = editando !== null

  if (!aberto) return null

  const abrirNovo = () => {
    setEditando('novo'); setNome(''); setCidade(''); setPrecoBob(''); setFrete(''); setPrecosTipo({})
  }

  const abrirEdicao = (c: ClienteEstoque) => {
    setEditando(c.id)
    setNome(c.nome)
    setCidade(c.cidade ?? '')
    setPrecoBob(c.precoProdutoAKg ? String(c.precoProdutoAKg).replace('.', ',') : '')
    setFrete(String(freteDe(c)).replace('.', ','))
    const mapa: Record<number, string> = {}
    for (const p of c.precos) mapa[p.tipoProdutoBId] = String(p.preco).replace('.', ',')
    setPrecosTipo(mapa)
  }

  const salvar = async () => {
    if (!nome.trim()) { toast.error('Informe o nome do cliente.'); return }
    setSalvando(true)
    try {
      const corpo = {
        nome: nome.trim(),
        cidade: cidade.trim() || null,
        precoProdutoAKg: lerNum(precoBob),
        ...(ehProdutoC ? { freteProdutoC: lerNum(frete) } : { freteProdutoB: lerNum(frete) }),
        precos: tipos.map(t => ({ tipoProdutoBId: t.id, preco: lerNum(precosTipo[t.id] ?? '') })),
      }
      if (editando === 'novo') await api.post('/api/estoque/clientes', corpo)
      else await api.put(`/api/estoque/clientes/${editando}`, corpo)

      toast.success('Cliente salvo!')
      setEditando(null)
      carregar(true)
      onSalvou()
    } catch {
      toast.error('Não consegui salvar o cliente.')
    } finally {
      setSalvando(false)
    }
  }

  const filtrados = clientes
    .filter(c => c.nome.toLowerCase().includes(busca.trim().toLowerCase()))
    // Na tela de ProdutosA, quem tem preço por kg vem primeiro
    .sort((a, b) => foco === 'produtoA' ? Number(b.precoProdutoAKg > 0) - Number(a.precoProdutoAKg > 0) : 0)

  return (
    <Modal titulo="Clientes e preços" descricao="O que cada cliente paga. O custo vem da carga. Os preços de todos os clientes também estão no painel Preços." largura="lg"
           onClose={onClose} sujo={sujo} classeCorpo="px-4 pb-4 space-y-2">
        {/* A busca fica presa no topo enquanto a lista rola */}
        <div className="sticky top-0 z-10 -mx-4 px-4 py-3 bg-white border-b border-gray-100 flex gap-2">
          <div className="relative flex-1 min-w-0">
            <Search size={16} aria-hidden className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" />
            <input
              value={busca}
              onChange={e => setBusca(e.target.value)}
              placeholder="Buscar cliente"
              aria-label="Buscar cliente"
              type="search"
              data-nao-suja
              className={`${classeCampo} pl-9`}
            />
          </div>
          <Botao variante="primario" icone={Plus} onClick={abrirNovo}>Novo</Botao>
        </div>

          {estado === 'carregando' && <Carregando linhas={4} rotulo="Carregando os clientes" />}
          {estado === 'erro' && <ErroCarga mensagem="Não consegui carregar os clientes." onTentar={() => carregar()} />}

          {editando === 'novo' && (
            <Formulario
              titulo="Novo cliente"
              nome={nome} setNome={setNome}
              cidade={cidade} setCidade={setCidade}
              precoBob={precoBob} setPrecoBob={setPrecoBob}
              frete={frete} setFrete={setFrete}
              tipos={tipos} precosTipo={precosTipo} setPrecosTipo={setPrecosTipo}
              salvando={salvando} onSalvar={salvar} onCancelar={() => setEditando(null)}
            />
          )}

          {estado === 'ok' && filtrados.length === 0 && editando !== 'novo' && (
            busca.trim()
              ? <Vazio compacto icone={Search} titulo={`Nenhum cliente com "${busca.trim()}"`}>Confira a escrita ou cadastre em Novo.</Vazio>
              : <Vazio compacto icone={Users} titulo="Nenhum cliente cadastrado ainda"
                       acao={<Botao variante="primario" icone={Plus} onClick={abrirNovo}>Cadastrar cliente</Botao>}>
                  Cadastre o cliente com o preço que ele paga.
                </Vazio>
          )}

          {estado === 'ok' && filtrados.map(c => editando === c.id ? (
            <Formulario
              key={c.id}
              titulo={`Editando ${c.nome}`}
              nome={nome} setNome={setNome}
              cidade={cidade} setCidade={setCidade}
              precoBob={precoBob} setPrecoBob={setPrecoBob}
              frete={frete} setFrete={setFrete}
              tipos={tipos} precosTipo={precosTipo} setPrecosTipo={setPrecosTipo}
              salvando={salvando} onSalvar={salvar} onCancelar={() => setEditando(null)}
            />
          ) : (
            <CartaoCliente key={c.id} c={c} tipos={tipos} foco={foco} frete={freteDe(c)} onClick={() => abrirEdicao(c)} />
          ))}
    </Modal>
  )
}

// Um cliente na lista: o preço da produtoA por kg sempre escrito por extenso
// ("R$ 21,13 / kg", EST-06), em destaque na tela de ProdutosA; os das produtosB
// em seguida. Tocar abre a edição.
function CartaoCliente({ c, tipos, foco, frete, onClick }: {
  c: ClienteEstoque; tipos: TipoProdutoB[]; foco: 'produtoA' | 'produtoB'; frete: number; onClick: () => void
}) {
  const temProdutoA = c.precoProdutoAKg > 0
  const destaqueProdutoA = foco === 'produtoA'
  const produtoA = (
    <div className="flex items-baseline justify-between gap-3">
      <span className="text-sm text-gray-600">ProdutoA</span>
      {temProdutoA
        ? <span className={`tabular-nums font-semibold text-gray-900 ${destaqueProdutoA ? 'font-display text-xl' : 'text-sm'}`}>{brlKg(c.precoProdutoAKg)}</span>
        : <span className={`text-sm ${destaqueProdutoA ? 'font-medium text-amber-800' : 'text-gray-500'}`}>sem preço por kg</span>}
    </div>
  )
  const produtosB = tipos.length > 0 && (
    <div>
      <p className="text-xs text-gray-500 mb-1">Por pacote</p>
      <dl className="grid grid-cols-2 sm:grid-cols-3 gap-1.5">
        {tipos.map(t => {
          const p = c.precos.find(x => x.tipoProdutoBId === t.id)?.preco ?? 0
          return (
            <div key={t.id} className="bg-white border border-gray-200 rounded-lg px-2.5 py-1.5 min-w-0">
              <dt className="text-xs text-gray-500 truncate">{t.nome}</dt>
              <dd className={`text-sm tabular-nums ${p > 0 ? 'font-semibold text-gray-900' : 'text-gray-500'}`}>{p > 0 ? brl(p) : 'sem preço'}</dd>
            </div>
          )
        })}
        <div className="bg-white border border-gray-200 rounded-lg px-2.5 py-1.5 min-w-0">
          <dt className="text-xs text-gray-500">Frete</dt>
          <dd className={`text-sm tabular-nums ${frete > 0 ? 'font-semibold text-gray-900' : 'text-gray-500'}`}>{frete > 0 ? brl(frete) : 'não paga'}</dd>
        </div>
      </dl>
    </div>
  )

  return (
    <button type="button" onClick={onClick} aria-label={`Editar os preços de ${c.nome}`}
            className={`group w-full text-left border rounded-xl px-3.5 py-3 space-y-2.5 transition-colors
              ${c.ativo ? 'bg-gray-50 border-gray-200 hover:bg-gray-100 hover:border-gray-300' : 'bg-white border-dashed border-gray-300'}`}>
      <div className="flex items-start justify-between gap-3">
        <p className="min-w-0">
          <span className="block font-semibold text-gray-900 truncate">{c.nome}</span>
          {(c.cidade || !c.ativo) && (
            <span className="block text-sm text-gray-500">
              {c.cidade}{c.cidade && !c.ativo ? ' · ' : ''}{!c.ativo && 'inativo'}
            </span>
          )}
        </p>
        <Pencil size={16} aria-hidden className="mt-1 shrink-0 text-gray-500 opacity-60 group-hover:opacity-100 transition-opacity" />
      </div>
      {destaqueProdutoA ? <>{produtoA}{produtosB}</> : <>{produtosB}{produtoA}</>}
    </button>
  )
}

function Formulario(props: {
  titulo: string
  nome: string; setNome: (v: string) => void
  cidade: string; setCidade: (v: string) => void
  precoBob: string; setPrecoBob: (v: string) => void
  frete: string; setFrete: (v: string) => void
  tipos: TipoProdutoB[]
  precosTipo: Record<number, string>; setPrecosTipo: (v: Record<number, string>) => void
  salvando: boolean; onSalvar: () => void; onCancelar: () => void
}) {
  const { titulo, nome, setNome, cidade, setCidade, precoBob, setPrecoBob, frete, setFrete,
          tipos, precosTipo, setPrecosTipo } = props

  return (
    <div className="border border-tinta-200 bg-tinta-50/40 rounded-xl p-3.5 space-y-3 animate-entrada">
      <p className="font-display text-base font-semibold text-gray-900">{titulo}</p>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Nome">
          <input value={nome} onChange={e => setNome(e.target.value)}
                 placeholder="Ex.: BEM SERVIDO" className={classeCampo} />
        </Campo>

        <Campo rotulo="Cidade" dica="Sai no romaneio.">
          <input value={cidade} onChange={e => setCidade(e.target.value)}
                 placeholder="Ex.: CIDADE" className={classeCampo} />
        </Campo>
      </div>

      <Campo rotulo="ProdutoA: R$ por kg" dica="Até 4 casas: 20,4575.">
        <input value={precoBob} onChange={e => setPrecoBob(e.target.value)}
               inputMode="decimal" placeholder="Ex.: 21,13" className={`${classeNumero} sm:max-w-[12rem]`} />
      </Campo>

      {tipos.length > 0 && (
        <fieldset>
          <legend className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">
            Venda: R$ por pacote
          </legend>
          <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
            {tipos.map(t => (
              <Campo key={t.id} rotulo={<span className="font-normal text-gray-600">{t.nome}</span>}>
                <input
                  value={precosTipo[t.id] ?? ''}
                  onChange={e => setPrecosTipo({ ...precosTipo, [t.id]: e.target.value })}
                  inputMode="decimal" placeholder="Ex.: 96,02" className={classeNumero}
                />
              </Campo>
            ))}
            <Campo rotulo={<span className="font-normal text-gray-600">Frete por pacote</span>} dica="0 para quem não paga.">
              <input value={frete} onChange={e => setFrete(e.target.value)}
                     inputMode="decimal" placeholder="0,00" className={classeNumero} />
            </Campo>
          </div>
        </fieldset>
      )}

      <div className="flex gap-2 pt-1">
        <Botao variante="primario" onClick={props.onSalvar} carregando={props.salvando} textoCarregando="Salvando..." className="flex-1 sm:flex-none">
          Salvar
        </Botao>
        <Botao variante="fantasma" onClick={props.onCancelar}>Cancelar</Botao>
      </div>
    </div>
  )
}
