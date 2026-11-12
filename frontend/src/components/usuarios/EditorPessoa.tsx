import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Download, KeyRound, Mail, Pencil, UserCheck } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal, Campo } from '../ModalBase'
import Botao from '../ui/Botao'
import Aviso from '../ui/Aviso'
import { useConfirmar } from '../ui/Confirmar'
import { classeCampo } from '../EstoquePecas'
import { Selo, CampoNumero } from '../viagens/ViagensPecas'
import { MotoristaModal, SenhaModal, SeloValidade } from '../MotoristasSecao'
import SenhaPendente, { entregaDaSenha, type Pendente } from './SenhaPendente'
import { useAuth } from '../../contexts/AuthContext'
import { usuariosApi, type EmailSituacao, type Pessoa } from '../../services/usuariosApi'
import { usuariosApi as contasApi } from '../../services/api'
import { viagensApi, type MotoristaFrota } from '../../services/viagensCaminhaoApi'
import { erroDaApi } from '../../services/semPararApi'
import { num } from '../../utils/formato'

// Editor da pessoa na tela "Usuários" (USU-11, Parte 10). Três marcações de acesso, sem caixinha por
// tela: Controle de viagens (= classificação Motorista), Controle de produtosA e Acesso total
// (= administrador). As travas são do servidor (Parte 6); quando ele recusa, o motivo aparece aqui.

const PRODUTOSA = 'portal-produtosA'
type Tipo = 'escritorio' | 'motorista' | 'produtosA' | 'semLogin'

export function tipoDaPessoa(p: Pessoa): Tipo {
  const l = p.logins[0]
  if (!l) return 'semLogin'
  if (!l.perfil) return 'escritorio'
  return l.perfil === 'motorista' ? 'motorista' : 'produtosA'
}

const emailValido = (e: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e.trim())

/** Situação do e-mail do motorista por extenso (bloco "Dados do motorista"). */
const SITUACAO_EMAIL: Record<EmailSituacao, string> = {
  sem: 'Sem e-mail',
  repetido: 'E-mail repetido: igual ao de outro login ou cadastro. Troque para mandar o acesso.',
  confirmado: 'Confirmado: entra pelo e-mail com a senha que criou.',
  aguardando: 'Acesso enviado, aguardando criar a senha.',
  pendente: 'E-mail ainda não enviado.',
}

function Bloco({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <section className="border border-gray-200 rounded-xl p-3 sm:p-4 space-y-3">
      <h3 className="font-semibold text-gray-900">{titulo}</h3>
      {children}
    </section>
  )
}

/** Uma marcação de acesso. Quando não dá para mudar, mostra o motivo em texto (sem caixinha travada). */
function Marcacao({ titulo, descricao, ligado, fixo, ocupado, onAlternar, children }: {
  titulo: string; descricao: string; ligado: boolean; fixo?: string; ocupado: boolean
  onAlternar: () => void; children?: ReactNode
}) {
  return (
    <div className="border-t border-gray-100 first:border-t-0 pt-3 first:pt-0 space-y-2">
      {fixo ? (
        <div>
          <p className="font-medium text-gray-900 flex flex-wrap items-center gap-2">
            {titulo} {ligado ? <Selo>Tem</Selo> : <Selo tom="atencao">Não tem</Selo>}
          </p>
          <p className="text-sm text-gray-600">{fixo}</p>
        </div>
      ) : (
        <label className="flex items-start gap-3 min-h-11 cursor-pointer">
          <input type="checkbox" className="w-5 h-5 mt-0.5 shrink-0" checked={ligado} disabled={ocupado} onChange={onAlternar} />
          <span>
            <span className="font-medium text-gray-900 block">{titulo}</span>
            <span className="text-sm text-gray-600">{descricao}</span>
          </span>
        </label>
      )}
      {children}
    </div>
  )
}

