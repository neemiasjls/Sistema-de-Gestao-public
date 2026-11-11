import api, { downloadBlob, nomeDoArquivo } from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Viagens dos caminhões (no lugar do acerto mensal em planilha), Retornos e o
// painel do motorista. As contas moram na API (ViagemCaminhaoCalculo).

export const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho',
  'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
export const nomeMes = (mes: number, ano: number) => `${MESES[mes - 1] ?? mes} de ${ano}`

export type StatusViagem = 'lancada' | 'conferida' | 'fechada'
export const NOME_STATUS: Record<StatusViagem, string> = {
  lancada: 'Lançada', conferida: 'Conferida', fechada: 'Fechada',
}

// Tipos de despesa (iguais aos da API e da folha do motorista)
export const TIPOS_DESPESA = [
  ['CARREGAMENTO', 'Carregamento'], ['GASOLINA', 'Gasolina'], ['CRED_CELULAR', 'Crédito de celular'],
  ['CAIXINHA', 'Caixinha'], ['BORRACHARIA', 'Borracharia'], ['AUTO_ELETRICO', 'Auto elétrico'],
  ['LAVAGEM', 'Lavagem'], ['LUBRIFICACAO', 'Lubrificação'], ['MECANICO', 'Mecânico'],
  ['DESCARGA', 'Descarga'], ['OUTRA', 'Outra despesa'],
] as const
export type TipoDespesa = typeof TIPOS_DESPESA[number][0]
export const nomeDespesa = (t: string) => TIPOS_DESPESA.find(x => x[0] === t)?.[1] ?? t

/**
 * Cliente da carga de volumes da viagem. Os fretes (valor*) são os da tabela de
 * preços no dia em que a linha entrou (VIA-10): a tela não muda, só mostra.
 * Linha que volta com o id e o mesmo cliente guarda os valores; sem id, a API
 * pega os da tabela. produtosB = amarelas + brancas; produtosBSemCor é o total
 * das viagens antigas (planilha), que não dizia a cor.
 */
export type Volumes = {
  id?: number; cliente: string; caixasVolumes: number; caixasProdutoE: number; caixasProdutoD: number; produtosB: number
  produtosBAmarelas: number; produtosBBrancas: number; produtosC: number; produtosBSemCor?: number
  valorCaixa: number; valorProdutoE: number; valorProdutoD: number; valorProdutoB: number; valorProdutoC: number; total?: number
}
export type Abastecimento = {
  data: string | null; km: number | null; litros: number | null; valor: number; local: string | null
  observacao: string | null; forma: 'NOTA' | 'PAGO'
}
export type Despesa = { tipo: string; descricao: string | null; valor: number; observacao: string | null }
export type Carga = {
  tipo: 'VOLUMES' | 'RETORNO'; transportadora: string | null; data: string | null; origem: string | null
  destino: string | null; valor: number; retornoId: number | null
  /** Anotação do motorista × retorno importado (VIA-22): ligado, manual, conferir, sem_par, solto */
  id?: number; ligacao?: SituacaoLigacao | null; ligacaoAviso?: string | null
  retornoLigado?: { id: number; data: string; transportadora: string | null; freteTotal: number; viagemId: number | null } | null
}
export type SituacaoLigacao = 'ligado' | 'manual' | 'conferir' | 'sem_par' | 'solto'
/** Anotação "outra carga" do motorista que ficou sem retorno ligado no mês, com os retornos livres perto da data */
export type AnotacaoPendente = {
  id: number; viagemId: number; transportadora: string | null; data: string | null; origem: string | null; destino: string | null
  valor: number; situacao: SituacaoLigacao; aviso: string | null; placa: string; placaCarreta: string | null
  viagemSaida: string | null; viagemStatus: StatusViagem; motorista: string; candidatos: Retorno[]
}

