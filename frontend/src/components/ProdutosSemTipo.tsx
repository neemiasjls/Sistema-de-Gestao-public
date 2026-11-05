import { useEffect, useState } from 'react'
import { CircleHelp } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao from './ui/Botao'
import { formatBRL, tiposVolumeApi, type CorVolume, type TipoVolume } from '../services/api'

// Produto da nota que não cai em nenhum tipo de volume (NFE-09). O romaneio usa os
// nomes de sempre, então antes de gerar a tela pergunta como esse produto se
// chama: um tipo que já existe ou um nome novo. A escolha vira escrita do tipo,
// e da próxima vez o produto já entra sozinho. Sem isso o produto ficava fora.

export interface ProdutoSemTipo {
  cliente: string
  nf: string
  produto: string
  quantidade: number
  preco: number
}

const NOVO = '__novo__'
const campo = 'w-full min-h-11 sm:min-h-9 rounded-lg border border-gray-300 bg-white px-2.5 text-sm text-gray-900'

const corDe = (texto: string): CorVolume =>
  /\b(VR|VERMELHO|VERMELHOS)\b/i.test(texto) ? 'VERMELHO' : /\b(BR|BRANCO|BRANCOS)\b/i.test(texto) ? 'BRANCO' : 'OUTRO'

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

export default function ProdutosSemTipo({ produtos, onResolvido }: {
  produtos: ProdutoSemTipo[]
  onResolvido: (produto: string) => void
}) {
  const [tipos, setTipos] = useState<TipoVolume[]>([])
  const [escolha, setEscolha] = useState<Record<string, string>>({})
  const [nomeNovo, setNomeNovo] = useState<Record<string, string>>({})
  const [salvando, setSalvando] = useState<string | null>(null)

  useEffect(() => {
    if (!produtos.length) return
    tiposVolumeApi.listar().then(r => setTipos(r.data.tipos)).catch(() => toast.error('Não consegui carregar os tipos de volumes.'))
  }, [produtos.length])

  if (!produtos.length) return null

  // O mesmo produto em duas notas se resolve uma vez só
  const unicos = [...new Map(produtos.map(p => [p.produto, p])).values()]

  const salvar = async (produto: string) => {
    const valor = escolha[produto] ?? ''
    if (!valor) { toast.error('Escolha o nome no romaneio.'); return }
    setSalvando(produto)
    try {
      let id: number
      if (valor === NOVO) {
        const nome = (nomeNovo[produto] ?? '').trim()
        if (!nome) { toast.error('Escreva o nome novo.'); return }
        const r = await tiposVolumeApi.criar({ nome, cor: corDe(produto) })
        id = r.data.id
      } else id = Number(valor)
      await tiposVolumeApi.adicionarEscrita(id, produto)
      toast.success('Cadastrado.')
      onResolvido(produto)
    } catch (e) {
      toast.error(erroDe(e, 'Não consegui cadastrar.'))
    } finally {
      setSalvando(null)
    }
  }

  return (
    <div role="alert" className="rounded-xl border border-amber-300 bg-amber-50 px-4 py-3 space-y-3 animate-entrada">
      <div className="flex items-start gap-2.5">
        <CircleHelp size={18} className="text-amber-700 shrink-0 mt-0.5" aria-hidden />
        <p className="text-sm text-gray-900">
          <b>{unicos.length === 1 ? 'Um produto das notas não tem nome' : `${unicos.length} produtos das notas não têm nome`}</b> no
          romaneio. Escolha como ele aparece antes de gerar, para nada ficar de fora.
        </p>
      </div>
      <ul className="space-y-2">
        {unicos.map(p => (
          <li key={p.produto} className="rounded-lg bg-white/80 border border-black/5 px-3 py-2 space-y-2 text-sm">
            <p className="text-gray-900 break-words">
              <b>{p.produto}</b>
              <span className="text-gray-600"> · {p.cliente} · NF {p.nf} · {p.quantidade} caixas a {formatBRL(p.preco)}</span>
            </p>
            <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto] sm:items-center">
              <select value={escolha[p.produto] ?? ''} aria-label={`Nome no romaneio para ${p.produto}`}
                      onChange={e => setEscolha(v => ({ ...v, [p.produto]: e.target.value }))} className={campo}>
                <option value="">Nome no romaneio...</option>
                {tipos.map(t => <option key={t.id} value={t.id}>{t.nome}</option>)}
                <option value={NOVO}>Nome novo...</option>
              </select>
              {escolha[p.produto] === NOVO ? (
                <input value={nomeNovo[p.produto] ?? ''} placeholder="Ex.: Extra Br Pvc" aria-label="Nome novo"
                       onChange={e => setNomeNovo(v => ({ ...v, [p.produto]: e.target.value }))} className={campo} />
              ) : <span className="hidden sm:block" />}
              <Botao variante="primario" onClick={() => salvar(p.produto)} carregando={salvando === p.produto}
                     textoCarregando="Salvando..." bloco className="sm:w-auto">Usar este nome</Botao>
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}
