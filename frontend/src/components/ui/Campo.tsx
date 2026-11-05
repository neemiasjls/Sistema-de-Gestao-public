import { useId, useLayoutEffect, useRef, type ReactNode } from 'react'

// Rótulo + campo + dica/erro, ligados (D-04). Tocar no rótulo abre o campo e o
// leitor de tela diz o nome do campo, a dica e o erro.
//
// Uso: <Campo rotulo="Placa" dica="Sem traço"><input .../></Campo>
// O filho pode ser o campo direto ou algo com o campo dentro (campo + botão
// "Adicionar", autocompletar): a ligação vai para o primeiro input/select/
// textarea que houver dentro. Se ele já tiver id, usa o dele.

type PropsCampo = {
  rotulo: ReactNode
  children: ReactNode
  /** Linha pequena embaixo, para explicar o formato */
  dica?: ReactNode
  /** Mensagem de erro: deixa o campo vermelho e é lida pelo leitor de tela */
  erro?: ReactNode
  className?: string
  /** Vários campos sob um título só (cada um com o seu nome): vira fieldset + legend */
  grupo?: boolean
}

export default function Campo({ rotulo, children, dica, erro, className = '', grupo = false }: PropsCampo) {
  const gerado = useId()
  const caixa = useRef<HTMLDivElement>(null)
  const rotuloRef = useRef<HTMLLabelElement>(null)
  const idDica = dica && !erro ? `${gerado}-dica` : undefined
  const idErro = erro ? `${gerado}-erro` : undefined

  // Liga depois de desenhar: acha o campo dentro e acerta id, rótulo e descrições
  useLayoutEffect(() => {
    if (grupo) return
    const campo = caixa.current?.querySelector<HTMLElement>('input:not([type="hidden"]), select, textarea')
    if (!campo || !rotuloRef.current) return
    if (!campo.id) campo.id = gerado
    rotuloRef.current.htmlFor = campo.id
    const descritoPor = [idDica, idErro].filter(Boolean).join(' ')
    if (descritoPor) campo.setAttribute('aria-describedby', descritoPor)
    else campo.removeAttribute('aria-describedby')
    if (erro) campo.setAttribute('aria-invalid', 'true')
    else campo.removeAttribute('aria-invalid')
  })

  if (grupo) return (
    <fieldset className={className} aria-describedby={idDica}>
      <legend className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">{rotulo}</legend>
      {children}
      {idDica && <p id={idDica} className="text-xs text-gray-500 mt-1">{dica}</p>}
      {idErro && <p id={idErro} role="alert" className="text-xs font-medium text-red-700 mt-1">{erro}</p>}
    </fieldset>
  )

  return (
    <div className={className}>
      <label ref={rotuloRef} htmlFor={gerado} className="block text-sm sm:text-xs font-medium text-gray-700 mb-1">{rotulo}</label>
      <div ref={caixa} className={erro ? '[&_input]:border-red-500 [&_select]:border-red-500 [&_textarea]:border-red-500' : 'contents'}>
        {children}
      </div>
      {idDica && <p id={idDica} className="text-xs text-gray-500 mt-1">{dica}</p>}
      {idErro && <p id={idErro} className="text-xs font-medium text-red-700 mt-1">{erro}</p>}
    </div>
  )
}
