const assert = require('node:assert/strict');
const fs = require('node:fs');
const { createHash } = require('node:crypto');
const { pipeline } = require('node:stream/promises');
const { Readable } = require('node:stream');
const path = require('node:path');

const MAX_ARCHIVE = 64 * 1024 * 1024;
const MAX_UNPACKED = 256 * 1024 * 1024;
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

function previewVersion(version, semver) {
  const parts = semver.prerelease(version);
  // The maintained forks use -moe4all.N as a distribution revision, not a preview channel.
  return !!parts && !(parts.length === 2 && parts[0] === 'moe4all' && Number.isSafeInteger(parts[1]));
}

function assetUrl(url, item, tag) {
  const parsed = new URL(url);
  assert.equal(parsed.origin, 'https://github.com', 'Unexpected release host');
  assert(!parsed.username && !parsed.password && !parsed.search && !parsed.hash, 'Unexpected asset credentials or query');
  assert(parsed.pathname.startsWith('/' + item.repository + '/releases/download/' + tag + '/'), 'Asset belongs to another repository or tag');
  return parsed.href;
}

function selectRelease(releases, item, installed, preview, semver) {
  assert(Array.isArray(releases), 'Invalid GitHub release response');
  const candidates = [];
  for (const release of releases) {
    if (release.draft || typeof release.tag_name !== 'string' || !release.tag_name.startsWith('release-')) continue;
    const version = semver.valid(release.tag_name.slice(8));
    if (!version || (!preview && (release.prerelease || previewVersion(version, semver)))) continue;
    const assets = release.assets;
    if (!Array.isArray(assets)) continue;
    const archive = assets.find(a => a.name === item.id + '.tgz');
    const checksum = assets.find(a => a.name === 'SHA256SUMS.txt');
    const manifest = assets.find(a => a.name === 'plugin-release.json');
    if (!archive || !checksum || (!manifest && !item.legacyManifest)) continue;
    if (!Number.isSafeInteger(archive.size) || archive.size <= 0 || archive.size > MAX_ARCHIVE) continue;
    const tag = release.tag_name;
    candidates.push({ version, tag, bytes: archive.size,
      archive: assetUrl(archive.browser_download_url, item, tag),
      checksum: assetUrl(checksum.browser_download_url, item, tag),
      manifest: manifest ? assetUrl(manifest.browser_download_url, item, tag) : null,
      url: 'https://github.com/' + item.repository + '/releases/tag/' + tag,
      update: semver.valid(installed) !== null && semver.gt(version, installed), prerelease: !!release.prerelease || previewVersion(version, semver) });
  }
  return candidates.sort((a, b) => semver.rcompare(a.version, b.version))[0] ?? null;
}

async function receive(fetcher, url, max, signal, onProgress = () => {}) {
  const response = await fetcher(url, { signal: AbortSignal.any([signal, AbortSignal.timeout(60000)]),
    headers: { accept: 'application/vnd.github+json', 'user-agent': 'MoE4All-Capabilities' } });
  assert(response.ok, 'Release request failed: HTTP ' + response.status);
  assert(response.body, 'Empty release response');
  const chunks = [];
  let size = 0;
  for await (const chunk of response.body) {
    signal.throwIfAborted();
    size += chunk.byteLength;
    assert(size <= max, 'Release response exceeds size limit');
    chunks.push(Buffer.from(chunk));
    onProgress(size);
  }
  return Buffer.concat(chunks);
}

function checkCompatibility(pkg, host, semver) {
  const ranges = [[pkg.engines?.node, process.versions.node, 'Node'], [pkg.engines?.dsh, host.dshVersion, 'DSH']];
  for (const [name, range] of Object.entries(pkg.peerDependencies ?? {})) {
    if (name.startsWith('@deepseek-ai/') && !pkg.peerDependenciesMeta?.[name]?.optional) {
      const version = host.peers[name];
      assert(version, 'Required host package is unavailable: ' + name);
      ranges.push([range, version, name]);
    }
  }
  for (const [range, version, label] of ranges) {
    if (!range) continue;
    assert(typeof range === 'string' && semver.validRange(range), 'Invalid compatibility range for ' + label);
    assert(version && semver.satisfies(version, range, { includePrerelease: true }), 'Incompatible ' + label + ': ' + (version ?? 'unknown') + ' requires ' + range);
  }
}

