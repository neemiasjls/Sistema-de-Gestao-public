import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Egg, Plus } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { brl, dataBr, hoje, num } from '../utils/formato'
import { embaladosApi, nomeCor, type Cor, type Quebra } from '../services/embaladosApi'
import {
  FormLancamento, ListaLancamentos, erroDe, numOuNulo, paraCampo, usePeriodo, type Coluna, type Valores,
} from '../components/embalados/EmbaladosPecas'

// Quebras (VOLUME-17): as produtosC vêm dos controles de embalados; o preço da
// caixa, do romaneio de embalar (sem funrural) menos o desconto por caixa do
// acerto. Valor = preço ÷ 12 × produtosC. Dia sem controle: lança aqui.

const DE_PRECO: Record<Quebra['dePreco'], string> = {
  romaneio: 'do romaneio', planilha: 'da planilha', manual: 'digitado', falta: '',
}

export default function EmbaladosQuebrasPage() {
  const { periodo, rotulo, filtro } = usePeriodo()
  const [itens, setItens] = useState<Quebra[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [editando, setEditando] = useState<Quebra | 'nova' | null>(null)
  const confirmar = useConfirmar()

  const carregar = () => {
    setFalhou(false)
    embaladosApi.quebras(periodo).then(setItens).catch(() => setFalhou(true))
  }
  useEffect(() => { setItens(null); carregar() }, [periodo.de, periodo.ate])

  const excluir = async (q: Quebra) => {
    if (q.id == null) return
    if (!await confirmar({
      titulo: `Excluir a quebra de ${dataBr(q.data)} (${nomeCor(q.cor).toLowerCase()})?`,
      texto: q.controleNumero ? 'As produtosC continuam vindo do controle; sai só o preço digitado.' : undefined,
      confirmar: 'Excluir',
    })) return
    try { await embaladosApi.excluirQuebra(q.id); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const colunas: Coluna<Quebra>[] = [
    { rotulo: 'Dia', valor: q => dataBr(q.data), soTabela: true },
    { rotulo: 'Cor', valor: q => nomeCor(q.cor), soTabela: true },
    {
      rotulo: 'ProdutosC', direita: true,
      valor: q => <>{num(q.produtosC, q.produtosC % 1 ? 1 : 0)}</>,
    },
    {
      rotulo: 'Preço da caixa', direita: true,
      valor: q => q.precoCaixa == null
        ? <span className="text-amber-800 font-medium">falta</span>
        : <span title={q.dePreco === 'romaneio' && q.precoRomaneio != null
            ? `Romaneio ${q.romaneio}: ${brl(q.precoRomaneio)} − desconto ${brl(q.descontoCaixa ?? 0)}` : undefined}>
            {brl(q.precoCaixa)}
            {DE_PRECO[q.dePreco] && <span className="block text-xs text-gray-500">{DE_PRECO[q.dePreco]}{q.romaneio ? ` ${q.romaneio}` : ''}</span>}
          </span>,
    },
    { rotulo: 'Valor', direita: true, valor: q => <span className="font-medium">{brl(q.valor)}</span> },
    {
      rotulo: 'De onde', valor: q => (
        <span className="text-sm">
          {q.controleNumero
            ? <Link to={`/estoque/volumes/${q.controleId}`} className="text-tinta-700 hover:underline">controle {q.controleNumero}</Link>
            : <span className="text-gray-500">lançada aqui</span>}
          {q.aviso && <span className="block text-amber-800">{q.aviso}</span>}
          {q.observacao && <span className="block text-gray-500">{q.observacao}</span>}
        </span>
      ),
    },
  ]

  const total = (itens ?? []).reduce((s, q) => s + q.valor, 0)
  const produtosC = (itens ?? []).reduce((s, q) => s + q.produtosC, 0)

  return (
    <div className="space-y-4 max-w-5xl">
      <CabecalhoTela
        descricao="As quebras anotadas nos controles de embalados, com o preço do romaneio de embalar menos o desconto por caixa do acerto. Dia sem controle: lance aqui."
        acoes={<Botao variante="primario" icone={Plus} onClick={() => setEditando('nova')}>Lançar quebra</Botao>}
      />
      {filtro}

      {falhou ? <ErroCarga mensagem="Não consegui carregar as quebras." onTentar={carregar} />
        : itens == null ? <Carregando linhas={5} rotulo="Carregando as quebras" />
        : itens.length === 0 ? <Vazio icone={Egg} titulo={`Nenhuma quebra em ${rotulo.toLowerCase()}`}>As quebras anotadas nos controles aparecem aqui sozinhas.</Vazio>
        : (
          <ListaLancamentos
            itens={itens} colunas={colunas} chave={(q, i) => `${q.data}-${q.cor}-${i}`}
            titulo={q => <>{dataBr(q.data)} · {nomeCor(q.cor)}</>}
            onEditar={setEditando} onExcluir={excluir}
            rodape={<p className="text-right font-semibold text-gray-900 tabular-nums">
              {num(produtosC, produtosC % 1 ? 1 : 0)} produtosC · {brl(total)}
            </p>}
          />
        )}

      {editando && (
        <EditarQuebra quebra={editando === 'nova' ? null : editando} onClose={() => setEditando(null)} onPronto={carregar} />
      )}
    </div>
  )
}

function EditarQuebra({ quebra: q, onClose, onPronto }: { quebra: Quebra | null; onClose: () => void; onPronto: () => void }) {
  const doControle = !!q?.controleNumero
  const inicial: Valores = {
    data: q?.data ?? hoje(), cor: q?.cor ?? 'VERMELHO',
    produtosC: q ? paraCampo(q.produtosC) : '',
    preco: q && q.dePreco !== 'romaneio' ? paraCampo(q.precoCaixa) : '',
    observacao: q?.observacao ?? '',
  }
  const salvar = async (v: Valores) => {
    await embaladosApi.salvarQuebra({
      data: v.data, cor: v.cor as Cor,
      produtosC: doControle ? null : numOuNulo(v.produtosC),
      precoCaixa: numOuNulo(v.preco), observacao: v.observacao || null,
    })
    onPronto()
  }
  return (
    <FormLancamento
      titulo={q ? `Quebra de ${dataBr(q.data)} · ${nomeCor(q.cor)}` : 'Lançar quebra'}
      inicial={inicial} onSalvar={salvar} onClose={onClose}
      antes={doControle && (
        <p className="text-sm text-gray-600 mb-3">
          As produtosC vêm do controle {q!.controleNumero}; para mudar, corrija lá. Aqui só o preço e a observação.
          {q!.dePreco === 'romaneio' && q!.precoCaixa != null && <> Hoje o preço é {brl(q!.precoCaixa)}, do romaneio {q!.romaneio}; deixe vazio para continuar assim.</>}
        </p>
      )}
      campos={[
        ...(q ? [] : [
          { chave: 'data', rotulo: 'Dia', tipo: 'data' as const, obrigatorio: true },
          { chave: 'cor', rotulo: 'Cor', tipo: 'cor' as const },
        ]),
        ...(doControle ? [] : [{ chave: 'produtosC', rotulo: 'ProdutosC quebradas', tipo: 'numero' as const, obrigatorio: true, dica: 'Aceita meia: 6,5' }]),
        { chave: 'preco', rotulo: 'Preço da caixa', tipo: 'dinheiro', dica: 'Vazio: usa o do romaneio de embalar menos o desconto do acerto' },
        { chave: 'observacao', rotulo: 'Observação', tipo: 'texto', largo: true },
      ]}
    />
  )
}
