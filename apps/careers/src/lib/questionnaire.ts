// A follow-up questionnaire on the careers server (workflow-engine.md §8): the candidate opens the signed link from
// their email, answers page by page, and each page posts to the `questionnaire` action. Server-only (it calls the API).
// Answers only: no device check, telemetry, upload or time on the pages, so the page ships no JavaScript. Saving to
// finish later keeps the answers on the request; the same link opens them again (apply-flow.md §8).
import { answerPublicQuestionnaire, getPublicQuestionnaire, savePublicQuestionnaireDraft, type ApplicationAnswer, type PublicQuestion } from './api';
import {
  emptyState,
  errorsFromProblem,
  pagesFor,
  parseState,
  postedQuestions,
  readAnswers,
  text,
  validateAnswers,
  type FieldErrors,
  type FormShape,
  type Step,
} from './apply';
import { forwardedFor } from './apply-server';

/** What the page renders after a post: the page to show with the answers so far, or how the questionnaire ended. */
export interface QuestionnaireResult {
  step: Step;
  answers: Record<string, string | string[]>;
  errors: FieldErrors;
  /** A problem that belongs to no field, shown at the top. */
  formError?: string;
  outcome?: 'sent' | 'answered' | 'expired' | 'closed' | 'not-found';
  /** The answers were saved to finish later. */
  saved?: boolean;
}

/**
 * A link token as the API issues it: Data Protection's base64url text. Anything else is refused before it reaches an
 * API path, so a crafted link cannot change the route the careers server calls.
 */
export const isToken = (token: string | undefined | null): token is string => !!token && /^[A-Za-z0-9_-]{16,1000}$/.test(token);

const SERVER_PROBLEM = 'Something went wrong on our side. Your answers are still here, so try again in a moment.';

/** The steps of a follow-up: one per page, no details or review step. */
export const questionnaireSteps = (form: FormShape): Step[] => pagesFor(form).map((page) => page.step);

/** The answers a draft holds, as the form holds them. */
export function answersFrom(answers: readonly ApplicationAnswer[]): Record<string, string | string[]> {
  const result: Record<string, string | string[]> = {};
  for (const answer of answers) {
    if (answer.values && answer.values.length > 0) result[answer.questionId] = [...answer.values];
    else if (answer.value) result[answer.questionId] = answer.value;
  }

  return result;
}

/**
 * Handles one posted page. Back (`intent` = `go:<step>`) keeps what was typed without checking it, and so does Save
 * (`intent` = `save`). Next checks the page and moves on; on the last page every page is checked again and the answers
 * are sent. Never throws for the candidate's mistakes.
 */
export async function answerQuestionnaire(form: FormData, clientAddress: string | undefined): Promise<QuestionnaireResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  if (!isToken(token)) return { step: 'page-1', answers: {}, errors: {}, outcome: 'not-found' };

  const forwarded = forwardedFor(clientAddress);
  const response = await getPublicQuestionnaire(organization, token, forwarded);
  if (response.status === 404) return { step: 'page-1', answers: {}, errors: {}, outcome: 'not-found' };
  if (response.status !== 200) return { step: 'page-1', answers: {}, errors: {}, formError: SERVER_PROBLEM };

  const questionnaire = response.data;
  if (questionnaire.status !== 'Open') return { step: 'page-1', answers: {}, errors: {}, outcome: ended(questionnaire.status) };

  const questions = questionnaire.questions;
  const pages = pagesFor(questionnaire);
  const steps = pages.map((page) => page.step);
  const posted = text(form, 'step') as Step;
  const step: Step = steps.includes(posted) ? posted : (steps[0] ?? 'page-1');
  const answers = readAnswers(form, postedQuestions(form, questions), { ...emptyState(), answers: parseState(form.get('state')).answers }).answers;

  const intent = text(form, 'intent');
  const target = intent.startsWith('go:') ? (intent.slice(3) as Step) : null;
  if (target && steps.includes(target)) return { step: target, answers, errors: {} };

  if (intent === 'save') {
    const saved = await savePublicQuestionnaireDraft(organization, token, { answers: toAnswers(answers, questions) }, forwarded);
    switch (saved.status) {
      case 204:
        return { step, answers, errors: {}, saved: true };
      case 409:
        return { step, answers, errors: {}, outcome: 'answered' };
      case 410:
        return { step, answers, errors: {}, outcome: 'expired' };
      case 404:
        return { step, answers, errors: {}, outcome: 'not-found' };
      default:
        return { step, answers, errors: {}, formError: 'We could not save your answers. Try again in a moment.' };
    }
  }

  const pageQuestions = pages.find((page) => page.step === step)?.questions ?? [];
  const problems = validateAnswers({ ...emptyState(), answers }, pageQuestions);
  if (Object.keys(problems).length > 0) return { step, answers, errors: problems };
  if (step !== steps.at(-1)) return { step: steps[steps.indexOf(step) + 1]!, answers, errors: {} };

  // Every page again: the answers came back from the browser, and a page can be skipped by posting straight to the last.
  for (const page of pages) {
    const problems = validateAnswers({ ...emptyState(), answers }, page.questions);
    if (Object.keys(problems).length > 0) return { step: page.step, answers, errors: problems };
  }

  const sent = await answerPublicQuestionnaire(organization, token, { answers: toAnswers(answers, questions) }, forwarded);
  switch (sent.status) {
    case 204:
      return { step, answers, errors: {}, outcome: 'sent' };
    case 409:
      return { step, answers, errors: {}, outcome: 'answered' };
    case 410:
      return { step, answers, errors: {}, outcome: 'expired' };
    case 404:
      return { step, answers, errors: {}, outcome: 'not-found' };
    case 400: {
      const placed = errorsFromProblem(sent.data.errors ?? {}, questions, questionnaire);
      return { step: steps.includes(placed.step) ? placed.step : step, answers, errors: placed.errors, formError: placed.unplaced[0] };
    }
    case 429:
      return { step, answers, errors: {}, formError: 'Too many attempts from your network. Wait a minute, then send again.' };
    default:
      return { step, answers, errors: {}, formError: SERVER_PROBLEM };
  }
}

const ended = (status: 'Answered' | 'Expired' | 'Closed'): QuestionnaireResult['outcome'] =>
  status === 'Answered' ? 'answered' : status === 'Expired' ? 'expired' : 'closed';

/** The answers in the API's shape, in question order; unanswered questions are left out. */
export function toAnswers(answers: Record<string, string | string[]>, questions: readonly PublicQuestion[]) {
  return questions.flatMap((question) => {
    const answer = answers[question.id];
    if (answer === undefined) return [];
    return [Array.isArray(answer) ? { questionId: question.id, values: answer } : { questionId: question.id, value: answer }];
  });
}
