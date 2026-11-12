import { useCallback, useEffect, useState } from 'react'
import { RotateCcw } from 'lucide-react'
import { ErroCarga } from '../components/ui/Estados'
import { BotaoIcone } from '../components/ui/Botao'
import { carregarPainel, type Painel } from '../services/painelApi'
import {
  Acerto, Boletos, Volumes, PedeAtencao, Rotina, Semana, dataExtenso, montarAlertas, situacaoDoDia, useFeitos,
} from '../components/painel/PainelPecas'
import ResumoMes from '../components/painel/ResumoMes'
import ResumoDia from '../components/painel/ResumoDia'

// Painel do dia (F-01, UI-18): a tela inicial mostra o que importa hoje, do mais
// urgente para o menos — o que pede atenção, a rotina do dia (rotina.md), boletos
// que vencem em 7 dias, volumes e acerto da semana — e embaixo o resumo do mês com as
// exportações de CT-e (o que o Dashboard antigo tinha). Uma chamada só (/api/painel).

export default function DashboardPage() {
  const [painel, setPainel] = useState<Painel | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)

  const carregar = useCallback(() => {
    setCarregando(true)
    setFalhou(false)
    carregarPainel()
      .then(setPainel)
      .catch(() => setFalhou(true))
      .finally(() => setCarregando(false))
  }, [])
  useEffect(carregar, [carregar])

  return (
    <div className="space-y-6 lg:space-y-8 max-w-6xl">
      {carregando && !painel ? <EsqueletoPainel /> :
       falhou && !painel ? <ErroCarga mensagem="Não consegui montar o painel do dia." onTentar={carregar} /> :
       painel && <Conteudo painel={painel} atualizando={carregando} onAtualizar={carregar} falhouAgora={falhou} />}

      {/* O resumo do mês carrega sozinho: se o painel falhar, ele ainda serve */}
      <ResumoMes />
    </div>
  )
}

function Conteudo({ painel: p, atualizando, onAtualizar, falhouAgora }: {
  painel: Painel; atualizando: boolean; onAtualizar: () => void; falhouAgora: boolean
}) {
  const alertas = montarAlertas(p)
  const [feitos, alternar] = useFeitos(p.hoje)
  const tarefasHoje = p.rotina.find(d => d.dia === p.diaHoje)?.tarefas ?? []
  const prontasHoje = tarefasHoje.filter(t => situacaoDoDia(t, true, feitos) === 'feito').length
  const pendentesHoje = tarefasHoje.filter(t => t.situacao === 'pendente').length
  // Hora da última carga boa: mostra que o painel está vivo (e quando foi visto)
  const [visto, setVisto] = useState(() => new Date())
  useEffect(() => { if (!atualizando && !falhouAgora) setVisto(new Date()) }, [p, atualizando, falhouAgora])
  const hora = visto.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
  // Entrada em cascata só na primeira vez (esqueleto → conteúdo); Atualizar não repete
  const entra = (n: number, classe = '') => ({ className: `animate-entrada ${classe}`, style: { animationDelay: `${n * 60}ms` } })
  const frase = alertas.length === 0 && pendentesHoje === 0
    ? 'Nada atrasado.'
    : [alertas.length > 0 && `${alertas.length} ${alertas.length === 1 ? 'ponto pede' : 'pontos pedem'} atenção`,
       pendentesHoje > 0 && `${pendentesHoje} ${pendentesHoje === 1 ? 'tarefa de hoje falta' : 'tarefas de hoje faltam'}`]
        .filter(Boolean).join(' · ') + '.'

  return (
    <>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-display text-2xl sm:text-[1.75rem] font-semibold text-gray-900 leading-tight tracking-tight">{dataExtenso(p.hoje)}</p>
          <p className="text-gray-600 mt-1" aria-live="polite">{frase}</p>
        </div>
        <div className="flex items-center gap-1 shrink-0">
          <span className="hidden sm:inline text-sm text-gray-500 tabular-nums" aria-live="polite">
            {atualizando ? 'Atualizando...' : `Atualizado às ${hora}`}
          </span>
          <BotaoIcone icone={RotateCcw} rotulo={`Atualizar o painel (visto às ${hora})`} onClick={onAtualizar} disabled={atualizando}
                      className={atualizando ? '[&>svg]:animate-spin' : ''} />
        </div>
      </div>
      {falhouAgora && (
        <ErroCarga mensagem="Não consegui atualizar o painel." detalhe="Mostrando o que veio da última vez." onTentar={onAtualizar} />
      )}

      <ResumoDia alertas={alertas} prontas={prontasHoje} tarefas={tarefasHoje.length} faltam={pendentesHoje}
                 pagar={p.boletos.pagar} receber={p.boletos.receber} />

      {/* Computador: urgência e rotina à esquerda, boletos e barracão à direita.
          Tablet: boletos ao lado de volumes + acerto. Celular: uma coluna, na ordem de urgência. */}
      <div className="grid gap-4 lg:gap-6 lg:grid-cols-12 items-start">
        <div className="space-y-4 lg:space-y-6 lg:col-span-7 min-w-0">
          <div {...entra(2)}><PedeAtencao alertas={alertas} /></div>
          <div {...entra(3)}><Rotina rotina={p.rotina} diaHoje={p.diaHoje} hoje={p.hoje} mensal={p.mensal} feitos={feitos} alternar={alternar} /></div>
        </div>
        <div className="grid gap-4 lg:gap-6 md:grid-cols-2 lg:grid-cols-1 items-start lg:col-span-5 min-w-0">
          <div {...entra(3, 'min-w-0')}><Boletos pagar={p.boletos.pagar} receber={p.boletos.receber} /></div>
          <div {...entra(4, 'space-y-4 lg:space-y-6 min-w-0')}>
            <Volumes volumes={p.volumes} />
            <Acerto acerto={p.acerto} />
          </div>
        </div>
      </div>

      <div {...entra(5)}><Semana semana={p.semana} segunda={p.segunda} domingo={p.domingo} /></div>
    </>
  )
}

