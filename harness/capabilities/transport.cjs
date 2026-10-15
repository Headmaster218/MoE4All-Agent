const { execFileSync } = require('node:child_process');

function createTransport(tool) {
  const { ProxyAgent } = tool('undici');
  let agent;
  let proxy = process.env.HTTPS_PROXY || process.env.https_proxy || process.env.HTTP_PROXY || process.env.http_proxy;
  if (!proxy && process.platform === 'win32') {
    try {
      const key = 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings';
      const enabled = execFileSync('reg.exe', ['query', key, '/v', 'ProxyEnable'], { windowsHide: true, encoding: 'utf8' });
      if (/REG_DWORD\s+0x1\b/u.test(enabled)) {
        const server = execFileSync('reg.exe', ['query', key, '/v', 'ProxyServer'], { windowsHide: true, encoding: 'utf8' });
        const value = /ProxyServer\s+REG_SZ\s+(.+)/u.exec(server)?.[1]?.trim();
        proxy = value?.split(';').find(s => s.startsWith('https='))?.slice(6)
          || value?.split(';').find(s => s.startsWith('http='))?.slice(5) || (value?.includes('=') ? undefined : value);
      }
    } catch { /* Windows proxy settings are optional; direct HTTPS remains available. */ }
  }
  if (proxy) {
    if (!/^https?:\/\//u.test(proxy)) proxy = 'http://' + proxy;
    agent = new ProxyAgent(proxy);
  }
  async function request(url, init) {
    let response;
    const connection = new AbortController();
    const timer = setTimeout(() => connection.abort(new DOMException('Release connection timed out', 'TimeoutError')), agent ? 5000 : 30000);
    const directSignal = AbortSignal.any([...(init.signal ? [init.signal] : []), connection.signal]);
    try { response = await fetch(url, { ...init, signal: directSignal, redirect: 'manual' }); }
    catch (error) { if (!agent || init.signal?.aborted) throw error; }
    finally { clearTimeout(timer); }
    if (agent && (!response || response.status === 403 || response.status === 429 || response.status >= 500)) {
      await response?.body?.cancel();
      response = await fetch(url, { ...init, redirect: 'manual', dispatcher: agent });
    }
    return response;
  }
  return {
    async fetch(url, init = {}) {
      let current = new URL(url);
      for (let redirects = 0; redirects <= 5; redirects++) {
        if (current.protocol !== 'https:' || current.username || current.password
          || !['api.github.com', 'github.com', 'objects.githubusercontent.com', 'release-assets.githubusercontent.com'].includes(current.hostname)) {
          throw new Error('Unexpected release redirect host');
        }
        const response = await request(current, init);
        if (![301, 302, 303, 307, 308].includes(response.status)) return response;
        const location = response.headers.get('location');
        await response.body?.cancel();
        if (!location) throw new Error('Release redirect has no location');
        current = new URL(location, current);
      }
      throw new Error('Too many release redirects');
    },
    async dispose() { await agent?.close(); },
  };
}

module.exports = { createTransport };
