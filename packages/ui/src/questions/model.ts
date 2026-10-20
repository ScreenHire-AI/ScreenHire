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
