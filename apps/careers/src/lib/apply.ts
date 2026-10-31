// The apply flow's state, steps and checks (docs/architecture/apply-flow.md). Pure functions over FormData and the API's
// form definition, so they unit-test without a server. The API checks everything again on submission; these checks
// give the candidate an answer at the step where they made the mistake.
import type {
  AiUsePolicy,
  ApplicationAnswer,
  PublicApplicationDraftRequest,
  PublicApplicationRequest,
  PublicQuestion,
  QuestionSection,
  TelemetrySnapshotInput,
} from '@screenhire/api-client';
import { parseUploadRef, type UploadRef } from '@screenhire/ui/questions/files';
import { fieldId, fields, questionPages, RESUME_QUESTION_ID } from '@screenhire/ui/questions/model';

/**
 * The version of "What we collect and why" (pages/[organization]/privacy.astro). Every application records the version
 * its candidate was shown before anything was collected. Change it whenever that page's substance changes.
 */
export const NOTICE_VERSION = '2026-10-30.1';

// Field names and ids come from the shared question components (packages/ui), which render the question fields; the
// error summary's links depend on them matching. The form asks for the resume first, on the details step.
export { fieldId, fields, RESUME_QUESTION_ID };

/** `details`, then one `page-N` per page of questions (apply-flow.md §7), then `review`. */
export type Step = 'details' | 'review' | `page-${number}`;

/** One page of questions: its step, its heading (null for an automatic page) and its questions in order. */
export interface FormPage {
  step: Step;
  name: string | null;
  questions: PublicQuestion[];
}

/** What paging a form needs from the API's form definition. */
export interface FormShape {
  questions: readonly PublicQuestion[];
  sections?: readonly QuestionSection[] | null;
  questionsPerPage?: number;
}

/** A draft the candidate saved: the token in its link and the address it went to. */
export interface DraftRef {
  token: string;
  email: string;
}

export type AiUseAnswer = 'NotProvided' | 'Used' | 'DidNotUse';

/** Everything the candidate has entered so far. The form carries it between steps in one hidden field. */
export interface ApplyState {
  firstName: string;
  /** Optional: a candidate may give one name. */
  lastName: string;
  email: string;
  phone: string;
  /** The assistive-technology switch: turns typing and timing signals off, never held against anyone. */
  accommodationRequested: boolean;
  /** Answers to non-file questions, by question id: text, an option id, `yes`/`no`, or option ids for multiple choice. */
  answers: Record<string, string | string[]>;
  /** Checked uploads, by file question id. */
  files: Record<string, UploadRef>;
  aiUseDeclaration: AiUseAnswer;
  aiUseDetails: string;
  /**
   * When this server showed the current step (ISO 8601), or empty. On each post of the step its time goes into
   * `pageSeconds`. Set by the server, not the browser, but it round-trips through the hidden field like everything else,
   * so the integrity engine treats the times as a hint (submit velocity measures active time per page).
   */
  pageShownAt: string;
  /** The active seconds on each step so far in this sitting, at most `MAX_PAGE_SECONDS` each. */
  pageSeconds: Record<string, number>;
  /** `review` when the candidate opened this step from the review's Change: Continue goes back there. */
  returnTo: '' | 'review';
  /** The draft this form was saved as, or resumed from; saving again updates it while the email is the same. */
  draft: DraftRef | null;
  /**
   * The telemetry session the API issued with the form the first time this server showed it, or empty. The telemetry
   * module sends its snapshots with it, and submission binds them to the application (integrity-engine.md §10.2).
   */
  telemetrySession: string;
  /** The candidate ticked the device check, where the job's basis asks for consent. */
  deviceCheckConsent: boolean;
}

/** Problems to show, by form field name, worded for the candidate. */
export type FieldErrors = Record<string, string>;

/** The most each part of the name may hold (Candidate.MaxNamePartLength). */
export const MAX_NAME = 100;
export const MAX_EMAIL = 320;
export const MAX_PHONE = 50;
export const MAX_AI_DETAILS = 2000;
/** The longest answer and the most choices the API takes (ApplicationForm.LongTextMaxLength, the request validator). */
export const MAX_ANSWER = 5000;
export const MAX_CHOICES = 50;
/** The most one page counts: a page left open longer is not more active (SubmissionObservation.MaxSecondsPerPage). */
export const MAX_PAGE_SECONDS = 1800;

