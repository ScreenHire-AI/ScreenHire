<script lang="ts">
  import type { Question } from '@screenhire/api-client';
  import FormBuilder from '#lib/components/form-builder.svelte';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { applyFormKinds, duplicateId } from '#lib/form-builder.js';
  import type { SectionDraft } from '#lib/form-sections.js';
  import { idFrom, withQuestionType } from '#lib/workflow.js';

  // The form builder over screening questions, as the questionnaire builder mounts it. `onchange` sees every new list,
  // and `onsections` the sections when `withSections` offers them.
  let {
    initial,
    errors = {},
    onchange,
    withSections = false,
    initialSections = null,
    onsections,
  }: {
    initial: Question[];
    errors?: Record<string, string[]>;
    onchange?: (questions: Question[]) => void;
    withSections?: boolean;
    initialSections?: SectionDraft[] | null;
    onsections?: (sections: SectionDraft[] | null) => void;
  } = $props();
  // svelte-ignore state_referenced_locally: the harness starts from its props and then owns the list.
  let questions = $state<Question[]>(structuredClone(initial));
  // svelte-ignore state_referenced_locally: the same, for the sections.
  let sections = $state<SectionDraft[] | null>(structuredClone(initialSections));
  $effect(() => onchange?.($state.snapshot(questions) as Question[]));
  $effect(() => onsections?.($state.snapshot(sections) as SectionDraft[] | null));
</script>

<Tooltip.Provider>
  <FormBuilder
    bind:items={questions}
    {withSections}
    bind:sections
    groups={applyFormKinds}
    title="Screening questions"
    empty="No questions yet."
    key={(question) => question.id}
    kindOf={(question) => question.type}
    withKind={withQuestionType}
    labelOf={(question) => question.label}
    setLabel={(question, value) => (question.label = value)}
    requiredOf={(question) => question.required ?? false}
    setRequired={(question, value) => (question.required = value)}
    help={{ label: 'Help text', get: (question) => question.helpText, set: (question, value) => (question.helpText = value) }}
    optionsOf={(question) => question.options ?? []}
    setOptions={(question, options) => (question.options = options)}
    newItem={(type) => withQuestionType({ id: idFrom('question', questions.map((q) => q.id), 'question'), type: 'ShortText', label: 'New question' }, type)}
    duplicate={(question) => ({ ...($state.snapshot(question) as Question), id: duplicateId(question.id, questions.map((q) => q.id)) })}
    errorsFor={(question) => errors[question.id] ?? []}
  />
</Tooltip.Provider>
