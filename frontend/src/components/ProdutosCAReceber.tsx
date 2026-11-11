import { Link } from 'react-router-dom'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { CheckCircle2, Grid3x3, Pencil, Plus, RefreshCw, RotateCcw, Trash2 } from 'lucide-react'
import api from '../services/api'
import { comoLista, comoObjeto } from '../utils/resposta'
import { brl, dataBr, hoje, lerNum, num } from '../utils/formato'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import Botao, { BotaoIcone } from './ui/Botao'
import Aviso from './ui/Aviso'
import { Modal, Campo, BotoesModal } from './ModalBase'
import { Selo } from './viagens/ViagensPecas'
import type { ClienteEstoque, TipoProdutoB } from './ClientesEstoqueModal'

/**
 * ProdutosC a receber (EST-14/EST-08): não é boleto. As vendas de produtosC vazias (pacote de 100) que vêm do
 * controle de carregamento (coluna U), por cliente, a BRZ primeiro. A BRZ paga por Pix e a baixa é automática
 * na conferência com o banco; o resto se marca recebido à mão.
 */

type Venda = {
  id: number; data: string; clienteId: number; cliente: string; pacotes: number; unidades: number; quantidade: number
  preco: number; valor: number; origem: 'controle' | 'mao'; observacao?: string | null; cobradaNoRomaneio?: string | null
  recebidoEm?: string | null; recebidoPix?: string | null; semPreco: boolean
}
type Grupo = { clienteId: number; cliente: string; brz: boolean; aReceber: number; recebido: number; vendas: Venda[] }
type Resumo = { grupos: Grupo[]; aReceber: number; aReceberBrz: number; semPreco: number }
type PixRelato = { chave: string; data: string; valor: number; situacao: 'baixado' | 'certo' | 'duvida' | 'sem-casamento' | 'ja-baixado'; opcoes: Venda[][] }
type ResultadoPix = { pix: PixRelato[]; marcadas: number; erro?: string | null }

const quantidade = (v: Venda) =>
  `${num(v.pacotes, 0)} ${v.pacotes === 1 ? 'pacote' : 'pacotes'}` + (v.unidades > 0 ? ` e ${num(v.unidades, 0)} produtosC` : '')

/**
 * modo "vendas" (aba ProdutosC): todas as vendas de todos os clientes, com o valor; sem preço aparece "Sem valor".
 * modo "brz" (Boletos → A receber → BRZ): só a BRZ, com o recebido e a baixa pelo Pix (EST-08).
 */
