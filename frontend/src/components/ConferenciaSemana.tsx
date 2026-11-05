import { CheckCircle2, CircleDashed, TriangleAlert } from 'lucide-react'
import { brl } from '../utils/formato'
import { Carregando, Vazio } from './ui/Estados'

// Resposta de POST /api/acerto-alfa/conferencia
export type DiaConferencia = {
  data: string
  romaneioFazenda: number | null
  funruralFazenda: number | null
  funruralClientes: number
  clientes: string[]
  situacao: 'ok' | 'diferenca' | 'sem_fazenda' | 'sem_clientes'
  observacoes: string[]
}
export type BoletoConferido = {
  cliente: string
  data: string
  noRomaneio: number | null
  noBoleto: number | null
  numeroBoleto: string | null
  diferenca: number
  situacao: 'ok' | 'centavo' | 'diferenca' | 'falta_boleto' | 'sobra_boleto'
}
/** Soma dos boletos da fazenda do cliente no dia × valor total do romaneio dele (margem de 2 centavos) */
export type TotalCliente = {
  cliente: string
  data: string
  totalRomaneio: number | null
  somaBoletos: number
  qtdBoletos: number
  diferenca: number
  situacao: 'ok' | 'centavos' | 'diferenca' | 'sem_boletos' | 'sem_total'
}
export type Conferencia = {
  inicio: string | null
  fim: string | null
  dias: DiaConferencia[]
  boletos: BoletoConferido[]
  totais?: TotalCliente[]
  ignorados: string[]
  avisos: string[]
}

const nomeCliente = (a: string) =>
  ({ AURORA: 'Aurora', BRZ: 'BRZ', 'CEDRO FRUTAS': 'Cedro Frutas', DUNAS: 'Dunas', FAROL: 'Litoral (Farol)' }[a] ?? a)

const diaSemana = (iso: string) => {
  const d = new Date(iso.slice(0, 10) + 'T12:00:00')
  const semana = d.toLocaleDateString('pt-BR', { weekday: 'long' }).replace('-feira', '')
  return `${semana.charAt(0).toUpperCase()}${semana.slice(1)}, ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })}`
}

function Selo({ tipo, children }: { tipo: 'ok' | 'erro' | 'falta'; children: React.ReactNode }) {
  const estilo = {
    ok:    'bg-emerald-50 text-emerald-800 border-emerald-200',
    erro:  'bg-red-50 text-red-800 border-red-200',
    falta: 'bg-gema-50 text-gema-900 border-gema-200',
  }[tipo]
  const Icone = tipo === 'ok' ? CheckCircle2 : tipo === 'erro' ? TriangleAlert : CircleDashed
  return (
    <span className={`inline-flex items-center gap-1 text-xs font-medium border rounded-full px-2 py-0.5 whitespace-nowrap ${estilo}`}>
      <Icone size={13} aria-hidden /> {children}
    </span>
  )
}

function CartaoDia({ dia }: { dia: DiaConferencia }) {
  const difFun = dia.funruralFazenda == null ? null : dia.funruralClientes - dia.funruralFazenda
  const funOk = difFun != null && Math.abs(difFun) <= 0.01

  return (
    <div className={`bg-white border rounded-xl overflow-hidden ${dia.situacao === 'diferenca' ? 'border-red-200' : 'border-gray-200'}`}>
      <div className="px-4 py-3 flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="font-semibold text-gray-900">{diaSemana(dia.data)}</h3>
          <p className="text-sm text-gray-500">
            {dia.clientes.length ? dia.clientes.map(nomeCliente).join(' e ') : 'nenhum cliente'}
            {dia.romaneioFazenda && <> contra o romaneio da fazenda <span className="tabular-nums">{dia.romaneioFazenda}</span></>}
          </p>
        </div>
        {dia.situacao === 'ok' && <Selo tipo="ok">Bateu</Selo>}
        {dia.situacao === 'diferenca' && <Selo tipo="erro">Não bateu</Selo>}
        {dia.situacao === 'sem_fazenda' && <Selo tipo="falta">Falta o romaneio da fazenda</Selo>}
        {dia.situacao === 'sem_clientes' && <Selo tipo="falta">Sem romaneio de cliente</Selo>}
      </div>

      {dia.situacao !== 'sem_fazenda' && dia.situacao !== 'sem_clientes' && (
        <dl className="px-4 py-2.5 border-t border-gray-100 grid grid-cols-2 sm:grid-cols-[auto_auto_1fr] gap-x-6 gap-y-1 text-sm tabular-nums">
          <div><dt className="text-xs text-gray-500">Funrural dos clientes</dt><dd className="text-gray-900">{brl(dia.funruralClientes)}</dd></div>
          <div><dt className="text-xs text-gray-500">Funrural da fazenda</dt><dd className="text-gray-900">{brl(dia.funruralFazenda ?? 0)}</dd></div>
          {!funOk && difFun != null && (
            <div className="col-span-2 sm:col-span-1">
              <dt className="text-xs text-red-800">Diferença</dt>
              <dd className="font-semibold text-red-800">{difFun > 0 ? '+' : ''}{brl(difFun)}</dd>
            </div>
          )}
        </dl>
      )}

      {dia.observacoes.length > 0 && (
        <ul className="px-4 pb-3 space-y-0.5">
          {dia.observacoes.map(o => <li key={o} className="text-sm text-gray-500">{o}</li>)}
        </ul>
      )}
    </div>
  )
}

