import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  ArrowLeft, Download, FileSpreadsheet, FileText, Lock, Plus, RotateCcw, Save, TriangleAlert, Trash2, Upload, X,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  acertoAlfaApi, erroAcerto, CONDICOES_SUGERIDAS, OBSERVACOES_SUGERIDAS, ROTULO_CANCELADO, ROTULO_SITUACAO,
  type AcertoDetalhe, type AcertoEdicao, type ArquivoAcerto, type DiferencaDocumento, type PagamentoAcerto,
  type ResultadoPdf, type RomaneioAcerto, type SituacaoArquivo,
} from '../services/acertoAlfaApi'
import { Modal } from './ModalBase'
import { dataBr, lerNum, num } from '../utils/formato'
import ConferenciaSemana, { type Conferencia } from './ConferenciaSemana'
import Botao, { BotaoIcone } from './ui/Botao'
import Aviso, { toastConfira } from './ui/Aviso'
import AreaSoltar from './ui/AreaSoltar'
import Campo from './ui/Campo'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { classeCampo, classeNumero } from './EstoquePecas'
import { LinhaRomaneio, SituacaoRomaneio } from './RomaneiosEmbalar'

// Um acerto guardado com a cara da planilha "ACERTO G. ALFA": o controle de
// descontos dos romaneios à esquerda, pagamentos e depósitos à direita e os
// totais embaixo. Cada célula se edita no lugar. A planilha baixada sai do que
// está SALVO aqui — o Excel virou só a saída, então é nesta tela que se corrige.
//
// Celular (D-06): a tabela de 600 px rolava para o lado dentro do cartão. Agora,
// abaixo de 768 px, cada linha vira um cartão com os mesmos campos e as contas
// da linha embaixo; os dois desenhos usam o mesmo rascunho.

// Na tela os números ficam como texto enquanto se digita ("1.234,5" no meio do
// caminho não pode virar número e voltar formatado a cada tecla).
// doArquivo (ACE-11): a linha veio do PDF e não se edita, só se exclui (no romaneio, o
// desconto por caixa continua editável: ele não está no documento, ACE-04).
type LinhaRomaneio = { chave: number; data: string; numero: string; valor: string; caixas: string; desconto: string; doArquivo: boolean }
type LinhaPagamento = { chave: number; data: string; condicao: string; valor: string; observacao: string; numero: string; doArquivo: boolean }
type Rascunho = { numero: string; semana: string; romaneios: LinhaRomaneio[]; pagamentos: LinhaPagamento[]; notas: string[] }

let proximaChave = 1
const chave = () => proximaChave++

const paraRascunho = (a: AcertoDetalhe): Rascunho => ({
  numero: String(a.numero),
  semana: a.semana,
  romaneios: a.romaneios.map(r => ({
    chave: chave(),
    data: r.data ?? '',
    // Na planilha a coluna do número às vezes leva texto ("BOLETO", "CH DEV")
    numero: r.rotulo ?? (r.numero > 0 ? String(r.numero) : ''),
    valor: num(r.valor),
    caixas: String(r.caixas),
    desconto: r.descontoCaixa == null ? '' : num(r.descontoCaixa),
    doArquivo: r.doArquivo === true,
  })),
  pagamentos: a.pagamentos.map(p => ({
    chave: chave(),
    data: p.data ?? '',
    condicao: p.condicao,
    valor: p.valor == null ? '' : num(p.valor),
    observacao: p.observacao,
    numero: p.numero,
    doArquivo: p.doArquivo === true,
  })),
  notas: [...a.notas],
})

const inteiro = (t: string) => {
  const n = Number(t.replace(/\./g, '').trim() || '0')
  return Number.isInteger(n) ? n : NaN
}
const dinheiro = (t: string) => Math.round(lerNum(t) * 100) / 100
const opcional = (t: string) => (t.trim() ? dinheiro(t) : null)

const romaneioDaLinha = (x: LinhaRomaneio): RomaneioAcerto => {
  const t = x.numero.trim()
  const ehNumero = /^\d+$/.test(t)
  return {
    numero: ehNumero ? Number(t) : 0,
    rotulo: ehNumero || !t ? null : t.toUpperCase(),
    data: x.data || null,
    valor: dinheiro(x.valor),
    caixas: inteiro(x.caixas),
    descontoCaixa: opcional(x.desconto),
  }
}

const pagamentoDaLinha = (p: LinhaPagamento): PagamentoAcerto => ({
  data: p.data || null,
  condicao: p.condicao.trim().toUpperCase(),
  valor: opcional(p.valor),
  observacao: p.observacao.trim(),
  numero: p.numero.trim(),
})

const vazia = (p: PagamentoAcerto) => !p.data && !p.condicao && p.valor == null && !p.observacao && !p.numero

// SALDO ANT.: a primeira linha com essa condição. Sem valor = automática (vale o
// saldo final do acerto de número anterior, calculado pela API na hora).
const SALDO_ANT = 'SALDO ANT.'
const ehSaldoAnt = (p: LinhaPagamento) => p.condicao.trim().toUpperCase() === SALDO_ANT

const paraEdicao = (r: Rascunho): AcertoEdicao => ({
  numero: inteiro(r.numero),
  semana: r.semana,
  romaneios: r.romaneios.map(romaneioDaLinha),
  // Linha toda em branco some ao salvar, como na planilha
  pagamentos: r.pagamentos.map(pagamentoDaLinha).filter(p => !vazia(p)),
  notas: r.notas.map(n => n.trim()).filter(Boolean),
})

// Só os campos que a tela edita: doArquivo fica de fora da comparação de "alterado"
const doServidor = (a: AcertoDetalhe): AcertoEdicao => ({
  numero: a.numero, semana: a.semana,
  romaneios: a.romaneios.map(r => ({ numero: r.numero, rotulo: r.rotulo, data: r.data, valor: r.valor, caixas: r.caixas, descontoCaixa: r.descontoCaixa })),
  pagamentos: a.pagamentos.map(p => ({ data: p.data, condicao: p.condicao, valor: p.valor, observacao: p.observacao, numero: p.numero })),
  notas: a.notas,
})

// As contas das colunas F e G e do pé da planilha
const descontoDe = (r: RomaneioAcerto) => (Number.isFinite(r.caixas) ? r.caixas : 0) * (r.descontoCaixa ?? 0)
const aPagarDe = (r: RomaneioAcerto) => r.valor - descontoDe(r)

// O que a tela consegue apontar antes de mandar; o resto a API confere
function problemas(e: AcertoEdicao): string[] {
  const p: string[] = []
  if (!(e.numero > 0)) p.push('Número do acerto inválido.')
  if (!e.semana) p.push('Informe a semana.')
  e.romaneios.forEach((r, i) => {
    const nome = r.numero > 0 ? `Romaneio ${r.numero}` : `Romaneio da linha ${i + 1}`
    if (!(r.numero > 0) && !r.rotulo) p.push(`Romaneio da linha ${i + 1}: informe o número (ou um texto como BOLETO).`)
    if (!r.data) p.push(`${nome}: informe a data.`)
    if (r.valor < 0) p.push(`${nome}: valor não pode ser negativo.`)
    if (!(r.caixas >= 0)) p.push(`${nome}: caixas inválidas.`)
    if (r.descontoCaixa != null && r.descontoCaixa < 0) p.push(`${nome}: desconto por caixa não pode ser negativo.`)
  })
  const nums = e.romaneios.map(r => r.numero).filter(n => n > 0)
  const rep = nums.find((n, i) => nums.indexOf(n) !== i)
  if (rep) p.push(`O romaneio ${rep} aparece mais de uma vez.`)
  e.pagamentos.forEach((x, i) => {
    if (!x.condicao) p.push(`Pagamento da linha ${i + 1}: informe a condição (PIX, BOLETO, DEPÓSITO...).`)
  })
  const bols = e.pagamentos.filter(x => x.condicao === 'BOLETO' && x.numero).map(x => x.numero.toUpperCase())
  const bolRep = bols.find((n, i) => bols.indexOf(n) !== i)
  if (bolRep) p.push(`O boleto ${bolRep} aparece mais de uma vez.`)
  return p
}

