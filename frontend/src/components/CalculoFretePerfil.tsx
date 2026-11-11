import { useCallback, useEffect, useMemo, useState } from 'react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { Carregando, ErroCarga } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { boletosContasApi as api, type CalculoFretePerfil as Calculo, type LinhaFretePerfil } from '../services/boletosContasApi'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { dataBr, lerNum } from '../utils/formato'

// Cálculo do frete da Girassol de uma carga (BOL-17): o cálculo das notas (só ver) e a queda do volume
// por caixa de cada tipo normal (produtoE e produtoD nunca levam). Só Admin e Bento lançam a queda;
// os outros (e a tela Viagens, com somenteLeitura) só veem.

const inputCls = 'w-full sm:w-28 min-h-11 sm:min-h-0 text-sm text-right tabular-nums border border-gray-300 rounded-lg px-3 py-2'
const QUEDA_MAXIMA = 999.99
const arred = (v: number) => Math.round(v * 100) / 100
const caixasBr = (v: number) => v.toLocaleString('pt-BR', { maximumFractionDigits: 2 })
const paraCampo = (v: number | undefined) => (v ? v.toFixed(2).replace('.', ',') : '')
const dataDe = (iso: string) => dataBr(iso.slice(0, 10))

// Texto digitado → número válido (null = vazio; NaN = inválido)
function lerQueda(t: string): number | null {
  const s = t.trim()
  if (!s) return null
  if (!/^\d{1,3}([.,]\d{1,2})?$/.test(s)) return NaN
  const v = lerNum(s)
  return v < 0 || v > QUEDA_MAXIMA ? NaN : v
}

