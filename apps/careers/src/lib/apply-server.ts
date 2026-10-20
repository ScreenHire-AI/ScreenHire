// The apply flow on the careers server: one step of the form per request, and the uploads it makes for a browser
// without JavaScript. Server-only (it calls the API); the rules themselves are in ./apply and ./files.
import {
  createPublicUpload,
  getPublicApplicationForm,
  submitPublicApplication,
  verifyPublicUpload,
  type PublicQuestion,
  type UploadTicket,
} from './api';
import type { AiUsePolicy } from '@screenhire/api-client';
import {
  emptyState,
  errorsFromProblem,
  fields,
  parseState,
  questionsOn,
  readAnswers,
  readDetails,
  readReview,
  screeningQuestions,
  stepsFor,
  text,
  toRequest,
  validateAnswers,
  validateDetails,
  validateReview,
  type ApplyState,
  type FieldErrors,
  type Step,
} from './apply';
import { fileProblem, type UploadRef } from './files';

/** The honeypot: a field people never see or reach, so only a bot fills it in. Its name avoids anything autofill knows. */
export const HONEYPOT_FIELD = 'leave_this_empty';

/** The field Cloudflare Turnstile's widget posts its token in. */
export const TURNSTILE_FIELD = 'cf-turnstile-response';

/** What the page renders after a step: the step to show with the state so far, or how the application ended. */
export interface ApplyResult {
  step: Step;
  state: ApplyState;
  errors: FieldErrors;
  /** A problem that belongs to no field, shown at the top of the step. */
  formError?: string;
  outcome?: 'submitted' | 'closed' | 'not-found';
}

const SERVER_PROBLEM = 'Something went wrong on our side. Your answers are still here, so try again in a moment.';

/**
 * The candidate's address for the API, which rate-limits per candidate and trusts this header only from the careers
 * servers (ScreenHire:Auth:TrustedProxies).
 */
export const forwardedFor = (address: string | undefined): RequestInit | undefined =>
  address ? { headers: { 'X-Forwarded-For': address } } : undefined;

/**
 * Handles one posted step. Back and change buttons (`intent` = `go:<step>`) keep what was typed without checking it.
 * Next checks the step and moves on. On the last step, everything is checked again and sent to the API.
 */
export async function applyStep(form: FormData, clientAddress: string | undefined): Promise<ApplyResult> {
  const organization = text(form, 'organization');
  const jobSlug = text(form, 'job');
  const forwarded = forwardedFor(clientAddress);
  const response = await getPublicApplicationForm(organization, jobSlug, forwarded);
  if (response.status === 410) return { step: 'details', state: emptyState(), errors: {}, outcome: 'closed' };
  if (response.status !== 200) return { step: 'details', state: emptyState(), errors: {}, outcome: 'not-found' };

  const definition = response.data;
  const questions = definition.questions;
  const steps = stepsFor(questions);
  const posted = text(form, 'step') as Step;
  const step: Step = steps.includes(posted) ? posted : 'details';

  let state = parseState(form.get('state'));
  if (step === 'details') state = readDetails(form, state);
  state = step === 'review' ? readReview(form, state) : readAnswers(form, questionsOn(step, questions), state);

  // Files posted without JavaScript: the careers server uploads them the way the island would have.
  const errors: FieldErrors = {};
  for (const question of questionsOn(step, questions).filter((q) => q.type === 'File')) {
    const file = form.get(fields.file(question.id));
    if (!(file instanceof File) || file.name === '') continue;
    const uploaded = await uploadFile(organization, definition.job.id, question, file, definition.uploads.maxBytes, forwarded);
    if ('error' in uploaded) errors[fields.file(question.id)] = uploaded.error;
    else state = { ...state, files: { ...state.files, [question.id]: uploaded } };
  }

  const intent = text(form, 'intent');
  const target = intent.startsWith('go:') ? (intent.slice(3) as Step) : null;
  if (target && steps.includes(target)) return { step: target, state, errors: {} };

  // An upload's own problem ("larger than 10 MB") says more than the step's "Add a file." for the same field.
  const problems = { ...stepErrors(step, state, questions, definition.aiUsePolicy), ...errors };
  if (Object.keys(problems).length > 0) return { step, state, errors: problems };
  if (step !== 'review') return { step: steps[steps.indexOf(step) + 1]!, state, errors: {} };

  // Every step again: the state came back from the browser, and a step can be skipped by posting straight to the last.
  for (const each of steps) {
    const problems = stepErrors(each, state, questions, definition.aiUsePolicy);
    if (Object.keys(problems).length > 0) return { step: each, state, errors: problems };
  }

  const request = toRequest(state, questions, {
    honeypotFilled: text(form, HONEYPOT_FIELD) !== '',
    botCheckToken: text(form, TURNSTILE_FIELD) || null,
  });
  const submitted = await submitPublicApplication(organization, definition.job.id, request, forwarded);
  switch (submitted.status) {
    case 201:
    // Already applied: the same confirmation, so the form never tells anyone whether an email address applied.
    case 409:
      return { step, state, errors: {}, outcome: 'submitted' };
    case 410:
      return { step, state, errors: {}, outcome: 'closed' };
    case 400: {
      const placed = errorsFromProblem(submitted.data.errors ?? {}, questions);
      return {
        step: steps.includes(placed.step) ? placed.step : 'review',
        state,
        errors: placed.errors,
        formError: placed.unplaced[0],
      };
    }
    case 429:
      return { step, state, errors: {}, formError: 'Too many attempts from your network. Wait a minute, then send again.' };
    default:
      return { step, state, errors: {}, formError: SERVER_PROBLEM };
  }
}

