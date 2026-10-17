import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { appendFileSync, lstatSync, readFileSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { publicationSafety, releasePlan } from './release-mode.mjs'

export function agentReleasePlan(build, environment) {
  assert.equal(build.repository, 'Headmaster218/MoE4All-Agent')
  const plan = releasePlan({ name: 'MoE4All-Agent', version: build.version }, environment)
  assert.equal(plan.tag, 'release-' + build.version, 'Agent tags must use release-<version>')
  if (environment.sourceTag) {
    assert.equal(environment.event, 'workflow_dispatch')
    assert.equal(environment.sourceTag, plan.tag, 'Requested tag must match the checked-out version')
    assert.equal(environment.sourceTagSha, plan.sha, 'Requested tag must resolve to the checkout')
  }
  return { ...plan, draft: true, version: build.version,
    allowCreateTag: environment.event === 'workflow_dispatch' && !environment.sourceTag }
}

function command(root, executable, args, missing = false) {
  const result = spawnSync(executable, args, { cwd: root, encoding: 'utf8', timeout: 120_000, maxBuffer: 8 * 1024 * 1024 })
  if (missing && result.status !== 0 && /HTTP 404/u.test(result.stderr ?? '')) return null
  assert.equal(result.status, 0, result.stderr || String(result.error))
  return result.stdout.trim()
}

function remoteSafety(root, plan) {
  const endpoint = 'repos/' + plan.repository
  const gh = (args, missing = false) => command(root, 'gh', args, missing)
  // GitHub's tag lookup omits draft releases; the authenticated list includes them.
  const pages = JSON.parse(gh(['api', endpoint + '/releases?per_page=100', '--paginate', '--slurp']))
  const existing = pages.flat().some(release => release.tag_name === plan.tag)
  const tag = gh(['api', endpoint + '/git/ref/tags/' + encodeURIComponent(plan.tag)], true)
  let object = tag ? JSON.parse(tag).object : null
  for (let depth = 0; object?.type === 'tag' && depth < 4; depth++) object = JSON.parse(gh(['api', endpoint + '/git/tags/' + object.sha])).object
  if (object) assert.equal(object.type, 'commit')
  publicationSafety(plan, existing, object?.sha)
  if (!object) assert(plan.allowCreateTag, 'Only a new main draft may create a missing release tag')
  return { object, gh, endpoint }
}

function regularFile(file) {
  const stat = lstatSync(file)
  assert(stat.isFile() && !stat.isSymbolicLink(), 'A regular release asset is required')
  return file
}

function publish(root, plan) {
  const prefix = `MoE4All-Agent-Windows-x64-v${plan.version}.zip`
  const archive = regularFile(resolve(root, 'artifacts/agent-release', prefix))
  const checksum = regularFile(archive + '.sha256')
  const sum = readFileSync(checksum, 'utf8').trim()
  assert.equal(sum.slice(66), prefix, 'Checksum must name the exact Agent ZIP')
  assert.match(sum, /^[a-f0-9]{64}  /u)
  assert.equal(createHash('sha256').update(readFileSync(archive)).digest('hex'), sum.slice(0, 64))
  const notes = regularFile(resolve(root, `documentation/releases/${plan.version}.md`))
  const { object, gh, endpoint } = remoteSafety(root, plan)
  if (!object) gh(['api', '--method', 'POST', endpoint + '/git/refs', '-f', 'ref=refs/tags/' + plan.tag, '-f', 'sha=' + plan.sha])
  const args = ['release', 'create', plan.tag, archive, checksum, '--repo', plan.repository, '--verify-tag',
    '--draft', '--title', 'MoE4All Agent ' + plan.version, '--notes-file', notes]
  if (plan.version.includes('-')) args.push('--prerelease')
  console.log(gh(args))
  console.log(JSON.stringify({ ...plan, assets: [prefix, prefix + '.sha256'] }))
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const root = fileURLToPath(new URL('../', import.meta.url))
  const build = JSON.parse(readFileSync(resolve(root, 'harness/distribution/agent-build.json'), 'utf8'))
  const git = args => command(root, 'git', args)
  const sha = git(['rev-parse', 'HEAD'])
  const sourceTag = process.env.SOURCE_TAG || ''
  if (sourceTag) assert.match(sourceTag, /^release-\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/u)
  else assert.equal(sha, process.env.GITHUB_SHA, 'Checkout differs from the workflow commit')
  const plan = agentReleasePlan(build, { sha, repository: process.env.GITHUB_REPOSITORY, event: process.env.GITHUB_EVENT_NAME,
    ref: process.env.GITHUB_REF, sourceTag, sourceTagSha: sourceTag ? git(['rev-parse', sourceTag + '^{commit}']) : '' })
  git(['merge-base', '--is-ancestor', 'HEAD', 'origin/main'])
  assert(process.env.RUNNER_TEMP)
  const record = resolve(process.env.RUNNER_TEMP, 'moe4all-agent-release-plan.json')
  if (process.argv[2] === 'preflight') {
    regularFile(resolve(root, `documentation/releases/${plan.version}.md`))
    remoteSafety(root, plan)
    writeFileSync(record, JSON.stringify(plan))
    assert(process.env.GITHUB_OUTPUT)
    appendFileSync(process.env.GITHUB_OUTPUT, `tag=${plan.tag}\n`)
    console.log(JSON.stringify(plan))
  } else {
    assert.equal(process.argv[2], 'publish')
    assert.deepEqual(plan, JSON.parse(readFileSync(record, 'utf8')), 'Publish must use the validated source and version')
    publish(root, plan)
  }
}
