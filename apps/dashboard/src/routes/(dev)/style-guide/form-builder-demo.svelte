<script lang="ts" module>
  import StarIcon from '@lucide/svelte/icons/star';
  import { generalKinds, type AnswerKindGroup, type AnswerOption } from '#lib/form-builder.js';

  type ScorecardKind = (typeof generalKinds)[number]['value'] | 'Rating5' | 'Rating10' | 'RatingYesNo';

  /** A scorecard's menu: the General kinds and the Rating group, each rating with a star (section 11, Answer menu). */
  export const scorecardKinds: readonly AnswerKindGroup<ScorecardKind>[] = [
    { label: 'General', kinds: generalKinds },
    {
      label: 'Rating',
      kinds: [
        { value: 'Rating5', label: '1 to 5', icon: StarIcon },
        { value: 'Rating10', label: '1 to 10', icon: StarIcon },
        { value: 'RatingYesNo', label: 'Yes or no', icon: StarIcon },
      ],
    },
  ];
</script>

<script lang="ts">
  import FormBuilder from '#lib/components/form-builder.svelte';
  import { duplicateId } from '#lib/form-builder.js';
  import { idFrom } from '#lib/workflow.js';

  // The form builder on a scorecard-shaped question, not the workflow's: its own kinds, a Rating group and "What to
  // look for" in place of help text, through the builder's callbacks alone. Nothing is saved.
  interface Criterion {
    id: string;
    kind: ScorecardKind;
    label: string;
    required: boolean;
    guidance: string | null;
    options: AnswerOption[];
  }

  let questions = $state<Criterion[]>([
    { id: 'problem_solving', kind: 'Rating5', label: 'Problem solving', required: true, guidance: 'Breaks the exercise into steps and explains the trade-offs.', options: [] },
    { id: 'dotnet', kind: 'Rating10', label: '.NET and C#', required: true, guidance: 'Uses the language and the framework well.', options: [] },
    {
      id: 'next_interview',
      kind: 'MultiChoice',
      label: 'What should the next interview cover?',
      required: false,
      guidance: null,
      options: [
        { id: 'system_design', label: 'System design' },
        { id: 'testing', label: 'Testing' },
        { id: 'databases', label: 'Databases' },
      ],
    },
  ]);

  const withOptions = (kind: ScorecardKind, options: AnswerOption[]) =>
    kind === 'SingleChoice' || kind === 'MultiChoice' ? (options.length ? options : [{ id: 'option_1', label: 'Option 1' }]) : [];
</script>

<FormBuilder
  bind:items={questions}
  groups={scorecardKinds}
  title="Questions"
  empty="No questions yet."
  key={(question) => question.id}
  kindOf={(question) => question.kind}
  withKind={(question, kind) => ({ ...question, kind, options: withOptions(kind, question.options) })}
  labelOf={(question) => question.label}
  setLabel={(question, value) => (question.label = value)}
  requiredOf={(question) => question.required}
  setRequired={(question, value) => (question.required = value)}
  help={{ label: 'What to look for', get: (question) => question.guidance, set: (question, value) => (question.guidance = value) }}
  optionsOf={(question) => question.options}
  setOptions={(question, options) => (question.options = options)}
  newItem={(kind) => ({
    id: idFrom('question', questions.map((question) => question.id), 'question'),
    kind,
    label: 'New question',
    required: false,
    guidance: null,
    options: withOptions(kind, []),
  })}
  duplicate={(question) => ({ ...$state.snapshot(question), id: duplicateId(question.id, questions.map((other) => other.id)) })}
>
  {#snippet note()}
    <p class="text-muted-foreground">Ask about the work and the skills the job needs, never personality, emotion or honesty.</p>
  {/snippet}
</FormBuilder>
