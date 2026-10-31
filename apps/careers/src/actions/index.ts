import { defineAction } from 'astro:actions';
import { applyStep } from '../lib/apply-server';
import { answerQuestionnaire } from '../lib/questionnaire';
import { stopKeeping } from '../lib/talent-pool';

/** The careers server's address for the candidate, or undefined where Astro cannot tell (it throws when prerendering). */
const addressOf = (context: { clientAddress: string }): string | undefined => {
  try {
    return context.clientAddress;
  } catch {
    return undefined;
  }
};

export const server = {
  /**
   * One step of the apply form, posted as an HTML form, so the whole flow works with JavaScript off. Returns what the
   * page renders next; never throws for the candidate's mistakes, so what they typed is never lost.
   */
  apply: defineAction({
    accept: 'form',
    // `embed` is the site the careers page is embedded in, which the step form's address keeps
    // (docs/architecture/careers-widget.md); applyStep checks it against the organization's list.
    handler: async (form: FormData, context) =>
      applyStep(form, addressOf(context), context.request.headers.get('user-agent'), new Date(), context.url.searchParams.get('embed')),
  }),

  /**
   * A follow-up questionnaire's answers, posted as an HTML form from the link in the candidate's email. Answers only:
   * nothing about the browser goes with them. Never throws for the candidate's mistakes.
   */
  questionnaire: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => answerQuestionnaire(form, addressOf(context)),
  }),

  /**
   * Stops the employer keeping the candidate's details for future roles, posted as an HTML form from the link in a
   * talent pool invitation. Never throws for the candidate.
   */
  talentPool: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => stopKeeping(form, addressOf(context)),
  }),
};
