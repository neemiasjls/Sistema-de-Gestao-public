import api from './api'
import { comDados, comoObjeto } from '../utils/resposta'

// Extrato da conta PJ no Sicoob, lido pela API do banco (só consulta)

export interface LancamentoSicoob {
  id: string
  data: string | null
  credito: boolean
  valor: number
  descricao: string
  documento: string
  cpfCnpj: string
  complemento: string
  pix: PixAchado | null
}

// Pix de cliente que bate com boletos a receber em aberto (BOL-14)
export interface PixAchado {
  cliente: 'CD' | 'GIRASSOL' | null
  pagador: string | null      // nome da configuração da emissão
  semCnpj: boolean            // o banco não informou o CNPJ: achado pelo nome do pagador escrito no Pix
  certo: boolean
  duvida: boolean
  maisOpcoes: boolean
  opcoes: BoletoConferido[][]
  jaRecebidos: BoletoConferido[]
}

export interface ResultadoBaixaPix {
  id: number
  nossoNumero: string
  valor: number | null
  vencimento: string | null
  situacao: 'baixado' | 'ja-no-banco' | 'ja-recebido' | 'recusado' | 'incerto' | 'erro'
  mensagem: string
}

export interface ExtratoSicoob {
  configurado: boolean
  erro?: string
  inicio: string
  fim: string
  hoje?: string
  saldo?: number | null
  saldoLimite?: number | null
  saldoBloqueado?: number | null
  entradas?: number
  saidas?: number
  lancamentos: LancamentoSicoob[]
}

export interface BoletoConferido {
  id: number
  cliente: 'CD' | 'GIRASSOL'
  rotulo: string
  nossoNumero: string
  valor: number
  vencimento: string | null
  recebidoEm: string | null
  placa: string
  dataCarga: string | null
}

export interface SituacaoBoletoBanco {
  id: number
  nossoNumero: string
  valor: number | null
  recebidoEm: string | null
  situacao: string | null
  liquidado: boolean
  dataLiquidacao: string | null
  valorPago: number | null
}

export interface ConferenciaBoletos {
  configurado: boolean
  erro?: string
  inicio: string
  fim: string
  pagosSemBaixa: { boleto: BoletoConferido; dataCredito: string; valorCredito: number }[]
  vencidosSemCredito: BoletoConferido[]
  creditos: { data: string; valor: number; achou: boolean; duvida: boolean; boletos: BoletoConferido[]; alternativas: BoletoConferido[][] }[]
}

// Boletos dos postos pagos pelo extrato (ABA-07): o que casou exato já vem marcado pago; o resto é dúvida
export interface BoletoPostoVisto {
  id: number
  posto: string
  numero: string
  valor: number
  vencimento: string | null
  agendadoPara: string | null
}

export interface PostoMarcado {
  boleto: BoletoPostoVisto
  data: string
  chave: string
  descricao: string
  motivo: string
  agora: boolean
}

export interface DuvidaPosto {
  chave: string
  data: string
  valor: number
  descricao: string
  motivo: string
  opcoes: BoletoPostoVisto[]
}

export interface ConferenciaPostos {
  inicio: string
  fim: string
  marcados: PostoMarcado[]
  duvidas: DuvidaPosto[]
}

export interface ResumoConferenciaTudo {
  em: string
  inicio: string
  fim: string
  receber: {
    emAberto: number
    consultados: number
    aindaEmAberto: number
    parouNoLimite: boolean
    marcados: { id: number; cliente: string; nossoNumero: string; valor: number | null; data: string; valorPago: number | null; semDataNoBanco: boolean }[]
    naoMarcados: { id: number; cliente: string; nossoNumero: string; valor: number | null; situacao: string }[]
    erro: string | null
  }
  postos: { marcados: { id: number; posto: string; numero: string; valor: number; data: string }[]; duvidas: number; erro: string | null }
  pix: { aguardando: { id: string; data: string; valor: number; pagador: string; duvida: boolean; boletos: string[] }[]; erro: string | null }
}

export const sicoobApi = {
  // Conferência com o banco (boletos a receber, postos e Pix): só marca o que já foi pago; nada é baixado no banco
  conferirTudo: () =>
    api.post('/api/sicoob/conferir-tudo').then(r => comDados(r, d => {
      const c = comoObjeto<ResumoConferenciaTudo>(d, [], 'conferência com o banco')
      c.receber = comoObjeto<ResumoConferenciaTudo['receber']>(c.receber, ['marcados', 'naoMarcados'], 'boletos a receber')
      c.postos = comoObjeto<ResumoConferenciaTudo['postos']>(c.postos, ['marcados'], 'postos')
      c.pix = comoObjeto<ResumoConferenciaTudo['pix']>(c.pix, ['aguardando'], 'Pix')
      return c
    })),
  // Ação REAL no banco: baixa no Sicoob e apaga do sistema; só o dono, depois da confirmação na tela
  baixarEExcluir: (id: number) =>
    api.post(`/api/sicoob/boletos/${id}/baixar-e-excluir`, { confirmado: true }).then(r => comDados(r, d =>
      comoObjeto<{ nossoNumero: string; mensagem: string }>(d, [], 'baixa do boleto'))),
  conferirPostos: (inicio?: string, fim?: string) =>
    api.post('/api/sicoob/postos/conferir', { inicio, fim }).then(r => comDados(r, d => {
      const c = comoObjeto<ConferenciaPostos>(d, ['marcados', 'duvidas'], 'conferência dos postos')
      c.duvidas = c.duvidas.map(x => comoObjeto<DuvidaPosto>(x, ['opcoes'], 'dúvida do posto'))
      return c
    })),
  marcarPosto: (boletoId: number, chave: string, data: string) =>
    api.post('/api/sicoob/postos/marcar', { boletoId, chave, data }),
  // Ação REAL no banco: só depois da confirmação na tela
  baixarPix: (boletos: number[], dataPix: string, valorPix: number) =>
    api.post('/api/sicoob/pix/baixar', { boletos, dataPix, valorPix, confirmado: true }).then(r => comDados(r, d =>
      comoObjeto<{ resultados: ResultadoBaixaPix[] }>(d, ['resultados'], 'baixa dos boletos').resultados)),
  situacaoBoleto: (id: number) =>
    api.get(`/api/sicoob/boletos/${id}/situacao`).then(r => comDados(r, d => comoObjeto<SituacaoBoletoBanco>(d, [], 'situação do boleto'))),
  conferirBoletos: (inicio?: string, fim?: string) =>
    api.get('/api/sicoob/conferir-boletos', { params: { inicio, fim } }).then(r => comDados(r, d =>
      comoObjeto<ConferenciaBoletos>(d, ['pagosSemBaixa', 'vencidosSemCredito', 'creditos'], 'conferência dos boletos'))),
  extrato: (inicio?: string, fim?: string) =>
    api.get('/api/sicoob/extrato', { params: { inicio, fim } }).then(r => comDados(r, d => {
      const e = comoObjeto<ExtratoSicoob>(d, ['lancamentos'], 'extrato do Sicoob')
      e.lancamentos = e.lancamentos.map(l => {
        const x = comoObjeto<LancamentoSicoob>(l, [], 'lançamento')
        x.pix = x.pix ? comoObjeto<PixAchado>(x.pix, ['opcoes', 'jaRecebidos'], 'Pix do lançamento') : null
        return x
      })
      return e
    })),
}
