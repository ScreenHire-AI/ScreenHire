import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
import ClipboardListIcon from '@lucide/svelte/icons/clipboard-list';
import InboxIcon from '@lucide/svelte/icons/inbox';
import ListChecksIcon from '@lucide/svelte/icons/list-checks';
import ListTodoIcon from '@lucide/svelte/icons/list-todo';
import PencilIcon from '@lucide/svelte/icons/pencil';
import ScanSearchIcon from '@lucide/svelte/icons/scan-search';
import SendIcon from '@lucide/svelte/icons/send';
import StickyNoteIcon from '@lucide/svelte/icons/sticky-note';
import TagIcon from '@lucide/svelte/icons/tag';
import TagsIcon from '@lucide/svelte/icons/tags';
import Trash2Icon from '@lucide/svelte/icons/trash-2';
import type { ApplicationEventType, ApplicationTimelineEntry, TeamMember } from '@screenhire/api-client';
import type { Component } from 'svelte';
import { personName } from '#lib/team.js';

/**
 * An application's Activity (design system section 22): every entry newest first, saying who and how long ago, and a
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
  IntegrityAssessed: ScanSearchIcon,
  NoteAdded: StickyNoteIcon,
  NoteEdited: PencilIcon,
  NoteDeleted: Trash2Icon,
  ScorecardSubmitted: ClipboardCheckIcon,
  EmailSent: SendIcon,
  QuestionnaireSent: ClipboardListIcon,
  QuestionnaireCompleted: ListChecksIcon,
  TaskCreated: ListTodoIcon,
};

/** What the screen knows that an entry names by id: stage names and follow-up questionnaire names. */
export interface ActivityNames {
  stageName: (id: string | null | undefined) => string;
  followUpName: (requestId: string | null | undefined) => string;
}

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
    case 'IntegrityAssessed':
      // Levels and evidence are the Integrity tab's to show; Activity records only that the checks ran.
      return 'Integrity checks ran';
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
