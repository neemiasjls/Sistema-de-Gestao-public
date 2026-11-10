import { useState } from 'react'
import toast from 'react-hot-toast'
import { CheckCircle2, SearchCheck } from 'lucide-react'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { ErroCarga } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { formatBRL } from '../services/api'
import { boletosContasApi } from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { sicoobApi, type BoletoConferido, type ConferenciaBoletos, type ResumoConferenciaTudo, type SituacaoBoletoBanco } from '../services/sicoobApi'
import { dataBr } from '../utils/formato'

// Conferência dos boletos a receber da CD e da Girassol com o extrato do Sicoob (BOL-10).
// O banco credita UM valor por dia ("CRÉD.LIQUIDAÇÃO COBRANÇA") com todos os boletos pagos;
// o servidor acha quais boletos somam cada crédito. Aqui só se mostra e se dá a baixa.

const nome = (b: BoletoConferido) => `boleto ${b.nossoNumero || 'sem número'} da ${b.rotulo} (${formatBRL(b.valor)})`

export default function ConferenciaBoletosPagos({ mudou }: { mudou: () => void }) {
  const confirmar = useConfirmar()
  const [dados, setDados] = useState<ConferenciaBoletos | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [baixando, setBaixando] = useState<number | 'todos' | null>(null)
  const [verCreditos, setVerCreditos] = useState(false)
  const [consultando, setConsultando] = useState<number | null>(null)
  const [situacoes, setSituacoes] = useState<Record<number, SituacaoBoletoBanco | string>>({})
  const [resumo, setResumo] = useState<ResumoConferenciaTudo | string | null>(null)

  // Pergunta ao banco a situação de um boleto (pago com juros não fecha na soma do crédito do dia)
  const consultar = async (b: BoletoConferido) => {
    setConsultando(b.id)
    try {
      const { data } = await sicoobApi.situacaoBoleto(b.id)
      setSituacoes(s => ({ ...s, [b.id]: data }))
    } catch (e) {
      const msg = await erroApi(e, 'Não consegui consultar o boleto no Sicoob.')
      setSituacoes(s => ({ ...s, [b.id]: msg }))
    } finally { setConsultando(null) }
  }

  // Primeiro a conferência com o banco (marca o que já foi pago: boletos liquidados e postos), depois a soma dos créditos
  const conferirComBanco = async () => {
    setCarregando(true)
    try {
      const { data } = await sicoobApi.conferirTudo()
      setResumo(data)
      if (data.receber.marcados.length || data.postos.marcados.length) mudou()
    } catch (e) { setResumo(await erroApi(e, 'Não consegui fazer a conferência automática com o Sicoob.')) }
    await conferir()
  }

  const conferir = async () => {
    setCarregando(true)
    try {
      const { data } = await sicoobApi.conferirBoletos()
      setDados(data); setErro(null)
    } catch (e) { setErro(await erroApi(e, 'Não consegui conferir com o Sicoob.')) }
    finally { setCarregando(false) }
  }

  const baixar = async (itens: { boleto: BoletoConferido; dataCredito: string }[], todos: boolean) => {
    if (todos && !await confirmar({
      titulo: `Dar baixa em ${itens.length} ${itens.length === 1 ? 'boleto' : 'boletos'}?`,
      texto: 'Cada um fica como recebido na data do crédito no banco.', confirmar: 'Dar baixa', perigo: false,
    })) return
    setBaixando(todos ? 'todos' : itens[0].boleto.id)
    let feitos = 0
    try {
      for (const i of itens) { await boletosContasApi.marcarRecebido(i.boleto.id, i.dataCredito); feitos++ }
      toast.success(feitos === 1 ? `Baixa dada: ${nome(itens[0].boleto)} em ${dataBr(itens[0].dataCredito)}.` : `Baixa dada em ${feitos} boletos.`)
    } catch (e) {
      toast.error(await erroApi(e, `Não consegui dar baixa${feitos ? ` (${feitos} já foram)` : ''}.`))
    } finally {
      setBaixando(null)
      mudou()
      await conferir()
    }
  }

  return (
    <section aria-label="Boletos pagos da CD e da Girassol" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-center gap-3">
        <div className="flex-1">
          <h3 className="font-semibold text-gray-900">Boletos pagos da CD e da Girassol</h3>
          <p className="text-sm text-gray-600">O Sicoob junta num crédito só todos os boletos pagos no dia. O sistema acha quais boletos somam cada crédito dos últimos 60 dias.</p>
        </div>
        <Botao variante="primario" icone={SearchCheck} carregando={carregando} textoCarregando="Conferindo..." onClick={conferirComBanco}>
          Conferir com o banco
        </Botao>
      </div>

      {typeof resumo === 'string' && <Aviso tipo="atencao" titulo="Conferência automática não rodou">{resumo}</Aviso>}
      {resumo && typeof resumo !== 'string' && <ResumoConferencia r={resumo} />}
      {erro && <ErroCarga mensagem={erro} onTentar={conferir} />}
      {dados && !dados.configurado && <Aviso tipo="atencao" titulo="Extrato do Sicoob ainda não ligado">{dados.erro}</Aviso>}

      {dados?.configurado && !erro && (
        <div className="space-y-3">
          {dados.pagosSemBaixa.length === 0 ? (
            <p className="flex items-center gap-1.5 text-sm text-emerald-800">
              <CheckCircle2 size={16} aria-hidden /> Nenhum boleto pago no banco esperando baixa.
            </p>
          ) : (
            <div className="rounded-lg border border-tinta-200 bg-tinta-50 p-3 space-y-2">
              <div className="flex flex-col sm:flex-row sm:items-center gap-2">
                <p className="flex-1 text-sm font-semibold text-tinta-900">
                  {dados.pagosSemBaixa.length === 1 ? '1 boleto pago no banco, falta dar baixa' : `${dados.pagosSemBaixa.length} boletos pagos no banco, falta dar baixa`}
                </p>
                {dados.pagosSemBaixa.length > 1 && (
                  <Botao tamanho="sm" carregando={baixando === 'todos'} disabled={baixando !== null}
                         onClick={() => baixar(dados.pagosSemBaixa, true)}>Dar baixa em todos</Botao>
                )}
              </div>
              <ul className="space-y-1.5">
                {dados.pagosSemBaixa.map(p => (
                  <li key={p.boleto.id} className="flex flex-wrap items-center gap-2 text-sm">
                    <span className="flex-1 min-w-[220px] text-gray-900">
                      Boleto <b>{p.boleto.nossoNumero}</b> da {p.boleto.rotulo}, {formatBRL(p.boleto.valor)}
                      {p.boleto.vencimento && <> · venceu {dataBr(p.boleto.vencimento)}</>}
                      {' · '}no crédito de {formatBRL(p.valorCredito)} em {dataBr(p.dataCredito)}
                    </span>
                    <Botao tamanho="sm" carregando={baixando === p.boleto.id} disabled={baixando !== null}
                           onClick={() => baixar([p], false)}>Dar baixa em {dataBr(p.dataCredito).slice(0, 5)}</Botao>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {dados.creditos.filter(c => c.duvida).map(c => (
            <Aviso key={`d-${c.data}-${c.valor}`} tipo="atencao" titulo={`Crédito de ${formatBRL(c.valor)} em ${dataBr(c.data)}: mais de uma combinação`}>
              Usei {c.boletos.map(b => b.nossoNumero).join(' + ')}. Também fecha com: {c.alternativas.map(a => a.map(b => b.nossoNumero).join(' + ')).join('; ')}. Confira qual foi pago.
            </Aviso>
          ))}

          {dados.vencidosSemCredito.length > 0 && (
            <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 space-y-2">
              <p className="text-sm font-semibold text-amber-900">
                {dados.vencidosSemCredito.length === 1 ? '1 boleto vencido sem crédito no banco' : `${dados.vencidosSemCredito.length} boletos vencidos sem crédito no banco`}
              </p>
              <p className="text-sm text-amber-900">Pago com juros ou desconto não fecha na soma: consulte o boleto no banco.</p>
              <ul className="space-y-2">
                {dados.vencidosSemCredito.map(b => {
                  const s = situacoes[b.id]
                  return (
                    <li key={b.id} className="text-sm space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="flex-1 min-w-[220px] text-gray-900">
                          {nome(b)}, venceu {b.vencimento ? dataBr(b.vencimento) : 'sem data'}
                        </span>
                        <Botao tamanho="sm" variante="secundario" carregando={consultando === b.id} textoCarregando="Consultando..."
                               disabled={consultando !== null || baixando !== null || !b.nossoNumero} onClick={() => consultar(b)}>
                          Consultar no banco
                        </Botao>
                      </div>
                      {typeof s === 'string' && <p className="text-red-700">{s}</p>}
                      {s && typeof s !== 'string' && (
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="flex-1 min-w-[220px] text-gray-800">
                            No Sicoob: <b>{s.situacao || 'sem situação'}</b>
                            {s.liquidado && (s.dataLiquidacao ? <> · pago em {dataBr(s.dataLiquidacao)}</> : <> · sem data do pagamento</>)}
                            {s.valorPago != null && <> · {formatBRL(s.valorPago)}</>}
                          </span>
                          {s.liquidado && s.dataLiquidacao && (
                            <Botao tamanho="sm" carregando={baixando === b.id} disabled={baixando !== null}
                                   onClick={() => baixar([{ boleto: b, dataCredito: s.dataLiquidacao! }], false)}>
                              Dar baixa em {dataBr(s.dataLiquidacao).slice(0, 5)}
                            </Botao>
                          )}
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>
            </div>
          )}

          {dados.creditos.some(c => !c.achou) && (
            <Aviso tipo="info" titulo="Crédito de cobrança sem boleto que some o valor"
                   itens={dados.creditos.filter(c => !c.achou).map(c => `${formatBRL(c.valor)} em ${dataBr(c.data)}: pode ser boleto de outro cliente ou com juros.`)} />
          )}

          {dados.creditos.length > 0 && (
            <div>
              <button type="button" onClick={() => setVerCreditos(v => !v)} aria-expanded={verCreditos}
                      className="text-sm text-tinta-700 underline underline-offset-2 min-h-10 sm:min-h-0">
                {verCreditos ? 'Esconder' : 'Ver'} os {dados.creditos.length} créditos de cobrança e seus boletos
              </button>
              {verCreditos && (
                <ul className="mt-2 divide-y divide-gray-100 border border-gray-200 rounded-lg">
                  {dados.creditos.map(c => (
                    <li key={`${c.data}-${c.valor}`} className="px-3 py-2 text-sm">
                      <span className="tabular-nums font-medium">{dataBr(c.data)} · {formatBRL(c.valor)}</span>
                      <span className="block text-gray-700">
                        {c.achou ? c.boletos.map(b => `${b.nossoNumero} ${b.rotulo} ${formatBRL(b.valor)}${b.recebidoEm ? '' : ' (sem baixa)'}`).join(' + ') : 'Nenhuma combinação de boletos'}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
      )}
    </section>
  )
}

// O que a conferência com o banco fez: recebidos marcados, postos marcados e Pix esperando a confirmação dele
function ResumoConferencia({ r }: { r: ResumoConferenciaTudo }) {
  const { receber, postos, pix } = r
  const linhas: string[] = []
  linhas.push(receber.marcados.length === 0
    ? `Nenhum boleto a receber liquidado no banco (${receber.consultados} de ${receber.emAberto} em aberto consultados).`
    : `${receber.marcados.length === 1 ? '1 boleto a receber marcado' : `${receber.marcados.length} boletos a receber marcados`} como recebido: ` +
      receber.marcados.map(m => `${m.nossoNumero} em ${dataBr(m.data)}${m.semDataNoBanco ? ' (o banco não informou a data)' : ''}${m.valorPago != null ? `, pago ${formatBRL(m.valorPago)}` : ''}`).join('; ') + '.')
  if (receber.parouNoLimite) linhas.push(`O Sicoob pediu um tempo: ${receber.emAberto - receber.consultados} boletos ficaram para a próxima conferência.`)
  else if (receber.emAberto > receber.consultados && !receber.erro) linhas.push(`${receber.emAberto - receber.consultados} boletos ficaram para a próxima conferência.`)
  if (receber.erro) linhas.push(`Boletos a receber: ${receber.erro}`)
  for (const n of receber.naoMarcados) linhas.push(`Boleto ${n.nossoNumero}${n.valor != null ? ` (${formatBRL(n.valor)})` : ''}: ${n.situacao}. Não marquei.`)
  linhas.push(postos.erro ? `Postos: ${postos.erro}`
    : postos.marcados.length === 0 ? 'Nenhum boleto de posto novo pago no extrato.'
    : `${postos.marcados.length === 1 ? '1 boleto de posto marcado' : `${postos.marcados.length} boletos de posto marcados`} como pago: ` +
      postos.marcados.map(p => `${p.posto} ${p.numero} em ${dataBr(p.data)}`).join('; ') + '.')
  if (!postos.erro && postos.duvidas > 0) linhas.push(`${postos.duvidas === 1 ? '1 débito de posto' : `${postos.duvidas} débitos de posto`} em dúvida: veja na aba Extrato.`)
  linhas.push(pix.erro ? `Pix: ${pix.erro}`
    : pix.aguardando.length === 0 ? 'Nenhum Pix de boleto esperando confirmação.'
    : `${pix.aguardando.length === 1 ? '1 Pix de boleto aguardando' : `${pix.aguardando.length} Pix de boleto aguardando`} sua confirmação na aba Extrato: ` +
      pix.aguardando.map(p => `${formatBRL(p.valor)} em ${dataBr(p.data)} de ${p.pagador} (${p.duvida ? 'mais de uma combinação' : p.boletos.join(' + ')})`).join('; ') + '.')
  const tudoCerto = !receber.erro && !postos.erro && !pix.erro && !receber.parouNoLimite
  return <Aviso tipo={tudoCerto ? 'info' : 'atencao'} titulo="Conferência com o banco" itens={linhas} />
}
