import type {
  AiAssistanceLevel,
  AiUseDeclaration,
  AiUsePolicy,
  ApplicationSummary,
  AuthenticityLevel,
  DismissReason,
  ExplainReason,
  VerificationSignalView,
  VerificationSummary,
  StageKind,
  AiAssistanceAssessmentView,
  AuthenticityAssessmentView,
  CheckGroup,
  CheckGroupState,
  CheckGroupView,
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
  /** The status the level shows as: its icon and colour (section 3.2). */
  status: StatusName;
  /** 0 for the lowest level; sorting puts the highest first. */
  rank: number;
}

export const authenticityLevels: Record<AuthenticityLevel, LevelView> = {
  NoConcerns: { label: 'No concerns', status: 'done', rank: 0 },
  ReviewSuggested: { label: 'Review suggested', status: 'review', rank: 1 },
  VerificationRecommended: { label: 'Verification suggested', status: 'verification', rank: 2 },
};

// AI assistance is information, not a status, so every level takes the AI colour and the sparkles icon, and the words
// carry the level. In lists only Some and Strong signs show, as the bare AI mark (`aiMarkOf`).
export const aiAssistanceLevels: Record<AiAssistanceLevel, LevelView> = {
  NotIndicated: { label: 'No signs of AI help', status: 'ai', rank: 0 },
  SomeIndicators: { label: 'Some signs of AI help', status: 'ai', rank: 1 },
  StrongIndicators: { label: 'Strong signs of AI help', status: 'ai', rank: 2 },
};

/** Authenticity's levels from lowest to highest, the order menus and counts list them in. */
const authenticityOrder =['NoConcerns', 'ReviewSuggested', 'VerificationRecommended'] as const satisfies readonly AuthenticityLevel[];

/**
 * One thing a check found, with the assessment it belongs to. The Verification tab lists findings by check group, so a
 * reader sees where each came from, and the review controls need the assessment's id.
 */
export interface Finding {
  signal: VerificationSignalView;
  assessmentId: string;
  kind: AssessmentKind;
}

// Every group, in the API's order. The record makes a group the API adds fail the type check here, rather than its findings
// going missing from the tab.
const groupPositions = { Network: 0, Contact: 1, Documents: 2, Answers: 3, Duplicates: 4, FormUse: 5 } as const satisfies Record<CheckGroup, number>;
const groupOrder = Object.keys(groupPositions) as CheckGroup[];

/**
 * An application's findings by check group, in the groups' order. Within a group the ones that count toward the level come
 * first, then the notes (AI help and context that never counts), each in the order the API sent them.
 */
export function findingsByGroup(
  authenticity: AuthenticityAssessmentView | null | undefined,
  aiAssistance: AiAssistanceAssessmentView | null | undefined,
): Record<CheckGroup, Finding[]> {
  const all: Finding[] = [
    ...(authenticity?.signals ?? []).map((signal) => ({ signal, assessmentId: authenticity!.id, kind: 'authenticity' as const })),
    ...(aiAssistance?.signals ?? []).map((signal) => ({ signal, assessmentId: aiAssistance!.id, kind: 'aiAssistance' as const })),
  ];
  const counts = (finding: Finding) => finding.kind === 'authenticity' && finding.signal.countsTowardLevel;
  return Object.fromEntries(
    groupOrder.map((group) => [group, all.filter((finding) => finding.signal.group === group).toSorted((a, b) => Number(counts(b)) - Number(counts(a)))]),
  ) as Record<CheckGroup, Finding[]>;
}

/** The anchor a group's panel carries, so its tile on the map can scroll to it. */
export const groupAnchor = (group: CheckGroup) => `verification-group-${group.toLowerCase()}`;

/** A group tile's words: its state in one phrase. They never say what a finding means about the person. */
export function groupStateText(group: CheckGroupView): string {
  switch (group.state) {
    case 'Found':
      return `${group.found} found`;
    case 'InformationOnly':
      return `${group.notes} ${group.notes === 1 ? 'note' : 'notes'}, information only`;
    case 'NothingFound':
      return 'Nothing found';
    case 'NotChecked':
      return 'Not checked';
    case 'TurnedOff':
      return 'Turned off';
  }
}

/**
 * The status a group tile shows as: its icon and colour. Found needs a look, whatever the level (the header shows the level
 * once, and a level's own icon would read as a check of its own, such as Verify's ID card); the rest are calm or
 * information. Information only takes the AI mark only where every note is AI help (`groupStatusOf`).
 */
export const groupStatuses: Record<CheckGroupState, StatusName> = {
  Found: 'review',
  InformationOnly: 'information',
  NothingFound: 'done',
  NotChecked: 'notInEffect',
  TurnedOff: 'notInEffect',
};

/**
 * A group tile's status given its findings: the sparkles mark means AI help (section 3.2), so an information-only group
 * takes it only when all its notes are AI help. A VPN or other context note is information in grey.
 */
export function groupStatusOf(group: CheckGroupView, findings: readonly Finding[]): StatusName {
  if (group.state === 'InformationOnly' && findings.length > 0 && findings.every((finding) => finding.kind === 'aiAssistance')) return 'ai';
  return groupStatuses[group.state];
}

/** A group that did not run, or had nothing to read: a calm result names it so a quiet result is never read as a clean one. */
export const groupDidNotRun = (group: CheckGroupView) => group.state === 'NotChecked' || group.state === 'TurnedOff';

