import {
  House, Truck, ClipboardList, Fuel, Mail, PackageOpen, Disc3, Boxes, Banknote, Receipt,
  Landmark, HandCoins, FileText, Route as Rota, Egg, FolderArchive, Milestone, ArrowLeftRight,
  Grid3x3, Tags, EggOff, ShoppingBag, Wallet, Split, IdCard, Container, NotebookPen,
  type LucideIcon,
} from 'lucide-react'

// O que existe no menu e como se agrupa (D-14, UI-16).
//
// Os grupos seguem o jeito que ele trabalha (rotina.md), não o tipo de tela:
//   Dia a dia       o que se faz toda semana: viagens, romaneios por cliente,
//                   faturas de posto e o e-mail de terça com os boletos
//   Estoques        o barracão: volumes embalados, produtosA e produtosB (EST, VOLUME)
//   Financeiro      contas: boletos, acerto da fazenda, financeiro e saldos
//   Fechamento do mês  as planilhas mensais: seguro (SG nos CT-es) e Sem Parar
//   Cadastros e arquivos  o que se ajusta de vez em quando: tipos de volumes,
//                   notas guardadas e a planilha avulsa de NF-e
// Antes "Documentos" juntava cadastro (Tipos de volumes) com envio (E-mails), e
// Estoques ficava sem seção, escondido num sub-menu que custava um toque a mais.

// Cada item leva a chave da área (USU-11), a mesma do catálogo do servidor
// (AreasAcesso.Areas). O teste "menu" do Conferir falha se a chave não existir lá.
export type ItemMenu = {
  to: string
  label: string
  icon: LucideIcon
  area: string
  /** Só acende com o endereço exato (Início) */
  exact?: boolean
}

export type SecaoMenu = { titulo?: string; itens: ItemMenu[] }

export const secoesMenu: SecaoMenu[] = [
  {
    itens: [
      { to: '/', label: 'Início', icon: House, exact: true, area: 'inicio' },
      // Atalho de quem é Motorista + Usuário (USU-11): só aparece com a área escrita
      { to: '/controle-viagens', label: 'Controle de viagens', icon: NotebookPen, area: 'painel-motorista' },
    ],
  },
  {
    titulo: 'Dia a dia',
    itens: [
      { to: '/frete',           label: 'Viagens',              icon: Truck, area: 'viagens' },
      { to: '/viagens-caminhoes', label: 'Acerto viagens', icon: Milestone, area: 'acerto-viagens' },
      { to: '/retornos',        label: 'Retornos',             icon: ArrowLeftRight, area: 'retornos' },
      { to: '/romaneios',       label: 'Romaneios',            icon: ClipboardList, area: 'romaneios' },
      { to: '/abastecimentos',  label: 'Abastecimentos',       icon: Fuel, area: 'abastecimentos' },
      { to: '/emails-clientes', label: 'E-mails aos clientes', icon: Mail, area: 'emails-clientes' },
    ],
  },
  {
    // Módulo Embalados (VOLUME-16): o barracão e o que era a planilha COMISSÕES VOLUMES EMBALADOS
    titulo: 'Embalados',
    itens: [
      { to: '/estoque/volumes',         label: 'Controle embalados',  icon: PackageOpen, area: 'volumes-embalados' },
      { to: '/embalados/quebras',    label: 'Quebras',             icon: EggOff, area: 'embalados-quebras' },
      { to: '/embalados/despesas',   label: 'Despesas embalados', icon: ShoppingBag, area: 'embalados-despesas' },
      { to: '/embalados/comissoes',  label: 'Comissões',           icon: HandCoins, area: 'embalados-comissoes' },
      { to: '/embalados/salarios',   label: 'Salários',            icon: Wallet, area: 'embalados-salarios' },
    ],
  },
  {
    titulo: 'Estoques',
    itens: [
      { to: '/barracao/comissoes', label: 'Comissões',      icon: HandCoins, area: 'barracao-comissoes' },
      { to: '/estoque/produtosB', label: 'ProdutosB',     icon: Boxes, area: 'produtosB' },
      { to: '/estoque/produtosA',    label: 'ProdutosA',        icon: Disc3, area: 'produtosA' },
      { to: '/estoque/produtosC',   label: 'ProdutosC',       icon: Grid3x3, area: 'produtosC' },
    ],
  },
  {
    titulo: 'Barracão',
    itens: [
      { to: '/barracao/despesas', label: 'Despesas barracão', icon: ShoppingBag, area: 'barracao-despesas' },
    ],
  },
  {
    titulo: 'Financeiro',
    itens: [
      { to: '/boletos',       label: 'Boletos',          icon: Banknote, area: 'boletos' },
      { to: '/acerto-alfa', label: 'Acerto Alfa',    icon: Receipt, area: 'acerto-alfa' },
      { to: '/financeiro',    label: 'Financeiro',       icon: Landmark, area: 'financeiro' },
      { to: '/despesas-divididas', label: 'Despesas divididas', icon: Split, area: 'despesas-divididas' },
      { to: '/saldos',        label: 'Saldos a receber', icon: HandCoins, area: 'saldos' },
      { to: '/agrotrade-tip',   label: 'Agrotrade / TIP Bank', icon: Wallet, area: 'agrotrade-tip' },
    ],
  },
  {
    titulo: 'Fechamento do mês',
    itens: [
      { to: '/cte-export', label: 'Seguro',    icon: FileText, area: 'seguro' },
      { to: '/semparar',   label: 'Sem Parar', icon: Rota, area: 'semparar' },
    ],
  },
  {
    titulo: 'Cadastros',
    itens: [
      // Motoristas virou Usuários (USU-11); a tela nova entra na Parte 9 do plano
      { to: '/cadastros/usuarios',   label: 'Usuários',   icon: IdCard, area: 'motoristas' },
      { to: '/cadastros/placas',     label: 'Placas',     icon: Container, area: 'placas' },
    ],
  },
  {
    titulo: 'Cadastros e arquivos',
    itens: [
      { to: '/precos',    label: 'Clientes',       icon: Tags, area: 'clientes' },
      { to: '/tipos-volume', label: 'Tipos de volumes',  icon: Egg, area: 'tipos-volume' },
      { to: '/notas',     label: 'Notas fiscais',  icon: FolderArchive, area: 'notas' },
    ],
  },
]

