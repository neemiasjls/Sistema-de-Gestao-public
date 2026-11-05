import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react'
import { TriangleAlert } from 'lucide-react'
import { Modal } from '../ModalBase'
import Botao from './Botao'

// Confirmação do próprio sistema no lugar do confirm() do navegador (D-08): a caixa
// do navegador é cinza, no iPhone mostra o endereço do site no título e não diz o
// que vai ser apagado. Esta diz o item e põe o botão de apagar em vermelho.
//
// Uso:  const confirmar = useConfirmar()
//       if (!await confirmar({ titulo: 'Apagar a saída?', texto: 'BEM SERVIDO, 12 produtosA', confirmar: 'Apagar' })) return

export type OpcoesConfirmar = {
  titulo: ReactNode
  /** O que será afetado, com nome do item */
  texto?: ReactNode
  /** Texto do botão de confirmar (padrão: "Apagar") */
  confirmar?: string
  cancelar?: string
  /** Botão vermelho (padrão). false = ação que não apaga nada (ex.: substituir, enviar) */
  perigo?: boolean
}

type Pedido = OpcoesConfirmar & { responder: (ok: boolean) => void }

const Contexto = createContext<(o: OpcoesConfirmar) => Promise<boolean>>(
  // Fora do provedor (não deveria acontecer): cai no do navegador
  async o => window.confirm(typeof o.titulo === 'string' ? o.titulo : 'Confirmar?'),
)

export function ProvedorConfirmar({ children }: { children: ReactNode }) {
  const [pedido, setPedido] = useState<Pedido | null>(null)
  const atual = useRef<Pedido | null>(null)

  const confirmar = useCallback((o: OpcoesConfirmar) => new Promise<boolean>(resolver => {
    // Um pedido novo com outro aberto: o antigo conta como "não"
    atual.current?.responder(false)
    const p: Pedido = {
      ...o,
      responder: ok => {
        if (atual.current !== p) return
        atual.current = null
        setPedido(null)
        resolver(ok)
      },
    }
    atual.current = p
    setPedido(p)
  }), [])

  const perigo = pedido?.perigo ?? true
  return (
    <Contexto.Provider value={confirmar}>
      {children}
      {pedido && (
        <Modal
          titulo={pedido.titulo}
          alerta
          onClose={() => pedido.responder(false)}
          rodape={
            <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
              <Botao variante="secundario" onClick={() => pedido.responder(false)} data-foco-inicial>
                {pedido.cancelar ?? 'Cancelar'}
              </Botao>
              <Botao variante={perigo ? 'perigo' : 'primario'} onClick={() => pedido.responder(true)}>
                {pedido.confirmar ?? 'Apagar'}
              </Botao>
            </div>
          }
        >
          <div className="flex gap-3">
            {perigo && (
              <span className="w-10 h-10 rounded-full bg-red-50 text-red-700 flex items-center justify-center shrink-0">
                <TriangleAlert size={20} strokeWidth={2} aria-hidden />
              </span>
            )}
            <div className="text-sm text-gray-700 leading-relaxed min-w-0 break-words pt-0.5">
              {pedido.texto ?? (perigo ? 'Isso não pode ser desfeito.' : null)}
            </div>
          </div>
        </Modal>
      )}
    </Contexto.Provider>
  )
}

export function useConfirmar() {
  return useContext(Contexto)
}
