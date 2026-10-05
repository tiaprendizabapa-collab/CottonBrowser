(() => {
  "use strict";

  const date = document.querySelector("#local-date");
  const time = document.querySelector("#local-time");
  const title = document.querySelector("#welcome-title");
  let userName = "";

  function updateLocalTime() {
    const now = new Date();
    const hour = now.getHours();
    const greeting = hour < 12 ? "Bom dia" : hour < 18 ? "Boa tarde" : "Boa noite";
    const formattedDate = new Intl.DateTimeFormat("pt-BR", {
      weekday: "long",
      day: "numeric",
      month: "long"
    }).format(now);

    date.textContent = formattedDate.charAt(0).toLocaleUpperCase("pt-BR") + formattedDate.slice(1);
    time.textContent = new Intl.DateTimeFormat("pt-BR", {
      hour: "2-digit",
      minute: "2-digit"
    }).format(now);
    title.textContent = userName ? `${greeting}, ${userName}.` : `${greeting}.`;
  }

  window.addEventListener("cottonbrowser-user-name", (event) => {
    if (typeof event.detail !== "string") return;
    userName = event.detail.trim().slice(0, 64);
    updateLocalTime();
  });

  window.addEventListener("cottonbrowser-home-settings", (event) => {
    const settings = event.detail;
    if (!settings || typeof settings !== "object") return;
    document.body.dataset.homeBackground = settings.background;
    document.querySelector('.date-line').hidden = !settings.showClock;
    title.hidden = !settings.showGreeting;
    document.querySelector('.welcome-copy').hidden = !settings.showGreeting;
    document.querySelector('.quick-section').hidden = !settings.showShortcuts;
    const links = document.querySelector('.quick-links');
    links.replaceChildren();
    for (const entry of (settings.shortcuts || []).slice(0, 12)) {
      let url; try { url = new URL(entry.url); } catch { continue; }
      if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) continue;
      const link = document.createElement('a'); link.className = 'quick-link'; link.href = url.href;
      const mark = document.createElement('span'); mark.className = 'quick-mark mark-google'; mark.setAttribute('aria-hidden','true'); mark.textContent = entry.title.slice(0,2).toUpperCase();
      const label = document.createElement('span'); label.className = 'quick-label'; label.textContent = entry.title;
      const arrow = document.createElement('span'); arrow.className = 'quick-arrow'; arrow.textContent = '↗'; arrow.setAttribute('aria-hidden','true');
      link.append(mark,label,arrow); links.append(link);
    }
  });

  updateLocalTime();
  window.setInterval(updateLocalTime, 30_000);
})();
