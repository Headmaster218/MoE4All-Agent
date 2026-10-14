const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');

const source = path.resolve(process.argv[2]);
const deploy = path.resolve(process.argv[3]);
const yaml = createRequire(path.join(source, 'package.json'))('js-yaml');
const original = yaml.load(fs.readFileSync(path.join(source, 'pnpm-lock.yaml'), 'utf8'));
const deployed = yaml.load(fs.readFileSync(path.join(deploy, 'pnpm-lock.yaml'), 'utf8'));
let checked = 0;
for (const [name, value] of Object.entries(deployed.packages)) {
  if (name.includes('@file:') || name.includes('@link:')) continue;
  assert.ok(original.packages[name], `Deployment added an unlocked registry package: ${name}`);
  assert.deepEqual(value.resolution, original.packages[name].resolution, `Deployment changed integrity: ${name}`);
  checked++;
}
assert.ok(checked > 0, 'Deployment lock is empty');
console.log(`Frozen deployment provenance passed: ${checked} registry package resolutions.`);
