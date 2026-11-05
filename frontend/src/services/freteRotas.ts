import { ctesApi, type FreteCalculoResult } from './api'

// O resultado contém os grupos de NF já usados no cálculo. Atualizar as rotas
// mantém esses grupos e pede ao servidor um novo rateio para a carga inteira.
export async function recalcularRotasFrete(
  resultado: FreteCalculoResult,
  valorCarga: number,
  valorPedagio: number,
): Promise<FreteCalculoResult> {
  const { data } = await ctesApi.calcularFrete({
    valorCarga,
    valorPedagio,
    itens: resultado.itens.map(it => ({
      numeroNF: it.numeroNF,
      emitente: it.emitente,
      cidadeEmitente: it.cidadeOrigem ?? '',
      destinatario: it.destinatario,
      cidadeDestino: it.cidadeDestino,
      uf: it.uf,
      pesoKg: it.pesoKg,
      nfsDetalhadas: it.nfsDetalhadas,
    })),
  })
  return data
}
