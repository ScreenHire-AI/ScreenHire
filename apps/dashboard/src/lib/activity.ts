import ArrowRightIcon from '~icons/lucide/arrow-right';
import BookmarkIcon from '~icons/lucide/bookmark';
import BookmarkXIcon from '~icons/lucide/bookmark-x';
import ClipboardCheckIcon from '~icons/lucide/clipboard-check';
import ClipboardListIcon from '~icons/lucide/clipboard-list';
import InboxIcon from '~icons/lucide/inbox';
import ListChecksIcon from '~icons/lucide/list-checks';
import ListTodoIcon from '~icons/lucide/list-todo';
import MailPlusIcon from '~icons/lucide/mail-plus';
import PencilIcon from '~icons/lucide/pencil';
import ScanSearchIcon from '~icons/lucide/scan-search';
import SendIcon from '~icons/lucide/send';
import StickyNoteIcon from '~icons/lucide/sticky-note';
import TagIcon from '~icons/lucide/tag';
import TagsIcon from '~icons/lucide/tags';
import Trash2Icon from '~icons/lucide/trash-2';
import type { ApplicationEventType, ApplicationTimelineEntry, TalentPoolExit, TeamMember } from '@screenhire/api-client';
import type { Component } from 'svelte';
import { personName } from '#lib/team.js';

/**
 * An application's Activity (design system section 21): every entry newest first, saying who and how long ago, and a
 * filter by kind with counts. A note shows its latest text and says Edited, so its edits are not lines of their own; a
 * deleted note leaves one line saying who deleted it and when, never its text.
 */

/** The kinds Activity filters by, in the order of the filter. Entries of no kind show under All only. */
export const activityKinds = [
  { value: 'notes', label: 'Notes' },
  { value: 'emails', label: 'Emails' },
  { value: 'scorecards', label: 'Scorecards' },
  { value: 'stage', label: 'Stage and tags' },
] as const;

export type ActivityKind = (typeof activityKinds)[number]['value'];

const kinds: Partial<Record<ApplicationEventType, ActivityKind>> = {
  NoteAdded: 'notes',
  NoteDeleted: 'notes',
  EmailSent: 'emails',
  ScorecardSubmitted: 'scorecards',
  Submitted: 'stage',
  StageChanged: 'stage',
  TagAdded: 'stage',
  TagRemoved: 'stage',
  KeptInMind: 'stage',
  LeftTalentPool: 'stage',
  InvitedToApply: 'emails',
};

export const activityKind = (entry: Pick<ApplicationTimelineEntry, 'type'>): ActivityKind | undefined => kinds[entry.type];

/** The entries Activity lists, newest first: no edit lines, and no line for a note that was deleted. */
export function visibleEntries(entries: readonly ApplicationTimelineEntry[]): ApplicationTimelineEntry[] {
  return entries.filter((entry) => entry.type !== 'NoteEdited' && !(entry.type === 'NoteAdded' && entry.note === null)).toReversed();
}

/** How many entries each kind holds, and all of them. */
export function kindCounts(entries: readonly ApplicationTimelineEntry[]): Record<ActivityKind | 'all', number> {
  const counts = { all: entries.length, notes: 0, emails: 0, scorecards: 0, stage: 0 };
  for (const entry of entries) {
    const kind = activityKind(entry);
    if (kind) counts[kind] += 1;
  }
  return counts;
}

export const eventIcons: Record<ApplicationEventType, Component> = {
  Submitted: InboxIcon,
  StageChanged: ArrowRightIcon,
  TagAdded: TagIcon,
  TagRemoved: TagsIcon,
  VerificationAssessed: ScanSearchIcon,
  NoteAdded: StickyNoteIcon,
  NoteEdited: PencilIcon,
  NoteDeleted: Trash2Icon,
  ScorecardSubmitted: ClipboardCheckIcon,
  EmailSent: SendIcon,
  QuestionnaireSent: ClipboardListIcon,
  QuestionnaireCompleted: ListChecksIcon,
  TaskCreated: ListTodoIcon,
  KeptInMind: BookmarkIcon,
  LeftTalentPool: BookmarkXIcon,
  InvitedToApply: MailPlusIcon,
};

