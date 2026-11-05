import { useState, FormEvent } from 'react'
import axios from 'axios'
import { Check, Circle, Eye, EyeOff } from 'lucide-react'
import { authApi } from '../services/api'
import { useAuth } from '../contexts/AuthContext'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'

// As mesmas regras da API (AuthService.ValidarSenhaNova). Mostradas enquanto
// a pessoa digita, para ela não descobrir a regra só no erro.
const regras = [
  { texto: 'Pelo menos 8 caracteres', ok: (s: string) => s.length >= 8 },
  { texto: 'Uma letra',               ok: (s: string) => /\p{L}/u.test(s) },
  { texto: 'Um número',               ok: (s: string) => /\d/.test(s) },
]

const campo = 'w-full min-h-11 rounded-lg border border-gray-300 bg-white px-3 text-sm text-gray-900 transition-colors hover:border-gray-400 focus:border-tinta-500'

export default function FormTrocarSenha({ provisoria, onConcluido }: {
  provisoria: boolean
  onConcluido?: () => void
}) {
  const { aplicarSessao } = useAuth()
  const [atual, setAtual]         = useState('')
  const [nova, setNova]           = useState('')
  const [confirma, setConfirma]   = useState('')
  const [verSenhas, setVerSenhas] = useState(false)
  const [erro, setErro]           = useState<string | null>(null)
  const [salvando, setSalvando]   = useState(false)

  const regrasOk = regras.every(r => r.ok(nova))
  const confere  = nova.length > 0 && nova === confirma

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (!atual) { setErro(provisoria ? 'Cole a senha provisória do e-mail.' : 'Informe a senha atual.'); return }
    if (!regrasOk) { setErro('A senha nova ainda não cumpre as regras abaixo.'); return }
    if (!confere) { setErro('A confirmação está diferente da senha nova.'); return }

    setSalvando(true)
    setErro(null)
    try {
      const r = await authApi.trocarSenha(atual, nova)
      aplicarSessao(r.data.token, r.data.usuario)
      setAtual(''); setNova(''); setConfirma('')
      onConcluido?.()
    } catch (err: unknown) {
      setErro(axios.isAxiosError(err)
        ? (err.response?.data as { erro?: string })?.erro ?? 'Não consegui trocar a senha.'
        : 'Não consegui falar com o servidor.')
    } finally {
      setSalvando(false)
    }
  }

  const tipo = verSenhas ? 'text' : 'password'

  return (
    <form onSubmit={enviar} className="space-y-4">
      <div>
        <label htmlFor="senha-atual" className="block text-sm font-medium text-gray-700 mb-1">
          {provisoria ? 'Senha provisória (do e-mail)' : 'Senha atual'}
        </label>
        <input id="senha-atual" type={tipo} value={atual} onChange={e => setAtual(e.target.value)}
               autoComplete="current-password" autoFocus className={campo} />
      </div>

      <div>
        <div className="flex items-center justify-between mb-1">
          <label htmlFor="senha-nova" className="block text-sm font-medium text-gray-700">Senha nova</label>
          <button type="button" onClick={() => setVerSenhas(v => !v)}
                  aria-pressed={verSenhas}
                  className="flex items-center gap-1 min-h-11 sm:min-h-8 -my-2 px-1 text-sm text-gray-600 hover:text-gray-900">
            {verSenhas ? <EyeOff size={15} aria-hidden /> : <Eye size={15} aria-hidden />}
            {verSenhas ? 'Ocultar' : 'Mostrar'}
          </button>
        </div>
        <input id="senha-nova" aria-describedby="senha-regras" type={tipo} value={nova} onChange={e => setNova(e.target.value)}
               autoComplete="new-password" className={campo} />
        <ul id="senha-regras" className="mt-2 space-y-1">
          {regras.map(r => {
            const ok = r.ok(nova)
            return (
              <li key={r.texto} className={`flex items-center gap-2 text-sm ${ok ? 'text-emerald-800' : 'text-gray-500'}`}>
                {ok ? <Check size={15} aria-hidden /> : <Circle size={10} className="mx-0.5" aria-hidden />}
                {r.texto}<span className="sr-only">{ok ? ': cumprida' : ': falta'}</span>
              </li>
            )
          })}
        </ul>
      </div>

      <div>
        <label htmlFor="senha-repetida" className="block text-sm font-medium text-gray-700 mb-1">Repita a senha nova</label>
        <input id="senha-repetida" type={tipo} value={confirma} onChange={e => setConfirma(e.target.value)}
               autoComplete="new-password" className={campo}
               aria-invalid={confirma.length > 0 && !confere ? true : undefined} />
        {confirma.length > 0 && !confere && (
          <p className="text-sm font-medium text-red-700 mt-1">Está diferente da senha nova.</p>
        )}
      </div>

      {erro && <Aviso tipo="erro">{erro}</Aviso>}

      <Botao type="submit" variante="primario" bloco carregando={salvando} textoCarregando="Salvando...">
        {provisoria ? 'Criar minha senha e entrar' : 'Trocar senha'}
      </Botao>
    </form>
  )
}
