/**
 * Per-browser preferences: the colour theme and whether single-key shortcuts are on. They live in localStorage, which
 * can be missing or refuse writes (private windows, blocked site data), so every read and write falls back quietly.
 */

export type Theme = 'light' | 'dark';

const themeKey = 'screenhire:theme';
const shortcutsKey = 'screenhire:shortcuts';

function read(key: string): string | null {
  try {
    return globalThis.localStorage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function write(key: string, value: string): void {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    // The preference still applies for this visit.
  }
}

function initialTheme(): Theme {
  const stored = read(themeKey);
  if (stored === 'light' || stored === 'dark') {
    return stored;
  }

  return globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

class Preferences {
  /** Light by default, or the system's choice until the person picks one. The tokens switch on `.dark` (`@screenhire/ui`). */
  theme = $state<Theme>(initialTheme());

  /**
   * Single-key shortcuts (G J, J / K, X, V) can be turned off, for speech input and anyone who presses keys by accident
   * (WCAG 2.1.4). Ctrl K stays on: it has a modifier.
   */
  shortcutsEnabled = $state(read(shortcutsKey) !== 'off');

  constructor() {
    this.#applyTheme();
  }

  setTheme(theme: Theme): void {
    this.theme = theme;
    write(themeKey, theme);
    this.#applyTheme();
  }

  toggleTheme(): void {
    this.setTheme(this.theme === 'dark' ? 'light' : 'dark');
  }

  setShortcutsEnabled(enabled: boolean): void {
    this.shortcutsEnabled = enabled;
    write(shortcutsKey, enabled ? 'on' : 'off');
  }

  #applyTheme(): void {
    globalThis.document?.documentElement.classList.toggle('dark', this.theme === 'dark');
  }
}

export const preferences = new Preferences();
