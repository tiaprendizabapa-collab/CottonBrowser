// Complemento ao uBlock Origin Lite: não altera o tempo, áudio ou velocidade do vídeo.
(() => {
    'use strict';
    if (!(location.hostname === 'youtube.com' || location.hostname.endsWith('.youtube.com')) ||
        window.__LEAN_YOUTUBE_PROTECTION__) return;
    window.__LEAN_YOUTUBE_PROTECTION__ = true;
    const style = document.createElement('style');
    style.textContent = `
        ytd-ad-slot-renderer, ytd-display-ad-renderer, ytd-promoted-sparkles-web-renderer,
        ytd-promoted-video-renderer, ytd-in-feed-ad-layout-renderer,
        ytd-banner-promo-renderer, #player-ads, #masthead-ad,
        .ytp-ad-overlay-container { display: none !important; }
    `;
    let timer = null;
    const lastClick = new WeakMap();
    function scan() {
        timer = null;
        if (!style.isConnected && document.documentElement) document.documentElement.appendChild(style);
        const player = document.querySelector('#movie_player.ad-showing, #movie_player.ad-interrupting');
        if (!player) return;
        for (const button of player.querySelectorAll('.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button')) {
            const computed = getComputedStyle(button);
            if (button.disabled || button.getAttribute('aria-disabled') === 'true' ||
                button.getClientRects().length === 0 || computed.visibility === 'hidden' ||
                computed.display === 'none' || Number(computed.opacity) === 0) continue;
            const now = Date.now();
            if (now - (lastClick.get(button) || 0) < 1500) continue;
            lastClick.set(button, now);
            button.click();
            break;
        }
    }
    function schedule() {
        if (timer === null) timer = setTimeout(scan, 120);
    }
    const observer = new MutationObserver(schedule);
    function start() {
        observer.observe(document, { subtree: true, childList: true, attributes: true,
            attributeFilter: ['class', 'style', 'disabled', 'aria-disabled'] });
        schedule();
    }
    document.addEventListener('yt-navigate-finish', schedule);
    window.addEventListener('pageshow', start);
    window.addEventListener('pagehide', () => {
        observer.disconnect();
        if (timer !== null) clearTimeout(timer);
        timer = null;
    });
    start();
})();
