const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const { existsSync } = require('node:fs');
const { createRequire } = require('node:module');
const { randomUUID } = require('node:crypto');
const path = require('node:path');
const net = require('node:net');
const release = require('./release.cjs');
const profileTools = require('./profile.cjs');
const { json, writeJson, contained, noLinks, identity, hash } = profileTools;

function errorText(error) {
  return String(error?.message ?? error).replace(/(https?:\/\/)[^\s/@]+:[^\s/@]+@/giu, '$1<redacted>@')
    .replace(/([?&](?:token|key|password)=)[^&\s]+/giu, '$1<redacted>').slice(-1500);
}

async function treeHash(root, progress = () => {}) {
  const { createHash } = require('node:crypto');
  const digest = createHash('sha256');
  let count = 0, bytes = 0;
  async function visit(dir, relative = '') {
    for (const entry of (await fs.readdir(dir)).sort()) {
      const name = path.join(relative, entry), file = path.join(dir, entry), stat = await fs.lstat(file);
      assert(++count < 100000 && (bytes += stat.isFile() ? stat.size : 0) < 1024 ** 3, 'Plugin staging size limit exceeded');
      digest.update(name.replaceAll('\\', '/') + '\0');
      if (stat.isSymbolicLink()) digest.update('link\0' + await fs.readlink(file));
      else if (stat.isDirectory()) { digest.update('dir\0'); await visit(file, name); }
      else { assert(stat.isFile()); digest.update(await fs.readFile(file)); }
      digest.update('\0');
      progress('validate', count, 0);
    }
  }
  await visit(root);
  return digest.digest('hex');
}

async function packageInfo(requireFrom, name) {
  let entry;
  try { return await json(requireFrom.resolve(name + '/package.json')); }
  catch { entry = requireFrom.resolve(name); }
  for (let dir = path.dirname(entry); path.dirname(dir) !== dir; dir = path.dirname(dir)) {
    if (!existsSync(path.join(dir, 'package.json'))) continue;
    const pkg = await json(path.join(dir, 'package.json'));
    if (pkg.name === name) return pkg;
  }
  throw new Error('Cannot resolve host package ' + name);
}

class CapabilityCenter {
  constructor(root, options = {}) {
    this.root = path.resolve(root);
    this.profile = path.join(this.root, 'data/home/profiles/web');
    this.storage = path.join(this.root, 'data/capabilities');
    this.catalog = options.catalog ?? require('./catalog.json').plugins;
    this.tool = options.tool ?? require('./deps.cjs').tools(this.root);
    this.options = options;
    this.notify = options.notify ?? (() => {});
    this.signal = options.signal ?? new AbortController().signal;
    this.endpoint = 'http://127.0.0.1:17373';
  }

  async init() {
    const marker = path.join(this.root, 'harness/distribution/agent-release.json');
    const build = await json(existsSync(marker) ? marker : path.join(this.root, 'harness/distribution/agent-build.json'));
    assert(build.product === 'moe4all-agent' || build.repository === 'Headmaster218/MoE4All-Agent', 'Not a MoE4All Agent installation');
    await noLinks(this.root, this.profile);
    await noLinks(this.root, this.storage);
    assert.equal((await json(path.join(this.profile, 'package.json'))).name, 'moe4all-agent-profile-web');
    await fs.mkdir(this.storage, { recursive: true });
    this.statePath = path.join(this.storage, 'state.json');
    this.settingsPath = path.join(this.storage, 'settings.json');
    this.journalPath = path.join(this.storage, 'transaction.json');
    this.settings = existsSync(this.settingsPath) ? await json(this.settingsPath)
      : { autoCheck: true, preview: false, skipped: {}, schema: 2 };
    const resetChannel = this.settings.schema !== 2;
    if (resetChannel) {
      // Old previews inferred this setting from the Agent version, not a channel choice.
      this.settings.preview = false; this.settings.schema = 2;
      await writeJson(this.settingsPath, this.settings);
    }
    this.settings.skipped ??= {};
    assert(this.settings.skipped && typeof this.settings.skipped === 'object' && !Array.isArray(this.settings.skipped), 'Invalid skipped versions');
    assert(typeof this.settings.autoCheck === 'boolean' && typeof this.settings.preview === 'boolean', 'Invalid capability settings');
    this.state = existsSync(this.statePath) ? await json(this.statePath) : { plugins: {}, operation: null, engine: null };
    if (resetChannel) for (const row of Object.values(this.state.plugins)) {
      row.checkedAt = 0; row.candidate = null; row.phase = 'idle'; row.error = '';
    }
    this.transport = this.options.fetch ? null : require('./transport.cjs').createTransport(this.tool);
    this.fetcher = this.options.fetch ?? this.transport.fetch;
    return this;
  }

