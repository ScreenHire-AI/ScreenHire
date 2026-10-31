// What a candidate sees for one screening question, as pure functions over the question's definition. The careers
// apply page and the dashboard's questionnaire preview both render through these, so the preview is the form. The names
// and ids are a contract with the careers apply flow: the native form posts `q:{id}`, and its error summary links to
// `#field-...` (apps/careers/src/lib/apply.ts).
import { acceptedLabel, describeSize } from './files';

export type QuestionKind = 'ShortText' | 'LongText' | 'SingleChoice' | 'MultiChoice' | 'YesNo' | 'Number' | 'Url' | 'File';

/**
 * A screening question as the form needs it. Both the API's public form question and the hiring definition's question
 * fit this shape, so neither side converts.
 */
export interface QuestionSpec {
  id: string;
  type: QuestionKind;
  label: string;
  required?: boolean | null;
  helpText?: string | null;
  options?: readonly { id: string; label: string }[] | null;
  min?: number | null;
  max?: number | null;
  maxLength?: number | null;
  acceptedFileTypes?: readonly string[] | null;
}

/** One answer as the form holds it: text, `yes`/`no`, an option id, or option ids for multiple choice. */
export type AnswerValue = string | string[];

/** The question every form asks first unless the questionnaire has its own (the API's ApplicationForm). */
export const RESUME_QUESTION_ID = 'resume';

export const DEFAULT_RESUME_QUESTION: QuestionSpec = {
  id: RESUME_QUESTION_ID,
  type: 'File',
  label: 'Resume or CV',
  required: false,
  acceptedFileTypes: ['.pdf', '.docx'],
};

/** The largest file the careers form takes by default (the API's `ScreenHire:Apply:MaxUploadBytes`). */
export const DEFAULT_MAX_FILE_BYTES = 10 * 1024 * 1024;

/** A form's questions in the order the candidate meets them: the resume first, then the questionnaire. */
export function formQuestions(questions: readonly QuestionSpec[]): QuestionSpec[] {
  const resume = questions.find((question) => question.id === RESUME_QUESTION_ID) ?? DEFAULT_RESUME_QUESTION;
  return [resume, ...questions.filter((question) => question.id !== RESUME_QUESTION_ID)];
}

/** Form field names. Question fields are prefixed so they can never clash with the fixed fields. */
export const fields = {
  answer: (questionId: string) => `q:${questionId}`,
  file: (questionId: string) => `file:${questionId}`,
  upload: (questionId: string) => `upload:${questionId}`,
};

/** The element id for a field name, for labels and the error summary's links. */
export const fieldId = (name: string): string => `field-${name.replace(/[^a-zA-Z0-9_-]/g, '-')}`;

/** The line under a question: its help text, plus the range a number question accepts. */
export function hintFor(question: QuestionSpec): string | null {
  const range =
    question.type === 'Number' && (question.min != null || question.max != null)
      ? `A number${question.min != null ? ` from ${question.min}` : ''}${question.max != null ? ` up to ${question.max}` : ''}.`
      : null;
  return [question.helpText, range].filter(Boolean).join(' ') || null;
}

/** The line under a file question: its help text, then the types and size it takes. */
export const fileHintFor = (question: QuestionSpec, maxBytes: number): string =>
  [question.helpText, `${acceptedLabel(question.acceptedFileTypes ?? [])}, up to ${describeSize(maxBytes)}.`].filter(Boolean).join(' ');

/** The choices a yes/no or choice question offers, as radio or checkbox values. */
export const choicesFor = (question: QuestionSpec): { value: string; label: string }[] =>
  question.type === 'YesNo'
    ? [
        { value: 'yes', label: 'Yes' },
        { value: 'no', label: 'No' },
      ]
    : (question.options ?? []).map((option) => ({ value: option.id, label: option.label }));

export const isChoice = (question: QuestionSpec): boolean =>
  question.type === 'YesNo' || question.type === 'SingleChoice' || question.type === 'MultiChoice';

/** An answer as a list of selected values, for radios and checkboxes. */
export const selectedOf = (answer: AnswerValue | undefined): string[] =>
  answer === undefined ? [] : Array.isArray(answer) ? answer : [answer];

/** The `aria-describedby` for a field with an optional hint and error. */
export const describedBy = (id: string, hint: unknown, error: unknown): string | undefined =>
  [hint && `${id}-hint`, error && `${id}-error`].filter(Boolean).join(' ') || undefined;

/** A questionnaire's named page, as the hiring definition stores it. */
export interface SectionSpec {
  id: string;
  name: string;
  questionIds: readonly string[];
}

/** One page of a form: its heading (null for an automatic page) and the ids of its questions, in order. */
export interface QuestionPage {
  name: string | null;
  questionIds: string[];
}

/**
 * The pages a candidate meets (apply-flow.md §7): the questionnaire's named sections in order, or, without sections,
 * the questions in pages of `pageSize`. `skip` leaves out questions shown elsewhere (the resume, on the details step); a
 * page left empty is dropped. The careers pages and the dashboard's preview both page through this, so the preview is
 * the form.
 */
export function questionPages(
  questions: readonly QuestionSpec[],
  sections: readonly SectionSpec[] | null | undefined,
  pageSize: number,
  skip: readonly string[] = [],
): QuestionPage[] {
  const shown = questions.map((question) => question.id).filter((id) => !skip.includes(id));
  if (sections && sections.length > 0) {
    const known = new Set(shown);
    const pages = sections
      .map((section) => ({ name: section.name, questionIds: section.questionIds.filter((id) => known.has(id)) }))
      .filter((page) => page.questionIds.length > 0);
    // A question no section names (a definition saved before its sections) still gets asked, on a last page.
    const placed = new Set(pages.flatMap((page) => page.questionIds));
    const rest = shown.filter((id) => !placed.has(id));
    return rest.length > 0 ? [...pages, { name: null, questionIds: rest }] : pages;
  }

  const size = Math.max(1, Math.floor(pageSize) || 1);
  const pages: QuestionPage[] = [];
  for (let i = 0; i < shown.length; i += size) pages.push({ name: null, questionIds: shown.slice(i, i + size) });
  return pages;
}

/** How many of `questions` have an answer: text, a choice, or a file. */
export function answeredCount(
  questions: readonly QuestionSpec[],
  answers: Readonly<Record<string, AnswerValue | undefined>>,
  files: Readonly<Record<string, unknown>> = {},
): number {
  return questions.filter((question) => {
    if (question.type === 'File') return files[question.id] != null;
    const answer = answers[question.id];
    return Array.isArray(answer) ? answer.length > 0 : answer !== undefined && answer.trim() !== '';
  }).length;
}
