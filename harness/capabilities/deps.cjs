const { createRequire } = require('node:module');
const path = require('node:path');

function tools(root) {
  return createRequire(path.join(root, 'harness/runtime/update-tools/package.json'));
}

module.exports = { tools };
