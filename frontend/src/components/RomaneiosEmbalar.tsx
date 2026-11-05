import { Link } from 'react-router-dom'
import { AlertTriangle, ArrowDownToLine } from 'lucide-react'
import Botao from './ui/Botao'
import { dataBr } from '../utils/formato'
import { descreverRomaneio, type RomaneioFazenda } from '../services/volumesEmbaladosApi'

// Romaneio da fazenda sem funrural = volumes que pegam na fazenda para embalar no
// barracão. Entra como entrada "PAULO" no controle de volumes embalados da regra
// (o primeiro com data de produtoB igual ou até 3 dias depois do romaneio).

const diaMes = (iso: string | null) => (iso ? dataBr(iso).slice(0, 5) : '')

/** Para onde o romaneio foi nos volumes embalados, numa linha, com o link do controle. */
export function SituacaoRomaneio({ r }: { r: RomaneioFazenda }) {
  switch (r.situacao) {
    case 'lancado':
      return (
        <span className="text-emerald-700">
          Entrou no{' '}
          <Link to={`/estoque/volumes/${r.controleId}`} className="font-medium underline underline-offset-2 hover:text-emerald-900">
            controle de volumes {r.controleNumero}{r.controleData ? ` · ${diaMes(r.controleData)}` : ''}
          </Link>
        </span>
      )
    case 'aguardando':
      return <span className="text-amber-800">Aguardando o controle da produtoB. Entra sozinho quando ele for anotado.</span>
    case 'retirado':
      return <span className="text-gray-500">Tirado dos volumes embalados à mão.</span>
    case 'historico':
      return <span className="text-gray-500">Do tempo das planilhas: não entra (a entrada já foi digitada lá).</span>
    case 'sem_tipo':
      return <span className="text-red-700">Nenhum produto vira caixa para embalar: não entrou nos volumes.</span>
  }
}

/** O romaneio numa linha: "Romaneio da fazenda 133423 · 29/09 — 170 caixas de vermelho". */
export function LinhaRomaneio({ r }: { r: RomaneioFazenda }) {
  const desc = descreverRomaneio(r)
  return (
    <>
      <p className="text-sm text-gray-900">
        <span className="font-medium">Romaneio da fazenda {r.numero}</span>
        <span className="text-gray-500"> · {diaMes(r.data)}</span>
        {desc && <span className="text-gray-700"> — {desc}</span>}
      </p>
      {r.avisos.map(a => (
        <p key={a} className="text-xs text-amber-800 flex gap-1.5"><AlertTriangle size={13} className="mt-0.5 shrink-0" aria-hidden />{a}</p>
      ))}
    </>
  )
}

/**
 * Romaneios da fazenda que ainda não estão em controle nenhum. Com onPuxar, cada
 * um ganha o botão para entrar neste controle com um toque.
 */
export function CartaoRomaneiosSemControle({ romaneios, onPuxar, puxando, dataControle }: {
  romaneios: RomaneioFazenda[]
  onPuxar?: (r: RomaneioFazenda) => void
  puxando?: number | null
  // No controle: o tirado à mão de até uma semana perto desta data também aparece, para voltar
  dataControle?: string
}) {
  const perto = (r: RomaneioFazenda) =>
    !!dataControle && Math.abs(new Date(r.data + 'T00:00:00').getTime() - new Date(dataControle + 'T00:00:00').getTime()) <= 7 * 86400000
  const pendentes = romaneios.filter(r => r.situacao === 'aguardando' || (onPuxar && r.situacao === 'retirado' && perto(r)))
  if (pendentes.length === 0) return null
  return (
    <div className="bg-amber-50 border border-amber-200 rounded-xl overflow-hidden">
      <div className="px-4 pt-3 pb-1">
        <h2 className="font-display text-base font-semibold text-gray-900">Romaneios da fazenda sem controle</h2>
        <p className="text-sm text-gray-700">
          Volumes para embalar (romaneio sem funrural) que não estão em nenhum controle.
          {pendentes.some(r => r.situacao === 'aguardando') && ' Entram sozinhos no controle da produtoB quando ele for anotado.'}
        </p>
      </div>
      <ul className="divide-y divide-amber-200">
        {pendentes.map(r => (
          <li key={r.numero} className="px-4 py-2.5 flex flex-wrap items-center gap-x-3 gap-y-1.5">
            <div className="min-w-0 flex-1 basis-56">
              <LinhaRomaneio r={r} />
              <p className="text-xs text-gray-600">
                {r.situacao === 'retirado' ? 'Tirado à mão. ' : ''}{r.acertoNumero ? `No acerto ${r.acertoNumero}.` : ''}
              </p>
            </div>
            {onPuxar && (
              <Botao variante="secundario" tamanho="sm" icone={ArrowDownToLine} onClick={() => onPuxar(r)}
                     carregando={puxando === r.numero} className="w-full sm:w-auto">
                Puxar para este controle
              </Botao>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}
