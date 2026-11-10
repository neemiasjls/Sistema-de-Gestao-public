import CabecalhoTela from '../components/ui/CabecalhoTela'
import Campo from '../components/ui/Campo'
import Aviso from '../components/ui/Aviso'
import AreaSoltar from '../components/ui/AreaSoltar'
import Botao, { BotaoIcone, classesBotao } from '../components/ui/Botao'
import { Link } from 'react-router-dom'
import { Download, FileCode2, FileSpreadsheet, FileUp, Percent, Upload, X } from 'lucide-react'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import AliquotasSeguro from '../components/AliquotasSeguro'
import { useCallback, useState } from 'react'
import toast from 'react-hot-toast'
import api, { downloadBlob } from '../services/api'
import PlanilhasSeguroGuardadas from '../components/PlanilhasSeguroGuardadas'
import SecaoRecolhivel from '../components/SecaoRecolhivel'
import { classeNumero } from '../components/EstoquePecas'
import { Modal } from '../components/ModalBase'
import BuscaPlaca from '../components/BuscaPlaca'
import { viagensApi } from '../services/viagensCaminhaoApi'
import { brl, dataBr } from '../utils/formato'

type SemPlaca = { cte: number | null; data: string; cliente: string; ufCliente: string; vFrete: number | null }

// converte "1.234,56" -> 1234.56
const parseBr = (v: string) => {
  const n = parseFloat(v.replace(/\./g, '').replace(',', '.'))
  return isNaN(n) ? 0 : n
}

