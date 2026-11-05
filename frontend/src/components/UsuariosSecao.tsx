import { useConfirmar } from './ui/Confirmar'
import { useState, useEffect, FormEvent } from 'react'
import toast from 'react-hot-toast'
import axios from 'axios'
import { Copy, KeyRound, Pencil, UserPlus } from 'lucide-react'
import { usuariosApi, type UsuarioResumo, type SenhaProvisoria } from '../services/api'
import { useAuth } from '../contexts/AuthContext'
import Botao, { BotaoIcone } from './ui/Botao'
import Campo from './ui/Campo'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { classeCampo } from './EstoquePecas'

// Usuário antigo, do tempo do login por nome, ainda sem e-mail de verdade.
const emailReal = (e: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e)

function erroDe(err: unknown, padrao: string) {
  return axios.isAxiosError(err) ? (err.response?.data as { erro?: string })?.erro ?? padrao : padrao
}

export default function UsuariosSecao() {
  const { usuario: eu } = useAuth()
  const [usuarios, setUsuarios] = useState<UsuarioResumo[]>([])
  const [loading, setLoading]   = useState(true)
  const [falhou, setFalhou]     = useState(false)

  const [novoNome, setNovoNome]   = useState('')
  const [novoEmail, setNovoEmail] = useState('')
  const [criando, setCriando]     = useState(false)

  const [editId, setEditId]       = useState<number | null>(null)
  const [editNome, setEditNome]   = useState('')
  const [editEmail, setEditEmail] = useState('')
  const [salvandoEdicao, setSalvandoEdicao] = useState(false)
  const confirmar = useConfirmar()

  // Senha que não saiu por e-mail: aparece uma vez, para o dono repassar.
  const [pendente, setPendente] = useState<{ nome: string; email: string; senha: string; aviso: string } | null>(null)

  async function carregar() {
    try {
      setUsuarios((await usuariosApi.listar()).data)
      setFalhou(false)
    } catch {
      setFalhou(true)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { carregar() }, [])

  function tratarEntrega(r: SenhaProvisoria, nome: string, email: string, acao: string) {
    if (r.emailEnviado) {
      toast.success(`${acao} Senha provisória enviada para ${email}.`)
      setPendente(null)
    } else if (r.senhaProvisoria) {
      setPendente({ nome, email, senha: r.senhaProvisoria, aviso: r.avisoEmail ?? 'O e-mail não saiu.' })
    }
  }

  async function criar(e: FormEvent) {
    e.preventDefault()
    if (!novoNome.trim() || !novoEmail.trim()) { toast.error('Preencha o nome e o e-mail.'); return }
    setCriando(true)
    try {
      const r = await usuariosApi.criar({ nome: novoNome.trim(), email: novoEmail.trim() })
      tratarEntrega(r.data, novoNome.trim(), novoEmail.trim().toLowerCase(), 'Usuário criado.')
      setNovoNome(''); setNovoEmail('')
      carregar()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui criar o usuário.'))
    } finally {
      setCriando(false)
    }
  }

  async function mandarNovaSenha(u: UsuarioResumo) {
    if (!await confirmar({
      titulo: `Gerar senha nova para ${u.nome}?`,
      texto: 'Vai uma senha provisória para o e-mail dele, e a senha atual deixa de valer.',
      confirmar: 'Gerar senha nova',
    })) return
    try {
      const r = await usuariosApi.novaSenha(u.id)
      tratarEntrega(r.data, u.nome, u.email, '')
      carregar()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui gerar a senha.'))
    }
  }

  async function salvarEdicao(e: FormEvent) {
    e.preventDefault()
    if (editId == null) return
    setSalvandoEdicao(true)
    try {
      await usuariosApi.editar(editId, { nome: editNome, email: editEmail })
      toast.success('Usuário atualizado.')
      setEditId(null)
      carregar()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui salvar.'))
    } finally {
      setSalvandoEdicao(false)
    }
  }

  async function alternarAtivo(u: UsuarioResumo) {
    try {
      if (u.ativo) await usuariosApi.desativar(u.id)
      else await usuariosApi.reativar(u.id)
      toast.success(u.ativo ? `${u.nome} não entra mais no sistema.` : `${u.nome} pode entrar de novo.`)
      carregar()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui alterar.'))
    }
  }

  async function copiar(texto: string) {
    try { await navigator.clipboard.writeText(texto); toast.success('Senha copiada.') }
    catch { toast.error('Não consegui copiar. Selecione e copie à mão.') }
  }

  return (
    <section aria-labelledby="usuarios" className="space-y-4">
      <div>
        <h2 id="usuarios" className="text-lg font-semibold text-gray-900">Usuários</h2>
        <p className="text-sm text-gray-600 mt-0.5">
          Quem pode entrar no sistema. A senha provisória vai por e-mail e a pessoa cria a dela no primeiro acesso.
        </p>
      </div>

      {pendente && (
        <div role="alert" className="bg-gema-50 border border-gema-300 rounded-xl p-4 space-y-3">
          <p className="text-sm text-gray-900">
            <b>{pendente.aviso}</b> Mande esta senha para {pendente.nome} por outro caminho, como o WhatsApp.
            Ela aparece só agora.
          </p>
          <code className="block w-fit max-w-full break-all font-mono text-lg bg-white border border-gema-200 rounded-lg px-3 py-1.5 select-all">
            {pendente.senha}
          </code>
          <div className="flex flex-wrap gap-2">
            <Botao variante="primario" icone={Copy} onClick={() => copiar(pendente.senha)}>Copiar</Botao>
            <Botao variante="secundario" onClick={() => copiar(`Seu acesso ao sistema da TRX Transportes:\nE-mail: ${pendente.email}\nSenha provisória: ${pendente.senha}\n\nEntre em ${location.origin} e crie a sua senha.`)}>
              Copiar mensagem pronta
            </Botao>
            <Botao variante="fantasma" onClick={() => setPendente(null)}>Já mandei</Botao>
          </div>
        </div>
      )}

      {/* Lista */}
      {loading ? (
        <Carregando linhas={2} rotulo="Carregando os usuários" />
      ) : falhou ? (
        <ErroCarga mensagem="Não consegui carregar os usuários." onTentar={() => { setLoading(true); carregar() }} />
      ) : usuarios.length === 0 ? (
        <Vazio titulo="Nenhum usuário" />
      ) : (
        <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100 overflow-hidden">
          {usuarios.map(u => (
            <li key={u.id} className={`px-4 sm:px-5 py-3.5 ${!u.ativo ? 'bg-gray-50' : ''}`}>
              {editId === u.id ? (
                <form onSubmit={salvarEdicao} className="grid sm:grid-cols-[1fr_1fr_auto] gap-3 items-end">
                  <Campo rotulo="Nome"><input value={editNome} onChange={e => setEditNome(e.target.value)} className={classeCampo} /></Campo>
                  <Campo rotulo="E-mail"><input type="email" value={editEmail} onChange={e => setEditEmail(e.target.value)} className={classeCampo} /></Campo>
                  <div className="flex gap-2">
                    <Botao type="submit" variante="primario" carregando={salvandoEdicao} className="flex-1 sm:flex-none">Salvar</Botao>
                    <Botao variante="fantasma" onClick={() => setEditId(null)}>Cancelar</Botao>
                  </div>
                </form>
              ) : (
                <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className={`font-semibold ${u.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{u.nome}</span>
                      {u.id === eu?.id && <span className="text-sm text-gray-500">você</span>}
                      {u.dono && <span className="text-xs font-medium bg-tinta-50 text-tinta-800 border border-tinta-200 rounded-full px-2 py-0.5">Dono</span>}
                      {u.trocarSenha && u.ativo && (
                        <span className="text-xs font-medium bg-gema-50 text-gema-900 border border-gema-200 rounded-full px-2 py-0.5">Ainda não entrou</span>
                      )}
                      {!u.ativo && <span className="text-sm text-gray-500">desativado</span>}
                    </div>
                    <p className={`text-sm break-all ${emailReal(u.email) ? 'text-gray-600' : 'text-red-700 font-medium'}`}>
                      {emailReal(u.email) ? u.email : `"${u.email}" não é um e-mail. Edite para colocar o e-mail real.`}
                    </p>
                  </div>

                  <div className="flex items-center gap-1 shrink-0">
                    <BotaoIcone icone={Pencil} rotulo={`Editar nome e e-mail de ${u.nome}`}
                                onClick={() => { setEditId(u.id); setEditNome(u.nome); setEditEmail(emailReal(u.email) ? u.email : '') }} />
                    {u.ativo && u.id !== eu?.id && (
                      <Botao variante="secundario" tamanho="sm" icone={KeyRound} onClick={() => mandarNovaSenha(u)} title="Mandar senha provisória nova">
                        Nova senha
                      </Botao>
                    )}
                    {!u.dono && (
                      <Botao variante={u.ativo ? 'perigoLeve' : 'secundario'} tamanho="sm" onClick={() => alternarAtivo(u)}>
                        {u.ativo ? 'Desativar' : 'Reativar'}
                      </Botao>
                    )}
                  </div>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {/* Novo usuário */}
      <form onSubmit={criar} aria-labelledby="usuario-novo" className="bg-white rounded-xl border border-gray-200 p-4 sm:p-5 space-y-4">
        <h3 id="usuario-novo" className="font-semibold text-gray-900">Novo usuário</h3>
        <div className="grid sm:grid-cols-2 gap-3">
          <Campo rotulo="Nome">
            <input value={novoNome} onChange={e => setNovoNome(e.target.value)} placeholder="Nome do usuário" className={classeCampo} />
          </Campo>
          <Campo rotulo="E-mail">
            <input type="email" value={novoEmail} onChange={e => setNovoEmail(e.target.value)} placeholder="pessoa@email.com" className={classeCampo} />
          </Campo>
        </div>
        <Botao type="submit" variante="primario" icone={UserPlus} carregando={criando} textoCarregando="Criando..." bloco className="sm:w-auto">
          Criar e mandar a senha
        </Botao>
      </form>
    </section>
  )
}
