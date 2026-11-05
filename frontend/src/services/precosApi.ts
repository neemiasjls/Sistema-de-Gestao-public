import api from './api'
import { comoObjeto } from '../utils/resposta'

// Painel de preços por cliente (VIA-10): frete e venda num lugar só. Não tem
// cadastro próprio — lê e grava a tabela de frete das viagens e o cadastro do
// estoque, ligados um ao outro (PrecosController).

export type TipoPacote = { id: number; nome: string; codigo: string | null; produto: 'PRODUTOB' | 'PRODUTOC'; unidadesPorPacote: number }

export type LinhaPreco = {
  chave: string
  /** Cliente da tabela de frete (leva volumes) */
  freteId: number | null
  /** Cliente do cadastro do estoque (compra produtosB, produtosC, produtosA) */
  estoqueId: number | null
  cliente: string
  /** Nome no estoque, quando diferente (COM. OMEGA × COMERCIAL OMEGA) */
  nomeEstoque: string | null
  apelidos: string | null
  ativo: boolean
  caixa: number | null; produtoE: number | null; produtoD: number | null
  /** Frete por pacote de produtoB e de produtoC (do cadastro do estoque) */
  produtoB: number | null; produtoC: number | null
  venda: { produtoAKg: number; precos: { tipoProdutoBId: number; preco: number | null }[] } | null
}

export type Painel = {
  tipos: TipoPacote[]
  linhas: LinhaPreco[]
  estoqueLivres: { id: number; nome: string; ativo: boolean }[]
}

export const precosApi = {
  painel: () => api.get('/api/precos').then(r => comoObjeto<Painel>(r.data, ['tipos', 'linhas', 'estoqueLivres'], 'preços')),
  salvarFrete: (id: number, p: { cliente: string; apelidos: string | null; valorCaixa: number; valorProdutoE: number | null; valorProdutoD: number; ativo: boolean }) =>
    api.put(`/api/precos/frete/${id}`, p),
  salvarEstoque: (id: number, p: { freteProdutoB: number; freteProdutoC: number | null; precoProdutoAKg: number | null; precos: { tipoProdutoBId: number; preco: number | null }[] }) =>
    api.put(`/api/precos/estoque/${id}`, p),
  ligar: (freteId: number, estoqueClienteId: number | null, criar = false) =>
    api.put(`/api/precos/frete/${freteId}/estoque`, { estoqueClienteId, criar }).then(r => r.data as { estoqueId: number | null }),
  novoCliente: (cliente: string, apelidos: string | null, levaVolumes: boolean) =>
    api.post('/api/precos/clientes', { cliente, apelidos, levaVolumes }).then(r => r.data as { estoqueId: number; freteId: number | null }),
}
