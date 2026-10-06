// Only handles a player that never received media metadata. Normal playback,
// paused videos, live streams and videos that have already loaded are untouched.
(() => {
    'use strict';
    if (window !== window.top || location.protocol !== 'https:' ||
        !(location.hostname === 'youtube.com' || location.hostname.endsWith('.youtube.com')) ||
        window.__COTTON_YOUTUBE_PLAYBACK__) return;
    window.__COTTON_YOUTUBE_PLAYBACK__ = true;

    let currentId = '', stalledAt = null, attempted = false, hasLoaded = false, timer = null, panel = null, saved = null;
    const interval = 2000, timeout = 20000;
    function removePanel() { panel?.remove(); panel = null; }
    function reset(id = '') {
        currentId = id; stalledAt = null; attempted = false; hasLoaded = false; saved = null; removePanel();
    }
    function context() {
        const path = location.pathname;
        const id = path === '/watch' ? new URLSearchParams(location.search).get('v') :
            path.startsWith('/shorts/') ? path.split('/')[2] : null;
        const player = document.querySelector('#movie_player');
        const video = player?.querySelector('video');
        if (!id || !/^[\w-]{11}$/.test(id) || !player || !video || player.getClientRects().length === 0) return null;
        const actualId = player.getVideoData?.()?.video_id;
        if (actualId && actualId !== id) return null; // SPA navigation has not installed the new player yet.
        return { id, player, video };
    }
    function permanentFailure(player) {
        const status = player.getPlayerResponse?.()?.playabilityStatus?.status;
        return status && status !== 'OK';
    }
    function restore(video, complete = true) {
        if (!saved || saved.id !== currentId) return;
        video.muted = saved.muted; video.volume = saved.volume; video.playbackRate = saved.rate;
        if (complete) saved = null;
    }
    function recover(ctx) {
        // No restart, cache/cookie removal, ad bypass or reload loop. Retry the
        // current video once via the player's own API, preserving media settings.
        attempted = true;
        if (typeof ctx.player.loadVideoById !== 'function') return false;
        saved = { id: ctx.id, muted: ctx.video.muted, volume: ctx.video.volume, rate: ctx.video.playbackRate };
        try {
            ctx.video.addEventListener('loadedmetadata', () => {
                if (ctx.id === currentId && context()?.video === ctx.video) restore(ctx.video);
            }, { once: true });
            ctx.player.loadVideoById(ctx.id, 0);
            restore(ctx.video, false);
            return true;
        } catch { saved = null; return false; }
    }
    function showPanel(ctx) {
        if (panel?.isConnected) return;
        panel = document.createElement('div');
        panel.setAttribute('role', 'status');
        panel.setAttribute('data-cotton-video-recovery', '');
        panel.style.cssText = 'position:fixed;right:20px;bottom:24px;z-index:2147483647;max-width:340px;padding:14px;border-radius:12px;background:#252535;color:#eee;font:14px Segoe UI,sans-serif;box-shadow:0 4px 20px #0006';
        const message = document.createElement('div');
        message.textContent = 'O vídeo não carregou. Tente novamente sem fechar o navegador.';
        const retry = document.createElement('button');
        retry.type = 'button'; retry.textContent = 'Tentar carregar vídeo';
        retry.style.cssText = 'margin-top:10px;padding:8px 12px;border:0;border-radius:8px;background:#bba4ee;color:#171724;cursor:pointer;font:inherit';
        retry.addEventListener('click', () => {
            const latest = context();
            if (!latest || latest.id !== currentId) { removePanel(); return; }
            if (!recover(latest)) location.reload();
            stalledAt = Date.now(); removePanel();
        });
        const reload = document.createElement('button');
        reload.type = 'button'; reload.textContent = 'Recarregar página';
        reload.style.cssText = 'margin:10px 0 0 8px;padding:8px;border:1px solid #888;border-radius:8px;background:transparent;color:inherit;cursor:pointer;font:inherit';
        reload.addEventListener('click', () => location.reload());
        panel.append(message, retry, reload); document.documentElement.appendChild(panel);
    }
    function scan() {
        try {
            if (document.visibilityState !== 'visible' || !navigator.onLine) { stalledAt = null; return; }
            const ctx = context();
            if (!ctx) { stalledAt = null; removePanel(); return; }
            if (ctx.id !== currentId) reset(ctx.id);
            if (permanentFailure(ctx.player)) { stalledAt = null; removePanel(); return; }
            // HAVE_METADATA is enough: do not restart buffering videos or seek
            // in a video that has started, including streams with infinite duration.
            // YouTube can expose its expected duration even when no media has
            // loaded. Duration alone must not hide a real startup failure.
            if (ctx.video.readyState >= 1 || ctx.video.currentTime > 0) hasLoaded = true;
            if (hasLoaded || ctx.player.getVideoData?.()?.isLive) {
                restore(ctx.video); stalledAt = null; removePanel(); return;
            }
            const state = ctx.player.getPlayerState?.();
            if (state === 2 || state === 0 || state === 5 || (ctx.video.paused && state !== 3 && state !== 1)) {
                stalledAt = null; removePanel(); return;
            }
            if (stalledAt === null) stalledAt = Date.now();
            if (Date.now() - stalledAt < timeout) return;
            if (!attempted) { recover(ctx); stalledAt = Date.now(); }
            else showPanel(ctx);
        } catch { /* A replaced player is picked up on the next sample. */ }
    }
    function start() { if (timer === null) timer = setInterval(scan, interval); }
    document.addEventListener('yt-navigate-start', () => { stalledAt = null; removePanel(); });
    document.addEventListener('yt-navigate-finish', scan);
    document.addEventListener('visibilitychange', () => { stalledAt = null; if (document.visibilityState === 'visible') scan(); });
    window.addEventListener('pageshow', start);
    window.addEventListener('pagehide', () => { clearInterval(timer); timer = null; stalledAt = null; removePanel(); });
    start();
})();
