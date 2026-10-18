import React, { useSyncExternalStore } from 'react'
import { createRoot } from 'react-dom/client'
import { CapabilitiesSection, CapabilitiesNotification } from '/@dsh/packages/client/ui-settings-general/src/client/Capabilities.tsx'
import { DesktopController } from '/@dsh/packages/client/ui-settings-general/src/client/desktop-controller.ts'
import { SettingsRoot } from '/@dsh/packages/client/ui-settings-general/src/client/SettingsRoot.tsx'
import { en, zh } from '/@dsh/packages/client/ui-settings-general/src/client/locales.ts'
import { MOE4ALL_THEMES } from '/@dsh/packages/client/ui-theme/src/moe4all-themes.ts'
import '/@dsh/packages/client/ui-theme/src/styles/base.css'
import '/@dsh/packages/client/ui-theme/src/styles/design-platform.css'
import '/@dsh/packages/client/ui-theme/src/styles/moe4all.css'

const query = new URLSearchParams(location.search), language = query.get('lang') ?? 'zh', theme = query.get('theme') ?? 'green'
const palette = MOE4ALL_THEMES.find(row => row.id === theme)!
document.body.toggleAttribute('data-ds-dark-theme', palette.colorScheme === 'dark')
for (const [key, value] of Object.entries(palette.tokens)) document.body.style.setProperty(key, value)
document.body.style.cssText += ';margin:0;padding:24px;font-family:var(--dsw-font-family);background:var(--dsw-alias-bg-base);color:var(--dsw-alias-label-primary);box-sizing:border-box;'
const listeners = new Set<(event: { data: unknown }) => void>(), messages: unknown[] = []
const controller = new DesktopController({ postMessage: value => messages.push(value), addEventListener: (_, listener) => listeners.add(listener), removeEventListener: (_, listener) => listeners.delete(listener) })
const emit = (data: unknown) => listeners.forEach(listener => listener({ data }))
const t = (key: string) => (language === 'zh' ? zh : en)[key] ?? key
const props = { controller, t, useDesktop: (selector: (state: unknown) => unknown) => selector(useSyncExternalStore(controller.store.subscribe, controller.store.getSnapshot, controller.store.getSnapshot)) }
const plugins = [
  ['dsh-llm-moe4all', '0.2.2', '0.2.3'], ['dsh-mneme-moe4all', '0.7.29-moe4all.1', '0.7.30-moe4all.1'],
  ['dsh-market-moe4all', '1.45.1-moe4all.1', '1.45.1-moe4all.1'], ['dsh-remote-web-ui-moe4all', '0.3.17-moe4all.1', '0.3.17-moe4all.1'],
  ['dsh-plugin-cron-moe4all', '0.2.7-moe4all.1', '0.2.7-moe4all.1'], ['dsh-plugin-tts-moe4all', '0.3.1-moe4all.1', '0.3.1-moe4all.1'],
  ['dsh-easyrewrite-moe4all', '2.4.1-moe4all.1', '2.4.1-moe4all.1'], ['dsh-web-search-brave-moe4all', '1.0.0-moe4all.1', '1.0.0-moe4all.1'],
].map(([id, installedVersion, latestVersion]) => ({ id, zh: id, en: id, installedVersion, latestVersion,
  updateAvailable: installedVersion !== latestVersion, phase: installedVersion === latestVersion ? 'current' : 'available',
  releaseUrl: `https://github.com/Headmaster218/${id}/releases` }))
const snapshot = { schema: 'moe4all/capabilities/v1', settings: { autoCheck: true, preview: true, skipped: {} }, plugins, pending: false, checking: false,
  engine: { canInstall: true, installed: { tag: 'release-1.0.0' }, latest: { tag: 'release-1.0.1' }, runtime: { owned: false }, updateAvailable: true, phase: 'idle' } }
const rows = [{ id: 'plugins', order: 15, label: language === 'zh' ? '插件' : 'Plugins' }, { id: 'moe4all', order: 16, label: 'MoE4All' }, { id: 'capabilities', order: 17, label: t('cap.title') }]
createRoot(document.getElementById('root')!).render(<>
  <h1 style={{ fontSize: 18, fontWeight: 500 }}>MoE4All Agent</h1>
  <div style={{ width: 170 }}><SettingsRoot wide useSections={selector => selector(rows)} useOnboardingSteps={selector => selector([])} useSessions={selector => selector({ phase: 'ready', current: 'qa', byId: { qa: { blank: false } } })}
    renderSlot={(slot, owner, options) => slot === 'settings.trigger' ? <>{t('trigger')}</> : slot === 'settings.header' ? t('title') : slot === 'settings.close' ? t('close')
      : slot === 'settings.notification' ? <CapabilitiesNotification {...props} {...owner} /> : slot === 'settings.section' ? options?.only === 'capabilities' ? <CapabilitiesSection {...props} /> : <h2>{rows.find(row => row.id === options?.only)?.label}</h2> : null} /></div>
</>)
Object.assign(window, { capabilityQa: { emit, controller, messages, snapshot } })
setTimeout(() => {
  emit({ type: 'moe4all-desktop-state', phase: 'available', installedVersion: 'release-0.1.0-preview.3', version: 'release-0.1.0-preview.4', completed: 0, total: 90 * 1048576, message: '', open: true })
  emit({ type: 'moe4all-capabilities-state', busy: false, snapshot })
}, 100)
