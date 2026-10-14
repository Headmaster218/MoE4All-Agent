import assert from 'node:assert/strict'
import { readFile, mkdir } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'
const require = createRequire(new URL('../dsh/apps/web/package.json', import.meta.url))
const { chromium } = require('playwright')
const root = path.resolve(process.argv[2] ?? 'artifacts/appearance/screenshots')
const output = path.join(root, 'web-startup')
await mkdir(output, { recursive: true })
const html = await readFile(path.join(root, 'startup.html'), 'utf8')
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] })
let count = 0
try {
  const page = await browser.newPage()
  const errors = []
  page.on('pageerror', error => errors.push(error.message))
  for (const language of ['zh', 'en']) for (const theme of ['green', 'black', 'gray', 'white']) {
    const state = JSON.parse(await readFile(path.join(root, `${language}-${theme}.json`), 'utf8'))
    for (const [width, height] of [[760, 460], [1024, 600], [1280, 720], [390, 780]]) {
      await page.setViewportSize({ width, height })
      await page.setContent(html)
      for (const phase of ['start', 'extract', 'error']) {
        await page.evaluate(({ state, phase }) => window.setState({ ...state, failed: phase === 'error', progress: phase === 'extract' ? 57 : null,
          log: phase === 'error' ? 'Runtime missing: C:\\very-long-installation-folder\\harness\\runtime\nResolve the issue and restart.' : '',
          detail: phase === 'error' ? 'Missing runtime. Reinstall the full package.' : state.detail }), { state, phase })
        const bounds = await page.locator('#track').boundingBox()
        assert.ok(bounds.y >= 0 && bounds.y + bounds.height <= height, `${language}-${theme}-${width}: offscreen progress`)
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Horizontal overflow')
        assert.equal(await page.locator('#track').getAttribute('aria-valuenow'), phase === 'extract' ? '57' : null)
        if (width === 1024) await page.screenshot({ path: path.join(output, `${language}-${theme}-${phase}.png`) })
        count++
      }
    }
  }
  assert.deepEqual(errors, [])
  console.log(`Startup HTML passed: ${count} language/palette/viewport/phase combinations.`)
} finally { await browser.close() }
