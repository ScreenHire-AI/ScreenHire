<script lang="ts">
  import type { Recommendation, ScorecardQuestion } from '@screenhire/api-client';
  import RatingAnswer from '#lib/components/rating-answer.svelte';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { recommendationLabels, recommendations } from '#lib/workflow.js';

  // What a reviewer fills in for a scorecard (design system section 11, Scorecard and Rating answer): each question in
  // its own kind (ratings and yes-or-no on one row of joined buttons, choices as radios or checkboxes, text in a box),
  // then the overall recommendation and a note for the team. `ScorecardSheet` submits it; the scorecard editor's preview
  // shows it as a reviewer sees it. Nothing here shows or takes integrity output (CLAUDE.md non-negotiable 2).
  let {
    questions,
    values = $bindable({}),
    picks = $bindable({}),
    recommendation = $bindable(''),
    note = $bindable(''),
    tried = false,
    unanswered = new Set(),
    idPrefix,
  }: {
    questions: readonly ScorecardQuestion[];
    /** One value per question: a number or "yes"/"no" as text, an option id, or the text. */
    values?: Record<string, string>;
    /** A multi-choice question's picked option ids. */
    picks?: Record<string, string[]>;
    recommendation?: Recommendation | '';
    note?: string;
    /** A submit was tried, so missing answers say so. */
    tried?: boolean;
    /** The required questions still unanswered. */
    unanswered?: ReadonlySet<string>;
    /** Makes the field ids unique on the page. */
    idPrefix: string;
  } = $props();

  /** As the API allows them (ApplicationScorecard.MaxNoteLength, ScorecardAnswers). */
  const maxNoteLength = 2000;
  const maxShortText = 500;
  const maxLongText = 10_000;
  const missingRecommendation = $derived(tried && !recommendation);
  const fieldId = (question: ScorecardQuestion) => `${idPrefix}-${question.id}`;

  // A multi-choice answer keeps the options in the question's order.
  function pick(question: ScorecardQuestion, optionId: string, checked: boolean) {
    const next = new Set(picks[question.id] ?? []);
    if (checked) next.add(optionId);
    else next.delete(optionId);
    picks[question.id] = (question.options ?? []).map((option) => option.id).filter((id) => next.has(id));
  }
</script>

{#snippet label(question: ScorecardQuestion)}
  <span id={`${fieldId(question)}-label`} class="text-body font-semibold"
    >{question.label}{#if question.required}<span class="text-muted-foreground" aria-hidden="true"> *</span><span class="sr-only"> (required)</span
      >{/if}</span
  >
{/snippet}

{#snippet guidance(question: ScorecardQuestion)}
  {#if question.guidance}<p id={`${fieldId(question)}-guidance`} class="-mt-1 text-muted-foreground">{question.guidance}</p>{/if}
  {#if tried && unanswered.has(question.id)}<Field.Error id={`${fieldId(question)}-error`} errors={[{ message: 'Answer this question.' }]} />{/if}
{/snippet}

{#each questions as question (question.id)}
  {@const described =
    [question.guidance ? `${fieldId(question)}-guidance` : '', tried && unanswered.has(question.id) ? `${fieldId(question)}-error` : '']
      .filter(Boolean)
      .join(' ') || undefined}
  {#if question.type === 'ShortText' || question.type === 'LongText'}
    <div class="flex flex-col gap-2">
      <label for={fieldId(question)}>{@render label(question)}</label>
      {@render guidance(question)}
      {#if question.type === 'ShortText'}
        <Input id={fieldId(question)} maxlength={maxShortText} aria-describedby={described} bind:value={values[question.id]} />
      {:else}
        <Textarea id={fieldId(question)} rows={4} maxlength={maxLongText} aria-describedby={described} bind:value={values[question.id]} />
      {/if}
    </div>
  {:else if question.type === 'SingleChoice' || question.type === 'MultiChoice'}
    <fieldset class="flex flex-col gap-2" aria-describedby={described}>
      <legend>{@render label(question)}</legend>
      {@render guidance(question)}
      <div class="grid gap-1.5 @sm:grid-cols-2">
        {#each question.options ?? [] as option (option.id)}
          <label
            class="flex min-h-9 items-center gap-2 rounded-lg border border-input px-3 text-body has-checked:border-primary has-checked:bg-accent has-checked:text-accent-foreground pointer-coarse:min-h-11"
          >
            {#if question.type === 'SingleChoice'}
              <input type="radio" class="size-4 flex-none accent-primary" name={fieldId(question)} value={option.id} bind:group={values[question.id]} />
            {:else}
              <input
                type="checkbox"
                class="size-4 flex-none accent-primary"
                value={option.id}
                checked={picks[question.id]?.includes(option.id) ?? false}
                onchange={(event) => pick(question, option.id, event.currentTarget.checked)}
              />
            {/if}
            {option.label}
          </label>
        {/each}
      </div>
    </fieldset>
  {:else}
    <div class="flex flex-col gap-2">
      {@render label(question)}
      {@render guidance(question)}
      <RatingAnswer
        type={question.type}
        labelledby={`${fieldId(question)}-label`}
        describedby={described}
        invalid={tried && unanswered.has(question.id)}
        bind:value={() => values[question.id] ?? '', (next) => (values[question.id] = next ?? '')}
      />
    </div>
  {/if}
{/each}

<div class="flex flex-col gap-2">
  <span id={`${idPrefix}-recommendation`} class="text-body font-semibold">Overall recommendation <span class="sr-only">(required)</span></span>
  <ToggleGroup.Root
    type="single"
    variant="outline"
    size="lg"
    spacing={0}
    aria-labelledby={`${idPrefix}-recommendation`}
    aria-describedby={missingRecommendation ? `${idPrefix}-recommendation-error` : undefined}
    class="max-w-full flex-wrap"
    bind:value={() => recommendation, (next) => (recommendation = (next as Recommendation) ?? '')}
  >
    {#each recommendations as value (value)}
      <ToggleGroup.Item {value} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground"
        >{recommendationLabels[value]}</ToggleGroup.Item
      >
    {/each}
  </ToggleGroup.Root>
  {#if missingRecommendation}<Field.Error id={`${idPrefix}-recommendation-error`} errors={[{ message: 'Choose a recommendation.' }]} />{/if}
</div>

<Field.Field>
  <Field.Label for={`${idPrefix}-note`}>Note for the team</Field.Label>
  <Textarea id={`${idPrefix}-note`} rows={4} maxlength={maxNoteLength} bind:value={note} />
</Field.Field>
