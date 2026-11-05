import { defineAction } from 'astro:actions';
import { applyStep } from '../lib/apply-server';
import { makeRequest, requestLink } from '../lib/data-request';
import { changeInterview } from '../lib/interview';
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
    // (docs/architecture/careers-widget.md); applyStep checks it against the organization's list. `src` is the job board
    // whose link brought the candidate (docs/architecture/apply-flow.md), kept the same way.
    handler: async (form: FormData, context) =>
      applyStep(
        form,
        addressOf(context),
        context.request.headers.get('user-agent'),
        new Date(),
        context.url.searchParams.get('embed'),
        context.url.searchParams.get('src'),
      ),
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
   * An interview's time, picked, moved or cancelled from the link in the invitation email (docs/architecture/interview-scheduling.md),
   * posted as HTML forms. The choice and nothing about the browser goes with it. Never throws for the candidate.
   */
  interviewBook: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => changeInterview('book', form, addressOf(context)),
  }),
  interviewReschedule: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => changeInterview('reschedule', form, addressOf(context)),
  }),
  interviewCancel: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => changeInterview('cancel', form, addressOf(context)),
  }),

  /**
   * Stops the employer keeping the candidate's details for future roles, posted as an HTML form from the link in a
   * talent pool invitation. Never throws for the candidate.
   */
  talentPool: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => stopKeeping(form, addressOf(context)),
  }),

  /**
   * Asks for the emailed link to a data request (docs/architecture/data-requests.md), posted as an HTML form with the bot
   * check's token. The answer never says whether the address applied. Never throws for the candidate.
   */
  requestDataLink: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => requestLink(form, addressOf(context)),
  }),

  /** A copy or erasure, chosen on the page the emailed link opens. The token travels in a hidden field. Never throws for the candidate. */
  makeDataRequest: defineAction({
    accept: 'form',
    handler: async (form: FormData, context) => makeRequest(form, addressOf(context)),
  }),
};
