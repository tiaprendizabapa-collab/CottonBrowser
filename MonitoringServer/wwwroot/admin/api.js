export class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

const copy = value => structuredClone(value);
const initialDraft = scope => ({
  id: crypto.randomUUID(), scope,
  name: scope === 'global' ? 'Política global' : 'Política do departamento',
  revision: 0,
  urls: { allowed: [], blocked: [] }, extensions: [],
  dlp: { blockExeDownloads: false, blockUnapprovedUploads: false },
  branding: { logoUrl: null, primaryColor: '#315de8', mandatoryBookmarks: [] },
  updatedAt: new Date().toISOString()
});

export function createDemoApi() {
  const drafts = new Map();
  const pause = () => new Promise(resolve => setTimeout(resolve, 180));
  return {
    async getPolicy(scope) {
      await pause();
      if (!drafts.has(scope)) drafts.set(scope, initialDraft(scope));
      return copy(drafts.get(scope));
    },
    async savePolicy(scope, request) {
      await pause();
      const current = drafts.get(scope) ?? initialDraft(scope);
      if (request.expectedRevision !== current.revision)
        throw new ApiError('O rascunho foi alterado por outra sessão.', 409);
      const next = { ...copy(request), id: current.id, scope,
        revision: current.revision + 1, updatedAt: new Date().toISOString() };
      delete next.expectedRevision;
      drafts.set(scope, next);
      return copy(next);
    },
    async getActiveSites() { return { version: 1, allowedOrigins: [], appliedAt: null }; },
    async applySites() { throw new ApiError('A demonstração não aplica regras no navegador.', 400); }
  };
}

export function createHttpApi(getToken) {
  async function request(method, scope, body) {
    const response = await fetch(`/api/admin/policy-drafts?scope=${encodeURIComponent(scope)}`, {
      method,
      credentials: 'omit', cache: 'no-store',
      headers: { Authorization: `Bearer ${getToken()}`,
        ...(body ? { 'Content-Type': 'application/json' } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {})
    });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) {
      const message = Array.isArray(result.errors) ? result.errors.join(' ') :
        result.error ?? `Falha na API (${response.status}).`;
      throw new ApiError(message, response.status);
    }
    return result;
  }
  return {
    getPolicy: scope => request('GET', scope),
    savePolicy: (scope, draft) => request('PUT', scope, draft),
    async getActiveSites() {
      const response = await fetch('/api/admin/site-exceptions', {
        credentials: 'omit', cache: 'no-store',
        headers: { Authorization: `Bearer ${getToken()}` }
      });
      if (!response.ok) throw new ApiError(`Não foi possível consultar as exceções (${response.status}).`, response.status);
      return response.json();
    },
    async applySites(expectedRevision) {
      const response = await fetch('/api/admin/site-exceptions/apply', {
        method: 'POST', credentials: 'omit', cache: 'no-store',
        headers: { Authorization: `Bearer ${getToken()}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedRevision })
      });
      if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new ApiError(error.error ?? `Não foi possível ativar os sites (${response.status}).`, response.status);
      }
      return response.json();
    }
  };
}