export type Retorno = {
  id: number; data: string; destino: string | null; transportadora: string | null; valorFrete: number; pedagio: number
  adicional: number; descontoSeguro: number; porcentagemTip: number | null; descontoManual: number | null; desconto: number
  freteTotal: number; motorista: string | null; placa: string | null; observacoes: string | null; formaPagamento: string | null
  viagemId: number | null; porcentagemIcms: number; foraDoControle: boolean; origem: 'escritorio' | 'motorista' | 'importacao'
  conferido: boolean
  /** VIA-27: descarga paga (= Adicional); contrato da Strada (PDF) e o que sai dele. Nulo = falta dado */
  descarga?: number | null; contrato?: string | null; valorContrato?: number | null
  adiantamentoDescarga?: number | null; reembolsoDescarga?: number | null
  /** ligado, manual, conferir, sem_par, solto (nulo = ainda não procurado) */
  contratoLigacao?: string | null; contratoAviso?: string | null
}

/** Contrato da Strada com PDF ainda sem retorno (para escolher à mão) */
export type ContratoLivre = { numero: string; placa: string | null; data: string | null; valor: number | null; nf: string | null }

export type Calculo = {
  totalVolumes: number; totalRetornos: number; valorTotal: number; dieselNota: number; dieselPago: number; totalDiesel: number
  diarias: number; outrasDespesas: number; despesas: number; totalDespesas: number; sobraAnterior: number
  dinheiroViagem: number; sobra: number; resultado_: number; caixinha: number; descarga: number; baseComissao: number
  comissao: number; icmsRetornos: number; comissaoPlanilha: number; caixasVolumes: number; caixasProdutoE: number
  caixasProdutoD: number; produtosB: number; kmRodados: number | null; media: number | null
  produtosBAmarelas: number; produtosBBrancas: number; produtosC: number
}

export type ViagemResumo = {
  id: number; placa: string; placaCarreta: string | null; numero: number; motorista: string; motoristaId: number | null
  /** Acerto a que a viagem pertence: "M{id}" (motorista, VIA-21) ou "P{placa}" (sem motorista) */
  grupo?: string
  destino: string | null; dataSaida: string | null; dataChegada: string | null; status: StatusViagem
  origem: 'motorista' | 'escritorio' | 'importacao'; enviadoEm: string | null
  totalVolumes: number; totalRetornos: number; valorTotal: number; totalDespesas: number; comissao: number; sobra: number
  resultado: number; semVolumes: boolean; retornosAConferir: number; retornosDeclarados: number
}

export type ListaViagens = { ano: number; mes: number; viagens: ViagemResumo[]; placas: string[]; aConferir: number }

export type CarregamentoCandidato = { id: number; data: string; placa: string; motorista: string | null; nomeArquivo: string | null; aplicado: boolean; clientes: number }

export type ViagemDetalhe = {
  id: number; motoristaId: number | null; motorista: string; motoristaNome: string; placa: string; placaCarreta: string | null
  destino: string | null; dataSaida: string | null; dataChegada: string | null; kmSaida: number | null; kmChegada: number | null
  litros: number | null; levou: number; dataDinheiro: string | null; devolveu: number; sobraAnteriorInformada: number | null
  diariasQuantidade: number; valorDiaria: number; porcentagemComissao: number; mesReferencia: number; anoReferencia: number
  status: StatusViagem; origem: 'motorista' | 'escritorio' | 'importacao'; observacoes: string | null
  enviadoEm: string | null; conferidoEm: string | null; criadoEm: string; atualizadoEm: string
  numero: number; anteriorId: number | null; proximaId: number | null
  volumes: Volumes[]; abastecimentos: Abastecimento[]; despesas: (Despesa & { nome: string })[]; cargas: Carga[]
  retornos: Retorno[]; retornosSugeridos: Retorno[]; carregamentos: CarregamentoCandidato[]; calculo: Calculo
}

export type ViagemEntrada = {
  motoristaId: number | null; motoristaNome: string | null; placa: string; placaCarreta: string | null; destino: string | null
  dataSaida: string | null; dataChegada: string | null; kmSaida: number | null; kmChegada: number | null; litros: number | null
  levou: number; dataDinheiro: string | null; devolveu: number; sobraAnteriorInformada: number | null
  diariasQuantidade: number; porcentagemComissao: number | null; mesReferencia: number | null; anoReferencia: number | null
  observacoes: string | null; volumes?: Volumes[]; abastecimentos: Abastecimento[]; despesas: Despesa[]; cargas: Carga[]
}

