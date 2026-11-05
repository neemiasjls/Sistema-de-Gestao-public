import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  ArrowLeft, CheckCircle2, ChevronLeft, ChevronRight, Download, FileSpreadsheet, FileText, FileUp,
  Pencil, Plus, Trash2, TriangleAlert, Upload, X, XCircle,
} from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import { brl, dataBr, lerNum } from '../utils/formato'
import { classeCampo, classeNumero } from '../components/EstoquePecas'
import {
  semPararApi, erroDaApi, ehConflito, nomeMes, MESES,
  type ControleResumo, type ControleDetalhe, type LinhaSemParar, type ResultadoImportacao, type TipoLinha, type Origem,
} from '../services/semPararApi'

// Sem Parar: um controle de pedágio por mês, guardado no sistema. A lista mostra
// os meses; o controle aberto (?controle=ID) mostra e deixa mudar a fatura de cada
// placa, os vales-pedágio e os créditos inseridos. A planilha sai em Exportar Excel.

export default function SemPararPage() {
  const [params, setParams] = useSearchParams()
  const id = Number(params.get('controle')) || null
  const abrir = (novo: number | null) => setParams(novo ? { controle: String(novo) } : {})
  return id ? <Controle key={id} id={id} onAbrir={abrir} /> : <Lista onAbrir={abrir} />
}

function SeloOrigem({ origem, editado }: { origem: Origem; editado: boolean }) {
  const t = { gerado: 'Do relatório e fatura', planilha: 'Da planilha', manual: 'Lançado à mão' }[origem]
  return (
    <span className="inline-flex flex-wrap gap-1">
      <span className="text-xs font-medium px-1.5 py-0.5 rounded-md bg-tinta-50 text-tinta-800 border border-tinta-100">{t}</span>
      {editado && origem !== 'manual' && (
        <span className="text-xs font-medium px-1.5 py-0.5 rounded-md bg-gema-100 text-gema-900 border border-gema-200">mudado na tela</span>
      )}
    </span>
  )
}

// ───────────────────────────── Lista ─────────────────────────────

