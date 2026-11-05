import { useEffect, useMemo, useState } from 'react'
import { Plus, Wallet } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { brl } from '../utils/formato'
import { embaladosApi, type Salario } from '../services/embaladosApi'
import {
  FormLancamento, ListaLancamentos, dinheiroOuFalta, erroDe, nomeMes, numOuNulo, paraCampo, usePeriodo, type Coluna,
} from '../components/embalados/EmbaladosPecas'

// Salários do barracão (VOLUME-18): o que era a aba PGTOS SALÀRIOS — por mês, cada
// pagamento (Marta, diárias, Admin) com INSS e FGTS.

const soma = (s: Salario) => (s.valor ?? 0) + (s.inss ?? 0) + (s.fgts ?? 0)

export default function EmbaladosSalariosPage() {
  const { periodo, rotulo, filtro } = usePeriodo()
  const [itens, setItens] = useState<Salario[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [editando, setEditando] = useState<Salario | 'novo' | null>(null)
  const confirmar = useConfirmar()

  const carregar = () => {
    setFalhou(false)
    embaladosApi.salarios(periodo).then(setItens).catch(() => setFalhou(true))
  }
  useEffect(() => { setItens(null); carregar() }, [periodo.de, periodo.ate])

  const referencias = useMemo(() => [...new Set((itens ?? []).map(s => s.referencia))], [itens])

  const excluir = async (s: Salario) => {
    if (!await confirmar({ titulo: `Excluir "${s.referencia}" de ${nomeMes(s.mes).toLowerCase()}?`, confirmar: 'Excluir' })) return
    try { await embaladosApi.excluirSalario(s.id); carregar() }
    catch (e) { toast.error(erroDe(e, 'Não consegui excluir.')) }
  }

  const colunas: Coluna<Salario>[] = [
    { rotulo: 'Mês', valor: s => nomeMes(s.mes), soTabela: true },
    { rotulo: 'Referência', valor: s => s.referencia, soTabela: true },
    { rotulo: 'Valor', direita: true, valor: s => dinheiroOuFalta(s.valor) },
    { rotulo: 'INSS', direita: true, valor: s => s.inss == null ? '' : brl(s.inss) },
    { rotulo: 'FGTS', direita: true, valor: s => s.fgts == null ? '' : brl(s.fgts) },
    { rotulo: 'Total', direita: true, valor: s => <span className="font-medium">{brl(soma(s))}</span> },
    { rotulo: 'Observação', valor: s => s.observacao ?? '' },
  ]
  const total = (itens ?? []).reduce((t, s) => t + soma(s), 0)

  return (
    <div className="space-y-4 max-w-5xl">
      <CabecalhoTela
        descricao="Pagamentos do barracão por mês: salários e diárias, com INSS e FGTS."
        acoes={<Botao variante="primario" icone={Plus} onClick={() => setEditando('novo')}>Lançar pagamento</Botao>}
      />
      {filtro}
      {falhou ? <ErroCarga mensagem="Não consegui carregar os salários." onTentar={carregar} />
        : itens == null ? <Carregando linhas={5} rotulo="Carregando os salários" />
        : itens.length === 0 ? <Vazio icone={Wallet} titulo={`Nenhum pagamento em ${rotulo.toLowerCase()}`}>Lance o primeiro no botão acima.</Vazio>
        : (
          <ListaLancamentos itens={itens} colunas={colunas} chave={s => s.id}
                            titulo={s => <>{s.referencia} <span className="font-normal text-gray-500">· {nomeMes(s.mes)}</span></>}
                            onEditar={setEditando} onExcluir={excluir}
                            rodape={<p className="text-right font-semibold text-gray-900 tabular-nums">Total com INSS e FGTS: {brl(total)}</p>} />
        )}
      {editando && (() => {
        const s = editando === 'novo' ? null : editando
        const agora = new Date()
        return (
          <FormLancamento
            titulo={s ? 'Corrigir pagamento' : 'Lançar pagamento'} onClose={() => setEditando(null)}
            inicial={{
              mes: (s?.mes ?? `${agora.getFullYear()}-${String(agora.getMonth() + 1).padStart(2, '0')}-01`).slice(0, 7),
              referencia: s?.referencia ?? '', valor: paraCampo(s?.valor), inss: paraCampo(s?.inss), fgts: paraCampo(s?.fgts),
              observacao: s?.observacao ?? '',
            }}
            campos={[
              { chave: 'mes', rotulo: 'Mês', tipo: 'mes', obrigatorio: true },
              { chave: 'referencia', rotulo: 'Referência', tipo: 'texto', obrigatorio: true, sugestoes: referencias, dica: 'Ex.: SALÁRIO MARTA, SALÁRIOS DIÁRIAS' },
              { chave: 'valor', rotulo: 'Valor', tipo: 'dinheiro' },
              { chave: 'inss', rotulo: 'INSS', tipo: 'dinheiro' },
              { chave: 'fgts', rotulo: 'FGTS', tipo: 'dinheiro' },
              { chave: 'observacao', rotulo: 'Observação', tipo: 'texto' },
            ]}
            onSalvar={async v => {
              await embaladosApi.salvarSalario(s?.id ?? null, {
                mes: `${v.mes}-01`, referencia: v.referencia, valor: numOuNulo(v.valor), inss: numOuNulo(v.inss),
                fgts: numOuNulo(v.fgts), observacao: v.observacao || null,
              })
              carregar()
            }}
          />
        )
      })()}
    </div>
  )
}
