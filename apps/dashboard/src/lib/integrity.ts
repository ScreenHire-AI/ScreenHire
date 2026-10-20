import type {
  AiAssistanceLevel,
  AiUseDeclaration,
  AiUsePolicy,
  ApplicationSummary,
  AuthenticityLevel,
  DismissReason,
  IntegritySummary,
} from '@screenhire/api-client';
import type { BadgeVariant } from '#lib/components/ui/badge/index.js';
import type { MarkShape, MarkTone } from '#lib/distribution.js';

/**
 * How the dashboard shows one integrity level (docs/architecture/integrity-engine.md, design-system section 2.2):
 * always a shape mark, a colour and words. Levels are categories, never scores (ADR-0004); `rank` only orders them.
 */
export interface LevelView {
  /** The level in words: "Review suggested". */
  label: string;
  /** The table's short form: "Review". */
  short: string;
  shape: MarkShape;
  tone: MarkTone;
  variant: BadgeVariant;
  /** 0 for the lowest level; sorting puts the highest first. */
  rank: number;
}

export const authenticityLevels: Record<AuthenticityLevel, LevelView> = {
  NoConcerns: { label: 'No concerns', short: 'No concerns', shape: 'circle', tone: 'clear', variant: 'clear', rank: 0 },
  ReviewSuggested: { label: 'Review suggested', short: 'Review', shape: 'diamond', tone: 'review', variant: 'review', rank: 1 },
  VerificationRecommended: { label: 'Verification recommended', short: 'Verify', shape: 'square', tone: 'verify', variant: 'verify', rank: 2 },
};

// AI assistance is information, not a status, so its chips share one colour and the ring mark, and the words carry the
// level. "Not indicated" is quiet, so a column of them does not draw the eye.
export const aiAssistanceLevels: Record<AiAssistanceLevel, LevelView> = {
  NotIndicated: { label: 'Not indicated', short: 'None', shape: 'ring', tone: 'neutral', variant: 'outline', rank: 0 },
  SomeIndicators: { label: 'Some indicators', short: 'Some', shape: 'ring', tone: 'ai', variant: 'ai', rank: 1 },
  StrongIndicators: { label: 'Strong indicators', short: 'Strong', shape: 'ring', tone: 'ai', variant: 'ai', rank: 2 },
};

/** An application whose checks have not finished: the API sends no integrity summary yet. */
export const pendingLevel: LevelView = { label: 'Checks pending', short: 'Pending', shape: 'ring', tone: 'neutral', variant: 'outline', rank: -1 };

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
    levels: { NoConcerns: 0, ReviewSuggested: 0, VerificationRecommended: 0 },
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
