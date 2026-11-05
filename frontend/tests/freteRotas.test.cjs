const assert = require('node:assert/strict')
const { test } = require('node:test')
const fs = require('node:fs')
const path = require('node:path')
const vm = require('node:vm')
const root = path.join(__dirname, '..')
const ts = require(path.join(root, 'node_modules/typescript'))
const page = fs.readFileSync(path.join(root, 'src/pages/FreteCalculoPage.tsx'), 'utf8')
const ast = ts.createSourceFile('page.tsx', page, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
const component = ast.statements.find(n => ts.isFunctionDeclaration(n) && n.name?.text === 'FreteCalculoPage')
const names = ['atualizarResultadoDasRotas', 'salvarRota', 'salvarEdicao', 'abrirSalvo']
const handlers = component.body.statements.filter(n => ts.isVariableStatement(n) && n.declarationList.declarations.some(d => names.includes(d.name.getText(ast)))).map(n => n.getText(ast)).join('\n')
const compile = source => ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
const copy = x => JSON.parse(JSON.stringify(x))

function harness() {
  const calls = [], errors = [], seenCargaIds = [], routes = new Map([['ALDEIA|ANAPOLIS|GO', 1000]])
  const result = {
    pesoTotal: 300, liquotaBruta: 10, liquotaArredondada: 10, totalPesoXKm: 200000, totalNFs: 3,
    rotasNaoCadastradas: ['ALDEIA → DIVINOPOLIS DO TOCANTINS/TO'],
    itens: [
      { numeroNF: '1, 2', emitente: 'FAZENDA', destinatario: 'CLIENTE A', cidadeOrigem: 'ALDEIA', cidadeDestino: 'DIVINOPOLIS DO TOCANTINS', uf: 'TO', pesoKg: 100, distanciaKm: null, pesoXKm: 0, percentualPedagio: 0, valorPedagio: 0, nfsDetalhadas: [{ numero: '1', pesoKg: 40 }, { numero: '2', pesoKg: 60 }] },
      { numeroNF: '3', emitente: 'FAZENDA', destinatario: 'CLIENTE B', cidadeOrigem: 'ALDEIA', cidadeDestino: 'ANAPOLIS', uf: 'GO', pesoKg: 200, distanciaKm: 1000, pesoXKm: 200000, percentualPedagio: 1, valorPedagio: 300, nfsDetalhadas: [{ numero: '3', pesoKg: 200 }] },
    ],
  }
  let fail = false
  const ctesApi = {
    adicionarDistancia: async r => { calls.push('salvar'); routes.set(`${r.origem}|${r.destino}|${r.uf}`, r.km) },
    atualizarDistancia: async (_, r) => { calls.push('editar'); routes.set(`${r.origem}|${r.destino}|${r.uf}`, r.km) },
    calcularFrete: async input => {
      seenCargaIds.push(context.cargaAbertaId)
      calls.push(copy(input))
      if (fail) throw Error('API indisponível')
      const itens = input.itens.map(i => {
        const km = routes.get(`${i.cidadeEmitente}|${i.cidadeDestino}|${i.uf}`)
        return { ...i, cidadeOrigem: i.cidadeEmitente, distanciaKm: km, pesoXKm: (km ?? 0) * i.pesoKg }
      })
      const totalPesoXKm = itens.reduce((sum, i) => sum + i.pesoXKm, 0)
      itens.forEach(i => { i.percentualPedagio = i.pesoXKm / totalPesoXKm; i.valorPedagio = i.percentualPedagio * input.valorPedagio })
      return { data: { ...result, itens, totalPesoXKm, rotasNaoCadastradas: itens.filter(i => i.distanciaKm == null).map(i => `${i.cidadeOrigem} → ${i.cidadeDestino}/${i.uf}`) } }
    },
  }
  const helper = { exports: {}, require: () => ({ ctesApi }) }
  vm.runInNewContext(compile(fs.readFileSync(path.join(root, 'src/services/freteRotas.ts'), 'utf8')), helper)
  const context = {
    resultado: copy(result), valorCarga: '3000', valorPedagio: '300', cargaAbertaId: 140,
    novaRota: { origem: 'ALDEIA', ufOrigem: 'SP', destino: 'DIVINOPOLIS DO TOCANTINS', uf: 'TO', km: 1588 },
    editRota: { origem: 'ALDEIA', ufOrigem: 'SP', destino: 'DIVINOPOLIS DO TOCANTINS', uf: 'TO', km: 2000 }, editandoId: 30,
    ctesApi, recalcularRotasFrete: helper.exports.recalcularRotasFrete,
    carregarRotas: async () => calls.push('listar'), cancelarEdicao() {},
    setResultado: r => { context.resultado = r }, setSalvandoRota() {}, setNovaRota() {},
    toast: { success() {}, error: msg => errors.push(msg) },
  }
  const noop = () => {}
  for (const n of ['setPlaca', 'setMotorista', 'setPlacasDivergentes', 'setMotoristasDivergentes', 'setValorCarga', 'setValorPedagio', 'setItens', 'setCargaAbertaId', 'setPlanilhasSalvas', 'setPlanilhasGeradas', 'setAnalise', 'setXmlFiles', 'setMostrarSalvos']) context[n] = noop
  context.setCargaAbertaId = id => { context.cargaAbertaId = id }
  Object.assign(context, {
    placaDasNotas: { current: '' }, motoristaDasNotas: { current: '' }, xmlRef: { current: [] }, nextId: 1,
    doNotas: () => ({ valor: '' }), compararBsoft: () => 0, window: { scrollTo() {} },
    calculosFreteApi: { obter: async () => ({ data: { placa: 'ABC1234', valorCarga: 3000, valorPedagio: 300, payloadJson: JSON.stringify({ itens: [], resultado: result, valorCarga: '3000', valorPedagio: '300' }) } }) },
  })
  vm.createContext(context)
  vm.runInContext(compile(handlers) + '\n globalThis.testHandlers = { atualizarResultadoDasRotas, salvarRota, salvarEdicao, abrirSalvo };', context)
  return { context, calls, errors, seenCargaIds, routes, result, handlers: context.testHandlers, fail: () => { fail = true } }
}

test('cadastrar rota manual atualiza a NF pendente e redistribui o pedágio de toda a carga', async () => {
  const h = harness()
  await h.handlers.salvarRota()
  assert.equal(h.context.resultado.itens[0].distanciaKm, 1588)
  assert.equal(h.context.resultado.rotasNaoCadastradas.length, 0)
  assert.equal(h.context.resultado.totalPesoXKm, 358800)
  assert.ok(h.context.resultado.itens[0].valorPedagio > 0)
  assert.ok(h.context.resultado.itens[1].valorPedagio < 300)
  assert.ok(Math.abs(h.context.resultado.itens.reduce((s, i) => s + i.valorPedagio, 0) - 300) < 0.000001)
  const request = h.calls.find(c => typeof c === 'object')
  assert.equal(request.itens[0].cidadeEmitente, 'ALDEIA')
  assert.equal(request.itens[0].numeroNF, '1, 2')
  assert.deepEqual(request.itens[0].nfsDetalhadas, h.result.itens[0].nfsDetalhadas)
  assert.equal(request.itens.length, 2)
})

test('editar a rota também atualiza a distância e o rateio', async () => {
  const h = harness()
  await h.handlers.salvarEdicao()
  assert.equal(h.context.resultado.itens[0].distanciaKm, 2000)
  assert.equal(h.context.resultado.itens[0].valorPedagio, 150)
  assert.equal(h.context.resultado.itens[1].valorPedagio, 150)
})

test('reabrir carga com aviso antigo consulta a rota cadastrada depois', async () => {
  const h = harness()
  h.routes.set('ALDEIA|DIVINOPOLIS DO TOCANTINS|TO', 1588)
  await h.handlers.abrirSalvo(141)
  assert.equal(h.context.resultado.itens[0].distanciaKm, 1588)
  assert.equal(h.context.resultado.rotasNaoCadastradas.length, 0)
  assert.equal(h.errors.length, 0)
})

test('sem cálculo aberto, o cadastro não dispara um cálculo vazio', async () => {
  const h = harness()
  h.context.resultado = null
  await h.handlers.salvarRota()
  assert.deepEqual(h.calls, ['salvar', 'listar'])
})

test('reabrir outra carga troca o ID antes de aguardar o cálculo das rotas', async () => {
  const h = harness()
  h.routes.set('ALDEIA|DIVINOPOLIS DO TOCANTINS|TO', 1588)
  await h.handlers.abrirSalvo(141)
  assert.deepEqual(h.seenCargaIds, [141])
})

test('falha ao atualizar não desfaz o cadastro nem mantém valores antigos na tela', async () => {
  const h = harness()
  h.fail()
  await h.handlers.salvarRota()
  assert.equal(h.routes.get('ALDEIA|DIVINOPOLIS DO TOCANTINS|TO'), 1588)
  assert.equal(h.context.resultado, null)
  assert.equal(h.errors.length, 1)
  assert.match(h.errors[0], /atualizar as distâncias/)
})