  async host() {
    if (this.options.host) return this.options.host;
    const anchor = createRequire(path.join(this.root, 'harness/runtime/dsh/package.json'));
    const app = await json(path.join(this.root, 'harness/runtime/dsh/package.json'));
    const peers = {};
    // Inventory the actual host, not the old plugin's peers: a new release can add peers.
    const scope = path.join(this.root, 'harness/runtime/dsh/node_modules/@deepseek-ai');
    for (const short of await fs.readdir(scope)) {
      if (!/^[0-9A-Za-z._-]+$/u.test(short)) continue;
      const name = '@deepseek-ai/' + short;
      try { peers[name] = (await packageInfo(anchor, name)).version; }
      catch { /* Missing or broken host packages still fail archive compatibility checks. */ }
    }
    return { dshVersion: app.version, peers };
  }

  async emit() {
    await writeJson(this.statePath, this.state);
    const snapshot = await this.snapshot();
    this.lastSnapshot = snapshot;
    this.notify(snapshot);
    return snapshot;
  }

  async snapshot() {
    const plugins = [];
    for (const item of this.catalog) {
      let installedVersion = '', reason = '';
      try {
        const moduleRoot = await fs.realpath(path.join(this.profile, 'node_modules', item.id));
        contained(await fs.realpath(path.join(this.profile, 'node_modules')), moduleRoot);
        installedVersion = (await json(path.join(moduleRoot, 'package.json'))).version;
      } catch (error) { reason = errorText(error); }
      const row = this.state.plugins[item.id] ?? {};
      const candidate = row.candidate;
      const operation = this.state.operation?.component === item.id
        && !['cancelled', 'complete', 'rolled-back'].includes(this.state.operation.phase) ? this.state.operation : null;
      plugins.push({ id: item.id, kind: 'plugin', zh: item.zh, en: item.en, installedVersion,
        latestVersion: candidate?.version ?? '', releaseUrl: candidate?.url ?? 'https://github.com/' + item.repository + '/releases',
        updateAvailable: !reason && !!candidate && this.tool('semver').gt(candidate.version, installedVersion),
        phase: operation?.phase ?? row.phase ?? 'idle', error: reason || operation?.error || row.error || '',
        errorStage: operation?.error ? 'update' : reason || row.error ? 'check' : undefined,
        completed: operation?.completed ?? 0, total: operation?.total ?? 0,
        pending: operation?.phase === 'ready', rollbackAvailable: !!this.state.rollback && this.state.rollback.component === item.id,
        checkedAt: row.checkedAt ?? 0, lastUpdate: row.lastUpdate ?? null,
        preparedVersion: operation?.version ?? '', pendingAction: operation?.action ?? 'update' });
    }
    return { schema: 'moe4all/capabilities/v1', settings: this.settings, plugins, engine: this.state.engine, checking: !!this.checking,
      operation: this.state.operation, pending: !!this.state.operation && ['ready', 'applying', 'awaiting-health'].includes(this.state.operation.phase) };
  }

  async lock(action) {
    const file = path.join(this.storage, 'operation.lock');
    let handle;
    try { handle = await fs.open(file, 'wx', 0o600); }
    catch (error) {
      if (error.code !== 'EEXIST') throw error;
      const owner = await json(file);
      assert(Number.isSafeInteger(owner.pid) && owner.pid > 0, 'Invalid update lock');
      try { process.kill(owner.pid, 0); throw new Error('Another capability operation is running'); }
      catch (probe) { if (probe.code !== 'ESRCH') throw probe; }
      await fs.unlink(file);
      handle = await fs.open(file, 'wx', 0o600);
      if (this.state.operation && ['download', 'verify', 'prepare', 'snapshot', 'dependencies', 'validate'].includes(this.state.operation.phase)) {
        this.state.operation.phase = 'cancelled';
        this.state.operation.error = 'The previous preparation was interrupted; installed plugins were not changed.';
      }
    }
    await handle.writeFile(JSON.stringify({ pid: process.pid, createdAt: Date.now(), id: randomUUID() }));
    try { return await action(); }
    finally { await handle.close(); await fs.unlink(file); }
  }

