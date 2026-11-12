import { useEffect, useId, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  ArrowRight, Check, ChevronRight, CircleDashed, CircleDot, Egg, Mail, PackageOpen, Receipt, TriangleAlert,
  Banknote, HandCoins, IdCard, type LucideIcon,
} from 'lucide-react'
import { formatBRL } from '../../services/api'
import { Abas } from '../ui/Abas'
import type {
  AcertoPainel, BoletoPainel, DiaRotina, GrupoBoletos, Painel, SituacaoTarefa, Tarefa,
} from '../../services/painelApi'

// Peças do Painel do dia (F-01, UI-18). A ordem da tela é a da urgência: o que
// pede atenção (vencido, não enviado, não bate) no topo; depois o que é de hoje;
// depois o que vence e o estado do barracão e do acerto; por fim a semana e o mês.

// ── Datas ────────────────────────────────────────────────────────────

const DIAS_CURTOS = ['dom.', 'seg.', 'ter.', 'qua.', 'qui.', 'sex.', 'sáb.']

/** "aaaa-mm-dd" sem fuso: new Date("2026-10-02") seria meia-noite UTC, dia anterior no Brasil */
export const dataLocal = (iso: string) => {
  const [a, m, d] = iso.slice(0, 10).split('-').map(Number)
  return new Date(a, m - 1, d)
}
export const ddmm = (iso: string) => {
  const d = dataLocal(iso)
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}`
}
export const dataExtenso = (iso: string) => {
  const t = dataLocal(iso).toLocaleDateString('pt-BR', { weekday: 'long', day: 'numeric', month: 'long' })
  return t.charAt(0).toUpperCase() + t.slice(1)
}

function quando(b: BoletoPainel): { texto: string; tom: 'vencido' | 'hoje' | 'breve' } {
  if (b.dias < -1) return { texto: `Venceu há ${-b.dias} dias`, tom: 'vencido' }
  if (b.dias === -1) return { texto: 'Venceu ontem', tom: 'vencido' }
  if (b.dias === 0) return { texto: 'Vence hoje', tom: 'hoje' }
  if (b.dias === 1) return { texto: 'Vence amanhã', tom: 'breve' }
  const d = dataLocal(b.vencimento)
  return { texto: `Vence ${DIAS_CURTOS[d.getDay()]} ${ddmm(b.vencimento)}`, tom: 'breve' }
}

const plural = (n: number, um: string, varios: string) => `${n} ${n === 1 ? um : varios}`

// ── Cartão e cabeçalho de seção ──────────────────────────────────────

export function Cartao({ titulo, icone: Icone, link, rotuloLink, children, extra, id }: {
  titulo: ReactNode
  icone?: LucideIcon
  link?: string
  rotuloLink?: string
  extra?: ReactNode
  children: ReactNode
  id?: string
}) {
  const auto = useId()
  const idTitulo = id ?? auto
  return (
    <section aria-labelledby={idTitulo} className="bg-white border border-gray-200 rounded-xl overflow-hidden scroll-mt-4">
      <header className="flex items-center gap-3 pl-4 pr-2 sm:pl-5 py-2 min-h-14 border-b border-gray-100">
        {Icone && <Icone size={18} strokeWidth={1.9} className="text-tinta-500 shrink-0" aria-hidden />}
        <h2 id={idTitulo} className="font-display text-lg font-semibold text-gray-900 leading-tight flex-1 min-w-0">{titulo}</h2>
        {extra}
        {link && (
          <Link to={link} className="inline-flex items-center gap-1 min-h-11 sm:min-h-9 px-3 rounded-lg text-sm font-semibold text-tinta-700 hover:bg-gray-100 shrink-0">
            {rotuloLink ?? 'Abrir'} <ArrowRight size={15} aria-hidden />
          </Link>
        )}
      </header>
      {children}
    </section>
  )
}

/** Linha que leva a outra tela: 56 px, a seta no fim diz que é toque */
function LinhaLink({ to, children, className = '' }: { to: string; children: ReactNode; className?: string }) {
  return (
    <Link to={to} className={`group flex items-center gap-3 px-4 sm:px-5 py-3 min-h-14 hover:bg-gray-50 focus-visible:bg-gray-50 transition-colors ${className}`}>
      {children}
      <ChevronRight size={18} className="text-gray-400 group-hover:text-gray-600 shrink-0 -mr-1 transition-transform group-hover:translate-x-0.5" aria-hidden />
    </Link>
  )
}

// ── Pede atenção ─────────────────────────────────────────────────────

type Tom = 'vermelho' | 'ambar'
export type Alerta = { chave: string; tom: Tom; icone: LucideIcon; titulo: string; detalhe?: string; link: string }

/** O que está fora do lugar, do mais grave para o menos. Vazio = "Tudo em dia". */
export function montarAlertas(p: Painel): Alerta[] {
  const a: Alerta[] = []
  const { pagar, receber } = p.boletos
  if (pagar.vencidos.quantidade > 0)
    a.push({ chave: 'pagar-vencidos', tom: 'vermelho', icone: Banknote,
      titulo: `${plural(pagar.vencidos.quantidade, 'boleto a pagar vencido', 'boletos a pagar vencidos')}`,
      detalhe: totalTexto(pagar.vencidos.total, pagar.vencidos.semValor), link: '/boletos' })
  if (receber.vencidos.quantidade > 0)
    a.push({ chave: 'receber-vencidos', tom: 'vermelho', icone: HandCoins,
      titulo: `${plural(receber.vencidos.quantidade, 'boleto a receber atrasado', 'boletos a receber atrasados')}`,
      detalhe: totalTexto(receber.vencidos.total, receber.vencidos.semValor), link: '/boletos' })
  const hoje = pagar.hojeQuantidade + receber.hojeQuantidade
  if (hoje > 0)
    a.push({ chave: 'vencem-hoje', tom: 'ambar', icone: Banknote,
      titulo: `${plural(hoje, 'boleto vence hoje', 'boletos vencem hoje')}`,
      detalhe: [pagar.hojeQuantidade && `${pagar.hojeQuantidade} a pagar`, receber.hojeQuantidade && `${receber.hojeQuantidade} a receber`].filter(Boolean).join(' · '),
      link: '/boletos' })
  if (p.emails.length > 0)
    a.push({ chave: 'emails', tom: 'ambar', icone: Mail,
      titulo: `${plural(p.emails.length, 'e-mail de cliente não enviado', 'e-mails de cliente não enviados')}`,
      detalhe: p.emails.map(e => `${e.rotulo}, carga de ${ddmm(e.dataCarga)}${e.situacao === 'teste' ? ', só no teste' : e.situacao === 'erro' ? ', deu erro' : ''}`).join(' · '),
      link: '/emails-clientes' })
  const cnh = p.cnh ?? []
  if (cnh.length > 0) {
    const vencidas = cnh.filter(c => c.dias < 0).length
    a.push({ chave: 'cnh', tom: vencidas > 0 ? 'vermelho' : 'ambar', icone: IdCard,
      titulo: vencidas > 0
        ? `${plural(vencidas, 'CNH vencida', 'CNHs vencidas')}${cnh.length > vencidas ? ` e ${cnh.length - vencidas} a vencer` : ''}`
        : `${plural(cnh.length, 'CNH vence', 'CNHs vencem')} em até 30 dias`,
      detalhe: cnh.map(c => `${c.nome}: ${c.dias < 0 ? `venceu há ${-c.dias} ${c.dias === -1 ? 'dia' : 'dias'}` : c.dias === 0 ? 'vence hoje' : `vence em ${c.dias} ${c.dias === 1 ? 'dia' : 'dias'}`}`).join(' · '),
      link: '/cadastros/motoristas' })
  }
  const naoBate = p.volumes.controles.filter(c => c.status === 'nao_bate')
  if (naoBate.length > 0)
    a.push({ chave: 'volumes-nao-bate', tom: 'ambar', icone: PackageOpen,
      titulo: naoBate.length === 1 ? `O controle de volumes ${naoBate[0].numero} não bate` : `${naoBate.length} controles de volumes não batem`,
      detalhe: naoBate.length === 1 ? diferencaTexto(naoBate[0].diferencas) : naoBate.map(c => c.numero).join(', '),
      link: naoBate.length === 1 ? `/estoque/volumes/${naoBate[0].id}` : '/estoque/volumes' })
  const sem = p.volumes.romaneiosSemControle ?? []
  if (sem.length > 0)
    a.push({ chave: 'fazenda-sem-controle', tom: 'ambar', icone: Egg,
      titulo: `${plural(sem.length, 'romaneio da fazenda sem controle de volumes', 'romaneios da fazenda sem controle de volumes')}`,
      detalhe: sem.map(r => `${r.numero} de ${ddmm(r.data)}`).join(' · '), link: '/estoque/volumes' })
  return a
}

function totalTexto(total: number, semValor: number) {
  const t = formatBRL(total)
  return semValor > 0 ? `${t} · ${plural(semValor, 'sem valor', 'sem valor')}` : t
}

export function diferencaTexto(d: { nome: string; valor: number; emProdutosC: boolean }[]) {
  if (d.length === 0) return ''
  return d.slice(0, 3).map(x => {
    const n = Math.abs(x.valor).toLocaleString('pt-BR', { maximumFractionDigits: 2 })
    const un = x.emProdutosC ? (Math.abs(x.valor) === 1 ? 'produtoC' : 'produtosC') : (Math.abs(x.valor) === 1 ? 'caixa' : 'caixas')
    return `${x.nome}: ${x.valor > 0 ? 'sobram' : 'faltam'} ${n} ${un}`
  }).join(' · ') + (d.length > 3 ? ' · ...' : '')
}

// faixa: fio de 4 px na borda esquerda da linha, para o grave se ver de longe
const TONS: Record<Tom, { icone: string; titulo: string; faixa: string }> = {
  vermelho: { icone: 'bg-red-50 text-red-700 border-red-200', titulo: 'text-red-800', faixa: 'before:bg-red-500' },
  ambar:    { icone: 'bg-amber-50 text-amber-700 border-amber-200', titulo: 'text-gray-900', faixa: 'before:bg-amber-400' },
}

export function PedeAtencao({ alertas }: { alertas: Alerta[] }) {
  if (alertas.length === 0) return <TudoEmDia />
  const grave = alertas.some(a => a.tom === 'vermelho')
  return (
    <Cartao titulo="Pede atenção" icone={TriangleAlert} id="painel-atencao"
            extra={<span className={`inline-flex items-center justify-center min-w-7 h-7 px-2 rounded-full border text-sm font-semibold tabular-nums
                                     ${grave ? 'bg-red-50 text-red-700 border-red-200' : 'bg-amber-50 text-amber-800 border-amber-200'}`} aria-hidden>{alertas.length}</span>}>
      <ul className="divide-y divide-gray-100">
        {alertas.map(a => (
          <li key={a.chave}>
            <LinhaLink to={a.link} className={`relative before:absolute before:inset-y-0 before:left-0 before:w-1 ${TONS[a.tom].faixa}`}>
              <span className={`w-10 h-10 rounded-lg border flex items-center justify-center shrink-0 ${TONS[a.tom].icone}`}>
                <a.icone size={19} strokeWidth={1.9} aria-hidden />
              </span>
              <span className="flex-1 min-w-0">
                <span className={`block font-semibold leading-snug ${TONS[a.tom].titulo}`}>{a.titulo}</span>
                {a.detalhe && <span className="block text-sm text-gray-500 leading-snug mt-0.5 break-words">{a.detalhe}</span>}
              </span>
            </LinhaLink>
          </li>
        ))}
      </ul>
    </Cartao>
  )
}

/** Estado vazio do topo: nada vencido, nada por enviar, os volumes batendo */
function TudoEmDia() {
  return (
    <section aria-labelledby="painel-atencao" className="relative overflow-hidden rounded-xl border border-gray-200 bg-white px-5 py-6 sm:px-6 flex items-center gap-4 scroll-mt-4">
      <span className="relative w-14 h-14 rounded-full bg-gema-50 border border-gema-200 flex items-center justify-center shrink-0" aria-hidden>
        <span className="absolute inset-[9px] rounded-full bg-gema-400 animate-[fundo_400ms_ease-out_both]" />
        {/* Sobre amarelo cheio: cor fixa, senão some no tema escuro (UI-23) */}
        <Check size={22} strokeWidth={2.6} className="relative text-sobre-gema" />
      </span>
      <div className="min-w-0">
        <h2 id="painel-atencao" className="font-display text-xl font-semibold text-gray-900 leading-tight">Tudo em dia</h2>
        <p className="text-sm text-gray-500 mt-1 leading-snug">
          Nenhum boleto vencido, nenhum e-mail esperando e os volumes batendo.
        </p>
      </div>
    </section>
  )
}

// ── Rotina ───────────────────────────────────────────────────────────

const SITUACAO: Record<SituacaoTarefa, { rotulo: string; Icone: LucideIcon; caixa: string }> = {
  feito:     { rotulo: 'Feito',       Icone: Check,        caixa: 'bg-emerald-50 text-emerald-700 border-emerald-200' },
  pendente:  { rotulo: 'Falta',       Icone: CircleDashed, caixa: 'bg-amber-50 text-amber-700 border-amber-200' },
  andamento: { rotulo: 'Em andamento', Icone: CircleDot,   caixa: 'bg-tinta-50 text-tinta-600 border-tinta-200' },
  lembrete:  { rotulo: 'Lembrete',    Icone: CircleDashed, caixa: 'bg-white text-gray-400 border-gray-300' },
}

// Lembrete (o que ele faz fora do sistema) se marca à mão, só neste aparelho e só
// por hoje. Não é registro: é o risco no papel.
const chaveFeitos = (dia: string) => `trx_painel_feitos_${dia}`
function lerFeitos(dia: string): string[] {
  try { const v = JSON.parse(localStorage.getItem(chaveFeitos(dia)) ?? '[]'); return Array.isArray(v) ? v : [] }
  catch { return [] }
}
/** Fica no DashboardPage: a ficha "Tarefas de hoje" do topo conta os lembretes marcados também */
export function useFeitos(dia: string) {
  const [feitos, setFeitos] = useState<string[]>(() => lerFeitos(dia))
  useEffect(() => setFeitos(lerFeitos(dia)), [dia])
  const alternar = (chave: string) => setFeitos(f => {
    const novo = f.includes(chave) ? f.filter(x => x !== chave) : [...f, chave]
    try { localStorage.setItem(chaveFeitos(dia), JSON.stringify(novo)) } catch { /* sem armazenamento: vale até recarregar */ }
    return novo
  })
  return [feitos, alternar] as const
}

const ROTULO_DIA: Record<number, string> = { 1: 'Seg', 2: 'Ter', 3: 'Qua', 4: 'Qui', 5: 'Sex', 6: 'Fim de semana' }

/** Tarefa conta como feita se a API diz, ou se é lembrete marcado à mão hoje */
export const situacaoDoDia = (t: Tarefa, ehHoje: boolean, feitos: string[]): SituacaoTarefa =>
  (t.situacao === 'lembrete' && ehHoje && feitos.includes(t.chave) ? 'feito' : t.situacao)

export function Rotina({ rotina, diaHoje, hoje, mensal, feitos, alternar }: {
  rotina: DiaRotina[]; diaHoje: number; hoje: string; mensal: Tarefa[]
  feitos: string[]; alternar: (chave: string) => void
}) {
  const [dia, setDia] = useState(diaHoje)
  const atual = rotina.find(d => d.dia === dia) ?? rotina[0]
  const ehHoje = dia === diaHoje
  const idAbas = `rotina-${useId().replace(/:/g, '')}`

  const situacao = (t: Tarefa) => situacaoDoDia(t, ehHoje, feitos)
  const tarefas = atual?.tarefas ?? []
  const prontas = tarefas.filter(t => situacao(t) === 'feito').length
  const progresso = tarefas.length > 0 ? Math.round((prontas / tarefas.length) * 100) : 0
  const mensalAberto = mensal.filter(t => t.situacao !== 'feito')
  const diaInicioMes = dataLocal(hoje).getDate() <= 12

  return (
    <Cartao titulo={ehHoje ? 'O que é de hoje' : `Rotina de ${atual?.nome.toLowerCase()}`} id="painel-rotina"
            extra={tarefas.length > 0 && (
              <span className="text-sm text-gray-500 tabular-nums whitespace-nowrap">
                <span className="sr-only">Prontas: </span>{prontas} de {tarefas.length}
              </span>
            )}>
      {/* Quanto do dia já foi: o mesmo "x de y" do cabeçalho, em barra */}
      {tarefas.length > 0 && (
        <div className="h-1 bg-gray-100" aria-hidden>
          <div className={`h-full transition-[width] duration-500 ease-out ${prontas === tarefas.length ? 'bg-emerald-500' : 'bg-tinta-500'}`}
               style={{ width: `${progresso}%` }} />
        </div>
      )}
      <div className="px-4 sm:px-5 pt-3">
        <Abas id={idAbas} valor={String(dia)} onChange={v => setDia(Number(v))} rotulo="Dia da semana"
              opcoes={rotina.map(d => [String(d.dia), d.dia === diaHoje ? `${ROTULO_DIA[d.dia]} · hoje` : ROTULO_DIA[d.dia]] as const)} />
      </div>
      <div role="tabpanel" id={`${idAbas}-painel`} aria-labelledby={`${idAbas}-aba-${dia}`}>
        {!ehHoje && (
          <p className="px-4 sm:px-5 pt-2 text-sm text-gray-500">A situação é desta semana; os lembretes só se marcam no dia.</p>
        )}
        <ul className="mt-2 divide-y divide-gray-100 border-t border-gray-100">
          {tarefas.map(t => (
            <LinhaTarefa key={t.chave} tarefa={t} situacao={situacao(t)}
                         marcavel={t.situacao === 'lembrete' && ehHoje}
                         onMarcar={() => alternar(t.chave)} />
          ))}
        </ul>
      </div>

      {/* Fechamento do mês (ROT-05): aparece no começo do mês ou enquanto faltar algo */}
      {(diaInicioMes || mensalAberto.length > 0) && mensal.length > 0 && (
        <div className="border-t border-gray-200 bg-gray-50">
          <h3 className="px-4 sm:px-5 pt-3 pb-1 font-display text-base font-semibold text-gray-800">Fechamento do mês</h3>
          <ul className="divide-y divide-gray-100">
            {mensal.map(t => <LinhaTarefa key={t.chave} tarefa={t} situacao={t.situacao} />)}
          </ul>
        </div>
      )}
    </Cartao>
  )
}

function LinhaTarefa({ tarefa: t, situacao, marcavel = false, onMarcar }: {
  tarefa: Tarefa; situacao: SituacaoTarefa; marcavel?: boolean; onMarcar?: () => void
}) {
  const s = SITUACAO[situacao]
  const feito = situacao === 'feito'
  // Lembrete é uma caixinha de marcar (ele risca à mão); o resto, o selo da situação
  const marca = situacao === 'lembrete' && marcavel ? (
    <span className="w-6 h-6 mx-0.5 rounded-md border-2 border-gray-300 bg-white transition-colors" />
  ) : (
    <span className={`w-7 h-7 rounded-full border flex items-center justify-center ${s.caixa}`}>
      <s.Icone size={15} strokeWidth={feito ? 2.8 : 2} aria-hidden />
    </span>
  )
  return (
    <li className="flex items-stretch">
      {marcavel ? (
        <button type="button" onClick={onMarcar} aria-pressed={feito}
                aria-label={`${feito ? 'Desmarcar' : 'Marcar como feito'}: ${t.titulo}`}
                className="pl-4 sm:pl-5 pr-1 flex items-center shrink-0 min-w-14 hover:[&>span]:border-tinta-400">
          {marca}
        </button>
      ) : (
        <span className="pl-4 sm:pl-5 pr-1 flex items-center shrink-0 min-w-14">
          {marca}
          <span className="sr-only">{s.rotulo}: </span>
        </span>
      )}
      <LinhaLink to={t.link} className="flex-1 min-w-0 !pl-2">
        <span className="flex-1 min-w-0">
          <span className={`block leading-snug ${feito ? 'text-gray-500' : 'text-gray-900 font-medium'}`}>{t.titulo}</span>
          {t.detalhe && (
            <span className={`block text-sm leading-snug mt-0.5 ${situacao === 'pendente' ? 'text-amber-800' : 'text-gray-500'}`}>{t.detalhe}</span>
          )}
        </span>
      </LinhaLink>
    </li>
  )
}

// ── Boletos ──────────────────────────────────────────────────────────

const TOM_DATA = {
  vencido: 'text-red-700 font-semibold',
  hoje:    'text-amber-800 font-semibold',
  breve:   'text-gray-500',
}

export function Boletos({ pagar, receber }: { pagar: GrupoBoletos; receber: GrupoBoletos }) {
  return (
    <Cartao titulo="Boletos" icone={Banknote} link="/boletos" rotuloLink="Abrir boletos" id="painel-boletos">
      <BlocoBoletos titulo="A pagar" g={pagar} vazio="Nada a pagar vencido ou vencendo em 7 dias." />
      <BlocoBoletos titulo="A receber" g={receber} vazio="Nada a receber vencido ou vencendo em 7 dias." borda />
    </Cartao>
  )
}

function BlocoBoletos({ titulo, g, vazio, borda = false }: { titulo: string; g: GrupoBoletos; vazio: string; borda?: boolean }) {
  const resumo = [
    g.vencidos.quantidade > 0 && <span key="v" className="text-red-700 font-semibold">{plural(g.vencidos.quantidade, 'vencido', 'vencidos')} · {formatBRL(g.vencidos.total)}</span>,
    g.aVencer.quantidade > 0 && <span key="a">{g.aVencer.quantidade} em 7 dias · {formatBRL(g.aVencer.total)}</span>,
  ].filter(Boolean)
  return (
    <div className={borda ? 'border-t border-gray-200' : ''}>
      <div className="px-4 sm:px-5 pt-3 pb-1 flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
        <h3 className="font-display text-base font-semibold text-gray-800">{titulo}</h3>
        {resumo.length > 0 && <p className="text-sm text-gray-500 tabular-nums flex flex-wrap gap-x-3">{resumo}</p>}
      </div>
      {g.itens.length === 0 ? (
        <p className="px-4 sm:px-5 pb-4 pt-1 text-sm text-gray-500">{vazio}</p>
      ) : (
        <ul className="divide-y divide-gray-100">
          {g.itens.map(b => {
            const q = quando(b)
            return (
              <li key={`${b.origem}-${b.id}`}>
                <LinhaLink to="/boletos" className="!min-h-0 !py-2.5">
                  <span className="flex-1 min-w-0">
                    <span className="block text-gray-900 font-medium leading-snug truncate">{b.quem}</span>
                    <span className="block text-sm leading-snug mt-0.5">
                      <span className={TOM_DATA[q.tom]}>{q.texto}</span>
                      {b.numero && <span className="text-gray-500"> · {b.numero}</span>}
                    </span>
                  </span>
                  {b.valor === null
                    ? <span className="text-sm text-amber-800 font-medium whitespace-nowrap">Sem valor</span>
                    : <span className="font-semibold text-gray-900 tabular-nums whitespace-nowrap">{formatBRL(b.valor)}</span>}
                </LinhaLink>
              </li>
            )
          })}
        </ul>
      )}
      {(g.mais > 0 || g.semVencimento > 0) && (
        <p className="px-4 sm:px-5 py-2.5 text-sm text-gray-500 border-t border-gray-100">
          {[g.mais > 0 && `E mais ${g.mais} em Boletos.`, g.semVencimento > 0 && `${plural(g.semVencimento, 'boleto em aberto sem vencimento', 'boletos em aberto sem vencimento')}.`].filter(Boolean).join(' ')}
        </p>
      )}
    </div>
  )
}

// ── Volumes embalados ───────────────────────────────────────────────────

export function Volumes({ volumes }: { volumes: Painel['volumes'] }) {
  const sem = volumes.romaneiosSemControle
  const tudoBem = volumes.controles.length === 0 && sem !== null && sem.length === 0
  return (
    <Cartao titulo="Controle embalados" icone={PackageOpen} link="/estoque/volumes" rotuloLink="Abrir" id="painel-volumes">
      {volumes.conferidos > 0 && (
        <p className="px-4 sm:px-5 pt-3 text-sm text-gray-500">
          {volumes.batem === volumes.conferidos
            ? `Os ${plural(volumes.conferidos, 'controle recente bate', 'controles recentes batem')}.`
            : `${volumes.batem} de ${plural(volumes.conferidos, 'controle recente', 'controles recentes')} batem.`}
        </p>
      )}
      {tudoBem ? (
        <p className="px-4 sm:px-5 py-4 text-sm text-gray-700 flex items-center gap-2">
          <Check size={16} className="text-emerald-700" aria-hidden /> Nada faltando no barracão.
        </p>
      ) : (
        <ul className="mt-2 divide-y divide-gray-100 border-t border-gray-100">
          {volumes.controles.map(c => (
            <li key={c.id}>
              <LinhaLink to={`/estoque/volumes/${c.id}`}>
                <span className="flex-1 min-w-0">
                  <span className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-gray-900">Controle {c.numero}</span>
                    <span className="text-sm text-gray-500">{ddmm(c.data)}</span>
                    <Etiqueta tom={c.status === 'nao_bate' ? 'vermelho' : 'ambar'}>{c.status === 'nao_bate' ? 'Não bate' : 'Incompleto'}</Etiqueta>
                  </span>
                  <span className="block text-sm text-gray-500 leading-snug mt-0.5 break-words">
                    {c.status === 'nao_bate' ? diferencaTexto(c.diferencas) : c.faltando.join(' ') || 'Faltam dados.'}
                  </span>
                </span>
              </LinhaLink>
            </li>
          ))}
          {(sem ?? []).map(r => (
            <li key={`r-${r.numero}`}>
              <LinhaLink to="/estoque/volumes">
                <span className="flex-1 min-w-0">
                  <span className="flex items-center gap-2 flex-wrap">
                    <span className="font-medium text-gray-900">Romaneio da fazenda {r.numero}</span>
                    <span className="text-sm text-gray-500">{ddmm(r.data)}</span>
                  </span>
                  <span className="block text-sm text-amber-800 leading-snug mt-0.5">
                    {plural(r.caixas, 'caixa', 'caixas')} sem controle de volumes{r.acertoNumero ? ` · acerto ${r.acertoNumero}` : ''}
                  </span>
                </span>
              </LinhaLink>
            </li>
          ))}
        </ul>
      )}
      {sem === null && (
        <p className="px-4 sm:px-5 py-3 text-sm text-amber-800 border-t border-gray-100 flex gap-2">
          <TriangleAlert size={16} className="shrink-0 mt-0.5" aria-hidden />
          Não consegui ver os romaneios da fazenda. Confira na tela de volumes.
        </p>
      )}
    </Cartao>
  )
}

function Etiqueta({ tom, children }: { tom: Tom; children: ReactNode }) {
  return (
    <span className={`inline-flex items-center px-2 py-px rounded-full text-xs font-semibold border ${tom === 'vermelho' ? 'bg-red-50 text-red-700 border-red-200' : 'bg-amber-50 text-amber-800 border-amber-200'}`}>
      {children}
    </span>
  )
}

// ── Acerto Alfa ────────────────────────────────────────────────────

export function Acerto({ acerto }: { acerto: Painel['acerto'] }) {
  const a = acerto.atual
  return (
    <Cartao titulo="Acerto Alfa" icone={Receipt} link="/acerto-alfa" rotuloLink="Abrir" id="painel-acerto">
      <div className="px-4 sm:px-5 py-3 space-y-3">
        <p className="text-sm text-gray-500">Semana de {ddmm(acerto.semana)}</p>
        {a ? <FichaAcerto a={a} /> : (
          <p className="text-gray-800 leading-snug">
            Ainda não chegou nenhum PDF desta semana. <span className="text-gray-500">Boletos e romaneios da fazenda entram pela tela do acerto.</span>
          </p>
        )}
        {!a && acerto.anterior && (
          <p className="text-sm text-gray-500 border-t border-gray-100 pt-3">
            Último: acerto {acerto.anterior.numero} (semana de {ddmm(acerto.anterior.semana)}), saldo{' '}
            <span className="font-semibold text-gray-800 tabular-nums">{formatBRL(acerto.anterior.saldo)}</span>
          </p>
        )}
      </div>
    </Cartao>
  )
}

function FichaAcerto({ a }: { a: AcertoPainel }) {
  return (
    <div className="space-y-2.5">
      <div className="flex items-baseline justify-between gap-3">
        <span className="font-display text-xl font-semibold text-gray-900">Acerto {a.numero}</span>
        <span className="text-right">
          <span className="block text-xs text-gray-500">Saldo com a Livia</span>
          <span className="font-semibold text-gray-900 tabular-nums">{formatBRL(a.saldo)}</span>
        </span>
      </div>
      <dl className="grid grid-cols-3 gap-2 text-center">
        <Fato rotulo={a.romaneios === 1 ? 'romaneio' : 'romaneios'} valor={a.romaneios} />
        <Fato rotulo={a.boletos === 1 ? 'boleto' : 'boletos'} valor={a.boletos} />
        <Fato rotulo="sem valor" valor={a.boletosSemValor} alerta={a.boletosSemValor > 0} />
      </dl>
    </div>
  )
}

function Fato({ rotulo, valor, alerta = false }: { rotulo: string; valor: number; alerta?: boolean }) {
  return (
    <div className={`rounded-lg border px-2 py-2 ${alerta ? 'bg-amber-50 border-amber-200' : 'bg-gray-50 border-gray-200'}`}>
      <dd className={`font-display text-xl font-semibold tabular-nums ${alerta ? 'text-amber-800' : 'text-gray-900'}`}>{valor}</dd>
      <dt className={`text-xs ${alerta ? 'text-amber-800' : 'text-gray-500'}`}>{rotulo}</dt>
    </div>
  )
}

// ── Semana ───────────────────────────────────────────────────────────

export function Semana({ semana, segunda, domingo }: { semana: Painel['semana']; segunda: string; domingo: string }) {
  const itens: [string, string, string?][] = [
    ['Cargas', String(semana.cargas), semana.placas ? plural(semana.placas, 'caminhão', 'caminhões') : undefined],
    ['Notas nas cargas', String(semana.notas)],
    ['Romaneios', String(semana.romaneios)],
    ['CT-es', String(semana.ctes), semana.ctesCancelados ? plural(semana.ctesCancelados, 'cancelado', 'cancelados') : undefined],
    ['Frete dos CT-es', formatBRL(semana.frete)],
  ]
  return (
    <section aria-labelledby="painel-semana" className="space-y-2">
      <h2 id="painel-semana" className="font-display text-lg font-semibold text-gray-900">
        Esta semana <span className="text-sm font-normal text-gray-500 font-sans">{ddmm(segunda)} a {ddmm(domingo)}</span>
      </h2>
      <dl className="bg-white border border-gray-200 rounded-xl grid grid-cols-2 sm:grid-cols-5 overflow-hidden">
        {itens.map(([rotulo, valor, nota], i) => (
          <div key={rotulo} className={`px-4 py-3 min-w-0 border-gray-100
              ${i % 2 === 0 ? 'border-r' : ''} ${i < 4 ? 'border-b' : ''} ${i === 4 ? 'col-span-2 sm:col-span-1' : ''}
              sm:border-b-0 ${i < 4 ? 'sm:border-r' : 'sm:border-r-0'}`}>
            <dt className="text-sm text-gray-500 truncate">{rotulo}</dt>
            <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums truncate">{valor}</dd>
            {nota && <dd className="text-xs text-gray-500">{nota}</dd>}
          </div>
        ))}
      </dl>
    </section>
  )
}
