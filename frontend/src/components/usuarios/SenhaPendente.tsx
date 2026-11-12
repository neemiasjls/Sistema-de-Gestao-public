import toast from 'react-hot-toast'
import { Copy } from 'lucide-react'
import Botao from '../ui/Botao'

export type Pendente = { nome: string; email: string; senha: string; aviso: string }

/** Senha provisória que não saiu por e-mail: aparece uma vez, para o administrador repassar. */
export default function SenhaPendente({ p, onFechar }: { p: Pendente; onFechar: () => void }) {
  const copiar = async (texto: string) => {
    try { await navigator.clipboard.writeText(texto); toast.success('Senha copiada.') }
    catch { toast.error('Não consegui copiar. Selecione e copie à mão.') }
  }
  return (
    <div role="alert" className="bg-gema-50 border border-gema-300 rounded-xl p-4 space-y-3">
      <p className="text-sm text-gray-900">
        <b>{p.aviso}</b> Mande esta senha para {p.nome} por outro caminho, como o WhatsApp. Ela aparece só agora.
      </p>
      <code className="block w-fit max-w-full break-all font-mono text-lg bg-white border border-gema-200 rounded-lg px-3 py-1.5 select-all">
        {p.senha}
      </code>
      <div className="flex flex-wrap gap-2">
        <Botao variante="primario" icone={Copy} onClick={() => copiar(p.senha)}>Copiar</Botao>
        <Botao variante="secundario" onClick={() => copiar(`Seu acesso ao sistema da TRX Transportes:\nE-mail: ${p.email}\nSenha provisória: ${p.senha}\n\nEntre em ${location.origin} e crie a sua senha.`)}>
          Copiar mensagem pronta
        </Botao>
        <Botao variante="fantasma" onClick={onFechar}>Já mandei</Botao>
      </div>
    </div>
  )
}

/** Devolve a senha para mostrar quando o e-mail não saiu; avisa quando saiu. */
export function entregaDaSenha(r: { emailEnviado: boolean; senhaProvisoria?: string | null; avisoEmail?: string | null } | null | undefined,
  nome: string, email: string): Pendente | null {
  if (!r) return null
  if (r.emailEnviado) { toast.success(`Senha provisória enviada para ${email}.`); return null }
  if (r.senhaProvisoria) return { nome, email, senha: r.senhaProvisoria, aviso: r.avisoEmail ?? 'O e-mail não saiu.' }
  return null
}
