import { useCallback, useEffect, useMemo, useState } from 'react'
import { Pencil, Plus, Search, Tags } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Segmentado } from '../components/ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import { classeCampo, classeNumero } from '../components/EstoquePecas'
import { brl, lerNum } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import { precosApi, type LinhaPreco, type Painel, type TipoPacote } from '../services/precosApi'

// Painel de preços por cliente (VIA-10), separado em Frete e Venda. Frete: caixa
// normal, produtoE, produtoD, produtoB e produtoC (por pacote). Venda: produtosB,
// produtoC e produtoA. Viagens, ProdutosB, ProdutosC e ProdutosA calculam com estes
// valores; o que já foi lançado guarda o valor do dia.

type Vista = 'frete' | 'venda'

const PADRAO_PACOTE = 10

/** Nome curto da coluna: os tipos do estoque têm nome comprido ("Pacote amarelo (volume grande branco)"). */
const nomeTipo = (t: TipoPacote) =>
  t.codigo === 'AMARELA' ? 'ProdutoB amarela' : t.codigo === 'BRANCA' ? 'ProdutoB branca' : t.codigo === 'PRODUTOC' ? 'ProdutoC' : t.nome

const precoDe = (l: LinhaPreco, tipoId: number) => l.venda?.precos.find(p => p.tipoProdutoBId === tipoId)?.preco ?? null

