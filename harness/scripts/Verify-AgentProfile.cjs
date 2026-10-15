const { createHash } = require('node:crypto');
const { createReadStream, existsSync } = require('node:fs');
const { execFileSync } = require('node:child_process');
const { readFile, stat } = require('node:fs/promises');
const { createRequire } = require('node:module');
const path = require('node:path');

async function fileHash(file) {
  const hash = createHash('sha256');
  for await (const chunk of createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

async function verifyAgentProfile(harness, profile, { locked = true } = {}) {
  const manifest = JSON.parse(await readFile(path.join(profile, 'package.json'), 'utf8'));
  const dependencies = createRequire(path.join(profile, 'node_modules', 'dsh-llm-moe4all', 'package.json'));
  const { t } = dependencies('tar');
  const expected = JSON.parse(await readFile(path.join(harness, 'distribution', 'profile', 'package.json'), 'utf8'));
  if (locked && JSON.stringify(Object.keys(manifest.dependencies).sort()) !== JSON.stringify(Object.keys(expected.dependencies).sort())) {
    throw new Error('Build profiles must contain exactly the bundled plugins.');
  }
  const lock = locked ? dependencies('js-yaml').load(await readFile(path.join(harness, 'distribution', 'profile', 'pnpm-lock.yaml'), 'utf8')) : null;
  let files = 0;
  let packages = 0;
  for (const name of Object.keys(expected.dependencies)) {
    const spec = manifest.dependencies[name];
    if (typeof spec !== 'string' || !spec.startsWith('file:')) throw new Error(`Plugin is not archive-pinned: ${name}`);
    if (locked && spec.replaceAll('\\', '/') !== lock.importers['.'].dependencies[name]?.specifier.replaceAll('\\', '/')) {
      throw new Error(`Installed plugin does not match the canonical lock: ${name}`);
    }
    const archiveName = path.basename(spec.slice(5).replaceAll('\\', '/'));
    if (!new RegExp(`^${name}-[a-f0-9]{16}\\.tgz$`).test(archiveName)) {
      throw new Error(`Plugin archive is not content-addressed: ${name}`);
    }
    const archive = path.join(harness, 'runtime', 'plugin-packs', archiveName);
    if ((await fileHash(archive)).slice(0, 16) !== archiveName.slice(name.length + 1, -4)) {
      throw new Error(`Plugin archive digest mismatch: ${name}`);
    }
    const installed = path.join(profile, 'node_modules', name);
    const source = path.join(harness, 'plugins', name);
    const tracked = locked && existsSync(path.join(source, '.git')) ? new Map(
      execFileSync('git', ['-C', source, 'ls-tree', '-r', '-z', 'HEAD'], { encoding: 'utf8' })
        .split('\0').filter(Boolean).map(record => {
          const tab = record.indexOf('\t');
          return [record.slice(tab + 1), record.slice(0, tab).split(' ')[2]];
        }),
    ) : null;
    const pending = [];
    await t({ file: archive, onReadEntry(entry) {
      if (entry.type === 'Directory') return;
      const relative = entry.path.slice('package/'.length);
      if (entry.type !== 'File' || !entry.path.startsWith('package/') || relative.includes('\\')
        || relative.split('/').includes('..') || path.isAbsolute(relative)) {
        throw new Error(`Unsafe plugin package entry: ${entry.path}`);
      }
      const hash = createHash('sha256');
      const gitHash = createHash('sha1').update(`blob ${entry.size}\0`);
      entry.on('data', chunk => { hash.update(chunk); gitHash.update(chunk); });
      pending.push(new Promise((resolve, reject) => {
        entry.on('error', reject);
        entry.on('end', async () => {
          try {
            const expectedHash = hash.digest('hex');
            if (tracked ? tracked.get(relative) !== gitHash.digest('hex')
              : await fileHash(path.join(source, relative)) !== expectedHash) {
              throw new Error(`Plugin archive does not match committed source: ${name}/${relative}`);
            }
            const file = path.join(installed, relative);
            if (!(await stat(file)).isFile() || await fileHash(file) !== expectedHash) {
              throw new Error(`Stale installed plugin file: ${name}/${relative}`);
            }
            files++;
            resolve();
          } catch (error) { reject(error); }
        });
      }).catch(error => error));
    } });
    const error = (await Promise.all(pending)).find(result => result instanceof Error);
    if (error) throw error;
    packages++;
  }
  if (packages !== 8) throw new Error(`Expected 8 bundled plugin archives, found ${packages}`);
  return { packages, files };
}

module.exports = { verifyAgentProfile };
if (require.main === module) verifyAgentProfile(path.resolve(process.argv[2]), path.resolve(process.argv[3]), { locked: !process.argv.includes('--development') })
  .then(result => console.log(`Plugin runtime provenance passed: ${result.packages} archives, ${result.files} files.`))
  .catch(error => { console.error(error.message); process.exitCode = 1; });
