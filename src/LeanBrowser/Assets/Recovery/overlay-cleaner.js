(() => {
  'use strict';
  if (window.__BC_CLEANER_113__) return;
  let enabled=false, observer=null, timer=null, scans=0;
  const hidden=new Map();
  const protectedSelector = 'video,audio,iframe,canvas,object,embed,script,.player-area,.player,.one,.jwplayer,.jw-wrapper,.jw-controls,.video-js,[data-player]';
  const normalize=v=>String(v||'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replace(/\s+/g,' ').trim().toLowerCase();
  function protectedNode(el) {
    return !el || el===document.body || el===document.documentElement ||
      el.matches(protectedSelector) || !!el.querySelector(protectedSelector);
  }
  function knownCard(el) {
    const t=normalize(el.textContent);
    if (!t || t.length>1200 || protectedNode(el)) return false;
    const adBlocker=t.includes('ad blocker pro') || t.includes('adblocker pro');
    const extensionCTA=/(adicionar|instalar) (uma |a )?extensao|chrome web store/.test(t);
    // Conjunction, not a generic match on 'popup', 'privacy', 'advert', 'VAST', etc.
    return (adBlocker && extensionCTA) ||
      (t.includes('apoie o projeto') && /doac|doar|pix/.test(t));
  }
  function hide(el) {
    if (!enabled || hidden.has(el) || protectedNode(el) || !knownCard(el)) return;
    const st=getComputedStyle(el);
    const r=el.getBoundingClientRect();
    if (st.display==='none' || r.width<120 || r.height<35) return;
    const explicit=el.matches('dialog,[role="dialog"],.modal,.overlay,.adblocker-pro,.ad-blocker-pro');
    if (!explicit && !['fixed','absolute','sticky'].includes(st.position)) return;
    hidden.set(el,{value:el.style.getPropertyValue('display'),priority:el.style.getPropertyPriority('display')});
    el.style.setProperty('display','none','important');
    // Keep nodes, event handlers, neighboring backdrops and media untouched.
  }
  function scan() {
    timer=null;
    if (!enabled || !document.body) return;
    scans++;
    // Reversible changes: restore a hidden card if it becomes a media container.
    for (const [el,style] of hidden) {
      if (!el.isConnected) { hidden.delete(el); continue; }
      if (protectedNode(el)) { restore(el,style); hidden.delete(el); }
    }
    const candidates=[...document.querySelectorAll('dialog,[role="dialog"],div,section,aside,article')];
    let considered=0;
    for (const el of candidates) {
      if (hidden.has(el) || protectedNode(el)) continue;
      if (!knownCard(el)) continue;
      hide(el);
      if (++considered>=20) break;
    }
  }
  function schedule() {
    if (enabled && timer===null) timer=setTimeout(scan,180);
  }
  function restore(el,style) {
    if (style.value) el.style.setProperty('display',style.value,style.priority);
    else el.style.removeProperty('display');
  }
  function configure(next) {
    next=!!next;
    if (next===enabled) return;
    enabled=next;
    if (!enabled) {
      observer?.disconnect(); observer=null;
      if (timer!==null) clearTimeout(timer);
      timer=null;
      for (const [el,style] of hidden) restore(el,style);
      hidden.clear();
      return;
    }
    observer=new MutationObserver(schedule);
    observer.observe(document,{subtree:true,childList:true,characterData:true});
    schedule();
  }
  window.__BC_CLEANER_113__={configure,stats:()=>({enabled,hiddenCards:hidden.size,scans})};
  if (window.__BC_STATE_113__?.enabled && window.__BC_STATE_113__?.cosmetic) configure(true);
})();
