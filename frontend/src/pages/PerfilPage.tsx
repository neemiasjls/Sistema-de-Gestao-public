import Botao from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import Campo from '../components/ui/Campo'
import { classeCampo } from '../components/EstoquePecas'
import { useState, FormEvent } from 'react'
import toast from 'react-hot-toast'
import axios from 'axios'
import { KeyRound } from 'lucide-react'
import { authApi } from '../services/api'
import { useAuth } from '../contexts/AuthContext'
import EscolhaTema from '../components/EscolhaTema'
import FormTrocarSenha from '../components/FormTrocarSenha'
import UsuariosSecao from '../components/UsuariosSecao'
import PortalProdutosASecao from '../components/PortalProdutosASecao'
import SecaoRecolhivel from '../components/SecaoRecolhivel'

const emailReal = (e: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)

function Secao({ titulo, descricao, children }: { titulo: string; descricao?: string; children: React.ReactNode }) {
  return (
    <section className="bg-white border border-gray-200 rounded-xl p-4 sm:p-5">
      <h2 className="text-lg font-semibold text-gray-900">{titulo}</h2>
      {descricao && <p className="text-sm text-gray-600 mt-0.5">{descricao}</p>}
      <div className="mt-4">{children}</div>
    </section>
  )
}

function MeusDados() {
  const { usuario, aplicarSessao } = useAuth()
  const [nome, setNome]   = useState(usuario?.nome ?? '')
  const [email, setEmail] = useState(usuario && emailReal(usuario.email) ? usuario.email : '')
  const [senha, setSenha] = useState('')
  const [salvando, setSalvando] = useState(false)

  const mudou = nome.trim() !== usuario?.nome || email.trim().toLowerCase() !== usuario?.email

  async function salvar(e: FormEvent) {
    e.preventDefault()
    if (!senha) { toast.error('Digite sua senha atual para salvar.'); return }
    setSalvando(true)
    try {
      const r = await authApi.atualizarPerfil({ nome: nome.trim(), email: email.trim(), senhaAtual: senha })
      aplicarSessao(r.data.token, r.data.usuario)
      setSenha('')
      toast.success('Dados salvos. Seu login agora é ' + r.data.usuario.email + '.')
    } catch (err) {
      toast.error(axios.isAxiosError(err) ? (err.response?.data as { erro?: string })?.erro ?? 'Não consegui salvar.' : 'Não consegui salvar.')
    } finally {
      setSalvando(false)
    }
  }

  return (
    <form onSubmit={salvar} className="space-y-3">
      {usuario && !emailReal(usuario.email) && (
        <Aviso tipo="info">Você ainda entra com <b>{usuario.email}</b>. Cadastre seu e-mail real: ele passa a ser o seu login.</Aviso>
      )}
      <div className="grid sm:grid-cols-2 gap-3">
        <Campo rotulo="Nome">
          <input value={nome} onChange={e => setNome(e.target.value)} className={classeCampo} autoComplete="name" />
        </Campo>
        <Campo rotulo="E-mail (é o seu login)">
          <input type="email" value={email} onChange={e => setEmail(e.target.value)} className={classeCampo}
                 placeholder="voce@email.com" autoComplete="email" />
        </Campo>
      </div>
      {mudou && (
        <div className="grid sm:grid-cols-2 gap-3 items-end animate-entrada">
          <Campo rotulo="Senha atual, para confirmar">
            <input type="password" value={senha} onChange={e => setSenha(e.target.value)} className={classeCampo}
                   autoComplete="current-password" />
          </Campo>
          <Botao type="submit" variante="primario" carregando={salvando} textoCarregando="Salvando...">Salvar dados</Botao>
        </div>
      )}
    </form>
  )
}


export default function PerfilPage() {
  const { usuario } = useAuth()
  const [senhaAberta, setSenhaAberta] = useState(false)

  return (
    <div className="max-w-3xl space-y-5">
      <div className="flex items-center gap-4">
        <div className="w-14 h-14 rounded-full bg-tinta-700 text-gema-300 flex items-center justify-center text-2xl font-semibold font-display shrink-0">
          {usuario?.nome.charAt(0).toUpperCase()}
        </div>
        <div className="min-w-0">
          <h2 className="font-display text-2xl font-semibold text-gray-900 truncate">{usuario?.nome}</h2>
          <p className="text-sm text-gray-500">{usuario?.dono ? 'Dono do sistema' : 'Usuário'}</p>
        </div>
      </div>

      <Secao titulo="Meus dados">
        <MeusDados />
      </Secao>

      <Secao titulo="Aparência" descricao="A cor da tela neste aparelho. Fica guardada neste navegador.">
        <EscolhaTema />
      </Secao>

      <SecaoRecolhivel titulo="Mudar senha" descricao="Senha atual e a nova" icone={KeyRound}
                       aberto={senhaAberta} onAlternar={setSenhaAberta}>
        <div className="max-w-sm">
          <FormTrocarSenha provisoria={false} onConcluido={() => { toast.success('Senha trocada.'); setSenhaAberta(false) }} />
        </div>
      </SecaoRecolhivel>

      {usuario?.dono && <UsuariosSecao />}

      {usuario?.dono && <PortalProdutosASecao />}

      {/* Cadastro dos motoristas, logo abaixo dos usuários: escritório e dono */}
    </div>
  )
}
