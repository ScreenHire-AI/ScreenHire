/**
 * Per-browser preferences: the colour theme, whether single-key shortcuts are on, and each table's sort. They live in
 * localStorage, which can be missing or refuse writes (private windows, blocked site data), so every read and write
 * falls back quietly. Being per browser, a sort does not follow a person to another browser.
 */

export type Theme = 'light' | 'dark';

/** A table's one sorted column (design system L2). */
export interface TableSort {
  id: string;
  desc: boolean;
}

const themeKey = 'screenhire:theme';
const shortcutsKey = 'screenhire:shortcuts';
const sortKey = (table: string) => `screenhire:sort:${table}`;

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

  /** The sort the person last chose for a table, read from storage each time; anything unreadable counts as none. */
  sortFor(table: string): TableSort | undefined {
    try {
      const value: unknown = JSON.parse(read(sortKey(table)) ?? 'null');
      if (typeof value === 'object' && value !== null && 'id' in value && 'desc' in value && typeof value.id === 'string' && typeof value.desc === 'boolean') {
        return { id: value.id, desc: value.desc };
      }
    } catch {
      // Not JSON: someone else's value under our key.
    }
    return undefined;
  }

  setSort(table: string, sort: TableSort): void {
    write(sortKey(table), JSON.stringify({ id: sort.id, desc: sort.desc }));
  }

  #applyTheme(): void {
    globalThis.document?.documentElement.classList.toggle('dark', this.theme === 'dark');
  }
}

export const preferences = new Preferences();
