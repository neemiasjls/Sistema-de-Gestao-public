import { useCallback, useEffect, useMemo, useState } from 'react'
import { FileUp, Plus, Search, Users } from 'lucide-react'
import Botao from '../ui/Botao'
import ImportarCnhModal from '../ImportarCnhModal'
import EditorPessoa from './EditorPessoa'
import NovaPessoa from './NovaPessoa'
import { Carregando, ErroCarga, Vazio } from '../ui/Estados'
import { Segmentado } from '../ui/Abas'
import Campo from '../ui/Campo'
import { classeCampo } from '../EstoquePecas'
import { Selo } from '../viagens/ViagensPecas'
import { SeloValidade } from '../MotoristasSecao'
import { usuariosApi, type Classificacao, type Pessoa } from '../../services/usuariosApi'

// Lista única da tela "Usuários" (USU-11, Parte 9): cada pessoa uma vez, com as classificações
// (Usuário, Motorista, Portal das produtosA), os logins, a situação e o que acessa. Só o administrador.
// Clicar na pessoa abre o editor (Parte 10).

type Filtro = 'todos' | Classificacao

const FILTROS = [
  ['todos', 'Todos'],
  ['usuario', 'Usuários'],
  ['motorista', 'Motoristas'],
  ['portal', 'Portal das produtosA'],
] as const

const NOME_CLASSIFICACAO: Record<Classificacao, string> = {
  usuario: 'Usuário',
  motorista: 'Motorista',
  portal: 'Portal das produtosA',
}

const semAcento = (t: string) => t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()

function SelosClassificacao({ p }: { p: Pessoa }) {
  if (p.classificacoes.length === 0) return <Selo tom="atencao">Sem classificação</Selo>
  return <>{p.classificacoes.map(c => <Selo key={c} tom="info">{NOME_CLASSIFICACAO[c] ?? c}</Selo>)}</>
}

function Logins({ p }: { p: Pessoa }) {
  if (p.logins.length === 0) return <Selo tom="atencao">Sem login</Selo>
  return (
    <span className="flex flex-col gap-0.5">
      {p.logins.map(l => (
        <span key={l.usuarioId} className={`break-all ${l.ativo ? 'text-gray-800' : 'text-gray-500 line-through'}`}>
          {l.login}
          {l.trocarSenha && <span className="text-xs text-amber-800"> · senha provisória</span>}
        </span>
      ))}
    </span>
  )
}

function Situacao({ p }: { p: Pessoa }) {
  return (
    <span className="flex flex-wrap gap-1">
      {p.ativo ? <Selo>Ativo</Selo> : <Selo tom="atencao">Desativado</Selo>}
      {p.administrador && <Selo tom="info">Administrador</Selo>}
    </span>
  )
}

function Acesso({ p }: { p: Pessoa }) {
  if (p.acessoTotal) return <Selo tom="info">Acesso total</Selo>
  if (!p.controleViagens && !p.controleProdutosA) return <Selo tom="atencao">Sem acesso</Selo>
  return (
    <span className="flex flex-wrap gap-1">
      {p.controleViagens && <Selo>Controle de viagens</Selo>}
      {p.controleProdutosA && <Selo>Controle de produtosA</Selo>}
    </span>
  )
}

function Cnh({ p }: { p: Pessoa }) {
  if (!p.classificacoes.includes('motorista')) return <span className="text-sm text-gray-500">Não é motorista</span>
  return <SeloValidade validade={p.cnhValidade} />
}

function Avisos({ p }: { p: Pessoa }) {
  if (p.avisos.length === 0) return null
  return (
    <span className="flex flex-wrap gap-1 mt-1">
      {p.avisos.map((a, i) => <Selo key={i} tom="atencao">{a}</Selo>)}
    </span>
  )
}