export default function EditorPessoa({ p, onClose, onMudou }: { p: Pessoa; onClose: () => void; onMudou: () => void }) {
  const { usuario: eu } = useAuth()
  const confirmar = useConfirmar()
  const tipo = tipoDaPessoa(p)
  const login = p.logins[0]
  const souEu = p.usuarioId != null && p.usuarioId === eu?.id

  const [motoristas, setMotoristas] = useState<MotoristaFrota[] | null>(null)
  const [catalogo, setCatalogo] = useState<string[]>([])
  const [erro, setErro] = useState<string | null>(null)
  const [ocupado, setOcupado] = useState(false)
  const [pendente, setPendente] = useState<Pendente | null>(null)
  // O que o servidor avisou depois de uma ação que deu certo (fica até fechar)
  const [avisoAcao, setAvisoAcao] = useState<string | null>(null)
  const [editarMotorista, setEditarMotorista] = useState(false)
  const [senhaMotorista, setSenhaMotorista] = useState(false)

  const nomeOriginal = p.nome
  const emailOriginal = p.email ?? login?.login ?? ''
  const [nome, setNome] = useState(nomeOriginal)
  const [email, setEmail] = useState(emailOriginal)
  useEffect(() => { setNome(p.nome); setEmail(p.email ?? p.logins[0]?.login ?? '') }, [p])

  // Formulários que abrem ao ligar uma marcação
  const [darViagens, setDarViagens] = useState(false)
  const [cadastroEscolhido, setCadastroEscolhido] = useState<string>('novo')
  const [novoLogin, setNovoLogin] = useState('')
  const [novaComissao, setNovaComissao] = useState<number>(6)
  const [darTotal, setDarTotal] = useState(false)
  const [emailTotal, setEmailTotal] = useState(p.email ?? '')

  const carregarMotoristas = useCallback(() => {
    viagensApi.motoristas().then(setMotoristas).catch(() => setMotoristas([]))
  }, [])
  useEffect(carregarMotoristas, [carregarMotoristas])
  useEffect(() => { usuariosApi.areas().then(a => setCatalogo(a.map(x => x.chave))).catch(() => setCatalogo([])) }, [])

  const cadastro = motoristas?.find(m => m.id === p.motoristaId) ?? null

  const executar = async (fn: () => Promise<void>, ok?: string) => {
    setErro(null); setAvisoAcao(null); setOcupado(true)
    try { await fn(); if (ok) toast.success(ok); onMudou(); carregarMotoristas() }
    catch (e) { setErro(await erroDaApi(e, 'Não consegui gravar. Tente de novo.')) }
    finally { setOcupado(false) }
  }

  // ── Dados ──────────────────────────────────────────────────────────────
  const editaDados = tipo === 'escritorio' || tipo === 'produtosA'
  const dadosMudaram = editaDados && (nome.trim() !== nomeOriginal || email.trim() !== emailOriginal)
  const salvarDados = () => {
    if (!nome.trim() || !emailValido(email)) { setErro('Preencha o nome e um e-mail válido.'); return }
    executar(async () => { await contasApi.editar(p.usuarioId!, { nome: nome.trim(), email: email.trim() }) }, 'Dados salvos.')
  }

  const motivoSemDesativar = !p.ativo ? null
    : souEu ? 'Você não pode desativar o seu próprio usuário.'
    : p.administrador ? 'Para desativar, tire antes o acesso total.' : null

  const alternarAtivo = async () => {
    if (p.ativo && !await confirmar({
      titulo: `Desativar ${p.nome}?`,
      texto: 'A pessoa não entra mais no sistema. O que ela lançou continua lá; dá para reativar depois.',
      confirmar: 'Desativar',
    })) return
    const ativar = !p.ativo
    executar(async () => {
      if ((tipo === 'motorista' || tipo === 'semLogin') && p.motoristaId != null) {
        const r = await viagensApi.ativoMotorista(p.motoristaId, ativar)
        if (r?.aviso) toast.error(r.aviso, { duration: 8000 })
      } else if (p.usuarioId != null) {
        if (ativar) await contasApi.reativar(p.usuarioId)
        else await contasApi.desativar(p.usuarioId)
      }
    }, ativar ? `${p.nome} pode entrar de novo.` : `${p.nome} não entra mais.`)
  }

  const mandarSenhaNova = async () => {
    if (!await confirmar({
      titulo: `Gerar senha nova para ${p.nome}?`,
      texto: 'Vai uma senha provisória para o e-mail dela, e a senha atual deixa de valer.',
      confirmar: 'Gerar senha nova', perigo: false,
    })) return
    executar(async () => {
      const r = await contasApi.novaSenha(p.usuarioId!)
      setPendente(entregaDaSenha(r.data, p.nome, emailOriginal))
    })
  }

  // Login do motorista por e-mail: manda a senha provisória (a de hoje continua valendo até ele criar a nova)
  const mandarAcessoEmail = async (para: string) => {
    if (p.motoristaId == null) return
    const temporaria = p.emailSituacao === 'confirmado'
    if (!await confirmar({
      titulo: temporaria ? `Mandar senha temporária para ${p.nome}?` : `Mandar o e-mail de acesso para ${p.nome}?`,
      texto: `Vai uma senha provisória para ${para}. A senha de hoje continua valendo até ${p.nome} criar a nova.`,
      confirmar: temporaria ? 'Mandar senha temporária' : 'Mandar e-mail', perigo: false,
    })) return
    executar(async () => {
      const r = await usuariosApi.acessoEmail(p.motoristaId!)
      setPendente(entregaDaSenha(r?.senha, p.nome, para))
    })
  }

  // ── Controle de viagens (= classificação Motorista) ────────────────────
  // No escritório vale a classificação (cadastro de motorista ativo), não o acesso total
  const temViagens = tipo === 'escritorio' ? p.motoristaId != null && p.motoristaAtivo !== false : p.controleViagens
  const fixoViagens = tipo === 'motorista' ? 'Login próprio de motorista: sempre entra no Controle de viagens. Para tirar, desative a pessoa.'
    : tipo === 'semLogin' ? 'Cadastro de motorista ainda sem login: não entra até ganhar um login.'
    : tipo === 'produtosA' ? 'Conta do portal das produtosA: entra só no portal.'
    : undefined

  const alternarViagens = async () => {
    if (temViagens) {
      if (!await confirmar({
        titulo: `Tirar o Controle de viagens de ${p.nome}?`,
        texto: 'O cadastro de motorista é desativado (não é apagado). As viagens dele continuam no controle.',
        confirmar: 'Tirar',
      })) return
      executar(async () => { await usuariosApi.tirarMotorista(p.usuarioId!) }, 'Controle de viagens tirado.')
      return
    }
    if (p.motoristaId != null) {
      // Já tem cadastro (desativado): reativa
      if (!await confirmar({ titulo: `Devolver o Controle de viagens a ${p.nome}?`, texto: 'O cadastro de motorista dele volta a ficar ativo.', confirmar: 'Devolver', perigo: false })) return
      executar(async () => {
        const r = await viagensApi.ativoMotorista(p.motoristaId!, true)
        if (r?.aviso) toast.error(r.aviso, { duration: 8000 })
      }, 'Controle de viagens devolvido.')
      return
    }
    setDarViagens(v => !v)
  }

  const confirmarDarViagens = async () => {
    const existente = cadastroEscolhido !== 'novo' ? motoristas?.find(m => String(m.id) === cadastroEscolhido) : null
    if (!existente && novoLogin.trim().length < 3) { setErro('O usuário do cadastro novo precisa de pelo menos 3 letras.'); return }
    const texto = existente
      ? <>Junta {p.nome} com o cadastro de motorista <b>{existente.nome}</b>.{existente.temUsuario !== false && <> O login de motorista antigo (<b>{existente.login}</b>) é excluído; as viagens, os retornos e o resto passam para este login.</>}</>
      : <>Cria um cadastro de motorista novo ligado a este login, com comissão de {num(novaComissao, novaComissao % 1 ? 2 : 0)}%.</>
    if (!await confirmar({ titulo: `Dar o Controle de viagens a ${p.nome}?`, texto, confirmar: 'Dar o acesso', perigo: !!existente })) return
    executar(async () => {
      await usuariosApi.darMotorista(p.usuarioId!, existente
        ? { motoristaId: existente.id }
        : { nome: p.nome, login: novoLogin.trim(), porcentagemComissao: novaComissao })
      setDarViagens(false)
    }, 'Controle de viagens dado.')
  }

  // ── Controle de produtosA ────────────────────────────────────────────────
  const temProdutosA = p.controleProdutosA
  const fixoProdutosA = p.acessoTotal ? 'Incluído no acesso total.'
    : tipo === 'produtosA' ? 'É o que esta conta acessa. Para tirar, desative a conta.'
    : tipo === 'semLogin' ? 'Precisa de um login para entrar no portal.'
    : undefined

  const alternarProdutosA = async () => {
    const ligar = !temProdutosA
    if (!await confirmar({
      titulo: ligar ? `Dar o Controle de produtosA a ${p.nome}?` : `Tirar o Controle de produtosA de ${p.nome}?`,
      texto: ligar ? 'Ela passa a entrar também no portal das produtosA, com o mesmo login.' : 'Ela deixa de entrar no portal das produtosA.',
      confirmar: ligar ? 'Dar o acesso' : 'Tirar', perigo: !ligar,
    })) return
    // Escritório sem acesso total mantém as outras áreas que já tinha
    const outras = tipo === 'escritorio' ? p.areas.filter(a => a !== PRODUTOSA && catalogo.includes(a)) : []
    executar(async () => {
      await usuariosApi.permissoes(p.usuarioId!, false, ligar ? [...outras, PRODUTOSA] : outras)
    }, ligar ? 'Controle de produtosA dado.' : 'Controle de produtosA tirado.')
  }

  // ── Acesso total (= administrador) ─────────────────────────────────────
  const temTotal = p.administrador
  const fixoTotal = souEu ? 'Você não pode tirar o seu próprio acesso total.'
    : tipo === 'produtosA' ? 'Conta do portal das produtosA não recebe acesso total.'
    : tipo === 'motorista' && p.motoristaId == null ? 'Complete antes o cadastro de motorista (bloco abaixo).'
    : !p.ativo ? 'Reative a pessoa antes.'
    : undefined

  const alternarTotal = async () => {
    if (temTotal) {
      const comMotorista = p.motoristaId != null
      if (!await confirmar({
        titulo: `Tirar o acesso total de ${p.nome}?`,
        texto: comMotorista
          ? 'Deixa de ser administrador e o login do escritório é desativado. Volta a entrar só no Controle de viagens, com login próprio de motorista.'
          : 'Deixa de ser administrador e fica sem acesso ao sistema do escritório. Para tirar de vez, desative a pessoa depois.',
        confirmar: 'Tirar o acesso total',
      })) return
      executar(async () => {
        await usuariosApi.admin(p.usuarioId!, false)
        if (comMotorista) {
          // O login novo de motorista recebe o acesso pelo e-mail do cadastro: senha que não saiu ou aviso aparecem aqui
          const r = await usuariosApi.tirarUsuario(p.usuarioId!)
          setAvisoAcao(r?.aviso ?? null)
          setPendente(entregaDaSenha(r?.senha, p.nome, cadastro?.email ?? 'o e-mail do cadastro'))
        }
        else await usuariosApi.permissoes(p.usuarioId!, false, [])
      }, 'Acesso total tirado.')
      return
    }
    if (tipo === 'escritorio') {
      if (!await confirmar({
        titulo: `Dar acesso total a ${p.nome}?`,
        texto: 'Vira administrador: vê e altera tudo no sistema, inclusive os usuários e o que cada um acessa.',
        confirmar: 'Dar acesso total', perigo: false,
      })) return
      executar(async () => { await usuariosApi.admin(p.usuarioId!, true) }, 'Acesso total dado.')
      return
    }
    setDarTotal(v => !v)
  }

  const confirmarDarTotal = async () => {
    if (!emailValido(emailTotal)) { setErro('Informe um e-mail válido para o login do escritório.'); return }
    if (!await confirmar({
      titulo: `Dar acesso total a ${p.nome}?`,
      texto: <>Cria o login do escritório com o e-mail <b>{emailTotal.trim()}</b> e manda uma senha provisória para ele. Vira administrador.
        {tipo === 'motorista' && login && <> O login de motorista antigo (<b>{login.login}</b>) é excluído; o cadastro de motorista e as viagens ficam.</>}</>,
      confirmar: 'Criar o login', perigo: tipo === 'motorista',
    })) return
    executar(async () => {
      const r = await usuariosApi.darUsuario(p.motoristaId!, emailTotal.trim(), true, [])
      await usuariosApi.admin(r.usuarioId, true)
      setPendente(entregaDaSenha(r.senha, p.nome, emailTotal.trim().toLowerCase()))
      setDarTotal(false)
    }, 'Login do escritório criado com acesso total.')
  }

  const ehMotorista = p.classificacoes.includes('motorista') || p.motoristaId != null

  return (
    <Modal titulo={p.nome || 'Pessoa'} onClose={onClose} sujo={dadosMudaram} largura="lg"
           descricao={login ? <>Entra com <b>{login.login}</b></> : 'Sem login de acesso'}>
      {erro && <Aviso tipo="erro" onFechar={() => setErro(null)}>{erro}</Aviso>}
      {avisoAcao && <Aviso tipo="atencao" onFechar={() => setAvisoAcao(null)}>{avisoAcao}</Aviso>}
      {pendente && <SenhaPendente p={pendente} onFechar={() => setPendente(null)} />}
      {p.avisos.length > 0 && <Aviso tipo="atencao" itens={p.avisos} />}

      <Bloco titulo="Dados">
        {editaDados ? (
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Campo rotulo="Nome"><input value={nome} onChange={e => setNome(e.target.value)} className={classeCampo} /></Campo>
            <Campo rotulo="E-mail (login)">
              <input type="email" value={email} onChange={e => setEmail(e.target.value)} className={classeCampo} autoCapitalize="none" autoComplete="off" />
            </Campo>
          </div>
        ) : (
          <p className="text-sm text-gray-700">
            Nome e usuário de motorista mudam em <b>Dados do motorista</b>, mais abaixo.
          </p>
        )}
        {dadosMudaram && <Botao variante="primario" carregando={ocupado} onClick={salvarDados}>Salvar dados</Botao>}

        <div className="flex flex-wrap items-center gap-2 pt-1">
          {p.ativo ? <Selo>Ativo</Selo> : <Selo tom="atencao">Desativado</Selo>}
          {motivoSemDesativar
            ? <span className="text-sm text-gray-600">{motivoSemDesativar}</span>
            : (p.usuarioId != null || p.motoristaId != null) && (
              <Botao variante={p.ativo ? 'perigoLeve' : 'secundario'} tamanho="sm" disabled={ocupado} onClick={alternarAtivo}>
                {p.ativo ? 'Desativar' : 'Reativar'}
              </Botao>
            )}
          {editaDados && p.ativo && !souEu && (
            <Botao variante="secundario" tamanho="sm" icone={KeyRound} disabled={ocupado} onClick={mandarSenhaNova}>Mandar senha nova</Botao>
          )}
          {tipo === 'motorista' && cadastro && p.emailSituacao !== 'confirmado' && (
            <Botao variante="secundario" tamanho="sm" icone={KeyRound} onClick={() => setSenhaMotorista(true)}>Trocar a senha do painel</Botao>
          )}
        </div>
      </Bloco>

      <Bloco titulo="Acesso">
        <Marcacao titulo="Controle de viagens" descricao="Painel do motorista: lança e edita as viagens dele."
                  ligado={temViagens} fixo={fixoViagens} ocupado={ocupado} onAlternar={alternarViagens}>
          {darViagens && !temViagens && (
            <div className="bg-gray-50 rounded-lg p-3 space-y-3">
              <Campo rotulo="Cadastro de motorista">
                <select value={cadastroEscolhido} onChange={e => setCadastroEscolhido(e.target.value)} className={classeCampo}>
                  <option value="novo">Criar um cadastro novo</option>
                  {(motoristas ?? []).map(m => (
                    <option key={m.id} value={m.id}>{m.nome} ({m.login}){m.ativo ? '' : ' · desativado'}</option>
                  ))}
                </select>
              </Campo>
              {cadastroEscolhido === 'novo' && (
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  <Campo rotulo="Usuário do motorista" dica="Minúsculo, sem acento.">
                    <input value={novoLogin} onChange={e => setNovoLogin(e.target.value.toLowerCase().replace(/[^a-z0-9]/g, ''))}
                           className={classeCampo} autoCapitalize="none" autoComplete="off" />
                  </Campo>
                  <Campo rotulo="Comissão (%)"><CampoNumero valor={novaComissao} onChange={v => setNovaComissao(v ?? 0)} /></Campo>
                </div>
              )}
              <div className="flex flex-wrap gap-2">
                <Botao variante="primario" carregando={ocupado} onClick={confirmarDarViagens}>Dar o Controle de viagens</Botao>
                <Botao variante="fantasma" onClick={() => setDarViagens(false)}>Cancelar</Botao>
              </div>
            </div>
          )}
        </Marcacao>

        <Marcacao titulo="Controle de produtosA" descricao="Portal das produtosA: anota as produtosA pelo celular."
                  ligado={temProdutosA} fixo={fixoProdutosA} ocupado={ocupado} onAlternar={alternarProdutosA} />

        <Marcacao titulo="Acesso total ao sistema" descricao="Administrador: vê e altera tudo, inclusive os usuários."
                  ligado={temTotal} fixo={fixoTotal} ocupado={ocupado} onAlternar={alternarTotal}>
          {!temTotal && p.acessoTotal && <p className="text-sm text-amber-800">Tem acesso total sem ser administrador. Marque para virar administrador.</p>}
          {darTotal && !temTotal && (
            <div className="bg-gray-50 rounded-lg p-3 space-y-3">
              <Campo rotulo="E-mail do login do escritório" dica="A senha provisória vai para este e-mail.">
                <input type="email" value={emailTotal} onChange={e => setEmailTotal(e.target.value)} className={classeCampo}
                       autoCapitalize="none" autoComplete="off" />
              </Campo>
              <div className="flex flex-wrap gap-2">
                <Botao variante="primario" carregando={ocupado} onClick={confirmarDarTotal}>Criar o login</Botao>
                <Botao variante="fantasma" onClick={() => setDarTotal(false)}>Cancelar</Botao>
              </div>
            </div>
          )}
        </Marcacao>
      </Bloco>

      {ehMotorista && (
        <Bloco titulo="Dados do motorista">
          {p.motoristaId == null ? (
            <>
              <Aviso tipo="atencao">Login de motorista sem cadastro: o painel não abre até completar (comissão).</Aviso>
              <Botao variante="secundario" icone={UserCheck} onClick={() => setEditarMotorista(true)}>Completar cadastro</Botao>
            </>
          ) : !motoristas ? (
            <p className="text-sm text-gray-600">Carregando o cadastro...</p>
          ) : !cadastro ? (
            <Aviso tipo="atencao">Não achei o cadastro de motorista desta pessoa.</Aviso>
          ) : (
            <>
              <dl className="grid grid-cols-[auto,1fr] gap-x-3 gap-y-1.5 text-sm">
                <dt className="text-gray-600">Login antigo (nome de usuário)</dt><dd className="break-all">{cadastro.login}</dd>
                <dt className="text-gray-600">Comissão</dt><dd>{num(cadastro.porcentagemComissao, cadastro.porcentagemComissao % 1 ? 2 : 0)}%</dd>
                <dt className="text-gray-600">CNH</dt><dd><SeloValidade validade={cadastro.cnhValidade} /></dd>
                <dt className="text-gray-600">E-mail</dt>
                <dd className="break-all">
                  {cadastro.email || <span className="text-amber-800">Sem e-mail</span>}
                  {cadastro.email && p.emailSituacao && (
                    <span className={`block text-xs ${p.emailSituacao === 'confirmado' ? 'text-gray-600' : 'text-amber-800'}`}>
                      {SITUACAO_EMAIL[p.emailSituacao]}
                    </span>
                  )}
                </dd>
              </dl>
              <div className="flex flex-wrap gap-2">
                <Botao variante="secundario" icone={Pencil} onClick={() => setEditarMotorista(true)}>Editar dados do motorista</Botao>
                {cadastro.email && (p.emailSituacao === 'pendente' || p.emailSituacao === 'aguardando' || p.emailSituacao === 'confirmado') && (
                  <Botao variante="secundario" icone={Mail} disabled={ocupado} onClick={() => mandarAcessoEmail(cadastro.email!)}>
                    {p.emailSituacao === 'confirmado' ? 'Mandar senha temporária'
                      : p.emailSituacao === 'aguardando' ? 'Mandar de novo' : 'Mandar e-mail de acesso'}
                  </Botao>
                )}
                {cadastro.temCnhPdf && (
                  <Botao variante="secundario" icone={Download}
                         onClick={() => viagensApi.baixarCnh(cadastro.id, cadastro.nome).catch(async e => toast.error(await erroDaApi(e, 'Não consegui baixar.')))}>
                    Baixar a CNH (PDF)
                  </Botao>
                )}
              </div>
            </>
          )}
        </Bloco>
      )}

      {editarMotorista && (
        <MotoristaModal m={cadastro}
                        base={p.motoristaId == null && p.usuarioId != null && login ? { id: p.usuarioId, nome: p.nome, login: login.login, ativo: login.ativo } : undefined}
                        onClose={() => setEditarMotorista(false)}
                        onSalvou={() => { setEditarMotorista(false); onMudou(); carregarMotoristas() }} />
      )}
      {senhaMotorista && cadastro && (
        <SenhaModal m={cadastro} onClose={() => setSenhaMotorista(false)} onSalvou={() => { setSenhaMotorista(false); onMudou() }} />
      )}
    </Modal>
  )
}
