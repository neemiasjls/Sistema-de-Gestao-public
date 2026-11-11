import { useEffect, useState, type FormEvent, type ReactNode } from 'react'
import axios from 'axios'
import { Check, Circle, Eye, EyeOff } from 'lucide-react'
import { useAuth } from '../contexts/AuthContext'
import { portalProdutosAApi } from '../services/api'
import Botao from '../components/ui/Botao'
import Aviso from '../components/ui/Aviso'
import { Segmentado } from '../components/ui/Abas'
import ServidorAcordando from '../components/ui/ServidorAcordando'

// Entrada pública do portal das produtosA (EST-09, USU-05). O QR do barracão abre
// /produtosA?c=<código>: o código fica guardado nesta aba e sai da barra de endereço.
// Criar conta só com o código válido; depois do cadastro já entra direto no portal.

const CHAVE_CODIGO = 'trx_produtosA_codigo'
const CHAVE_JA_ENTROU = 'trx_produtosA_ja_entrou'
const CHAVE_ENCERRADA = 'trx_produtosA_sessao_encerrada'

// As mesmas regras da API (AuthService.ValidarSenhaNova)
const regras = [
  { texto: 'Pelo menos 8 caracteres', ok: (s: string) => s.length >= 8 },
  { texto: 'Uma letra',               ok: (s: string) => /\p{L}/u.test(s) },
  { texto: 'Um número',               ok: (s: string) => /\d/.test(s) },
]

// 16 px no celular: o iPhone não dá zoom ao tocar no campo (UI-05)
const campo = 'w-full min-h-12 rounded-lg border border-gray-300 bg-white px-3 text-base text-gray-900 hover:border-gray-400 focus:outline-none focus:ring-2 focus:ring-tinta-500 focus:border-transparent'

const ler = (k: string) => { try { return sessionStorage.getItem(k) } catch { return null } }
const lerLocal = (k: string) => { try { return localStorage.getItem(k) } catch { return null } }

function erroDa(err: unknown, padrao: string) {
  if (axios.isAxiosError(err)) {
    if (!err.response) return 'Sem conexão. Confira a internet e tente de novo.'
    return (err.response.data as { erro?: string } | undefined)?.erro ?? padrao
  }
  return padrao
}

type Aba = 'entrar' | 'criar'
type EstadoCodigo = 'sem' | 'conferindo' | 'valido' | 'invalido' | 'erro'

export default function PortalEntradaPage() {
  const [codigo] = useState<string | null>(() => {
    const daUrl = new URLSearchParams(window.location.search).get('c')?.trim() || null
    if (daUrl) { try { sessionStorage.setItem(CHAVE_CODIGO, daUrl) } catch { /* sem armazenamento */ } }
    return daUrl ?? ler(CHAVE_CODIGO)
  })
  const [estado, setEstado] = useState<EstadoCodigo>(codigo ? 'conferindo' : 'sem')
  const [aba, setAba] = useState<Aba>(() => (codigo && !lerLocal(CHAVE_JA_ENTROU) ? 'criar' : 'entrar'))
  const [encerrada] = useState(() => {
    const v = ler(CHAVE_ENCERRADA) === '1'
    try { sessionStorage.removeItem(CHAVE_ENCERRADA) } catch { /* sem armazenamento */ }
    return v
  })

  // Tira o código da barra (não fica no histórico nem num print da tela)
  useEffect(() => {
    if (new URLSearchParams(window.location.search).has('c'))
      window.history.replaceState(window.history.state, '', window.location.pathname)
  }, [])

  function conferir() {
    if (!codigo) return
    setEstado('conferindo')
    portalProdutosAApi.codigoValido(codigo)
      .then(r => setEstado(r.data.valido ? 'valido' : 'invalido'))
      .catch(() => setEstado('erro'))
  }
  useEffect(conferir, [codigo])

  return (
    <div className="h-tela overflow-y-auto overflow-x-hidden bg-menu flex flex-col items-center px-4 py-8">
      <div className="flex flex-col items-center mb-6">
        <span className="w-12 h-12 rounded-full bg-[#fff] flex items-center justify-center mb-3">
          <span className="w-6 h-6 rounded-full bg-gema-400" />
        </span>
        <h1 className="font-display text-3xl font-semibold text-white">ProdutosA</h1>
        <p className="text-sm text-tinta-300 mt-1">Anotação das retiradas do barracão</p>
      </div>

      <div className="bg-white rounded-2xl shadow-xl shadow-tinta-950/30 w-full max-w-sm p-5 sm:p-7">
        {encerrada && (
          <Aviso tipo="atencao" className="mb-4">Sua sessão terminou ou a conta foi desativada. Entre de novo.</Aviso>
        )}
        <div className="flex justify-center mb-5">
          <Segmentado<Aba> valor={aba} onChange={setAba} rotulo="Entrar ou criar conta"
                           opcoes={[['entrar', 'Entrar'], ['criar', 'Criar minha conta']]} />
        </div>
        {aba === 'entrar'
          ? <Entrar />
          : <Criar codigo={codigo} estado={estado} onTentar={conferir} />}
      </div>

      <div className="w-full max-w-sm mt-5 min-h-[4.5rem] text-tinta-100">
        <ServidorAcordando variante="cartao" />
      </div>
    </div>
  )
}

