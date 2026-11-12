import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Tela única "Usuários" (USU-11): pessoas, permissões, administrador e as classificações
// Usuário/Motorista. Tudo sob /api/usuario, que só o administrador acessa (conferido no banco).

export type Classificacao = 'usuario' | 'motorista' | 'portal'

export interface AreaCatalogo {
  chave: string
  nome: string
  grupo: string | null
}

export interface LoginPessoa {
  usuarioId: number
  login: string
  perfil: string
  ativo: boolean
  trocarSenha: boolean
}

export interface Pessoa {
  chave: string
  nome: string
  classificacoes: Classificacao[]
  logins: LoginPessoa[]
  ativo: boolean
  administrador: boolean
  acessoTotal: boolean
  areas: string[]
  controleViagens: boolean
  controleProdutosA: boolean
  usuarioId: number | null
  motoristaId: number | null
  motoristaAtivo: boolean | null
  email: string | null
  cnhValidade: string | null
  anotacoes: number
  avisos: string[]
}

export interface SenhaProvisoria {
  erro?: string | null
  emailEnviado: boolean
  avisoEmail?: string | null
  senhaProvisoria?: string | null
}

/** Contagens da junção do cadastro de motorista ao login de escritório. */
export interface Juncao {
  motoristaId: number
  loginExcluido?: number | null
  repassadas: Record<string, number>
  viagens?: number
  retornos?: number
  carregamentos?: number
}

export interface NovoCadastroMotorista {
  nome?: string
  login?: string
  porcentagemComissao?: number
}

export const usuariosApi = {
  areas: () => api.get('/api/usuario/areas').then(r => comoLista<AreaCatalogo>(r.data, 'áreas')),
  pessoas: () => api.get('/api/usuario/pessoas').then(r => comoLista<Pessoa>(r.data, 'pessoas')),

  criar: (nome: string, email: string, total: boolean, areas: string[]) =>
    api.post<SenhaProvisoria>('/api/usuario', { nome, email, total, areas }).then(r => r.data),

  /** "*" nunca vai na lista: acesso total só por `total`. Login motorista aceita só portal-produtosA. */
  permissoes: (id: number, total: boolean, areas: string[]) =>
    api.put<{ permissoes: string }>(`/api/usuario/${id}/permissoes`, { total, areas }).then(r => r.data),
  admin: (id: number, admin: boolean) =>
    api.put<{ admin: boolean }>(`/api/usuario/${id}/admin`, { admin }).then(r => r.data),

  /** Dá a classificação Motorista a um login de escritório: liga a um cadastro existente ou cria um novo. */
  darMotorista: (id: number, dados: { motoristaId: number } | NovoCadastroMotorista) =>
    api.post(`/api/usuario/${id}/motorista`, dados).then(r => comoObjeto<Juncao>(r.data, [], 'junção')),
  /** Tira "Motorista": desativa o cadastro (nunca apaga). */
  tirarMotorista: (id: number) =>
    api.delete<{ motoristaId: number }>(`/api/usuario/${id}/motorista`).then(r => r.data),

  /** Dá a classificação Usuário a quem é só motorista: cria o login com senha provisória por e-mail. */
  darUsuario: (motoristaId: number, email: string, total: boolean, areas: string[]) =>
    api.post<{ usuarioId: number; senha: SenhaProvisoria; juncao: Juncao }>(
      `/api/usuario/motorista/${motoristaId}/usuario`, { email, total, areas }).then(r => r.data),
  /** Tira "Usuário": desativa o login de escritório e o cadastro volta a ter login próprio de motorista. */
  tirarUsuario: (id: number) =>
    api.delete<{ motoristaId: number; loginMotorista: number | null }>(`/api/usuario/${id}/usuario`).then(r => r.data),
}