/** Dados da CNH e documentos do motorista (VIA-20). Datas "aaaa-mm-dd". */
export type DadosCnh = {
  cpf: string | null; rg: string | null; rgOrgaoUf: string | null; dataNascimento: string | null
  cidadeNascimento: string | null; ufNascimento: string | null; cnhRegistro: string | null; cnhCategoria: string | null
  cnhEmissao: string | null; cnhValidade: string | null; pai: string | null; mae: string | null
}
export type MotoristaFrota = DadosCnh & {
  id: number; nome: string; apelidos: string | null; login: string; porcentagemComissao: number
  /** Placa habitual antiga: não aparece mais na tela (VIA-20) */
  placaCavalo?: string | null; placaCarreta?: string | null; ativo: boolean; acesso: boolean
  temCnhPdf?: boolean; cnhAtualizadaEm?: string | null
  usuarioId?: number | null
  /** Tem usuário de acesso (perfil motorista), ativo ou não */
  temUsuario?: boolean
}
export type MotoristaEntrada = DadosCnh & {
  nome: string; apelidos: string | null; login: string; porcentagemComissao: number; ativo: boolean; usuarioId?: number | null
}
/** Leitura do PDF da CNH: o que foi lido, quem é no cadastro e o que muda (antes e depois) */
export type LeituraCnh = {
  dados: DadosCnh & { nome: string | null }
  naoAchou: string[]
  motorista: { id: number; nome: string; ativo: boolean; temCnhPdf: boolean } | null
  achadoPor: 'cpf' | 'nome' | null
  diferencas: { campo: string; rotulo: string; atual: string | null; novo: string | null }[]
  iguais: number
}
export type PlacaFrota = { id: number; placa: string; tipo: 'Cavalo' | 'Carreta'; responsavel: string | null; ativo: boolean; viagens: number }
export type LeituraCrlv = {
  arquivo: string; placa: string | null; especie: string | null; tipo: 'Cavalo' | 'Carreta' | null
  tipoAtual: string | null; situacao: 'nova' | 'muda' | 'igual' | 'aviso'; aviso: string | null
}
export type PlacaEntrada = { placa: string; tipo: 'Cavalo' | 'Carreta'; responsavel: string | null; ativo: boolean }
/** Usuário de motorista criado à mão, sem cadastro de motorista */
export type UsuarioMotoristaSemCadastro = { id: number; nome: string; login: string; ativo: boolean }

/** Fretes de um cliente de volumes como a viagem grava (painel de preços, VIA-10). */
export type PrecoCliente = {
  id: number; cliente: string; apelidos: string | null; valorCaixa: number; valorProdutoE: number; valorProdutoD: number
  valorProdutoB: number; valorProdutoC: number; estoqueClienteId: number | null; ordem: number; ativo: boolean
}

export type ItemCarregamento = {
  cliente: string; volumes: number; produtoE: number; produtoD: number; produtosB: number
  amarelas: number; brancas: number; produtosC: number
}

export type Carregamento = {
  id: number; data: string; placa: string; motorista: string | null; destino: string | null; observacao: string | null
  nomeArquivo: string | null; viagemId: number | null; itens: ItemCarregamento[]
  volumes: number; produtoE: number; produtoD: number; produtosB: number; amarelas: number; brancas: number; produtosC: number
  /** Lido pela regra antiga (somava detalhe e totais): ler de novo */
  regraAntiga: boolean
}

export type ResultadoReler = {
  id: number; placa: string; data: string; clientes: number; viagemId: number | null; aplicado: boolean
  volumes: number; produtoE: number; produtoD: number; amarelas: number; brancas: number; produtosC: number; avisos: string[]
}

