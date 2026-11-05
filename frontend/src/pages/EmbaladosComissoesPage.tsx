import { useEffect, useMemo, useState } from 'react'
import { HandCoins, Plus, Search } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { classeCampo } from '../components/EstoquePecas'
import { brl, dataBr, hoje, num } from '../utils/formato'
import { embaladosApi, type Comissao } from '../services/embaladosApi'
import {
  FormLancamento, ImportarPlanilhaEmbalados, ListaLancamentos, ResumoEmbalados, erroDe, numOuNulo, paraCampo, usePeriodo,
  type Coluna,
} from '../components/embalados/EmbaladosPecas'

// Comissões (VOLUME-18): o que era a aba COMISSÕES. Por venda: comissão por caixa =
// venda − compra − frete. No topo, o resumo do período: comissões − salários −
// despesas − quebras = sobra, dividida por 3.

export default function EmbaladosComissoesPage() {
  const { periodo, rotulo, filtro } = usePeriodo()
  const [itens, setItens] = useState<Comissao[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [editando, setEditando] = useState<Comissao | 'nova' | null>(null)
  const [busca, setBusca] = useState('')
  const [versao, setVersao] = useState(0)
  const confirmar = useConfirmar()

  const carregar = () => {
    setFalhou(false)
    embaladosApi.comissoes(periodo).then(setItens).catch(() => setFalhou(true))
    setVersao(v => v + 1)
  }
  useEffect(() => { setItens(null); carregar() }, [periodo.de, periodo.ate])

  const filtrados = useMemo(() => {
    const b = busca.trim().toUpperCase()
    return (itens ?? []).filter(c => !b || c.cliente.toUpperCase().includes(b) || (c.tipo ?? '').toUpperCase().includes(b))
  }, [itens, busca])
  const sugere = (f: (c: Comissao) => string | null) => [...new Set((itens ?? []).map(f).filter((x): x is string => !!x))].slice(0, 60)

  const excluir = async (c: Comissao) => {
    if (!await confirmar({ titulo: `Excluir a venda de ${c.cliente} de ${dataBr(c.data)}?`, confirmar: 'Excluir' })) return
    try { await embaladosApi.excluirComissao(c.id); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const preco = (v: number | null) => v == null ? <span className="text-amber-800 font-medium">falta</span> : brl(v)
  const colunas: Coluna<Comissao>[] = [
    { rotulo: 'Dia', valor: c => dataBr(c.data), soTabela: true },
    { rotulo: 'Cliente', valor: c => c.cliente, soTabela: true },
    { rotulo: 'Tipo', valor: c => c.tipo ?? '' },
    { rotulo: 'Caixas', direita: true, valor: c => c.caixas ? num(c.caixas, c.caixas % 1 ? 1 : 0) : <span className="text-amber-800 font-medium">falta</span> },
    { rotulo: 'Compra', direita: true, valor: c => c.tipo ? preco(c.precoCompra) : '' },
    { rotulo: 'Venda', direita: true, valor: c => preco(c.precoVenda) },
    { rotulo: 'Frete', direita: true, valor: c => brl(c.frete) },
    { rotulo: 'Por caixa', direita: true, valor: c => brl(c.porCaixa) },
    { rotulo: 'Comissão', direita: true, valor: c => <span className="font-medium">{brl(c.total)}</span> },
    { rotulo: 'Fornecedor', valor: c => [c.fornecedor, c.observacao].filter(Boolean).join(' · ') },
  ]
  const total = filtrados.reduce((s, c) => s + c.total, 0)
  const caixas = filtrados.reduce((s, c) => s + c.caixas, 0)

  return (
    <div className="space-y-4 max-w-6xl">
      <CabecalhoTela
        descricao="Cada venda com o preço de compra, de venda e o frete. A comissão por caixa é venda − compra − frete."
        acoes={<div className="flex flex-wrap gap-2">
          <ImportarPlanilhaEmbalados onPronto={carregar} />
          <Botao variante="primario" icone={Plus} onClick={() => setEditando('nova')}>Lançar venda</Botao>
        </div>}
      />
      <div className="flex flex-wrap items-center gap-2">
        {filtro}
        <label className="relative flex-1 min-w-[12rem] max-w-xs">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500" aria-hidden />
          <input value={busca} onChange={e => setBusca(e.target.value)} placeholder="Cliente ou tipo" aria-label="Buscar cliente ou tipo"
                 className={`${classeCampo} pl-9`} data-nao-suja />
        </label>
      </div>

      <ResumoEmbalados periodo={periodo} rotulo={rotulo} versao={versao} />

      {falhou ? <ErroCarga mensagem="Não consegui carregar as comissões." onTentar={carregar} />
        : itens == null ? <Carregando linhas={6} rotulo="Carregando as comissões" />
        : filtrados.length === 0 ? <Vazio icone={HandCoins} titulo={busca ? 'Nada com essa busca' : `Nenhuma venda em ${rotulo.toLowerCase()}`}>
            {busca ? 'Tente outro nome.' : 'Lance a primeira ou importe a planilha.'}
          </Vazio>
        : (
          <ListaLancamentos itens={filtrados} colunas={colunas} chave={c => c.id}
                            titulo={c => <>{c.cliente} <span className="font-normal text-gray-500">· {dataBr(c.data)}</span></>}
                            onEditar={setEditando} onExcluir={excluir}
                            rodape={<p className="text-right font-semibold text-gray-900 tabular-nums">
                              {filtrados.length} vendas · {num(caixas, 0)} caixas · {brl(total)}
                            </p>} />
        )}

      {editando && (() => {
        const c = editando === 'nova' ? null : editando
        return (
          <FormLancamento
            titulo={c ? 'Corrigir venda' : 'Lançar venda'} onClose={() => setEditando(null)}
            inicial={{
              data: c?.data ?? hoje(), cliente: c?.cliente ?? '', tipo: c?.tipo ?? '', caixas: paraCampo(c?.caixas),
              compra: paraCampo(c?.precoCompra), venda: paraCampo(c?.precoVenda), frete: paraCampo(c?.frete ?? 9),
              fornecedor: c?.fornecedor ?? 'Paulo Alfa', observacao: c?.observacao ?? '',
            }}
            campos={[
              { chave: 'data', rotulo: 'Dia', tipo: 'data', obrigatorio: true },
              { chave: 'cliente', rotulo: 'Cliente', tipo: 'texto', obrigatorio: true, sugestoes: sugere(x => x.cliente) },
              { chave: 'tipo', rotulo: 'Tipo', tipo: 'texto', sugestoes: sugere(x => x.tipo) },
              { chave: 'caixas', rotulo: 'Caixas', tipo: 'numero', obrigatorio: true },
              { chave: 'compra', rotulo: 'Preço de compra', tipo: 'dinheiro' },
              { chave: 'venda', rotulo: 'Preço de venda', tipo: 'dinheiro' },
              { chave: 'frete', rotulo: 'Frete por caixa', tipo: 'dinheiro' },
              { chave: 'fornecedor', rotulo: 'Fornecedor', tipo: 'texto', sugestoes: sugere(x => x.fornecedor) },
              { chave: 'observacao', rotulo: 'Observação', tipo: 'texto', largo: true },
            ]}
            onSalvar={async v => {
              await embaladosApi.salvarComissao(c?.id ?? null, {
                data: v.data, cliente: v.cliente, tipo: v.tipo || null, caixas: numOuNulo(v.caixas) ?? 0,
                precoCompra: numOuNulo(v.compra), precoVenda: numOuNulo(v.venda), frete: numOuNulo(v.frete) ?? 0,
                fornecedor: v.fornecedor || null, observacao: v.observacao || null,
              })
              carregar()
            }}
          />
        )
      })()}
    </div>
  )
}
