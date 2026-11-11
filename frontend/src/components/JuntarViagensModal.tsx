import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Merge, Trash2 } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import { Carregando, ErroCarga } from './ui/Estados'
import { useConfirmar } from './ui/Confirmar'
import { SeloStatus, Selo } from './viagens/ViagensPecas'
import { brl, dataBr, num } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import {
  viagensApi, NOME_STATUS,
  type Duplicadas, type ItemJuntado, type OrigemViagem, type ParDuplicado, type ResumoDuplicada,
} from '../services/viagensCaminhaoApi'

// Viagens repetidas já gravadas (docs/planos/importacoes-mesclar.md, Parte 5; UI-21):
// cada par lado a lado (fica × sai), as diferenças e o que muda de viagem. Nada grava sem clique:
// "Juntar" por par, "Juntar as marcadas" só para pares de mesma data (o provável é um a um)
// e "Apagar" nas vazias.

const NOME_ORIGEM: Record<OrigemViagem, string> = {
  motorista: 'Enviada pelo motorista', escritorio: 'Lançada no escritório', importacao: 'Da planilha',
}
const periodo = (v: { dataSaida: string | null; dataChegada: string | null }) =>
  `${v.dataSaida ? dataBr(v.dataSaida) : 'sem data'}${v.dataChegada ? ` a ${dataBr(v.dataChegada)}` : ''}`

/** Busca a prévia; a tela usa para o aviso do topo e o modal para a lista */
export function useDuplicadas() {
  const [dados, setDados] = useState<Duplicadas | null>(null)
  const [falhou, setFalhou] = useState(false)
  const carregar = useCallback(() => {
    setFalhou(false)
    viagensApi.duplicadas().then(setDados).catch(() => setFalhou(true))
  }, [])
  useEffect(carregar, [carregar])
  return { dados, falhou, carregar }
}