export default function CTeExportPage() {
  const [arquivos, setArquivos] = useState<File[]>([])
  const [das, setDas]                 = useState('')
  const [iof, setIof]                 = useState('')
  const [servico, setServico]         = useState('')
  const [taxa, setTaxa]               = useState('')
  // Valor da fatura do seguro (SEG-05): obrigatório, dele sai a diferença dos seguros
  const [fatura, setFatura]           = useState('')
  const [aba, setAba]                 = useState<'planilha' | 'aliquotas'>('planilha')
  const idAbas = useAbas('seguro')
  const [gerando, setGerando]         = useState(false)
  // Muda a cada geração: a lista das guardadas recarrega e mostra a nova
  const [geradas, setGeradas]         = useState(0)
  // Avisos da geração (mês substituído, não guardou): ficam até fechar (D-07)
  const [avisos, setAvisos]           = useState<string[]>([])
  // Frete que veio sem placa: a tela pede a placa antes de gerar (cte → placa digitada)
  const [semPlaca, setSemPlaca]       = useState<SemPlaca[] | null>(null)
  const [placasManuais, setPlacasManuais] = useState<Record<string, string>>({})
  const [cavalos, setCavalos]         = useState<string[]>([])

  // Os XMLs chegam já filtrados pela AreaSoltar; o mesmo nome não entra duas vezes
  const processarArquivos = useCallback((xmls: File[]) => {
    setArquivos(prev => {
      const nomes = new Set(prev.map(f => f.name))
      return [...prev, ...xmls.filter(f => !nomes.has(f.name))]
    })
    toast.success(`${xmls.length} arquivo(s) adicionado(s).`)
  }, [])

  const gerar = async (manuais: Record<string, string> = {}, semPlacaOk = false) => {
    if (!arquivos.length) return
    if (!(parseBr(fatura) > 0)) { toast.error('Digite o valor da fatura do seguro.'); return }
    setGerando(true)
    try {
      const fd = new FormData()
      arquivos.forEach(f => fd.append('arquivos', f))
      const preenchidas = Object.fromEntries(Object.entries(manuais).filter(([, p]) => p.trim()))
      if (Object.keys(preenchidas).length) fd.append('placas', JSON.stringify(preenchidas))

      const params = new URLSearchParams({
        das:               String(parseBr(das)),
        iofSeguros:        String(parseBr(iof)),
        servicoEscritorio: String(parseBr(servico)),
        taxaBSoft:         String(parseBr(taxa)),
        faturaSeguro:      String(parseBr(fatura)),
      })
      if (semPlacaOk) params.set('semPlacaOk', 'true')

      const resp = await api.post(
        `/api/cte/exportar-planilha?${params.toString()}`,
        fd,
        { headers: { 'Content-Type': 'multipart/form-data' }, responseType: 'blob' }
      )

      // nome vem do servidor (ex: "09-SEGURO SETEMBRO 2026.xlsx")
      const header = resp.headers['x-filename'] as string | undefined
      const data = new Date().toLocaleDateString('pt-BR').replace(/\//g, '')
      const filename = header ? decodeURIComponent(header) : `SEGURO-${data}.xlsx`
      downloadBlob(resp.data as Blob, filename)
      toast.success('Planilha SG gerada e baixada.')
      setSemPlaca(null)

      // A planilha também fica guardada no sistema. Mês que já estava guardado
      // é trocado; se não deu para guardar, o aviso diz (o arquivo saiu igual).
      const avisos = resp.headers['x-avisos'] as string | undefined
      setAvisos(avisos ? decodeURIComponent(avisos).split(' | ') : [])
      setGeradas(n => n + 1)
    } catch (e) {
      // 422 = frete sem placa: abre a janela para escolher a placa de cada um
      const r = (e as { response?: { status?: number; data?: Blob } }).response
      if (r?.status === 422 && r.data) {
        try {
          const corpo = JSON.parse(await r.data.text()) as { semPlaca: SemPlaca[] }
          setSemPlaca(corpo.semPlaca)
          if (!cavalos.length)
            viagensApi.placas().then(ps => setCavalos(ps.filter(p => p.ativo && p.tipo === 'Cavalo').map(p => p.placa).sort()))
              .catch(() => toast.error('Não consegui carregar as placas do cadastro.'))
          return
        } catch { /* cai no erro comum */ }
      }
      if (r?.status === 400 && r.data) {
        try {
          const corpo = JSON.parse(await r.data.text()) as { erro?: string }
          if (corpo.erro) { toast.error(corpo.erro); return }
        } catch { /* cai no erro comum */ }
      }
      toast.error('Não consegui gerar a planilha.')
    } finally {
      setGerando(false)
    }
  }

  const campo = (
    label: string, value: string, set: (v: string) => void, placeholder: string, hint?: string,
  ) => (
    <Campo rotulo={label} dica={hint}>
      <input
        value={value}
        onChange={e => set(e.target.value)}
        placeholder={placeholder}
        inputMode="decimal"
        className={`${classeNumero} text-right`}
      />
    </Campo>
  )

  return (
    <div className="space-y-6 max-w-5xl">
      <CabecalhoTela
        descricao="A planilha SG do mês e as que já estão guardadas."
        acoes={
          <Link to="/cte-export/importar" className={classesBotao('secundario')}>
            <Upload size={18} aria-hidden /> Importar CT-es para o resumo
          </Link>
        }
      />

      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Seguro"
            opcoes={[['planilha', 'Planilha SG', FileSpreadsheet], ['aliquotas', 'Alíquotas', Percent]]} />

      {aba === 'aliquotas' ? (
        <PainelAba id={idAbas} valor={aba}><AliquotasSeguro /></PainelAba>
      ) : (
      <PainelAba id={idAbas} valor={aba}>
      {/* Gerar a planilha: fechada por padrão (SEG-01); abre ao tocar e mostra tudo */}
      <SecaoRecolhivel
        titulo="Planilha SG (Seguro / ICMS)"
        icone={FileSpreadsheet}
        className="max-w-3xl"
        descricao={arquivos.length > 0
          ? `${arquivos.length} ${arquivos.length === 1 ? 'XML escolhido' : 'XMLs escolhidos'}, falta gerar`
          : 'Gerar a planilha nova do mês a partir dos XMLs de CT-e'}
      >
      <div className="space-y-5">
      <p className="text-sm text-gray-600">
        Gera um único arquivo com a aba <b>MÊS-GERAL</b>, uma aba por <b>placa</b> e o <b>RESUMO</b>,
        já com as fórmulas.
      </p>

      {/* 1. Valores do mês (digitados antes de gerar) */}
      <fieldset className="space-y-3">
        <legend className="text-sm font-semibold text-gray-900">Valores do mês</legend>
        <p className="text-sm text-gray-600 -mt-1">
          Deixe vazio para ficar em branco (menos a fatura do seguro). A <b>quantidade de caminhões</b> é contada sozinha, pelas placas dos CT-es.
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 gap-y-3">
          {campo('Valor do DAS (R$)', das, setDas, 'Ex: 42.696,00',
                 'Calcula a alíquota do ICMS frete para somar exato com esse valor.')}
          {campo('IOF Seguros (R$)', iof, setIof, 'Ex: 331,22')}
          {campo('Serviços Escritório (R$)', servico, setServico, 'Ex: 1.100,00')}
          {campo('Taxa B Soft (R$)', taxa, setTaxa, 'Ex: 698,31')}
          {campo('Valor da fatura do seguro (R$)', fatura, setFatura, 'Ex: 2.450,90',
                 'Obrigatório. A diferença dos seguros sai dele.')}
        </div>
      </fieldset>

      {/* 2. Os XMLs */}
      <div className="space-y-3">
        <AreaSoltar aceita={['.xml']} onArquivos={processarArquivos} icone={FileUp}
                    titulo="Arraste os XMLs de CT-e do mês aqui" textoBotao="Escolher XMLs"
                    dica="O mesmo arquivo não entra duas vezes" />

        {arquivos.length > 0 && (
          <div className="rounded-xl border border-gray-200 overflow-hidden">
            <div className="flex items-center justify-between gap-2 pl-4 pr-2 py-1.5 bg-gray-50 border-b border-gray-200">
              <span className="text-sm font-medium text-gray-800 tabular-nums">
                {arquivos.length} {arquivos.length === 1 ? 'arquivo escolhido' : 'arquivos escolhidos'}
              </span>
              <Botao variante="fantasma" tamanho="sm" onClick={() => setArquivos([])}>Limpar tudo</Botao>
            </div>
            <ul className="max-h-56 overflow-y-auto divide-y divide-gray-100">
              {arquivos.map((f, i) => (
                <li key={f.name} className="flex items-center gap-2 pl-4 pr-1">
                  <FileCode2 size={15} className="text-gray-400 shrink-0" aria-hidden />
                  <span className="flex-1 min-w-0 truncate text-sm text-gray-700">{f.name}</span>
                  <BotaoIcone icone={X} rotulo={`Tirar ${f.name}`} perigo onClick={() => setArquivos(prev => prev.filter((_, j) => j !== i))} />
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>

      {/* 3. Gerar */}
      <Botao variante="primario" icone={Download} bloco className="sm:w-auto" onClick={() => gerar()} carregando={gerando} textoCarregando="Gerando..."
             disabled={arquivos.length === 0 || !(parseBr(fatura) > 0)}
             title={arquivos.length === 0 ? 'Escolha os XMLs primeiro' : !(parseBr(fatura) > 0) ? 'Digite o valor da fatura do seguro' : undefined}>
        Gerar planilha SG (.xlsx)
      </Botao>
      </div>
      </SecaoRecolhivel>

      {semPlaca && (
        <Modal titulo="Frete sem placa" onClose={() => setSemPlaca(null)} largura="lg" confirmarAoFechar={Object.values(placasManuais).some(Boolean)}>
          <p className="text-sm text-gray-600">
            {semPlaca.length === 1 ? 'Este CT-e veio' : `Estes ${semPlaca.length} CT-es vieram`} sem placa no XML.
            Escolha a placa de cada um para o frete entrar na aba do caminhão.
          </p>
          <ul className="space-y-3">
            {semPlaca.map((l, i) => {
              const k = String(l.cte ?? `sem-${i}`)
              return (
                <li key={k} className="grid grid-cols-1 sm:grid-cols-[1fr_12rem] gap-2 items-center border border-gray-200 rounded-lg p-3">
                  <div className="text-sm min-w-0">
                    <p className="font-medium text-gray-800">CT-e {l.cte ?? 'sem número'} · {l.data ? dataBr(l.data) : 'sem data'}</p>
                    <p className="text-gray-600 break-words">{l.cliente || 'Cliente não veio'}{l.ufCliente ? ` (${l.ufCliente})` : ''}
                      {l.vFrete != null && ` · frete ${brl(l.vFrete)}`}</p>
                  </div>
                  {l.cte != null
                    ? <BuscaPlaca rotulo={`Placa do CT-e ${l.cte}`} valor={placasManuais[k] ?? ''} opcoes={cavalos} placeholder="Placa do cavalo"
                                  className={`${classeNumero} w-full`} onChange={p => setPlacasManuais(v => ({ ...v, [k]: p }))} />
                    : <p className="text-xs text-amber-800">Sem número do CT-e: não dá para ligar a placa.</p>}
                </li>
              )
            })}
          </ul>
          {(() => {
            const validas = (p?: string) => !!p && cavalos.includes(p)
            const faltam = semPlaca.filter((l, i) => !validas(placasManuais[String(l.cte ?? `sem-${i}`)])).length
            return (
              <div className="flex flex-col sm:flex-row gap-2 pt-1">
                <Botao variante="primario" className="flex-1" carregando={gerando} textoCarregando="Gerando..." disabled={faltam > 0}
                       onClick={() => gerar(placasManuais)}>
                  {faltam > 0 ? `Falta escolher ${faltam} ${faltam === 1 ? 'placa' : 'placas'}` : 'Gerar com essas placas'}
                </Botao>
                <Botao variante="fantasma" disabled={gerando} onClick={() => gerar(placasManuais, true)}>Gerar sem placa</Botao>
              </div>
            )
          })()}
        </Modal>
      )}

      {avisos.length > 0 && <Aviso tipo="atencao" titulo="Confira a planilha gerada" itens={avisos} onFechar={() => setAvisos([])} />}

      <div className="pt-4 border-t border-gray-200">
        <PlanilhasSeguroGuardadas atualizar={geradas} />
      </div>
      </PainelAba>
      )}
    </div>
  )
}
