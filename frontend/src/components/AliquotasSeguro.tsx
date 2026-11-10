import { useCallback, useEffect, useState } from 'react'
import { Plus, Save, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone } from './ui/Botao'
import { Carregando, ErroCarga } from './ui/Estados'
import { classeNumero } from './EstoquePecas'
import { aliquotasSeguroApi } from '../services/aliquotasSeguroApi'
import { lerNum } from '../utils/formato'

// Alíquotas do seguro de carga por UF do destino (SEG-06). Vale para as
// próximas planilhas SG; as guardadas ficam com as de quando foram geradas.

type Linha = { chave: string; uf: string; aliquota: string }

const mostrar = (n: number) => n.toLocaleString('pt-BR', { minimumFractionDigits: 3, maximumFractionDigits: 4 })
const erroApi = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

export default function AliquotasSeguro() {
  const [linhas, setLinhas] = useState<Linha[] | null>(null)
  const [original, setOriginal] = useState('')
  const [erro, setErro] = useState(false)
  const [gravando, setGravando] = useState(false)

  const aplicar = (lista: { id: number; uf: string; aliquota: number }[]) => {
    const ls = lista.map(a => ({ chave: String(a.id), uf: a.uf, aliquota: mostrar(a.aliquota) }))
    setLinhas(ls)
    setOriginal(JSON.stringify(ls.map(l => [l.uf, l.aliquota])))
  }

  const carregar = useCallback(() => {
    setErro(false)
    aliquotasSeguroApi.listar().then(aplicar).catch(() => setErro(true))
  }, [])
  useEffect(carregar, [carregar])

  if (erro) return <ErroCarga mensagem="Não consegui carregar as alíquotas." onTentar={carregar} />
  if (!linhas) return <Carregando linhas={6} />

  const mudar = (i: number, campo: 'uf' | 'aliquota', v: string) =>
    setLinhas(ls => ls!.map((l, j) => j === i ? { ...l, [campo]: campo === 'uf' ? v.toUpperCase().slice(0, 2) : v } : l))

  const ufs = linhas.map(l => l.uf.trim())
  const problema =
    linhas.length === 0 ? 'Deixe pelo menos uma UF.'
    : ufs.some(u => !/^[A-Z]{2}$/.test(u)) ? 'Toda UF tem duas letras, ex.: GO.'
    : ufs.some((u, i) => ufs.indexOf(u) !== i) ? 'Tem UF repetida.'
    : linhas.some(l => !(lerNum(l.aliquota) > 0 && lerNum(l.aliquota) < 100)) ? 'Toda alíquota tem de ser maior que zero.'
    : null
  const mudou = JSON.stringify(linhas.map(l => [l.uf, l.aliquota])) !== original

  const gravar = async () => {
    setGravando(true)
    try {
      aplicar(await aliquotasSeguroApi.gravar(linhas.map(l => ({ uf: l.uf.trim(), aliquota: lerNum(l.aliquota) }))))
      toast.success('Alíquotas gravadas. Valem para as próximas planilhas.')
    } catch (e) {
      toast.error(erroApi(e, 'Não consegui gravar as alíquotas.'))
    } finally {
      setGravando(false)
    }
  }

  return (
    <div className="space-y-4 max-w-md">
      <p className="text-sm text-gray-600">
        Alíquota do seguro de carga pela UF do cliente, em %: <b>0,069</b> quer dizer 0,069% do valor da nota.
        Mudar aqui vale para as <b>próximas</b> planilhas; as já guardadas não mudam.
      </p>

      <div className="rounded-xl border border-gray-200 overflow-hidden">
        <div className="grid grid-cols-[5rem_1fr_2.5rem] gap-2 px-3 py-2 bg-gray-50 border-b border-gray-200 text-sm font-medium text-gray-700">
          <span>UF</span><span className="text-right">Alíquota (%)</span><span className="sr-only">Tirar</span>
        </div>
        <ul className="divide-y divide-gray-100">
          {linhas.map((l, i) => (
            <li key={l.chave} className="grid grid-cols-[5rem_1fr_2.5rem] gap-2 items-center px-3 py-2">
              <input value={l.uf} onChange={e => mudar(i, 'uf', e.target.value)} aria-label={`UF da linha ${i + 1}`}
                     placeholder="UF" maxLength={2} className={`${classeNumero} w-full uppercase`} />
              <input value={l.aliquota} onChange={e => mudar(i, 'aliquota', e.target.value)} inputMode="decimal"
                     aria-label={`Alíquota de ${l.uf || `a linha ${i + 1}`}`} placeholder="Ex: 0,069"
                     className={`${classeNumero} w-full text-right`} />
              <BotaoIcone icone={Trash2} rotulo={`Tirar ${l.uf || `a linha ${i + 1}`}`} perigo
                          onClick={() => setLinhas(ls => ls!.filter((_, j) => j !== i))} />
            </li>
          ))}
        </ul>
      </div>

      <div className="flex flex-col sm:flex-row gap-2">
        <Botao variante="secundario" icone={Plus}
               onClick={() => setLinhas(ls => [...ls!, { chave: `nova-${Date.now()}`, uf: '', aliquota: '' }])}>
          Acrescentar UF
        </Botao>
        <Botao variante="primario" icone={Save} onClick={gravar} carregando={gravando} textoCarregando="Gravando..."
               disabled={!mudou || !!problema} title={problema ?? (!mudou ? 'Nada mudou' : undefined)}>
          Gravar alíquotas
        </Botao>
      </div>
      {mudou && problema && <p className="text-sm text-red-700" role="alert">{problema}</p>}
    </div>
  )
}
