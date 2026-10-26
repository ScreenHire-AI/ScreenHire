<script lang="ts">
  import type { DefinitionError, QuestionOption, ScorecardQuestion, ScorecardQuestionType } from '@screenhire/api-client';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import { duplicateId, scorecardKinds } from '#lib/form-builder.js';
  import { errorsAt, errorsUnder, idFrom } from '#lib/workflow.js';

  // A library scorecard's questions in the form builder (design system section 11, Scorecard and Form builder): the
  // General kinds and the Rating group, whose questions make the candidate's rating, and "What to look for" in place of
  // help text. Every scorecard then asks for an overall recommendation and a note, which are not questions here.
  // `errors` are the API's, with paths such as `questions[0].label`.
  let {
    questions = $bindable(),
    errors = [],
  }: {
    questions: ScorecardQuestion[];
    errors?: readonly DefinitionError[];
  } = $props();

  const withOptions = (type: ScorecardQuestionType, options: readonly QuestionOption[] | null | undefined): QuestionOption[] | null =>
    type === 'SingleChoice' || type === 'MultiChoice' ? (options?.length ? [...options] : [{ id: 'option_1', label: 'Option 1' }]) : null;
</script>

<FormBuilder
  bind:items={questions}
  groups={scorecardKinds}
  title="Questions"
  empty="No questions yet."
  key={(question) => question.id}
  kindOf={(question) => question.type}
  withKind={(question, type) => ({ ...question, type, options: withOptions(type, question.options) })}
  labelOf={(question) => question.label}
  setLabel={(question, value) => (question.label = value)}
  requiredOf={(question) => question.required ?? false}
  setRequired={(question, value) => (question.required = value)}
  help={{ label: 'What to look for', get: (question) => question.guidance ?? null, set: (question, value) => (question.guidance = value) }}
  optionsOf={(question) => question.options ?? []}
  setOptions={(question, options) => (question.options = options)}
  newItem={(type) => ({
    id: idFrom('question', questions.map((question) => question.id), 'question'),
    type,
    label: 'New question',
    required: false,
    guidance: null,
    options: withOptions(type, []),
  })}
  duplicate={(question) => ({ ...$state.snapshot(question), id: duplicateId(question.id, questions.map((other) => other.id)) })}
  errorsFor={(_, index) => errorsUnder(errors, `questions[${index}]`).map((error) => error.message)}
  problems={errorsAt(errors, 'questions')}
>
  {#snippet note()}
    <p class="text-muted-foreground">Ask about the work and the skills the job needs, never personality, emotion or honesty.</p>
  {/snippet}
</FormBuilder>
