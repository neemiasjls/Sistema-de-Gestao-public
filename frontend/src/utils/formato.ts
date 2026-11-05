// Formatação e leitura de números no padrão brasileiro.

export const brl = (v: number) =>
  v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })

export const num = (v: number, casas = 2) =>
  v.toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas })

export const dataBr = (iso: string) => {
  if (!iso) return ''
  const [a, m, d] = iso.split('-')
  return `${d}/${m}/${a}`
}

export const hoje = () => new Date().toISOString().slice(0, 10)

/**
 * Lê um número digitado à mão. Se tem vírgula, o ponto é separador de milhar
 * (1.234,56); se não tem, o ponto é a própria vírgula decimal (1234.56) — tem
 * gente que digita dos dois jeitos.
 */
export const lerNum = (v: string): number => {
  const t = (v ?? '').trim()
  if (!t) return 0
  const n = t.includes(',')
    ? Number(t.replace(/\./g, '').replace(',', '.'))
    : Number(t)
  return isNaN(n) ? 0 : n
}
