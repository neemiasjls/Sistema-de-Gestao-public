import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Download, FileText, Landmark } from 'lucide-react'
import { Segmentado } from '../ui/Abas'
import AreaSoltar from '../ui/AreaSoltar'
import Aviso from '../ui/Aviso'
import Botao, { BotaoIcone } from '../ui/Botao'
import { Carregando, ErroCarga, Vazio } from '../ui/Estados'
import ModalManterSubstituir from './ModalManterSubstituir'
import { formatBRL } from '../../services/api'
import {
  stradaApi, erroStrada, type Agrupar, type ConferenciaBanco, type FaturaResumo, type Modo, type ResultadoArquivo,
} from '../../services/stradaApi'
import { dataBr } from '../../utils/formato'

// Agrotrade / TIP Bank (PEN-39): cada fatura da Strada é uma transferência numerada, com planilha por sócio.
// A lista sai em grupos por dia: do vencimento ou do dia que caiu no banco.

const AGRUPAR = [['vencimento', 'Por vencimento'], ['caiu', 'Pelo dia que caiu']] as const
const DIAS = ['domingo', 'segunda', 'terça', 'quarta', 'quinta', 'sexta', 'sábado']
const MESES = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

/** "Sexta, 24 de outubro" a partir de "2026-10-24". */
function diaPorExtenso(iso: string) {
  const [a, m, d] = iso.slice(0, 10).split('-').map(Number)
  const dia = DIAS[new Date(a, m - 1, d).getDay()]
  return `${dia[0].toUpperCase()}${dia.slice(1)}, ${d} de ${MESES[m - 1]}`
}

const diaMes = (iso: string) => dataBr(iso).slice(0, 5)

type Grupo = { chave: string; titulo: string; faturas: FaturaResumo[]; repasse: number }

function agruparPorDia(faturas: FaturaResumo[], modo: Agrupar): Grupo[] {
  const grupos: Grupo[] = []
  for (const f of faturas) {
    const dia = modo === 'caiu' ? (f.caiuNoBanco ? f.dataCreditoBanco : null) : f.dataPagamento
    const chave = dia ? dia.slice(0, 10) : 'sem'
    const titulo = dia ? diaPorExtenso(dia)
      : modo === 'caiu' ? 'Ainda não caiu' : 'Sem data de vencimento'
    let g = grupos.find(x => x.chave === chave)
    if (!g) grupos.push(g = { chave, titulo, faturas: [], repasse: 0 })
    g.faturas.push(f)
    g.repasse += f.repasse
  }
  return grupos
}

