// Descrição do extrato do Sicoob legível: o banco manda "Recebimento Pix|@NOME|@XX.XXX.XXX/0004-XX|@ADT 55616".
// Vira "Pix recebido de Nome" e, embaixo, "CNPJ XX.XXX.XXX/0004-XX · cliente · ADT 55616".
// Texto sem "|@" fica como está.

// Mesma regra do PixBoletos.DocumentoDoPix (API): CNPJ com espaço no lugar da barra, ou CPF
const DOC = /(?<!\d)(\d{2}\.?\d{3}\.?\d{3}[/ ]?\d{4}-?\d{2}|\d{3}\.\d{3}\.\d{3}-\d{2})(?!\d)/

const digitos = (s: string) => s.replace(/\D/g, '')

export function formatarDocumento(s: string): string {
  const d = digitos(s)
  if (d.length === 14) return `CNPJ ${d.slice(0, 2)}.${d.slice(2, 5)}.${d.slice(5, 8)}/${d.slice(8, 12)}-${d.slice(12)}`
  if (d.length === 11) return `CPF ${d.slice(0, 3)}.${d.slice(3, 6)}.${d.slice(6, 9)}-${d.slice(9)}`
  return s
}

const LIGACOES = new Set(['de', 'da', 'do', 'das', 'dos', 'e'])
const SIGLAS = new Set(['LTDA', 'ME', 'EPP', 'EIRELI', 'SA', 'S/A', 'S.A.', 'S.A', 'MEI', 'CIA', 'SS', 'SPE'])

// Nome em caixa de título, com siglas (LTDA, ME...) e letras soltas em maiúsculo
export function caixaDeTitulo(nome: string): string {
  return nome.trim().split(/\s+/).map((p, i) => {
    const alto = p.toUpperCase()
    if (SIGLAS.has(alto) || p.length === 1 || /\d/.test(p)) return alto
    const baixo = p.toLowerCase()
    if (i > 0 && LIGACOES.has(baixo)) return baixo
    return baixo.charAt(0).toUpperCase() + baixo.slice(1)
  }).join(' ')
}

const partes = (s: string) => s.split('|@').map(x => x.trim()).filter(Boolean)

export interface DescricaoLimpa {
  titulo: string
  detalhe: string   // vazio = sem segunda linha
  documento: string // só os dígitos ('' = sem documento)
}

/**
 * clienteDoDocumento: nome do cliente de boleto (CD/Perfil) dono do CNPJ, quando for de um deles.
 */
export function limparDescricao(descricao: string, complemento: string, cpfCnpj: string,
  clienteDoDocumento: (doc: string) => string | null = () => null): DescricaoLimpa {
  const texto = (descricao ?? '').trim()
  const comp = (complemento ?? '').trim()
  const campoDoc = digitos(cpfCnpj ?? '')

  if (!texto.includes('|@') && !comp.includes('|@')) {
    const cliente = campoDoc ? clienteDoDocumento(campoDoc) : null
    return {
      titulo: texto,
      detalhe: [comp, cpfCnpj ? formatarDocumento(cpfCnpj) : '', cliente ?? ''].filter(Boolean).join(' · '),
      documento: campoDoc,
    }
  }

  const ps = partes(texto)
  let docTexto = ''
  const resto: string[] = []
  for (const p of ps) {
    const m = DOC.exec(p)
    // A parte inteira é o documento
    if (!docTexto && m && p.replace(m[0], '').trim() === '') docTexto = m[0]
    else resto.push(p)
  }

  let titulo: string
  const tipo = resto[0] ?? ''
  if (/^recebimento pix$/i.test(tipo) && resto.length > 1) {
    titulo = `Pix recebido de ${caixaDeTitulo(resto[1])}`
    resto.splice(0, 2)
  } else if (/^(pagamento|envio) pix$/i.test(tipo) && resto.length > 1) {
    titulo = `Pix enviado para ${caixaDeTitulo(resto[1])}`
    resto.splice(0, 2)
  } else {
    titulo = resto.join(' · ')
    resto.length = 0
  }

  const doc = docTexto ? digitos(docTexto) : campoDoc
  const cliente = doc ? clienteDoDocumento(doc) : null
  const compLimpo = partes(comp).join(' · ')
  const detalhe = [
    doc ? formatarDocumento(docTexto || cpfCnpj) : '',
    cliente ?? '',
    ...resto,
    compLimpo && compLimpo !== titulo ? compLimpo : '',
  ].filter(Boolean).join(' · ')
  return { titulo: titulo || texto, detalhe, documento: doc }
}
