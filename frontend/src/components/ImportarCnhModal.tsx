import { useState } from 'react'
import { FileUp } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import AreaSoltar from './ui/AreaSoltar'
import { dataBr } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import { viagensApi, type LeituraCnh } from '../services/viagensCaminhaoApi'

// Importar os PDFs da CNH digital (VIA-20, UI-21): vários de uma vez. Cada um é lido (um por vez,
// o OCR é pesado), o sistema acha sozinho se é motorista novo ou atualização (CPF, depois nome),
// mostra as mudanças antes → depois e só grava os marcados ao confirmar.

const valor = (v: string | null) =>
  v == null || v === '' ? 'em branco' : /^\d{4}-\d{2}-\d{2}$/.test(v) ? dataBr(v) : v

type Item = {
  arquivo: File
  leitura: LeituraCnh | null
  erro: string | null
  marcado: boolean
  repetido: boolean
  feito: boolean
}

// Mesma pessoa = mesmo CPF ou mesmo cadastro achado; sem nenhum dos dois, não dá para dizer
const mesmaPessoa = (a: LeituraCnh, b: LeituraCnh) =>
  (!!a.dados.cpf && a.dados.cpf === b.dados.cpf) || (!!a.motorista && a.motorista.id === b.motorista?.id)
const podeGravar = (it: Item) => !!it.leitura && !it.feito && !it.repetido && (!!it.leitura.motorista || !!it.leitura.dados.nome)

export default function ImportarCnhModal({ onClose, onFeito }: { onClose: () => void; onFeito: (ficaAberto: boolean) => void }) {
  const [itens, setItens] = useState<Item[]>([])
  const [lendo, setLendo] = useState<string | null>(null)
  const [gravando, setGravando] = useState(false)

  const escolher = async (arquivos: File[]) => {
    const lidos: Item[] = [...itens]
    for (const [i, f] of arquivos.entries()) {
      if (lidos.some(x => x.arquivo.name === f.name && x.arquivo.size === f.size)) continue
      setLendo(arquivos.length > 1 ? `Lendo ${i + 1} de ${arquivos.length}: ${f.name}` : 'Lendo a CNH...')
      let it: Item
      try {
        const l = await viagensApi.lerCnh(f)
        // Duas CNHs da mesma pessoa no lote: só a primeira entra
        const repetido = lidos.some(x => x.leitura && mesmaPessoa(x.leitura, l))
        it = { arquivo: f, leitura: l, erro: null, marcado: !repetido, repetido, feito: false }
      } catch (e) {
        it = { arquivo: f, leitura: null, erro: await erroDaApi(e, 'Não consegui ler esta CNH. Preencha à mão.'), marcado: false, repetido: false, feito: false }
      }
      it.marcado = it.marcado && podeGravar(it)
      lidos.push(it)
      setItens([...lidos])
    }
    setLendo(null)
  }

  const marcar = (i: number) => setItens(v => v.map((x, j) => j === i ? { ...x, marcado: !x.marcado } : x))

  const gravar = async () => {
    setGravando(true)
    let criados = 0, atualizados = 0, falhas = 0
    const avisos: string[] = []
    const prox = [...itens]
    for (const [i, it] of prox.entries()) {
      if (!it.marcado || !it.leitura || !podeGravar(it)) continue
      try {
        const r = await viagensApi.aplicarCnh(it.arquivo, it.leitura.motorista?.id ?? null, it.leitura.dados)
        if (r.aviso) avisos.push(r.aviso)
        if (r.criado) criados++; else atualizados++
        prox[i] = { ...it, feito: true, marcado: false }
      } catch (e) {
        falhas++
        prox[i] = { ...it, erro: await erroDaApi(e, 'Não consegui gravar.') }
      }
      setItens([...prox])
    }
    setGravando(false)
    const partes = [criados && `${criados} criado(s)`, atualizados && `${atualizados} atualizado(s)`].filter(Boolean)
    if (partes.length) toast.success(`Motoristas: ${partes.join(' · ')}.`, { duration: 6000 })
    avisos.forEach(a => toast.error(a, { duration: 8000 }))
    if (falhas) toast.error(`${falhas} não gravou; veja na lista.`)
    if (criados || atualizados) onFeito(falhas > 0 || prox.some(x => !x.feito && x.leitura))
  }

  const marcados = itens.filter(x => x.marcado && podeGravar(x)).length
  const pendentes = itens.some(x => x.leitura && !x.feito)

  return (
    <Modal titulo="Importar CNHs (PDF)" onClose={onClose} largura="lg" confirmarAoFechar={pendentes}>
      <p className="text-sm text-gray-600">
        Envie um ou vários PDFs da CNH digital. Eu acho cada motorista sozinho e mostro o que muda antes de gravar.
      </p>
      <AreaSoltar aceita={['.pdf']} multiplos icone={FileUp} carregando={!!lendo} textoCarregando={lendo ?? ''}
                  titulo={itens.length ? 'Arraste mais PDFs aqui' : 'Arraste os PDFs das CNHs aqui'} onArquivos={escolher} />

      {itens.length > 0 && (
        <ul className="space-y-2">
          {itens.map((it, i) => <Linha key={i} it={it} onMarcar={() => marcar(i)} />)}
        </ul>
      )}

      {itens.length > 0 && (
        <div className="flex gap-2 pt-1">
          <Botao variante="primario" onClick={gravar} carregando={gravando} textoCarregando="Gravando..."
                 disabled={!marcados || !!lendo} className="flex-1">
            {marcados ? `Gravar ${marcados} ${marcados === 1 ? 'CNH' : 'CNHs'}` : 'Nada marcado para gravar'}
          </Botao>
          <Botao variante="fantasma" onClick={onClose}>Fechar</Botao>
        </div>
      )}
    </Modal>
  )
}

