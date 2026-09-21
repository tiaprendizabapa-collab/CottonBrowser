(() => {
  'use strict';
  if (window.__BC_DIALOGS_113__) return;
  const names = ['alert','confirm','prompt'];
  const originals = Object.fromEntries(names.map(n=>[n,window[n]]));
  const descriptors = Object.fromEntries(names.map(n=>[n,Object.getOwnPropertyDescriptor(window,n)]));
  let mode = 'off';
  let blocked = {alert:0,confirm:0,prompt:0};
  let passed = 0;
  const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase();
  function knownAd(message) {
    if (typeof message !== 'string') return false;
    const t=normalize(message);
    return /ad\s*blocker\s*pro|chrome\s*web\s*store|chromewebstore\.google\.com/.test(t) ||
      /(?:adicionar|instalar)\s+(?:uma\s+|a\s+)?extensao/.test(t);
  }
  const wrappers = Object.fromEntries(names.map(name=>[name,function(...args) {
    const block = mode === 'all' || (mode === 'known' && knownAd(args[0]));
    if (block) {
      blocked[name]++;
      // Never accept an advertising offer. No simulated click on the page.
      return name === 'confirm' ? false : name === 'prompt' ? null : undefined;
    }
    passed++;
    return Reflect.apply(originals[name],window,args);
  }]));
  function configure(next) {
    if (!['off','known','all'].includes(next)) return;
    mode=next;
    for (const name of names) {
      try {
        if (mode === 'off') {
          if (window[name] === wrappers[name]) {
            if (descriptors[name]) Object.defineProperty(window,name,descriptors[name]);
            else window[name]=originals[name];
          }
        } else if (window[name] === originals[name]) {
          Object.defineProperty(window,name,{
            ...(descriptors[name] || {configurable:true,enumerable:true,writable:true}),
            value:wrappers[name]
          });
        }
      } catch { /* A competing extension/page may own the property. */ }
    }
  }
  Object.defineProperty(window,'__BC_DIALOGS_113__',{
    configurable:true,
    value:Object.freeze({configure,stats:()=>({mode,blocked:{...blocked},passed,
      installed:names.filter(n=>window[n]===wrappers[n])})})
  });
})();
