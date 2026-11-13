import { Modal } from '../ModalBase'
import Botao from '../ui/Botao'
import type { Modo, ResultadoArquivo } from '../../services/stradaApi'

// Arquivo da Strada que já estava no sistema e mudou: Manter ou Substituir, com as mudanças (UI-21).
export default function ModalManterSubstituir({ itens, enviando, onClose, escolher }: {
  itens: ResultadoArquivo[]; enviando: boolean; onClose: () => void; escolher: (m: Modo) => void
}) {
  return (
    <Modal titulo="Arquivo que já está no sistema" onClose={onClose} largura="lg"
           descricao="Estes arquivos já tinham subido e agora trazem mudanças. Veja o que muda e escolha."
           rodape={
             <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
               <Botao onClick={() => escolher('manter')} carregando={enviando}>Manter como está</Botao>
               <Botao variante="primario" onClick={() => escolher('substituir')} carregando={enviando}>Substituir</Botao>
             </div>
           }>
      {itens.map((i, n) => (
        <div key={n} className="border border-gray-200 rounded-lg p-3 space-y-1">
          <p className="font-medium text-gray-900">{i.arquivo}{i.referencia ? ` (${i.referencia})` : ''}</p>
          {i.mensagem && <p className="text-sm text-gray-700">{i.mensagem}</p>}
          <details className="text-sm" open={i.mudancas.length <= 6}>
            <summary className="cursor-pointer text-gray-700">{i.mudancas.length} {i.mudancas.length === 1 ? 'mudança' : 'mudanças'}</summary>
            <ul className="mt-1 space-y-0.5 text-gray-800">
              {i.mudancas.map((m, k) => <li key={k}><span className="font-medium">{m.chave}</span>: {m.descricao}</li>)}
            </ul>
          </details>
        </div>
      ))}
    </Modal>
  )
}