function EsqueletoPainel() {
  return (
    <div role="status" aria-live="polite" className="space-y-6">
      <span className="sr-only">Montando o painel do dia...</span>
      <div className="space-y-2" aria-hidden>
        <div className="esqueleto h-7 w-64 max-w-full" />
        <div className="esqueleto h-4 w-48" />
      </div>
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 lg:gap-4" aria-hidden>
        {[0, 1, 2, 3].map(i => (
          <div key={i} className="rounded-xl border border-gray-200 bg-white px-4 py-3 sm:px-5 sm:py-4 space-y-2">
            <div className="esqueleto h-3.5 w-24" /><div className="esqueleto h-7 w-20" /><div className="esqueleto h-3 w-28 max-w-full" />
          </div>
        ))}
      </div>
      <div className="grid gap-4 lg:gap-6 lg:grid-cols-12" aria-hidden>
        <div className="space-y-4 lg:col-span-7">
          {[3, 5].map((n, k) => (
            <div key={k} className="bg-white border border-gray-200 rounded-xl overflow-hidden">
              <div className="h-14 px-5 flex items-center border-b border-gray-100"><div className="esqueleto h-4 w-32" /></div>
              {Array.from({ length: n }, (_, i) => (
                <div key={i} className="flex items-center gap-3 px-5 py-3.5 border-b border-gray-100 last:border-0">
                  <div className="esqueleto h-8 w-8 rounded-lg shrink-0" />
                  <div className="flex-1 space-y-2"><div className="esqueleto h-3.5" style={{ width: `${70 - i * 8}%` }} /><div className="esqueleto h-3 w-1/3" /></div>
                </div>
              ))}
            </div>
          ))}
        </div>
        <div className="space-y-4 lg:col-span-5">
          {[4, 2].map((n, k) => (
            <div key={k} className="bg-white border border-gray-200 rounded-xl overflow-hidden">
              <div className="h-14 px-5 flex items-center border-b border-gray-100"><div className="esqueleto h-4 w-28" /></div>
              {Array.from({ length: n }, (_, i) => (
                <div key={i} className="flex items-center gap-3 px-5 py-3.5"><div className="flex-1 esqueleto h-3.5" /><div className="esqueleto h-4 w-16" /></div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
