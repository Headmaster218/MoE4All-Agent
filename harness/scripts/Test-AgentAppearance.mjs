import assert from 'node:assert/strict'
import { mkdir, writeFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
import { setTimeout } from 'node:timers/promises'

const require = createRequire(new URL('../dsh/apps/web/package.json', import.meta.url))
const { chromium } = require('playwright')
const url = process.argv[2] ?? 'http://127.0.0.1:17374/'
const output = path.resolve(process.argv[3] ?? 'artifacts/appearance/web')
await mkdir(output, { recursive: true })
const deadline = Date.now() + 30000
for (;;) {
  try { if ((await fetch(url)).status === 200) break } catch { /* The isolated UI server may still be starting. */ }
  assert.ok(Date.now() < deadline, 'Isolated UI server did not become ready')
  await setTimeout(500)
}
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] })
const report = []
const errors = []

// Inspect rendered text and placeholders, including inherited colors and alpha.
async function audit(page, name) {
  await page.waitForTimeout(700)
  const result = await page.evaluate(() => {
    const rgb = value => {
      const channels = value.match(/[\d.]+/g)?.map(Number) ?? [0, 0, 0, 0]
      const color = channels.slice(0, 3).map(channel => value.startsWith('color(srgb ') ? channel * 255 : channel)
      return [...color, channels[3] ?? 1]
    }
    const blend = (front, back) => front.slice(0, 3).map((value, i) => value * front[3] + back[i] * (1 - front[3]))
    const lum = color => color.map(x => x / 255).map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4)
      .reduce((sum, x, i) => sum + x * [.2126, .7152, .0722][i], 0)
    const failures = []
    let checked = 0
    for (const el of document.querySelectorAll('body *')) {
      const text = [...el.childNodes].filter(n => n.nodeType === Node.TEXT_NODE).map(n => n.textContent.trim()).join(' ').trim()
      const placeholder = el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement ? el.placeholder : ''
      if ((!text && !placeholder) || el.closest('svg,script,style')) continue
      const r = el.getBoundingClientRect()
      if (r.width < 1 || r.height < 1 || r.bottom < 0 || r.top > innerHeight) continue
      const top = document.elementFromPoint(Math.max(0, Math.min(innerWidth - 1, r.left + r.width / 2)), Math.max(0, Math.min(innerHeight - 1, r.top + r.height / 2)))
      if (top && !el.contains(top) && !top.contains(el)) continue
      const ancestors = []
      for (let parent = el; parent; parent = parent.parentElement) ancestors.unshift(parent)
      let bg = [255, 255, 255]
      let opacity = 1
      let visible = true
      for (const parent of ancestors) {
        const style = getComputedStyle(parent)
        if (style.visibility === 'hidden' || style.display === 'none') visible = false
        bg = blend(rgb(style.backgroundColor), bg)
        opacity *= Number(style.opacity)
      }
      if (!visible || opacity < .01) continue
      const style = getComputedStyle(el, !text && placeholder ? '::placeholder' : null)
      const fg = rgb(style.color)
      fg[3] *= opacity * (!text && placeholder ? Number(style.opacity || 1) : 1)
      const l = [lum(blend(fg, bg)), lum(bg)].sort((a, b) => b - a)
      const ratio = (l[0] + .05) / (l[1] + .05)
      checked++
      if (ratio < 4.5 - .01) failures.push({ text: (text || placeholder).slice(0, 85), className: el.className, ratio: +ratio.toFixed(2), color: style.color, bg })
    }
    return { checked, failures }
  })
  report.push({ name, ...result })
  await page.screenshot({ path: path.join(output, `${name}.png`) })
  console.log(name, JSON.stringify(result))
}

