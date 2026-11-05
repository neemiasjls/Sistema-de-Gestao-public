import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import Botao, { classesBotao } from '../components/ui/Botao'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { PackageOpen, Plus, ChevronDown, ChevronRight, RotateCcw } from 'lucide-react'
import { dataBr } from '../utils/formato'
import {
  volumesApi, caixasEProdutosC, caixasProduzido, TODOS_CRUS, TODOS_PRODUZIDOS, ORIGEM_ANOTACAO,
  type ItemLista, type Status, type RomaneioFazenda, type Controle, type LinhaVolumes,
} from '../services/volumesEmbaladosApi'
import { CartaoRomaneiosSemControle } from '../components/RomaneiosEmbalar'
import { Selo } from '../components/VolumesPecas'

export { Selo }

const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']
const nomeMes = (iso: string) => {
  const m = MESES[Number(iso.slice(5, 7)) - 1] ?? ''
  return `${m.charAt(0).toUpperCase()}${m.slice(1)} de ${iso.slice(0, 4)}`
}

// Lista dos controles de volumes embalados (um por produtoB), mais recente em
// cima. O botão de anotar fica em destaque: é o que se faz no celular.
// Tocar no cartão abre o controle; a seta abre um resumo ali mesmo.

type Detalhe = Controle | 'carregando' | 'erro'

export default function VolumesEmbaladosPage() {
  const [itens, setItens] = useState<ItemLista[]>([])
  const [proximo, setProximo] = useState<number | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  // Romaneios da fazenda sem funrural ainda sem controle (entram ao anotar a produtoB)
  const [romaneios, setRomaneios] = useState<RomaneioFazenda[]>([])
  // Resumo aberto pela seta: o controle inteiro é buscado só na primeira vez
  const [abertos, setAbertos] = useState<Set<number>>(new Set())
  const [detalhes, setDetalhes] = useState<Record<number, Detalhe>>({})
  const navigate = useNavigate()

  // Falhou: mostra o erro com "Tentar de novo" (antes aparecia "Nenhum controle
  // ainda", como se não houvesse nada)
  const carregar = () => {
    setCarregando(true)
    setFalhou(false)
    volumesApi.lista()
      .then(r => { setItens(r.controles); setProximo(r.proximoNumero) })
      .catch(() => setFalhou(true))
      .finally(() => setCarregando(false))
    volumesApi.romaneiosFazenda().then(setRomaneios).catch(() => {})
  }
  useEffect(carregar, [])

  const alternar = (id: number) => {
    const abrir = !abertos.has(id)
    setAbertos(a => {
      const n = new Set(a)
      if (abrir) n.add(id); else n.delete(id)
      return n
    })
    if (abrir && (detalhes[id] === undefined || detalhes[id] === 'erro')) buscar(id)
  }

  const buscar = (id: number) => {
    setDetalhes(d => ({ ...d, [id]: 'carregando' }))
    volumesApi.detalhe(id)
      .then(c => setDetalhes(d => ({ ...d, [id]: c })))
      .catch(() => setDetalhes(d => ({ ...d, [id]: 'erro' })))
  }

  return (
    <div className="space-y-5 max-w-3xl">
      <CabecalhoTela
        descricao="Um controle por produtoB. Anote o que embalou; depois lance as entradas da fazenda e as saídas. A conferência tem de fechar em zero."
        acoes={
          <Link to="/estoque/volumes/anotar" className={`${classesBotao('primario', 'md', true)} sm:w-auto text-base sm:text-sm`}>
            <Plus size={18} aria-hidden /> Anotar produtoB{proximo ? ` (${proximo})` : ''}
          </Link>
        }
      />

      <CartaoRomaneiosSemControle romaneios={romaneios} />

      {carregando ? (
        <Carregando linhas={4} rotulo="Carregando os controles" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar os controles." onTentar={carregar} />
      ) : itens.length === 0 ? (
        <Vazio icone={PackageOpen} titulo="Nenhum controle ainda">Comece anotando a próxima produtoB.</Vazio>
      ) : (
        // Agrupados por mês, como as pastas das planilhas: acha o controle pela data
        <div className="space-y-5">
          {porMes(itens).map(([mes, lista]) => (
            <section key={mes} aria-label={nomeMes(mes)}>
              <h2 className="font-display text-sm font-semibold text-gray-600 mb-2 px-1 flex items-baseline justify-between">
                {nomeMes(mes)}
                <span className="font-sans font-normal text-xs text-gray-500 tabular-nums">
                  {lista.length} {lista.length === 1 ? 'controle' : 'controles'}
                </span>
              </h2>
              <ul className="space-y-2">
                {lista.map(c => (
                  <CartaoControle key={c.id} item={c} aberto={abertos.has(c.id)} onSeta={() => alternar(c.id)}
                                  detalhe={detalhes[c.id]} onAbrir={() => navigate(`/estoque/volumes/${c.id}`)}
                                  onTentar={() => buscar(c.id)} />
                ))}
              </ul>
            </section>
          ))}
        </div>
      )}
    </div>
  )
}