/** Situação de cada aba na importação (UI-22): COMPLETADA e SUBSTITUIDA gravam sozinhas; só a provável (CONFIRMAR) espera. */
export type ViagemDoAcerto = {
  aba: string; situacao: 'NOVA' | 'IGUAL' | 'CONFIRMAR' | 'SUBSTITUIDA' | 'COMPLETADA'; grau?: string | null
  viagemId: number; origem: string; status: StatusViagem; saida: string | null
  diferencas: { campo: string; sistema: string; arquivo: string; completou?: boolean }[]
}
export type ResultadoAcerto = {
  arquivo: string; erro?: string; placa?: string; mes?: number; ano?: number; criadas?: number; puladas?: number
  iguais?: number; substituidas?: number; completadas?: number; viagens?: ViagemDoAcerto[]
  avisos?: string[]; conferencia?: { aba: string; viagemId: number; confere: boolean; diferencas: string[]; comissao: number; comissaoPlanilha: number | null; sobra: number }[]
}
export type ResultadoCarregamento = {
  arquivo: string; erro?: string
  cargas?: {
    aba: string; guardado: boolean; id?: number; placa?: string; data?: string; clientes?: number; viagemId?: number | null; aplicado?: boolean
    volumes?: number; produtoE?: number; produtoD?: number; amarelas?: number; brancas?: number; produtosC?: number; avisos: string[]
  }[]
}
export type ResultadoRetornos = {
  arquivo: string; erro?: string; mes?: number; ano?: number; linhas?: number; novos?: number; jaExistiam?: number
  vinculados?: number; anotacoesLigadas?: number; anotacoesPendentes?: number; total?: number; totalPlanilha?: number | null; confere?: boolean; avisos?: string[]
}

export type RetornosMes = {
  ano: number; mes: number
  retornos: {
    retorno: Retorno; viagem: { id: number; placa: string; dataSaida: string | null; destino: string | null; status: StatusViagem } | null
    anotacao: { id: number; viagemId: number; data: string | null; valor: number; ligacao: SituacaoLigacao | null; ligacaoAviso: string | null } | null
  }[]
  anotacoesPendentes: AnotacaoPendente[]; comAnotacao: number
  total: number; totalFrete: number; quantidade: number; aConferir: number; semViagem: number
}

export type RetornoEntrada = {
  data: string | null; destino: string | null; transportadora: string | null; valorFrete: number; pedagio: number
  adicional: number; descontoSeguro: number; porcentagemTip: number | null; descontoManual: number | null
  motorista: string | null; placa: string | null; observacoes: string | null; formaPagamento: string | null
  viagemId: number | null; porcentagemIcms: number; foraDoControle: boolean; conferido: boolean | null
  /** VIA-27: contratoManual com número = escolhido à mão; sem número = solto; false = o sistema procura */
  contrato?: string | null; contratoManual?: boolean
}

const multipart = { headers: { 'Content-Type': 'multipart/form-data' } }
// Viagens repetidas já gravadas (docs/planos/importacoes-mesclar.md, Partes 4 e 5)
export type OrigemViagem = 'motorista' | 'escritorio' | 'importacao'
export type ResumoDuplicada = {
  id: number; placa: string; motoristaNome: string | null; origem: OrigemViagem; status: StatusViagem
  dataSaida: string | null; dataChegada: string | null; destino: string | null; chaveImportacao: string | null
  aba: string | null; levou: number; caixas: number; abastecimentos: number; despesas: number
  retornos: number; anotacoes: number; carregamentos: string[]
}
export type ParDuplicado = {
  placa: string; grau: 'data' | 'provavel'; sinal: string | null
  manterId: number; removerId: number; valoresDe: number; motivo: string; situacaoFinal: StatusViagem
  fica: ResumoDuplicada; sai: ResumoDuplicada
  diferencas: { campo: string; fica: string; sai: string }[]
  mudamDeViagem: {
    retornos: { id: number; data: string | null; transportadora: string | null; freteTotal: number; sai: boolean }[]
    carregamento: { id: number; data: string; vaiPara: 'fica' | 'solto' }[]
    anotacoes: { id: number; tipo: string; data: string | null; transportadora: string | null; valor: number }[]
  }
}
export type ViagemVazia = {
  id: number; placa: string; motoristaNome: string | null; dataSaida: string | null; dataChegada: string | null
  origem: OrigemViagem; status: StatusViagem; anotacoes: number; carregamento: boolean
}
export type Duplicadas = { pares: ParDuplicado[]; vazias: ViagemVazia[] }
export type ItemJuntado = { manterId?: number; removerId?: number; apagadaId?: number; ok: boolean; avisos: string[] }

const arquivos = (lista: File[]) => {
  const fd = new FormData()
  lista.forEach(a => fd.append('arquivos', a))
  return fd
}

