import { useServidor } from '../../services/servidor'

// Aviso de espera (D-02). Aparece quando alguma requisição passa de 4 s:
//   - servidor dormindo (nada respondeu ainda, ou faz mais de 14 min): "Ligando
//     o servidor", com a explicação de que leva até um minuto;
//   - servidor acordado e só lento: "Ainda carregando".
// Uma faixa fina e uma linha de texto, sem cobrir nada: dá para continuar
// olhando e usando o que já está na tela.

export default function ServidorAcordando({ variante = 'faixa' }: { variante?: 'faixa' | 'cartao' }) {
  const { lento, acordando } = useServidor()

  if (variante === 'cartao') {
    // Login e a primeira carga: sem layout em volta
    return (
      <div role="status" aria-live="polite" className={`transition-opacity duration-300 ${lento ? 'opacity-100' : 'opacity-0'}`}>
        {lento && (
          <div className="text-center space-y-2">
            <div className="barra-espera mx-auto w-40" aria-hidden />
            <p className="text-sm font-medium">{acordando ? 'Ligando o servidor' : 'Ainda carregando'}</p>
            <p className="text-xs opacity-80">{acordando ? 'Na primeira vez do dia leva até um minuto.' : 'A resposta está demorando um pouco.'}</p>
          </div>
        )}
      </div>
    )
  }

  return (
    // Na linha, logo abaixo da barra de cima: empurra a tela um pouco em vez de
    // cobrir o que está em cima dela (o "Como funciona", um botão)
    <div role="status" aria-live="polite" className="flex-shrink-0">
      {lento && (
        <div className="animate-fundo">
          <div className="barra-espera" aria-hidden />
          <p className="flex items-center justify-center gap-2 px-4 py-1.5 text-xs sm:text-sm text-tinta-900 bg-gema-50 border-b border-gema-200">
            <span className="font-semibold">{acordando ? 'Ligando o servidor' : 'Ainda carregando'}</span>
            <span className="text-tinta-800">{acordando ? 'leva até um minuto na primeira vez do dia' : 'a resposta está demorando'}</span>
          </p>
        </div>
      )}
    </div>
  )
}
