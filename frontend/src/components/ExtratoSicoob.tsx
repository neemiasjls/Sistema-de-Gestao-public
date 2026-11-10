import { useCallback, useEffect, useMemo, useState } from 'react'
import { Landmark, RefreshCw } from 'lucide-react'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { Segmentado } from './ui/Abas'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { formatBRL } from '../services/api'
import { boletosContasApi, type ConfigEmissao } from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { sicoobApi, type ExtratoSicoob } from '../services/sicoobApi'
import { dataBr } from '../utils/formato'
import { limparDescricao } from '../utils/descricaoExtrato'
import ConferenciaBoletosPagos from './ConferenciaBoletosPagos'
import PixBaixaBoletos from './PixBaixaBoletos'
import PostosPagosExtrato from './PostosPagosExtrato'

// Aba "Extrato do banco" de Boletos: o que entrou e saiu da conta PJ no Sicoob,
// lido pela API do banco (só consulta, BOL-10). Entrada vira boleto só pelo pagador
// (Pix de cliente pelo CNPJ ou pelo nome, BOL-14); nunca só pelo valor.

type Mostrar = 'entradas' | 'saidas' | 'tudo'

const inputCls = 'w-full min-h-11 sm:min-h-0 text-sm border border-gray-300 rounded-lg px-3 py-2'

const hojeLocal = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const inicioDoMes = () => `${hojeLocal().slice(0, 8)}01`
const ROTULO_CLIENTE: Record<string, string> = { GIRASSOL: 'Girassol (Horacio)', CD: 'CD (Cedro Frutas)' }
const digitos = (s: string) => s.replace(/\D/g, '')