export default function AbaTransferencias({ ano, mes, versao, mudou, abrir }: {
  ano: number; mes: number; versao: number; mudou: () => void; abrir: (n: string) => void
}) {
  const [modo, setModo] = useState<Agrupar>('vencimento')
  const [faturas, setFaturas] = useState<FaturaResumo[] | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setCarregando(true)
    try {
      const { data: f } = await stradaApi.faturas(ano, mes, modo)
      setFaturas(f); setErro(null)
    } catch (e) { setErro(await erroStrada(e, 'Não consegui carregar as transferências.')) }
    finally { setCarregando(false) }
  }, [ano, mes, modo])
  useEffect(() => { carregar() }, [carregar, versao])

  const [conferindo, setConferindo] = useState(false)
  const [banco, setBanco] = useState<ConferenciaBanco[] | null>(null)
  const conferirBanco = async () => {
    setConferindo(true)
    try {
      const { data: r } = await stradaApi.conferirBanco()
      setBanco(r)
      const caiu = r.filter(x => x.situacao === 'caiu').length
      toast.success(r.length === 0 ? 'Nenhuma fatura dos últimos 60 dias para conferir.' : `${caiu} de ${r.length} ${r.length === 1 ? 'fatura conferida' : 'faturas conferidas'} com crédito no banco.`)
      mudou()
    } catch (e) { toast.error(await erroStrada(e, 'Não consegui conferir com o Sicoob.')) }
    finally { setConferindo(false) }
  }

  const [baixando, setBaixando] = useState<string | null>(null)
  const baixar = async (numero: string) => {
    setBaixando(numero)
    try { await stradaApi.baixarPlanilha(numero) }
    catch (e) { toast.error(await erroStrada(e, `Não consegui baixar a planilha da fatura ${numero}.`)) }
    finally { setBaixando(null) }
  }
  const [baixandoTodas, setBaixandoTodas] = useState(false)
  const baixarTodas = async () => {
    setBaixandoTodas(true)
    try { await stradaApi.baixarPlanilhas(ano, mes) }
    catch (e) { toast.error(await erroStrada(e, 'Não consegui baixar as planilhas do mês.')) }
    finally { setBaixandoTodas(false) }
  }

  const grupos = faturas ? agruparPorDia(faturas, modo) : []

  return (
    <div className="space-y-5">
      <Envio mudou={mudou} />
      <div className="flex flex-col sm:flex-row sm:items-center gap-2">
        <p className="flex-1 text-sm text-gray-600">O repasse de cada fatura tem de cair na conta do Sicoob (Pix da Strada) até 5 dias depois do vencimento.</p>
        <Botao icone={Landmark} carregando={conferindo} textoCarregando="Lendo o banco..." onClick={conferirBanco}>Conferir com o banco</Botao>
      </div>
      {banco && banco.some(b => b.situacao !== 'caiu') && (
        <Aviso tipo="atencao" titulo="Faturas para conferir no banco" onFechar={() => setBanco(null)}
               itens={banco.filter(b => b.situacao !== 'caiu').map(b => `Fatura ${b.fatura}: ${b.mensagem ?? b.situacao}`)} />
      )}
      <div className="flex flex-col sm:flex-row sm:items-center gap-2">
        <div className="flex-1 min-w-0">
          <Segmentado<Agrupar> valor={modo} opcoes={AGRUPAR} onChange={setModo} rotulo="Agrupar as transferências" />
        </div>
        <Botao variante="primario" icone={Download} carregando={baixandoTodas} textoCarregando="Montando as planilhas..."
               disabled={!faturas || faturas.length === 0} onClick={baixarTodas}>Baixar todas do mês</Botao>
      </div>
      {carregando && !faturas ? <Carregando linhas={4} rotulo="Carregando as transferências" />
        : erro ? <ErroCarga mensagem={erro} onTentar={carregar} />
        : !faturas || faturas.length === 0 ? (
          <Vazio icone={FileText} titulo="Nenhuma transferência neste mês">Suba os arquivos de repasse da Strada acima; a fatura entra no mês do vencimento.</Vazio>
        ) : (
          <div className="space-y-5">
            {grupos.map(g => (
              <section key={g.chave} aria-label={g.titulo} className="space-y-2">
                <h3 className="flex flex-wrap items-baseline justify-between gap-x-3 text-sm font-semibold text-gray-900">
                  <span>{g.titulo}</span>
                  <span className="font-normal text-gray-600 tabular-nums">Repasse {formatBRL(g.repasse)}</span>
                </h3>
                <ul className="space-y-2">
                  {g.faturas.map(f => (
                    <li key={f.id}>
                      <CartaoTransferencia f={f} abrir={abrir} baixar={baixar} baixando={baixando === f.numero} />
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
        )}
    </div>
  )
}

function situacaoBanco(f: FaturaResumo): { texto: string; cls: string } {
  if (f.caiuNoBanco) return { texto: f.dataCreditoBanco ? `Caiu em ${diaMes(f.dataCreditoBanco)}` : 'Caiu no banco, sem a data do crédito', cls: 'text-gray-700' }
  if (f.conferidaSemCredito) return { texto: 'Não caiu: confira', cls: 'text-amber-800' }
  return { texto: 'Ainda não caiu', cls: 'text-gray-600' }
}

function CartaoTransferencia({ f, abrir, baixar, baixando }: {
  f: FaturaResumo; abrir: (n: string) => void; baixar: (n: string) => void; baixando: boolean
}) {
  const banco = situacaoBanco(f)
  return (
    <div className="flex items-start gap-2 bg-white border border-gray-200 rounded-xl p-3 sm:p-4 hover:border-gray-400 transition-colors">
      <button type="button" onClick={() => abrir(f.numero)} className="flex-1 min-w-0 text-left">
        <p className="font-semibold text-gray-900">
          {f.numeroTransferencia != null ? `Transferência ${f.numeroTransferencia}` : 'Transferência sem número'}
          <span className="font-normal text-gray-600"> · fatura {f.numero}</span>
        </p>
        <p className="text-sm text-gray-600">Vencimento {f.dataPagamento ? dataBr(f.dataPagamento) : 'sem data'}</p>
        {f.porSocio.length > 0 ? (
          <ul className="mt-2 grid grid-cols-1 sm:grid-cols-3 gap-x-4 gap-y-0.5 text-sm" aria-label="Transferido por sócio">
            {f.porSocio.map(s => (
              <li key={s.socio ?? s.nome} className="flex justify-between sm:block gap-2">
                <span className="text-gray-600">{s.nome}</span> <span className="tabular-nums text-gray-900">{formatBRL(s.repasse)}</span>
              </li>
            ))}
          </ul>
        ) : <p className="mt-2 text-sm text-amber-800">Sem contratos no arquivo: falta a divisão por sócio.</p>}
        <p className="mt-2 text-sm text-gray-700 tabular-nums">
          Total {formatBRL(f.total)} · taxa {formatBRL(f.taxa)} · <span className="font-semibold text-gray-900">repasse {formatBRL(f.repasse)}</span>
        </p>
        <p className={`mt-1 text-sm ${banco.cls}`}>{banco.texto}</p>
        {(f.incompleta || f.pendencias > 0) && (
          <p className="mt-1 text-sm text-amber-800">
            {f.incompleta ? 'Fatura incompleta: falta o "Exportar detalhamento". ' : ''}
            {f.pendencias > 0 ? `${f.pendencias} ${f.pendencias === 1 ? 'contrato para conferir' : 'contratos para conferir'}.` : ''}
          </p>
        )}
      </button>
      <BotaoIcone icone={Download} rotulo={`Baixar a planilha da transferência${f.numeroTransferencia != null ? ` ${f.numeroTransferencia}` : ''} (fatura ${f.numero})`}
                  onClick={() => baixar(f.numero)} disabled={baixando} aria-busy={baixando}
                  className="!bg-tinta-700 !text-white hover:!bg-tinta-800 shadow-sm" />
    </div>
  )
}

// Sobe .txt e .xlsx (faturas) e .pdf (contratos) juntos; arquivo que já existe e mudou pede Manter ou Substituir (UI-21).
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
      toast.error(await erroStrada(e, 'Não consegui subir os arquivos.'))
    } finally { setEnviando(false) }
  }

  return (
    <section aria-label="Subir arquivos da Strada" className="space-y-3">
      <AreaSoltar aceita={['.txt', '.xlsx', '.pdf']} onArquivos={a => subir(a)} carregando={enviando} textoCarregando="Lendo os arquivos..."
                  titulo="Arraste os arquivos da Strada"
                  dica='Arquivo de repasse de cada fatura (.txt), o "Exportar detalhamento" (.xlsx) da fatura que veio só com o total e o PDF "Baixar contrato" dos reembolsos. Pode subir vários de uma vez.' />
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
