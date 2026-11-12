import { useMemo } from 'react'
import { ChevronRight, SearchX, Trash2, Truck } from 'lucide-react'
import Botao, { BotaoIcone } from '../ui/Botao'
import { Vazio } from '../ui/Estados'
import { classeCampo } from '../EstoquePecas'
import { formatBRL, type CalculoFreteSalvoResumo } from '../../services/api'
import BarraLista, { separadorPerigo } from './BarraLista'

// Aba "Cargas" da tela Viagens: as cargas salvas, com busca por placa e filtro
// por mês (tudo no navegador, em cima da lista que já veio). Computador: tabela
// com os valores em coluna; celular e tablet: um cartão por carga.

const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']
const soPlaca = (s: string) => s.replace(/[^a-z0-9]/gi, '').toUpperCase()
const chaveMes = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
const nomeMes = (chave: string) => { const [a, m] = chave.split('-'); return `${MESES[+m - 1]} de ${a}` }
const dia = (d: Date) => d.toLocaleDateString('pt-BR')
const hora = (d: Date) => d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
const romaneios = (n: number) => n === 0 ? 'Nenhum' : `${n} romaneio${n === 1 ? '' : 's'}`

function SeloAberta() {
  return <span className="inline-flex items-center whitespace-nowrap rounded-full border border-tinta-300 bg-tinta-100 px-2 py-0.5 text-xs font-semibold text-tinta-800">Aberta na tela</span>
}

