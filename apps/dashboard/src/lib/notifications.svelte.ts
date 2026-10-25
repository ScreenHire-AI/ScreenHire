import type { AutomationRunEntry } from '@screenhire/api-client';

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

/** The most the tray shows at once; an older one leaves when a new one arrives. */
export const maxNotifications = 3;

/**
 * The notifications the shell shows in its tray, newest first. They stay until someone dismisses them, because each
 * one holds a link (WCAG 2.2.1). They live in this tab only: the event that raised one is gone after a reload, and
 * the run it came from stays on the application's page.
 */
export class Notifications {
  items = $state<Notification[]>([]);

  /** Adds `notification` once; the same id twice (a redelivered event) shows once. */
  add(notification: Notification): void {
    if (this.items.some((item) => item.id === notification.id)) return;
    this.items = [notification, ...this.items].slice(0, maxNotifications);
  }

  dismiss(id: string): void {
    this.items = this.items.filter((item) => item.id !== id);
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
    id: `mention:${noteId}`,
    kind: 'mention',
    title: `${application.candidate.fullName}, ${application.jobTitle}`,
    detail: authorName ? `${authorName} mentioned you in a note.` : 'You were mentioned in a note.',
    href: `/applications/${application.id}?tab=activity#note-${noteId}`,
  };
}
