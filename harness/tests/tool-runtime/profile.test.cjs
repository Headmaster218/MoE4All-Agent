const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const test = require('node:test');

const root = path.resolve(__dirname, '../../..');
const yaml = createRequire(path.join(root, 'harness/dsh/package.json'))('js-yaml');
const schema = yaml.DEFAULT_SCHEMA.extend(new yaml.Type('tag:yaml.org,2002:js', {
  kind: 'scalar', construct: expression => ({ expression }),
}));
const rows = yaml.load(fs.readFileSync(path.join(root, 'harness/distribution/profile/cordis.patch.yml'), 'utf8'), { schema });
const runtime = rows.find(row => row.id === 'web-runtime');

test('packaged tasks keep GUI orientation without Harness development instructions', () => {
  assert.ok(runtime);
  assert.equal(runtime.config.surfaceContext, true);
  assert.equal(runtime.config.developmentContext, false);
  assert.equal(runtime.config.printUrl, true);
});

test('portable runtime preserves startup-owned browser and trusted-host settings', () => {
  assert.deepEqual(runtime.inject, ['webStartup']);
  assert.deepEqual(runtime.config.openBrowser, { expression: 'ctx.webStartup.openBrowser' });
  assert.deepEqual(runtime.config.trustedHosts, { expression: 'ctx.webStartup.trustedHosts' });
  assert.equal(rows.filter(row => row.id === 'web-runtime').length, 1);
});
