import { useState, FormEvent } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { useAuth } from '../contexts/AuthContext'
import Botao from '../components/ui/Botao'
import ServidorAcordando from '../components/ui/ServidorAcordando'

export default function LoginPage() {
  const { login } = useAuth()
  const [email, setEmail]       = useState('')
  const [senha, setSenha]       = useState('')
  const [verSenha, setVerSenha] = useState(false)
  const [erro, setErro]         = useState<string | null>(null)
  const [loading, setLoading]   = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (!email || !senha) { setErro('Informe o e-mail (ou usuário) e a senha.'); return }
    setLoading(true)
    setErro(null)
    const err = await login(email.trim(), senha)
    if (err) setErro(err)
    setLoading(false)
  }

  return (
    <div className="min-h-screen bg-menu flex flex-col items-center justify-center p-4">
      {/* Marca sobre o fundo escuro, com a faixa da rodovia embaixo */}
      <div className="flex flex-col items-center mb-8">
        <span className="w-14 h-14 rounded-full bg-[#fff] flex items-center justify-center mb-4">
          <span className="w-7 h-7 rounded-full bg-gema-400" />
        </span>
        <h1 className="font-display text-3xl font-semibold text-white">TRX Transportes</h1>
        <p className="text-sm text-tinta-300 mt-1">Gestão operacional</p>
        <span aria-hidden className="mt-5 flex gap-2">
          {[0, 1, 2, 3].map(i => <span key={i} className="w-6 h-1 rounded-full bg-gema-400/80" />)}
        </span>
      </div>

      <div className="bg-white rounded-2xl shadow-xl shadow-tinta-950/30 w-full max-w-sm p-7">
        <form onSubmit={handleSubmit} noValidate className="space-y-4">
          <div>
            <label htmlFor="login-email" className="block text-sm font-medium text-gray-700 mb-1">
              E-mail ou usuário
            </label>
            {/* Motorista entra com o usuário (joseferraz), sem @: o campo não pode ser type=email */}
            <input
              id="login-email"
              type="text"
              inputMode="email"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
              value={email}
              onChange={e => setEmail(e.target.value)}
              placeholder="voce@email.com"
              autoFocus
              autoComplete="username"
              className="w-full border border-gray-300 rounded-lg px-3 py-2.5 text-sm
                         focus:outline-none focus:ring-2 focus:ring-tinta-500 focus:border-transparent"
            />
          </div>

          <div>
            <label htmlFor="login-senha" className="block text-sm font-medium text-gray-700 mb-1">
              Senha
            </label>
            <div className="relative">
              <input
                id="login-senha"
                type={verSenha ? 'text' : 'password'}
                value={senha}
                onChange={e => setSenha(e.target.value)}
                placeholder="••••••••"
                autoComplete="current-password"
                className="w-full border border-gray-300 rounded-lg pl-3 pr-11 py-2.5 text-sm
                           focus:outline-none focus:ring-2 focus:ring-tinta-500 focus:border-transparent"
              />
              <button
                type="button"
                onClick={() => setVerSenha(v => !v)}
                title={verSenha ? 'Ocultar senha' : 'Mostrar senha'}
                aria-label={verSenha ? 'Ocultar senha' : 'Mostrar senha'}
                className="absolute inset-y-0 right-0 w-11 flex items-center justify-center
                           text-gray-500 hover:text-gray-800"
              >
                {verSenha ? <EyeOff size={20} strokeWidth={1.8} aria-hidden /> : <Eye size={20} strokeWidth={1.8} aria-hidden />}
              </button>
            </div>
          </div>

          {erro && (
            <div role="alert" className="bg-red-50 border border-red-200 text-red-800 text-sm rounded-lg px-3 py-2">
              {erro}
            </div>
          )}

          <Botao type="submit" variante="primario" bloco carregando={loading} textoCarregando="Entrando...">
            Entrar
          </Botao>

          <p className="text-xs text-center text-gray-500">
            Esqueceu a senha? Peça uma nova ao responsável pelo sistema.
          </p>
        </form>

      </div>

      {/* A API dorme: enquanto a pessoa digita ela já está ligando (main.tsx) */}
      <div className="w-full max-w-sm mt-5 min-h-[4.5rem] text-tinta-100">
        <ServidorAcordando variante="cartao" />
      </div>

      <p className="text-xs text-center text-tinta-300 mt-2">
        CAIO JOSE MOREIRA E CIA LTDA
      </p>
    </div>
  )
}
