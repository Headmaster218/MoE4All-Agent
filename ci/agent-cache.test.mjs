import assert from 'node:assert/strict'
import test from 'node:test'
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { agentCachePlan } from './agent-cache.mjs'

const testRoot = process.env.MOE4ALL_CI_TEST_ROOT || process.env.RUNNER_TEMP
assert(testRoot, 'Set an isolated MOE4ALL_CI_TEST_ROOT or RUNNER_TEMP')
mkdirSync(testRoot, { recursive: true })
function fixture() {
  const root = mkdtempSync(join(testRoot, 'agent-cache-'))
  const write = (name, value) => { const file = join(root, name); mkdirSync(join(file, '..'), { recursive: true }); writeFileSync(file, value) }
  const runtime = { node: '24.9.0', pnpm: '11.7.0', registry: 'https://registry.npmjs.org', nodeWindowsX64Sha256: 'a'.repeat(64) }
  write('.node-version', '24.9.0\n')
  write('harness/distribution/runtime-versions.json', JSON.stringify(runtime))
  write('harness/distribution/build-tools/package.json', JSON.stringify({ dependencies: { pnpm: runtime.pnpm } }))
  write('ci/cache.json', JSON.stringify({ schema: 1, npmVersion: '11.6.0', lockfiles: ['harness/distribution/build-tools/package-lock.json'] }))
  write('harness/distribution/build-tools/package-lock.json', JSON.stringify({ lockfileVersion: 3 }))
  for (const name of ['harness/dsh/pnpm-lock.yaml', 'harness/dsh/pnpm-workspace.yaml', 'harness/distribution/profile/pnpm-lock.yaml']) write(name, 'fixture: true\n')
  const env = { os: 'Windows', arch: 'X64', nodeVersion: runtime.node, temp: root, ref: 'refs/heads/main', event: 'push' }
  return { root, write, env, runtime, plan: change => agentCachePlan(root, { ...env, ...change }) }
}

test('Agent main and tag share dependency keys while PR/tag never save', () => {
  const f = fixture(), main = f.plan(), tag = f.plan({ ref: 'refs/tags/release-0.2.2' })
  assert.equal(main.pnpmKey, tag.pnpmKey)
  assert.equal(main.nodeKey, tag.nodeKey)
  assert.equal(main.save, true)
  assert.equal(tag.save, false)
  assert.equal(f.plan({ event: 'pull_request' }).save, false)
})

test('DSH, profile, workspace policy and npm tool locks all invalidate the pnpm key', () => {
  for (const name of ['harness/dsh/pnpm-lock.yaml', 'harness/distribution/profile/pnpm-lock.yaml', 'harness/dsh/pnpm-workspace.yaml', 'harness/distribution/build-tools/package-lock.json']) {
    const f = fixture(), before = f.plan()
    f.write(name, name.endsWith('.json') ? JSON.stringify({ lockfileVersion: 3, changed: true }) : 'changed: true\n')
    const after = f.plan()
    assert.notEqual(after.pnpmKey, before.pnpmKey)
    assert.equal(after.pnpmRestoreKey, before.pnpmRestoreKey)
    assert.equal(after.nodeKey, before.nodeKey)
  }
})

test('Node archive cache is checksum-specific and incompatible runtime overrides fail', () => {
  const f = fixture(), before = f.plan()
  f.runtime.nodeWindowsX64Sha256 = 'b'.repeat(64)
  f.write('harness/distribution/runtime-versions.json', JSON.stringify(f.runtime))
  assert.notEqual(f.plan().nodeKey, before.nodeKey)
  assert.throws(() => f.plan({ nodeVersion: '24.21.0' }))
  f.runtime.pnpm = '99.0.0'
  f.write('harness/distribution/runtime-versions.json', JSON.stringify(f.runtime))
  assert.throws(() => f.plan())
})

test('build and deploy both consume the explicit store; local defaults and Node verification remain', () => {
  const build = readFileSync(new URL('../harness/scripts/Build-DevHarness.ps1', import.meta.url), 'utf8')
  const release = readFileSync(new URL('../harness/scripts/Build-AgentRelease.ps1', import.meta.url), 'utf8')
  for (const script of [build, release]) {
    assert(script.includes('$pnpmStore = [IO.Path]::GetFullPath($env:MOE4ALL_PNPM_STORE)'))
    assert(script.includes('MOE4ALL_PNPM_STORE must be absolute.'))
  }
  assert(build.includes("$pnpmStore = Join-Path $runtimeRoot 'pnpm-store'"))
  assert(build.includes('$archive = Join-Path $env:MOE4ALL_NODE_ARCHIVE_ROOT'))
  assert(build.includes('Node archive SHA256 does not match the pinned runtime.'))
  assert(release.includes('("--config.store-dir=$pnpmStore")'))
})
