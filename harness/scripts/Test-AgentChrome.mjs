import assert from 'node:assert/strict'
import { readFile, mkdir } from 'node:fs/promises'
import { createRequire } from 'node:module'
import path from 'node:path'

const [scriptPath, outputPath, modulesPath] = process.argv.slice(2)
assert.ok(scriptPath && outputPath, 'Usage: Test-AgentChrome.mjs <native-test/chrome-script.js> <output> [Playwright directory]')
const require = createRequire(modulesPath ? path.join(path.resolve(modulesPath), 'package.json') : new URL('../dsh/apps/web/package.json', import.meta.url))
const { chromium } = require('playwright')
const script = await readFile(scriptPath, 'utf8')
await mkdir(outputPath, { recursive: true })
const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--disable-gpu'] })
try {
  const page = await browser.newPage()
  for (const width of [980, 1380, 1920]) {
    await page.setViewportSize({ width, height: 800 })
    for (const details of [false, true]) {
      await page.setContent(`<style>
        *{box-sizing:border-box}body{margin:0;font-family:Arial;background:#202421;color:#e4e9e5}
        main{display:grid;grid-template-columns:240px minmax(0,1fr) ${details ? '320px' : '0px'};height:100vh}
        header{height:38px;display:flex;align-items:center;justify-content:space-between;padding:4px 12px;gap:12px}
        section{min-width:0;overflow:hidden}button{flex-shrink:0}h1{font-size:14px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        #native{position:fixed;right:0;top:0;width:187px;height:30px;background:#384139;pointer-events:none}
        </style><main ${details ? '' : 'data-details-collapsed'}>
        <aside><header data-moe4all-drag><button>MoE4All</button><button>Menu</button></header></aside>
        <section><header data-moe4all-drag data-moe4all-caption="conversation"><h1>Conversation</h1><button id="conversation-action">Actions</button></header></section>
        <section><header data-moe4all-drag data-moe4all-caption="details"><h1>Details</h1><button id="details-action">Close</button></header></section>
        </main><div id="native"></div>`)
      await page.evaluate(script)
      await page.evaluate(script)
      const result = await page.evaluate((details) => {
        const action = document.querySelector(details ? '#details-action' : '#conversation-action')
        const native = document.querySelector('#native').getBoundingClientRect()
        const button = action.getBoundingClientRect()
        const header = action.closest('header')
        return { top: document.querySelector('main').getBoundingClientRect().top,
          right: button.right, nativeLeft: native.left, buttonRegion: getComputedStyle(action).webkitAppRegion,
          headerRegion: getComputedStyle(header).webkitAppRegion, copies: document.querySelectorAll('#moe4all-native-chrome').length }
      }, details)
      assert.equal(result.top, 0, 'Content must begin at the former title band')
      assert.ok(result.right <= result.nativeLeft, 'Page actions overlap native caption controls')
      assert.equal(result.headerRegion, 'drag')
      assert.equal(result.buttonRegion, 'no-drag')
      assert.equal(result.copies, 1, 'Repeated geometry changes must not accumulate styles')
      await page.locator(details ? '#details-action' : '#conversation-action').click()
      await page.screenshot({ path: path.join(outputPath, `chrome-${width}-${details ? 'details' : 'conversation'}.png`) })
    }
  }
  console.log('Chrome layout passed: 980/1380/1920px, details open/closed, no lost row, caption clearance, drag and clickable actions, idempotent injection.')
} finally { await browser.close() }