  async check(force = false, automatic = false) {
    if (automatic && !this.settings.autoCheck) return this.snapshot();
    return this.lock(async () => {
      this.checking = true;
      for (const item of this.catalog) {
        this.signal.throwIfAborted();
        const old = this.state.plugins[item.id] ?? {};
        if (!force && old.checkedAt && Date.now() - old.checkedAt < 6 * 60 * 60 * 1000) continue;
        this.state.plugins[item.id] = { ...old, phase: 'checking', error: '' }; await this.emit();
        try {
          const pkg = await json(path.join(this.profile, 'node_modules', item.id, 'package.json'));
          const bytes = await release.receive(this.fetcher, 'https://api.github.com/repos/' + item.repository + '/releases?per_page=30', 2 * 1024 * 1024, this.signal);
          const candidate = release.selectRelease(JSON.parse(bytes), item, pkg.version, this.settings.preview, this.tool('semver'));
          this.state.plugins[item.id] = { ...old, candidate, checkedAt: Date.now(), phase: candidate?.update ? 'available' : 'current', error: '' };
        } catch (error) { this.state.plugins[item.id] = { ...old, phase: 'error', error: errorText(error) }; }
        await this.emit();
      }
      await this.checkEngine(force);
      this.checking = false;
      return this.emit();
    });
  }

  async engineRequest(route, method = 'GET', body = {}) {
    const fetcher = this.options.engineFetch ?? fetch;
    const response = await fetcher(this.endpoint + route, { method, redirect: 'error',
      headers: { origin: this.endpoint, 'content-type': 'application/json' },
      ...(method === 'POST' ? { body: JSON.stringify(body) } : {}), signal: AbortSignal.timeout(15000) });
    assert(response.ok, 'Engine manager request failed: HTTP ' + response.status);
    return response.json();
  }

  async checkEngine(force) {
    try {
      const refresh = force || !this.state.engine?.checkedAt || Date.now() - this.state.engine.checkedAt > 6 * 60 * 60 * 1000;
      let result = await this.engineRequest('/api/moe4all/release' + (refresh ? '?force=1' : ''));
      const runtime = await this.engineRequest('/api/moe4all/status');
      // The manager's in-memory feed cache is empty after an Agent restart.
      if (!refresh && !result.latest && !result.message && this.state.engine?.latest) {
        const latest = this.state.engine.latest;
        result = { ...result, latest, updateAvailable: latest.tag !== result.installed?.tag };
      }
      const canInstall = result.supported && result.confirmedReleaseTag === true
        && (result.managed || (!runtime.executable && !result.installed && runtime.mode !== 'connect'));
      const checkError = result.message || (!refresh && !result.latest ? this.state.engine?.checkError : '') || '';
      const installPhase = result.install?.stage ?? 'idle';
      const phase = installPhase === 'idle' || installPhase === 'complete'
        ? result.latest ? result.updateAvailable ? 'available' : 'current' : 'idle' : installPhase;
      this.state.engine = { ...result, runtime, canInstall, checkedAt: refresh ? Date.now() : this.state.engine.checkedAt,
        checkError, phase: checkError && installPhase === 'idle' ? 'error' : phase,
        error: result.install?.error || checkError, managerUpdateRequired: result.supported && result.confirmedReleaseTag !== true };
    } catch (error) { this.state.engine = { phase: 'error', error: errorText(error), supported: false }; }
  }

  async engineInstall(confirmed, expectedVersion) {
    assert.equal(confirmed, true, 'Downloading requires explicit user confirmation');
    return this.lock(async () => {
      await this.checkEngine(false);
      const engine = this.state.engine;
      assert(engine.canInstall && engine.latest && (engine.updateAvailable || !engine.installed), 'Only a managed or unconfigured Engine can be installed here; external Engines are not taken over');
      assert.equal(engine.latest.tag, expectedVersion, 'The selected Engine version changed; check again before confirming');
      assert(!engine.runtime.owned, 'Stop the Agent-managed Engine in Engine settings before updating it');
      await this.engineRequest('/api/moe4all/install', 'POST', { expectedTag: expectedVersion });
      await this.checkEngine(false);
      return this.emit();
    });
  }

