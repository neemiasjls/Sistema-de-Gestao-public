import { useState } from 'react'
import { UserCheck } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import { Segmentado } from './ui/Abas'
import { formatBRL } from '../services/api'
import {
  cadastrarDonos, erroCobranca, type PlacaSemCadastro, type SocioDono,
} from '../services/cobrancasAbastecimentoApi'

const DONOS = [['1', 'Bento'], ['2', 'Caio'], ['3', 'Davi']] as const
type Escolha = '' | (typeof DONOS)[number][0]

// A fatura trouxe placa que não está no cadastro de veículos (ABA-06). Nenhum
// abastecimento pode ficar fora da divisão: pergunta o dono de cada placa,
// grava no cadastro e a tela refaz a importação com tudo na divisão.
export default function DonosDasPlacas({ placas, onPronto, onClose }: {
  placas: PlacaSemCadastro[]
  /** Depois de gravar no cadastro: refaz a importação (ou a planilha) */
  onPronto: () => Promise<void> | void
  onClose: () => void
}) {
  const [donos, setDonos] = useState<Record<string, Escolha>>({})
  const [salvando, setSalvando] = useState(false)
  const faltam = placas.filter(p => !donos[p.placa]).length

  const gravar = async () => {
    if (faltam) { toast.error(faltam === 1 ? 'Falta escolher o dono de uma placa.' : `Falta escolher o dono de ${faltam} placas.`); return }
    setSalvando(true)
    try {
      await cadastrarDonos(placas.map(p => ({ placa: p.placa, socio: Number(donos[p.placa]) as SocioDono })))
      toast.success(placas.length === 1 ? 'Placa cadastrada.' : `${placas.length} placas cadastradas.`)
      onClose()
      await onPronto()
    } catch (e) {
      toast.error(await erroCobranca(e, 'Não consegui gravar no cadastro de veículos.'))
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal
      titulo={placas.length === 1 ? 'Placa fora do cadastro' : 'Placas fora do cadastro'}
      descricao="Diga de quem é cada caminhão. A placa entra no cadastro de veículos e a importação é feita de novo."
      onClose={onClose}
      sujo={Object.values(donos).some(Boolean)}
      rodape={
        <div className="flex flex-col-reverse sm:flex-row gap-2 sm:justify-end">
          <Botao variante="fantasma" onClick={onClose} disabled={salvando}>Cancelar</Botao>
          <Botao variante="primario" icone={UserCheck} onClick={gravar} carregando={salvando}
                 textoCarregando="Gravando..." disabled={faltam > 0}>
            Cadastrar e importar de novo
          </Botao>
        </div>
      }
    >
      <Aviso tipo="atencao">
        Nada foi guardado ainda: sem o dono, o abastecimento ficaria fora da planilha de divisão.
      </Aviso>
      <ul className="space-y-3">
        {placas.map(p => (
          <li key={p.placa} className="rounded-xl border border-gray-200 bg-white p-3 space-y-2">
            <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
              <span className="font-display text-lg font-semibold text-gray-900 tabular-nums">{p.placa}</span>
              <span className="text-sm text-gray-700 tabular-nums">
                {p.abastecimentos} {p.abastecimentos === 1 ? 'abastecimento' : 'abastecimentos'} · {formatBRL(p.valor)}
              </span>
            </div>
            <p className="text-sm text-gray-600">
              {p.motoristas.length ? <>Motorista na fatura: <b className="text-gray-800">{p.motoristas.join(', ')}</b></> : 'A fatura não traz o motorista.'}
            </p>
            <Segmentado<Escolha>
              rotulo={`Dono da placa ${p.placa}`}
              valor={donos[p.placa] ?? ''}
              opcoes={DONOS}
              onChange={v => setDonos(d => ({ ...d, [p.placa]: v }))}
            />
          </li>
        ))}
      </ul>
    </Modal>
  )
}
