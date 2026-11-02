import type { CandidateEmailIssue } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** The candidate's status beside the name when the email provider flagged their address (T1, T2): its icon, colour and label. */
export function emailIssueStatus(issue: CandidateEmailIssue | undefined): { status: StatusName; label: string } | undefined {
  if (issue === 'Bounced') return { status: 'needsFixes', label: 'Email bounced' };
  if (issue === 'SpamComplaint') return { status: 'needsFixes', label: 'Marked as spam' };
  return undefined;
}

/**
 * What the flag means for the team, in words (E2): ScreenHire sends this address nothing more. It is a delivery fact and never
 * a judgement of the candidate, so the sentence says so.
 */
export function emailIssueNotice(issue: CandidateEmailIssue | undefined, reason?: string | null): string | undefined {
  if (issue === 'Bounced') {
    return `ScreenHire does not email this address any more${reason ? ` (${reason})` : ''}. This says nothing else about the candidate. Ask them for another address, or allow email again if you know this one works.`;
  }

  if (issue === 'SpamComplaint') {
    return 'ScreenHire does not email this address any more. This says nothing else about the candidate.';
  }

  return undefined;
}
