import { useEffect, useState } from 'react'
import { HandCoins, Plus, ShoppingBag } from 'lucide-react'
import toast from 'react-hot-toast'
import api from '../services/api'
import { comoLista } from '../utils/resposta'
import { brl, dataBr, hoje } from '../utils/formato'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import ImportarLancamentos from '../components/ImportarLancamentos'
import {
  FormLancamento, ListaLancamentos, dinheiroOuFalta, erroDe, numOuNulo, paraCampo, usePeriodo, type Coluna,
} from '../components/embalados/EmbaladosPecas'

// Sem zeros sobrando: 5 pacotes, 154,36 kg, 4,4567 de comissão
const qtd = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 4 })

type Lancamento = {
  id: number; data: string; cliente?: string; descricao?: string; produto?: string | null;
  quantidade?: number; valor: number | null; total?: number | null; fornecedor?: string | null; observacao: string | null
}

export default function BarracaoPage({ tipo }: { tipo: 'comissoes' | 'despesas' }) {
  const comissao = tipo === 'comissoes'
  const { periodo, rotulo, filtro } = usePeriodo()
  const [itens, setItens] = useState<Lancamento[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [versao, setVersao] = useState(0)
  const [editando, setEditando] = useState<Lancamento | 'novo' | null>(null)
  const confirmar = useConfirmar()
  const caminho = `/api/barracao/${tipo}`
  useEffect(() => {
    let atual = true
    setItens(null); setFalhou(false)
    api.get(caminho, { params: periodo }).then(r => { if (atual) setItens(comoLista<Lancamento>(r.data, tipo)) })
      .catch(() => { if (atual) setFalhou(true) })
    return () => { atual = false }
  }, [caminho, periodo.de, periodo.ate, versao])
  const carregar = () => setVersao(v => v + 1)
  const excluir = async (l: Lancamento) => {
    if (!await confirmar({ titulo: `Excluir ${l.cliente ?? l.descricao} de ${dataBr(l.data)}?`, confirmar: 'Excluir' })) return
    try { await api.delete(`${caminho}/${l.id}`); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }
  const colunas: Coluna<Lancamento>[] = [
    { rotulo: 'Dia', valor: l => dataBr(l.data) },
    { rotulo: comissao ? 'Cliente' : 'Referência', valor: l => l.cliente ?? l.descricao ?? '', soTabela: true },
    ...(comissao ? [
      { rotulo: 'Produto', valor: (l: Lancamento) => l.produto ?? '' },
      { rotulo: 'Quantidade', direita: true, valor: (l: Lancamento) => qtd(l.quantidade ?? 0) },
      { rotulo: 'Comissão por unidade', direita: true, valor: (l: Lancamento) => dinheiroOuFalta(l.valor) },
      { rotulo: 'Fornecedor', valor: (l: Lancamento) => l.fornecedor ?? '' },
    ] : []),
    { rotulo: comissao ? 'Comissão total' : 'Valor', direita: true, valor: l => dinheiroOuFalta(comissao ? l.total ?? null : l.valor) },
    { rotulo: 'Observação', valor: l => l.observacao ?? '' },
  ]
  const total = (itens ?? []).reduce((s, l) => s + (comissao ? l.total ?? 0 : l.valor ?? 0), 0)
  return <div className="space-y-4 max-w-6xl">
    <CabecalhoTela descricao={comissao ? 'Comissões das vendas de produtosB e produtosA, por cliente. Quantidade em pacotes ou quilogramas, conforme o produto.' : 'Despesas do barracão, por dia e referência.'}
      acoes={<div className="flex flex-wrap gap-2">
        <ImportarLancamentos caminho="/api/barracao" titulo="barracão" onPronto={carregar} />
        <Botao variante="primario" icone={Plus} onClick={() => setEditando('novo')}>{comissao ? 'Lançar comissão' : 'Lançar despesa'}</Botao>
      </div>} />
    {filtro}
    {falhou ? <ErroCarga mensagem="Não consegui carregar os lançamentos." onTentar={carregar} />
      : itens == null ? <Carregando linhas={6} />
      : itens.length === 0 ? <Vazio icone={comissao ? HandCoins : ShoppingBag} titulo={`Nenhum lançamento em ${rotulo.toLowerCase()}`}>Importe a planilha ou lance pelo botão acima.</Vazio>
      : <ListaLancamentos itens={itens} colunas={colunas} chave={l => l.id} titulo={l => l.cliente ?? l.descricao}
        onEditar={setEditando} onExcluir={excluir} rodape={<p className="text-right font-semibold text-gray-900">{itens.length} lançamentos · {brl(total)}</p>} />}
    {editando && (() => {
      const l = editando === 'novo' ? null : editando
      return <FormLancamento titulo={l ? 'Corrigir lançamento' : comissao ? 'Lançar comissão' : 'Lançar despesa'} onClose={() => setEditando(null)}
        inicial={{ data: l?.data ?? hoje(), cliente: l?.cliente ?? '', descricao: l?.descricao ?? '', produto: l?.produto ?? '',
          quantidade: l?.quantidade == null ? '' : qtd(l.quantidade), valor: l?.valor == null ? '' : qtd(l.valor), fornecedor: l?.fornecedor ?? '', observacao: l?.observacao ?? '' }}
        campos={[
          { chave: 'data', rotulo: 'Dia', tipo: 'data', obrigatorio: true },
          { chave: 'valor', rotulo: comissao ? 'Comissão por unidade' : 'Valor', tipo: 'dinheiro' },
          ...(comissao ? [
            { chave: 'cliente', rotulo: 'Cliente', tipo: 'texto' as const, obrigatorio: true, largo: true },
            { chave: 'produto', rotulo: 'Produto', tipo: 'texto' as const, sugestoes: ['Amarelas', 'Brancas', 'ProdutosB', 'ProdutosA'] },
            { chave: 'quantidade', rotulo: 'Quantidade', tipo: 'numero' as const, obrigatorio: true },
            { chave: 'fornecedor', rotulo: 'Fornecedor', tipo: 'texto' as const },
          ] : [{ chave: 'descricao', rotulo: 'Referência', tipo: 'texto' as const, obrigatorio: true, largo: true }]),
          { chave: 'observacao', rotulo: 'Observação', tipo: 'texto', largo: true },
        ]}
        onSalvar={async v => {
          const dados = comissao ? { data: v.data, cliente: v.cliente, produto: v.produto || null, quantidade: numOuNulo(v.quantidade) ?? 0,
            valor: numOuNulo(v.valor), fornecedor: v.fornecedor || null, observacao: v.observacao || null }
            : { data: v.data, descricao: v.descricao, valor: numOuNulo(v.valor), observacao: v.observacao || null }
          if (l) await api.put(`${caminho}/${l.id}`, dados); else await api.post(caminho, dados)
          carregar()
        }} />
    })()}
  </div>
}
