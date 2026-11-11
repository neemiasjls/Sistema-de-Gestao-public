import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import Botao, { classesBotao } from '../components/ui/Botao'
import { Link, useNavigate } from 'react-router-dom'
import Aviso from '../components/ui/Aviso'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import { Boxes, FileUp, PackageCheck, Plus, RefreshCw, Tags } from 'lucide-react'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import api, { downloadBlob, nomeDoArquivo } from '../services/api'
import type { ClienteEstoque, TipoProdutoB } from '../components/ClientesEstoqueModal'
import ImportarPlanilhaModal from '../components/ImportarPlanilhaModal'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import {
  AcoesSaida, CabecalhoSaidas, CartaoSaida, ContaSaida, Fatos, ModalRomaneio, PainelCarga, SeletorCarga,
  classeCampo, classeNumero, diaCurto, plural,
} from '../components/EstoquePecas'
import { Selo } from '../components/viagens/ViagensPecas'
import { brl, dataBr, hoje, lerNum, num } from '../utils/formato'
import { comoLista, comoObjeto } from '../utils/resposta'

type PorTipo = { tipoId: number; tipo: string; entrada: number; saida: number; estoque: number }

type Carga = {
  id: number; data: string; fornecedor: string; quantidadePacotes: number
  valorProdutosB: number; valorFrete: number; transportador?: string; observacao?: string
  encerrada: boolean; valorTotal: number; custoPacote: number
  entrada: number; vendidos: number; estoque: number; porTipo: PorTipo[]
  recuperado: number; aReceber: number; valorVendido: number; comissao: number; totalVendas: number
}

type ItemVenda = {
  tipoProdutoBId: number; tipo: string; pacotes: number; unidades: number
  preco: number; equivalente: number
}

type Venda = {
  id: number; data: string; clienteId: number; cliente: string; custoPacote: number
  observacao?: string; pacotes: number; valor: number; custo: number; comissao: number
  fretePacote?: number; frete?: number; totalCliente?: number
  numero?: number | null; proximoNumero?: number | null
  /** De onde veio: controle de carregamento, uso nos volumes embalados ou lançada à mão */
  origem?: 'controle' | 'embalados' | 'mao'
  /** Já cobrada noutro documento (produtoC da BRZ no romaneio da nota) */
  cobradaNoRomaneio?: string | null
  itens: ItemVenda[]
}

/** ProdutosB (pacote de 150) e produtosC (pacote de 100) têm cargas e saídas próprias (EST-07). */
export type ProdutoPacote = 'PRODUTOB' | 'PRODUTOC'

/** "19 pacotes e 120 unidades" (pacote de 150; as unidades são a fração avulsa, EST-02) */
const pacotesEUnidades = (pacotes: number, unidades: number, avulsas = 'unidades') =>
  plural(pacotes, 'pacote', 'pacotes') + (unidades > 0 ? ` e ${unidades} ${avulsas}` : '')

