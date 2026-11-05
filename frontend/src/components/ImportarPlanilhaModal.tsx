import { useState } from 'react'
import { FileUp } from 'lucide-react'
import toast from 'react-hot-toast'
import api from '../services/api'
import { Modal } from './ModalBase'
import { brl, dataBr, lerNum, num } from '../utils/formato'

type ClientePrevia = {
  nome: string
  existe: boolean
  precoSugerido: number
  vendas: number
  produtosA?: number
  pesoKg?: number
  pacotes?: number
  temNova?: boolean
}

type Linha = { cliente: string; data: string; texto: string }
type Mudanca = { cliente: string; data: string; antes: string; depois: string }
type Resumo = {
  carga: string[]; novas: Linha[]; mudadas: Mudanca[]; iguais: number; soNoSite: Linha[]; mantidas: Linha[]
}

type Previa = {
  carga: {
    data: string; fornecedor: string
    quantidade?: number; valorTotal?: number; custoKg?: number; controle?: string
    quantidadePacotes?: number; valorProdutosB?: number; valorFrete?: number
    tipos?: { nome: string; entrada: number }[]
  }
  jaImportada: boolean
  resumo?: Resumo | null
  totalVendas: number
  totalProdutosA?: number
  pesoKg?: number
  custoPacote?: number
  clientes: ClientePrevia[]
}

type Props = {
  aberto: boolean
  produto: 'produtosA' | 'produtosB'
  onClose: () => void
  onImportou: (cargaId: number) => void
}

/**
 * Traz uma planilha de controle inteira para dentro do sistema.
 *
 * O preço é confirmado antes de gravar porque ele fica congelado dentro de cada
 * venda: importar com o preço errado deixaria a comissão errada para sempre. O
 * padrão sugerido é o próprio custo da carga — assim, quem esquecer de preencher
 * fica com comissão zero em vez de com um lucro inventado.
 */