export default function PrecosPage() {
  const [dados, setDados] = useState<Painel | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [vista, setVista] = useState<Vista>('frete')
  const [busca, setBusca] = useState('')
  const [edicao, setEdicao] = useState<LinhaPreco | 'novo' | null>(null)

  const carregar = useCallback(() => {
    setFalhou(false)
    precosApi.painel().then(setDados).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const linhas = useMemo(() => {
    const t = busca.trim().toLowerCase()
    return (dados?.linhas ?? []).filter(l => !t || l.cliente.toLowerCase().includes(t) || (l.apelidos ?? '').toLowerCase().includes(t)
      || (l.nomeEstoque ?? '').toLowerCase().includes(t))
  }, [dados, busca])

  const tipos = dados?.tipos ?? []

  return (
    <div className="space-y-4 max-w-6xl">
      <CabecalhoTela
        descricao="Frete e venda de cada cliente num lugar só. As viagens, as produtosB, as produtosC e as produtosA calculam com estes valores; o que já foi lançado guarda o valor do dia."
        acoes={<Botao variante="primario" icone={Plus} onClick={() => setEdicao('novo')}>Novo cliente</Botao>}
      />
      <div className="flex flex-col sm:flex-row gap-3 sm:items-center justify-between">
        <Segmentado valor={vista} onChange={setVista} rotulo="Preços de" opcoes={[['frete', 'Frete'], ['venda', 'Venda']] as const} />
        <div className="relative sm:w-72">
          <Search size={16} aria-hidden className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" />
          <input type="search" value={busca} onChange={e => setBusca(e.target.value)} placeholder="Buscar cliente"
                 aria-label="Buscar cliente" className={`${classeCampo} pl-9`} />
        </div>
      </div>
      <p className="text-sm text-gray-600 max-w-prose">
        {vista === 'frete'
          ? 'Frete por caixa (normal, produtoE, produtoD) e por pacote (produtoB de 150 e produtoC de 100). Zero = não paga.'
          : 'O que o cliente paga por pacote de produtoB e de produtoC e por quilo de produtoA. Em branco = sem preço: a saída entra sem valor e avisa.'}
      </p>

      {falhou ? <ErroCarga mensagem="Não consegui carregar os preços." onTentar={carregar} />
        : !dados ? <Carregando linhas={8} rotulo="Carregando os preços" />
        : linhas.length === 0 ? (
          busca.trim()
            ? <Vazio icone={Search} titulo={`Nenhum cliente com "${busca.trim()}"`}>Confira a escrita ou cadastre em Novo cliente.</Vazio>
            : <Vazio icone={Tags} titulo="Nenhum cliente com preço" acao={<Botao variante="primario" icone={Plus} onClick={() => setEdicao('novo')}>Novo cliente</Botao>} />
        )
        : vista === 'frete' ? <TabelaFrete linhas={linhas} onEditar={setEdicao} />
        : <TabelaVenda linhas={linhas} tipos={tipos} onEditar={setEdicao} />}

      {edicao && dados && (
        <ModalPreco linha={edicao === 'novo' ? null : edicao} painel={dados} onClose={() => setEdicao(null)}
                    onSalvou={() => { setEdicao(null); carregar() }} />
      )}
    </div>
  )
}

// ───────────────────────────── Frete ─────────────────────────────

function Valor({ v, zero = 'não paga', semValor = '—' }: { v: number | null; zero?: string; semValor?: string }) {
  if (v == null) return <span className="text-gray-500">{semValor}</span>
  if (v === 0) return <span className="text-gray-500">{zero}</span>
  return <span className="tabular-nums text-gray-900">{brl(v)}</span>
}

/** Frete de pacote de cliente de volumes sem cadastro no estoque: a viagem usa o padrão (R$ 10) */
function Pacote({ l, v }: { l: LinhaPreco; v: number | null }) {
  if (l.estoqueId == null) return <span className="text-amber-800 tabular-nums" title="Sem cadastro no estoque: vale o padrão">{brl(PADRAO_PACOTE)} padrão</span>
  return <Valor v={v} />
}

function NomeCliente({ l }: { l: LinhaPreco }) {
  return (
    <>
      <p className={`font-medium ${l.ativo ? 'text-gray-900' : 'text-gray-500'}`}>{l.cliente}{!l.ativo && ' (inativo)'}</p>
      {(l.apelidos || l.nomeEstoque) && (
        <p className="text-xs text-gray-500">{[l.apelidos, l.nomeEstoque ? `no estoque: ${l.nomeEstoque}` : null].filter(Boolean).join(' · ')}</p>
      )}
      {l.freteId == null && <p className="text-xs text-gray-500">Não leva volumes (só estoque)</p>}
    </>
  )
}

function TabelaFrete({ linhas, onEditar }: { linhas: LinhaPreco[]; onEditar: (l: LinhaPreco) => void }) {
  return (
    <>
      <ul className="sm:hidden space-y-2">
        {linhas.map(l => (
          <li key={l.chave} className="bg-white border border-gray-200 rounded-xl p-3 space-y-2">
            <div className="flex items-start gap-2">
              <div className="flex-1 min-w-0"><NomeCliente l={l} /></div>
              <BotaoIcone icone={Pencil} rotulo={`Editar ${l.cliente}`} onClick={() => onEditar(l)} />
            </div>
            <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
              {l.freteId != null && <>
                <dt className="text-gray-600">Caixa normal</dt><dd className="text-right"><Valor v={l.caixa} /></dd>
                <dt className="text-gray-600">ProdutoE</dt><dd className="text-right"><Valor v={l.produtoE} /></dd>
                <dt className="text-gray-600">ProdutoD</dt><dd className="text-right"><Valor v={l.produtoD} /></dd>
              </>}
              <dt className="text-gray-600">ProdutoB</dt><dd className="text-right"><Pacote l={l} v={l.produtoB} /></dd>
              <dt className="text-gray-600">ProdutoC</dt><dd className="text-right"><Pacote l={l} v={l.produtoC} /></dd>
            </dl>
          </li>
        ))}
      </ul>
      <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-gray-600 text-left">
            <tr>
              <th className="px-3 py-2 font-medium">Cliente</th>
              <th className="px-3 py-2 font-medium text-right">Caixa normal</th>
              <th className="px-3 py-2 font-medium text-right">ProdutoE</th>
              <th className="px-3 py-2 font-medium text-right">ProdutoD</th>
              <th className="px-3 py-2 font-medium text-right">ProdutoB</th>
              <th className="px-3 py-2 font-medium text-right">ProdutoC</th>
              <th className="px-3 py-2"><span className="sr-only">Editar</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {linhas.map(l => (
              <tr key={l.chave}>
                <td className="px-3 py-2"><NomeCliente l={l} /></td>
                <td className="px-3 py-2 text-right"><Valor v={l.caixa} /></td>
                <td className="px-3 py-2 text-right"><Valor v={l.produtoE} /></td>
                <td className="px-3 py-2 text-right"><Valor v={l.produtoD} /></td>
                <td className="px-3 py-2 text-right whitespace-nowrap"><Pacote l={l} v={l.produtoB} /></td>
                <td className="px-3 py-2 text-right whitespace-nowrap"><Pacote l={l} v={l.produtoC} /></td>
                <td className="px-1 py-1 text-right"><BotaoIcone icone={Pencil} rotulo={`Editar ${l.cliente}`} onClick={() => onEditar(l)} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}

// ───────────────────────────── Venda ─────────────────────────────

function TabelaVenda({ linhas, tipos, onEditar }: { linhas: LinhaPreco[]; tipos: TipoPacote[]; onEditar: (l: LinhaPreco) => void }) {
  const semCadastro = <span className="text-amber-800">sem cadastro no estoque</span>
  return (
    <>
      <ul className="sm:hidden space-y-2">
        {linhas.map(l => (
          <li key={l.chave} className="bg-white border border-gray-200 rounded-xl p-3 space-y-2">
            <div className="flex items-start gap-2">
              <div className="flex-1 min-w-0"><NomeCliente l={l} /></div>
              <BotaoIcone icone={Pencil} rotulo={`Editar ${l.cliente}`} onClick={() => onEditar(l)} />
            </div>
            {l.venda == null ? <p className="text-sm">{semCadastro}</p> : (
              <dl className="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
                {tipos.map(t => <FragmentoPreco key={t.id} rotulo={nomeTipo(t)} v={precoDe(l, t.id)} />)}
                <dt className="text-gray-600">ProdutoA, por quilo</dt>
                <dd className="text-right"><Valor v={l.venda.produtoAKg || null} semValor="sem preço" /></dd>
              </dl>
            )}
          </li>
        ))}
      </ul>
      <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-gray-600 text-left">
            <tr>
              <th className="px-3 py-2 font-medium">Cliente</th>
              {tipos.map(t => <th key={t.id} className="px-3 py-2 font-medium text-right">{nomeTipo(t)}</th>)}
              <th className="px-3 py-2 font-medium text-right">ProdutoA (quilo)</th>
              <th className="px-3 py-2"><span className="sr-only">Editar</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {linhas.map(l => (
              <tr key={l.chave}>
                <td className="px-3 py-2"><NomeCliente l={l} /></td>
                {l.venda == null
                  ? <td colSpan={tipos.length + 1} className="px-3 py-2 text-right">{semCadastro}</td>
                  : <>
                    {tipos.map(t => <td key={t.id} className="px-3 py-2 text-right"><Valor v={precoDe(l, t.id)} semValor="sem preço" /></td>)}
                    <td className="px-3 py-2 text-right"><Valor v={l.venda.produtoAKg || null} semValor="sem preço" /></td>
                  </>}
                <td className="px-1 py-1 text-right"><BotaoIcone icone={Pencil} rotulo={`Editar ${l.cliente}`} onClick={() => onEditar(l)} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}

function FragmentoPreco({ rotulo, v }: { rotulo: string; v: number | null }) {
  return <><dt className="text-gray-600">{rotulo}</dt><dd className="text-right"><Valor v={v} semValor="sem preço" /></dd></>
}

// ───────────────────────────── Editar ─────────────────────────────

/** Número em texto: vazio = sem valor (null); "0" fica zero. */
const texto = (v: number | null | undefined) => (v == null ? '' : String(v).replace('.', ','))
const valorOuNulo = (t: string) => (t.trim() === '' ? null : lerNum(t))

function CampoPreco({ rotulo, valor, onChange, dica, placeholder = '0,00' }: {
  rotulo: string; valor: string; onChange: (t: string) => void; dica?: string; placeholder?: string
}) {
  return (
    <Campo rotulo={rotulo} dica={dica}>
      <input value={valor} onChange={e => onChange(e.target.value.replace(/[^\d.,]/g, ''))} inputMode="decimal"
             placeholder={placeholder} className={classeNumero} />
    </Campo>
  )
}

const CRIAR = 'criar'

function ModalPreco({ linha, painel, onClose, onSalvou }: {
  linha: LinhaPreco | null; painel: Painel; onClose: () => void; onSalvou: () => void
}) {
  const novo = linha == null
  const [cliente, setCliente] = useState(linha?.cliente ?? '')
  const [apelidos, setApelidos] = useState(linha?.apelidos ?? '')
  const [ativo, setAtivo] = useState(linha?.ativo ?? true)
  const [levaVolumes, setLevaVolumes] = useState(true)
  const [caixa, setCaixa] = useState(texto(linha?.caixa))
  const [produtoE, setProdutoE] = useState(texto(linha?.produtoE))
  const [produtoD, setProdutoD] = useState(texto(linha?.produtoD))
  // Cadastro do estoque ligado (só cliente de frete): id, "criar" ou "" (sem cadastro)
  const [vinculo, setVinculo] = useState<string>(linha?.estoqueId != null ? String(linha.estoqueId) : '')
  const daLinha = (estoqueId: number | null) => painel.linhas.find(l => l.estoqueId === estoqueId) ?? null
  const inicialEstoque = linha?.estoqueId != null ? linha : null
  const [produtoB, setProdutoB] = useState(texto(inicialEstoque?.produtoB ?? PADRAO_PACOTE))
  const [produtoC, setProdutoC] = useState(texto(inicialEstoque?.produtoC ?? PADRAO_PACOTE))
  const [produtoA, setProdutoA] = useState(texto(inicialEstoque?.venda?.produtoAKg || null))
  const [precos, setPrecos] = useState<Record<number, string>>(
    Object.fromEntries(painel.tipos.map(t => [t.id, texto(inicialEstoque ? precoDe(inicialEstoque, t.id) : null)])))
  const [salvando, setSalvando] = useState(false)

  const temFrete = linha != null && linha.freteId != null
  const temEstoque = linha == null || linha.freteId == null || vinculo !== ''

  // Trocar o cadastro ligado traz os valores dele (ou os padrões, para um novo)
  const mudarVinculo = (v: string) => {
    setVinculo(v)
    const outro = v && v !== CRIAR ? daLinha(Number(v)) : null
    setProdutoB(texto(outro?.produtoB ?? PADRAO_PACOTE))
    setProdutoC(texto(outro?.produtoC ?? PADRAO_PACOTE))
    setProdutoA(texto(outro?.venda?.produtoAKg || null))
    setPrecos(Object.fromEntries(painel.tipos.map(t => [t.id, texto(outro ? precoDe(outro, t.id) : null)])))
  }

  const metade = lerNum(caixa) > 0 ? Math.round(lerNum(caixa) / 2 * 100) / 100 : null

  const salvar = async () => {
    if (!cliente.trim()) { toast.error('Informe o cliente.'); return }
    if (temFrete && valorOuNulo(caixa) == null) { toast.error('Informe o frete da caixa normal.'); return }
    setSalvando(true)
    try {
      let freteId = linha?.freteId ?? null
      let estoqueId = linha?.estoqueId ?? null
      if (novo) {
        const r = await precosApi.novoCliente(cliente.trim(), apelidos.trim() || null, levaVolumes)
        freteId = r.freteId; estoqueId = r.estoqueId
      }
      if (freteId != null && (temFrete || novo)) {
        const cx = valorOuNulo(caixa) ?? 9
        await precosApi.salvarFrete(freteId, {
          cliente: cliente.trim(), apelidos: apelidos.trim() || null, valorCaixa: cx,
          valorProdutoE: valorOuNulo(produtoE), valorProdutoD: valorOuNulo(produtoD) ?? 3, ativo,
        })
      }
      if (temFrete && freteId != null) {
        const atual = linha?.estoqueId != null ? String(linha.estoqueId) : ''
        if (vinculo !== atual) {
          const r = await precosApi.ligar(freteId, vinculo && vinculo !== CRIAR ? Number(vinculo) : null, vinculo === CRIAR)
          estoqueId = r.estoqueId
        }
      }
      if (estoqueId != null && temEstoque) {
        await precosApi.salvarEstoque(estoqueId, {
          freteProdutoB: valorOuNulo(produtoB) ?? 0, freteProdutoC: valorOuNulo(produtoC),
          precoProdutoAKg: valorOuNulo(produtoA) ?? 0,
          precos: painel.tipos.map(t => ({ tipoProdutoBId: t.id, preco: valorOuNulo(precos[t.id] ?? '') })),
        })
      }
      toast.success('Preços salvos.')
      onSalvou()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }

  const livres = painel.estoqueLivres.filter(e => e.ativo || String(e.id) === vinculo)

  return (
    <Modal titulo={linha?.cliente ?? 'Novo cliente'} onClose={onClose} confirmarAoFechar largura="lg"
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />}>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Cliente">
          <input value={cliente} onChange={e => setCliente(e.target.value.toUpperCase())} disabled={linha != null && linha.freteId == null}
                 className={classeCampo} />
        </Campo>
        {(novo ? levaVolumes : temFrete) && (
          <Campo rotulo="Outros nomes" dica="Como vem no controle de carregamento, separado por vírgula">
            <input value={apelidos} onChange={e => setApelidos(e.target.value.toUpperCase())} className={classeCampo} />
          </Campo>
        )}
      </div>
      {novo && (
        <label className="inline-flex items-center gap-2 min-h-11 text-sm">
          <input type="checkbox" checked={levaVolumes} onChange={e => setLevaVolumes(e.target.checked)} className="w-5 h-5" />
          Leva volumes (entra no frete das viagens)
        </label>
      )}

      {(temFrete || (novo && levaVolumes)) && (
        <fieldset className="space-y-2">
          <legend className="font-display text-base font-semibold text-gray-900">Frete por caixa</legend>
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <CampoPreco rotulo="Caixa normal" valor={caixa} onChange={setCaixa} placeholder="9,00" />
            <CampoPreco rotulo="ProdutoE" valor={produtoE} onChange={setProdutoE}
                        dica={metade != null ? `Metade da caixa: ${brl(metade)}` : undefined}
                        placeholder={metade != null ? String(metade).replace('.', ',') : '4,50'} />
            <CampoPreco rotulo="ProdutoD" valor={produtoD} onChange={setProdutoD} placeholder="3,00" />
          </div>
        </fieldset>
      )}

      {temFrete && linha && (
        <Campo rotulo="Cadastro no estoque" dica="É onde ficam o frete por pacote e os preços de venda deste cliente.">
          <select value={vinculo} onChange={e => mudarVinculo(e.target.value)} className={classeCampo}>
            {linha.estoqueId != null && <option value={String(linha.estoqueId)}>{linha.nomeEstoque ?? linha.cliente}</option>}
            {livres.map(e => <option key={e.id} value={String(e.id)}>{e.nome}</option>)}
            <option value={CRIAR}>Cadastrar com o nome {cliente.trim() || 'do cliente'}</option>
            <option value="">Sem cadastro (frete de pacote no padrão, sem venda)</option>
          </select>
        </Campo>
      )}

      {temEstoque ? (
        <>
          <fieldset className="space-y-2">
            <legend className="font-display text-base font-semibold text-gray-900">Frete por pacote</legend>
            <div className="grid grid-cols-2 gap-3">
              <CampoPreco rotulo="ProdutoB (150)" valor={produtoB} onChange={setProdutoB} dica="0 = não paga" />
              <CampoPreco rotulo="ProdutoC (100)" valor={produtoC} onChange={setProdutoC} dica="0 = não paga" />
            </div>
          </fieldset>
          <fieldset className="space-y-2">
            <legend className="font-display text-base font-semibold text-gray-900">Venda</legend>
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {painel.tipos.map(t => (
                <CampoPreco key={t.id} rotulo={`${nomeTipo(t)}, por pacote`} valor={precos[t.id] ?? ''}
                            onChange={v => setPrecos({ ...precos, [t.id]: v })} placeholder="sem preço" />
              ))}
              <CampoPreco rotulo="ProdutoA, por quilo" valor={produtoA} onChange={setProdutoA} placeholder="sem preço" dica="Até 4 casas: 20,4575" />
            </div>
          </fieldset>
        </>
      ) : (
        <Aviso tipo="atencao">Sem cadastro no estoque: a viagem cobra R$ 10,00 por pacote de produtoB e de produtoC, e as saídas desse cliente não entram no estoque.</Aviso>
      )}

      {temFrete && (
        <label className="inline-flex items-center gap-2 min-h-11 text-sm">
          <input type="checkbox" checked={ativo} onChange={e => setAtivo(e.target.checked)} className="w-5 h-5" />
          Ativo
        </label>
      )}
    </Modal>
  )
}
