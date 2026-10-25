<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { Recommendation, ScorecardAnswerInput, ScorecardQuestion, WorkflowStage } from '@screenhire/api-client';
  import { ApiError, createSubmitScorecard } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import RatingAnswer from '#lib/components/rating-answer.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { describeError } from '#lib/query.js';
  import { scaleOf } from '#lib/scorecards.js';
  import { cn } from '#lib/utils.js';
  import { recommendationLabels, recommendations } from '#lib/workflow.js';

  // A reviewer fills a stage's scorecard in a sheet, so the candidate stays behind it (design system section 9). Each
  // question answers in its own kind: ratings and yes-or-no on one row of joined buttons (Rating answer), choices as
  // radios or checkboxes, text in a box. A question left alone is unanswered, which a required one may not be. The
  // overall recommendation is required: it is what ReviewerDecision automations test. Nothing here shows or takes
  // integrity output (CLAUDE.md non-negotiable 2); the rating answers make the candidate's rating on the server.
  let {
    open = $bindable(false),
    applicationId,
    stages,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** The stages this reviewer can score: they have a scorecard, the application reached them, and the reviewer has not scored them yet. */
    stages: readonly WorkflowStage[];
    /** After the scorecard was saved: a sentence for the page's status line. */
    ondone: (message: string) => void | Promise<void>;
  } = $props();

  /** As the API allows them (ApplicationScorecard.MaxNoteLength, ScorecardAnswers). */
  const maxNoteLength = 2000;
  const maxShortText = 500;
  const maxLongText = 10_000;
  const uid = $props.id();
  const submit = createSubmitScorecard();
  let stageId = $state('');
  // One value per question: a number or "yes"/"no" as text, an option id, or the text; picks for a multi-choice.
  let values = $state<Record<string, string>>({});
  let picks = $state<Record<string, string[]>>({});
  let recommendation = $state<Recommendation | ''>('');
  let note = $state('');
  let tried = $state(false);
  let failure = $state('');

  const stage = $derived(stages.find((candidate) => candidate.id === stageId) ?? stages[0]);
  const questions = $derived(stage?.scorecard?.questions ?? []);
  const missingRecommendation = $derived(tried && !recommendation);
  const unanswered = $derived(new Set(questions.filter((question) => question.required && !answered(question)).map((question) => question.id)));

  // Starts fresh each time it opens, on the first stage offered (the current one when it is offered).
  $effect(() => {
    if (!open) return;
    untrack(() => {
      stageId = stages[0]?.id ?? '';
      clear();
      recommendation = '';
      note = '';
      tried = false;
      failure = '';
    });
  });

  function clear() {
    values = {};
    picks = {};
  }

  function answered(question: ScorecardQuestion): boolean {
    return question.type === 'MultiChoice' ? (picks[question.id]?.length ?? 0) > 0 : (values[question.id] ?? '').trim() !== '';
  }

  function answer(question: ScorecardQuestion): ScorecardAnswerInput | null {
    if (!answered(question)) return null;
    if (question.type === 'MultiChoice') return { questionId: question.id, values: picks[question.id] };
    const value = values[question.id].trim();
    return scaleOf(question.type).length > 0 ? { questionId: question.id, rating: Number(value) } : { questionId: question.id, value };
  }

  const fieldId = (question: ScorecardQuestion) => `${uid}-${question.id}`;

  // A multi-choice answer keeps the options in the question's order.
  function pick(question: ScorecardQuestion, optionId: string, checked: boolean) {
    const next = new Set(picks[question.id] ?? []);
    if (checked) next.add(optionId);
    else next.delete(optionId);
    picks[question.id] = (question.options ?? []).map((option) => option.id).filter((id) => next.has(id));
  }

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    tried = true;
    failure = '';
    if (!stage || !recommendation || unanswered.size > 0) return;
    try {
      await submit.mutateAsync({
        applicationId,
        data: {
          stageId: stage.id,
          answers: questions.flatMap((question) => answer(question) ?? []),
          recommendation,
          note: note.trim() || null,
        },
      });
      open = false;
      await ondone(`Scorecard for ${stage.name} saved.`);
    } catch (error) {
      failure =
        error instanceof ApiError && error.status === 409
          ? 'You already submitted a scorecard for this stage.'
          : describeError(error, "We couldn't save the scorecard just now. Try again in a minute.");
    }
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

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Fill scorecard</Sheet.Title>
      <Sheet.Description>Your team sees it with your name, and it is final once submitted.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="@container flex flex-col gap-5 px-4 pb-4" {onsubmit}>
      {#if stages.length > 1}
        <Field.Field>
          <Field.Label for={`${uid}-stage`}>Stage</Field.Label>
          <select id={`${uid}-stage`} class={selectClass} bind:value={() => stage?.id ?? '', (id) => ((stageId = id), clear())}>
            {#each stages as option (option.id)}<option value={option.id}>{option.name}</option>{/each}
          </select>
        </Field.Field>
      {:else if stage}
        <p class="text-body">Stage: <span class="font-semibold">{stage.name}</span></p>
      {/if}

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
        <span id={`${uid}-recommendation`} class="text-body font-semibold">Overall recommendation <span class="sr-only">(required)</span></span>
        <ToggleGroup.Root
          type="single"
          variant="outline"
          size="lg"
          spacing={0}
          aria-labelledby={`${uid}-recommendation`}
          aria-describedby={missingRecommendation ? `${uid}-recommendation-error` : undefined}
          class="max-w-full flex-wrap"
          bind:value={() => recommendation, (next) => (recommendation = (next as Recommendation) ?? '')}
        >
          {#each recommendations as value (value)}
            <ToggleGroup.Item {value} class="text-foreground">{recommendationLabels[value]}</ToggleGroup.Item>
          {/each}
        </ToggleGroup.Root>
        {#if missingRecommendation}<Field.Error id={`${uid}-recommendation-error`} errors={[{ message: 'Choose a recommendation.' }]} />{/if}
      </div>

      <Field.Field>
        <Field.Label for={`${uid}-note`}>Note for the team</Field.Label>
        <Textarea id={`${uid}-note`} rows={4} maxlength={maxNoteLength} bind:value={note} />
      </Field.Field>

      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={submit.isPending || !stage}><ClipboardCheckIcon aria-hidden="true" />Submit scorecard</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
