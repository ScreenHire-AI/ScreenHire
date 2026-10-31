import type {
  AiAssistanceLevel,
  AiUseDeclaration,
  AiUsePolicy,
  ApplicationSource,
  ApplicationSummary,
  AuthenticityLevel,
  DismissReason,
  ExplainReason,
  VerificationSignalView,
  VerificationSummary,
  SignalCategory,
  StageKind,
} from '@screenhire/api-client';
import type { QueryClient } from '@tanstack/svelte-query';
import { invalidatePath } from '#lib/query.js';
import { applicationsPath, jobsPath } from '#lib/realtime.js';
import type { StatusName } from '#lib/status.js';

/**
 * How the dashboard shows one verification level (docs/architecture/verification-engine.md, design-system section 3.2):
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

/**
 * The kinds of check the Verification tab shows a summary card for (design system section 16). Network, Identity,
 * Automation and Content always show; files and form activity only when they found something. Each is one of the
 * engine's categories, kept apart because the engine counts independent categories to raise a level
 * (verification-engine.md section 10.1). The names say what was looked at, never what it means about the person: form
 * activity (the telemetry aggregates) is neither "automation" nor "behaviour".
 */
export const checkKinds: readonly { category: SignalCategory; title: string; always: boolean }[] = [
  { category: 'Network', title: 'Network', always: true },
  { category: 'Identity', title: 'Identity', always: true },
  { category: 'Automation', title: 'Automation', always: true },
  { category: 'Content', title: 'Content', always: true },
  { category: 'Document', title: 'Files', always: false },
  { category: 'Behavioural', title: 'Form activity', always: false },
];

/**
 * The kinds of check that had nothing to look at, by how the application arrived. A candidate a recruiter added by hand
 * came through no careers form, so the API stored no network address, session or form telemetry for them
 * (`ApplicationSubmission.SubmitAsync`): Network and Automation did not run, and Form activity had nothing to measure.
 * It goes by the source alone, the same for everyone added by hand. A careers applicant's checks are never marked this
 * way, so nothing here tells an assistive-technology opt-out apart (CLAUDE.md non-negotiable 4).
 */
export function uncheckedCategories(source: ApplicationSource | undefined): SignalCategory[] {
  return source === 'Recruiter' ? ['Network', 'Automation'] : [];
}

/** The one quiet line a hand-added candidate's authenticity result carries about the checks that could not run. */
export const addedByHandNote = 'Added by hand, so the network, automation and form activity checks had nothing to look at.';

/** One kind of check: the reasons it found that count toward the level. */
export interface CheckSummary {
  category: SignalCategory;
  title: string;
  signals: VerificationSignalView[];
}

/** A card per kind of check, in the order of `checkKinds`, from an assessment's reasons. */
export function checkSummaries(signals: readonly VerificationSignalView[]): CheckSummary[] {
  const reasons = signals.filter((signal) => signal.countsTowardLevel);
  return checkKinds
    .map((kind) => ({ category: kind.category, title: kind.title, always: kind.always, signals: reasons.filter((signal) => signal.category === kind.category) }))
    .filter((kind) => kind.always || kind.signals.length > 0)
    .map(({ category, title, signals: found }) => ({ category, title, signals: found }));
}

/** An application whose checks have not finished: the API sends no verification summary yet. */
export const pendingLevel: LevelView = { label: 'Checks pending', short: 'Checks pending', status: 'working', rank: -1 };

export type AssessmentKind = 'authenticity' | 'aiAssistance';

export const assessmentNames: Record<AssessmentKind, string> = { authenticity: 'Authenticity', aiAssistance: 'AI assistance' };

/** One assessment's chip data from a list row's summary, or null while the checks are pending. */
export function chipOf(summary: VerificationSummary | null | undefined, kind: AssessmentKind) {
  if (!summary) return null;
  return kind === 'authenticity'
    ? { level: authenticityLevels[summary.authenticity], reasons: summary.authenticityReasons.length, dismissed: summary.authenticityDismissed }
    : { level: aiAssistanceLevels[summary.aiAssistance], reasons: summary.aiAssistanceReasons.length, dismissed: summary.aiAssistanceDismissed };
}

/** The rank the table sorts by; undefined (pending) sorts last either way. */
export function rankOf(application: ApplicationSummary, kind: AssessmentKind): number | undefined {
  return chipOf(application.verification, kind)?.level.rank;
}

