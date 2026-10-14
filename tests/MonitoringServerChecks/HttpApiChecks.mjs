import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';

// Only loopback HTTP and in-memory telemetry/department drafts are exercised.
// Never activate local site exceptions or use deployment credentials here.
const reservation = createServer();
await new Promise(resolve => reservation.listen(0, '127.0.0.1', resolve));
const port = reservation.address().port;
await new Promise(resolve => reservation.close(resolve));
const origin = `http://127.0.0.1:${port}`;
const adminToken = randomUUID(), ingestToken = randomUUID();
const server = spawn('dotnet', [fileURLToPath(new URL('../../src/MonitoringServer/bin/Debug/net8.0/MonitoringServer.dll', import.meta.url)),
  '--urls', origin], {
  cwd: fileURLToPath(new URL('../../src/MonitoringServer/', import.meta.url)), windowsHide: true,
  env: { ...process.env, COTTON_ADMIN_TOKEN: adminToken, COTTON_INGEST_TOKEN: ingestToken }
});
let output = '';
server.stdout.on('data', chunk => { output += chunk; });
server.stderr.on('data', chunk => { output += chunk; });
let startError;
server.on('error', error => { startError = error; });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let checks = 0;
async function request(path, expected, token, body, method = body === undefined ? 'GET' : 'POST') {
  const response = await fetch(origin + path, { method, headers: {
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...(body !== undefined ? { 'Content-Type': 'application/json' } : {})
  }, ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
  assert.equal(response.status, expected, `${method} ${path.split('?')[0]}`);
  checks++;
  return response.status === 401 || response.status === 403 ? null : response.json();
}

try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    if (startError) throw startError;
    if (server.exitCode !== null) throw new Error(`Servidor encerrou antes dos checks: ${server.exitCode}`);
    try { ready = (await fetch(origin + '/health')).ok; } catch {}
    if (ready) break;
    await delay(50);
  }
  assert.ok(ready, 'servidor de teste iniciou');
  await request('/api/admin/reports/top-sites', 401);
  await request('/api/admin/reports/top-sites', 403, ingestToken);
  const event = { eventType: 'navigation', userId: 'checks', sessionId: 'isolated',
    url: 'https://example.com/path', domain: 'example.com', searchTerm: null,
    title: 'Example', isActive: true, occurredAt: new Date().toISOString() };
  await request('/api/telemetry/navigation', 400, ingestToken, [null]);
  for (const key of ['eventType', 'userId', 'sessionId', 'url', 'domain'])
    await request('/api/telemetry/navigation', 400, ingestToken, [{ ...event, [key]: null }]);
  await request('/api/telemetry/navigation', 400, ingestToken, [{ ...event, domain: 'spoofed.example' }]);
  await request('/api/telemetry/navigation', 400, ingestToken, [{ ...event, title: 'x'.repeat(301) }]);
  const accepted = await request('/api/telemetry/navigation', 202, ingestToken, [null, event]);
  assert.equal(accepted.accepted, 1);
  const sites = await request('/api/admin/reports/top-sites', 200, adminToken);
  assert.equal(sites[0].domain, 'example.com');
  assert.equal(sites[0].accesses, 1);
  const policy = { expectedRevision: 0, name: 'HTTP checks', urls: { allowed: [], blocked: [] },
    extensions: [], dlp: { blockExeDownloads: false, blockUnapprovedUploads: false },
    branding: { logoUrl: null, primaryColor: '#315de8', mandatoryBookmarks: [] } };
  await request('/api/admin/policy-drafts?scope=department:checks', 403, ingestToken, policy, 'PUT');
  await request('/api/admin/policy-drafts?scope=department:checks', 400, adminToken,
    { ...policy, urls: { allowed: ['https://same.example/'], blocked: ['https://same.example:443'] } }, 'PUT');
  await request('/api/admin/policy-drafts?scope=department:checks', 200, adminToken, policy, 'PUT');
  await request('/api/admin/policy-drafts?scope=department:checks', 409, adminToken, policy, 'PUT');
  await request(`/hubs/navigation/negotiate?negotiateVersion=1&access_token=${adminToken}`, 200, undefined, {});
  await delay(100);
  assert.ok(!output.includes(adminToken) && !output.includes(ingestToken), 'tokens ausentes dos logs');
  checks++;
  console.log(`${checks} verificações HTTP de autenticação, validação e logs passaram.`);
} finally {
  server.kill();
  if (server.exitCode === null)
    await Promise.race([new Promise(resolve => server.once('exit', resolve)), delay(3000)]);
}
