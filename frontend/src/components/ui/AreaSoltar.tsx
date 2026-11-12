import { useEffect, useId, useRef, useState, type DragEvent, type ReactNode } from 'react'
import { FileUp, LoaderCircle, type LucideIcon } from 'lucide-react'

// Área de soltar arquivos (D-16, UI-15). Antes cada tela tinha uma `div` com
// clique: o teclado não chegava nela e o leitor de tela não dizia o que era.
// Aqui a área inteira é um <button> de verdade:
//   - Tab chega nela, o foco aparece, Enter ou Espaço abrem a escolha de arquivos;
//   - o leitor de tela lê o título e, pelo aria-describedby, os tipos aceitos;
//   - arrastar por cima muda o desenho ("Solte para enviar");
//   - no celular (sem arrastar) o texto vira "Escolher arquivos";
//   - arquivo de tipo errado não some calado: a linha embaixo diz qual ficou de fora;
//   - soltar o arquivo fora da área (por engano) não abre o PDF no lugar do
//     sistema, o que perdia o que estava na tela.
//
// API (para a Fase 3: Notas, CT-es, Acerto, E-mails):
//
//   <AreaSoltar
//     aceita={['.pdf']}                 // extensões aceitas (obrigatório)
//     onArquivos={files => ...}         // só os aceitos, na ordem em que vieram
//     titulo="Arraste as faturas e os boletos aqui"
//     dica="Posto 7 e Estrada, vários de uma vez"   // opcional, linha de baixo
//     icone={Fuel}                      // opcional (padrão: FileUp)
//     multiplos                         // padrão true; false = um arquivo só
//     carregando={lendo}                // trava e mostra o giro
//     textoCarregando="Lendo os PDFs..."
//     desabilitado={false}
//     compacta                          // uma linha só, para caber num cartão
//     textoBotao="Escolher PDFs"        // padrão "Escolher arquivos"
//   />
//
// `tiposLegiveis` monta o texto dos tipos ("PDF", "XML ou PDF") para a dica do
// leitor de tela; passe `descricaoTipos` se quiser outro texto.

type Props = {
  aceita: string[]
  onArquivos: (arquivos: File[]) => void
  titulo: ReactNode
  dica?: ReactNode
  icone?: LucideIcon
  multiplos?: boolean
  carregando?: boolean
  textoCarregando?: ReactNode
  desabilitado?: boolean
  compacta?: boolean
  /** Do tamanho de um botão comum (mostra só `textoBotao`), para ficar num canto; continua aceitando arrastar */
  discreta?: boolean
  textoBotao?: string
  /** Aceita também arquivo sem extensão (o servidor confere o conteúdo; ex.: foto salva do WhatsApp sem ".jpg") */
  aceitaSemExtensao?: boolean
  /** Texto dos tipos aceitos para o leitor de tela e a linha de baixo (padrão: montado de `aceita`) */
  descricaoTipos?: string
  className?: string
}

export const tiposLegiveis = (aceita: string[]) => {
  const nomes = [...new Set(aceita.map(a => a.replace(/^\./, '').toUpperCase()))]
  return nomes.length <= 1 ? (nomes[0] ?? '') : `${nomes.slice(0, -1).join(', ')} ou ${nomes[nomes.length - 1]}`
}

const temArquivos = (e: { dataTransfer: DataTransfer | null }) =>
  Array.from(e.dataTransfer?.types ?? []).includes('Files')

