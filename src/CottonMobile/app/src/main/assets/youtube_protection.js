/* Injected at document start on YouTube origins. No native bridge or network blocking. */
(() => {
  'use strict';

  try {
    const host = location.hostname.toLowerCase();
    if (host !== 'youtube.com' && !host.endsWith('.youtube.com')) return;
    if (window.__cottonYouTubeProtectionV2) return;
    Object.defineProperty(window, '__cottonYouTubeProtectionV2', { value: true });

    const adFields = ['adPlacements', 'adSlots', 'playerAds', 'adBreaks',
      'adBreakHeartbeatParams', 'ad3Module'];
    const originalParse = JSON.parse;
    const originalStringify = JSON.stringify;
    const hasOwn = (object, key) => Object.prototype.hasOwnProperty.call(object, key);

    function isObject(value) {
      return value !== null && typeof value === 'object' && !Array.isArray(value);
    }

    function looksLikePlayer(value) {
      return isObject(value) && (
        hasOwn(value, 'videoDetails') || hasOwn(value, 'streamingData') ||
        (hasOwn(value, 'playabilityStatus') &&
          adFields.some(key => hasOwn(value, key)))
      );
    }

    function stripPlayerAds(player) {
      if (!isObject(player)) return player;
      for (const key of adFields) {
        try { delete player[key]; } catch (_) { /* Read-only player object. */ }
      }
      return player;
    }

    function cleanEmbeddedPlayer(value) {
      if (isObject(value)) return looksLikePlayer(value) ? stripPlayerAds(value) : value;
      if (typeof value !== 'string' ||
          !adFields.some(key => value.includes('"' + key + '"'))) return value;
      try {
        const player = originalParse(value);
        if (!looksLikePlayer(player)) return value;
        stripPlayerAds(player);
        return originalStringify(player);
      } catch (_) {
        return value;
      }
    }

    function cleanCandidate(value) {
      if (value === null || typeof value !== 'object') return value;
      // Mobile responses embed the player under response.contents/... or config.args.
      // Bound the traversal and protect against cycles in site-created objects.
      const stack = [[value, 0]];
      const seen = new WeakSet();
      let budget = 4000;
      while (stack.length && budget-- > 0) {
        const [current, depth] = stack.pop();
        if (!current || typeof current !== 'object' || seen.has(current)) continue;
        seen.add(current);
        if (looksLikePlayer(current)) stripPlayerAds(current);
        for (const key of ['playerResponse', 'player_response', 'ytInitialPlayerResponse']) {
          if (!hasOwn(current, key)) continue;
          try { current[key] = cleanEmbeddedPlayer(current[key]); } catch (_) { /* Immutable result. */ }
        }
        if (depth >= 12) continue;
        for (const key of Object.keys(current)) {
          let child;
          try { child = current[key]; } catch (_) { continue; }
          if (child && typeof child === 'object') stack.push([child, depth + 1]);
        }
      }
      return value;
    }

    function hookInitialData(key) {
      try {
        const descriptor = Object.getOwnPropertyDescriptor(window, key);
        if (descriptor && !descriptor.configurable) {
          cleanCandidate(window[key]);
          return;
        }
        if (descriptor && !hasOwn(descriptor, 'value')) return;
        let current = cleanCandidate(descriptor ? descriptor.value : undefined);
        Object.defineProperty(window, key, {
          configurable: true,
          enumerable: true,
          get() { return current; },
          set(next) { current = cleanCandidate(next); }
        });
      } catch (_) { /* The site may define this property differently. */ }
    }

    for (const key of ['ytInitialPlayerResponse', 'ytInitialData', 'ytplayer']) hookInitialData(key);

    try {
      JSON.parse = function (...args) {
        const value = originalParse.apply(this, args);
        const source = args[0];
        if (typeof source !== 'string' ||
            !adFields.some(key => source.includes(key))) return value;
        return cleanCandidate(value);
      };
    } catch (_) { /* Leave native parsing available. */ }

    function isPlayerEndpoint(value) {
      try {
        const url = new URL(value, location.href);
        return (url.hostname === 'youtube.com' ||
          url.hostname.endsWith('.youtube.com')) &&
          /\/youtubei\/v1\/player\/?$/.test(url.pathname);
      } catch (_) {
        return false;
      }
    }

    function cleanPlayerText(text) {
      if (typeof text !== 'string' || !adFields.some(key => text.includes(key))) return text;
      try { return originalStringify(cleanCandidate(originalParse(text))); }
      catch (_) { return text; }
    }

    // The mobile player can use XHR instead of fetch/Response.json. Patch only
    // completed YouTube player responses; headers, status and binary responses stay native.
    if (typeof XMLHttpRequest !== 'undefined' && XMLHttpRequest.prototype) {
      const proto = XMLHttpRequest.prototype;
      const originalOpen = proto.open;
      const states = new WeakMap();
      try {
        proto.open = function (...args) {
          const result = originalOpen.apply(this, args);
          states.set(this, { url: args[1], raw: undefined, clean: undefined });
          return result;
        };
        for (const key of ['responseText', 'response']) {
          const descriptor = Object.getOwnPropertyDescriptor(proto, key);
          if (!descriptor || !descriptor.get || !descriptor.configurable) continue;
          Object.defineProperty(proto, key, {
            ...descriptor,
            get() {
              const raw = descriptor.get.call(this);
              const state = states.get(this);
              if (!state || this.readyState !== 4 ||
                  !isPlayerEndpoint(this.responseURL || state.url)) return raw;
              if (typeof raw === 'string') {
                if (raw !== state.raw) {
                  state.raw = raw;
                  state.clean = cleanPlayerText(raw);
                }
                return state.clean;
              }
              return this.responseType === 'json' ? cleanCandidate(raw) : raw;
            }
          });
        }
      } catch (_) { /* Some WebView providers expose read-only descriptors. */ }
    }

    if (typeof Response !== 'undefined' && Response.prototype) {
      const originalJson = Response.prototype.json;
      if (typeof originalJson === 'function') {
        try {
          Response.prototype.json = function (...args) {
            const playerEndpoint = isPlayerEndpoint(this.url);
            const result = originalJson.apply(this, args);
            return playerEndpoint ? result.then(cleanCandidate) : result;
          };
        } catch (_) { /* Read-only browser API. */ }
      }

      const originalText = Response.prototype.text;
      if (typeof originalText === 'function') {
        try {
          Response.prototype.text = function (...args) {
            const playerEndpoint = isPlayerEndpoint(this.url);
            const result = originalText.apply(this, args);
            if (!playerEndpoint) return result;
            return result.then(cleanPlayerText);
          };
        } catch (_) { /* Read-only browser API. */ }
      }
    }

    const styleId = 'cotton-youtube-protection';
    const hiddenSlots = [
      'ytd-display-ad-renderer', 'ytd-promoted-video-renderer',
      'ytd-promoted-sparkles-web-renderer', 'ytd-ad-slot-renderer',
      'ytd-in-feed-ad-layout-renderer', 'ytm-promoted-sparkles-web-renderer',
      'ytm-ad-slot-renderer', 'ytm-companion-ad-renderer', 'ytm-promoted-video-renderer',
      'ytm-display-ad-renderer', 'ytm-mealbar-promo-renderer', 'ytm-statement-banner-renderer[is-ad]',
      '#player-ads', '#masthead-ad', '.ytp-ad-overlay-container'
    ];
    function installStyle() {
      try {
        if (!document.documentElement || document.getElementById(styleId)) return;
        const style = document.createElement('style');
        style.id = styleId;
        style.textContent = hiddenSlots.join(',') + '{display:none!important}';
        (document.head || document.documentElement).appendChild(style);
      } catch (_) { /* DOM may not be ready yet. */ }
    }

    const skipSelectors = [
      '.ytp-ad-skip-button', '.ytp-ad-skip-button-modern',
      '.ytp-skip-ad-button', '.ytp-ad-skip-button-container button',
      '.ytp-ad-skip-button-slot button', '.ytm-ad-skip-button',
      '.ytm-ad-skip-button-modern', '.ytm-skip-ad-button',
      '.ytm-ad-skip-button-container button', '.ad-skip-button',
      'button[aria-label="Skip Ads"]', 'button[aria-label="Pular anúncio"]',
      'button[aria-label="Pular anúncios"]'
    ].join(',');
    function clickAvailableSkipButtons() {
      try {
        for (const button of document.querySelectorAll(skipSelectors)) {
          if (button.disabled || button.getAttribute('aria-disabled') === 'true') continue;
          if (!button.getClientRects().length) continue;
          const style = window.getComputedStyle(button);
          if (style.display === 'none' || style.visibility === 'hidden') continue;
          button.click();
        }
      } catch (_) { /* A page transition can remove a button during inspection. */ }
    }

    let scheduled = false;
    function scheduleDomCheck() {
      if (scheduled) return;
      scheduled = true;
      setTimeout(() => {
        scheduled = false;
        installStyle();
        clickAvailableSkipButtons();
      }, 120);
    }

    function pageDataChanged() {
      for (const key of ['ytInitialPlayerResponse', 'ytInitialData', 'ytplayer']) {
        try { cleanCandidate(window[key]); } catch (_) { /* Read-only page data. */ }
      }
      scheduleDomCheck();
    }

    installStyle();
    document.addEventListener('DOMContentLoaded', pageDataChanged, { once: true });
    document.addEventListener('yt-navigate-finish', pageDataChanged);
    document.addEventListener('yt-page-data-updated', pageDataChanged);
    window.addEventListener('pageshow', pageDataChanged);
    try {
      new MutationObserver(scheduleDomCheck).observe(document, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ['disabled', 'aria-disabled', 'class', 'style']
      });
    } catch (_) { /* DOM events still cover normal navigation. */ }
  } catch (_) { /* Protection must never stop page loading. */ }
})();