  async bootstrapEngine() {
    if (this.state.engineBootstrapAttempted) return this.snapshot();
    await this.checkEngine(false);
    const engine = this.state.engine, runtime = engine.runtime;
    if (!engine.canInstall || !engine.latest || engine.installed || engine.versions?.length
      || !runtime || runtime.mode === 'connect' || runtime.executable || runtime.ready || runtime.owned) return this.emit();
    this.state.engineBootstrapAttempted = true;
    await this.emit();
    return this.engineInstall(true, engine.latest.tag);
  }

  async engineCancel() {
    return this.lock(async () => {
      await this.engineRequest('/api/moe4all/install-cancel', 'POST');
      await this.checkEngine(false);
      return this.emit();
    });
  }

  async prepare(id, expectedVersion, confirmed) {
    assert.equal(confirmed, true, 'Downloading requires explicit user confirmation');
    const item = this.catalog.find(entry => entry.id === id);
    assert(item, 'Unknown managed plugin');
    return this.lock(async () => {
      assert(!this.state.operation || !['ready', 'applying', 'awaiting-health'].includes(this.state.operation.phase), 'Apply or cancel the pending plugin update first');
      const candidate = this.state.plugins[id]?.candidate;
      assert(candidate && candidate.version === expectedVersion, 'The selected release changed; check again before confirming');
      const current = await json(path.join(this.profile, 'node_modules', id, 'package.json'));
      assert(this.tool('semver').gt(candidate.version, current.version), 'The selected release is not newer');
      const operationId = randomUUID();
      const directory = path.join(this.storage, 'transactions', operationId);
      await fs.mkdir(directory, { recursive: true });
      const archive = path.join(directory, id + '.tgz');
      this.state.operation = { id: operationId, component: id, version: candidate.version, phase: 'download', completed: 0, total: candidate.bytes, error: '' };
      await this.emit();
      let last = 0;
      const progress = (phase, completed, total) => {
        Object.assign(this.state.operation, { phase, completed, total });
        if (Date.now() - last > 200) {
          last = Date.now();
          this.notify({ ...this.lastSnapshot, operation: { ...this.state.operation },
            plugins: this.lastSnapshot.plugins.map(row => row.id === id ? { ...row, phase, completed, total } : row) });
        }
      };
      try {
        await release.downloadRelease(item, candidate, archive, await this.host(), this.tool, this.fetcher, this.signal, progress);
        this.state.operation.phase = 'prepare'; await this.emit();
        const staged = await profileTools.stageProfile({ root: this.root, profile: this.profile, catalog: this.catalog, directory,
          item, archive, version: candidate.version, tool: this.tool, signal: this.signal, progress, install: this.options.install });
        const transaction = { schema: 1, id: operationId, component: id, version: candidate.version, directory,
          staged: staged.staged, backup: path.join(directory, 'previous'), base: staged.base, phase: 'ready',
          after: await identity(staged.staged, this.catalog), treeHash: await treeHash(path.join(staged.staged, 'node_modules'), progress) };
        await writeJson(this.journalPath, transaction);
        this.state.operation.phase = 'ready'; this.state.operation.completed = candidate.bytes;
      } catch (error) {
        this.state.operation.phase = this.signal.aborted ? 'cancelled' : 'error';
        this.state.operation.error = errorText(error);
      }
      return this.emit();
    });
  }

  async setSettings(value) {
    assert(typeof value.autoCheck === 'boolean' && typeof value.preview === 'boolean');
    return this.lock(async () => {
      this.settings = { ...this.settings, autoCheck: value.autoCheck, preview: value.preview };
      for (const row of Object.values(this.state.plugins)) { row.checkedAt = 0; row.candidate = null; row.phase = 'idle'; row.error = ''; }
      await writeJson(this.settingsPath, this.settings);
      return this.emit();
    });
  }

  async skip(id, version) {
    assert(id === 'agent' || id === 'engine' || this.catalog.some(item => item.id === id), 'Unknown update component');
    assert(typeof version === 'string' && /^[0-9A-Za-z.+-]{1,100}$/u.test(version), 'Invalid skipped version');
    return this.lock(async () => {
      this.settings.skipped[id] = version;
      await writeJson(this.settingsPath, this.settings);
      return this.emit();
    });
  }

