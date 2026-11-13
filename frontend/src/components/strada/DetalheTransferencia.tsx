import { useState } from 'react'
import toast from 'react-hot-toast'
import { Download, Pencil } from 'lucide-react'
import Aviso from '../ui/Aviso'
import Botao from '../ui/Botao'
import Campo from '../ui/Campo'
import { formatBRL } from '../../services/api'
import { erroStrada, stradaApi, type BlocoSocio, type FaturaDetalhe } from '../../services/stradaApi'
import { dataBr } from '../../utils/formato'

// Topo do detalhe da fatura no modelo da planilha TIP: número da transferência,
// datas, botão de baixar e os três blocos (Bento, Caio, Dudu) na ordem da planilha.

const data = (d: string | null | undefined) => (d ? dataBr(d) : null)
const placaVista = (p: string | null) => (p ? p.replace(/[-\s]/g, '') : 'sem placa')

export default function DetalheTransferencia({ fatura, mudou }: { fatura: FaturaDetalhe; mudou: () => void }) {
  const [editando, setEditando] = useState(false)
  const [novo, setNovo] = useState('')
  const [erroNumero, setErroNumero] = useState<string | null>(null)
  const [gravando, setGravando] = useState(false)
  const [baixando, setBaixando] = useState(false)

  const abrirEdicao = () => { setNovo(String(fatura.numeroTransferencia ?? '')); setErroNumero(null); setEditando(true) }

  const gravarNumero = async () => {
    const n = Number(novo)
    if (!Number.isInteger(n) || n <= 0) { setErroNumero('Digite um número inteiro, como 512.'); return }
    if (n === fatura.numeroTransferencia) { setEditando(false); return }
    setGravando(true)
    try {
      await stradaApi.mudarNumero(fatura.numero, n)
      toast.success(`Fatura ${fatura.numero} agora é a transferência ${n}.`)
      setEditando(false); mudou()
    } catch (e) { setErroNumero(await erroStrada(e, 'Não consegui trocar o número.')) }
    finally { setGravando(false) }
  }

  const baixar = async () => {
    setBaixando(true)
    try { await stradaApi.baixarPlanilha(fatura.numero) }
    catch (e) { toast.error(await erroStrada(e, 'Não consegui montar a planilha.')) }
    finally { setBaixando(false) }
  }

  const caiu = data(fatura.dataCreditoBanco)

  return (
    <div className="space-y-4">
      <section aria-label="Transferência" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-start gap-3">
          <div className="flex-1 min-w-0">
            {editando ? (
              <form className="flex flex-wrap items-end gap-2" onSubmit={e => { e.preventDefault(); gravarNumero() }}>
                <Campo rotulo="Número da transferência" erro={erroNumero} className="w-40">
                  <input inputMode="numeric" value={novo} autoFocus
                         onChange={e => { setNovo(e.target.value.replace(/\D/g, '')); setErroNumero(null) }}
                         className="w-full min-h-11 sm:min-h-10 text-sm border border-gray-300 rounded-lg px-3 py-2 tabular-nums" />
                </Campo>
                <Botao tamanho="sm" variante="primario" type="submit" carregando={gravando}>Gravar</Botao>
                <Botao tamanho="sm" variante="fantasma" type="button" onClick={() => setEditando(false)}>Cancelar</Botao>
              </form>
            ) : (
              <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                <h3 className="text-lg font-semibold text-gray-900">
                  {fatura.numeroTransferencia != null ? `Transferência ${fatura.numeroTransferencia}` : 'Transferência sem número'}
                </h3>
                <Botao tamanho="sm" variante="fantasma" icone={Pencil} onClick={abrirEdicao} className="whitespace-nowrap">Trocar número</Botao>
              </div>
            )}
            <p className="text-sm text-gray-700 mt-1">
              Vencimento {data(fatura.dataPagamento) ?? 'sem data'} · {caiu ? `caiu no banco em ${caiu}` : 'ainda não caiu no banco'}
            </p>
          </div>
          <Botao variante="primario" icone={Download} carregando={baixando} textoCarregando="Montando a planilha..."
                 onClick={baixar} disabled={!!fatura.planilhaErro} className="sm:shrink-0">
            Baixar planilha
          </Botao>
        </div>
        {fatura.planilhaErro && (
          <Aviso tipo="atencao" titulo="A planilha ainda não sai"
                 itens={fatura.semSocio.length > 0
                   ? fatura.semSocio.map(c => `Contrato ${c.contrato} (${formatBRL(c.valor)}, ${c.placa ?? 'sem placa'}) sem sócio: ligue à viagem abaixo ou suba o PDF do contrato.`)
                   : [fatura.planilhaErro]} />
        )}
      </section>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
        {fatura.blocos.map(b => <Bloco key={b.socio} b={b} />)}
      </div>

      <p className="flex flex-col sm:flex-row sm:justify-end sm:items-baseline gap-1 sm:gap-3 bg-white border border-gray-200 rounded-xl px-4 py-3">
        <span className="text-sm text-gray-700">Saldo total da transferência</span>
        <strong className="text-lg tabular-nums text-gray-900">{formatBRL(fatura.totalTransferido)}</strong>
      </p>
    </div>
  )
}

function Bloco({ b }: { b: BlocoSocio }) {
  const nome = b.nome.charAt(0).toUpperCase() + b.nome.slice(1).toLowerCase()
  return (
    <section aria-label={`Bloco ${nome}`} className="bg-white border border-gray-200 rounded-xl p-4 flex flex-col">
      <h4 className="text-sm font-semibold text-gray-900 mb-2">{nome}</h4>
      {b.linhas.length === 0 ? (
        <p className="text-sm text-gray-600 flex-1">Sem contrato deste sócio.</p>
      ) : (
        <table className="w-full text-sm flex-1">
          <thead>
            <tr className="text-left text-gray-600">
              <th scope="col" className="font-medium pb-1">Contrato</th>
              <th scope="col" className="font-medium pb-1">Placa</th>
              <th scope="col" className="font-medium pb-1 text-right">Valor</th>
            </tr>
          </thead>
          <tbody>
            {b.linhas.map(l => (
              <tr key={l.contrato} className="border-t border-gray-100">
                <td className="py-1 tabular-nums break-all pr-2">{l.contrato}</td>
                <td className="py-1 pr-2">{placaVista(l.placa)}</td>
                <td className="py-1 text-right tabular-nums whitespace-nowrap">{formatBRL(l.valor)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <dl className="mt-3 pt-2 border-t border-gray-200 text-sm space-y-0.5">
        <div className="flex justify-between gap-2"><dt className="text-gray-700">Saldo total de faturamentos</dt><dd className="tabular-nums">{formatBRL(b.total)}</dd></div>
        <div className="flex justify-between gap-2"><dt className="text-gray-700">Taxa de transferência (1%)</dt><dd className="tabular-nums">{formatBRL(b.taxa)}</dd></div>
        <div className="flex justify-between gap-2 font-semibold"><dt>Saldo transferido em conta</dt><dd className="tabular-nums">{formatBRL(b.saldo)}</dd></div>
      </dl>
    </section>
  )
}
