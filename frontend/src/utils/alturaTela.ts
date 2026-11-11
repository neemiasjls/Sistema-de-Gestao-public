// Celular (iPhone): o 100dvh às vezes fica preso na altura de quando o teclado estava aberto, e a página
// sobe por baixo da barra do relógio: o menu de baixo aparecia no meio da tela e o resto ficava preto.
// Aqui a altura vem do window.innerHeight (atualiza ao girar, ao a barra de endereço mudar e ao fechar o
// teclado) e, com o teclado fechado, a janela volta para o topo (o conteúdo rola por dentro, não a janela).
function campoComFoco() {
  const el = document.activeElement as HTMLElement | null
  return !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable)
}

function ajustar() {
  document.documentElement.style.setProperty('--altura-tela', `${window.innerHeight}px`)
  if (!campoComFoco() && (window.scrollY !== 0 || window.scrollX !== 0)) window.scrollTo(0, 0)
}

export function acompanharAlturaTela() {
  ajustar()
  window.addEventListener('resize', ajustar)
  window.addEventListener('orientationchange', () => setTimeout(ajustar, 300))
  window.visualViewport?.addEventListener('resize', ajustar)
  // Ao sair do campo o teclado fecha um pouco depois
  document.addEventListener('focusout', () => setTimeout(ajustar, 350))
  document.addEventListener('visibilitychange', () => { if (!document.hidden) ajustar() })
}
