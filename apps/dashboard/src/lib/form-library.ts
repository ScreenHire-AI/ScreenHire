// The form library (workflow-engine.md section 7, Forms): library forms' questions, which hiring templates copy. Pure
// functions, so their rules are tested without a page.
import type { FormKind, FormTemplateUpdate, Question, QuestionType } from '@screenhire/api-client';
import { generalKinds, type AnswerKindGroup } from '#lib/form-builder.js';
import { withQuestionType } from '#lib/workflow.js';

/**
 * An equality monitoring form's menu: only choices, because its answers are only ever counted (the API refuses any other
 * kind, any required question and any knockout).
 */
export const equalityKinds: readonly AnswerKindGroup<QuestionType>[] = [
  { label: 'Choices', kinds: generalKinds.filter((kind) => kind.value === 'YesNo' || kind.value === 'SingleChoice' || kind.value === 'MultiChoice') },
];

/**
 * A new question id for a library form: a stem from the label and a short random tail. A hiring template holds several
 * library forms, and a question id must be unique across all of them, so ids from different forms should never meet.
 */
export function libraryQuestionId(taken: Iterable<string>, random: () => number = Math.random): string {
  const used = new Set(taken);
  for (;;) {
    const id = `q_${Math.floor(random() * 36 ** 6).toString(36).padStart(6, '0')}`;
    if (!used.has(id)) return id;
  }
}

/** A new question for a library form of `kind`: an equality monitoring form's is an optional choice with "Prefer not to say". */
export function newFormQuestion(kind: FormKind | undefined, taken: Iterable<string>, type?: QuestionType): Question {
  const id = libraryQuestionId(taken);
  if (kind === 'EqualityMonitoring') {
    if (type === 'YesNo') return { id, type: 'YesNo', label: 'New question', required: false, helpText: null };
    return {
      id,
      type: type === 'MultiChoice' ? 'MultiChoice' : 'SingleChoice',
      label: 'New question',
      required: false,
      helpText: null,
      options: [
        { id: 'option_1', label: 'Option 1' },
        { id: 'prefer_not_to_say', label: 'Prefer not to say' },
      ],
    };
  }
  return withQuestionType({ id, type: 'ShortText', label: 'New question', required: false }, type ?? 'ShortText');
}

/** The question a new library form starts with, so the API's "at least one question" holds. */
export const firstFormQuestion = (kind: FormKind | undefined): Question => newFormQuestion(kind, []);

/** What a form's kind is called where a person reads it. */
export const formKindLabels: Record<NonNullable<FormKind>, string> = {
  Standard: 'Form',
  EqualityMonitoring: 'Equality monitoring',
};

/** What updating the templates did, in one notice: "Office role is published as version 4. Bar job has it in its draft." */
export function updateResultWords(results: readonly FormTemplateUpdate[]): string {
  const sentences = results.map((result) => {
    const name = result.templateName || 'A template';
    switch (result.outcome) {
      case 'Published':
        return `${name} is published as version ${result.version}.`;
      case 'UpdatedDraft':
        return `${name} has it in its draft, version ${result.version}, to publish when you are ready.`;
      default:
        return `${name} was not updated: ${result.problem ?? 'nothing to change.'}`;
    }
  });
  return sentences.join(' ');
}
