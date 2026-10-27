'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { createRequire } = require('node:module');
const { test } = require('node:test');
const { prepareProfile } = require('../../scripts/Prepare-BundledRuntime.cjs');
const root = path.resolve(__dirname, '../../..');
const sourceRequire = createRequire(path.join(root, 'harness/dsh/package.json'));
const yaml = sourceRequire('js-yaml');
const { entryListSchema: schema } = sourceRequire(path.join(root, 'harness/dsh/vendor/include'));

function fixture(t) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'moe4all-profile-'));
  t.after(() => fs.rmSync(path.toNamespacedPath(base), { recursive: true, force: true }));
  const dsh = path.join(base, 'harness/runtime/dsh');
  const profile = path.join(base, 'data/home/profiles/web');
  fs.mkdirSync(path.join(profile, 'node_modules'), { recursive: true });
  const write = (file, value) => { fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, value); };
  write(path.join(dsh, 'package.json'), '{}');
  fs.mkdirSync(path.join(dsh, 'node_modules/.pnpm'), { recursive: true });
  for (const name of ['cosmokit', 'dsh-base', 'dsh-scope', 'dsh-timeout', 'dsh-web-app', 'schemastery'])
    write(path.join(dsh, '.moe4all-workspace', name, 'package.json'), JSON.stringify({ name: `@deepseek-ai/${name}` }));
  write(path.join(dsh, 'node_modules/commander/index.js'), 'module.exports = {};');
  for (const [name, target] of [
    ['js-yaml', path.dirname(sourceRequire.resolve('js-yaml/package.json'))],
    ['@deepseek-ai/cordis-plugin-include', path.join(root, 'harness/dsh/vendor/include')],
  ]) {
    const alias = path.join(dsh, 'node_modules', name);
    fs.mkdirSync(path.dirname(alias), { recursive: true });
    fs.symlinkSync(target, alias, process.platform === 'win32' ? 'junction' : 'dir');
  }
  const file = path.join(profile, 'cordis.patch.yml');
  write(file, '- id: web-runtime\n  inject: [webStartup]\n  config:\n    openBrowser: !!js ctx.webStartup.openBrowser\n    trustedHosts: !!js ctx.webStartup.trustedHosts\n    inert: !!js globalThis.__moe4allPreparationExecuted = true\n- id: moe4all-engine\n  config:\n    endpoint: http://127.0.0.1:1234/v1\n');
  return { base, dsh, file };
}

test('profile relocation preserves Loader expressions without executing or flattening them', t => {
  const f = fixture(t);
  const before = yaml.load(fs.readFileSync(f.file, 'utf8'), { schema });
  prepareProfile(f.base, undefined, path.join(f.base, 'harness'), path.join(f.base, 'data'), f.dsh);
  const text = fs.readFileSync(f.file, 'utf8');
  const after = yaml.load(text, { schema });
  assert.deepEqual(after[0], before[0]);
  assert.equal(after[1].config.endpoint, 'http://127.0.0.1:8080/v1');
  assert.equal(globalThis.__moe4allPreparationExecuted, undefined);
  prepareProfile(f.base, undefined, path.join(f.base, 'harness'), path.join(f.base, 'data'), f.dsh);
  assert.equal(fs.readFileSync(f.file, 'utf8'), text);
});

test('unknown profile tags still fail without rewriting the document', t => {
  const f = fixture(t);
  fs.writeFileSync(f.file, '- id: unsafe\n  config: !unknown value\n');
  const before = fs.readFileSync(f.file, 'utf8');
  assert.throws(() => prepareProfile(f.base, undefined, path.join(f.base, 'harness'), path.join(f.base, 'data'), f.dsh), /unknown tag/);
  assert.equal(fs.readFileSync(f.file, 'utf8'), before);
});
