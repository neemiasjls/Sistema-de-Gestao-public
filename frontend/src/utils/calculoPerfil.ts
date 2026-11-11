// Cálculo do frete Perfil (BOL-17): o .txt "dd-MM-aaaa PLACA.txt" fica guardado na carga.
// Data (aaaa-mm-dd) e placa saem do nome do arquivo; sem os dois, devolve null.
export function calculoPerfilDoNome(nome: string): { data: string; placa: string } | null {
  const m = /^(\d{2})-(\d{2})-(\d{4}) (.+)\.txt$/i.exec(nome.trim())
  return m ? { data: `${m[3]}-${m[2]}-${m[1]}`, placa: m[4].trim().toUpperCase() } : null
}

export const linkBoletosPerfil = (c: { data: string; placa: string }) =>
  `/boletos?aba=receber&cliente=GIRASSOL&carga=${c.data}&placa=${encodeURIComponent(c.placa)}`
