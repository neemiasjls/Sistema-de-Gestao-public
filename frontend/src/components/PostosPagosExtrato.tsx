import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { CheckCircle2, RotateCcw } from 'lucide-react'
import { Carregando } from './ui/Estados'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { useConfirmar } from './ui/Confirmar'
import { formatBRL } from '../services/api'
import { boletosContasApi } from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { sicoobApi, type BoletoPostoVisto, type ConferenciaPostos, type DuvidaPosto } from '../services/sicoobApi'
import { limparDescricao } from '../utils/descricaoExtrato'
import { dataBr } from '../utils/formato'

// Boletos de posto pagos (ABA-07): depois de ler o extrato, o débito que casa exato com um boleto de posto
// em aberto vira pago com a data do débito; o que ficou em dúvida ele escolhe. Nada é pago pelo sistema.

const diaMes = (d: string) => dataBr(d).slice(0, 5)

export default function PostosPagosExtrato({ inicio, fim, versao }: { inicio: string; fim: string; versao: number }) {
  const [conf, setConf] = useState<ConferenciaPostos | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [ocupado, setOcupado] = useState<string | null>(null)
  const confirmar = useConfirmar()

  const conferir = useCallback(async () => {
    setCarregando(true)
    try {
      const { data } = await sicoobApi.conferirPostos(inicio || undefined, fim || undefined)
      setConf(data)
      setErro(null)
      const agora = data.marcados.filter(m => m.agora).length
      if (agora > 0) toast.success(agora === 1 ? '1 boleto de posto marcado pago pelo extrato.' : `${agora} boletos de posto marcados pagos pelo extrato.`)
    } catch (e) {
      setErro(await erroApi(e, 'Não consegui conferir os boletos dos postos com o extrato.'))
    } finally { setCarregando(false) }
  }, [inicio, fim])

  // Só confere quando o extrato acabou de ser lido (versao muda)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (versao > 0) conferir() }, [versao])

  const desfazer = async (b: BoletoPostoVisto, data: string) => {
    const ok = await confirmar({
      titulo: 'Desfazer o pagamento?',
      texto: <>Boleto de <b>{b.posto}</b>{b.numero ? ` · ${b.numero}` : ''} · {formatBRL(b.valor)}, pago em {dataBr(data)}.
        Ele volta para em aberto e o extrato não o marca de novo com este débito.</>,
      confirmar: 'Desfazer',
      perigo: false,
    })
    if (!ok) return
    setOcupado(`d-${b.id}`)
    try {
      await boletosContasApi.pagarPosto(b.id, null)
      toast.success('Pagamento desfeito.')
      await conferir()
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui desfazer o pagamento.'))
    } finally { setOcupado(null) }
  }

  const escolher = async (d: DuvidaPosto, b: BoletoPostoVisto) => {
    setOcupado(`m-${d.chave}-${b.id}`)
    try {
      await sicoobApi.marcarPosto(b.id, d.chave, d.data)
      toast.success(`Boleto de ${b.posto} marcado pago em ${dataBr(d.data)}.`)
      await conferir()
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui marcar o boleto.'))
    } finally { setOcupado(null) }
  }

  const boletoTexto = (b: BoletoPostoVisto) => (
    <>
      <b className="font-semibold">{b.posto || 'Posto sem nome'}</b>
      {b.numero && <> · {b.numero}</>}
      {' · '}<span className="tabular-nums">{formatBRL(b.valor)}</span>
      {' · '}{b.vencimento ? `vence ${dataBr(b.vencimento)}` : 'sem vencimento'}
    </>
  )

  return (
    <section aria-labelledby="postos-pagos" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
      <h2 id="postos-pagos" className="text-base font-semibold text-gray-900">Boletos de posto pagos</h2>

      {!conf && carregando && <Carregando linhas={2} rotulo="Conferindo os boletos dos postos" />}
      {erro && !carregando && (
        <Aviso tipo="atencao" acao={<Botao tamanho="sm" variante="secundario" icone={RotateCcw} onClick={conferir}>Tentar de novo</Botao>}>
          {erro}
        </Aviso>
      )}

      {conf && !erro && (
        conf.marcados.length === 0 && conf.duvidas.length === 0 ? (
          <p className="text-sm text-gray-600">Nenhum boleto de posto pago neste período do extrato.</p>
        ) : (
          <>
            {conf.duvidas.length > 0 && (
              <ul className="space-y-2">
                {conf.duvidas.map(d => (
                  <li key={d.chave} className="rounded-lg border border-tinta-200 bg-tinta-50 px-3 py-2 space-y-2">
                    <p className="text-sm text-tinta-900">
                      Débito de <b className="tabular-nums">{formatBRL(d.valor)}</b> em {dataBr(d.data)}
                      {' · '}{limparDescricao(d.descricao, '', '').titulo || 'sem descrição no banco'}
                    </p>
                    <p className="text-sm text-gray-600">Pode ser um destes boletos ({d.motivo}):</p>
                    {d.opcoes.length === 0 ? (
                      <p className="text-sm text-gray-600">Nenhum boleto de posto em aberto com este valor.</p>
                    ) : (
                      <ul className="space-y-1.5">
                        {d.opcoes.map(b => (
                          <li key={b.id} className="flex flex-wrap items-center gap-2 rounded-lg bg-white border border-gray-200 px-3 py-2">
                            <p className="flex-1 min-w-[200px] text-sm text-gray-900 break-words">{boletoTexto(b)}</p>
                            <Botao tamanho="sm" variante="secundario" carregando={ocupado === `m-${d.chave}-${b.id}`}
                                   disabled={ocupado !== null} onClick={() => escolher(d, b)}>
                              É este: marcar pago em {diaMes(d.data)}
                            </Botao>
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ul>
            )}

            {conf.marcados.length > 0 && (
              <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
                {conf.marcados.map(m => (
                  <li key={`${m.boleto.id}-${m.chave}`} className="flex flex-wrap items-center gap-2 px-3 py-2">
                    <div className="flex-1 min-w-[200px]">
                      <p className="text-sm text-gray-900 break-words">{boletoTexto(m.boleto)}</p>
                      <p className="flex items-center gap-1.5 text-sm text-emerald-800">
                        <CheckCircle2 size={14} aria-hidden className="shrink-0" />
                        Pago em {diaMes(m.data)} — identificado no extrato{m.agora ? ' agora' : ''}
                      </p>
                    </div>
                    <Botao tamanho="sm" variante="fantasma" icone={RotateCcw} carregando={ocupado === `d-${m.boleto.id}`}
                           disabled={ocupado !== null} onClick={() => desfazer(m.boleto, m.data)}>
                      Desfazer
                    </Botao>
                  </li>
                ))}
              </ul>
            )}
          </>
        )
      )}
    </section>
  )
}