  async compatibility(dshVersion) {
    assert(!this.state.operation || !['download', 'verify', 'prepare', 'snapshot', 'dependencies', 'validate', 'ready', 'applying', 'awaiting-health'].includes(this.state.operation.phase),
      'Finish or cancel the pending plugin update before updating the Agent');
    assert(this.tool('semver').valid(dshVersion), 'The Agent release must declare its DSH version');
    const host = await this.host();
    for (const item of this.catalog) {
      const pkg = await json(path.join(this.profile, 'node_modules', item.id, 'package.json'));
      // New host peer versions are unknown until unpacking: enforce the explicit host range now.
      release.checkCompatibility({ engines: pkg.engines }, { ...host, dshVersion }, this.tool('semver'));
    }
    return { compatible: true, dshVersion };
  }

  async transaction() {
    if (!existsSync(this.journalPath)) return null;
    const value = await json(this.journalPath);
    assert(value.schema === 1 && /^[0-9a-f-]{36}$/u.test(value.id) && this.catalog.some(item => item.id === value.component), 'Invalid plugin transaction');
    const dir = path.join(this.storage, 'transactions', value.id);
    // Installations are portable. Derive destinations from the current root, never from journal paths.
    assert(typeof value.directory === 'string' && path.isAbsolute(value.directory) && path.basename(value.directory) === value.id);
    assert.equal(value.staged, path.join(value.directory, 'profile')); assert.equal(value.backup, path.join(value.directory, 'previous'));
    value.directory = dir; value.staged = path.join(dir, 'profile'); value.backup = path.join(dir, 'previous');
    await noLinks(this.root, dir);
    return value;
  }

  async requireCold() {
    if (this.options.cold) return;
    const busy = await new Promise(resolve => {
      const socket = net.createConnection({ host: '127.0.0.1', port: 17373 });
      const finish = value => { socket.destroy(); resolve(value); };
      socket.once('connect', () => finish(true)); socket.once('error', () => finish(false));
      socket.setTimeout(1500, () => finish(true));
    });
    assert(!busy, 'Close the current Agent before applying a plugin update');
  }

  async apply() {
    this.profileRepairRequired = false;
    await this.requireCold();
    return this.lock(async () => {
      const transaction = await this.transaction();
      if (!transaction) return this.snapshot();
      this.profileRepairRequired = ['ready', 'applying', 'awaiting-health', 'rollback-requested'].includes(transaction.phase);
      if (['applying', 'awaiting-health', 'rollback-requested'].includes(transaction.phase)) {
        if (transaction.phase === 'rollback-requested') assert.deepEqual(await identity(this.profile, this.catalog), transaction.after, 'Profile changed since update; refusing to overwrite user changes');
        const requested = transaction.phase === 'rollback-requested';
        await profileTools.restore(this.profile, transaction, this.journalPath);
        const restoredVersion = transaction.base.versions[transaction.component];
        this.state.operation = { component: transaction.component, version: restoredVersion, phase: 'rolled-back', error: '' };
        if (requested) this.state.plugins[transaction.component] = { ...this.state.plugins[transaction.component], phase: 'current', error: '',
          lastUpdate: { from: transaction.version, to: restoredVersion, at: Date.now(), kind: 'rollback' } };
        this.state.rollback = null;
        return this.emit();
      }
      if (transaction.phase !== 'ready') return this.snapshot();
      try {
        assert.deepEqual(await identity(this.profile, this.catalog), transaction.base, 'Profile changed after update preparation; check and prepare again');
        assert.deepEqual(await identity(transaction.staged, this.catalog), transaction.after, 'Staged profile metadata changed');
        assert.equal(await treeHash(path.join(transaction.staged, 'node_modules')), transaction.treeHash, 'Staged plugin files changed');
      } catch (error) {
        transaction.phase = 'cancelled'; await writeJson(this.journalPath, transaction);
        this.state.operation = { component: transaction.component, version: transaction.version, phase: 'error', error: errorText(error) };
        this.state.rollback = null;
        return this.emit();
      }
      try { await profileTools.activate(this.profile, transaction, this.journalPath, this.options.fault); }
      catch (error) {
        // Continue only when the rollback proves the original live profile is intact.
        assert.deepEqual(await identity(this.profile, this.catalog), transaction.base, 'Plugin recovery is incomplete; refusing to start a mixed profile');
        this.state.operation = { component: transaction.component, version: transaction.version, phase: 'error', error: errorText(error) };
        this.state.rollback = null;
        return this.emit();
      }
      this.state.operation.phase = 'awaiting-health';
      return this.emit();
    });
  }

