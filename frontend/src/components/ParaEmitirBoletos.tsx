import { useCallback, useEffect, useState } from 'react'
import { Landmark, TriangleAlert } from 'lucide-react'
import Botao from './ui/Botao'
import { Carregando, ErroCarga } from './ui/Estados'
import { boletosContasApi as api, type CargaParaEmitir, type ClienteReceber } from '../services/boletosContasApi'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { dataBr } from '../utils/formato'

// Cargas dos últimos 30 dias sem boleto, já montadas (BOL-13). "Emitir" abre o Montar boleto
// naquela carga, onde a confirmação continua obrigatória (um boleto por vez).
export default function ParaEmitirBoletos({ cliente, rotuloCliente, versao, onEmitir }: {
  cliente: ClienteReceber
  rotuloCliente: string
  versao: number                 // muda depois de emitir: a lista recarrega
  onEmitir: (data: string, placa: string | null) => void
}) {
  const [cargas, setCargas] = useState<CargaParaEmitir[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setErro(null)
    try { setCargas((await api.paraEmitirAReceber(cliente)).data) }
    catch (e) { setCargas(null); setErro(await erroApi(e, 'Não consegui ver as cargas sem boleto.')) }
  }, [cliente])
  useEffect(() => { setCargas(null); carregar() }, [carregar, versao])

  return (
    <section aria-labelledby="para-emitir" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
      <div>
        <h2 id="para-emitir" className="text-base font-semibold text-gray-900">Para emitir · {rotuloCliente}</h2>
        <p className="text-sm text-gray-600">Cargas dos últimos 30 dias ainda sem boleto, já calculadas. Emitir pede confirmação a cada boleto.</p>
      </div>
      {erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !cargas ? <Carregando linhas={2} rotulo="Procurando cargas sem boleto" />
        : cargas.length === 0 ? <p className="text-sm text-gray-600">Nenhuma carga sem boleto nos últimos 30 dias.</p>
        : (
          <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
            {cargas.map(c => {
              const m = c.montagem, b = m.boleto
              const avisos = [...m.faltando, ...b.avisos]
              const pode = m.impedeEmitir.length === 0
              return (
                <li key={`${c.data}-${c.placa ?? ''}`} className="p-3 flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-3">
                  <div className="flex-1 min-w-0 space-y-1">
                    <p className="text-sm text-gray-900">
                      <span className="font-medium tabular-nums">Carga de {dataBr(c.data)}</span>
                      {' · '}{b.placa ?? <span className="text-amber-800">sem placa</span>}
                      {' · '}{b.valor != null ? <span className="font-semibold tabular-nums">{formatBRL(b.valor)}</span> : <span className="text-amber-800">sem valor</span>}
                      {' · vence '}<span className="tabular-nums">{dataBr(b.vencimento)}</span>
                      {cliente === 'CD' && m.caixas != null && <span className="text-gray-600"> · {String(m.caixas).replace('.', ',')} caixas de produtoD</span>}
                    </p>
                    {c.arquivo && <p className="text-xs text-gray-500 break-words">{c.arquivo}</p>}
                    {[...new Set([...m.impedeEmitir, ...avisos])].map(a => (
                      <p key={a} className={`text-sm flex gap-1.5 ${pode ? 'text-gray-600' : 'text-amber-800'}`}>
                        <TriangleAlert size={14} className="shrink-0 mt-0.5" aria-hidden />{a}
                      </p>
                    ))}
                  </div>
                  {pode && (
                    <Botao variante="primario" tamanho="sm" icone={Landmark} className="self-start sm:self-center"
                           onClick={() => onEmitir(c.data, b.placa ?? c.placa)}>Emitir</Botao>
                  )}
                </li>
              )
            })}
          </ul>
        )}
    </section>
  )
}
