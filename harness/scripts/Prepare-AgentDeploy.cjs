const fs = require('node:fs');
const path = require('node:path');
const { createRequire } = require('node:module');
const { pathToFileURL } = require('node:url');

const source = path.resolve(process.argv[2]);
const yaml = createRequire(path.join(source, 'package.json'))('js-yaml');
const workspace = path.join(source, 'pnpm-workspace.yaml');
const config = yaml.load(fs.readFileSync(workspace, 'utf8'));
const relative = 'packages/subprocess/subprocess-local';
const name = '@deepseek-ai/dsh-subprocess-local';
const manifest = JSON.parse(fs.readFileSync(path.join(source, relative, 'package.json'), 'utf8'));
if (manifest.name !== name || config.allowBuilds?.[`${name}@file:${relative}`] !== true) {
  throw new Error('The pinned workspace did not approve its subprocess helper build.');
}
// Deploy rewrites approved workspace file dependencies to absolute file URLs.
config.allowBuilds[`${name}@${pathToFileURL(path.join(source, relative)).href}`] = true;
fs.writeFileSync(workspace, yaml.dump(config, { lineWidth: -1 }));

