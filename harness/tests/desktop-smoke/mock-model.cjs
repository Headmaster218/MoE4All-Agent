'use strict';

const http = require('node:http');
const { randomUUID } = require('node:crypto');

// Synthetic requests only: never connect this test server to a production profile.
async function startMockModel(port = 0) {
  const captures = [];
  const sockets = new Set();
  const timers = new Set();
  const server = http.createServer(async (req, res) => {
    const json = (status, body) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); };
    if (req.method === 'GET' && req.url === '/health') return json(200, { status: 'ok' });
    if (req.method === 'GET' && req.url === '/v1/models') return json(200, { object: 'list', data:
      ['Qwen3.6-35B-A3B-QA', 'Qwen3.8-Flash-Next-QA'].map(id => ({ id, object: 'model', owned_by: 'qa' })) });
    if (req.method === 'GET' && req.url === '/__qa/captures') return json(200, captures);
    if (req.method !== 'POST' || req.url !== '/v1/chat/completions') return json(404, { error: { message: 'Unknown QA route' } });
    try {
      const chunks = [];
      let size = 0;
      for await (const chunk of req) {
        size += chunk.length;
        if (size > 2 * 1024 * 1024) return json(413, { error: { message: 'QA request too large' } });
        chunks.push(chunk);
      }
      const body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
      const messages = Array.isArray(body.messages) ? body.messages : [];
      const text = content => typeof content === 'string' ? content : Array.isArray(content)
        ? content.filter(part => part.type === 'text').map(part => part.text).join('\n') : '';
      const lastUserIndex = messages.findLastIndex(message => message.role === 'user');
      const lastUser = text(messages[lastUserIndex]?.content);
      const hasToolResult = messages.slice(lastUserIndex + 1).some(message => message.role === 'tool');
      const calledTools = messages.slice(lastUserIndex + 1).flatMap(message => (message.tool_calls ?? []).map(call => call.function?.name));
      const toolNames = (body.tools ?? []).map(tool => tool.function?.name).filter(Boolean);
      const capture = { model: body.model, stream: body.stream, lastUser: lastUser.slice(0, 4000),
        chatTemplateKwargs: body.chat_template_kwargs, reasoningEffort: body.reasoning_effort,
        tools: toolNames, hasToolResult, closed: false };
      captures.push(capture);
      if (captures.length > 256) captures.shift();
      const auxiliary = toolNames.length === 0;
      let tool;
      if (!auxiliary) {
        if (lastUser.includes('QA_LIST_DIRECTORY') && !hasToolResult) tool = { name: 'list_directory', arguments: { path: '.', limit: 10 } };
        if (lastUser.includes('QA_WRITE') && !calledTools.includes('read')) tool = { name: 'read', arguments: { file_path: 'sub/qa-result.md' } };
        else if (lastUser.includes('QA_WRITE') && !calledTools.includes('write')) tool = { name: 'write', arguments: { file_path: 'sub/qa-result.md', content: '# QA result\n\nVerified through the real Agent tool.\n' } };
        if (lastUser.includes('QA_ASK') && !hasToolResult) tool = { name: 'ask_user_question', arguments: { questions: [{
          id: 'qa-question', question: 'QA: choose the next action', options: [{ label: 'Continue QA' }, { label: 'Cancel QA' }],
        }] } };
      }
      if (tool && !toolNames.includes(tool.name)) return json(400, { error: { message: `Required QA tool unavailable: ${tool.name}` } });
      const id = `chatcmpl-${randomUUID()}`;
      const finalText = auxiliary ? 'QA session' : hasToolResult ? 'QA_TOOL_DONE [result](sub/qa-result.md)' : 'QA_REPLY_OK';
      if (!body.stream) return json(200, { id, object: 'chat.completion', created: 1, model: body.model,
        choices: [{ index: 0, message: { role: 'assistant', content: finalText }, finish_reason: 'stop' }],
        usage: { prompt_tokens: 10, completion_tokens: 4, total_tokens: 14 } });
      res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache', connection: 'keep-alive' });
      res.flushHeaders();
      const send = (delta, finish = null) => res.write(`data: ${JSON.stringify({ id, object: 'chat.completion.chunk', created: 1,
        model: body.model, choices: [{ index: 0, delta, finish_reason: finish }] })}\n\n`);
      let timer;
      const finish = () => {
        if (timer) { clearInterval(timer); clearTimeout(timer); timers.delete(timer); }
        capture.closed = true;
      };
      res.on('close', finish);
      send({ role: 'assistant' });
      if (!auxiliary && lastUser.includes('QA_TIMEOUT')) {
        timer = setTimeout(() => res.end(), 15000); timers.add(timer);
        return;
      }
      if (tool) {
        send({ tool_calls: [{ index: 0, id: `call-${randomUUID()}`, type: 'function', function: { name: tool.name, arguments: JSON.stringify(tool.arguments) } }] });
        send({}, 'tool_calls'); res.end('data: [DONE]\n\n');
        return;
      }
      const streaming = !auxiliary && lastUser.includes('QA_STREAM');
      const parts = streaming ? Array.from({ length: 600 }, (_, i) => `QA_STREAM_${i} `) : [finalText];
      let index = 0;
      timer = setInterval(() => {
        if (res.destroyed) return finish();
        if (index < parts.length) { send({ content: parts[index++] }); return; }
        finish(); send({}, 'stop'); res.end('data: [DONE]\n\n');
      }, streaming ? 25 : 5);
      timers.add(timer);
    } catch (error) {
      if (!res.headersSent) json(400, { error: { message: String(error) } });
      else res.destroy();
    }
  });
  server.on('connection', socket => { sockets.add(socket); socket.on('close', () => sockets.delete(socket)); });
  await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolve); });
  return { url: `http://127.0.0.1:${server.address().port}`, captures,
    async close() {
      for (const timer of timers) { clearInterval(timer); clearTimeout(timer); }
      for (const socket of sockets) socket.destroy();
      await new Promise(resolve => server.close(resolve));
    } };
}

module.exports = { startMockModel };
