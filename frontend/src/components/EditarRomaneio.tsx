import { useState } from 'react'
import toast from 'react-hot-toast'
import { FileUp } from 'lucide-react'
import { Modal, BotoesModal, Campo } from './ModalBase'
import { romaneiosClienteApi } from '../services/romaneiosClienteApi'

// Editar um romaneio guardado (Admin, 02/11/2026): o número (vive no nome do
// arquivo, "181--AURORA-02-11-2026.xlsx") e, se quiser, trocar a planilha por
// uma acertada no Excel. Usado na aba Romaneios e nas cargas de Calcular frete.

const erroDe = (e: unknown, padrao: string) =>
  (e as { response?: { data?: { erro?: string } } })?.response?.data?.erro ?? padrao

export default function EditarRomaneio({ id, nome, onClose, onSalvo }: {
  id: number; nome: string; onClose: () => void; onSalvo: (nomeNovo: string) => void
}) {
  const ehTexto = nome.toLowerCase().endsWith('.txt')
  const m = /^(\d+)--/.exec(nome)
  const numeroAtual = m ? m[1] : ''
  const [numero, setNumero] = useState(numeroAtual)
  const [arquivo, setArquivo] = useState<File | null>(null)
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const mudouNumero = !ehTexto && numero.trim() !== numeroAtual
  const sujo = mudouNumero || arquivo != null

  const salvar = async () => {
    const n = parseInt(numero, 10)
    if (mudouNumero && !(n > 0)) { setErro('Informe o número do romaneio.'); return }
    if (!sujo) { onClose(); return }
    setSalvando(true)
    try {
      const { data } = await romaneiosClienteApi.editar(id, { numero: mudouNumero ? n : undefined, arquivo })
      toast.success(`Romaneio salvo: ${data.nome}`)
      onSalvo(data.nome)
      onClose()
    } catch (e) { toast.error(erroDe(e, 'Não consegui salvar o romaneio.')) }
    finally { setSalvando(false) }
  }

  return (
    <Modal onClose={onClose} titulo="Editar romaneio" descricao={<span className="break-all">{nome}</span>} sujo={sujo}
           rodape={<BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />}>
      <div className="space-y-4">
        {!ehTexto && (
          <Campo rotulo="Número do romaneio" erro={erro}
                 dica={numeroAtual ? `Hoje: ${numeroAtual}. O próximo romaneio do cliente segue deste número.` : 'Este romaneio está sem número.'}>
            <input value={numero} inputMode="numeric" autoFocus
                   onChange={e => { setNumero(e.target.value.replace(/\D/g, '')); setErro(null) }}
                   onKeyDown={e => { if (e.key === 'Enter') salvar() }}
                   className="w-40 min-h-11 sm:min-h-9 border border-gray-300 rounded-lg px-3 text-sm tabular-nums" />
          </Campo>
        )}
        <Campo rotulo={ehTexto ? 'Trocar o arquivo (.txt)' : 'Trocar a planilha (.xlsx acertada no Excel)'}
               dica={arquivo ? arquivo.name : 'Opcional. Fica no lugar da que está guardada.'}>
          <label className="inline-flex items-center gap-2 px-3 min-h-11 sm:min-h-9 border border-gray-300 text-sm font-medium text-gray-700 rounded-lg cursor-pointer hover:bg-gray-50">
            <FileUp size={15} aria-hidden /> {arquivo ? 'Escolher outro arquivo' : 'Escolher arquivo'}
            <input type="file" hidden accept={ehTexto ? '.txt' : '.xlsx'}
                   onChange={e => { setArquivo(e.target.files?.[0] ?? null); e.target.value = '' }} />
          </label>
        </Campo>
      </div>
    </Modal>
  )
}
