// A follow-up questionnaire on the careers server (workflow-engine.md §8): the candidate opens the signed link from
// their email, answers, and the form posts to the `questionnaire` action. Server-only (it calls the API). Answers only:
// no device check, telemetry or upload, so the page ships no JavaScript.
import { answerPublicQuestionnaire, getPublicQuestionnaire, type PublicQuestion } from './api';
import { emptyState, errorsFromProblem, readAnswers, text, validateAnswers, type FieldErrors } from './apply';
import { forwardedFor } from './apply-server';

/** What the page renders after a post: the answers back with their problems, or how the questionnaire ended. */
export interface QuestionnaireResult {
  answers: Record<string, string | string[]>;
  errors: FieldErrors;
  /** A problem that belongs to no field, shown at the top. */
  formError?: string;
  outcome?: 'sent' | 'answered' | 'expired' | 'closed' | 'not-found';
}

/**
 * A link token as the API issues it: Data Protection's base64url text. Anything else is refused before it reaches an
 * API path, so a crafted link cannot change the route the careers server calls.
 */
export const isToken = (token: string | undefined): token is string => !!token && /^[A-Za-z0-9_-]{16,1000}$/.test(token);

const SERVER_PROBLEM = 'Something went wrong on our side. Your answers are still here, so try again in a moment.';

/** Handles the posted form: checks the answers, then sends them. Never throws for the candidate's mistakes. */
export async function answerQuestionnaire(form: FormData, clientAddress: string | undefined): Promise<QuestionnaireResult> {
  const organization = text(form, 'organization');
  const token = text(form, 'token');
  if (!isToken(token)) return { answers: {}, errors: {}, outcome: 'not-found' };

  const forwarded = forwardedFor(clientAddress);
  const response = await getPublicQuestionnaire(organization, token, forwarded);
  if (response.status === 404) return { answers: {}, errors: {}, outcome: 'not-found' };
  if (response.status !== 200) return { answers: {}, errors: {}, formError: SERVER_PROBLEM };

  const questionnaire = response.data;
  if (questionnaire.status !== 'Open') return { answers: {}, errors: {}, outcome: ended(questionnaire.status) };

  const questions = questionnaire.questions;
  const answers = readAnswers(form, questions, emptyState()).answers;
  const errors = validateAnswers({ ...emptyState(), answers }, questions);
  if (Object.keys(errors).length > 0) return { answers, errors };

  const sent = await answerPublicQuestionnaire(organization, token, { answers: toAnswers(answers, questions) }, forwarded);
  switch (sent.status) {
    case 204:
      return { answers, errors: {}, outcome: 'sent' };
    case 409:
      return { answers, errors: {}, outcome: 'answered' };
    case 410:
      return { answers, errors: {}, outcome: 'expired' };
    case 404:
      return { answers, errors: {}, outcome: 'not-found' };
    case 400: {
      const placed = errorsFromProblem(sent.data.errors ?? {}, questions);
      return { answers, errors: placed.errors, formError: placed.unplaced[0] };
    }
    case 429:
      return { answers, errors: {}, formError: 'Too many attempts from your network. Wait a minute, then send again.' };
    default:
      return { answers, errors: {}, formError: SERVER_PROBLEM };
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
