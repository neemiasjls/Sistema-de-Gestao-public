import {
  House, Truck, ClipboardList, Fuel, Mail, PackageOpen, Disc3, Boxes, Banknote, Receipt,
  Landmark, HandCoins, FileText, Route as Rota, Egg, FolderArchive, Milestone, ArrowLeftRight,
  Grid3x3, Tags, EggOff, ShoppingBag, Wallet, Split, IdCard, Container,
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

export type ItemMenu = {
  to: string
  label: string
  icon: LucideIcon
  /** Só acende com o endereço exato (Início) */
  exact?: boolean
}

export type SecaoMenu = { titulo?: string; itens: ItemMenu[] }

export const secoesMenu: SecaoMenu[] = [
  { itens: [{ to: '/', label: 'Início', icon: House, exact: true }] },
  {
    titulo: 'Dia a dia',
    itens: [
      { to: '/frete',           label: 'Viagens',              icon: Truck },
      { to: '/viagens-caminhoes', label: 'Acerto viagens', icon: Milestone },
      { to: '/retornos',        label: 'Retornos',             icon: ArrowLeftRight },
      { to: '/romaneios',       label: 'Romaneios',            icon: ClipboardList },
      { to: '/abastecimentos',  label: 'Abastecimentos',       icon: Fuel },
      { to: '/emails-clientes', label: 'E-mails aos clientes', icon: Mail },
    ],
  },
  {
    // Módulo Embalados (VOLUME-16): o barracão e o que era a planilha COMISSÕES VOLUMES EMBALADOS
    titulo: 'Embalados',
    itens: [
      { to: '/estoque/volumes',         label: 'Controle embalados',  icon: PackageOpen },
      { to: '/embalados/quebras',    label: 'Quebras',             icon: EggOff },
      { to: '/embalados/despesas',   label: 'Despesas embalados', icon: ShoppingBag },
      { to: '/embalados/comissoes',  label: 'Comissões',           icon: HandCoins },
      { to: '/embalados/salarios',   label: 'Salários',            icon: Wallet },
    ],
  },
  {
    titulo: 'Estoques',
    itens: [
      { to: '/estoque/produtosA',    label: 'ProdutosA',        icon: Disc3 },
      { to: '/estoque/produtosB', label: 'ProdutosB',     icon: Boxes },
      { to: '/estoque/produtosC',   label: 'ProdutosC',       icon: Grid3x3 },
    ],
  },
  {
    titulo: 'Barracão',
    itens: [
      { to: '/barracao/comissoes', label: 'Comissões barracão', icon: HandCoins },
      { to: '/barracao/despesas', label: 'Despesas barracão', icon: ShoppingBag },
    ],
  },
  {
    titulo: 'Financeiro',
    itens: [
      { to: '/boletos',       label: 'Boletos',          icon: Banknote },
      { to: '/acerto-alfa', label: 'Acerto Alfa',    icon: Receipt },
      { to: '/financeiro',    label: 'Financeiro',       icon: Landmark },
      { to: '/despesas-divididas', label: 'Despesas divididas', icon: Split },
      { to: '/saldos',        label: 'Saldos a receber', icon: HandCoins },
      { to: '/agrotrade-tip',   label: 'Agrotrade / TIP Bank', icon: Wallet },
    ],
  },
  {
    titulo: 'Fechamento do mês',
    itens: [
      { to: '/cte-export', label: 'Seguro',    icon: FileText },
      { to: '/semparar',   label: 'Sem Parar', icon: Rota },
    ],
  },
  {
    titulo: 'Cadastros',
    itens: [
      { to: '/cadastros/motoristas', label: 'Motoristas', icon: IdCard },
      { to: '/cadastros/placas',     label: 'Placas',     icon: Container },
    ],
  },
  {
    titulo: 'Cadastros e arquivos',
    itens: [
      { to: '/precos',    label: 'Preços',         icon: Tags },
      { to: '/tipos-volume', label: 'Tipos de volumes',  icon: Egg },
      { to: '/notas',     label: 'Notas fiscais',  icon: FolderArchive },
    ],
  },
]

// Barra de baixo no celular (F-04): os quatro destinos que ele mais abre no
// telefone (UI-05: estoques, abastecimento e boletos; Início é a porta de
// entrada) e o Menu com o resto. Viagens fica no menu: no celular ele só
// consulta, e o lançamento é no computador.
export type Atalho = { to: string; label: string; icon: LucideIcon; exact?: boolean }

export const atalhos: Atalho[] = [
  { to: '/',               label: 'Início',        icon: House, exact: true },
  { to: '/estoque/volumes',   label: 'Volumes',          icon: PackageOpen },
  { to: '/abastecimentos', label: 'Abastecimento', icon: Fuel },
  { to: '/boletos',        label: 'Boletos',       icon: Banknote },
]

export function casa(pathname: string, to: string, exact?: boolean) {
  if (exact) return pathname === to
  return pathname === to || pathname.startsWith(to + '/')
}

// Telas com rodapé fixo próprio (o Salvar do Anotar produtoB): a barra de baixo
// sairia por cima dele, então nelas ela não aparece e o menu volta para o topo.
const SEM_BARRA = [/^\/estoque\/volumes\/anotar\/?$/, /^\/estoque\/volumes\/\d+\/editar\/?$/]
export const temRodapeProprio = (pathname: string) => SEM_BARRA.some(r => r.test(pathname))

// Nome da tela atual e do grupo dela, para o título da barra de cima.
export function paginaAtual(pathname: string): { secao?: string; label: string } {
  for (const secao of secoesMenu)
    for (const item of secao.itens)
      if (casa(pathname, item.to, item.exact)) return { secao: secao.titulo, label: item.label }
  if (pathname.startsWith('/perfil')) return { label: 'Meu perfil' }
  return { label: 'TRX Transportes' }
}
