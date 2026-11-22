/** How long a copy button says what happened before it goes back to its own words. */
const sayForMs = 2000;

/**
 * What a component's copy buttons say after a press: Copied, or Could not copy where the browser gives no clipboard, for
 * two seconds. One per component, made while it initialises (it clears its timer when the component goes); `key` tells
 * apart the buttons that share it.
 */
export class CopyFeedback {
  /** The last press, until two seconds have passed. */
  last = $state<{ key: string; ok: boolean } | null>(null);
  #timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    $effect(() => () => clearTimeout(this.#timer));
  }

  /** Runs `write`, then says whether it worked. */
  async copy(key: string, write: () => Promise<void>) {
    let ok = true;
    try {
      await write();
    } catch {
      ok = false;
    }
    clearTimeout(this.#timer);
    this.last = { key, ok };
    this.#timer = setTimeout(() => (this.last = null), sayForMs);
  }

  /** Copies plain text. */
  copyText(key: string, text: string) {
    return this.copy(key, () => navigator.clipboard.writeText(text));
  }

  /** Whether the button `key` copied, for its check mark. */
  copied(key: string) {
    return this.last?.key === key && this.last.ok;
  }

  /** What the button `key` says: its own words, or what its last press did. */
  said(key: string, label: string) {
    return this.last?.key === key ? (this.last.ok ? 'Copied' : 'Could not copy') : label;
  }
}
