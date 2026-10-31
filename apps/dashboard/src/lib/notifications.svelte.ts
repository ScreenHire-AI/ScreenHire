import type { AutomationRunEntry } from '@screenhire/api-client';
import type { Toast } from '#lib/teammate-changes.js';

/** One notification in the tray: what happened, who it is about, and where to look. */
export interface Notification {
  id: string;
  /** "Ellis Moreno, Platform Engineer". */
  title: string;
  /** The automation's note, or what it did. */
  detail: string;
  href: string;
  /** What raised it, for the card's icon: an automation that notified the team, or a note that mentioned you. */
  kind?: 'team' | 'mention';
}

/** The most the tray shows at once, cards and toasts together; an older one leaves when a new one arrives. */
export const maxNotifications = 3;

/** How long a toast stays while nothing holds it: long enough to read a sentence and its link. */
export const toastLifetime = 8_000;

const tick = 250;

/** One entry in the tray: a card that stays until dismissed, or a toast about a teammate's change. */
export type TrayItem = (Notification & { toast?: undefined }) | (Toast & { toast: true });

/** Whether an entry is a toast, which closes on its own. */
export const isToast = (item: TrayItem): item is Toast & { toast: true } => item.toast === true;

/** Why a toast is not counting down: the pointer is on the tray, or the focus is in it. */
export type Hold = 'pointer' | 'focus';

export interface NotificationsOptions {
  /** Whether toasts close on their own (the person's Close on their own setting). */
  closeToasts?: () => boolean;
  /** Whether something covers the tray now: an open sheet or dialog draws over it, so a toast waits beneath one. */
  covered?: () => boolean;
}

// A modal sheet or dialog (bits-ui marks its content `role="dialog"` with `data-state="open"`), which draws over the tray.
const modalOpen = () => typeof document !== 'undefined' && document.querySelector('[role="dialog"][data-state="open"], [role="alertdialog"][data-state="open"]') !== null;

/**
 * The shell's tray, newest first: notification cards, which stay until someone dismisses them because each holds a link
 * (WCAG 2.2.1), and toasts about teammates' changes, which close after {@link toastLifetime} unless the person turned
 * that off. Cards and toasts share one limit, so the corner never fills the screen. A toast counts down only while
 * nothing holds it: the pointer or the focus in the tray, a hidden tab, or a sheet or dialog over it. It lives in this
 * tab only: the event that raised it is gone after a reload, and the change stays in Activity or History.
 */
// Our own layer rather than svelte-sonner: the tray already owns the corner with its cards, and a second stack there
// would overlap them.
export class Notifications {
  items = $state<TrayItem[]>([]);

  readonly #remaining = new Map<string, number>();
  readonly #holds = new Set<Hold>();
  readonly #closeToasts: () => boolean;
  readonly #covered: () => boolean;
  #timer: ReturnType<typeof setInterval> | undefined;

  constructor(options: NotificationsOptions = {}) {
    this.#closeToasts = options.closeToasts ?? (() => true);
    this.#covered = options.covered ?? modalOpen;
  }

  /** Adds a card once; the same id twice (a redelivered event) shows once. It pushes out the oldest toast first. */
  add(notification: Notification): void {
    if (this.items.some((item) => item.id === notification.id)) return;
    this.#trim([notification, ...this.items]);
  }

  /** Adds a toast once. It pushes out the oldest toast; while three cards are open it does not show, as they need the person. */
  toast(toast: Toast): void {
    if (this.items.some((item) => item.id === toast.id)) return;
    if (this.items.length >= maxNotifications && !this.items.some(isToast)) return;
    this.#remaining.set(toast.id, toastLifetime);
    this.#trim([{ ...toast, toast: true }, ...this.items]);
    this.#start();
  }

  dismiss(id: string): void {
    this.items = this.items.filter((item) => item.id !== id);
    this.#remaining.delete(id);
    if (this.#remaining.size === 0) this.#stop();
  }

  /** Holds the toasts while the pointer or the focus is in the tray, and lets them go on when it leaves. */
  hold(reason: Hold, held: boolean): void {
    if (held) this.#holds.add(reason);
    else this.#holds.delete(reason);
  }

  /** Stops the countdown, for the shell when it closes. */
  dispose(): void {
    this.#stop();
  }

  #trim(items: TrayItem[]): void {
    const kept = [...items];
    while (kept.length > maxNotifications) {
      const oldestToast = kept.findLastIndex(isToast);
      const removed = kept.splice(oldestToast >= 0 ? oldestToast : kept.length - 1, 1)[0]!;
      this.#remaining.delete(removed.id);
    }
    this.items = kept;
    if (this.#remaining.size === 0) this.#stop();
  }

  #held(): boolean {
    return this.#holds.size > 0 || !this.#closeToasts() || (typeof document !== 'undefined' && document.hidden) || this.#covered();
  }

  #start(): void {
    this.#timer ??= setInterval(() => {
      if (this.#held()) return;
      for (const [id, left] of this.#remaining) {
        if (left - tick > 0) this.#remaining.set(id, left - tick);
        else this.dismiss(id);
      }
    }, tick);
  }

  #stop(): void {
    clearInterval(this.#timer);
    this.#timer = undefined;
  }
}

/**
 * The words for a NotifyTeam run (TeamNotified carries ids only, so the shell reads the run and the application
 * first): one per run, the candidate and job as the title, and the action's note, or the automation's name when it has none.
 */
export function teamNotification(
  runId: string,
  run: Pick<AutomationRunEntry, 'automationName' | 'actions'> | undefined,
  application: { id: string; candidate: { fullName: string }; jobTitle: string },
): Notification {
  const note = run?.actions.find((action) => action.action.type === 'NotifyTeam')?.action.note?.trim();
  return {
    id: runId,
    title: `${application.candidate.fullName}, ${application.jobTitle}`,
    detail: note || (run ? `The automation "${run.automationName}" notified the team.` : 'An automation notified the team.'),
    href: `/applications/${application.id}`,
  };
}

/** The tray id of a note's mention card, so the card can leave when the note is deleted. */
export function mentionNotificationId(noteId: string): string {
  return `mention:${noteId}`;
}

/**
 * The words for a mention (TeammateMentioned carries ids only, so the shell reads the application and the team first):
 * one per note, the candidate and job as the title, who mentioned you, and a link to the note on Activity. The note's
 * text is never in the card, as it is never in the email.
 */
export function mentionNotification(
  noteId: string,
  application: { id: string; candidate: { fullName: string }; jobTitle: string },
  authorName: string | undefined,
): Notification {
  return {
    id: mentionNotificationId(noteId),
    kind: 'mention',
    title: `${application.candidate.fullName}, ${application.jobTitle}`,
    detail: authorName ? `${authorName} mentioned you in a note.` : 'You were mentioned in a note.',
    href: `/applications/${application.id}?tab=activity#note-${noteId}`,
  };
}
