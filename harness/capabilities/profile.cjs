const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { existsSync, globSync, lstatSync, readdirSync } = require('node:fs');
const { spawn } = require('node:child_process');
const { createHash } = require('node:crypto');
const path = require('node:path');

const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const json = async file => JSON.parse(await fs.readFile(file, 'utf8'));

function contained(root, file) {
  const base = path.resolve(root), target = path.resolve(file);
  const relative = path.relative(base, target);
  assert(relative && relative !== '..' && !relative.startsWith('..' + path.sep) && !path.isAbsolute(relative), 'Path leaves Agent storage');
  return target;
}

async function noLinks(root, target) {
  contained(root, target);
  for (let part = target; part !== path.resolve(root); part = path.dirname(part)) {
    try { assert(!(await fs.lstat(part)).isSymbolicLink(), 'Agent storage may not traverse links'); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
}

async function writeJson(file, value) {
  await fs.mkdir(path.dirname(file), { recursive: true });
  const temporary = file + '.' + require('node:crypto').randomUUID() + '.tmp';
  await fs.writeFile(temporary, JSON.stringify(value, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
  await fs.rename(temporary, file);
}

async function identity(profile, catalog) {
  const packageBytes = await fs.readFile(path.join(profile, 'package.json'));
  const lockBytes = await fs.readFile(path.join(profile, 'pnpm-lock.yaml'));
  const manifest = JSON.parse(packageBytes);
  assert.equal(manifest.name, 'moe4all-agent-profile-web', 'Only the Agent-owned profile can be updated');
  const versions = {};
  for (const item of catalog) {
    const installed = await json(path.join(profile, 'node_modules', item.id, 'package.json'));
    assert.equal(installed.name, item.id);
    versions[item.id] = installed.version;
  }
  return { packageHash: hash(packageBytes), lockHash: hash(lockBytes),
    workspaceHash: hash(await fs.readFile(path.join(profile, 'pnpm-workspace.yaml'))), versions };
}

async function installedArchive(directory, destination, tool) {
  const pkg = await json(path.join(directory, 'package.json'));
  assert(Array.isArray(pkg.files) && pkg.files.length, 'Installed plugin must declare its packaged files');
  const files = new Set(['package.json']);
  function visit(relative) {
    assert(!relative.split(/[\\/]/u).includes('..') && !path.isAbsolute(relative), 'Unsafe installed plugin file');
    if (relative.split(/[\\/]/u).some(part => part === 'node_modules' || part.startsWith('.'))) return;
    const file = path.join(directory, relative);
    const stat = lstatSync(file);
    assert(!stat.isSymbolicLink(), 'Installed plugin payload contains a link');
    if (stat.isDirectory()) for (const child of readdirSync(file)) visit(path.join(relative, child));
    else { assert(stat.isFile()); files.add(relative); }
  }
  for (const pattern of pkg.files) {
    assert(typeof pattern === 'string' && !pattern.startsWith('!') && !pattern.includes('..') && !path.isAbsolute(pattern));
    for (const file of globSync(pattern, { cwd: directory })) visit(file);
  }
  for (const file of readdirSync(directory)) if (/^(?:LICENSE(?:\..*)?|README(?:\..*)?)$/iu.test(file)) visit(file);
  await tool('tar').c({ cwd: directory, file: destination, prefix: 'package/', gzip: true, portable: true, noMtime: true }, [...files].sort());
}

async function runInstaller(node, cli, directory, store, signal, progress) {
  await new Promise((resolve, reject) => {
    const env = Object.fromEntries(Object.entries(process.env).filter(([key]) => !/(?:KEY|SECRET|TOKEN|PASSWORD)/iu.test(key)));
    const child = spawn(node, [cli, 'install', '--ignore-scripts', '--no-frozen-lockfile', '--prefer-offline', '--store-dir', store,
      '--fetch-retries=1', '--fetch-timeout=30000'], { cwd: directory, env: { ...env, CI: 'true' }, windowsHide: true, shell: false,
      signal, stdio: ['ignore', 'pipe', 'pipe'] });
    let tail = '';
    const feed = chunk => { tail = (tail + chunk.toString()).slice(-3000); progress('prepare', 0, 0); };
    child.stdout.on('data', feed); child.stderr.on('data', feed);
    child.once('error', reject);
    child.once('close', code => code === 0 ? resolve() : reject(new Error('Plugin dependency installation failed: ' + tail)));
  });
}

async function stageProfile(options) {
  const { root, profile, catalog, directory, item, archive, tool, signal, progress } = options;
  const base = await identity(profile, catalog);
  const manifest = await json(path.join(profile, 'package.json'));
  const staged = path.join(directory, 'profile');
  await fs.mkdir(staged, { recursive: true });
  const yaml = tool('js-yaml');
  const workspace = yaml.load(await fs.readFile(path.join(profile, 'pnpm-workspace.yaml'), 'utf8'));
  assert(workspace.nodeLinker === 'hoisted' && workspace.autoInstallPeers === false, 'Unsupported Agent package layout');
  const before = structuredClone(manifest);
  for (const entry of catalog) {
    signal.throwIfAborted();
    const installed = path.join(profile, 'node_modules', entry.id);
    const actual = await fs.realpath(installed);
    contained(await fs.realpath(path.join(profile, 'node_modules')), actual);
    const baseline = path.join(directory, entry.id + '-before.tgz');
    await installedArchive(actual, baseline, tool);
    manifest.dependencies[entry.id] = 'file:' + (entry.id === item.id ? archive : baseline).replaceAll('\\', '/');
  }
  for (const [name, spec] of Object.entries(manifest.dependencies)) {
    if (catalog.some(entry => entry.id === name) || typeof spec !== 'string') continue;
    if (/^(file|link):/u.test(spec)) manifest.dependencies[name] = spec.slice(0, spec.indexOf(':') + 1) + path.resolve(profile, spec.slice(spec.indexOf(':') + 1)).replaceAll('\\', '/');
  }
  const anchor = value => {
    if (typeof value === 'string' && value.startsWith('link:')) return 'link:' + path.resolve(profile, value.slice(5)).replaceAll('\\', '/');
    if (Array.isArray(value)) return value.map(anchor);
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, anchor(v)]));
    return value;
  };
  const stagedWorkspace = anchor(workspace);
  stagedWorkspace.ignoreScripts = true;
  if (manifest.pnpm) manifest.pnpm = anchor(manifest.pnpm);
  await writeJson(path.join(staged, 'package.json'), manifest);
  await fs.copyFile(path.join(profile, 'pnpm-lock.yaml'), path.join(staged, 'pnpm-lock.yaml'));
  await fs.writeFile(path.join(staged, 'pnpm-workspace.yaml'), yaml.dump(stagedWorkspace, { lineWidth: -1 }));
  const cli = path.join(root, 'harness/runtime/update-tools/node_modules/pnpm/bin/pnpm.cjs');
  await (options.install ?? runInstaller)(process.execPath, cli, staged, path.join(root, 'cache/node/pnpm'), signal, progress);
  const scope = path.join(profile, 'node_modules/@deepseek-ai');
  if (existsSync(scope)) {
    await fs.mkdir(path.join(staged, 'node_modules/@deepseek-ai'), { recursive: true });
    for (const name of await fs.readdir(scope)) {
      const original = path.join(scope, name);
      if (!(await fs.lstat(original)).isSymbolicLink()) continue;
      const target = await fs.realpath(original);
      contained(path.join(root, 'harness/runtime/dsh'), target);
      const link = path.join(staged, 'node_modules/@deepseek-ai', name);
      if (!existsSync(link)) await fs.symlink(target, link, 'junction');
    }
  }
  for (const entry of catalog) {
    const pkg = await json(path.join(staged, 'node_modules', entry.id, 'package.json'));
    assert.equal(pkg.name, entry.id);
    assert.equal(pkg.version, entry.id === item.id ? options.version : base.versions[entry.id], 'An unrelated plugin version changed');
  }
  assert.deepEqual(await identity(profile, catalog), base, 'The live profile changed while preparing the update');
  const after = await json(path.join(staged, 'package.json'));
  const checked = { ...after, dependencies: before.dependencies };
  assert.deepEqual(after.pnpm, anchor(before.pnpm), 'Plugin installation changed pnpm settings');
  if (before.pnpm) checked.pnpm = before.pnpm;
  assert.deepEqual(checked, before, 'Plugin installation changed profile settings');
  // Install in staging with absolute pins; persist portable pins relative to the live profile.
  const portable = value => {
    if (typeof value === 'string') {
      for (const prefix of ['file:', 'link:']) {
        let offset = value.indexOf(prefix);
        if (offset < 0) continue;
        const start = offset + prefix.length;
        const suffix = value.slice(start);
        const end = suffix.indexOf('(');
        const target = end < 0 ? suffix : suffix.slice(0, end);
        const resolved = path.resolve(staged, target);
        contained(root, resolved);
        return value.slice(0, start) + path.relative(profile, resolved).replaceAll('\\', '/') + (end < 0 ? '' : suffix.slice(end));
      }
      return value;
    }
    if (Array.isArray(value)) return value.map(portable);
    if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, child]) => [portable(key), portable(child)]));
    return value;
  };
  const persisted = portable(after);
  if (before.pnpm) persisted.pnpm = before.pnpm;
  await writeJson(path.join(staged, 'package.json'), persisted);
  await fs.writeFile(path.join(staged, 'pnpm-lock.yaml'), yaml.dump(portable(yaml.load(await fs.readFile(path.join(staged, 'pnpm-lock.yaml'), 'utf8'))), { lineWidth: -1 }));
  await fs.copyFile(path.join(profile, 'pnpm-workspace.yaml'), path.join(staged, 'pnpm-workspace.yaml'));
  return { base, staged, manifest: after, stagedWorkspace };
}

