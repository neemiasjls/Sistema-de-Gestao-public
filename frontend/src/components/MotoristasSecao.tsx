import { useCallback, useEffect, useState } from 'react'
import { Download, FileUp, KeyRound, Pencil, Plus, UserCheck, Users } from 'lucide-react'
import toast from 'react-hot-toast'
import Botao, { BotaoIcone } from './ui/Botao'
import Aviso from './ui/Aviso'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { Modal, Campo, BotoesModal } from './ModalBase'
import { classeCampo } from './EstoquePecas'
import { Selo, CampoNumero } from './viagens/ViagensPecas'
import { dataBr, num } from '../utils/formato'
import ImportarCnhModal from './ImportarCnhModal'
import { erroDaApi } from '../services/semPararApi'
import {
  viagensApi,
  type DadosCnh, type MotoristaFrota, type UsuarioMotoristaSemCadastro,
} from '../services/viagensCaminhaoApi'

// Cadastro dos motoristas (Admin, 07/10/2026). Desde 26/10/2026 mora em Cadastros → Motoristas
// (VIA-20), com os dados da CNH (importa o PDF da CNH digital) e sem placa padrão. Só escritório e dono
// (a API devolve 403 para o perfil motorista). Criar já cria o usuário de acesso
// com a senha padrão (usuário + 123); o dono pode digitar outra senha; desativado
// não entra. Usuário de motorista criado à mão, sem cadastro, aparece para completar.

type Edicao = { m: MotoristaFrota | null; base?: UsuarioMotoristaSemCadastro }

const DIA = 86_400_000
/** Dias até a validade da CNH (negativo = vencida). Datas "aaaa-mm-dd" sem fuso. */
export function diasParaVencer(validade: string | null | undefined): number | null {
  if (!validade) return null
  const [a, m, d] = validade.slice(0, 10).split('-').map(Number)
  const hoje = new Date()
  return Math.round((new Date(a, m - 1, d).getTime() - new Date(hoje.getFullYear(), hoje.getMonth(), hoje.getDate()).getTime()) / DIA)
}

/** Vencida = vermelho; vence em até 30 dias = aviso com "vence em X dias" */
export function SeloValidade({ validade }: { validade: string | null }) {
  const dias = diasParaVencer(validade)
  if (dias == null) return <Selo tom="atencao">CNH sem validade</Selo>
  if (dias < 0)
    return <span className="inline-block text-xs font-medium px-1.5 py-0.5 rounded-md border whitespace-nowrap bg-red-50 text-red-800 border-red-200">
      CNH vencida há {-dias} {dias === -1 ? 'dia' : 'dias'} ({dataBr(validade!)})</span>
  if (dias <= 30)
    return <Selo tom="atencao">{dias === 0 ? 'CNH vence hoje' : `CNH vence em ${dias} ${dias === 1 ? 'dia' : 'dias'}`} ({dataBr(validade!)})</Selo>
  return <Selo>CNH até {dataBr(validade!)}</Selo>
}

