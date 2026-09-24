export function renderOverview(root, state, actions) {
  root.innerHTML = `
    <div class="page-head"><div><h1>Visão geral</h1><p>Estrutura inicial de administração do navegador corporativo.</p></div><span class="pill">Piloto técnico</span></div>
    <div class="notice"><strong>Somente sites permitidos podem ser ativados neste computador.</strong>As demais políticas são rascunhos. Não há distribuição para outros dispositivos, confirmação de aplicação ou SSO corporativo.</div>
    <div class="metric-grid">
      <div class="metric"><span>Revisão do rascunho</span><strong id="metric-revision">—</strong><small>Somente neste servidor</small></div>
      <div class="metric"><span>Sites permitidos ativos</span><strong id="metric-sites">—</strong><small>Somente neste computador</small></div>
      <div class="metric"><span>Bloqueios de segurança</span><strong>—</strong><small>Eventos específicos ainda não coletados</small></div>
    </div>
    <section class="panel"><div class="panel-head"><div><h2>Controle de segurança</h2><p>Ative exceções locais para sites permitidos. URLs bloqueadas, extensões e DLP continuam em rascunho.</p></div></div><button id="open-security" class="button" type="button">Abrir controle de segurança</button></section>
    <section class="panel"><div class="panel-head"><div><h2>Monitoramento existente</h2><p>O painel legado continua disponível para os eventos de navegação já configurados.</p></div></div><a class="link" href="/admin.html">Abrir monitoramento atual →</a></section>`;
  root.querySelector('#metric-revision').textContent = state.draft ? String(state.draft.revision) : '—';
  root.querySelector('#metric-sites').textContent = state.demo ? '—' : String(state.activeSites?.allowedOrigins?.length ?? 0);
  root.querySelector('#open-security').addEventListener('click', () => actions.navigate('security'));
}