export const viagensApi = {
  listar: (ano: number, mes: number, placa?: string) =>
    api.get('/api/viagens-caminhao', { params: { ano, mes, placa: placa || undefined } })
      .then(r => comoObjeto<ListaViagens>(r.data, ['viagens', 'placas'], 'viagens')),
  detalhe: (id: number) => api.get(`/api/viagens-caminhao/${id}`).then(r =>
    comoObjeto<ViagemDetalhe>(r.data, ['volumes', 'abastecimentos', 'despesas', 'cargas', 'retornos', 'retornosSugeridos', 'carregamentos'], 'viagem')),
  criar: (e: ViagemEntrada) => api.post<{ id: number }>('/api/viagens-caminhao', e).then(r => r.data),
  salvar: (id: number, e: ViagemEntrada) => api.put<{ id: number }>(`/api/viagens-caminhao/${id}`, e).then(r => r.data),
  excluir: (id: number) => api.delete(`/api/viagens-caminhao/${id}`),
  status: (id: number, status: StatusViagem) => api.put(`/api/viagens-caminhao/${id}/status`, { status }),
  /** Prévia (só leitura) das viagens repetidas e das vazias */
  duplicadas: () => api.get('/api/viagens-caminhao/duplicadas').then(r => comoObjeto<Duplicadas>(r.data, ['pares', 'vazias'], 'viagens repetidas')),
  /** Cada par e cada vazia têm resposta própria; um que falha não desfaz os outros */
  juntar: (pares: { manterId: number; removerId: number }[], apagarVazias: number[] = []) =>
    api.post('/api/viagens-caminhao/juntar', { pares, apagarVazias }).then(r => comoObjeto<{ itens: ItemJuntado[] }>(r.data, ['itens'], 'junção').itens),
  aplicarCarregamento: (id: number, carregamentoId: number) =>
    api.post<{ avisos: string[] }>(`/api/viagens-caminhao/${id}/carregamento/${carregamentoId}`).then(r => comoObjeto<{ avisos: string[] }>(r.data, ['avisos'])),
  // Acerto do motorista no mês (VIA-21); sem motorista, pela placa
  exportar: async (quem: { motoristaId?: number | null; placa?: string }, ano: number, mes: number) => {
    const r = await api.get('/api/viagens-caminhao/excel', { params: { motoristaId: quem.motoristaId ?? undefined, placa: quem.placa || undefined, ano, mes }, responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `${mes}-V-acerto.xlsx`))
  },
  /** abas = as viagens já no sistema que devem ser trocadas pelo arquivo (VIA-23); sem abas, nenhuma é trocada */
  importarAcerto: (lista: File[], placa?: string, abas?: string[]) =>
    api.post('/api/viagens-caminhao/importar-acerto', arquivos(lista), { ...multipart, params: { placa: placa || undefined, abas: abas?.length ? abas.join(',') : undefined } })
      .then(r => comoLista<ResultadoAcerto>(r.data, 'importação')),
  importarCarregamento: (lista: File[]) =>
    api.post('/api/viagens-caminhao/importar-carregamento', arquivos(lista), multipart).then(r => comoLista<ResultadoCarregamento>(r.data, 'importação')),
  carregamentos: (ano: number, mes: number) =>
    api.get('/api/viagens-caminhao/carregamentos', { params: { ano, mes } }).then(r => comoLista<Carregamento>(r.data, 'carregamentos')),
  excluirCarregamento: (id: number) => api.delete(`/api/viagens-caminhao/carregamentos/${id}`)
    .then(r => comoObjeto<{ avisos: string[] }>(r.data, ['avisos'], 'carregamento')),
  /** Lê de novo o carregamento guardado com a regra atual (VIA-09) */
  relerCarregamento: (id: number) => api.post(`/api/viagens-caminhao/carregamentos/${id}/reprocessar`)
    .then(r => comoObjeto<ResultadoReler>(r.data, ['avisos'], 'carregamento')),

  motoristas: () => api.get('/api/viagens-caminhao/motoristas').then(r => comoLista<MotoristaFrota>(r.data, 'motoristas')),
  salvarMotorista: (id: number | null, m: MotoristaEntrada) =>
    (id ? api.put(`/api/viagens-caminhao/motoristas/${id}`, m) : api.post('/api/viagens-caminhao/motoristas', m))
      .then(r => r.data as { id: number; aviso: string | null }),
  senhaPadrao: (id: number) => api.post(`/api/viagens-caminhao/motoristas/${id}/senha-padrao`),
  senhaMotorista: (id: number, senha: string) => api.put(`/api/viagens-caminhao/motoristas/${id}/senha`, { senha }),
  ativoMotorista: (id: number, ativo: boolean) =>
    api.put(`/api/viagens-caminhao/motoristas/${id}/ativo`, { ativo }).then(r => r.data as { aviso: string | null }),
  usuariosSemCadastro: () => api.get('/api/viagens-caminhao/motoristas/usuarios-sem-cadastro')
    .then(r => comoLista<UsuarioMotoristaSemCadastro>(r.data, 'usuários de motorista')),

  lerCnh: (arquivo: File) => {
    const fd = new FormData(); fd.append('arquivo', arquivo)
    // "dados" é objeto: na lista de listas virava [] e sumiam o nome e o que ia ser gravado
    return api.post('/api/motoristas-frota/cnh/ler', fd, multipart).then(r => {
      const l = comoObjeto<LeituraCnh>(r.data, ['naoAchou', 'diferencas'], 'leitura da CNH')
      l.dados = comoObjeto<LeituraCnh['dados']>(l.dados, [], 'leitura da CNH')
      return l
    })
  },
  aplicarCnh: (arquivo: File, motoristaId: number | null, dados: LeituraCnh['dados']) => {
    const fd = new FormData()
    fd.append('arquivo', arquivo); fd.append('dados', JSON.stringify(dados))
    if (motoristaId != null) fd.append('motoristaId', String(motoristaId))
    return api.post('/api/motoristas-frota/cnh/aplicar', fd, multipart)
      .then(r => r.data as { id: number; criado: boolean; login: string | null; aviso: string | null })
  },
  baixarCnh: async (id: number, nome: string) => {
    const r = await api.get(`/api/motoristas-frota/${id}/cnh`, { responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `CNH ${nome}.pdf`))
  },

  placas: () => api.get('/api/placas').then(r => comoLista<PlacaFrota>(r.data, 'placas')),
  salvarPlaca: (id: number | null, p: PlacaEntrada) =>
    (id ? api.put(`/api/placas/${id}`, p) : api.post('/api/placas', p)).then(r => r.data as { id: number }),
  lerCrlv: (lista: File[]) => api.post('/api/placas/crlv/ler', arquivos(lista), multipart).then(r => comoLista<LeituraCrlv>(r.data, 'leitura dos documentos')),
  aplicarCrlv: (itens: { placa: string; tipo: string }[]) =>
    api.post('/api/placas/crlv/aplicar', itens).then(r => r.data as { criadas: number; mudadas: number }),
  excluirPlaca: (id: number) => api.delete(`/api/placas/${id}`).then(r => r.data as { desativada: boolean; mensagem: string }),

  precos: () => api.get('/api/viagens-caminhao/precos').then(r => comoLista<PrecoCliente>(r.data, 'tabela de frete')),
  salvarPreco: (id: number | null, p: Omit<PrecoCliente, 'id'>) =>
    id ? api.put(`/api/viagens-caminhao/precos/${id}`, p) : api.post('/api/viagens-caminhao/precos', p),
  excluirPreco: (id: number) => api.delete(`/api/viagens-caminhao/precos/${id}`),
}

