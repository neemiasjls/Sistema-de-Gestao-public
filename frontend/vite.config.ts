import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { createHash } from 'node:crypto'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

// Service worker do app instalado (F-03, UI-20). Só no build: pega o modelo
// pwa/sw.js, põe nele a lista do "casco" (o que este build gerou + os arquivos
// de public/ que o site usa) e uma versão tirada desse conteúdo, e publica como
// /sw.js. Build igual = versão igual = o celular não baixa nada de novo.
function cascoPwa(): Plugin {
  const PUBLICO = 'public'
  // Fora do casco: regras do Pages e a página de compartilhar (vai pelo nome bonito)
  const FORA = new Set(['_redirects', '_headers', 'compartilhar.html'])
  return {
    name: 'casco-pwa',
    apply: 'build',
    generateBundle(_, bundle) {
      const doBuild = Object.keys(bundle).filter(f => !f.endsWith('.map') && f !== 'index.html')
      const doPublico = readdirSync(PUBLICO).filter(f => !FORA.has(f) && statSync(join(PUBLICO, f)).isFile())
      const casco = ['/', '/compartilhar', ...[...doBuild, ...doPublico].sort().map(f => '/' + f)]

      const modelo = readFileSync('pwa/sw.js', 'utf8')
      const hash = createHash('sha256').update(modelo)
      for (const f of doBuild.sort()) {
        const item = bundle[f]
        hash.update(f).update(item.type === 'chunk' ? item.code : item.source)
      }
      const index = bundle['index.html']
      if (index && index.type === 'asset') hash.update(index.source)
      for (const f of doPublico.sort()) hash.update(f).update(readFileSync(join(PUBLICO, f)))
      hash.update(readFileSync(join(PUBLICO, 'compartilhar.html')))
      const versao = hash.digest('hex').slice(0, 12)

      this.emitFile({
        type: 'asset',
        fileName: 'sw.js',
        source: modelo.replace("'__VERSAO__'", JSON.stringify(versao)).replace('const CASCO = __CASCO__', 'const CASCO = ' + JSON.stringify(casco, null, 2)),
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), cascoPwa()],
  server: {
    port: 5174,
    strictPort: true,
    host: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      // O site chama /health ao abrir, para acordar a API (D-02)
      '/health': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
    },
  },
})