// Mantém a ordem da API (mais recente em cima) e junta os do mesmo mês
function porMes(itens: ItemLista[]): [string, ItemLista[]][] {
  const grupos: [string, ItemLista[]][] = []
  for (const c of itens) {
    const mes = (c.data ?? '').slice(0, 7)
    const ultimo = grupos[grupos.length - 1]
    if (ultimo && ultimo[0] === mes) ultimo[1].push(c)
    else grupos.push([mes, [c]])
  }
  return grupos
}

// Um controle da lista. Tocar em qualquer lugar do cartão abre o controle; a
// seta só abre e fecha o resumo ali mesmo (não deixa o toque chegar no cartão).
function CartaoControle({ item: c, aberto, onSeta, detalhe, onAbrir, onTentar }: {
  item: ItemLista; aberto: boolean; onSeta: () => void; detalhe: Detalhe | undefined; onAbrir: () => void; onTentar: () => void
}) {
  return (
    <li role="link" tabIndex={0} aria-label={`Abrir o controle ${c.numero}`}
        onClick={onAbrir}
        onKeyDown={e => {
          if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) { e.preventDefault(); onAbrir() }
        }}
        className={`bg-white border rounded-xl overflow-hidden cursor-pointer transition-[border-color,box-shadow] hover:border-gray-300 hover:shadow-sm
          ${aberto ? 'border-gray-300 shadow-sm' : 'border-gray-200'}`}>
      <div className="flex items-start gap-3 pl-4 pr-1.5 py-3">
        <div className="w-14 shrink-0 pt-0.5">
          <p className="font-display text-2xl font-semibold text-gray-900 tabular-nums leading-none">{c.numero}</p>
          <p className="text-sm text-gray-500 tabular-nums mt-1">{dataBr(c.data).slice(0, 5)}</p>
        </div>
        <div className="flex-1 min-w-0 pt-0.5">
          <Selo status={c.status} />
          <p className="mt-1.5 text-sm text-gray-800 tabular-nums">
            {c.embalou.length > 0
              ? c.embalou.map((e, i) => (
                  <span key={e.codigo + i} className="whitespace-nowrap">
                    {i > 0 && <span className="text-gray-400" aria-hidden> · </span>}
                    <span className="font-medium">{e.codigo}</span> {e.caixas.toLocaleString('pt-BR')}
                  </span>
                ))
              : <span className="text-amber-800">Sem o que foi embalado</span>}
          </p>
          <Resumo item={c} />
        </div>
        <button type="button" aria-expanded={aberto}
                aria-label={aberto ? `Fechar o resumo do controle ${c.numero}` : `Ver o resumo do controle ${c.numero}`}
                title={aberto ? 'Fechar o resumo' : 'Ver o resumo aqui'}
                onClick={e => { e.stopPropagation(); onSeta() }}
                onKeyDown={e => e.stopPropagation()}
                className={`w-11 h-11 shrink-0 flex items-center justify-center rounded-lg transition-colors
                  ${aberto ? 'bg-gray-100 text-gray-800' : 'text-gray-500 hover:bg-gray-100 hover:text-gray-800'}`}>
          <ChevronDown size={20} aria-hidden className={`transition-transform duration-200 ${aberto ? 'rotate-180' : ''}`} />
        </button>
      </div>
      {aberto && <ResumoAberto item={c} detalhe={detalhe} onTentar={onTentar} />}
    </li>
  )
}

