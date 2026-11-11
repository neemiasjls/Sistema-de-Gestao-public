import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import Campo from '../components/ui/Campo'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { classeCampo } from '../components/EstoquePecas'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import {
  CheckCircle2, ChevronDown, Copy, Download, FileArchive, FileText, FileUp, FlaskConical, History,
  Mail, Paperclip, RefreshCw, Send, TriangleAlert, Upload, X, XCircle,
} from 'lucide-react'
import {
  emailsClienteApi, erroEmail, jaEnviado,
  type EnvioEmail, type PreviaEmail, type PreviaEmails, type ResultadoEnvioEmail,
} from '../services/emailsClienteApi'
import { boletosContasApi, type ItemImportacaoReceber } from '../services/boletosContasApi'
import { brl, dataBr } from '../utils/formato'
import { Modal } from '../components/ModalBase'
import SecaoRecolhivel from '../components/SecaoRecolhivel'
import { useConfirmar } from '../components/ui/Confirmar'
import ComparaBoletos from '../components/ComparaBoletos'

// Toda terça vai um e-mail para a Cedro Frutas (CD) e outro para o Dunas, com
// um .zip do romaneio e dos boletos; a cada carga da Girassol, o boleto do frete
// em PDF direto, com assunto e texto do cálculo do frete Perfil (.txt). A tela junta
// as peças que o sistema já guarda, mostra o que falta e só manda depois de confirmar.
//
// Organização (BOL-07): data no topo, os boletos emitidos podem ser arrastados
// aqui mesmo, um cartão por cliente com o estado dele (pronto, falta algo, já
// enviado), ações no rodapé do cartão e o histórico recolhido no fim.

type Rascunho = { para: string; assunto: string; corpo: string; chaves: string[] }

const rascunhoDe = (e: PreviaEmail): Rascunho => ({
  para: e.destinatarios.join('\n'),
  assunto: e.assunto,
  corpo: e.corpo,
  chaves: e.arquivos.filter(a => a.incluir).map(a => a.chave),
})

const enderecos = (t: string) =>
  [...new Set(t.split(/[;,\s]+/).map(s => s.trim()).filter(Boolean))]

