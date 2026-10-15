const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { CapabilityCenter } = require('./center.cjs');
const profile = require('./profile.cjs');
const release = require('./release.cjs');

assert(process.env.MOE4ALL_CAPABILITY_TEST_ROOT, 'An isolated test root is required');
assert(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, 'Pinned updater tools are required');
const tool = createRequire(path.join(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, 'package.json'));
const semver = tool('semver');
const item = { id: 'test-plugin', repository: 'Headmaster218/test-plugin', zh: '测试', en: 'Test' };
const other = { ...item, id: 'other-plugin', repository: 'Headmaster218/other-plugin' };
const catalog = [item, other];
const host = { dshVersion: '0.1.1-rc.2', peers: {} };

async function fixture(options = {}) {
  const root = await fs.mkdtemp(path.join(process.env.MOE4ALL_CAPABILITY_TEST_ROOT, 'capabilities-'));
  const directory = path.join(root, 'data/home/profiles/web');
  await profile.writeJson(path.join(root, 'harness/distribution/agent-build.json'), { repository: 'Headmaster218/MoE4All-Agent', version: '0.1.0-preview.4' });
  await profile.writeJson(path.join(directory, 'package.json'), { name: 'moe4all-agent-profile-web', private: true,
    dependencies: Object.fromEntries(catalog.map(row => [row.id, '1.0.0'])), dsh: { profile: { bundles: catalog.map(row => row.id) } } });
  await fs.writeFile(path.join(directory, 'pnpm-lock.yaml'), 'lockfileVersion: "9.0"\n');
  await fs.writeFile(path.join(directory, 'pnpm-workspace.yaml'), 'packages: [.]\nnodeLinker: hoisted\nautoInstallPeers: false\n');
  await fs.writeFile(path.join(directory, 'cordis.patch.yml'), 'user-owned: untouched\n');
  for (const row of catalog) await plugin(path.join(directory, 'node_modules', row.id), row.id, '1.0.0');
  const calls = [];
  const responses = new Map();
  const fetcher = async url => {
    calls.push(url);
    assert(responses.has(url), 'Unexpected network request: ' + url);
    return new Response(responses.get(url));
  };
  const installer = async (node, cli, staged) => {
    const pkg = await profile.json(path.join(staged, 'package.json'));
    for (const [name, spec] of Object.entries(pkg.dependencies)) {
      const target = path.join(staged, 'node_modules', name);
      await fs.mkdir(target, { recursive: true });
      await tool('tar').x({ file: spec.slice(5), cwd: target, strip: 1 });
    }
  };
  const center = await new CapabilityCenter(root, { tool, host, catalog, fetch: fetcher, cold: true,
    engineFetch: async url => new Response(JSON.stringify(url.includes('/status') ? { mode: 'prompt', owned: false } : { supported: true, managed: false })),
    install: installer, ...options }).init();
  return { root, directory, calls, responses, center };
}

async function plugin(target, name, version, extra = {}) {
  await profile.writeJson(path.join(target, 'package.json'), { name, version, main: 'lib/index.js', files: ['lib/**', 'cordis.patch.yml', 'LICENSE'],
    dsh: { bundle: { patch: './cordis.patch.yml' } }, engines: { dsh: '>=0.1.1-rc.2 <0.2.0-0' }, ...extra });
  await fs.mkdir(path.join(target, 'lib'), { recursive: true });
  await fs.writeFile(path.join(target, 'lib/index.js'), 'module.exports = ' + JSON.stringify(version) + ';\n');
  await fs.writeFile(path.join(target, 'cordis.patch.yml'), '[]\n');
  await fs.writeFile(path.join(target, 'LICENSE'), 'Test fixture\n');
}

