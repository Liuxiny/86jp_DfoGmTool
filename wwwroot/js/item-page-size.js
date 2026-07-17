(function () {
  const KEY = 'dfo-gmtool.itemPageSize';
  const OLD_KEYS = ['dfo-gmtool.givePageSize', 'dfo-gmtool.inventoryPageSize'];
  const VALUES = [10, 15, 20, 25];
  const DEFAULT_VALUE = 10;
  const listeners = new Set();

  function normalize(value) {
    const parsed = parseInt(value, 10);
    if (VALUES.includes(parsed)) return parsed;
    if (parsed === 30) return 25;
    return DEFAULT_VALUE;
  }

  function readStored() {
    const current = localStorage.getItem(KEY);
    if (current != null)
      return normalize(current);

    for (const oldKey of OLD_KEYS) {
      const oldValue = localStorage.getItem(oldKey);
      if (oldValue != null)
        return normalize(oldValue);
    }

    return DEFAULT_VALUE;
  }

  let pageSize = readStored();

  function persist(value) {
    localStorage.setItem(KEY, String(value));
    for (const oldKey of OLD_KEYS)
      localStorage.removeItem(oldKey);
  }

  persist(pageSize);

  function notify() {
    for (const listener of Array.from(listeners))
      listener(pageSize);
  }

  window.ItemPageSize = {
    values: VALUES.slice(),
    get() {
      return pageSize;
    },
    set(value) {
      const next = normalize(value);
      if (next === pageSize) return pageSize;
      pageSize = next;
      persist(pageSize);
      notify();
      return pageSize;
    },
    subscribe(listener) {
      if (typeof listener !== 'function') return () => {};
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
  };
})();
