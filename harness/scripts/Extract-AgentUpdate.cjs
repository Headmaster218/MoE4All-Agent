const path = require('node:path');
const { parseArgs } = require('node:util');
const { unpack } = require('./Prepare-BundledRuntime.cjs');

/** Reuse the bounded native-zlib worker pool used by first-start extraction. */
async function main() {
  const { values } = parseArgs({ options: Object.fromEntries(['root', 'archive', 'destination', 'sha256', 'files'].map(name => [name, { type: 'string' }])) });
  const root = path.resolve(values.root);
  const files = Number(values.files);
  if (!Number.isSafeInteger(files) || files < 1) throw new Error('Invalid update archive file count');
  let completed = 0, last = 0;
  const progress = { extracted() {
    completed++;
    if (completed === files || Date.now() - last >= 200) {
      last = Date.now();
      process.stdout.write(`@@MOE4ALL_UPDATE\textract\t${completed}\t${files}\tUnpacking application files\n`);
    }
  } };
  await unpack(root, { file: path.relative(root, path.resolve(values.archive)), root: path.relative(root, path.resolve(values.destination)), sha256: values.sha256, files }, progress);
}
if (require.main === module) main().catch(error => { console.error(error.message); process.exitCode = 1; });
module.exports = { main };