/** Authenticity above its lowest level and not dismissed by a reviewer: the "Needs review" queue. */
export function needsReview(application: ApplicationSummary): boolean {
  const verification = application.verification;
  return Boolean(verification && verification.authenticity !== 'NoConcerns' && !verification.authenticityDismissed);
}

export function verificationRecommended(application: ApplicationSummary): boolean {
  const verification = application.verification;
  return Boolean(verification && verification.authenticity === 'VerificationRecommended' && !verification.authenticityDismissed);
}

/** The candidate table's verification filter; null shows every row. */
export interface VerificationFilter {
  authenticity: AuthenticityLevel | 'Pending' | null;
  aiAssistance: AiAssistanceLevel | null;
}

export const noVerificationFilter: VerificationFilter = { authenticity: null, aiAssistance: null };

export function matchesVerificationFilter(application: ApplicationSummary, filter: VerificationFilter): boolean {
  const verification = application.verification;
  if (filter.authenticity === 'Pending') {
    if (verification) return false;
  } else if (filter.authenticity && verification?.authenticity !== filter.authenticity) {
    return false;
  }
  return !filter.aiAssistance || verification?.aiAssistance === filter.aiAssistance;
}

export function isVerificationFiltered(filter: VerificationFilter): boolean {
  return Boolean(filter.authenticity || filter.aiAssistance);
}

/**
 * The stages a bulk move offers. While a verification filter picks the rows, no bulk action may end a candidacy: a
 * filtered "select all" then Rejected would be rejection driven by verification signals (ADR-0004). One candidate at a time
 * still moves anywhere, because that is a person's decision about that candidate.
 */
export function bulkMoveStages<Stage extends { kind: StageKind }>(stages: readonly Stage[], filter: VerificationFilter): Stage[] {
  return isVerificationFiltered(filter) ? stages.filter((stage) => stage.kind !== 'Rejected' && stage.kind !== 'Withdrawn') : [...stages];
}

export interface VerificationOverview {
  total: number;
  pending: number;
  levels: Record<AuthenticityLevel, number>;
  /** Assessments above the lowest level that a reviewer has dismissed; they still count under their level. */
  dismissed: number;
}

/** Counts for a job's Authenticity panel. The parts add up to the total. */
export function verificationOverview(applications: readonly ApplicationSummary[]): VerificationOverview {
  const overview: VerificationOverview = {
    total: applications.length,
    pending: 0,
    levels: Object.fromEntries(authenticityOrder.map((level) => [level, 0])) as Record<AuthenticityLevel, number>,
    dismissed: 0,
  };
  for (const { verification } of applications) {
    if (!verification) {
      overview.pending++;
      continue;
    }
    overview.levels[verification.authenticity]++;
    if (verification.authenticityDismissed) overview.dismissed++;
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

export const explainReasonLabels: Record<ExplainReason, string> = {
  ConfirmedWithCandidate: 'Confirmed with the candidate',
  ExpectedForRole: 'Expected for this role',
  KnownFalsePositive: 'A known false positive',
  Other: 'Other',
};

/** A reason someone marked explained, as the dismiss dialog lists it before a Dismiss all. */
export interface ExplainedReasonItem {
  id: string;
  title: string;
  reason: ExplainReason;
  note: string | null;
  /** Who explained it, as `reviewerName` words them ("you", or a name). */
  actor: string;
}

/**
 * Whether a person has marked every reason that counts toward the level explained, so the panel offers Dismiss all.
 * It decides nothing: the level stays as assessed until someone dismisses it (ADR-0004).
 */
export function everyReasonExplained(signals: readonly VerificationSignalView[]): boolean {
  const reasons = signals.filter((signal) => signal.countsTowardLevel);
  return reasons.length > 0 && reasons.every((signal) => signal.explained);
}

/**
 * After a dismissal, a note or an explanation: they change the verification panel, the review log, the list chips
 * (dismissed) and the "Needs review" counts, and no live event announces them.
 */
export function refetchAfterReview(queryClient: QueryClient): Promise<unknown> {
  return Promise.all([invalidatePath(queryClient, applicationsPath), invalidatePath(queryClient, jobsPath)]);
}

/** Who took a review action, as the verification panel and the review log name them: "you", or their name. */
export function reviewerName(userId: string, name: string | null, currentUserId: string | undefined): string {
  if (userId === currentUserId) return 'you';
  return name ?? 'a teammate';
}
