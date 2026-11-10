import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { Save } from 'lucide-react'
import Botao from './ui/Botao'
import Aviso from './ui/Aviso'
import Campo from './ui/Campo'
import { Carregando, ErroCarga } from './ui/Estados'
import { boletosContasApi as api, type ConfigEmissao } from '../services/boletosContasApi'
import { erroCobranca as erroApi } from '../services/cobrancasAbastecimentoApi'
import { lerNum, num } from '../utils/formato'

// Boletos › Sicoob › Emissão de boletos (BOL-12): o que o sistema usa para montar e emitir o boleto
// a receber de cada cliente. Vale para os próximos boletos; os já emitidos ficam como saíram.
const inputCls = 'w-full min-h-11 sm:min-h-0 text-sm border border-gray-300 rounded-lg px-3 py-2'

const ROTULO: Record<string, string> = { GIRASSOL: 'Girassol (Horacio)', CD: 'CD (Cedro Frutas)' }

type Texto = Record<'diasVencimento' | 'precoCaixaProdutoD' | 'multaPct' | 'jurosMes', string>
const paraTexto = (c: ConfigEmissao): Texto => ({
  diasVencimento: String(c.diasVencimento), precoCaixaProdutoD: num(c.precoCaixaProdutoD),
  multaPct: num(c.multaPct), jurosMes: num(c.jurosMes),
})

// A mesma validação do servidor (BoletoMontador.Validar), para avisar antes de salvar
function validar(c: ConfigEmissao): string[] {
  const l: string[] = []
  const dig = (s: string) => s.replace(/\D/g, '')
  if (!Number.isInteger(c.diasVencimento) || c.diasVencimento < 1 || c.diasVencimento > 180) l.push('Dias para o vencimento: de 1 a 180.')
  if (!c.pagadorNome.trim()) l.push('Informe o nome do pagador.')
  if (dig(c.pagadorCnpj).length !== 14) l.push('O CNPJ do pagador tem 14 números.')
  if (!c.pagadorEndereco.trim() || !c.pagadorBairro.trim() || !c.pagadorCidade.trim()) l.push('Informe endereço, bairro e cidade do pagador.')
  if (!/^[A-Z]{2}$/.test(c.pagadorUf.trim().toUpperCase())) l.push('A UF do pagador tem duas letras, ex.: GO.')
  if (dig(c.pagadorCep).length !== 8) l.push('O CEP do pagador tem 8 números.')
  if (c.cliente !== 'GIRASSOL' && !(c.precoCaixaProdutoD > 0)) l.push('Preço da caixa de produtoD: maior que zero.')
  if (!(c.multaPct > 0 && c.multaPct <= 20)) l.push('Multa: maior que zero e até 20%.')
  if (!(c.jurosMes > 0 && c.jurosMes <= 10)) l.push('Juros: maior que zero e até 10% ao mês.')
  if (!c.instrucao3.trim()) l.push('Informe a instrução 3.')
  else if (c.instrucao3.trim().length > 40) l.push('A instrução 3 tem no máximo 40 letras (limite do Sicoob).')
  return l
}

export default function ConfigEmissaoBoletos() {
  const [dados, setDados] = useState<{ config: ConfigEmissao; aviso: string | null }[] | null>(null)
  const [podeSalvar, setPodeSalvar] = useState(false)
  const [erro, setErro] = useState<string | null>(null)

  const carregar = useCallback(async () => {
    setErro(null)
    try {
      const { data } = await api.configEmissao()
      setDados(data.clientes); setPodeSalvar(data.podeSalvar)
    } catch (e) { setDados(null); setErro(await erroApi(e, 'Não consegui ver a configuração da emissão.')) }
  }, [])
  useEffect(() => { carregar() }, [carregar])

  if (erro) return <ErroCarga mensagem={erro} onTentar={carregar} />
  if (!dados) return <Carregando />
  return (
    <div className="space-y-4">
      <p className="text-sm text-gray-600">
        Como o sistema monta e emite o boleto a receber de cada cliente no Sicoob. Vale para os próximos boletos;
        os já emitidos ficam como saíram.
      </p>
      {!podeSalvar && <Aviso tipo="info">Só o dono pode mudar a configuração. Aqui dá para conferir.</Aviso>}
      {dados.map(d => (
        <FormCliente key={d.config.cliente} inicial={d.config} aviso={d.aviso} podeSalvar={podeSalvar}
                     onSalvo={l => { setDados(l.clientes); setPodeSalvar(l.podeSalvar) }} />
      ))}
    </div>
  )
}