try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } })
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
  await page.addInitScript(() => {
    const listeners = new Set()
    window.emitDesktop = state => listeners.forEach(listener => listener({ data: {
      type: 'moe4all-desktop-state', phase: 'idle', installedVersion: 'preview.test', version: '',
      completed: 0, total: 0, message: '', open: false, ...state,
    } }))
    window.chrome.webview = {
      addEventListener: (type, listener) => listeners.add(listener),
      removeEventListener: (type, listener) => listeners.delete(listener),
      postMessage: value => {
        if (value.type === 'moe4all-appearance') window.lastAppearance = value
        else if (value.command === 'status') window.emitDesktop({})
        else if (value.command === 'check') setTimeout(() => window.emitDesktop({ phase: 'available', version: 'preview.next', total: 524288000, open: true }), 100)
        else if (value.command === 'install') window.installRequests = (window.installRequests || 0) + 1
      },
    }
  })
  assert.equal((await page.goto(url, { waitUntil: 'domcontentloaded' })).status(), 200)
  await page.waitForFunction(() => window.lastAppearance?.type === 'moe4all-appearance', { timeout: 30000 })
  await page.waitForTimeout(5000)
  assert.equal(await page.title(), 'MoE4All Agent')
  for (const language of process.env.MOE4ALL_APPEARANCE_QUICK ? ['en'] : ['zh', 'en']) {
    const zh = language === 'zh'
    for (const theme of process.env.MOE4ALL_APPEARANCE_QUICK ? ['white'] : ['green', 'black', 'gray', 'white']) {
      await page.evaluate(value => window.dispatchEvent(new CustomEvent('moe4all-appearance', { detail: value })), { language, theme })
      await page.waitForFunction(value => window.lastAppearance.language === value.language && window.lastAppearance.theme === value.theme, { language, theme })
      const close = page.getByRole('button', { name: zh ? '关闭' : 'Close', exact: true })
      if (await close.count()) await close.first().click()
      await page.getByRole('button', { name: zh ? '设置' : 'Settings', exact: true }).click()
      await page.getByRole('button', { name: zh ? '通用设置' : 'General', exact: true }).click()
      await audit(page, `${language}-${theme}-general`)
      await page.getByRole('button', { name: zh ? '检查更新' : 'Check updates', exact: true }).click()
      const update = page.getByRole('dialog', { name: zh ? 'MoE4All Agent 更新' : 'MoE4All Agent updates', exact: true })
      await update.getByRole('button', { name: zh ? '下载并安装' : 'Download and install' }).waitFor()
      await audit(page, `${language}-${theme}-update`)
      assert.equal(await page.evaluate(() => window.installRequests || 0), 0)
      for (const phase of ['download', 'extract', 'error']) {
        await page.evaluate(phase => window.emitDesktop({ phase, version: 'preview.next', completed: phase === 'download' ? 50 : 0, total: phase === 'download' ? 100 : 0, message: phase === 'error' ? 'Network unavailable. Retry when connected.' : '' }), phase)
        await audit(page, `${language}-${theme}-update-${phase}`)
      }
      await update.getByRole('button', { name: zh ? '关闭' : 'Close', exact: true }).last().click()
      await page.getByRole('button', { name: 'MoE4All', exact: true }).click()
      await audit(page, `${language}-${theme}-engine`)
      await page.getByRole('button', { name: zh ? '插件' : 'Plugins', exact: true }).click()
      await audit(page, `${language}-${theme}-plugins`)
      for (const [key, label] of [['easyrewrite', 'EasyRewrite'], ['remote-settings', zh ? '远程访问设置' : 'Remote access settings'], ['market-settings', zh ? '插件市场' : 'Plugin Market']]) {
        const title = page.getByText(label, { exact: true }).last()
        await title.click()
        await audit(page, `${language}-${theme}-${key}`)
      }
      await page.getByText(zh ? '语音' : 'Voice', { exact: true }).click()
      await audit(page, `${language}-${theme}-tts`)
      await page.getByRole('button', { name: zh ? '关闭' : 'Close', exact: true }).first().click()
      await page.getByRole('button', { name: zh ? '+ 新建' : '+ New', exact: true }).click()
      await audit(page, `${language}-${theme}-cron`)
      await page.getByRole('button', { name: zh ? '取消' : 'Cancel', exact: true }).click()
      await page.getByRole('button', { name: zh ? '打开记忆库' : 'Open memory panel', exact: true }).click()
      await audit(page, `${language}-${theme}-memory`)
      await page.locator('.mneme-vtabs').getByRole('button', { name: zh ? '设置' : 'Settings', exact: true }).click()
      await audit(page, `${language}-${theme}-memory-settings`)
      await page.getByRole('button', { name: zh ? '关闭' : 'Close', exact: true }).click()
      await audit(page, `${language}-${theme}-home`)
    }
  }
  await page.reload()
  await page.waitForFunction(() => window.lastAppearance?.language === 'en' && window.lastAppearance?.theme === 'white')
  assert.ok(!(await page.locator('body').innerText()).includes('Failed to load plugins'))
  const initialClose = page.getByRole('button', { name: 'Close', exact: true })
  if (await initialClose.count()) await initialClose.first().click()
  for (const width of [768, 390]) {
    await page.setViewportSize({ width, height: 900 })
    await audit(page, `en-white-${width}`)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await page.getByRole('button', { name: 'Settings', exact: true }).click()
    await page.getByRole('button', { name: 'General', exact: true }).click()
    await audit(page, `en-white-general-${width}`)
    await page.getByRole('button', { name: /^(Check updates|View update)$/ }).click()
    const update = page.getByRole('dialog', { name: 'MoE4All Agent updates', exact: true })
    await update.getByRole('button', { name: 'Download and install' }).waitFor()
    await audit(page, `en-white-update-${width}`)
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth))
    await update.getByRole('button', { name: 'Close', exact: true }).last().click()
    await page.getByRole('button', { name: 'Close', exact: true }).first().click()
  }
  await page.setViewportSize({ width: 1440, height: 1000 })
  await page.emulateMedia({ colorScheme: 'dark' })
  const onboardingClose = page.getByRole('button', { name: 'Close', exact: true })
  if (await onboardingClose.count()) await onboardingClose.first().click()
  await page.getByRole('button', { name: 'Settings', exact: true }).click()
  await page.getByRole('button', { name: 'General', exact: true }).click()
  await page.locator('button[data-theme="green"]').click()
  await page.getByRole('checkbox', { name: 'Follow system' }).check()
  await page.waitForFunction(() => window.lastAppearance.preference === 'system' && window.lastAppearance.theme === 'green')
  await page.emulateMedia({ colorScheme: 'light' })
  await page.waitForFunction(() => window.lastAppearance.theme === 'white')
  await audit(page, 'en-system-light')
  await page.emulateMedia({ colorScheme: 'dark' })
  await page.waitForFunction(() => window.lastAppearance.theme === 'green')
  await page.locator('button[data-theme="black"]').click()
  await page.waitForFunction(() => window.lastAppearance.preference === 'system' && window.lastAppearance.theme === 'black' && window.lastAppearance.darkPreference === 'black')
  await page.reload()
  await page.waitForFunction(() => window.lastAppearance.preference === 'system' && window.lastAppearance.darkPreference === 'black' && window.lastAppearance.theme === 'black')
  await audit(page, 'en-system-dark-restored')
  assert.deepEqual(errors, [])
  const failures = report.filter(item => item.failures.length)
  await writeFile(path.join(output, 'report.json'), JSON.stringify({ errors, report }, null, 2))
  assert.equal(failures.length, 0, `${failures.length} views have contrast failures; see report.json`)
} finally {
  await writeFile(path.join(output, 'report.json'), JSON.stringify({ errors, report }, null, 2))
  await browser.close()
}