function SituacaoBoleto({ b }: { b: BoletoConferido }) {
  switch (b.situacao) {
    case 'ok': return <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-800"><CheckCircle2 size={15} aria-hidden /> Bateu</span>
    case 'centavo': return <span className="text-sm text-gray-500">1 centavo</span>
    case 'diferenca': return <span className="text-sm font-semibold text-red-800 tabular-nums">{b.diferenca > 0 ? '+' : ''}{brl(b.diferenca)}</span>
    case 'falta_boleto': return <span className="text-sm font-medium text-gema-900">Boleto não veio</span>
    case 'sobra_boleto': return <span className="text-sm font-medium text-gema-900">Sem romaneio</span>
  }
}

function SituacaoTotal({ t }: { t: TotalCliente }) {
  switch (t.situacao) {
    case 'ok': return <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-800"><CheckCircle2 size={15} aria-hidden /> Bateu</span>
    case 'centavos': return <span className="text-sm text-gray-500">{Math.round(Math.abs(t.diferenca) * 100)} centavo(s)</span>
    case 'diferenca': return <span className="text-sm font-semibold text-red-800 tabular-nums">{t.diferenca > 0 ? '+' : ''}{brl(t.diferenca)}</span>
    case 'sem_boletos': return <span className="text-sm font-medium text-gema-900">Boletos não vieram</span>
    case 'sem_total': return <span className="text-sm font-medium text-red-800">Total não lido</span>
  }
}

// Soma dos boletos da fazenda × "VALOR A PAGAR" do romaneio de cada cliente
function TotaisClientes({ totais }: { totais: TotalCliente[] }) {
  const problemas = totais.filter(t => t.situacao === 'diferenca' || t.situacao === 'sem_total').length
  return (
    <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
      <div className="px-4 py-3 border-b border-gray-100 flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="font-semibold text-gray-900">Total do romaneio × soma dos boletos</h3>
          <p className="text-sm text-gray-500">Até 2 centavos de diferença é arredondamento.</p>
        </div>
        {problemas > 0
          ? <Selo tipo="erro">{problemas} não {problemas === 1 ? 'bateu' : 'bateram'}</Selo>
          : <Selo tipo="ok">Batem com os romaneios</Selo>}
      </div>
      <ul className="divide-y divide-gray-100">
        {totais.map(t => (
          <li key={`${t.cliente}-${t.data}`} className={`px-4 py-3 space-y-1.5 ${t.situacao === 'diferenca' || t.situacao === 'sem_total' ? 'bg-red-50/70' : ''}`}>
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="font-medium text-gray-900">{nomeCliente(t.cliente)}</p>
                <p className="text-sm text-gray-500 tabular-nums">{diaSemana(t.data)}, {t.qtdBoletos === 1 ? '1 boleto' : `${t.qtdBoletos} boletos`}</p>
              </div>
              <SituacaoTotal t={t} />
            </div>
            <dl className="grid grid-cols-2 gap-2 text-sm tabular-nums sm:max-w-md">
              <div><dt className="text-xs text-gray-500">Total do romaneio</dt><dd className="text-gray-900">{t.totalRomaneio != null ? brl(t.totalRomaneio) : '—'}</dd></div>
              <div><dt className="text-xs text-gray-500">Soma dos boletos</dt><dd className="text-gray-900">{brl(t.somaBoletos)}</dd></div>
            </dl>
          </li>
        ))}
      </ul>
    </div>
  )
}