function stepErrors(step: Step, state: ApplyState, questions: readonly PublicQuestion[], policy: AiUsePolicy) {
  switch (step) {
    case 'details':
      return validateDetails(state, questions);
    case 'questions':
      return validateAnswers(state, screeningQuestions(questions));
    case 'review':
      return validateReview(state, policy);
  }
}

/** Asks the API for an upload link for a file question. */
export async function requestUpload(
  organization: string,
  jobId: string,
  questionId: string,
  file: { fileName: string; sizeBytes: number },
  forwarded: RequestInit | undefined,
): Promise<UploadTicket | { error: string }> {
  const response = await createPublicUpload(organization, jobId, questionId, file, forwarded);
  if (response.status === 200) return response.data;
  if (response.status === 400) return { error: firstProblem(response.data.errors) ?? 'We could not take this file.' };
  if (response.status === 410) return { error: 'This job has closed.' };
  return { error: 'We could not start the upload. Try again in a moment.' };
}

/** Asks the API to check an upload that finished; it deletes a file that fails. */
export async function checkUpload(
  organization: string,
  jobId: string,
  questionId: string,
  uploadId: string,
  forwarded: RequestInit | undefined,
): Promise<UploadRef | { error: string }> {
  const response = await verifyPublicUpload(organization, jobId, questionId, uploadId, forwarded);
  if (response.status === 200) return { uploadId: response.data.uploadId, fileName: response.data.fileName, sizeBytes: response.data.sizeBytes };
  if (response.status === 400) return { error: firstProblem(response.data.errors) ?? 'We could not take this file.' };
  return { error: 'We could not check the upload. Try again in a moment.' };
}

async function uploadFile(
  organization: string,
  jobId: string,
  question: PublicQuestion,
  file: File,
  maxBytes: number,
  forwarded: RequestInit | undefined,
): Promise<UploadRef | { error: string }> {
  const problem = fileProblem(file, question.acceptedFileTypes ?? [], maxBytes);
  if (problem) return { error: problem };

  const ticket = await requestUpload(organization, jobId, question.id, { fileName: file.name, sizeBytes: file.size }, forwarded);
  if ('error' in ticket) return ticket;

  // Straight to blob storage through the upload link, as the browser would: not an API route, so no generated client.
  const put = await fetch(ticket.uploadUrl, {
    method: ticket.method,
    headers: { ...ticket.headers, 'Content-Type': file.type || 'application/octet-stream' },
    body: file,
  });
  if (!put.ok) return { error: 'We could not upload this file. Try again in a moment.' };

  return checkUpload(organization, jobId, question.id, ticket.uploadId, forwarded);
}

const firstProblem = (errors: Record<string, string[]> | null | undefined): string | undefined =>
  errors ? Object.values(errors).flat()[0] : undefined;
