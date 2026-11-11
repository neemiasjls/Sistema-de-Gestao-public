import { useConfirmar } from './ui/Confirmar'
import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import axios from 'axios'
import QRCode from 'qrcode'
import { Copy, History, KeyRound, Printer, QrCode, RefreshCw } from 'lucide-react'
import {
  portalProdutosADonoApi, usuariosApi,
  type ContaPortalProdutosA, type CodigoPortalProdutosA, type SenhaProvisoria,
} from '../services/api'
import Botao, { classesBotao } from './ui/Botao'
import { Carregando, ErroCarga, Vazio } from './ui/Estados'
import { comoLista } from '../utils/resposta'

// Contas criadas pelo QR do barracão e o próprio QR (EST-09, USU-05). Só o dono vê esta seção.
// Desativar corta na hora (a API confere a conta a cada pedido).

function erroDe(err: unknown, padrao: string) {
  return axios.isAxiosError(err) ? (err.response?.data as { erro?: string })?.erro ?? padrao : padrao
}

const diaBr = (iso: string) =>
  new Date(iso).toLocaleDateString('pt-BR', { timeZone: 'America/Sao_Paulo' })

/** Endereço que o QR abre. A tela /produtosA confere o código antes do cadastro. */
export const linkPortal = (codigo: string) => `${window.location.origin}/produtosA?c=${encodeURIComponent(codigo)}`

/** Folha A4 para colar no barracão, impressa por um iframe escondido (sem janela nova). */
async function imprimirQr(codigo: string) {
  const img = await QRCode.toDataURL(linkPortal(codigo), { width: 1200, margin: 2, errorCorrectionLevel: 'M' })
  const endereco = `${window.location.host}/produtosA`
  const frame = document.createElement('iframe')
  frame.setAttribute('aria-hidden', 'true')
  frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0'
  document.body.appendChild(frame)
  const doc = frame.contentDocument
  if (!doc || !frame.contentWindow) { frame.remove(); throw new Error('sem iframe') }
  doc.open()
  doc.write(`<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><title>QR das produtosA</title>
<style>
  @page { size: A4 portrait; margin: 18mm; }
  html, body { margin: 0; font-family: Arial, Helvetica, sans-serif; color: #000; background: #fff; }
  main { display: flex; flex-direction: column; align-items: center; text-align: center; }
  h1 { font-size: 34pt; margin: 8mm 0 4mm; }
  p { font-size: 15pt; margin: 0 0 8mm; }
  img { width: 150mm; height: 150mm; }
  .end { font-size: 13pt; margin-top: 6mm; word-break: break-all; }
</style></head><body><main>
  <h1>Anote as produtosA aqui</h1>
  <p>Aponte a câmera do celular para o código.</p>
  <img src="${img}" alt="">
  <div class="end">${endereco}</div>
</main></body></html>`)
  doc.close()
  const janela = frame.contentWindow
  const imagem = doc.querySelector('img')
  const pronto = () => {
    janela.focus()
    janela.print()
    setTimeout(() => frame.remove(), 1000)
  }
  if (imagem && !imagem.complete) imagem.onload = pronto
  else pronto()
}