export default function JuntarViagensModal({ dados, falhou, onRecarregar, onClose, onMudou }: {
  dados: Duplicadas | null; falhou: boolean
  onRecarregar: () => void; onClose: () => void
  /** Algo foi gravado: a tela recarrega a lista de viagens */
  onMudou: () => void
}) {
  const confirmar = useConfirmar()
  const [marcados, setMarcados] = useState<Set<string>>(new Set())
  const [enviando, setEnviando] = useState<string | null>(null)
  const [resultado, setResultado] = useState<ItemJuntado[] | null>(null)

  const pares = dados?.pares ?? []
  const vazias = dados?.vazias ?? []
  const chave = (p: ParDuplicado) => `${p.manterId}-${p.removerId}`
  const deData = pares.filter(p => p.grau === 'data')
  const escolhidos = deData.filter(p => marcados.has(chave(p)))

  const marcar = (p: ParDuplicado) => setMarcados(m => {
    const n = new Set(m); if (n.has(chave(p))) n.delete(chave(p)); else n.add(chave(p)); return n
  })

  const enviar = async (qual: string, lista: ParDuplicado[], apagar: number[]) => {
    setEnviando(qual)
    try {
      const itens = await viagensApi.juntar(lista.map(p => ({ manterId: p.manterId, removerId: p.removerId })), apagar)
      setResultado(itens)
      const falhas = itens.filter(i => !i.ok).length
      if (falhas) toast.error(`${falhas} ${falhas === 1 ? 'item não foi feito' : 'itens não foram feitos'}. Confira o aviso.`)
      else toast.success('Pronto. Confira o resultado na tela.')
      setMarcados(new Set())
      onMudou(); onRecarregar()
    } catch (e) { toast.error(await erroDaApi(e, 'Não consegui juntar.')) }
    finally { setEnviando(null) }
  }

  const juntarUm = async (p: ParDuplicado) => {
    if (!await confirmar({
      titulo: 'Juntar estas duas viagens?', perigo: false, confirmar: 'Juntar',
      texto: `${p.placa} · fica a viagem ${p.manterId} e a ${p.removerId} sai. ${p.motivo}`,
    })) return
    await enviar(chave(p), [p], [])
  }
  const juntarMarcados = async () => {
    if (!escolhidos.length) return
    if (!await confirmar({
      titulo: `Juntar ${escolhidos.length} ${escolhidos.length === 1 ? 'par' : 'pares'} de mesma data?`, perigo: false, confirmar: 'Juntar',
      texto: 'Em cada par fica uma viagem e a outra sai, como mostrado na lista.',
    })) return
    await enviar('marcados', escolhidos, [])
  }
  const apagar = async (id: number, texto: string) => {
    if (!await confirmar({ titulo: 'Apagar a viagem vazia?', texto: `${texto}. Isso não pode ser desfeito.` })) return
    await enviar(`vazia-${id}`, [], [id])
  }

  const nada = dados && pares.length === 0 && vazias.length === 0
  return (
    <Modal titulo="Viagens repetidas" onClose={onClose} largura="xl" fundoCinza
           descricao="A mesma viagem gravada duas vezes (planilha e motorista ou escritório). Juntar deixa uma só, sem perder retornos, controle de carregamento nem anotações."
           rodape={
             <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
               <Botao onClick={onClose} disabled={!!enviando}>Fechar</Botao>
               {deData.length > 0 && (
                 <Botao variante="primario" icone={Merge} onClick={juntarMarcados} disabled={!escolhidos.length || (!!enviando && enviando !== 'marcados')}
                        carregando={enviando === 'marcados'} textoCarregando="Juntando...">
                   Juntar as marcadas ({escolhidos.length})
                 </Botao>
               )}
             </div>
           }>
      {resultado && (
        <Aviso tipo={resultado.some(i => !i.ok) ? 'atencao' : 'sucesso'} titulo="O que foi feito" onFechar={() => setResultado(null)}
               itens={resultado.map(i => {
                 const quem = i.apagadaId != null ? `Viagem ${i.apagadaId}` : `Viagens ${i.manterId} + ${i.removerId}`
                 return <span className={i.ok ? '' : 'text-red-800'}>{quem}: {i.ok ? '' : 'não feito. '}{i.avisos.join(' ') || 'juntadas.'}</span>
               })} />
      )}

      {falhou ? <ErroCarga mensagem="Não consegui ver as viagens repetidas." onTentar={onRecarregar} />
        : !dados ? <Carregando linhas={3} rotulo="Procurando viagens repetidas" />
        : nada ? <p className="text-sm text-gray-700 bg-white border border-gray-200 rounded-lg p-3">Nenhuma viagem repetida nem vazia.</p>
        : (
          <>
            {pares.length > 0 && (
              <section className="space-y-3" aria-labelledby="pares-repetidos">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h3 id="pares-repetidos" className="font-semibold text-gray-900">
                    {pares.length} {pares.length === 1 ? 'par parece a mesma viagem' : 'pares parecem a mesma viagem'}
                  </h3>
                  {deData.length > 1 && (
                    <button type="button" className="text-sm font-medium text-tinta-700 hover:underline min-h-10"
                            onClick={() => setMarcados(escolhidos.length === deData.length ? new Set() : new Set(deData.map(chave)))}>
                      {escolhidos.length === deData.length ? 'Desmarcar todas' : 'Marcar todas de mesma data'}
                    </button>
                  )}
                </div>
                <ul className="space-y-3">
                  {pares.map(p => (
                    <CartaoPar key={chave(p)} par={p} marcado={marcados.has(chave(p))} onMarcar={() => marcar(p)}
                               enviando={enviando === chave(p)} ocupado={!!enviando} onJuntar={() => juntarUm(p)} />
                  ))}
                </ul>
              </section>
            )}

            {vazias.length > 0 && (
              <section className="space-y-2" aria-labelledby="viagens-vazias">
                <h3 id="viagens-vazias" className="font-semibold text-gray-900">
                  {vazias.length} {vazias.length === 1 ? 'viagem vazia' : 'viagens vazias'}
                </h3>
                <p className="text-sm text-gray-600">Sem volumes, retorno, abastecimento, despesa e com levou 0. Só sai se você apagar.</p>
                <ul className="bg-white border border-gray-200 rounded-lg divide-y divide-gray-100">
                  {vazias.map(v => {
                    const texto = `${v.placa} · viagem ${v.id} · ${periodo(v)}`
                    return (
                      <li key={v.id} className="flex flex-col sm:flex-row sm:items-center gap-2 justify-between px-3 py-2.5">
                        <div className="min-w-0 text-sm">
                          <p className="font-medium text-gray-900 tabular-nums">
                            <Link to={`/viagens-caminhoes/${v.id}`} className="hover:underline">{texto}</Link>
                          </p>
                          <p className="text-gray-600">
                            {v.motoristaNome ?? 'Sem motorista'} · {NOME_ORIGEM[v.origem] ?? v.origem} · {NOME_STATUS[v.status] ?? v.status}
                            {v.anotacoes > 0 && <> · {v.anotacoes} {v.anotacoes === 1 ? 'anotação' : 'anotações'} de carga</>}
                            {v.carregamento && <> · controle de carregamento ligado</>}
                          </p>
                        </div>
                        <Botao variante="perigoLeve" tamanho="sm" icone={Trash2} disabled={!!enviando && enviando !== `vazia-${v.id}`}
                               carregando={enviando === `vazia-${v.id}`} textoCarregando="Apagando..." onClick={() => apagar(v.id, texto)}>
                          Apagar
                        </Botao>
                      </li>
                    )
                  })}
                </ul>
              </section>
            )}
          </>
        )}
    </Modal>
  )
}

