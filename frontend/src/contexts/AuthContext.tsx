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
  /** Administrador ou "*": o sistema inteiro (USU-11). Os outros só veem os painéis. */
  acessoTotal: boolean
  /** A área está liberada? "*" abre tudo menos o painel do motorista, que precisa vir escrito. */
  pode: (area: string) => boolean
}

// Áreas especiais do site (não são chaves do catálogo do servidor)
export const AREA_ADMIN = 'admin'   // só o administrador (Dono)
export const AREA_LIVRE = 'livre'   // qualquer um logado (o próprio perfil)
export const PAINEL_MOTORISTA = 'painel-motorista'
export const PORTAL_PRODUTOSA = 'portal-produtosA'

/** Áreas do usuário. Sem a lista (API antiga), cai no que o perfil dava antes. */
export function areasDo(u: UsuarioResumo | null): string[] {
  if (!u) return []
  if (Array.isArray(u.areas)) return u.areas
  if (u.perfil === 'motorista') return [PAINEL_MOTORISTA]
  if (u.perfil === 'produtosA') return [PORTAL_PRODUTOSA]
  return ['*']
}

export function temAcessoTotal(u: UsuarioResumo | null): boolean {
  if (!u) return false
  const perfil = u.perfil ?? ''
  if (perfil !== '') return false
  return u.dono || areasDo(u).includes('*')
}

export function podeArea(u: UsuarioResumo | null, area: string): boolean {
  if (!u) return false
  if (area === AREA_LIVRE) return true
  if (area === AREA_ADMIN) return !!u.dono && (u.perfil ?? '') === ''
  const areas = areasDo(u)
  if (area === PAINEL_MOTORISTA) return areas.includes(PAINEL_MOTORISTA)
  return areas.includes(area) || ((u.dono || areas.includes('*')) && (u.perfil ?? '') === '')
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
    <AuthContext.Provider value={{
      usuario, token, loading, login, aplicarSessao, logout,
      acessoTotal: temAcessoTotal(usuario),
      pode: (area: string) => podeArea(usuario, area),
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth() {
  return useContext(AuthContext)
}
