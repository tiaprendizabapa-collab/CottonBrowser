import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { createStore, createRequestGate } from '../../src/MonitoringServer/wwwroot/admin/state.js';

const source = await readFile(new URL('../../src/MonitoringServer/wwwroot/admin/app.js', import.meta.url), 'utf8');
const draft = (scope, revision = 0) => ({ scope, revision, name: scope,
  urls: { allowed: [], blocked: [] }, extensions: [], dlp: {}, branding: {} });
const activeSites = { allowedOrigins: [] };

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

async function dashboard() {
  const elements = new Map();
  const element = id => {
    if (!elements.has(id)) elements.set(id, { value: '', handlers: {},
      addEventListener(name, callback) { this.handlers[name] = callback; } });
    return elements.get(id);
  };
  element('#content').querySelector = element;
  const calls = { getPolicy: [], getActiveSites: [], savePolicy: [], applySites: [] };
  const api = Object.fromEntries(Object.keys(calls).map(name => [name, (...args) => {
    const request = { ...deferred(), args };
    calls[name].push(request);
    return request.promise;
  }]));
  class ApiError extends Error { constructor(message, status) { super(message); this.status = status; } }
  let store, actions;
  const context = vm.createContext({
    document: { querySelector: element, querySelectorAll: () => [] },
    window: { addEventListener() {} }, location: { hash: '' }, confirm: () => true
  });
  const exportsByModule = {
    './state.js': { createStore(initial) { store = createStore(initial); return store; }, createRequestGate },
    './api.js': { createDemoApi: () => api, createHttpApi: () => api, ApiError },
    './overview.js': { renderOverview(root, state, suppliedActions) { actions = suppliedActions; } },
    './security.js': { renderSecurity(root, state, suppliedActions) { actions = suppliedActions; } }
  };
  const app = new vm.SourceTextModule(source, { context });
  await app.link(specifier => {
    const exports = exportsByModule[specifier];
    return new vm.SyntheticModule(Object.keys(exports), function () {
      for (const [name, value] of Object.entries(exports)) this.setExport(name, value);
    }, { context });
  });
  await app.evaluate();
  return { calls, ApiError, get state() { return store.get(); }, get actions() { return actions; },
    connect(token) {
      element('#admin-token').value = token;
      return element('#connect-form').handlers.submit({ preventDefault() {} });
    },
    disconnect() { element('#disconnect').handlers.click(); },
    scope(scope) { return element('#scope-picker').handlers.change({ target: { value: scope } }); },
    completeLoad(index, scope = 'global') {
      calls.getPolicy[index].resolve(draft(scope));
      calls.getActiveSites[index].resolve(activeSites);
    }
  };
}

// Late success must not restore policy data after disconnect.
{
  const app = await dashboard();
  const connecting = app.connect('admin');
  app.disconnect();
  app.completeLoad(0);
  await connecting;
  assert.equal(app.state.token, '');
  assert.equal(app.state.draft, null);
  assert.equal(app.state.loading, false);
}

// An old authentication failure cannot disconnect a newer successful login.
{
  const app = await dashboard();
  const older = app.connect('old-token');
  app.disconnect();
  const newer = app.connect('new-token');
  app.completeLoad(1);
  await newer;
  app.calls.getPolicy[0].reject(new app.ApiError('Invalid token', 401));
  app.calls.getActiveSites[0].resolve(activeSites);
  await older;
  assert.equal(app.state.token, 'new-token');
  assert.equal(app.state.draft.scope, 'global');
}

// Leaving and returning to a scope must still discard its older response.
{
  const app = await dashboard();
  const connecting = app.connect('admin');
  app.completeLoad(0);
  await connecting;
  const first = app.scope('department:ti');
  const second = app.scope('global');
  const third = app.scope('department:ti');
  app.completeLoad(3, 'department:ti');
  await third;
  app.calls.getPolicy[1].resolve(draft('department:ti', 99));
  app.calls.getActiveSites[1].resolve(activeSites);
  app.completeLoad(2);
  await Promise.all([first, second]);
  assert.equal(app.state.draft.scope, 'department:ti');
  assert.equal(app.state.draft.revision, 0);
}

// Save cannot run twice or restore a logged-out draft when it completes.
{
  const app = await dashboard();
  const connecting = app.connect('admin');
  app.completeLoad(0);
  await connecting;
  const saving = app.actions.save();
  await app.actions.save();
  assert.equal(app.calls.savePolicy.length, 1);
  assert.equal(app.state.pending, true);
  app.disconnect();
  app.calls.savePolicy[0].resolve(draft('global', 1));
  await saving;
  assert.equal(app.state.draft, null);
  assert.equal(app.state.pending, false);
}

// A completed activation cannot read or display data in a new session.
{
  const app = await dashboard();
  const connecting = app.connect('admin');
  app.completeLoad(0);
  await connecting;
  const applying = app.actions.applySites();
  await app.actions.applySites();
  assert.equal(app.calls.applySites.length, 1);
  app.disconnect();
  app.calls.applySites[0].resolve({});
  await applying;
  assert.equal(app.calls.getActiveSites.length, 1);
  assert.equal(app.state.activeSites, null);
}

console.log('5 verificações de concorrência e desconexão do Admin passaram.');
