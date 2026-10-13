export function createStore(initial) {
  let state = { ...initial };
  const listeners = new Set();
  return {
    get() { return state; },
    set(patch) {
      state = { ...state, ...patch };
      for (const listener of listeners) listener(state);
    },
    subscribe(listener) { listeners.add(listener); return () => listeners.delete(listener); }
  };
}

// A response from an older request must never restore a previous scope or login.
export function createRequestGate() {
  let revision = 0;
  return {
    begin() {
      const current = ++revision;
      return () => current === revision;
    },
    invalidate() { revision++; }
  };
}