function CampoSenha({ id, rotulo, valor, onChange, ver, onVer, autoComplete, descrito }: {
  id: string; rotulo: string; valor: string; onChange: (v: string) => void
  ver: boolean; onVer?: () => void; autoComplete: string; descrito?: string
}) {
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700 mb-1">{rotulo}</label>
      <div className="relative">
        <input id={id} type={ver ? 'text' : 'password'} value={valor} onChange={e => onChange(e.target.value)}
               autoComplete={autoComplete} aria-describedby={descrito} className={`${campo} pr-12`} />
        {onVer && (
          <button type="button" onClick={onVer} aria-label={ver ? 'Ocultar senha' : 'Mostrar senha'}
                  className="absolute inset-y-0 right-0 w-12 flex items-center justify-center text-gray-500 hover:text-gray-800">
            {ver ? <EyeOff size={20} aria-hidden /> : <Eye size={20} aria-hidden />}
          </button>
        )}
      </div>
    </div>
  )
}

function CampoTexto({ id, rotulo, children }: { id: string; rotulo: string; children: ReactNode }) {
  return (
    <div>
      <label htmlFor={id} className="block text-sm font-medium text-gray-700 mb-1">{rotulo}</label>
      {children}
    </div>
  )
}

function Entrar() {
  const { login } = useAuth()
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [ver, setVer] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [entrando, setEntrando] = useState(false)
  const [esqueci, setEsqueci] = useState(false)

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (!email.trim() || !senha) { setErro('Informe o e-mail e a senha.'); return }
    setEntrando(true); setErro(null)
    const err = await login(email.trim(), senha)
    if (err) { setErro(err); setEntrando(false); return }
    try { localStorage.setItem(CHAVE_JA_ENTROU, '1') } catch { /* sem armazenamento */ }
  }

  if (esqueci) return <EsqueciSenha emailInicial={email} onVoltar={() => setEsqueci(false)} />

  return (
    <form onSubmit={enviar} noValidate className="space-y-4">
      <CampoTexto id="portal-email" rotulo="E-mail">
        <input id="portal-email" type="email" inputMode="email" autoCapitalize="none" autoCorrect="off" spellCheck={false}
               autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} className={campo} />
      </CampoTexto>
      <CampoSenha id="portal-senha" rotulo="Senha" valor={senha} onChange={setSenha} ver={ver} onVer={() => setVer(v => !v)}
                  autoComplete="current-password" />
      {erro && <Aviso tipo="erro">{erro}</Aviso>}
      <Botao type="submit" variante="primario" bloco carregando={entrando} textoCarregando="Entrando..." className="min-h-12">
        Entrar
      </Botao>
      <button type="button" onClick={() => setEsqueci(true)}
              className="w-full min-h-11 text-sm font-medium text-tinta-700 hover:text-tinta-900 underline underline-offset-2">
        Esqueci a senha
      </button>
    </form>
  )
}

