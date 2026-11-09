import type {
  ApplicationTaskEntry,
  ApplicationTimelineEntry,
  Recommendation,
  ScorecardEntry,
  ScorecardTemplateSummary,
  TeamMember,
} from '@screenhire/api-client';
import { sentenceStart } from '#lib/history.js';
import { scorecardWords } from '#lib/scorecard-choices.js';
import { personName } from '#lib/team.js';

// What the Scorecards tab's roster draws (design system section 11, Scorecard): one row per scorecard given, then the
// scorecards asked for that are not in yet. Kept apart from the component so the rules are testable.

/** The recommendations from the least to the most favourable, for the Recommendation column's sort. */
export const recommendationRank: Record<NonNullable<Recommendation>, number> = { StrongNo: 0, No: 1, Yes: 2, StrongYes: 3 };

/** One given scorecard, with the values the roster's columns show and sort by. */
export interface RosterRow {
  id: string;
  entry: ScorecardEntry;
  /** The reviewer's name only; "You" for the signed-in person. */
  reviewer: string;
  stage: string;
  /** Undefined when the API withheld it. */
  recommendationRank: number | undefined;
  /** This scorecard's own rating; undefined when it has none or the API withheld it. */
  rating: number | undefined;
  submittedAt: string;
}

export function rosterRows(
  scorecards: readonly ScorecardEntry[],
  names: { stageName: (stageId: string) => string; members: readonly TeamMember[] | undefined; currentUserId: string | undefined },
): RosterRow[] {
  return scorecards.map((entry) => ({
    id: entry.id,
    entry,
    reviewer: sentenceStart(personName(entry.reviewerUserId, names.members, names.currentUserId)),
    stage: names.stageName(entry.stageId),
    recommendationRank: entry.recommendation ? recommendationRank[entry.recommendation] : undefined,
    rating: entry.rating ?? undefined,
    submittedAt: entry.submittedAt,
  }));
}

/** The side sheet's title: the scorecard's name, or its stage's when it predates names ("Interview scorecard"). */
export const scorecardTitle = (entry: ScorecardEntry, stageName: (stageId: string) => string): string =>
  scorecardWords(entry.scorecardName ?? stageName(entry.stageId));

/**
 * The scorecards the sheet can step through with Previous and Next: the rows in the order the roster shows them, without
 * the ones the API withheld (they have nothing to read).
 */
export const readable = (sorted: readonly RosterRow[]): RosterRow[] => sorted.filter((row) => !row.entry.withheld);

/** The reviewer's role, for the sheet's subtitle; empty for someone no longer on the team. */
export const roleOf = (userId: string, members: readonly TeamMember[] | undefined): string =>
  members?.find((member) => member.userId === userId)?.roleName ?? '';

/** A scorecard asked for that is not in yet. */
export interface WaitingRow {
  taskId: string;
  assigneeUserId: string;
  /** The scorecard's name, in words ("Team interview scorecard"). */
  scorecard: string;
  askedAt: string;
  /** Who asked, from the timeline; null when an automation or an API key did. */
  askedByUserId: string | null;
}

/** The open requested-scorecard tasks of an application, oldest first, with who asked for each. */
export function waitingRows(
  tasks: readonly ApplicationTaskEntry[] | undefined,
  timeline: readonly ApplicationTimelineEntry[] | undefined,
  library: readonly ScorecardTemplateSummary[],
): WaitingRow[] {
  return (tasks ?? [])
    .filter((task) => task.kind === 'Scorecard' && task.completedAt === null && task.assigneeUserId !== null)
    .toSorted((a, b) => a.createdAt.localeCompare(b.createdAt) || a.id.localeCompare(b.id))
    .map((task) => ({
      taskId: task.id,
      assigneeUserId: task.assigneeUserId!,
      scorecard: scorecardWords(library.find((card) => card.id === task.scorecardTemplateId)?.name ?? 'requested'),
      askedAt: task.createdAt,
      askedByUserId: timeline?.find((entry) => entry.type === 'TaskCreated' && entry.taskId === task.id)?.actorUserId ?? null,
    }));
}
