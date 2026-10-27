'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { startMockModel } = require('./mock-model.cjs');

test('synthetic model exposes both reasoning families and records request controls', async () => {
  const server = await startMockModel();
  try {
    assert.equal((await (await fetch(`${server.url}/v1/models`)).json()).data.length, 2);
    const response = await fetch(`${server.url}/v1/chat/completions`, { method: 'POST', headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ model: 'Qwen3.6-35B-A3B-QA', messages: [{ role: 'user', content: 'QA normal' }],
        chat_template_kwargs: { enable_thinking: false }, stream: true, tools: [{ function: { name: 'read' } }] }) });
    const text = await response.text();
    assert.match(text, /QA_REPLY_OK/); assert.match(text, /\[DONE\]/);
    assert.deepEqual(server.captures[0].chatTemplateKwargs, { enable_thinking: false });
    assert.equal(server.captures[0].closed, true);
  } finally { await server.close(); }
});

test('tool calls require real advertised tools and settle only after a tool result', async () => {
  const server = await startMockModel();
  try {
    const body = { model: 'qa', messages: [{ role: 'user', content: 'QA_LIST_DIRECTORY' }], stream: true };
    const post = value => fetch(`${server.url}/v1/chat/completions`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(value) });
    assert.equal((await post({ ...body, tools: [{ function: { name: 'read' } }] })).status, 400);
    const tools = [{ function: { name: 'list_directory' } }];
    assert.match(await (await post({ ...body, tools })).text(), /tool_calls/);
    assert.match(await (await post({ ...body, tools, messages: [...body.messages, { role: 'tool', content: '{}' }] })).text(), /QA_TOOL_DONE/);
  } finally { await server.close(); }
});

test('cancellation closes a live stream and teardown closes stalled responses', async () => {
  const server = await startMockModel();
  try {
    const abort = new AbortController();
    const response = await fetch(`${server.url}/v1/chat/completions`, { method: 'POST', headers: { 'content-type': 'application/json' }, signal: abort.signal,
      body: JSON.stringify({ model: 'qa', stream: true, messages: [{ role: 'user', content: 'QA_STREAM' }], tools: [{ function: { name: 'read' } }] }) });
    const reader = response.body.getReader(); await reader.read(); abort.abort();
    await assert.rejects(reader.read());
    for (let i = 0; i < 100 && !server.captures[0].closed; i++) await new Promise(resolve => setTimeout(resolve, 10));
    assert.equal(server.captures[0].closed, true);
  } finally { await server.close(); }
});
