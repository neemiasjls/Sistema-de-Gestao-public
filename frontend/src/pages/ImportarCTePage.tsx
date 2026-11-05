import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Campo from '../components/ui/Campo'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowLeft, CheckCircle2, Download, FileUp, Inbox, Info, X, XCircle } from 'lucide-react'
import toast from 'react-hot-toast'
import { ctesApi, exportApi, downloadBlob, formatBRL, socioColor, type CTe, type Socio } from '../services/api'
import { classeCampo, classeNumero } from '../components/EstoquePecas'

interface ImportResult {
  arquivo: string
  sucesso: boolean
  erro?: string
  jaExistia: boolean
  cteId?: number
  numeroCte?: string
}

// A placa leva a cor do dono do caminhão (EMP-02): é a única cor da linha
function PlacaBadge({ placa, socio }: { placa: string; socio: Socio }) {
  const c = socioColor(socio)
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-md text-xs font-semibold tabular-nums border whitespace-nowrap ${c.bg} ${c.text} ${c.border}`}>
      {placa}
    </span>
  )
}

const dataCurta = (iso: string) => new Date(iso).toLocaleDateString('pt-BR')
const aliquota = (c: CTe) => (c.aliquotaICMS ? `${c.aliquotaICMS.toLocaleString('pt-BR', { minimumFractionDigits: 4, maximumFractionDigits: 4 })}` : '—')

export default function ImportarCTePage() {
  const [importing, setImporting] = useState(false)
  const [resultados, setResultados] = useState<ImportResult[]>([])
  const [ctes, setCtes] = useState<CTe[]>([])
  const [loadingCtes, setLoadingCtes] = useState(false)
  const [filtroMes, setFiltroMes] = useState(new Date().getMonth() + 1)
  const [filtroAno, setFiltroAno] = useState(new Date().getFullYear())
  const [exportandoGeral, setExportandoGeral] = useState(false)
  const [exportandoPlaca, setExportandoPlaca] = useState(false)

  const exportarGeral = async () => {
    setExportandoGeral(true)
    try {
      const { data } = await exportApi.cteGeral(filtroMes, filtroAno)
      downloadBlob(data, `cte_geral_${filtroAno}_${String(filtroMes).padStart(2, '0')}.xlsx`)
    } catch { toast.error('Não consegui exportar o CT-e Geral.') }
    finally { setExportandoGeral(false) }
  }

  const exportarPorPlaca = async () => {
    setExportandoPlaca(true)
    try {
      const { data } = await exportApi.ctePorPlaca(filtroMes, filtroAno)
      downloadBlob(data, `cte_por_placa_${filtroAno}_${String(filtroMes).padStart(2, '0')}.xlsx`)
    } catch { toast.error('Não consegui exportar o CT-e por Placa.') }
    finally { setExportandoPlaca(false) }
  }

  const [falhou, setFalhou] = useState(false)
  const carregarCtes = useCallback(async () => {
    setLoadingCtes(true)
    try {
      const { data } = await ctesApi.listar({ mes: filtroMes, ano: filtroAno })
      setCtes(data)
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally {
      setLoadingCtes(false)
    }
  }, [filtroMes, filtroAno])
  // Antes a lista só aparecia depois de tocar em Filtrar
  useEffect(() => { carregarCtes() }, [carregarCtes])

  const processarArquivos = async (xmlFiles: File[]) => {
    if (!xmlFiles.length) return
    setImporting(true)
    try {
      const { data } = await ctesApi.importarMultiplos(xmlFiles)
      setResultados(data)

      const ok = data.filter((r: ImportResult) => r.sucesso && !r.jaExistia).length
      const dup = data.filter((r: ImportResult) => r.jaExistia).length
      const err = data.filter((r: ImportResult) => !r.sucesso).length

      if (ok > 0) toast.success(`${ok} CT-e(s) importado(s).`)
      if (dup > 0) toast(`${dup} já existia(m) no sistema.`, { icon: <Info size={18} className="text-tinta-600 shrink-0" aria-hidden /> })
      if (err > 0) toast.error(`${err} arquivo(s) com erro. Veja a lista.`)

      await carregarCtes()
    } catch {
      toast.error('Não consegui importar os arquivos.')
    } finally {
      setImporting(false)
    }
  }

  const ativos = ctes.filter(c => c.status !== 'Cancelado')
  const totalFrete = ativos.reduce((s, c) => s + c.valorFrete, 0)
  const comErro = resultados.filter(r => !r.sucesso).length

  return (
    <div className="space-y-6 max-w-5xl">
      <Link to="/cte-export" className="inline-flex items-center gap-1.5 -ml-1 px-1 min-h-11 sm:min-h-9 text-sm font-medium text-gray-600 hover:text-gray-900">
        <ArrowLeft size={16} aria-hidden /> Planilha SG
      </Link>
      <CabecalhoTela
        titulo="Importar CT-es"
        descricao="Grava os CT-es no sistema. É daqui que saem o resumo do mês no Dashboard e as planilhas CT-e Geral e por Placa. A planilha SG continua na tela CT-es."
      />

      <AreaSoltar aceita={['.xml']} onArquivos={processarArquivos} icone={FileUp}
                  titulo="Arraste os XMLs de CT-e aqui" textoBotao="Escolher XMLs"
                  dica="CT-e e eventos de cancelamento"
                  carregando={importing} textoCarregando="Importando os CT-es..." />

      {/* Resultados da importação */}
      {resultados.length > 0 && (
        <section className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-entrada">
          <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
            <div className="min-w-0 py-0.5">
              <h3 className="font-semibold text-gray-900">Resultado da importação</h3>
              <p className={`text-sm ${comErro ? 'text-red-800 font-medium' : 'text-gray-500'}`}>
                {resultados.length} {resultados.length === 1 ? 'arquivo' : 'arquivos'}{comErro ? `, ${comErro} com erro` : ''}
              </p>
            </div>
            <BotaoIcone icone={X} rotulo="Fechar o resultado" onClick={() => setResultados([])} />
          </div>
          <ul className="divide-y divide-gray-100 max-h-72 overflow-y-auto">
            {resultados.map((r, i) => (
              <li key={i} className={`px-4 py-2.5 flex items-start gap-3 text-sm ${!r.sucesso ? 'bg-red-50/60' : ''}`}>
                {r.sucesso && !r.jaExistia ? <CheckCircle2 size={17} className="text-emerald-700 shrink-0 mt-0.5" aria-hidden />
                  : r.jaExistia ? <Info size={17} className="text-tinta-600 shrink-0 mt-0.5" aria-hidden />
                  : <XCircle size={17} className="text-red-700 shrink-0 mt-0.5" aria-hidden />}
                <div className="min-w-0 flex-1 sm:flex sm:gap-4">
                  <span className="block sm:w-64 shrink-0 truncate text-gray-500">{r.arquivo}</span>
                  <span className={`block ${r.sucesso ? 'text-gray-800' : 'text-red-800 font-medium'}`}>
                    {r.jaExistia
                      ? `CT-e ${r.numeroCte} já existe`
                      : r.sucesso
                        ? `CT-e ${r.numeroCte} importado`
                        : r.erro}
                  </span>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Filtros e listagem */}
      <section aria-labelledby="ctes-importados" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <div className="px-4 py-3 border-b border-gray-100 space-y-3">
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h3 id="ctes-importados" className="font-semibold text-gray-900">CT-es importados</h3>
            {ativos.length > 0 && (
              <p className="text-sm text-gray-600 tabular-nums">
                {ativos.length} {ativos.length === 1 ? 'ativo' : 'ativos'} · frete <b className="text-gray-900">{formatBRL(totalFrete)}</b>
              </p>
            )}
          </div>
          <div className="grid grid-cols-2 sm:flex sm:flex-wrap sm:items-end gap-3">
            <Campo rotulo="Mês" className="sm:w-40">
              <select value={filtroMes} onChange={e => setFiltroMes(Number(e.target.value))} className={classeCampo}>
                {Array.from({ length: 12 }, (_, i) => (
                  <option key={i + 1} value={i + 1}>
                    {new Date(2024, i).toLocaleString('pt-BR', { month: 'long' })}
                  </option>
                ))}
              </select>
            </Campo>
            <Campo rotulo="Ano" className="sm:w-24">
              <input type="number" inputMode="numeric" value={filtroAno} onChange={e => setFiltroAno(Number(e.target.value))} className={classeNumero} />
            </Campo>
            <div className="col-span-2 flex gap-2 sm:ml-auto [&>*]:flex-1 sm:[&>*]:flex-none">
              <Botao variante="secundario" icone={Download} onClick={exportarGeral} disabled={ctes.length === 0} carregando={exportandoGeral}>
                CT-e Geral
              </Botao>
              <Botao variante="secundario" icone={Download} onClick={exportarPorPlaca} disabled={ctes.length === 0} carregando={exportandoPlaca}
                     title="Uma aba por placa">
                CT-e por Placa
              </Botao>
            </div>
          </div>
        </div>

        {falhou ? (
          <div className="p-3"><ErroCarga mensagem="Não consegui carregar os CT-es." onTentar={carregarCtes} /></div>
        ) : loadingCtes && ctes.length === 0 ? (
          <div className="p-3"><Carregando linhas={3} rotulo="Carregando os CT-es" /></div>
        ) : ctes.length === 0 ? (
          <Vazio compacto icone={Inbox} titulo="Nenhum CT-e neste mês">Importe os XMLs acima ou escolha outro mês.</Vazio>
        ) : (
          <>
            {/* Celular: um cartão por CT-e (a tabela tem 12 colunas) */}
            <ul className="md:hidden divide-y divide-gray-100">
              {ctes.map(c => {
                const cancelado = c.status === 'Cancelado'
                return (
                  <li key={c.id} className={`px-4 py-3 space-y-1.5 ${cancelado ? 'bg-gray-50' : ''}`}>
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className={`font-semibold tabular-nums ${cancelado ? 'text-gray-500 line-through' : 'text-gray-900'}`}>CT-e {c.numeroCTe}</p>
                        <p className="text-sm text-gray-500 tabular-nums">{dataCurta(c.dataEmissao)}{c.numeroNF ? ` · NF ${c.numeroNF}` : ''}</p>
                      </div>
                      <div className="text-right">
                        <p className="font-display text-lg font-semibold text-gray-900 tabular-nums leading-tight">{formatBRL(c.valorFrete)}</p>
                        <p className="text-xs text-gray-500">frete</p>
                      </div>
                    </div>
                    <p className="text-sm text-gray-700 break-words">
                      {c.remetente} <span className="text-gray-500">({c.ufOrigem})</span> para {c.destinatario} <span className="text-gray-500">({c.ufDestino})</span>
                    </p>
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-gray-600 tabular-nums">
                      <PlacaBadge placa={c.placa} socio={c.socio} />
                      <span>Nota {formatBRL(c.valorNF)}</span>
                      <span>Alíquota {aliquota(c)}</span>
                      {cancelado && <span className="text-xs font-medium text-red-800 bg-red-50 border border-red-200 rounded-full px-2 py-0.5">Cancelado</span>}
                    </div>
                  </li>
                )
              })}
            </ul>

            <div className="hidden md:block overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-xs text-gray-500">
                  <tr>
                    <th className="px-3 py-2.5 text-left font-medium">CT-e</th>
                    <th className="px-3 py-2.5 text-left font-medium">Nota</th>
                    <th className="px-3 py-2.5 text-left font-medium">Data</th>
                    <th className="px-3 py-2.5 text-left font-medium">Placa</th>
                    <th className="px-3 py-2.5 text-left font-medium">Fornecedor</th>
                    <th className="px-3 py-2.5 text-left font-medium">Cliente</th>
                    <th className="px-3 py-2.5 text-right font-medium">Valor da nota</th>
                    <th className="px-3 py-2.5 text-right font-medium">Frete</th>
                    <th className="px-3 py-2.5 text-right font-medium">Alíquota</th>
                    <th className="px-3 py-2.5 text-left font-medium">Situação</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {ctes.map(c => {
                    const cancelado = c.status === 'Cancelado'
                    return (
                      <tr key={c.id} className={cancelado ? 'bg-gray-50 text-gray-500' : 'hover:bg-gray-50'}>
                        <td className={`px-3 py-2.5 tabular-nums font-medium ${cancelado ? 'line-through' : 'text-gray-900'}`}>{c.numeroCTe}</td>
                        <td className="px-3 py-2.5 tabular-nums text-gray-600">{c.numeroNF ?? '—'}</td>
                        <td className="px-3 py-2.5 whitespace-nowrap tabular-nums text-gray-600">{dataCurta(c.dataEmissao)}</td>
                        <td className="px-3 py-2.5"><PlacaBadge placa={c.placa} socio={c.socio} /></td>
                        <td className="px-3 py-2.5 text-gray-700 max-w-[160px]">
                          <span className="block truncate" title={c.remetente}>{c.remetente}</span>
                          <span className="text-xs text-gray-500">{c.ufOrigem}</span>
                        </td>
                        <td className="px-3 py-2.5 text-gray-700 max-w-[160px]">
                          <span className="block truncate" title={c.destinatario}>{c.destinatario}</span>
                          <span className="text-xs text-gray-500">{c.ufDestino}</span>
                        </td>
                        <td className="px-3 py-2.5 text-right tabular-nums text-gray-700">{formatBRL(c.valorNF)}</td>
                        <td className="px-3 py-2.5 text-right tabular-nums font-semibold text-gray-900">{formatBRL(c.valorFrete)}</td>
                        <td className="px-3 py-2.5 text-right tabular-nums text-gray-700">{aliquota(c)}</td>
                        <td className="px-3 py-2.5">
                          {cancelado
                            ? <span className="text-xs font-medium text-red-800 bg-red-50 border border-red-200 rounded-full px-2 py-0.5">Cancelado</span>
                            : <span className="text-xs font-medium text-gray-700 bg-gray-100 border border-gray-200 rounded-full px-2 py-0.5">Ativo</span>}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>
    </div>
  )
}
