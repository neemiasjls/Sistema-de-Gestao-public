import ProdutosCAReceber from '../components/ProdutosCAReceber'

// Aba ProdutosC (EST-08, 11/11/2026): só para ver todas as vendas de produtosC e os valores.
// O recebimento da BRZ fica em Boletos → A receber → BRZ (produtosC).
export default function EstoqueProdutosCPage() {
  return (
    <div className="space-y-5 w-full">
      <ProdutosCAReceber modo="vendas" />
    </div>
  )
}
