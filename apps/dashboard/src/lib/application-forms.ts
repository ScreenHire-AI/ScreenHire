// A candidate's forms on their Application tab (workflow-engine.md section 8): each form sent to them with its state, and
// the job's forms someone may send by hand. Pure functions, so the words are tested without a page.
import type { ApplicationFollowUp, FollowUpQuestionnaire } from '@screenhire/api-client';

/** Where a sent form stands: answered, waiting for the candidate, or past its link's expiry. */
export type SentFormState = { kind: 'answered'; at: string } | { kind: 'waiting'; at: string } | { kind: 'expired'; at: string };

/** The state of a form sent to the candidate at `now`: its answer, else its last send and whether that link still works. */
export function sentFormState(followUp: ApplicationFollowUp, now: Date): SentFormState {
  if (followUp.completedAt) return { kind: 'answered', at: followUp.completedAt };
  return { kind: new Date(followUp.expiresAt) <= now ? 'expired' : 'waiting', at: followUp.lastSentAt };
}

/** What follows the time in a sent form's line: "Sent 3 days ago, waiting". */
export const stateTail: Record<SentFormState['kind'], string> = {
  answered: '',
  waiting: ', waiting',
  expired: ', its link has expired',
};

/** One of the job's forms as "Send a form" offers it. */
export type SendableForm = {
  id: string;
  name: string;
  /** Sent before and not answered: the button says Send again. */
  sent: boolean;
  /** Answered: nothing more can be sent. */
  answered: boolean;
  equality: boolean;
};

/**
 * The job's forms, in its workflow's order, each with what sending it now would do. An answered form is listed, so the
 * person sees why it cannot go again.
 */
export function sendableForms(forms: readonly FollowUpQuestionnaire[], sent: readonly ApplicationFollowUp[]): SendableForm[] {
  const byForm = new Map(sent.map((followUp) => [followUp.questionnaireId, followUp]));
  return forms.map((form) => {
    const request = byForm.get(form.id);
    return {
      id: form.id,
      name: form.name || form.id,
      sent: Boolean(request) && !request?.completedAt,
      answered: Boolean(request?.completedAt),
      equality: form.kind === 'EqualityMonitoring',
    };
  });
}