export default function AreaSoltar({
  aceita, onArquivos, titulo, dica, icone: Icone = FileUp, multiplos = true, carregando = false,
  textoCarregando, desabilitado = false, compacta = false, discreta = false, textoBotao = 'Escolher arquivos', descricaoTipos, className = '',
  aceitaSemExtensao = false,
}: Props) {
  const id = useId()
  const entrada = useRef<HTMLInputElement>(null)
  // Contador: o dragleave dispara ao passar por cima de cada filho; sem ele a
  // área piscava entre "arrastando" e "parada"
  const dentro = useRef(0)
  const [arrastando, setArrastando] = useState(false)
  const [recusados, setRecusados] = useState<string[]>([])
  const travada = desabilitado || carregando
  const tipos = descricaoTipos ?? tiposLegiveis(aceita)
  const idTipos = `${id}-tipos`
  const idRecusa = `${id}-recusa`

  // Arquivo solto fora da área abriria no lugar do sistema (e o que estava
  // digitado ia embora). Enquanto a área existe, soltar fora não faz nada.
  useEffect(() => {
    const segurar = (e: globalThis.DragEvent) => { if (temArquivos(e)) e.preventDefault() }
    window.addEventListener('dragover', segurar)
    window.addEventListener('drop', segurar)
    return () => { window.removeEventListener('dragover', segurar); window.removeEventListener('drop', segurar) }
  }, [])

  const receber = (lista: FileList | File[] | null) => {
    const todos = Array.from(lista ?? [])
    if (!todos.length) return
    const ext = aceita.map(a => a.toLowerCase())
    const ok = todos.filter(f => ext.some(e => f.name.toLowerCase().endsWith(e)) || (aceitaSemExtensao && !/\.[^./\\]+$/.test(f.name)))
    const fora = todos.filter(f => !ok.includes(f)).map(f => f.name)
    const usados = multiplos ? ok : ok.slice(0, 1)
    if (!multiplos && ok.length > 1) fora.push(...ok.slice(1).map(f => f.name))
    setRecusados(fora)
    if (usados.length) onArquivos(usados)
  }

  const aoEntrar = (e: DragEvent) => {
    if (!temArquivos(e)) return
    e.preventDefault()
    dentro.current += 1
    if (!travada) setArrastando(true)
  }
  const aoSair = () => {
    dentro.current = Math.max(0, dentro.current - 1)
    if (dentro.current === 0) setArrastando(false)
  }
  const aoSoltar = (e: DragEvent) => {
    e.preventDefault()
    dentro.current = 0
    setArrastando(false)
    if (!travada) receber(e.dataTransfer.files)
  }

  const descricao = [idTipos, recusados.length ? idRecusa : ''].filter(Boolean).join(' ')

  const entradaArquivo = (
    <input
      ref={entrada}
      type="file"
      accept={aceita.join(',')}
      multiple={multiplos}
      hidden
      tabIndex={-1}
      onChange={e => { receber(e.target.files); e.target.value = '' }}
    />
  )
  const textoRecusa = recusados.length > 0 && <>
    Ficou de fora (não é {tipos}{!multiplos ? ' ou passou de um' : ''}): <span className="break-all">{recusados.join(', ')}</span>
  </>

  // Discreta: um botão comum que também recebe arquivo arrastado. O tracejado
  // só aparece enquanto se arrasta por cima, para dizer "pode soltar aqui".
  if (discreta) return (
    <div className={className}>
      <button
        type="button"
        onClick={() => entrada.current?.click()}
        onDragEnter={aoEntrar}
        onDragOver={e => { if (temArquivos(e)) { e.preventDefault(); e.dataTransfer.dropEffect = travada ? 'none' : 'copy' } }}
        onDragLeave={aoSair}
        onDrop={aoSoltar}
        disabled={travada}
        aria-busy={carregando || undefined}
        aria-describedby={descricao}
        title={typeof titulo === 'string' ? titulo : undefined}
        className={`inline-flex items-center justify-center gap-2 w-full rounded-lg px-4 min-h-11 sm:min-h-10 text-sm font-semibold
          select-none transition-colors disabled:cursor-not-allowed disabled:opacity-60 border
          ${arrastando
            ? 'border-dashed border-tinta-500 bg-tinta-50 text-tinta-800'
            : 'border-gray-300 bg-white text-gray-800 hover:bg-gray-50 hover:border-gray-400'}`}
      >
        {carregando
          ? <LoaderCircle size={18} className="animate-spin shrink-0" aria-hidden />
          : <Icone size={18} strokeWidth={2} className="shrink-0" aria-hidden />}
        {carregando ? (textoCarregando ?? 'Lendo os arquivos...') : arrastando ? 'Solte para enviar' : textoBotao}
      </button>
      {entradaArquivo}
      <p id={idTipos} className="sr-only">
        {multiplos ? `Aceita ${tipos}, vários de uma vez; dá para arrastar os arquivos até aqui` : `Aceita um arquivo ${tipos}; dá para arrastar até aqui`}
      </p>
      <p id={idRecusa} role="status" aria-live="polite" className={`text-sm font-medium text-amber-800 ${recusados.length ? 'mt-1' : 'sr-only'}`}>
        {textoRecusa}
      </p>
    </div>
  )

  return (
    <div className={className}>
      <button
        type="button"
        onClick={() => entrada.current?.click()}
        onDragEnter={aoEntrar}
        onDragOver={e => { if (temArquivos(e)) { e.preventDefault(); e.dataTransfer.dropEffect = travada ? 'none' : 'copy' } }}
        onDragLeave={aoSair}
        onDrop={aoSoltar}
        disabled={travada}
        aria-busy={carregando || undefined}
        aria-describedby={descricao}
        data-arrastando={arrastando || undefined}
        className={`group w-full rounded-xl border-2 border-dashed text-left transition-[border-color,background-color,box-shadow] duration-200
          disabled:cursor-not-allowed
          ${compacta ? 'flex items-center gap-3 px-4 py-3 min-h-[3.5rem]' : 'flex flex-col items-center text-center gap-1 px-5 py-6 sm:py-8'}
          ${arrastando
            ? 'border-tinta-500 bg-tinta-50 shadow-[0_6px_20px_-12px_rgba(33,58,82,.45)]'
            : travada
              ? 'border-gray-300 bg-gray-50 opacity-80'
              : 'border-gray-300 bg-white hover:border-tinta-400 hover:bg-tinta-50/60'}`}
      >
        <span
          aria-hidden
          className={`shrink-0 inline-flex items-center justify-center rounded-full transition-transform duration-200 ease-out
            ${compacta ? 'w-9 h-9' : 'w-12 h-12 mb-1.5'}
            ${arrastando ? 'bg-tinta-700 text-white -translate-y-0.5' : 'bg-tinta-50 text-tinta-600 group-hover:-translate-y-0.5'}`}
        >
          {carregando
            ? <LoaderCircle size={compacta ? 18 : 22} className="animate-spin" />
            : <Icone size={compacta ? 18 : 22} strokeWidth={1.75} />}
        </span>

        <span className={`min-w-0 ${compacta ? 'flex-1' : ''}`}>
          <span className="block text-sm font-semibold text-gray-900">
            {carregando
              ? (textoCarregando ?? 'Lendo os arquivos...')
              : arrastando
                ? 'Solte para enviar'
                : <>
                    {/* Celular não arrasta: lá o convite é tocar */}
                    <span className="hidden sm:inline">{titulo}</span>
                    <span className="sm:hidden">{textoBotao}</span>
                  </>}
          </span>
          {dica && !carregando && <span className="block text-sm text-gray-600 mt-0.5">{dica}</span>}
        </span>

        {!carregando && (
          <span
            aria-hidden
            className={`hidden sm:inline-flex items-center rounded-lg border border-gray-300 bg-white px-3 min-h-8 text-sm font-semibold text-gray-800
              transition-colors group-hover:border-tinta-400 ${compacta ? 'shrink-0' : 'mt-3'}`}
          >
            {compacta ? textoBotao : `ou ${textoBotao.charAt(0).toLowerCase()}${textoBotao.slice(1)}`}
          </span>
        )}
      </button>

      {entradaArquivo}

      <p id={idTipos} className={`text-xs text-gray-500 mt-1.5 ${compacta ? '' : 'text-center'}`}>
        {multiplos ? `Aceita ${tipos}, vários de uma vez` : `Aceita um arquivo ${tipos}`}
      </p>
      <p id={idRecusa} role="status" aria-live="polite" className={`text-sm font-medium text-amber-800 ${recusados.length ? 'mt-1' : 'sr-only'}`}>
        {textoRecusa}
      </p>
    </div>
  )
}
