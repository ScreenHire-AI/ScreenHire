import { undoMilliseconds } from '#lib/tasks.svelte.js';

/**
 * One offer to undo what the person just did, shown by `UndoNotice` for 10 seconds (design system section 11, Undo
 * notice). The change has already happened on the server; Undo reverses it there. A second offer replaces the first,
 * whose time then runs out. `undo` returns a sentence when it fails, which the notice shows in place of the offer.
 */
export class UndoOffer {
  /** What was done, in a sentence: `Deleted "Senior engineers".` Empty while there is nothing to undo. */
  message = $state('');
  /** Why the last Undo failed, or empty. */
  failure = $state('');
  /** Set while Undo runs, so a second press does nothing. */
  working = $state(false);

  #undo: (() => Promise<string | null>) | undefined;
  #timer: ReturnType<typeof setTimeout> | undefined;
  #held = false;
  readonly #milliseconds: number;

  constructor(milliseconds = undoMilliseconds) {
    this.#milliseconds = milliseconds;
  }

  get open(): boolean {
    return this.message !== '' || this.failure !== '';
  }

  offer(message: string, undo: () => Promise<string | null>): void {
    // The card stays in the page when one offer replaces another, so a person holding it is still holding it.
    const held = this.#held && this.open;
    this.dismiss();
    this.message = message;
    this.#undo = undo;
    this.#held = held;
    if (!held) this.#timer = setTimeout(() => this.dismiss(), this.#milliseconds);
  }

  async run(): Promise<void> {
    const undo = this.#undo;
    if (!undo || this.working) return;
    clearTimeout(this.#timer);
    this.working = true;
    const failure = await undo();
    this.working = false;
    // A newer offer came in while this one ran. It keeps the card.
    if (this.#undo !== undo) return;
    this.#undo = undefined;
    this.#held = false;
    this.message = '';
    this.failure = failure ?? '';
    // A failure stays a little longer than the offer did, then goes.
    if (failure) this.#timer = setTimeout(() => this.dismiss(), this.#milliseconds);
  }

  /** Stops the clock while the person points at or focuses the notice, so there is time to reach it (WCAG 2.2.1). */
  hold(): void {
    this.#held = true;
    clearTimeout(this.#timer);
    this.#timer = undefined;
  }

  /** Starts the clock again, from the full time, once the person leaves the notice. */
  release(): void {
    this.#held = false;
    if (this.open && this.#timer === undefined && !this.working) this.#timer = setTimeout(() => this.dismiss(), this.#milliseconds);
  }

  dismiss(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    this.#held = false;
    this.#undo = undefined;
    this.message = '';
    this.failure = '';
  }
}
