const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createRequire } = require('node:module');
const { CapabilityCenter } = require('./center.cjs');
const profile = require('./profile.cjs');
const release = require('./release.cjs');

test('eight-plugin built profile stages and cold-switches through real pnpm, retaining the HF stub and rollback',
  { skip: !process.env.MOE4ALL_CAPABILITY_REAL_PROFILE }, async () => {
    assert(process.env.MOE4ALL_CAPABILITY_TEST_ROOT && process.env.MOE4ALL_CAPABILITY_TEST_TOOLS);
    const tool = createRequire(path.join(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, 'package.json'));
    const root = await fs.mkdtemp(path.join(process.env.MOE4ALL_CAPABILITY_TEST_ROOT, 'real-profile-'));
    const target = path.join(root, 'data/home/profiles/web');
    await fs.cp(process.env.MOE4ALL_CAPABILITY_REAL_PROFILE, target, { recursive: true, verbatimSymlinks: true });
    const linkedHost = path.join(target, 'node_modules/@deepseek-ai');
    profile.contained(root, linkedHost);
    await fs.rm(linkedHost, { recursive: true, force: true });
    await profile.writeJson(path.join(root, 'harness/distribution/agent-build.json'), { repository: 'Headmaster218/MoE4All-Agent', version: '0.1.0-preview.4' });
    const tools = path.join(root, 'harness/runtime/update-tools');
    await fs.mkdir(path.dirname(tools), { recursive: true });
    await fs.symlink(process.env.MOE4ALL_CAPABILITY_TEST_TOOLS, tools, 'junction');
    const catalog = require('./catalog.json').plugins;
    const item = catalog.find(row => row.id === 'dsh-mneme-moe4all');
    const base = await profile.identity(target, catalog);
    const source = path.join(root, 'fixture-release');
    await fs.cp(path.join(target, 'node_modules', item.id), source, { recursive: true });
    const pkg = await profile.json(path.join(source, 'package.json'));
    pkg.version = '0.7.30-moe4all.1'; await profile.writeJson(path.join(source, 'package.json'), pkg);
    const archive = path.join(root, item.id + '.tgz');
    await profile.installedArchive(source, archive, tool);
    const payload = await fs.readFile(archive), tag = 'release-' + pkg.version;
    const prefix = `https://github.com/${item.repository}/releases/download/${tag}/`;
    const metadata = Buffer.from(JSON.stringify({ schema: 'moe4all/plugin-release/v1', name: item.id, version: pkg.version,
      repository: item.repository, tag, commit: 'a'.repeat(40), archive: { name: item.id + '.tgz', bytes: payload.length, sha256: release.sha256(payload) } }));
    const peers = Object.fromEntries(Object.keys(pkg.peerDependencies ?? {}).map(name => [name,
      name === '@deepseek-ai/cordis' ? '4.0.1' : name === '@deepseek-ai/schemastery' ? '3.18.1' : '0.1.1-rc.2']));
    const center = await new CapabilityCenter(root, { tool, cold: true, host: { dshVersion: '0.1.1-rc.2', peers },
      fetch: async url => {
        if (url.startsWith('https://api.github.com/')) return Response.json(url.includes(item.repository + '/') ? [{ tag_name: tag, assets:
          [item.id + '.tgz', 'SHA256SUMS.txt', 'plugin-release.json'].map(name => ({ name, size: name.endsWith('.tgz') ? payload.length : 100, browser_download_url: prefix + name })) }] : []);
        if (url.endsWith('.tgz')) return new Response(payload);
        if (url.endsWith('plugin-release.json')) return new Response(metadata);
        if (url.endsWith('SHA256SUMS.txt')) return new Response(`${release.sha256(payload)}  ${item.id}.tgz\n${release.sha256(metadata)}  plugin-release.json\n`);
        assert.fail('Unexpected request ' + url);
      }, engineFetch: async () => new Response('{}'),
    }).init();
    await center.check(true);
    const result = await center.prepare(item.id, pkg.version, true);
    assert.equal(result.operation.phase, 'ready', result.operation.error);
    assert.deepEqual(await profile.identity(target, catalog), base);
    await center.apply();
    assert.equal((await profile.identity(target, catalog)).versions[item.id], pkg.version);
    const lock = tool('js-yaml').load(await fs.readFile(path.join(target, 'pnpm-lock.yaml'), 'utf8'));
    const stub = path.resolve(target, lock.overrides['@huggingface/transformers'].slice(5));
    assert.equal(stub, path.join(target, 'stubs/hf-transformers-stub'));
    await fs.access(path.join(stub, 'package.json'));
    for (const entry of Object.values(lock.importers['.'].dependencies)) {
      assert.equal(path.resolve(target, entry.specifier.slice(5)), path.resolve(target, entry.version.split('(')[0].slice(5)), 'The installed lock pin must match the portable manifest pin');
    }
    await center.healthy(); await center.rollback(true); await center.apply();
    assert.deepEqual(await profile.identity(target, catalog), base);
    await center.dispose();
  });