// The journal survives a crash between directory renames; recovery always runs before the Agent starts.
async function activate(profile, transaction, journalPath, fault = async () => {}) {
  const parts = ['node_modules', 'package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml'];
  await fs.mkdir(transaction.backup, { recursive: true });
  transaction.phase = 'applying';
  transaction.moved = [];
  transaction.activated = [];
  await writeJson(journalPath, transaction);
  try {
    for (const name of parts) {
      await fs.rename(path.join(profile, name), path.join(transaction.backup, name));
      transaction.moved.push(name); await writeJson(journalPath, transaction);
      await fault('backed-up', name);
      await fs.rename(path.join(transaction.staged, name), path.join(profile, name));
      transaction.activated.push(name); await writeJson(journalPath, transaction);
      await fault('activated', name);
    }
    transaction.phase = 'awaiting-health';
    await writeJson(journalPath, transaction);
  } catch (error) {
    await restore(profile, transaction, journalPath);
    throw error;
  }
}

async function restore(profile, transaction, journalPath) {
  // Infer on-disk moves too: a process may have died before the next journal write.
  for (const name of ['node_modules', 'package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml']) {
    const old = path.join(transaction.backup, name);
    if (!existsSync(old)) continue;
    const current = path.join(profile, name);
    if (existsSync(current)) {
      const failed = path.join(transaction.directory, 'failed-' + name);
      assert(!existsSync(failed), 'Both recovery destinations exist; refusing to discard files');
      await fs.rename(current, failed);
    }
    await fs.rename(old, current);
  }
  transaction.phase = 'rolled-back';
  await writeJson(journalPath, transaction);
}

module.exports = { contained, noLinks, writeJson, identity, installedArchive, runInstaller, stageProfile, activate, restore, hash, json };
