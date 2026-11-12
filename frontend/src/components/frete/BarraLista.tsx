import type { ReactNode } from 'react'
import { Search } from 'lucide-react'
import { classeCampo } from '../EstoquePecas'

// Barra de cima das listas da tela Viagens (Rotas cadastradas e Cargas): busca à
// esquerda, filtros ao lado e a contagem ("12 de 138 cargas") logo abaixo.
export default function BarraLista({ busca, onBusca, rotuloBusca, exemploBusca, filtros, contagem }: {
  busca: string
  onBusca: (v: string) => void
  rotuloBusca: string
  exemploBusca: string
  filtros?: ReactNode
  contagem: ReactNode
}) {
  return (
    <div className="space-y-2">
      <div className="flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-3">
        <div className="relative sm:w-72">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
          <input type="search" value={busca} onChange={e => onBusca(e.target.value)}
                 placeholder={exemploBusca} aria-label={rotuloBusca} className={`${classeCampo} pl-9`} data-nao-suja />
        </div>
        {filtros}
      </div>
      <p className="text-sm text-gray-600 tabular-nums" aria-live="polite">{contagem}</p>
    </div>
  )
}

/** Separa a lixeira das outras ações da linha, para não apagar por engano. */
export const separadorPerigo = 'ml-2 pl-2 border-l border-gray-200'