function Linha({ it, onMarcar }: { it: Item; onMarcar: () => void }) {
  const [detalhes, setDetalhes] = useState(false)
  const l = it.leitura
  const novo = l && !l.motorista
  const nome = l?.motorista?.nome ?? l?.dados.nome ?? ''
  const lista = detalhes ? l?.diferencas ?? [] : l?.diferencas.slice(0, 3) ?? []

  return (
    <li className="text-sm border border-gray-200 dark:border-white/10 rounded-lg p-3 space-y-1.5">
      <div className="flex items-start gap-3">
        {podeGravar(it) && (
          <input type="checkbox" checked={it.marcado} onChange={onMarcar} className="mt-1 h-5 w-5 shrink-0"
                 aria-label={`Gravar ${nome || it.arquivo.name}`} />
        )}
        <div className="min-w-0 flex-1">
          <p className="font-medium text-gray-800 break-words">
            {l ? (novo ? <>Novo: <b>{nome || 'sem nome lido'}</b></> : <>Atualiza <b>{nome}</b></>) : it.arquivo.name}
            {it.feito && <span className="ml-2 text-green-700 dark:text-green-400 font-normal">gravado</span>}
          </p>
          <p className="text-xs text-gray-500 break-all">
            {it.arquivo.name}
            {l && !novo && ` · achado pelo ${l.achadoPor === 'cpf' ? 'CPF' : 'nome'}`}
            {l && ` · ${l.diferencas.length ? `${l.diferencas.length} ${l.diferencas.length === 1 ? 'dado muda' : 'dados mudam'}` : 'nada diferente, só guarda o PDF'}`}
          </p>
        </div>
      </div>

      {it.erro && <Aviso tipo="erro">{it.erro}</Aviso>}
      {it.repetido && <Aviso tipo="atencao">Mesma pessoa de outra CNH desta lista; fica de fora.</Aviso>}
      {l && !novo && l.motorista && !l.motorista.ativo && <p className="text-xs text-amber-800">Este motorista está desativado.</p>}
      {novo && !l?.dados.nome && <Aviso tipo="erro">A CNH não trouxe o nome. Cadastre o motorista à mão primeiro.</Aviso>}
      {l && l.naoAchou.length > 0 && !it.feito && (
        <p className="text-xs text-amber-800">Não achei no PDF: {l.naoAchou.join(', ')}. Dá para preencher em Editar.</p>
      )}

      {l && l.diferencas.length > 0 && !it.feito && (
        <>
          <ul className="text-xs text-gray-700 list-disc pl-4 space-y-0.5">
            {lista.map(d => (
              <li key={d.campo}><b>{d.rotulo}</b>: {novo ? valor(d.novo) : <>{valor(d.atual)} → {valor(d.novo)}</>}</li>
            ))}
          </ul>
          {l.diferencas.length > 3 && (
            <button type="button" onClick={() => setDetalhes(v => !v)} className="text-xs text-tinta-700 dark:text-tinta-300 font-medium underline">
              {detalhes ? 'Esconder detalhes' : 'Ver detalhes'}
            </button>
          )}
        </>
      )}
    </li>
  )
}
