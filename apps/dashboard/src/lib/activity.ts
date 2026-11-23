import ArrowRightIcon from '~icons/lucide/arrow-right';
import BookmarkIcon from '~icons/lucide/bookmark';
import BookmarkXIcon from '~icons/lucide/bookmark-x';
import CalendarCheckIcon from '~icons/lucide/calendar-check';
import CalendarClockIcon from '~icons/lucide/calendar-clock';
import CalendarPlusIcon from '~icons/lucide/calendar-plus';
import CalendarRangeIcon from '~icons/lucide/calendar-range';
import CalendarCogIcon from '~icons/lucide/calendar-cog';
import CalendarSearchIcon from '~icons/lucide/calendar-search';
import CalendarSyncIcon from '~icons/lucide/calendar-sync';
import CalendarXIcon from '~icons/lucide/calendar-x';
import ClipboardCheckIcon from '~icons/lucide/clipboard-check';
import ClipboardListIcon from '~icons/lucide/clipboard-list';
import InboxIcon from '~icons/lucide/inbox';
import ListChecksIcon from '~icons/lucide/list-checks';
import ListTodoIcon from '~icons/lucide/list-todo';
import MailPlusIcon from '~icons/lucide/mail-plus';
import PencilIcon from '~icons/lucide/pencil';
import ReplyIcon from '~icons/lucide/reply';
import ScanSearchIcon from '~icons/lucide/scan-search';
import SendIcon from '~icons/lucide/send';
import StickyNoteIcon from '~icons/lucide/sticky-note';
import TagIcon from '~icons/lucide/tag';
import TagsIcon from '~icons/lucide/tags';
import Trash2Icon from '~icons/lucide/trash-2';
import type { ActivityEntry, ApplicationEventType, ApplicationTimelineEntry, TalentPoolExit, TeamMember } from '@screenhire/api-client';
import type { Component } from 'svelte';
import { formatDateTime } from '#lib/dates.js';
import { sentenceStart } from '#lib/history.js';
import { rejectionReasonLabels } from '#lib/rejection.js';
import { personName } from '#lib/team.js';

/**
 * An application's Activity (design system section 21): every entry newest first, saying who and how long ago, and a
 * filter by kind with counts. A note shows its latest text and says Edited, so its edits are not lines of their own; a
 * deleted note leaves one line saying who deleted it and when, never its text. The Verification kind holds the times the
 * checks ran and what people and API keys did to the candidate's verification (`#lib/verification-activity.ts`).
 */

/** The kinds Activity filters by, in the order of the filter. Entries of no kind show under All only. */
export const activityKinds = [
  { value: 'notes', label: 'Notes' },
  { value: 'emails', label: 'Emails' },
  { value: 'scorecards', label: 'Scorecards' },
  { value: 'interviews', label: 'Interviews' },
  { value: 'stage', label: 'Stage and tags' },
  { value: 'verification', label: 'Verification' },
] as const;

export type ActivityKind = (typeof activityKinds)[number]['value'];

const kinds: Partial<Record<ApplicationEventType, ActivityKind>> = {
  NoteAdded: 'notes',
  NoteDeleted: 'notes',
  EmailSent: 'emails',
  EmailReceived: 'emails',
  ScorecardSubmitted: 'scorecards',
  Submitted: 'stage',
  VerificationAssessed: 'verification',
  StageChanged: 'stage',
  TagAdded: 'stage',
  TagRemoved: 'stage',
  KeptInMind: 'stage',
  LeftTalentPool: 'stage',
  InvitedToApply: 'emails',
  InterviewInvited: 'interviews',
  InterviewBooked: 'interviews',
  InterviewRescheduled: 'interviews',
  InterviewCancelled: 'interviews',
  InterviewNewTimesOffered: 'interviews',
  InterviewOptionChanged: 'interviews',
  InterviewTimesSuggested: 'interviews',
  InterviewAnotherTimeAsked: 'interviews',
};

export const activityKind = (entry: Pick<ApplicationTimelineEntry, 'type'>): ActivityKind | undefined => kinds[entry.type];

/** The entries Activity lists, newest first: no edit lines, and no line for a note that was deleted. */
export function visibleEntries(entries: readonly ApplicationTimelineEntry[]): ApplicationTimelineEntry[] {
  return entries.filter((entry) => entry.type !== 'NoteEdited' && !(entry.type === 'NoteAdded' && entry.note === null)).toReversed();
}