export const emptyState = (telemetrySession = ''): ApplyState => ({
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  accommodationRequested: false,
  answers: {},
  files: {},
  aiUseDeclaration: 'NotProvided',
  aiUseDetails: '',
  pageShownAt: '',
  pageSeconds: {},
  returnTo: '',
  draft: null,
  telemetrySession,
  deviceCheckConsent: false,
});

/** A time stamp as this server writes it: an ISO 8601 instant that parses, or empty. */
const parseInstant = (value: unknown): string =>
  typeof value === 'string' && value.length <= 40 && /^\d{4}-\d{2}-\d{2}T/.test(value) && !Number.isNaN(Date.parse(value)) ? value : '';

/** Whether `value` names a step: `details`, `review` or `page-N`. */
const isStep = (value: unknown): value is Step =>
  typeof value === 'string' && (value === 'details' || value === 'review' || /^page-[1-9]\d?$/.test(value));

/** The questions on the details step: the resume. */
export const detailsQuestions = (questions: readonly PublicQuestion[]): PublicQuestion[] =>
  questions.filter((question) => question.id === RESUME_QUESTION_ID);

/** The questions after the details step: everything but the resume. */
export const screeningQuestions = (questions: readonly PublicQuestion[]): PublicQuestion[] =>
  questions.filter((question) => question.id !== RESUME_QUESTION_ID);

/** The pages of questions after the details step: the named sections, or pages of the organization's size. */
export function pagesFor(form: FormShape): FormPage[] {
  const byId = new Map(form.questions.map((question) => [question.id, question]));
  return questionPages(form.questions, form.sections, form.questionsPerPage ?? 8, [RESUME_QUESTION_ID]).map((page, i) => ({
    step: `page-${i + 1}` as Step,
    name: page.name,
    questions: page.questionIds.map((id) => byId.get(id)!),
  }));
}

/** The steps for a job: its details, a step per page of questions (none when it asks only for the resume), its review. */
export const stepsFor = (form: FormShape): Step[] => ['details', ...pagesFor(form).map((page) => page.step), 'review'];

/** The questions shown on a step. */
export const questionsOn = (step: Step, form: FormShape): PublicQuestion[] =>
  step === 'details'
    ? detailsQuestions(form.questions)
    : step === 'review'
      ? []
      : (pagesFor(form).find((page) => page.step === step)?.questions ?? []);

/** The step that asks `questionId`. */
export const stepOf = (questionId: string, form: FormShape): Step =>
  questionId === RESUME_QUESTION_ID
    ? 'details'
    : (pagesFor(form).find((page) => page.questions.some((q) => q.id === questionId))?.step ?? 'review');

/**
 * The questions a posted step rendered, from its hidden `pageQuestions` field. Reading only these means an answer is
 * never wiped if the pages changed between showing the step and posting it (a recruiter edited the sections).
 */
export function postedQuestions(form: FormData, questions: readonly PublicQuestion[]): PublicQuestion[] {
  const ids = new Set(text(form, 'pageQuestions').split(',').filter(Boolean));
  return questions.filter((question) => ids.has(question.id));
}

/**
 * Adds the time since the step was shown to its total, at most `MAX_PAGE_SECONDS` a page. A missing stamp adds nothing.
 * The break between saving a draft and coming back is never inside a step, so it is never counted.
 */
export function recordTime(state: ApplyState, step: Step, now: Date): ApplyState {
  const shown = Date.parse(state.pageShownAt);
  if (Number.isNaN(shown)) return state;
  const seconds = Math.max(0, Math.min(MAX_PAGE_SECONDS, Math.round((now.getTime() - shown) / 1000)));
  const total = Math.min(MAX_PAGE_SECONDS, (state.pageSeconds[step] ?? 0) + seconds);
  return { ...state, pageSeconds: { ...state.pageSeconds, [step]: total } };
}