async function publish(f, version = '1.1.0', extra = {}) {
  const source = path.join(f.root, 'fixture-package-' + version);
  await plugin(source, item.id, version, extra);
  const archive = path.join(f.root, 'fixture-' + version + '.tgz');
  await profile.installedArchive(source, archive, tool);
  const bytes = await fs.readFile(archive);
  const tag = 'release-' + version;
  const prefix = 'https://github.com/' + item.repository + '/releases/download/' + tag + '/';
  const metadata = Buffer.from(JSON.stringify({ schema: 'moe4all/plugin-release/v1', name: item.id, version, repository: item.repository, tag,
    commit: 'a'.repeat(40), archive: { name: item.id + '.tgz', bytes: bytes.length, sha256: release.sha256(bytes) } }));
  const assets = [item.id + '.tgz', 'SHA256SUMS.txt', 'plugin-release.json'].map(name => ({ name, size: name.endsWith('.tgz') ? bytes.length : 100,
    browser_download_url: prefix + name }));
  const row = { tag_name: tag, prerelease: !!semver.prerelease(version), assets };
  f.responses.set('https://api.github.com/repos/' + item.repository + '/releases?per_page=30', JSON.stringify([row]));
  f.responses.set('https://api.github.com/repos/' + other.repository + '/releases?per_page=30', '[]');
  f.responses.set(prefix + item.id + '.tgz', bytes);
  f.responses.set(prefix + 'plugin-release.json', metadata);
  f.responses.set(prefix + 'SHA256SUMS.txt', release.sha256(bytes) + '  ' + item.id + '.tgz\n' + release.sha256(metadata) + '  plugin-release.json\n');
  return { archive, row, prefix };
}

test('automatic/manual checks are metadata-only; confirmation and exact versions are mandatory', async () => {
  const f = await fixture(); await publish(f);
  const before = await profile.identity(f.directory, catalog);
  const status = await f.center.check(true, true);
  assert.equal(status.plugins[0].updateAvailable, true);
  assert(f.calls.every(url => url.startsWith('https://api.github.com/')));
  await assert.rejects(f.center.prepare(item.id, '1.1.0', false), /confirmation/);
  await assert.rejects(f.center.prepare(item.id, '1.2.0', true), /selected release changed/);
  assert.deepEqual(await profile.identity(f.directory, catalog), before);
  await f.center.setSettings({ autoCheck: false, preview: false });
  f.calls.length = 0; await f.center.check(true, true); assert.equal(f.calls.length, 0);
});

test('installed snapshots materialize pnpm hardlinks as regular files and settle promptly', { timeout: 5000 }, async () => {
  const f = await fixture();
  const source = path.join(f.directory, 'node_modules', item.id);
  const original = path.join(source, 'lib/index.js');
  await fs.link(original, path.join(source, 'lib/same-inode.js'));
  assert((await fs.stat(original)).nlink > 1);
  const archive = path.join(f.root, 'hardlinks.tgz');
  await profile.installedArchive(source, archive, tool);
  const entries = [];
  await tool('tar').t({ file: archive, onReadEntry: entry => entries.push([entry.path, entry.type]) });
  assert(entries.every(([, type]) => type === 'File'));
  const extracted = path.join(f.root, 'extracted');
  await fs.mkdir(extracted);
  await tool('tar').x({ file: archive, cwd: extracted, strip: 1 });
  assert.equal(await fs.readFile(path.join(extracted, 'lib/same-inode.js'), 'utf8'), await fs.readFile(original, 'utf8'));
});

test('an unsettled CLI cannot exit successfully while preparation is incomplete', async () => {
  const f = await fixture();
  const runtime = path.join(f.root, 'harness/runtime/update-tools');
  await fs.mkdir(path.dirname(runtime), { recursive: true });
  await fs.symlink(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, runtime, 'junction');
  const script = path.join(f.root, 'unsettled.cjs');
  await fs.writeFile(script, `const {CapabilityCenter}=require(${JSON.stringify(require.resolve('./center.cjs'))});
CapabilityCenter.prototype.snapshot=()=>new Promise(()=>{});
process.argv=[process.execPath,__filename,'status','--root',${JSON.stringify(f.root)}];
require(${JSON.stringify(require.resolve('./cli.cjs'))}).main().catch(e=>{console.error(e);process.exitCode=2});\n`);
  const result = await new Promise(resolve => require('node:child_process').execFile(process.execPath, [script], { timeout: 5000 },
    (error, stdout, stderr) => resolve({ error, stdout, stderr })));
  assert.equal(result.error?.code, 1);
  assert.match(result.stderr, /operation did not finish/);
});

