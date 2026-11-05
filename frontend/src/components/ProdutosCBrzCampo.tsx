// ProdutoC vai junto com a carga da BRZ só às vezes e não vem na nota, então a
// tela pergunta. "Não" deixa a linha em branco no romaneio; "Sim" pede a
// quantidade e mostra o preço, que já vem com o padrão mas pode mudar.

export interface ProdutosCBrz {
  vai: boolean | null   // null = ainda não respondeu
  quantidade: number
  preco: number
}

export const produtosCSemResposta = (precoPadrao = 22): ProdutosCBrz =>
  ({ vai: null, quantidade: 0, preco: precoPadrao })

// Para o botão de gerar: devolve o que falta responder, ou null se está ok.
export function faltaProdutosC(b: ProdutosCBrz): string | null {
  if (b.vai === null) return 'Responda se foram produtosC junto com a carga da BRZ.'
  if (b.vai && b.quantidade <= 0) return 'Informe a quantidade de produtosC da BRZ.'
  if (b.vai && b.preco <= 0) return 'Informe o preço da produtoC da BRZ.'
  return null
}

const campo = 'min-h-11 sm:min-h-9 rounded-lg border bg-white px-3 text-sm text-center tabular-nums text-gray-900 focus:border-tinta-500'

export default function ProdutosCBrzCampo({ valor, onChange }: {
  valor: ProdutosCBrz
  onChange: (v: ProdutosCBrz) => void
}) {
  const respondido = valor.vai !== null && !(valor.vai && valor.quantidade <= 0)
  const opcao = (sim: boolean) => (
    <button type="button" role="radio" aria-checked={valor.vai === sim}
            onClick={() => onChange({ ...valor, vai: sim })}
            className={`min-w-[4rem] min-h-10 sm:min-h-8 px-3 text-sm rounded-md transition-colors ${
              valor.vai === sim ? 'bg-tinta-700 text-white font-semibold shadow-sm' : 'text-gray-700 hover:bg-gray-100'}`}>
      {sim ? 'Sim' : 'Não'}
    </button>
  )

  return (
    <div className={`rounded-xl px-4 py-3 flex flex-wrap items-center gap-x-4 gap-y-3 border ${
      respondido ? 'bg-gray-50 border-gray-200' : 'bg-amber-50 border-amber-300'}`}>
      <span id="brz-produtosC" className="text-sm font-medium text-gray-900">BRZ: foram produtosC junto?</span>
      <div role="radiogroup" aria-labelledby="brz-produtosC" className="inline-flex gap-1 bg-white border border-gray-200 rounded-lg p-0.5">
        {opcao(true)}
        {opcao(false)}
      </div>

      {valor.vai && (
        <div className="flex flex-wrap items-center gap-3 animate-entrada">
          <label className="flex items-center gap-2 text-sm text-gray-800">
            Quantidade
            <input type="number" min={1} inputMode="numeric" autoFocus value={valor.quantidade || ''} placeholder="0"
                   onChange={e => onChange({ ...valor, quantidade: Math.max(0, parseInt(e.target.value) || 0) })}
                   className={`${campo} w-20 ${valor.quantidade > 0 ? 'border-gray-300' : 'border-amber-500'}`} />
          </label>
          <label className="flex items-center gap-2 text-sm text-gray-800">
            Preço por produtoC (R$)
            <input type="number" min={0} step="0.01" inputMode="decimal" value={valor.preco || ''}
                   onChange={e => onChange({ ...valor, preco: Math.max(0, parseFloat(e.target.value) || 0) })}
                   className={`${campo} w-24 border-gray-300`} />
          </label>
        </div>
      )}
    </div>
  )
}
