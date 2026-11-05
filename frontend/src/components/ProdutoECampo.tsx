import { useState } from 'react'
import { CheckCircle2 } from 'lucide-react'

// Caixas de produtoE do relatório de frete. Quando a nota do Beta veio, o
// sistema já contou e só confirma — antes o campo aparecia sempre, e parecia
// que estava pedindo para digitar o que já tinha achado.
const classe = 'w-24 min-h-11 sm:min-h-9 rounded-lg border border-gray-300 bg-white px-3 text-sm text-center tabular-nums text-gray-900 focus:border-tinta-500'

export default function ProdutoECampo({ nasNotas, valor, onChange }: {
  nasNotas: number
  valor: number
  onChange: (v: number) => void
}) {
  const [somar, setSomar] = useState(false)

  const campo = (rotulo: string) => (
    <input
      type="number"
      min={0}
      inputMode="numeric"
      value={valor || ''}
      placeholder="0"
      autoFocus={somar}
      aria-label={rotulo}
      onChange={e => onChange(Math.max(0, parseFloat(e.target.value) || 0))}
      className={classe}
    />
  )

  if (nasNotas > 0) {
    return (
      <div className="rounded-xl px-4 py-3 flex flex-wrap items-center gap-3 border bg-emerald-50 border-emerald-200">
        <CheckCircle2 size={18} className="text-emerald-700 shrink-0" aria-hidden />
        <span className="text-sm text-gray-900">
          <b className="tabular-nums">{nasNotas}</b> caixas de produtoE vieram na nota do Beta.
        </span>
        {somar || valor > 0 ? (
          <span className="flex items-center gap-2 text-sm text-gray-800 animate-entrada">
            Somar mais {campo('Caixas de produtoE a somar')} caixas
          </span>
        ) : (
          <button type="button" onClick={() => setSomar(true)}
                  className="min-h-11 sm:min-h-8 px-1 text-sm font-medium text-gray-700 underline underline-offset-2 hover:text-gray-900">
            Somar mais
          </button>
        )}
      </div>
    )
  }

  return (
    <div className={`rounded-xl px-4 py-3 flex flex-wrap items-center gap-3 border ${
      valor > 0 ? 'bg-gema-50 border-gema-200' : 'bg-gray-50 border-gray-200'}`}>
      <span className="text-sm text-gray-800">
        Nenhuma caixa de produtoE nas notas. Se teve, informe:
      </span>
      {campo('Caixas de produtoE')}
    </div>
  )
}
