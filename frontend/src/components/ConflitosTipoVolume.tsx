import { CheckCircle2, TriangleAlert } from 'lucide-react'
import { formatBRL, type ConflitoTipo, type CorrecaoTipo } from '../services/api'

// Quem emite a nota às vezes escreve o tipo errado (GB10X30 virou GV10X30 no
// Farol). O romaneio tem uma linha por tipo, então as caixas somavam e ficava
// um preço só. Aqui a linha suspeita aparece com a nota, a quantidade e o
// preço, e dá para trocar o tipo antes de gerar. O sistema só avisa da
// divergência e aponta a linha provável; quem escolhe o tipo é o usuário.
//
// No celular cada linha da nota vira um cartão (a tabela tinha 5 colunas).

const chave = (nf: string, produto: string, preco: number) => `${nf}|${produto}|${preco}`

const campo = 'w-full sm:w-auto min-h-11 sm:min-h-9 rounded-lg border bg-white px-2.5 text-sm text-gray-900'

export default function ConflitosTipoVolume({ conflitos, tipos, correcoes, onChange }: {
  conflitos: ConflitoTipo[]
  tipos: string[]
  correcoes: CorrecaoTipo[]
  onChange: (c: CorrecaoTipo[]) => void
}) {
  if (!conflitos.length) return null

  const trocaDe = (nf: string, produto: string, preco: number) =>
    correcoes.find(c => chave(c.nf, c.produtoNota, c.preco) === chave(nf, produto, preco))

  const definir = (nf: string, produtoNota: string, preco: number, tipoOriginal: string, novoTipo: string) => {
    const resto = correcoes.filter(c => chave(c.nf, c.produtoNota, c.preco) !== chave(nf, produtoNota, preco))
    onChange(novoTipo === tipoOriginal ? resto : [...resto, { nf, produtoNota, preco, novoTipo }])
  }

  return (
    <div className="space-y-3">
      {conflitos.map((c, i) => {
        const resolvido = c.ocorrencias.some(o => trocaDe(o.nf, o.produtoNota, o.preco))
        return (
          <div key={i} role={resolvido ? undefined : 'alert'} className={`rounded-xl border px-4 py-3 space-y-3 animate-entrada ${
            resolvido ? 'bg-emerald-50 border-emerald-200' : 'bg-amber-50 border-amber-300'}`}>
            <div className="flex items-start gap-2.5">
              {resolvido
                ? <CheckCircle2 size={18} className="text-emerald-700 shrink-0 mt-0.5" aria-hidden />
                : <TriangleAlert size={18} className="text-amber-700 shrink-0 mt-0.5" aria-hidden />}
              <div className="text-sm text-gray-900 space-y-1">
                <p><b>{c.cliente}</b>: {c.motivo}</p>
                {c.sugestao && <p className="text-gray-700">{c.sugestao}</p>}
              </div>
            </div>

            <ul className="space-y-2">
              {c.ocorrencias.map(o => {
                const troca = trocaDe(o.nf, o.produtoNota, o.preco)
                const suspeita = c.sugestaoNF === o.nf && c.sugestaoPreco === o.preco
                  && c.sugestaoProduto === o.produtoNota
                return (
                  <li key={chave(o.nf, o.produtoNota, o.preco)}
                      className={`rounded-lg bg-white/80 border px-3 py-2 grid gap-2 sm:grid-cols-[6rem_1fr_5rem_6.5rem_auto] sm:items-center text-sm ${
                        suspeita && !troca ? 'border-amber-400' : 'border-black/5'}`}>
                    <span className="tabular-nums"><span className="sm:hidden text-gray-500">NF </span>{o.nf}</span>
                    <span className="text-gray-700 min-w-0 break-words">
                      {o.produtoNota}
                      {suspeita && !troca && <span className="block text-sm font-medium text-amber-800">provável divergente</span>}
                    </span>
                    <span className="tabular-nums sm:text-right">{o.quantidade}<span className="text-gray-500"> caixas</span></span>
                    <span className={`tabular-nums sm:text-right ${suspeita && !troca ? 'text-amber-900 font-semibold' : ''}`}>{formatBRL(o.preco)}</span>
                    <select
                      value={troca?.novoTipo ?? o.tipo}
                      aria-label={`Tipo no romaneio para ${o.produtoNota}, nota ${o.nf}`}
                      onChange={e => definir(o.nf, o.produtoNota, o.preco, o.tipo, e.target.value)}
                      className={`${campo} ${troca ? 'border-emerald-500 font-medium' : 'border-gray-300'}`}
                    >
                      {!tipos.includes(o.tipo) && <option value={o.tipo}>{o.tipo}</option>}
                      {tipos.map(t => <option key={t} value={t}>{t}</option>)}
                    </select>
                  </li>
                )
              })}
            </ul>

            {!resolvido && (
              <p className="text-sm text-amber-900">
                Se gerar assim, o romaneio sai com as caixas somadas num preço só. Confira as notas e, se for o caso, troque o tipo da linha errada.
              </p>
            )}
          </div>
        )
      })}
    </div>
  )
}
