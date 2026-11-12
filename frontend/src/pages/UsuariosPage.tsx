import CabecalhoTela from '../components/ui/CabecalhoTela'
import Aviso from '../components/ui/Aviso'
import ListaPessoas from '../components/usuarios/ListaPessoas'
import MotoristasSecao from '../components/MotoristasSecao'
import PortalProdutosASecao from '../components/PortalProdutosASecao'
import { useAuth } from '../contexts/AuthContext'

// Tela única "Usuários" (USU-11): administrador vê todos juntos (usuários, motoristas e portal das
// produtosA), o QR e o código do portal. Quem não é administrador, mas tem a área "motoristas", vê só o
// cadastro de motoristas de antes. Criar e editar: clicar na pessoa abre o editor (Parte 10).

export default function UsuariosPage() {
  const { usuario, pode } = useAuth()

  if (!usuario?.dono) {
    if (!pode('motoristas')) return <Aviso tipo="atencao">Você não tem acesso a esta tela.</Aviso>
    return (
      <div className="space-y-5 max-w-6xl">
        <CabecalhoTela descricao="Cadastro dos motoristas com os dados da CNH. Importe o PDF da CNH digital para criar um motorista novo ou atualizar o que já existe. A validade vencida fica em vermelho e a que vence em até 30 dias, em aviso." />
        <MotoristasSecao />
      </div>
    )
  }

  return (
    <div className="space-y-6 max-w-6xl">
      <CabecalhoTela descricao="Todos que entram no sistema, numa lista só: usuários do escritório, motoristas e o portal das produtosA." />

      <ListaPessoas />

      <PortalProdutosASecao />
    </div>
  )
}
