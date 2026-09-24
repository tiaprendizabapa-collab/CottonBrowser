import { createStore } from './state.js';
import { createDemoApi, createHttpApi, ApiError } from './api.js';
import { renderOverview } from './overview.js';
import { renderSecurity } from './security.js';

const root = document.querySelector('#content');
const scopePicker = document.querySelector('#scope-picker');
const store = createStore({ page: location.hash === '#security' ? 'security' : 'overview',
  scope: 'global', token: '', demo: false, draft: null, activeSites: null, dirty: false,
  loading: false, message: '', messageType: '', error: '' });
const httpApi = createHttpApi(() => store.get().token);
let demoApi = createDemoApi();

function renderChrome(state) {
  document.querySelector('#page-title').textContent = state.page === 'security' ? 'Controle de segurança' : 'Visão geral';
  document.querySelector('#connection-mode').textContent = state.demo ? 'Demonstração' :
    state.token ? 'Admin conectado' : 'Desconectado';
  document.querySelector('#disconnect').hidden = !state.token && !state.demo;
  scopePicker.disabled = !state.token && !state.demo;
  scopePicker.value = state.scope;
  document.querySelectorAll('[data-page]').forEach(link => {
    link.classList.toggle('active', link.dataset.page === state.page);
    if (link.dataset.page === state.page) link.setAttribute('aria-current', 'page');
    else link.removeAttribute('aria-current');
  });
}
store.subscribe(renderChrome);

function renderConnect() {
  const error = store.get().error;
  root.innerHTML = `<section class="panel connect-card"><h1>Conectar ao Admin</h1><p>Informe o token Admin configurado no servidor. Ele permanece apenas na memória desta aba. Para explorar a interface sem servidor, use a demonstração.</p><form id="connect-form"><label class="field-label" for="admin-token">Token Admin</label><input id="admin-token" type="password" autocomplete="off" required><div class="connect-actions"><button class="button" type="submit">Conectar</button><button id="try-demo" class="button subtle" type="button">Testar demonstração</button></div></form><p id="connect-error" class="status error" role="alert"></p></section>`;
  root.querySelector('#connect-error').textContent = error;
  root.querySelector('#connect-form').addEventListener('submit', async event => {
    event.preventDefault();
    const token = root.querySelector('#admin-token').value.trim();
    store.set({ token, demo: false, error: '' });
    await loadPolicy();
  });
  root.querySelector('#try-demo').addEventListener('click', async () => {
    demoApi = createDemoApi();
    store.set({ token: '', demo: true, error: '' });
    await loadPolicy();
  });
}

function renderPage() {
  const state = store.get();
  if (!state.token && !state.demo) return renderConnect();
  if (state.loading) {
    root.innerHTML = '<p class="status" role="status">Carregando rascunho…</p>';
    return;
  }
  if (!state.draft) {
    root.innerHTML = '<section class="panel"><h1>Não foi possível carregar</h1><p id="load-error" class="status error" role="alert"></p><button id="retry-load" class="button" type="button">Tentar novamente</button></section>';
    root.querySelector('#load-error').textContent = state.error;
    root.querySelector('#retry-load').addEventListener('click', loadPolicy);
    return;
  }
  if (state.page === 'security') renderSecurity(root, state, actions);
  else renderOverview(root, state, actions);
}

async function loadPolicy() {
  const scope = store.get().scope;
  store.set({ loading: true, message: '', error: '' });
  renderPage();
  try {
    const api = store.get().demo ? demoApi : httpApi;
    const [draft, activeSites] = await Promise.all([api.getPolicy(scope), api.getActiveSites()]);
    if (scope !== store.get().scope) return;
    store.set({ draft, activeSites, dirty: false, loading: false });
  } catch (error) {
    if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
      store.set({ token: '', draft: null, activeSites: null, loading: false,
        error: 'Token inválido ou ausente. Confira COTTON_ADMIN_TOKEN.' });
    } else {
      store.set({ loading: false, draft: null, activeSites: null,
        error: error.message || 'Não foi possível carregar o rascunho.' });
    }
  }
  renderPage();
}

const actions = {
  navigate(page) { location.hash = page; },
  markDirty() { store.set({ dirty: true, message: '' }); },
  refresh() { renderPage(); },
  async save() {
    const state = store.get();
    if (!state.draft || !state.draft.name.trim()) {
      store.set({ message: 'Informe um nome para o rascunho.', messageType: 'error' });
      return renderPage();
    }
    store.set({ message: 'Salvando…', messageType: '' });
    renderPage();
    try {
      const draft = state.draft;
      const request = { expectedRevision: draft.revision, name: draft.name,
        urls: draft.urls, extensions: draft.extensions, dlp: draft.dlp, branding: draft.branding };
      const api = state.demo ? demoApi : httpApi;
      const saved = await api.savePolicy(state.scope, request);
      store.set({ draft: saved, dirty: false,
        message: state.demo ? 'Salvo na demonstração desta aba.' :
          'Rascunho salvo. Para aplicar os sites permitidos, clique em Ativar sites neste computador.',
        messageType: 'success' });
    } catch (error) {
      store.set({ message: error.status === 409 ?
        'Conflito de revisão. Copie suas alterações e recarregue o escopo antes de salvar.' :
        error.message || 'Falha ao salvar.', messageType: 'error' });
    }
    renderPage();
  },
  async applySites() {
    const state = store.get();
    if (state.demo || state.scope !== 'global' || state.dirty || !state.draft) {
      store.set({ message: 'Conecte-se, selecione Toda a empresa e salve as alterações antes de ativar.', messageType: 'error' });
      return renderPage();
    }
    store.set({ message: 'Ativando sites neste computador…', messageType: '' });
    renderPage();
    try {
      await httpApi.applySites(state.draft.revision);
      const activeSites = await httpApi.getActiveSites();
      store.set({ activeSites, message: `${activeSites.allowedOrigins.length} site(s) ativo(s) neste computador. Reabra ou atualize a página no CottonBrowser.`, messageType: 'success' });
    } catch (error) {
      store.set({ message: error.message || 'Falha ao ativar sites.', messageType: 'error' });
    }
    renderPage();
  }
};

document.querySelector('#disconnect').addEventListener('click', () => {
  if (store.get().dirty && !confirm('Descartar alterações não salvas?')) return;
  store.set({ token: '', demo: false, draft: null, dirty: false, error: '', message: '' });
  renderPage();
});
scopePicker.addEventListener('change', async event => {
  const current = store.get();
  if (current.dirty && !confirm('Descartar alterações não salvas neste escopo?')) {
    scopePicker.value = current.scope;
    return;
  }
  store.set({ scope: event.target.value, draft: null, dirty: false });
  await loadPolicy();
});
window.addEventListener('hashchange', () => {
  store.set({ page: location.hash === '#security' ? 'security' : 'overview' });
  renderPage();
});
renderChrome(store.get());
renderPage();