/** How many entries each kind holds, and all of them. */
export function kindCounts(entries: readonly ApplicationTimelineEntry[]): Record<ActivityKind | 'all', number> {
  const counts = { all: entries.length, notes: 0, emails: 0, scorecards: 0, interviews: 0, stage: 0, verification: 0 };
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
  EmailReceived: ReplyIcon,
  QuestionnaireSent: ClipboardListIcon,
  QuestionnaireCompleted: ListChecksIcon,
  TaskCreated: ListTodoIcon,
  KeptInMind: BookmarkIcon,
  LeftTalentPool: BookmarkXIcon,
  InvitedToApply: MailPlusIcon,
  InterviewInvited: CalendarPlusIcon,
  InterviewBooked: CalendarCheckIcon,
  InterviewRescheduled: CalendarClockIcon,
  InterviewCancelled: CalendarXIcon,
  InterviewNewTimesOffered: CalendarRangeIcon,
  InterviewOptionChanged: CalendarCogIcon,
  InterviewTimesSuggested: CalendarSearchIcon,
  InterviewAnotherTimeAsked: CalendarSyncIcon,
};

/** What the screen knows that an entry names by id: stage names, follow-up questionnaire names and job titles. */
export interface ActivityNames {
  stageName: (id: string | null | undefined) => string;
  followUpName: (requestId: string | null | undefined) => string;
  /** Another job's title, for the saved candidates' entries; "another job" when the reader cannot see it. */
  jobName?: (id: string | null | undefined) => string;
}

/** Why a candidate left saved candidates, as Activity says it. Expired is for old rows only: nothing expires now. */
const poolExits: Record<NonNullable<TalentPoolExit>, (job: string) => string> = {
  Expired: () => 'Left saved candidates: their permission ran out',
  Removed: () => 'Removed them from saved candidates',
  CandidateAsked: () => 'Removed them from saved candidates at their request',
  AddedToJob: (job) => `Added them to ${job} from saved candidates`,
  AppliedAgain: (job) => `Left saved candidates: applied for ${job}`,
  BackInJob: () => 'Left saved candidates: back in this job',
  KeptFromAnotherJob: (job) => `Left saved candidates here: saved from ${job} instead`,
};