export default function CalculoFretePerfil({ data, placa, somenteLeitura = false, rotuloSalvar = 'Salvar a queda', salvarSemMudar = false, onClose, onSalvo }: {
  data: string               // aaaa-mm-dd
  placa: string
  somenteLeitura?: boolean
  rotuloSalvar?: string
  salvarSemMudar?: boolean   // fluxo de emitir: o botão segue mesmo sem mudança
  onClose: () => void
  onSalvo?: (c: Calculo) => void
}) {
  const [calc, setCalc] = useState<Calculo | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [valores, setValores] = useState<Record<string, string>>({})
  const [salvando, setSalvando] = useState(false)
  const confirmar = useConfirmar()

  const preencher = (c: Calculo) => {
    setCalc(c)
    setValores(Object.fromEntries(c.linhas.filter(l => l.podeTerQueda).map(l => [l.tipo, paraCampo(l.quedaPorCaixa)])))
  }

  const carregar = useCallback(async () => {
    setErro(null)
    try { preencher((await api.fretePerfil(data, placa)).data) }
    catch (e) { setCalc(null); setErro(await erroApi(e, 'Não consegui abrir o cálculo do frete.')) }
  }, [data, placa])
  useEffect(() => { carregar() }, [carregar])

  const podeEditar = !!calc && calc.podeLancar && !somenteLeitura

  // Conta na hora, com o que está digitado (o valor certo volta do servidor ao salvar)
  const conta = useMemo(() => {
    if (!calc) return null
    let quedaTotal = 0, invalido = false, mudou = false
    const quedaDa = (l: LinhaFretePerfil) => {
      if (!l.podeTerQueda) return { v: null as number | null, queda: 0 }
      const v = lerQueda(valores[l.tipo] ?? '')
      if (v != null && isNaN(v)) { invalido = true; return { v, queda: 0 } }
      if ((v ?? 0) !== (l.quedaPorCaixa ?? 0)) mudou = true
      const q = arred(l.caixas * (v ?? 0))
      quedaTotal += q
      return { v, queda: q }
    }
    const porTipo = Object.fromEntries(calc.linhas.map(l => [l.tipo, quedaDa(l)]))
    quedaTotal = arred(quedaTotal)
    const devido = arred(calc.totais.freteBruto - quedaTotal)
    const liquido = arred(devido - calc.saldoAnterior)
    return { porTipo, quedaTotal, invalido, mudou, liquido, semBoleto: liquido <= 0 }
  }, [calc, valores])

  const sujo = podeEditar && !!conta?.mudou

  async function gravar(descontos: { tipo: string; quedaPorCaixa: number }[], ok: string) {
    if (!calc) return
    setSalvando(true)
    try {
      const novo = (await api.gravarQuedaFretePerfil(calc.data.slice(0, 10), calc.placa, descontos)).data
      preencher(novo)
      toast.success(ok)
      onSalvo?.(novo)
    } catch (e) { toast.error(await erroApi(e, 'Não consegui gravar a queda.'), { duration: 10000 }) }
    finally { setSalvando(false) }
  }

  const salvar = () => {
    if (!calc || !conta || conta.invalido) return
    const descontos = calc.linhas
      .filter(l => l.podeTerQueda)
      .map(l => ({ tipo: l.tipo, quedaPorCaixa: lerQueda(valores[l.tipo] ?? '') ?? 0 }))
      .filter(d => d.quedaPorCaixa > 0)
    gravar(descontos, descontos.length ? 'Queda do volume gravada.' : 'Marcado: não houve queda do volume.')
  }

  // Marca "não houve queda"; com queda lançada, pergunta antes de tirar
  const naoHouve = async () => {
    if (calc?.comQueda && !await confirmar({
      titulo: 'Marcar que não houve queda do volume?',
      texto: 'A queda lançada sai e o cálculo volta ao frete das notas, sem desconto.',
      confirmar: 'Não houve queda',
    })) return
    gravar([], 'Marcado: não houve queda do volume.')
  }

  const titulo = `Cálculo do frete Perfil · carga de ${dataBr(data)} · ${placa}`

  const rodape = calc && (
    <div className="flex flex-col-reverse sm:flex-row sm:items-center gap-2">
      {podeEditar && calc.houveQueda !== false && (
        <Botao variante="fantasma" onClick={naoHouve} disabled={salvando}>Não houve queda</Botao>
      )}
      <div className="hidden sm:block flex-1" />
      <Botao variante="secundario" onClick={onClose}>{podeEditar ? 'Cancelar' : 'Fechar'}</Botao>
      {podeEditar && (
        <Botao variante="primario" carregando={salvando} onClick={salvar}
               disabled={salvando || !conta || conta.invalido || (!conta.mudou && !salvarSemMudar)}>
          {rotuloSalvar}
        </Botao>
      )}
    </div>
  )

  return (
    <Modal titulo={titulo} onClose={onClose} largura="xl" sujo={sujo} rodape={rodape}
           descricao={podeEditar ? 'Houve queda do volume? Digite a queda por caixa de cada tipo e salve, ou marque que não houve. Dá para mudar depois.' : undefined}>
      {erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !calc || !conta ? <Carregando linhas={5} rotulo="Abrindo o cálculo do frete" />
        : (
          <div className="space-y-4">
            {calc.avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira" itens={calc.avisos} />}
            {calc.temBoleto && (
              <Aviso tipo="atencao" titulo="Esta carga já tem boleto">
                Se o boleto saiu sem a queda, dê baixa nele em Boletos a receber (botão Dar baixa) e emita de novo com o desconto.
              </Aviso>
            )}
            {!somenteLeitura && !calc.podeLancar && (
              <Aviso tipo="info">Só o Admin e o Bento lançam a queda do volume. Aqui você só vê o cálculo.</Aviso>
            )}

            <section aria-labelledby="cfp-notas" className="space-y-1">
              <h3 id="cfp-notas" className="text-sm font-semibold text-gray-900">
                Notas da carga{calc.numeroBoleto != null && <span className="font-normal text-gray-600"> · boleto {calc.numeroBoleto}</span>}
              </h3>
              {calc.notas.length === 0 ? (
                <p className="text-sm text-amber-800">O cálculo não trouxe as notas.</p>
              ) : (
                <ul className="text-sm text-gray-700 flex flex-wrap gap-x-4 gap-y-1">
                  {calc.notas.map((n, i) => (
                    <li key={`${n.nf ?? 'sem'}-${i}`} className="tabular-nums">
                      {n.nf ? `NF ${n.nf}` : <span className="text-amber-800">NF sem número</span>}
                      {' · '}{caixasBr(n.caixas)} caixas{n.produtoE ? ' produtoE' : ''}
                      {n.emitente && <span className="text-gray-500"> · {n.emitente}</span>}
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section aria-labelledby="cfp-tipos" className="space-y-2">
              <h3 id="cfp-tipos" className="text-sm font-semibold text-gray-900">Frete e queda por tipo de volume</h3>
              {calc.linhas.length === 0 ? (
                <p className="text-sm text-amber-800">Não achei os tipos de volume das notas desta carga.</p>
              ) : (<>
                {/* Celular: um cartão por tipo */}
                <ul className="sm:hidden divide-y divide-gray-100 border border-gray-200 rounded-lg">
                  {calc.linhas.map(l => {
                    const q = conta.porTipo[l.tipo]
                    return (
                      <li key={l.tipo} className="p-3 space-y-2">
                        <div className="flex items-start justify-between gap-3">
                          <p className="font-medium text-gray-900 min-w-0 break-words">{l.tipo}</p>
                          <p className="font-semibold tabular-nums whitespace-nowrap">{formatBRL(l.frete)}</p>
                        </div>
                        <p className="text-sm text-gray-600 tabular-nums">{caixasBr(l.caixas)} caixas × {formatBRL(l.fretePorCaixa)}</p>
                        <CampoQueda l={l} editar={podeEditar} valor={valores[l.tipo] ?? ''} invalido={q?.v != null && isNaN(q.v)}
                                    queda={q?.queda ?? 0} onChange={v => setValores(s => ({ ...s, [l.tipo]: v }))} />
                      </li>
                    )
                  })}
                </ul>
                <div className="hidden sm:block">
                  <table className="w-full text-sm">
                    <thead className="text-xs text-gray-500">
                      <tr>
                        <th className="text-left font-medium py-1">Tipo</th>
                        <th className="text-right font-medium">Caixas</th>
                        <th className="text-right font-medium">Frete por caixa</th>
                        <th className="text-right font-medium">Frete</th>
                        <th className="text-right font-medium pl-3">Queda por caixa</th>
                        <th className="text-right font-medium">Queda</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100">
                      {calc.linhas.map(l => {
                        const q = conta.porTipo[l.tipo]
                        const invalido = q?.v != null && isNaN(q.v)
                        return (
                          <tr key={l.tipo} className="align-middle">
                            <td className="py-1.5 pr-2 text-gray-900">{l.tipo}</td>
                            <td className="text-right tabular-nums">{caixasBr(l.caixas)}</td>
                            <td className="text-right tabular-nums">{formatBRL(l.fretePorCaixa)}</td>
                            <td className="text-right tabular-nums">{formatBRL(l.frete)}</td>
                            <td className="text-right pl-3 py-1">
                              {!l.podeTerQueda ? <span className="text-gray-500">não leva queda</span>
                                : podeEditar ? (
                                  <input value={valores[l.tipo] ?? ''} inputMode="decimal" placeholder="0,00"
                                         aria-label={`Queda por caixa de ${l.tipo} (R$)`} aria-invalid={invalido}
                                         onChange={e => setValores(s => ({ ...s, [l.tipo]: e.target.value }))}
                                         className={`${inputCls} ${invalido ? 'border-red-600' : ''}`} />
                                ) : <span className="tabular-nums">{l.quedaPorCaixa ? formatBRL(l.quedaPorCaixa) : '—'}</span>}
                            </td>
                            <td className="text-right tabular-nums">{l.podeTerQueda && q?.queda ? formatBRL(q.queda) : '—'}</td>
                          </tr>
                        )
                      })}
                    </tbody>
                    <tfoot className="border-t border-gray-200 font-semibold text-gray-900">
                      <tr>
                        <td className="py-1.5">Total</td>
                        <td className="text-right tabular-nums">{caixasBr(calc.totais.caixas)}</td>
                        <td />
                        <td className="text-right tabular-nums">{formatBRL(calc.totais.freteBruto)}</td>
                        <td />
                        <td className="text-right tabular-nums">{conta.quedaTotal ? formatBRL(conta.quedaTotal) : '—'}</td>
                      </tr>
                    </tfoot>
                  </table>
                </div>
              </>)}
              {conta.invalido && <p className="text-sm text-red-800">Queda por caixa: de R$ 0,00 a R$ 999,99, com até duas casas.</p>}
            </section>

            <section aria-labelledby="cfp-resumo" className="border border-gray-200 rounded-xl p-3 space-y-2">
              <h3 id="cfp-resumo" className="text-sm font-semibold text-gray-900">
                Resumo{conta.mudou && <span className="font-normal text-gray-600"> · com o que está digitado, ainda sem salvar</span>}
              </h3>
              <p className={`text-sm ${calc.houveQueda == null ? 'text-amber-800' : 'text-gray-700'}`}>
                Houve queda do volume? {calc.houveQueda == null ? 'Ainda não marcado.' : calc.houveQueda ? 'Sim.' : 'Não.'}
              </p>
              <dl className="grid grid-cols-[1fr_auto] gap-x-3 gap-y-1 text-sm">
                <dt className="text-gray-600">Frete das notas</dt><dd className="tabular-nums text-right">{formatBRL(calc.totais.freteBruto)}</dd>
                <dt className="text-gray-600">Queda do volume</dt><dd className="tabular-nums text-right">{conta.quedaTotal ? `− ${formatBRL(conta.quedaTotal)}` : '—'}</dd>
                <dt className="text-gray-600">Saldo anterior</dt><dd className="tabular-nums text-right">{calc.saldoAnterior ? `− ${formatBRL(calc.saldoAnterior)}` : '—'}</dd>
                <dt className="font-semibold text-gray-900">Valor do boleto</dt>
                <dd className="font-semibold tabular-nums text-right">{conta.semBoleto ? 'sem boleto' : formatBRL(conta.liquido)}</dd>
              </dl>
              {conta.semBoleto && (
                <Aviso tipo="atencao" titulo="Não precisa fazer o boleto">
                  A queda do volume passou o frete. Fica um saldo de <b>{formatBRL(-conta.liquido)}</b> para o próximo boleto da Girassol.
                </Aviso>
              )}
              {calc.atualizadoPor && calc.atualizadoEm && (
                <p className="text-xs text-gray-500">Marcado por {calc.atualizadoPor} em {dataDe(calc.atualizadoEm)}.</p>
              )}
              {calc.emitido && (
                <p className="text-xs text-gray-500">
                  Boleto emitido com {formatBRL(calc.emitido.valor)} (frete {formatBRL(calc.emitido.freteBruto)}, queda {formatBRL(calc.emitido.descontoTotal)}
                  {calc.emitido.saldoAbatido ? `, saldo ${formatBRL(calc.emitido.saldoAbatido)}` : ''}) em {dataDe(calc.emitido.em)}.
                </p>
              )}
            </section>
          </div>
        )}
    </Modal>
  )
}

// Queda de um tipo no cartão do celular
function CampoQueda({ l, editar, valor, invalido, queda, onChange }: {
  l: LinhaFretePerfil; editar: boolean; valor: string; invalido: boolean; queda: number; onChange: (v: string) => void
}) {
  if (!l.podeTerQueda) return <p className="text-sm text-gray-500">Não leva queda.</p>
  return (
    <div className="flex items-end gap-3">
      {editar ? (
        <label className="flex-1 min-w-0 text-sm text-gray-700">
          Queda por caixa (R$)
          <input value={valor} inputMode="decimal" placeholder="0,00" aria-invalid={invalido}
                 onChange={e => onChange(e.target.value)} className={`${inputCls} mt-1 ${invalido ? 'border-red-600' : ''}`} />
        </label>
      ) : (
        <p className="flex-1 text-sm text-gray-600">Queda por caixa: {l.quedaPorCaixa ? formatBRL(l.quedaPorCaixa) : '—'}</p>
      )}
      <p className="text-sm tabular-nums whitespace-nowrap pb-2">{queda ? `− ${formatBRL(queda)}` : '—'}</p>
    </div>
  )
}