// Barra de baixo no celular (F-04): os quatro destinos que ele mais abre no
// telefone (UI-05: estoques, abastecimento e boletos; Início é a porta de
// entrada) e o Menu com o resto. Viagens fica no menu: no celular ele só
// consulta, e o lançamento é no computador.
export type Atalho = { to: string; label: string; icon: LucideIcon; area: string; exact?: boolean }

export const atalhos: Atalho[] = [
  { to: '/',               label: 'Início',        icon: House, exact: true, area: 'inicio' },
  { to: '/estoque/volumes',   label: 'Volumes',          icon: PackageOpen, area: 'volumes-embalados' },
  { to: '/abastecimentos', label: 'Abastecimento', icon: Fuel, area: 'abastecimentos' },
  { to: '/boletos',        label: 'Boletos',       icon: Banknote, area: 'boletos' },
]

export function casa(pathname: string, to: string, exact?: boolean) {
  if (exact) return pathname === to
  return pathname === to || pathname.startsWith(to + '/')
}

// Telas com rodapé fixo próprio (o Salvar do Anotar produtoB): a barra de baixo
// sairia por cima dele, então nelas ela não aparece e o menu volta para o topo.
const SEM_BARRA = [/^\/produtosA\/?$/, /^\/estoque\/volumes\/anotar\/?$/, /^\/estoque\/volumes\/\d+\/editar\/?$/]
export const temRodapeProprio = (pathname: string) => SEM_BARRA.some(r => r.test(pathname))

// Nome da tela atual e do grupo dela, para o título da barra de cima.
export function paginaAtual(pathname: string): { secao?: string; label: string } {
  for (const secao of secoesMenu)
    for (const item of secao.itens)
      if (casa(pathname, item.to, item.exact)) return { secao: secao.titulo, label: item.label }
  if (pathname.startsWith('/perfil')) return { label: 'Meu perfil' }
  if (pathname.startsWith('/produtosA')) return { label: 'Portal das produtosA' }
  return { label: 'TRX Transportes' }
}

// Telas fora do menu e a área de cada uma (USU-11). Vem antes do menu: o histórico
// do portal mora dentro de /estoque/produtosA, mas é só do administrador.
const ROTAS_FORA_DO_MENU: { to: string; area: string }[] = [
  { to: '/estoque/produtosA/historico-portal', area: 'admin' },
  { to: '/produtosA',              area: 'portal-produtosA' },
  { to: '/cadastros/motoristas', area: 'motoristas' },   // endereço antigo de Usuários
  { to: '/perfil',               area: 'livre' },
  { to: '/usuarios',             area: 'livre' },        // vai para o perfil
  { to: '/minha-senha',          area: 'livre' },        // vai para o perfil
  { to: '/planilhas',            area: 'romaneios' },    // vai para Romaneios
]

/** Área que libera o endereço, ou null se o endereço não é de tela nenhuma. */
export function areaDaRota(pathname: string): string | null {
  const p = pathname.length > 1 ? pathname.replace(/\/+$/, '') : pathname
  for (const r of ROTAS_FORA_DO_MENU) if (casa(p, r.to)) return r.area
  for (const secao of secoesMenu)
    for (const item of secao.itens)
      if (casa(p, item.to, item.exact)) return item.area
  return null
}

/** Primeira tela do menu que a pessoa pode abrir (sem nenhuma, o perfil). */
export function primeiraLiberada(pode: (area: string) => boolean): string {
  for (const secao of secoesMenu)
    for (const item of secao.itens)
      if (item.area !== 'painel-motorista' && pode(item.area)) return item.to
  return '/perfil'
}
