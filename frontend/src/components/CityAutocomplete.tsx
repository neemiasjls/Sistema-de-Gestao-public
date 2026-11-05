import { useEffect, useRef, useState } from 'react'

interface Municipio {
  nome: string   // uppercase
  uf: string
}

// Cache global — carrega uma vez e reusa
let _cache: Municipio[] | null = null
let _loading: Promise<Municipio[]> | null = null

async function getMunicipios(): Promise<Municipio[]> {
  if (_cache) return _cache
  if (_loading) return _loading
  _loading = fetch('https://servicodados.ibge.gov.br/api/v1/localidades/municipios?orderBy=nome')
    .then(r => r.json())
    .then((data: { nome: string; microrregiao: { mesorregiao: { UF: { sigla: string } } } }[]) => {
      _cache = data.map(m => ({
        nome: m.nome.toUpperCase(),
        uf:   m.microrregiao.mesorregiao.UF.sigla,
      }))
      return _cache
    })
  return _loading
}

// Remove acentos para comparação
function norm(s: string) {
  return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toUpperCase()
}

interface Props {
  value: string
  placeholder?: string
  onSelect: (nome: string, uf: string) => void
  onChange?: (texto: string) => void
  className?: string
  disabled?: boolean
}

export default function CityAutocomplete({ value, placeholder, onSelect, onChange, className, disabled }: Props) {
  const [query, setQuery]         = useState(value)
  const [sugestoes, setSugestoes] = useState<Municipio[]>([])
  const [aberto, setAberto]       = useState(false)
  const [municipios, setMunicipios] = useState<Municipio[]>([])
  const containerRef = useRef<HTMLDivElement>(null)

  // Pré-carrega municípios ao montar
  useEffect(() => {
    getMunicipios().then(setMunicipios).catch(() => {})
  }, [])

  // Sincroniza quando o valor vem de fora (ex: após importar XML)
  useEffect(() => { setQuery(value) }, [value])

  // Fecha dropdown ao clicar fora
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node))
        setAberto(false)
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [])

  const handleChange = (q: string) => {
    setQuery(q)
    onChange?.(q)
    if (q.length < 2) { setSugestoes([]); setAberto(false); return }
    const n = norm(q)
    const filtered = municipios.filter(m => norm(m.nome).startsWith(n)).slice(0, 8)
    const result = filtered.length > 0
      ? filtered
      : municipios.filter(m => norm(m.nome).includes(n)).slice(0, 8)
    setSugestoes(result)
    setAberto(result.length > 0)
  }

  const handleSelect = (m: Municipio) => {
    setQuery(m.nome)
    setSugestoes([])
    setAberto(false)
    onSelect(m.nome, m.uf)
  }

  // Se sair do campo sem selecionar da lista, usa o texto digitado diretamente
  const handleBlur = () => {
    setAberto(false)
    if (query && query !== value) {
      onSelect(query.toUpperCase(), '')
    }
  }

  return (
    <div ref={containerRef} className="relative">
      <input
        value={query}
        onChange={e => handleChange(e.target.value)}
        onFocus={() => { if (sugestoes.length > 0) setAberto(true) }}
        onBlur={handleBlur}
        placeholder={placeholder ?? 'Buscar cidade...'}
        disabled={disabled}
        className={className}
        autoComplete="off"
      />
      {aberto && sugestoes.length > 0 && (
        <ul className="absolute z-50 top-full left-0 right-0 bg-white border border-gray-200 rounded-lg shadow-lg max-h-52 overflow-y-auto mt-0.5 text-xs">
          {sugestoes.map(m => (
            <li
              key={`${m.nome}-${m.uf}`}
              onMouseDown={() => handleSelect(m)}
              className="px-3 py-2 cursor-pointer hover:bg-tinta-50 flex justify-between items-center gap-2"
            >
              <span className="text-gray-800">{m.nome}</span>
              <span className="text-gray-400 font-mono shrink-0">{m.uf}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
