import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Download, Upload } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone, classesBotao } from '../ui/Botao'
import { Carregando, ErroCarga } from '../ui/Estados'
import SecaoRecolhivel from '../SecaoRecolhivel'
import { ctesApi, viagensApi, exportApi, downloadBlob, formatBRL, socioColor, type CTe, type Viagem, type Socio } from '../../services/api'
import { comoLista } from '../../utils/resposta'

// O que o Dashboard antigo mostrava, agora embaixo do Painel do dia: os números
// de CT-es e viagens do mês e as duas exportações de CT-e. Ganhou a troca de mês
// (D-13: antes ficava preso no mês corrente, e no começo do mês quem exporta quer
// o anterior). As listas ficam recolhidas: são consulta, não o que importa hoje.

function PlacaBadge({ placa, socio }: { placa: string; socio: Socio }) {
  const c = socioColor(socio)
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-mono font-semibold border ${c.bg} ${c.text} ${c.border}`}>
      {placa}
    </span>
  )
}

const nomeMes = (mes: number, ano: number) =>
  new Date(ano, mes - 1).toLocaleString('pt-BR', { month: 'long', year: 'numeric' })

export default function ResumoMes() {
  const agora = new Date()
  const [ref, setRef] = useState({ mes: agora.getMonth() + 1, ano: agora.getFullYear() })
  const { mes, ano } = ref
  const ehAtual = mes === agora.getMonth() + 1 && ano === agora.getFullYear()
  const [ctes, setCtes] = useState<CTe[]>([])
  const [viagens, setViagens] = useState<Viagem[]>([])
  const [loading, setLoading] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const [exportando, setExportando] = useState<'geral' | 'placa' | null>(null)

  const andar = (passo: number) => setRef(r => {
    const d = new Date(r.ano, r.mes - 1 + passo)
    return { mes: d.getMonth() + 1, ano: d.getFullYear() }
  })

  const exportar = async (qual: 'geral' | 'placa') => {
    setExportando(qual)
    const sufixo = `${ano}_${String(mes).padStart(2, '0')}`
    try {
      if (qual === 'geral') {
        const { data } = await exportApi.cteGeral(mes, ano)
        downloadBlob(data, `cte_geral_${sufixo}.xlsx`)
      } else {
        const { data } = await exportApi.ctePorPlaca(mes, ano)
        downloadBlob(data, `cte_por_placa_${sufixo}.xlsx`)
      }
    } catch { toast.error(qual === 'geral' ? 'Erro ao exportar CT-e Geral.' : 'Erro ao exportar CT-e por Placa.') }
    finally { setExportando(null) }
  }

  const carregar = useCallback(() => {
    setLoading(true)
    setFalhou(false)
    Promise.all([ctesApi.listar({ mes, ano }), viagensApi.listar({ mes, ano })])
      .then(([r1, r2]) => {
        setCtes(comoLista<CTe>(r1.data, 'CT-es'))
        setViagens(comoLista<Viagem>(r2.data, 'viagens'))
      })
      .catch(() => setFalhou(true))
      .finally(() => setLoading(false))
  }, [mes, ano])
  useEffect(carregar, [carregar])

  const ativos = ctes.filter(c => c.status === 'Ativo')
  const totalFrete = ativos.reduce((s, c) => s + c.valorFrete, 0)
  const totalNF = ativos.reduce((s, c) => s + c.valorNF, 0)
  const cancelados = ctes.length - ativos.length

  return (
    <section aria-labelledby="painel-mes" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
        <div className="flex items-center gap-1 -ml-2">
          <BotaoIcone icone={ChevronLeft} rotulo="Mês anterior" onClick={() => andar(-1)} />
          <h2 id="painel-mes" className="font-display text-lg font-semibold text-gray-900 first-letter:uppercase min-w-[9.5rem] text-center sm:text-left" aria-live="polite">
            {nomeMes(mes, ano)}
          </h2>
          <BotaoIcone icone={ChevronRight} rotulo="Próximo mês" onClick={() => andar(1)} disabled={ehAtual} />
        </div>
        {/* Baixar planilha: contorno com o ícone de baixar (UI-12) */}
        <div className="grid grid-cols-2 sm:flex gap-2 w-full sm:w-auto">
          <Botao variante="secundario" tamanho="sm" icone={Download} onClick={() => exportar('geral')}
                 disabled={ativos.length === 0 || loading} carregando={exportando === 'geral'} textoCarregando="Exportando...">
            CT-e Geral
          </Botao>
          <Botao variante="secundario" tamanho="sm" icone={Download} onClick={() => exportar('placa')}
                 disabled={ativos.length === 0 || loading} carregando={exportando === 'placa'} textoCarregando="Exportando...">
            CT-e por Placa
          </Botao>
        </div>
      </div>

      {loading ? (
        <Carregando forma="cartoes" linhas={4} rotulo="Carregando o resumo do mês" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar o resumo do mês." onTentar={carregar} />
      ) : (
        <>
          <dl className="grid grid-cols-2 lg:grid-cols-4 bg-white border border-gray-200 rounded-xl divide-gray-100 overflow-hidden
                         [&>div]:border-gray-100 [&>div:nth-child(odd)]:border-r [&>div:nth-child(-n+2)]:border-b
                         lg:[&>div]:border-b-0 lg:[&>div:not(:last-child)]:border-r">
            <Numero rotulo="CT-es no mês" valor={String(ativos.length)} nota={cancelados ? `${cancelados} cancelado${cancelados > 1 ? 's' : ''}` : undefined} />
            <Numero rotulo="Total de fretes" valor={formatBRL(totalFrete)} />
            <Numero rotulo="Total das notas" valor={formatBRL(totalNF)} />
            <Numero rotulo="Viagens no mês" valor={String(viagens.length)} />
          </dl>

          <SecaoRecolhivel titulo="CT-es e viagens do mês" lembrar="trx_painel_mes_listas"
                           descricao={ctes.length === 0 ? 'Nenhum CT-e importado neste mês.' : `${ctes.length} CT-e${ctes.length > 1 ? 's' : ''} e ${viagens.length} viage${viagens.length === 1 ? 'm' : 'ns'}`}>
            <ListasMes ctes={ctes} viagens={viagens} />
          </SecaoRecolhivel>
        </>
      )}
    </section>
  )
}

function Numero({ rotulo, valor, nota }: { rotulo: string; valor: string; nota?: string }) {
  return (
    <div className="px-4 py-3 sm:px-5 sm:py-4 min-w-0">
      <dt className="text-sm text-gray-500">{rotulo}</dt>
      <dd className="font-display text-xl sm:text-2xl font-semibold text-gray-900 mt-0.5 tabular-nums truncate">{valor}</dd>
      {nota && <dd className="text-xs text-gray-500 mt-0.5">{nota}</dd>}
    </div>
  )
}

function ListasMes({ ctes, viagens }: { ctes: CTe[]; viagens: Viagem[] }) {
  return (
    <div className="space-y-5 -mx-4 sm:mx-0">
      <div>
        <h3 className="font-display text-base font-semibold text-gray-800 px-4 sm:px-0 mb-2">CT-es recentes</h3>
        {ctes.length === 0 ? (
          <div className="px-4 py-6 text-center text-sm text-gray-500 space-y-3">
            <p>Nenhum CT-e importado neste mês.</p>
            <Link to="/cte-export/importar" className={classesBotao('primario', 'sm')}>
              <Upload size={16} aria-hidden /> Importar CT-es
            </Link>
          </div>
        ) : (
          <>
            <ul className="sm:hidden divide-y divide-gray-100 border-y border-gray-100">
              {ctes.slice(0, 20).map(c => (
                <li key={c.id} className="px-4 py-3 space-y-1.5">
                  <div className="flex items-center justify-between gap-2">
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="font-mono text-sm font-medium text-gray-800">CT-e {c.numeroCTe}</span>
                      <PlacaBadge placa={c.placa} socio={c.socio} />
                    </div>
                    <span className="font-semibold text-gray-900 tabular-nums whitespace-nowrap">{formatBRL(c.valorFrete)}</span>
                  </div>
                  <p className="text-sm text-gray-700 truncate">{c.destinatario}{c.ufDestino ? ` · ${c.ufDestino}` : ''}</p>
                  <div className="flex items-center justify-between gap-2 text-xs text-gray-500">
                    <span>
                      {new Date(c.dataEmissao).toLocaleDateString('pt-BR')} · Nota {formatBRL(c.valorNF)}
                      {c.aliquotaICMS ? ` · ${c.aliquotaICMS.toFixed(2)}%` : ''}
                    </span>
                    {c.status === 'Cancelado' && (
                      <span className="px-2 py-0.5 rounded-full bg-red-50 text-red-700 border border-red-200">Cancelado</span>
                    )}
                  </div>
                </li>
              ))}
            </ul>
            <div className="hidden sm:block overflow-x-auto border border-gray-100 rounded-lg">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-xs text-gray-500">
                  <tr>
                    <th className="px-4 py-2.5 text-left font-medium">CT-e</th>
                    <th className="px-4 py-2.5 text-left font-medium">Data</th>
                    <th className="px-4 py-2.5 text-left font-medium">Placa</th>
                    <th className="px-4 py-2.5 text-left font-medium">Destinatário</th>
                    <th className="px-4 py-2.5 text-left font-medium">UF</th>
                    <th className="px-4 py-2.5 text-right font-medium">Valor da nota</th>
                    <th className="px-4 py-2.5 text-right font-medium">Frete</th>
                    <th className="px-4 py-2.5 text-right font-medium">Alíquota</th>
                    <th className="px-4 py-2.5 text-center font-medium">Situação</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {ctes.slice(0, 20).map(c => (
                    <tr key={c.id} className="hover:bg-gray-50">
                      <td className="px-4 py-2.5 font-mono text-xs text-gray-700">{c.numeroCTe}</td>
                      <td className="px-4 py-2.5 text-gray-600 whitespace-nowrap">{new Date(c.dataEmissao).toLocaleDateString('pt-BR')}</td>
                      <td className="px-4 py-2.5"><PlacaBadge placa={c.placa} socio={c.socio} /></td>
                      <td className="px-4 py-2.5 text-gray-700 max-w-[180px] truncate">{c.destinatario}</td>
                      <td className="px-4 py-2.5 text-gray-500">{c.ufDestino}</td>
                      <td className="px-4 py-2.5 text-right text-gray-700">{formatBRL(c.valorNF)}</td>
                      <td className="px-4 py-2.5 text-right font-medium text-gray-900">{formatBRL(c.valorFrete)}</td>
                      <td className="px-4 py-2.5 text-right text-tinta-700 font-medium">{c.aliquotaICMS ? `${c.aliquotaICMS.toFixed(2)}%` : '-'}</td>
                      <td className="px-4 py-2.5 text-center">
                        {c.status === 'Cancelado'
                          ? <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-red-50 text-red-700 border border-red-200">Cancelado</span>
                          : <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-green-50 text-green-700 border border-green-200">Ativo</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      {viagens.length > 0 && (
        <div>
          <h3 className="font-display text-base font-semibold text-gray-800 px-4 sm:px-0 mb-2">Viagens do mês</h3>
          <ul className="sm:hidden divide-y divide-gray-100 border-y border-gray-100">
            {viagens.map(v => (
              <li key={v.id} className="px-4 py-3 space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <PlacaBadge placa={v.veiculo.placa} socio={v.veiculo.socio} />
                  <span className="text-xs text-gray-500">
                    Saldo final{' '}
                    <span className={`text-sm font-semibold tabular-nums ${v.saldoFinal >= 0 ? 'text-green-700' : 'text-red-600'}`}>{formatBRL(v.saldoFinal)}</span>
                  </span>
                </div>
                <p className="text-sm text-gray-700 truncate">{v.motorista.nome} · {v.destino ?? '-'}</p>
                <p className="text-xs text-gray-500">Saída {new Date(v.dataSaida).toLocaleDateString('pt-BR')} · Frete {formatBRL(v.freteRecebido + v.freteRetorno)}</p>
              </li>
            ))}
          </ul>
          <div className="hidden sm:block overflow-x-auto border border-gray-100 rounded-lg">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-500">
                <tr>
                  <th className="px-4 py-2.5 text-left font-medium">Placa</th>
                  <th className="px-4 py-2.5 text-left font-medium">Motorista</th>
                  <th className="px-4 py-2.5 text-left font-medium">Saída</th>
                  <th className="px-4 py-2.5 text-left font-medium">Destino</th>
                  <th className="px-4 py-2.5 text-right font-medium">Frete</th>
                  <th className="px-4 py-2.5 text-right font-medium">Saldo final</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {viagens.map(v => (
                  <tr key={v.id} className="hover:bg-gray-50">
                    <td className="px-4 py-2.5"><PlacaBadge placa={v.veiculo.placa} socio={v.veiculo.socio} /></td>
                    <td className="px-4 py-2.5 text-gray-700">{v.motorista.nome}</td>
                    <td className="px-4 py-2.5 text-gray-600">{new Date(v.dataSaida).toLocaleDateString('pt-BR')}</td>
                    <td className="px-4 py-2.5 text-gray-700">{v.destino ?? '-'}</td>
                    <td className="px-4 py-2.5 text-right text-gray-700">{formatBRL(v.freteRecebido + v.freteRetorno)}</td>
                    <td className={`px-4 py-2.5 text-right font-semibold ${v.saldoFinal >= 0 ? 'text-green-700' : 'text-red-600'}`}>{formatBRL(v.saldoFinal)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  )
}
