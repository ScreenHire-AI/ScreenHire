/**
 * Per-browser preferences: the colour theme, whether single-key shortcuts are on, whether a move asks first, each table's
 * sort and whether the bell has opened itself after onboarding. (Skipping a setup step or hiding the checklist is the
 * organization's, saved on the server.) They live in
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
const confirmMovesKey = 'screenhire:confirm-moves';
const sortKey = (table: string) => `screenhire:sort:${table}`;
const setupTourKey = (userId: string) => `screenhire:setup-tour:${userId}`;
const pipelineViewKey = 'screenhire:pipeline-view';

/** How a job's candidates show: a table or the board. */
export type PipelineView = 'table' | 'board';

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

  /**
   * Whether a move into a stage that sends emails or creates tasks asks first, listing what it will do. On until the
   * person ticks "Don't ask again" in that dialog; the account menu turns it back on. One setting for every stage.
   */
  confirmMoves = $state(read(confirmMovesKey) !== 'off');

  setConfirmMoves(enabled: boolean): void {
    this.confirmMoves = enabled;
    write(confirmMovesKey, enabled ? 'on' : 'off');
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

  /**
   * A job's candidates as a table or the board, as the person last chose on any job: some people work from the board. It
   * is theirs, in this browser, with no view to save; a saved view does not hold it.
   */
  pipelineView = $state<PipelineView>(read(pipelineViewKey) === 'board' ? 'board' : 'table');

  setPipelineView(view: PipelineView): void {
    this.pipelineView = view;
    write(pipelineViewKey, view);
  }

  /**
   * Finishing onboarding asks the bell to open once by itself, at its setup steps, so a new Owner finds them. This marks
   * the ask for the person; nothing happens for anyone who has already had it.
   */
  queueSetupTour(userId: string): void {
    if (read(setupTourKey(userId)) === null) write(setupTourKey(userId), 'pending');
  }

  /** Whether the bell should open itself for this person, once: true the first time it asks after onboarding, false ever after. */
  takeSetupTour(userId: string): boolean {
    if (read(setupTourKey(userId)) !== 'pending') return false;
    write(setupTourKey(userId), 'done');
    return true;
  }

  #applyTheme(): void {
    globalThis.document?.documentElement.classList.toggle('dark', this.theme === 'dark');
  }
}

export const preferences = new Preferences();