export default function ProdutosCAReceber({ modo }: { modo: 'vendas' | 'brz' }) {
  const ehBrz = modo === 'brz'
  const [resumo, setResumo] = useState<Resumo | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [pix, setPix] = useState<ResultadoPix | null>(null)
  const [conferindo, setConferindo] = useState(false)
  const [editando, setEditando] = useState<Venda | 'nova' | null>(null)
  const [recebendo, setRecebendo] = useState<Venda | null>(null)
  const confirmar = useConfirmar()

  const carregar = useCallback(async () => {
    try {
      const r = await api.get('/api/estoque/produtosC')
      const d = comoObjeto<Resumo>(r.data, ['grupos'], 'produtosC a receber')
      setResumo({ ...d, grupos: d.grupos.map(g => ({ ...g, vendas: g.vendas ?? [] })) })
      setFalhou(false)
    } catch { setFalhou(true) }
  }, [])
  useEffect(() => { carregar() }, [carregar])

  const conferirPix = async () => {
    setConferindo(true)
    try {
      const r = await api.post('/api/estoque/produtosC/conferir-pix')
      const d = comoObjeto<ResultadoPix>(r.data, ['pix'], 'Pix da BRZ')
      setPix(d)
      if (d.erro) toast.error(d.erro)
      else toast.success(d.marcadas ? `${d.marcadas} venda(s) da BRZ recebida(s) pelo Pix.` : 'Pix da BRZ conferido.')
      carregar()
    } catch { toast.error('Não consegui conferir o Pix da BRZ.') }
    finally { setConferindo(false) }
  }

  const baixarOpcao = async (p: PixRelato, opcao: Venda[]) => {
    try {
      await api.post('/api/estoque/produtosC/baixar-pix', { chave: p.chave, data: p.data, vendaIds: opcao.map(v => v.id) })
      toast.success('ProdutosC marcadas como recebidas.')
      setPix(x => x && { ...x, pix: x.pix.filter(y => y.chave !== p.chave) })
      carregar()
    } catch { toast.error('Não consegui dar baixa.') }
  }

  const voltarAReceber = async (v: Venda) => {
    if (!await confirmar({ titulo: 'Voltar para a receber?', texto: <><b>{v.cliente}</b>, {dataBr(v.data)}: {brl(v.valor)}{v.recebidoPix ? '. Ela tinha sido baixada por um Pix.' : '.'}</>, confirmar: 'Voltar para a receber', perigo: false })) return
    try { await api.post(`/api/estoque/produtosC/vendas/${v.id}/recebido`, { recebidoEm: null }); toast.success('Voltou para a receber.'); carregar() }
    catch { toast.error('Não consegui mudar.') }
  }

  const apagar = async (v: Venda) => {
    if (!await confirmar({
      titulo: 'Apagar esta venda de produtosC?',
      texto: <><b>{v.cliente}</b>, {dataBr(v.data)}: {quantidade(v)}, {brl(v.valor)}.{v.origem === 'controle' ? ' Ela veio do controle de carregamento: ler o controle de novo traz de volta.' : ''}{v.recebidoEm ? ' Ela já está recebida.' : ''}</>,
      confirmar: 'Apagar venda',
    })) return
    try { await api.delete(`/api/estoque/produtosB/vendas/${v.id}`); toast.success('Venda apagada.'); carregar() }
    catch { toast.error('Não consegui apagar.') }
  }

  const avisosPix = (pix?.pix ?? []).filter(p => p.situacao === 'sem-casamento' || p.situacao === 'duvida')
  const grupos = (resumo?.grupos ?? []).filter(g => !ehBrz || g.brz)
  const todas = grupos.flatMap(g => g.vendas)
  const totalVendido = todas.reduce((t, v) => t + (v.semPreco ? 0 : v.valor), 0)
  const semValor = todas.filter(v => v.semPreco).length

  return (
    <section aria-labelledby="produtosC-receber" className="rounded-xl border-2 border-dashed border-gema-300 bg-gema-50/40 p-4 space-y-4">
      <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3">
        <div className="min-w-0">
          <h2 id="produtosC-receber" className="flex items-center gap-2 text-base font-semibold text-gray-900">
            <Grid3x3 size={18} aria-hidden className="text-gema-700 shrink-0" /> {ehBrz ? 'ProdutosC a receber da BRZ' : 'Vendas de produtosC'}
          </h2>
          <p className="text-sm text-gray-600 max-w-[68ch]">
            {ehBrz
              ? 'Não é boleto: a BRZ paga as produtosC por Pix e a baixa é sozinha na conferência com o banco. Aqui você vê o que falta receber.'
              : <>Todas as vendas de produtosC vazias, com o valor. Saem sozinhas do controle de carregamento (coluna U). O recebimento da BRZ fica em <Link to="/boletos" className="font-medium text-tinta-700 hover:underline">Boletos → A receber → BRZ (produtosC)</Link>.</>}
          </p>
        </div>
        <div className="flex flex-col sm:flex-row gap-2 shrink-0">
          {ehBrz
            ? <Botao variante="secundario" icone={RefreshCw} carregando={conferindo} textoCarregando="Conferindo..." onClick={conferirPix}>Conferir Pix da BRZ</Botao>
            : <Botao variante="secundario" icone={Plus} onClick={() => setEditando('nova')}>Lançar à mão</Botao>}
        </div>
      </div>

      {resumo && (
        <div className="grid grid-cols-2 gap-3">
          <div className="rounded-lg bg-white border border-gray-200 px-3 py-2">
            <p className="text-xs text-gray-500">{ehBrz ? 'A receber da BRZ' : 'Total vendido'}</p>
            <p className="font-display text-xl font-semibold text-gray-900 tabular-nums">{brl(ehBrz ? resumo.aReceberBrz : totalVendido)}</p>
          </div>
          <div className="rounded-lg bg-white border border-gray-200 px-3 py-2">
            <p className="text-xs text-gray-500">{ehBrz ? 'Vendas em aberto' : 'Vendas sem valor'}</p>
            <p className="font-display text-xl font-semibold text-gray-900 tabular-nums">{ehBrz ? todas.filter(v => !v.recebidoEm).length : semValor}</p>
          </div>
        </div>
      )}

      {semValor > 0 && (
        <Aviso tipo="atencao">
          {semValor === 1 ? '1 venda está' : `${semValor} vendas estão`} sem o preço da produtoC do cliente: cadastre em Clientes → Venda.
        </Aviso>
      )}

      {ehBrz && avisosPix.map(p => (
        <Aviso key={p.chave} tipo="atencao" titulo={`Pix da BRZ de ${brl(p.valor)} em ${dataBr(p.data)}`}>
          {p.situacao === 'sem-casamento' ? (
            <>Pix da BRZ sem produtosC que somem {brl(p.valor)}. Confira as quantidades no controle ou lance à mão.</>
          ) : (
            <div className="space-y-2">
              <p>Mais de uma combinação soma o valor. Escolha qual foi paga:</p>
              {p.opcoes.map((o, i) => (
                <div key={i} className="flex flex-col sm:flex-row sm:items-center gap-2">
                  <span className="text-sm">{o.map(v => `${dataBr(v.data)} (${brl(v.valor)})`).join(' + ')}</span>
                  <Botao variante="secundario" tamanho="sm" onClick={() => baixarOpcao(p, o)}>Foi esta</Botao>
                </div>
              ))}
            </div>
          )}
        </Aviso>
      ))}

      {falhou ? (
        <ErroCarga mensagem="Não consegui carregar as produtosC a receber." onTentar={carregar} />
      ) : !resumo ? (
        <Carregando linhas={3} rotulo="Carregando as produtosC" />
      ) : grupos.length === 0 ? (
        <Vazio icone={Grid3x3} titulo={ehBrz ? 'Nenhuma produtoC da BRZ' : 'Nenhuma venda de produtosC'} compacto>
          As vendas entram sozinhas quando o controle de carregamento tem produtosC (coluna U).
        </Vazio>
      ) : (
        <div className="space-y-4">
          {grupos.map(g => (
            <div key={g.clienteId} className="bg-white border border-gray-200 rounded-xl">
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 px-4 py-3 border-b border-gray-100">
                <h3 className="font-semibold text-gray-900">{g.cliente}</h3>
                <p className="text-sm text-gray-600 tabular-nums">{ehBrz
                  ? <>a receber <b className="text-gray-900">{brl(g.aReceber)}</b></>
                  : <>vendido <b className="text-gray-900">{brl(g.vendas.reduce((t, v) => t + (v.semPreco ? 0 : v.valor), 0))}</b></>}</p>
              </div>
              <ul className="divide-y divide-gray-100">
                {g.vendas.map(v => (
                  <li key={v.id} className="px-4 py-3 flex flex-col sm:flex-row sm:items-center gap-2">
                    <div className="flex-1 min-w-0">
                      <p className="flex flex-wrap items-baseline gap-x-2">
                        <span className="font-medium text-gray-900 tabular-nums">{dataBr(v.data)}</span>
                        <span className="text-sm text-gray-600 tabular-nums">{quantidade(v)}</span>
                        {v.semPreco
                          ? <span className="font-semibold text-amber-800">Sem valor</span>
                          : <span className="font-semibold text-gray-900 tabular-nums">{brl(v.valor)}</span>}
                      </p>
                      <p className="flex flex-wrap gap-1 mt-1">
                        {!ehBrz ? null : v.recebidoEm
                          ? <span className="inline-flex items-center gap-1 text-xs font-medium text-emerald-800"><CheckCircle2 size={14} aria-hidden />Recebido em {dataBr(v.recebidoEm)}{v.recebidoPix ? ' (Pix)' : ''}</span>
                          : <Selo tom="atencao">A receber</Selo>}
                        {v.semPreco && <Selo tom="atencao">Falta o preço da produtoC</Selo>}
                        {v.origem === 'controle' && <Selo tom="info">Do controle de carregamento</Selo>}
                        {v.cobradaNoRomaneio && <Selo>Também no {v.cobradaNoRomaneio.toLowerCase()}</Selo>}
                      </p>
                      {v.observacao && <p className="text-xs text-gray-500 mt-1">{v.observacao}</p>}
                    </div>
                    <div className="flex items-center gap-1 sm:shrink-0">
                      {!ehBrz ? null : v.recebidoEm
                        ? <BotaoIcone icone={RotateCcw} rotulo="Voltar para a receber" onClick={() => voltarAReceber(v)} />
                        : <Botao variante="secundario" tamanho="sm" icone={CheckCircle2} onClick={() => setRecebendo(v)}>Recebido</Botao>}
                      <BotaoIcone icone={Pencil} rotulo="Editar venda" onClick={() => setEditando(v)} />
                      <BotaoIcone icone={Trash2} rotulo="Apagar venda" perigo onClick={() => apagar(v)} />
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      {editando && (
        <ModalVendaProdutoC venda={editando === 'nova' ? null : editando} onClose={() => setEditando(null)}
                           onSalvou={() => { setEditando(null); carregar() }} />
      )}
      {recebendo && (
        <ModalRecebido venda={recebendo} onClose={() => setRecebendo(null)} onSalvou={() => { setRecebendo(null); carregar() }} />
      )}
    </section>
  )
}

function ModalRecebido({ venda, onClose, onSalvou }: { venda: Venda; onClose: () => void; onSalvou: () => void }) {
  const [data, setData] = useState(hoje())
  const [salvando, setSalvando] = useState(false)
  const salvar = async () => {
    setSalvando(true)
    try { await api.post(`/api/estoque/produtosC/vendas/${venda.id}/recebido`, { recebidoEm: data }); toast.success('Marcada como recebida.'); onSalvou() }
    catch { toast.error('Não consegui marcar.') }
    finally { setSalvando(false) }
  }
  return (
    <Modal titulo="ProdutosC recebidas" descricao={`${venda.cliente} · ${dataBr(venda.data)} · ${brl(venda.valor)}`} onClose={onClose}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Marcar recebida" />}>
      <Campo rotulo="Data do recebimento">
        <input type="date" value={data} onChange={e => setData(e.target.value)} className="w-full border border-gray-300 rounded-lg px-3 py-2 text-base sm:text-sm" />
      </Campo>
    </Modal>
  )
}

function ModalVendaProdutoC({ venda, onClose, onSalvou }: { venda: Venda | null; onClose: () => void; onSalvou: () => void }) {
  const [clientes, setClientes] = useState<ClienteEstoque[]>([])
  const [tipoId, setTipoId] = useState<number | null>(null)
  const [clienteId, setClienteId] = useState<number | ''>(venda?.clienteId ?? '')
  const [data, setData] = useState(venda?.data ?? hoje())
  const [pacotes, setPacotes] = useState(venda ? String(venda.pacotes) : '')
  const [avulsas, setAvulsas] = useState(venda && venda.unidades ? String(venda.unidades) : '')
  const [observacao, setObservacao] = useState(venda?.observacao ?? '')
  const [salvando, setSalvando] = useState(false)

  useEffect(() => {
    api.get('/api/estoque/clientes').then(r => setClientes(comoLista<ClienteEstoque>(r.data, 'clientes').filter(c => c.ativo || c.id === venda?.clienteId))).catch(() => toast.error('Não consegui carregar os clientes.'))
    api.get('/api/estoque/tipos').then(r => setTipoId(comoLista<TipoProdutoB>(r.data, 'tipos').find(t => t.produto === 'PRODUTOC')?.id ?? null)).catch(() => {})
  }, [venda?.clienteId])

  const cliente = clientes.find(c => c.id === clienteId)
  const preco = cliente?.precos.find(p => p.tipoProdutoBId === tipoId)?.preco ?? 0
  const qtdPacotes = Math.round(lerNum(pacotes))
  const qtdAvulsas = Math.round(lerNum(avulsas))
  const valor = (qtdPacotes + qtdAvulsas / 100) * preco
  const sujo = !venda && !!(clienteId || pacotes || avulsas || observacao)

  const salvar = async () => {
    if (!clienteId) { toast.error('Escolha o cliente.'); return }
    if (qtdPacotes + qtdAvulsas <= 0) { toast.error('Informe a quantidade.'); return }
    if (!tipoId) { toast.error('Falta o tipo ProdutoC no cadastro.'); return }
    setSalvando(true)
    try {
      if (venda) await api.put(`/api/estoque/produtosC/vendas/${venda.id}`, { clienteId, data, pacotes: qtdPacotes, unidades: qtdAvulsas, observacao: observacao || null })
      else await api.post('/api/estoque/produtosB/vendas', {
        cargaId: null, clienteId, data, observacao: observacao || null,
        itens: [{ tipoProdutoBId: tipoId, pacotes: qtdPacotes, unidades: qtdAvulsas }],
      })
      toast.success(venda ? 'Venda corrigida.' : 'Venda lançada.')
      onSalvou()
    } catch { toast.error('Não consegui salvar.') }
    finally { setSalvando(false) }
  }

  const campo = 'w-full border border-gray-300 rounded-lg px-3 py-2 text-base sm:text-sm'
  return (
    <Modal titulo={venda ? 'Corrigir venda de produtosC' : 'Nova venda de produtosC'} onClose={onClose} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo={venda ? 'Salvar' : 'Lançar venda'} />}>
      {venda?.origem === 'controle' && (
        <Aviso tipo="info">Veio do controle de carregamento: se o controle for lido de novo com outra quantidade, vale a do controle.</Aviso>
      )}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Cliente">
          <select value={clienteId} onChange={e => setClienteId(e.target.value ? Number(e.target.value) : '')} className={campo}>
            <option value="">Escolha...</option>
            {clientes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Data da carga">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={campo} />
        </Campo>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Campo rotulo="Pacotes de 100">
          <input value={pacotes} onChange={e => setPacotes(e.target.value)} inputMode="numeric" placeholder="0" className={`${campo} tabular-nums`} />
        </Campo>
        <Campo rotulo="ProdutosC avulsas">
          <input value={avulsas} onChange={e => setAvulsas(e.target.value)} inputMode="numeric" placeholder="0" className={`${campo} tabular-nums`} />
        </Campo>
      </div>
      <Campo rotulo="Observação">
        <input value={observacao} onChange={e => setObservacao(e.target.value)} placeholder="Opcional" className={campo} />
      </Campo>
      {clienteId !== '' && (preco > 0
        ? <p className="text-sm text-gray-700 tabular-nums">{brl(preco)} por pacote · valor <b>{brl(valor)}</b>{venda && venda.preco > 0 && venda.clienteId === clienteId ? ' (fica o preço gravado na venda)' : ''}</p>
        : <Aviso tipo="atencao">Este cliente não tem preço de produtoC em Clientes → Venda: a venda fica sem valor.</Aviso>)}
    </Modal>
  )
}