/** The state from the hidden field. Anything malformed falls back to empty: the candidate re-enters it, nothing breaks. */
export function parseState(raw: FormDataEntryValue | null | undefined): ApplyState {
  const state = emptyState();
  if (typeof raw !== 'string' || raw.length === 0) return state;
  let value: unknown;
  try {
    value = JSON.parse(raw);
  } catch {
    return state;
  }

  if (typeof value !== 'object' || value === null) return state;
  const input = value as Record<string, unknown>;
  const string = (key: string, max: number) => (typeof input[key] === 'string' ? (input[key] as string).slice(0, max) : '');
  state.firstName = string('firstName', MAX_NAME);
  state.lastName = string('lastName', MAX_NAME);
  state.email = string('email', MAX_EMAIL);
  state.phone = string('phone', MAX_PHONE);
  state.accommodationRequested = input.accommodationRequested === true;
  state.aiUseDeclaration = isAiUseAnswer(input.aiUseDeclaration) ? input.aiUseDeclaration : 'NotProvided';
  state.aiUseDetails = string('aiUseDetails', MAX_AI_DETAILS);
  state.pageShownAt = parseInstant(input.pageShownAt);
  state.returnTo = input.returnTo === 'review' ? 'review' : '';
  state.telemetrySession = parseToken(input.telemetrySession);
  if (typeof input.draft === 'object' && input.draft !== null) {
    const draft = input.draft as Record<string, unknown>;
    const token = parseToken(draft.token);
    if (token && typeof draft.email === 'string') state.draft = { token, email: draft.email.slice(0, MAX_EMAIL) };
  }

  if (typeof input.pageSeconds === 'object' && input.pageSeconds !== null) {
    for (const [step, seconds] of Object.entries(input.pageSeconds as Record<string, unknown>).slice(0, 60)) {
      if (isStep(step) && typeof seconds === 'number' && Number.isInteger(seconds) && seconds >= 0 && seconds <= MAX_PAGE_SECONDS) {
        state.pageSeconds[step] = seconds;
      }
    }
  }
  state.deviceCheckConsent = input.deviceCheckConsent === true;

  if (typeof input.answers === 'object' && input.answers !== null) {
    for (const [id, answer] of Object.entries(input.answers as Record<string, unknown>)) {
      if (typeof answer === 'string') state.answers[id] = answer.slice(0, MAX_ANSWER);
      else if (Array.isArray(answer)) state.answers[id] = answer.filter((item): item is string => typeof item === 'string').slice(0, MAX_CHOICES);
    }
  }

  if (typeof input.files === 'object' && input.files !== null) {
    for (const [id, file] of Object.entries(input.files as Record<string, unknown>)) {
      const upload = parseUploadRef(file);
      if (upload) state.files[id] = upload;
    }
  }

  return state;
}

/** A Data Protection token as the API issues it (base64url), or empty. */
export const parseToken = (value: unknown): string =>
  typeof value === 'string' && value.length <= 1024 && /^[A-Za-z0-9_-]+$/.test(value) ? value : '';

const isAiUseAnswer = (value: unknown): value is AiUseAnswer => value === 'NotProvided' || value === 'Used' || value === 'DidNotUse';

/** A posted field's trimmed text, or empty when it is missing or a file. */
export const text = (form: FormData, name: string): string => {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
};

/** The details step's fields over `state`. */
export const readDetails = (form: FormData, state: ApplyState): ApplyState => ({
  ...state,
  firstName: text(form, 'firstName'),
  lastName: text(form, 'lastName'),
  email: text(form, 'email'),
  phone: text(form, 'phone'),
  accommodationRequested: form.get('accommodationRequested') === 'on',
  deviceCheckConsent: form.get('deviceCheckConsent') === 'on',
});

/**
 * The answers to `questions` over `state`. File questions take the upload reference the island left in its hidden
 * field; a file posted without JavaScript is the server's to upload.
 */
export function readAnswers(form: FormData, questions: readonly PublicQuestion[], state: ApplyState): ApplyState {
  const answers = { ...state.answers };
  const files = { ...state.files };
  for (const question of questions) {
    const name = fields.answer(question.id);
    if (question.type === 'File') {
      const upload = parseUploadRef(form.get(fields.upload(question.id)));
      if (upload) files[question.id] = upload;
    } else if (question.type === 'MultiChoice') {
      const chosen = form.getAll(name).filter((value): value is string => typeof value === 'string');
      if (chosen.length > 0) answers[question.id] = chosen;
      else delete answers[question.id];
    } else {
      const value = text(form, name);
      if (value.length > 0) answers[question.id] = value;
      else delete answers[question.id];
    }
  }

  return { ...state, answers, files };
}

