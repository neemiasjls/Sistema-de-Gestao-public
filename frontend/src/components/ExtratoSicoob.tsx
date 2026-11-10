import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { CheckCircle2, Landmark, RefreshCw } from 'lucide-react'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { Segmentado } from './ui/Abas'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { formatBRL } from '../services/api'
import { boletosContasApi } from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { sicoobApi, type BoletoSugerido, type ExtratoSicoob, type LancamentoSicoob } from '../services/sicoobApi'
import { dataBr } from '../utils/formato'
import ConferenciaBoletosPagos from './ConferenciaBoletosPagos'
import PixBaixaBoletos from './PixBaixaBoletos'

// Aba "Extrato do banco" de Boletos: o que entrou e saiu da conta PJ no Sicoob,
// lido pela API do banco (só consulta, BOL-10). Entrada de mesmo valor que um
// boleto a receber em aberto oferece "Marcar recebido" com a data do crédito.

type Mostrar = 'entradas' | 'saidas' | 'tudo'

const inputCls = 'w-full min-h-11 sm:min-h-0 text-sm border border-gray-300 rounded-lg px-3 py-2'

const hojeLocal = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
const inicioDoMes = () => `${hojeLocal().slice(0, 8)}01`

export default function ExtratoSicoob() {
  const [inicio, setInicio] = useState(inicioDoMes)
  const [fim, setFim] = useState(hojeLocal)
  const [mostrar, setMostrar] = useState<Mostrar>('entradas')
  const [extrato, setExtrato] = useState<ExtratoSicoob | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [marcando, setMarcando] = useState<number | null>(null)

  const carregar = useCallback(async (de: string, ate: string) => {
    setCarregando(true)
    try {
      const { data } = await sicoobApi.extrato(de || undefined, ate || undefined)
      setExtrato(data)
      setErro(null)
    } catch (e) {
      setErro(await erroApi(e, 'Não consegui ler o extrato do Sicoob.'))
    } finally { setCarregando(false) }
  }, [])

  useEffect(() => { carregar(inicioDoMes(), hojeLocal()) }, [carregar])

  const marcar = async (l: LancamentoSicoob, b: BoletoSugerido) => {
    if (!l.data) return
    setMarcando(b.id)
    try {
      await boletosContasApi.marcarRecebido(b.id, l.data)
      toast.success(`Boleto ${b.nossoNumero} da ${b.rotulo} marcado como recebido em ${dataBr(l.data)}.`)
      await carregar(inicio, fim)
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui marcar o boleto.'))
    } finally { setMarcando(null) }
  }

  const lista = (extrato?.lancamentos ?? []).filter(l =>
    mostrar === 'tudo' ? true : mostrar === 'entradas' ? l.credito : !l.credito)
  const paraMarcar = (extrato?.lancamentos ?? []).filter(l => l.sugestoes.length > 0).length
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

          {paraMarcar > 0 && (
            <Aviso tipo="info" titulo={paraMarcar === 1 ? '1 entrada bate com boleto a receber' : `${paraMarcar} entradas batem com boletos a receber`}>
              Confira e marque como recebido. A data usada é a do crédito no banco.
            </Aviso>
          )}

          {pixParaBaixar > 0 && (
            <Aviso tipo="info" titulo={pixParaBaixar === 1 ? '1 Pix de cliente bate com boletos a receber' : `${pixParaBaixar} Pix de clientes batem com boletos a receber`}>
              Confira e dê baixa: o boleto é baixado no Sicoob e fica recebido na data do Pix.
            </Aviso>
          )}

          <Segmentado valor={mostrar} onChange={setMostrar} rotulo="Mostrar"
                      opcoes={[['entradas', 'Entradas'], ['saidas', 'Saídas'], ['tudo', 'Tudo']]} />

          {lista.length === 0 ? (
            <Vazio icone={Landmark} titulo={mostrar === 'saidas' ? 'Nenhuma saída no período' : mostrar === 'entradas' ? 'Nenhuma entrada no período' : 'Nenhum lançamento no período'} />
          ) : (
            <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
              {lista.map((l, i) => (
                <li key={`${l.id}-${i}`} className="px-4 py-3 space-y-2">
                  <div className="flex items-start gap-3">
                    <span className="w-12 shrink-0 text-sm text-gray-500 tabular-nums pt-0.5">{l.data ? dataBr(l.data).slice(0, 5) : '—'}</span>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-medium text-gray-900 break-words">{l.descricao || 'Sem descrição no banco'}</p>
                      {(l.complemento || l.cpfCnpj) && (
                        <p className="text-sm text-gray-500 break-words">{[l.complemento, l.cpfCnpj].filter(Boolean).join(' · ')}</p>
                      )}
                    </div>
                    <span className={`shrink-0 text-sm tabular-nums whitespace-nowrap ${l.credito ? 'font-semibold text-gray-900' : 'text-gray-600'}`}>
                      {l.credito ? '+' : '−'} {formatBRL(l.valor)}
                    </span>
                  </div>

                  {l.jaMarcado && (
                    <p className="sm:pl-[60px] flex items-center gap-1.5 text-sm text-emerald-800">
                      <CheckCircle2 size={14} aria-hidden />
                      Boleto {l.jaMarcado.nossoNumero} da {l.jaMarcado.rotulo} já marcado como recebido
                    </p>
                  )}
                  {l.pix && <PixBaixaBoletos l={l} pix={l.pix} mudou={() => carregar(inicio, fim)} />}
                  {l.sugestoes.map(b => (
                    <div key={b.id} className="sm:ml-[60px] flex flex-wrap items-center gap-2 rounded-lg border border-tinta-200 bg-tinta-50 px-3 py-2">
                      <p className="flex-1 min-w-[200px] text-sm text-tinta-900">
                        Pode ser o boleto <b>{b.nossoNumero || 'sem número'}</b> da {b.rotulo}
                        {b.dataCarga && <> · carga de {dataBr(b.dataCarga)}</>}
                        {b.vencimento && <> · vence {dataBr(b.vencimento)}</>}
                      </p>
                      <Botao tamanho="sm" variante="secundario" carregando={marcando === b.id} disabled={marcando !== null || !l.data}
                             onClick={() => marcar(l, b)}>
                        Marcar recebido em {l.data ? dataBr(l.data).slice(0, 5) : '—'}
                      </Botao>
                    </div>
                  ))}
                </li>
              ))}
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