export const retornosApi = {
  listar: (ano: number, mes: number) => api.get('/api/retornos', { params: { ano, mes } })
    .then(r => comoObjeto<RetornosMes>(r.data, ['retornos', 'anotacoesPendentes'], 'retornos')),
  ligarAnotacao: (cargaId: number, retornoId: number | null) => api.put(`/api/retornos/anotacoes/${cargaId}`, { retornoId }),
  criar: (e: RetornoEntrada) => api.post<{ id: number }>('/api/retornos', e).then(r => r.data),
  salvar: (id: number, e: RetornoEntrada) => api.put(`/api/retornos/${id}`, e),
  excluir: (id: number) => api.delete(`/api/retornos/${id}`),
  vincular: (id: number, viagemId: number | null) => api.put(`/api/retornos/${id}/viagem`, { viagemId }),
  contratosLivres: (placa: string | null, data: string | null) => api.get('/api/retornos/contratos-livres', { params: { placa, data } })
    .then(r => comoLista<ContratoLivre>(r.data, 'contratos')),
  importar: (lista: File[]) => api.post('/api/retornos/importar', arquivos(lista), multipart).then(r => comoLista<ResultadoRetornos>(r.data, 'importação')),
  exportar: async (ano: number, mes: number) => {
    const r = await api.get('/api/retornos/excel', { params: { ano, mes }, responseType: 'blob' })
    downloadBlob(r.data as Blob, nomeDoArquivo(r.headers['content-disposition'] as string | undefined, `${mes}-R-${ano}.xlsx`))
  },
}

