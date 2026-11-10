import api from './api'
import { comDados, comoObjeto } from '../utils/resposta'

// Extrato da conta PJ no Sicoob, lido pela API do banco (só consulta)

export interface BoletoSugerido {
  id: number
  cliente: 'CD' | 'GIRASSOL'
  rotulo: string
  nossoNumero: string
  valor: number
  vencimento: string | null
  dataCarga: string | null
  placa: string
}

export interface LancamentoSicoob {
  id: string
  data: string | null
  credito: boolean
  valor: number
  descricao: string
  documento: string
  cpfCnpj: string
  complemento: string
  jaMarcado: { id: number; cliente: string; rotulo: string; nossoNumero: string; dataCarga: string | null } | null
  sugestoes: BoletoSugerido[]
  pix: PixAchado | null
}

// Pix de cliente que bate com boletos a receber em aberto (BOL-14)
export interface PixAchado {
  cliente: 'CD' | 'GIRASSOL' | null
  pagador: string | null      // nome da configuração da emissão; null = o banco não informou o CNPJ
  semCnpj: boolean
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

export const sicoobApi = {
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
        const x = comoObjeto<LancamentoSicoob>(l, ['sugestoes'], 'lançamento')
        x.pix = x.pix ? comoObjeto<PixAchado>(x.pix, ['opcoes', 'jaRecebidos'], 'Pix do lançamento') : null
        return x
      })
      return e
    })),
}
