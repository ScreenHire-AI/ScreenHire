const typingTargets = [
  'input:not([type="checkbox"]):not([type="radio"]):not([type="button"])',
  'textarea',
  'select',
  '[contenteditable=""]',
  '[contenteditable="true"]',
  '[role="combobox"]',
  '[role="listbox"]',
  '[role="menu"]',
  '[role="dialog"]',
].join(', ');

/**
 * True when a key press belongs to a text field, a list box, a menu or a dialog (the command palette), not to a
 * shortcut. A checkbox is not one: X on a row's checkbox still selects.
 */
export function isTypingTarget(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest(typingTargets) !== null;
}

/** True when the key press has a modifier, so it is the browser's or the system's, never a single-key shortcut. */
export function hasModifier(event: KeyboardEvent): boolean {
  return event.ctrlKey || event.metaKey || event.altKey;
}

/** How long after G the second key of a "go to" shortcut counts. */
export const chordTimeoutMs = 1500;

/**
 * "Go to" shortcuts: G, then a letter, within {@link chordTimeoutMs}. Returns a keydown handler that calls `go` with
 * the destination and reports whether it handled the key.
 */
export function createChordHandler(
  destinations: Readonly<Record<string, string>>,
  go: (href: string) => void,
  now: () => number = Date.now,
): (event: KeyboardEvent) => boolean {
  let pressedG = Number.NEGATIVE_INFINITY;
  return (event) => {
    if (hasModifier(event) || isTypingTarget(event.target)) {
      return false;
    }

    const key = event.key.toLowerCase();
    if (now() - pressedG <= chordTimeoutMs && key in destinations) {
      pressedG = Number.NEGATIVE_INFINITY;
      go(destinations[key]!);
      return true;
    }

    pressedG = key === 'g' ? now() : Number.NEGATIVE_INFINITY;
    return key === 'g';
  };
}

/** One shortcut as the `?` dialog lists it. */
export interface Shortcut {
  /** What it does, as a label: "Go to Jobs". */
  label: string;
  /** The key caps, pressed together (`Ctrl`, `K`) or one after the other when `sequence` is set (`G`, `J`). */
  keys: readonly string[];
  sequence?: boolean;
  /** A single-key shortcut, which the person can turn off (WCAG 2.1.4). */
  singleKey: boolean;
}

export interface ShortcutGroup {
  label: string;
  shortcuts: readonly Shortcut[];
}

/** True on Apple platforms, where the command palette is ⌘K. */
const isApple = () => typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform);

/**
 * Every keyboard shortcut in the dashboard, for the `?` dialog (design system section 15). A new shortcut joins this
 * list in the change that adds it.
 */
export function shortcutGroups(
  sections: readonly { label: string; key: string }[],
  apple: boolean = isApple(),
): readonly ShortcutGroup[] {
  return [
    {
      label: 'Anywhere',
      shortcuts: [
        { label: 'Open the command palette', keys: apple ? ['⌘', 'K'] : ['Ctrl', 'K'], singleKey: false },
        { label: 'Show keyboard shortcuts', keys: ['?'], singleKey: true },
        ...sections.map((section) => ({
          label: `Go to ${section.label}`,
          keys: ['G', section.key.toUpperCase()],
          sequence: true,
          singleKey: true,
        })),
      ],
    },
    {
      label: 'Candidate table',
      shortcuts: [
        { label: 'Next candidate', keys: ['J'], singleKey: true },
        { label: 'Previous candidate', keys: ['K'], singleKey: true },
        { label: 'Select the candidate', keys: ['X'], singleKey: true },
        { label: 'Open the candidate', keys: ['Enter'], singleKey: false },
      ],
    },
    {
      label: 'Job pipeline',
      shortcuts: [
        { label: 'Switch between table and board', keys: ['V'], singleKey: true },
        { label: 'Pick up a card to drag it with the arrow keys', keys: ['Space'], singleKey: false },
        { label: 'Move a card to the next stage', keys: ['Shift', '→'], singleKey: false },
        { label: 'Move a card to the previous stage', keys: ['Shift', '←'], singleKey: false },
        { label: 'Open a card', keys: ['Enter'], singleKey: false },
      ],
    },
  ];
}
