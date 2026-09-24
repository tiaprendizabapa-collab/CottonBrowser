function normalizeUrl(value, kind) {
  try {
    const raw = value.trim();
    const host = raw.split('/')[0].split(':')[0].toLowerCase();
    const parts = host.split('.').map(Number);
    const ipv4 = parts.length === 4 && parts.every(part => Number.isInteger(part) && part >= 0 && part <= 255);
    const local = host === 'localhost' || ipv4 && (parts[0] === 10 || parts[0] === 127 ||
      parts[0] === 172 && parts[1] >= 16 && parts[1] <= 31 ||
      parts[0] === 192 && parts[1] === 168);
    const parsed = new URL(raw.includes('://') ? raw : `${local ? 'http' : 'https'}://${raw}`);
    if (!['http:', 'https:'].includes(parsed.protocol) || parsed.username || parsed.password ||
        parsed.search || parsed.hash || raw.length > 2048 || /\s|\\/.test(raw) ||
        parsed.hostname === 'app.cottonbrowser.test') return null;
    return kind === 'allowed' ? `${parsed.origin}/` : parsed.href;
  } catch { return null; }
}

function showMessage(root, message, type = 'error') {
  const element = root.querySelector('#edit-message');
  element.textContent = message;
  element.className = `status ${type}`;
}

function addRow(list, label, onRemove, extra) {
  const row = document.createElement('li');
  const text = document.createElement('span');
  text.textContent = label;
  row.append(text);
  if (extra) row.append(extra);
  const remove = document.createElement('button');
  remove.type = 'button';
  remove.className = 'button danger';
  remove.textContent = 'Remover';
  remove.setAttribute('aria-label', `Remover ${label}`);
  remove.addEventListener('click', onRemove);
  row.append(remove);
  list.append(row);
}

