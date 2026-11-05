import { useState } from 'react'
import { FileUp } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import AreaSoltar from './ui/AreaSoltar'
import { erroDaApi } from '../services/semPararApi'
import { viagensApi, type LeituraCrlv } from '../services/viagensCaminhaoApi'

// Importar o CRLV (PDF) das placas (VIA-20, UI-21): lê vários de uma vez, mostra novas, mudanças de tipo,
// iguais e avisos, e só grava ao confirmar. Não mexe em quem cuida da placa.

export default function ImportarCrlvModal({ onClose, onFeito }: { onClose: () => void; onFeito: () => void }) {
  const [itens, setItens] = useState<LeituraCrlv[] | null>(null)
  const [lendo, setLendo] = useState(false)
  const [gravando, setGravando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [detalhes, setDetalhes] = useState(false)

  const escolher = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setErro(null); setLendo(true)
    try { setItens(await viagensApi.lerCrlv(arquivos)) }
    catch (e) { setErro(await erroDaApi(e, 'Não consegui ler os documentos.')) }
    finally { setLendo(false) }
  }

  const novas = (itens ?? []).filter(i => i.situacao === 'nova')
  const mudam = (itens ?? []).filter(i => i.situacao === 'muda')
  const iguais = (itens ?? []).filter(i => i.situacao === 'igual')
  const avisos = (itens ?? []).filter(i => i.situacao === 'aviso')
  const aGravar = [...novas, ...mudam]

  const confirmar = async () => {
    setGravando(true)
    try {
      const r = await viagensApi.aplicarCrlv(aGravar.map(i => ({ placa: i.placa!, tipo: i.tipo! })))
      toast.success(`${r.criadas} ${r.criadas === 1 ? 'placa criada' : 'placas criadas'}, ${r.mudadas} ${r.mudadas === 1 ? 'mudou' : 'mudaram'} de tipo.`)
      onFeito()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui gravar.')) }
    finally { setGravando(false) }
  }

  const rotuloTipo = (t: string | null) => t === 'Carreta' ? 'carreta' : 'cavalo'

  return (
    <Modal titulo="Importar documento (CRLV)" onClose={onClose} largura="lg" confirmarAoFechar={!!itens}>
      {!itens && (
        <>
          <p className="text-sm text-gray-600">
            Envie os PDFs do CRLV, quantos quiser. Eu leio a placa e o tipo (cavalo ou carreta) e mostro o resumo antes de gravar.
          </p>
          <AreaSoltar aceita={['.pdf']} multiplos icone={FileUp} carregando={lendo} textoCarregando="Lendo os documentos..."
                      titulo="Arraste os PDFs do CRLV aqui" onArquivos={escolher} />
          {erro && <Aviso tipo="erro">{erro}</Aviso>}
        </>
      )}

      {itens && (
        <>
          <div className="text-sm bg-gema-50 border border-gema-200 text-gray-800 rounded-lg p-3 space-y-2">
            <p className="font-medium">
              {novas.length} {novas.length === 1 ? 'placa nova' : 'placas novas'} · {mudam.length} {mudam.length === 1 ? 'muda' : 'mudam'} de tipo · {iguais.length} {iguais.length === 1 ? 'igual' : 'iguais'} ao cadastro
              {avisos.length > 0 && <> · {avisos.length} {avisos.length === 1 ? 'aviso' : 'avisos'}</>}
            </p>
            {aGravar.length === 0 && <p className="text-gray-700">Nada para gravar.</p>}
            {(detalhes ? aGravar : aGravar.slice(0, 4)).length > 0 && (
              <ul className="text-xs text-gray-700 list-disc pl-4 space-y-0.5">
                {(detalhes ? aGravar : aGravar.slice(0, 4)).map(i => (
                  <li key={i.placa}>
                    <b>{i.placa}</b>: {i.situacao === 'nova' ? `nova, ${rotuloTipo(i.tipo)}` : `${rotuloTipo(i.tipoAtual)} → ${rotuloTipo(i.tipo)}`}
                  </li>
                ))}
              </ul>
            )}
            {detalhes && iguais.length > 0 && (
              <p className="text-xs text-gray-600">Iguais: {iguais.map(i => i.placa).join(', ')}</p>
            )}
            {(aGravar.length > 4 || iguais.length > 0) && (
              <button type="button" onClick={() => setDetalhes(v => !v)} className="text-tinta-700 dark:text-tinta-300 font-medium underline">
                {detalhes ? 'Esconder detalhes' : 'Ver detalhes'}
              </button>
            )}
          </div>

          {avisos.length > 0 && (
            <Aviso tipo="atencao" titulo="Não vão ser cadastrados">
              <ul className="list-disc pl-4 space-y-0.5">
                {avisos.map((i, k) => <li key={k}><b>{i.arquivo}</b>: {i.aviso}</li>)}
              </ul>
            </Aviso>
          )}

          <div className="flex gap-2 pt-1">
            <Botao variante="primario" onClick={confirmar} carregando={gravando} textoCarregando="Gravando..."
                   disabled={aGravar.length === 0} className="flex-1">
              Gravar {aGravar.length} {aGravar.length === 1 ? 'placa' : 'placas'}
            </Botao>
            <Botao variante="fantasma" onClick={() => { setItens(null); setDetalhes(false) }}>Outros arquivos</Botao>
          </div>
        </>
      )}
    </Modal>
  )
}
