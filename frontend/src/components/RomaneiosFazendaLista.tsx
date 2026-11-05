import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Download, Search, Warehouse } from 'lucide-react'
import toast from 'react-hot-toast'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { BotaoIcone } from './ui/Botao'
import { classeCampo } from './EstoquePecas'
import { brl, dataBr, num } from '../utils/formato'
import { acertoAlfaApi, type RomaneioDaFazenda } from '../services/acertoAlfaApi'

// Romaneios da fazenda (tela Romaneios, parte Fazenda): todos os que entraram nos
// acertos Alfa, do mais novo para o mais velho, agrupados por mês. Os que
// vieram em PDF dizem se são de cliente (desconto de funrural) ou de embalar e
// dá para baixar o PDF; os das planilhas antigas só têm os números.

const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
const nomeMes = (iso: string) => `${MESES[Number(iso.slice(5, 7)) - 1] ?? ''} de ${iso.slice(0, 4)}`

export default function RomaneiosFazendaLista() {
  const [itens, setItens] = useState<RomaneioDaFazenda[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [busca, setBusca] = useState('')

  const carregar = () => {
    setFalhou(false)
    acertoAlfaApi.romaneiosFazenda()
      .then(l => setItens(l.sort((a, b) => (b.data ?? '').localeCompare(a.data ?? '') || (b.numero ?? 0) - (a.numero ?? 0))))
      .catch(() => setFalhou(true))
  }
  useEffect(carregar, [])

  const grupos = useMemo(() => {
    const b = busca.trim()
    const lista = (itens ?? []).filter(r => !b || String(r.numero ?? '').includes(b) || String(r.acertoNumero).includes(b))
    const g: [string, RomaneioDaFazenda[]][] = []
    for (const r of lista) {
      const mes = (r.data ?? '').slice(0, 7) || 'sem data'
      const ultimo = g[g.length - 1]
      if (ultimo && ultimo[0] === mes) ultimo[1].push(r); else g.push([mes, [r]])
    }
    return g
  }, [itens, busca])

  const baixar = (r: RomaneioDaFazenda) =>
    acertoAlfaApi.baixarPdf(r.acertoId, r.arquivoId!, `ROMANEIO ${r.numero}.pdf`).catch(() => toast.error('Não consegui baixar o PDF.'))

  if (falhou) return <ErroCarga mensagem="Não consegui carregar os romaneios da fazenda." onTentar={carregar} />
  if (itens == null) return <Carregando linhas={5} rotulo="Carregando os romaneios da fazenda" />
  if (itens.length === 0) return <Vazio icone={Warehouse} titulo="Nenhum romaneio da fazenda">Eles entram pelo Acerto Alfa.</Vazio>

  return (
    <div className="space-y-4">
      <label className="relative block max-w-xs">
        <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500" aria-hidden />
        <input value={busca} onChange={e => setBusca(e.target.value)} inputMode="numeric" placeholder="Número do romaneio ou do acerto"
               aria-label="Buscar pelo número do romaneio ou do acerto" className={`${classeCampo} pl-9`} />
      </label>
      {grupos.length === 0 && <p className="text-sm text-gray-600">Nenhum romaneio com esse número.</p>}
      {grupos.map(([mes, lista]) => (
        <section key={mes} aria-label={mes === 'sem data' ? 'Sem data' : nomeMes(mes)}>
          <h3 className="font-display text-sm font-semibold text-gray-600 mb-2 px-1 flex items-baseline justify-between">
            {mes === 'sem data' ? 'Sem data' : nomeMes(mes)}
            <span className="font-sans font-normal text-xs text-gray-500 tabular-nums">{lista.length} romaneios</span>
          </h3>
          <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
            {lista.map((r, i) => (
              <li key={`${r.acertoId}-${r.numero}-${i}`} className="pl-4 pr-1.5 py-2.5 flex items-start gap-2">
                <div className="flex-1 min-w-0">
                  <p className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <span className="font-semibold text-gray-900 tabular-nums">{r.numero ? `Romaneio ${r.numero}` : 'Sem número'}</span>
                    {r.tipo === 'cliente' && <span className="text-xs font-medium text-tinta-800 bg-tinta-50 rounded-md px-1.5 py-0.5">de cliente</span>}
                    {r.tipo === 'embalar' && <span className="text-xs font-medium text-gema-900 bg-gema-100 rounded-md px-1.5 py-0.5">para embalar</span>}
                  </p>
                  <p className="text-sm text-gray-600 tabular-nums mt-0.5">
                    {r.data ? dataBr(r.data) : <span className="text-amber-800">sem data</span>}
                    {r.caixas != null && <> · {num(r.caixas, 0)} caixas</>}
                    {r.valor != null && <> · {brl(r.valor)}</>}
                    {r.descontoCaixa != null && <> · desconto {brl(r.descontoCaixa)} por caixa</>}
                    {' · '}<Link to="/acerto-alfa" className="text-tinta-700 hover:underline">acerto {r.acertoNumero}</Link>
                  </p>
                </div>
                {r.arquivoId != null && <BotaoIcone icone={Download} rotulo={`Baixar o PDF do romaneio ${r.numero}`} onClick={() => baixar(r)} />}
              </li>
            ))}
          </ul>
        </section>
      ))}
    </div>
  )
}
