// Etiqueta colorida do cliente de romaneio. Usada na tela de Planilhas NF-e e
// no painel de planilhas da carga, em Frete/Líquota.
export default function ClienteBadge({ apelido }: { apelido: string }) {
  const cores: Record<string, string> = {
    BRZ: 'bg-tinta-100 text-tinta-800 border-tinta-300',
    AURORA: 'bg-green-100 text-green-800 border-green-300',
    CEDRO: 'bg-purple-100 text-purple-800 border-purple-300',
    'CEDRO FRUTAS': 'bg-purple-100 text-purple-800 border-purple-300',
    DUNAS: 'bg-orange-100 text-orange-800 border-orange-300',
    FAROL: 'bg-red-100 text-red-800 border-red-300',
  }
  const cls = cores[apelido] ?? 'bg-gray-100 text-gray-800 border-gray-300'
  return (
    <span className={`inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold border ${cls}`}>
      {apelido}
    </span>
  )
}
