const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { createRequire } = require('node:module');
const { verifyAgentProfile } = require('./Verify-AgentProfile.cjs');
const root = path.resolve(__dirname, '../..');
const realModules = path.join(process.env.MOE4ALL_TEST_PROFILE || path.join(root, 'harness/agent-state/home/profiles/web'), 'node_modules');
const { c } = createRequire(path.join(realModules, 'dsh-llm-moe4all/package.json'))('tar');
const names = Object.keys(JSON.parse(fs.readFileSync(path.join(root, 'harness/distribution/profile/package.json'), 'utf8')).dependencies);

async function fixture() {
  const output = process.env.MOE4ALL_PROFILE_TEST_ROOT || path.join(root, 'artifacts/profile-provenance-tests');
  fs.mkdirSync(output, { recursive: true });
  const harness = fs.mkdtempSync(path.join(output, 'case-'));
  const profile = path.join(harness, 'agent-state/home/profiles/web');
  const packs = path.join(harness, 'runtime/plugin-packs');
  fs.mkdirSync(packs, { recursive: true });
  fs.mkdirSync(path.join(profile, 'node_modules'), { recursive: true });
  for (const name of ['tar', 'js-yaml']) {
    fs.symlinkSync(fs.realpathSync(path.join(realModules, name)), path.join(profile, 'node_modules', name), 'junction');
  }
  const dependencies = {};
  for (const name of names) {
    const source = path.join(harness, 'sources', name);
    const installed = path.join(profile, 'node_modules', name);
    fs.mkdirSync(path.join(source, 'package/lib'), { recursive: true });
    fs.mkdirSync(path.join(installed, 'lib'), { recursive: true });
    const manifest = JSON.stringify({ name, version: '0.2.2' });
    for (const dir of [path.join(source, 'package'), installed]) {
      fs.writeFileSync(path.join(dir, 'package.json'), manifest);
      fs.writeFileSync(path.join(dir, 'lib/client.js'), 'current committed runtime');
    }
    fs.cpSync(path.join(source, 'package'), path.join(harness, 'plugins', name), { recursive: true });
    const temporary = path.join(source, 'package.tgz');
    await c({ file: temporary, gzip: true, portable: true, noMtime: true, cwd: source }, ['package']);
    const bytes = fs.readFileSync(temporary);
    const digest = crypto.createHash('sha256').update(bytes).digest('hex').slice(0, 16);
    const filename = `${name}-${digest}.tgz`;
    fs.writeFileSync(path.join(packs, filename), bytes);
    dependencies[name] = `file:../../../../runtime/plugin-packs/${filename}`;
  }
  fs.mkdirSync(path.join(harness, 'distribution/profile'), { recursive: true });
  fs.writeFileSync(path.join(profile, 'package.json'), JSON.stringify({ dependencies }));
  fs.writeFileSync(path.join(harness, 'distribution/profile/package.json'), JSON.stringify({ dependencies }));
  fs.writeFileSync(path.join(harness, 'distribution/profile/pnpm-lock.yaml'), JSON.stringify({
    importers: { '.': { dependencies: Object.fromEntries(Object.entries(dependencies).map(([name, specifier]) => [name, { specifier }])) } },
  }));
  return { harness, profile, dependencies };
}

test('bundled profile versions match the pinned plugin source manifests', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'harness/distribution/profile/package.json'), 'utf8'));
  for (const [name, version] of Object.entries(manifest.dependencies)) {
    const source = JSON.parse(fs.readFileSync(path.join(root, 'harness/plugins', name, 'package.json'), 'utf8'));
    assert.equal(version, source.version, `${name} profile version drifted from its pinned source`);
  }
});

test('checks every published file in all eight bundled plugin archives', async () => {
  const f = await fixture();
  assert.deepEqual(await verifyAgentProfile(f.harness, f.profile), { packages: 8, files: 16 });
});

test('rejects stale materialized code even when the plugin version is unchanged', async () => {
  const f = await fixture();
  fs.writeFileSync(path.join(f.profile, 'node_modules/dsh-llm-moe4all/lib/client.js'), 'old cached runtime');
  await assert.rejects(verifyAgentProfile(f.harness, f.profile), /Stale installed plugin file/u);
});

test('rejects a profile that selects an older archive instead of the canonical lock', async () => {
  const f = await fixture();
  f.dependencies['dsh-llm-moe4all'] = 'file:../../../../runtime/plugin-packs/dsh-llm-moe4all.tgz';
  fs.writeFileSync(path.join(f.profile, 'package.json'), JSON.stringify({ dependencies: f.dependencies }));
  await assert.rejects(verifyAgentProfile(f.harness, f.profile), /canonical lock/u);
});

test('rejects an old archive and installed tree even when their hashes agree with each other', async () => {
  const f = await fixture();
  fs.writeFileSync(path.join(f.harness, 'plugins/dsh-llm-moe4all/lib/client.js'), 'new main runtime');
  await assert.rejects(verifyAgentProfile(f.harness, f.profile), /does not match committed source/u);
});
