import CabecalhoTela from '../components/ui/CabecalhoTela'
import MotoristasSecao from '../components/MotoristasSecao'

// Cadastro de motoristas (VIA-20): dados da CNH, importação do PDF da CNH digital,
// acesso ao painel do celular. Antes era a aba Motoristas de Acerto viagens.

export default function MotoristasPage() {
  return (
    <div className="space-y-5 max-w-6xl">
      <CabecalhoTela descricao="Cadastro dos motoristas com os dados da CNH. Importe o PDF da CNH digital para criar um motorista novo ou atualizar o que já existe. A validade vencida fica em vermelho e a que vence em até 30 dias, em aviso." />
      <MotoristasSecao />
    </div>
  )
}
