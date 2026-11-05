// Libera a porta 5174 antes do "npm run dev" (mata instâncias antigas do Vite).
// Roda automaticamente via script "predev" do package.json.
const { execSync } = require('child_process')

const PORT = 5174
try {
  const out = execSync(`netstat -ano -p tcp | findstr :${PORT}`, { encoding: 'utf8' })
  const pids = new Set()
  for (const line of out.split('\n')) {
    const m = line.match(/LISTENING\s+(\d+)/)
    if (m) pids.add(m[1])
  }
  for (const pid of pids) {
    try {
      execSync(`taskkill /PID ${pid} /F`, { stdio: 'ignore' })
      console.log(`[free-port] processo ${pid} que segurava a porta ${PORT} foi encerrado`)
    } catch { /* já morreu */ }
  }
} catch {
  // nada ouvindo na porta — segue o jogo
}
