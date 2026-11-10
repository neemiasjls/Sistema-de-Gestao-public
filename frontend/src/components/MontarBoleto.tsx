import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Check, Copy, Download, Landmark, X } from 'lucide-react'
import { Modal } from './ModalBase'
import Campo from './ui/Campo'
import Aviso from './ui/Aviso'
import Botao, { BotaoIcone } from './ui/Botao'
import { Carregando, ErroCarga } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { boletosContasApi as api, type BoletoEmitido, type BoletoMontagem, type ClienteReceber } from '../services/boletosContasApi'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { dataBr, lerNum } from '../utils/formato'

// O sistema monta o boleto a receber com as regras do Sicoob (BOL-11), para conferir
// com o que foi feito à mão. Montar não grava nada; "Emitir no Sicoob" registra a cobrança
// REAL (BOL-10), só depois da confirmação a cada boleto, e grava em Boletos a receber.

const inputCls = 'w-full min-h-11 sm:min-h-0 text-sm border border-gray-300 rounded-lg px-3 py-2'

async function copiar(texto: string) {
  try { await navigator.clipboard.writeText(texto); toast.success('Copiado.') }
  catch { toast.error('Não consegui copiar; selecione o texto.') }
}

export default function MontarBoleto({ cliente, rotuloCliente, dataInicial, placaInicial, onClose, onEmitido }: {
  cliente: ClienteReceber
  rotuloCliente: string
  dataInicial: string
  placaInicial?: string | null
  onClose: () => void
  onEmitido?: () => void
}) {
  const [data, setData] = useState(dataInicial)
  const [placa, setPlaca] = useState(placaInicial ?? '')
  const [res, setRes] = useState<BoletoMontagem | null>(null)
  const [carregando, setCarregando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [emitindo, setEmitindo] = useState(false)
  const [emitido, setEmitido] = useState<BoletoEmitido | null>(null)
  const confirmar = useConfirmar()
  // CD: caixas de produtoD (vêm do romaneio; ele pode corrigir antes de emitir)
  const [caixas, setCaixas] = useState('')
  // A data, a placa e as caixas que valem são as da última montagem (o que está na tela), não o que foi digitado depois
  const [montado, setMontado] = useState<{ data: string; placa: string; caixas: number | null }>({ data: dataInicial, placa: placaInicial ?? '', caixas: null })
  const ehCd = cliente === 'CD'

  const montar = useCallback(async (d: string, p: string, cx: number | null = null) => {
    if (!d) return
    setCarregando(true); setErro(null)
    try {
      const m = (await api.montarAReceber(cliente, d, p, cx)).data
      setRes(m); setMontado({ data: d, placa: p, caixas: m.caixas })
      setCaixas(m.caixas != null ? String(m.caixas).replace('.', ',') : '')
    }
    catch (e) { setRes(null); setErro(await erroApi(e, 'Não consegui montar o boleto.')) }
    finally { setCarregando(false) }
  }, [cliente])
  useEffect(() => { montar(dataInicial, placaInicial ?? '') }, [montar, dataInicial, placaInicial])

  // Caixas digitadas valem só na mesma carga; trocou a data, valem as do romaneio novo
  const caixasDigitadas = ehCd && caixas.trim() !== '' ? lerNum(caixas) : null
  const montarDoForm = () => {
    const mesmaCarga = data === montado.data
    montar(data, placa, mesmaCarga && caixasDigitadas != null && caixasDigitadas > 0 ? caixasDigitadas : null)
  }
  const caixasMudaram = ehCd && res?.caixas != null && caixasDigitadas !== res.caixas
  // Preço da caixa tirado da própria montagem (valor ÷ caixas), para mostrar a conta enquanto ele digita
  const precoCaixa = res?.caixas && res.boleto.valor != null ? res.boleto.valor / res.caixas : null

  const grupos = res ? [...new Set(res.boleto.campos.map(c => c.grupo))] : []
  const avisos = res ? [...res.faltando, ...res.boleto.avisos] : []
  const difs = res?.comparacao?.filter(d => !d.bate).length ?? 0


  async function emitir() {
    if (!res || res.boleto.valor == null) return
    const b = res.boleto
    const ok = await confirmar({
      titulo: 'Emitir este boleto no Sicoob?',
      texto: (
        <div className="space-y-2 text-sm">
          <p className="font-medium text-red-800">Cobrança real: o boleto fica registrado no banco para o cliente pagar.</p>
          <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
            <dt className="text-gray-600">Pagador</dt><dd className="text-gray-900">{b.pagador.nome} · {b.pagador.cnpj}</dd>
            {ehCd && res.caixas != null && (<>
              <dt className="text-gray-600">Caixas de produtoD</dt>
              <dd className="text-gray-900 tabular-nums">
                {String(res.caixas).replace('.', ',')}
                {res.caixasRomaneio != null && res.caixasRomaneio !== res.caixas && ` (o romaneio tem ${String(res.caixasRomaneio).replace('.', ',')})`}
              </dd>
            </>)}
            <dt className="text-gray-600">Valor</dt><dd className="text-gray-900 font-semibold tabular-nums">{formatBRL(b.valor!)}</dd>
            <dt className="text-gray-600">Vencimento</dt><dd className="text-gray-900 tabular-nums">{dataBr(b.vencimento)}</dd>
            <dt className="text-gray-600">Carga</dt><dd className="text-gray-900">{dataBr(b.emissao)} · {b.placa} · seu número {b.seuNumero}</dd>
          </dl>
        </div>
      ),
      confirmar: 'Emitir no Sicoob',
      perigo: true,
    })
    if (!ok) return
    setEmitindo(true)
    try {
      const r = await api.emitirAReceber({ cliente, data: montado.data, placa: montado.placa || null, valor: b.valor!, vencimento: b.vencimento,
                                              caixas: ehCd ? montado.caixas : null })
      setEmitido(r)
      toast.success(`Boleto emitido no Sicoob: nosso número ${r.nossoNumero}.`)
      onEmitido?.()
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui emitir o boleto.'), { duration: 12000 })
      onEmitido?.()   // emissão sem resposta fica gravada "a conferir": a lista precisa mostrar
    } finally {
      setEmitindo(false)
      montar(montado.data, montado.placa, montado.caixas)
    }
  }

  return (
    <Modal titulo={`Montar boleto · ${rotuloCliente}`} onClose={onClose} largura="xl" fecharFora
           descricao="Montar só mostra. Emitir no Sicoob registra a cobrança, com confirmação.">
      <form className={`grid grid-cols-1 gap-3 sm:items-end ${ehCd ? 'sm:grid-cols-[1fr_1fr_auto_auto]' : 'sm:grid-cols-[1fr_1fr_auto]'}`}
            onSubmit={e => { e.preventDefault(); montarDoForm() }}>
        <Campo rotulo="Data da carga"><input type="date" value={data} onChange={e => setData(e.target.value)} className={inputCls} data-nao-suja /></Campo>
        <Campo rotulo="Caminhão (se houver mais de uma carga no dia)">
          <input value={placa} onChange={e => setPlaca(e.target.value)} className={inputCls} placeholder="Sem preencher: o da carga" data-nao-suja />
        </Campo>
        {ehCd && (
          <Campo rotulo="Caixas de produtoD">
            <input value={caixas} onChange={e => setCaixas(e.target.value)} inputMode="decimal" className={`${inputCls} sm:w-32`}
                   placeholder="Do romaneio" data-nao-suja />
          </Campo>
        )}
        <Botao type="submit" variante="primario" carregando={carregando}>Montar</Botao>
      </form>
      {caixasMudaram && (
        <p className="text-sm text-amber-800 mt-2">
          {caixasDigitadas != null && caixasDigitadas > 0
            ? <>{String(caixasDigitadas).replace('.', ',')} caixas{precoCaixa ? ` × ${formatBRL(precoCaixa)} = ${formatBRL(Math.round(caixasDigitadas * precoCaixa * 100) / 100)}` : ''}. Toque em Montar para recalcular o boleto antes de emitir.</>
            : 'Digite as caixas de produtoD (mais que zero) e toque em Montar.'}
        </p>
      )}

      {erro ? <ErroCarga mensagem={erro} onTentar={montarDoForm} />
        : !res ? <Carregando linhas={6} rotulo="Montando o boleto" />
        : (
          <div className="space-y-4">
            {avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira antes de usar" itens={avisos} />}

            {emitido ? (
              <section aria-labelledby="mb-emitido" className="border border-gray-200 rounded-xl p-3 space-y-2">
                <h3 id="mb-emitido" className="text-sm font-semibold text-gray-900">
                  Emitido no Sicoob · nosso número {emitido.nossoNumero}
                </h3>
                <p className="text-sm text-gray-600">Gravado em Boletos a receber como {emitido.nomeArquivo}.</p>
                {emitido.linhaDigitavel && (
                  <div className="flex items-center gap-2">
                    <span className="flex-1 min-w-0 text-sm tabular-nums break-all">{emitido.linhaDigitavel}</span>
                    <BotaoIcone icone={Copy} rotulo="Copiar a linha digitável" onClick={() => copiar(emitido.linhaDigitavel!)} />
                  </div>
                )}
                {emitido.temPdf ? (
                  <Botao variante="secundario" icone={Download}
                         onClick={() => api.baixarAReceber({ id: emitido.id, nomeArquivo: emitido.nomeArquivo })
                           .catch(async e => toast.error(await erroApi(e, 'Não consegui baixar o PDF.')))}>
                    Baixar o PDF
                  </Botao>
                ) : (
                  <p className="text-sm text-amber-800">O Sicoob não devolveu o PDF: baixe no Sicoobnet e suba aqui em A receber.</p>
                )}
              </section>
            ) : res.impedeEmitir.length > 0 ? (
              <Aviso tipo="info" titulo="Emitir no Sicoob não disponível" itens={res.impedeEmitir} />
            ) : (
              <section aria-labelledby="mb-emitir" className="border border-gray-200 rounded-xl p-3 flex flex-col sm:flex-row sm:items-center gap-3">
                <div className="flex-1 min-w-0">
                  <h3 id="mb-emitir" className="text-sm font-semibold text-gray-900">Pronto para emitir</h3>
                  <p className="text-sm text-gray-600">
                    Confira os campos abaixo. O boleto vai como {res.nomeArquivo}, sem Pix, com o PDF guardado aqui.
                  </p>
                </div>
                <Botao variante="primario" icone={Landmark} carregando={emitindo} onClick={emitir}
                       disabled={carregando || emitindo || caixasMudaram}>Emitir no Sicoob</Botao>
              </section>
            )}

            {res.comparacao ? (
              <section aria-labelledby="mb-comp" className="border border-gray-200 rounded-xl p-3 space-y-2">
                <h3 id="mb-comp" className="text-sm font-semibold text-gray-900">
                  {difs === 0 ? 'Igual ao boleto já feito' : `${difs} diferença${difs > 1 ? 's' : ''} com o boleto já feito`}
                  <span className="font-normal text-gray-600"> · {res.feitoNome}{res.feitoNossoNumero ? ` (${res.feitoNossoNumero})` : ''}</span>
                </h3>
                <table className="w-full text-sm">
                  <thead className="text-xs text-gray-500">
                    <tr><th className="text-left font-medium py-1">Campo</th><th className="text-left font-medium">Montado</th><th className="text-left font-medium">Feito</th><th><span className="sr-only">Bate</span></th></tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {res.comparacao.map(d => (
                      <tr key={d.campo} className={d.bate ? '' : 'bg-red-50/60'}>
                        <td className="py-1.5 pr-2 text-gray-700">{d.campo}</td>
                        <td className="pr-2 tabular-nums">{d.montado}</td>
                        <td className="pr-2 tabular-nums">{d.feito}</td>
                        <td className="text-right">
                          {d.bate ? <Check size={16} className="inline text-gray-500" aria-label="Bate" />
                                  : <X size={16} className="inline text-red-700" aria-label="Diferente" />}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </section>
            ) : (
              <p className="text-sm text-gray-600">Ainda não há boleto feito para a carga de {dataBr(data)}.</p>
            )}

            {res.fontes.length > 0 && (
              <p className="text-sm text-gray-600">
                {res.fontes.map(f => <span key={f.nome} className="block"><b className="font-medium text-gray-800">{f.nome}</b>: {f.descricao}</span>)}
              </p>
            )}

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
              {grupos.map(g => (
                <section key={g} className="border border-gray-200 rounded-xl p-3" aria-label={g}>
                  <h3 className="text-sm font-semibold text-gray-900 mb-1">{g}</h3>
                  <dl className="divide-y divide-gray-100">
                    {res.boleto.campos.filter(c => c.grupo === g).map(c => (
                      <div key={c.rotulo} className="flex items-center gap-2 py-1.5">
                        <dt className="text-sm text-gray-600 w-36 shrink-0">{c.rotulo}</dt>
                        <dd className="flex-1 min-w-0 text-sm text-gray-900 break-words tabular-nums">
                          {c.valor ?? <span className="text-amber-800">{c.aviso ?? 'Não veio'}</span>}
                          {c.valor && c.aviso && <span className="block text-xs text-amber-800">{c.aviso}</span>}
                        </dd>
                        {c.valor && <BotaoIcone icone={Copy} rotulo={`Copiar ${c.rotulo}`} onClick={() => copiar(c.valor!)} />}
                      </div>
                    ))}
                  </dl>
                </section>
              ))}
            </div>
          </div>
        )}
    </Modal>
  )
}
