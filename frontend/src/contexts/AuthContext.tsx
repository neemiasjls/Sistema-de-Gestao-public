import { createContext, useContext, useState, useEffect, ReactNode } from 'react'
import axios from 'axios'
import api, { authApi, type UsuarioResumo } from '../services/api'

interface AuthContextData {
  usuario: UsuarioResumo | null
  token: string | null
  loading: boolean
  login: (email: string, senha: string) => Promise<string | null>
  // Depois de trocar a senha a API devolve um token novo, sem a marca de
  // senha provisória; é por aqui que ele entra no lugar do antigo.
  aplicarSessao: (token: string, usuario: UsuarioResumo) => void
  logout: () => void
}

const AuthContext = createContext<AuthContextData>({} as AuthContextData)

const TOKEN_KEY = 'trx_token'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<UsuarioResumo | null>(null)
  const [token, setToken]     = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  // Restaura sessão ao carregar
  useEffect(() => {
    const stored = localStorage.getItem(TOKEN_KEY)
    if (stored) {
      setToken(stored)
      // Valida token buscando dados do usuário (api.ts já injeta o header via interceptor).
      // Só sai do login se a API disser que o token não vale (401/403). Falha de
      // rede ou 5xx (deploy no meio, API acordando) tenta de novo algumas vezes
      // antes de desistir: antes, um 502 da Render deslogava à toa.
      let cancelado = false
      const esperas = [2000, 5000, 10000]
      const tentar = (n: number) => {
        api.get<UsuarioResumo>('/api/auth/me')
          .then(r => { if (!cancelado) { setUsuario(r.data); setLoading(false) } })
          .catch(err => {
            if (cancelado) return
            const status = axios.isAxiosError(err) ? err.response?.status : undefined
            if (status !== 401 && status !== 403 && n < esperas.length) {
              window.setTimeout(() => tentar(n + 1), esperas[n])
              return
            }
            localStorage.removeItem(TOKEN_KEY)
            setToken(null)
            setLoading(false)
          })
      }
      tentar(0)
      return () => { cancelado = true }
    } else {
      setLoading(false)
    }
  }, [])

  function aplicarSessao(t: string, u: UsuarioResumo) {
    localStorage.setItem(TOKEN_KEY, t)
    setToken(t)
    setUsuario(u)
  }

  async function login(email: string, senha: string): Promise<string | null> {
    try {
      const res = await authApi.login(email, senha)
      aplicarSessao(res.data.token, res.data.usuario)
      return null
    } catch (err: unknown) {
      if (axios.isAxiosError(err)) {
        return (err.response?.data as { erro?: string })?.erro ?? 'Erro ao fazer login.'
      }
      return 'Erro ao conectar ao servidor.'
    }
  }

  function logout() {
    localStorage.removeItem(TOKEN_KEY)
    setToken(null)
    setUsuario(null)
  }

  return (
    <AuthContext.Provider value={{ usuario, token, loading, login, aplicarSessao, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  return useContext(AuthContext)
}