function CartaoPar({ par: p, marcado, onMarcar, enviando, ocupado, onJuntar }: {
  par: ParDuplicado; marcado: boolean; onMarcar: () => void; enviando: boolean; ocupado: boolean; onJuntar: () => void
}) {
  const m = p.mudamDeViagem
  const vaoJunto = [
    ...m.retornos.map(r => r.sai
      ? `Retorno ${r.transportadora ?? ''} ${brl(r.freteTotal)} sai (repetido, "fora do controle" com o mesmo valor).`
      : `Retorno ${r.transportadora ?? ''} ${brl(r.freteTotal)}${r.data ? ` de ${dataBr(r.data)}` : ''} passa para a que fica.`),
    ...m.carregamento.map(c => c.vaiPara === 'fica'
      ? `Controle de carregamento de ${dataBr(c.data)} passa para a que fica.`
      : `Controle de carregamento de ${dataBr(c.data)} fica solto (a que fica já tem um).`),
    ...m.anotacoes.map(a => `Anotação de carga (${a.tipo}${a.data ? `, ${dataBr(a.data)}` : ''}${a.valor ? `, ${brl(a.valor)}` : ''}) passa para a que fica.`),
  ]
  const valoresDaQueSai = p.valoresDe === p.removerId
  return (
    <li className={`bg-white border rounded-lg p-3 space-y-3 ${marcado ? 'border-tinta-500 ring-1 ring-tinta-500' : 'border-gray-200'}`}>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        {p.grau === 'data' ? (
          <label className="flex items-center gap-2 min-h-10 cursor-pointer">
            <input type="checkbox" className="h-5 w-5" checked={marcado} onChange={onMarcar} />
            <span className="font-semibold text-gray-900 tabular-nums">{p.placa} · {periodo(p.fica)}</span>
          </label>
        ) : <p className="font-semibold text-gray-900 tabular-nums">{p.placa} · {periodo(p.fica)}</p>}
        {p.grau === 'data' ? <Selo>Mesma data de saída</Selo> : <Selo tom="atencao">Provável: confira</Selo>}
      </div>
      {p.grau === 'provavel' && p.sinal && <p className="text-sm text-amber-800">Por que parece a mesma: {p.sinal}. Este par só se junta um a um.</p>}

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
        <LadoViagem rotulo="Fica" v={p.fica} valores={!valoresDaQueSai} />
        <LadoViagem rotulo="Sai" v={p.sai} valores={valoresDaQueSai} />
      </div>
      <p className="text-sm text-gray-700">{p.motivo} Situação depois de juntar: <b>{NOME_STATUS[p.situacaoFinal] ?? p.situacaoFinal}</b>.</p>

      {p.diferencas.length > 0 ? (
        <details className="text-sm">
          <summary className="cursor-pointer font-medium text-tinta-700 min-h-10 flex items-center">
            Ver {p.diferencas.length} {p.diferencas.length === 1 ? 'diferença' : 'diferenças'}
          </summary>
          <div className="mt-1 divide-y divide-gray-100 border border-gray-100 rounded-md">
            <div className="hidden sm:grid grid-cols-[1fr_1.4fr_1.4fr] gap-3 px-3 py-1.5 text-xs text-gray-500 bg-gray-50">
              <span>Campo</span><span>Viagem {p.manterId} (fica)</span><span>Viagem {p.removerId} (sai)</span>
            </div>
            {p.diferencas.map((d, k) => (
              <div key={k} className="grid grid-cols-1 sm:grid-cols-[1fr_1.4fr_1.4fr] gap-x-3 gap-y-0.5 px-3 py-2 tabular-nums">
                <span className="font-medium text-gray-900">{d.campo}</span>
                <span className={!valoresDaQueSai ? 'font-semibold text-amber-800 bg-amber-50 px-1 rounded' : 'text-gray-700'}>
                  <span className="sm:hidden text-gray-500 font-normal">Fica: </span>{d.fica || '(vazio)'}
                </span>
                <span className={valoresDaQueSai ? 'font-semibold text-amber-800 bg-amber-50 px-1 rounded' : 'text-gray-700'}>
                  <span className="sm:hidden text-gray-500 font-normal">Sai: </span>{d.sai || '(vazio)'}
                </span>
              </div>
            ))}
          </div>
          <p className="mt-1 text-xs text-gray-500">Em destaque, o valor que vale depois de juntar.</p>
        </details>
      ) : <p className="text-sm text-gray-600">Os valores das duas são iguais.</p>}

      {vaoJunto.length > 0 && (
        <ul className="text-sm text-gray-700 list-disc pl-5 space-y-0.5">{vaoJunto.map((t, i) => <li key={i}>{t}</li>)}</ul>
      )}

      <div className="flex justify-end">
        <Botao variante={p.grau === 'provavel' ? 'primario' : 'secundario'} tamanho="sm" icone={Merge} onClick={onJuntar}
               disabled={ocupado && !enviando} carregando={enviando} textoCarregando="Juntando...">
          Juntar este par
        </Botao>
      </div>
    </li>
  )
}

