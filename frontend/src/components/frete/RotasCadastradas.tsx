import type { Dispatch, SetStateAction } from 'react'
import { ArrowRight, Check, MapPin, Pencil, Plus, SearchX, Trash2, X } from 'lucide-react'
import Botao, { BotaoIcone } from '../ui/Botao'
import Campo from '../ui/Campo'
import { Vazio } from '../ui/Estados'
import CityAutocomplete from '../CityAutocomplete'
import BarraLista, { separadorPerigo } from './BarraLista'

// Aba "Rotas cadastradas" da tela Viagens. Cadastrar fica em cima (o aviso de
// rota que falta no cálculo traz para cá, no topo); embaixo a lista com busca
// por cidade. Computador: tabela; celular: um cartão por rota.

export interface Rota {
  id?: number
  origem: string
  ufOrigem: string
  destino: string
  uf: string
  km: number
}

const semAcento = (s: string) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
const campoLinha = 'w-full min-h-11 sm:min-h-10 border border-gray-300 rounded-lg px-2.5 py-1.5 text-sm'
const campoTabela = 'border border-tinta-300 rounded-lg px-2 py-1 text-sm'

export default function RotasCadastradas(p: {
  rotas: Rota[]
  busca: string
  onBusca: (v: string) => void
  novaRota: Rota
  setNovaRota: Dispatch<SetStateAction<Rota>>
  salvandoRota: boolean
  onCadastrar: () => void
  editandoId: number | null
  editRota: Rota
  setEditRota: Dispatch<SetStateAction<Rota>>
  onEditar: (r: Rota) => void
  onSalvarEdicao: () => void
  onCancelarEdicao: () => void
  onRemover: (r: Rota) => void
}) {
  const { rotas, busca, novaRota, setNovaRota, editRota, setEditRota, editandoId } = p
  const termo = semAcento(busca.trim())
  // A rota em edição continua na lista mesmo se a busca deixar de casar com ela
  const lista = termo
    ? rotas.filter(r => r.id === editandoId || semAcento(`${r.origem} ${r.ufOrigem} ${r.destino} ${r.uf}`).includes(termo))
    : rotas

  const acoes = (r: Rota) => <>
    <BotaoIcone icone={Pencil} rotulo={`Editar a rota de ${r.origem} até ${r.destino}`} onClick={() => p.onEditar(r)} />
    <span className={separadorPerigo}>
      <BotaoIcone icone={Trash2} perigo rotulo={`Remover a rota de ${r.origem} até ${r.destino}`} onClick={() => p.onRemover(r)} />
    </span>
  </>

  return (
    <div className="space-y-5 max-w-6xl">
      {/* Cadastrar nova rota */}
      <section aria-labelledby="titulo-nova-rota" className="rounded-xl border border-gray-200 bg-white p-4 sm:p-5">
        <h3 id="titulo-nova-rota" className="text-sm font-semibold text-gray-900 mb-3">Cadastrar nova rota</h3>
        <div className="grid grid-cols-[1fr_5.5rem] sm:grid-cols-6 gap-2 sm:gap-3 items-end">
          <Campo className="sm:col-span-2" rotulo="Cidade de origem">
            <CityAutocomplete value={novaRota.origem} placeholder="Ex.: ALDEIA"
              onSelect={(nome, uf) => setNovaRota(v => ({ ...v, origem: nome, ufOrigem: uf }))} className={campoLinha} />
          </Campo>
          <Campo rotulo="UF de origem">
            <input value={novaRota.ufOrigem} onChange={e => setNovaRota(v => ({ ...v, ufOrigem: e.target.value.toUpperCase() }))}
                   placeholder="SP" maxLength={2} className={`${campoLinha} uppercase`} />
          </Campo>
          <Campo className="sm:col-span-2" rotulo="Cidade de destino">
            <CityAutocomplete value={novaRota.destino} placeholder="Ex.: GOIÂNIA"
              onSelect={(nome, uf) => setNovaRota(v => ({ ...v, destino: nome, uf }))} className={campoLinha} />
          </Campo>
          <Campo rotulo="UF de destino">
            <input value={novaRota.uf} onChange={e => setNovaRota(v => ({ ...v, uf: e.target.value.toUpperCase() }))}
                   placeholder="GO" maxLength={2} className={`${campoLinha} uppercase`} />
          </Campo>
          <Campo rotulo="Distância (km)" className="col-span-2 sm:col-span-1">
            <input inputMode="numeric" value={novaRota.km || ''} onChange={e => setNovaRota(v => ({ ...v, km: parseInt(e.target.value) || 0 }))}
                   placeholder="722" className={`${campoLinha} tabular-nums`} />
          </Campo>
          <div className="col-span-2 sm:col-span-1">
            <Botao variante="primario" icone={Plus} bloco onClick={p.onCadastrar} carregando={p.salvandoRota} textoCarregando="Salvando...">
              Cadastrar
            </Botao>
          </div>
        </div>
      </section>

      {rotas.length === 0 ? (
        <div className="rounded-xl border border-gray-200 bg-white">
          <Vazio compacto icone={MapPin} titulo="Nenhuma rota cadastrada">Cadastre a primeira logo acima.</Vazio>
        </div>
      ) : (
        <section aria-label="Rotas cadastradas" className="space-y-3">
          <BarraLista busca={busca} onBusca={p.onBusca} rotuloBusca="Buscar rota pela cidade" exemploBusca="Cidade de origem ou destino"
            contagem={termo ? `${lista.length} de ${rotas.length} rotas` : `${rotas.length} rotas`} />

          {lista.length === 0 ? (
            <div className="rounded-xl border border-gray-200 bg-white">
              <Vazio compacto icone={SearchX} titulo="Nenhuma rota com essa cidade"
                     acao={<Botao variante="secundario" tamanho="sm" onClick={() => p.onBusca('')}>Limpar busca</Botao>}>
                Busca: "{busca.trim()}"
              </Vazio>
            </div>
          ) : <>
            {/* Celular: um cartão por rota; editar abre os campos no próprio cartão */}
            <ul className="sm:hidden rounded-xl border border-gray-200 bg-white divide-y divide-gray-100">
              {lista.map(r => editandoId === r.id ? (
                <li key={r.id} className="p-4 space-y-3 bg-tinta-50 first:rounded-t-xl last:rounded-b-xl">
                  <div className="grid grid-cols-[1fr_5.5rem] gap-2">
                    <Campo rotulo="Cidade de origem">
                      <CityAutocomplete value={editRota.origem} onSelect={(nome, uf) => setEditRota(v => ({ ...v, origem: nome, ufOrigem: uf }))} className={campoLinha} />
                    </Campo>
                    <Campo rotulo="UF">
                      <input value={editRota.ufOrigem} onChange={e => setEditRota(v => ({ ...v, ufOrigem: e.target.value.toUpperCase() }))}
                             maxLength={2} className={`${campoLinha} uppercase text-center`} />
                    </Campo>
                    <Campo rotulo="Cidade de destino">
                      <CityAutocomplete value={editRota.destino} onSelect={(nome, uf) => setEditRota(v => ({ ...v, destino: nome, uf }))} className={campoLinha} />
                    </Campo>
                    <Campo rotulo="UF">
                      <input value={editRota.uf} onChange={e => setEditRota(v => ({ ...v, uf: e.target.value.toUpperCase() }))}
                             maxLength={2} className={`${campoLinha} uppercase text-center`} />
                    </Campo>
                    <Campo rotulo="Distância (km)" className="col-span-2">
                      <input inputMode="numeric" value={editRota.km || ''} onChange={e => setEditRota(v => ({ ...v, km: parseInt(e.target.value) || 0 }))}
                             className={`${campoLinha} tabular-nums`} />
                    </Campo>
                  </div>
                  <div className="flex gap-2">
                    <Botao variante="primario" icone={Check} onClick={p.onSalvarEdicao} className="flex-1">Salvar a rota</Botao>
                    <Botao variante="secundario" onClick={p.onCancelarEdicao}>Cancelar</Botao>
                  </div>
                </li>
              ) : (
                <li key={r.id} className="flex items-center gap-2 pl-4 pr-1 py-2.5">
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-gray-900 flex flex-wrap items-center gap-x-1.5">
                      <span>{r.origem}<span className="text-gray-500">/{r.ufOrigem}</span></span>
                      <ArrowRight size={14} className="text-gray-500" aria-label="até" />
                      <span>{r.destino}<span className="text-gray-500">/{r.uf}</span></span>
                    </p>
                    <p className="text-sm text-gray-600 tabular-nums">{r.km.toLocaleString('pt-BR')} km</p>
                  </div>
                  <div className="flex items-center">{acoes(r)}</div>
                </li>
              ))}
            </ul>

            {/* Computador: tabela */}
            <div className="hidden sm:block rounded-xl border border-gray-200 bg-white">
              <table className="w-full text-sm">
                <thead className="sticky -top-4 sm:-top-5 lg:-top-6 z-10 text-xs [&_th]:bg-gray-50 [&_th]:shadow-[inset_0_-1px_0] [&_th]:shadow-gray-200 text-gray-500 [&_th:first-child]:rounded-tl-xl [&_th:last-child]:rounded-tr-xl">
                  <tr className="border-b border-gray-200">
                    <th className="px-4 py-2.5 text-left font-medium">Origem</th>
                    <th className="px-4 py-2.5 text-left font-medium">Destino</th>
                    <th className="px-4 py-2.5 text-right font-medium">Distância (km)</th>
                    <th className="px-4 py-2.5"><span className="sr-only">Ações</span></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {lista.map(r => editandoId === r.id ? (
                    <tr key={r.id} className="bg-tinta-50">
                      <td className="px-3 py-1.5">
                        <span className="flex gap-1.5">
                          <CityAutocomplete value={editRota.origem} onSelect={(nome, uf) => setEditRota(v => ({ ...v, origem: nome, ufOrigem: uf }))}
                                            className={`w-full min-w-32 ${campoTabela}`} />
                          <input value={editRota.ufOrigem} onChange={e => setEditRota(v => ({ ...v, ufOrigem: e.target.value.toUpperCase() }))}
                                 aria-label="UF de origem" maxLength={2} className={`w-14 shrink-0 ${campoTabela} uppercase text-center`} />
                        </span>
                      </td>
                      <td className="px-3 py-1.5">
                        <span className="flex gap-1.5">
                          <CityAutocomplete value={editRota.destino} onSelect={(nome, uf) => setEditRota(v => ({ ...v, destino: nome, uf }))}
                                            className={`w-full min-w-32 ${campoTabela}`} />
                          <input value={editRota.uf} onChange={e => setEditRota(v => ({ ...v, uf: e.target.value.toUpperCase() }))}
                                 aria-label="UF de destino" maxLength={2} className={`w-14 shrink-0 ${campoTabela} uppercase text-center`} />
                        </span>
                      </td>
                      <td className="px-3 py-1.5 text-right">
                        <input inputMode="numeric" value={editRota.km || ''} onChange={e => setEditRota(v => ({ ...v, km: parseInt(e.target.value) || 0 }))}
                               aria-label="Distância em km" className={`w-20 ${campoTabela} text-right tabular-nums`} />
                      </td>
                      <td className="px-4 py-1">
                        <div className="flex justify-end gap-0.5">
                          <BotaoIcone icone={Check} rotulo="Salvar a rota" onClick={p.onSalvarEdicao} className="text-tinta-700" />
                          <BotaoIcone icone={X} rotulo="Cancelar a edição" onClick={p.onCancelarEdicao} />
                        </div>
                      </td>
                    </tr>
                  ) : (
                    <tr key={r.id} className="hover:bg-gray-50 transition-colors">
                      <td className="px-4 py-2 font-medium text-gray-900">{r.origem}<span className="font-normal text-gray-500"> / {r.ufOrigem}</span></td>
                      <td className="px-4 py-2 text-gray-800">{r.destino}<span className="text-gray-500"> / {r.uf}</span></td>
                      <td className="px-4 py-2 text-right tabular-nums text-gray-900">{r.km.toLocaleString('pt-BR')}</td>
                      <td className="px-4 py-1">
                        <div className="flex items-center justify-end">{acoes(r)}</div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>}
        </section>
      )}
    </div>
  )
}