const tamanho = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB`
    : `${Math.max(1, Math.round(bytes / 1024))} KB`

// "CD.zip (40 KB): a, b" ou, sem .zip (Perfil), só os anexos
const anexosDe = (h: EnvioEmail) =>
  h.nomeZip ? `${h.nomeZip} (${tamanho(h.tamanhoZip)}): ${h.anexos.join(', ')}` : `${h.anexos.join(', ')} (${tamanho(h.tamanhoZip)})`

const quando = (iso: string) => new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })

const rotuloClienteBoleto: Record<string, string> = { CD: 'CD (Cedro Frutas)', GIRASSOL: 'Girassol (Horacio)' }

// Estado de um cartão, do mais importante para o menos
type Estado = { tipo: 'enviado' | 'falta' | 'vazio' | 'pronto'; texto: string }

// Envio feito antes de o boleto ser substituído foi com o boleto velho: não conta
const enviosValidos = (e: PreviaEmail) =>
  e.boletoTrocadoEm ? e.envios.filter(x => Date.parse(x.enviadoEm) > Date.parse(e.boletoTrocadoEm!)) : e.envios

function estadoDe(e: PreviaEmail, escolhidos: number): Estado {
  const enviado = enviosValidos(e).find(x => !x.erro)
  if (enviado) return { tipo: 'enviado', texto: `Enviado ${quando(enviado.enviadoEm)}${enviado.modoTeste ? ' (teste)' : ''}` }
  if (e.arquivos.length === 0 && !e.frete) return { tipo: 'vazio', texto: 'Nada desta carga' }
  if (e.faltando.length > 0) return { tipo: 'falta', texto: e.faltando.length === 1 ? 'Falta 1 item' : `Faltam ${e.faltando.length} itens` }
  if (escolhidos === 0) return { tipo: 'falta', texto: 'Sem anexo escolhido' }
  return { tipo: 'pronto', texto: e.boletoTrocadoEm ? 'Boleto substituído: enviar de novo' : 'Pronto para enviar' }
}

const corEstado: Record<Estado['tipo'], string> = {
  enviado: 'bg-emerald-50 text-emerald-800 border-emerald-200',
  falta:   'bg-red-50 text-red-800 border-red-200',
  vazio:   'bg-gray-100 text-gray-700 border-gray-200',
  pronto:  'bg-gema-100 text-gema-900 border-gema-300',
}

function SeloEstado({ estado }: { estado: Estado }) {
  const Icone = estado.tipo === 'enviado' ? CheckCircle2 : estado.tipo === 'falta' ? TriangleAlert : estado.tipo === 'pronto' ? Send : Mail
  return (
    <span className={`inline-flex items-center gap-1 text-xs font-medium px-2 py-0.5 rounded-full border whitespace-nowrap ${corEstado[estado.tipo]}`}>
      <Icone size={12} aria-hidden /> {estado.texto}
    </span>
  )
}

export default function EmailsClientePage() {
  const [previa, setPrevia] = useState<PreviaEmails | null>(null)
  const [data, setData] = useState('')
  const [carregando, setCarregando] = useState(false)
  const [falhou, setFalhou] = useState(false)
  const [rascunhos, setRascunhos] = useState<Record<string, Rascunho>>({})
  const [historico, setHistorico] = useState<EnvioEmail[]>([])
  const [confirmar, setConfirmar] = useState<{ email: PreviaEmail; reenviar: boolean; aviso?: string } | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [resultados, setResultados] = useState<Record<string, ResultadoEnvioEmail>>({})
  const [encolhido, setEncolhido] = useState<Record<string, boolean>>({})
  const semDadosDe = (e: PreviaEmail) => e.arquivos.length === 0 && !e.frete && e.envios.length === 0
  const pendentesDe = (e: PreviaEmail) => previa?.faltaEnviar?.find(f => f.cliente === e.cliente)?.dias ?? []
  // Já enviado e sem outra carga pendente: nasce encolhido (Admin, 04/11/2026)
  const resolvido = (e: PreviaEmail) => estadoDe(e, escolhidosDe(e).length).tipo === 'enviado' && pendentesDe(e).length === 0
  const estaEncolhido = (e: PreviaEmail) => encolhido[e.cliente] ?? resolvido(e)
  // O que falta importar ou enviar vem primeiro; o já enviado vai para o fim
  const ordem = (e: PreviaEmail) => {
    const t = estadoDe(e, escolhidosDe(e).length).tipo
    return t === 'falta' || t === 'pronto' ? 0 : resolvido(e) ? 2 : 1
  }
  const [subindo, setSubindo] = useState(false)
  // O que o cálculo do frete Perfil subido avisou: fica até fechar (D-07)
  const [avisosRelatorio, setAvisosRelatorio] = useState<string[]>([])
  // Boletos emitidos (CD, Girassol) arrastados aqui
  const [subindoBoletos, setSubindoBoletos] = useState(false)
  const perguntar = useConfirmar()
  const [reconhecidos, setReconhecidos] = useState<ItemImportacaoReceber[] | null>(null)
  const entradaRelatorio = useRef<HTMLInputElement>(null)

  // O que está na tela agora, para recarregar a prévia sem perder o que foi editado
  const atual = useRef<{ previa: PreviaEmails | null; rascunhos: Record<string, Rascunho> }>({ previa: null, rascunhos: {} })
  atual.current = { previa, rascunhos }

  const carregar = useCallback(async (dia?: string, manterEdicao = false) => {
    setCarregando(true)
    try {
      const [{ data: p }, { data: h }] = await Promise.all([emailsClienteApi.previa(dia), emailsClienteApi.envios()])
      const antes = atual.current
      setRascunhos(Object.fromEntries(p.emails.map(e => {
        const novo = rascunhoDe(e)
        const velho = antes.rascunhos[e.cliente]
        const original = antes.previa?.emails.find(x => x.cliente === e.cliente)
        // Mesma data e o texto foi mexido: fica o que foi digitado; os anexos novos entram marcados
        if (manterEdicao && velho && original && antes.previa?.data === p.data) {
          const base = rascunhoDe(original)
          const existe = new Set(e.arquivos.map(a => a.chave))
          return [e.cliente, {
            para: velho.para !== base.para ? velho.para : novo.para,
            assunto: velho.assunto !== base.assunto ? velho.assunto : novo.assunto,
            corpo: velho.corpo !== base.corpo ? velho.corpo : novo.corpo,
            chaves: [...new Set([...velho.chaves.filter(c => existe.has(c)), ...novo.chaves.filter(c => !base.chaves.includes(c))])],
          }]
        }
        return [e.cliente, novo]
      })))
      setPrevia(p)
      setData(p.data)
      setResultados({})
      setEncolhido({})
      setHistorico(h)
      setFalhou(false)
    } catch (e) {
      setFalhou(true)
      toast.error(await erroEmail(e, 'Não consegui montar a prévia dos e-mails.'))
    } finally { setCarregando(false) }
  }, [])

  useEffect(() => { carregar() }, [carregar])

  const mudar = (cliente: string, parte: Partial<Rascunho>) =>
    setRascunhos(r => ({ ...r, [cliente]: { ...r[cliente], ...parte } }))

  const alternar = (cliente: string, chave: string) => {
    const atuais = rascunhos[cliente]?.chaves ?? []
    mudar(cliente, { chaves: atuais.includes(chave) ? atuais.filter(c => c !== chave) : [...atuais, chave] })
  }

  const salvarPadrao = async (e: PreviaEmail) => {
    const lista = enderecos(rascunhos[e.cliente].para)
    try {
      const { data: r } = await emailsClienteApi.salvarDestinatarios(e.cliente, lista)
      mudar(e.cliente, { para: r.destinatarios.join('\n') })
      toast.success(`Destinatários de ${e.rotulo} guardados.`)
    } catch (err) { toast.error(await erroEmail(err, 'Não consegui guardar os destinatários.')) }
  }

  const baixarZip = async (e: PreviaEmail) => {
    try { await emailsClienteApi.baixarZip(e.cliente, data, rascunhos[e.cliente].chaves, e.nomeZip) }
    catch (err) { toast.error(await erroEmail(err, 'Não consegui montar o .zip.')) }
  }

  // Cálculo do frete Perfil (.txt) que não foi gerado pelo sistema: guarda e remonta a prévia na data dele
  const subirRelatorio = async (arquivo: File | undefined) => {
    if (!arquivo) return
    setSubindo(true)
    try {
      const { data: r } = await emailsClienteApi.subirRelatorioFrete(arquivo)
      toast.success(`Relatório ${r.nome} guardado.`)
      setAvisosRelatorio(r.avisos)
      await carregar(r.data)
    } catch (err) { toast.error(await erroEmail(err, 'Não consegui guardar o cálculo do frete Perfil.')) }
    finally { setSubindo(false) }
  }

  // Boletos que a transportadora emite (CD, Girassol): vão para Boletos a receber,
  // pelo mesmo caminho da tela Boletos. Boleto e romaneio da fazenda voltam
  // recusados (têm o caminho deles no Acerto Alfa). Depois a prévia é remontada
  // na data da carga do boleto, para ele já aparecer como anexo.
  const subirBoletos = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setSubindoBoletos(true)
    try {
      const { data: r } = await boletosContasApi.importarAReceberDaTransportadora(arquivos)
      let itens = r.itens ?? []
      let guardados = r.novos + r.completados
      // Boleto que já existe: pergunta se troca pelo subido agora ou deixa como está
      const jaExistem = itens.filter(i => i.situacao === 'REPETIDO')
      if (jaExistem.length > 0 && await perguntar({
        titulo: jaExistem.length === 1 ? 'Esse boleto já existe' : `${jaExistem.length} boletos já existem`,
        texto: <>
          <ComparaBoletos itens={jaExistem} />
          <p className="mt-3">Substituir troca o boleto anterior pelo que você subiu agora. Cancelar deixa o anterior como está.</p>
        </>,
        confirmar: 'Substituir',
        cancelar: 'Cancelar',
        perigo: false,
      })) {
        const nomes = new Set(jaExistem.map(i => i.arquivo))
        const { data: r2 } = await boletosContasApi.importarAReceberDaTransportadora(arquivos.filter(f => nomes.has(f.name)), true)
        const trocados = new Map((r2.itens ?? []).map(i => [i.arquivo, i]))
        itens = itens.map(i => trocados.get(i.arquivo) ?? i)
        guardados += r2.substituidos ?? 0
      }
      setReconhecidos(itens)
      if (guardados > 0) toast.success(guardados === 1 ? '1 boleto guardado.' : `${guardados} boletos guardados.`)
      else toast.error('Nenhum boleto novo guardado. Veja a lista.')
      const datas = [...new Set(itens.filter(i => i.situacao !== 'RECUSADO' && i.dataCarga).map(i => i.dataCarga!))]
      const destino = datas.length === 1 ? datas[0] : data || undefined
      await carregar(destino, destino === data)
    } catch (err) { toast.error(await erroEmail(err, 'Não consegui guardar os boletos.')) }
    finally { setSubindoBoletos(false) }
  }

  const botaoRelatorio = (
    <Botao variante="secundario" tamanho="sm" icone={Upload} carregando={subindo} textoCarregando="Guardando..."
           onClick={() => entradaRelatorio.current?.click()}>
      Subir cálculo do frete Perfil (.txt)
    </Botao>
  )

  const abrirConfirmacao = (e: PreviaEmail) => {
    const r = rascunhos[e.cliente]
    if (enderecos(r.para).length === 0) { toast.error('Informe pelo menos um destinatário.'); return }
    if (r.chaves.length === 0 && !e.semBoleto) { toast.error('Escolha pelo menos um arquivo.'); return }
    if (!r.assunto.trim()) { toast.error('Informe o assunto.'); return }
    const jaFoi = enviosValidos(e).find(x => !x.erro && x.modoTeste === previa?.modoTeste)
    setConfirmar({
      email: e, reenviar: !!jaFoi,
      aviso: jaFoi ? `Este e-mail já foi enviado em ${quando(jaFoi.enviadoEm)}${jaFoi.modoTeste ? ' (teste)' : ''}. Confirmar manda de novo.` : undefined,
    })
  }

  const enviar = async () => {
    if (!confirmar) return
    const { email: e, reenviar } = confirmar
    const r = rascunhos[e.cliente]
    setEnviando(true)
    try {
      const { data: res } = await emailsClienteApi.enviar({
        cliente: e.cliente, data, destinatarios: enderecos(r.para), assunto: r.assunto, corpo: r.corpo,
        chaves: r.chaves, reenviar,
      })
      setResultados(x => ({ ...x, [e.cliente]: res }))
      if (res.enviado) toast.success(res.modoTeste ? `E-mail de teste enviado para ${res.enviadoPara.join(', ')}.` : `E-mail ${e.rotulo} enviado.`)
      else toast.error(res.erro ?? 'O e-mail não foi enviado.')
      setConfirmar(null)
      const [{ data: p }, { data: h }] = await Promise.all([emailsClienteApi.previa(data), emailsClienteApi.envios()])
      // Mantém o que foi editado; só atualiza o histórico de cada cliente
      setPrevia(p)
      setHistorico(h)
    } catch (err) {
      if (jaEnviado(err)) {
        setConfirmar({ email: e, reenviar: true, aviso: await erroEmail(err, 'Este e-mail já foi enviado.') })
      } else {
        toast.error(await erroEmail(err, 'Não consegui enviar o e-mail.'))
        setConfirmar(null)
      }
    } finally { setEnviando(false) }
  }

  const datas = previa ? [...new Set([previa.data, ...previa.datas])].sort().reverse() : []
  const escolhidosDe = (e: PreviaEmail) => e.arquivos.filter(a => rascunhos[e.cliente]?.chaves.includes(a.chave))
  const emailsEmOrdem = previa ? [...previa.emails].sort((a, b) => ordem(a) - ordem(b)) : []

  return (
    <div className="space-y-5 max-w-5xl">
      <CabecalhoTela descricao="O da semana da Cedro Frutas e do Dunas (romaneio, boletos e nota num .zip) e o do frete da Girassol (o boleto do frete). Confira a prévia de cada um antes de enviar." />
      <input ref={entradaRelatorio} type="file" accept=".txt" hidden tabIndex={-1}
             onChange={x => { subirRelatorio(x.target.files?.[0]); x.target.value = '' }} />

      {previa?.modoTeste && (
        <div role="status" className="flex items-start gap-3 rounded-xl border-2 border-amber-400 bg-amber-50 px-4 py-3">
          <FlaskConical size={22} className="text-amber-700 shrink-0 mt-0.5" aria-hidden />
          <div>
            <p className="font-semibold text-amber-950">Modo de teste: tudo vai para {previa.destinoTeste}</p>
            <p className="text-sm text-amber-900">
              Nenhum cliente recebe nada. O e-mail sai com "[TESTE]" no assunto e diz no corpo para quem iria.
              Para liberar o envio aos clientes, tire a variável Email__DestinoTeste na Render.
            </p>
          </div>
        </div>
      )}
      {previa && !previa.configurado && (
        <Aviso tipo="erro" titulo="Envio de e-mail não configurado no servidor">
          Falta a chave do Brevo ou o remetente. A prévia funciona, mas o botão Enviar vai falhar.
        </Aviso>
      )}
      {avisosRelatorio.length > 0 && (
        <Aviso tipo="atencao" titulo="Cálculo do frete Perfil guardado. Confira" itens={avisosRelatorio} onFechar={() => setAvisosRelatorio([])} />
      )}

      {/* Data da carga: tudo abaixo é desta data */}
      <section aria-label="Data da carga" className="bg-white border border-gray-200 rounded-xl p-4 space-y-3">
        <div className="grid grid-cols-2 sm:flex sm:flex-wrap sm:items-end gap-3">
          <Campo rotulo="Data da carga" className="min-w-0 sm:w-44">
            <select value={data} onChange={e => carregar(e.target.value)} disabled={carregando} className={classeCampo}>
              {datas.map(d => <option key={d} value={d}>{dataBr(d)}</option>)}
            </select>
          </Campo>
          <Campo rotulo="Outra data" className="min-w-0 sm:w-44">
            <input type="date" value={data} onChange={e => e.target.value && carregar(e.target.value)} disabled={carregando} className={classeCampo} />
          </Campo>
          <Botao variante="secundario" icone={RefreshCw} onClick={() => carregar(data || undefined)} disabled={carregando}
                 className={`col-span-2 sm:col-span-1 ${carregando ? '[&>svg]:animate-spin' : ''}`}>
            Atualizar prévia
          </Botao>
        </div>

        {/* Os três e-mails num relance; tocar leva ao cartão */}
        {previa && !previa.emails.some(e => !semDadosDe(e)) && (
          <p className="pt-3 border-t border-gray-100 text-sm text-gray-600">Não existe e-mail pendente nesta carga.</p>
        )}
        {previa && previa.emails.some(e => !semDadosDe(e)) && (
          <ul className="flex flex-wrap gap-2 pt-3 border-t border-gray-100">
            {emailsEmOrdem.filter(e => !semDadosDe(e)).map(e => (
              <li key={e.cliente}>
                <a href={`#email-${e.cliente}`}
                   onClick={ev => {
                     ev.preventDefault()
                     setEncolhido(m => ({ ...m, [e.cliente]: false }))
                     window.setTimeout(() => document.getElementById(`email-${e.cliente}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 30)
                   }}
                   className="inline-flex items-center gap-2 min-h-11 sm:min-h-9 pl-3 pr-1.5 rounded-lg border border-gray-200 hover:bg-gray-50 text-sm">
                  <span className="font-medium text-gray-900">{e.rotulo}</span>
                  <SeloEstado estado={estadoDe(e, escolhidosDe(e).length)} />
                </a>
              </li>
            ))}
          </ul>
        )}

        {previa && (
          <p className="text-sm text-gray-500 break-all">
            Sai de {previa.remetenteNome} &lt;{previa.remetente || 'não configurado'}&gt;
          </p>
        )}
      </section>

      <section aria-label="Boletos emitidos" className="space-y-1.5">
        <AreaSoltar compacta aceita={['.pdf']} onArquivos={subirBoletos} icone={FileUp} textoBotao="Escolher PDFs"
                    titulo="Boletos da CD e da Girassol: arraste os PDFs aqui"
                    dica="Vão para Boletos a receber e já entram no e-mail da carga"
                    carregando={subindoBoletos} textoCarregando="Lendo os boletos..." />
      </section>
      {reconhecidos && (
        <ListaReconhecidos itens={reconhecidos} onFechar={() => setReconhecidos(null)}
                           onIrPara={d => { setReconhecidos(null); carregar(d) }} dataAtual={data} />
      )}

      {!previa && carregando && <Carregando linhas={3} rotulo="Montando a prévia dos e-mails" />}
      {!previa && !carregando && falhou && (
        <ErroCarga mensagem="Não consegui montar a prévia dos e-mails." onTentar={() => carregar(data || undefined)} />
      )}

      {previa && emailsEmOrdem.map(e => {
        const r = rascunhos[e.cliente]
        if (!r) return null
        const res = resultados[e.cliente]
        const escolhidos = escolhidosDe(e)
        const estado = estadoDe(e, escolhidos.length)
        // Cargas deste cliente ainda sem envio, dentro do próprio card (Admin, 15/10/2026)
        const pendentes = pendentesDe(e)
        const listaPendentes = pendentes.length > 0 && (
          <div className="flex flex-wrap items-center gap-2" role="group" aria-label={`Cargas de ${e.rotulo} sem envio`}>
            <span className="text-sm text-gray-600">Falta enviar:</span>
            {pendentes.map(d => (
              <button key={d.data} type="button" onClick={() => carregar(d.data)} disabled={carregando || d.data === data}
                      title={d.pronto ? 'Pronto para enviar' : d.faltando.join(' ')}
                      aria-current={d.data === data ? 'true' : undefined}
                      className={`inline-flex items-center gap-1.5 min-h-10 sm:min-h-8 px-2.5 rounded-lg border text-sm tabular-nums
                        ${d.data === data ? 'border-tinta-400 bg-tinta-50 text-tinta-900 font-semibold' : 'border-gray-200 hover:bg-gray-50 text-gray-800'}`}>
                {dataBr(d.data).slice(0, 5)}
                <span className={d.pronto ? 'text-emerald-700' : 'text-amber-800'}>{d.pronto ? 'pronto' : 'falta algo'}</span>
              </button>
            ))}
          </div>
        )
        // Nada deste cliente nesta carga: não aparece (Admin, 15/10/2026: "fica poluindo"),
        // a não ser que tenha outra carga pendente, que aparece para abrir
        if (semDadosDe(e)) {
          if (pendentes.length === 0) return null
          return (
            <section key={e.cliente} id={`email-${e.cliente}`}
                     className="scroll-mt-4 bg-white border border-gray-200 rounded-xl px-4 py-3 space-y-2">
              <p className="flex items-center gap-2 font-semibold text-gray-900"><Mail size={18} className="text-gray-400" aria-hidden />{e.rotulo}</p>
              {listaPendentes}
            </section>
          )
        }
        if (estaEncolhido(e)) {
          return (
            <section key={e.cliente} id={`email-${e.cliente}`}
                     className="scroll-mt-4 bg-white border border-gray-200 rounded-xl px-4 py-3 flex flex-wrap items-center gap-2">
              <Mail size={18} className="text-gray-400" aria-hidden />
              <p className="text-sm text-gray-700 flex-1 min-w-[180px] flex flex-wrap items-center gap-2">
                <span className="font-semibold text-gray-900">{e.rotulo}</span>
                <SeloEstado estado={estado} />
              </p>
              <Botao variante="fantasma" tamanho="sm" onClick={() => setEncolhido(m => ({ ...m, [e.cliente]: false }))}>
                Abrir <ChevronDown size={16} aria-hidden />
              </Botao>
            </section>
          )
        }
        const idCartao = `email-${e.cliente}`
        return (
          <section key={e.cliente} id={idCartao} aria-labelledby={`${idCartao}-titulo`}
                   className="scroll-mt-4 bg-white border border-gray-200 rounded-xl overflow-hidden">
            <header className="px-4 py-3 border-b border-gray-100 flex items-start gap-3">
              <span className="w-9 h-9 rounded-lg bg-tinta-50 text-tinta-700 flex items-center justify-center shrink-0" aria-hidden>
                <Mail size={18} />
              </span>
              <div className="flex-1 min-w-0">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <h2 id={`${idCartao}-titulo`} className="text-lg font-semibold text-gray-900 leading-tight">{e.rotulo}</h2>
                  <SeloEstado estado={estado} />
                </div>
                <p className="text-sm text-gray-500 mt-0.5 tabular-nums">
                  {e.ehFrete ? 'Boleto do frete em PDF, sem .zip' : `Anexos num ${e.nomeZip}`} · carga de {dataBr(data)}
                </p>
              </div>
              <Botao variante="fantasma" tamanho="sm" onClick={() => setEncolhido(m => ({ ...m, [e.cliente]: true }))}
                     className="shrink-0 -mr-2">
                Encolher <ChevronDown size={16} className="rotate-180" aria-hidden />
              </Botao>
            </header>
            {listaPendentes && <div className="px-4 pt-3">{listaPendentes}</div>}

            {/* O que falta e o que conferir vem antes de tudo */}
            {(e.faltando.length > 0 || e.avisos.length > 0) && (
              <div className="px-4 pt-4 grid gap-2 sm:grid-cols-2">
                {e.faltando.length > 0 && <Aviso tipo="erro" titulo="Falta" itens={e.faltando} />}
                {e.avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira" itens={e.avisos} />}
              </div>
            )}

            <div className="p-4 grid gap-5 lg:grid-cols-2">
              {/* Mensagem: para, assunto, texto */}
              <div className="space-y-3 min-w-0">
                <div>
                  <div className="flex flex-wrap items-end justify-between gap-x-2 mb-1">
                    <label htmlFor={`email-para-${e.cliente}`} className="text-sm sm:text-xs font-medium text-gray-700 pb-1">Para (um por linha)</label>
                    <div className="flex gap-1 -mr-2">
                      <Botao variante="fantasma" tamanho="sm" onClick={() => mudar(e.cliente, { para: e.destinatariosPadrao.join('\n') })}>Voltar aos de sempre</Botao>
                      <Botao variante="fantasma" tamanho="sm" onClick={() => salvarPadrao(e)} className="text-tinta-700">Guardar como padrão</Botao>
                    </div>
                  </div>
                  <textarea id={`email-para-${e.cliente}`} value={r.para} onChange={x => mudar(e.cliente, { para: x.target.value })} rows={4}
                            className={`${classeCampo} py-2 font-mono`} />
                  {previa.modoTeste && (
                    <p className="text-sm text-amber-800 mt-1">No modo de teste vai só para {previa.destinoTeste}.</p>
                  )}
                </div>
                <Campo rotulo="Assunto">
                  <input value={r.assunto} onChange={x => mudar(e.cliente, { assunto: x.target.value })} className={classeCampo} />
                </Campo>
                <Campo rotulo="Texto">
                  <textarea value={r.corpo} onChange={x => mudar(e.cliente, { corpo: x.target.value })} rows={8} className={`${classeCampo} py-2`} />
                </Campo>
              </div>

              {/* Anexos (e, na Girassol, o cálculo do frete Perfil de onde sai o assunto) */}
              <div className="space-y-3 min-w-0">
                {e.ehFrete && <FreteResumo email={e} botao={botaoRelatorio} />}
                <fieldset>
                  <legend className="w-full flex items-center justify-between gap-2 mb-1">
                    <span className="text-sm sm:text-xs font-medium text-gray-700">{e.ehFrete ? 'Anexos' : `No ${e.nomeZip}`}</span>
                    {e.arquivos.length > 0 && (
                      <span className="text-sm text-gray-500 tabular-nums">{escolhidos.length} de {e.arquivos.length} marcados</span>
                    )}
                  </legend>
                  {e.arquivos.length === 0 ? (
                    <div className="rounded-lg border border-dashed border-amber-300 bg-amber-50/60">
                      <Vazio compacto titulo="Nenhum arquivo encontrado para esta data" />
                    </div>
                  ) : (
                    <ul className="divide-y divide-gray-100 border border-gray-200 rounded-lg">
                      {e.arquivos.map(a => (
                        <li key={a.chave} className="flex items-center gap-2 pl-3 pr-1">
                          <label className="flex items-start gap-3 flex-1 min-w-0 py-2.5 cursor-pointer">
                            <input type="checkbox" checked={r.chaves.includes(a.chave)} onChange={() => alternar(e.cliente, a.chave)}
                                   className="mt-0.5 w-5 h-5 shrink-0 accent-tinta-700" />
                            <span className="min-w-0">
                              <span className="block text-sm font-medium text-gray-900 break-all">{a.nome}</span>
                              <span className="block text-sm text-gray-500">{a.descricao} · <span className="tabular-nums">{tamanho(a.tamanho)}</span></span>
                            </span>
                          </label>
                          <BotaoIcone icone={Download} rotulo={`Baixar ${a.nome} para conferir`}
                                      onClick={() => emailsClienteApi.baixarArquivo(e.cliente, data, a)
                                        .catch(async err => toast.error(await erroEmail(err, 'Não consegui abrir o arquivo.')))} />
                        </li>
                      ))}
                    </ul>
                  )}
                </fieldset>

                {res && (
                  <Aviso tipo={res.enviado ? 'sucesso' : 'erro'}>
                    {res.enviado
                      ? <>Enviado para {res.enviadoPara.join(', ')}{res.modoTeste ? ' (modo de teste)' : ''}, com {res.anexos.length} arquivo(s){e.ehFrete ? '' : ' no .zip'}.</>
                      : <>Não foi enviado: {res.erro}</>}
                    {res.aviso && <p className="mt-1">{res.aviso}</p>}
                  </Aviso>
                )}
              </div>
            </div>

            {/* Ações do cartão */}
            <footer className="px-4 py-3 border-t border-gray-100 bg-gray-50 flex flex-col-reverse sm:flex-row sm:items-center gap-2">
              <p className="text-sm text-gray-600 sm:flex-1 inline-flex items-center gap-1.5 tabular-nums">
                <Paperclip size={14} className="shrink-0" aria-hidden />
                {escolhidos.length === 0
                  ? 'Nenhum arquivo marcado'
                  : `${escolhidos.length} ${escolhidos.length === 1 ? 'arquivo' : 'arquivos'}${e.ehFrete ? '' : ` no ${e.nomeZip}`}`}
              </p>
              {!e.ehFrete && (
                <Botao variante="secundario" icone={FileArchive} onClick={() => baixarZip(e)} disabled={escolhidos.length === 0}>
                  Baixar o .zip
                </Botao>
              )}
              <Botao variante="primario" icone={Send} onClick={() => abrirConfirmacao(e)} disabled={carregando || (escolhidos.length === 0 && !e.semBoleto)}>
                {previa.modoTeste ? 'Enviar teste' : enviosValidos(e).some(x => !x.erro && !x.modoTeste) ? 'Enviar de novo' : 'Enviar'}
              </Botao>
            </footer>
          </section>
        )
      })}

      <SecaoRecolhivel
        titulo="Histórico de envios"
        icone={History}
        descricao={historico.length === 0
          ? 'Nenhum e-mail enviado ainda'
          : `${historico.length} ${historico.length === 1 ? 'envio' : 'envios'}; o último em ${quando(historico[0].enviadoEm)}`}
      >
        {historico.length === 0 ? (
          <Vazio compacto titulo="Nenhum e-mail enviado ainda" />
        ) : (
          <>
            <ul className="sm:hidden -mx-4 divide-y divide-gray-100">
              {historico.map(h => (
                <li key={h.id} className="px-4 py-3 space-y-1 text-sm">
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium text-gray-900">{h.rotulo} · carga {dataBr(h.dataCarga)}</span>
                    <ResultadoEnvio h={h} />
                  </div>
                  <p className="text-gray-500 tabular-nums">{quando(h.enviadoEm)}</p>
                  <p className="text-gray-900 break-words">{h.assunto}</p>
                  <p className="text-gray-600 break-all">{h.destinatarios.join(', ')}</p>
                  {h.modoTeste && <p className="text-amber-800 break-all">Teste: foi para {h.enviadoPara.join(', ')}</p>}
                  <p className="text-gray-600 break-all">{anexosDe(h)}</p>
                  {h.erro && <p className="text-red-800">{h.erro}</p>}
                </li>
              ))}
            </ul>
            <div className="hidden sm:block overflow-x-auto -mx-5">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 text-xs text-gray-500">
                  <tr>
                    <th className="px-3 py-2 text-left font-medium">Enviado em</th>
                    <th className="px-3 py-2 text-left font-medium">Cliente</th>
                    <th className="px-3 py-2 text-left font-medium">Carga</th>
                    <th className="px-3 py-2 text-left font-medium">Assunto</th>
                    <th className="px-3 py-2 text-left font-medium">Destinatários</th>
                    <th className="px-3 py-2 text-left font-medium">Anexos</th>
                    <th className="px-3 py-2 text-left font-medium">Resultado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {historico.map(h => (
                    <tr key={h.id} className="align-top">
                      <td className="px-3 py-2 whitespace-nowrap text-gray-600 tabular-nums">{quando(h.enviadoEm)}</td>
                      <td className="px-3 py-2 whitespace-nowrap">{h.rotulo}</td>
                      <td className="px-3 py-2 whitespace-nowrap tabular-nums">{dataBr(h.dataCarga)}</td>
                      <td className="px-3 py-2 min-w-[240px]">{h.assunto}</td>
                      <td className="px-3 py-2 text-gray-600 min-w-[200px]">
                        {h.destinatarios.join(', ')}
                        {h.modoTeste && <p className="text-amber-800">Teste: foi para {h.enviadoPara.join(', ')}</p>}
                      </td>
                      <td className="px-3 py-2 text-gray-600 min-w-[200px]">{anexosDe(h)}</td>
                      <td className="px-3 py-2 min-w-[140px]">{h.erro ? <span className="text-red-800">{h.erro}</span> : <ResultadoEnvio h={h} />}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </SecaoRecolhivel>

      {confirmar && (() => {
        const e = confirmar.email
        const r = rascunhos[e.cliente]
        const para = enderecos(r.para)
        const nomes = e.arquivos.filter(a => r.chaves.includes(a.chave)).map(a => a.nome)
        return (
          <Modal titulo={`Enviar o e-mail ${e.rotulo}?`} onClose={() => !enviando && setConfirmar(null)}
                 rodape={
                   <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
                     <Botao variante="secundario" onClick={() => setConfirmar(null)} disabled={enviando}>Cancelar</Botao>
                     <Botao variante="primario" icone={Send} onClick={enviar} carregando={enviando} textoCarregando="Enviando...">
                       {confirmar.reenviar ? 'Enviar de novo' : 'Enviar'}
                     </Botao>
                   </div>
                 }>
            {previa?.modoTeste && (
              <p className="rounded-lg border-2 border-amber-400 bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-950">
                Modo de teste: vai só para {previa.destinoTeste}
              </p>
            )}
            {confirmar.aviso && <Aviso tipo="atencao">{confirmar.aviso}</Aviso>}
            <dl className="text-sm space-y-2">
              <div><dt className="text-gray-500">{previa?.modoTeste ? 'Iria para' : 'Para'}</dt><dd className="text-gray-900 break-all">{para.join(', ')}</dd></div>
              <div><dt className="text-gray-500">Assunto</dt><dd className="text-gray-900 break-words">{r.assunto}</dd></div>
              <div>
                <dt className="text-gray-500">{e.ehFrete ? `${nomes.length} anexo(s)` : `${e.nomeZip} com ${nomes.length} arquivo(s)`}</dt>
                <dd><ul className="list-disc pl-5 text-gray-900">{nomes.map(n => <li key={n} className="break-all">{n}</li>)}</ul></dd>
              </div>
            </dl>
            {e.faltando.length > 0 && (
              <Aviso tipo="erro">Ainda falta: {e.faltando.length} item(ns). Vai assim mesmo?</Aviso>
            )}
          </Modal>
        )
      })()}
    </div>
  )
}

function ResultadoEnvio({ h }: { h: EnvioEmail }) {
  return h.erro
    ? <span className="text-sm font-medium text-red-800">Falhou</span>
    : <span className="inline-flex items-center gap-1 text-sm font-medium text-emerald-800"><CheckCircle2 size={14} aria-hidden />{h.modoTeste ? 'Enviado (teste)' : 'Enviado'}</span>
}

// O que cada PDF trouxe e onde ficou (como a lista do Acerto Alfa). Dado que
// não veio do PDF aparece escrito, nunca em branco (UI-04).
function ListaReconhecidos({ itens, onFechar, onIrPara, dataAtual }: {
  itens: ItemImportacaoReceber[]; onFechar: () => void; onIrPara: (data: string) => void; dataAtual: string
}) {
  const recusados = itens.filter(i => i.situacao === 'RECUSADO').length
  const onde: Record<ItemImportacaoReceber['situacao'], string> = {
    NOVO: 'Guardado em Boletos a receber',
    COMPLETADO: 'Completou o boleto que já estava lançado sem PDF',
    SUBSTITUIDO: 'Substituiu o boleto que já estava guardado',
    REPETIDO: 'Já estava guardado: ficou de fora',
    RECUSADO: 'Não guardado',
  }
  const venc = (v: string | null) => (v ? `vence ${dataBr(v)}` : 'vencimento não lido')
  const falta = (t: string): ReactNode => <span className="font-medium text-amber-800">{t}</span>

  return (
    <section className="bg-white rounded-xl border border-gray-200 overflow-hidden animate-entrada">
      <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
        <div className="min-w-0 py-0.5">
          <h3 className="font-semibold text-gray-900">O que foi reconhecido</h3>
          <p className={`text-sm ${recusados ? 'text-red-800 font-medium' : 'text-gray-500'}`}>
            {itens.length} {itens.length === 1 ? 'arquivo' : 'arquivos'}{recusados ? `, ${recusados} fora` : ', todos reconhecidos'}
          </p>
        </div>
        <BotaoIcone icone={X} rotulo="Fechar a lista" onClick={onFechar} />
      </div>
      {itens.length === 0 ? (
        <Vazio compacto titulo="Nenhum arquivo lido" />
      ) : (
        <ul className="divide-y divide-gray-100">
          {itens.map((i, n) => {
            const Icone = i.situacao === 'RECUSADO' ? XCircle : i.situacao === 'REPETIDO' ? Copy : CheckCircle2
            const cor = i.situacao === 'RECUSADO' ? 'text-red-700' : i.situacao === 'REPETIDO' ? 'text-gray-500' : 'text-emerald-700'
            return (
              <li key={`${i.arquivo}-${n}`} className={`px-4 py-3 flex items-start gap-3 ${i.situacao === 'RECUSADO' ? 'bg-red-50/60' : ''}`}>
                <Icone size={17} className={`mt-0.5 shrink-0 ${cor}`} aria-hidden />
                <div className="min-w-0 flex-1 space-y-0.5">
                  <p className="text-sm font-medium text-gray-900 break-all">{i.arquivo}</p>
                  {i.situacao !== 'RECUSADO' && (
                    <p className="text-sm text-gray-700 tabular-nums">
                      {i.cliente ? rotuloClienteBoleto[i.cliente] ?? i.cliente : falta('cliente não lido')}
                      {' · '}{i.valor != null ? brl(i.valor) : falta('valor não lido')}
                      {' · '}{venc(i.vencimento)}
                      {' · '}{i.dataCarga ? `carga ${dataBr(i.dataCarga)}` : falta('sem data da carga')}
                      {i.placa ? ` · ${i.placa}` : ''}
                    </p>
                  )}
                  <p className={`text-sm ${i.situacao === 'RECUSADO' ? 'text-red-800 font-medium' : 'text-gray-600'}`}>
                    {onde[i.situacao]}
                    {i.dataCarga && i.situacao !== 'RECUSADO' && i.dataCarga !== dataAtual && (
                      <> · <button onClick={() => onIrPara(i.dataCarga!)} className="font-medium text-tinta-700 underline underline-offset-2 hover:text-tinta-900">ver os e-mails da carga de {dataBr(i.dataCarga)}</button></>
                    )}
                  </p>
                  {i.avisos.map(a => <p key={a} className={`text-sm ${i.situacao === 'RECUSADO' ? 'text-red-800' : 'text-amber-800'}`}>{a}</p>)}
                </div>
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

// O que o cálculo do frete Perfil da carga diz (de onde saem assunto e texto do e-mail
// da Girassol). Sem o cálculo, avisa e oferece subir o .txt (UI-04).
function FreteResumo({ email: e, botao }: { email: PreviaEmail; botao: React.ReactNode }) {
  const f = e.frete
  if (!f) {
    return (
      <Aviso tipo="erro" acao={botao}>Sem o cálculo do frete Perfil desta carga: o assunto sai sem as caixas.</Aviso>
    )
  }
  const cx = (n: number | null) => (n == null ? 'não lido' : n.toLocaleString('pt-BR'))
  return (
    <div className="rounded-lg border border-gray-200 bg-gray-50 pl-3 pr-1 py-2 space-y-2">
      <div className="flex items-center gap-2 min-w-0">
        <FileText size={16} className="text-tinta-600 shrink-0" aria-hidden />
        <p className="text-sm font-medium text-gray-900 break-all flex-1">{f.relatorioNome}</p>
        {f.relatorioId != null && (
          <BotaoIcone icone={Download} rotulo="Baixar o cálculo do frete Perfil"
                      onClick={() => emailsClienteApi.baixarRelatorio(f.relatorioId!, f.relatorioNome ?? 'relatorio.txt')
                        .catch(async err => toast.error(await erroEmail(err, 'Não consegui baixar o cálculo do frete Perfil.')))} />
        )}
      </div>
      <dl className="grid grid-cols-3 gap-2 text-sm tabular-nums pr-2">
        <div><dt className="text-xs text-gray-500">Caixas</dt><dd className={f.caixasNormais == null ? 'text-amber-800 font-medium' : 'text-gray-900'}>{cx(f.caixasNormais)}</dd></div>
        <div><dt className="text-xs text-gray-500">ProdutoE</dt><dd className={f.caixasProdutoE == null ? 'text-amber-800 font-medium' : 'text-gray-900'}>{cx(f.caixasProdutoE)}</dd></div>
        <div><dt className="text-xs text-gray-500">Total do frete</dt><dd className={f.totalFrete == null ? 'text-amber-800 font-medium' : 'text-gray-900'}>{f.totalFrete == null ? 'não lido' : brl(f.totalFrete)}</dd></div>
      </dl>
      <div>{botao}</div>
    </div>
  )
}
