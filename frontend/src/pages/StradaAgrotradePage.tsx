import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { AlertTriangle, ChevronLeft, ChevronRight, FileText, Inbox, LayoutDashboard, Link2, ListChecks, Search, Truck, Unlink } from 'lucide-react'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import Botao from '../components/ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { Modal } from '../components/ModalBase'
import { formatBRL } from '../services/api'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import {
  stradaApi, type ContratoFatura, type FaturaDetalhe, type FaturaResumo, type Modo, type Painel,
  type ResultadoArquivo, type ViagemBase, type ViagemResumo,
} from '../services/stradaApi'
import { dataBr } from '../utils/formato'

// Agrotrade / TIP Bank (PEN-39): sobe os arquivos da Strada (.txt de repasse e PDFs "Baixar contrato")
// e a planilha AGROTRADE; o sistema dá baixa nos fretes e reembolsos e divide cada transferência por sócio.

type Aba = 'painel' | 'faturas' | 'base'
const ABAS = [
  ['painel', 'Painel', LayoutDashboard],
  ['faturas', 'Faturas', ListChecks],
  ['base', 'Base de viagens', Truck],
] as const

const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']
const inputCls = 'w-full min-h-11 sm:min-h-10 text-sm border border-gray-300 rounded-lg px-3 py-2'
const dinheiro = (v: number | null | undefined) => (v == null ? 'sem valor' : formatBRL(v))
const data = (d: string | null | undefined) => (d ? dataBr(d) : 'sem data')

const SITUACAO: Record<ContratoFatura['situacao'], { texto: string; cls: string }> = {
  ligado: { texto: 'Ligado à viagem', cls: 'bg-gray-100 text-gray-700' },
  manual: { texto: 'Ligado à mão', cls: 'bg-gray-100 text-gray-700' },
  sugestao: { texto: 'Confirmar NF', cls: 'bg-amber-100 text-amber-800' },
  'sem viagem': { texto: 'Sem viagem', cls: 'bg-red-50 text-red-700' },
}
const TIPO: Record<ContratoFatura['tipo'], string> = { frete: 'Frete', reembolso: 'Reembolso', indefinido: 'A definir' }

function useMes() {
  const hoje = new Date()
  const [ano, setAno] = useState(hoje.getFullYear())
  const [mes, setMes] = useState(hoje.getMonth() + 1)
  const andar = (n: number) => {
    const d = new Date(ano, mes - 1 + n, 1)
    setAno(d.getFullYear()); setMes(d.getMonth() + 1)
  }
  return { ano, mes, andar }
}

function SeletorMes({ ano, mes, andar }: ReturnType<typeof useMes>) {
  return (
    <div className="inline-flex items-center gap-1 bg-white border border-gray-200 rounded-lg p-1" role="group" aria-label="Mês">
      <button type="button" onClick={() => andar(-1)} aria-label="Mês anterior"
              className="min-h-10 sm:min-h-8 px-2 rounded-md text-gray-600 hover:bg-gray-100"><ChevronLeft className="w-4 h-4" /></button>
      <span className="px-2 text-sm font-medium text-gray-900 min-w-36 text-center capitalize" aria-live="polite">{MESES[mes - 1]} de {ano}</span>
      <button type="button" onClick={() => andar(1)} aria-label="Próximo mês"
              className="min-h-10 sm:min-h-8 px-2 rounded-md text-gray-600 hover:bg-gray-100"><ChevronRight className="w-4 h-4" /></button>
    </div>
  )
}

export default function StradaAgrotradePage() {
  const id = useAbas('strada')
  const [aba, setAba] = useState<Aba>('painel')
  const periodo = useMes()
  const [versao, setVersao] = useState(0)
  const [faturaAberta, setFaturaAberta] = useState<string | null>(null)
  const recarregar = () => setVersao(v => v + 1)

  return (
    <div className="space-y-5">
      <CabecalhoTela
        descricao="Fretes e reembolsos da Agrotrade pagos pela Strada (TIP Bank). Suba os arquivos de repasse (.txt) e os PDFs dos contratos; o sistema dá baixa nas viagens e divide cada transferência por sócio, com a taxa de 1%."
        acoes={<SeletorMes {...periodo} />}
      />
      <Abas id={id} valor={aba} opcoes={ABAS} onChange={setAba} rotulo="Partes da tela" />
      <PainelAba id={id} valor={aba}>
        {aba === 'painel' && <AbaPainel ano={periodo.ano} mes={periodo.mes} versao={versao} abrir={setFaturaAberta} />}
        {aba === 'faturas' && <AbaFaturas ano={periodo.ano} mes={periodo.mes} versao={versao} mudou={recarregar} abrir={setFaturaAberta} />}
        {aba === 'base' && <AbaBase ano={periodo.ano} mes={periodo.mes} versao={versao} mudou={recarregar} />}
      </PainelAba>
      {faturaAberta && <DetalheFatura numero={faturaAberta} onClose={() => setFaturaAberta(null)} mudou={recarregar} />}
    </div>
  )
}

