/* Injected at document start on YouTube origins. No native bridge or network blocking. */
(() => {
  'use strict';

  try {
    const host = location.hostname.toLowerCase();
    if (host !== 'youtube.com' && !host.endsWith('.youtube.com')) return;
    if (window.__cottonYouTubeProtectionV1) return;
    Object.defineProperty(window, '__cottonYouTubeProtectionV1', { value: true });

    const adFields = ['adPlacements', 'adSlots', 'playerAds'];
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
      if (isObject(value)) return stripPlayerAds(value);
      if (typeof value !== 'string' ||
          !adFields.some(key => value.includes('"' + key + '"'))) return value;
      try {
        const player = originalParse(value);
        if (!isObject(player)) return value;
        stripPlayerAds(player);
        return originalStringify(player);
      } catch (_) {
        return value;
      }
    }

    function cleanCandidate(value) {
      if (!isObject(value)) return value;
      if (looksLikePlayer(value)) stripPlayerAds(value);
      for (const key of ['playerResponse', 'player_response', 'ytInitialPlayerResponse']) {
        if (!hasOwn(value, key)) continue;
        try { value[key] = cleanEmbeddedPlayer(value[key]); } catch (_) { /* Immutable result. */ }
      }
      if (looksLikePlayer(value.response)) stripPlayerAds(value.response);
      return value;
    }

    function hookInitialPlayerResponse() {
      try {
        const key = 'ytInitialPlayerResponse';
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

    hookInitialPlayerResponse();

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
            return result.then(text => {
              try {
                const player = originalParse(text);
                if (!isObject(player)) return text;
                cleanCandidate(player);
                return originalStringify(player);
              } catch (_) {
                return text;
              }
            });
          };
        } catch (_) { /* Read-only browser API. */ }
      }
    }

    const styleId = 'cotton-youtube-protection';
    const hiddenSlots = [
      'ytd-display-ad-renderer', 'ytd-promoted-video-renderer',
      'ytd-promoted-sparkles-web-renderer', 'ytd-ad-slot-renderer',
      'ytd-in-feed-ad-layout-renderer', 'ytm-promoted-sparkles-web-renderer',
      'ytm-ad-slot-renderer', 'ytm-companion-ad-renderer',
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

    installStyle();
    document.addEventListener('DOMContentLoaded', scheduleDomCheck, { once: true });
    document.addEventListener('yt-navigate-finish', scheduleDomCheck);
    document.addEventListener('yt-page-data-updated', scheduleDomCheck);
    window.addEventListener('pageshow', scheduleDomCheck);
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