const TIPO_ARQUIVO: Record<ArquivoAcerto['tipo'], string> = {
  BOLETO: 'Boleto',
  ROMANEIO: 'Romaneio da fazenda',
  PLANILHA: 'Planilha importada',
  OUTRO: 'Não reconhecido',
}

const tamanho = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${num(bytes / 1024 / 1024, 1)} MB`

// Campo que parece célula da planilha e vira caixa ao passar o mouse ou focar
// (só no computador; no celular os cartões usam o campo de sempre, de 44 px)
const celula = 'w-full min-w-0 min-h-9 bg-transparent border border-transparent rounded-md px-2 py-1 text-sm transition-colors hover:border-gray-300 focus:border-tinta-500 focus:bg-white'
const th = 'font-medium px-2 py-2 whitespace-nowrap'

// Crédito com Livia em verde, débito em vermelho
const corSaldo = (v: number) => (v > 0.004 ? 'text-emerald-700' : v < -0.004 ? 'text-red-700' : 'text-gray-900')

export default function AcertoAlfaDetalhe({ id, onVoltar, onMudou }: {
  id: number
  onVoltar: () => void
  onMudou: () => void
}) {
  const [acerto, setAcerto] = useState<AcertoDetalhe | null>(null)
  const [rascunho, setRascunho] = useState<Rascunho | null>(null)
  const [carregando, setCarregando] = useState(true)
  const [falhou, setFalhou] = useState(false)
  const [tentativa, setTentativa] = useState(0)
  const [salvando, setSalvando] = useState(false)
  const [baixando, setBaixando] = useState(false)
  const [subindo, setSubindo] = useState(false)
  const [conferencia, setConferencia] = useState<Conferencia | null>(null)
  const [conferindo, setConferindo] = useState(false)
  // Avisos da planilha baixada e dos PDFs subidos: ficam até ele fechar (D-07)
  const [avisosAgora, setAvisosAgora] = useState<string[]>([])
  // Último envio de PDFs: o que aconteceu com cada um, e os que esperam Cancelar ou Substituir
  const [lidosAgora, setLidosAgora] = useState<ResultadoPdf[]>([])
  const [pendentes, setPendentes] = useState<{ arquivos: File[]; itens: ResultadoPdf[] } | null>(null)
  const confirmar = useConfirmar()

  const aplicar = (a: AcertoDetalhe) => { setAcerto(a); setRascunho(paraRascunho(a)) }

  const conferir = useCallback(async () => {
    setConferindo(true)
    try { setConferencia((await acertoAlfaApi.conferencia(id)).data) }
    catch (e) { toast.error(await erroAcerto(e, 'Não consegui fazer a conferência da semana.')) }
    finally { setConferindo(false) }
  }, [id])

  useEffect(() => {
    let cancelado = false
    setCarregando(true)
    setFalhou(false)
    setConferencia(null)
    acertoAlfaApi.detalhe(id)
      .then(r => { if (!cancelado) aplicar(r.data) })
      .catch(() => { if (!cancelado) setFalhou(true) })
      .finally(() => { if (!cancelado) setCarregando(false) })
    conferir()
    return () => { cancelado = true }
  }, [id, conferir, tentativa])

  const edicao = useMemo(() => (rascunho ? paraEdicao(rascunho) : null), [rascunho])
  const alterado = !!acerto && !!edicao && JSON.stringify(edicao) !== JSON.stringify(doServidor(acerto))

  // Fechar ou recarregar a aba com edição pendente pede confirmação ao navegador
  useEffect(() => {
    if (!alterado) return
    const avisar = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', avisar)
    return () => window.removeEventListener('beforeunload', avisar)
  }, [alterado])

  // Os totais da tela saem do rascunho, para acompanharem cada tecla. A linha
  // SALDO ANT. sem valor é a automática: entra com o saldo do acerto anterior.
  const saldoAuto = acerto?.saldoAnterior.saldo ?? null
  const totais = useMemo(() => {
    if (!rascunho) return null
    const roms = rascunho.romaneios.map(romaneioDaLinha)
    const iSaldo = rascunho.pagamentos.findIndex(ehSaldoAnt)
    const pags = rascunho.pagamentos.map((p, i) => {
      const x = pagamentoDaLinha(p)
      return i === iSaldo && x.valor == null && saldoAuto != null ? { ...x, valor: saldoAuto } : x
    })
    const aPagar = roms.reduce((s, r) => s + aPagarDe(r), 0)
    const pagamentos = pags.reduce((s, p) => s + (p.valor ?? 0), 0)
    return {
      valorRomaneios: roms.reduce((s, r) => s + r.valor, 0),
      descontos: roms.reduce((s, r) => s + descontoDe(r), 0),
      caixas: roms.reduce((s, r) => s + (Number.isFinite(r.caixas) ? r.caixas : 0), 0),
      aPagar,
      pagamentos,
      saldo: Math.round((pagamentos - aPagar) * 100) / 100,
    }
  }, [rascunho, saldoAuto])

  const voltarSimples = (
    <Botao variante="fantasma" icone={ArrowLeft} onClick={onVoltar} className="-ml-2">Acertos guardados</Botao>
  )

  if (carregando && !acerto) {
    return (
      <div className="space-y-4">
        {voltarSimples}
        <Carregando forma="cartoes" linhas={4} rotulo="Abrindo o acerto" />
        <Carregando linhas={5} rotulo="Abrindo o acerto" />
      </div>
    )
  }
  if (falhou || !acerto || !rascunho || !edicao || !totais) {
    return (
      <div className="space-y-4">
        {voltarSimples}
        <ErroCarga mensagem="Não consegui abrir o acerto." detalhe="Pode ter sido excluído, ou a internet caiu. Tente de novo."
                   onTentar={() => setTentativa(t => t + 1)} />
      </div>
    )
  }

  const mudar = (f: (r: Rascunho) => Rascunho) => setRascunho(r => (r ? f(r) : r))
  const mudarRomaneio = (k: number, campo: Partial<LinhaRomaneio>) =>
    mudar(r => ({ ...r, romaneios: r.romaneios.map(x => (x.chave === k ? { ...x, ...campo } : x)) }))
  const mudarPagamento = (k: number, campo: Partial<LinhaPagamento>) =>
    mudar(r => ({ ...r, pagamentos: r.pagamentos.map(p => (p.chave === k ? { ...p, ...campo } : p)) }))
  const tirarRomaneio = (k: number) => mudar(y => ({ ...y, romaneios: y.romaneios.filter(z => z.chave !== k) }))
  const tirarPagamento = (k: number) => mudar(r => ({ ...r, pagamentos: r.pagamentos.filter(x => x.chave !== k) }))

  const incluirRomaneio = () => mudar(r => ({
    ...r,
    romaneios: [...r.romaneios, {
      chave: chave(), data: r.romaneios[r.romaneios.length - 1]?.data || r.semana, numero: '', valor: '', caixas: '', desconto: '', doArquivo: false,
    }],
  }))
  const incluirPagamento = () => mudar(r => ({
    ...r,
    pagamentos: [...r.pagamentos, { chave: chave(), data: '', condicao: '', valor: '', observacao: '', numero: '', doArquivo: false }],
  }))

  // Linha que veio do arquivo não se edita: o único jeito de mudar é excluir (ACE-11).
  // Linha lançada à mão sai direto, como sempre (volta com "Descartar").
  const excluirRomaneio = async (x: LinhaRomaneio, nome: string) => {
    if (x.doArquivo && !await confirmar({
      titulo: `Excluir o romaneio ${nome}?`,
      texto: 'Ele veio do arquivo e não se edita. Sai do acerto quando você salvar; até lá, "Descartar" traz de volta.',
      confirmar: 'Excluir',
    })) return
    tirarRomaneio(x.chave)
  }
  const excluirPagamento = async (p: LinhaPagamento, nome: string) => {
    if (p.doArquivo && !await confirmar({
      titulo: `Excluir o ${nome}?`,
      texto: 'Ele veio do arquivo e não se edita. Sai do acerto quando você salvar; até lá, "Descartar" traz de volta.',
      confirmar: 'Excluir',
    })) return
    tirarPagamento(p.chave)
  }

  // SALDO ANT. = o crédito ou débito com que o acerto anterior fechou. Vazio é
  // automático (corrigir o anterior corrige este); com valor, foi digitado à mão.
  const linhaSaldo = rascunho.pagamentos.find(ehSaldoAnt)
  const saldoAnterior = acerto.saldoAnterior
  const saldoAutomatico = !!linhaSaldo && !linhaSaldo.valor.trim()
  const digitarSaldoAMao = () => {
    if (linhaSaldo) mudarPagamento(linhaSaldo.chave, { valor: num(saldoAnterior.saldo ?? 0), data: linhaSaldo.data || saldoAnterior.semana || '' })
  }
  const voltarSaldoAutomatico = () => { if (linhaSaldo) mudarPagamento(linhaSaldo.chave, { valor: '' }) }
  const incluirSaldoAnterior = () => mudar(r => ({
    ...r,
    pagamentos: [{ chave: chave(), data: saldoAnterior.semana ?? '', condicao: SALDO_ANT, valor: '', observacao: '', numero: '', doArquivo: false }, ...r.pagamentos],
  }))

  const salvar = async () => {
    const p = problemas(edicao)
    if (p.length) { toast.error(p[0]); return }
    setSalvando(true)
    try {
      // Vai junto quando a tela abriu o acerto: se chegou PDF depois, a API recusa em vez de apagar
      const { data } = await acertoAlfaApi.salvar(id, { ...edicao, atualizadoEm: acerto.atualizadoEm })
      aplicar(data)
      toast.success('Acerto salvo.')
      onMudou()
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }

  const descartar = async () => {
    if (await confirmar({ titulo: 'Descartar as alterações?', texto: 'O que foi mudado nesta tela e ainda não foi salvo volta como estava.', confirmar: 'Descartar' }))
      setRascunho(paraRascunho(acerto))
  }

  const voltar = async () => {
    if (alterado && !await confirmar({ titulo: 'Sair sem salvar?', texto: `Há alterações não salvas no acerto ${acerto.numero}. Saindo agora, elas se perdem.`, confirmar: 'Sair sem salvar', cancelar: 'Continuar editando' })) return
    onVoltar()
  }

  const baixarPlanilha = async () => {
    if (alterado && !await confirmar({ titulo: 'Baixar sem as alterações?', texto: 'A planilha sai do que está salvo, sem as alterações desta tela.', confirmar: 'Baixar assim mesmo', perigo: false })) return
    setBaixando(true)
    try {
      const avisos = await acertoAlfaApi.baixarPlanilha(id, `${acerto.numero}-ACERTO G. ALFA.xlsx`)
      if (avisos) { setAvisosAgora(avisos.split(' | ')); toastConfira('Planilha baixada. Confira o aviso na tela.') }
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui gerar a planilha.')) }
    finally { setBaixando(false) }
  }

  const baixarArquivo = async (a: ArquivoAcerto) => {
    try { await acertoAlfaApi.baixarArquivo(id, a) }
    catch (e) { toast.error(await erroAcerto(e, 'Não consegui baixar o arquivo.')) }
  }

  const removerArquivo = async (a: ArquivoAcerto) => {
    if (!await confirmar({ titulo: 'Remover o arquivo?', texto: <><b className="break-all">{a.nome}</b>. O que veio dele continua no acerto.</>, confirmar: 'Remover' })) return
    try {
      await acertoAlfaApi.excluirArquivo(id, a.id)
      const { data } = await acertoAlfaApi.detalhe(id)
      // Mantém a edição em andamento: remover arquivo não mexe nos dados
      setAcerto(data)
      if (!alterado) setRascunho(paraRascunho(data))
      toast.success('Arquivo removido.')
      onMudou()
      conferir()
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui remover o arquivo.')) }
  }

  // Os PDFs novos se somam ao que está SALVO; com edição pendente ela se perderia
  // Documento que já está no sistema e veio diferente não entra sozinho: abre a janela
  // de Cancelar ou Substituir (ACE-11, UI-21); "Substituir" reenvia só esses arquivos.
  const subir = async (files: File[], substituir = false) => {
    const pdfs = files.filter(f => f.name.toLowerCase().endsWith('.pdf'))
    if (!pdfs.length) { toast.error('Selecione os PDFs.'); return }
    if (alterado) { toast.error('Salve ou descarte as alterações antes de subir mais PDFs.'); return }
    setSubindo(true)
    try {
      const { data } = await acertoAlfaApi.acrescentarArquivos(id, pdfs, substituir)
      aplicar(data.acerto)
      const porArquivo = data.arquivos ?? []
      const esperando = porArquivo.filter(a => a.situacao === 'CONFIRMAR')
      setPendentes(esperando.length
        ? { arquivos: pdfs.filter(f => esperando.some(c => c.arquivo === f.name)), itens: esperando }
        : null)
      // Reenvio: troca só a linha dos arquivos reenviados; o resto do envio anterior fica
      setLidosAgora(antes => substituir
        ? [...antes.filter(a => !porArquivo.some(p => p.arquivo === a.arquivo)), ...porArquivo]
        : porArquivo)
      const partes = [
        data.boletosNovos > 0 && `${data.boletosNovos} ${data.boletosNovos === 1 ? 'boleto' : 'boletos'}`,
        data.romaneiosNovos > 0 && `${data.romaneiosNovos} ${data.romaneiosNovos === 1 ? 'romaneio' : 'romaneios'}`,
        data.substituidos > 0 && `${data.substituidos} ${data.substituidos === 1 ? 'documento substituído' : 'documentos substituídos'}`,
      ].filter(Boolean)
      if (partes.length) toast.success(`Entrou: ${partes.join(', ')}.`)
      else if (esperando.length) toastConfira('Documento repetido com diferença. Escolha na janela.')
      else if (data.arquivosNovos > 0) toast.success('PDF guardado, sem boleto ou romaneio novo.')
      else toast.success('Nada novo: já estava tudo no acerto.')
      // Os avisos de cada arquivo aparecem na lista dos arquivos lidos; aqui só os gerais
      const nomes = porArquivo.map(a => `${a.arquivo}: `)
      setAvisosAgora((data.avisos ?? []).filter(x => !nomes.some(n => x.startsWith(n)) && !/já estavam guardados, iguais/.test(x)))
      onMudou()
      conferir()
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui ler os PDFs.')) }
    finally { setSubindo(false) }
  }

  const excluir = async () => {
    if (!await confirmar({
      titulo: `Excluir o acerto ${acerto.numero}?`,
      texto: `Vão junto os ${acerto.arquivos.length} arquivo(s) guardados nele. Não dá para desfazer.`,
      confirmar: 'Excluir acerto',
    })) return
    try {
      await acertoAlfaApi.excluir(id)
      toast.success('Acerto excluído.')
      onMudou()
      onVoltar()
    } catch (e) { toast.error(await erroAcerto(e, 'Não consegui excluir.')) }
  }

  const avisos = [...acerto.avisos, ...acerto.avisosPlanilha]
  const pdfsGuardados = acerto.arquivos.filter(a => a.tipo !== 'PLANILHA').length

  return (
    <div className="space-y-4">
      <datalist id="acerto-condicoes">{CONDICOES_SUGERIDAS.map(c => <option key={c} value={c} />)}</datalist>
      <datalist id="acerto-observacoes">{OBSERVACOES_SUGERIDAS.map(c => <option key={c} value={c} />)}</datalist>

      {/* Cabeçalho: voltar, nome do acerto e as duas ações */}
      <div className="space-y-3">
        <Botao variante="fantasma" icone={ArrowLeft} onClick={voltar} className="-ml-2">Acertos guardados</Botao>
        <div className="flex flex-col sm:flex-row sm:items-end gap-3">
          <div className="flex-1 min-w-0">
            <h2 className="text-2xl font-semibold text-gray-900 leading-tight">
              Acerto {acerto.numero}
              <span className="ml-2 font-sans text-base font-normal text-gray-500 tabular-nums">semana de {dataBr(acerto.semana)}</span>
            </h2>
            <p className="text-sm text-gray-500 tabular-nums mt-0.5">
              Guardado em {new Date(acerto.criadoEm).toLocaleDateString('pt-BR')}
              {acerto.atualizadoEm !== acerto.criadoEm && <>, alterado em {new Date(acerto.atualizadoEm).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })}</>}
            </p>
          </div>
          <div className="flex gap-2 [&>*:first-child]:flex-1 sm:[&>*:first-child]:flex-none">
            <Botao variante="primario" icone={Download} onClick={baixarPlanilha} carregando={baixando} textoCarregando="Gerando...">
              Baixar planilha
            </Botao>
            <BotaoIcone icone={Trash2} rotulo={`Excluir o acerto ${acerto.numero}`} perigo onClick={excluir} className="border border-gray-300" />
          </div>
        </div>
      </div>

      {avisosAgora.length > 0 && (
        <Aviso tipo="atencao" titulo="Confira" itens={avisosAgora} onFechar={() => setAvisosAgora([])} />
      )}
      {avisos.length > 0 && <Aviso tipo="atencao" itens={avisos} />}

      {/* Barra de salvar: só aparece com alteração pendente, e fica presa no alto ao rolar */}
      {alterado && (
        <div role="status" className="sticky top-2 z-10 bg-gema-50 border border-gema-300 rounded-xl pl-4 pr-2 py-2 flex flex-wrap items-center gap-2 shadow-md animate-entrada">
          <TriangleAlert size={17} className="text-gema-800 shrink-0" aria-hidden />
          <span className="text-sm font-semibold text-gema-900 flex-1 min-w-[10rem]">Alterações não salvas</span>
          <div className="flex gap-2 w-full sm:w-auto [&>*]:flex-1 sm:[&>*]:flex-none">
            <Botao variante="fantasma" icone={RotateCcw} onClick={descartar}>Descartar</Botao>
            <Botao variante="primario" icone={Save} onClick={salvar} carregando={salvando} textoCarregando="Salvando...">Salvar</Botao>
          </div>
        </div>
      )}

      {/* Dados do acerto (número e semana): raramente mudam, ficam juntos e pequenos */}
      <div className="bg-white rounded-xl border border-gray-200 p-4 grid grid-cols-2 sm:flex sm:flex-wrap gap-3">
        <Campo rotulo="Número do acerto" className="sm:w-36">
          <input value={rascunho.numero} inputMode="numeric"
                 onChange={e => mudar(r => ({ ...r, numero: e.target.value }))}
                 className={classeNumero} />
        </Campo>
        <Campo rotulo="Semana (segunda-feira)" className="sm:w-48">
          <input type="date" value={rascunho.semana}
                 onChange={e => mudar(r => ({ ...r, semana: e.target.value }))}
                 className={classeCampo} />
        </Campo>
      </div>

      {/* Os dois blocos da planilha: lado a lado em tela bem larga, um embaixo do outro no resto */}
      <div className="grid grid-cols-1 2xl:grid-cols-2 gap-4 items-start">
        {/* Controle de descontos dos romaneios */}
        <section aria-labelledby="acerto-romaneios" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <CabecalhoBloco id="acerto-romaneios" titulo="Controle de descontos dos romaneios"
                          detalhe={`${rascunho.romaneios.length} ${rascunho.romaneios.length === 1 ? 'linha' : 'linhas'}`}
                          acao={<Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={incluirRomaneio}>Incluir linha</Botao>} />

          {rascunho.romaneios.length === 0 ? (
            <Vazio compacto titulo="Nenhum romaneio da fazenda ainda">Os romaneios entram sozinhos com os PDFs, ou pela linha nova.</Vazio>
          ) : (
            <>
              {/* Celular: um cartão por romaneio */}
              <ul className="md:hidden divide-y divide-gray-100">
                {rascunho.romaneios.map((x, i) => {
                  const r = romaneioDaLinha(x)
                  const nome = x.numero.trim() || `da linha ${i + 1}`
                  if (x.doArquivo) {
                    return (
                      <li key={x.chave} className="px-4 py-3.5 space-y-3">
                        <div className="flex items-baseline justify-between gap-3">
                          <div className="min-w-0">
                            <p className="font-semibold text-gray-900 tabular-nums">Romaneio {x.numero}</p>
                            <p className="text-sm text-gray-600 tabular-nums">
                              {x.data ? dataBr(x.data) : <span className="text-amber-800 font-medium">sem data</span>} · {faltou(x.caixas, 'sem caixas')} caixas
                            </p>
                          </div>
                          <p className="font-semibold text-gray-900 tabular-nums whitespace-nowrap">{faltou(x.valor, 'sem valor')}</p>
                        </div>
                        <SeloDoArquivo detalhe="só o desconto por caixa muda" />
                        <div className="grid grid-cols-2 gap-3 items-end">
                          <CampoCartao rotulo="Desconto por caixa"><input value={x.desconto} inputMode="decimal" placeholder="sem"
                                 onChange={e => mudarRomaneio(x.chave, { desconto: e.target.value })}
                                 onBlur={() => x.desconto.trim() && mudarRomaneio(x.chave, { desconto: num(lerNum(x.desconto)) })}
                                 className={`${classeNumero} text-right`} /></CampoCartao>
                          <div className="flex items-center justify-end gap-3">
                            <dl className="text-sm tabular-nums text-right">
                              <div className="flex gap-1 justify-end"><dt className="text-gray-500">Desconto</dt><dd className="text-gray-700">{num(descontoDe(r))}</dd></div>
                              <div className="flex gap-1 justify-end"><dt className="text-gray-500">A pagar</dt><dd className="font-semibold text-gray-900">{num(aPagarDe(r))}</dd></div>
                            </dl>
                            <BotaoIcone icone={Trash2} perigo rotulo={`Excluir o romaneio ${nome}`} onClick={() => excluirRomaneio(x, nome)} />
                          </div>
                        </div>
                      </li>
                    )
                  }
                  return (
                    <li key={x.chave} className="px-4 py-3.5 space-y-3">
                      <div className="grid grid-cols-2 gap-3">
                        <CampoCartao rotulo="Data"><input type="date" value={x.data} onChange={e => mudarRomaneio(x.chave, { data: e.target.value })} className={classeCampo} /></CampoCartao>
                        <CampoCartao rotulo="Número"><input value={x.numero} placeholder="ou BOLETO" onChange={e => mudarRomaneio(x.chave, { numero: e.target.value })} className={classeNumero} /></CampoCartao>
                      </div>
                      <div className="grid grid-cols-3 gap-3">
                        <CampoCartao rotulo="Valor"><input value={x.valor} inputMode="decimal" placeholder="0,00"
                               onChange={e => mudarRomaneio(x.chave, { valor: e.target.value })}
                               onBlur={() => x.valor.trim() && mudarRomaneio(x.chave, { valor: num(lerNum(x.valor)) })}
                               className={`${classeNumero} text-right`} /></CampoCartao>
                        <CampoCartao rotulo="Caixas"><input value={x.caixas} inputMode="numeric" placeholder="0"
                               onChange={e => mudarRomaneio(x.chave, { caixas: e.target.value })} className={`${classeNumero} text-right`} /></CampoCartao>
                        <CampoCartao rotulo="Por caixa"><input value={x.desconto} inputMode="decimal" placeholder="sem"
                               onChange={e => mudarRomaneio(x.chave, { desconto: e.target.value })}
                               onBlur={() => x.desconto.trim() && mudarRomaneio(x.chave, { desconto: num(lerNum(x.desconto)) })}
                               className={`${classeNumero} text-right`} /></CampoCartao>
                      </div>
                      <div className="flex items-center gap-3">
                        <dl className="flex-1 flex flex-wrap gap-x-4 text-sm tabular-nums">
                          <div className="flex gap-1"><dt className="text-gray-500">Desconto</dt><dd className="text-gray-700">{num(descontoDe(r))}</dd></div>
                          <div className="flex gap-1"><dt className="text-gray-500">A pagar</dt><dd className="font-semibold text-gray-900">{num(aPagarDe(r))}</dd></div>
                        </dl>
                        <BotaoIcone icone={Trash2} perigo rotulo={`Tirar o romaneio ${nome}`} onClick={() => excluirRomaneio(x, nome)} />
                      </div>
                    </li>
                  )
                })}
              </ul>

              {/* Computador: com a cara da planilha */}
              <div className="hidden md:block overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="text-xs text-gray-500 bg-gray-50">
                    <tr>
                      <th className={`${th} text-left w-[136px]`}>Data</th>
                      <th className={`${th} text-left w-[96px]`}>Número</th>
                      <th className={`${th} text-right`}>Valor do romaneio</th>
                      <th className={`${th} text-right w-[72px]`}>Caixas</th>
                      <th className={`${th} text-right w-[88px]`}>Desconto por caixa</th>
                      <th className={`${th} text-right`}>Valor do desconto</th>
                      <th className={`${th} text-right`}>Valor a pagar</th>
                      <th className="w-10"><span className="sr-only">Tirar</span></th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {rascunho.romaneios.map((x, i) => {
                      const r = romaneioDaLinha(x)
                      const nome = x.numero.trim() || `da linha ${i + 1}`
                      return (
                        <tr key={x.chave} className="hover:bg-gray-50/70">
                          {x.doArquivo ? (
                            <>
                              <td className="px-1 py-0.5"><Fixo>{x.data ? dataBr(x.data) : faltou('', 'sem data')}</Fixo></td>
                              <td className="px-1 py-0.5">
                                <Fixo className="inline-flex items-center gap-1.5">
                                  {x.numero}
                                  <span title="Veio do arquivo: não se edita, só se exclui (o desconto por caixa muda)">
                                    <Lock size={12} className="text-gray-400" aria-hidden />
                                    <span className="sr-only">, veio do arquivo</span>
                                  </span>
                                </Fixo>
                              </td>
                              <td className="px-1 py-0.5"><Fixo className="text-right">{faltou(x.valor, 'sem valor')}</Fixo></td>
                              <td className="px-1 py-0.5"><Fixo className="text-right">{faltou(x.caixas, 'sem caixas')}</Fixo></td>
                            </>
                          ) : (
                            <>
                              <td className="px-1 py-0.5">
                                <input type="date" value={x.data} aria-label={`Data do romaneio ${nome}`} onChange={e => mudarRomaneio(x.chave, { data: e.target.value })} className={celula} />
                              </td>
                              <td className="px-1 py-0.5">
                                <input value={x.numero} placeholder="número" aria-label={`Número do romaneio ${nome}`} title="Número do romaneio, ou um texto como BOLETO ou CH DEV"
                                       onChange={e => mudarRomaneio(x.chave, { numero: e.target.value })} className={`${celula} tabular-nums`} />
                              </td>
                              <td className="px-1 py-0.5">
                                <input value={x.valor} inputMode="decimal" placeholder="0,00" aria-label={`Valor do romaneio ${nome}`}
                                       onChange={e => mudarRomaneio(x.chave, { valor: e.target.value })}
                                       onBlur={() => x.valor.trim() && mudarRomaneio(x.chave, { valor: num(lerNum(x.valor)) })}
                                       className={`${celula} text-right tabular-nums`} />
                              </td>
                              <td className="px-1 py-0.5">
                                <input value={x.caixas} inputMode="numeric" placeholder="0" aria-label={`Caixas do romaneio ${nome}`}
                                       onChange={e => mudarRomaneio(x.chave, { caixas: e.target.value })}
                                       className={`${celula} text-right tabular-nums`} />
                              </td>
                            </>
                          )}
                          <td className="px-1 py-0.5">
                            <input value={x.desconto} inputMode="decimal" placeholder="sem" aria-label={`Desconto por caixa do romaneio ${nome}`}
                                   onChange={e => mudarRomaneio(x.chave, { desconto: e.target.value })}
                                   onBlur={() => x.desconto.trim() && mudarRomaneio(x.chave, { desconto: num(lerNum(x.desconto)) })}
                                   className={`${celula} text-right tabular-nums`} />
                          </td>
                          <td className="px-2 py-0.5 text-right tabular-nums text-gray-500 whitespace-nowrap">{num(descontoDe(r))}</td>
                          <td className="px-2 py-0.5 text-right tabular-nums font-medium text-gray-900 whitespace-nowrap">{num(aPagarDe(r))}</td>
                          <td className="px-0.5 py-0.5 text-center">
                            <BotaoIcone icone={Trash2} perigo rotulo={`${x.doArquivo ? 'Excluir' : 'Tirar'} o romaneio ${nome}`} onClick={() => excluirRomaneio(x, nome)} />
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="border-t-2 border-gray-200 text-gray-900 tabular-nums">
                    <tr>
                      <td className="px-2 py-2.5 font-semibold" colSpan={2}>Total dos romaneios</td>
                      <td className="px-2 py-2.5 text-right">{num(totais.valorRomaneios)}</td>
                      <td className="px-2 py-2.5 text-right">{totais.caixas.toLocaleString('pt-BR')}</td>
                      <td />
                      <td className="px-2 py-2.5 text-right text-gray-500">{num(totais.descontos)}</td>
                      <td className="px-2 py-2.5 text-right font-semibold">{num(totais.aPagar)}</td>
                      <td />
                    </tr>
                  </tfoot>
                </table>
              </div>
              {/* Celular: o total da coluna, já que a tabela não aparece */}
              <dl className="md:hidden px-4 py-3 border-t-2 border-gray-200 grid grid-cols-3 gap-2 text-sm tabular-nums">
                <div><dt className="text-xs text-gray-500">Valor</dt><dd className="text-gray-900">{num(totais.valorRomaneios)}</dd></div>
                <div><dt className="text-xs text-gray-500">Caixas</dt><dd className="text-gray-900">{totais.caixas.toLocaleString('pt-BR')}</dd></div>
                <div><dt className="text-xs text-gray-500">A pagar</dt><dd className="font-semibold text-gray-900">{num(totais.aPagar)}</dd></div>
              </dl>
            </>
          )}

          {/* Observações que ela escreve embaixo do total ("teve queda de R$ 7,00 por caixa") */}
          <div className="px-4 py-3 border-t border-gray-100 space-y-2">
            <div className="flex items-center justify-between gap-2">
              <h4 className="text-sm font-medium text-gray-700">Observações embaixo do total</h4>
              <Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={() => mudar(r => ({ ...r, notas: [...r.notas, ''] }))}>
                Incluir observação
              </Botao>
            </div>
            {rascunho.notas.length === 0 && <p className="text-sm text-gray-500">Nenhuma observação.</p>}
            {rascunho.notas.map((n, i) => (
              <div key={i} className="flex items-center gap-1">
                <input value={n} placeholder="texto livre" aria-label={`Observação ${i + 1}`}
                       onChange={e => mudar(r => ({ ...r, notas: r.notas.map((x, j) => (j === i ? e.target.value : x)) }))}
                       className={classeCampo} />
                <BotaoIcone icone={Trash2} perigo rotulo={`Tirar a observação ${i + 1}`}
                            onClick={() => mudar(r => ({ ...r, notas: r.notas.filter((_, j) => j !== i) }))} />
              </div>
            ))}
          </div>
        </section>

        {/* Pagamentos e depósitos */}
        <section aria-labelledby="acerto-pagamentos" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <CabecalhoBloco id="acerto-pagamentos" titulo="Pagamentos e depósitos"
                          detalhe={`${rascunho.pagamentos.length} ${rascunho.pagamentos.length === 1 ? 'linha' : 'linhas'}`}
                          acao={<Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={incluirPagamento}>Incluir linha</Botao>} />

          <SaldoAnteriorAviso saldo={saldoAnterior} linha={linhaSaldo} automatico={saldoAutomatico}
                              onAMao={digitarSaldoAMao} onAutomatico={voltarSaldoAutomatico} onIncluir={incluirSaldoAnterior} />

          {rascunho.pagamentos.length === 0 ? (
            <Vazio compacto titulo="Nenhum pagamento ou depósito">Os boletos entram com os PDFs; PIX e depósito pela linha nova.</Vazio>
          ) : (
            <>
              {/* Celular: um cartão por pagamento */}
              <ul className="md:hidden divide-y divide-gray-100">
                {rascunho.pagamentos.map((p, i) => {
                  const esperando = p.condicao.trim().toUpperCase() === 'BOLETO' && !p.valor.trim()
                  const auto = saldoAutomatico && p.chave === linhaSaldo?.chave
                  const nome = `${p.condicao.trim() || 'da linha'} ${p.numero.trim() || i + 1}`
                  return (
                    <li key={p.chave} className={`px-4 py-3.5 space-y-3 ${esperando ? 'bg-gema-50' : auto ? 'bg-tinta-50/70' : ''}`}>
                      {auto ? (
                        <div className="flex items-baseline justify-between gap-3">
                          <div className="min-w-0">
                            <p className="font-semibold text-gray-900">{SALDO_ANT}</p>
                            <p className="text-sm text-tinta-800">{saldoAnterior.numero ? `Automático, do acerto ${saldoAnterior.numero}` : 'Automático'} · {saldoAnterior.semana ? dataBr(saldoAnterior.semana) : 'sem data'}</p>
                          </div>
                          <p className={`font-semibold tabular-nums whitespace-nowrap ${saldoAnterior.saldo == null ? 'text-amber-800' : 'text-gray-900'}`}>
                            {saldoAnterior.saldo == null ? 'sem valor' : num(saldoAnterior.saldo)}
                          </p>
                        </div>
                      ) : p.doArquivo ? (
                        <div className="space-y-1">
                          <div className="flex items-baseline justify-between gap-3">
                            <div className="min-w-0">
                              <p className="font-semibold text-gray-900">{p.condicao} <span className="font-normal tabular-nums break-all">{p.numero}</span></p>
                              <p className="text-sm text-gray-600 tabular-nums">
                                {p.observacao.trim() || <span className="text-amber-800 font-medium">sem pagador</span>} · vence {p.data ? dataBr(p.data) : <span className="text-amber-800 font-medium">sem data</span>}
                              </p>
                            </div>
                            <p className="font-semibold text-gray-900 tabular-nums whitespace-nowrap">{faltou(p.valor, 'sem valor')}</p>
                          </div>
                          <SeloDoArquivo />
                        </div>
                      ) : (
                        <>
                          <div className="grid grid-cols-2 gap-3">
                            <CampoCartao rotulo={p.condicao.trim().toUpperCase() === 'BOLETO' ? 'Vencimento' : 'Data'}>
                              <input type="date" value={p.data} onChange={e => mudarPagamento(p.chave, { data: e.target.value })} className={classeCampo} />
                            </CampoCartao>
                            <CampoCartao rotulo="Condição">
                              <input value={p.condicao} list="acerto-condicoes" placeholder="PIX, BOLETO..."
                                     onChange={e => mudarPagamento(p.chave, { condicao: e.target.value })}
                                     onBlur={() => mudarPagamento(p.chave, { condicao: p.condicao.trim().toUpperCase() })}
                                     className={`${classeCampo} uppercase`} />
                            </CampoCartao>
                          </div>
                          <div className="grid grid-cols-2 gap-3">
                            <CampoCartao rotulo="Valor">
                              <input value={p.valor} inputMode="decimal" placeholder={esperando ? 'aguardando' : '0,00'}
                                     onChange={e => mudarPagamento(p.chave, { valor: e.target.value })}
                                     onBlur={() => p.valor.trim() && mudarPagamento(p.chave, { valor: num(lerNum(p.valor)) })}
                                     className={`${classeNumero} text-right`} />
                            </CampoCartao>
                            <CampoCartao rotulo="Número">
                              <input value={p.numero} onChange={e => mudarPagamento(p.chave, { numero: e.target.value })} className={classeNumero} />
                            </CampoCartao>
                          </div>
                          <CampoCartao rotulo="Observação">
                            <input value={p.observacao} list="acerto-observacoes" placeholder="cliente ou texto livre"
                                   onChange={e => mudarPagamento(p.chave, { observacao: e.target.value })} className={classeCampo} />
                          </CampoCartao>
                        </>
                      )}
                      <div className="flex items-center justify-between gap-3">
                        <p className="text-sm text-gema-900 font-medium">{esperando ? 'Boleto esperando o valor' : ''}</p>
                        <BotaoIcone icone={Trash2} perigo rotulo={`${p.doArquivo ? 'Excluir' : 'Tirar'} o pagamento ${nome}`} onClick={() => excluirPagamento(p, `boleto ${p.numero.trim()}`)} />
                      </div>
                    </li>
                  )
                })}
              </ul>

              <div className="hidden md:block overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="text-xs text-gray-500 bg-gray-50">
                    <tr>
                      <th className={`${th} text-left w-[136px]`}>Data</th>
                      <th className={`${th} text-left w-[120px]`}>Condição</th>
                      <th className={`${th} text-right`}>Valor</th>
                      <th className={`${th} text-left`}>Observação</th>
                      <th className={`${th} text-left w-[96px]`}>Número</th>
                      <th className="w-10"><span className="sr-only">Tirar</span></th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {rascunho.pagamentos.map((p, i) => {
                      // Boleto lançado e ainda sem valor: é o que falta chegar
                      const esperando = p.condicao.trim().toUpperCase() === 'BOLETO' && !p.valor.trim()
                      // SALDO ANT. automático: data e valor são os do acerto anterior
                      const auto = saldoAutomatico && p.chave === linhaSaldo?.chave
                      const nome = `${p.condicao.trim() || 'da linha'} ${p.numero.trim() || i + 1}`
                      return (
                        <tr key={p.chave} className={esperando ? 'bg-gema-50' : auto ? 'bg-tinta-50/70' : 'hover:bg-gray-50/70'}>
                          {p.doArquivo ? (
                            <>
                              <td className="px-1 py-0.5"><Fixo>{p.data ? dataBr(p.data) : faltou('', 'sem data')}</Fixo></td>
                              <td className="px-1 py-0.5"><Fixo>{p.condicao}</Fixo></td>
                              <td className="px-1 py-0.5"><Fixo className="text-right">{faltou(p.valor, 'sem valor')}</Fixo></td>
                              <td className="px-1 py-0.5"><Fixo>{faltou(p.observacao, 'sem pagador')}</Fixo></td>
                              <td className="px-1 py-0.5">
                                <Fixo className="inline-flex items-center gap-1.5">
                                  <span className="break-all">{p.numero}</span>
                                  <span title="Veio do arquivo: não se edita, só se exclui">
                                    <Lock size={12} className="text-gray-400" aria-hidden />
                                    <span className="sr-only">, veio do arquivo</span>
                                  </span>
                                </Fixo>
                              </td>
                            </>
                          ) : (<>
                          <td className="px-1 py-0.5">
                            {auto ? (
                              <span className="block px-2 py-1 text-sm text-gray-700 tabular-nums" title="Segunda-feira do acerto anterior">
                                {saldoAnterior.semana ? dataBr(saldoAnterior.semana) : '—'}
                              </span>
                            ) : (
                              <input type="date" value={p.data} aria-label={`Data do pagamento ${nome}`} onChange={e => mudarPagamento(p.chave, { data: e.target.value })} className={celula}
                                     title={p.condicao.trim().toUpperCase() === 'BOLETO' ? 'Vencimento do boleto' : undefined} />
                            )}
                          </td>
                          <td className="px-1 py-0.5">
                            <input value={p.condicao} list="acerto-condicoes" placeholder="condição" aria-label={`Condição do pagamento ${i + 1}`}
                                   onChange={e => mudarPagamento(p.chave, { condicao: e.target.value })}
                                   onBlur={() => mudarPagamento(p.chave, { condicao: p.condicao.trim().toUpperCase() })}
                                   className={`${celula} uppercase`} />
                          </td>
                          <td className="px-1 py-0.5">
                            {auto ? (
                              <span className={`block px-2 py-1 text-sm text-right tabular-nums ${saldoAnterior.saldo == null ? 'text-amber-800' : 'text-gray-900'}`}
                                    title={saldoAnterior.numero ? `Saldo final do acerto ${saldoAnterior.numero}` : 'Sem acerto anterior no sistema'}>
                                {saldoAnterior.saldo == null ? 'sem valor' : num(saldoAnterior.saldo)}
                              </span>
                            ) : (
                              <input value={p.valor} inputMode="decimal" placeholder={esperando ? 'aguardando' : '0,00'} aria-label={`Valor do pagamento ${nome}`}
                                     onChange={e => mudarPagamento(p.chave, { valor: e.target.value })}
                                     onBlur={() => p.valor.trim() && mudarPagamento(p.chave, { valor: num(lerNum(p.valor)) })}
                                     className={`${celula} text-right tabular-nums`} />
                            )}
                          </td>
                          <td className="px-1 py-0.5">
                            {auto ? (
                              <span className="block px-2 py-1 text-sm text-tinta-800 whitespace-nowrap">
                                {saldoAnterior.numero ? `automático · do acerto ${saldoAnterior.numero}` : 'automático'}
                              </span>
                            ) : (
                              <input value={p.observacao} list="acerto-observacoes" placeholder="cliente ou texto livre" aria-label={`Observação do pagamento ${nome}`}
                                     onChange={e => mudarPagamento(p.chave, { observacao: e.target.value })} className={celula} />
                            )}
                          </td>
                          <td className="px-1 py-0.5">
                            <input value={p.numero} aria-label={`Número do pagamento ${i + 1}`} onChange={e => mudarPagamento(p.chave, { numero: e.target.value })} className={`${celula} tabular-nums`} />
                          </td>
                          </>)}
                          <td className="px-0.5 py-0.5 text-center">
                            <BotaoIcone icone={Trash2} perigo rotulo={`${p.doArquivo ? 'Excluir' : 'Tirar'} o pagamento ${nome}`} onClick={() => excluirPagamento(p, `boleto ${p.numero.trim()}`)} />
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                  <tfoot className="border-t-2 border-gray-200 text-gray-900 tabular-nums">
                    <tr>
                      <td className="px-2 py-2.5 font-semibold" colSpan={2}>Total de depósitos e pagamentos</td>
                      <td className="px-2 py-2.5 text-right font-semibold">{num(totais.pagamentos)}</td>
                      <td colSpan={3} />
                    </tr>
                  </tfoot>
                </table>
              </div>
              <div className="md:hidden px-4 py-3 border-t-2 border-gray-200 flex items-baseline justify-between gap-3 text-sm tabular-nums">
                <span className="text-gray-600">Total de depósitos e pagamentos</span>
                <span className="font-semibold text-gray-900">{num(totais.pagamentos)}</span>
              </div>
            </>
          )}
        </section>
      </div>

      {/* O pé da planilha: o saldo é o número que importa, por último e maior */}
      <dl className="grid grid-cols-1 sm:grid-cols-3 gap-px bg-gray-200 rounded-xl border border-gray-200 overflow-hidden [&>div]:px-4 [&>div]:py-3">
        <div className="bg-white">
          <dt className="text-xs font-medium text-gray-500">Total dos romaneios (a pagar)</dt>
          <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums">{formatBRL(totais.aPagar)}</dd>
        </div>
        <div className="bg-white">
          <dt className="text-xs font-medium text-gray-500">Total de depósitos e pagamentos</dt>
          <dd className="font-display text-xl font-semibold text-gray-900 tabular-nums">{formatBRL(totais.pagamentos)}</dd>
        </div>
        <div className="bg-gray-50">
          <dt className="text-xs font-medium text-gray-500">Créditos e débitos com Livia</dt>
          <dd className={`font-display text-2xl font-semibold tabular-nums leading-tight ${corSaldo(totais.saldo)}`}>
            {formatBRL(totais.saldo)}
            <span className="ml-2 font-sans text-sm font-medium">{totais.saldo > 0.004 ? 'crédito' : totais.saldo < -0.004 ? 'débito' : 'zerado'}</span>
          </dd>
        </div>
      </dl>

      {/* Romaneios da fazenda sem funrural: volumes para embalar, com o controle de volumes de cada um */}
      {acerto.romaneiosEmbalar.length > 0 && (
        <section aria-labelledby="acerto-embalar" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
          <CabecalhoBloco id="acerto-embalar" titulo="Volumes para embalar" detalhe="Romaneios da fazenda sem funrural. Viram entrada nos volumes embalados." />
          <ul className="divide-y divide-gray-100">
            {acerto.romaneiosEmbalar.map(r => (
              <li key={r.numero} className="px-4 py-3 space-y-0.5">
                <LinhaRomaneio r={r} />
                <p className="text-sm"><SituacaoRomaneio r={r} /></p>
              </li>
            ))}
          </ul>
        </section>
      )}

      {/* Arquivos */}
      <section aria-labelledby="acerto-arquivos" className="bg-white rounded-xl border border-gray-200 overflow-hidden">
        <CabecalhoBloco id="acerto-arquivos" titulo="Arquivos guardados" detalhe={`${acerto.arquivos.length} ${acerto.arquivos.length === 1 ? 'arquivo' : 'arquivos'}`} />
        <div className="px-4 pt-3">
          <AreaSoltar compacta aceita={['.pdf']} onArquivos={subir} icone={Upload}
                      titulo="Arraste mais PDFs deste acerto" textoBotao="Subir mais PDFs"
                      dica={alterado ? 'Salve ou descarte as alterações antes de subir mais PDFs.' : undefined}
                      desabilitado={alterado} carregando={subindo} textoCarregando="Lendo os PDFs..." />
        </div>
        {lidosAgora.length > 0 && (
          <div className="px-4 pt-3">
            <ArquivosLidos itens={lidosAgora} esperando={pendentes?.itens.map(i => i.arquivo) ?? []} onFechar={() => setLidosAgora([])} />
          </div>
        )}
        {pendentes && (
          <JanelaSubstituir itens={pendentes.itens} enviando={subindo}
                            onCancelar={() => setPendentes(null)}
                            onSubstituir={() => subir(pendentes.arquivos, true)} />
        )}
        {acerto.arquivos.length === 0 ? (
          <Vazio compacto titulo="Nenhum arquivo guardado" />
        ) : (
          <ul className="divide-y divide-gray-100 mt-3 border-t border-gray-100">
            {acerto.arquivos.map(a => {
              const Icone = a.tipo === 'PLANILHA' ? FileSpreadsheet : FileText
              return (
                <li key={a.id} className="pl-4 pr-2 py-1.5 flex items-center gap-3">
                  <Icone size={17} className="text-gray-400 shrink-0" aria-hidden />
                  <div className="min-w-0 flex-1 py-1">
                    <p className="text-sm text-gray-900 break-all">{a.nome}</p>
                    <p className={`text-sm ${a.tipo === 'OUTRO' ? 'text-amber-800 font-medium' : 'text-gray-500'}`}>
                      {TIPO_ARQUIVO[a.tipo] ?? a.tipo} <span className="text-gray-500 font-normal tabular-nums">· {tamanho(a.tamanho)}</span>
                    </p>
                  </div>
                  <BotaoIcone icone={Download} rotulo={`Baixar ${a.nome}`} onClick={() => baixarArquivo(a)} />
                  <BotaoIcone icone={Trash2} perigo rotulo={`Remover ${a.nome}`} onClick={() => removerArquivo(a)} />
                </li>
              )
            })}
          </ul>
        )}
      </section>

      {pdfsGuardados > 0 && (
        <>
          <ConferenciaSemana dados={conferencia} carregando={conferindo} />
          {!conferindo && !conferencia && (
            <Botao variante="secundario" icone={RotateCcw} onClick={conferir}>Conferir a semana de novo</Botao>
          )}
          <p className="text-sm text-gray-500">
            A conferência lê os PDFs guardados, da semana de {dataBr(acerto.semana)}; as edições da planilha não entram nela.
          </p>
        </>
      )}
    </div>
  )
}

function CabecalhoBloco({ id, titulo, detalhe, acao }: { id: string; titulo: string; detalhe?: string; acao?: ReactNode }) {
  return (
    <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2 min-h-[3.25rem]">
      <div className="min-w-0 py-0.5">
        <h3 id={id} className="font-semibold text-gray-900 leading-tight">{titulo}</h3>
        {detalhe && <p className="text-sm text-gray-500">{detalhe}</p>}
      </div>
      {acao && <div className="shrink-0">{acao}</div>}
    </div>
  )
}

// Rótulo pequeno em cima do campo, dentro do cartão do celular. É um <label>
// em volta do campo: tocar no rótulo abre o campo e o leitor de tela lê o nome.
function CampoCartao({ rotulo, children }: { rotulo: string; children: ReactNode }) {
  return (
    <label className="block min-w-0">
      <span className="block text-sm font-medium text-gray-700 mb-1 truncate">{rotulo}</span>
      {children}
    </label>
  )
}

// Célula só de leitura (linha que veio do arquivo), com a mesma altura do campo
function Fixo({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <span className={`block px-2 py-1 min-h-9 leading-7 text-sm text-gray-800 tabular-nums ${className}`}>{children}</span>
}
// Dado que o arquivo não trouxe: avisa em vez de deixar em branco (UI-04)
const faltou = (t: string, texto: string) => (t.trim() ? t : <span className="text-amber-800 font-medium">{texto}</span>)

// Selo da linha que veio do arquivo: não se edita, só se exclui
function SeloDoArquivo({ detalhe }: { detalhe?: string }) {
  return (
    <span className="inline-flex items-center gap-1 text-xs text-gray-500" title="Veio do arquivo: não se edita, só se exclui">
      <Lock size={12} aria-hidden /> Veio do arquivo{detalhe ? `; ${detalhe}` : ''}
    </span>
  )
}

/**
 * O que aconteceu com cada PDF do último envio. Situação CONFIRMAR: "esperando sua
 * escolha" enquanto a janela está aberta; fechada sem substituir, "cancelado".
 */
export function ArquivosLidos({ itens, esperando, onFechar }: {
  itens: { arquivo: string; situacao: SituacaoArquivo; avisos: string[] }[]
  esperando: string[]
  onFechar: () => void
}) {
  return (
    <div className="rounded-lg border border-gray-200 bg-gray-50 pl-3 pr-1 py-2 flex items-start gap-2 animate-entrada">
      <ul className="flex-1 min-w-0 space-y-1.5 py-1">
        {itens.map(i => {
          const cancelado = i.situacao === 'CONFIRMAR' && !esperando.includes(i.arquivo)
          const atencao = i.situacao === 'CONFIRMAR' || i.situacao === 'FORA' || i.situacao === 'ILEGIVEL'
          // A situação já diz "já estava": não repete o aviso igual
          const avisos = i.situacao === 'IGUAL' ? i.avisos.filter(a => !a.startsWith('Já estava')) : i.avisos
          return (
            <li key={i.arquivo} className="text-sm">
              <span className="font-medium text-gray-900 break-all">{i.arquivo}</span>:{' '}
              <span className={atencao && !cancelado ? 'font-medium text-amber-800' : 'text-gray-700'}>
                {cancelado ? ROTULO_CANCELADO : ROTULO_SITUACAO[i.situacao] ?? i.situacao}
              </span>
              {avisos.map(a => <span key={a} className="block text-amber-800">{a}</span>)}
            </li>
          )
        })}
      </ul>
      <BotaoIcone icone={X} rotulo="Fechar a lista" onClick={onFechar} />
    </div>
  )
}

/**
 * Documento que já está no sistema (mesmo número de romaneio ou nosso número do boleto)
 * e veio diferente no arquivo novo: mostra o que muda e pergunta Cancelar ou Substituir
 * (ACE-11, UI-21). Nada destes arquivos foi gravado até ele escolher Substituir.
 */
export function JanelaSubstituir({ itens, enviando, onCancelar, onSubstituir }: {
  itens: { arquivo: string; diferencas: DiferencaDocumento[] }[]
  enviando: boolean
  onCancelar: () => void
  onSubstituir: () => void
}) {
  const varios = itens.length > 1
  return (
    <Modal titulo={varios ? 'Documentos que já estão no sistema' : 'Documento que já está no sistema'} onClose={onCancelar} largura="lg"
           descricao={varios
             ? 'Estes arquivos trazem documentos com o mesmo número de outros já lançados, mas com diferenças. Veja o que muda e escolha.'
             : 'Este arquivo traz um documento com o mesmo número de outro já lançado, mas com diferenças. Veja o que muda e escolha.'}
           rodape={
             <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
               <Botao variante="secundario" onClick={onCancelar} disabled={enviando} data-foco-inicial>Cancelar</Botao>
               <Botao variante="primario" onClick={onSubstituir} carregando={enviando} textoCarregando="Substituindo...">Substituir</Botao>
             </div>
           }>
      <div className="space-y-3">
        {itens.map(i => {
          // Resumo: cada documento com os campos que mudam
          const porDocumento = new Map<string, DiferencaDocumento[]>()
          i.diferencas.forEach(d => porDocumento.set(d.documento, [...(porDocumento.get(d.documento) ?? []), d]))
          return (
            <div key={i.arquivo} className="border border-gray-200 rounded-lg p-3 space-y-1.5">
              <p className="font-medium text-gray-900 break-all">{i.arquivo}</p>
              <ul className="text-sm text-gray-800 space-y-0.5">
                {[...porDocumento].map(([doc, difs]) => (
                  <li key={doc}><span className="font-medium">{doc}</span>: muda {difs.map(d => d.campo.toLowerCase()).join(', ')}</li>
                ))}
              </ul>
              <details className="text-sm">
                <summary className="cursor-pointer text-tinta-700 font-medium py-2 -my-2 sm:py-0 sm:my-0">Ver detalhes</summary>
                <ul className="mt-2 space-y-2">
                  {i.diferencas.map((d, k) => (
                    <li key={k} className="grid grid-cols-1 sm:grid-cols-[minmax(8rem,auto)_1fr_1fr] gap-x-3 gap-y-0.5 tabular-nums">
                      <span className="font-medium text-gray-900">{d.documento} · {d.campo}</span>
                      <span className="text-gray-700"><span className="text-gray-500">No sistema:</span> {d.atual}</span>
                      <span className="text-gray-900"><span className="text-gray-500">No arquivo:</span> {d.novo}</span>
                    </li>
                  ))}
                </ul>
              </details>
            </div>
          )
        })}
        <p className="text-sm text-gray-600">
          Substituir troca o documento inteiro pelo do arquivo, no mesmo lugar. O desconto por caixa digitado continua.
        </p>
      </div>
    </Modal>
  )
}

// Acima dos pagamentos: de onde vem o SALDO ANT. e como trocar entre automático e à mão
function SaldoAnteriorAviso({ saldo, linha, automatico, onAMao, onAutomatico, onIncluir }: {
  saldo: AcertoDetalhe['saldoAnterior']
  linha: LinhaPagamento | undefined
  automatico: boolean
  onAMao: () => void
  onAutomatico: () => void
  onIncluir: () => void
}) {
  const origem = saldo.numero != null && saldo.saldo != null
    ? <>saldo final do acerto {saldo.numero}{saldo.semana ? ` (${dataBr(saldo.semana)})` : ''}: <b className="tabular-nums">{formatBRL(saldo.saldo)}</b></>
    : null
  const faixa = (tom: 'neutro' | 'tinta' | 'atencao', texto: ReactNode, botao?: ReactNode) => (
    <div className={`px-4 py-2 border-b flex flex-wrap items-center gap-x-3 gap-y-1 text-sm ${
      tom === 'atencao' ? 'border-amber-200 bg-amber-50 text-amber-900'
        : tom === 'tinta' ? 'border-tinta-100 bg-tinta-50 text-gray-800' : 'border-gray-100 bg-gray-50 text-gray-700'}`}>
      <span className="flex-1 min-w-[12rem]">{texto}</span>
      {botao}
    </div>
  )

  if (!linha) {
    if (!origem) return null
    return faixa('neutro', <>Sem linha de saldo anterior. O {origem}.</>,
      <Botao variante="fantasma" tamanho="sm" onClick={onIncluir}>Incluir saldo anterior</Botao>)
  }
  if (automatico) {
    return origem
      ? faixa('tinta', <>Saldo anterior automático, {origem}. Muda sozinho se aquele acerto for corrigido.</>,
          <Botao variante="fantasma" tamanho="sm" onClick={onAMao}>Digitar à mão</Botao>)
      : faixa('atencao', <span className="inline-flex gap-1.5"><TriangleAlert size={15} className="mt-0.5 shrink-0" aria-hidden />Não há acerto anterior no sistema: o saldo anterior ficou sem valor.</span>,
          <Botao variante="fantasma" tamanho="sm" onClick={onAMao}>Digitar à mão</Botao>)
  }
  const digitado = lerNum(linha.valor)
  const diferente = saldo.saldo != null && Math.abs(digitado - saldo.saldo) > 0.004
  return faixa(diferente ? 'atencao' : 'neutro',
    <>Saldo anterior digitado à mão.{origem && <> O {origem}{diferente ? ', diferente do digitado' : ''}.</>}</>,
    origem ? <Botao variante="fantasma" tamanho="sm" onClick={onAutomatico}>Voltar para automático</Botao> : undefined)
}
