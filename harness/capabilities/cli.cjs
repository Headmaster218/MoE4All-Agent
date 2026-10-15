const { parseArgs } = require('node:util');
const { CapabilityCenter, errorText } = require('./center.cjs');

async function main() {
  const { values, positionals } = parseArgs({ allowPositionals: true, options: {
    root: { type: 'string' }, id: { type: 'string' }, version: { type: 'string' },
    confirm: { type: 'boolean', default: false }, force: { type: 'boolean', default: false },
    automatic: { type: 'boolean', default: false }, autoCheck: { type: 'string' }, preview: { type: 'string' },
  } });
  if (!values.root) throw new Error('Agent root is required');
  const controller = new AbortController();
  process.once('SIGTERM', () => controller.abort());
  const send = value => process.stdout.write('@@MOE4ALL_CAPABILITIES\t' + JSON.stringify(value) + '\n');
  const center = await new CapabilityCenter(values.root, { signal: controller.signal, notify: send }).init();
  try {
    let result;
    switch (positionals[0]) {
      case 'status': result = await center.snapshot(); break;
      case 'compatibility': result = await center.compatibility(values.version); break;
      case 'skip': result = await center.skip(values.id, values.version); break;
      case 'check': result = await center.check(values.force, values.automatic); break;
      case 'engine-status': result = await center.lock(async () => { await center.checkEngine(false); return center.emit(); }); break;
      case 'engine-install': result = await center.engineInstall(values.confirm, values.version); break;
      case 'engine-cancel': result = await center.engineCancel(); break;
      case 'prepare': result = await center.prepare(values.id, values.version, values.confirm); break;
      case 'apply': result = await center.apply(); break;
      case 'healthy': result = await center.healthy(); break;
      case 'rollback': result = await center.rollback(values.confirm); break;
      case 'cancel': result = await center.cancel(); break;
      case 'settings':
        if (!['true', 'false'].includes(values.autoCheck) || !['true', 'false'].includes(values.preview)) throw new Error('Boolean settings are required');
        result = await center.setSettings({ autoCheck: values.autoCheck === 'true', preview: values.preview === 'true' }); break;
      default: throw new Error('Unknown capability command');
    }
    send(result);
  } finally { await center.dispose(); }
}

if (require.main === module) main().catch(error => {
  process.stderr.write(errorText(error) + '\n'); process.exitCode = 1;
});
module.exports = { main };
