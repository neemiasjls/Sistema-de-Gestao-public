import { Component, type ErrorInfo, type ReactNode } from 'react'
import { RefreshCw, RotateCcw, TriangleAlert } from 'lucide-react'
import Botao from './Botao'

// Proteção de cada tela (D-01). Se algo dentro da tela quebrar, só a área da
// tela troca por este aviso: o menu e a barra de cima continuam, e dá para ir
// para outra tela ou tentar de novo. Antes a página inteira ficava branca.
//
// O Layout põe uma por rota, com key = endereço: trocar de tela já começa limpo.

type Props = { children: ReactNode }
type Estado = { erro: Error | null; tentativa: number }

export default class ProtecaoTela extends Component<Props, Estado> {
  state: Estado = { erro: null, tentativa: 0 }

  static getDerivedStateFromError(erro: Error): Partial<Estado> {
    return { erro }
  }

  componentDidCatch(erro: Error, info: ErrorInfo) {
    // Fica no console para quem for investigar; a tela mostra só o curto
    console.error('Tela quebrou:', erro, info.componentStack)
  }

  render() {
    const { erro, tentativa } = this.state
    if (!erro) return <div key={tentativa} className="contents">{this.props.children}</div>

    return (
      <div role="alert" className="max-w-xl mx-auto mt-6 sm:mt-12 bg-white border border-gray-200 rounded-2xl p-6 sm:p-8 text-center animate-entrada">
        <span className="mx-auto w-12 h-12 rounded-full bg-amber-50 text-amber-700 flex items-center justify-center">
          <TriangleAlert size={24} strokeWidth={1.75} aria-hidden />
        </span>
        <h2 className="font-display text-xl font-semibold text-gray-900 mt-4">Esta tela teve um problema</h2>
        <p className="text-sm text-gray-600 mt-1.5 max-w-sm mx-auto">
          O resto do sistema continua funcionando. Tente de novo; se repetir, recarregue a página.
        </p>
        <div className="mt-5 flex flex-col sm:flex-row gap-2 justify-center">
          <Botao variante="primario" icone={RotateCcw} onClick={() => this.setState(s => ({ erro: null, tentativa: s.tentativa + 1 }))}>
            Tentar de novo
          </Botao>
          <Botao variante="secundario" icone={RefreshCw} onClick={() => window.location.reload()}>
            Recarregar a página
          </Botao>
        </div>
        <details className="mt-5 text-left">
          <summary className="text-xs text-gray-500 cursor-pointer text-center">Detalhe para quem for consertar</summary>
          <pre className="mt-2 text-xs text-gray-600 bg-gray-50 border border-gray-200 rounded-lg p-3 whitespace-pre-wrap break-words max-h-40 overflow-auto">
            {erro.message}
          </pre>
        </details>
      </div>
    )
  }
}
