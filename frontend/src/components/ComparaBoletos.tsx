import { brl, dataBr } from '../utils/formato'
import type { ItemImportacaoReceber } from '../services/boletosContasApi'

// Boleto subido que já existe: o guardado ao lado do que vai substituir, com as
// informações principais. O que mudou fica destacado.
export default function ComparaBoletos({ itens }: { itens: ItemImportacaoReceber[] }) {
  const valor = (v: number | null | undefined) => (v == null ? '—' : brl(v))
  const data = (d: string | null | undefined) => (d ? dataBr(d) : '—')
  const texto = (t: string | null | undefined) => t || '—'
  return (
    <div className="space-y-3">
      {itens.map(i => {
        const linhas: [string, string, string][] = [
          ['Nº do boleto', texto(i.nossoNumero), texto(i.novo?.nossoNumero)],
          ['Valor', valor(i.valor), valor(i.novo?.valor)],
          ['Emissão', data(i.emissao), data(i.novo?.emissao)],
          ['Vencimento', data(i.vencimento), data(i.novo?.vencimento)],
        ]
        return (
          <div key={i.arquivo} className="border border-gray-200 rounded-lg overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 text-xs text-gray-600">
                <tr>
                  <th className="px-3 py-1.5 text-left font-medium"><span className="sr-only">Informação</span></th>
                  <th className="px-3 py-1.5 text-left font-medium">Boleto anterior</th>
                  <th className="px-3 py-1.5 text-left font-medium">Boleto que vai substituir</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {linhas.map(([rotulo, antes, depois]) => (
                  <tr key={rotulo}>
                    <th scope="row" className="px-3 py-1.5 text-left font-medium text-gray-600 whitespace-nowrap">{rotulo}</th>
                    <td className="px-3 py-1.5 tabular-nums text-gray-900">{antes}</td>
                    <td className={`px-3 py-1.5 tabular-nums ${i.novo && antes !== depois ? 'font-semibold text-amber-800 bg-amber-50' : 'text-gray-900'}`}>{depois}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      })}
    </div>
  )
}
