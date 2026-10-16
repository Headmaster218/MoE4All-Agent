(() => {
  const state = window.__moe4allDesktopChrome ??= { pending: false }
  state.inset = __MOE4ALL_CAPTION_WIDTH__
  const install = () => {
    const root = document.documentElement
    if (!root || !document.head || !document.body) return
    root.dataset.moe4allDesktop = 'integrated'
    root.style.setProperty('--moe4all-caption-width', `${state.inset}px`)
    if (!document.getElementById('moe4all-native-chrome')) {
      const style = document.createElement('style')
      style.id = 'moe4all-native-chrome'
      style.textContent = `
        [data-moe4all-drag] { app-region: drag; -webkit-app-region: drag; }
        button, a, input, select, textarea, [role=button], [role=tab], [role=checkbox],
        [role=switch], [role=slider], [role=combobox], [contenteditable],
        [data-moe4all-no-drag], [role=dialog], dialog, [popover] {
          app-region: no-drag !important; -webkit-app-region: no-drag !important;
        }
        [data-details-collapsed] [data-moe4all-caption=conversation],
        [data-moe4all-caption=details] {
          padding-right: calc(var(--moe4all-caption-width) + 12px);
          min-height: 38px; box-sizing: border-box;
        }
        #moe4all-native-drag-region {
          position: fixed; z-index: 2147483644; top: 0; left: 0;
          right: var(--moe4all-caption-width); height: 36px;
          background: transparent; pointer-events: none; user-select: none;
          app-region: drag; -webkit-app-region: drag;
        }
      `
      document.head.append(style)
    }
    if (!document.getElementById('moe4all-native-drag-region')) {
      const region = document.createElement('div')
      region.id = 'moe4all-native-drag-region'
      region.setAttribute('aria-hidden', 'true')
      document.body.append(region)
    }
  }
  // WebView2's document-created callback runs before even <html> exists.
  if (document.readyState === 'loading') {
    if (!state.pending) {
      state.pending = true
      document.addEventListener('DOMContentLoaded', () => {
        state.pending = false
        install()
      }, { once: true })
    }
  } else install()
})()