export default function PortalProdutosASecao() {
  const confirmar = useConfirmar()

  const [contas, setContas]   = useState<ContaPortalProdutosA[]>([])
  const [carregandoContas, setCarregandoContas] = useState(true)
  const [falhouContas, setFalhouContas] = useState(false)

  const [codigo, setCodigo]   = useState<CodigoPortalProdutosA | null>(null)
  const [carregandoCodigo, setCarregandoCodigo] = useState(true)
  const [falhouCodigo, setFalhouCodigo] = useState(false)
  const [qr, setQr]           = useState<string | null>(null)
  const [trocando, setTrocando] = useState(false)
  const [imprimindo, setImprimindo] = useState(false)

  // Senha que não saiu por e-mail: aparece uma vez, para o dono repassar.
  const [pendente, setPendente] = useState<{ nome: string; email: string; senha: string; aviso: string } | null>(null)

  async function carregarContas() {
    try {
      setContas(comoLista<ContaPortalProdutosA>((await portalProdutosADonoApi.contas()).data, 'contas do portal'))
      setFalhouContas(false)
    } catch {
      setFalhouContas(true)
    } finally {
      setCarregandoContas(false)
    }
  }

  async function carregarCodigo() {
    try {
      setCodigo((await portalProdutosADonoApi.codigo()).data)
      setFalhouCodigo(false)
    } catch {
      setFalhouCodigo(true)
    } finally {
      setCarregandoCodigo(false)
    }
  }

  useEffect(() => { carregarContas(); carregarCodigo() }, [])

  // QR feito no navegador: o código não passa por nenhum serviço de fora
  useEffect(() => {
    if (!codigo?.codigo) { setQr(null); return }
    let vivo = true
    QRCode.toDataURL(linkPortal(codigo.codigo), { width: 480, margin: 2, errorCorrectionLevel: 'M' })
      .then(url => { if (vivo) setQr(url) })
      .catch(() => { if (vivo) setQr(null) })
    return () => { vivo = false }
  }, [codigo?.codigo])

  async function gerarOuTrocar() {
    const temCodigo = !!codigo?.codigo
    if (temCodigo && !await confirmar({
      titulo: 'Trocar o código do QR?',
      texto: 'O QR impresso para de funcionar. Quem já tem conta continua entrando; só o cadastro novo precisa do QR novo.',
      confirmar: 'Trocar código',
    })) return
    setTrocando(true)
    try {
      setCodigo((await portalProdutosADonoApi.trocarCodigo()).data)
      toast.success(temCodigo ? 'Código trocado. Imprima o QR novo.' : 'QR gerado.')
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui gerar o código.'))
    } finally {
      setTrocando(false)
    }
  }

  async function imprimir() {
    if (!codigo?.codigo) return
    setImprimindo(true)
    try { await imprimirQr(codigo.codigo) }
    catch { toast.error('Não consegui abrir a impressão.') }
    finally { setImprimindo(false) }
  }

  function tratarEntrega(r: SenhaProvisoria, nome: string, email: string) {
    if (r.emailEnviado) {
      toast.success(`Senha provisória enviada para ${email}.`)
      setPendente(null)
    } else if (r.senhaProvisoria) {
      setPendente({ nome, email, senha: r.senhaProvisoria, aviso: r.avisoEmail ?? 'O e-mail não saiu.' })
    }
  }

  async function mandarNovaSenha(c: ContaPortalProdutosA) {
    if (!await confirmar({
      titulo: `Gerar senha nova para ${c.nome}?`,
      texto: 'Vai uma senha provisória para o e-mail da pessoa, e a senha atual deixa de valer. No celular ela cria a senha dela.',
      confirmar: 'Gerar senha nova',
      perigo: false,
    })) return
    try {
      tratarEntrega((await usuariosApi.novaSenha(c.id)).data, c.nome, c.email)
      carregarContas()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui gerar a senha.'))
    }
  }

  async function alternarAtivo(c: ContaPortalProdutosA) {
    if (c.ativo && !await confirmar({
      titulo: `Desativar ${c.nome}?`,
      texto: 'Sai do portal na hora e não entra mais. As anotações dela continuam no sistema.',
      confirmar: 'Desativar',
    })) return
    try {
      if (c.ativo) await usuariosApi.desativar(c.id)
      else await usuariosApi.reativar(c.id)
      toast.success(c.ativo ? `${c.nome} não entra mais no portal.` : `${c.nome} pode entrar de novo.`)
      carregarContas()
    } catch (err) {
      toast.error(erroDe(err, 'Não consegui alterar.'))
    }
  }

  async function copiar(texto: string, ok: string) {
    try { await navigator.clipboard.writeText(texto); toast.success(ok) }
    catch { toast.error('Não consegui copiar. Selecione e copie à mão.') }
  }

  const ativas = contas.filter(c => c.ativo)
  const desativadas = contas.filter(c => !c.ativo)

  return (
    <section aria-labelledby="portal-produtosA" className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 id="portal-produtosA" className="text-lg font-semibold text-gray-900">Portal das produtosA</h2>
          <p className="text-sm text-gray-600 mt-0.5">
            Quem lê o QR do barracão cria a conta e anota as produtosA no celular. Só vê as produtosA.
          </p>
        </div>
        <Link to="/estoque/produtosA/historico-portal" className={classesBotao('secundario', 'sm')}>
          <History className="w-4 h-4" aria-hidden="true" /> Histórico do portal
        </Link>
      </div>

      {/* QR do barracão */}
      <div className="bg-white rounded-xl border border-gray-200 p-4 sm:p-5">
        <h3 className="font-semibold text-gray-900">QR do barracão</h3>
        {carregandoCodigo ? (
          <div className="mt-3"><Carregando linhas={1} rotulo="Carregando o QR" /></div>
        ) : falhouCodigo ? (
          <div className="mt-3">
            <ErroCarga mensagem="Não consegui carregar o QR." onTentar={() => { setCarregandoCodigo(true); carregarCodigo() }} />
          </div>
        ) : !codigo?.codigo ? (
          <Vazio compacto icone={QrCode} titulo="Ainda não tem QR"
                 acao={<Botao variante="primario" icone={QrCode} carregando={trocando} onClick={gerarOuTrocar}>Gerar QR</Botao>}>
            Gere o QR e imprima para colar no barracão.
          </Vazio>
        ) : (
          <div className="mt-3 flex flex-col sm:flex-row gap-4 sm:items-start">
            {qr ? (
              <img src={qr} alt="QR do portal das produtosA"
                   className="w-48 h-48 self-center sm:self-start rounded-lg border border-gray-200 bg-white" />
            ) : (
              <div className="w-48 h-48 self-center sm:self-start rounded-lg border border-dashed border-gray-300 grid place-items-center text-sm text-gray-600 text-center px-3">
                Não consegui desenhar o QR.
              </div>
            )}
            <div className="min-w-0 flex-1 space-y-3">
              <div>
                <p className="text-sm text-gray-600">O QR abre este endereço:</p>
                <code className="block mt-1 text-sm break-all font-mono bg-gray-50 border border-gray-200 rounded-lg px-3 py-2 select-all">
                  {linkPortal(codigo.codigo)}
                </code>
                {codigo.trocadoEm && (
                  <p className="text-sm text-gray-600 mt-1">Código desde {diaBr(codigo.trocadoEm)}.</p>
                )}
              </div>
              <div className="flex flex-wrap gap-2">
                <Botao variante="primario" icone={Printer} carregando={imprimindo} onClick={imprimir}>Imprimir</Botao>
                <Botao variante="secundario" icone={Copy} onClick={() => copiar(linkPortal(codigo.codigo!), 'Endereço copiado.')}>
                  Copiar endereço
                </Botao>
                <Botao variante="perigoLeve" icone={RefreshCw} carregando={trocando} onClick={gerarOuTrocar}>Trocar código</Botao>
              </div>
            </div>
          </div>
        )}
      </div>

      {pendente && (
        <div role="alert" className="bg-gema-50 border border-gema-300 rounded-xl p-4 space-y-3">
          <p className="text-sm text-gray-900">
            <b>{pendente.aviso}</b> Mande esta senha para {pendente.nome} por outro caminho, como o WhatsApp.
            Ela aparece só agora.
          </p>
          <code className="block w-fit max-w-full break-all font-mono text-lg bg-white border border-gema-200 rounded-lg px-3 py-1.5 select-all">
            {pendente.senha}
          </code>
          <div className="flex flex-wrap gap-2">
            <Botao variante="primario" icone={Copy} onClick={() => copiar(pendente.senha, 'Senha copiada.')}>Copiar</Botao>
            <Botao variante="secundario" onClick={() => copiar(`Seu acesso ao portal das produtosA da TRX Transportes:\nE-mail: ${pendente.email}\nSenha provisória: ${pendente.senha}\n\nEntre em ${location.origin}/produtosA e crie a sua senha.`, 'Mensagem copiada.')}>
              Copiar mensagem pronta
            </Botao>
            <Botao variante="fantasma" onClick={() => setPendente(null)}>Já mandei</Botao>
          </div>
        </div>
      )}

      {/* Contas */}
      {carregandoContas ? (
        <Carregando linhas={2} rotulo="Carregando as contas do portal" />
      ) : falhouContas ? (
        <ErroCarga mensagem="Não consegui carregar as contas do portal." onTentar={() => { setCarregandoContas(true); carregarContas() }} />
      ) : contas.length === 0 ? (
        <Vazio titulo="Ninguém se cadastrou ainda">Quem ler o QR e criar a conta aparece aqui.</Vazio>
      ) : (
        <div className="space-y-3">
          {[{ titulo: 'Ativas', lista: ativas }, { titulo: 'Desativadas', lista: desativadas }]
            .filter(g => g.lista.length > 0)
            .map(g => (
              <div key={g.titulo}>
                <h3 className="text-sm font-semibold text-gray-700 mb-1.5">{g.titulo} · {g.lista.length}</h3>
                <ul className="bg-white rounded-xl border border-gray-200 divide-y divide-gray-100 overflow-hidden">
                  {g.lista.map(c => (
                    <li key={c.id} className={`px-4 sm:px-5 py-3.5 ${!c.ativo ? 'bg-gray-50' : ''}`}>
                      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2">
                        <div className="min-w-0">
                          <div className="flex items-center gap-2 flex-wrap">
                            <span className={`font-semibold ${c.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{c.nome}</span>
                            {c.trocarSenha && c.ativo && (
                              <span className="text-xs font-medium bg-gema-50 text-gema-900 border border-gema-200 rounded-full px-2 py-0.5">Senha provisória</span>
                            )}
                            {!c.ativo && <span className="text-sm text-gray-500">desativada</span>}
                          </div>
                          <p className="text-sm text-gray-600 break-all">{c.email}</p>
                          <p className="text-sm text-gray-600">
                            Desde {diaBr(c.criadoEm)} · {c.anotacoes === 0 ? 'nenhuma anotação'
                              : `${c.anotacoes} ${c.anotacoes === 1 ? 'anotação' : 'anotações'}, a última em ${diaBr(c.ultimaAnotacao!)}`}
                          </p>
                        </div>
                        <div className="flex items-center gap-1 shrink-0">
                          {c.ativo && (
                            <Botao variante="secundario" tamanho="sm" icone={KeyRound} onClick={() => mandarNovaSenha(c)} title="Mandar senha provisória nova">
                              Nova senha
                            </Botao>
                          )}
                          <Botao variante={c.ativo ? 'perigoLeve' : 'secundario'} tamanho="sm" onClick={() => alternarAtivo(c)}>
                            {c.ativo ? 'Desativar' : 'Reativar'}
                          </Botao>
                        </div>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
        </div>
      )}
    </section>
  )
}
