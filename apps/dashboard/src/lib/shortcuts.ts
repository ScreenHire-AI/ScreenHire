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
