import { useEffect, useId, useState } from 'react'
import { FileUp } from 'lucide-react'
import toast from 'react-hot-toast'
import { Modal } from './ModalBase'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import AreaSoltar from './ui/AreaSoltar'
import { dataBr } from '../utils/formato'
import { erroDaApi } from '../services/semPararApi'
import { viagensApi, type DadosCnh, type LeituraCnh, type MotoristaFrota } from '../services/viagensCaminhaoApi'

// Importar a CNH (VIA-20, UI-21): PDF da CNH digital ou a imagem dela (JPG/PNG), vários de uma vez. Cada um
// é lido (um por vez, o OCR é pesado), o sistema acha sozinho se é motorista novo ou atualização (CPF, depois
// nome), mostra o tipo reconhecido e as mudanças antes → depois e só grava os marcados ao confirmar. Foto da
// CNH de papel não é lida: escolhendo de quem é, o arquivo fica guardado no cadastro.

const valor = (v: string | null) =>
  v == null || v === '' ? 'em branco' : /^\d{4}-\d{2}-\d{2}$/.test(v) ? dataBr(v) : v

type Item = {
  arquivo: File
  leitura: LeituraCnh | null
  erro: string | null
  marcado: boolean
  repetido: boolean
  feito: boolean
  /** Motorista escolhido na tela quando o sistema não achou sozinho (foto de papel, ou CNH de quem já existe) */
  escolhido: number | null
}

type Motorista = Pick<MotoristaFrota, 'id' | 'nome' | 'ativo' | 'temCnhPdf'> & Partial<DadosCnh>

const CAMPOS_DATA = ['dataNascimento', 'cnhEmissao', 'cnhValidade']
const dataCurta = (s: string | null) => s && /^\d{4}-\d{2}-\d{2}/.test(s) ? `${s.slice(8, 10)}/${s.slice(5, 7)}/${s.slice(0, 4)}` : s
const igual = (campo: string, a: string | null, b: string | null) =>
  campo === 'cpf' ? (a ?? '').replace(/\D/g, '') === (b ?? '').replace(/\D/g, '') : (a ?? '').trim() === (b ?? '').trim()

const idDe = (it: Item) => it.leitura?.motorista?.id ?? it.escolhido
// Mesma pessoa = mesmo CPF ou mesmo cadastro; sem nenhum dos dois, não dá para dizer
const mesmaPessoa = (a: Item, b: Item) =>
  (!!a.leitura?.dados.cpf && a.leitura.dados.cpf === b.leitura?.dados.cpf) || (idDe(a) != null && idDe(a) === idDe(b))
const podeGravar = (it: Item) =>
  !!it.leitura && !it.feito && !it.repetido && (idDe(it) != null || (it.leitura.reconhecida && !!it.leitura.dados.nome))

