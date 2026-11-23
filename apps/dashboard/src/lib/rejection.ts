// The principal reason for a rejection, kept on the move's entry in Activity so the team can tell a candidate why in plain
// words (docs/research/compliance-gap-map.md, CO-5). The list is the API's (RejectionReason.cs); there is no reason about
// the checks, because a check never rejects anyone (ADR-0004).
import type { RejectionReason as Nullable } from '@screenhire/api-client';

/** A reason the move keeps; the generated type also allows null, for a move with none. */
export type RejectionReason = NonNullable<Nullable>;

export const rejectionReasonLabels: Record<RejectionReason, string> = {
  RequirementNotMet: 'Did not meet a stated requirement',
  KnockoutAnswer: 'An answer to a knockout question',
  AnotherCandidate: 'Another candidate was a closer fit',
  PositionClosed: 'The position was filled or closed',
  NoResponse: 'No reply from the candidate',
  Other: 'Other (see the note)',
};

/** The reasons a person can pick: a knockout answer is the reason an automation gives, and the API refuses it from anyone else. */
export const personRejectionReasons: readonly RejectionReason[] = ['RequirementNotMet', 'AnotherCandidate', 'PositionClosed', 'NoResponse', 'Other'];