/** The review step's AI-use answer over `state`. */
export function readReview(form: FormData, state: ApplyState): ApplyState {
  const declaration = form.get('aiUseDeclaration');
  return {
    ...state,
    aiUseDeclaration: isAiUseAnswer(declaration) ? declaration : 'NotProvided',
    // Details only go with a "yes": a "no" with leftover text from an earlier choice would contradict itself.
    aiUseDetails: declaration === 'Used' ? text(form, 'aiUseDetails') : '',
  };
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** The details step's problems: contact details and the resume. */
export function validateDetails(state: ApplyState, questions: readonly PublicQuestion[]): FieldErrors {
  const errors: FieldErrors = {};
  if (!state.firstName) errors.firstName = 'Enter your first name.';
  else if (state.firstName.length > MAX_NAME) errors.firstName = `Keep your first name to ${MAX_NAME} characters or fewer.`;
  if (state.lastName.length > MAX_NAME) errors.lastName = `Keep your last name to ${MAX_NAME} characters or fewer.`;
  if (!state.email) errors.email = 'Enter your email address.';
  else if (state.email.length > MAX_EMAIL || !emailPattern.test(state.email)) errors.email = 'Enter an email address like name@example.com.';
  if (state.phone.length > MAX_PHONE) errors.phone = `Keep your phone number to ${MAX_PHONE} characters or fewer.`;
  return { ...errors, ...validateAnswers(state, detailsQuestions(questions)) };
}

/** Problems with the answers to `questions`, matching the API's rules. Knockout answers are valid answers. */
export function validateAnswers(state: ApplyState, questions: readonly PublicQuestion[]): FieldErrors {
  const errors: FieldErrors = {};
  for (const question of questions) {
    if (question.type === 'File') {
      if (question.required && !state.files[question.id]) errors[fields.file(question.id)] = 'Add a file.';
      continue;
    }

    const name = fields.answer(question.id);
    const answer = state.answers[question.id];
    const empty = answer === undefined || (Array.isArray(answer) ? answer.length === 0 : answer.length === 0);
    if (empty) {
      if (question.required) errors[name] = 'Answer this question.';
      continue;
    }

    const problem = answerProblem(question, answer);
    if (problem) errors[name] = problem;
  }

  return errors;
}

function answerProblem(question: PublicQuestion, answer: string | string[]): string | null {
  const options = new Set((question.options ?? []).map((option) => option.id));
  if (question.type === 'MultiChoice') {
    const values = Array.isArray(answer) ? answer : [answer];
    return values.every((value) => options.has(value)) && new Set(values).size === values.length ? null : 'Choose from the options.';
  }

  if (Array.isArray(answer)) return 'This question takes one answer.';
  const max = question.maxLength ?? Number.POSITIVE_INFINITY;
  switch (question.type) {
    case 'ShortText':
    case 'LongText':
      return answer.length > max ? `Keep this to ${max} characters or fewer.` : null;
    case 'YesNo':
      return answer === 'yes' || answer === 'no' ? null : 'Choose yes or no.';
    case 'SingleChoice':
      return options.has(answer) ? null : 'Choose one of the options.';
    case 'Number': {
      const number = Number(answer);
      if (!/^[+-]?(\d+(\.\d*)?|\.\d+)$/.test(answer) || !Number.isFinite(number)) return 'Enter a number.';
      if (question.min != null && number < question.min) return `Enter ${question.min} or more.`;
      if (question.max != null && number > question.max) return `Enter ${question.max} or less.`;
      return null;
    }
    case 'Url': {
      if (answer.length > max) return `Keep this to ${max} characters or fewer.`;
      try {
        const url = new URL(answer);
        return url.protocol === 'https:' || url.protocol === 'http:' ? null : 'Enter a full web address, starting with https://.';
      } catch {
        return 'Enter a full web address, starting with https://.';
      }
    }
    default:
      return null;
  }
}

/** The review step's problems: the AI-use answer, which a job that asks for disclosure or forbids AI requires. */
export function validateReview(state: ApplyState, policy: AiUsePolicy): FieldErrors {
  const errors: FieldErrors = {};
  if (policy !== 'Allowed' && state.aiUseDeclaration === 'NotProvided') {
    errors.aiUseDeclaration = 'Tell us whether you used AI tools for this application.';
  }

  if (state.aiUseDetails.length > MAX_AI_DETAILS) errors.aiUseDetails = `Keep this to ${MAX_AI_DETAILS} characters or fewer.`;
  return errors;
}

/** The API request for the state. */
export function toRequest(
  state: ApplyState,
  questions: readonly PublicQuestion[],
  checks: {
    honeypotFilled: boolean;
    botCheckToken: string | null;
    userAgent?: string | null;
    /** What the telemetry module put in the form on the last step: its final snapshot, fingerprint and markers. */
    telemetry?: TelemetrySnapshotInput | null;
    deviceFingerprint?: string | null;
    automationMarkers?: string[];
  },
): PublicApplicationRequest {
  // The module never runs with the assistive-technology switch on; nothing it might have left is sent either, and no
  // time on the pages is sent with it on: timing is off with the switch (CLAUDE.md non-negotiable 4).
  const measured = !state.accommodationRequested;
  const answers: ApplicationAnswer[] = [];
  for (const question of questions) {
    if (question.type === 'File') {
      const upload = state.files[question.id];
      if (upload) answers.push({ questionId: question.id, value: upload.uploadId });
      continue;
    }

    const answer = answerOf(state, question.id);
    if (answer) answers.push(answer);
  }

  return {
    candidate: { firstName: state.firstName, lastName: state.lastName || null, email: state.email, phone: state.phone || null },
    privacyNoticeVersion: NOTICE_VERSION,
    answers,
    accommodationRequested: state.accommodationRequested,
    aiUseDeclaration: state.aiUseDeclaration,
    aiUseDetails: state.aiUseDetails || null,
    honeypotFilled: checks.honeypotFilled,
    botCheckToken: checks.botCheckToken,
    // Session context for the integrity engine: the candidate's browser (the API only sees this server's) and the
    // active seconds on each page.
    userAgent: checks.userAgent ? checks.userAgent.slice(0, 1024) : null,
    pageTimes: measured ? Object.entries(state.pageSeconds).map(([page, seconds]) => ({ page, seconds })) : null,
    automationMarkers: measured ? (checks.automationMarkers ?? []) : [],
    deviceFingerprint: measured ? (checks.deviceFingerprint ?? null) : null,
    deviceCheckConsent: state.deviceCheckConsent,
    telemetrySessionToken: state.telemetrySession || null,
    telemetry: measured ? (checks.telemetry ?? null) : null,
  };
}

const answerOf = (state: ApplyState, questionId: string): ApplicationAnswer | null => {
  const answer = state.answers[questionId];
  if (answer === undefined) return null;
  return Array.isArray(answer) ? { questionId, values: answer } : { questionId, value: answer };
};

/**
 * What a draft keeps of the state (apply-flow.md §7): the details and the answers to the non-file questions, the switch
 * and the AI-use answer. Never files, the telemetry session, the device check or the time on the pages.
 */
export function toDraftRequest(state: ApplyState, questions: readonly PublicQuestion[]): PublicApplicationDraftRequest {
  return {
    candidate: { firstName: state.firstName, lastName: state.lastName || null, email: state.email, phone: state.phone || null },
    answers: questions.filter((question) => question.type !== 'File').flatMap((question) => answerOf(state, question.id) ?? []),
    accommodationRequested: state.accommodationRequested,
    aiUseDeclaration: state.aiUseDeclaration,
    aiUseDetails: state.aiUseDetails || null,
  };
}

const candidateFields: Record<string, string> = {
  'candidate.firstName': 'firstName',
  'candidate.lastName': 'lastName',
  'candidate.email': 'email',
  'candidate.phone': 'phone',
};

/**
 * The API's validation problem as field errors, and the earliest step that has one, so the candidate lands where the
 * first problem is. Keys the form has no field for come back in `unplaced`. Without `form` every question but the
 * resume counts as the first page's.
 */
export function errorsFromProblem(
  problems: Record<string, string[]>,
  questions: readonly PublicQuestion[],
  form: FormShape = { questions, questionsPerPage: Number.MAX_SAFE_INTEGER },
): { errors: FieldErrors; step: Step; unplaced: string[] } {
  const errors: FieldErrors = {};
  const unplaced: string[] = [];
  const steps = new Set<Step>();
  for (const [key, messages] of Object.entries(problems)) {
    const message = messages[0] ?? 'Check this answer.';
    const questionId = key.startsWith('answers.') ? key.slice('answers.'.length) : null;
    const question = questionId ? questions.find((q) => q.id === questionId) : undefined;
    if (candidateFields[key]) {
      errors[candidateFields[key]] = message;
      steps.add('details');
    } else if (question) {
      errors[question.type === 'File' ? fields.file(question.id) : fields.answer(question.id)] = message;
      steps.add(stepOf(question.id, form));
    } else if (key === 'aiUseDeclaration' || key === 'aiUseDetails') {
      errors[key] = message;
      steps.add('review');
    } else {
      unplaced.push(message);
    }
  }

  const step = stepsFor(form).find((s) => steps.has(s)) ?? 'review';
  return { errors, step, unplaced };
}
