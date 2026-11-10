import { useState } from 'react'
import toast from 'react-hot-toast'
import { AlertTriangle, CheckCircle2, HelpCircle, XCircle } from 'lucide-react'
import Aviso from './ui/Aviso'
import Botao from './ui/Botao'
import { useConfirmar } from './ui/Confirmar'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { sicoobApi, type BoletoConferido, type LancamentoSicoob, type PixAchado, type ResultadoBaixaPix } from '../services/sicoobApi'
import { dataBr } from '../utils/formato'

// Boleto pago por Pix (BOL-14): o Pix do cliente no extrato bate com boletos a receber em aberto
// (inclusive vencidos, pelo valor do boleto). "Dar baixa" baixa no Sicoob de verdade e marca recebido
// na data do Pix, boleto por boleto, depois da confirmação.

const nossos = (o: BoletoConferido[]) => o.map(b => b.nossoNumero || 'sem número').join(' + ')

const TOM: Record<ResultadoBaixaPix['situacao'], { cls: string; Icone: typeof CheckCircle2 }> = {
  'baixado': { cls: 'text-emerald-800', Icone: CheckCircle2 },
  'ja-no-banco': { cls: 'text-emerald-800', Icone: CheckCircle2 },
  'ja-recebido': { cls: 'text-gray-700', Icone: CheckCircle2 },
  'recusado': { cls: 'text-red-800', Icone: XCircle },
  'erro': { cls: 'text-red-800', Icone: XCircle },
  'incerto': { cls: 'text-amber-800', Icone: HelpCircle },
}

