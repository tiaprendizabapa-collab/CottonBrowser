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

  updateLocalTime();
  window.setInterval(updateLocalTime, 30_000);
})();
