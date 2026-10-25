import type {
  AiAssistanceLevel,
  AiUseDeclaration,
  AiUsePolicy,
  ApplicationSummary,
  AuthenticityLevel,
  DismissReason,
  IntegritySummary,
  StageKind,
} from '@screenhire/api-client';
import type { QueryClient } from '@tanstack/svelte-query';
import { invalidatePath } from '#lib/query.js';
import { applicationsPath, jobsPath } from '#lib/realtime.js';
import type { StatusName } from '#lib/status.js';

/**
 * How the dashboard shows one integrity level (docs/architecture/integrity-engine.md, design-system section 3.2):
 * always its status icon, a colour and words. Levels are categories, never scores (ADR-0004); `rank` only orders them.
 */
export interface LevelView {
  /** The level in words, always in full (section 16): "Verification suggested", "Some signs of AI help". */
  label: string;
  /** The words in a table's cell, where the column header names the assessment: "Some signs". */
  short: string;
  /** The status the level shows as: its icon and colour (section 3.2). */
  status: StatusName;
  /** 0 for the lowest level; sorting puts the highest first. */
  rank: number;
}

export const authenticityLevels: Record<AuthenticityLevel, LevelView> = {
  NoConcerns: { label: 'No concerns', short: 'No concerns', status: 'done', rank: 0 },
  ReviewSuggested: { label: 'Review suggested', short: 'Review suggested', status: 'review', rank: 1 },
  VerificationRecommended: { label: 'Verification suggested', short: 'Verification suggested', status: 'verification', rank: 2 },
};

// AI assistance is information, not a status, so every level takes the AI colour and the sparkles icon, and the words
// carry the level. In a table "No signs" is quiet, so a column of them does not draw the eye.
export const aiAssistanceLevels: Record<AiAssistanceLevel, LevelView> = {
  NotIndicated: { label: 'No signs of AI help', short: 'No signs', status: 'ai', rank: 0 },
  SomeIndicators: { label: 'Some signs of AI help', short: 'Some signs', status: 'ai', rank: 1 },
  StrongIndicators: { label: 'Strong signs of AI help', short: 'Strong signs', status: 'ai', rank: 2 },
};

/** Each assessment's levels from lowest to highest, the order legends and menus list them in. */
export const authenticityOrder = ['NoConcerns', 'ReviewSuggested', 'VerificationRecommended'] as const satisfies readonly AuthenticityLevel[];
export const aiAssistanceOrder = ['NotIndicated', 'SomeIndicators', 'StrongIndicators'] as const satisfies readonly AiAssistanceLevel[];

/** An application whose checks have not finished: the API sends no integrity summary yet. */
export const pendingLevel: LevelView = { label: 'Checks pending', short: 'Checks pending', status: 'working', rank: -1 };

export type AssessmentKind = 'authenticity' | 'aiAssistance';

export const assessmentNames: Record<AssessmentKind, string> = { authenticity: 'Authenticity', aiAssistance: 'AI assistance' };

/** One assessment's chip data from a list row's summary, or null while the checks are pending. */
export function chipOf(summary: IntegritySummary | null | undefined, kind: AssessmentKind) {
  if (!summary) return null;
  return kind === 'authenticity'
    ? { level: authenticityLevels[summary.authenticity], reasons: summary.authenticityReasons.length, dismissed: summary.authenticityDismissed }
    : { level: aiAssistanceLevels[summary.aiAssistance], reasons: summary.aiAssistanceReasons.length, dismissed: summary.aiAssistanceDismissed };
}

/** The rank the table sorts by; undefined (pending) sorts last either way. */
export function rankOf(application: ApplicationSummary, kind: AssessmentKind): number | undefined {
  return chipOf(application.integrity, kind)?.level.rank;
}

/** Authenticity above its lowest level and not dismissed by a reviewer: the "Needs review" queue. */
export function needsReview(application: ApplicationSummary): boolean {
  const integrity = application.integrity;
  return Boolean(integrity && integrity.authenticity !== 'NoConcerns' && !integrity.authenticityDismissed);
}

