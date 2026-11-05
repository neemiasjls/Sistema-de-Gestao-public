import api from './api'
import { comoLista, comoObjeto } from '../utils/resposta'

// Painel do dia (F-01): tudo da tela inicial numa chamada só (GET /api/painel).
// A conta (o que vence, o que falta, o que é de hoje) é da API; aqui só se confere
// o formato, para uma resposta errada virar "não consegui carregar" (UI-10).

export type SituacaoTarefa = 'feito' | 'pendente' | 'andamento' | 'lembrete'

export type Tarefa = {
  chave: string
  titulo: string
  detalhe: string | null
  link: string
  situacao: SituacaoTarefa
}

export type DiaRotina = { dia: number; nome: string; tarefas: Tarefa[] }

export type BoletoPainel = {
  origem: string            // POSTO, AVULSO (a pagar) ou CD, GIRASSOL (a receber)
  id: number
  quem: string
  descricao: string | null
  numero: string
  valor: number | null
  vencimento: string        // aaaa-mm-dd
  dias: number              // negativo = vencido há tantos dias
}

export type GrupoBoletos = {
  vencidos: { quantidade: number; total: number; semValor: number }
  aVencer: { quantidade: number; total: number; semValor: number }
  hojeQuantidade: number
  semVencimento: number
  itens: BoletoPainel[]
  mais: number
}

export type EmailPendente = {
  cliente: string
  rotulo: string
  dataCarga: string
  situacao: 'pendente' | 'teste' | 'erro'
  detalhe: string | null
}

export type ControleProblema = {
  id: number
  numero: number
  data: string
  status: 'incompleto' | 'nao_bate'
  faltando: string[]
  diferencas: { nome: string; valor: number; emProdutosC: boolean }[]
}

export type RomaneioSemControle = {
  numero: number
  data: string
  caixas: number
  acertoId: number | null
  acertoNumero: number | null
}

export type AcertoPainel = {
  id: number
  numero: number
  semana: string
  romaneios: number
  boletos: number
  boletosSemValor: number
  caixas: number
  aPagar: number
  pagamentos: number
  saldo: number
  arquivos: number
}

export type Painel = {
  hoje: string
  segunda: string
  domingo: string
  diaHoje: number
  boletos: { pagar: GrupoBoletos; receber: GrupoBoletos }
  rotina: DiaRotina[]
  mensal: Tarefa[]
  emails: EmailPendente[]
  /** CNH vencida (dias < 0) ou que vence em até 30 dias */
  cnh?: { id: number; nome: string; validade: string; dias: number }[]
  volumes: {
    controles: ControleProblema[]
    conferidos: number
    batem: number
    /** null = a lista dos romaneios da fazenda não veio (avisar, UI-04) */
    romaneiosSemControle: RomaneioSemControle[] | null
  }
  acerto: { semana: string; atual: AcertoPainel | null; anterior: AcertoPainel | null }
  semana: {
    cargas: number
    placas: number
    valorCargas: number
    notas: number
    ctes: number
    ctesCancelados: number
    frete: number
    romaneios: number
  }
}

const grupo = (v: unknown): GrupoBoletos => {
  const g = comoObjeto<GrupoBoletos>(v, ['itens'], 'boletos do painel')
  return {
    ...g,
    vencidos: comoObjeto(g.vencidos, [], 'boletos vencidos'),
    aVencer: comoObjeto(g.aVencer, [], 'boletos a vencer'),
  }
}

export async function carregarPainel(): Promise<Painel> {
  const { data } = await api.get('/api/painel')
  const p = comoObjeto<Painel>(data, ['rotina', 'mensal', 'emails'], 'painel')
  const boletos = comoObjeto<Painel['boletos']>(p.boletos, [], 'boletos do painel')
  const volumes = comoObjeto<Painel['volumes']>(p.volumes, ['controles'], 'volumes do painel')
  return {
    ...p,
    boletos: { pagar: grupo(boletos.pagar), receber: grupo(boletos.receber) },
    rotina: comoLista<DiaRotina>(p.rotina).map(d => ({ ...d, tarefas: comoLista<Tarefa>(d.tarefas) })),
    volumes: {
      ...volumes,
      romaneiosSemControle: volumes.romaneiosSemControle === null ? null : comoLista<RomaneioSemControle>(volumes.romaneiosSemControle),
    },
    acerto: comoObjeto(p.acerto, [], 'acerto do painel'),
    semana: comoObjeto(p.semana, [], 'semana do painel'),
  }
}
