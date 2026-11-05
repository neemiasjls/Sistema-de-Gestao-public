import { useCallback, useEffect, useState } from 'react'
import { FileUp, Pencil, Plus, Trash2, Truck } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import { Abas, PainelAba, useAbas } from '../components/ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { Modal, Campo, BotoesModal } from '../components/ModalBase'
import { classeCampo } from '../components/EstoquePecas'
import ImportarCrlvModal from '../components/ImportarCrlvModal'
import { Selo } from '../components/viagens/ViagensPecas'
import { socioColor, type Socio } from '../services/api'
import { erroDaApi } from '../services/semPararApi'
import { viagensApi, normalizarPlaca, placaValida, type PlacaFrota } from '../services/viagensCaminhaoApi'

// Cadastro de placas (VIA-20): só a placa, se é cavalo ou carreta e quem cuida dela.
// O painel do motorista só aceita placa daqui. Placa com viagens só desativa.

type Tipo = 'Cavalo' | 'Carreta'
type Aba = 'cavalos' | 'carretas'
const SOCIOS: Socio[] = ['Bento', 'Caio', 'Dudu']
const TIPO_DA_ABA: Record<Aba, Tipo> = { cavalos: 'Cavalo', carretas: 'Carreta' }

export default function PlacasPage() {
  const [aba, setAba] = useState<Aba>('cavalos')
  const [lista, setLista] = useState<PlacaFrota[] | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [edicao, setEdicao] = useState<{ p: PlacaFrota | null } | null>(null)
  const [importando, setImportando] = useState(false)
  const idAbas = useAbas('placas')
  const confirmar = useConfirmar()

  const carregar = useCallback(() => {
    setFalhou(false)
    viagensApi.placas().then(setLista).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])

  const tipo = TIPO_DA_ABA[aba]
  const doTipo = (lista ?? []).filter(p => p.tipo === tipo)

  const alternar = async (p: PlacaFrota) => {
    try {
      await viagensApi.salvarPlaca(p.id, { placa: p.placa, tipo: p.tipo, responsavel: p.responsavel, ativo: !p.ativo })
      toast.success(p.ativo ? `${p.placa} desativada.` : `${p.placa} ativada.`)
      carregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui alterar.')) }
  }

  const apagar = async (p: PlacaFrota) => {
    if (!await confirmar({
      titulo: `Apagar a placa ${p.placa}?`,
      texto: p.viagens > 0 ? `Ela tem ${p.viagens} ${p.viagens === 1 ? 'viagem' : 'viagens'}: em vez de apagar, fica desativada.` : 'O motorista deixa de poder escolher esta placa.',
      confirmar: p.viagens > 0 ? 'Desativar' : 'Apagar',
    })) return
    try { const r = await viagensApi.excluirPlaca(p.id); toast.success(r.mensagem); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  return (
    <div className="space-y-5 max-w-4xl">
      <CabecalhoTela
        descricao="As placas que o motorista pode escolher na folha da viagem, cavalo e carreta. O responsável é o sócio que paga a parte dessa placa nas despesas divididas."
        acoes={<>
          <Botao variante="secundario" icone={FileUp} onClick={() => setImportando(true)}>Importar documento (CRLV)</Botao>
          <Botao variante="primario" icone={Plus} onClick={() => setEdicao({ p: null })}>Nova placa</Botao>
        </>} />
      <Abas id={idAbas} valor={aba} onChange={setAba} rotulo="Tipo de placa"
            opcoes={[['cavalos', 'Cavalos', Truck], ['carretas', 'Carretas']] as const} />
      <PainelAba id={idAbas} valor={aba}>
        {falhou ? <ErroCarga mensagem="Não consegui carregar as placas." onTentar={carregar} />
          : !lista ? <Carregando linhas={4} rotulo="Carregando as placas" />
          : doTipo.length === 0 ? <Vazio icone={Truck} titulo={tipo === 'Carreta' ? 'Nenhuma carreta cadastrada' : 'Nenhum cavalo cadastrado'} />
          : (
            <ul className="bg-white border border-gray-200 rounded-xl divide-y divide-gray-100 overflow-hidden">
              {doTipo.map(p => (
                <li key={p.id} className={`px-4 py-3 flex flex-wrap items-center gap-x-3 gap-y-2 ${p.ativo ? '' : 'bg-gray-50'}`}>
                  <div className="flex-1 min-w-[10rem]">
                    <p className={`font-semibold tabular-nums ${p.ativo ? 'text-gray-900' : 'text-gray-500 line-through'}`}>{p.placa}</p>
                    <p className="text-sm text-gray-600">
                      {SOCIOS.includes(p.responsavel as Socio)
                        ? <span className={`inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-semibold ${socioColor(p.responsavel as Socio).border} ${socioColor(p.responsavel as Socio).bg} ${socioColor(p.responsavel as Socio).text}`}>
                            <span className={`w-2 h-2 rounded-full ${socioColor(p.responsavel as Socio).dot}`} />{p.responsavel}</span>
                        : <span className="text-gray-500">Sem responsável</span>}
                      {p.viagens > 0 && <> · {p.viagens} {p.viagens === 1 ? 'viagem' : 'viagens'}</>}
                    </p>
                    {!p.ativo && <div className="mt-1"><Selo>Desativada</Selo></div>}
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <BotaoIcone icone={Pencil} rotulo={`Editar ${p.placa}`} onClick={() => setEdicao({ p })} />
                    <BotaoIcone icone={Trash2} rotulo={`Apagar ${p.placa}`} onClick={() => apagar(p)} />
                    <Botao variante={p.ativo ? 'perigoLeve' : 'secundario'} tamanho="sm" onClick={() => alternar(p)}>
                      {p.ativo ? 'Desativar' : 'Reativar'}
                    </Botao>
                  </div>
                </li>
              ))}
            </ul>
          )}
      </PainelAba>
      {importando && <ImportarCrlvModal onClose={() => setImportando(false)} onFeito={() => { setImportando(false); carregar() }} />}
      {edicao && <PlacaModal p={edicao.p} tipoInicial={tipo} onClose={() => setEdicao(null)} onSalvou={() => { setEdicao(null); carregar() }} />}
    </div>
  )
}

function PlacaModal({ p, tipoInicial, onClose, onSalvou }: { p: PlacaFrota | null; tipoInicial: Tipo; onClose: () => void; onSalvou: () => void }) {
  const [placa, setPlaca] = useState(p?.placa ?? '')
  const [tipo, setTipo] = useState<Tipo>(p?.tipo ?? tipoInicial)
  const [responsavel, setResponsavel] = useState(p?.responsavel ?? '')
  const [ativo, setAtivo] = useState(p?.ativo ?? true)
  const [salvando, setSalvando] = useState(false)

  const salvar = async () => {
    if (!placaValida(placa)) { toast.error('Placa inválida (ex.: XAI-0F06).'); return }
    setSalvando(true)
    try {
      await viagensApi.salvarPlaca(p?.id ?? null, { placa: normalizarPlaca(placa), tipo, responsavel: responsavel.trim() || null, ativo })
      toast.success('Placa salva.')
      onSalvou()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui salvar.')) }
    finally { setSalvando(false) }
  }

  return (
    <Modal titulo={p ? 'Editar placa' : 'Nova placa'} onClose={onClose} confirmarAoFechar>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Campo rotulo="Placa">
          <input value={placa} onChange={e => setPlaca(e.target.value.toUpperCase())} className={classeCampo} placeholder="XAI-0F06" autoCapitalize="characters" />
        </Campo>
        <Campo rotulo="Tipo">
          <select value={tipo} onChange={e => setTipo(e.target.value as Tipo)} className={classeCampo}>
            <option value="Cavalo">Cavalo</option>
            <option value="Carreta">Carreta</option>
          </select>
        </Campo>
      </div>
      <Campo rotulo="Responsável" dica="Opcional. O sócio que paga a parte dessa placa nas despesas divididas.">
        <select value={responsavel} onChange={e => setResponsavel(e.target.value)} className={classeCampo}>
          <option value="">Sem responsável</option>
          {SOCIOS.map(s => <option key={s} value={s}>{s}</option>)}
        </select>
      </Campo>
      <label className="inline-flex items-center gap-2 min-h-11 text-sm">
        <input type="checkbox" checked={ativo} onChange={e => setAtivo(e.target.checked)} className="w-5 h-5" /> Ativa (o motorista pode escolher)
      </label>
      <BotoesModal salvando={salvando} onSalvar={salvar} onClose={onClose} />
    </Modal>
  )
}
