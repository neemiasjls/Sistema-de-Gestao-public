import FormTrocarSenha from '../components/FormTrocarSenha'
import { useAuth } from '../contexts/AuthContext'

// Primeiro acesso: a pessoa entrou com a senha provisória do e-mail. Nada do
// sistema aparece até ela criar a própria — a API também não responde.
export function CriarSenhaPage() {
  const { usuario, logout } = useAuth()

  return (
    <div className="min-h-screen bg-menu flex flex-col items-center justify-center p-4">
      <div className="bg-white rounded-2xl shadow-xl shadow-tinta-950/30 w-full max-w-sm p-7">
        <h1 className="font-display text-2xl font-semibold text-gray-900">
          Olá{usuario?.nome ? `, ${usuario.nome.split(' ')[0]}` : ''}
        </h1>
        <p className="text-sm text-gray-600 mt-1 mb-6">
          Crie a sua senha para começar a usar o sistema. Depois disso, a senha do e-mail deixa de valer.
        </p>
        <FormTrocarSenha provisoria />
      </div>
      <button onClick={logout} className="text-sm text-tinta-300 hover:text-white mt-6">
        Sair
      </button>
    </div>
  )
}
