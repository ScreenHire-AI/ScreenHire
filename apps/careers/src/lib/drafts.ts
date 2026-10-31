// Saved drafts on the careers apply page (docs/architecture/apply-flow.md §7): the form's state from a draft the
// candidate opened from their email, and the date its answers are deleted.
import type { PublicApplicationDraft } from '@screenhire/api-client';
import { emptyState, type ApplyState } from './apply';

/**
 * The form's state from `draft`: its details, answers, switch and AI-use answer, with the draft named so saving again
 * updates it. No files (a draft keeps none) and no time on the pages: this sitting's time starts now.
 */
export function fromDraft(draft: PublicApplicationDraft, token: string, telemetrySession: string): ApplyState {
  const state = emptyState(telemetrySession);
  for (const answer of draft.answers) {
    if (answer.values && answer.values.length > 0) state.answers[answer.questionId] = [...answer.values];
    else if (answer.value) state.answers[answer.questionId] = answer.value;
  }

  return {
    ...state,
    firstName: draft.firstName,
    lastName: draft.lastName ?? '',
    email: draft.email,
    phone: draft.phone ?? '',
    accommodationRequested: draft.accommodationRequested,
    aiUseDeclaration: draft.aiUseDeclaration,
    aiUseDetails: draft.aiUseDetails ?? '',
    draft: { token, email: draft.email },
  };
}

/** A date as the candidate reads it, such as "November 29, 2026", in UTC so it matches the email. */
export const longDate = (iso: string): string =>
  new Date(iso).toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