const porCaixaDe = (chave: string) => TODOS_PRODUZIDOS.find(t => t.chave === chave)?.porCaixa ?? 12
const porCaixaDoCodigo = (codigo: string) => TODOS_PRODUZIDOS.find(t => t.codigo === codigo)?.porCaixa ?? 12

// Soma das linhas de um grupo, por tipo cru (em produtosC)
const somaCrus = (linhas: LinhaVolumes[]) =>
  TODOS_CRUS.map(t => ({ nome: t.nome, v: linhas.reduce((s, l) => s + (l[t.chave] ?? 0), 0) })).filter(x => x.v !== 0)

// O resumo do controle: o que embalou, entradas, saídas e vendas, sobra, quebra
// e a conferência por tipo. Caixa crua sempre em caixas e produtosC (VOLUME-09).
function ResumoAberto({ item, detalhe, onTentar }: { item: ItemLista; detalhe: Detalhe | undefined; onTentar: () => void }) {
  if (detalhe === undefined || detalhe === 'carregando') {
    return (
      <div role="status" className="border-t border-gray-100 bg-gray-50 px-4 py-3.5 space-y-2.5">
        <span className="sr-only">Buscando o resumo...</span>
        {[70, 55, 62].map(w => <div key={w} className="esqueleto h-3.5" style={{ width: `${w}%` }} aria-hidden />)}
      </div>
    )
  }
  if (detalhe === 'erro') {
    return (
      <div role="alert" className="border-t border-gray-100 bg-red-50 px-4 py-3 flex items-center justify-between gap-3"
           onClick={e => e.stopPropagation()}>
        <p className="text-sm text-red-800">Não consegui buscar o resumo.</p>
        <Botao variante="secundario" tamanho="sm" icone={RotateCcw} onClick={onTentar}>Tentar de novo</Botao>
      </div>
    )
  }

  const c = detalhe
  const conf = c.conferencia
  const vendas = c.saidas.filter(l => l.origem === ORIGEM_ANOTACAO)
  const outrasSaidas = c.saidas.filter(l => l.origem !== ORIGEM_ANOTACAO)
  const crusDe = (l: LinhaVolumes | null) => TODOS_CRUS.filter(t => (l?.[t.chave] ?? 0) !== 0)
  const juntar = (partes: string[]) => (partes.length ? partes.join(' · ') : null)

  // Saídas e vendas: produzidos em caixas, crus que saíram sem embalar em caixas e produtosC
  const saiu = [
    ...conf.produzidos.filter(p => p.saidas !== 0).map(p => `${p.codigo} ${caixasProduzido(p.saidas, porCaixaDe(p.chave))}`),
    ...conf.crus.filter(x => x.saidasCruas !== 0).map(x => `${x.nome} ${caixasEProdutosC(x.saidasCruas)}`),
  ]
  const rotuloSaidas = [
    outrasSaidas.length ? `Saídas (${outrasSaidas.length})` : 'Saídas',
    vendas.length ? `vendas (${vendas.length})` : '',
  ].filter(Boolean).join(' e ')

  const linhas: { rotulo: string; valor: string | null; vazio: string; aviso?: boolean }[] = [
    { rotulo: 'Embalou', vazio: 'Nada anotado', aviso: true,
      valor: juntar(item.embalou.map(e => `${e.codigo} ${caixasProduzido(e.caixas, porCaixaDoCodigo(e.codigo))}`)) },
    { rotulo: c.entradas.length ? `Entradas (${c.entradas.length})` : 'Entradas', vazio: 'Nenhuma lançada', aviso: true,
      valor: juntar(somaCrus(c.entradas).map(x => `${x.nome} ${caixasEProdutosC(x.v)}`)) },
    { rotulo: rotuloSaidas, vazio: 'Nenhuma lançada', aviso: true, valor: juntar(saiu) },
    { rotulo: 'Sobra', vazio: 'Nada', valor: juntar(crusDe(c.sobra).map(t => `${t.nome} ${caixasEProdutosC(c.sobra![t.chave])}`)) },
    { rotulo: 'Quebra', vazio: 'Nada', valor: juntar(crusDe(c.quebra).map(t => `${t.nome} ${caixasEProdutosC(c.quebra![t.chave])}`)) },
  ]

  // Conferência: só os tipos que tiveram movimento (como na aba do controle)
  const crus = conf.crus.filter(x =>
    [x.entradas, x.sobraAnterior, x.pvcVinteEntrada, x.consumoProduzidos, x.usadoPvcVinte, x.saidasCruas, x.sobra, x.quebra]
      .some(v => v !== 0))
  const produzidos = conf.produzidos.filter(p => p.embalou !== 0 || p.saidas !== 0)
  const situacao = (bate: boolean, quanto: string, sobrou: boolean) => (bate ? 'bate' : `${sobrou ? 'sobrou' : 'faltou'} ${quanto}`)
  const cor = (bate: boolean) => bate
    ? 'bg-emerald-50 text-emerald-800 border-emerald-200'
    : 'bg-red-50 text-red-800 border-red-200'

  return (
    <div className="border-t border-gray-100 bg-gray-50 px-4 py-3 space-y-3 animate-entrada">
      <dl className="text-sm space-y-1.5">
        {linhas.map(l => (
          <div key={l.rotulo} className="grid grid-cols-[6.5rem_1fr] sm:grid-cols-[10rem_1fr] gap-2">
            <dt className="text-gray-500">{l.rotulo}</dt>
            <dd className={l.valor ? 'text-gray-900' : l.aviso ? 'text-amber-800' : 'text-gray-500'}>{l.valor ?? l.vazio}</dd>
          </div>
        ))}
      </dl>

      <div>
        <p className="text-xs font-medium text-gray-500 mb-1.5">Conferência</p>
        {crus.length === 0 && produzidos.length === 0 ? (
          <p className="text-sm text-gray-500">Nada lançado ainda para conferir.</p>
        ) : (
          <ul className="flex flex-wrap gap-1.5">
            {crus.map(x => (
              <li key={x.chave} className={`text-sm px-2 py-1 rounded-md border ${cor(x.bate)}`}>
                <b className="font-semibold">{x.nome}</b> {situacao(x.bate, caixasEProdutosC(Math.abs(x.estoque)), x.estoque > 0)}
              </li>
            ))}
            {produzidos.map(p => (
              <li key={p.chave} className={`text-sm px-2 py-1 rounded-md border ${cor(p.bate)}`}>
                <b className="font-semibold">{p.codigo}</b> {situacao(p.bate, caixasProduzido(Math.abs(p.estoque), porCaixaDe(p.chave)), p.estoque > 0)}
              </li>
            ))}
          </ul>
        )}
        {conf.avisos.length > 0 && (
          <ul className="mt-2 space-y-0.5">
            {conf.avisos.map(a => <li key={a} className="text-sm text-amber-800">{a}</li>)}
          </ul>
        )}
      </div>

      <p className="text-sm font-medium text-tinta-700 flex items-center gap-1">Toque no cartão para abrir o controle <ChevronRight size={15} aria-hidden /></p>
    </div>
  )
}

function Resumo({ item }: { item: ItemLista }) {
  if (item.status === 'incompleto') {
    const falta = [item.entradas === 0 && 'entradas', item.saidas === 0 && 'saídas'].filter(Boolean)
    return <p className="text-sm text-amber-800 mt-0.5">{falta.length ? `Faltam ${falta.join(' e ')}` : 'Falta o que foi embalado'}</p>
  }
  if (item.status === 'nao_bate') {
    return (
      <p className="text-sm text-red-700 mt-0.5">
        {item.diferencas.map(d =>
          `${d.nome}: ${d.valor > 0 ? 'sobrou' : 'faltou'} ${d.emProdutosC ? caixasEProdutosC(Math.abs(d.valor)) : caixasProduzido(Math.abs(d.valor), TODOS_PRODUZIDOS.find(t => t.nome === d.nome)?.porCaixa ?? 12)}`,
        ).join(' · ')}
      </p>
    )
  }
  return <p className="text-sm text-gray-500 mt-0.5 tabular-nums">{item.entradas} {item.entradas === 1 ? 'entrada' : 'entradas'} · {item.saidas} {item.saidas === 1 ? 'saída' : 'saídas'}</p>
}
