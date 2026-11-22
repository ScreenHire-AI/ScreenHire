/**
 * Preferences the dashboard reads everywhere. Those about the device stay in this browser: the colour theme, reduced
 * motion, each table's sort, the tab an automation opens on, whether the setup checklist was dismissed for an organization and whether the bell has opened
 * itself after onboarding. (Skipping a setup step or hiding the checklist is the organization's, saved on the server.) Those about the person, single-key shortcuts and whether a move asks first, live on the server
 * per organization (Your account, docs/architecture/sign-in-and-tenancy.md): the shell applies the saved values
 * (`applyHabits`) and saves each change (`saveHabitsWith`), and this browser's copy stands in until the server answers. The
 * browser's copy lives in localStorage, which can be missing or refuse writes (private windows, blocked site data), so every
 * read and write falls back quietly.
 */

export type Theme = 'light' | 'dark';

/** The theme the person chose: one of the two, or whatever the system says, followed as it changes. */
export type ThemeChoice = Theme | 'system';

/** The two habits kept on the server. */
export interface Habits {
  shortcutsEnabled: boolean;
  confirmMoves: boolean;
}

/** A table's one sorted column (design system L2). */
export interface TableSort {
  id: string;
  desc: boolean;
}

const themeKey = 'screenhire:theme';
const shortcutsKey = 'screenhire:shortcuts';
const confirmMovesKey = 'screenhire:confirm-moves';
const reduceMotionKey = 'screenhire:reduce-motion';
const sortKey = (table: string) => `screenhire:sort:${table}`;
const setupTourKey = (userId: string) => `screenhire:setup-tour:${userId}`;
const pipelineViewKey = 'screenhire:pipeline-view';
const automationViewKey = 'screenhire:automation-view';
const activityFiltersKey = 'screenhire:activity-filters';

/** The Activity feed's filters as the person last left them: whose jobs, and which kinds (none is every kind). */
export interface ActivityFilters {
  scope?: string;
  kinds: string[];
}

/** How a job's candidates show: a table or the board. */
export type PipelineView = 'table' | 'board';

/** How an automation opens in its sheet: as a sentence or as rules. */
export type AutomationView = 'sentence' | 'rules';

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

function initialThemeChoice(): ThemeChoice {
  const stored = read(themeKey);
  return stored === 'light' || stored === 'dark' ? stored : 'system';
}

const systemDark = () => globalThis.matchMedia?.('(prefers-color-scheme: dark)') ?? null;

const resolve = (choice: ThemeChoice): Theme => (choice === 'system' ? (systemDark()?.matches ? 'dark' : 'light') : choice);

class Preferences {
  /** What the person chose: Light, Dark, or System until they pick one. */
  themeChoice = $state<ThemeChoice>(initialThemeChoice());

  /** The theme showing now. The tokens switch on `.dark` (`@screenhire/ui`). */
  theme = $state<Theme>(resolve(initialThemeChoice()));

  /** Animations and transitions stopped, beyond what the system asks: `.reduce-motion` on the page (design system section 17). */
  reduceMotion = $state(read(reduceMotionKey) === 'on');

  #saveHabits: ((habits: Habits) => void) | undefined;

  /**
   * Single-key shortcuts (G J, J / K, X, V) can be turned off, for speech input and anyone who presses keys by accident
   * (WCAG 2.1.4). Ctrl K stays on: it has a modifier.
   */
  shortcutsEnabled = $state(read(shortcutsKey) !== 'off');

  constructor() {
    this.#applyTheme();
    this.#applyMotion();
    // System follows the system as it changes, with the page open.
    systemDark()?.addEventListener?.('change', () => {
      if (this.themeChoice === 'system') {
        this.theme = resolve('system');
        this.#applyTheme();
      }
    });
  }

  setThemeChoice(choice: ThemeChoice): void {
    this.themeChoice = choice;
    this.theme = resolve(choice);
    write(themeKey, choice);
    this.#applyTheme();
  }

  toggleTheme(): void {
    this.setThemeChoice(this.theme === 'dark' ? 'light' : 'dark');
  }

  setReduceMotion(on: boolean): void {
    this.reduceMotion = on;
    write(reduceMotionKey, on ? 'on' : 'off');
    this.#applyMotion();
  }

  setShortcutsEnabled(enabled: boolean): void {
    this.shortcutsEnabled = enabled;
    write(shortcutsKey, enabled ? 'on' : 'off');
    this.#saveHabits?.(this.habits);
  }

  /** The two habits as they stand. */
  get habits(): Habits {
    return { shortcutsEnabled: this.shortcutsEnabled, confirmMoves: this.confirmMoves };
  }

  /** The person's saved habits from the server; a value never saved there (null) keeps this browser's. */
  applyHabits(saved: { shortcutsEnabled?: boolean | null; confirmMoves?: boolean | null }): void {
    if (typeof saved.shortcutsEnabled === 'boolean') {
      this.shortcutsEnabled = saved.shortcutsEnabled;
      write(shortcutsKey, saved.shortcutsEnabled ? 'on' : 'off');
    }
    if (typeof saved.confirmMoves === 'boolean') {
      this.confirmMoves = saved.confirmMoves;
      write(confirmMovesKey, saved.confirmMoves ? 'on' : 'off');
    }
  }

  /** Where a change to a habit is saved: the shell's save to the server, or nowhere (undefined) once it goes away. */
  saveHabitsWith(save: ((habits: Habits) => void) | undefined): void {
    this.#saveHabits = save;
  }

  /**
   * Whether a move into a stage that sends emails or creates tasks asks first, listing what it will do. On until the
   * person ticks "Don't ask again" in that dialog; the account menu turns it back on. One setting for every stage.
   */
  confirmMoves = $state(read(confirmMovesKey) !== 'off');

  setConfirmMoves(enabled: boolean): void {
    this.confirmMoves = enabled;
    write(confirmMovesKey, enabled ? 'on' : 'off');
    this.#saveHabits?.(this.habits);
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

  /** The Activity feed's filters in this browser; anything unreadable counts as none chosen. */
  activityFilters(): ActivityFilters {
    try {
      const value: unknown = JSON.parse(read(activityFiltersKey) ?? 'null');
      if (typeof value === 'object' && value !== null) {
        const scope = 'scope' in value && typeof value.scope === 'string' ? value.scope : undefined;
        const kinds = 'kinds' in value && Array.isArray(value.kinds) ? value.kinds.filter((kind): kind is string => typeof kind === 'string') : [];
        return { scope, kinds };
      }
    } catch {
      // Not JSON: someone else's value under our key.
    }
    return { kinds: [] };
  }

  setActivityFilters(filters: ActivityFilters): void {
    write(activityFiltersKey, JSON.stringify(filters));
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
   * The tab every automation opens on, Sentence or Rules, as the person last chose one: some people think in rules. It is
   * theirs, in this browser, like the pipeline's table or board. An automation too complex for a sentence opens on Rules
   * whatever this says, and does not change it.
   */
  automationView = $state<AutomationView>(read(automationViewKey) === 'rules' ? 'rules' : 'sentence');

  setAutomationView(view: AutomationView): void {
    this.automationView = view;
    write(automationViewKey, view);
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

  #applyMotion(): void {
    globalThis.document?.documentElement.classList.toggle('reduce-motion', this.reduceMotion);
  }
}

export const preferences = new Preferences();
