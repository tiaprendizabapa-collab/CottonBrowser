(() => {
  "use strict";

  const TRUSTED_ORIGIN = "https://app.cottonbrowser.test";
  const MAX_URL_LENGTH = 2048;
  const MAX_TITLE_LENGTH = 256;
  const pending = new Map();

  if (window.location.origin !== TRUSTED_ORIGIN || window.top !== window || !window.chrome?.webview) return;

  function httpsUrl(value) {
    if (typeof value !== "string" || value.length === 0 || value.length > MAX_URL_LENGTH) throw new TypeError("URL invalida");
    const url = new URL(value);
    if (url.protocol !== "https:" || url.username || url.password) throw new TypeError("Use uma URL HTTPS");
    return url.href;
  }

  function bookmarkTitle(value) {
    if (typeof value !== "string") throw new TypeError("Titulo invalido");
    const title = value.trim();
    if (!title || title.length > MAX_TITLE_LENGTH || /[\u0000-\u001F\u007F]/.test(title)) throw new TypeError("Titulo invalido");
    return title;
  }

  function request(command, payload) {
    const id = crypto.randomUUID();
    const message = Object.freeze({ version: 1, id, command, payload });
    return new Promise((resolve, reject) => {
      const timer = window.setTimeout(() => {
        pending.delete(id);
        reject(new Error("O navegador nao respondeu a tempo"));
      }, 5000);
      pending.set(id, { resolve, reject, timer });
      try {
        window.chrome.webview.postMessage(message);
      } catch (error) {
        window.clearTimeout(timer);
        pending.delete(id);
        reject(error);
      }
    });
  }

  window.chrome.webview.addEventListener("message", (event) => {
    const response = event.data;
    if (response?.version === 1 && response.event === "permission-requested" && response.data) {
      window.dispatchEvent(new CustomEvent("cottonBrowser:permission-requested", { detail: response.data }));
      return;
    }
    if (!response || response.version !== 1 || typeof response.id !== "string" || typeof response.ok !== "boolean") return;
    const operation = pending.get(response.id);
    if (!operation) return;
    window.clearTimeout(operation.timer);
    pending.delete(response.id);
    if (response.ok) operation.resolve(Object.freeze({ code: response.code, data: response.data }));
    else operation.reject(new Error(typeof response.code === "string" ? response.code : "operation-failed"));
  });

  Object.defineProperty(window, "cottonBrowser", {
    configurable: false,
    enumerable: false,
    writable: false,
    value: Object.freeze({
      openTab(url) {
        return request("open-tab", Object.freeze({ url: httpsUrl(url) }));
      },
      saveBookmark(url, title) {
        return request("save-bookmark", Object.freeze({ url: httpsUrl(url), title: bookmarkTitle(title) }));
      },
      listPendingPermissions() {
        return request("list-pending-permissions", Object.freeze({}));
      },
      resolvePermission(id, allow) {
        if (typeof id !== "string" || typeof allow !== "boolean") throw new TypeError("Decisao invalida");
        return request("resolve-permission", Object.freeze({ id, allow }));
      }
    })
  });
})();