export default function CargasSalvas({ salvos, cargaAbertaId, busca, onBusca, mes, onMes, onAbrir, onExcluir }: {
  salvos: CalculoFreteSalvoResumo[]
  cargaAbertaId: number | null
  busca: string
  onBusca: (v: string) => void
  mes: string
  onMes: (v: string) => void
  onAbrir: (id: number) => void
  onExcluir: (c: CalculoFreteSalvoResumo) => void
}) {
  // Mais recente primeiro; meses que existem nas cargas, para o filtro
  const ordenadas = useMemo(() => [...salvos].sort((a, b) => b.criadoEm.localeCompare(a.criadoEm)), [salvos])
  const meses = useMemo(() => [...new Set(ordenadas.map(c => chaveMes(new Date(c.criadoEm))))], [ordenadas])

  const termo = soPlaca(busca)
  const lista = ordenadas.filter(c =>
    (!termo || soPlaca(c.placa).includes(termo)) && (!mes || chaveMes(new Date(c.criadoEm)) === mes))
  const filtrando = !!termo || !!mes
  const total = (f: (c: CalculoFreteSalvoResumo) => number) => lista.reduce((s, c) => s + (f(c) || 0), 0)

  if (salvos.length === 0)
    return <div className="rounded-xl border border-gray-200 bg-white">
      <Vazio compacto icone={Truck} titulo="Nenhuma carga salva ainda">Depois de calcular, use Salvar cálculo.</Vazio>
    </div>

  const limpar = () => { onBusca(''); onMes('') }
  const contagem = <>
    {filtrando ? `${lista.length} de ${salvos.length} cargas` : `${salvos.length} cargas`}
    {lista.length > 0 && <span className="text-gray-500"> · carga {formatBRL(total(c => c.valorCarga))} · pedágio {formatBRL(total(c => c.valorPedagio))}</span>}
  </>

  return (
    <div className="space-y-3 max-w-6xl">
      <BarraLista busca={busca} onBusca={onBusca} rotuloBusca="Buscar carga pela placa" exemploBusca="Placa, ex.: XAD-5H46"
        contagem={contagem}
        filtros={
          <select value={mes} onChange={e => onMes(e.target.value)} aria-label="Mês da carga" className={`${classeCampo} sm:w-56`} data-nao-suja>
            <option value="">Todos os meses</option>
            {meses.map(m => <option key={m} value={m}>{nomeMes(m)}</option>)}
          </select>
        } />

      {lista.length === 0 ? (
        <div className="rounded-xl border border-gray-200 bg-white">
          <Vazio compacto icone={SearchX} titulo="Nenhuma carga com esse filtro"
                 acao={<Botao variante="secundario" tamanho="sm" onClick={limpar}>Limpar filtros</Botao>}>
            {[termo && `Placa "${busca.trim()}"`, mes && `em ${nomeMes(mes)}`].filter(Boolean).join(' ')}
          </Vazio>
        </div>
      ) : <>
        {/* Celular e tablet: um cartão por carga */}
        <ul className="lg:hidden grid sm:grid-cols-2 gap-3">
          {lista.map(c => {
            const d = new Date(c.criadoEm)
            const aberta = cargaAbertaId === c.id
            return (
              <li key={c.id} className={`rounded-xl border bg-white p-4 flex flex-col gap-3 ${aberta ? 'border-tinta-400 ring-1 ring-tinta-300' : 'border-gray-200'}`}>
                <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
                  <span className="text-base font-semibold text-gray-900 tabular-nums tracking-wide">{c.placa}</span>
                  <span className="text-sm text-gray-600 tabular-nums">{dia(d)} <span className="text-gray-500">às {hora(d)}</span></span>
                  {aberta && <SeloAberta />}
                </div>
                <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                  <div><dt className="text-xs text-gray-500">Valor da carga</dt><dd className="font-semibold text-gray-900 tabular-nums">{formatBRL(c.valorCarga)}</dd></div>
                  <div><dt className="text-xs text-gray-500">Pedágio</dt><dd className="font-semibold text-gray-900 tabular-nums">{formatBRL(c.valorPedagio)}</dd></div>
                  <div className="col-span-2"><dt className="sr-only">Documentos</dt>
                    <dd className="text-gray-600 tabular-nums">{c.totalNFs} NFs · {c.totalCTes} CT-es · {romaneios(c.totalPlanilhas ?? 0)}</dd></div>
                </dl>
                <div className="flex items-center mt-auto">
                  <Botao variante="secundario" icone={ChevronRight} className="flex-1" onClick={() => onAbrir(c.id)}
                         aria-label={`Abrir a carga de ${c.placa}, ${dia(d)}`}>Abrir carga</Botao>
                  <span className={separadorPerigo}>
                    <BotaoIcone icone={Trash2} perigo rotulo={`Excluir a carga de ${c.placa}`} onClick={() => onExcluir(c)} />
                  </span>
                </div>
              </li>
            )
          })}
        </ul>

        {/* Computador: tabela, valores alinhados para comparar uma carga com a outra */}
        <div className="hidden lg:block rounded-xl border border-gray-200 bg-white">
          <table className="w-full text-sm">
            <thead className="sticky -top-4 sm:-top-5 lg:-top-6 z-10 text-xs [&_th]:bg-gray-50 [&_th]:shadow-[inset_0_-1px_0] [&_th]:shadow-gray-200 text-gray-500 [&_th:first-child]:rounded-tl-xl [&_th:last-child]:rounded-tr-xl">
              <tr className="border-b border-gray-200">
                <th className="px-4 py-2.5 text-left font-medium">Placa</th>
                <th className="px-4 py-2.5 text-left font-medium">Data</th>
                <th className="px-4 py-2.5 text-right font-medium">Valor da carga</th>
                <th className="px-4 py-2.5 text-right font-medium">Pedágio</th>
                <th className="px-4 py-2.5 text-right font-medium">NFs</th>
                <th className="whitespace-nowrap px-4 py-2.5 text-right font-medium">CT-es</th>
                <th className="px-4 py-2.5 text-left font-medium">Romaneios</th>
                <th className="px-4 py-2.5"><span className="sr-only">Ações</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {lista.map(c => {
                const d = new Date(c.criadoEm)
                const aberta = cargaAbertaId === c.id
                return (
                  <tr key={c.id} className={`transition-colors ${aberta ? 'bg-tinta-50' : 'hover:bg-gray-50'}`}>
                    <td className={`px-4 py-2 ${aberta ? 'shadow-[inset_3px_0_0] shadow-tinta-500' : ''}`}>
                      <span className="flex items-center gap-2">
                        <span className="font-semibold text-gray-900 tabular-nums tracking-wide">{c.placa}</span>
                        {aberta && <SeloAberta />}
                      </span>
                    </td>
                    <td className="px-4 py-2 tabular-nums text-gray-800 whitespace-nowrap">{dia(d)} <span className="text-gray-500">{hora(d)}</span></td>
                    <td className="px-4 py-2 text-right tabular-nums font-medium text-gray-900 whitespace-nowrap">{formatBRL(c.valorCarga)}</td>
                    <td className="px-4 py-2 text-right tabular-nums text-gray-800 whitespace-nowrap">{formatBRL(c.valorPedagio)}</td>
                    <td className="px-4 py-2 text-right tabular-nums text-gray-800">{c.totalNFs}</td>
                    <td className="px-4 py-2 text-right tabular-nums text-gray-800">{c.totalCTes}</td>
                    <td className={`px-4 py-2 whitespace-nowrap ${(c.totalPlanilhas ?? 0) > 0 ? 'text-gray-800' : 'text-gray-500'}`}>{romaneios(c.totalPlanilhas ?? 0)}</td>
                    <td className="px-4 py-1.5">
                      <div className="flex items-center justify-end">
                        <Botao variante="secundario" tamanho="sm" icone={ChevronRight} onClick={() => onAbrir(c.id)}
                               aria-label={`Abrir a carga de ${c.placa}, ${dia(d)}`}>Abrir</Botao>
                        <span className={separadorPerigo}>
                          <BotaoIcone icone={Trash2} perigo rotulo={`Excluir a carga de ${c.placa}`} onClick={() => onExcluir(c)} />
                        </span>
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
            {lista.length > 1 && (
              <tfoot className="border-t-2 border-gray-200 bg-gray-50 text-gray-900 font-semibold tabular-nums">
                <tr>
                  <td className="px-4 py-2.5 rounded-bl-xl" colSpan={2}>Total {filtrando ? 'do filtro' : 'de todas'}</td>
                  <td className="px-4 py-2.5 text-right whitespace-nowrap">{formatBRL(total(c => c.valorCarga))}</td>
                  <td className="px-4 py-2.5 text-right whitespace-nowrap">{formatBRL(total(c => c.valorPedagio))}</td>
                  <td className="px-4 py-2.5 text-right">{total(c => c.totalNFs)}</td>
                  <td className="px-4 py-2.5 text-right">{total(c => c.totalCTes)}</td>
                  <td className="px-4 py-2.5 rounded-br-xl" colSpan={2} />
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      </>}
    </div>
  )
}
