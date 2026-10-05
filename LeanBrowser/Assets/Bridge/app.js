(() => {
  "use strict";

  const status = document.querySelector("#status");
  const permissions = document.querySelector("#permissions");
  const show = (message) => { status.textContent = message; };

  document.querySelector("#open-tab-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const url = new FormData(event.currentTarget).get("url");
    try {
      await window.cottonBrowser.openTab(url);
      show("Nova aba aberta.");
    } catch {
      show("Nao foi possivel abrir a aba.");
    }
  });

  document.querySelector("#bookmark-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    try {
      await window.cottonBrowser.saveBookmark(form.get("url"), form.get("title"));
      show("Favorito salvo.");
    } catch {
      show("Nao foi possivel salvar o favorito.");
    }
  });

  function renderPermissions(items) {
    permissions.replaceChildren();
    if (!Array.isArray(items) || items.length === 0) {
      permissions.textContent = "Nenhuma permissão pendente.";
      return;
    }
    for (const item of items) {
      if (!item || typeof item.id !== "string" || typeof item.origin !== "string" || typeof item.kind !== "string") continue;
      const row = document.createElement("article");
      const text = document.createElement("p");
      text.textContent = `${item.origin} solicita acesso a ${item.kind}.`;
      const allow = document.createElement("button");
      allow.type = "button";
      allow.textContent = "Permitir uma vez";
      const deny = document.createElement("button");
      deny.type = "button";
      deny.textContent = "Negar";
      const decide = async (value) => {
        allow.disabled = true;
        deny.disabled = true;
        try {
          await window.cottonBrowser.resolvePermission(item.id, value);
          await loadPermissions();
        } catch {
          show("Nao foi possivel registrar a decisao.");
        }
      };
      allow.addEventListener("click", () => { void decide(true); });
      deny.addEventListener("click", () => { void decide(false); });
      row.append(text, allow, deny);
      permissions.append(row);
    }
  }

  async function loadPermissions() {
    try {
      const response = await window.cottonBrowser.listPendingPermissions();
      renderPermissions(response.data);
    } catch {
      permissions.textContent = "Nao foi possivel carregar as permissões.";
    }
  }

  window.addEventListener("cottonBrowser:permission-requested", () => { void loadPermissions(); });
  void loadPermissions();
})();