function EsqueciSenha({ emailInicial, onVoltar }: { emailInicial: string; onVoltar: () => void }) {
  const [email, setEmail] = useState(emailInicial)
  const [enviando, setEnviando] = useState(false)
  const [pronto, setPronto] = useState<string | null>(null)
  const [erro, setErro] = useState<string | null>(null)

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (!email.trim()) { setErro('Informe o e-mail da sua conta.'); return }
    setEnviando(true); setErro(null)
    try {
      const r = await portalProdutosAApi.esqueciSenha(email.trim())
      setPronto(r.data.mensagem)
    } catch (err) {
      setErro(erroDa(err, 'Não deu para pedir agora. Tente de novo.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <form onSubmit={enviar} noValidate className="space-y-4">
      <p className="text-sm text-gray-700">
        Digite o e-mail da sua conta. Chega nele uma senha provisória; ao entrar com ela, você cria a nova.
      </p>
      <CampoTexto id="portal-esqueci-email" rotulo="E-mail">
        <input id="portal-esqueci-email" type="email" inputMode="email" autoCapitalize="none" autoCorrect="off"
               spellCheck={false} autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} className={campo} />
      </CampoTexto>
      {pronto && <Aviso tipo="info">{pronto}</Aviso>}
      {erro && <Aviso tipo="erro">{erro}</Aviso>}
      {!pronto && (
        <Botao type="submit" variante="primario" bloco carregando={enviando} textoCarregando="Pedindo..." className="min-h-12">
          Mandar senha nova
        </Botao>
      )}
      <Botao bloco onClick={onVoltar} className="min-h-12">Voltar para entrar</Botao>
    </form>
  )
}

function Criar({ codigo, estado, onTentar }: { codigo: string | null; estado: EstadoCodigo; onTentar: () => void }) {
  const { aplicarSessao } = useAuth()
  const [nome, setNome] = useState('')
  const [email, setEmail] = useState('')
  const [senha, setSenha] = useState('')
  const [repete, setRepete] = useState('')
  const [ver, setVer] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [criando, setCriando] = useState(false)

  if (estado === 'sem')
    return <Aviso tipo="info">Para criar a conta, use o QR do barracão.</Aviso>
  if (estado === 'conferindo')
    return <p className="text-sm text-gray-600 text-center py-4" role="status">Conferindo o QR...</p>
  if (estado === 'invalido')
    return <Aviso tipo="atencao">Este QR não vale mais. Peça o QR novo ao Admin.</Aviso>
  if (estado === 'erro')
    return <Aviso tipo="erro" acao={<Botao tamanho="sm" onClick={onTentar}>Tentar de novo</Botao>}>Não consegui conferir o QR.</Aviso>

  const regrasOk = regras.every(r => r.ok(senha))

  async function enviar(e: FormEvent) {
    e.preventDefault()
    if (nome.trim().length < 2) { setErro('Escreva o seu nome.'); return }
    if (!/^\S+@\S+\.\S+$/.test(email.trim())) { setErro('Confira o e-mail.'); return }
    if (!regrasOk) { setErro('A senha ainda não cumpre as regras.'); return }
    if (senha !== repete) { setErro('As duas senhas estão diferentes.'); return }
    setCriando(true); setErro(null)
    try {
      const r = await portalProdutosAApi.cadastrar({ codigo: codigo!, nome: nome.trim(), email: email.trim(), senha })
      try { localStorage.setItem(CHAVE_JA_ENTROU, '1') } catch { /* sem armazenamento */ }
      aplicarSessao(r.data.token, r.data.usuario)
    } catch (err) {
      setErro(erroDa(err, 'Não deu para criar a conta agora. Tente de novo.'))
      setCriando(false)
    }
  }

  return (
    <form onSubmit={enviar} noValidate className="space-y-4">
      <CampoTexto id="portal-nome" rotulo="Seu nome">
        <input id="portal-nome" type="text" autoComplete="name" autoCapitalize="words" value={nome}
               onChange={e => setNome(e.target.value)} className={campo} />
      </CampoTexto>
      <CampoTexto id="portal-novo-email" rotulo="E-mail">
        <input id="portal-novo-email" type="email" inputMode="email" autoCapitalize="none" autoCorrect="off" spellCheck={false}
               autoComplete="username" value={email} onChange={e => setEmail(e.target.value)} className={campo} />
      </CampoTexto>
      <div>
        <CampoSenha id="portal-nova-senha" rotulo="Senha" valor={senha} onChange={setSenha} ver={ver} onVer={() => setVer(v => !v)}
                    autoComplete="new-password" descrito="portal-regras" />
        <ul id="portal-regras" className="mt-2 space-y-1">
          {regras.map(r => {
            const ok = r.ok(senha)
            return (
              <li key={r.texto} className={`flex items-center gap-1.5 text-sm ${ok ? 'text-gray-900' : 'text-gray-600'}`}>
                {ok ? <Check size={16} aria-hidden /> : <Circle size={16} aria-hidden />}
                {r.texto}<span className="sr-only">{ok ? ': cumprida' : ': falta'}</span>
              </li>
            )
          })}
        </ul>
      </div>
      <CampoSenha id="portal-repete" rotulo="Repita a senha" valor={repete} onChange={setRepete} ver={ver}
                  autoComplete="new-password" />
      {erro && <Aviso tipo="erro">{erro}</Aviso>}
      <Botao type="submit" variante="primario" bloco carregando={criando} textoCarregando="Criando..." className="min-h-12">
        Criar minha conta
      </Botao>
    </form>
  )
}
