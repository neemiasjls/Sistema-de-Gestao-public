import { Disc3, FileUp, PackageCheck, Plus, Users, X } from 'lucide-react'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import Botao from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import api, { downloadBlob, nomeDoArquivo } from '../services/api'
import ClientesEstoqueModal, { ClienteEstoque, TipoProdutoB, brlKg } from '../components/ClientesEstoqueModal'
import ImportarPlanilhaModal from '../components/ImportarPlanilhaModal'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import {
  AcoesSaida, CabecalhoSaidas, CartaoSaida, ContaSaida, Fatos, ModalRomaneio, PainelCarga, SeletorCarga,
  classeCampo, classeNumero, diaCurto, plural,
} from '../components/EstoquePecas'
import { brl, dataBr, hoje, lerNum, num } from '../utils/formato'
import { comoLista, comoObjeto } from '../utils/resposta'

type Carga = {
  id: number; data: string; fornecedor: string; quantidade: number; valorTotal: number
  custoKg: number; controle?: string; observacao?: string; encerrada: boolean
  vendidas: number; estoque: number; pesoKg: number; recuperado: number; aReceber: number
  valorVendido: number; comissao: number; totalVendas: number
}

type Venda = {
  id: number; data: string; clienteId: number; cliente: string; espessura?: number
  precoKg: number; custoKg: number; observacao?: string
  qtdeProdutosA: number; pesoKg: number; valor: number; custo: number; comissao: number
  numero?: number | null; proximoNumero?: number | null
  produtosA: { id: number; pesoGramas: number }[]
}