test('new plugin peers are checked against the actual packaged host, not the old plugin manifest', async () => {
  const f = await fixture({ host: null });
  const runtime = path.join(f.root, 'harness/runtime/dsh');
  await profile.writeJson(path.join(runtime, 'package.json'), { name: '@deepseek-ai/dsh', version: '0.1.1-rc.2' });
  const peer = '@deepseek-ai/dsh-client-ui-slots';
  await profile.writeJson(path.join(runtime, 'node_modules', peer, 'package.json'), { name: peer, version: '0.1.1-rc.2' });
  await publish(f, '1.1.0', { peerDependencies: { [peer]: '>=0.1.1-rc.2 <0.2.0-0' } });
  await f.center.check(true);
  assert.equal((await f.center.prepare(item.id, '1.1.0', true)).operation.phase, 'ready');
  await f.center.cancel();
  await fs.rm(path.join(runtime, 'node_modules', peer), { recursive: true });
  const rejected = await f.center.prepare(item.id, '1.1.0', true);
  assert.equal(rejected.operation.phase, 'error');
  assert.match(rejected.operation.error, /Required host package is unavailable/);
});

test('prepared plugin is cold-switched only, then healthy rollback preserves user data and unrelated plugins', async () => {
  const f = await fixture(); await publish(f); await f.center.check(true);
  const before = await profile.identity(f.directory, catalog);
  const prepared = await f.center.prepare(item.id, '1.1.0', true);
  assert.equal(prepared.operation.phase, 'ready', prepared.operation.error);
  assert.deepEqual(await profile.identity(f.directory, catalog), before);
  await f.center.apply();
  assert.equal((await profile.identity(f.directory, catalog)).versions[item.id], '1.1.0');
  assert.equal((await profile.identity(f.directory, catalog)).versions[other.id], '1.0.0');
  const manifest = await profile.json(path.join(f.directory, 'package.json'));
  assert(!manifest.dependencies[item.id].includes(f.root.replaceAll('\\', '/')), 'Persisted archive pins must be portable');
  await f.center.healthy(); await f.center.rollback(true); await f.center.apply();
  assert.deepEqual(await profile.identity(f.directory, catalog), before);
  assert.equal(await fs.readFile(path.join(f.directory, 'cordis.patch.yml'), 'utf8'), 'user-owned: untouched\n');
});

test('old Engine Manager is an installation prerequisite, not an installation error', async () => {
  const f = await fixture({ engineFetch: async url => Response.json(url.includes('/status')
    ? { mode: 'connect', owned: false } : { supported: true, latest: { tag: 'release-1.0.1' } }) });
  await f.center.checkEngine(false);
  assert.equal(f.center.state.engine.managerUpdateRequired, true);
  assert.equal(f.center.state.engine.error, '');
  assert.equal(f.center.state.engine.canInstall, false);
});

