import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import Botao from '../components/ui/Botao'
import { Archive, Download, FileUp, Warehouse } from 'lucide-react'
import { useCallback, useState } from 'react'
import ProdutoECampo from '../components/ProdutoECampo'
import ProdutosCBrzCampo, { produtosCSemResposta, faltaProdutosC, type ProdutosCBrz } from '../components/ProdutosCBrzCampo'
import ConflitosTipoVolume from '../components/ConflitosTipoVolume'
import ProdutosSemTipo, { type ProdutoSemTipo } from '../components/ProdutosSemTipo'
import toast from 'react-hot-toast'
import api, { downloadBlob, guardarArquivosNFe, type ConflitoTipo, type CorrecaoTipo } from '../services/api'
import ClienteBadge from '../components/ClienteBadge'
import RomaneiosFazendaLista from '../components/RomaneiosFazendaLista'
import RomaneiosGuardados from '../components/RomaneiosGuardados'

interface AnaliseResult {
  totalXmls: number
  totalNfes: number
  clientesEncontrados: string[]
  numerosSugeridos: Record<string, number>
  temCedro: boolean
  temBrz?: boolean
  precoProdutoCPadrao?: number
  temFrete: boolean
  produtosENasNotas: number
  conflitosTipo?: ConflitoTipo[]
  produtosSemTipo?: ProdutoSemTipo[]
  tiposRomaneio?: string[]
}

const campoNumero = 'min-h-11 sm:min-h-9 rounded-lg border bg-white px-3 text-sm text-center tabular-nums text-gray-900 focus:border-tinta-500'

const CLIENTES = [
  ['BRZ', 'BRZ Distribuidor'], ['AURORA', 'Volumes Aurora Atacado e Varejo'], ['CEDRO', 'C.D. COMERCIO DE HORTIFRUTI'],
  ['DUNAS', 'Verdurao Dunas'], ['FAROL', 'Litoral Comércio e Transportes'],
] as const