export default function EstoqueProdutosBPage({ produto = 'PRODUTOB' }: { produto?: ProdutoPacote }) {
  const ehProdutoC = produto === 'PRODUTOC'
  const avulsas = ehProdutoC ? 'produtosC' : 'unidades'
  const [cargas, setCargas] = useState<Carga[]>([])
  const [cargaId, setCargaId] = useState<number | null>(null)
  const [carga, setCarga] = useState<Carga | null>(null)
  const [vendas, setVendas] = useState<Venda[]>([])
  const [clientes, setClientes] = useState<ClienteEstoque[]>([])
  const [tipos, setTipos] = useState<TipoProdutoB[]>([])
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const [detalhe, setDetalhe] = useState<'carregando' | 'erro' | 'ok'>('carregando')
  const confirmar = useConfirmar()

  const [modalCarga, setModalCarga] = useState(false)
  const [modalVenda, setModalVenda] = useState(false)
  const navegar = useNavigate()
  const [modalImportar, setModalImportar] = useState(false)
  const [romaneio, setRomaneio] = useState<Venda | null>(null)
  const [aberta, setAberta] = useState<number | null>(null)
  const [avisosEmbalados, setAvisosEmbalados] = useState<string[] | null>(null)
  const [acertando, setAcertando] = useState(false)

  const carregarClientes = useCallback(() => {
    api.get('/api/estoque/clientes').then(r => setClientes(comoLista<ClienteEstoque>(r.data, 'clientes'))).catch(() => {})
  }, [])

  const carregarCargas = useCallback(async (selecionar?: number) => {
    setFalhou(false)
    try {
      const r = await api.get('/api/estoque/produtosB', { params: { produto } })
      const lista = comoLista<Carga>(r.data, ehProdutoC ? 'cargas de produtoC' : 'cargas de produtoB')
      setCargas(lista)
      setCargaId(prev => selecionar ?? prev ?? (lista.find(c => !c.encerrada) ?? lista[0])?.id ?? null)
    } catch {
      setFalhou(true)
    } finally {
      setCarregando(false)
    }
  }, [produto, ehProdutoC])

  const carregarDetalhe = useCallback(async (id: number, quieto = false) => {
    if (!quieto) setDetalhe('carregando')
    try {
      const r = await api.get(`/api/estoque/produtosB/${id}`)
      const d = comoObjeto<{ carga: Carga; vendas: Venda[] }>(r.data, ['vendas'], 'carga de produtoB')
      if (!d.carga) throw new Error('sem carga')
      setCarga({ ...d.carga, porTipo: d.carga.porTipo ?? [] })
      setVendas(d.vendas.map(v => ({ ...v, itens: v.itens ?? [] })))
      setDetalhe('ok')
    } catch {
      if (quieto) toast.error('Não consegui atualizar a carga.')
      else setDetalhe('erro')
    }
  }, [])

  useEffect(() => {
    carregarCargas()
    carregarClientes()
    api.get('/api/estoque/tipos')
      .then(r => setTipos(comoLista<TipoProdutoB>(r.data, 'tipos').filter(t => (t.produto ?? 'PRODUTOB') === produto)))
      .catch(() => {})
  }, [carregarCargas, carregarClientes, produto])

  useEffect(() => { if (cargaId) { setAberta(null); carregarDetalhe(cargaId) } }, [cargaId, carregarDetalhe])

  const recarregar = () => {
    carregarCargas()
    if (cargaId) carregarDetalhe(cargaId, true)
  }

  // Romaneio da venda em Excel, no mesmo modelo preenchido à mão. O número
  // fica gravado, então reexportar sai igual.
  const baixarRomaneio = async (v: Venda, n: number) => {
    try {
      const r = await api.get(
        `/api/estoque/produtosB/vendas/${v.id}/excel` + (n > 0 ? `?numero=${n}` : ''),
        { responseType: 'blob' })
      downloadBlob(r.data as Blob,
        nomeDoArquivo(r.headers['content-disposition'] as string | undefined, 'romaneio-emb.xlsx'))
      toast.success('Romaneio baixado.')
      setRomaneio(null)
      recarregar()
    } catch {
      toast.error('Não consegui gerar o romaneio.')
    }
  }

  // Uso nos volumes embalados (VOLUME-04): cada controle vira a saída das produtosB usadas;
  // a saída à mão no EMBALADOS do mesmo dia é aproveitada (não conta duas vezes)
  const acertarEmbalados = async () => {
    if (!await confirmar({
      titulo: 'Acertar o uso nos volumes embalados?',
      texto: 'Cada controle de volumes embalados vira uma saída das produtosB usadas para embalar (amarelas = Abe × 30, brancas = Ave × 30, pacote de 150). A saída lançada à mão no EMBALADOS no mesmo dia é aproveitada, sem contar duas vezes.',
      confirmar: 'Acertar', perigo: false,
    })) return
    setAcertando(true)
    try {
      const r = await api.post('/api/estoque/produtosB/sincronizar-embalados')
      const d = comoObjeto<{ controles: number; avisos: string[] }>(r.data, ['avisos'], 'embalados')
      toast.success(`${d.controles} controles de volumes embalados conferidos.`)
      setAvisosEmbalados(d.avisos.length ? d.avisos : null)
      recarregar()
    } catch {
      toast.error('Não consegui acertar o uso nos embalados.')
    } finally {
      setAcertando(false)
    }
  }

  const excluirVenda = async (v: Venda) => {
    if (!await confirmar({
      titulo: 'Apagar esta saída?',
      texto: <><b>{v.cliente}</b>, {dataBr(v.data)}: {num(v.pacotes)} pacotes. Os pacotes voltam para o estoque da carga.{v.origem === 'controle' ? ' Ela veio do controle de carregamento: ler o carregamento de novo traz de volta.' : ''}</>,
      confirmar: 'Apagar saída',
    })) return
    try {
      await api.delete(`/api/estoque/produtosB/vendas/${v.id}`)
      toast.success('Saída apagada.')
      setAberta(null)
      recarregar()
    } catch {
      toast.error('Não consegui apagar.')
    }
  }

  return (
    <div className="space-y-5 max-w-4xl">
      <CabecalhoTela
        descricao={ehProdutoC
          ? 'ProdutosC vazias em pacotes de 100. O custo por pacote sai da carga; o preço de cada cliente, do painel de preços; a diferença é a comissão. As saídas vêm do controle de carregamento (coluna U).'
          : 'O custo por pacote sai da carga (produtosB + frete ÷ pacotes). O preço de cada cliente vem do painel de preços; a diferença é a comissão. As saídas vêm do controle de carregamento e do uso nos volumes embalados.'}
        acoes={<>
          <Link to="/precos?vista=venda" className={classesBotao('secundario')}>
            <Tags className="w-4 h-4" aria-hidden="true" /> Preços dos clientes
          </Link>
          {!ehProdutoC && <Botao variante="secundario" icone={RefreshCw} carregando={acertando} textoCarregando="Acertando..." onClick={acertarEmbalados}>Acertar uso nos embalados</Botao>}
          {!ehProdutoC && <Botao variante="secundario" icone={FileUp} onClick={() => setModalImportar(true)}>Importar planilha</Botao>}
          <Botao variante="primario" icone={Plus} onClick={() => setModalCarga(true)}>Nova carga</Botao>
        </>}
      />
      {avisosEmbalados && (
        <Aviso tipo="atencao" titulo="Uso nos volumes embalados" itens={avisosEmbalados} onFechar={() => setAvisosEmbalados(null)} />
      )}

      {carregando ? (
        <Carregando forma="cartoes" linhas={4} rotulo="Carregando as cargas" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar as cargas." onTentar={() => { setCarregando(true); carregarCargas() }} />
      ) : cargas.length === 0 ? (
        <Vazio icone={Boxes} titulo={ehProdutoC ? 'Nenhuma carga de produtosC cadastrada' : 'Nenhuma carga cadastrada'}
               acao={<Botao variante="primario" icone={Plus} onClick={() => setModalCarga(true)}>Cadastrar a primeira carga</Botao>}>
          {ehProdutoC
            ? 'Cadastre o pedido de produtosC: quantos pacotes de 100 vieram, o valor e o frete. Sem carga aberta, as saídas do controle de carregamento não entram.'
            : 'Cadastre o pedido: quantos pacotes vieram, o valor, o frete e quanto entrou de cada tipo (somando a sobra do pedido anterior).'}
        </Vazio>
      ) : (
        <>
          <SeletorCarga cargas={cargas} valor={cargaId} onChange={setCargaId}
                        descrever={c => `${dataBr(c.data)} · ${c.fornecedor} · ${c.quantidadePacotes} pacotes`} />

          {detalhe === 'carregando' && !carga ? (
            <Carregando linhas={3} rotulo="Carregando a carga" />
          ) : detalhe === 'erro' ? (
            <ErroCarga mensagem="Não consegui carregar esta carga." onTentar={() => cargaId && carregarDetalhe(cargaId)} />
          ) : carga && (
            <div className={`space-y-5 transition-opacity duration-200 ${detalhe === 'carregando' ? 'opacity-60' : ''}`}
                 aria-busy={detalhe === 'carregando' || undefined}>
              <PainelCarga
                estoque={carga.estoque} total={carga.entrada} unidade="pacotes" casas={1}
                saiu={<>{num(carga.vendidos, 1)} pacotes saíram</>}
                aReceber={carga.aReceber} valorTotal={carga.valorTotal} comissao={carga.comissao}
                custo={`${brl(carga.custoPacote)} por pacote`} encerrada={carga.encerrada}
              >
                {carga.porTipo.length > 0 && (
                  <div className="border-t border-gray-100 px-4 py-3 sm:px-5">
                    <h3 className="text-xs font-medium text-gray-500 mb-2">Estoque por tipo</h3>
                    <ul className="divide-y divide-gray-100">
                      {carga.porTipo.map(t => (
                        <li key={t.tipoId} className="flex items-baseline justify-between gap-3 py-2 first:pt-0 last:pb-0">
                          <div className="min-w-0">
                            <p className="text-sm font-medium text-gray-900">{t.tipo}</p>
                            <p className="text-xs text-gray-500 tabular-nums">entrou {num(t.entrada, 0)} · saiu {num(t.saida, 1)}</p>
                          </div>
                          <p className={`font-display text-xl font-semibold tabular-nums shrink-0 ${t.estoque < 0 ? 'text-red-700' : 'text-gray-900'}`}>
                            {num(t.estoque, 1)} <span className="font-sans text-sm font-normal text-gray-500">pacotes</span>
                          </p>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </PainelCarga>

              <section className="space-y-3">
                <CabecalhoSaidas quantidade={vendas.length}
                                 acao={<Botao variante="primario" icone={Plus} onClick={() => setModalVenda(true)}>Nova saída</Botao>} />

                {vendas.length === 0 ? (
                  <Vazio icone={Boxes} titulo="Nenhuma saída nesta carga">
                    Toque em <b>Nova saída</b> para lançar o que o cliente levou.
                  </Vazio>
                ) : (
                  <ul className="space-y-2">
                    {vendas.map(v => (
                      <CartaoSaida
                        key={v.id} id={v.id} cliente={v.cliente} data={v.data}
                        aberta={aberta === v.id} onAlternar={() => setAberta(a => a === v.id ? null : v.id)}
                        valor={v.totalCliente ?? v.valor} comissao={v.comissao}
                        nota={(v.frete ?? 0) > 0 ? <>com {brl(v.frete!)} de frete</> : undefined}
                        resumo={<>
                          {v.itens.map(i => (
                            <span key={i.tipoProdutoBId} className="block">
                              {i.tipo}: {pacotesEUnidades(i.pacotes, i.unidades, avulsas)}
                            </span>
                          ))}
                          {(v.origem === 'controle' || v.origem === 'embalados' || v.cobradaNoRomaneio) && (
                            <span className="flex flex-wrap gap-1 mt-1">
                              {v.origem === 'controle' && <Selo tom="info">Do controle de carregamento</Selo>}
                              {v.origem === 'embalados' && <Selo tom="info">Usadas para embalar</Selo>}
                              {v.cobradaNoRomaneio && <Selo tom="atencao">Cobrada no {v.cobradaNoRomaneio.toLowerCase()}</Selo>}
                            </span>
                          )}
                        </>}
                      >
                        <Fatos itens={[
                          ['Total em pacotes', num(v.pacotes, 2)],
                          ['Custo da carga', `${brl(v.custoPacote)} por pacote`],
                          ['Custo total', brl(v.custo)],
                          ...((v.frete ?? 0) > 0 ? [['Frete', `${brl(v.frete!)}${v.fretePacote ? ` (${brl(v.fretePacote)} por pacote)` : ''}`] as [string, string]] : []),
                        ]} />

                        {v.itens.length > 0 && (
                          <ul className="bg-white border border-gray-200 rounded-lg divide-y divide-gray-100">
                            {v.itens.map(i => (
                              <li key={i.tipoProdutoBId} className="px-3 py-2.5 grid grid-cols-[1fr_auto] items-baseline gap-x-3 text-sm tabular-nums">
                                <span className="min-w-0">
                                  <span className="font-medium text-gray-900">{i.tipo}</span>
                                  <span className="block text-gray-600">
                                    {pacotesEUnidades(i.pacotes, i.unidades, avulsas)}
                                    {i.unidades > 0 && <> = {num(i.equivalente, 2)} pacotes</>}
                                  </span>
                                </span>
                                <span className="text-right">
                                  <span className="block font-semibold text-gray-900">{brl(i.equivalente * i.preco)}</span>
                                  <span className="block text-xs text-gray-500">{brl(i.preco)} por pacote</span>
                                </span>
                              </li>
                            ))}
                          </ul>
                        )}

                        {v.observacao && (
                          <p className="text-sm text-gray-700"><span className="text-gray-500">Observação: </span>{v.observacao}</p>
                        )}

                        <AcoesSaida numero={v.numero} onExportar={() => setRomaneio(v)} onApagar={() => excluirVenda(v)} />
                      </CartaoSaida>
                    ))}
                  </ul>
                )}
              </section>
            </div>
          )}
        </>
      )}

      {modalCarga && (
        <ModalCarga
          produto={produto}
          tipos={tipos}
          onClose={() => setModalCarga(false)}
          onSalvou={id => { setModalCarga(false); carregarCargas(id) }}
        />
      )}

      {modalVenda && carga && (
        <ModalVenda
          produto={produto}
          carga={carga}
          tipos={tipos}
          clientes={clientes.filter(c => c.ativo)}
          onClose={() => setModalVenda(false)}
          onSalvou={() => { setModalVenda(false); recarregar() }}
          onCadastrarCliente={() => { setModalVenda(false); navegar('/precos?vista=venda') }}
        />
      )}

      {romaneio && (
        <ModalRomaneio cliente={`${romaneio.cliente} · ${diaCurto(romaneio.data)}`}
                       sugestao={romaneio.numero ?? romaneio.proximoNumero} jaTem={!!romaneio.numero}
                       onClose={() => setRomaneio(null)} baixar={n => baixarRomaneio(romaneio, n)} />
      )}

      <ImportarPlanilhaModal
        aberto={modalImportar && !ehProdutoC}
        produto="produtosB"
        onClose={() => setModalImportar(false)}
        onImportou={id => { setModalImportar(false); carregarCargas(id); carregarClientes() }}
      />

    </div>
  )
}

// ── Nova carga ────────────────────────────────────────────────────────────

function ModalCarga({ produto, tipos, onClose, onSalvou }: {
  produto: ProdutoPacote; tipos: TipoProdutoB[]; onClose: () => void; onSalvou: (id: number) => void
}) {
  const ehProdutoC = produto === 'PRODUTOC'
  const [data, setData] = useState(hoje())
  const [fornecedor, setFornecedor] = useState('')
  const [pacotes, setPacotes] = useState('')
  const [valorEmb, setValorEmb] = useState('')
  const [frete, setFrete] = useState('')
  const [transportador, setTransportador] = useState('')
  const [observacao, setObservacao] = useState('')
  const [porTipo, setPorTipo] = useState<Record<number, string>>({})
  const [salvando, setSalvando] = useState(false)

  const qtd = Math.round(lerNum(pacotes))
  const total = lerNum(valorEmb) + lerNum(frete)
  const custo = qtd > 0 ? total / qtd : 0
  const somaTipos = tipos.reduce((s, t) => s + Math.round(lerNum(porTipo[t.id] ?? '')), 0)
  const sujo = !!(fornecedor || pacotes || valorEmb || frete || transportador || observacao || Object.values(porTipo).some(Boolean))

  const salvar = async () => {
    if (qtd <= 0) { toast.error('Informe quantos pacotes foram comprados.'); return }
    setSalvando(true)
    try {
      const r = await api.post<{ id: number }>('/api/estoque/produtosB/cargas', {
        data, fornecedor, quantidadePacotes: qtd, produto,
        valorProdutosB: lerNum(valorEmb), valorFrete: lerNum(frete),
        transportador: transportador || null, observacao: observacao || null,
        itens: tipos.map(t => ({ tipoProdutoBId: t.id, quantidade: Math.round(lerNum(porTipo[t.id] ?? '')) })),
      })
      toast.success('Carga cadastrada.')
      onSalvou(r.data.id)
    } catch {
      toast.error('Não consegui salvar a carga.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo={ehProdutoC ? 'Nova carga de produtosC' : 'Nova carga de produtosB'} onClose={onClose} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Cadastrar carga" />}>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Data do pedido">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={classeCampo} />
        </Campo>
        <Campo rotulo="Fornecedor">
          <input value={fornecedor} onChange={e => setFornecedor(e.target.value)}
                 placeholder="Ex.: PACKSUL" className={classeCampo} />
        </Campo>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
        <Campo rotulo="Pacotes comprados" className="col-span-2 sm:col-span-1">
          <input value={pacotes} onChange={e => setPacotes(e.target.value)}
                 inputMode="numeric" placeholder="Ex.: 800" className={classeNumero} />
        </Campo>
        <Campo rotulo="Valor">
          <input value={valorEmb} onChange={e => setValorEmb(e.target.value)}
                 inputMode="decimal" placeholder="Ex.: 58560" className={classeNumero} />
        </Campo>
        <Campo rotulo="Frete e descarga">
          <input value={frete} onChange={e => setFrete(e.target.value)}
                 inputMode="decimal" placeholder="Ex.: 200" className={classeNumero} />
        </Campo>
      </div>

      {custo > 0 && (
        <p className="flex flex-wrap items-baseline justify-between gap-x-3 rounded-lg bg-gema-50 border border-gema-200 px-3 py-2 text-sm">
          <span className="text-gray-700">Custo por pacote <span className="text-gray-500 tabular-nums">({brl(total)} ÷ {qtd})</span></span>
          <b className="font-display text-lg font-semibold text-gray-900 tabular-nums">{brl(custo)}</b>
        </p>
      )}

      <Campo rotulo="Transportador">
        <input value={transportador} onChange={e => setTransportador(e.target.value)}
               placeholder="Ex.: CAMINHÃO XAD-5H46" className={classeCampo} />
      </Campo>

      <Campo grupo rotulo="Quanto entrou no estoque, por tipo"
             dica="Some aqui a sobra do pedido anterior: é esse número que vira o estoque.">
        <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
          {tipos.map(t => (
            <Campo key={t.id} rotulo={<span className="font-normal text-gray-600">{t.nome}</span>}>
              <input
                value={porTipo[t.id] ?? ''}
                onChange={e => setPorTipo({ ...porTipo, [t.id]: e.target.value })}
                inputMode="numeric" placeholder="0" className={classeNumero}
              />
            </Campo>
          ))}
        </div>
      </Campo>
      {somaTipos > 0 && qtd > 0 && somaTipos !== qtd && (
        <Aviso tipo="atencao">
          Você lançou <b className="tabular-nums">{somaTipos}</b> contra <b className="tabular-nums">{qtd}</b> comprados: a
          diferença de <b className="tabular-nums">{Math.abs(somaTipos - qtd)}</b> deve ser a sobra.
        </Aviso>
      )}

      <Campo rotulo="Observação">
        <input value={observacao} onChange={e => setObservacao(e.target.value)}
               placeholder="Ex.: sobra anterior 246 amarelos / 12 brancos" className={classeCampo} />
      </Campo>
    </Modal>
  )
}

// ── Nova saída ────────────────────────────────────────────────────────────

function ModalVenda({ produto, carga, tipos, clientes, onClose, onSalvou, onCadastrarCliente }: {
  produto: ProdutoPacote
  carga: Carga
  tipos: TipoProdutoB[]
  clientes: ClienteEstoque[]
  onClose: () => void
  onSalvou: () => void
  onCadastrarCliente: () => void
}) {
  const [clienteId, setClienteId] = useState<number | ''>('')
  const [data, setData] = useState(hoje())
  const [observacao, setObservacao] = useState('')
  const [qtdPacotes, setQtdPacotes] = useState<Record<number, string>>({})
  const [qtdUnidades, setQtdUnidades] = useState<Record<number, string>>({})
  const [salvando, setSalvando] = useState(false)
  const ehProdutoC = produto === 'PRODUTOC'

  const cliente = clientes.find(c => c.id === clienteId)

  const linhas = tipos.map(t => {
    const pacotes = Math.round(lerNum(qtdPacotes[t.id] ?? ''))
    const unidades = Math.round(lerNum(qtdUnidades[t.id] ?? ''))
    const equivalente = pacotes + unidades / (t.unidadesPorPacote || 150)
    const preco = cliente?.precos.find(p => p.tipoProdutoBId === t.id)?.preco ?? 0
    return { tipo: t, pacotes, unidades, equivalente, preco, valor: equivalente * preco }
  })

  const totalPacotes = linhas.reduce((s, l) => s + l.equivalente, 0)
  const valor = linhas.reduce((s, l) => s + l.valor, 0)
  const custo = totalPacotes * carga.custoPacote
  const comissao = valor - custo
  const semPreco = linhas.filter(l => l.equivalente > 0 && l.preco <= 0)
  const sujo = !!(clienteId || observacao || totalPacotes > 0)

  const salvar = async () => {
    if (!clienteId) { toast.error('Escolha o cliente.'); return }
    if (totalPacotes <= 0) { toast.error('Informe a quantidade de pelo menos um tipo.'); return }
    setSalvando(true)
    try {
      const resp = await api.post<{ cargaEncerrada?: boolean }>('/api/estoque/produtosB/vendas', {
        cargaId: carga.id, clienteId, data,
        itens: linhas.filter(l => l.pacotes > 0 || l.unidades > 0).map(l => ({
          tipoProdutoBId: l.tipo.id, pacotes: l.pacotes, unidades: l.unidades,
        })),
        observacao: observacao || null,
      })
      toast.success('Saída lançada.')
      if (resp.data?.cargaEncerrada)
        toast('Acabaram os pacotes desta carga e ela foi encerrada. Cadastre a próxima carga para seguir lançando.', { icon: <PackageCheck size={18} className="text-tinta-600 shrink-0" aria-hidden />, duration: 9000 })
      onSalvou()
    } catch {
      toast.error('Não consegui salvar a saída.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo={ehProdutoC ? 'Nova saída de produtosC' : 'Nova saída de produtosB'} onClose={onClose} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Lançar saída" />}>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Cliente">
          <select value={clienteId} onChange={e => setClienteId(e.target.value ? Number(e.target.value) : '')} className={classeCampo}>
            <option value="">Escolha...</option>
            {clientes.map(c => <option key={c.id} value={c.id}>{c.nome}</option>)}
          </select>
        </Campo>
        <Campo rotulo="Data">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={classeCampo} />
        </Campo>
      </div>

      <fieldset className="space-y-2">
        <legend className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">Quantidade: pacotes e unidades avulsas</legend>
        {tipos.map(t => {
          const linha = linhas.find(l => l.tipo.id === t.id)!
          const estoqueTipo = carga.porTipo.find(p => p.tipoId === t.id)?.estoque ?? 0
          const passou = linha.equivalente > 0 && linha.equivalente > estoqueTipo
          return (
            <div key={t.id} className={`rounded-xl border p-3 transition-colors ${linha.equivalente > 0 ? 'border-tinta-200 bg-tinta-50/40' : 'border-gray-200'}`}>
              <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5 mb-2">
                <span className="text-sm font-semibold text-gray-900">{t.nome}</span>
                <span className="text-xs text-gray-500 tabular-nums">
                  estoque {num(estoqueTipo, 1)}{linha.preco > 0 && <> · {brl(linha.preco)} por pacote</>}
                </span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Campo rotulo={<span className="font-normal text-gray-600">Pacotes</span>}>
                  <input value={qtdPacotes[t.id] ?? ''} onChange={e => setQtdPacotes({ ...qtdPacotes, [t.id]: e.target.value })}
                         inputMode="numeric" placeholder="0" className={classeNumero} />
                </Campo>
                <Campo rotulo={<span className="font-normal text-gray-600">{ehProdutoC ? 'ProdutosC avulsas' : 'Unidades avulsas'}</span>}>
                  <input value={qtdUnidades[t.id] ?? ''} onChange={e => setQtdUnidades({ ...qtdUnidades, [t.id]: e.target.value })}
                         inputMode="numeric" placeholder="0" className={classeNumero} />
                </Campo>
              </div>
              <p className="text-xs text-gray-500 mt-1.5">Pacote de {t.unidadesPorPacote} {ehProdutoC ? 'produtosC' : 'unidades'}.</p>
              {passou && (
                <p className="text-sm text-amber-800 mt-1">Passa do estoque deste tipo ({num(estoqueTipo, 1)}).</p>
              )}
            </div>
          )
        })}
      </fieldset>

      <Campo rotulo="Observação">
        <input value={observacao} onChange={e => setObservacao(e.target.value)}
               placeholder="Opcional" className={classeCampo} />
      </Campo>

      {semPreco.length > 0 && (
        <Aviso tipo="atencao" acao={<Botao variante="secundario" tamanho="sm" onClick={onCadastrarCliente}>Cadastrar o preço</Botao>}>
          Sem preço cadastrado para: <b>{semPreco.map(l => l.tipo.nome).join(', ')}</b>. Esses itens saem com valor zero.
        </Aviso>
      )}

      {totalPacotes > 0 && (
        <ContaSaida valor={valor} comissao={comissao} linhas={[
          ['Total', `${num(totalPacotes, 2)} pacotes`],
          [`Custo da carga (${brl(carga.custoPacote)} por pacote)`, brl(custo)],
        ]} />
      )}
    </Modal>
  )
}