export default function ImportarPlanilhaModal({ aberto, produto, onClose, onImportou }: Props) {
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [previa, setPrevia] = useState<Previa | null>(null)
  const [precos, setPrecos] = useState<Record<string, string>>({})
  const [lendo, setLendo] = useState(false)
  const [importando, setImportando] = useState(false)

  if (!aberto) return null

  const unidade = produto === 'produtosA' ? 'kg' : 'pacote'

  const limpar = () => { setArquivo(null); setPrevia(null); setPrecos({}) }
  const fechar = () => { limpar(); onClose() }

  const escolher = async (f: File | null) => {
    if (!f) return
    setArquivo(f); setPrevia(null); setLendo(true)
    try {
      const fd = new FormData()
      fd.append('arquivo', f)
      const r = await api.post<Previa>(`/api/estoque/${produto}/importar/previa`, fd,
        { headers: { 'Content-Type': 'multipart/form-data' } })
      setPrevia(r.data)
      setPrecos(Object.fromEntries(
        r.data.clientes.map(c => [c.nome, String(c.precoSugerido).replace('.', ',')])))
    } catch (e: unknown) {
      const err = e as { response?: { data?: { erro?: string } } }
      toast.error(err.response?.data?.erro ?? 'Não consegui ler a planilha.')
      setArquivo(null)
    } finally {
      setLendo(false)
    }
  }

  const importar = async () => {
    if (!arquivo || !previa) return
    setImportando(true)
    try {
      const fd = new FormData()
      fd.append('arquivo', arquivo)
      fd.append('precos', JSON.stringify(
        previa.clientes.map(c => ({ nome: c.nome, preco: lerNum(precos[c.nome] ?? '') }))))

      const r = await api.post<{ cargaId: number; vendas: number; atualizada?: boolean; resumo?: Resumo }>(
        `/api/estoque/${produto}/importar`, fd,
        { headers: { 'Content-Type': 'multipart/form-data' } })

      if (r.data.atualizada && r.data.resumo) {
        const z = r.data.resumo
        toast.success(`Carga atualizada: ${z.novas.length} saída(s) nova(s), ${z.mudadas.length} corrigida(s).`)
      } else toast.success(`${r.data.vendas} saída(s) importada(s)!`)
      limpar()
      onImportou(r.data.cargaId)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { erro?: string } } }
      toast.error(err.response?.data?.erro ?? 'Não consegui importar.')
    } finally {
      setImportando(false)
    }
  }

  const custo = produto === 'produtosA' ? previa?.carga.custoKg ?? 0 : previa?.custoPacote ?? 0

  return (
    <Modal titulo={`Importar planilha de ${produto}`} onClose={fechar}>
      {!previa && (
        <>
          <p className="text-sm text-gray-600">
            Envie a planilha de controle que você preenche à mão. Eu leio a carga e
            todas as saídas, e mostro o que entendi antes de gravar.
          </p>
          <div
            onClick={() => document.getElementById('imp-planilha')?.click()}
            className="border-2 border-dashed border-gray-300 rounded-xl p-8 text-center cursor-pointer hover:border-tinta-300 hover:bg-gray-50"
          >
            <input id="imp-planilha" type="file" accept=".xlsx" hidden
                   onChange={e => escolher(e.target.files?.[0] ?? null)} />
            <FileUp size={32} strokeWidth={1.5} className="mx-auto mb-2 text-tinta-400" />
            <p className="text-sm font-medium text-gray-600">
              {lendo ? 'Lendo a planilha...' : 'Clique para escolher o arquivo (.xlsx)'}
            </p>
            {arquivo && <p className="text-xs text-gray-400 mt-1 truncate">{arquivo.name}</p>}
          </div>
        </>
      )}

      {previa && (
        <>
          {previa.jaImportada && previa.resumo && <ResumoReimportacao resumo={previa.resumo} />}

          <div className="bg-gray-50 border border-gray-200 rounded-lg p-3 text-sm space-y-1">
            <p className="font-semibold text-gray-800">
              {dataBr(previa.carga.data)} · {previa.carga.fornecedor}
            </p>
            {produto === 'produtosA' ? (
              <p className="text-gray-600">
                {previa.carga.quantidade} produtosA · {brl(previa.carga.valorTotal ?? 0)} ·
                custo {brl(previa.carga.custoKg ?? 0)}/kg
              </p>
            ) : (
              <p className="text-gray-600">
                {previa.carga.quantidadePacotes} pacotes comprados ·
                {' '}{brl(previa.carga.valorProdutosB ?? 0)} + frete {brl(previa.carga.valorFrete ?? 0)} ·
                custo {brl(previa.custoPacote ?? 0)} por pacote
              </p>
            )}
            <p className="text-gray-600">
              <b>{previa.totalVendas}</b> saída(s)
              {previa.totalProdutosA ? ` · ${previa.totalProdutosA} produtosA · ${num(previa.pesoKg ?? 0)} kg` : ''}
            </p>
            {previa.carga.tipos && previa.carga.tipos.length > 0 && (
              <p className="text-gray-600">
                Entrada: {previa.carga.tipos.filter(t => t.entrada > 0)
                  .map(t => `${t.nome} ${t.entrada}`).join(' · ')}
              </p>
            )}
          </div>

          {previa.clientes.some(c => c.temNova !== false) && <div>
            <p className="text-xs font-medium text-gray-700 mb-1">
              Preço de cada cliente (R$ por {unidade})
            </p>
            <p className="text-[11px] text-gray-400 mb-2">
              Vem preenchido com o custo ({brl(custo)}), que dá comissão zero.
              Troque pelos preços de verdade — depois de importar, não dá pra corrigir
              a comissão dessas vendas sem apagar e importar de novo.
            </p>
            <div className="space-y-1.5 max-h-72 overflow-y-auto">
              {previa.clientes.filter(c => c.temNova !== false).map(c => (
                <div key={c.nome} className="flex items-center gap-2 bg-white border border-gray-200 rounded-lg px-2 py-1.5">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-gray-800 truncate">
                      {c.nome}
                      {!c.existe && <span className="text-[10px] text-tinta-600 ml-1">novo</span>}
                    </p>
                    <p className="text-[11px] text-gray-400">
                      {c.vendas} saída(s)
                      {c.produtosA ? ` · ${c.produtosA} bob · ${num(c.pesoKg ?? 0)} kg` : ''}
                      {c.pacotes ? ` · ${num(c.pacotes, 1)} pacotes` : ''}
                    </p>
                  </div>
                  <input
                    value={precos[c.nome] ?? ''}
                    onChange={e => setPrecos({ ...precos, [c.nome]: e.target.value })}
                    inputMode="decimal"
                    className={`w-24 border rounded-lg px-2 py-1.5 text-sm text-right font-mono
                      ${lerNum(precos[c.nome] ?? '') <= custo
                        ? 'border-amber-300 bg-amber-50' : 'border-gray-200'}`}
                  />
                </div>
              ))}
            </div>
          </div>}

          <div className="flex gap-2 pt-1">
            <button onClick={importar} disabled={importando || (previa.jaImportada && semMudanca(previa.resumo))}
                    className="flex-1 px-4 py-2.5 bg-tinta-700 text-white text-sm font-semibold rounded-lg hover:bg-tinta-800 disabled:opacity-50">
              {importando ? 'Importando...'
                : previa.jaImportada
                  ? (semMudanca(previa.resumo) ? 'Nada novo para gravar' : 'Gravar o que mudou')
                  : `Importar ${previa.totalVendas} saída(s)`}
            </button>
            <button onClick={limpar} className="px-4 py-2.5 text-sm text-gray-600 hover:text-gray-900">
              Trocar arquivo
            </button>
          </div>
        </>
      )}
    </Modal>
  )
}

