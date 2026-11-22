// Our addition: whether a focus is the person's own keyboard move. A tooltip opens on hover and on keyboard focus, never
// when code moves the focus (a sheet's first field, a view that settles, the focus coming back when a menu or sheet
// closes). bits-ui's `ignoreNonKeyboardFocus` asks `:focus-visible`, which browsers also match for a script's focus after
// any key press, Enter and Escape included, so a sheet opened with Enter still showed its Close tooltip. A focus counts
// here only while the default action of a key that moves focus runs: Tab, and the arrows, Home and End a toolbar moves by.

const focusKeys = new Set(['Tab', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End']);

let keyMoving = false;
let codeFocus = false;
let listening = false;

/** Starts listening on the document, once. Safe to call from every tooltip. */
export function listenForFocusIntent(doc: Document | undefined = globalThis.document): void {
  if (listening || !doc) return;
  listening = true;
  doc.addEventListener(
    'keydown',
    (event) => {
      keyMoving = focusKeys.has(event.key);
      // The key's focus move happens in this task: after it, a focus is code's again.
      if (keyMoving) setTimeout(() => (keyMoving = false));
    },
    true,
  );
  doc.addEventListener('pointerdown', () => (keyMoving = false), true);
  // `focus` does not bubble, but it reaches the document in the capture phase, before the trigger's own handler.
  doc.addEventListener(
    'focus',
    () => {
      if (keyMoving) return;
      codeFocus = true;
      queueMicrotask(() => (codeFocus = false));
    },
    true,
  );
}

/** True while a focus that no key moved is being handled: a tooltip asked to open now stays shut. */
export const isCodeFocus = (): boolean => codeFocus;
