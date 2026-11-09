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

export const activityOutcomeLabels: Record<VerificationActivityOutcome, string> = {
  Completed: 'Completed',
  Found: 'Found something',
  NotRun: 'Did not run',
  Failed: 'Failed',
};

/** Found is a result to read, not a failure, so it takes the review colour, never the verify one (T1). */
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
  FormUse: 'Form use',
};