test('unconfirmed startup and interruption at every rename restore the original profile', async () => {
  for (const phase of ['backed-up', 'activated']) for (const part of ['node_modules', 'package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
    const f = await fixture({ fault: async (point, name) => { if (point === phase && name === part) throw new Error('simulated crash'); } });
    await publish(f); await f.center.check(true);
    const before = await profile.identity(f.directory, catalog);
    await f.center.prepare(item.id, '1.1.0', true);
    assert.match((await f.center.apply()).operation.error, /simulated crash/);
    assert.deepEqual(await profile.identity(f.directory, catalog), before);
  }
  const f = await fixture(); await publish(f); await f.center.check(true);
  const before = await profile.identity(f.directory, catalog);
  await f.center.prepare(item.id, '1.1.0', true); await f.center.apply(); await f.center.apply();
  assert.deepEqual(await profile.identity(f.directory, catalog), before);
});

test('bad SHA256, host incompatibility, lifecycle hooks and staged tampering never change the live profile', async () => {
  for (const mode of ['checksum', 'host', 'hooks', 'tamper']) {
    const f = await fixture();
    const p = await publish(f, '1.1.0', mode === 'host' ? { engines: { dsh: '>=0.2.0' } } : mode === 'hooks' ? { scripts: { postinstall: 'bad' } } : {});
    if (mode === 'checksum') f.responses.set(p.prefix + 'SHA256SUMS.txt', '0'.repeat(64) + '  test-plugin.tgz\n');
    await f.center.check(true);
    const before = await profile.identity(f.directory, catalog);
    const prepared = await f.center.prepare(item.id, '1.1.0', true);
    if (mode === 'tamper') {
      assert.equal(prepared.operation.phase, 'ready', prepared.operation.error);
      const tx = await f.center.transaction();
      await fs.appendFile(path.join(tx.staged, 'node_modules', item.id, 'lib/index.js'), 'tampered');
      assert.match((await f.center.apply()).operation.error, /files changed/);
    } else assert.equal(prepared.operation.phase, 'error', mode);
    assert.deepEqual(await profile.identity(f.directory, catalog), before);
  }
});

test('release channel, fixed source, required host range, and Agent compatibility are enforced', async () => {
  const f = await fixture(); const p = await publish(f, '1.1.0-preview.1');
  assert.equal(release.selectRelease([p.row], item, '1.0.0', false, semver), null);
  assert.equal(release.selectRelease([p.row], item, '1.0.0', true, semver).version, '1.1.0-preview.1');
  assert.equal(release.previewVersion('1.1.0-moe4all.1', semver), false);
  assert.equal(release.previewVersion('1.1.0-moe4all.1.preview.1', semver), true);
  assert.throws(() => release.assetUrl('https://example.com/file.tgz', item, p.row.tag_name));
  assert.throws(() => release.assetUrl(p.prefix.replace('test-plugin/', 'another/') + 'test-plugin.tgz', item, p.row.tag_name));
  assert.throws(() => release.checkCompatibility({ peerDependencies: { '@deepseek-ai/cordis': '^4.0.0' } }, host, semver), /unavailable/);
  await assert.rejects(f.center.compatibility('0.2.0'), /Incompatible DSH/);
  assert.equal((await f.center.compatibility(host.dshVersion)).compatible, true);
});

test('manual profile changes prevent apply or rollback; cancellation leaves installed files intact', async () => {
  const f = await fixture(); await publish(f); await f.center.check(true);
  await f.center.prepare(item.id, '1.1.0', true);
  await fs.appendFile(path.join(f.directory, 'pnpm-workspace.yaml'), '# user change\n');
  assert.match((await f.center.apply()).operation.error, /Profile changed/);
  await f.center.cancel();
  assert.equal((await profile.identity(f.directory, catalog)).versions[item.id], '1.0.0');
  await f.center.prepare(item.id, '1.1.0', true); await f.center.apply(); await f.center.healthy();
  await fs.appendFile(path.join(f.directory, 'pnpm-lock.yaml'), '# user change\n');
  await assert.rejects(f.center.rollback(true), /Profile changed/);
});

test('Engine checks never install; confirmed fresh installs work, external and running Engines are rejected', async () => {
  for (const mode of ['fresh', 'external', 'running']) {
    const calls = [];
    const f = await fixture({ engineFetch: async (url, init) => {
      calls.push([url, init.method]);
      return new Response(JSON.stringify(url.includes('/status')
        ? { mode: mode === 'external' ? 'connect' : 'prompt', executable: mode === 'fresh' ? '' : 'infr.exe', owned: mode === 'running' }
        : { supported: true, confirmedReleaseTag: true, managed: mode === 'running', latest: { tag: 'release-1.0.0' }, updateAvailable: true }));
    } });
    await f.center.checkEngine(false);
    assert(calls[0][0].includes('force=1')); assert(calls.every(row => row[1] === 'GET'));
    await assert.rejects(f.center.engineInstall(false), /confirmation/);
    if (mode === 'fresh') { await assert.rejects(f.center.engineInstall(true, 'release-0.9.0'), /version changed/); await f.center.engineInstall(true, 'release-1.0.0'); assert(calls.some(row => row[0].endsWith('/install') && row[1] === 'POST')); }
    else { await assert.rejects(f.center.engineInstall(true)); assert(calls.every(row => row[1] === 'GET')); }
  }
});

test('real pinned pnpm installer prepares offline runtime archives without lifecycle execution', async () => {
  const f = await fixture({ install: profile.runInstaller });
  const runtime = path.join(f.root, 'harness/runtime/update-tools');
  await fs.mkdir(path.dirname(runtime), { recursive: true });
  await fs.symlink(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, runtime, 'junction');
  await publish(f); await f.center.check(true);
  const result = await f.center.prepare(item.id, '1.1.0', true);
  assert.equal(result.operation.phase, 'ready', result.operation.error);
  await f.center.apply(); await f.center.healthy();
});

test('skipping an exact version is persistent, independent per component, and never disables checks', async () => {
  const f = await fixture(); await publish(f);
  await f.center.skip(item.id, '1.1.0'); await f.center.skip('agent', 'release-0.1.0-preview.4');
  await f.center.setSettings({ autoCheck: false, preview: true });
  const settings = await profile.json(f.center.settingsPath);
  assert.equal(settings.skipped[item.id], '1.1.0'); assert.equal(settings.skipped.agent, 'release-0.1.0-preview.4');
  const checked = await f.center.check(true);
  assert.equal(checked.plugins[0].updateAvailable, true);
  await assert.rejects(f.center.skip('../outside', '1.0.0'));
  await assert.rejects(f.center.skip('agent', 'bad/version'));
});

test('moving an installation preserves prepared updates and the applied rollback journal', async () => {
  for (const phase of ['ready', 'applied']) {
    const f = await fixture(); await publish(f); await f.center.check(true);
    const before = await profile.identity(f.directory, catalog);
    await f.center.prepare(item.id, '1.1.0', true);
    if (phase === 'applied') { await f.center.apply(); await f.center.healthy(); }
    const moved = f.root + '-moved'; await fs.rename(f.root, moved);
    const center = await new CapabilityCenter(moved, f.center.options).init();
    if (phase === 'ready') { await center.apply(); await center.healthy(); }
    await center.rollback(true); await center.apply();
    assert.deepEqual(await profile.identity(center.profile, catalog), before);
    await center.dispose();
  }
});

test('a stalled direct connection falls back to the configured proxy without aborting its release request', async () => {
  const originalFetch = globalThis.fetch, previousProxy = process.env.HTTPS_PROXY;
  let calls = 0;
  process.env.HTTPS_PROXY = 'http://127.0.0.1:1';
  globalThis.fetch = async (_url, init) => {
    if (++calls === 1) return new Promise((_, reject) => init.signal.addEventListener('abort', () => reject(init.signal.reason), { once: true }));
    assert(init.dispatcher); assert.equal(init.signal.aborted, false); return new Response('[]');
  };
  const transport = require('./transport.cjs').createTransport(tool);
  try {
    const controller = new AbortController();
    assert.equal(await (await transport.fetch('https://api.github.com/repos/Headmaster218/test-plugin/releases', { signal: controller.signal })).text(), '[]');
    assert.equal(calls, 2);
  } finally {
    await transport.dispose(); globalThis.fetch = originalFetch;
    if (previousProxy === undefined) delete process.env.HTTPS_PROXY; else process.env.HTTPS_PROXY = previousProxy;
  }
});

test('a successful direct connection keeps the response body alive beyond its connection timeout', async () => {
  const originalFetch = globalThis.fetch, previousProxy = process.env.HTTPS_PROXY;
  let directSignal;
  process.env.HTTPS_PROXY = 'http://127.0.0.1:1';
  globalThis.fetch = async (_url, init) => { directSignal = init.signal; return new Response('package'); };
  const transport = require('./transport.cjs').createTransport(tool);
  try {
    const controller = new AbortController();
    const response = await transport.fetch('https://github.com/Headmaster218/test-plugin/releases', { signal: controller.signal });
    await new Promise(resolve => setTimeout(resolve, 5100));
    assert.equal(directSignal.aborted, false);
    assert.equal(await response.text(), 'package');
    controller.abort();
    assert.equal(directSignal.aborted, true);
  } finally {
    await transport.dispose(); globalThis.fetch = originalFetch;
    if (previousProxy === undefined) delete process.env.HTTPS_PROXY; else process.env.HTTPS_PROXY = previousProxy;
  }
});