export default function ImportarCnhModal({ onClose, onFeito }: { onClose: () => void; onFeito: (ficaAberto: boolean) => void }) {
  const [itens, setItens] = useState<Item[]>([])
  // Para escolher de quem é a CNH quando o sistema não acha sozinho; sem a lista, a escolha só não aparece
  const [motoristas, setMotoristas] = useState<Motorista[]>([])
  useEffect(() => { viagensApi.motoristas().then(setMotoristas).catch(() => setMotoristas([])) }, [])
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
        it = { arquivo: f, leitura: l, erro: null, marcado: true, repetido: false, feito: false, escolhido: null }
        // Duas CNHs da mesma pessoa no lote: só a primeira entra
        it.repetido = lidos.some(x => x.leitura && mesmaPessoa(x, it))
        it.marcado = !it.repetido
      } catch (e) {
        it = { arquivo: f, leitura: null, erro: await erroDaApi(e, 'Não consegui ler esta CNH. Preencha à mão.'), marcado: false, repetido: false, feito: false, escolhido: null }
      }
      it.marcado = it.marcado && podeGravar(it)
      lidos.push(it)
      setItens([...lidos])
    }
    setLendo(null)
  }

  const marcar = (i: number) => setItens(v => v.map((x, j) => j === i ? { ...x, marcado: !x.marcado } : x))

  // Trocar o motorista de uma linha pode repetir (ou deixar de repetir) outra: confere o lote inteiro
  const escolherMotorista = (i: number, id: number | null) => setItens(v => {
    const prox = v.map((x, j) => j === i ? { ...x, escolhido: id } : x)
    return prox.map((x, j) => {
      if (!x.leitura || x.feito) return x
      const repetido = prox.some((y, k) => k < j && !!y.leitura && !y.feito && mesmaPessoa(y, x))
      if (j !== i && repetido === x.repetido) return x
      const n = { ...x, repetido }
      return { ...n, marcado: podeGravar(n) }
    })
  })

  const gravar = async () => {
    setGravando(true)
    let criados = 0, atualizados = 0, falhas = 0
    const avisos: string[] = []
    const prox = [...itens]
    for (const [i, it] of prox.entries()) {
      if (!it.marcado || !it.leitura || !podeGravar(it)) continue
      try {
        const r = await viagensApi.aplicarCnh(it.arquivo, idDe(it), it.leitura.dados)
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
    <Modal titulo="Importar CNHs" onClose={onClose} largura="lg" confirmarAoFechar={pendentes}>
      <p className="text-sm text-gray-600">
        Envie a CNH digital em PDF ou a imagem dela (JPG ou PNG), uma ou várias. Eu acho cada motorista sozinho e
        mostro o que muda antes de gravar. Foto da CNH de papel não dá para ler, mas fica guardada no cadastro.
      </p>
      <AreaSoltar aceita={['.pdf', '.jpg', '.jpeg', '.png']} aceitaSemExtensao multiplos icone={FileUp}
                  carregando={!!lendo} textoCarregando={lendo ?? ''}
                  titulo={itens.length ? 'Arraste mais CNHs aqui' : 'Arraste as CNHs aqui'} onArquivos={escolher} />

      {itens.length > 0 && (
        <ul className="space-y-2">
          {itens.map((it, i) => (
            <Linha key={i} it={it} motoristas={motoristas}
                   onMarcar={() => marcar(i)} onEscolher={id => escolherMotorista(i, id)} />
          ))}
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

function Linha({ it, motoristas, onMarcar, onEscolher }: {
  it: Item
  motoristas: Motorista[]
  onMarcar: () => void
  onEscolher: (id: number | null) => void
}) {
  const [detalhes, setDetalhes] = useState(false)
  const idSelect = useId()
  const l = it.leitura
  const escolhido = it.escolhido != null ? motoristas.find(m => m.id === it.escolhido) ?? null : null
  const destino = l?.motorista ?? escolhido
  const novo = !!l && !destino
  const nome = destino?.nome ?? l?.dados.nome ?? ''
  // Escolhido à mão: compara o que foi lido com o cadastro escolhido, para mostrar o que muda de verdade
  const difs = !l ? [] : escolhido
    ? l.diferencas
        .map(d => { const a = (escolhido[d.campo as keyof DadosCnh] as string | null | undefined) ?? null; return { ...d, atual: CAMPOS_DATA.includes(d.campo) ? dataCurta(a) : a } })
        .filter(d => !igual(d.campo, d.atual, d.novo))
    : l.diferencas
  const lista = detalhes ? difs : difs.slice(0, 3)
  const substitui = !!destino && (l?.motorista ? l.motorista.temCnhPdf : !!escolhido?.temCnhPdf)

  return (
    <li className="text-sm border border-gray-200 dark:border-white/10 rounded-lg p-3 space-y-1.5">
      <div className="flex items-start gap-3">
        {podeGravar(it) && (
          <input type="checkbox" checked={it.marcado} onChange={onMarcar} className="mt-1 h-5 w-5 shrink-0"
                 aria-label={`Gravar ${nome || it.arquivo.name}`} />
        )}
        <div className="min-w-0 flex-1">
          <p className="font-medium text-gray-800 break-words">
            {!l ? it.arquivo.name
              : destino ? <>{l.reconhecida ? 'Atualiza' : 'Guarda no cadastro de'} <b>{nome}</b></>
              : l.reconhecida ? <>Novo: <b>{nome || 'sem nome lido'}</b></>
              : <>De quem é esta CNH?</>}
            {it.feito && <span className="ml-2 text-green-700 dark:text-green-400 font-normal">gravado</span>}
          </p>
          <p className="text-xs text-gray-500 break-all">
            {it.arquivo.name}
            {l && ` · ${l.tipo}`}
            {l?.motorista && ` · achado pelo ${l.achadoPor === 'cpf' ? 'CPF' : 'nome'}`}
            {l?.reconhecida && ` · ${difs.length ? `${difs.length} ${difs.length === 1 ? 'dado muda' : 'dados mudam'}` : 'nada diferente, só guarda o arquivo'}`}
          </p>
        </div>
      </div>

      {it.erro && <Aviso tipo="erro">{it.erro}</Aviso>}
      {it.repetido && <Aviso tipo="atencao">Mesma pessoa de outra CNH desta lista; fica de fora.</Aviso>}
      {l && !l.reconhecida && !it.feito && (
        <Aviso tipo="atencao">
          Não deu para ler os dados desta imagem (CNH de papel ou foto). Escolha de quem é para guardar o arquivo; os
          dados continuam como estão no cadastro e dá para preencher em Editar.
        </Aviso>
      )}
      {l && !l.motorista && !it.feito && motoristas.length > 0 && (
        <div className="space-y-1">
          <label htmlFor={idSelect} className="block text-xs text-gray-600">
            {l.reconhecida ? 'Não achei no cadastro. É de um motorista que já existe?' : 'Motorista'}
          </label>
          <select id={idSelect} value={it.escolhido ?? ''} onChange={e => onEscolher(e.target.value ? Number(e.target.value) : null)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 text-base sm:text-sm">
            <option value="">{l.reconhecida && l.dados.nome ? `Não, cadastrar ${l.dados.nome} como novo` : 'Escolha o motorista'}</option>
            {motoristas.map(m => <option key={m.id} value={m.id}>{m.nome}{m.ativo ? '' : ' (desativado)'}</option>)}
          </select>
        </div>
      )}
      {destino && 'ativo' in destino && !destino.ativo && <p className="text-xs text-amber-800">Este motorista está desativado.</p>}
      {substitui && !it.feito && <p className="text-xs text-amber-800">Já tem um arquivo da CNH guardado; este substitui.</p>}
      {novo && l?.reconhecida && !l.dados.nome && <Aviso tipo="erro">A CNH não trouxe o nome. Escolha o motorista acima ou cadastre à mão primeiro.</Aviso>}
      {l?.reconhecida && l.naoAchou.length > 0 && !it.feito && (
        <p className="text-xs text-amber-800">Não veio do arquivo: {l.naoAchou.join(', ')}. Dá para preencher em Editar.</p>
      )}

      {l?.reconhecida && difs.length > 0 && !it.feito && (
        <>
          <ul className="text-xs text-gray-700 list-disc pl-4 space-y-0.5">
            {lista.map(d => (
              <li key={d.campo}><b>{d.rotulo}</b>: {novo ? valor(d.novo) : <>{valor(d.atual)} → {valor(d.novo)}</>}</li>
            ))}
          </ul>
          {difs.length > 3 && (
            <button type="button" onClick={() => setDetalhes(v => !v)} className="text-xs text-tinta-700 dark:text-tinta-300 font-medium underline">
              {detalhes ? 'Esconder detalhes' : 'Ver detalhes'}
            </button>
          )}
        </>
      )}
    </li>
  )
}
