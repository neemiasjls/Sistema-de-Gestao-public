import { useCallback, useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ArrowLeftRight, Download, Link2, Pencil, Plus, Trash2, Upload } from 'lucide-react'
import toast from 'react-hot-toast'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import AreaSoltar from '../components/ui/AreaSoltar'
import Aviso from '../components/ui/Aviso'
import { Segmentado } from '../components/ui/Abas'
import { Carregando, ErroCarga, Vazio } from '../components/ui/Estados'
import { useConfirmar } from '../components/ui/Confirmar'
import { Card } from '../components/ModalBase'
import { brl, dataBr, num } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import { retornosApi, type RetornosMes, type ResultadoRetornos, type RetornoEntrada, type AnotacaoPendente, type Retorno } from '../services/viagensCaminhaoApi'
import {
  NavegaMes, RetornoModal, Selo, ValorOuAviso, ListaResultado, retornoVazio, entradaDoRetorno, mesExtenso,
} from '../components/viagens/ViagensPecas'

// Retornos: a planilha "CONTROLE MENSAL DE RETORNOS" como tela. Cada frete de
// retorno com o cálculo da coluna J (frete total), por mês. O que o motorista
// anota como "outra carga" não vira retorno (VIA-22): o sistema liga a anotação ao
// retorno importado pela placa e pela data; aqui aparece o que ficou sem par ou em
// dúvida, para ligar à mão. A planilha sai em Exportar Excel.

type Filtro = 'todos' | 'conferir' | 'semViagem'

