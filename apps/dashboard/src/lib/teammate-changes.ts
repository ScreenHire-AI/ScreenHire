import type { MemberNotificationSettings, ToastKind } from '@screenhire/api-client';
import type { RealtimeEvent } from '@screenhire/realtime';

/** The kinds about a candidate on a job whose hiring team the person is on. */
export type CandidateKind = Exclude<ToastKind, 'Tasks' | 'Templates'>;

export type TaskAction = 'created' | 'completed' | 'reopened';

/** A teammate's change that the person may want to hear about, from one live event (ids only, as the event has them). */
export type TeammateChange =
  | { kind: CandidateKind; actorUserId: string; applicationId: string; jobId: string }
  | { kind: 'Tasks'; actorUserId: string; applicationId: string; jobId: string; taskId: string; action: TaskAction }
  | { kind: 'Templates'; actorUserId: string; templateId: string; version: number };

/** What decides whether a change is for this person. */
export interface ChangeContext {
  /** The signed-in person's user id. */
  userId: string | undefined;
  /** The jobs whose hiring team they are on (ADR-0015); seeing a job is not enough. */
  teamJobIds: ReadonlySet<string>;
  settings: MemberNotificationSettings;
}

/** Everything on: a member who never opened the Notifications sheet, and the fallback while it cannot be read. */
export const defaultNotificationSettings: MemberNotificationSettings = { toasts: true, closeToasts: true, off: [] };

const candidateEvents: Partial<Record<RealtimeEvent['type'], CandidateKind>> = {
  ApplicationMoved: 'Moves',
  ApplicationSubmitted: 'NewCandidates',
  ApplicationNoteAdded: 'Notes',
  ApplicationTagsChanged: 'Tags',
  ApplicationEmailed: 'Emails',
  ScorecardSubmitted: 'Scorecards',
};

// The change an event describes and who it is for, before the person's settings. Any event not named here (a
// verification result, a note edit, an automation's run, the candidate's answers) never becomes a toast.
function describe(event: RealtimeEvent, context: ChangeContext): TeammateChange | undefined {
  const me = context.userId;
  switch (event.type) {
    case 'ApplicationTaskCreated':
    case 'ApplicationTaskCompleted':
    case 'ApplicationTaskReopened': {
      const { actorUserId, assigneeUserId, applicationId, jobId, taskId } = event.data;
      if (!actorUserId || !assigneeUserId || assigneeUserId !== me) return undefined;
      const action: TaskAction =
        event.type === 'ApplicationTaskCreated' ? 'created' : event.type === 'ApplicationTaskCompleted' ? 'completed' : 'reopened';
      return { kind: 'Tasks', actorUserId, applicationId, jobId, taskId, action };
    }
    case 'HiringTemplateVersionPublished': {
      const { actorUserId, previousEditorUserId, templateId, version } = event.data;
      if (!actorUserId || !previousEditorUserId || previousEditorUserId !== me) return undefined;
      return { kind: 'Templates', actorUserId, templateId, version };
    }
    default: {
      const kind = candidateEvents[event.type];
      if (!kind || !('actorUserId' in event.data) || !('applicationId' in event.data) || !('jobId' in event.data)) return undefined;
      const { actorUserId, applicationId, jobId } = event.data;
      if (!actorUserId || !context.teamJobIds.has(jobId)) return undefined;
      return { kind, actorUserId, applicationId, jobId };
    }
  }
}

/**
 * The teammate's change in `event` that this person should see as a toast, or undefined. A change needs a member behind
 * it (an API key, an automation or the candidate is no teammate, and a key may be the person's own assistant), never
 * the person themselves, and a kind they have not turned off.
 */
export function changeFor(event: RealtimeEvent, context: ChangeContext): TeammateChange | undefined {
  if (!context.userId || !context.settings.toasts) return undefined;
  const change = describe(event, context);
  if (!change || change.actorUserId === context.userId || context.settings.off.includes(change.kind)) return undefined;
  return change;
}