export default function MotoristasSecao({ comTitulo = false }: { comTitulo?: boolean }) {
  const [lista, setLista] = useState<MotoristaFrota[] | null>(null)
  const [semCadastro, setSemCadastro] = useState<UsuarioMotoristaSemCadastro[]>([])
  const [falhou, setFalhou] = useState(false)
  const [edicao, setEdicao] = useState<Edicao | null>(null)
  const [senhaDe, setSenhaDe] = useState<MotoristaFrota | null>(null)
  const [importar, setImportar] = useState(false)
  const confirmar = useConfirmar()

  const carregar = useCallback(() => {
    setFalhou(false)
    Promise.all([viagensApi.motoristas(), viagensApi.usuariosSemCadastro()])
      .then(([l, s]) => { setLista(l); setSemCadastro(s) })
      .catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const alternarAtivo = async (m: MotoristaFrota) => {
    if (m.ativo && !await confirmar({
      titulo: `Desativar ${m.nome}?`,
      texto: 'Ele não entra mais no painel do motorista. As viagens dele continuam no controle.',
      confirmar: 'Desativar',
    })) return
    try {
      const r = await viagensApi.ativoMotorista(m.id, !m.ativo)
      if (r.aviso) toast.error(r.aviso, { duration: 8000 })
      else toast.success(m.ativo ? `${m.nome} não entra mais.` : `${m.nome} pode entrar de novo.`)
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui alterar.')) }
  }

  return (
    <section aria-labelledby={comTitulo ? 'motoristas-cadastro' : undefined} className="space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-2">
        <div className="min-w-0">
          {comTitulo && <h2 id="motoristas-cadastro" className="text-lg font-semibold text-gray-900">Motoristas</h2>}
          <p className="text-sm text-gray-600 max-w-prose mt-0.5">
            Cada motorista entra no celular com o usuário dele e a senha padrão (o usuário seguido de 123), ou a que você definir, e vê só a folha da viagem.
          </p>
        </div>
        <div className="flex flex-wrap gap-2 shrink-0">
          <Botao variante="secundario" icone={FileUp} onClick={() => setImportar(true)}>Importar CNH (PDF)</Botao>
          <Botao variante="secundario" icone={Plus} onClick={() => setEdicao({ m: null })}>Novo motorista</Botao>
        </div>
      </div>

      {semCadastro.length > 0 && (
        <Aviso tipo="atencao" titulo={semCadastro.length === 1 ? 'Usuário de motorista sem cadastro' : 'Usuários de motorista sem cadastro'}>
          <p>Entram no sistema, mas o painel não abre até completar o cadastro (comissão).</p>
          <ul className="mt-2 space-y-2">
            {semCadastro.map(u => (
              <li key={u.id} className="flex flex-wrap items-center justify-between gap-2">
                <span className="min-w-0">
                  <b>{u.nome}</b> · usuário <b>{u.login}</b>{!u.ativo && ' · desativado'}
                </span>
                <Botao variante="secundario" tamanho="sm" icone={UserCheck} onClick={() => setEdicao({ m: null, base: u })}>
                  Completar cadastro
                </Botao>
              </li>
            ))}
          </ul>
        </Aviso>
      )}

      {falhou ? <ErroCarga mensagem="Não consegui carregar os motoristas." onTentar={carregar} />
        : !lista ? <Carregando linhas={5} rotulo="Carregando os motoristas" />
        : lista.length === 0 ? <Vazio icone={Users} titulo="Nenhum motorista cadastrado" />
        : (
          <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100 overflow-hidden">
            {lista.map(m => (
              <li key={m.id} className={`px-4 py-3 flex flex-wrap items-center gap-x-3 gap-y-2 ${m.ativo ? '' : 'bg-gray-50'}`}>
                <div className="flex-1 min-w-[12rem]">
                  <p className={`font-semibold ${m.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{m.nome}</p>
                  <p className="text-sm text-gray-600">
                    Usuário <b className="font-medium text-gray-800">{m.login}</b> · comissão {num(m.porcentagemComissao, m.porcentagemComissao % 1 ? 2 : 0)}%
                  </p>
                  <p className="text-sm text-gray-600">
                    {m.cpf ? <>CPF {m.cpf}</> : <span className="text-gray-500">Sem CPF</span>}
                    {m.cnhCategoria && <> · categoria {m.cnhCategoria}</>}
                    {m.cnhRegistro && <> · registro {m.cnhRegistro}</>}
                  </p>
                  {m.dataNascimento && (
                    <p className="text-sm text-gray-600">
                      Nascido{m.dataNascimento ? ` em ${dataBr(m.dataNascimento)}` : ''}
                    </p>
                  )}
                  <div className="flex flex-wrap gap-1 mt-1">
                    {m.ativo && <SeloValidade validade={m.cnhValidade} />}
                    {!m.ativo && <Selo>Desativado</Selo>}
                    {m.temUsuario === false && <Selo tom="atencao">Sem usuário de acesso</Selo>}
                    {m.ativo && m.temUsuario !== false && !m.acesso && <Selo tom="atencao">Sem acesso</Selo>}
                    {m.apelidos && <Selo>Na planilha: {m.apelidos}</Selo>}
                  </div>
                </div>
                <div className="flex items-center gap-1 shrink-0">
                  {m.temCnhPdf && (
                    <BotaoIcone icone={Download} rotulo={`Baixar o PDF da CNH de ${m.nome}`}
                                onClick={() => viagensApi.baixarCnh(m.id, m.nome).catch(async e => toast.error(await erroDaApi(e, 'Não consegui baixar.')))} />
                  )}
                  <BotaoIcone icone={KeyRound} rotulo={`Trocar a senha de ${m.nome}`} onClick={() => setSenhaDe(m)} />
                  <BotaoIcone icone={Pencil} rotulo={`Editar ${m.nome}`} onClick={() => setEdicao({ m })} />
                  <Botao variante={m.ativo ? 'perigoLeve' : 'secundario'} tamanho="sm" onClick={() => alternarAtivo(m)}>
                    {m.ativo ? 'Desativar' : 'Reativar'}
                  </Botao>
                </div>
              </li>
            ))}
          </ul>
        )}

      {importar && <ImportarCnhModal onClose={() => setImportar(false)} onFeito={ficaAberto => { if (!ficaAberto) setImportar(false); carregar() }} />}
      {edicao && <MotoristaModal m={edicao.m} base={edicao.base} onClose={() => setEdicao(null)} onSalvou={() => { setEdicao(null); carregar() }} />}
      {senhaDe && <SenhaModal m={senhaDe} onClose={() => setSenhaDe(null)} onSalvou={() => { setSenhaDe(null); carregar() }} />}
    </section>
  )
}

const loginSugerido = (nome: string) =>
  nome.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().split(/\s+/)[0]?.replace(/[^a-z0-9]/g, '') ?? ''

export function MotoristaModal({ m, base, onClose, onSalvou }: {
  m: MotoristaFrota | null; base?: UsuarioMotoristaSemCadastro; onClose: () => void; onSalvou: () => void
}) {
  const [nome, setNome] = useState(m?.nome ?? base?.nome.toUpperCase() ?? '')
  const [login, setLogin] = useState(m?.login ?? base?.login ?? '')
  const [apelidos, setApelidos] = useState(m?.apelidos ?? '')
  const [pct, setPct] = useState<number>(m?.porcentagemComissao ?? 6)
  const [doc, setDoc] = useState<DadosCnh>({
    cpf: m?.cpf ?? null, rg: m?.rg ?? null, rgOrgaoUf: m?.rgOrgaoUf ?? null, dataNascimento: m?.dataNascimento ?? null,
    cidadeNascimento: m?.cidadeNascimento ?? null, ufNascimento: m?.ufNascimento ?? null, cnhRegistro: m?.cnhRegistro ?? null,
    cnhCategoria: m?.cnhCategoria ?? null, cnhEmissao: m?.cnhEmissao ?? null, cnhValidade: m?.cnhValidade ?? null,
    pai: m?.pai ?? null, mae: m?.mae ?? null,
  })
  const texto = (k: keyof DadosCnh, maiusculo = true) => (
    <input value={doc[k] ?? ''} className={classeCampo}
           onChange={e => setDoc(d => ({ ...d, [k]: (maiusculo ? e.target.value.toUpperCase() : e.target.value) || null }))} />
  )
  const data = (k: keyof DadosCnh) => (
    <input type="date" value={doc[k] ?? ''} className={classeCampo} onChange={e => setDoc(d => ({ ...d, [k]: e.target.value || null }))} />
  )
  const [email, setEmail] = useState(m?.email ?? '')
  const [ativo, setAtivo] = useState(m?.ativo ?? true)
  const [salvando, setSalvando] = useState(false)
  const [loginMexido, setLoginMexido] = useState(!!m || !!base)
  const loginOriginal = m?.login ?? base?.login ?? null

  const salvar = async () => {
    if (!nome.trim()) { toast.error('Informe o nome.'); return }
    if (login.length < 3) { toast.error('O usuário precisa de pelo menos 3 letras.'); return }
    setSalvando(true)
    try {
      const r = await viagensApi.salvarMotorista(m?.id ?? null, {
        nome, login, apelidos: apelidos || null, porcentagemComissao: pct,
        ativo, usuarioId: base?.id ?? null, email: email.trim() || null, ...doc,
      })
      if (r.aviso) toast.error(r.aviso, { duration: 8000 })
      else toast.success(base ? 'Cadastro completo.' : 'Motorista salvo.')
      onSalvou()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }

  const titulo = m ? 'Editar motorista' : base ? `Completar cadastro de ${base.login}` : 'Novo motorista'
  return (
    <Modal titulo={titulo} onClose={onClose} confirmarAoFechar>
      <Campo rotulo="Nome completo">
        <input value={nome} onChange={e => { setNome(e.target.value); if (!loginMexido) setLogin(loginSugerido(e.target.value)) }} className={classeCampo} />
      </Campo>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Usuário para entrar" dica="Minúsculo, sem acento. Nome parecido leva o sobrenome (joseferraz).">
          <input value={login} onChange={e => { setLogin(e.target.value.toLowerCase().replace(/[^a-z0-9]/g, '')); setLoginMexido(true) }}
                 className={classeCampo} autoCapitalize="none" autoComplete="off" />
        </Campo>
        <Campo rotulo="Comissão (%)"><CampoNumero valor={pct} onChange={v => setPct(v ?? 0)} /></Campo>
      </div>
      <Campo rotulo="Documentos e CNH" grupo dica="Vêm do PDF da CNH digital (Importar CNH) ou podem ser digitados.">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          <Campo rotulo="CPF">{texto('cpf', false)}</Campo>
          <Campo rotulo="Data de nascimento">{data('dataNascimento')}</Campo>
          <Campo rotulo="Documento de identidade">{texto('rg')}</Campo>
          <Campo rotulo="Órgão emissor e estado">{texto('rgOrgaoUf')}</Campo>
          <Campo rotulo="Número do registro da CNH">{texto('cnhRegistro')}</Campo>
          <Campo rotulo="Categoria">{texto('cnhCategoria')}</Campo>
          <Campo rotulo="Data de emissão">{data('cnhEmissao')}</Campo>
          <Campo rotulo="Validade">{data('cnhValidade')}</Campo>
          <Campo rotulo="Pai">{texto('pai')}</Campo>
          <Campo rotulo="Mãe">{texto('mae')}</Campo>
          <Campo rotulo="E-mail" dica="Opcional. Vai ser o login do motorista quando o acesso por e-mail entrar.">
            <input type="email" value={email} className={classeCampo} autoCapitalize="none" autoComplete="off"
                   onChange={e => setEmail(e.target.value)} />
          </Campo>
        </div>
      </Campo>
      <Campo rotulo="Como aparece nas planilhas" dica="Separado por vírgula (PAULINHO, J. FERRAZ). Serve para casar as importações.">
        <input value={apelidos} onChange={e => setApelidos(e.target.value.toUpperCase())} className={classeCampo} />
      </Campo>
      <label className="inline-flex items-center gap-2 min-h-11 text-sm"><input type="checkbox" checked={ativo} onChange={e => setAtivo(e.target.checked)} className="w-5 h-5" /> Ativo (pode entrar e lançar viagens)</label>
      {!m && !base && login && <Aviso tipo="info">Ele entra com o usuário <b>{login}</b> e a senha <b>{login}123</b>.</Aviso>}
      {base && login === base.login && <Aviso tipo="info">Ele continua entrando com o usuário <b>{login}</b> e a senha que já tem.</Aviso>}
      {loginOriginal && login !== loginOriginal && login && (
        <Aviso tipo="atencao">Mudando o usuário, ele passa a entrar com <b>{login}</b> e a senha volta para a padrão (<b>{login}123</b>).</Aviso>
      )}
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

// O dono/escritório digita a senha nova do motorista (ou volta para a padrão)
export function SenhaModal({ m, onClose, onSalvou }: { m: MotoristaFrota; onClose: () => void; onSalvou: () => void }) {
  const [senha, setSenha] = useState('')
  const [salvando, setSalvando] = useState(false)
  const [tentou, setTentou] = useState(false)
  const confirmar = useConfirmar()
  const problema = senha.length < 8 ? 'Pelo menos 8 caracteres.'
    : !/[a-zA-Z]/.test(senha) ? 'Precisa de pelo menos uma letra.'
    : !/\d/.test(senha) ? 'Precisa de pelo menos um número.' : null

  const salvar = async () => {
    setTentou(true)
    if (problema) return
    setSalvando(true)
    try {
      await viagensApi.senhaMotorista(m.id, senha)
      toast.success(`Senha de ${m.nome} trocada.`)
      onSalvou()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui trocar a senha.')) }
    finally { setSalvando(false) }
  }

  const voltarPadrao = async () => {
    if (!await confirmar({ titulo: 'Voltar a senha para a padrão?', texto: `${m.nome}: entra com o usuário ${m.login} e a senha ${m.login}123.`, confirmar: 'Voltar para a padrão', perigo: false })) return
    try { await viagensApi.senhaPadrao(m.id); toast.success('Senha voltou para a padrão.'); onSalvou() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui mudar a senha.')) }
  }

  return (
    <Modal titulo={`Senha de ${m.nome}`} descricao={<>Ele entra com o usuário <b>{m.login}</b>.</>} onClose={onClose} confirmarAoFechar>
      <Campo rotulo="Senha nova" dica="8 ou mais, com letra e número. Mande para ele por WhatsApp." erro={tentou && problema ? problema : undefined}>
        <input value={senha} onChange={e => setSenha(e.target.value)} className={classeCampo} autoComplete="new-password"
               autoCapitalize="none" spellCheck={false} />
      </Campo>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Trocar a senha" />
      <div className="border-t border-gray-200 pt-3">
        <Botao variante="fantasma" tamanho="sm" icone={KeyRound} onClick={voltarPadrao}>Voltar para a padrão ({m.login}123)</Botao>
      </div>
    </Modal>
  )
}