export default function ListaPessoas() {
  const [pessoas, setPessoas] = useState<Pessoa[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [filtro, setFiltro] = useState<Filtro>('todos')
  const [busca, setBusca] = useState('')
  const [desativados, setDesativados] = useState(false)
  const [aberta, setAberta] = useState<Pessoa | null>(null)
  const [nova, setNova] = useState(false)
  const [importar, setImportar] = useState(false)

  const carregar = useCallback(() => {
    setFalhou(false)
    setPessoas(null)
    usuariosApi.pessoas().then(setPessoas).catch(() => setFalhou(true))
  }, [])
  // Depois de gravar: recarrega sem piscar a lista e mantém o editor na mesma pessoa
  // (a chave muda quando o login ou o cadastro é juntado)
  const recarregar = useCallback(() => {
    usuariosApi.pessoas().then(lista => {
      setPessoas(lista)
      setAberta(a => a && (lista.find(p => p.chave === a.chave)
        ?? lista.find(p => (a.motoristaId != null && p.motoristaId === a.motoristaId) || (a.usuarioId != null && p.usuarioId === a.usuarioId))
        ?? null))
    }).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const visiveis = useMemo(() => {
    if (!pessoas) return []
    const termo = semAcento(busca.trim())
    return pessoas
      .filter(p => desativados || p.ativo)
      .filter(p => filtro === 'todos' || p.classificacoes.includes(filtro))
      .filter(p => !termo || semAcento(p.nome).includes(termo) || p.logins.some(l => semAcento(l.login).includes(termo)))
      .sort((a, b) => a.nome.localeCompare(b.nome, 'pt-BR'))
  }, [pessoas, filtro, busca, desativados])

  return (
    <section aria-labelledby="pessoas" className="space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-2">
        <div className="min-w-0">
          <h2 id="pessoas" className="text-lg font-semibold text-gray-900">Pessoas</h2>
          <p className="text-sm text-gray-600 mt-0.5">Todos que entram no sistema, uma vez cada, com o que cada um acessa. Toque na pessoa para editar.</p>
        </div>
        <div className="flex flex-wrap gap-2 shrink-0">
          <Botao variante="secundario" icone={FileUp} onClick={() => setImportar(true)}>Importar CNH (PDF)</Botao>
          <Botao variante="primario" icone={Plus} onClick={() => setNova(true)}>Nova pessoa</Botao>
        </div>
      </div>

      <div className="flex flex-col lg:flex-row lg:items-end gap-3">
        <div className="overflow-x-auto">
          <Segmentado valor={filtro} opcoes={FILTROS} onChange={setFiltro} rotulo="Classificação" />
        </div>
        <div className="flex-1 min-w-0 sm:max-w-xs">
          <Campo rotulo="Buscar por nome">
            <div className="relative">
              <Search className="w-4 h-4 text-gray-500 absolute left-3 top-1/2 -translate-y-1/2" aria-hidden />
              <input value={busca} onChange={e => setBusca(e.target.value)} className={`${classeCampo} pl-9`}
                     placeholder="Nome ou login" type="search" />
            </div>
          </Campo>
        </div>
        <label className="flex items-center gap-2 text-sm text-gray-700 py-2 cursor-pointer">
          <input type="checkbox" checked={desativados} onChange={e => setDesativados(e.target.checked)} className="w-4 h-4" />
          Mostrar desativados
        </label>
      </div>

      {falhou ? <ErroCarga mensagem="Não consegui carregar as pessoas." onTentar={carregar} />
        : !pessoas ? <Carregando linhas={6} rotulo="Carregando as pessoas" />
        : pessoas.length === 0 ? <Vazio icone={Users} titulo="Ninguém cadastrado ainda" />
        : visiveis.length === 0 ? <Vazio icone={Search} titulo="Ninguém com esse filtro">Troque a classificação, a busca ou marque "Mostrar desativados".</Vazio>
        : (
          <>
            <p className="text-sm text-gray-600">{visiveis.length} {visiveis.length === 1 ? 'pessoa' : 'pessoas'}</p>

            {/* Celular: cartões (UI-05) */}
            <ul className="sm:hidden space-y-2">
              {visiveis.map(p => (
                <li key={p.chave}>
                 <button type="button" onClick={() => setAberta(p)} aria-label={`Editar ${p.nome}`}
                         className={`w-full text-left border border-gray-200 rounded-xl p-3 space-y-2 hover:border-tinta-300 focus-visible:outline focus-visible:outline-2 focus-visible:outline-tinta-500 ${p.ativo ? 'bg-white' : 'bg-gray-50'}`}>
                  <div>
                    <p className={`font-semibold ${p.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{p.nome}</p>
                    <span className="flex flex-wrap gap-1 mt-1"><SelosClassificacao p={p} /></span>
                    <Avisos p={p} />
                  </div>
                  <dl className="grid grid-cols-[auto,1fr] gap-x-3 gap-y-1.5 text-sm items-start">
                    <dt className="text-gray-600">Login</dt><dd><Logins p={p} /></dd>
                    <dt className="text-gray-600">Situação</dt><dd><Situacao p={p} /></dd>
                    <dt className="text-gray-600">Acessa</dt><dd><Acesso p={p} /></dd>
                    <dt className="text-gray-600">CNH</dt><dd><Cnh p={p} /></dd>
                  </dl>
                 </button>
                </li>
              ))}
            </ul>

            {/* Tela larga: tabela */}
            <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-left text-gray-600">
                  <tr>
                    <th className="px-4 py-2 font-medium">Nome</th>
                    <th className="px-4 py-2 font-medium">Login</th>
                    <th className="px-4 py-2 font-medium">Situação</th>
                    <th className="px-4 py-2 font-medium">Acessa</th>
                    <th className="px-4 py-2 font-medium">CNH</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {visiveis.map(p => (
                    <tr key={p.chave} onClick={() => setAberta(p)}
                        className={`align-top cursor-pointer hover:bg-tinta-50/50 ${p.ativo ? '' : 'bg-gray-50'}`}>
                      <td className="px-4 py-3 min-w-[12rem]">
                        <button type="button" onClick={e => { e.stopPropagation(); setAberta(p) }}
                                className={`font-semibold text-left hover:underline ${p.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{p.nome}</button>
                        <span className="flex flex-wrap gap-1 mt-1"><SelosClassificacao p={p} /></span>
                        <Avisos p={p} />
                      </td>
                      <td className="px-4 py-3"><Logins p={p} /></td>
                      <td className="px-4 py-3"><Situacao p={p} /></td>
                      <td className="px-4 py-3"><Acesso p={p} /></td>
                      <td className="px-4 py-3"><Cnh p={p} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}

      {aberta && <EditorPessoa p={aberta} onClose={() => setAberta(null)} onMudou={recarregar} />}
      {nova && <NovaPessoa onClose={() => setNova(false)} onCriou={recarregar} />}
      {importar && <ImportarCnhModal onClose={() => setImportar(false)} onFeito={ficaAberto => { if (!ficaAberto) setImportar(false); recarregar() }} />}
    </section>
  )
}
