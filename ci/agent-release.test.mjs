import assert from 'node:assert/strict'
import test from 'node:test'
import { readFileSync } from 'node:fs'
import { agentReleasePlan } from './agent-release.mjs'

const build = { version: '0.2.2', repository: 'Headmaster218/MoE4All-Agent' }
const env = { repository: build.repository, sha: 'a'.repeat(40), event: 'workflow_dispatch', ref: 'refs/heads/main' }

test('manual main builds and release tags always produce Agent drafts', () => {
  const draft = agentReleasePlan(build, env)
  assert.equal(draft.draft, true)
  assert.equal(draft.allowCreateTag, true)
  const tagged = agentReleasePlan(build, { ...env, event: 'push', ref: 'refs/tags/release-0.2.2' })
  assert.equal(tagged.draft, true)
  assert.equal(tagged.allowCreateTag, false)
})

test('source_tag rebuild is immutable and cannot create or move an existing tag', () => {
  const rebuild = { ...env, sourceTag: 'release-0.2.2', sourceTagSha: env.sha }
  assert.equal(agentReleasePlan(build, rebuild).allowCreateTag, false)
  assert.throws(() => agentReleasePlan(build, { ...rebuild, sourceTagSha: 'b'.repeat(40) }))
  assert.throws(() => agentReleasePlan(build, { ...rebuild, sourceTag: 'release-0.2.1' }))
})

test('wrong refs, events, repositories and versions cannot prepare an Agent release', () => {
  for (const change of [{ ref: 'refs/heads/dev' }, { event: 'pull_request' }, { repository: 'other/MoE4All-Agent' },
    { event: 'push', ref: 'refs/tags/release-9.9.9' }, { event: 'push', ref: 'refs/tags/v0.2.2' }]) {
    assert.throws(() => agentReleasePlan(build, { ...env, ...change }))
  }
})

test('publisher checks authenticated drafts, fixed source and checksums before draft upload', () => {
  const source = readFileSync(new URL('./agent-release.mjs', import.meta.url), 'utf8')
  assert(source.includes("endpoint + '/releases?per_page=100', '--paginate', '--slurp'"))
  assert(source.includes('publicationSafety(plan, existing, object?.sha)'))
  assert(source.includes("'--draft', '--title'"))
  assert(source.includes("createHash('sha256').update(readFileSync(archive))"))
  assert(source.includes('Publish must use the validated source and version'))
  const workflow = readFileSync(new URL('../.github/workflows/release-windows.yml', import.meta.url), 'utf8')
  for (const gate of ['Test-AgentRelease.ps1', 'Test-AgentUpdate.ps1 -LegacyTags', 'profile.integration.test.cjs', 'agent-release.mjs preflight', 'agent-release.mjs publish']) assert(workflow.includes(gate))
})