export default function PixBaixaBoletos({ l, pix, mudou }: { l: LancamentoSicoob; pix: PixAchado; mudou: () => void }) {
  const confirmar = useConfirmar()
  const [escolha, setEscolha] = useState<number | null>(pix.certo ? 0 : null)
  const [baixando, setBaixando] = useState(false)
  const [resultados, setResultados] = useState<ResultadoBaixaPix[] | null>(null)

  const quem = pix.pagador ?? 'quem pagou (o banco não informou o CNPJ)'
  const dia = l.data ? dataBr(l.data).slice(0, 5) : 'data não informada'
  const opcao = escolha != null ? pix.opcoes[escolha] : null

  const darBaixa = async () => {
    if (!opcao || !l.data) return
    const ok = await confirmar({
      titulo: 'Dar baixa destes boletos no Sicoob?',
      confirmar: 'Dar baixa no banco',
      texto: (
        <div className="space-y-2 text-sm">
          <p className="font-medium text-red-800">
            Ação real no banco: o boleto é baixado no Sicoob e deixa de valer para o cliente pagar. O sistema não desfaz.
          </p>
          <p className="text-gray-900">Pix de <b>{formatBRL(l.valor)}</b> em {dataBr(l.data)} de {quem}</p>
          <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
            {opcao.map(b => (
              <li key={b.id} className="flex flex-wrap justify-between gap-x-3 px-3 py-2">
                <span className="font-medium text-gray-900">Boleto {b.nossoNumero} da {b.rotulo}</span>
                <span className="tabular-nums text-gray-900">{formatBRL(b.valor)}</span>
                <span className="w-full text-gray-600">Vencimento {b.vencimento ? dataBr(b.vencimento) : 'não informado'}</span>
              </li>
            ))}
          </ul>
          <p className="text-gray-700">Cada boleto baixado fica marcado como recebido em {dataBr(l.data)} (data do Pix). Boleto já liquidado ou baixado no banco só é marcado.</p>
        </div>
      ),
    })
    if (!ok) return
    setBaixando(true)
    try {
      const { data: r } = await sicoobApi.baixarPix(opcao.map(b => b.id), l.data, l.valor)
      setResultados(r)
      const certos = r.filter(x => x.situacao === 'baixado' || x.situacao === 'ja-no-banco').length
      if (certos === r.length) toast.success(certos === 1 ? 'Boleto baixado e marcado como recebido.' : `${certos} boletos baixados e marcados como recebidos.`)
      else toast.error('Algum boleto não foi baixado. Veja o resultado no lançamento.')
      mudou()
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui dar baixa nos boletos.'))
    } finally { setBaixando(false) }
  }

  if (resultados) return (
    <div className="sm:ml-[60px] rounded-lg border border-gray-200 bg-white px-3 py-2 space-y-1">
      <p className="text-sm font-medium text-gray-900">Pix de {formatBRL(l.valor)} em {dia}: resultado da baixa</p>
      <ul className="space-y-1">
        {resultados.map(r => {
          const { cls, Icone } = TOM[r.situacao] ?? TOM.erro
          return (
            <li key={r.id} className={`flex items-start gap-1.5 text-sm ${cls}`}>
              <Icone size={14} className="mt-0.5 shrink-0" aria-hidden />
              <span><b>{r.nossoNumero}</b> ({r.valor != null ? formatBRL(r.valor) : 'valor não informado'}): {r.mensagem}</span>
            </li>
          )
        })}
      </ul>
    </div>
  )

  if (pix.opcoes.length === 0) {
    if (pix.jaRecebidos.length > 0) return (
      <p className="sm:pl-[60px] flex items-center gap-1.5 text-sm text-emerald-800">
        <CheckCircle2 size={14} aria-hidden />
        Pix de {quem}: {pix.jaRecebidos.length === 1 ? 'boleto' : 'boletos'} {nossos(pix.jaRecebidos)} já {pix.jaRecebidos.length === 1 ? 'marcado' : 'marcados'} como recebido
      </p>
    )
    return pix.semCnpj ? null : (
      <p className="sm:pl-[60px] text-sm text-gray-600">Pix de {quem}: nenhum boleto em aberto soma este valor.</p>
    )
  }

  return (
    <div className="sm:ml-[60px] rounded-lg border border-tinta-200 bg-tinta-50 px-3 py-2 space-y-2">
      {pix.semCnpj && (
        <Aviso tipo="atencao" titulo="O banco não informou o CNPJ de quem pagou">
          Achei os boletos só pela soma. Confira quem pagou antes de dar baixa.
        </Aviso>
      )}
      {pix.certo && opcao ? (
        <p className="text-sm text-tinta-900">
          Pix de <b>{formatBRL(l.valor)}</b> em {dia} de {quem} = <b>{nossos(opcao)}</b>
        </p>
      ) : (
        <fieldset className="space-y-1">
          <legend className="text-sm text-tinta-900 mb-1">
            Pix de <b>{formatBRL(l.valor)}</b> em {dia} de {quem}: mais de uma combinação de boletos dá este valor. Escolha a que foi paga.
          </legend>
          {pix.opcoes.map((o, i) => (
            <label key={i} className="flex items-start gap-2 text-sm text-tinta-900 min-h-11 sm:min-h-0 py-1 cursor-pointer">
              <input type="radio" name={`pix-${l.id}`} className="mt-1" checked={escolha === i} onChange={() => setEscolha(i)} />
              <span><b>{nossos(o)}</b> da {o[0]?.rotulo}</span>
            </label>
          ))}
          {pix.maisOpcoes && (
            <p className="flex items-start gap-1.5 text-sm text-amber-800">
              <AlertTriangle size={14} className="mt-0.5 shrink-0" aria-hidden />
              Há outras combinações. Se nenhuma destas for a certa, dê baixa pelo Sicoobnet.
            </p>
          )}
        </fieldset>
      )}
      {opcao && (
        <ul className="text-sm text-gray-700 space-y-0.5">
          {opcao.map(b => (
            <li key={b.id}>
              Boleto {b.nossoNumero} da {b.rotulo} · {formatBRL(b.valor)} · vencimento {b.vencimento ? dataBr(b.vencimento) : 'não informado'}
            </li>
          ))}
        </ul>
      )}
      <Botao tamanho="sm" variante="primario" carregando={baixando} textoCarregando="Baixando no banco..."
             disabled={!opcao || !l.data || baixando} onClick={darBaixa}>
        Dar baixa
      </Botao>
    </div>
  )
}