async function inspectArchive(file, item, version, host, tool) {
  const tar = tool('tar');
  const semver = tool('semver');
  const entries = new Set();
  let bytes = 0, pkg;
  await new Promise((resolve, reject) => {
    const parser = new tar.Parser({ strict: true, maxMetaEntrySize: 65536, maxDecompressionRatio: 200, onReadEntry(entry) {
      const name = entry.path;
      const windowsName = name.toLowerCase();
      if (!name.startsWith('package/') || /[\\:\0]/u.test(name) || name.split('/').some(part => part === '..' || /[. ]$/u.test(part) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/iu.test(part))
        || entries.has(windowsName) || !['File', 'Directory'].includes(entry.type)) {
        parser.abort(new Error('Unsafe plugin archive entry: ' + name)); return;
      }
      entries.add(windowsName);
      bytes += entry.size;
      if (entries.size > 10000 || bytes > MAX_UNPACKED) { parser.abort(new Error('Plugin unpacking limit exceeded')); return; }
      if (name === 'package/package.json') {
        if (entry.size > 65536) { parser.abort(new Error('Plugin manifest is too large')); return; }
        const chunks = [];
        entry.on('data', chunk => chunks.push(chunk));
        entry.on('end', () => {
          try { pkg = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
          catch (error) { parser.abort(error); }
        });
      }
      entry.resume();
    } });
    const input = fs.createReadStream(file);
    input.on('error', error => parser.abort(error));
    parser.once('error', error => { input.destroy(); reject(error); });
    parser.once('end', resolve);
    input.pipe(parser);
  });
  assert(pkg?.name === item.id && pkg.version === version, 'Unexpected plugin name or version');
  assert(pkg.dsh?.bundle?.patch === './cordis.patch.yml' && entries.has('package/cordis.patch.yml'), 'Plugin bundle patch is missing');
  assert(typeof pkg.main === 'string' && !pkg.main.includes('..') && entries.has(('package/' + pkg.main.replace(/^\.\//u, '')).toLowerCase()), 'Plugin runtime entry is missing');
  for (const key of ['preinstall', 'install', 'postinstall', 'prepare']) assert(!pkg.scripts?.[key], 'Plugin installation lifecycle scripts are not allowed');
  checkCompatibility(pkg, host, semver);
  return pkg;
}

async function downloadRelease(item, release, out, host, tool, fetcher, signal, progress) {
  const archive = await receive(fetcher, release.archive, MAX_ARCHIVE, signal, size => progress('download', size, release.bytes));
  assert.equal(archive.length, release.bytes, 'Release size differs from metadata');
  const sums = (await receive(fetcher, release.checksum, 65536, signal)).toString('utf8');
  const checksumRows = sums.split(/\r?\n/u).map(line => /^([a-f0-9]{64})\s+\*?([^/\\]+)$/iu.exec(line)).filter(Boolean);
  const expected = checksumRows.filter(row => row[2] === item.id + '.tgz');
  assert(expected.length === 1 && sha256(archive) === expected[0][1].toLowerCase(), 'Release SHA256 mismatch');
  progress('verify', release.bytes, release.bytes);
  if (release.manifest) {
    const metadata = await receive(fetcher, release.manifest, 65536, signal);
    const metadataHash = checksumRows.filter(row => row[2] === 'plugin-release.json');
    assert(metadataHash.length === 1 && sha256(metadata) === metadataHash[0][1].toLowerCase(), 'Release manifest SHA256 mismatch');
    const info = JSON.parse(metadata);
    assert(info.schema === 'moe4all/plugin-release/v1' && info.name === item.id && info.version === release.version
      && info.repository === item.repository && info.tag === release.tag && /^[a-f0-9]{40}$/u.test(info.commit)
      && info.archive.name === item.id + '.tgz' && info.archive.bytes === archive.length && info.archive.sha256 === sha256(archive), 'Release manifest does not match package');
  }
  await fs.promises.mkdir(path.dirname(out), { recursive: true });
  await pipeline(Readable.from([archive]), fs.createWriteStream(out, { flags: 'wx' }));
  await inspectArchive(out, item, release.version, host, tool);
  return out;
}

module.exports = { MAX_ARCHIVE, assetUrl, selectRelease, receive, checkCompatibility, inspectArchive, downloadRelease, sha256, previewVersion };