  async healthy() {
    return this.lock(async () => {
      const transaction = await this.transaction();
      if (transaction?.phase === 'awaiting-health') {
        assert.deepEqual(await identity(this.profile, this.catalog), transaction.after, 'Activated profile changed before health confirmation');
        transaction.phase = 'applied'; await writeJson(this.journalPath, transaction);
        await writeJson(path.join(transaction.directory, 'applied.json'), transaction);
        this.state.rollback = { component: transaction.component, id: transaction.id };
        this.state.operation.phase = 'complete';
        this.state.plugins[transaction.component] = { ...this.state.plugins[transaction.component], phase: 'current', error: '',
          lastUpdate: { from: transaction.base.versions[transaction.component], to: transaction.version, at: Date.now() } };
      }
      await this.prune();
      return this.emit();
    });
  }

  async rollback(confirmed) {
    assert.equal(confirmed, true, 'Rollback requires explicit confirmation');
    return this.lock(async () => {
      const transaction = await this.transaction();
      assert(transaction?.phase === 'applied', 'No plugin rollback is available');
      assert.deepEqual(await identity(this.profile, this.catalog), transaction.after, 'Profile changed since update; refusing to overwrite user changes');
      transaction.phase = 'rollback-requested'; await writeJson(this.journalPath, transaction);
      this.state.operation = { id: transaction.id, component: transaction.component, version: transaction.base.versions[transaction.component], action: 'rollback', phase: 'ready', error: '' };
      return this.emit();
    });
  }

  async cancel() {
    return this.lock(async () => {
      const transaction = await this.transaction();
      assert(!transaction || !['applying', 'awaiting-health'].includes(transaction.phase), 'A cold switch cannot be interrupted');
      if (transaction?.phase === 'ready') { transaction.phase = 'cancelled'; await writeJson(this.journalPath, transaction); }
      if (transaction?.phase === 'rollback-requested') { transaction.phase = 'applied'; await writeJson(this.journalPath, transaction); }
      if (this.state.operation) this.state.operation.phase = 'cancelled';
      if (transaction?.phase === 'cancelled' && this.state.rollback?.id) {
        const prior = path.join(this.storage, 'transactions', this.state.rollback.id, 'applied.json');
        contained(this.storage, prior); await noLinks(this.root, prior);
        const saved = await json(prior);
        assert(saved.phase === 'applied');
        await writeJson(this.journalPath, saved);
      }
      await this.prune();
      return this.emit();
    });
  }

  async prune() {
    const base = path.join(this.storage, 'transactions');
    if (!existsSync(base)) return;
    await noLinks(this.root, base);
    const keep = new Set();
    const transaction = await this.transaction();
    if (transaction && !['cancelled', 'rolled-back'].includes(transaction.phase)) keep.add(transaction.id);
    if (this.state.rollback?.id) keep.add(this.state.rollback.id);
    // Archive pins from the current and rollback profiles remain valid across future updates.
    for (const file of [path.join(this.profile, 'package.json'), ...(transaction ? [path.join(transaction.backup, 'package.json')] : [])]) {
      if (!existsSync(file)) continue;
      const pkg = await json(file);
      for (const spec of Object.values(pkg.dependencies ?? {})) {
        if (typeof spec !== 'string' || !spec.startsWith('file:')) continue;
        const resolved = path.resolve(this.profile, spec.slice(5));
        if (!resolved.startsWith(base + path.sep)) continue;
        keep.add(path.relative(base, resolved).split(path.sep)[0]);
      }
    }
    for (const name of await fs.readdir(base)) {
      if (!/^[0-9a-f-]{36}$/u.test(name) || keep.has(name)) continue;
      const target = contained(base, path.join(base, name)); await noLinks(this.root, target);
      await fs.rm(target, { recursive: true });
    }
  }

  async dispose() { await this.transport?.dispose(); }
}

module.exports = { CapabilityCenter, treeHash, packageInfo, errorText };
