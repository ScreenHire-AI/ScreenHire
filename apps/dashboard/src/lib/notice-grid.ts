import type {
  AccountNotifications,
  EmailChoice,
  NoticeKind,
  NoticeRowState,
  NoticeScope,
  ToastKind,
  UpdateAccountNotificationsRequest,
} from '@screenhire/api-client';

/**
 * The notification grid's draft (Your account > Notifications): what the person is choosing before they save. Pure, so the
 * page stays a view of it and the rules are tested on their own.
 */
export interface NoticeDraft {
  scope: NoticeScope;
  pauseToasts: boolean;
  closeToasts: boolean;
  toastsOff: ToastKind[];
  rows: { kind: NoticeKind; toasts: readonly ToastKind[]; email: EmailChoice; slack: boolean | null }[];
}

export function draftFrom(notifications: AccountNotifications): NoticeDraft {
  return {
    scope: notifications.scope,
    pauseToasts: notifications.pauseToasts,
    closeToasts: notifications.closeToasts,
    toastsOff: [...notifications.toastsOff],
    rows: notifications.rows.map((row: NoticeRowState) => ({ kind: row.kind, toasts: row.toasts, email: row.email, slack: row.slack })),
  };
}

/** Whether a row's In the app switch is on: every toast it covers is on. A row with no toast has no switch. */
export const inAppOn = (draft: NoticeDraft, toasts: readonly ToastKind[]) => toasts.length > 0 && toasts.every((kind) => !draft.toastsOff.includes(kind));

/** Turns every toast a row covers on or off. */
export function setInApp(draft: NoticeDraft, toasts: readonly ToastKind[], on: boolean): ToastKind[] {
  const rest = draft.toastsOff.filter((kind) => !toasts.includes(kind));
  return on ? rest : [...rest, ...toasts];
}

/** Turns one toast kind on or off, for the kinds inside Teammates' moves and notes. */
export function setToast(draft: NoticeDraft, kind: ToastKind, on: boolean): ToastKind[] {
  const rest = draft.toastsOff.filter((each) => each !== kind);
  return on ? rest : [...rest, kind];
}

/** The PUT body: the whole grid. */
export function requestFrom(draft: NoticeDraft): UpdateAccountNotificationsRequest {
  return {
    scope: draft.scope,
    pauseToasts: draft.pauseToasts,
    closeToasts: draft.closeToasts,
    toastsOff: [...new Set(draft.toastsOff)],
    rows: draft.rows.map((row) => ({ kind: row.kind, email: row.email, slack: row.slack })),
  };
}

/** The words for each toast kind inside Teammates' moves and notes. */
export const teammateToastLabels: Readonly<Partial<Record<ToastKind, string>>> = {
  Moves: 'Moves',
  Notes: 'Notes',
  Tags: 'Tags',
  Emails: 'Emails',
  Scorecards: 'Scorecards',
  Templates: 'Templates you last changed',
};
