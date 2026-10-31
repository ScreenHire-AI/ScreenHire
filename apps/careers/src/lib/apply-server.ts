// The apply flow on the careers server: one step of the form per request, and the uploads it makes for a browser
// without JavaScript. Server-only (it calls the API); the rules themselves are in ./apply and ./files.
import {
  createPublicApplicationDraft,
  createPublicUpload,
  getPublicApplicationForm,
  savePublicApplicationDraft,
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
  postedQuestions,
  questionsOn,
  readAnswers,
  readDetails,
  readReview,
  recordTime,
  stepsFor,
  text,
  toDraftRequest,
  toRequest,
  validateAnswers,
  validateDetails,
  validateReview,
  type ApplyState,
  type FieldErrors,
  type FormShape,
  type Step,
} from './apply';
import { fileProblem, type UploadRef } from '@screenhire/ui/questions/files';
import { parseFingerprint, parseMarkers, parseSnapshot } from './telemetry';

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
  /** The draft was saved: where its link went and when it is deleted. */
  saved?: { email: string; expiresAt: string };
}

const SERVER_PROBLEM = 'Something went wrong on our side. Your answers are still here, so try again in a moment.';

/**
 * The candidate's address for the API, which rate-limits per candidate and trusts this header only from the careers
 * servers (ScreenHire:Auth:TrustedProxies).
 */
export const forwardedFor = (address: string | undefined): RequestInit | undefined =>
  address ? { headers: { 'X-Forwarded-For': address } } : undefined;

/**
 * Handles one posted step. Back and Change buttons (`intent` = `go:<step>`, or `change:<step>` from the review, which
 * brings the candidate back to it) keep what was typed without checking it, and so does Save (`intent` = `save`), which
 * keeps a draft to finish later. Next checks the step and moves on. On the last step, everything is checked again and
 * sent to the API, with the candidate's browser (`userAgent`), the time on each page and what the telemetry module left
 * in the form for the integrity engine.
 */
export async function applyStep(
  form: FormData,
  clientAddress: string | undefined,
  userAgent?: string | null,
  now: Date = new Date(),
): Promise<ApplyResult> {
  const organization = text(form, 'organization');
  const jobSlug = text(form, 'job');
  const forwarded = forwardedFor(clientAddress);
  const response = await getPublicApplicationForm(organization, jobSlug, forwarded);
  if (response.status === 410) return { step: 'details', state: emptyState(), errors: {}, outcome: 'closed' };
  if (response.status !== 200) return { step: 'details', state: emptyState(), errors: {}, outcome: 'not-found' };

  const definition = response.data;
  const questions = definition.questions;
  const steps = stepsFor(definition);
  const posted = text(form, 'step') as Step;
  const step: Step = steps.includes(posted) ? posted : 'details';

  // The time on this step goes in before anything else: Back, Save and a step with a problem all count.
  let state = recordTime(parseState(form.get('state')), step, now);
  if (step === 'details') state = readDetails(form, state);
  const shown = postedQuestions(form, questions);
  state = step === 'review' ? readReview(form, state) : readAnswers(form, shown, state);

  // Files posted without JavaScript: the careers server uploads them the way the island would have.
  const errors: FieldErrors = {};
  for (const question of shown.filter((q) => q.type === 'File')) {
    const file = form.get(fields.file(question.id));
    if (!(file instanceof File) || file.name === '') continue;
    const uploaded = await uploadFile(organization, definition.job.id, question, file, definition.uploads.maxBytes, forwarded);
    if ('error' in uploaded) errors[fields.file(question.id)] = uploaded.error;
    else state = { ...state, files: { ...state.files, [question.id]: uploaded } };
  }

  const intent = text(form, 'intent');
  const [move, target] = intent.split(':') as [string, Step | undefined];
  if ((move === 'go' || move === 'change') && target && steps.includes(target)) {
    const returnTo = target === 'review' ? '' : move === 'change' ? 'review' : state.returnTo;
    return { step: target, state: { ...state, returnTo }, errors: {} };
  }

  if (intent === 'save' && definition.saveDraftEnabled) {
    return saveDraft(organization, definition.job.id, step, state, questions, forwarded);
  }

  // An upload's own problem ("larger than 10 MB") says more than the step's "Add a file." for the same field.
  const problems = { ...stepErrors(step, state, definition), ...errors };
  if (Object.keys(problems).length > 0) return { step, state, errors: problems };
  if (step !== 'review') {
    const next = state.returnTo === 'review' ? 'review' : steps[steps.indexOf(step) + 1]!;
    return { step: next, state: next === 'review' ? { ...state, returnTo: '' } : state, errors: {} };
  }

  // Every step again: the state came back from the browser, and a step can be skipped by posting straight to the last.
  for (const each of steps) {
    const problems = stepErrors(each, state, definition);
    if (Object.keys(problems).length > 0) return { step: each, state, errors: problems };
  }

  const request = toRequest(state, questions, {
    honeypotFilled: text(form, HONEYPOT_FIELD) !== '',
    botCheckToken: text(form, TURNSTILE_FIELD) || null,
    userAgent: userAgent ?? null,
    telemetry: parseSnapshot(form.get('telemetry')),
    deviceFingerprint: parseFingerprint(form.get('deviceFingerprint')),
    automationMarkers: parseMarkers(form.get('automationMarkers')),
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
      const placed = errorsFromProblem(submitted.data.errors ?? {}, questions, definition);
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

/**
 * Keeps what the candidate typed as a draft to finish later (apply-flow.md §7). The draft in the state is saved again
 * while the email is the one its link went to; otherwise a new draft is made and the API emails its link. Either way
 * the candidate stays on the step, with a notice that says where the link went.
 */
async function saveDraft(
  organization: string,
  jobId: string,
  step: Step,
  state: ApplyState,
  questions: readonly PublicQuestion[],
  forwarded: RequestInit | undefined,
): Promise<ApplyResult> {
  const body = toDraftRequest(state, questions);
  const sameEmail = state.draft && state.draft.email.trim().toLowerCase() === state.email.trim().toLowerCase();
  if (state.draft && sameEmail) {
    const updated = await savePublicApplicationDraft(organization, state.draft.token, body, forwarded);
    if (updated.status === 200) {
      return { step, state, errors: {}, saved: { email: state.draft.email, expiresAt: updated.data.expiresAt } };
    }
    // A draft the sweep deleted, or one that expired: make a new one below, as on a first save.
  }

  const created = await createPublicApplicationDraft(organization, jobId, body, forwarded);
  switch (created.status) {
    case 201:
      return {
        step,
        state: { ...state, draft: { token: created.data.token, email: state.email } },
        errors: {},
        saved: { email: state.email, expiresAt: created.data.expiresAt },
      };
    case 410:
      return { step, state, errors: {}, outcome: 'closed' };
    case 429:
      return {
        step,
        state,
        errors: {},
        // The API's own cap names itself; the network's rate limit sends no body.
        formError: created.data?.title
          ? 'We have emailed this address several links today. Use one of them to finish, or send your application now.'
          : 'Too many attempts from your network. Wait a minute, then save again.',
      };
    default:
      return { step, state, errors: {}, formError: 'We could not save your answers. Try again in a moment, or send your application now.' };
  }
}

function stepErrors(step: Step, state: ApplyState, form: FormShape & { aiUsePolicy: AiUsePolicy }) {
  switch (step) {
    case 'details':
      return validateDetails(state, form.questions);
    case 'review':
      return validateReview(state, form.aiUsePolicy);
    default:
      return validateAnswers(state, questionsOn(step, form));
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
