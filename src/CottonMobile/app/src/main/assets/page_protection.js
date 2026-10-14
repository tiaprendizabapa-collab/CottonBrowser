/* Conservative cosmetic rules. CSS also covers dynamically inserted slots without DOM polling. */
(() => {
  'use strict';
  if (!/^https?:$/.test(location.protocol)) return;
  if (window.__cottonPageProtectionV1) return;
  Object.defineProperty(window, '__cottonPageProtectionV1', { value: true });
  const id = 'cotton-page-protection';
  function install() {
    if (!document.documentElement || document.getElementById(id)) return;
    const style = document.createElement('style');
    style.id = id;
    style.textContent = [
      'ins.adsbygoogle', 'ins[data-ad-client][data-ad-slot]',
      'iframe[id^="google_ads_iframe"]', 'iframe[id^="aswift_"]',
      '[id^="div-gpt-ad-"]', '[id^="google_ads_iframe"]',
      '#ad-banner', '#ad_banner', '#ad-container', '#ad_container',
      '#ads-container', '#ads_container', '.ad-banner', '.ad_banner',
      '.ad-slot', '.ad_slot', '.adsbygoogle', '.taboola-ad', '.OUTBRAIN'
    ].join(',') + '{display:none!important;min-height:0!important}';
    (document.head || document.documentElement).appendChild(style);
  }
  install();
  document.addEventListener('DOMContentLoaded', install, { once: true });
  window.addEventListener('pageshow', install);
})();