function FormCliente({ inicial, aviso, podeSalvar, onSalvo }: {
  inicial: ConfigEmissao
  aviso: string | null
  podeSalvar: boolean
  onSalvo: (l: { clientes: { config: ConfigEmissao; aviso: string | null }[]; podeSalvar: boolean }) => void
}) {
  const [c, setC] = useState(inicial)
  const [t, setT] = useState(() => paraTexto(inicial))
  const [salvando, setSalvando] = useState(false)
  const [tentou, setTentou] = useState(false)
  useEffect(() => { setC(inicial); setT(paraTexto(inicial)) }, [inicial])

  const atual: ConfigEmissao = {
    ...c, diasVencimento: Number(t.diasVencimento.trim() || NaN), precoCaixaProdutoD: lerNum(t.precoCaixaProdutoD),
    multaPct: lerNum(t.multaPct), jurosMes: lerNum(t.jurosMes), pagadorUf: c.pagadorUf.trim().toUpperCase(),
  }
  const erros = validar(atual)
  const rd = c.cliente !== 'GIRASSOL'
  const texto = (k: keyof ConfigEmissao) => ({
    value: String(c[k] ?? ''), disabled: !podeSalvar,
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setC({ ...c, [k]: e.target.value }),
  })
  const numero = (k: keyof Texto) => ({
    value: t[k], disabled: !podeSalvar, inputMode: 'decimal' as const,
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setT({ ...t, [k]: e.target.value }),
  })

  const salvar = async () => {
    setTentou(true)
    if (erros.length > 0) return
    setSalvando(true)
    try {
      const { data } = await api.gravarConfigEmissao(atual)
      toast.success(`Configuração de ${ROTULO[c.cliente] ?? c.cliente} salva.`)
      setTentou(false)
      onSalvo(data)
    } catch (e) { toast.error(await erroApi(e, 'Não consegui salvar a configuração.')) }
    finally { setSalvando(false) }
  }

  return (
    <section aria-labelledby={`cfg-${c.cliente}`} className="bg-white border border-gray-200 rounded-xl p-4 space-y-4">
      <h2 id={`cfg-${c.cliente}`} className="text-base font-semibold text-gray-900">{ROTULO[c.cliente] ?? c.cliente}</h2>
      {aviso && <Aviso tipo="atencao">{aviso}</Aviso>}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Campo rotulo="Dias para o vencimento" dica="Corridos, a partir da emissão">
          <input className={inputCls} {...numero('diasVencimento')} inputMode="numeric" />
        </Campo>
        {rd && (
          <Campo rotulo="Preço da caixa de produtoD (R$)" dica="Valor = caixas × preço">
            <input className={inputCls} {...numero('precoCaixaProdutoD')} />
          </Campo>
        )}
        <Campo rotulo="Multa (%)" dica="Percentual, a partir do dia seguinte ao vencimento">
          <input className={inputCls} {...numero('multaPct')} />
        </Campo>
        <Campo rotulo="Juros (% ao mês)" dica="Percentual, a partir do dia seguinte ao vencimento">
          <input className={inputCls} {...numero('jurosMes')} />
        </Campo>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Campo rotulo="Instrução 3" dica={`${c.instrucao3.trim().length} de 40 letras`}>
          <input className={inputCls} maxLength={60} {...texto('instrucao3')} />
        </Campo>
        <label className="flex items-center gap-3 min-h-11 text-sm text-gray-800 sm:mt-6">
          <input type="checkbox" className="w-5 h-5 accent-tinta-700" checked={c.pix} disabled={!podeSalvar}
                 onChange={e => setC({ ...c, pix: e.target.checked })} />
          Boleto com QR Code Pix
        </label>
      </div>

      <fieldset className="space-y-3">
        <legend className="text-sm font-medium text-gray-900">Pagador</legend>
        <div className="grid gap-3 sm:grid-cols-2">
          <Campo rotulo="Nome"><input className={inputCls} {...texto('pagadorNome')} /></Campo>
          <Campo rotulo="CNPJ"><input className={inputCls} inputMode="numeric" {...texto('pagadorCnpj')} /></Campo>
          <Campo rotulo="Endereço"><input className={inputCls} {...texto('pagadorEndereco')} /></Campo>
          <Campo rotulo="Bairro"><input className={inputCls} {...texto('pagadorBairro')} /></Campo>
        </div>
        <div className="grid gap-3 grid-cols-2 sm:grid-cols-4">
          <Campo rotulo="Cidade" className="col-span-2"><input className={inputCls} {...texto('pagadorCidade')} /></Campo>
          <Campo rotulo="UF"><input className={inputCls} maxLength={2} {...texto('pagadorUf')} /></Campo>
          <Campo rotulo="CEP"><input className={inputCls} inputMode="numeric" {...texto('pagadorCep')} /></Campo>
        </div>
      </fieldset>

      {tentou && erros.length > 0 && <Aviso tipo="erro" titulo="Confira antes de salvar" itens={erros} />}
      {podeSalvar && (
        <div className="flex justify-end">
          <Botao variante="primario" icone={Save} carregando={salvando} onClick={salvar}>Salvar {ROTULO[c.cliente] ?? c.cliente}</Botao>
        </div>
      )}
    </section>
  )
}