export function verificationRecommended(application: ApplicationSummary): boolean {
  const integrity = application.integrity;
  return Boolean(integrity && integrity.authenticity === 'VerificationRecommended' && !integrity.authenticityDismissed);
}

/** The candidate table's integrity filter; null shows every row. */
export interface IntegrityFilter {
  authenticity: AuthenticityLevel | 'Pending' | null;
  aiAssistance: AiAssistanceLevel | null;
}

export const noIntegrityFilter: IntegrityFilter = { authenticity: null, aiAssistance: null };

export function matchesIntegrityFilter(application: ApplicationSummary, filter: IntegrityFilter): boolean {
  const integrity = application.integrity;
  if (filter.authenticity === 'Pending') {
    if (integrity) return false;
  } else if (filter.authenticity && integrity?.authenticity !== filter.authenticity) {
    return false;
  }
  return !filter.aiAssistance || integrity?.aiAssistance === filter.aiAssistance;
}

export function isIntegrityFiltered(filter: IntegrityFilter): boolean {
  return Boolean(filter.authenticity || filter.aiAssistance);
}

/**
 * The stages a bulk move offers. While an integrity filter picks the rows, no bulk action may end a candidacy: a
 * filtered "select all" then Rejected would be rejection driven by integrity signals (ADR-0004). One candidate at a time
 * still moves anywhere, because that is a person's decision about that candidate.
 */
export function bulkMoveStages<Stage extends { kind: StageKind }>(stages: readonly Stage[], filter: IntegrityFilter): Stage[] {
  return isIntegrityFiltered(filter) ? stages.filter((stage) => stage.kind !== 'Rejected' && stage.kind !== 'Withdrawn') : [...stages];
}

export interface IntegrityOverview {
  total: number;
  pending: number;
  levels: Record<AuthenticityLevel, number>;
  /** Assessments above the lowest level that a reviewer has dismissed; they still count under their level. */
  dismissed: number;
}

/** Counts for a job's stacked bar and its legend. The parts add up to the total. */
export function integrityOverview(applications: readonly ApplicationSummary[]): IntegrityOverview {
  const overview: IntegrityOverview = {
    total: applications.length,
    pending: 0,
    levels: Object.fromEntries(authenticityOrder.map((level) => [level, 0])) as Record<AuthenticityLevel, number>,
    dismissed: 0,
  };
  for (const { integrity } of applications) {
    if (!integrity) {
      overview.pending++;
      continue;
    }
    overview.levels[integrity.authenticity]++;
    if (integrity.authenticityDismissed) overview.dismissed++;
  }
  return overview;
}

export const aiPolicyLabels: Record<AiUsePolicy, string> = {
  Allowed: 'Allowed',
  AllowedWithDisclosure: 'Allowed with disclosure',
  NotAllowed: 'Not allowed',
};

export const aiDeclarationLabels: Record<AiUseDeclaration, string> = {
  NotProvided: 'Not answered',
  DidNotUse: 'No',
  Used: 'Yes',
};

export const dismissReasonLabels: Record<DismissReason, string> = {
  VerifiedOutsideScreenHire: 'Verified outside ScreenHire',
  ExplainedByCandidate: 'Explained by the candidate',
  FalsePositive: 'The observations do not apply',
  Other: 'Other reason',
};

/**
 * After a dismissal or a note: they change the integrity panel, the review log, the list chips (dismissed) and the
 * "Needs review" counts, and no live event announces them.
 */
export function refetchAfterReview(queryClient: QueryClient): Promise<unknown> {
  return Promise.all([invalidatePath(queryClient, applicationsPath), invalidatePath(queryClient, jobsPath)]);
}

/** Who took a review action, as the integrity panel and the review log name them: "you", or their name. */
export function reviewerName(userId: string, name: string | null, currentUserId: string | undefined): string {
  if (userId === currentUserId) return 'you';
  return name ?? 'a teammate';
}
