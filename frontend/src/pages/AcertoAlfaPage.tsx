import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { useCallback, useEffect, useRef, useState } from 'react'
import {
  CheckCircle2, ChevronRight, CircleDashed, Download, FileQuestion, FileSpreadsheet, FileText, FolderOpen, LoaderCircle,
  Receipt, TriangleAlert, X,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  acertoAlfaApi, erroAcerto, segundaDaSemana, ROTULO_CANCELADO, ROTULO_SITUACAO,
  type AcertoResumo, type ArquivoIdentificado, type ResultadoImportacao,
} from '../services/acertoAlfaApi'
import { brl, dataBr } from '../utils/formato'
import type { Conferencia } from '../components/ConferenciaSemana'
import AcertoAlfaDetalhe, { JanelaSubstituir } from '../components/AcertoAlfaDetalhe'
import { SituacaoRomaneio } from '../components/RomaneiosEmbalar'

// O acerto com a Fazenda Alfa vai sendo montado ao longo da semana: cada PDF
// que chega (boleto ou romaneio da fazenda) entra no acerto da semana dele, que
// nasce sozinho com o próximo número. A planilha fica sempre pronta para baixar.
//
// Hierarquia da tela (Fase 3): primeiro o acerto da semana em aberto (o que
// importa hoje: quanto, o que falta), logo embaixo a área de soltar os PDFs, e
// por último os acertos guardados. Importar planilhas antigas é coisa de uma vez
// só, então fica discreto no cabeçalho da lista.

// Crédito com Livia em verde, débito em vermelho
const corSaldo = (v: number) => (v > 0.004 ? 'text-emerald-700' : v < -0.004 ? 'text-red-700' : 'text-gray-900')
const nomeSaldo = (v: number) => (v > 0.004 ? 'crédito' : v < -0.004 ? 'débito' : 'zerado')

const diaSemana = (iso: string) => {
  const d = new Date(iso.slice(0, 10) + 'T12:00:00')
  const nome = d.toLocaleDateString('pt-BR', { weekday: 'long' }).replace('-feira', '')
  return `${nome} ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })}`
}
const nomeCliente = (a: string) =>
  ({ AURORA: 'Aurora', BRZ: 'BRZ', CD: 'Cedro Frutas', 'CEDRO FRUTAS': 'Cedro Frutas', DUNAS: 'Dunas', LITORAL: 'Litoral', FAROL: 'Litoral (Farol)' }[a] ?? a)

const qtd = (n: number, um: string, varios: string) => `${n.toLocaleString('pt-BR')} ${n === 1 ? um : varios}`

// O que a conferência da semana diz que ainda não chegou ou não bateu
function pendencias(c: Conferencia): { texto: string; grave: boolean }[] {
  const p: { texto: string; grave: boolean }[] = []
  c.dias.filter(d => d.situacao === 'sem_fazenda').forEach(d =>
    p.push({ texto: `Romaneio da fazenda de ${diaSemana(d.data)}`, grave: false }))
  c.boletos.filter(b => b.situacao === 'falta_boleto').forEach(b =>
    p.push({ texto: `Boleto de ${nomeCliente(b.cliente)} de ${diaSemana(b.data)}`, grave: false }))
  c.dias.filter(d => d.situacao === 'diferenca').forEach(d =>
    p.push({ texto: `Funrural de ${diaSemana(d.data)}: fazenda ${brl(d.funruralFazenda ?? 0)}, clientes ${brl(d.funruralClientes)}`, grave: true }))
  c.boletos.filter(b => b.situacao === 'diferenca').forEach(b =>
    p.push({ texto: `Boleto de ${nomeCliente(b.cliente)} de ${diaSemana(b.data)}: ${brl(b.noBoleto ?? 0)} no boleto, ${brl(b.noRomaneio ?? 0)} no romaneio`, grave: true }))
  c.totais?.filter(t => t.situacao === 'diferenca').forEach(t =>
    p.push({ texto: `${nomeCliente(t.cliente)} de ${diaSemana(t.data)}: boletos somam ${brl(t.somaBoletos)} e o romaneio dá ${brl(t.totalRomaneio ?? 0)} (diferença ${brl(t.diferenca)})`, grave: true }))
  c.totais?.filter(t => t.situacao === 'sem_total').forEach(t =>
    p.push({ texto: `${nomeCliente(t.cliente)} de ${diaSemana(t.data)}: não achei o valor total no romaneio, a soma dos boletos não foi conferida`, grave: true }))
  return p
}