export default function PlanilhasPage() {
  const [aba, setAba] = useState<'gerar' | 'guardados' | 'fazenda'>('gerar')
  const idAbas = useAbas('planilhas')
  // Avisos da leitura e da geração: ficam na tela até fechar (D-07)
  const [avisos, setAvisos] = useState<string[]>([])
  const [arquivos, setArquivos] = useState<File[]>([])
  const [analisando, setAnalisando] = useState(false)
  const [analise, setAnalise] = useState<AnaliseResult | null>(null)
  const [qtdProdutosD, setQtdProdutosD] = useState(0)
  const [caixasProdutoE, setCaixasProdutoE] = useState(0)
  const [produtosC, setProdutosC] = useState<ProdutosCBrz>(produtosCSemResposta())
  const [numeros, setNumeros] = useState<Record<string, number>>({})
  const [gerando, setGerando] = useState(false)
  const [correcoes, setCorrecoes] = useState<CorrecaoTipo[]>([])

  // PDF solto junto com os XMLs vai para o arquivo de notas, onde encontra a
  // nota dele pelo número do arquivo.
  const guardarPdfs = async (pdfs: File[]) => {
    if (!pdfs.length) return
    try {
      const r = await guardarArquivosNFe(pdfs)
      if (!r) return
      if (r.pdfs > 0) toast.success(`${r.pdfs} PDF(s) guardado(s) em Notas Fiscais.`)
      if (r.avisos.length) setAvisos(v => [...v, ...r.avisos])
    } catch {
      toast.error('Não consegui guardar os PDFs.')
    }
  }

  const processarArquivos = useCallback(async (files: File[]) => {
    const xmlFiles = files.filter(f => f.name.toLowerCase().endsWith('.xml'))
    const pdfFiles = files.filter(f => f.name.toLowerCase().endsWith('.pdf'))

    if (!xmlFiles.length) {
      if (pdfFiles.length) { await guardarPdfs(pdfFiles); return }
      toast.error('Nenhum XML selecionado.')
      return
    }

    setArquivos(xmlFiles)
    setAnalise(null)
    setCorrecoes([])
    setAnalisando(true)

    try {
      const fd = new FormData()
      xmlFiles.forEach(f => fd.append('arquivos', f))
      const resp = await api.post<AnaliseResult>('/api/nfe/analisar', fd, {
        headers: { 'Content-Type': 'multipart/form-data' }
      })
      const data = resp.data
      setAnalise(data)
      setProdutosC(produtosCSemResposta(data.precoProdutoCPadrao))
      setNumeros(data.numerosSugeridos ?? {})
      if (!data.clientesEncontrados.length)
        setAvisos(v => [...v, 'Nenhum cliente de romaneio nesses XMLs: essa carga não gera planilha.'])

      // Os XMLs o servidor já arquivou ao ler; faltam os PDFs.
      await guardarPdfs(pdfFiles)
    } catch {
      toast.error('Não consegui analisar os arquivos.')
    } finally {
      setAnalisando(false)
    }
  }, [])

  const gerarPlanilhas = async () => {
    if (!arquivos.length) return
    if (analise?.temBrz) {
      const falta = faltaProdutosC(produtosC)
      if (falta) { toast.error(falta); return }
    }
    if (analise?.temCedro && qtdProdutosD <= 0) {
      toast.error('Informe a quantidade de caixas de produtosD da CEDRO FRUTAS.')
      return
    }
    if (analise?.produtosSemTipo?.length) {
      toast.error('Escolha o nome no romaneio dos produtos sem nome antes de gerar.')
      return
    }
    setGerando(true)
    try {
      const fd = new FormData()
      arquivos.forEach(f => fd.append('arquivos', f))
      fd.append('numeros', JSON.stringify(numeros))
      fd.append('correcoes', JSON.stringify(correcoes))
      const resp = await api.post<{ arquivos: { nome: string; conteudo: string }[]; avisos?: string[] }>(
        `/api/nfe/gerar-planilhas?qtdProdutosDCedro=${qtdProdutosD}&caixasProdutoE=${caixasProdutoE}&produtosCBrz=${analise?.temBrz && produtosC.vai ? produtosC.quantidade : 0}&precoProdutoCBrz=${produtosC.preco}`,
        fd,
        { headers: { 'Content-Type': 'multipart/form-data' } }
      )
      // Baixa cada arquivo individualmente (sem zip).
      // Obs: na 1ª vez o navegador pode pedir permissão para múltiplos downloads.
      for (const arq of resp.data.arquivos) {
        const bin = atob(arq.conteudo)
        const bytes = new Uint8Array(bin.length)
        for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
        downloadBlob(new Blob([bytes]), arq.nome)
        await new Promise(r => setTimeout(r, 300)) // pequeno intervalo entre downloads
      }
      toast.success(`${resp.data.arquivos.length} arquivo(s) baixado(s).`)
      if (resp.data.avisos?.length) setAvisos(v => [...v, ...resp.data.avisos!])
    } catch (e) {
      toast.error((e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? 'Não consegui gerar as planilhas.')
    } finally {
      setGerando(false)
    }
  }

  return (
    <div className="space-y-6 max-w-3xl">
      <CabecalhoTela descricao="Romaneios dos clientes (gerar pelas notas e ver os passados, por cliente) e os romaneios da fazenda que chegaram no acerto Alfa." />

      {/* A aba de guardados monta de novo a cada abertura, então já traz o que
          acabou de ser gerado. A de gerar só esconde, para não perder os XMLs. */}
      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Romaneios"
            opcoes={[['gerar', 'Gerar romaneio', FileUp], ['guardados', 'Clientes', Archive], ['fazenda', 'Fazenda', Warehouse]]} />

      <PainelAba id={idAbas} valor={aba}>
      {aba === 'guardados' && <RomaneiosGuardados />}
      {aba === 'fazenda' && <RomaneiosFazendaLista />}

      {avisos.length > 0 && aba === 'gerar' && <Aviso tipo="atencao" titulo="Confira" itens={avisos} onFechar={() => setAvisos([])} />}

      <div className={aba === 'gerar' ? 'space-y-6' : 'hidden'}>

      <AreaSoltar aceita={['.xml', '.pdf']} onArquivos={processarArquivos} icone={FileUp}
                  titulo="Arraste os arquivos das notas aqui" textoBotao="Escolher arquivos"
                  dica="XML e PDF das notas; o PDF vai para Notas fiscais"
                  carregando={analisando} textoCarregando="Analisando as notas..." />

      {/* Resultado da análise: o que foi lido e o que falta responder antes de gerar */}
      {analise && (
        <section aria-labelledby="planilhas-analise" className="bg-white rounded-xl border border-gray-200 p-4 sm:p-5 space-y-5 animate-entrada">
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h2 id="planilhas-analise" className="text-lg font-semibold text-gray-900">O que veio nas notas</h2>
            <p className="text-sm text-gray-600 tabular-nums">
              <b className="text-gray-900">{analise.totalXmls}</b> XMLs · <b className="text-gray-900">{analise.totalNfes}</b> notas lidas
            </p>
          </div>

          {analise.clientesEncontrados.length > 0 ? (
            <div>
              <h3 className="text-sm font-medium text-gray-700 mb-2">Clientes encontrados</h3>
              <div className="flex flex-wrap gap-2">
                {analise.clientesEncontrados.map(c => <ClienteBadge key={c} apelido={c} />)}
              </div>
            </div>
          ) : (
            <Aviso tipo="atencao">
              Nenhum cliente configurado encontrado. Verifique se os XMLs são de NF-e dos clientes: BRZ, AURORA, CEDRO, DUNAS ou FAROL.
            </Aviso>
          )}

          {/* Números dos romaneios (sugeridos = último + 1, editáveis) */}
          {Object.keys(numeros).length > 0 && (
            <fieldset>
              <legend className="text-sm font-medium text-gray-700">Número do romaneio</legend>
              <p className="text-sm text-gray-500 mb-2">Sugerido sozinho (o último mais um). Ajuste se precisar.</p>
              <div className="grid grid-cols-2 sm:flex sm:flex-wrap gap-3">
                {Object.entries(numeros).map(([apelido, n]) => (
                  <label key={apelido} className="flex items-center justify-between gap-2 bg-gray-50 border border-gray-200 rounded-lg pl-3 pr-1.5 py-1.5">
                    <span className="text-sm font-semibold text-gray-800">{apelido}</span>
                    <input
                      type="number"
                      min={1}
                      inputMode="numeric"
                      value={n || ''}
                      onChange={e => setNumeros(prev => ({ ...prev, [apelido]: Math.max(0, parseInt(e.target.value) || 0) }))}
                      className={`${campoNumero} w-24 border-gray-300`}
                    />
                  </label>
                ))}
              </div>
            </fieldset>
          )}

          {analise.temBrz && <ProdutosCBrzCampo valor={produtosC} onChange={setProdutosC} />}

          {/* ProdutosD da Cedro: não vêm na nota, obrigatório (NFE-03) */}
          {analise.temCedro && (
            <div className={`rounded-xl px-4 py-3 flex flex-wrap items-center gap-3 border ${qtdProdutosD > 0 ? 'bg-gray-50 border-gray-200' : 'bg-amber-50 border-amber-300'}`}>
              <label htmlFor="planilhas-produtosD" className="text-sm font-medium text-gray-900">
                Cedro Frutas: caixas de produtosD
              </label>
              <input
                id="planilhas-produtosD"
                type="number"
                min={1}
                inputMode="numeric"
                value={qtdProdutosD || ''}
                placeholder="0"
                aria-describedby={qtdProdutosD <= 0 ? 'planilhas-produtosD-falta' : undefined}
                onChange={e => setQtdProdutosD(Math.max(0, parseInt(e.target.value) || 0))}
                className={`${campoNumero} w-24 ${qtdProdutosD > 0 ? 'border-gray-300' : 'border-amber-500'}`}
              />
              {qtdProdutosD <= 0 && <span id="planilhas-produtosD-falta" className="text-sm font-medium text-amber-900">Obrigatório: não vem na nota</span>}
            </div>
          )}

          <ConflitosTipoVolume conflitos={analise.conflitosTipo ?? []} tipos={analise.tiposRomaneio ?? []}
                            correcoes={correcoes} onChange={setCorrecoes} />
          <ProdutosSemTipo produtos={analise.produtosSemTipo ?? []}
                           onResolvido={p => setAnalise(a => a && { ...a, produtosSemTipo: (a.produtosSemTipo ?? []).filter(x => x.produto !== p) })} />

          {analise.temFrete && (
            <ProdutoECampo nasNotas={analise.produtosENasNotas} valor={caixasProdutoE} onChange={setCaixasProdutoE} />
          )}

          {analise.clientesEncontrados.length > 0 && (
            <div className="pt-1 border-t border-gray-100">
              <Botao variante="primario" icone={Download} onClick={gerarPlanilhas} carregando={gerando} textoCarregando="Gerando..."
                     bloco className="sm:w-auto mt-4">
                Gerar e baixar planilhas
              </Botao>
            </div>
          )}
        </section>
      )}

      {/* Referência dos clientes configurados */}
      <section aria-labelledby="planilhas-clientes" className="rounded-xl border border-gray-200 bg-white p-4">
        <h2 id="planilhas-clientes" className="text-sm font-semibold text-gray-800 mb-3">Clientes configurados</h2>
        <ul className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-sm text-gray-700">
          {CLIENTES.map(([apelido, nome]) => (
            <li key={apelido} className="flex items-center gap-2 min-w-0"><ClienteBadge apelido={apelido} /><span className="truncate">{nome}</span></li>
          ))}
        </ul>
      </section>
      </div>
      </PainelAba>
    </div>
  )
}