export default function RetornosPage() {
  const [params, setParams] = useSearchParams()
  const agora = new Date()
  const mes = Number(params.get('mes')) || agora.getMonth() + 1
  const ano = Number(params.get('ano')) || agora.getFullYear()
  const [dados, setDados] = useState<RetornosMes | null>(null)
  const [falhou, setFalhou] = useState(false)
  const [filtro, setFiltro] = useState<Filtro>('todos')
  const [edicao, setEdicao] = useState<{ id: number | null; inicial: RetornoEntrada; retorno?: Retorno } | null>(null)
  const [importando, setImportando] = useState(false)
  const [mostrarImportar, setMostrarImportar] = useState(false)
  const [resultado, setResultado] = useState<ResultadoRetornos[] | null>(null)
  const [exportando, setExportando] = useState(false)
  const confirmar = useConfirmar()

  const carregar = useCallback(() => {
    setFalhou(false)
    setDados(null)
    retornosApi.listar(ano, mes).then(setDados).catch(() => setFalhou(true))
  }, [ano, mes])
  useEffect(carregar, [carregar])

  const irMes = (m: number, a: number) => setParams({ mes: String(m), ano: String(a) })

  const importar = async (arquivos: File[]) => {
    if (arquivos.length === 0) return
    setImportando(true)
    try {
      const r = await retornosApi.importar(arquivos)
      setResultado(r)
      const novos = r.reduce((s, x) => s + (x.novos ?? 0), 0)
      toast.success(novos > 0 ? `${novos} retornos novos.` : 'Nada novo: as linhas já estavam aqui.')
      carregar()
    } catch (e) {
      toast.error(await erroDaApi(e, 'Não consegui importar as planilhas.'))
    } finally { setImportando(false) }
  }

  const exportar = async () => {
    setExportando(true)
    try { await retornosApi.exportar(ano, mes) }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui gerar a planilha.')) }
    finally { setExportando(false) }
  }

  const excluir = async (id: number, nome: string) => {
    if (!await confirmar({ titulo: 'Apagar o retorno?', texto: nome })) return
    try { await retornosApi.excluir(id); toast.success('Retorno apagado.'); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui apagar.')) }
  }

  const ligarAnotacao = async (a: AnotacaoPendente, retornoId: number) => {
    try { await retornosApi.ligarAnotacao(a.id, retornoId); toast.success('Anotação ligada ao retorno.'); carregar() }
    catch (e) { toast.error(await erroDaApi(e, 'Não consegui ligar.')) }
  }

  const lista = (dados?.retornos ?? []).filter(x =>
    filtro === 'conferir' ? !x.retorno.conferido : filtro === 'semViagem' ? !x.retorno.viagemId && !x.retorno.foraDoControle : true)

  return (
    <div className="space-y-5 max-w-[96rem]">
      <CabecalhoTela
        descricao={<>Os fretes de retorno do mês, como na planilha <b>CONTROLE MENSAL DE RETORNOS</b>: frete total = valor do frete + adicional − pedágio − desconto − desconto do seguro. O retorno vem só da planilha importada; a outra carga que o motorista anota no painel é ligada a ele pela placa e pela data.</>}
        acoes={<>
          <Botao variante="secundario" icone={Upload} onClick={() => setMostrarImportar(v => !v)}>Importar planilhas</Botao>
          <Botao variante="secundario" icone={Download} onClick={exportar} carregando={exportando} textoCarregando="Gerando...">Exportar Excel</Botao>
          <Botao variante="primario" icone={Plus} onClick={() => setEdicao({ id: null, inicial: { ...retornoVazio(), data: `${ano}-${String(mes).padStart(2, '0')}-${String(Math.min(agora.getDate(), 28)).padStart(2, '0')}` } })}>Novo retorno</Botao>
        </>}
      />

      {mostrarImportar && (
        <AreaSoltar aceita={['.xlsx', '.xls']} onArquivos={importar} carregando={importando} textoCarregando="Lendo as planilhas..."
                    titulo="Arraste as planilhas de retornos aqui" icone={ArrowLeftRight}
                    dica="09-R-SETEMBRO -26.xlsx e as outras do mês. Importar de novo não duplica." />
      )}
      {resultado && (
        <ListaResultado titulo="Planilhas de retornos" onFechar={() => setResultado(null)} itens={resultado.map((r, i) => ({
          chave: `${r.arquivo}-${i}`, nome: r.arquivo, erro: r.erro, avisos: r.avisos, problema: r.confere === false,
          linha: r.erro ? null : <>
            {r.mes && r.ano ? mesExtenso(r.mes, r.ano) : ''} · {r.linhas} linhas · {r.novos ?? 0} novos, {r.atualizados ?? 0} atualizados, {r.iguais ?? 0} iguais
            {(r.vinculados ?? 0) > 0 && <> · {r.vinculados} ligadas à viagem</>}
            {(r.anotacoesLigadas ?? 0) > 0 && <> · {r.anotacoesLigadas} anotações do motorista ligadas</>}
            {(r.anotacoesPendentes ?? 0) > 0 && <> · <span className="text-amber-800 font-medium">{r.anotacoesPendentes} anotações sem par ou em dúvida</span></>}
            <br />Frete total {brl(r.total ?? 0)}
            {r.totalPlanilha != null && <> · na planilha {brl(r.totalPlanilha)} {r.confere ? <span className="text-emerald-800 font-medium">confere</span> : <span className="text-red-800 font-medium">não confere</span>}</>}
          </>,
        }))} />
      )}

      <div className="flex flex-col sm:flex-row sm:items-center gap-3 justify-between">
        <NavegaMes mes={mes} ano={ano} onChange={irMes} />
        <Segmentado rotulo="Mostrar" valor={filtro} onChange={setFiltro}
                    opcoes={[['todos', 'Todos'], ['conferir', `A conferir${dados?.aConferir ? ` (${dados.aConferir})` : ''}`], ['semViagem', 'Sem viagem']] as const} />
      </div>

      {falhou ? (
        <ErroCarga mensagem="Não consegui carregar os retornos." onTentar={carregar} />
      ) : !dados ? (
        <Carregando linhas={6} rotulo="Carregando os retornos" />
      ) : (
        <>
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
            <Card titulo="Frete total do mês" destaque>{brl(dados.total)}</Card>
            <Card titulo="Fretes">{dados.quantidade}</Card>
            <Card titulo="A conferir">{dados.aConferir}</Card>
            <Card titulo="Sem viagem ligada">{dados.semViagem}</Card>
          </div>
          {dados.aConferir > 0 && filtro !== 'conferir' && (
            <Aviso tipo="atencao" acao={<Botao variante="secundario" tamanho="sm" onClick={() => setFiltro('conferir')}>Ver os que faltam</Botao>}>
              {dados.aConferir} {dados.aConferir === 1 ? 'retorno lançado pelo motorista falta' : 'retornos lançados pelos motoristas faltam'} conferir.
            </Aviso>
          )}
          {dados.anotacoesPendentes.length > 0 && (
            <section className="bg-white border border-amber-200 rounded-xl p-3 space-y-2" aria-labelledby="anotacoes-pendentes">
              <h2 id="anotacoes-pendentes" className="font-semibold text-gray-900">Anotações dos motoristas sem retorno ligado ({dados.anotacoesPendentes.length})</h2>
              <p className="text-sm text-gray-600">O sistema não achou um retorno importado só para cada uma destas. Ligue à mão ou importe a planilha que falta.</p>
              <ul className="divide-y divide-gray-100">
                {dados.anotacoesPendentes.map(a => (
                  <li key={a.id} className="py-2 space-y-1">
                    <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
                      <Selo tom="atencao">{a.situacao === 'conferir' ? 'Para conferir' : a.situacao === 'solto' ? 'Solto no escritório' : 'Sem par'}</Selo>
                      <span className="font-medium text-gray-900">{a.data ? dataBr(a.data) : 'Sem data'} · {a.placa} · {a.motorista}</span>
                      <span className="text-gray-600 break-words">{a.transportadora ?? 'Sem transportadora'} · {[a.origem, a.destino].filter(Boolean).join(' / ') || 'sem origem e destino'} · anotou {brl(a.valor)}</span>
                      <Link to={`/viagens-caminhoes/${a.viagemId}`} className="text-xs font-medium text-tinta-700 underline">Viagem de {a.viagemSaida ? dataBr(a.viagemSaida) : 'data não informada'}</Link>
                    </div>
                    {a.aviso && <p className="text-xs text-gray-600">{a.aviso}</p>}
                    {a.candidatos.length === 0 ? <p className="text-xs text-gray-600">Nenhum retorno livre desta placa até 3 dias da data.</p> : (
                      <ul className="space-y-1">
                        {a.candidatos.map(r => (
                          <li key={r.id} className="flex flex-col sm:flex-row sm:items-center gap-2 text-sm bg-gray-50 rounded-lg px-3 py-1.5">
                            <span className="flex-1 min-w-0 break-words">{dataBr(r.data)} · {r.transportadora ?? 'Sem transportadora'} · {[r.observacoes, r.destino].filter(Boolean).join(' / ')} · frete {brl(r.valorFrete)}</span>
                            <span className="tabular-nums font-semibold">{brl(r.freteTotal)}</span>
                            <Botao variante="secundario" tamanho="sm" icone={Link2} onClick={() => ligarAnotacao(a, r.id)}>Ligar a este retorno</Botao>
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ul>
            </section>
          )}
          {lista.length === 0 ? (
            <Vazio icone={ArrowLeftRight} titulo={filtro === 'todos' ? `Nenhum retorno em ${mesExtenso(mes, ano)}` : 'Nada aqui'}>
              {filtro === 'todos' ? 'Lance um retorno novo ou importe a planilha do mês.' : 'Troque o filtro para ver os outros.'}
            </Vazio>
          ) : (
            <>
              {/* Celular: cartões */}
              <ul className="sm:hidden space-y-2">
                {lista.map(({ retorno: r, viagem, anotacao }) => (
                  <li key={r.id} className="bg-white border border-gray-200 rounded-xl p-3 space-y-1">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="font-semibold text-gray-900 break-words">{r.transportadora ?? 'Sem transportadora'}</p>
                        <p className="text-sm text-gray-600">{dataBr(r.data)} · {r.placa ?? 'sem placa'} · {r.motorista ?? ''}</p>
                        <p className="text-sm text-gray-600 break-words">{[r.observacoes, r.destino].filter(Boolean).join(' / ')}</p>
                      </div>
                      <p className="font-display text-lg font-semibold tabular-nums whitespace-nowrap">{brl(r.freteTotal)}</p>
                    </div>
                    <dl className="grid grid-cols-2 gap-x-3 gap-y-0.5 text-sm">
                      <dt className="text-gray-600">Frete contratado</dt><dd className="text-right tabular-nums">{brl(r.valorFrete)}</dd>
                      <dt className="text-gray-600">Descarga</dt><dd className="text-right tabular-nums">{r.adicional ? brl(r.adicional) : '—'}</dd>
                      <dt className="text-gray-600">Valor do contrato</dt><dd className="text-right"><ValorOuAviso valor={r.valorContrato} aviso={r.contratoAviso ?? 'Sem contrato ligado'} /></dd>
                      <dt className="text-gray-600">Adiantamento</dt><dd className="text-right"><ValorOuAviso valor={r.adiantamentoDescarga} aviso={r.contratoAviso ?? 'Sem contrato ligado'} /></dd>
                      <dt className="text-gray-600">Falta receber no TIP</dt><dd className="text-right"><ValorOuAviso valor={r.reembolsoDescarga} aviso={r.contratoAviso ?? 'Sem contrato ou descarga'} /></dd>
                    </dl>
                    <div className="flex flex-wrap gap-1">
                      {!r.conferido && <Selo tom="atencao">A conferir</Selo>}
                      {r.foraDoControle && <Selo>Fora do controle</Selo>}
                      {r.origem === 'motorista' && <Selo tom="info">Do motorista</Selo>}
                      {anotacao && <Selo tom="info">Ligado à anotação do motorista ({brl(anotacao.valor)})</Selo>}
                      {viagem ? <Link to={`/viagens-caminhoes/${viagem.id}`} className="text-xs font-medium text-tinta-700 underline">Viagem de {viagem.dataSaida ? dataBr(viagem.dataSaida) : '?'}</Link>
                        : !r.foraDoControle && <Selo>Sem viagem</Selo>}
                    </div>
                    <div className="flex gap-1 justify-end">
                      <BotaoIcone icone={Pencil} rotulo="Editar retorno" onClick={() => setEdicao({ id: r.id, inicial: entradaDoRetorno(r), retorno: r })} />
                      <BotaoIcone icone={Trash2} rotulo="Apagar retorno" perigo onClick={() => excluir(r.id, `${dataBr(r.data)} · ${r.transportadora ?? ''} · ${brl(r.freteTotal)}`)} />
                    </div>
                  </li>
                ))}
              </ul>
              {/* Computador: a planilha */}
              <div className="hidden sm:block bg-white border border-gray-200 rounded-xl overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 text-gray-600 text-left">
                    <tr>
                      <th className="px-3 py-2 font-medium">Data</th>
                      <th className="px-3 py-2 font-medium">Destino</th>
                      <th className="px-3 py-2 font-medium">Transportadora</th>
                      <th className="px-3 py-2 font-medium text-right">Frete contratado</th>
                      <th className="px-3 py-2 font-medium text-right">Pedágio</th>
                      <th className="px-3 py-2 font-medium text-right">Descarga</th>
                      <th className="px-3 py-2 font-medium text-right">Desconto do seguro</th>
                      <th className="px-3 py-2 font-medium text-right">Desconto</th>
                      <th className="px-3 py-2 font-medium text-right">Frete total</th>
                      <th className="px-3 py-2 font-medium text-right">Valor do contrato</th>
                      <th className="px-3 py-2 font-medium text-right">Adiantamento</th>
                      <th className="px-3 py-2 font-medium text-right">Falta receber no TIP</th>
                      <th className="px-3 py-2 font-medium">Motorista</th>
                      <th className="px-3 py-2 font-medium">Placa</th>
                      <th className="px-3 py-2 font-medium">Origem</th>
                      <th className="px-3 py-2 font-medium">Viagem</th>
                      <th className="px-3 py-2"><span className="sr-only">Ações</span></th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100">
                    {lista.map(({ retorno: r, viagem, anotacao }) => (
                      <tr key={r.id} className={!r.conferido ? 'bg-amber-50/60' : r.foraDoControle ? 'text-gray-500' : ''}>
                        <td className="px-3 py-2 whitespace-nowrap tabular-nums">{dataBr(r.data)}</td>
                        <td className="px-3 py-2">{r.destino}</td>
                        <td className="px-3 py-2">{r.transportadora}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{num(r.valorFrete)}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{r.pedagio ? num(r.pedagio) : ''}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{r.adicional ? num(r.adicional) : ''}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{r.descontoSeguro ? num(r.descontoSeguro) : ''}</td>
                        <td className="px-3 py-2 text-right tabular-nums" title={r.descontoManual != null ? 'Digitado à mão' : r.porcentagemTip ? `${num(r.porcentagemTip)}% TIP` : ''}>{r.desconto ? num(r.desconto) : ''}</td>
                        <td className="px-3 py-2 text-right tabular-nums font-semibold">{num(r.freteTotal)}</td>
                        <td className="px-3 py-2 text-right whitespace-nowrap"><ValorOuAviso valor={r.valorContrato} aviso={r.contratoAviso ?? 'Sem contrato ligado'} /></td>
                        <td className="px-3 py-2 text-right whitespace-nowrap"><ValorOuAviso valor={r.adiantamentoDescarga} aviso={r.contratoAviso ?? 'Sem contrato ligado'} /></td>
                        <td className="px-3 py-2 text-right whitespace-nowrap"><ValorOuAviso valor={r.reembolsoDescarga} aviso={r.contratoAviso ?? 'Sem contrato ou descarga'} /></td>
                        <td className="px-3 py-2">{r.motorista}</td>
                        <td className="px-3 py-2 whitespace-nowrap">{r.placa}</td>
                        <td className="px-3 py-2">{r.observacoes}</td>
                        <td className="px-3 py-2 whitespace-nowrap">
                          <div className="flex flex-col items-start gap-0.5">
                            {viagem ? <Link to={`/viagens-caminhoes/${viagem.id}`} className="font-medium text-tinta-700 underline underline-offset-2">{viagem.dataSaida ? dataBr(viagem.dataSaida) : 'Abrir'}</Link>
                              : !r.foraDoControle && <span className="text-gray-500">Sem viagem</span>}
                            {!r.conferido && <Selo tom="atencao">A conferir</Selo>}
                            {r.foraDoControle && <Selo>Fora do controle</Selo>}
                            {anotacao && <Selo tom="info">Anotação do motorista: {brl(anotacao.valor)}</Selo>}
                          </div>
                        </td>
                        <td className="px-1 py-1 whitespace-nowrap">
                          <BotaoIcone icone={Pencil} rotulo="Editar retorno" onClick={() => setEdicao({ id: r.id, inicial: entradaDoRetorno(r), retorno: r })} />
                          <BotaoIcone icone={Trash2} rotulo="Apagar retorno" perigo onClick={() => excluir(r.id, `${dataBr(r.data)} · ${r.transportadora ?? ''} · ${brl(r.freteTotal)}`)} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  {filtro === 'todos' && (
                    <tfoot className="bg-gray-50 font-semibold">
                      <tr>
                        <td className="px-3 py-2" colSpan={8}>Valor total de retornos</td>
                        <td className="px-3 py-2 text-right tabular-nums">{num(dados.total)}</td>
                        <td colSpan={8} />
                      </tr>
                    </tfoot>
                  )}
                </table>
              </div>
            </>
          )}
        </>
      )}

      {edicao && (
        <RetornoModal id={edicao.id} inicial={edicao.inicial} retorno={edicao.retorno} onClose={() => setEdicao(null)}
                      onSalvou={() => { setEdicao(null); carregar() }} />
      )}
    </div>
  )
}
