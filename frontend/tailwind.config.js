/** @type {import('tailwindcss').Config} */
import colors from 'tailwindcss/colors'
import plugin from 'tailwindcss/plugin'

// Paleta: "tinta" (asfalto azulado) para a estrutura e as ações, "gema" como o
// único ponto quente — Aldeia é a capital do volume. As duas ficam longe de
// propósito do azul, do verde e do laranja dos donos de caminhão: essas três
// cores dizem de quem é o caminhão e não podem se confundir com um botão.

const gray = {
  50: '#f4f6f8', 100: '#eaeef2', 200: '#dce2e8', 300: '#c3ccd6', 400: '#95a1ae',
  500: '#687583', 600: '#4c5866', 700: '#37424e', 800: '#242d37', 900: '#161d25', 950: '#0e141a',
}
const tinta = {
  50: '#eef3f7', 100: '#dde6ee', 200: '#bccbd9', 300: '#93a9bf', 400: '#6682a0',
  500: '#476582', 600: '#2c4a66', 700: '#213a52', 800: '#182b3d', 900: '#111f2c', 950: '#0b1520',
}
const gema = {
  50: '#fff8e5', 100: '#ffefbf', 200: '#fde08a', 300: '#fbcf55', 400: '#f6bd2c',
  500: '#eaa511', 600: '#c9850a', 700: '#9d640c', 800: '#7a4d0f', 900: '#5c3a0e', 950: '#3a2306',
}

const paletas = {
  gray, tinta, gema,
  red: colors.red, green: colors.green, emerald: colors.emerald, amber: colors.amber,
  orange: colors.orange, yellow: colors.yellow, purple: colors.purple,
  indigo: colors.indigo, teal: colors.teal, blue: colors.blue,
}

// ── Temas escuros ───────────────────────────────────────────────────
// As telas usam as classes de sempre (bg-white, text-gray-700, bg-red-50...).
// Cada uma aponta para uma variável, e o tema escuro troca o valor dela:
// fundo claro vira escuro, texto escuro vira claro. Assim as telas não
// precisam de um "dark:" em cada classe.
//
// Quatro escuros (a escolha fica no Perfil): o "Azul noite" é o escuro de
// sempre, tingido de tinta; os três pretos são neutros, do mais fundo ao mais
// leve. Cada um define só o chão (fundo, cartão, menu e a escala de cinza); as
// cores (vermelho, âmbar, verde...) são as mesmas, em véu sobre o cartão.

const ESCUROS = {
  // Escuro de sempre (classe .dark sozinha)
  azul: {
    cartao: '#161e27', fundo: '#0f151c', menu: '#111f2c',
    bg:     { 100: '#1c252f', 200: '#27323d', 300: '#36424f' },
    text:   { 300: '#4b5764', 400: '#8794a2', 500: '#95a1ae', 600: '#b3bdc8',
              700: '#cdd4dc', 800: '#e1e6eb', 900: '#f1f4f6', 950: '#f8fafb' },
    border: { 50: '#1a222b', 100: '#1f2832', 200: '#2a3541', 300: '#3a4652' },
  },
  // Preto total: fundo #000 (tela OLED apaga o pixel); cartão só um fio acima,
  // separado pela borda
  preto: {
    cartao: '#0e0f10', fundo: '#000000', menu: '#000000',
    bg:     { 100: '#17181a', 200: '#222326', 300: '#2f3134' },
    text:   { 300: '#4a4d52', 400: '#84888e', 500: '#9da1a7', 600: '#b9bcc1',
              700: '#d0d2d6', 800: '#e4e5e7', 900: '#f3f3f4', 950: '#fafafa' },
    border: { 50: '#151618', 100: '#1b1c1f', 200: '#2a2c30', 300: '#3b3e43' },
  },
  // Preto médio: o cinza-carvão dos aplicativos escuros
  medio: {
    cartao: '#1e1f21', fundo: '#141516', menu: '#0c0d0e',
    bg:     { 100: '#26282a', 200: '#313336', 300: '#3f4145' },
    text:   { 300: '#55585d', 400: '#94989e', 500: '#a3a7ac', 600: '#bec1c5',
              700: '#d4d6d9', 800: '#e6e7e9', 900: '#f4f4f5', 950: '#fafafa' },
    border: { 50: '#222426', 100: '#282a2d', 200: '#35383b', 300: '#46494d' },
  },
  // Preto claro: grafite, mais leve, para quem acha o preto pesado
  grafite: {
    cartao: '#2c2f33', fundo: '#222428', menu: '#1a1c1f',
    bg:     { 100: '#35383d', 200: '#40444a', 300: '#4f535a' },
    text:   { 300: '#62666d', 400: '#a6aab0', 500: '#adb1b7', 600: '#c5c8cc',
              700: '#d9dbde', 800: '#e9eaec', 900: '#f5f5f6', 950: '#fbfbfb' },
    border: { 50: '#313439', 100: '#373a3f', 200: '#45494f', 300: '#575b62' },
  },
}

// Texto em cinza no claro (D-05): o 400 da paleta dava 2,6:1 no branco e estava
// em 166 textos (dicas, vazios, unidades). Como as classes de texto têm variável
// própria (separada de fundo e borda), o texto cinza 400 e 500 escurece sem mexer
// em fundo nem borda: 400 = 5,4:1 no cartão e 5,0 no fundo da página; 500 = 6,6.
// Nos escuros o 400 sobe um tom onde ficava abaixo de 4,5 sobre o cinza 100.
// Regra (UI-09): todo texto que informa passa de 4,5:1 em todos os temas.
const TEXTO_CLARO = { gray: { 400: '#5f6b78', 500: '#525e6b' } }

