import { useCallback, useEffect, useState } from 'react'
import { Copy, FileSpreadsheet, FileText, Fuel, X } from 'lucide-react'
import toast from 'react-hot-toast'
import { formatBRL } from '../services/api'
import {
  cobrancasAbastecimentoApi as cobApi, erroCobranca, linhaBancoBento, placasSemCadastro,
  type ConferenciaPlanilha, type ListaCobrancas, type PlacaSemCadastro, type ResultadoImportacao,
} from '../services/cobrancasAbastecimentoApi'
import DonosDasPlacas from '../components/DonosDasPlacas'
import { ListaCobrancasPostos, PainelCobrancas, PlanilhasGuardadas } from '../components/CobrancasPostos'
import CabecalhoTela from '../components/ui/CabecalhoTela'
import Botao, { BotaoIcone } from '../components/ui/Botao'
import Aviso, { toastConfira } from '../components/ui/Aviso'
import AreaSoltar from '../components/ui/AreaSoltar'
import { ErroCarga } from '../components/ui/Estados'

const tamanho = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB`

export default function AbastecimentoPage() {
  const [pdfs, setPdfs]       = useState<File[]>([])
  const [gerando, setGerando] = useState(false)
  const [conf, setConf]       = useState<ConferenciaPlanilha | null>(null)
  const [importado, setImportado] = useState<ResultadoImportacao | null>(null)
  // Placa fora do cadastro de veículos: pergunta o dono e depois refaz (ABA-06)
  const [pedirDonos, setPedirDonos] = useState<{ placas: PlacaSemCadastro[]; refazer: () => Promise<void> } | null>(null)

  // Faturas, boletos e planilhas guardados, com a conferência e o painel prontos
  const [dados, setDados] = useState<ListaCobrancas | null>(null)
  const [falhou, setFalhou] = useState(false)

  // Falhou: mostra o erro com "Tentar de novo" no lugar das listas (antes
  // aparecia "nenhum boleto", como se não houvesse nada guardado)
  const carregar = useCallback(async () => {
    try { setDados((await cobApi.listar()).data); setFalhou(false) }
    catch { setFalhou(true) }
  }, [])

  useEffect(() => { carregar() }, [carregar])

  // A AreaSoltar já deixa passar só PDF (e diz na tela o que ficou de fora);
  // o mesmo nome duas vezes entra uma vez só
  const addFiles = useCallback((novos: File[]) => {
    setPdfs(prev => {
      const nomes = new Set(prev.map(f => f.name))
      return [...prev, ...novos.filter(f => !nomes.has(f.name))]
    })
  }, [])

  // Planilha de divisão das faturas guardadas: uma por posto, nunca juntando
  // postos diferentes (cada posto cobra no seu boleto). Baixa cada uma e
  // mostra a conferência somada.
  const gerarPlanilha = useCallback(async (faturas: { id: number; posto: string }[]) => {
    const porPosto = new Map<string, number[]>()
    for (const f of faturas) porPosto.set(f.posto, [...(porPosto.get(f.posto) ?? []), f.id])
    try {
      let soma = 0, qtde = 0, total: number | null = null
      const avisos: string[] = []
      for (const ids of porPosto.values()) {
        const c = await cobApi.gerarPlanilha(ids)
        soma += c.soma; qtde += c.qtde
        if (c.totalFatura != null) total = (total ?? 0) + c.totalFatura
        avisos.push(...c.avisos)
      }
      setConf({ soma: Math.round(soma * 100) / 100, qtde, totalFatura: total, avisos })
      if (avisos.length) toastConfira('Planilha baixada. Confira o aviso na tela.')
      else toast.success(porPosto.size > 1
        ? `${porPosto.size} planilhas geradas e baixadas, uma por posto.`
        : 'Planilha gerada e baixada!')
    } catch (e) {
      const placas = await placasSemCadastro(e)
      if (placas) setPedirDonos({ placas, refazer: () => gerarPlanilha(faturas) })
      else toast.error(await erroCobranca(e, 'Erro ao gerar planilha.'))
    } finally {
      await carregar()
    }
  }, [carregar])

  // Faturas e boletos vão juntos: a API separa pelo conteúdo, guarda tudo e
  // liga cada boleto à sua fatura. Com fatura no envio, a planilha sai na hora.
  const importar = async (): Promise<void> => {
    if (!pdfs.length) { toast.error('Selecione os PDFs das faturas e dos boletos.'); return }
    setGerando(true)
    setConf(null)
    setImportado(null)
    try {
      const r = (await cobApi.importar(pdfs)).data
      setImportado(r)
      setPdfs([])
      if (r.faturas.length) await gerarPlanilha(r.faturas.map(f => ({ id: f.id, posto: f.posto })))
      else {
        toast.success(r.boletos.length ? 'Boletos guardados.' : 'Nada novo para guardar.')
        await carregar()
      }
    } catch (e) {
      const placas = await placasSemCadastro(e)
      if (placas) setPedirDonos({ placas, refazer: importar })
      else toast.error(await erroCobranca(e, 'Erro ao importar os PDFs.'))
    } finally {
      setGerando(false)
    }
  }

  const bentoPorFatura = importado?.faturas.filter(f => f.quantidadeBento > 0) ?? []

  const copiar = async (texto: string, ok: string) => {
    try { await navigator.clipboard.writeText(texto); toast.success(ok) }
    catch { toast.error('Não consegui copiar. Selecione o texto e copie à mão.') }
  }

  const totalSelecionado = pdfs.reduce((t, f) => t + f.size, 0)

  return (
    <div className="space-y-6 max-w-5xl">
      <CabecalhoTela descricao={<>
        Envie os <b>PDFs das faturas e dos boletos dos postos</b>, juntos ou separados. As faturas
        geram a planilha de divisão por dono do caminhão (Bento, Caio e Davi Duarte); os
        boletos se ligam à fatura pelo número. Tudo fica guardado aqui.
      </>} />

      {falhou && !dados
        ? <ErroCarga mensagem="Não consegui carregar as cobranças dos postos." onTentar={carregar} />
        : <PainelCobrancas dados={dados} />}

      {/* Envio: soltar ou escolher, conferir a lista e importar */}
      <section aria-labelledby="abast-envio" className="space-y-3">
        <h2 id="abast-envio" className="sr-only">Importar faturas e boletos</h2>
        <AreaSoltar
          aceita={['.pdf']}
          onArquivos={addFiles}
          icone={Fuel}
          titulo="Arraste aqui as faturas e os boletos dos postos"
          dica="Posto 7 (Vila Norte) e Posto Estrada"
          textoBotao="Escolher PDFs"
          carregando={gerando}
          textoCarregando="Lendo os PDFs e gerando a planilha..."
        />

        {pdfs.length > 0 && (
          <div className="bg-white border border-gray-200 rounded-xl animate-entrada">
            <div className="flex items-center justify-between gap-3 pl-4 pr-2 py-1.5 border-b border-gray-100">
              <p className="text-sm text-gray-700">
                <span className="font-semibold text-gray-900 tabular-nums">{pdfs.length}</span>{' '}
                {pdfs.length === 1 ? 'arquivo escolhido' : 'arquivos escolhidos'}
                <span className="text-gray-500 tabular-nums"> · {tamanho(totalSelecionado)}</span>
              </p>
              <Botao variante="fantasma" tamanho="sm" onClick={() => setPdfs([])} disabled={gerando}>Tirar todos</Botao>
            </div>
            <ul className="max-h-56 overflow-y-auto divide-y divide-gray-100">
              {pdfs.map((f, i) => (
                <li key={f.name} className="flex items-center gap-3 pl-4 pr-1">
                  <FileText size={16} className="text-tinta-500 shrink-0" aria-hidden />
                  <span className="flex-1 min-w-0 text-sm text-gray-800 truncate" title={f.name}>{f.name}</span>
                  <span className="text-xs text-gray-500 tabular-nums shrink-0">{tamanho(f.size)}</span>
                  <BotaoIcone icone={X} rotulo={`Tirar ${f.name}`} perigo disabled={gerando}
                              onClick={() => setPdfs(prev => prev.filter((_, j) => j !== i))} />
                </li>
              ))}
            </ul>
          </div>
        )}

        <Botao variante="primario" icone={FileSpreadsheet} bloco className="sm:w-auto"
               onClick={importar} disabled={!pdfs.length} carregando={gerando} textoCarregando="Importando...">
          Importar e gerar planilha de abastecimentos
        </Botao>
      </section>

      {/* Bento: do boleto que ele paga, só uma parte é dele. Ele lança no
          controle do banco uma linha por posto: vencimento, valor e o posto com
          as placas dele — sai aqui pronto para copiar. Azul é a cor dele (EMP-02). */}
      {importado && importado.bento.quantidade > 0 && (
        <section aria-labelledby="abast-bento" className="rounded-2xl border border-blue-200 bg-blue-50 p-4 sm:p-5 space-y-4 animate-entrada">
          <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
            <div>
              <h2 id="abast-bento" className="text-sm font-semibold text-blue-800 font-sans tracking-normal">
                Abastecimentos do Bento nesta importação
              </h2>
              <p className="font-display text-3xl sm:text-4xl font-semibold text-blue-950 tabular-nums leading-tight mt-1">
                {formatBRL(importado.bento.valor)}
              </p>
              <p className="text-sm text-blue-900">
                em {importado.bento.quantidade} abastecimento{importado.bento.quantidade > 1 ? 's' : ''}
                {bentoPorFatura.length > 1 && `, em ${bentoPorFatura.length} postos`}.
                {' '}Vai para o controle do banco dele.
              </p>
            </div>
            {bentoPorFatura.length > 1 && (
              <Botao variante="secundario" icone={Copy} className="w-full sm:w-auto"
                     onClick={() => copiar(bentoPorFatura.map(f => linhaBancoBento(f).texto).join('\n'),
                                           `${bentoPorFatura.length} linhas copiadas. Cole na planilha do banco.`)}>
                Copiar todas as linhas
              </Botao>
            )}
          </div>

          {/* Celular: um cartão por posto; computador: a tabela com as três colunas do banco */}
          <ul className="sm:hidden space-y-2">
            {bentoPorFatura.map(f => {
              const l = linhaBancoBento(f)
              return (
                <li key={f.id} className="bg-white rounded-xl border border-blue-200 p-3 space-y-2">
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="text-sm text-gray-600">Vence <b className="text-gray-900 tabular-nums">{l.data || 'sem data'}</b></span>
                    <span className="font-semibold text-gray-900 tabular-nums">{formatBRL(f.valorBento)}</span>
                  </div>
                  <p className="text-sm text-gray-800 break-words">{l.descricao}</p>
                  <Botao variante="secundario" tamanho="sm" icone={Copy} bloco
                         onClick={() => copiar(l.texto, 'Linha copiada. Cole na planilha do banco.')}>
                    Copiar a linha
                  </Botao>
                </li>
              )
            })}
          </ul>
          <div className="hidden sm:block bg-white rounded-xl border border-blue-200 overflow-hidden">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-left text-xs text-blue-800 border-b border-blue-100">
                  <th className="px-4 py-2 font-medium">Vencimento</th>
                  <th className="px-4 py-2 font-medium text-right">Valor do Bento</th>
                  <th className="px-4 py-2 font-medium">Posto e placas</th>
                  <th className="px-2 py-2"><span className="sr-only">Copiar</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-blue-50">
                {bentoPorFatura.map(f => {
                  const l = linhaBancoBento(f)
                  return (
                    <tr key={f.id}>
                      <td className="px-4 py-2 tabular-nums text-gray-800">{l.data || <span className="text-amber-800">sem data</span>}</td>
                      <td className="px-4 py-2 tabular-nums text-right font-semibold text-gray-900">{formatBRL(f.valorBento)}</td>
                      <td className="px-4 py-2 text-gray-800">{l.descricao}</td>
                      <td className="px-2 py-1.5 text-right">
                        <Botao variante="fantasma" tamanho="sm" icone={Copy} title="Copia as três colunas para colar no Excel"
                               onClick={() => copiar(l.texto, 'Linha copiada. Cole na planilha do banco.')}>
                          Copiar
                        </Botao>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          {bentoPorFatura.some(f => !f.vencimento) && (
            <Aviso tipo="atencao">Uma fatura veio sem vencimento: preencha em <b>Corrigir dados</b> na lista abaixo.</Aviso>
          )}
        </section>
      )}

      {/* O que a importação guardou e o que precisa de atenção (UI-04: fica até fechar) */}
      {importado && (
        <Aviso
          tipo={importado.avisos.length || importado.boletos.some(b => !b.lidoDoPdf) ? 'atencao' : 'sucesso'}
          titulo={<>
            Guardado: {importado.faturas.length} fatura{importado.faturas.length === 1 ? '' : 's'} e{' '}
            {importado.boletos.length} boleto{importado.boletos.length === 1 ? '' : 's'}
          </>}
          itens={[
            ...(importado.boletos.some(b => !b.lidoDoPdf)
              ? ['Há boleto que não pôde ser lido: abra na lista abaixo e preencha os dados.'] : []),
            ...importado.avisos,
          ]}
          onFechar={() => setImportado(null)}
        />
      )}

      {/* Conferência: soma lida x valor da fatura */}
      {conf && (() => {
        const fechou = conf.totalFatura !== null && Math.abs(conf.soma - conf.totalFatura) < 0.05
        const diff = conf.totalFatura !== null ? conf.soma - conf.totalFatura : 0
        return (
          <Aviso
            tipo={fechou ? 'sucesso' : 'erro'}
            titulo={fechou ? 'Planilha conferida: fechou com a fatura' : 'A planilha não fechou com a fatura'}
            onFechar={() => setConf(null)}
          >
            <dl className="grid grid-cols-[1fr_auto] gap-x-6 gap-y-0.5 mt-1 max-w-xs">
              <dt>Abastecimentos lidos</dt><dd className="text-right tabular-nums font-semibold">{conf.qtde}</dd>
              <dt>Soma</dt><dd className="text-right tabular-nums font-semibold">{formatBRL(conf.soma)}</dd>
              {conf.totalFatura !== null && <>
                <dt>Valor das faturas</dt><dd className="text-right tabular-nums font-semibold">{formatBRL(conf.totalFatura)}</dd>
              </>}
              {conf.totalFatura !== null && !fechou && <>
                <dt>Diferença {diff < 0 ? '(faltando)' : '(a mais)'}</dt>
                <dd className="text-right tabular-nums font-semibold">{formatBRL(Math.abs(diff))}</dd>
              </>}
            </dl>
            {conf.avisos.length > 0 && (
              <ul className="list-disc pl-5 space-y-0.5 mt-2">
                {conf.avisos.map((a, i) => <li key={i}>{a}</li>)}
              </ul>
            )}
            {!fechou && <p className="mt-2">A planilha foi baixada mesmo assim: confira antes de usar.</p>}
          </Aviso>
        )
      })()}

      {pedirDonos && (
        <DonosDasPlacas placas={pedirDonos.placas} onPronto={pedirDonos.refazer} onClose={() => setPedirDonos(null)} />
      )}

      {!falhou && <>
        <ListaCobrancasPostos dados={dados} recarregar={carregar} onGerarPlanilha={gerarPlanilha} />
        <PlanilhasGuardadas dados={dados} recarregar={carregar} />
      </>}
    </div>
  )
}