/** What happened, in words: "Moved from Applied to Screening". */
export function describeEvent(entry: ApplicationTimelineEntry, names: ActivityNames): string {
  switch (entry.type) {
    case 'Submitted':
      return entry.actorUserId || entry.actorApiKeyId ? `Added to ${names.stageName(entry.toStageId)}` : `Applied on the careers page, into ${names.stageName(entry.toStageId)}`;
    case 'StageChanged':
      // A template switch put the candidate in a stage of the new pipeline: nobody moved them, the pipeline changed.
      if (entry.templateVersionId) {
        return entry.fromStageId === entry.toStageId
          ? `The pipeline changed: stayed in ${names.stageName(entry.toStageId)}`
          : `The pipeline changed: from ${names.stageName(entry.fromStageId)} to ${names.stageName(entry.toStageId)}`;
      }
      return `Moved from ${names.stageName(entry.fromStageId)} to ${names.stageName(entry.toStageId)}${entry.rejectionReason ? `: ${rejectionReasonLabels[entry.rejectionReason]}` : ''}`;
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
    case 'EmailReceived':
      // The candidate wrote, so nobody is named. The reply itself is in Messages.
      return `Candidate replied "${entry.emailSubject ?? 'to an email'}"`;
    case 'QuestionnaireSent':
      return `Sent the "${names.followUpName(entry.questionnaireRequestId)}" form`;
    case 'QuestionnaireCompleted':
      // The answers are on the Application tab.
      return `Answered the "${names.followUpName(entry.questionnaireRequestId)}" form`;
    case 'TaskCreated':
      // What the task asks shows under it, quoted, as a stage move's note does.
      return 'Created a task';
    case 'KeptInMind':
      // Why shows under it, quoted, as a stage move's note does.
      return 'Saved';
    case 'LeftTalentPool':
      return poolExits[entry.poolExit ?? 'Removed'](names.jobName?.(entry.otherJobId) ?? 'another job');
    case 'InvitedToApply':
      return `Invited to apply for ${names.jobName?.(entry.otherJobId) ?? 'another job'}`;
    // The interview's current time is the header's one fact; an entry says which time it was about, in the reader's zone,
    // because a move or a cancellation is history that the header no longer shows. No actor means the candidate did it.
    case 'InterviewInvited':
      return 'Invited to pick an interview time';
    case 'InterviewBooked':
      return entry.startsAt ? `Interview booked for ${formatDateTime(entry.startsAt)}` : 'Interview booked';
    case 'InterviewRescheduled':
      return entry.startsAt ? `Interview moved to ${formatDateTime(entry.startsAt)}` : 'Interview moved to another time';
    case 'InterviewNewTimesOffered':
      return entry.startsAt ? `Offered new times for the interview on ${formatDateTime(entry.startsAt)}` : 'Offered new interview times';
    case 'InterviewCancelled':
      return entry.startsAt ? `Interview on ${formatDateTime(entry.startsAt)} cancelled` : 'Interview cancelled';
    case 'InterviewOptionChanged':
      return 'Changed how they pick an interview time';
    case 'InterviewTimesSuggested':
      return 'Sent the times they are free for an interview';
    case 'InterviewAnotherTimeAsked':
      return 'Asked for another interview time';
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

type Linkable = Pick<ApplicationTimelineEntry, 'type'> &
  Partial<Pick<ApplicationTimelineEntry, 'noteId' | 'emailId' | 'scorecardId' | 'questionnaireRequestId' | 'otherApplicationId'>>;

/**
 * Where an entry's change is (design system section 11, Activity): the note on Activity, the email on Messages, the
 * scorecard's sheet, the follow-up form's answers, the interview, the verification, and for a move, a tag or the
 * application itself the candidate's Overview, under the stage rail. The Activity feed in the bell and the candidate's own
 * Activity tab link every entry this way.
 */
export function activityHref(entry: Linkable, applicationId: string): string {
  const candidate = `/applications/${applicationId}`;
  switch (entry.type) {
    case 'NoteAdded':
      return entry.noteId ? `${candidate}?tab=activity#note-${entry.noteId}` : `${candidate}?tab=activity`;
    case 'EmailSent':
    case 'EmailReceived':
      return entry.emailId ? `${candidate}?tab=messages#email-${entry.emailId}` : `${candidate}?tab=messages`;
    case 'ScorecardSubmitted':
      // No id when the reader still has their own scorecard to give (ADR-0013): the tab, not the sheet.
      return entry.scorecardId ? `${candidate}?tab=scorecards#scorecard-${entry.scorecardId}` : `${candidate}?tab=scorecards`;
    case 'QuestionnaireSent':
    case 'QuestionnaireCompleted':
      return entry.questionnaireRequestId ? `${candidate}?tab=application#follow-up-${entry.questionnaireRequestId}` : `${candidate}?tab=application`;
    case 'InterviewInvited':
    case 'InterviewBooked':
    case 'InterviewRescheduled':
    case 'InterviewCancelled':
    case 'InterviewNewTimesOffered':
    case 'InterviewOptionChanged':
    case 'InterviewTimesSuggested':
    case 'InterviewAnotherTimeAsked':
      return `${candidate}#interview`;
    case 'VerificationAssessed':
      return `${candidate}?tab=verification`;
    case 'InvitedToApply':
      return entry.otherApplicationId ? `/applications/${entry.otherApplicationId}` : candidate;
    default:
      return candidate;
  }
}

/**
 * One line of the workspace's Activity feed: `before`, then the record as a link (`link`, to `href`), then `after`, and who
 * did it when that is not the sentence's subject (`by`): "Riley Harper moved [Taylor Brook] to Screening", "[Taylor Brook]
 * applied", "Moved [Taylor Brook] to Screening" by ATS sync, via an API key. Checks say only that they finished, never what
 * they found (non-negotiable 1).
 */
export interface FeedLine {
  before: string;
  link: string;
  href: string;
  after: string;
  by?: string;
}

/** The words for one feed entry. `members` and `currentUserId` name the member who made it ("You" for the reader). */
export function feedLine(entry: ActivityEntry, members: readonly TeamMember[] | undefined, currentUserId: string | undefined): FeedLine {
  const doer = entry.by === 'Member' && entry.actorUserId ? sentenceStart(personName(entry.actorUserId, members, currentUserId)) : undefined;
  const by = entry.by === 'Automation' ? 'by an automation' : entry.by === 'ApiKey' ? `by ${entry.actorApiKeyName ?? 'a key'}, via an API key` : undefined;
  // A sentence whose subject is whoever made the change: their name when a member did, else it starts with the verb and
  // says who after it.
  const done = (verb: string, link: string, after: string, href: string): FeedLine =>
    doer ? { before: `${doer} ${verb}`, link, href, after } : { before: sentenceStart(verb), link, href, after, by };

  const event = entry.event;
  if (!event || !entry.applicationId) {
    const job = `/jobs/${entry.jobId}`;
    const change = entry.jobChange;
    if (change?.created) return done('created ', entry.jobTitle, '', job);
    const line = change?.line?.text;
    const more = change && change.moreLines > 0 ? ` and ${change.moreLines} more ${change.moreLines === 1 ? 'change' : 'changes'}` : '';
    return done('changed ', entry.jobTitle, line ? `: ${line}${more}` : more, job);
  }

  const name = entry.candidateName ?? 'a candidate';
  const href = activityHref(event, entry.applicationId);
  const candidate = (after: string): FeedLine => ({ before: '', link: name, href, after });
  const at = (word: string) => (event.startsAt ? ` ${word} ${formatDateTime(event.startsAt)}` : '');
  switch (event.type) {
    case 'Submitted':
      return entry.by === 'Candidate' ? candidate(' applied') : done('added ', name, entry.toStageName ? ` to ${entry.toStageName}` : '', href);
    case 'StageChanged':
      // A template switch is not a move: the person changed the job's pipeline, and the candidate landed in a stage of it.
      if (event.templateVersionId) return done('changed the pipeline for ', name, entry.toStageName ? `, now in ${entry.toStageName}` : '', href);
      return done('moved ', name, entry.toStageName ? ` to ${entry.toStageName}` : '', href);
    case 'TagAdded':
      return done('tagged ', name, ` "${event.tag ?? ''}"`, href);
    case 'TagRemoved':
      return done(`removed the tag "${event.tag ?? ''}" from `, name, '', href);
    case 'NoteAdded':
      return done('added a note on ', name, '', href);
    case 'VerificationAssessed':
      return { before: 'Checks finished for ', link: name, href, after: '' };
    case 'ScorecardSubmitted':
      return done(entry.stageName ? `submitted the ${entry.stageName} scorecard for ` : 'submitted a scorecard for ', name, '', href);
    case 'EmailSent':
      return done('emailed ', name, event.emailSubject ? ` "${event.emailSubject}"` : '', href);
    case 'EmailReceived':
      return candidate(event.emailSubject ? ` replied "${event.emailSubject}"` : ' replied');
    case 'QuestionnaireSent':
      return done('sent ', name, ` the "${entry.followUpName ?? 'follow-up'}" form`, href);
    case 'QuestionnaireCompleted':
      return candidate(` answered "${entry.followUpName ?? 'a follow-up form'}"`);
    case 'TaskCreated':
      return done('created a task for ', name, '', href);
    case 'KeptInMind':
      return done('saved ', name, ' for later', href);
    case 'InvitedToApply':
      return done('invited ', name, ' to apply for another job', href);
    case 'InterviewInvited':
      return done('invited ', name, ' to pick an interview time', href);
    case 'InterviewBooked':
      return entry.by === 'Candidate' ? candidate(` booked an interview${at('for')}`) : done('booked ', name, `'s interview${at('for')}`, href);
    case 'InterviewRescheduled':
      return entry.by === 'Candidate' ? candidate(` moved their interview${at('to')}`) : done('moved ', name, `'s interview${at('to')}`, href);
    case 'InterviewCancelled':
      return done('cancelled ', name, `'s interview${at('on')}`, href);
    case 'InterviewNewTimesOffered':
      return done('offered ', name, ' new interview times', href);
    case 'InterviewOptionChanged':
      return done('changed how ', name, ' picks an interview time', href);
    case 'InterviewTimesSuggested':
      return candidate(' sent the times they are free for an interview');
    case 'InterviewAnotherTimeAsked':
      return candidate(' asked for another interview time');
    default:
      return done('changed ', name, '', href);
  }
}