// ───────────────────────── Painel ─────────────────────────

function AbaPainel({ ano, mes, versao, abrir }: { ano: number; mes: number; versao: number; abrir: (n: string) => void }) {
  const [painel, setPainel] = useState<Painel | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setCarregando(true)
    try {
      const { data: p } = await stradaApi.painel(ano, mes)
      setPainel(p); setErro(null)
    } catch (e) { setErro(await erroApi(e, 'Não consegui carregar o painel.')) }
    finally { setCarregando(false) }
  }, [ano, mes])
  useEffect(() => { carregar() }, [carregar, versao])

  if (carregando && !painel) return <Carregando forma="cartoes" linhas={3} rotulo="Carregando o painel" />
  if (erro) return <ErroCarga mensagem={erro} onTentar={carregar} />
  if (!painel) return null

  const nada = painel.totais.recebido === 0 && painel.totais.aReceber === 0 && painel.incompletas.length === 0
  return (
    <div className="space-y-5">
      {painel.divergencias.length > 0 && (
        <section aria-label="Divergências" className="bg-white border border-amber-200 rounded-xl overflow-hidden">
          <div className="px-4 py-3 bg-amber-50 border-b border-amber-200 flex items-center gap-2">
            <AlertTriangle className="w-4 h-4 text-amber-700" aria-hidden />
            <h3 className="text-sm font-semibold text-amber-900">Para conferir ({painel.divergencias.length})</h3>
          </div>
          <ul className="divide-y divide-gray-100">
            {painel.divergencias.map((d, i) => (
              <li key={i} className="px-4 py-3 flex flex-col sm:flex-row sm:items-center gap-2">
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-semibold text-amber-800">{d.tipo}</p>
                  <p className="text-sm text-gray-800">{d.texto}</p>
                </div>
                {d.fatura && <Botao tamanho="sm" onClick={() => abrir(d.fatura!)}>Abrir a fatura {d.fatura}</Botao>}
              </li>
            ))}
          </ul>
        </section>
      )}

      {nada ? (
        <Vazio icone={Inbox} titulo="Nada neste mês">Nenhuma fatura paga nem viagem a receber em {MESES[mes - 1]} de {ano}.</Vazio>
      ) : (
        <>
          <section aria-label="Por sócio" className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
            {painel.socios.map(s => (
              <div key={s.socio ?? 'sem'} className="bg-white border border-gray-200 rounded-xl p-4 space-y-2">
                <p className="font-semibold text-gray-900">{s.nome}</p>
                <dl className="text-sm space-y-1">
                  <div className="flex justify-between gap-2"><dt className="text-gray-600">Recebido</dt><dd className="tabular-nums font-medium">{formatBRL(s.recebido)}</dd></div>
                  <div className="flex justify-between gap-2"><dt className="text-gray-600">Taxa de 1%</dt><dd className="tabular-nums">{formatBRL(s.taxa)}</dd></div>
                  <div className="flex justify-between gap-2"><dt className="text-gray-600">Repasse</dt><dd className="tabular-nums font-semibold">{formatBRL(s.repasse)}</dd></div>
                  <div className="flex justify-between gap-2 pt-1 border-t border-gray-100">
                    <dt className="text-gray-600">A receber ({s.viagensAReceber} {s.viagensAReceber === 1 ? 'item' : 'itens'})</dt>
                    <dd className="tabular-nums">{formatBRL(s.aReceber)}</dd>
                  </div>
                </dl>
              </div>
            ))}
            <div className="bg-gray-50 border border-gray-200 rounded-xl p-4 space-y-2">
              <p className="font-semibold text-gray-900">Total do mês</p>
              <dl className="text-sm space-y-1">
                <div className="flex justify-between gap-2"><dt className="text-gray-600">Recebido</dt><dd className="tabular-nums font-medium">{formatBRL(painel.totais.recebido)}</dd></div>
                <div className="flex justify-between gap-2"><dt className="text-gray-600">Taxa</dt><dd className="tabular-nums">{formatBRL(painel.totais.taxa)}</dd></div>
                <div className="flex justify-between gap-2"><dt className="text-gray-600">Repasse</dt><dd className="tabular-nums font-semibold">{formatBRL(painel.totais.repasse)}</dd></div>
                <div className="flex justify-between gap-2 pt-1 border-t border-gray-200"><dt className="text-gray-600">A receber</dt><dd className="tabular-nums">{formatBRL(painel.totais.aReceber)}</dd></div>
              </dl>
            </div>
          </section>

          {painel.incompletas.length > 0 && (
            <Aviso tipo="atencao" titulo="Fatura sem divisão por sócio"
                   itens={painel.incompletas.map(f => `Fatura ${f.numero} de ${data(f.dataPagamento)}: ${formatBRL(f.total)} (repasse ${formatBRL(f.repasse)}). O arquivo só trouxe o cabeçalho; ela fica fora dos totais até subir o "Exportar detalhamento".`)} />
          )}

          <section aria-label="A receber" className="bg-white border border-gray-200 rounded-xl overflow-hidden">
            <h3 className="px-4 py-3 text-sm font-semibold text-gray-900 border-b border-gray-200">A receber das viagens de {MESES[mes - 1]}</h3>
            {painel.aReceber.length === 0 ? (
              <p className="px-4 py-4 text-sm text-gray-600">Nada em aberto nas viagens deste mês.</p>
            ) : (
              <ul className="divide-y divide-gray-100">
                {painel.aReceber.map((r, i) => (
                  <li key={i} className="px-4 py-3 grid grid-cols-1 sm:grid-cols-[6rem_1fr_auto] gap-1 sm:gap-4 text-sm">
                    <span className="text-gray-600 tabular-nums">{data(r.data)}</span>
                    <span className="min-w-0">
                      <span className="font-medium text-gray-900">{r.tipo === 'frete' ? 'Frete' : r.tipo === 'diária' ? 'Diária' : 'Descarga'}</span>
                      {' · '}NF {r.nf ?? 'sem NF'} · {r.placa ?? 'sem placa'} · {r.nomeSocio}
                      <span className="block text-gray-600">{r.motivo}{r.contrato ? ` (contrato ${r.contrato})` : ''}</span>
                    </span>
                    <span className="tabular-nums font-medium sm:text-right">{formatBRL(r.valor)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </>
      )}

      {painel.contratosSemFatura.length > 0 && (
        <section aria-label="Contratos esperando a fatura" className="bg-white border border-gray-200 rounded-xl overflow-hidden">
          <h3 className="px-4 py-3 text-sm font-semibold text-gray-900 border-b border-gray-200">PDFs de contrato esperando a fatura ({painel.contratosSemFatura.length})</h3>
          <ul className="divide-y divide-gray-100">
            {painel.contratosSemFatura.map(c => (
              <li key={c.id} className="px-4 py-3 text-sm flex flex-col sm:flex-row sm:justify-between gap-1">
                <span>Contrato {c.contrato} · NF {c.nf ?? 'sem NF'} · {c.placa ?? 'sem placa'} · {c.situacao === 'sugestao' ? `NF quase igual à ${c.nfSugerida}` : c.situacao === 'sem viagem' ? 'sem viagem' : 'viagem achada'}</span>
                <span className="tabular-nums font-medium">{formatBRL(c.valor)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

// ───────────────────────── Faturas ─────────────────────────

function AbaFaturas({ ano, mes, versao, mudou, abrir }: { ano: number; mes: number; versao: number; mudou: () => void; abrir: (n: string) => void }) {
  const [faturas, setFaturas] = useState<FaturaResumo[] | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setCarregando(true)
    try {
      const { data: f } = await stradaApi.faturas(ano, mes)
      setFaturas(f); setErro(null)
    } catch (e) { setErro(await erroApi(e, 'Não consegui carregar as faturas.')) }
    finally { setCarregando(false) }
  }, [ano, mes])
  useEffect(() => { carregar() }, [carregar, versao])

  return (
    <div className="space-y-5">
      <Envio mudou={mudou} />
      {carregando && !faturas ? <Carregando linhas={4} rotulo="Carregando as faturas" />
        : erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !faturas || faturas.length === 0 ? (
          <Vazio icone={FileText} titulo="Nenhuma fatura neste mês">Suba os arquivos de repasse da Strada acima; a fatura entra no mês em que foi paga.</Vazio>
        ) : (
          <ul className="space-y-2" aria-label="Faturas do mês">
            {faturas.map(f => (
              <li key={f.id}>
                <button type="button" onClick={() => abrir(f.numero)}
                        className="w-full text-left bg-white border border-gray-200 rounded-xl p-4 hover:border-gray-400 transition-colors">
                  <div className="flex flex-col sm:flex-row sm:items-center gap-2">
                    <div className="flex-1 min-w-0">
                      <p className="font-semibold text-gray-900">Fatura {f.numero} <span className="font-normal text-gray-600">· paga em {data(f.dataPagamento)}</span></p>
                      <p className="text-sm text-gray-600">
                        {f.porSocio.length > 0 ? f.porSocio.map(s => `${s.nome} ${formatBRL(s.repasse)}`).join(' · ') : 'Sem contratos no arquivo'}
                      </p>
                    </div>
                    <div className="text-sm sm:text-right tabular-nums">
                      <p>Total {formatBRL(f.total)} · taxa {formatBRL(f.taxa)}</p>
                      <p className="font-semibold">Repasse {formatBRL(f.repasse)}</p>
                    </div>
                  </div>
                  {(f.incompleta || f.pendencias > 0) && (
                    <p className="mt-2 text-sm text-amber-800">
                      {f.incompleta ? 'Fatura incompleta: falta o "Exportar detalhamento". ' : ''}
                      {f.pendencias > 0 ? `${f.pendencias} ${f.pendencias === 1 ? 'contrato para conferir' : 'contratos para conferir'}.` : ''}
                    </p>
                  )}
                </button>
              </li>
            ))}
          </ul>
        )}
    </div>
  )
}

// Sobe .txt (faturas) e .pdf (contratos) juntos; arquivo que já existe e mudou pede Manter ou Substituir (UI-21).
function Envio({ mudou }: { mudou: () => void }) {
  const [enviando, setEnviando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoArquivo[]>([])
  const [pendentes, setPendentes] = useState<{ arquivos: File[]; itens: ResultadoArquivo[] } | null>(null)

  const subir = async (arquivos: File[], modo?: Modo) => {
    const txt = arquivos.filter(a => !a.name.toLowerCase().endsWith('.pdf'))
    const pdf = arquivos.filter(a => a.name.toLowerCase().endsWith('.pdf'))
    setEnviando(true)
    try {
      const r: ResultadoArquivo[] = []
      // Faturas primeiro: o PDF completa o contrato da fatura que já subiu.
      if (txt.length) r.push(...(await stradaApi.importarFaturas(txt, modo)).data)
      if (pdf.length) r.push(...(await stradaApi.importarPdfs(pdf, modo)).data)
      const confirmar = r.filter(x => x.status === 'confirmar')
      setResultado(prev => (modo ? [...prev.filter(p => !confirmar.some(c => c.arquivo === p.arquivo) && !r.some(x => x.arquivo === p.arquivo)), ...r] : r))
      setPendentes(confirmar.length ? { arquivos: arquivos.filter(a => confirmar.some(c => c.arquivo === a.name)), itens: confirmar } : null)
      const entrou = r.filter(x => ['criado', 'substituido', 'atualizado'].includes(x.status)).length
      if (entrou) toast.success(`${entrou} ${entrou === 1 ? 'arquivo entrou' : 'arquivos entraram'} no sistema.`)
      mudou()
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui subir os arquivos.'))
    } finally { setEnviando(false) }
  }

  return (
    <section aria-label="Subir arquivos da Strada" className="space-y-3">
      <AreaSoltar aceita={['.txt', '.pdf']} onArquivos={a => subir(a)} carregando={enviando} textoCarregando="Lendo os arquivos..."
                  titulo="Arraste os arquivos da Strada"
                  dica='Arquivo de repasse de cada fatura (.txt) e o PDF "Baixar contrato" dos reembolsos de descarga e diária. Pode subir vários de uma vez.' />
      {resultado.length > 0 && <ResultadoEnvio itens={resultado} onFechar={() => setResultado([])} />}
      {pendentes && (
        <ModalManterSubstituir itens={pendentes.itens} enviando={enviando} onClose={() => setPendentes(null)}
                               escolher={modo => subir(pendentes.arquivos, modo)} />
      )}
    </section>
  )
}

const STATUS: Record<ResultadoArquivo['status'], string> = {
  criado: 'Entrou', atualizado: 'Atualizado', substituido: 'Substituído', igual: 'Já estava, igual',
  mantido: 'Mantido como estava', confirmar: 'Esperando sua escolha', erro: 'Não entrou',
}

function ResultadoEnvio({ itens, onFechar }: { itens: ResultadoArquivo[]; onFechar: () => void }) {
  const erros = itens.filter(i => i.status === 'erro').length
  return (
    <Aviso tipo={erros ? 'atencao' : 'sucesso'} titulo={erros ? `${erros} ${erros === 1 ? 'arquivo não entrou' : 'arquivos não entraram'}` : 'Arquivos lidos'} onFechar={onFechar}>
      <ul className="mt-1 space-y-1 text-sm">
        {itens.map((i, n) => (
          <li key={n}>
            <span className="font-medium">{i.arquivo}</span>: {STATUS[i.status]}
            {i.referencia ? ` (${i.referencia})` : ''}{i.mensagem ? `. ${i.mensagem}` : ''}
            {i.avisos.length > 0 && <span className="block text-gray-700">{i.avisos.join(' · ')}</span>}
          </li>
        ))}
      </ul>
    </Aviso>
  )
}

function ModalManterSubstituir({ itens, enviando, onClose, escolher }: {
  itens: ResultadoArquivo[]; enviando: boolean; onClose: () => void; escolher: (m: Modo) => void
}) {
  return (
    <Modal titulo="Arquivo que já está no sistema" onClose={onClose} largura="lg"
           descricao="Estes arquivos já tinham subido e agora trazem mudanças. Veja o que muda e escolha."
           rodape={
             <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
               <Botao onClick={() => escolher('manter')} carregando={enviando}>Manter como está</Botao>
               <Botao variante="primario" onClick={() => escolher('substituir')} carregando={enviando}>Substituir</Botao>
             </div>
           }>
      {itens.map((i, n) => (
        <div key={n} className="border border-gray-200 rounded-lg p-3 space-y-1">
          <p className="font-medium text-gray-900">{i.arquivo}{i.referencia ? ` (${i.referencia})` : ''}</p>
          {i.mensagem && <p className="text-sm text-gray-700">{i.mensagem}</p>}
          <details className="text-sm" open={i.mudancas.length <= 6}>
            <summary className="cursor-pointer text-gray-700">{i.mudancas.length} {i.mudancas.length === 1 ? 'mudança' : 'mudanças'}</summary>
            <ul className="mt-1 space-y-0.5 text-gray-800">
              {i.mudancas.map((m, k) => <li key={k}><span className="font-medium">{m.chave}</span>: {m.descricao}</li>)}
            </ul>
          </details>
        </div>
      ))}
    </Modal>
  )
}

// ───────────────────────── Detalhe da fatura ─────────────────────────

function DetalheFatura({ numero, onClose, mudou }: { numero: string; onClose: () => void; mudou: () => void }) {
  const confirmar = useConfirmar()
  const [fatura, setFatura] = useState<FaturaDetalhe | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [ligando, setLigando] = useState<ContratoFatura | null>(null)
  const [ocupado, setOcupado] = useState<number | null>(null)

  const carregar = useCallback(async () => {
    try {
      const { data: f } = await stradaApi.fatura(numero)
      setFatura(f); setErro(null)
    } catch (e) { setErro(await erroApi(e, 'Não consegui abrir a fatura.')) }
  }, [numero])
  useEffect(() => { carregar() }, [carregar])

  const agir = async (c: ContratoFatura, acao: () => Promise<unknown>, ok: string) => {
    setOcupado(c.id)
    try {
      await acao()
      toast.success(ok)
      await carregar(); mudou()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui gravar.')) }
    finally { setOcupado(null) }
  }

  const desfazer = async (c: ContratoFatura) => {
    if (!await confirmar({ titulo: `Desfazer a ligação do contrato ${c.contrato}?`, texto: 'O sistema volta a procurar a viagem sozinho (pela coluna de contrato da planilha ou pela NF do PDF).', confirmar: 'Desfazer', perigo: false })) return
    agir(c, () => stradaApi.desligar(c.id), `Ligação do contrato ${c.contrato} desfeita.`)
  }

  return (
    <Modal titulo={`Fatura ${numero}`} onClose={onClose} largura="xl" fundoCinza
           descricao={fatura ? `Paga em ${data(fatura.dataPagamento)} · total ${formatBRL(fatura.total)} · taxa ${formatBRL(fatura.taxa)} · repasse ${formatBRL(fatura.repasse)}` : undefined}>
      {erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !fatura ? <Carregando linhas={3} rotulo="Abrindo a fatura" />
        : (
          <div className="space-y-4">
            {(fatura.avisos.length > 0 || fatura.observacoes.length > 0) && (
              <Aviso tipo="info" itens={[...fatura.avisos, ...fatura.observacoes]} />
            )}
            {fatura.porSocio.length > 0 && (
              <section aria-label="Divisão por sócio" className="bg-white border border-gray-200 rounded-xl p-4">
                <h3 className="text-sm font-semibold text-gray-900 mb-2">Divisão por sócio</h3>
                <ul className="space-y-1 text-sm">
                  {fatura.porSocio.map(s => (
                    <li key={s.socio ?? 'sem'} className="flex flex-col sm:flex-row sm:justify-between gap-0.5">
                      <span className="font-medium">{s.nome}</span>
                      <span className="tabular-nums">{formatBRL(s.total)} − taxa {formatBRL(s.taxa)} = <strong>{formatBRL(s.repasse)}</strong></span>
                    </li>
                  ))}
                </ul>
              </section>
            )}
            <ul className="space-y-2" aria-label="Contratos da fatura">
              {fatura.contratos.map(c => (
                <li key={c.id} className="bg-white border border-gray-200 rounded-xl p-4 space-y-2">
                  <div className="flex flex-col sm:flex-row sm:items-start gap-2">
                    <div className="flex-1 min-w-0">
                      <p className="font-semibold text-gray-900">
                        Contrato {c.contrato}
                        <span className={`ml-2 align-middle inline-block text-xs font-medium px-2 py-0.5 rounded-full ${SITUACAO[c.situacao].cls}`}>{SITUACAO[c.situacao].texto}</span>
                      </p>
                      <p className="text-sm text-gray-700">
                        {TIPO[c.tipo]} · {c.placa ?? 'sem placa'} · {c.nomeSocio}{c.nf ? ` · NF ${c.nf}` : ''}{c.temPdf ? ' · com PDF' : ''}
                      </p>
                      {c.viagem && (
                        <p className="text-sm text-gray-600">
                          Viagem de {data(c.viagem.data)}, NF {c.viagem.nfOriginal ?? c.viagem.nf}, {c.viagem.motorista ?? 'sem motorista'}{c.viagem.destino ? `, ${c.viagem.destino}` : ''}
                        </p>
                      )}
                      {c.situacao === 'sugestao' && (
                        <p className="text-sm text-amber-800">NF do contrato {c.nf} quase igual à da viagem {c.nfSugerida} ({c.placa}). Confirme se é a mesma viagem.</p>
                      )}
                    </div>
                    <div className="text-sm sm:text-right tabular-nums">
                      <p className="font-semibold">{formatBRL(c.valor)}</p>
                      {c.valorEsperado != null && <p className="text-gray-600">Esperado {formatBRL(c.valorEsperado)}</p>}
                      {c.diferenca != null && Math.abs(c.diferenca) >= 0.01 && (
                        <p className="text-amber-800">Diferença {formatBRL(c.diferenca)}</p>
                      )}
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {c.situacao === 'sugestao' && (
                      <Botao tamanho="sm" variante="primario" carregando={ocupado === c.id}
                             onClick={() => agir(c, () => stradaApi.confirmar(c.id), `Contrato ${c.contrato} ligado à viagem da NF ${c.nfSugerida}.`)}>
                        Confirmar
                      </Botao>
                    )}
                    {(c.situacao === 'sem viagem' || c.situacao === 'sugestao') && (
                      <Botao tamanho="sm" icone={Link2} onClick={() => setLigando(c)}>
                        {c.situacao === 'sugestao' ? 'Escolher outra viagem' : 'Ligar à viagem'}
                      </Botao>
                    )}
                    {c.situacao === 'manual' && (
                      <Botao tamanho="sm" variante="fantasma" icone={Unlink} carregando={ocupado === c.id} onClick={() => desfazer(c)}>Desfazer ligação</Botao>
                    )}
                  </div>
                  {c.situacao === 'sem viagem' && c.candidatos.length > 0 && (
                    <p className="text-sm text-gray-600">
                      Dica pelo valor (não liga sozinho): {c.candidatos.map(v => `NF ${v.nfOriginal ?? v.nf} ${v.placa ?? ''} de ${data(v.data)}`).join('; ')}.
                    </p>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}
      {ligando && (
        <ModalLigar contrato={ligando} onClose={() => setLigando(null)}
                    feito={async () => { setLigando(null); await carregar(); mudou() }} />
      )}
    </Modal>
  )
}

function ModalLigar({ contrato, onClose, feito }: { contrato: ContratoFatura; onClose: () => void; feito: () => void }) {
  const [busca, setBusca] = useState(contrato.nf ?? contrato.placa ?? '')
  const [viagens, setViagens] = useState<ViagemBase[] | null>(null)
  const [buscando, setBuscando] = useState(false)
  const [placa, setPlaca] = useState('')
  const [gravando, setGravando] = useState(false)

  const procurar = useCallback(async (texto: string) => {
    if (!texto.trim()) { setViagens(null); return }
    setBuscando(true)
    try {
      const { data: v } = await stradaApi.viagens({ busca: texto.trim() })
      setViagens(v)
    } catch (e) { toast.error(await erroApi(e, 'Não consegui procurar as viagens.')) }
    finally { setBuscando(false) }
  }, [])
  useEffect(() => { procurar(contrato.nf?.split(',')[0] ?? contrato.placa ?? '') }, [contrato, procurar])

  const ligar = async (pedido: { viagemId?: number; placa?: string }, texto: string) => {
    setGravando(true)
    try {
      await stradaApi.ligar(contrato.id, pedido)
      toast.success(texto)
      feito()
    } catch (e) { toast.error(await erroApi(e, 'Não consegui ligar o contrato.')) }
    finally { setGravando(false) }
  }

  const sugeridas: ViagemResumo[] = contrato.candidatos
  return (
    <Modal titulo={`Ligar o contrato ${contrato.contrato}`} onClose={onClose} largura="lg"
           descricao={`${formatBRL(contrato.valor)}${contrato.nf ? ` · NF ${contrato.nf}` : ''}${contrato.placa ? ` · ${contrato.placa}` : ''}`}>
      <form className="flex gap-2" onSubmit={e => { e.preventDefault(); procurar(busca) }}>
        <label className="flex-1">
          <span className="sr-only">Procurar viagem</span>
          <input value={busca} onChange={e => setBusca(e.target.value)} className={inputCls} placeholder="NF, placa, contrato ou motorista" />
        </label>
        <Botao type="submit" icone={Search} carregando={buscando}>Procurar</Botao>
      </form>
      {sugeridas.length > 0 && (
        <p className="text-sm text-gray-600">Viagens com reembolso anotado do mesmo valor (só dica): {sugeridas.map(v => `NF ${v.nfOriginal ?? v.nf}`).join(', ')}.</p>
      )}
      {viagens && viagens.length === 0 && <p className="text-sm text-gray-600">Nenhuma viagem achada com "{busca}".</p>}
      {viagens && viagens.length > 0 && (
        <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg max-h-80 overflow-y-auto">
          {viagens.slice(0, 30).map(v => (
            <li key={v.id} className="p-3 flex flex-col sm:flex-row sm:items-center gap-2 text-sm">
              <div className="flex-1 min-w-0">
                <p className="font-medium text-gray-900">NF {v.nfOriginal ?? v.nf} · {v.placa ?? 'sem placa'} · {data(v.data)}</p>
                <p className="text-gray-600">
                  {v.motorista ?? 'sem motorista'} · frete {dinheiro(v.freteEsperado)}
                  {v.difDescarga ? ` · descarga ${formatBRL(v.difDescarga)}` : ''}{v.valorDiaria ? ` · diária ${formatBRL(v.valorDiaria)}` : ''}
                  {v.contrato ? ` · contrato ${v.contrato}` : ''}
                </p>
              </div>
              <Botao tamanho="sm" carregando={gravando} onClick={() => ligar({ viagemId: v.id }, `Contrato ${contrato.contrato} ligado à viagem da NF ${v.nfOriginal ?? v.nf}.`)}>Ligar</Botao>
            </li>
          ))}
        </ul>
      )}
      <form className="border-t border-gray-200 pt-3 space-y-2" onSubmit={e => { e.preventDefault(); if (placa.trim()) ligar({ placa }, `Contrato ${contrato.contrato} ligado à placa ${placa.toUpperCase()}.`) }}>
        <p className="text-sm text-gray-700">Sem a viagem na base? Ligue só à placa, para o valor ir ao sócio certo.</p>
        <div className="flex gap-2">
          <label className="flex-1">
            <span className="sr-only">Placa</span>
            <input value={placa} onChange={e => setPlaca(e.target.value)} className={inputCls} placeholder="Placa do caminhão" />
          </label>
          <Botao type="submit" carregando={gravando} disabled={!placa.trim()}>Ligar à placa</Botao>
        </div>
      </form>
    </Modal>
  )
}

// ───────────────────────── Base de viagens ─────────────────────────

function AbaBase({ ano, mes, versao, mudou }: { ano: number; mes: number; versao: number; mudou: () => void }) {
  const [viagens, setViagens] = useState<ViagemBase[] | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoArquivo | null>(null)
  const [pendente, setPendente] = useState<{ arquivo: File; item: ResultadoArquivo } | null>(null)

  const carregar = useCallback(async () => {
    setCarregando(true)
    try {
      const { data: v } = await stradaApi.viagens({ ano, mes })
      setViagens(v); setErro(null)
    } catch (e) { setErro(await erroApi(e, 'Não consegui carregar as viagens.')) }
    finally { setCarregando(false) }
  }, [ano, mes])
  useEffect(() => { carregar() }, [carregar, versao])

  const subir = async (arquivo: File, modo?: Modo) => {
    setEnviando(true)
    try {
      const { data: r } = await stradaApi.importarBase(arquivo, modo)
      setResultado(r)
      setPendente(r.status === 'confirmar' ? { arquivo, item: r } : null)
      if (r.status !== 'confirmar' && r.status !== 'erro') {
        toast.success(`Base lida: ${r.novas} novas, ${r.alteradas} atualizadas, ${r.iguais} iguais.`)
        mudou()
      }
    } catch (e) {
      toast.error(await erroApi(e, 'Não consegui subir a planilha.'))
    } finally { setEnviando(false) }
  }

  return (
    <div className="space-y-5">
      <AreaSoltar aceita={['.xlsx']} multiplos={false} onArquivos={a => a[0] && subir(a[0])} carregando={enviando}
                  textoCarregando="Lendo a planilha..." titulo="Arraste a planilha AGROTRADE (.xlsx)"
                  dica="Abas VIAGENS e CONTRATOS TIP. Pode subir de novo sempre que anotar viagens novas: atualiza as que já estão (pela NF), acrescenta as novas e nunca apaga." />
      {resultado && resultado.status !== 'confirmar' && (
        <Aviso tipo={resultado.status === 'erro' ? 'erro' : 'sucesso'} onFechar={() => setResultado(null)}
               titulo={resultado.status === 'erro' ? 'A planilha não entrou' : `Base lida: ${resultado.novas} novas, ${resultado.alteradas} atualizadas, ${resultado.iguais} iguais`}
               itens={resultado.mensagem ? [resultado.mensagem, ...resultado.avisos] : resultado.avisos} />
      )}
      {pendente && (
        <ModalManterSubstituir itens={[pendente.item]} enviando={enviando} onClose={() => setPendente(null)}
                               escolher={modo => subir(pendente.arquivo, modo)} />
      )}

      {carregando && !viagens ? <Carregando linhas={5} rotulo="Carregando as viagens" />
        : erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !viagens || viagens.length === 0 ? (
          <Vazio icone={Truck} titulo="Nenhuma viagem neste mês">Suba a planilha AGROTRADE para trazer as viagens.</Vazio>
        ) : (
          <section aria-label="Viagens do mês" className="bg-white border border-gray-200 rounded-xl overflow-hidden">
            <h3 className="px-4 py-3 text-sm font-semibold text-gray-900 border-b border-gray-200">{viagens.length} {viagens.length === 1 ? 'viagem' : 'viagens'} em {MESES[mes - 1]}</h3>
            <ul className="divide-y divide-gray-100">
              {viagens.map(v => {
                const frete = v.contratos.find(c => c.tipo === 'frete')
                const reembolso = v.contratos.find(c => c.tipo === 'reembolso')
                return (
                  <li key={v.id} className="px-4 py-3 grid grid-cols-1 sm:grid-cols-[6rem_1fr_auto] gap-1 sm:gap-4 text-sm">
                    <span className="text-gray-600 tabular-nums">{data(v.data)}</span>
                    <span className="min-w-0">
                      <span className="font-medium text-gray-900">NF {v.nfOriginal ?? v.nf}</span> · {v.placa ?? 'sem placa'} · {v.nomeSocio} · {v.motorista ?? 'sem motorista'}
                      <span className="block text-gray-600">
                        {v.destino ?? 'sem destino'} · {v.contrato ? `contrato ${v.contrato}` : 'sem contrato'}
                        {frete ? ` · frete recebido${frete.dataTransferencia ? ` em ${data(frete.dataTransferencia)}` : ''}` : ''}
                        {v.difDescarga ? ` · descarga ${formatBRL(v.difDescarga)}${reembolso ? ' recebida' : ''}` : ''}
                        {v.valorDiaria ? ` · diária ${formatBRL(v.valorDiaria)}${reembolso ? ' recebida' : ''}` : ''}
                      </span>
                    </span>
                    <span className="tabular-nums sm:text-right">{dinheiro(v.freteEsperado)}</span>
                  </li>
                )
              })}
            </ul>
          </section>
        )}
    </div>
  )
}
