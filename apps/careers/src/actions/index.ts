import { defineAction } from 'astro:actions';
import { applyStep } from '../lib/apply-server';
import { answerQuestionnaire } from '../lib/questionnaire';

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
    handler: async (form: FormData, context) => applyStep(form, addressOf(context), context.request.headers.get('user-agent')),
  }),

  /**
   * A follow-up questionnaire's answers, posted as an HTML form from the link in the candidate's email. Answers only:
   * nothing about the browser goes with them. Never throws for the candidate's mistakes.
   */
  questionnaire: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => answerQuestionnaire(form, addressOf(context)),
  }),
};