const rgb = h => [1, 3, 5].map(i => parseInt(h.slice(i, i + 2), 16))
const hex = c => '#' + c.map(v => Math.round(v).toString(16).padStart(2, '0')).join('')
const mistura = (a, b, t) => hex(rgb(a).map((v, i) => v * t + rgb(b)[i] * (1 - t)))
const canais = h => rgb(h).join(' ')

function valorEscuro(tema, tipo, nome, p, s) {
  // Cinza é o chão do sistema: no escuro ganha valores escolhidos um a um.
  if (nome === 'gray') {
    if (tipo === 'bg' && s === '50') return tema.fundo
    return tema[tipo][s] ?? p[s]
  }
  const n = Number(s)
  if (tipo === 'bg') {
    // Fundos pastel viram um véu da cor sobre o cartão
    const veu = { 50: .12, 100: .18, 200: .28, 300: .4 }[n]
    if (veu) return mistura(p[500], tema.cartao, veu)
    // Tinta escura some no fundo escuro: os botões sobem um tom
    if (nome === 'tinta' && n >= 600 && n <= 800) return p[n - 200]
    return p[s]
  }
  if (tipo === 'text') {
    // Link e ícone em tinta: o 400 ficava abaixo de 4,5:1 no cartão escuro
    // (3,4 no grafite); o 300 passa em todos
    if (nome === 'tinta' && (n === 500 || n === 600)) return p[300]
    const claro = { 500: 400, 600: 400, 700: 300, 800: 200, 900: 100, 950: 50 }[n]
    return claro ? p[claro] : p[s]
  }
  // border
  const veu = { 50: .15, 100: .22, 200: .32, 300: .45 }[n]
  return veu ? mistura(p[500], tema.cartao, veu) : p[s]
}

const varsClaro = { '--bg-white': canais('#ffffff'), '--bg-menu': canais(tinta[900]) }
const varsEscuro = Object.fromEntries(Object.keys(ESCUROS).map(t => [t, {
  '--bg-white': canais(ESCUROS[t].cartao), '--bg-menu': canais(ESCUROS[t].menu),
}]))
const utilitarios = { bg: {}, text: {}, border: {} }

for (const [nome, p] of Object.entries(paletas)) {
  for (const tipo of ['bg', 'text', 'border']) {
    utilitarios[tipo][nome] = {}
    for (const s of Object.keys(p)) {
      if (!/^\d+$/.test(s)) continue
      const v = `--${tipo}-${nome}-${s}`
      varsClaro[v] = canais((tipo === 'text' && TEXTO_CLARO[nome]?.[s]) || p[s])
      for (const t of Object.keys(ESCUROS)) varsEscuro[t][v] = canais(valorEscuro(ESCUROS[t], tipo, nome, p, s))
      utilitarios[tipo][nome][s] = `rgb(var(${v}) / <alpha-value>)`
    }
  }
}

// O azul vai inteiro em .dark; os pretos levam só o que muda (.dark.tema-preto...)
const temasCss = { ':root': varsClaro, '.dark': varsEscuro.azul }
for (const t of Object.keys(ESCUROS)) {
  if (t === 'azul') continue
  temasCss[`.dark.tema-${t}`] = Object.fromEntries(
    Object.entries(varsEscuro[t]).filter(([k, v]) => varsEscuro.azul[k] !== v))
}

export default {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Barlow', 'system-ui', 'sans-serif'],
        display: ['"Barlow Semi Condensed"', 'Barlow', 'system-ui', 'sans-serif'],
      },
      colors: {
        gray, tinta, gema,
        bento: { DEFAULT: '#1d4ed8', light: '#dbeafe', text: '#1e40af' },
        caio:   { DEFAULT: '#15803d', light: '#dcfce7', text: '#166534' },
        dudu:  { DEFAULT: '#c2410c', light: '#ffedd5', text: '#9a3412' },
      },
      backgroundColor: {
        ...utilitarios.bg,
        white: 'rgb(var(--bg-white) / <alpha-value>)',
        // Menu lateral: tinta no claro e no azul; acompanha o preto nos pretos
        menu: 'rgb(var(--bg-menu) / <alpha-value>)',
      },
      textColor: {
        ...utilitarios.text,
        // Texto sobre o amarelo cheio (gema 400): o gema não muda no escuro, então
        // a letra fica escura em todos os temas (tinta-950 viraria clara: 1,5:1)
        'sobre-gema': tinta[950],
      },
      borderColor: utilitarios.border,
      keyframes: {
        entrada: { from: { opacity: '0', transform: 'translateY(6px)' }, to: { opacity: '1', transform: 'none' } },
        fundo:   { from: { opacity: '0' }, to: { opacity: '1' } },
        janela:  { from: { opacity: '0', transform: 'translateY(16px) scale(.985)' }, to: { opacity: '1', transform: 'none' } },
      },
      animation: {
        // "backwards", não "both": animação de transform que fica presa no fim
        // vira referência para os filhos com position:fixed, e aí toda janela
        // (modal) abria no meio da página, fora da tela, em vez do meio da tela.
        entrada: 'entrada 220ms cubic-bezier(.2,.7,.2,1) backwards',
        fundo:   'fundo 180ms ease-out both',
        janela:  'janela 240ms cubic-bezier(.2,.8,.2,1) both',
      },
    },
  },
  plugins: [
    plugin(({ addBase }) => addBase(temasCss)),
  ],
}