/** What the screen knows that an entry names by id: stage names, follow-up questionnaire names and job titles. */
export interface ActivityNames {
  stageName: (id: string | null | undefined) => string;
  followUpName: (requestId: string | null | undefined) => string;
  /** Another job's title, for the talent pool's entries; "another job" when the reader cannot see it. */
  jobName?: (id: string | null | undefined) => string;
}

/** Why a candidate left the talent pool, as Activity says it. */
const poolExits: Record<NonNullable<TalentPoolExit>, (job: string) => string> = {
  Expired: () => 'Left the talent pool: their permission ran out',
  Removed: () => 'Took them out of the talent pool',
  CandidateAsked: () => 'Took them out of the talent pool at their request',
  AddedToJob: (job) => `Added them to ${job} from the talent pool`,
  AppliedAgain: (job) => `Left the talent pool: applied for ${job}`,
  BackInJob: () => 'Left the talent pool: back in this job',
  KeptFromAnotherJob: (job) => `Left the talent pool here: kept in mind from ${job} instead`,
};

/** What happened, in words: "Moved from Applied to Screening". */
export function describeEvent(entry: ApplicationTimelineEntry, names: ActivityNames): string {
  switch (entry.type) {
    case 'Submitted':
      return entry.actorUserId || entry.actorApiKeyId ? `Added to ${names.stageName(entry.toStageId)}` : `Applied on the careers page, into ${names.stageName(entry.toStageId)}`;
    case 'StageChanged':
      return `Moved from ${names.stageName(entry.fromStageId)} to ${names.stageName(entry.toStageId)}`;
    case 'TagAdded':
      return `Tagged "${entry.tag}"`;
    case 'TagRemoved':
      return `Removed the tag "${entry.tag}"`;
    case 'NoteAdded':
      return 'Note';
    case 'NoteEdited':
      return 'Edited a note';
    case 'NoteDeleted':
      // Who and when only: the note's text is gone.
      return 'Deleted a note';
    case 'VerificationAssessed':
      // Levels and evidence are the Verification tab's to show; Activity records only that the checks ran.
      return 'Verification checks ran';
    case 'ScorecardSubmitted':
      // The ratings and the recommendation are the Scorecards tab's to show.
      return `Scorecard for ${names.stageName(entry.stageId)}`;
    case 'EmailSent':
      // The body is never kept on the timeline, only the subject the candidate received.
      return `Emailed "${entry.emailSubject ?? 'an update'}"`;
    case 'QuestionnaireSent':
      return `Sent the "${names.followUpName(entry.questionnaireRequestId)}" questionnaire`;
    case 'QuestionnaireCompleted':
      // The answers are on the Application tab.
      return `Answered the "${names.followUpName(entry.questionnaireRequestId)}" questionnaire`;
    case 'TaskCreated':
      // What the task asks shows under it, quoted, as a stage move's note does.
      return 'Created a task';
    case 'KeptInMind':
      // Why shows under it, quoted, as a stage move's note does.
      return 'Kept in mind: left the job without a rejection';
    case 'LeftTalentPool':
      return poolExits[entry.poolExit ?? 'Removed'](names.jobName?.(entry.otherJobId) ?? 'another job');
    case 'InvitedToApply':
      return `Invited to apply for ${names.jobName?.(entry.otherJobId) ?? 'another job'}`;
  }
}

/** Who did it, after the event: "by you", "by Robin Hale", "by ATS sync" (an API key), "by an automation". */
export function actorText(
  entry: Partial<Pick<ApplicationTimelineEntry, 'automationRunId' | 'actorUserId' | 'actorApiKeyId' | 'actorApiKeyName'>>,
  members: readonly TeamMember[] | undefined,
  currentUserId: string | undefined,
): string | undefined {
  if (entry.automationRunId) return 'by an automation';
  if (entry.actorUserId) return `by ${personName(entry.actorUserId, members, currentUserId)}`;
  if (entry.actorApiKeyId) return entry.actorApiKeyName ? `by ${entry.actorApiKeyName}` : 'by an API key';
  return undefined;
}
