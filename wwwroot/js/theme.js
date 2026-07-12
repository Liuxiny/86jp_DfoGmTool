(function () {
  'use strict';

  const STORAGE_KEY = 'dfo-gm-theme';
  const DEFAULT_THEME = 'blue';
  const VALID_THEMES = new Set(['white', 'sky', 'black', 'blue']);
  const LIGHT_THEMES = new Set(['white', 'sky']);

  function readStoredTheme() {
    try {
      const value = localStorage.getItem(STORAGE_KEY);
      return VALID_THEMES.has(value) ? value : DEFAULT_THEME;
    } catch (_) {
      return DEFAULT_THEME;
    }
  }

  function applyTheme(theme, persist) {
    const next = VALID_THEMES.has(theme) ? theme : DEFAULT_THEME;
    document.documentElement.dataset.theme = next;
    document.documentElement.style.colorScheme = LIGHT_THEMES.has(next) ? 'light' : 'dark';

    document.querySelectorAll('[data-theme-option]').forEach((button) => {
      const active = button.dataset.themeOption === next;
      button.classList.toggle('active', active);
      button.setAttribute('aria-pressed', String(active));
    });

    if (persist) {
      try {
        localStorage.setItem(STORAGE_KEY, next);
      } catch (_) {
        // Theme switching still works when storage is unavailable.
      }
    }
  }

  function bindThemeSwitcher() {
    document.querySelectorAll('[data-theme-option]').forEach((button) => {
      button.addEventListener('click', () => applyTheme(button.dataset.themeOption, true));
    });
    applyTheme(readStoredTheme(), false);
  }

  applyTheme(readStoredTheme(), false);
  window.DfoTheme = { bind: bindThemeSwitcher, set: applyTheme };
}());