export default function ExtratoSicoob() {
  const [inicio, setInicio] = useState(inicioDoMes)
  const [fim, setFim] = useState(hojeLocal)
  const [mostrar, setMostrar] = useState<Mostrar>('entradas')
  const [extrato, setExtrato] = useState<ExtratoSicoob | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  // Período do extrato lido por último: a conferência dos postos usa o mesmo
  const [lido, setLido] = useState({ de: '', ate: '', versao: 0 })
  const [pagadores, setPagadores] = useState<ConfigEmissao[]>([])

  const carregar = useCallback(async (de: string, ate: string) => {
    setCarregando(true)
    try {
      const { data } = await sicoobApi.extrato(de || undefined, ate || undefined)
      setExtrato(data)
      setErro(null)
      if (data.configurado) setLido(l => ({ de, ate, versao: l.versao + 1 }))
    } catch (e) {
      setErro(await erroApi(e, 'Não consegui ler o extrato do Sicoob.'))
    } finally { setCarregando(false) }
  }, [])

  useEffect(() => { carregar(inicioDoMes(), hojeLocal()) }, [carregar])
  // Só para dizer de qual cliente é o CNPJ do Pix; sem a configuração, a descrição sai sem o cliente
  useEffect(() => {
    boletosContasApi.configEmissao().then(r => setPagadores(r.data.clientes.map(c => c.config))).catch(() => {})
  }, [])
  const clienteDoCnpj = useMemo(() => {
    const mapa = new Map(pagadores.filter(c => c.pagadorCnpj).map(c => [digitos(c.pagadorCnpj), ROTULO_CLIENTE[c.cliente] ?? c.cliente]))
    return (doc: string) => mapa.get(doc) ?? null
  }, [pagadores])

  const lista = (extrato?.lancamentos ?? []).filter(l =>
    mostrar === 'tudo' ? true : mostrar === 'entradas' ? l.credito : !l.credito)
  const pixParaBaixar = (extrato?.lancamentos ?? []).filter(l => (l.pix?.opcoes.length ?? 0) > 0).length

  return (
    <div className="space-y-4">
      <ConferenciaBoletosPagos mudou={() => carregar(inicio, fim)} />
      <section aria-label="Período" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
        <div className="grid grid-cols-1 sm:grid-cols-[1fr_1fr_auto] gap-3 items-end">
          <label className="block text-sm">
            <span className="text-gray-600">De</span>
            <input type="date" value={inicio} max={fim} onChange={e => setInicio(e.target.value)} className={inputCls} />
          </label>
          <label className="block text-sm">
            <span className="text-gray-600">Até</span>
            <input type="date" value={fim} min={inicio} max={hojeLocal()} onChange={e => setFim(e.target.value)} className={inputCls} />
          </label>
          <Botao variante="primario" icone={RefreshCw} carregando={carregando} textoCarregando="Lendo o banco..."
                 onClick={() => carregar(inicio, fim)}>
            Ver extrato
          </Botao>
        </div>
        <p className="text-sm text-gray-500">Conta PJ no Sicoob. Só leitura: o sistema não paga nem transfere nada.</p>
      </section>

      {!extrato && carregando && <Carregando linhas={4} rotulo="Lendo o extrato do Sicoob" />}
      {erro && !carregando && <ErroCarga mensagem={erro} onTentar={() => carregar(inicio, fim)} />}
      {extrato && !extrato.configurado && (
        <Aviso tipo="atencao" titulo="Extrato do Sicoob ainda não ligado">{extrato.erro}</Aviso>
      )}

      {extrato?.configurado && !erro && (
        <>
          <dl className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <Numero rotulo="Saldo agora" valor={extrato.saldo} />
            <Numero rotulo={`Entradas de ${dataBr(extrato.inicio).slice(0, 5)} a ${dataBr(extrato.fim).slice(0, 5)}`} valor={extrato.entradas} />
            <Numero rotulo="Saídas no período" valor={extrato.saidas} />
          </dl>

          {pixParaBaixar > 0 && (
            <Aviso tipo="info" titulo={pixParaBaixar === 1 ? '1 Pix de cliente bate com boletos a receber' : `${pixParaBaixar} Pix de clientes batem com boletos a receber`}>
              Confira e dê baixa: o boleto é baixado no Sicoob e fica recebido na data do Pix.
            </Aviso>
          )}

          {lido.versao > 0 && <PostosPagosExtrato inicio={lido.de} fim={lido.ate} versao={lido.versao} />}

          <Segmentado valor={mostrar} onChange={setMostrar} rotulo="Mostrar"
                      opcoes={[['entradas', 'Entradas'], ['saidas', 'Saídas'], ['tudo', 'Tudo']]} />

          {lista.length === 0 ? (
            <Vazio icone={Landmark} titulo={mostrar === 'saidas' ? 'Nenhuma saída no período' : mostrar === 'entradas' ? 'Nenhuma entrada no período' : 'Nenhum lançamento no período'} />
          ) : (
            <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
              {lista.map((l, i) => {
                const pixCliente = l.pix?.cliente ? ROTULO_CLIENTE[l.pix.cliente] : null
                const desc = limparDescricao(l.descricao, l.complemento, l.cpfCnpj, doc => clienteDoCnpj(doc) ?? pixCliente)
                return (
                <li key={`${l.id}-${i}`} className="px-4 py-3 space-y-2">
                  <div className="flex items-start gap-3">
                    <span className="w-12 shrink-0 text-sm text-gray-500 tabular-nums pt-0.5">{l.data ? dataBr(l.data).slice(0, 5) : '—'}</span>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-gray-900 break-words">{desc.titulo || 'Sem descrição no banco'}</p>
                      {desc.detalhe && <p className="text-sm text-gray-500 break-words">{desc.detalhe}</p>}
                    </div>
                    <span className={`shrink-0 text-sm tabular-nums whitespace-nowrap ${l.credito ? 'font-semibold text-gray-900' : 'text-gray-600'}`}>
                      {l.credito ? '+' : '−'} {formatBRL(l.valor)}
                    </span>
                  </div>

                  {l.pix && <PixBaixaBoletos l={l} pix={l.pix} mudou={() => carregar(inicio, fim)} />}
                </li>
                )
              })}
            </ul>
          )}
        </>
      )}
    </div>
  )
}

function Numero({ rotulo, valor }: { rotulo: string; valor: number | null | undefined }) {
  return (
    <div className="bg-white border border-gray-200 rounded-xl px-4 py-3">
      <dt className="text-sm text-gray-500">{rotulo}</dt>
      <dd className="text-xl font-semibold text-gray-900 tabular-nums">{valor == null ? 'Não informado pelo banco' : formatBRL(valor)}</dd>
    </div>
  )
}
