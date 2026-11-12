import { lazy, Suspense, type ComponentType, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { BrowserRouter, Routes, Route, useLocation, Navigate, Link } from 'react-router-dom'
import { AuthProvider, useAuth, PAINEL_MOTORISTA, PORTAL_PRODUTOSA } from './contexts/AuthContext'
import LoginPage from './pages/LoginPage'
import { CriarSenhaPage } from './pages/SenhaPages'
import { Carregando } from './components/ui/Estados'

// Cada tela vira um pedaço próprio do pacote e só baixa quando é aberta (Fase 3):
// o .js único passava de 700 kB e o celular baixava tudo para mostrar o Início.
// Login e criar senha ficam no pacote principal: são a primeira coisa que aparece.
// Depois de um deploy, a aba aberta ainda aponta para pedaços que não existem
// mais: o import falha. Recarrega a página uma vez para pegar a versão nova; se
// falhar de novo (sem internet), o erro sobe para a ProtecaoTela da rota.
const RECARREGOU = 'trx_recarregou_pedaco'
function tela<T extends ComponentType<any>>(carregar: () => Promise<{ default: T }>) {
  return lazy(() => carregar().then(m => {
    try { sessionStorage.removeItem(RECARREGOU) } catch { /* sem armazenamento */ }
    return m
  }).catch(erro => {
    let jaTentou = true
    try { jaTentou = sessionStorage.getItem(RECARREGOU) === '1'; sessionStorage.setItem(RECARREGOU, '1') } catch { /* sem armazenamento */ }
    if (!jaTentou && navigator.onLine) { window.location.reload(); return new Promise<{ default: T }>(() => {}) }
    throw erro
  }))
}

const DashboardPage = tela(() => import('./pages/DashboardPage'))
const FreteCalculoPage = tela(() => import('./pages/FreteCalculoPage'))
const BoletosPage = tela(() => import('./pages/BoletosPage'))
const FinanceiroPage = tela(() => import('./pages/FinanceiroPage'))
const SaldosPage = tela(() => import('./pages/SaldosPage'))
const StradaAgrotradePage = tela(() => import('./pages/StradaAgrotradePage'))
const PlanilhasPage = tela(() => import('./pages/PlanilhasPage'))
const CTeExportPage = tela(() => import('./pages/CTeExportPage'))
const SemPararPage = tela(() => import('./pages/SemPararPage'))
const AbastecimentoPage = tela(() => import('./pages/AbastecimentoPage'))
const AcertoAlfaPage = tela(() => import('./pages/AcertoAlfaPage'))
const NotasFiscaisPage = tela(() => import('./pages/NotasFiscaisPage'))
const EstoqueProdutosAPage = tela(() => import('./pages/EstoqueProdutosAPage'))
const HistoricoPortalProdutosAPage = tela(() => import('./pages/HistoricoPortalProdutosAPage'))
const EstoqueProdutosBPage = tela(() => import('./pages/EstoqueProdutosBPage'))
const PrecosPage = tela(() => import('./pages/PrecosPage'))
const VolumesEmbaladosPage = tela(() => import('./pages/VolumesEmbaladosPage'))
const EmbaladosQuebrasPage = tela(() => import('./pages/EmbaladosQuebrasPage'))
const EmbaladosDespesasPage = tela(() => import('./pages/EmbaladosDespesasPage'))
const EmbaladosComissoesPage = tela(() => import('./pages/EmbaladosComissoesPage'))
const EmbaladosSalariosPage = tela(() => import('./pages/EmbaladosSalariosPage'))
const BarracaoPage = tela(() => import('./pages/BarracaoPage'))
const DespesasDivididasPage = tela(() => import('./pages/DespesasDivididasPage'))
const UsuariosPage = tela(() => import('./pages/UsuariosPage'))
const EstoqueProdutosCPage = tela(() => import('./pages/EstoqueProdutosCPage'))
const PlacasPage = tela(() => import('./pages/PlacasPage'))
const VolumesAnotarPage =tela(() => import('./pages/VolumesAnotarPage'))
const VolumesControlePage = tela(() => import('./pages/VolumesControlePage'))
const PerfilPage = tela(() => import('./pages/PerfilPage'))
const TiposVolumePage = tela(() => import('./pages/TiposVolumePage'))
const EmailsClientePage = tela(() => import('./pages/EmailsClientePage'))
const ImportarCTePage = tela(() => import('./pages/ImportarCTePage'))
const ViagensCaminhaoPage = tela(() => import('./pages/ViagensCaminhaoPage'))
const ViagemCaminhaoPage = tela(() => import('./pages/ViagemCaminhaoPage'))
const RetornosPage = tela(() => import('./pages/RetornosPage'))
const MotoristaPainelPage = tela(() => import('./pages/MotoristaPainelPage'))
const PortalProdutosAPage = tela(() => import('./pages/PortalProdutosAPage'))
const PortalEntradaPage = tela(() => import('./pages/PortalEntradaPage'))
const BuscaGeral = tela(() => import('./components/navegacao/BuscaGeral'))
import ProtecaoTela from './components/ui/ProtecaoTela'
import ServidorAcordando from './components/ui/ServidorAcordando'
import { ProvedorConfirmar } from './components/ui/Confirmar'
import MenuLateral, { Marca } from './components/navegacao/MenuLateral'
import BarraAtalhos from './components/navegacao/BarraAtalhos'
import { areaDaRota, paginaAtual, primeiraLiberada, temRodapeProprio } from './components/navegacao/menu'
import Aviso from './components/ui/Aviso'
import Botao from './components/ui/Botao'
import { Disc3, LogOut, Menu, NotebookPen, Search } from 'lucide-react'

// O menu (grupos, nomes e ícones) mora em components/navegacao/menu.ts (UI-16).
export { Marca }

function Layout() {
  const [menuAberto, setMenuAberto] = useState(false)
  const [buscaAberta, setBuscaAberta] = useState(false)
  const location = useLocation()
  const { pode } = useAuth()
  const atual = paginaAtual(location.pathname)
  // Tela sem permissão (USU-11): o aviso fica no lugar dela, com o menu. O site só
  // esconde; quem barra de verdade é a API.
  const areaTela = areaDaRota(location.pathname)
  const semAcesso = areaTela !== null && !pode(areaTela)
  const primeira = primeiraLiberada(pode)
  const temBusca = pode('inicio')
  // Tela com rodapé fixo próprio (Salvar do Anotar): sem a barra de baixo, e o
  // botão do menu volta para a barra de cima
  const semBarra = temRodapeProprio(location.pathname)
  const coluna = useRef<HTMLDivElement>(null)

  // A gaveta fecha sozinha ao trocar de tela, inclusive pelo voltar do navegador.
  // Esc, foco preso e o X ficam no MenuLateral (D-20).
  useEffect(() => { setMenuAberto(false) }, [location.pathname])
  // Busca única (F-02): Ctrl+K no Windows, Cmd+K no Mac, de qualquer tela. Com
  // uma janela aberta por cima, não abre outra.
  useEffect(() => {
    if (!temBusca) return   // a busca geral é do Início: sem ele, some
    const tecla = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.altKey || e.key.toLowerCase() !== 'k') return
      e.preventDefault()
      if (document.querySelector('[aria-modal="true"]')) return
      setMenuAberto(false)
      setBuscaAberta(true)
    }
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [temBusca])
  // Nome da tela na aba do navegador e no histórico
  useEffect(() => { document.title = `${atual.label} · TRX Transportes` }, [atual.label])
  // Com a gaveta aberta, o resto da tela sai do alcance do teclado e do leitor de tela.
  // useLayoutEffect: tira o inert antes de a gaveta devolver o foco ao botão Menu.
  useLayoutEffect(() => { coluna.current?.toggleAttribute('inert', menuAberto) }, [menuAberto])
  // Pré-carrega o pedaço da busca depois que a tela abriu: o primeiro Ctrl+K não espera a rede
  useEffect(() => { const t = setTimeout(() => { import('./components/navegacao/BuscaGeral') }, 2500); return () => clearTimeout(t) }, [])

  // Altura da tela visível (h-tela, em index.css): no celular o 100vh conta a
  // barra de endereço e a parte de baixo da página ficava escondida.
  return (
    <div className="flex h-tela overflow-hidden">
      <MenuLateral aberto={menuAberto} onFechar={() => setMenuAberto(false)} />

      <div ref={coluna} className="flex-1 min-w-0 flex flex-col overflow-hidden">
        {/* Barra de cima */}
        <header className="bg-white/90 backdrop-blur border-b border-gray-200 px-4 sm:px-6 lg:px-8 h-14 lg:h-16 flex items-center gap-2 lg:gap-3 flex-shrink-0">
          {semBarra && (
            <button
              type="button"
              className="lg:hidden -ml-2 w-11 h-11 flex items-center justify-center rounded-lg text-gray-600 hover:bg-gray-100"
              onClick={() => setMenuAberto(true)}
              aria-haspopup="dialog"
              aria-expanded={menuAberto}
              aria-controls="menu-principal"
            >
              <span className="sr-only">Abrir menu</span>
              <Menu size={22} aria-hidden />
            </button>
          )}
          {/* O nome da tela mora só aqui, igual ao do menu (D-03, UI-13): as telas
              não repetem o título embaixo. É o h1 da página e o nível 1 da escala
              (D-19): Semi Condensed 600, 20 px no celular e 24 px no computador. */}
          <div className="flex items-baseline gap-2 min-w-0">
            {atual.secao && (
              <>
                <span className="font-display text-[15px] font-medium text-gray-500 hidden sm:inline whitespace-nowrap">{atual.secao}</span>
                <span className="text-gray-300 hidden sm:inline" aria-hidden>/</span>
              </>
            )}
            <h1 className="font-display text-xl lg:text-2xl font-semibold tracking-tight text-gray-900 truncate">{atual.label}</h1>
          </div>

          {/* Busca única (F-02): ícone de 44 px no celular; no computador, a caixa
              com o atalho, para ele aprender o Ctrl+K */}
          {temBusca && <button
            type="button"
            onClick={() => setBuscaAberta(true)}
            aria-haspopup="dialog"
            aria-keyshortcuts="Control+K Meta+K"
            className="ml-auto -mr-2 sm:mr-0 shrink-0 inline-flex items-center gap-2 w-11 h-11 justify-center rounded-lg text-gray-600 hover:bg-gray-100 hover:text-gray-900
                       md:w-64 lg:w-72 md:h-10 md:justify-start md:px-3 md:border md:border-gray-200 md:bg-gray-50 md:hover:bg-white md:hover:border-gray-300 md:text-gray-500"
          >
            <Search size={20} className="md:w-[18px] md:h-[18px] shrink-0" aria-hidden />
            <span className="sr-only md:not-sr-only md:flex-1 md:text-left md:text-sm">Buscar</span>
            <kbd className="hidden md:inline-flex items-center h-6 px-1.5 rounded border border-gray-200 bg-white font-sans text-[11px] font-medium text-gray-500" aria-hidden>
              Ctrl K
            </kbd>
          </button>}
        </header>

        {/* "Ligando o servidor" quando alguma resposta passa de 4 s (D-02) */}
        <ServidorAcordando />

        {/* Conteúdo. Respiro (D-19): 16 px no celular, 24 no tablet, 32 no computador */}
        <main className="flex-1 min-w-0 overflow-y-auto overflow-x-hidden overscroll-contain px-4 pt-4 pb-10 sm:px-6 sm:pt-5 lg:px-8 lg:pt-6 lg:pb-12">
          {/* Cada tela entra com um leve deslize: a troca fica visível sem distrair */}
          <div key={location.pathname} className="animate-entrada">
          {/* Se a tela quebrar, só ela troca pelo aviso; o menu fica (D-01) */}
          <ProtecaoTela key={location.pathname}>
          {/* Enquanto o pedaço da tela baixa, o esqueleto das peças (UI-08) */}
          <Suspense fallback={<Carregando linhas={4} rotulo="Abrindo a tela" />}>
          {semAcesso ? (
            <div className="max-w-lg">
              <Aviso tipo="atencao" titulo="Você não tem acesso a esta tela"
                     acao={<Link to={primeira} className="inline-flex items-center min-h-11 font-medium text-tinta-700 underline underline-offset-2 hover:text-tinta-900">Ir para {paginaAtual(primeira).label}</Link>}>
                Se precisar dela, peça ao Admin ou ao Bento.
              </Aviso>
            </div>
          ) : (
          <Routes>
            <Route path="/" element={pode('inicio') ? <DashboardPage /> : <Navigate to={primeira} replace />} />
            <Route path="/frete" element={<FreteCalculoPage />} />
            {/* "Planilhas NF-e" virou a tela Romaneios (09/10/2026) */}
            <Route path="/planilhas" element={<Navigate to="/romaneios" replace />} />
            <Route path="/notas" element={<NotasFiscaisPage />} />
            <Route path="/romaneios" element={<PlanilhasPage />} />
            <Route path="/boletos" element={<BoletosPage />} />
            <Route path="/financeiro" element={<FinanceiroPage />} />
            <Route path="/despesas-divididas" element={<DespesasDivididasPage />} />
            <Route path="/saldos" element={<SaldosPage />} />
            <Route path="/agrotrade-tip" element={<StradaAgrotradePage />} />
            <Route path="/cte-export" element={<CTeExportPage />} />
            <Route path="/cte-export/importar" element={<ImportarCTePage />} />
            <Route path="/semparar" element={<SemPararPage />} />
            <Route path="/viagens-caminhoes" element={<ViagensCaminhaoPage />} />
            <Route path="/viagens-caminhoes/nova" element={<ViagemCaminhaoPage />} />
            <Route path="/viagens-caminhoes/:id" element={<ViagemCaminhaoPage />} />
            {/* Motoristas virou Usuários (USU-11): lista única de pessoas */}
            <Route path="/cadastros/usuarios" element={<UsuariosPage />} />
            <Route path="/cadastros/motoristas" element={<Navigate to="/cadastros/usuarios" replace />} />
            <Route path="/cadastros/placas" element={<PlacasPage />} />
            <Route path="/retornos" element={<RetornosPage />} />
            <Route path="/abastecimentos" element={<AbastecimentoPage />} />
            <Route path="/acerto-alfa" element={<AcertoAlfaPage />} />
            <Route path="/tipos-volume" element={<TiposVolumePage />} />
            <Route path="/emails-clientes" element={<EmailsClientePage />} />
            <Route path="/estoque/produtosA" element={<EstoqueProdutosAPage />} />
            <Route path="/estoque/produtosA/historico-portal" element={<HistoricoPortalProdutosAPage />} />
            {/* Portal das produtosA (EST-09): o escritório anota com o login dele, dentro do sistema (USU-05) */}
            <Route path="/produtosA" element={<PortalProdutosAPage dentroDoSistema />} />
            <Route path="/estoque/produtosB" element={<EstoqueProdutosBPage key="produtosB" />} />
            <Route path="/estoque/produtosC" element={<EstoqueProdutosCPage />} />
            <Route path="/precos" element={<PrecosPage />} />
            <Route path="/estoque/volumes" element={<VolumesEmbaladosPage />} />
            <Route path="/embalados/quebras" element={<EmbaladosQuebrasPage />} />
            <Route path="/embalados/despesas" element={<EmbaladosDespesasPage />} />
            <Route path="/embalados/comissoes" element={<EmbaladosComissoesPage />} />
            <Route path="/embalados/salarios" element={<EmbaladosSalariosPage />} />
            <Route path="/barracao/comissoes" element={<BarracaoPage key="comissoes" tipo="comissoes" />} />
            <Route path="/barracao/despesas" element={<BarracaoPage key="despesas" tipo="despesas" />} />
            <Route path="/estoque/volumes/anotar" element={<VolumesAnotarPage />} />
            <Route path="/estoque/volumes/:id/editar" element={<VolumesAnotarPage />} />
            <Route path="/estoque/volumes/:id" element={<VolumesControlePage />} />
            <Route path="/perfil" element={<PerfilPage />} />
            {/* Endereços antigos: tudo isso agora mora no perfil */}
            <Route path="/usuarios" element={<Navigate to="/cadastros/usuarios" replace />} />
            <Route path="/minha-senha" element={<Navigate to="/perfil" replace />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
          )}
          </Suspense>
          </ProtecaoTela>
          </div>
        </main>

        {/* Atalhos embaixo no celular (F-04): no fluxo, então o conteúdo encolhe e
            nada fica atrás dela */}
        {!semBarra && <BarraAtalhos menuAberto={menuAberto} onAbrirMenu={() => setMenuAberto(true)} />}
      </div>

      {buscaAberta && (
        <Suspense fallback={null}>
          <BuscaGeral onFechar={() => setBuscaAberta(false)} />
        </Suspense>
      )}
    </div>
  )
}

// Guard: redireciona para /login se não autenticado
function PrivateRoutes() {
  const { token, usuario, loading, acessoTotal, pode } = useAuth()
  const location = useLocation()

  if (loading) {
    // Primeira carga (confere o login guardado). Com a API dormindo isso leva até
    // um minuto: a marca fica no meio e o aviso explica a espera.
    return (
      <div className="h-tela bg-menu text-white flex flex-col items-center justify-center gap-8 px-6">
        <Marca />
        <ServidorAcordando variante="cartao" />
      </div>
    )
  }

  // /produtosA (o QR do barracão) sem login: a entrada pública do portal (entrar, criar conta,
  // esqueci a senha). Logado, segue o caminho de sempre (perfil produtosA: o portal; escritório:
  // o portal dentro do sistema; motorista: a folha dele). Fica aqui, e não numa rota própria
  // "/produtosA" lá em cima: as rotas do Layout casariam só o resto do endereço ("/") e o
  // escritório caía no Início em vez do portal.
  if (!token) {
    if (ehEntradaPortal(location.pathname)) return (
      <ProtecaoTela>
        <Suspense fallback={<div className="h-tela bg-menu" />}>
          <PortalEntradaPage />
        </Suspense>
      </ProtecaoTela>
    )
    return <Navigate to="/login" replace />
  }

  // Senha provisória do e-mail: primeiro cria a própria, depois entra.
  if (usuario?.trocarSenha) return <CriarSenhaPage />

  const caminho = semBarraFinal(location.pathname)

  // Sem acesso total (USU-11, ajuste D): sem Início, menu nem abas, só os painéis.
  // Com um painel, entra direto nele; com os dois, a raiz mostra os dois botões;
  // qualquer outro endereço volta para lá. A API também barra o resto.
  if (!acessoTotal) {
    const viagens = pode(PAINEL_MOTORISTA)
    const produtosA = pode(PORTAL_PRODUTOSA)
    if (viagens && produtosA) {
      if (caminho === ROTA_VIAGENS) return <FolhaSozinha voltar={{ to: '/', rotulo: 'Voltar aos painéis' }} />
      if (caminho === '/produtosA') return <PortalSozinho voltar={{ to: '/', rotulo: 'Voltar aos painéis' }} sairPara="/login" />
      if (caminho === '/') return <EscolhaPainel />
      return <Navigate to="/" replace />
    }
    if (viagens) return caminho === ROTA_VIAGENS ? <FolhaSozinha /> : <Navigate to={ROTA_VIAGENS} replace />
    if (produtosA) return caminho === '/produtosA' ? <PortalSozinho /> : <Navigate to="/produtosA" replace />
    return <SemPainel />
  }

  // Acesso total com o painel do motorista (Bento): a mesma folha, em tela cheia,
  // com o caminho de volta ao sistema. Sem a área, o Layout mostra o aviso.
  if (caminho === ROTA_VIAGENS && pode(PAINEL_MOTORISTA))
    return <FolhaSozinha voltar={{ to: '/', rotulo: 'Voltar ao sistema' }} />

  return (
    <ProvedorConfirmar>
      <Layout />
    </ProvedorConfirmar>
  )
}

type Voltar = { to: string; rotulo: string }

function PortalSozinho({ voltar, sairPara }: { voltar?: Voltar; sairPara?: string }) {
  return (
    <ProvedorConfirmar>
      <ProtecaoTela>
        <Suspense fallback={<div className="h-tela bg-gray-50 p-4"><Carregando linhas={4} rotulo="Abrindo as produtosA" /></div>}>
          <PortalProdutosAPage voltar={voltar} sairPara={sairPara} />
        </Suspense>
      </ProtecaoTela>
    </ProvedorConfirmar>
  )
}

// Painel do motorista (Controle de viagens) em tela cheia, sem menu
function FolhaSozinha({ voltar }: { voltar?: Voltar }) {
  useEffect(() => { document.title = 'Controle de viagens · TRX Transportes' }, [])
  return (
    <ProvedorConfirmar>
      <ProtecaoTela>
        <Suspense fallback={<div className="h-tela bg-gray-50 p-4"><Carregando linhas={4} rotulo="Abrindo a folha" /></div>}>
          <MotoristaPainelPage voltar={voltar} />
        </Suspense>
      </ProtecaoTela>
    </ProvedorConfirmar>
  )
}

// Quem tem os dois painéis (ex.: Caio): uma tela só, feita para o celular, com os
// dois botões no meio e o sair (USU-11, resposta 6)
function EscolhaPainel() {
  const { usuario, logout } = useAuth()
  useEffect(() => { document.title = 'TRX Transportes' }, [])
  const botao = 'w-full min-h-[5.5rem] rounded-2xl bg-white text-tinta-900 shadow-lg flex items-center gap-4 px-5 text-left font-display text-xl font-semibold hover:bg-tinta-50 active:scale-[0.99] transition'
  return (
    <div className="h-tela overflow-y-auto bg-menu text-white flex flex-col px-4 pt-[max(1.5rem,env(safe-area-inset-top))] pb-[max(1.5rem,env(safe-area-inset-bottom))]">
      <div className="w-full max-w-sm mx-auto"><Marca /></div>
      <main className="flex-1 w-full max-w-sm mx-auto flex flex-col justify-center gap-4 py-8">
        {usuario?.nome && <p className="text-tinta-200 text-center mb-2">Olá, {usuario.nome}</p>}
        <Link to="/produtosA" className={botao}>
          <Disc3 size={30} strokeWidth={1.75} className="shrink-0 text-tinta-700" aria-hidden />
          Controle de produtosA
        </Link>
        <Link to={ROTA_VIAGENS} className={botao}>
          <NotebookPen size={30} strokeWidth={1.75} className="shrink-0 text-tinta-700" aria-hidden />
          Controle de viagens
        </Link>
      </main>
      <div className="w-full max-w-sm mx-auto">
        <button type="button" onClick={logout}
                className="w-full min-h-12 inline-flex items-center justify-center gap-2 rounded-xl text-tinta-100 hover:bg-white/10 font-medium">
          <LogOut size={20} aria-hidden /> Sair
        </button>
      </div>
    </div>
  )
}

// Logado, mas sem nenhum painel nem acesso ao sistema
function SemPainel() {
  const { logout } = useAuth()
  return (
    <div className="h-tela bg-gray-50 flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <Aviso tipo="atencao" titulo="Você não tem acesso a nenhuma tela"
               acao={<Botao icone={LogOut} onClick={logout}>Sair</Botao>}>
          Peça a liberação ao Admin ou ao Bento.
        </Aviso>
      </div>
    </div>
  )
}

const ROTA_VIAGENS = '/controle-viagens'

// Endereço sem a barra do fim e em minúsculas ("/produtosA/" e "/ProdutosA" valem)
function semBarraFinal(pathname: string) {
  const p = pathname.replace(/\/+$/, '').toLowerCase()
  return p === '' ? '/' : p
}

// O endereço do QR do barracão (aceita "/produtosA/" e maiúsculas, como a rota fazia)
function ehEntradaPortal(pathname: string) {
  return semBarraFinal(pathname) === '/produtosA'
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<PublicRoute />} />
          <Route path="/*" element={<PrivateRoutes />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}

// Redireciona para / se já estiver logado
function PublicRoute() {
  const { token, loading } = useAuth()
  if (loading) return null
  if (token) return <Navigate to="/" replace />
  return <LoginPage />
}
