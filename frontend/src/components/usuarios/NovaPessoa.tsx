import { useState } from 'react'
import { Boxes, ShieldCheck, Truck, type LucideIcon } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal, Campo } from '../ModalBase'
import Botao from '../ui/Botao'
import Aviso from '../ui/Aviso'
import { classeCampo } from '../EstoquePecas'
import { MotoristaModal } from '../MotoristasSecao'
import SenhaPendente, { entregaDaSenha, type Pendente } from './SenhaPendente'
import { usuariosApi } from '../../services/usuariosApi'
import { erroDaApi } from '../../services/semPararApi'

// "Nova pessoa" (USU-11, Parte 10): pergunta primeiro o que ela vai acessar e cria pelo caminho certo.

type Escolha = 'viagens' | 'produtosA' | 'total'

const OPCOES: [Escolha, string, string, LucideIcon][] = [
  ['viagens', 'Controle de viagens', 'Motorista: cadastro com CNH e comissão, e o login do painel.', Truck],
  ['produtosA', 'Controle de produtosA', 'Conta do portal das produtosA.', Boxes],
  ['total', 'Acesso total ao sistema', 'Administrador do escritório: login por e-mail.', ShieldCheck],
]

const emailValido = (e: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e.trim())

export default function NovaPessoa({ onClose, onCriou }: { onClose: () => void; onCriou: () => void }) {
  const [escolha, setEscolha] = useState<Escolha | null>(null)
  const [nome, setNome] = useState('')
  const [email, setEmail] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [erro, setErro] = useState<string | null>(null)
  const [pendente, setPendente] = useState<Pendente | null>(null)

  if (escolha === 'viagens')
    return <MotoristaModal m={null} onClose={onClose} onSalvou={() => { onCriou(); onClose() }} />

  const criarTotal = async () => {
    if (!nome.trim() || !emailValido(email)) { setErro('Preencha o nome e um e-mail válido.'); return }
    setErro(null); setSalvando(true)
    const mail = email.trim().toLowerCase()
    try {
      const r = await usuariosApi.criar(nome.trim(), email.trim(), true, [])
      // O "criar" não devolve o id: acha a pessoa pelo login para fazer administrador
      const nova = (await usuariosApi.pessoas()).find(p => p.logins.some(l => l.login.toLowerCase() === mail && !l.perfil))
      if (nova?.usuarioId != null) await usuariosApi.admin(nova.usuarioId, true)
      else toast.error('Login criado, mas não achei para marcar como administrador. Abra a pessoa e marque o acesso total.', { duration: 8000 })
      toast.success('Login criado com acesso total.')
      onCriou()
      const p = entregaDaSenha(r, nome.trim(), mail)
      if (p) setPendente(p); else onClose()
    } catch (e) { setErro(await erroDaApi(e, 'Não consegui criar.')) }
    finally { setSalvando(false) }
  }

  const sujo = escolha === 'total' && !pendente && (nome.trim() !== '' || email.trim() !== '')

  return (
    <Modal titulo="Nova pessoa" onClose={onClose} sujo={sujo}
           descricao={escolha ? undefined : 'O que ela vai acessar?'}>
      {erro && <Aviso tipo="erro" onFechar={() => setErro(null)}>{erro}</Aviso>}
      {pendente ? (
        <>
          <SenhaPendente p={pendente} onFechar={onClose} />
        </>
      ) : !escolha ? (
        <ul className="space-y-2">
          {OPCOES.map(([k, titulo, texto, Icone]) => (
            <li key={k}>
              <button type="button" onClick={() => setEscolha(k)}
                      className="w-full text-left flex items-start gap-3 border border-gray-200 rounded-xl p-3 min-h-11 hover:bg-gray-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-tinta-500">
                <Icone className="w-5 h-5 mt-0.5 text-tinta-700 shrink-0" aria-hidden />
                <span>
                  <span className="font-medium text-gray-900 block">{titulo}</span>
                  <span className="text-sm text-gray-600">{texto}</span>
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : escolha === 'produtosA' ? (
        <>
          <Aviso tipo="info">
            A conta do portal das produtosA a própria pessoa cria pelo QR do portal, na parte "Portal das produtosA" desta tela.
            Para um motorista também anotar produtosA, abra a pessoa e marque "Controle de produtosA".
          </Aviso>
          <div className="flex flex-wrap gap-2">
            <Botao variante="secundario" onClick={() => setEscolha(null)}>Voltar</Botao>
            <Botao variante="fantasma" onClick={onClose}>Fechar</Botao>
          </div>
        </>
      ) : (
        <>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Campo rotulo="Nome"><input value={nome} onChange={e => setNome(e.target.value)} placeholder="Nome do usuário" className={classeCampo} /></Campo>
            <Campo rotulo="E-mail">
              <input type="email" value={email} onChange={e => setEmail(e.target.value)} placeholder="pessoa@email.com" className={classeCampo}
                     autoCapitalize="none" autoComplete="off" />
            </Campo>
          </div>
          <Aviso tipo="info">Vira administrador. A senha provisória vai por e-mail e a pessoa cria a dela no primeiro acesso.</Aviso>
          <div className="flex flex-wrap gap-2">
            <Botao variante="primario" carregando={salvando} onClick={criarTotal}>Criar e mandar a senha</Botao>
            <Botao variante="fantasma" onClick={() => setEscolha(null)}>Voltar</Botao>
          </div>
        </>
      )}
    </Modal>
  )
}