function Lista({ onAbrir }: { onAbrir: (id: number) => void }) {
  const [itens, setItens] = useState<ControleResumo[] | null>(null)
  const [modalGerar, setModalGerar] = useState(false)
  const [modalNovo, setModalNovo] = useState(false)
  const [importando, setImportando] = useState(false)
  const [resultado, setResultado] = useState<ResultadoImportacao[] | null>(null)
  const [enviados, setEnviados] = useState<File[]>([])
  const [falhou, setFalhou] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)
  const confirmar = useConfirmar()

  // Falhou: erro com "Tentar de novo" (antes virava "Nenhum mês guardado ainda")
  const carregar = useCallback(() => {
    setFalhou(false)
    semPararApi.listar().then(setItens).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const importar = async (arquivos: File[], substituir = false) => {
    if (arquivos.length === 0) return
    setImportando(true)
    try {
      const r = await semPararApi.importarPlanilhas(arquivos, substituir)
      setEnviados(arquivos)
      setResultado(prev => substituir && prev
        ? prev.map(p => r.find(x => x.nome === p.nome) ?? p)
        : r)
      const ok = r.filter(x => x.status === 'criado' || x.status === 'atualizado').length
      if (ok > 0) toast.success(`${ok} ${ok === 1 ? 'planilha importada' : 'planilhas importadas'}.`)
      carregar()
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui importar as planilhas.'))
    } finally {
      setImportando(false)
      if (inputRef.current) inputRef.current.value = ''
    }
  }

  const editados = resultado?.filter(r => r.status === 'editado') ?? []
  const substituirEditados = async () => {
    const nomes = editados.map(r => r.mes && r.ano ? nomeMes(r.mes, r.ano) : r.nome).join(', ')
    if (!await confirmar({
      titulo: 'Substituir pelo que está na planilha?', texto: <><b>{nomes}</b>. O que foi mudado na tela nesses meses se perde.</>,
      confirmar: 'Substituir',
    })) return
    importar(enviados.filter(f => editados.some(r => r.nome === f.name)), true)
  }

  return (
    <div className="space-y-5 max-w-3xl">
      <CabecalhoTela
        descricao="Um controle de pedágio por mês, guardado aqui. Gere pelo relatório de vale-pedágio e a fatura, importe as planilhas já feitas ou lance à mão. A planilha sai no botão Exportar Excel de cada mês."
        acoes={<>
          <Botao variante="secundario" icone={Upload} onClick={() => inputRef.current?.click()} carregando={importando} textoCarregando="Importando...">
            Importar planilhas
          </Botao>
          <Botao variante="primario" icone={FileUp} onClick={() => setModalGerar(true)}>Gerar do relatório e da fatura</Botao>
        </>}
      >
        <input ref={inputRef} type="file" accept=".xlsx" multiple hidden tabIndex={-1}
               onChange={e => importar(Array.from(e.target.files ?? []))} />
      </CabecalhoTela>

      {resultado && (
        <section className="bg-white border border-gray-200 rounded-xl overflow-hidden animate-entrada">
          <div className="pl-4 pr-2 py-2 border-b border-gray-100 flex items-center justify-between gap-2">
            <h3 className="font-semibold text-gray-900">Planilhas enviadas</h3>
            <BotaoIcone icone={X} rotulo="Fechar a lista" onClick={() => setResultado(null)} />
          </div>
          <ul className="divide-y divide-gray-100">
            {resultado.map(r => (
              <li key={r.nome} className={`px-4 py-3 text-sm flex gap-3 ${r.status === 'erro' ? 'bg-red-50/60' : ''}`}>
                {r.status === 'erro'
                  ? <XCircle size={17} className="text-red-700 mt-0.5 shrink-0" aria-hidden />
                  : r.status === 'editado'
                    ? <TriangleAlert size={17} className="text-amber-700 mt-0.5 shrink-0" aria-hidden />
                    : <CheckCircle2 size={17} className="text-emerald-700 mt-0.5 shrink-0" aria-hidden />}
                <div className="min-w-0 space-y-0.5">
                  <p className="font-medium text-gray-900 break-words">{r.nome}</p>
                  <p className={r.status === 'erro' ? 'text-red-800' : r.status === 'editado' ? 'text-amber-900' : 'text-gray-600 tabular-nums'}>
                    {r.status === 'erro' ? r.erro
                      : r.status === 'editado' ? r.erro
                      : `${r.mes && r.ano ? nomeMes(r.mes, r.ano) : ''} · ${r.status === 'criado' ? 'criado' : 'atualizado'} · ${r.linhas} linhas`}
                  </p>
                  {r.avisos.map(a => <p key={a} className="text-amber-800">{a}</p>)}
                </div>
              </li>
            ))}
          </ul>
          {editados.length > 0 && (
            <div className="px-4 py-3 border-t border-gray-100 bg-amber-50/60">
              <Botao variante="secundario" onClick={substituirEditados} disabled={importando} bloco className="sm:w-auto">
                Substituir {editados.length === 1 ? 'o mês mudado' : `os ${editados.length} meses mudados`} pela planilha
              </Botao>
            </div>
          )}
        </section>
      )}

      <section aria-labelledby="sp-meses" className="space-y-2">
        <div className="flex items-center justify-between gap-2">
          <h2 id="sp-meses" className="text-lg font-semibold text-gray-900">Meses guardados</h2>
          <Botao variante="fantasma" tamanho="sm" icone={Plus} onClick={() => setModalNovo(true)}>Novo mês à mão</Botao>
        </div>
        {falhou ? (
          <ErroCarga mensagem="Não consegui carregar os controles." onTentar={carregar} />
        ) : itens === null ? (
          <Carregando linhas={4} rotulo="Carregando os controles" />
        ) : itens.length === 0 ? (
          <Vazio icone={FileSpreadsheet} titulo="Nenhum mês guardado ainda">
            Importe as planilhas já feitas ou gere o mês pelo relatório e a fatura.
          </Vazio>
        ) : (
          <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100 overflow-hidden">
            {itens.map(c => (
              <li key={c.id}>
                <button onClick={() => onAbrir(c.id)}
                        className="group w-full text-left flex items-center gap-3 px-4 py-3 hover:bg-gray-50 transition-colors">
                  <div className="w-24 shrink-0">
                    <p className="font-display text-lg font-semibold text-gray-900 leading-tight">{MESES[c.mes - 1]}</p>
                    <p className="text-sm text-gray-500 tabular-nums">{c.ano}</p>
                  </div>
                  <div className="flex-1 min-w-0 space-y-1">
                    <p className="font-display text-lg font-semibold text-gray-900 tabular-nums leading-tight">{brl(c.valor)}</p>
                    <p className="text-sm text-gray-600 tabular-nums flex flex-wrap gap-x-3">
                      <span className="whitespace-nowrap">Fatura {brl(c.fatura)}</span>
                      <span className="whitespace-nowrap">Créditos {brl(c.creditos)}</span>
                      <span className="whitespace-nowrap">Vale-pedágio {brl(c.vpo)}</span>
                    </p>
                    <SeloOrigem origem={c.origem} editado={c.editado} />
                  </div>
                  <ChevronRight size={18} className="text-gray-400 group-hover:text-gray-700 shrink-0" aria-hidden />
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {modalGerar && <ModalGerar onClose={() => setModalGerar(false)} onSalvou={id => { setModalGerar(false); onAbrir(id) }} />}
      {modalNovo && <ModalNovo itens={itens ?? []} onClose={() => setModalNovo(false)} onSalvou={id => { setModalNovo(false); onAbrir(id) }} />}
    </div>
  )
}

// A fatura que chega é sempre a do mês que passou (SEM-02): dia 1º do mês atual
// menos um dia cai no mês anterior, e em janeiro vira dezembro do ano anterior.
const mesPassado = () => {
  const d = new Date(new Date().getFullYear(), new Date().getMonth(), 0)
  return { mes: d.getMonth() + 1, ano: d.getFullYear() }
}

function CamposMes({ mes, ano, setMes, setAno }: {
  mes: number; ano: number; setMes: (m: number) => void; setAno: (a: number) => void
}) {
  return (
    <div className="grid grid-cols-2 gap-3">
      <Campo rotulo="Mês">
        <select value={mes} onChange={e => setMes(Number(e.target.value))} className={classeCampo}>
          {MESES.map((m, i) => <option key={m} value={i + 1}>{m}</option>)}
        </select>
      </Campo>
      <Campo rotulo="Ano">
        <input type="number" inputMode="numeric" value={ano} onChange={e => setAno(Number(e.target.value))} className={classeNumero} />
      </Campo>
    </div>
  )
}

// Um arquivo só (relatório ou fatura): a área de soltar; escolhido, vira uma
// linha com o nome e o X para trocar
function Arquivo({ rotulo, aceita, file, onPick, icone }: {
  rotulo: string; aceita: string; file: File | null; onPick: (f: File | null) => void; icone: typeof FileUp
}) {
  return (
    <div className="space-y-1">
      <p className="text-sm sm:text-xs font-medium text-gray-700">{rotulo}</p>
      {file ? (
        <div className="flex items-center gap-3 rounded-xl border border-emerald-200 bg-emerald-50 pl-3 pr-1 py-1">
          <CheckCircle2 size={18} className="text-emerald-700 shrink-0" aria-hidden />
          <span className="flex-1 min-w-0 text-sm font-medium text-gray-900 break-all py-1.5">{file.name}</span>
          <BotaoIcone icone={X} rotulo={`Trocar ${file.name}`} onClick={() => onPick(null)} />
        </div>
      ) : (
        <AreaSoltar compacta multiplos={false} aceita={[aceita]} icone={icone} onArquivos={f => onPick(f[0] ?? null)}
                    titulo={`Arraste ${rotulo === 'Fatura' ? 'a fatura' : 'o relatório de VPO'} aqui`} textoBotao="Escolher arquivo" />
      )}
    </div>
  )
}

function ModalGerar({ onClose, onSalvou }: { onClose: () => void; onSalvou: (id: number) => void }) {
  const inicio = mesPassado()
  const [mes, setMes] = useState(inicio.mes)
  const [ano, setAno] = useState(inicio.ano)
  const [vpo, setVpo] = useState<File | null>(null)
  const [fatura, setFatura] = useState<File | null>(null)
  const [salvando, setSalvando] = useState(false)
  const confirmar = useConfirmar()

  const gerar = async (substituir = false) => {
    if (!vpo) { toast.error('Escolha o relatório de VPO (.xlsx).'); return }
    if (!fatura) { toast.error('Escolha a fatura do Sem Parar (.pdf).'); return }
    setSalvando(true)
    try {
      const r = await semPararApi.gerar(vpo, fatura, mes, ano, substituir)
      toast.success(`${nomeMes(mes, ano)} ${r.criado ? 'salvo' : 'atualizado'}.`)
      onSalvou(r.id)
    } catch (e) {
      const msg = await erroDaApi(e, 'Não consegui ler os arquivos.')
      if (ehConflito(e) && await confirmar({ titulo: 'Substituir o mês guardado?', texto: msg, confirmar: 'Substituir' })) { setSalvando(false); gerar(true); return }
      toast.error(msg)
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo="Gerar do relatório e da fatura" onClose={onClose} sujo={!!vpo || !!fatura}
           descricao="O relatório de VPO (ConsultaViagensEmbarcador.xlsx) e a fatura em PDF viram o controle do mês, guardado aqui.">
      <CamposMes mes={mes} ano={ano} setMes={setMes} setAno={setAno} />
      <Arquivo rotulo="Relatório de VPO" aceita=".xlsx" icone={FileSpreadsheet} file={vpo} onPick={setVpo} />
      <Arquivo rotulo="Fatura" aceita=".pdf" icone={FileText} file={fatura} onPick={setFatura} />
      <BotoesModal salvando={salvando} onSalvar={() => gerar()} onClose={onClose} rotulo="Gerar e salvar" />
    </Modal>
  )
}

function ModalNovo({ itens, onClose, onSalvou }: {
  itens: ControleResumo[]; onClose: () => void; onSalvou: (id: number) => void
}) {
  const inicio = mesPassado()
  const [mes, setMes] = useState(inicio.mes)
  const [ano, setAno] = useState(inicio.ano)
  const [salvando, setSalvando] = useState(false)
  const existe = itens.find(c => c.mes === mes && c.ano === ano)

  const salvar = async () => {
    if (existe) { onSalvou(existe.id); return }
    setSalvando(true)
    try {
      const r = await semPararApi.criar(mes, ano)
      toast.success(`${nomeMes(mes, ano)} criado.`)
      onSalvou(r.id)
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui criar o mês.'))
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo="Novo mês à mão" onClose={onClose}
           descricao="Começa vazio, com as placas da frota. Depois lance a fatura, os vales-pedágio e os créditos.">
      <CamposMes mes={mes} ano={ano} setMes={setMes} setAno={setAno} />
      {existe && <Aviso tipo="info">{nomeMes(mes, ano)} já existe; o botão abre ele.</Aviso>}
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo={existe ? 'Abrir' : 'Criar'} />
    </Modal>
  )
}

// ───────────────────────────── Controle do mês ─────────────────────────────

type Aba = 'placas' | 'vpo' | 'creditos' | 'arquivos'
type EdicaoLinha = { tipo: TipoLinha; linha?: LinhaSemParar; placa?: string }

function Controle({ id, onAbrir }: { id: number; onAbrir: (id: number | null) => void }) {
  const [c, setC] = useState<ControleDetalhe | null>(null)
  const [erro, setErro] = useState(false)
  const [aba, setAba] = useState<Aba>('placas')
  const idAbas = useAbas('semparar')
  const [edicao, setEdicao] = useState<EdicaoLinha | null>(null)
  const [novaPlaca, setNovaPlaca] = useState(false)
  const [exportando, setExportando] = useState(false)
  const confirmar = useConfirmar()

  const carregar = useCallback(async () => {
    setErro(false)
    try { setC(await semPararApi.detalhe(id)) }
    catch { setErro(true) }
  }, [id])
  useEffect(() => { carregar() }, [carregar])

  const voltar = <Botao variante="fantasma" icone={ArrowLeft} onClick={() => onAbrir(null)} className="-ml-2">Controles</Botao>

  if (erro) return (
    <div className="max-w-4xl space-y-3">
      {voltar}
      <ErroCarga mensagem="Não consegui abrir este controle." detalhe="Pode ter sido apagado, ou a internet caiu. Tente de novo." onTentar={carregar} />
    </div>
  )
  if (!c) return (
    <div className="max-w-4xl space-y-3">
      {voltar}
      <Carregando forma="cartoes" linhas={4} rotulo="Carregando o controle" />
      <Carregando linhas={4} rotulo="Carregando o controle" />
    </div>
  )

  const vpos = c.linhas.filter(l => l.tipo === 'VPO')
  const creditos = c.linhas.filter(l => l.tipo === 'CREDITO')
  const t = c.totais

  const exportar = async () => {
    setExportando(true)
    try { await semPararApi.exportarExcel(c) }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui gerar a planilha.')) }
    finally { setExportando(false) }
  }

  const excluir = async () => {
    if (!await confirmar({
      titulo: `Apagar o controle de ${nomeMes(c.mes, c.ano)}?`,
      texto: 'Sai inteiro: fatura, vales-pedágio, créditos e arquivos.',
      confirmar: 'Apagar controle',
    })) return
    try { await semPararApi.excluir(c.id); toast.success('Controle apagado.'); onAbrir(null) }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  const excluirLinha = async (l: LinhaSemParar) => {
    const desc = l.tipo === 'VPO' ? `o vale-pedágio de ${l.placa}${l.data ? ` em ${dataBr(l.data)}` : ''}`
      : l.tipo === 'CREDITO' ? `o crédito de ${brl(l.valor)} da ${l.placa}` : `a fatura da ${l.placa}`
    if (!await confirmar({ titulo: 'Apagar esta linha?', texto: <>Apagar {desc} (<b>{brl(l.valor)}</b>).</>, confirmar: 'Apagar' })) return
    try { await semPararApi.excluirLinha(l.id); toast.success('Apagado.'); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  const tirarPlaca = async (placa: string) => {
    if (!await confirmar({ titulo: `Tirar a placa ${placa} deste mês?`, texto: 'A coluna dela sai da planilha deste mês.', confirmar: 'Tirar placa' })) return
    try { await semPararApi.placas(c.id, c.placas.filter(p => p !== placa)); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui tirar a placa.')) }
  }

  return (
    <div className="max-w-4xl space-y-4">
      <div className="flex items-center justify-between gap-2">
        {voltar}
        <div className="flex items-center gap-1">
          <BotaoIcone icone={ChevronLeft} rotulo="Mês anterior" disabled={!c.anteriorId} onClick={() => onAbrir(c.anteriorId)} />
          <BotaoIcone icone={ChevronRight} rotulo="Próximo mês" disabled={!c.proximoId} onClick={() => onAbrir(c.proximoId)} />
        </div>
      </div>

      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-2xl font-semibold text-gray-900">{nomeMes(c.mes, c.ano)}</h2>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <SeloOrigem origem={c.origem} editado={c.editado} />
            <span className="text-sm text-gray-500 break-all">{c.nome}</span>
          </div>
        </div>
        <div className="flex gap-2">
          <Botao variante="primario" icone={Download} onClick={exportar} carregando={exportando} textoCarregando="Gerando..." className="flex-1 sm:flex-none">
            Exportar Excel
          </Botao>
          <BotaoIcone icone={Trash2} rotulo="Apagar o controle" perigo onClick={excluir} className="border border-gray-300" />
        </div>
      </div>

      {c.avisos.length > 0 && <Aviso tipo="atencao" itens={c.avisos} />}

      {/* Os totais numa faixa; o Valor (o que se paga) por último, em gema */}
      <div>
        <dl className="grid grid-cols-2 sm:grid-cols-4 gap-px bg-gray-200 rounded-xl border border-gray-200 overflow-hidden [&>div]:px-3.5 [&>div]:py-3 [&>div]:min-w-0">
          <Total titulo="Fatura" valor={t.fatura} />
          <Total titulo="Créditos" valor={t.creditos} sinal="−" />
          <Total titulo="Vale-pedágio" valor={t.vpo} sinal="+" />
          <Total titulo="Valor" valor={t.valor} destaque />
        </dl>
        <p className="text-sm text-gray-500 mt-1.5">Valor = fatura − créditos + vale-pedágio, por placa.</p>
      </div>

      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Partes do controle" opcoes={[
        ['placas', 'Por placa'],
        ['vpo', `Vale-pedágio ${vpos.length}`],
        ['creditos', `Créditos ${creditos.length}`],
        ['arquivos', `Arquivos ${c.arquivos.length}`],
      ]} />

      <PainelAba id={idAbas} valor={aba}>
        {aba === 'placas' && (
          <AbaPlacas c={c} onFatura={(placa, linha) => setEdicao({ tipo: 'FATURA', placa, linha })}
                     onNovaPlaca={() => setNovaPlaca(true)} onTirarPlaca={tirarPlaca} />
        )}

        {aba === 'vpo' && (
          <div className="space-y-3">
            <Botao variante="primario" icone={Plus} bloco className="sm:w-auto" onClick={() => setEdicao({ tipo: 'VPO' })}>Novo vale-pedágio</Botao>
            {vpos.length === 0 ? <Vazio titulo="Nenhum vale-pedágio neste mês" /> : (
              <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
                {vpos.map(l => (
                  <LinhaItem key={l.id} l={l} onEditar={() => setEdicao({ tipo: 'VPO', linha: l })} onExcluir={() => excluirLinha(l)}>
                    {l.data ? dataBr(l.data) : <span className="font-medium text-amber-800">Sem data</span>}
                  </LinhaItem>
                ))}
              </ul>
            )}
          </div>
        )}

        {aba === 'creditos' && (
          <div className="space-y-3">
            <Botao variante="primario" icone={Plus} bloco className="sm:w-auto" onClick={() => setEdicao({ tipo: 'CREDITO' })}>Novo crédito</Botao>
            {creditos.length === 0 ? <Vazio titulo="Nenhum crédito inserido neste mês" /> : (
              c.placas.filter(p => creditos.some(l => l.placa === p)).map(p => {
                const daPlaca = creditos.filter(l => l.placa === p)
                return (
                  <div key={p} className="bg-white border border-gray-200 rounded-xl overflow-hidden">
                    <div className="flex items-center justify-between gap-2 px-4 py-2 bg-gray-50 border-b border-gray-100">
                      <span className="text-sm font-semibold text-gray-900 tabular-nums">{p}</span>
                      <span className="text-sm text-gray-600 tabular-nums">
                        {daPlaca.length} {daPlaca.length === 1 ? 'crédito' : 'créditos'} · {brl(daPlaca.reduce((s, l) => s + l.valor, 0))}
                      </span>
                    </div>
                    <ul className="divide-y divide-gray-100">
                      {daPlaca.map((l, i) => (
                        <LinhaItem key={l.id} l={l} semPlaca onEditar={() => setEdicao({ tipo: 'CREDITO', linha: l })} onExcluir={() => excluirLinha(l)}>
                          {i + 1}º crédito
                        </LinhaItem>
                      ))}
                    </ul>
                  </div>
                )
              })
            )}
          </div>
        )}

        {aba === 'arquivos' && (
          c.arquivos.length === 0 ? <Vazio titulo="Nenhum arquivo guardado">Este mês foi lançado à mão.</Vazio> : (
            <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
              {c.arquivos.map(a => (
                <li key={a.id} className="flex items-center gap-3 pl-4 pr-2 py-2">
                  <FileText size={17} className="text-gray-400 shrink-0" aria-hidden />
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-gray-900 break-all">{a.nome}</p>
                    <p className="text-sm text-gray-500 tabular-nums">
                      {{ PLANILHA: 'Planilha importada', VPO: 'Relatório de VPO', FATURA: 'Fatura' }[a.tipo]} · {(a.tamanho / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 0 })} KB
                    </p>
                  </div>
                  <BotaoIcone icone={Download} rotulo={`Baixar ${a.nome}`}
                              onClick={() => semPararApi.baixarArquivo(a).catch(() => toast.error('Não consegui baixar.'))} />
                </li>
              ))}
            </ul>
          )
        )}
      </PainelAba>

      {edicao && (
        <ModalLinha c={c} edicao={edicao} onClose={() => setEdicao(null)}
                    onSalvou={() => { setEdicao(null); carregar() }} />
      )}
      {novaPlaca && (
        <ModalPlaca c={c} onClose={() => setNovaPlaca(false)} onSalvou={() => { setNovaPlaca(false); carregar() }} />
      )}
    </div>
  )
}

function Total({ titulo, valor, destaque, sinal }: { titulo: string; valor: number; destaque?: boolean; sinal?: string }) {
  return (
    <div className={destaque ? 'bg-gema-50' : 'bg-white'}>
      <dt className={`text-xs font-medium ${destaque ? 'text-gray-700' : 'text-gray-500'}`}>
        {sinal && <span aria-hidden className="mr-1 text-gray-400">{sinal}</span>}{titulo}
      </dt>
      <dd className={`font-display font-semibold text-gray-900 mt-0.5 leading-tight tabular-nums break-words ${destaque ? 'text-xl sm:text-2xl' : 'text-lg sm:text-xl'}`}>{brl(valor)}</dd>
    </div>
  )
}

function LinhaItem({ l, semPlaca, onEditar, onExcluir, children }: {
  l: LinhaSemParar; semPlaca?: boolean; onEditar: () => void; onExcluir: () => void; children: React.ReactNode
}) {
  const nome = `${l.tipo === 'VPO' ? 'vale-pedágio' : 'crédito'} de ${brl(l.valor)}${semPlaca ? '' : ` da ${l.placa}`}`
  return (
    <li className="flex items-center gap-2 pl-4 pr-2 py-1.5">
      <div className="flex-1 min-w-0 flex flex-wrap items-baseline gap-x-3">
        <span className="text-sm text-gray-600 w-24 shrink-0 tabular-nums">{children}</span>
        {!semPlaca && <span className="text-sm font-medium text-gray-900 tabular-nums">{l.placa}</span>}
      </div>
      <span className="text-sm font-semibold text-gray-900 tabular-nums">{brl(l.valor)}</span>
      <BotaoIcone icone={Pencil} rotulo={`Mudar o ${nome}`} onClick={onEditar} />
      <BotaoIcone icone={Trash2} perigo rotulo={`Apagar o ${nome}`} onClick={onExcluir} />
    </li>
  )
}

function AbaPlacas({ c, onFatura, onNovaPlaca, onTirarPlaca }: {
  c: ControleDetalhe
  onFatura: (placa: string, linha?: LinhaSemParar) => void
  onNovaPlaca: () => void
  onTirarPlaca: (placa: string) => void
}) {
  const faturaDa = (p: string) => c.linhas.find(l => l.tipo === 'FATURA' && l.placa === p)
  const semLinhas = (p: string) => !c.linhas.some(l => l.placa === p)
  const t = c.totais
  const semFatura = t.porPlaca.filter(p => !faturaDa(p.placa) && !semLinhas(p.placa)).map(p => p.placa)

  return (
    <div className="space-y-3">
      {semFatura.length > 0 && <Aviso tipo="atencao">Sem fatura lançada: <b>{semFatura.join(', ')}</b>.</Aviso>}

      {/* Celular: um cartão por placa */}
      <ul className="sm:hidden bg-white border border-gray-200 rounded-xl divide-y divide-gray-100">
        {t.porPlaca.map(p => (
          <li key={p.placa} className="px-4 py-3 space-y-2">
            <div className="flex items-baseline justify-between gap-2">
              <span className="font-semibold text-gray-900 tabular-nums">{p.placa}</span>
              <span className="font-display text-lg font-semibold text-gray-900 tabular-nums">{brl(p.valor)}</span>
            </div>
            <dl className="grid grid-cols-3 gap-2 text-sm tabular-nums">
              <div><dt className="text-xs text-gray-500">Fatura</dt><dd className={faturaDa(p.placa) ? 'text-gray-800' : 'text-amber-800 font-medium'}>{faturaDa(p.placa) ? brl(p.fatura) : 'sem'}</dd></div>
              <div><dt className="text-xs text-gray-500">Créditos</dt><dd className="text-gray-800">{brl(p.creditos)}</dd></div>
              <div><dt className="text-xs text-gray-500">Vale-pedágio</dt><dd className="text-gray-800">{brl(p.vpo)}</dd></div>
            </dl>
            <div className="flex gap-2">
              <Botao variante="secundario" tamanho="sm" icone={Pencil} className="flex-1" onClick={() => onFatura(p.placa, faturaDa(p.placa))}>
                {faturaDa(p.placa) ? 'Mudar a fatura' : 'Lançar a fatura'}
              </Botao>
              {semLinhas(p.placa) && (
                <BotaoIcone icone={Trash2} perigo rotulo={`Tirar a placa ${p.placa}`} onClick={() => onTirarPlaca(p.placa)} className="border border-gray-300" />
              )}
            </div>
          </li>
        ))}
        <li className="px-4 py-3 bg-gray-50 flex items-baseline justify-between gap-2 font-semibold text-gray-900 tabular-nums">
          <span>Total</span><span>{brl(t.valor)}</span>
        </li>
      </ul>

      {/* Computador: a tabela, como no resumo da planilha */}
      <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-gray-50 text-gray-500 text-xs">
            <tr>
              <th className="text-left font-medium px-4 py-2">Placa</th>
              <th className="text-right font-medium px-3 py-2">Fatura</th>
              <th className="text-right font-medium px-3 py-2">Créditos</th>
              <th className="text-right font-medium px-3 py-2">Vale-pedágio</th>
              <th className="text-right font-medium px-3 py-2">Valor</th>
              <th className="w-24"><span className="sr-only">Ações</span></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100 tabular-nums">
            {t.porPlaca.map(p => (
              <tr key={p.placa} className="hover:bg-gray-50/70">
                <td className="px-4 py-1.5 font-medium text-gray-900">{p.placa}</td>
                <td className="px-3 py-1.5 text-right">{faturaDa(p.placa) ? brl(p.fatura) : <span className="text-amber-800 font-medium">sem</span>}</td>
                <td className="px-3 py-1.5 text-right">{brl(p.creditos)}</td>
                <td className="px-3 py-1.5 text-right">{brl(p.vpo)}</td>
                <td className="px-3 py-1.5 text-right font-semibold text-gray-900">{brl(p.valor)}</td>
                <td className="px-1 py-0.5">
                  <div className="flex justify-end">
                    <BotaoIcone icone={Pencil} rotulo={`Mudar a fatura da ${p.placa}`} onClick={() => onFatura(p.placa, faturaDa(p.placa))} />
                    {semLinhas(p.placa) && (
                      <BotaoIcone icone={Trash2} perigo rotulo={`Tirar a placa ${p.placa}`} onClick={() => onTirarPlaca(p.placa)} />
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
          <tfoot className="bg-gray-50 font-semibold text-gray-900 tabular-nums border-t-2 border-gray-200">
            <tr>
              <td className="px-4 py-2">Total</td>
              <td className="px-3 py-2 text-right">{brl(t.fatura)}</td>
              <td className="px-3 py-2 text-right">{brl(t.creditos)}</td>
              <td className="px-3 py-2 text-right">{brl(t.vpo)}</td>
              <td className="px-3 py-2 text-right">{brl(t.valor)}</td>
              <td />
            </tr>
          </tfoot>
        </table>
      </div>

      <Botao variante="fantasma" icone={Plus} onClick={onNovaPlaca}>Adicionar placa</Botao>
    </div>
  )
}

const valorTexto = (v?: number) => (v === undefined ? '' : v.toFixed(2).replace('.', ','))

function ModalLinha({ c, edicao, onClose, onSalvou }: {
  c: ControleDetalhe; edicao: EdicaoLinha; onClose: () => void; onSalvou: () => void
}) {
  const { tipo, linha } = edicao
  const [placa, setPlaca] = useState(linha?.placa ?? edicao.placa ?? '')
  const [outra, setOutra] = useState(false)
  const ultimaData = [...c.linhas].reverse().find(l => l.tipo === 'VPO' && l.data)?.data
  const [data, setData] = useState(linha?.data ?? ultimaData ?? `${c.ano}-${String(c.mes).padStart(2, '0')}-01`)
  const [valor, setValor] = useState(valorTexto(linha?.valor))
  const [salvando, setSalvando] = useState(false)

  const titulo = {
    FATURA: `Fatura da ${placa}`,
    VPO: linha ? 'Mudar vale-pedágio' : 'Novo vale-pedágio',
    CREDITO: linha ? 'Mudar crédito' : 'Novo crédito',
  }[tipo]

  const salvar = async () => {
    const p = placa.trim()
    if (!p) { toast.error('Escolha a placa.'); return }
    if (tipo === 'VPO' && !data) { toast.error('Informe a data.'); return }
    // Fatura em branco: tira a fatura da placa
    if (tipo === 'FATURA' && !valor.trim()) {
      if (!linha) { onClose(); return }
      setSalvando(true)
      try { await semPararApi.excluirLinha(linha.id); toast.success('Fatura tirada.'); onSalvou() }
      catch (e) { toast.error(await erroDaApi(e, 'Não consegui salvar.')) }
      finally { setSalvando(false) }
      return
    }
    if (!valor.trim()) { toast.error('Informe o valor.'); return }
    setSalvando(true)
    const dto = { tipo, placa: p, data: tipo === 'VPO' ? data : null, valor: lerNum(valor) }
    try {
      if (linha && tipo !== 'FATURA') await semPararApi.atualizarLinha(linha.id, dto)
      else await semPararApi.criarLinha(c.id, dto)   // fatura: lançar de novo troca o valor
      toast.success('Salvo.')
      onSalvou()
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui salvar.'))
    } finally {
      setSalvando(false)
    }
  }

  return (
    <Modal titulo={titulo} onClose={onClose} confirmarAoFechar>
      {tipo !== 'FATURA' && (
        <Campo rotulo="Placa">
          {outra ? (
            <input autoFocus value={placa} onChange={e => setPlaca(e.target.value.toUpperCase())}
                   placeholder="AAA-0A00" className={classeNumero} />
          ) : (
            <select value={placa} onChange={e => e.target.value === '__outra' ? (setOutra(true), setPlaca('')) : setPlaca(e.target.value)}
                    className={classeNumero}>
              <option value="">Escolha</option>
              {c.placas.map(p => <option key={p} value={p}>{p}</option>)}
              <option value="__outra">Outra placa...</option>
            </select>
          )}
        </Campo>
      )}
      {tipo === 'VPO' && (
        <Campo rotulo="Data">
          <input type="date" value={data} onChange={e => setData(e.target.value)} className={classeCampo} />
        </Campo>
      )}
      <Campo rotulo="Valor (R$)" dica={tipo === 'FATURA' ? 'Deixe em branco para tirar a fatura desta placa.' : undefined}>
        <input inputMode="decimal" value={valor} onChange={e => setValor(e.target.value)}
               placeholder="0,00" className={classeNumero} autoFocus={tipo === 'FATURA'} />
      </Campo>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}

function ModalPlaca({ c, onClose, onSalvou }: { c: ControleDetalhe; onClose: () => void; onSalvou: () => void }) {
  const [placa, setPlaca] = useState('')
  const [salvando, setSalvando] = useState(false)
  const salvar = async () => {
    const p = placa.trim().toUpperCase()
    if (p.replace(/[^A-Z0-9]/g, '').length !== 7) { toast.error('Placa com 7 letras e números (ex.: ABC-1D23).'); return }
    setSalvando(true)
    try { await semPararApi.placas(c.id, [...c.placas, p]); toast.success('Placa adicionada.'); onSalvou() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui adicionar.')) }
    finally { setSalvando(false) }
  }
  return (
    <Modal titulo="Adicionar placa" onClose={onClose} confirmarAoFechar descricao="Vira uma coluna nova na planilha deste mês.">
      <Campo rotulo="Placa">
        <input autoFocus value={placa} onChange={e => setPlaca(e.target.value.toUpperCase())}
               placeholder="AAA-0A00" className={classeNumero} />
      </Campo>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} rotulo="Adicionar" />
    </Modal>
  )
}
