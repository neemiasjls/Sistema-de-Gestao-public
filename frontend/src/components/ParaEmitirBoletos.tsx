import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Calculator, Landmark, TriangleAlert } from 'lucide-react'
import Botao from './ui/Botao'
import { Carregando, ErroCarga } from './ui/Estados'
import { boletosContasApi as api, type CargaParaEmitir, type ClienteReceber } from '../services/boletosContasApi'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { dataBr } from '../utils/formato'
import { contaPerfil, temDescontoPerfil, textoQueda } from './MontarBoleto'

// Cargas dos últimos 30 dias sem boleto, já montadas (BOL-13). "Emitir" abre o Montar boleto
// naquela carga, onde a confirmação continua obrigatória (um boleto por vez).
export default function ParaEmitirBoletos({ cliente, rotuloCliente, versao, onEmitir, onCalculo }: {
  cliente: ClienteReceber
  rotuloCliente: string
  versao: number                 // muda depois de emitir: a lista recarrega
  onEmitir: (data: string, placa: string | null) => void
  onCalculo?: (data: string, placa: string) => void   // Perfil: abre o cálculo do frete e a queda do volume (BOL-17)
}) {
  const [marcando, setMarcando] = useState<string | null>(null)
  const [cargas, setCargas] = useState<CargaParaEmitir[] | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setErro(null)
    try { setCargas((await api.paraEmitirAReceber(cliente)).data) }
    catch (e) { setCargas(null); setErro(await erroApi(e, 'Não consegui ver as cargas sem boleto.')) }
  }, [cliente])
  useEffect(() => { setCargas(null); carregar() }, [carregar, versao])

  // Perfil (BOL-17): marca "não houve queda do volume" direto na carga; "houve" abre o painel
  async function naoHouveQueda(data: string, placa: string) {
    setMarcando(`${data}-${placa}`)
    try {
      await api.gravarQuedaFretePerfil(data, placa, [])
      toast.success('Marcado: não houve queda do volume.')
      await carregar()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui marcar a queda.'), { duration: 10000 }) }
    finally { setMarcando(null) }
  }

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
              const fp = m.fretePerfil
              const placaCarga = b.placa ?? c.placa
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
                    {fp && temDescontoPerfil(fp) && <p className="text-sm text-gray-700 tabular-nums">{contaPerfil(fp)}</p>}
                    {fp && placaCarga && onCalculo && (fp.houveQueda == null ? (
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="text-sm font-medium text-amber-800">Houve queda do volume?</p>
                        <Botao variante="secundario" tamanho="sm" carregando={marcando === `${c.data}-${placaCarga}`}
                               disabled={marcando != null} onClick={() => naoHouveQueda(c.data, placaCarga)}>Não houve</Botao>
                        <Botao variante="secundario" tamanho="sm" disabled={marcando != null}
                               onClick={() => onCalculo(c.data, placaCarga)}>Houve queda</Botao>
                      </div>
                    ) : (
                      <p className="text-sm text-gray-700">
                        Queda do volume: {textoQueda(fp)}{' · '}
                        <button type="button" className="underline underline-offset-2 text-tinta-700 dark:text-tinta-300 min-h-10 sm:min-h-0" onClick={() => onCalculo(c.data, placaCarga)}>Mudar</button>
                      </p>
                    ))}
                    {c.arquivo && <p className="text-xs text-gray-500 break-words">{c.arquivo}</p>}
                    {[...new Set([...m.impedeEmitir, ...avisos])].map(a => (
                      <p key={a} className={`text-sm flex gap-1.5 ${pode ? 'text-gray-600' : 'text-amber-800'}`}>
                        <TriangleAlert size={14} className="shrink-0 mt-0.5" aria-hidden />{a}
                      </p>
                    ))}
                  </div>
                  <div className="flex flex-wrap gap-2 self-start sm:self-center">
                    {fp && placaCarga && onCalculo && (
                      <Botao variante="secundario" tamanho="sm" icone={Calculator} onClick={() => onCalculo(c.data, placaCarga)}>Cálculo do frete</Botao>
                    )}
                    {pode && (
                      <Botao variante="primario" tamanho="sm" icone={Landmark} onClick={() => onEmitir(c.data, placaCarga)}>Emitir</Botao>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        )}
    </section>
  )
}