function LadoViagem({ rotulo, v, valores }: { rotulo: string; v: ResumoDuplicada; valores: boolean }) {
  return (
    <div className={`rounded-md border p-2.5 text-sm space-y-1 ${rotulo === 'Fica' ? 'border-tinta-300 bg-tinta-50' : 'border-gray-200 bg-gray-50'}`}>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <p className="font-semibold text-gray-900">
          {rotulo} · <Link to={`/viagens-caminhoes/${v.id}`} className="hover:underline">viagem {v.id}</Link>{v.aba ? ` (${v.aba})` : ''}
        </p>
        <SeloStatus status={v.status} />
      </div>
      <p className="text-gray-700">{NOME_ORIGEM[v.origem] ?? v.origem}{v.motoristaNome ? ` · ${v.motoristaNome}` : ''}{v.destino ? ` · ${v.destino}` : ''}</p>
      <p className="text-gray-700 tabular-nums">
        {num(v.caixas, 0)} caixas · diesel {brl(v.abastecimentos)} · despesas {brl(v.despesas)} · levou {brl(v.levou)}
      </p>
      <p className="text-gray-600 tabular-nums">
        {v.retornos} {v.retornos === 1 ? 'retorno' : 'retornos'} · {v.anotacoes} {v.anotacoes === 1 ? 'anotação' : 'anotações'}
        {v.carregamentos.length > 0 && <> · controle {v.carregamentos.map(dataBr).join(', ')}</>}
      </p>
      {valores && <p className="text-xs font-medium text-amber-800">Os valores desta valem depois de juntar.</p>}
    </div>
  )
}
