import { defineAction } from 'astro:actions';
import { applyStep } from '../lib/apply-server';

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
    handler: async (form: FormData, context) => applyStep(form, addressOf(context)),
  }),
};