/**
 * Changes that read as one toast when they arrive together: one person doing the same kind of thing on one job, such
 * as a bulk move. A task or a template is always its own.
 */
export function groupKey(change: TeammateChange): string {
  switch (change.kind) {
    case 'Tasks':
      return `Tasks:${change.taskId}:${change.action}`;
    case 'Templates':
      return `Templates:${change.templateId}:${change.version}`;
    default:
      return `${change.kind}:${change.actorUserId}:${change.jobId}`;
  }
}

/** One toast in the tray: who did what, around the record's name, which is its link. */
export interface Toast {
  id: string;
  kind: ToastKind;
  /** The sentence in three parts; the middle one is the link ("Jordan Ellis moved ", "Taylor Brook", " to Interview"). */
  sentence: readonly [string, string, string];
  href: string;
  /** A muted second line for context (the job), or empty. */
  context: string;
}

/** What the screen read through the API to word a single change. */
export interface ChangeFacts {
  actorName: string;
  candidateName?: string;
  jobTitle?: string;
  stageName?: string;
  taskNote?: string;
  templateName?: string;
}

const taskWords: Record<TaskAction, string> = {
  created: 'gave you a task: ',
  completed: 'completed your task ',
  reopened: 'reopened your task ',
};

/** The words and link for one change. */
export function toastFor(id: string, change: TeammateChange, facts: ChangeFacts): Toast {
  const actor = facts.actorName;
  const candidate = facts.candidateName ?? 'a candidate';
  const job = facts.jobTitle ?? '';
  switch (change.kind) {
    case 'Tasks':
      return {
        id,
        kind: change.kind,
        sentence: [`${actor} ${taskWords[change.action]}`, facts.taskNote ?? 'a task', ''],
        href: `/applications/${change.applicationId}`,
        context: job ? `${candidate}, ${job}` : candidate,
      };
    case 'Templates':
      return {
        id,
        kind: change.kind,
        sentence: [`${actor} published version ${change.version} of `, facts.templateName ?? 'a hiring template', ''],
        href: `/templates/${change.templateId}`,
        context: 'Hiring template',
      };
    default: {
      const href = `/applications/${change.applicationId}`;
      const words: Record<CandidateKind, [string, string, string]> = {
        Moves: [`${actor} moved `, candidate, facts.stageName ? ` to ${facts.stageName}` : ''],
        NewCandidates: [`${actor} added `, candidate, ''],
        Notes: [`${actor} added a note on `, candidate, ''],
        Tags: [`${actor} changed the tags on `, candidate, ''],
        Emails: [`${actor} emailed `, candidate, ''],
        Scorecards: [`${actor} submitted a scorecard for `, candidate, ''],
      };
      const tab: Partial<Record<CandidateKind, string>> = { Notes: 'activity', Emails: 'messages', Scorecards: 'scorecards' };
      return { id, kind: change.kind, sentence: words[change.kind], href: tab[change.kind] ? `${href}?tab=${tab[change.kind]}` : href, context: job };
    }
  }
}

const groupWords: Record<CandidateKind, (count: number) => string> = {
  Moves: (count) => `moved ${count} candidates on `,
  NewCandidates: (count) => `added ${count} candidates to `,
  Notes: (count) => `added notes to ${count} candidates on `,
  Tags: (count) => `changed the tags on ${count} candidates on `,
  Emails: (count) => `emailed ${count} candidates on `,
  Scorecards: (count) => `submitted ${count} scorecards on `,
};

/** One toast for several changes of one kind by one person on one job, linking to the job. */
export function groupToastFor(id: string, kind: CandidateKind, jobId: string, count: number, actorName: string, jobTitle: string | undefined): Toast {
  return {
    id,
    kind,
    sentence: [`${actorName} ${groupWords[kind](count)}`, jobTitle ?? 'a job', ''],
    href: `/jobs/${jobId}`,
    context: '',
  };
}