// ── Painel do motorista ──
export type FolhaMotorista = {
  placa: string; placaCarreta: string | null; destino: string | null; dataSaida: string | null; dataChegada: string | null
  kmSaida: number | null; kmChegada: number | null; litros: number | null; levou: number; dataDinheiro: string | null
  diariasQuantidade: number; observacoes: string | null; abastecimentos: Abastecimento[]; despesas: Despesa[]; cargas: Carga[]
}
export type InicioMotorista = {
  nome: string; primeiroNome: string; valorDiaria: number
  /** Para "de quem é a caixinha": clientes da tabela e nomes que ele já usou */
  sugestoesCaixinha?: string[]
  ultima: { id: number; enviadoEm: string | null; placa: string; destino: string | null; dataSaida: string | null; podeCorrigir: boolean; folha: FolhaMotorista | null } | null
}

/** Sobra da viagem anterior do mesmo motorista (VIA-21; só sobra e data) */
export type SobraAnterior = { temAnterior: boolean; sobra: number; dataViagemAnterior: string | null }

export type PlacasPainel = { cavalos: string[]; carretas: string[] }

export const motoristaPainelApi = {
  placas: () => api.get('/api/motorista-painel/placas').then(r => comoObjeto<PlacasPainel>(r.data, ['cavalos', 'carretas'], 'placas')),
  inicio: () => api.get('/api/motorista-painel/inicio').then(r => comoObjeto<InicioMotorista>(r.data, [], 'painel')),
  sobraAnterior: (placa: string, dataSaida: string | null, viagemId: number | null, signal?: AbortSignal) =>
    api.get('/api/motorista-painel/sobra-anterior', { params: { placa, dataSaida: dataSaida || undefined, viagemId: viagemId ?? undefined }, signal })
      .then(r => comoObjeto<SobraAnterior>(r.data, [], 'sobra anterior')),
  enviar: (f: FolhaMotorista) => api.post<{ id: number; enviadoEm: string }>('/api/motorista-painel/viagens', paraEntrada(f)).then(r => r.data),
  corrigir: (id: number, f: FolhaMotorista) => api.put<{ id: number; enviadoEm: string }>(`/api/motorista-painel/viagens/${id}`, paraEntrada(f)).then(r => r.data),
}

const paraEntrada = (f: FolhaMotorista): ViagemEntrada => ({
  motoristaId: null, motoristaNome: null, placa: f.placa, placaCarreta: f.placaCarreta, destino: f.destino,
  dataSaida: f.dataSaida, dataChegada: f.dataChegada, kmSaida: f.kmSaida, kmChegada: f.kmChegada, litros: f.litros,
  levou: f.levou, dataDinheiro: f.dataDinheiro, devolveu: 0, sobraAnteriorInformada: null, diariasQuantidade: f.diariasQuantidade,
  porcentagemComissao: null, mesReferencia: null, anoReferencia: null, observacoes: f.observacoes,
  abastecimentos: f.abastecimentos, despesas: f.despesas, cargas: f.cargas,
})

/** Diárias pelas datas: um dia por noite fora (R$ 100), passos de meia. */
export function diariasPelasDatas(saida: string | null, chegada: string | null): number {
  if (!saida || !chegada) return 0
  const d = (Date.parse(chegada + 'T12:00:00') - Date.parse(saida + 'T12:00:00')) / 86_400_000
  return isFinite(d) && d > 0 ? Math.round(d) : 0
}

export const placaValida = (p: string) => /^[A-Z]{3}-?\d[A-Z0-9]\d{2}$/.test(p.trim().toUpperCase())
export const normalizarPlaca = (p: string) => {
  const t = p.trim().toUpperCase().replace(/\s/g, '')
  const m = /^([A-Z]{3})-?(\d[A-Z0-9]\d{2})$/.exec(t)
  return m ? `${m[1]}-${m[2]}` : t
}