export type AssessmentKind = 'authenticity' | 'aiAssistance';

/**
 * The one word a verification state shows as, everywhere a level shows (design system section 3.2): Clear, Review or
 * Verify, and the muted Pending and Dismissed. They are categories, never scores (ADR-0004).
 */
export type VerificationStateKind = 'clear' | 'review' | 'verify' | 'pending' | 'dismissed';

export interface VerificationState {
  kind: VerificationStateKind;
  /** The word on screen. */
  word: string;
  /** The long name, for screen readers: "Review suggested", "Verification suggested". */
  label: string;
  /** The status the state shows as: its icon (Review and Verify draw the level mark in its place) and colour. */
  status: StatusName;
  /** How many of the level mark's three steps are filled; 0 where the state has no mark. */
  steps: 0 | 2 | 3;
  /** The level a Dismissed state was assessed at: the level stays as assessed (ADR-0004). */
  assessedAs?: AuthenticityLevel;
}

const stateOfLevel: Record<AuthenticityLevel, VerificationState> = {
  NoConcerns: { kind: 'clear', word: 'Clear', label: 'No concerns', status: 'done', steps: 0 },
  ReviewSuggested: { kind: 'review', word: 'Review', label: 'Review suggested', status: 'review', steps: 2 },
  VerificationRecommended: { kind: 'verify', word: 'Verify', label: 'Verification suggested', status: 'verification', steps: 3 },
};

/** The filter's choices in order, each by its one word: Clear, Review, Verify, then Pending. */
export const verificationFilterOptions: readonly { value: AuthenticityLevel | 'Pending'; word: string }[] = [
  ...(['NoConcerns', 'ReviewSuggested', 'VerificationRecommended'] as const).map((level) => ({ value: level, word: stateOfLevel[level].word })),
  { value: 'Pending', word: 'Pending' },
];

export const pendingState: VerificationState = { kind: 'pending', word: 'Pending', label: 'Checks pending', status: 'working', steps: 0 };

/**
 * An application's verification state from its list row's summary (or the detail's authenticity level and whether a
 * reviewer dismissed it). A dismissed result above the lowest level is Dismissed, and `assessedAs` keeps the level it had.
 * No summary means the checks have not finished.
 */
export function verificationStateOf(level: AuthenticityLevel | null | undefined, dismissed: boolean): VerificationState {
  if (!level) return pendingState;
  if (dismissed && level !== 'NoConcerns') {
    return { kind: 'dismissed', word: 'Dismissed', label: `${stateOfLevel[level].label}, dismissed after review`, status: 'notInEffect', steps: 0, assessedAs: level };
  }
  return stateOfLevel[level];
}

export function stateOf(summary: VerificationSummary | null | undefined): VerificationState {
  return verificationStateOf(summary?.authenticity, summary?.authenticityDismissed ?? false);
}

/** The rank the Verification column sorts by: Verify, Review, Dismissed, Clear. Pending has none and sorts last either way. */
const sortRanks: Record<VerificationStateKind, number | undefined> = { verify: 4, review: 3, dismissed: 2, clear: 1, pending: undefined };

export function verificationRank(application: ApplicationSummary): number | undefined {
  return sortRanks[stateOf(application.verification).kind];
}

/** Whether the AI mark shows: only Some and Strong signs. It is information and never sorts, filters or counts. */
export function aiMarkOf(summary: VerificationSummary | null | undefined): AiAssistanceLevel | undefined {
  const level = summary?.aiAssistance;
  return level === 'SomeIndicators' || level === 'StrongIndicators' ? level : undefined;
}

/**
 * The one sentence the candidate's Overview gives for a verification state that needs a look. It asks for a person's next
 * step and never speaks of rejecting (ADR-0004). Clear, Pending and Dismissed have none.
 */
export function overviewStepOf(state: VerificationState): string | undefined {
  if (state.kind === 'verify') return 'Confirm who they are on a short video call before interviewing.';
  if (state.kind === 'review') return 'Read the evidence before the first screen.';
  return undefined;
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
  /** Clear, Review or Verify, or Pending; AI assistance is information and never filters. */
  authenticity: AuthenticityLevel | 'Pending' | null;
}

export const noVerificationFilter: VerificationFilter = { authenticity: null };

export function matchesVerificationFilter(application: ApplicationSummary, filter: VerificationFilter): boolean {
  const verification = application.verification;
  if (!filter.authenticity) return true;
  if (filter.authenticity === 'Pending') return !verification;
  return verification?.authenticity === filter.authenticity;
}

export function isVerificationFiltered(filter: VerificationFilter): boolean {
  return Boolean(filter.authenticity);
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
 * After a dismissal, a note or an explanation: they change the verification panel, Activity, the list chips
 * (dismissed) and the "Needs review" counts, and no live event announces them.
 */
export function refetchAfterReview(queryClient: QueryClient): Promise<unknown> {
  return Promise.all([invalidatePath(queryClient, applicationsPath), invalidatePath(queryClient, jobsPath)]);
}

/** Who took a review action, as the verification panel and Activity name them: "you", or their name. */
export function reviewerName(userId: string | null, name: string | null, currentUserId: string | undefined): string {
  if (userId !== null && userId === currentUserId) return 'you';
  return name ?? 'a teammate';
}