export default function AcertoAlfaPage() {
  const [guardados, setGuardados] = useState<AcertoResumo[] | null>(null)
  const [aberto, setAberto] = useState<number | null>(null)
  const [proximo, setProximo] = useState<number | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [importando, setImportando] = useState(false)
  const [importados, setImportados] = useState<ResultadoImportacao[] | null>(null)
  const [identificados, setIdentificados] = useState<ArquivoIdentificado[] | null>(null)
  // PDFs com documento repetido e diferente, esperando Cancelar ou Substituir
  const [pendentes, setPendentes] = useState<{ arquivos: File[]; itens: ArquivoIdentificado[] } | null>(null)
  // Avisos do envio que a lista por arquivo não mostra: ficam até fechar (D-07)
  const [avisos, setAvisos] = useState<string[]>([])
  const [falhou, setFalhou] = useState(false)
  const entradaPlanilhas = useRef<HTMLInputElement>(null)
  const confirmar = useConfirmar()

  const semanaAtual = segundaDaSemana()
  // A lista vem da semana mais nova para a mais velha e, na mesma semana, do maior número
  const daSemana = guardados?.find(a => a.semana === semanaAtual)

  const carregar = useCallback(async () => {
    setFalhou(false)
    try { setGuardados((await acertoAlfaApi.listar()).data) }
    catch { setFalhou(true) }
    acertoAlfaApi.proximoNumero().then(r => setProximo(r.data.proximo)).catch(() => {})
  }, [])

  useEffect(() => { carregar() }, [carregar])

  const abrir = (id: number) => {
    setAberto(id)
    // A página rola dentro do <main>, não da janela
    setTimeout(() => document.querySelector('main')?.scrollTo({ top: 0, behavior: 'smooth' }), 0)
  }

  // PDFs vão para o acerto da semana deles; planilhas de acerto, para a importação.
  // O que não é nem um nem outro a AreaSoltar já mostra como "Ficou de fora".
  const soltar = async (files: File[]) => {
    const pdfs = files.filter(f => f.name.toLowerCase().endsWith('.pdf'))
    const planilhas = files.filter(f => f.name.toLowerCase().endsWith('.xlsx'))
    if (pdfs.length) await enviarPdfs(pdfs)
    if (planilhas.length) await importar(planilhas)
  }

  // Documento que já está num acerto e veio diferente espera Cancelar ou Substituir
  // (ACE-11, UI-21); "Substituir" reenvia só esses arquivos.
  const enviarPdfs = async (pdfs: File[], substituir = false) => {
    setEnviando(true)
    if (!substituir) { setIdentificados(null); setAvisos([]) }
    try {
      const { data } = await acertoAlfaApi.enviarDaSemana(pdfs, substituir)
      const itens = data.arquivos ?? []
      // Reenvio: troca só a linha dos arquivos reenviados
      setIdentificados(antes => substituir && antes
        ? [...antes.filter(a => !itens.some(i => i.nome === a.nome)), ...itens]
        : itens)
      const esperando = itens.filter(a => a.situacao === 'CONFIRMAR')
      setPendentes(esperando.length
        ? { arquivos: pdfs.filter(f => esperando.some(c => c.nome === f.name)), itens: esperando }
        : null)
      const guardados = itens.filter(a => a.destino).length
      const iguais = itens.filter(a => a.situacao === 'IGUAL').length
      if (substituir) toast.success(`${guardados} ${guardados === 1 ? 'arquivo substituído' : 'arquivos substituídos'}.`)
      else toast.success(`${guardados} de ${pdfs.length} arquivo(s) guardado(s)`
        + (iguais ? `, ${iguais} já ${iguais === 1 ? 'estava, igual' : 'estavam, iguais'}` : '')
        + (esperando.length ? '; escolha na janela o que fazer com o repetido' : guardados + iguais < itens.length ? '; confira a lista' : '')
        + '.', { duration: 6000 })
      // O que a lista por arquivo não mostra: boleto ou romaneio que o acerto já tinha
      const doAcerto = data.acertos.flatMap(a => a.avisos)
        .filter(x => !/já estavam guardados/.test(x) && !itens.some(i => i.avisos.includes(x)))
      setAvisos(antes => [...(substituir ? antes : []), ...doAcerto])
      await carregar()
    } catch (e) {
      const dados = (e as { response?: { data?: { arquivos?: ArquivoIdentificado[] } } })?.response?.data
      toast.error(await erroAcerto(e, 'Não consegui guardar os PDFs.'))
      if (dados?.arquivos?.length) setIdentificados(dados.arquivos)
    } finally {
      setEnviando(false)
    }
  }

  const importar = async (files: File[]) => {
    const planilhas = files.filter(f => f.name.toLowerCase().endsWith('.xlsx') && !f.name.startsWith('~$'))
    if (!planilhas.length) { toast.error('Selecione as planilhas de acerto (.xlsx).'); return }
    if (!await confirmar({
      titulo: `Importar ${planilhas.length} planilha(s) de acerto?`,
      texto: 'Se já existir acerto com o mesmo número, os dados dele são trocados pelos da planilha.',
      confirmar: 'Importar', perigo: false,
    })) return
    setImportando(true)
    try {
      const { data } = await acertoAlfaApi.importarPlanilhas(planilhas)
      setImportados(data)
      const certos = data.filter(r => !r.erro).length
      if (certos) toast.success(`${certos} acerto(s) importado(s).`)
      if (data.some(r => r.erro)) toast.error('Alguma planilha não entrou. Veja a lista.')
      await carregar()
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui importar as planilhas.')) }
    finally { setImportando(false) }
  }

  if (aberto != null) {
    return (
      <div className="space-y-6 max-w-[1600px]">
        <AcertoAlfaDetalhe id={aberto} onVoltar={() => { setAberto(null); carregar() }} onMudou={carregar} />
      </div>
    )
  }

  return (
    <div className="space-y-6 max-w-4xl">
      <CabecalhoTela descricao={<>
        Vá soltando os <b>PDFs dos boletos</b> e dos <b>romaneios da fazenda</b> conforme chegam, um ou vários de uma vez.
        O sistema vê o que é cada um: o da fazenda entra no acerto da semana dele, criado sozinho com o próximo número;
        o <b>boleto da CD ou da Girassol</b> que você emitiu vai para Boletos a receber. O que já foi lançado ou corrigido
        na tela não é mexido. A planilha fica pronta para baixar a qualquer hora.
      </>} />

      {guardados == null && !falhou
        ? <Carregando forma="cartoes" linhas={4} rotulo="Carregando o acerto da semana" />
        : <AcertoDaSemana resumo={daSemana} semana={semanaAtual} proximo={proximo} onAbrir={abrir} />}

      <AreaSoltar
        aceita={['.pdf', '.xlsx']}
        descricaoTipos="PDF (boletos e romaneios) ou planilha de acerto"
        onArquivos={soltar}
        icone={Receipt}
        titulo="Arraste os boletos e romaneios da fazenda aqui"
        dica="Pode misturar boletos da fazenda, romaneios e boletos da CD e da Girassol"
        textoBotao="Escolher PDFs"
        carregando={enviando || importando}
        textoCarregando={importando ? 'Importando as planilhas...' : 'Lendo e guardando os PDFs...'}
      />

      {avisos.length > 0 && (
        <Aviso tipo="atencao" titulo="Confira no acerto" itens={avisos} onFechar={() => setAvisos([])} />
      )}

      {identificados && identificados.length > 0 && (
        <ArquivosIdentificados itens={identificados} esperando={pendentes?.itens.map(i => i.nome) ?? []}
                               onAbrir={abrir} onFechar={() => setIdentificados(null)} />
      )}

      {pendentes && (
        <JanelaSubstituir itens={pendentes.itens.map(i => ({ arquivo: i.nome, diferencas: i.diferencas ?? [] }))}
                          enviando={enviando} onCancelar={() => setPendentes(null)}
                          onSubstituir={() => enviarPdfs(pendentes.arquivos, true)} />
      )}

      {importados && <ResultadoImportacaoLista itens={importados} onAbrir={abrir} onFechar={() => setImportados(null)} />}

      {/* Acertos guardados */}
      <section aria-labelledby="acertos-guardados" className="space-y-3">
        <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
          <div className="min-w-0">
            <h2 id="acertos-guardados" className="text-lg font-semibold text-gray-900">Acertos guardados</h2>
            <p className="text-sm text-gray-600">Abra um acerto para ver e corrigir a planilha, baixar e conferir a semana.</p>
          </div>
          <Botao variante="secundario" tamanho="sm" icone={FileSpreadsheet} carregando={importando} textoCarregando="Importando..."
                 onClick={() => entradaPlanilhas.current?.click()} className="shrink-0"
                 title="As planilhas “194-ACERTO G. ALFA-21-09-2026.xlsx” do Excel: número e semana saem do nome">
            Importar planilhas do Excel
          </Botao>
          <input ref={entradaPlanilhas} type="file" accept=".xlsx" multiple hidden tabIndex={-1}
                 onChange={e => { importar(Array.from(e.target.files ?? [])); e.target.value = '' }} />
        </div>
        {falhou && guardados == null ? (
          <ErroCarga mensagem="Não consegui carregar os acertos guardados." onTentar={carregar} />
        ) : guardados == null ? (
          <Carregando linhas={4} rotulo="Carregando os acertos" />
        ) : guardados.length === 0 ? (
          <Vazio icone={FolderOpen} titulo="Nenhum acerto guardado ainda">
            Solte os PDFs da semana acima ou importe as planilhas feitas no Excel.
          </Vazio>
        ) : (
          <div className="bg-white rounded-xl border border-gray-200 overflow-hidden">
            {/* Computador: colunas com o nome em cima, os valores alinhados à direita */}
            <div aria-hidden className="hidden sm:grid grid-cols-[minmax(9rem,1.1fr)_1fr_1fr_1fr_5.5rem] gap-4 px-4 py-2 bg-gray-50 border-b border-gray-200 text-xs font-medium text-gray-500">
              <span>Acerto</span>
              <span className="text-right">Romaneios a pagar</span>
              <span className="text-right">Depósitos e pagamentos</span>
              <span className="text-right">Créditos e débitos</span>
              <span />
            </div>
            <ul className="divide-y divide-gray-100">
              {guardados.map(a => (
                <li key={a.id}>
                  <button onClick={() => abrir(a.id)}
                          className="group w-full text-left px-4 py-3 grid grid-cols-[1fr_auto] sm:grid-cols-[minmax(9rem,1.1fr)_1fr_1fr_1fr_5.5rem] gap-x-4 gap-y-1 items-center hover:bg-gray-50 transition-colors">
                    <span className="min-w-0">
                      <span className="block font-display text-lg font-semibold text-gray-900 leading-tight">
                        Acerto {a.numero}
                        {a.semana === segundaDaSemana() && <span className="ml-2 align-middle font-sans text-xs font-medium text-tinta-800 bg-tinta-50 border border-tinta-200 rounded-md px-1.5 py-0.5">esta semana</span>}
                      </span>
                      <span className="block text-sm text-gray-500 tabular-nums">Semana de {dataBr(a.semana)}</span>
                    </span>

                    {/* Celular: o saldo à direita do nome; o resto numa linha embaixo */}
                    <span className={`sm:hidden text-right font-display text-lg font-semibold tabular-nums leading-tight ${corSaldo(a.saldo)}`}>
                      {formatBRL(a.saldo)}
                      <span className="block font-sans text-xs font-medium text-gray-500">{nomeSaldo(a.saldo)}</span>
                    </span>
                    <span className="sm:hidden col-span-2 text-sm text-gray-600 tabular-nums flex flex-wrap gap-x-3">
                      <span>A pagar {formatBRL(a.totalAPagar)}</span>
                      <span>Pago {formatBRL(a.totalPagamentos)}</span>
                    </span>

                    <span className="hidden sm:block text-right tabular-nums text-gray-800">
                      {formatBRL(a.totalAPagar)}
                      <span className="block text-xs text-gray-500">{qtd(a.qtdRomaneios, 'romaneio', 'romaneios')}</span>
                    </span>
                    <span className="hidden sm:block text-right tabular-nums text-gray-800">{formatBRL(a.totalPagamentos)}</span>
                    <span className={`hidden sm:block text-right tabular-nums font-semibold ${corSaldo(a.saldo)}`}>
                      {formatBRL(a.saldo)}
                      <span className="block text-xs font-medium text-gray-500">{nomeSaldo(a.saldo)}</span>
                    </span>
                    <span className="hidden sm:flex items-center justify-end gap-1 text-sm text-gray-500 tabular-nums">
                      <FileText size={14} aria-hidden /> {a.qtdArquivos}
                      <span className="sr-only">{a.qtdArquivos === 1 ? 'arquivo' : 'arquivos'}</span>
                      <ChevronRight size={18} className="text-gray-400 group-hover:text-gray-700 group-hover:translate-x-0.5 transition-transform" aria-hidden />
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        )}
      </section>
    </div>
  )
}

// O acerto da semana em andamento, em destaque: quanto está, o que já chegou, o
// que falta e a planilha
function AcertoDaSemana({ resumo, semana, proximo, onAbrir }: {
  resumo: AcertoResumo | undefined
  semana: string
  proximo: number | null
  onAbrir: (id: number) => void
}) {
  const [conferencia, setConferencia] = useState<Conferencia | null>(null)
  const [conferindo, setConferindo] = useState(false)
  const [falhouConferir, setFalhouConferir] = useState(false)
  const [baixando, setBaixando] = useState(false)
  const [avisosPlanilha, setAvisosPlanilha] = useState<string[]>([])
  const id = resumo?.id
  const versao = resumo?.atualizadoEm

  // Refaz a conferência quando o acerto muda (chegou PDF, alguém salvou)
  useEffect(() => {
    setConferencia(null)
    setFalhouConferir(false)
    if (id == null) return
    let cancelado = false
    setConferindo(true)
    acertoAlfaApi.conferencia(id)
      .then(r => { if (!cancelado) setConferencia(r.data) })
      .catch(() => { if (!cancelado) setFalhouConferir(true) })
      .finally(() => { if (!cancelado) setConferindo(false) })
    return () => { cancelado = true }
  }, [id, versao])

  if (!resumo) {
    return (
      <section aria-label="Acerto da semana" className="rounded-xl border border-dashed border-tinta-300 bg-tinta-50/60 px-4 py-4 flex gap-3">
        <CircleDashed size={22} strokeWidth={1.75} className="text-tinta-600 shrink-0 mt-0.5" aria-hidden />
        <div>
          <p className="font-semibold text-gray-900">Semana de {dataBr(semana)}: nada chegou ainda</p>
          <p className="text-sm text-gray-600 mt-0.5">
            O primeiro PDF solto aqui embaixo cria o acerto{proximo ? <> número <b className="tabular-nums">{proximo}</b></> : null}.
          </p>
        </div>
      </section>
    )
  }

  const baixar = async () => {
    setBaixando(true)
    try {
      const avisos = await acertoAlfaApi.baixarPlanilha(resumo.id, `${resumo.numero}-ACERTO G. ALFA.xlsx`)
      setAvisosPlanilha(avisos ? avisos.split(' | ') : [])
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui gerar a planilha.')) }
    finally { setBaixando(false) }
  }

  const falta = conferencia ? pendencias(conferencia) : []
  const naoBate = falta.filter(f => f.grave)

  return (
    <section aria-labelledby="acerto-semana" className="bg-white rounded-xl border border-gray-200 shadow-sm overflow-hidden">
      {/* Uma faixa de gema no alto: é o acerto vivo, o que se mexe esta semana */}
      <div className="h-1 bg-gema-400" aria-hidden />
      <div className="p-4 sm:p-5 space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-start gap-3">
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-gray-600">Acerto da semana em aberto</p>
            <h2 id="acerto-semana" className="text-2xl font-semibold text-gray-900 leading-tight">
              Acerto {resumo.numero}
              <span className="ml-2 font-sans text-base font-normal text-gray-500 tabular-nums">semana de {dataBr(resumo.semana)}</span>
            </h2>
          </div>
          <div className="flex gap-2 [&>*]:flex-1 sm:[&>*]:flex-none">
            <Botao variante="secundario" onClick={() => onAbrir(resumo.id)}>
              Abrir acerto <ChevronRight size={18} aria-hidden />
            </Botao>
            <Botao variante="primario" icone={Download} onClick={baixar} carregando={baixando} textoCarregando="Gerando...">
              Baixar planilha
            </Botao>
          </div>
        </div>

        {/* Fica na tela enquanto houver diferença na conferência: o que não está batendo */}
        {naoBate.length > 0 && (
          <Aviso tipo="erro" titulo={naoBate.length === 1 ? 'A conferência achou uma diferença' : `A conferência achou ${naoBate.length} diferenças`}
                 itens={naoBate.map(f => f.texto)} />
        )}

        {/* Os números da semana numa faixa só, o saldo por último e maior */}
        <dl className="grid grid-cols-2 lg:grid-cols-4 gap-px bg-gray-200 rounded-lg border border-gray-200 overflow-hidden
                       [&>div]:bg-white [&>div]:px-3.5 [&>div]:py-3 [&>div]:min-w-0">
          <div>
            <dt className="text-xs font-medium text-gray-500">Boletos</dt>
            <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums leading-tight">{resumo.qtdBoletos}</dd>
            {resumo.qtdBoletosSemValor > 0 && (
              <dd className="text-xs font-medium text-amber-800">{qtd(resumo.qtdBoletosSemValor, 'linha esperando valor', 'linhas esperando valor')}</dd>
            )}
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">Romaneios da fazenda</dt>
            <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums leading-tight">{resumo.qtdRomaneios}</dd>
            <dd className="text-xs text-gray-500 tabular-nums">{qtd(resumo.totalCaixas, 'caixa', 'caixas')}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">Romaneios a pagar</dt>
            <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums leading-tight">{formatBRL(resumo.totalAPagar)}</dd>
          </div>
          <div className="!bg-gray-50">
            <dt className="text-xs font-medium text-gray-500">Créditos e débitos com Livia</dt>
            <dd className={`font-display text-xl font-semibold tabular-nums leading-tight ${corSaldo(resumo.saldo)}`}>{formatBRL(resumo.saldo)}</dd>
            <dd className="text-xs text-gray-500">{nomeSaldo(resumo.saldo)}</dd>
          </div>
        </dl>

        <div>
          <h3 className="text-sm font-semibold text-gray-800 mb-1.5">Pela conferência da semana</h3>
          {conferindo && !conferencia ? (
            <p role="status" className="text-sm text-gray-600 flex items-center gap-2">
              <LoaderCircle size={15} className="animate-spin" aria-hidden /> Conferindo com os romaneios salvos...
            </p>
          ) : falhouConferir ? (
            <p className="text-sm text-gray-600">Não consegui fazer a conferência agora. Abra o acerto para conferir de novo.</p>
          ) : !conferencia || (conferencia.dias.length === 0 && conferencia.boletos.length === 0) ? (
            <p className="text-sm text-gray-600">Nada para conferir ainda: a conferência usa os PDFs guardados e os romaneios dos clientes da semana.</p>
          ) : falta.length === 0 ? (
            <p className="text-sm font-medium text-emerald-800 flex items-center gap-1.5">
              <CheckCircle2 size={16} aria-hidden /> Nada faltando: o que chegou bate com os romaneios dos clientes.
            </p>
          ) : (
            <ul className="space-y-1">
              {falta.map(f => (
                <li key={f.texto} className={`text-sm flex items-start gap-2 ${f.grave ? 'text-red-800 font-medium' : 'text-gray-800'}`}>
                  {f.grave
                    ? <TriangleAlert size={15} className="mt-0.5 shrink-0 text-red-700" aria-hidden />
                    : <CircleDashed size={15} className="mt-0.5 shrink-0 text-gema-700" aria-hidden />}
                  {f.grave ? f.texto : <span><span className="text-gray-500">Falta:</span> {f.texto}</span>}
                </li>
              ))}
            </ul>
          )}
        </div>

        {avisosPlanilha.length > 0 && (
          <Aviso tipo="atencao" titulo="Planilha baixada. Confira" itens={avisosPlanilha} onFechar={() => setAvisosPlanilha([])} />
        )}
      </div>
    </section>
  )
}

// O que cada planilha importada virou: número, semana e os totais lidos contra os gravados pelo Excel
function ResultadoImportacaoLista({ itens, onAbrir, onFechar }: {
  itens: ResultadoImportacao[]
  onAbrir: (id: number) => void
  onFechar: () => void
}) {
  const bate = (a: number | null | undefined, b: number | null | undefined) => a == null || b == null || Math.abs(a - b) < 0.005
  return (
    <section className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-entrada">
      <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
        <h3 className="font-semibold text-gray-900">Planilhas importadas</h3>
        <BotaoIcone icone={X} rotulo="Fechar a lista" onClick={onFechar} />
      </div>
      <ul className="divide-y divide-gray-100">
        {itens.map(r => {
          const confere = r.totais && r.gravados && bate(r.totais.aPagar, r.gravados.aPagar)
            && bate(r.totais.pagamentos, r.gravados.pagamentos) && bate(r.totais.saldo, r.gravados.saldo)
          return (
            <li key={r.arquivo} className="px-4 py-3 flex items-start gap-3">
              {r.erro
                ? <FileQuestion size={17} className="mt-0.5 shrink-0 text-red-700" aria-hidden />
                : confere
                  ? <CheckCircle2 size={17} className="mt-0.5 shrink-0 text-emerald-700" aria-hidden />
                  : <TriangleAlert size={17} className="mt-0.5 shrink-0 text-amber-700" aria-hidden />}
              <div className="flex-1 min-w-0 space-y-0.5">
                <p className="text-sm font-medium text-gray-900 break-all">{r.arquivo}</p>
                {r.erro ? (
                  <p className="text-sm text-red-800">{r.erro}</p>
                ) : (
                  <>
                    <p className="text-sm text-gray-700 tabular-nums">
                      Acerto {r.numero}, semana de {dataBr(r.semana ?? '')} {r.criado ? '(novo)' : '(atualizado)'}
                      {r.totais && <> · <span className={`font-semibold ${corSaldo(r.totais.saldo)}`}>{formatBRL(r.totais.saldo)}</span></>}
                    </p>
                    <p className={`text-sm ${confere ? 'text-emerald-800' : 'text-red-800 font-medium'}`}>
                      {confere ? 'Os totais batem com os da planilha.' : 'Totais diferentes dos da planilha.'}
                    </p>
                  </>
                )}
                {r.avisos.map(a => <p key={a} className="text-sm text-amber-800">{a}</p>)}
              </div>
              {r.acertoId != null && (
                <Botao variante="fantasma" tamanho="sm" onClick={() => onAbrir(r.acertoId!)} className="shrink-0">Abrir</Botao>
              )}
            </li>
          )
        })}
      </ul>
    </section>
  )
}

// O que o último envio reconheceu em cada arquivo e onde guardou (UI-04: o que
// não foi reconhecido aparece em vermelho, em vez de sumir)
const nomeTipo: Record<ArquivoIdentificado['tipo'], string> = {
  BOLETO_FAZENDA: 'Boleto da fazenda',
  ROMANEIO_FAZENDA: 'Romaneio da fazenda',
  BOLETO_RECEBER: 'Boleto a receber',
  NAO_RECONHECIDO: 'Não reconhecido',
  ILEGIVEL: 'PDF ilegível',
  FORA: 'Não é PDF',
}

function detalhes(a: ArquivoIdentificado): string[] {
  const d: string[] = []
  if (a.tipo === 'BOLETO_FAZENDA') {
    d.push(`pagador ${a.cliente ? nomeCliente(a.cliente) : 'não lido'}`)
    d.push(a.numero ? `número ${a.numero}` : 'sem número')
  } else if (a.tipo === 'BOLETO_RECEBER') {
    if (a.cliente) d.push(a.cliente)
    if (a.numero) d.push(`nosso número ${a.numero}`)
    d.push(a.placa ? `caminhão ${a.placa}` : 'sem placa')
  } else if (a.tipo === 'ROMANEIO_FAZENDA') {
    d.push(a.numero ? `romaneio ${a.numero}` : 'sem número')
    if (a.caixas != null) d.push(`${a.caixas.toLocaleString('pt-BR')} caixas`)
  }
  if (a.valor != null) d.push(formatBRL(a.valor))
  if (a.tipo === 'BOLETO_RECEBER' && a.data) d.push(`carga de ${dataBr(a.data)}`)
  else if (a.data) d.push(`de ${dataBr(a.data)}`)
  if (a.vencimento) d.push(`vence ${dataBr(a.vencimento)}`)
  return d
}

function ArquivosIdentificados({ itens, esperando, onAbrir, onFechar }: {
  itens: ArquivoIdentificado[]
  esperando: string[]
  onAbrir: (id: number) => void
  onFechar: () => void
}) {
  const reconhecido = (a: ArquivoIdentificado) => ['BOLETO_FAZENDA', 'ROMANEIO_FAZENDA', 'BOLETO_RECEBER'].includes(a.tipo)
  const semReconhecer = itens.filter(a => !reconhecido(a)).length
  return (
    <section className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-entrada">
      <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
        <div className="min-w-0 py-1">
          <h3 className="font-semibold text-gray-900">O que foi reconhecido</h3>
          <p className={`text-sm ${semReconhecer ? 'text-red-800 font-medium' : 'text-gray-500'}`}>
            {qtd(itens.length, 'arquivo', 'arquivos')}{semReconhecer ? `, ${semReconhecer} sem reconhecer` : ', todos reconhecidos'}
          </p>
        </div>
        <BotaoIcone icone={X} rotulo="Fechar a lista" onClick={onFechar} />
      </div>
      <ul className="divide-y divide-gray-100">
        {itens.map(a => {
          const ok = reconhecido(a)
          const info = detalhes(a)
          return (
            <li key={a.nome} className={`px-4 py-3 flex items-start gap-3 ${ok ? '' : 'bg-red-50/60'}`}>
              {!ok
                ? <FileQuestion size={17} className="mt-0.5 shrink-0 text-red-700" aria-hidden />
                : a.situacao === 'CONFIRMAR'
                  ? <TriangleAlert size={17} className="mt-0.5 shrink-0 text-amber-700" aria-hidden />
                  : <CheckCircle2 size={17} className="mt-0.5 shrink-0 text-emerald-700" aria-hidden />}
              <div className="min-w-0 flex-1 space-y-0.5">
                <p className="text-sm font-medium text-gray-900 break-all">{a.nome}</p>
                <p className="text-sm tabular-nums">
                  <span className={ok ? 'font-medium text-gray-800' : 'text-red-800 font-semibold'}>{nomeTipo[a.tipo]}</span>
                  {info.length > 0 && <span className="text-gray-600">: {info.join(' · ')}</span>}
                </p>
                <p className="text-sm text-gray-600">
                  {a.situacao === 'CONFIRMAR'
                    ? <span className="font-medium text-amber-800">{esperando.includes(a.nome) ? ROTULO_SITUACAO.CONFIRMAR : ROTULO_CANCELADO}</span>
                    : a.situacao === 'IGUAL' && !a.destino
                      ? <>{ROTULO_SITUACAO.IGUAL}{a.acertoId != null && <>{' · '}
                          <button onClick={() => onAbrir(a.acertoId!)} className="font-medium text-tinta-700 underline underline-offset-2 hover:text-tinta-900 py-2 -my-2 sm:py-0 sm:my-0">Abrir o acerto</button></>}</>
                      : a.destino
                        ? <>{a.situacao === 'SUBSTITUIDO' ? 'Substituído em' : 'Guardado em'} {a.acertoId != null
                            ? <button onClick={() => onAbrir(a.acertoId!)} className="font-medium text-tinta-700 underline underline-offset-2 hover:text-tinta-900 py-2 -my-2 sm:py-0 sm:my-0">{a.destino}</button>
                            : a.destino}</>
                        : 'Não guardado agora'}
                </p>
                {(a.situacao === 'IGUAL' ? a.avisos.filter(x => !x.startsWith('Já estava')) : a.avisos)
                  .map(x => <p key={x} className="text-sm text-amber-800">{x}</p>)}
                {/* Romaneio sem funrural: volumes para embalar, com o controle de volumes para onde foi */}
                {a.embalar?.map(r => (
                  <p key={r.numero} className="text-sm">
                    <span className="text-gray-600">Volumes para embalar (sem funrural): </span><SituacaoRomaneio r={r} />
                  </p>
                ))}
              </div>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