export default function EstoqueProdutosAPage() {
  const [cargas, setCargas] = useState<Carga[]>([])
  const [cargaId, setCargaId] = useState<number | null>(null)
  const [carga, setCarga] = useState<Carga | null>(null)
  const [vendas, setVendas] = useState<Venda[]>([])
  const [clientes, setClientes] = useState<ClienteEstoque[]>([])
  const [tipos, setTipos] = useState<TipoProdutoB[]>([])
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  // Detalhe da carga escolhida: carregando, erro (com tentar de novo) ou pronto
  const [detalhe, setDetalhe] = useState<'carregando' | 'erro' | 'ok'>('carregando')
  const confirmar = useConfirmar()

  const [modalCarga, setModalCarga] = useState(false)
  const [modalVenda, setModalVenda] = useState(false)
  const [modalClientes, setModalClientes] = useState(false)
  const [modalImportar, setModalImportar] = useState(false)
  const [romaneio, setRomaneio] = useState<Venda | null>(null)
  const [aberta, setAberta] = useState<number | null>(null)

  const carregarClientes = useCallback(() => {
    api.get('/api/estoque/clientes').then(r => setClientes(comoLista<ClienteEstoque>(r.data, 'clientes'))).catch(() => {})
  }, [])

  const carregarCargas = useCallback(async (selecionar?: number) => {
    setFalhou(false)
    try {
      const r = await api.get('/api/estoque/produtosA')
      const lista = comoLista<Carga>(r.data, 'cargas de produtoA')
      setCargas(lista)
      setCargaId(prev => selecionar ?? prev ?? (lista.find(c => !c.encerrada) ?? lista[0])?.id ?? null)
    } catch {
      setFalhou(true)
    } finally {
      setCarregando(false)
    }
  }, [])

  // `quieto`: recarga depois de lançar/apagar, sem trocar a tela pelo esqueleto
  const carregarDetalhe = useCallback(async (id: number, quieto = false) => {
    if (!quieto) setDetalhe('carregando')
    try {
      const r = await api.get(`/api/estoque/produtosA/${id}`)
      const d = comoObjeto<{ carga: Carga; vendas: Venda[] }>(r.data, ['vendas'], 'carga de produtoA')
      if (!d.carga) throw new Error('sem carga')
      setCarga(d.carga)
      setVendas(d.vendas.map(v => ({ ...v, produtosA: v.produtosA ?? [] })))
      setDetalhe('ok')
    } catch {
      if (quieto) toast.error('Não consegui atualizar a carga.')
      else setDetalhe('erro')
    }
  }, [])

  useEffect(() => {
    carregarCargas()
    carregarClientes()
    api.get('/api/estoque/tipos').then(r => setTipos(comoLista<TipoProdutoB>(r.data, 'tipos'))).catch(() => {})
  }, [carregarCargas, carregarClientes])

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
        `/api/estoque/produtosA/vendas/${v.id}/excel` + (n > 0 ? `?numero=${n}` : ''),
        { responseType: 'blob' })
      downloadBlob(r.data as Blob,
        nomeDoArquivo(r.headers['content-disposition'] as string | undefined, 'romaneio.xlsx'))
      toast.success('Romaneio baixado.')
      setRomaneio(null)
      recarregar()
    } catch {
      toast.error('Não consegui gerar o romaneio.')
    }
  }

  const excluirVenda = async (v: Venda) => {
    if (!await confirmar({
      titulo: 'Apagar esta saída?',
      texto: <><b>{v.cliente}</b>, {dataBr(v.data)}: {plural(v.qtdeProdutosA, 'produtoA', 'produtosA')}, {num(v.pesoKg)} kg. As produtosA voltam para o estoque da carga.</>,
      confirmar: 'Apagar saída',
    })) return
    try {
      await api.delete(`/api/estoque/produtosA/vendas/${v.id}`)
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
        descricao="Cada carga tem seu custo por kg. O que o cliente paga vem do cadastro dele; a diferença é a comissão."
        acoes={<>
          <Botao variante="secundario" icone={Users} onClick={() => setModalClientes(true)}>Clientes</Botao>
          <Botao variante="secundario" icone={FileUp} onClick={() => setModalImportar(true)}>Importar planilha</Botao>
          <Botao variante="primario" icone={Plus} onClick={() => setModalCarga(true)}>Nova carga</Botao>
        </>}
      />

      {carregando ? (
        <Carregando forma="cartoes" linhas={4} rotulo="Carregando as cargas" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar as cargas." onTentar={() => { setCarregando(true); carregarCargas() }} />
      ) : cargas.length === 0 ? (
        <Vazio icone={Disc3} titulo="Nenhuma carga cadastrada"
               acao={<Botao variante="primario" icone={Plus} onClick={() => setModalCarga(true)}>Cadastrar a primeira carga</Botao>}>
          Comece pela carga: quantas produtosA vieram, o valor e o custo por kg.
        </Vazio>
      ) : (
        <>
          <SeletorCarga cargas={cargas} valor={cargaId} onChange={setCargaId}
                        descrever={c => `${dataBr(c.data)} · ${c.fornecedor} · ${c.quantidade} produtosA${c.controle ? ` · ${c.controle}` : ''}`} />

          {detalhe === 'carregando' && !carga ? (
            <Carregando linhas={3} rotulo="Carregando a carga" />
          ) : detalhe === 'erro' ? (
            <ErroCarga mensagem="Não consegui carregar esta carga." onTentar={() => cargaId && carregarDetalhe(cargaId)} />
          ) : carga && (
            <div className={`space-y-5 transition-opacity duration-200 ${detalhe === 'carregando' ? 'opacity-60' : ''}`}
                 aria-busy={detalhe === 'carregando' || undefined}>
              <PainelCarga
                estoque={carga.estoque} total={carga.quantidade} unidade="produtosA"
                saiu={<>{plural(carga.vendidas, 'saiu', 'saíram')} · {num(carga.pesoKg)} kg</>}
                aReceber={carga.aReceber} valorTotal={carga.valorTotal} comissao={carga.comissao}
                custo={brlKg(carga.custoKg)} encerrada={carga.encerrada}
              />

              <section className="space-y-3">
                <CabecalhoSaidas quantidade={vendas.length}
                                 acao={<Botao variante="primario" icone={Plus} onClick={() => setModalVenda(true)}>Nova saída</Botao>} />

                {vendas.length === 0 ? (
                  <Vazio icone={Disc3} titulo="Nenhuma saída nesta carga">
                    Toque em <b>Nova saída</b> para lançar o que o cliente levou.
                  </Vazio>
                ) : (
                  <ul className="space-y-2">
                    {vendas.map(v => (
                      <CartaoSaida
                        key={v.id} id={v.id} cliente={v.cliente} data={v.data}
                        aberta={aberta === v.id} onAlternar={() => setAberta(a => a === v.id ? null : v.id)}
                        valor={v.valor} comissao={v.comissao}
                        resumo={<>
                          {plural(v.qtdeProdutosA, 'produtoA', 'produtosA')} · {num(v.pesoKg)} kg
                          <span className="block sm:inline font-medium text-gray-800"><span className="hidden sm:inline font-normal text-gray-500" aria-hidden> · </span>{brlKg(v.precoKg)}</span>
                        </>}
                      >
                        <Fatos itens={[
                          ['Preço do cliente', brlKg(v.precoKg)],
                          ['Custo da carga', brlKg(v.custoKg)],
                          ['Diferença', brlKg(v.precoKg - v.custoKg)],
                          ['Custo total', brl(v.custo)],
                          ...(v.espessura ? [['Espessura', String(v.espessura)] as [string, string]] : []),
                        ]} />

                        {v.produtosA.length > 0 && (
                          <div>
                            <p className="text-xs text-gray-500 mb-1.5">
                              Peso de cada produtoA <span className="tabular-nums">({v.produtosA.length})</span>
                            </p>
                            <ol className="grid grid-cols-2 sm:grid-cols-4 gap-1.5">
                              {v.produtosA.map((b, i) => (
                                <li key={b.id} className="flex items-baseline gap-2 bg-white border border-gray-200 rounded-lg px-2.5 py-1.5 text-sm tabular-nums">
                                  <span className="text-xs text-gray-500 w-4 text-right">{i + 1}</span>
                                  <span className="text-gray-900">{num(b.pesoGramas / 1000, 3)} kg</span>
                                </li>
                              ))}
                            </ol>
                          </div>
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
          onClose={() => setModalCarga(false)}
          onSalvou={id => { setModalCarga(false); carregarCargas(id) }}
        />
      )}

      {modalVenda && carga && (
        <ModalVenda
          carga={carga}
          clientes={clientes.filter(c => c.ativo)}
          onClose={() => setModalVenda(false)}
          onSalvou={() => { setModalVenda(false); recarregar() }}
          onCadastrarCliente={() => { setModalVenda(false); setModalClientes(true) }}
        />
      )}

      {romaneio && (
        <ModalRomaneio cliente={`${romaneio.cliente} · ${diaCurto(romaneio.data)}`}
                       sugestao={romaneio.numero ?? romaneio.proximoNumero} jaTem={!!romaneio.numero}
                       onClose={() => setRomaneio(null)} baixar={n => baixarRomaneio(romaneio, n)} />
      )}

      <ImportarPlanilhaModal
        aberto={modalImportar}
        produto="produtosA"
        onClose={() => setModalImportar(false)}
        onImportou={id => { setModalImportar(false); carregarCargas(id); carregarClientes() }}
      />

      <ClientesEstoqueModal
        aberto={modalClientes}
        foco="produtoA"
        onClose={() => setModalClientes(false)}
        tipos={tipos}
        onSalvou={carregarClientes}
      />
    </div>
  )
}

// ── Nova carga ────────────────────────────────────────────────────────────

function ModalCarga({ onClose, onSalvou }: { onClose: () => void; onSalvou: (id: number) => void }) {
  const [data, setData] = useState(hoje())
  const [fornecedor, setFornecedor] = useState('')
  const [quantidade, setQuantidade] = useState('')
  const [valorTotal, setValorTotal] = useState('')
  const [custoKg, setCustoKg] = useState('')
  const [controle, setControle] = useState('')
  const [salvando, setSalvando] = useState(false)

  // O custo por kg costuma sair do próprio pedido: valor ÷ peso total.
  const qtd = Math.round(lerNum(quantidade))
  const valor = lerNum(valorTotal)
  const custo = lerNum(custoKg)
  const pesoImplicito = custo > 0 ? valor / custo : 0
  const sujo = !!(fornecedor || quantidade || valorTotal || custoKg || controle)

  const salvar = async () => {
    if (qtd <= 0) { toast.error('Informe quantas produtosA vieram.'); return }
    if (custo <= 0) { toast.error('Informe o custo por kg.'); return }
    setSalvando(true)
    try {
      const r = await api.post<{ id: number }>('/api/estoque/produtosA/cargas', {
        data, fornecedor, quantidade: qtd, valorTotal: valor, custoKg: custo,
        controle: controle || null,
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
    <Modal titulo="Nova carga de produtosA" onClose={onClose} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Cadastrar carga" />}>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Data do pedido">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={classeCampo} />
        </Campo>
        <Campo rotulo="Fornecedor">
          <input value={fornecedor} onChange={e => setFornecedor(e.target.value)}
                 placeholder="Ex.: FILMES SUL" className={classeCampo} />
        </Campo>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Campo rotulo="Quantidade de produtosA">
          <input value={quantidade} onChange={e => setQuantidade(e.target.value)}
                 inputMode="numeric" placeholder="Ex.: 400" className={classeNumero} />
        </Campo>
        <Campo rotulo="Controle">
          <input value={controle} onChange={e => setControle(e.target.value)}
                 placeholder="Ex.: ((02))" className={classeCampo} />
        </Campo>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Campo rotulo="Valor do pedido (com frete)">
          <input value={valorTotal} onChange={e => setValorTotal(e.target.value)}
                 inputMode="decimal" placeholder="Ex.: 83091,21" className={classeNumero} />
        </Campo>
        <Campo rotulo="Custo por kg">
          <input value={custoKg} onChange={e => setCustoKg(e.target.value)}
                 inputMode="decimal" placeholder="Ex.: 16,34" className={classeNumero} />
        </Campo>
      </div>

      {pesoImplicito > 0 && qtd > 0 && (
        <Aviso tipo="info">
          Com esse valor e esse custo, a carga tem <b className="tabular-nums">{num(pesoImplicito)} kg</b>, cerca
          de <b className="tabular-nums">{num(pesoImplicito / qtd, 1)} kg</b> por produtoA. Se não bater com o peso
          real, confira o custo por kg.
        </Aviso>
      )}
    </Modal>
  )
}

// ── Nova saída ────────────────────────────────────────────────────────────

function ModalVenda({ carga, clientes, onClose, onSalvou, onCadastrarCliente }: {
  carga: Carga
  clientes: ClienteEstoque[]
  onClose: () => void
  onSalvou: () => void
  onCadastrarCliente: () => void
}) {
  const [clienteId, setClienteId] = useState<number | ''>('')
  const [data, setData] = useState(hoje())
  const [espessura, setEspessura] = useState('')
  const [observacao, setObservacao] = useState('')
  const [pesos, setPesos] = useState<number[]>([])
  const [digitando, setDigitando] = useState('')
  const [salvando, setSalvando] = useState(false)

  const cliente = clientes.find(c => c.id === clienteId)
  const pesoKg = pesos.reduce((s, p) => s + p, 0) / 1000
  const precoKg = cliente?.precoProdutoAKg ?? 0
  const valor = pesoKg * precoKg
  const custo = pesoKg * carga.custoKg
  const comissao = valor - custo
  const sujo = !!(clienteId || pesos.length || digitando || espessura || observacao)

  const adicionar = () => {
    const g = Math.round(lerNum(digitando))
    if (g <= 0) return
    setPesos(p => [...p, g])
    setDigitando('')
  }

  const salvar = async () => {
    if (!clienteId) { toast.error('Escolha o cliente.'); return }
    if (pesos.length === 0) { toast.error('Adicione ao menos uma produtoA.'); return }
    setSalvando(true)
    try {
      const resp = await api.post<{ cargaEncerrada?: boolean }>('/api/estoque/produtosA/vendas', {
        cargaId: carga.id, clienteId, data,
        espessura: espessura ? Math.round(lerNum(espessura)) : null,
        pesos, observacao: observacao || null,
      })
      toast.success(`${plural(pesos.length, 'produtoA lançada', 'produtosA lançadas')}.`)
      if (resp.data?.cargaEncerrada)
        toast('Acabaram as produtosA desta carga e ela foi encerrada. Cadastre a próxima carga para seguir lançando.', { icon: <PackageCheck size={18} className="text-tinta-600 shrink-0" aria-hidden />, duration: 9000 })
      onSalvou()
    } catch {
      toast.error('Não consegui salvar a saída.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo="Nova saída de produtosA" onClose={onClose} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Lançar saída" />}>
      <Campo rotulo="Cliente">
        <select value={clienteId} onChange={e => setClienteId(e.target.value ? Number(e.target.value) : '')} className={classeCampo}>
          <option value="">Escolha...</option>
          {clientes.map(c => (
            <option key={c.id} value={c.id}>
              {c.nome}{c.precoProdutoAKg > 0 ? ` · ${brlKg(c.precoProdutoAKg)}` : ' · sem preço por kg'}
            </option>
          ))}
        </select>
      </Campo>
      {cliente && precoKg > 0 && (
        <p className="-mt-1 flex items-baseline justify-between gap-3 rounded-lg bg-gema-50 border border-gema-200 px-3 py-2">
          <span className="text-sm text-gray-700">Preço deste cliente</span>
          <b className="font-display text-lg font-semibold text-gray-900 tabular-nums">{brlKg(precoKg)}</b>
        </p>
      )}
      {cliente && precoKg <= 0 && (
        <Aviso tipo="atencao" acao={<Botao variante="secundario" tamanho="sm" onClick={onCadastrarCliente}>Cadastrar o preço</Botao>}>
          <b>{cliente.nome}</b> está sem preço de produtoA. A saída vai sair com valor zero.
        </Aviso>
      )}

      <div className="grid grid-cols-2 gap-3">
        <Campo rotulo="Data da retirada">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={classeCampo} />
        </Campo>
        <Campo rotulo="Espessura">
          <input value={espessura} onChange={e => setEspessura(e.target.value)}
                 inputMode="numeric" placeholder="Ex.: 20" className={classeNumero} />
        </Campo>
      </div>

      <Campo rotulo="Peso de cada produtoA, em gramas" dica="Como vem da balança: 12890 = 12,89 kg. Aperte Enter para somar a próxima.">
        <div className="flex gap-2">
          <input
            value={digitando}
            onChange={e => setDigitando(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); adicionar() } }}
            inputMode="numeric" placeholder="Ex.: 12740"
            className={`${classeNumero} flex-1`}
          />
          <Botao variante="primario" icone={Plus} onClick={adicionar}>Somar</Botao>
        </div>
      </Campo>

      {pesos.length > 0 && (
        <ol className="grid grid-cols-2 sm:grid-cols-3 gap-1.5" aria-label="ProdutosA desta saída">
          {pesos.map((p, i) => (
            <li key={i} className="flex items-center gap-2 bg-white border border-gray-200 rounded-lg pl-2.5 pr-0.5 text-sm tabular-nums animate-entrada">
              <span className="text-xs text-gray-500 w-4 text-right">{i + 1}</span>
              <span className="flex-1 text-gray-900">{num(p / 1000, 3)} kg</span>
              <button type="button" onClick={() => setPesos(ps => ps.filter((_, j) => j !== i))}
                      aria-label={`Tirar a produtoA ${i + 1}, de ${num(p / 1000, 3)} kg`} title="Tirar esta produtoA"
                      className="w-10 h-10 sm:w-8 sm:h-8 inline-flex items-center justify-center rounded-md text-gray-500 hover:text-red-700 hover:bg-red-50 transition-colors">
                <X size={16} aria-hidden />
              </button>
            </li>
          ))}
        </ol>
      )}

      <Campo rotulo="Observação">
        <input value={observacao} onChange={e => setObservacao(e.target.value)}
               placeholder="Opcional" className={classeCampo} />
      </Campo>

      {pesos.length > 0 && (
        <ContaSaida valor={valor} comissao={comissao} linhas={[
          [plural(pesos.length, 'produtoA', 'produtosA'), `${num(pesoKg)} kg`],
          [`Preço (${brlKg(precoKg)})`, brl(valor)],
          [`Custo da carga (${brlKg(carga.custoKg)})`, brl(custo)],
        ]}>
          {pesos.length > carga.estoque && (
            <p className="mt-3 text-sm text-amber-800">
              A carga tem <b className="tabular-nums">{plural(carga.estoque, 'produtoA', 'produtosA')}</b> em estoque e você está
              lançando <b className="tabular-nums">{pesos.length}</b>. Dá para salvar, mas o estoque fica negativo.
            </p>
          )}
        </ContaSaida>
      )}
    </Modal>
  )
}
