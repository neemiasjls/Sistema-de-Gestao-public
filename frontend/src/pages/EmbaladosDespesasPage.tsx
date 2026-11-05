import { useEffect, useMemo, useState } from 'react'
import { Plus, ShoppingBag } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { brl, dataBr, hoje } from '../utils/formato'
import { embaladosApi, type Despesa } from '../services/embaladosApi'
import {
  FormLancamento, ListaLancamentos, dinheiroOuFalta, erroDe, numOuNulo, paraCampo, usePeriodo, type Coluna,
} from '../components/embalados/EmbaladosPecas'

// Despesas das produtosB (VOLUME-18): o que era a aba DESP. EMBAL. — produtosA,
// fitas, lanche e limpeza do barracão.

export default function EmbaladosDespesasPage() {
  const { periodo, rotulo, filtro } = usePeriodo()
  const [itens, setItens] = useState<Despesa[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [editando, setEditando] = useState<Despesa | 'nova' | null>(null)
  const confirmar = useConfirmar()

  const carregar = () => {
    setFalhou(false)
    embaladosApi.despesas(periodo).then(setItens).catch(() => setFalhou(true))
  }
  useEffect(() => { setItens(null); carregar() }, [periodo.de, periodo.ate])

  const sugestoes = useMemo(() => [...new Set((itens ?? []).map(d => d.descricao))].slice(0, 40), [itens])

  const excluir = async (d: Despesa) => {
    if (!await confirmar({ titulo: `Excluir "${d.descricao}" de ${dataBr(d.data)}?`, confirmar: 'Excluir' })) return
    try { await embaladosApi.excluirDespesa(d.id); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const colunas: Coluna<Despesa>[] = [
    { rotulo: 'Dia', valor: d => dataBr(d.data) },
    { rotulo: 'O que foi', valor: d => d.descricao, soTabela: true },
    { rotulo: 'Valor', direita: true, valor: d => dinheiroOuFalta(d.valor) },
    { rotulo: 'Observação', valor: d => d.observacao ?? '' },
  ]
  const total = (itens ?? []).reduce((s, d) => s + (d.valor ?? 0), 0)

  return (
    <div className="space-y-4 max-w-4xl">
      <CabecalhoTela
        descricao="Tudo o que se gasta para embalar: produtosA, fitas, lanche e limpeza do barracão."
        acoes={<Botao variante="primario" icone={Plus} onClick={() => setEditando('nova')}>Lançar despesa</Botao>}
      />
      {filtro}
      {falhou ? <ErroCarga mensagem="Não consegui carregar as despesas." onTentar={carregar} />
        : itens == null ? <Carregando linhas={5} rotulo="Carregando as despesas" />
        : itens.length === 0 ? <Vazio icone={ShoppingBag} titulo={`Nenhuma despesa em ${rotulo.toLowerCase()}`}>Lance a primeira no botão acima.</Vazio>
        : (
          <ListaLancamentos itens={itens} colunas={colunas} chave={d => d.id} titulo={d => d.descricao}
                            onEditar={setEditando} onExcluir={excluir}
                            rodape={<p className="text-right font-semibold text-gray-900 tabular-nums">{itens.length} despesas · {brl(total)}</p>} />
        )}
      {editando && (() => {
        const d = editando === 'nova' ? null : editando
        return (
          <FormLancamento
            titulo={d ? 'Corrigir despesa' : 'Lançar despesa'} onClose={() => setEditando(null)}
            inicial={{ data: d?.data ?? hoje(), descricao: d?.descricao ?? '', valor: paraCampo(d?.valor), observacao: d?.observacao ?? '' }}
            campos={[
              { chave: 'data', rotulo: 'Dia', tipo: 'data', obrigatorio: true },
              { chave: 'valor', rotulo: 'Valor', tipo: 'dinheiro' },
              { chave: 'descricao', rotulo: 'O que foi', tipo: 'texto', obrigatorio: true, largo: true, sugestoes },
              { chave: 'observacao', rotulo: 'Observação', tipo: 'texto', largo: true },
            ]}
            onSalvar={async v => {
              await embaladosApi.salvarDespesa(d?.id ?? null, { data: v.data, descricao: v.descricao, valor: numOuNulo(v.valor), observacao: v.observacao || null })
              carregar()
            }}
          />
        )
      })()}
    </div>
  )
}
