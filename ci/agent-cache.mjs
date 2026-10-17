import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { appendFileSync, mkdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { cachePlan } from './dependency-cache.mjs'

export function agentCachePlan(root, environment) {
  const runtime = JSON.parse(readFileSync(resolve(root, 'harness/distribution/runtime-versions.json'), 'utf8'))
  const config = JSON.parse(readFileSync(resolve(root, 'ci/cache.json'), 'utf8'))
  const npm = cachePlan(root, config, environment)
  for (const value of [runtime.node, runtime.pnpm]) assert.match(value, /^\d+\.\d+\.\d+$/u)
  assert.equal(runtime.node, readFileSync(resolve(root, '.node-version'), 'utf8').trim(), 'CI Node must match the bundled runtime')
  assert.equal(environment.nodeVersion, runtime.node, 'CI must use the fixed Agent Node')
  assert.equal(runtime.registry, 'https://registry.npmjs.org')
  assert.match(runtime.nodeWindowsX64Sha256, /^[a-f0-9]{64}$/u)
  const build = JSON.parse(readFileSync(resolve(root, 'harness/distribution/build-tools/package.json'), 'utf8'))
  assert.equal(build.dependencies.pnpm, runtime.pnpm)
  const digest = createHash('sha256').update(npm.key)
  for (const name of ['harness/dsh/pnpm-lock.yaml', 'harness/dsh/pnpm-workspace.yaml', 'harness/distribution/profile/pnpm-lock.yaml']) {
    const bytes = readFileSync(resolve(root, name))
    assert(bytes.length > 0, 'Cannot cache an empty dependency input')
    digest.update(name + '\0').update(bytes).update('\0')
  }
  const prefix = `moe4all-agent-pnpm-v1-${environment.os}-${environment.arch}-node-${runtime.node}-npm-${config.npmVersion}-pnpm-${runtime.pnpm}-`
  return { pnpmKey: prefix + digest.digest('hex'), pnpmRestoreKey: prefix,
    pnpmPath: resolve(environment.temp, 'moe4all-dependency-cache/pnpm'),
    nodeKey: `moe4all-node-archive-v1-Windows-X64-${runtime.node}-${runtime.nodeWindowsX64Sha256}`,
    nodePath: resolve(environment.temp, 'moe4all-dependency-cache/node-archives'), save: npm.save }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const root = fileURLToPath(new URL('../', import.meta.url))
  const plan = agentCachePlan(root, { os: process.env.RUNNER_OS, arch: process.env.RUNNER_ARCH,
    nodeVersion: process.versions.node, temp: process.env.MOE4ALL_CI_CACHE_ROOT || process.env.RUNNER_TEMP,
    event: process.env.GITHUB_EVENT_NAME, ref: process.env.GITHUB_REF })
  assert(process.env.GITHUB_OUTPUT && process.env.GITHUB_ENV)
  for (const path of [plan.pnpmPath, plan.nodePath]) mkdirSync(path, { recursive: true })
  for (const [name, value] of Object.entries(plan)) appendFileSync(process.env.GITHUB_OUTPUT, `${name}=${value}\n`)
  appendFileSync(process.env.GITHUB_ENV, `MOE4ALL_PNPM_STORE=${plan.pnpmPath}\nMOE4ALL_NODE_ARCHIVE_ROOT=${plan.nodePath}\nnpm_config_side_effects_cache=false\n`)
  console.log(JSON.stringify(plan))
}