const semMudanca = (r?: Resumo | null) =>
  !r || (r.carga.length === 0 && r.novas.length === 0 && r.mudadas.length === 0)

/** Mesma carga subida de novo: o que entra, o que muda e o que fica como está (UI-21). */
function ResumoReimportacao({ resumo }: { resumo: Resumo }) {
  const [detalhes, setDetalhes] = useState(false)
  const nada = semMudanca(resumo)
  const partes = [
    resumo.novas.length > 0 && `${resumo.novas.length} saída(s) nova(s)`,
    resumo.mudadas.length > 0 && `${resumo.mudadas.length} saída(s) com mudança`,
    resumo.carga.length > 0 && 'dados da carga mudaram',
    resumo.iguais > 0 && `${resumo.iguais} igual(is) ao site`,
  ].filter(Boolean)
  return (
    <div className="text-sm bg-gema-50 border border-gema-200 text-gray-800 rounded-lg p-3 space-y-2">
      <p className="font-medium">
        Essa carga já está no site.{' '}
        {nada ? 'A planilha não traz nada novo.' : 'Vou gravar só o que a planilha traz de novo; o resto fica como está.'}
      </p>
      {partes.length > 0 && <p className="text-gray-700">{partes.join(' · ')}</p>}
      {resumo.soNoSite.length > 0 && (
        <p className="text-gray-600">{resumo.soNoSite.length} saída(s) existem só no site e continuam lá.</p>
      )}
      {resumo.mantidas.length > 0 && (
        <p className="text-gray-600">{resumo.mantidas.length} saída(s) vieram do controle de carregamento e não mudam.</p>
      )}
      {(resumo.novas.length + resumo.mudadas.length + resumo.carga.length + resumo.soNoSite.length + resumo.mantidas.length) > 0 && (
        <button type="button" onClick={() => setDetalhes(d => !d)} className="text-tinta-700 dark:text-tinta-300 font-medium underline">
          {detalhes ? 'Esconder detalhes' : 'Ver detalhes'}
        </button>
      )}
      {detalhes && (
        <div className="space-y-2 max-h-64 overflow-y-auto">
          {resumo.carga.length > 0 && <Bloco titulo="Carga">{resumo.carga.map(c => <li key={c}>{c}</li>)}</Bloco>}
          {resumo.novas.length > 0 && <Bloco titulo="Entram">
            {resumo.novas.map((n, i) => <li key={i}>{dataBr(n.data)} · {n.cliente}: {n.texto}</li>)}</Bloco>}
          {resumo.mudadas.length > 0 && <Bloco titulo="Mudam">
            {resumo.mudadas.map((m, i) => <li key={i}>{dataBr(m.data)} · {m.cliente}: {m.antes} → {m.depois}</li>)}</Bloco>}
          {resumo.soNoSite.length > 0 && <Bloco titulo="Só no site (ficam)">
            {resumo.soNoSite.map((n, i) => <li key={i}>{dataBr(n.data)} · {n.cliente}: {n.texto}</li>)}</Bloco>}
          {resumo.mantidas.length > 0 && <Bloco titulo="Do carregamento (não mudam)">
            {resumo.mantidas.map((n, i) => <li key={i}>{dataBr(n.data)} · {n.cliente}: {n.texto}</li>)}</Bloco>}
        </div>
      )}
    </div>
  )
}

function Bloco({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="text-xs font-semibold text-gray-700">{titulo}</p>
      <ul className="text-xs text-gray-700 list-disc pl-4 space-y-0.5">{children}</ul>
    </div>
  )
}
