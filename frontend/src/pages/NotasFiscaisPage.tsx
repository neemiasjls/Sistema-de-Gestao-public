import { Download, FileUp, FolderArchive, Search } from 'lucide-react'
import { comoObjeto } from '../utils/resposta'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import Botao from '../components/ui/Botao'
import { Segmentado } from '../components/ui/Abas'
import { classeCampo } from '../components/EstoquePecas'
import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import api, { downloadBlob, nomeDoArquivo } from '../services/api'
import { brl, dataBr } from '../utils/formato'

type Nota = {
  id: number
  chaveAcesso: string
  numeroNF: string
  dataEmissao: string | null
  emitente: string
  destinatario: string
  cidadeDestino: string
  uf: string
  valorNF: number
  nomeXml: string
  nomePdf: string | null
  temPdf: boolean
}

export default function NotasFiscaisPage() {
  const [notas, setNotas]       = useState<Nota[]>([])
  const [total, setTotal]       = useState(0)
  const [busca, setBusca]       = useState('')
  const [semPdf, setSemPdf]     = useState(false)
  const [carregando, setCarregando] = useState(true)
  const [enviando, setEnviando] = useState(false)
  const [falhou, setFalhou] = useState(false)
  // Avisos da última leitura (nota repetida, PDF sem XML...): ficam até fechar (D-07)
  const [avisos, setAvisos] = useState<string[]>([])

  const carregar = useCallback(async (termo: string, soSemPdf: boolean) => {
    setCarregando(true)
    try {
      const r = await api.get('/api/nfe-arquivos', {
        params: { busca: termo || undefined, semPdf: soSemPdf || undefined },
      })
      const d = comoObjeto<{ total: number; itens: Nota[] }>(r.data, ['itens'], 'notas')
      setNotas(d.itens)
      setTotal(d.total ?? d.itens.length)
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally {
      setCarregando(false)
    }
  }, [])

  useEffect(() => {
    const t = setTimeout(() => carregar(busca, semPdf), 300)
    return () => clearTimeout(t)
  }, [busca, semPdf, carregar])

  const enviar = async (validos: File[]) => {
    if (!validos.length) return
    setEnviando(true)
    try {
      const fd = new FormData()
      validos.forEach(f => fd.append('arquivos', f))
      const r = await api.post<{ novos: number; atualizados: number; pdfs: number; avisos: string[] }>(
        '/api/nfe-arquivos', fd, { headers: { 'Content-Type': 'multipart/form-data' } })

      const { novos, atualizados, pdfs, avisos } = r.data
      const partes = []
      if (novos > 0) partes.push(`${novos} nota(s) nova(s)`)
      if (atualizados > 0) partes.push(`${atualizados} atualizada(s)`)
      if (pdfs > 0) partes.push(`${pdfs} PDF(s) vinculado(s)`)
      toast.success(partes.length ? partes.join(' · ') : 'Nada novo para guardar.')

      setAvisos(avisos ?? [])
      await carregar(busca, semPdf)
    } catch (e: unknown) {
      const err = e as { response?: { data?: { erro?: string } } }
      toast.error(err.response?.data?.erro ?? 'Não consegui guardar os arquivos.')
    } finally {
      setEnviando(false)
    }
  }

  const baixar = async (n: Nota, tipo: 'xml' | 'pdf') => {
    try {
      const r = await api.get(`/api/nfe-arquivos/${n.id}/${tipo}`, { responseType: 'blob' })
      const padrao = tipo === 'xml' ? n.nomeXml : (n.nomePdf ?? `NF-${n.numeroNF}.pdf`)
      downloadBlob(r.data as Blob,
        nomeDoArquivo(r.headers['content-disposition'] as string | undefined, padrao))
    } catch {
      toast.error(`Não consegui baixar o ${tipo.toUpperCase()}.`)
    }
  }

  const semPdfCount = notas.filter(n => !n.temPdf).length

  return (
    <div className="space-y-5 max-w-5xl">
      <CabecalhoTela descricao="O XML e o DANFE de cada nota, guardados para baixar quando precisar." />

      <AreaSoltar aceita={['.xml', '.pdf']} onArquivos={enviar} icone={FileUp}
                  titulo="Arraste os arquivos das notas aqui" textoBotao="Escolher arquivos"
                  dica="O PDF encontra a nota dele pelo número"
                  carregando={enviando} textoCarregando="Guardando as notas..." />

      {avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira" itens={avisos} onFechar={() => setAvisos([])} />}

      {/* Busca */}
      <div className="flex flex-col sm:flex-row sm:items-center gap-3">
        <div className="relative flex-1 min-w-0">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500 pointer-events-none" aria-hidden />
          <input value={busca} onChange={e => setBusca(e.target.value)} type="search"
                 placeholder="Número da NF, chave, emitente ou destinatário" aria-label="Buscar notas"
                 className={`${classeCampo} pl-9`} />
        </div>
        <Segmentado rotulo="Mostrar" valor={semPdf ? 'sem' : 'todas'} onChange={v => setSemPdf(v === 'sem')}
                    opcoes={[['todas', 'Todas'], ['sem', 'Só sem PDF']]} />
      </div>

      <p className="text-sm text-gray-600 tabular-nums" aria-live="polite">
        {carregando ? 'Buscando...' : falhou ? '' : (
          <>
            <b className="text-gray-900">{total}</b> {total === 1 ? 'nota guardada' : 'notas guardadas'}
            {notas.length < total && <> · mostrando as {notas.length} mais recentes</>}
            {!semPdf && semPdfCount > 0 && (
              <span className="text-amber-800 font-medium"> · {semPdfCount} sem PDF nesta lista</span>
            )}
          </>
        )}
      </p>

      {/* Lista */}
      {falhou ? (
        <ErroCarga mensagem="Não consegui carregar as notas." onTentar={() => carregar(busca, semPdf)} />
      ) : carregando && notas.length === 0 ? (
        <Carregando linhas={4} rotulo="Carregando as notas" />
      ) : !carregando && notas.length === 0 ? (
        <Vazio icone={FolderArchive} titulo={busca || semPdf ? 'Nada encontrado com esse filtro' : 'Nenhuma nota guardada ainda'}>
          {busca || semPdf ? 'Tente outro número, nome ou tire o filtro.' : 'Arraste os XMLs e os PDFs das notas no quadro acima.'}
        </Vazio>
      ) : (
        <ul className={`bg-white border border-gray-200 rounded-xl divide-y divide-gray-100 overflow-hidden transition-opacity ${carregando ? 'opacity-60' : ''}`}>
          {notas.map(n => (
            <li key={n.id} className="px-4 py-3 flex flex-col sm:flex-row sm:items-center gap-3">
              <div className="min-w-0 flex-1">
                <div className="flex items-baseline gap-x-3 gap-y-1 flex-wrap">
                  <span className="font-display text-lg font-semibold text-gray-900 tabular-nums leading-tight">NF {n.numeroNF}</span>
                  <span className="text-sm text-gray-500 tabular-nums">{dataBr(n.dataEmissao ?? '') || 'sem data'}</span>
                  <span className="text-sm font-semibold text-gray-900 tabular-nums sm:ml-auto">{brl(n.valorNF)}</span>
                  {!n.temPdf && (
                    <span className="text-xs font-medium bg-amber-50 text-amber-900 border border-amber-200 rounded-full px-2 py-0.5">sem PDF</span>
                  )}
                </div>
                <p className="text-sm text-gray-800 mt-0.5 truncate">{n.destinatario}</p>
                <p className="text-sm text-gray-500 truncate">
                  {n.emitente} para {n.cidadeDestino}{n.uf ? `/${n.uf}` : ''}
                </p>
              </div>

              <div className="flex gap-2 shrink-0 [&>*]:flex-1 sm:[&>*]:flex-none">
                <Botao variante="secundario" tamanho="sm" icone={Download} onClick={() => baixar(n, 'xml')} aria-label={`Baixar o XML da nota ${n.numeroNF}`}>XML</Botao>
                <Botao variante="secundario" tamanho="sm" icone={Download} onClick={() => baixar(n, 'pdf')} disabled={!n.temPdf}
                       aria-label={`Baixar o PDF da nota ${n.numeroNF}`} title={n.temPdf ? undefined : 'Esta nota ainda não tem PDF guardado'}>PDF</Botao>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
