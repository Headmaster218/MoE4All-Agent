import assert from 'node:assert/strict'
import fs from 'node:fs/promises'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL, fileURLToPath } from 'node:url'

const [dshArg, evidenceArg] = process.argv.slice(2)
assert(dshArg && evidenceArg && process.env.MOE4ALL_QA_MODULES, 'Staged DSH, evidence output and QA tools are required')
const dsh = path.resolve(dshArg), evidence = path.resolve(evidenceArg)
const workRoot = path.resolve(process.env.MOE4ALL_WORK_ROOT ?? 'D:/AIinfr/infr/local-work')
for (const [category, target] of [['staging', dsh], ['evidence', evidence]]) {
  const relative = path.relative(path.join(workRoot, category, 'agent'), target)
  assert(relative && !relative.startsWith('..') && !path.isAbsolute(relative), 'Keep UI tests in the documented task categories')
}
const requireDsh = createRequire(path.join(dsh, 'package.json'))
const viteRequire = createRequire(requireDsh.resolve('vitest/package.json'))
const { createServer } = await import(pathToFileURL(viteRequire.resolve('vite')).href)
const { chromium } = createRequire(path.join(process.env.MOE4ALL_QA_MODULES, 'package.json'))('playwright')
const fixture = path.join(dsh, '.qa-capabilities')
const reactRequire = createRequire(path.join(dsh, 'packages/client/ui-settings-general/package.json'))
await fs.mkdir(fixture, { recursive: true }); await fs.mkdir(evidence, { recursive: true })
await fs.cp(fileURLToPath(new URL('./qa/capabilities/', import.meta.url)), fixture, { recursive: true })
try { await fs.symlink(await fs.realpath(path.join(dsh, 'packages/client/ui-settings-general/node_modules')), path.join(fixture, 'node_modules'), 'junction') }
catch (error) { if (error.code !== 'EEXIST') throw error }
const server = await createServer({ configFile: false, root: fixture, cacheDir: path.join(fixture, 'cache'),
  resolve: { alias: [
    ...['react', 'react/jsx-runtime', 'react/jsx-dev-runtime', 'react-dom', 'react-dom/client'].map(name => ({ find: new RegExp('^' + name.replaceAll('/', '\\/') + '$'), replacement: reactRequire.resolve(name) })),
    { find: '/@dsh', replacement: dsh.replaceAll('\\', '/') },
    { find: '@deepseek-ai/dsh-client-runtime/client', replacement: path.join(dsh, 'packages/client/runtime/src/client/contract/store.ts') },
    { find: '@deepseek-ai/dsh-client-ui-primitives', replacement: path.join(dsh, 'packages/client/ui-primitives/src/index.ts') },
  ] }, server: { host: '127.0.0.1', port: 0, fs: { allow: [dsh, await fs.realpath(path.join(dsh, 'node_modules')), await fs.realpath(path.join(fixture, 'node_modules'))] } } })
let browser
const reports = [], errors = []
try {
  await server.listen()
  const port = server.httpServer.address().port
  browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] })
  const page = await browser.newPage()
  page.on('pageerror', error => errors.push(error.message))
  page.on('console', message => { if (message.type() === 'error') errors.push(message.text()) })
  for (const lang of ['zh', 'en']) for (const theme of ['green', 'black', 'gray', 'white']) for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 })
    await page.goto(`http://127.0.0.1:${port}/?lang=${lang}&theme=${theme}`)
    const title = lang === 'zh' ? '能力中心' : 'Capabilities'
    await page.getByRole('heading', { name: title, exact: true }).waitFor()
    const report = await page.evaluate(() => {
      const failures = []
      const rgb = text => { const values = text.match(/[\d.]+/g)?.map(Number) ?? [0, 0, 0, 0]; return [...values.slice(0, 3).map(value => text.startsWith('color(srgb ') ? value * 255 : value), values[3] ?? 1] }
      const blend = (front, back) => front.slice(0, 3).map((value, i) => value * front[3] + back[i] * (1 - front[3]))
      const lum = color => color.map(value => value / 255).map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4).reduce((sum, value, i) => sum + value * [.2126, .7152, .0722][i], 0)
      let checked = 0
      for (const el of document.querySelectorAll('[role=dialog] *')) {
        const text = [...el.childNodes].filter(node => node.nodeType === 3).map(node => node.textContent.trim()).join(' ')
        if (!text || el.closest('svg,script,style')) continue
        const bounds = el.getBoundingClientRect(), style = getComputedStyle(el)
        if (!bounds.width || !bounds.height || style.visibility === 'hidden' || bounds.top > innerHeight || bounds.bottom < 0) continue
        const top = document.elementFromPoint(Math.min(innerWidth - 1, bounds.left + bounds.width / 2), Math.max(0, Math.min(innerHeight - 1, bounds.top + bounds.height / 2)))
        if (top && !el.contains(top) && !top.contains(el)) continue
        if (bounds.left < -1 || bounds.right > innerWidth + 1) failures.push({ text, problem: 'overflow' })
        let bg = [255, 255, 255], opacity = 1
        const chain = []; for (let parent = el; parent; parent = parent.parentElement) chain.unshift(parent)
        for (const parent of chain) { const css = getComputedStyle(parent); bg = blend(rgb(css.backgroundColor), bg); opacity *= Number(css.opacity) }
        const fg = rgb(style.color); fg[3] *= opacity
        const levels = [lum(blend(fg, bg)), lum(bg)].sort((a, b) => b - a), ratio = (levels[0] + .05) / (levels[1] + .05)
        checked++
        if (ratio < 4.49) failures.push({ text, ratio })
      }
      return { checked, failures, rows: document.querySelectorAll('[data-capability]').length }
    })
    assert.equal(report.rows, 10)
    reports.push({ lang, theme, width, ...report })
    await page.screenshot({ path: path.join(evidence, `${lang}-${theme}-${width}.png`) })
    assert.deepEqual(report.failures, [], JSON.stringify(reports.at(-1)))
    await page.getByRole('button', { name: lang === 'zh' ? '关闭' : 'Close', exact: true }).click()
    await page.getByRole('button', { name: lang === 'zh' ? '设置' : 'Settings', exact: true }).click()
    const labels = await page.locator('nav button').allTextContents()
    assert.deepEqual(labels, [lang === 'zh' ? '插件' : 'Plugins', 'MoE4All', title])
    await page.getByRole('button', { name: title, exact: true }).click()
    await page.getByRole('heading', { name: title }).waitFor()
    if (width === 1280) await page.screenshot({ path: path.join(evidence, `${lang}-${theme}-settings.png`) })
  }
  assert.deepEqual(errors, [])
  await fs.writeFile(path.join(evidence, 'browser-report.json'), JSON.stringify({ mode: 'isolated-component-fixture', reports, errors }, null, 2))
  console.log(JSON.stringify({ cases: reports.length, errors, evidence }))
} finally { await browser?.close(); await server.close() }