export function renderSecurity(root, state, actions) {
  const draft = state.draft;
  root.innerHTML = `
    <div class="page-head"><div><h1>Controle de segurança</h1><p>Configure sites permitidos neste computador e rascunhos das demais regras.</p></div><span class="pill">Revisão <span id="draft-revision"></span></span></div>
    <div class="notice"><strong id="draft-warning"></strong>Apenas sites permitidos podem ser ativados localmente. URLs bloqueadas, extensões e DLP continuam como rascunhos.</div>
    <div class="two-column">
      <section class="panel"><div class="panel-head"><div><h2>URLs bloqueadas</h2><p>Endereços HTTP(S) completos; sem curingas ou parâmetros.</p></div></div><label class="field-label" for="blocked-input">Adicionar endereço</label><form id="blocked-form" class="form-row"><input id="blocked-input" type="url" placeholder="https://exemplo.com/" required maxlength="2048"><button class="button" type="submit">Adicionar</button></form><ul id="blocked-list" class="entry-list"></ul></section>
      <section class="panel"><div class="panel-head"><div><h2>Sites permitidos</h2><p>Exceção para o bloqueio básico e a troca forçada de HTTP por HTTPS neste computador. Vale para toda a origem, não apenas /login.</p></div></div><label class="field-label" for="allowed-input">Adicionar IP ou endereço</label><form id="allowed-form" class="form-row"><input id="allowed-input" type="text" inputmode="url" placeholder="192.168.3.205/login" required maxlength="2048"><button class="button" type="submit">Adicionar</button></form><p>IPs privados sem protocolo usam HTTP, que não criptografa senhas e dados.</p><ul id="allowed-list" class="entry-list"></ul><div class="site-apply"><div id="applied-sites" class="status"></div><button id="apply-sites" type="button" class="button subtle">Ativar sites neste computador</button></div></section>
    </div>
    <section class="panel"><div class="panel-head"><div><h2>Extensões</h2><p>Cadastre um ID Chromium de 32 caracteres e o estado desejado. Inventário e implantação ainda não disponíveis.</p></div></div><label class="field-label" for="extension-input">ID da extensão</label><form id="extension-form" class="form-row"><input id="extension-input" placeholder="abcdefghijklmnopabcdefghijklmnop" maxlength="32" required><button class="button" type="submit">Adicionar extensão</button></form><ul id="extension-list" class="entry-list"></ul></section>
    <section class="panel"><div class="panel-head"><div><h2>Transferência de dados</h2><p>Preferências de DLP previstas no modelo de política.</p></div></div><label class="toggle-row"><span><strong>Bloquear downloads .exe</strong><small>Rascunho; sem enforcement no cliente nesta entrega.</small></span><input id="block-exe" type="checkbox"></label><label class="toggle-row"><span><strong>Bloquear uploads não autorizados</strong><small>Indisponível até validação técnica da cobertura de uploads.</small></span><input id="block-uploads" type="checkbox" disabled></label></section>
    <section class="panel"><label class="field-label" for="policy-name">Nome do rascunho</label><input id="policy-name" maxlength="80" required><div class="savebar"><div><div id="edit-message" class="status" role="status" aria-live="polite"></div><small class="status">O servidor guarda rascunhos apenas em memória; reiniciar apaga os dados.</small></div><button id="save-policy" class="button" type="button">Salvar rascunho</button></div></section>`;

  root.querySelector('#draft-revision').textContent = String(draft.revision);
  root.querySelector('#draft-warning').textContent = state.demo ?
    'Modo demonstração: dados descartados ao recarregar; nenhuma exceção é aplicada.' :
    'Conectado: salve o rascunho e depois ative a lista de sites.';
  root.querySelector('#policy-name').value = draft.name;
  root.querySelector('#block-exe').checked = draft.dlp.blockExeDownloads;
  const active = state.activeSites?.allowedOrigins ?? [];
  root.querySelector('#applied-sites').textContent = state.demo ? 'Conecte com token Admin para ativar.' :
    state.scope !== 'global' ? 'A ativação local só aceita o escopo Toda a empresa.' :
    `${active.length} site(s) ativo(s) neste computador.`;
  root.querySelector('#apply-sites').disabled = state.demo || state.scope !== 'global' || state.dirty;
  root.querySelector('#apply-sites').addEventListener('click', () => actions.applySites());
  if (state.message) showMessage(root, state.message, state.messageType);

  function markDirty(refresh = false) {
    actions.markDirty();
    if (refresh) actions.refresh();
    else {
      root.querySelector('#apply-sites').disabled = true;
      showMessage(root, 'Alterações não salvas.', '');
    }
  }

  function bindUrls(kind) {
    const list = root.querySelector(`#${kind}-list`);
    const values = draft.urls[kind];
    if (!values.length) {
      const empty = document.createElement('li');
      empty.className = 'empty';
      empty.textContent = 'Nenhum endereço cadastrado.';
      list.append(empty);
    }
    values.forEach((url, index) => addRow(list, url, () => {
      values.splice(index, 1);
      markDirty(true);
    }));
    root.querySelector(`#${kind}-form`).addEventListener('submit', event => {
      event.preventDefault();
      const input = root.querySelector(`#${kind}-input`);
      const url = normalizeUrl(input.value, kind);
      const other = draft.urls[kind === 'allowed' ? 'blocked' : 'allowed'];
      if (!url) return showMessage(root, 'Use um IP ou URL HTTP(S) sem parâmetros, fragmento ou credenciais.');
      if (values.some(item => item.toLowerCase() === url.toLowerCase()) ||
          other.some(item => item.toLowerCase() === url.toLowerCase()))
        return showMessage(root, 'Esta URL já existe em uma das listas.');
      if (values.length >= 200) return showMessage(root, 'Limite de 200 URLs por lista.');
      values.push(url);
      markDirty(true);
    });
  }
  bindUrls('blocked');
  bindUrls('allowed');

  const extensionList = root.querySelector('#extension-list');
  if (!draft.extensions.length) {
    const empty = document.createElement('li');
    empty.className = 'empty';
    empty.textContent = 'Nenhuma extensão cadastrada.';
    extensionList.append(empty);
  }
  draft.extensions.forEach((extension, index) => {
    const label = document.createElement('label');
    label.className = 'form-row';
    const toggle = document.createElement('input');
    toggle.type = 'checkbox';
    toggle.checked = extension.enabled;
    toggle.setAttribute('aria-label', `Habilitar extensão ${extension.id}`);
    toggle.addEventListener('change', () => { extension.enabled = toggle.checked; markDirty(); });
    label.append(toggle, document.createTextNode('Habilitada'));
    addRow(extensionList, extension.id, () => {
      draft.extensions.splice(index, 1);
      markDirty(true);
    }, label);
  });
  root.querySelector('#extension-form').addEventListener('submit', event => {
    event.preventDefault();
    const id = root.querySelector('#extension-input').value.trim().toLowerCase();
    if (!/^[a-p]{32}$/.test(id)) return showMessage(root, 'O ID deve conter 32 caracteres entre a e p.');
    if (draft.extensions.some(item => item.id === id)) return showMessage(root, 'Extensão já cadastrada.');
    if (draft.extensions.length >= 100) return showMessage(root, 'Limite de 100 extensões.');
    draft.extensions.push({ id, enabled: true });
    markDirty(true);
  });
  root.querySelector('#block-exe').addEventListener('change', event => {
    draft.dlp.blockExeDownloads = event.target.checked;
    markDirty();
  });
  root.querySelector('#policy-name').addEventListener('input', event => {
    draft.name = event.target.value;
    markDirty();
  });
  root.querySelector('#save-policy').addEventListener('click', () => actions.save());
}