export default function ConferenciaSemana({ dados, carregando }: { dados: Conferencia | null; carregando: boolean }) {
  if (carregando && !dados) {
    return <Carregando linhas={3} rotulo="Conferindo com os romaneios salvos" />
  }
  if (!dados) return null

  const vazio = dados.dias.length === 0 && dados.boletos.length === 0 && !dados.totais?.length
  const problemasBoleto = dados.boletos.filter(b => b.situacao === 'diferenca').length
  const centavos = dados.boletos.filter(b => b.situacao === 'centavo').length

  return (
    <section aria-labelledby="conferencia-semana" className={`space-y-3 transition-opacity ${carregando ? 'opacity-60' : ''}`}>
      <div>
        <h2 id="conferencia-semana" className="text-lg font-semibold text-gray-900">Conferência da semana</h2>
        <p className="text-sm text-gray-600">
          Os romaneios dos clientes vêm das cargas salvas no sistema. Nada para enviar além dos PDFs.
        </p>
      </div>

      {vazio && (
        <Vazio titulo="Nada para conferir nestes PDFs">Nenhum romaneio de cliente ou da fazenda da semana.</Vazio>
      )}

      <div className="grid gap-3 lg:grid-cols-2">
        {dados.dias.map(d => <CartaoDia key={d.data} dia={d} />)}
      </div>

      {dados.boletos.length > 0 && (
        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden">
          <div className="px-4 py-3 border-b border-gray-100 flex flex-wrap items-center justify-between gap-2">
            <h3 className="font-semibold text-gray-900">Boletos</h3>
            {problemasBoleto > 0
              ? <Selo tipo="erro">{problemasBoleto} com diferença</Selo>
              : <Selo tipo="ok">Batem com os romaneios</Selo>}
          </div>

          {/* Celular: um cartão por boleto */}
          <ul className="sm:hidden divide-y divide-gray-100">
            {dados.boletos.map((b, i) => (
              <li key={i} className={`px-4 py-3 space-y-1.5 ${b.situacao === 'diferenca' ? 'bg-red-50/70' : ''}`}>
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-medium text-gray-900">{nomeCliente(b.cliente)}</p>
                    <p className="text-sm text-gray-500 tabular-nums">{diaSemana(b.data)}{b.numeroBoleto && <>, boleto {b.numeroBoleto}</>}</p>
                  </div>
                  <SituacaoBoleto b={b} />
                </div>
                <dl className="grid grid-cols-2 gap-2 text-sm tabular-nums">
                  <div><dt className="text-xs text-gray-500">No romaneio</dt><dd className="text-gray-900">{b.noRomaneio != null ? brl(b.noRomaneio) : '—'}</dd></div>
                  <div><dt className="text-xs text-gray-500">No boleto</dt><dd className="text-gray-900">{b.noBoleto != null ? brl(b.noBoleto) : '—'}</dd></div>
                </dl>
              </li>
            ))}
          </ul>

          <div className="hidden sm:block overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-xs text-gray-500 bg-gray-50">
                <tr>
                  <th className="text-left font-medium px-4 py-2">Cliente</th>
                  <th className="text-right font-medium px-3 py-2">No romaneio</th>
                  <th className="text-right font-medium px-3 py-2">No boleto</th>
                  <th className="text-right font-medium px-4 py-2">Situação</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {dados.boletos.map((b, i) => (
                  <tr key={i} className={b.situacao === 'diferenca' ? 'bg-red-50/70' : ''}>
                    <td className="px-4 py-2.5">
                      <p className="text-gray-900">{nomeCliente(b.cliente)}</p>
                      <p className="text-sm text-gray-500 tabular-nums">{diaSemana(b.data)}{b.numeroBoleto && <>, boleto {b.numeroBoleto}</>}</p>
                    </td>
                    <td className="px-3 py-2.5 text-right tabular-nums">{b.noRomaneio != null ? brl(b.noRomaneio) : '—'}</td>
                    <td className="px-3 py-2.5 text-right tabular-nums">{b.noBoleto != null ? brl(b.noBoleto) : '—'}</td>
                    <td className="px-4 py-2.5 text-right whitespace-nowrap"><SituacaoBoleto b={b} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {centavos > 0 && (
            <p className="px-4 py-2.5 border-t border-gray-100 text-sm text-gray-500">
              {centavos === 1 ? 'Um boleto difere' : `${centavos} boletos diferem`} em 1 centavo do romaneio. É arredondamento e está tudo certo.
            </p>
          )}
        </div>
      )}

      {!!dados.totais?.length && <TotaisClientes totais={dados.totais} />}

      {(dados.ignorados.length > 0 || dados.avisos.length > 0) && (
        <ul className="space-y-1">
          {[...dados.ignorados, ...dados.avisos].map(t => <li key={t} className="text-sm text-gray-500">{t}</li>)}
        </ul>
      )}
    </section>
  )
}
