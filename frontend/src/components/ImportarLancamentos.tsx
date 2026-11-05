import { useRef, useState } from 'react'
import { Upload } from 'lucide-react'
import toast from 'react-hot-toast'
import api from '../services/api'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

type Linha = { grupo: string; descricao: string; antes: string | null; depois: string | null }
type Resumo = { novas: Linha[]; mudadas: Linha[]; mantidas: Linha[]; soNoSite: Linha[]; iguais: number; avisos: string[]; ano: number }

export default function ImportarLancamentos({ caminho, titulo, onPronto }: {
  caminho: string; titulo: string; onPronto: () => void
}) {
  const entrada = useRef<HTMLInputElement>(null)
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [resumo, setResumo] = useState<Resumo | null>(null)
  const [ocupado, setOcupado] = useState(false)
  const [detalhes, setDetalhes] = useState(false)
  const enviar = async (f: File, previa: boolean) => {
    const form = new FormData(); form.append('arquivo', f)
    return (await api.post<Resumo>(`${caminho}/importar${previa ? '/previa' : ''}`, form)).data
  }
  const escolher = async (f?: File) => {
    if (!f) return
    setOcupado(true)
    try { setResumo(await enviar(f, true)); setArquivo(f); setDetalhes(false) }
    catch (e) { toast.error(erroDe(e, 'Não consegui ler a planilha.')) }
    finally { setOcupado(false) }
  }
  const gravar = async () => {
    if (!arquivo || ocupado) return
    setOcupado(true)
    try {
      const r = await enviar(arquivo, false)
      toast.success(`${r.novas.length} lançamento(s) novo(s), ${r.mudadas.length} atualizado(s).`)
      setResumo(null); setArquivo(null); onPronto()
    } catch (e) { toast.error(erroDe(e, 'Não consegui importar. Confira e tente de novo.')) }
    finally { setOcupado(false) }
  }
  const linhas = resumo ? [...resumo.novas, ...resumo.mudadas, ...resumo.mantidas] : []
  return <>
    <input ref={entrada} type="file" accept=".xlsx,.xls" className="sr-only" aria-label={`Escolher planilha de ${titulo}`}
      disabled={ocupado} onChange={e => { void escolher(e.target.files?.[0]); e.target.value = '' }} />
    <Botao variante="secundario" icone={Upload} carregando={ocupado} onClick={() => entrada.current?.click()}>Importar planilha</Botao>
    {resumo && <Modal titulo={`Importar ${titulo}`} onClose={() => { if (!ocupado) { setResumo(null); setArquivo(null) } }}>
      <p className="text-sm text-gray-700">{arquivo?.name} · Somente lançamentos de {resumo.ano}.</p>
      <p className="text-sm text-gray-900">{resumo.novas.length} novos · {resumo.mudadas.length} com alteração · {resumo.iguais} iguais.</p>
      <p className="text-sm text-gray-600">{resumo.mantidas.length} lançados na tela e {resumo.soNoSite.length} que só estão no site serão mantidos.</p>
      {resumo.avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira antes de importar" itens={resumo.avisos} />}
      {linhas.length > 0 && <>
        <Botao variante="fantasma" onClick={() => setDetalhes(d => !d)}>{detalhes ? 'Esconder detalhes' : 'Ver detalhes'}</Botao>
        {detalhes && <ul className="space-y-3 max-h-72 overflow-y-auto text-sm">
          {linhas.slice(0, 100).map((l, i) => <li key={i} className="border-b border-gray-200 pb-2 break-words">
            <p className="font-semibold text-gray-900">{l.grupo} · {l.descricao}</p>
            {l.antes != null && <p className="text-gray-600">No site: {l.antes}</p>}
            <p className="text-gray-700">Na planilha: {l.depois}</p>
          </li>)}
          {linhas.length > 100 && <li className="text-gray-600">Mostrando os primeiros 100 de {linhas.length} lançamentos; o resumo conta todos.</li>}
        </ul>}
      </>}
      <div className="flex flex-wrap justify-end gap-2 pt-2">
        <Botao variante="secundario" disabled={ocupado} onClick={() => { setResumo(null); setArquivo(null) }}>Manter como está</Botao>
        <Botao variante="primario" carregando={ocupado} disabled={resumo.novas.length + resumo.mudadas.length === 0} onClick={gravar}>
          {resumo.mudadas.length > 0 ? 'Substituir alterações e importar' : 'Importar novas linhas'}
        </Botao>
      </div>
    </Modal>}
  </>
}
