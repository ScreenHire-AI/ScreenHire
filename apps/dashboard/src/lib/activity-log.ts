import type { CheckGroup, VerificationActivityOutcome } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';
import { checkLabels } from '#lib/ai-usage.js';

// The words of the Activity log (docs/architecture/verification-engine.md 10.12): the checks that ran and the model calls the
// AI checks made. A result says what the check did, never what it means for a person.

/** The analyzers' names as a person reads them; a model call's check uses `checkLabels`. */
const analyzerLabels: Record<string, string> = {
  network: 'Network address',
  identity: 'Email and phone',
  documents: 'Uploaded documents',
  answers: 'Written answers',
  consistency: 'Consistency across sources',
  duplicates: 'Other applications',
  automation: 'Automation markers',
  behavioural: 'How the form was filled in',
};

export const activityCheckLabel = (check: string) => analyzerLabels[check] ?? checkLabels[check] ?? check;

/**
 * How a run or a check ended, as its status's words (Nothing found, Found, Did not run). A model call is no check: it answered or
 * it failed (`callOutcomeLabels`).
 */
export const activityOutcomeLabels: Record<VerificationActivityOutcome, string> = {
  Completed: 'Nothing found',
  Found: 'Found',
  NotRun: 'Did not run',
  Failed: 'Failed',
};

/** A model call's outcome: the provider answered, or the call failed. */
export const callOutcomeLabels: Partial<Record<VerificationActivityOutcome, string>> = {
  Completed: 'Answered',
  Failed: 'Failed',
};

/** The outcomes the log filters by: a run's three. */
export const activityOutcomeFilters: readonly VerificationActivityOutcome[] = ['Found', 'Completed', 'NotRun'];

/** A run's one line, as its row reads: "Frontend Engineer (Svelte), 8 checks, 1 did not run". */
export function runLabel(run: { jobTitle?: string | null; checks: number; notRun: number; items: readonly { check: string }[] }): string {
  if (run.checks === 0) return [run.jobTitle, run.items[0] ? activityCheckLabel(run.items[0].check) : 'Model call'].filter(Boolean).join(', ');
  const parts = [run.jobTitle ?? 'Application', `${run.checks} ${run.checks === 1 ? 'check' : 'checks'}`];
  if (run.notRun > 0) parts.push(`${run.notRun} did not run`);
  return parts.join(', ');
}

/** Found is a result to read, not a failure, so it takes the review colour, never the verify one (T1). Nothing found is calm. */
export const activityOutcomeStatuses: Record<VerificationActivityOutcome, StatusName> = {
  Completed: 'done',
  Found: 'review',
  NotRun: 'notInEffect',
  Failed: 'needsFixes',
};

export const activityGroupLabels: Record<CheckGroup, string> = {
  Network: 'Network',
  Contact: 'Contact details',
  Documents: 'Documents',
  Answers: 'Answers',
  Duplicates: 'Duplicates',
  FormUse: 'Form activity',
};
