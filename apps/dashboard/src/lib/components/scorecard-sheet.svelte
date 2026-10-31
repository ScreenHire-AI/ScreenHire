<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { Recommendation, ScorecardAnswerInput, ScorecardQuestion } from '@screenhire/api-client';
  import { ApiError, createGetScorecardTemplate, createSubmitScorecard } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ScorecardFields from '#lib/components/scorecard-fields.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import { scorecardWords, type ScorecardChoice } from '#lib/scorecard-choices.js';
  import { scaleOf } from '#lib/scorecards.js';
  import { cn } from '#lib/utils.js';

  // A reviewer fills a scorecard in a sheet, so the candidate stays behind it (design system section 9): a stage's own
  // (the job's copy, for a stage the candidate reached) or any library scorecard, given at the stage the candidate is in
  // now. Each question answers in its own kind: ratings and yes-or-no on one row of joined buttons (Rating answer),
  // choices as radios or checkboxes, text in a box. A question left alone is unanswered, which a required one may not be.
  // The overall recommendation is required: it is what ReviewerDecision automations test. Nothing here shows or takes
  // integrity output (CLAUDE.md non-negotiable 2); the rating answers make the candidate's rating on the server.
  let {
    open = $bindable(false),
    applicationId,
    choices,
    initial,
    ondone,
  }: {
    open?: boolean;
    applicationId: string;
    /** The scorecards this reviewer can still give (`scorecardChoices` in #lib/scorecard-choices.ts). */
    choices: readonly ScorecardChoice[];
    /** The choice it opens on, by value; none chosen when it is not one of `choices`. */
    initial?: string;
    /** After the scorecard was saved: a sentence for the page's status line. */
    ondone: (message: string) => void | Promise<void>;
  } = $props();

  const uid = $props.id();
  const submit = createSubmitScorecard();
  let chosenValue = $state('');
  // One value per question: a number or "yes"/"no" as text, an option id, or the text; picks for a multi-choice.
  let values = $state<Record<string, string>>({});
  let picks = $state<Record<string, string[]>>({});
  let recommendation = $state<Recommendation | ''>('');
  let note = $state('');
  let tried = $state(false);
  let failure = $state('');

  const choice = $derived(choices.find((candidate) => candidate.value === chosenValue));
  // A library scorecard's questions are read when it is chosen; a stage's come with the job's workflow.
  const library = createGetScorecardTemplate(
    () => (choice && !choice.questions ? (choice.key ?? '') : ''),
    () => ({ query: { enabled: Boolean(choice && !choice.questions && choice.key) } }),
  );
  const questions = $derived<readonly ScorecardQuestion[]>(choice?.questions ?? (choice ? (library.data?.data.questions ?? []) : []));
  const loading = $derived(Boolean(choice && !choice.questions && library.isPending));
  const unanswered = $derived(new Set(questions.filter((question) => question.required && !answered(question)).map((question) => question.id)));
  const stageChoices = $derived(choices.filter((option) => option.group === 'stages'));
  const builtIns = $derived(choices.filter((option) => option.group === 'builtIn'));
  const own = $derived(choices.filter((option) => option.group === 'own'));

  // Starts fresh each time it opens, on the scorecard it was opened for.
  $effect(() => {
    if (!open) return;
    untrack(() => {
      chosenValue = choices.some((option) => option.value === initial) ? initial! : '';
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

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    tried = true;
    failure = '';
    if (!choice) {
      failure = 'Choose a scorecard.';
      return;
    }
    if (loading || !recommendation || unanswered.size > 0) return;
    try {
      await submit.mutateAsync({
        applicationId,
        data: {
          stageId: choice.stageId ?? null,
          scorecardTemplateId: choice.stageId ? null : choice.key,
          answers: questions.flatMap((question) => answer(question) ?? []),
          recommendation,
          note: note.trim() || null,
        },
      });
      open = false;
      await ondone(`${scorecardWords(choice.name)} saved.`.replace(/^./, (first) => first.toUpperCase()));
    } catch (error) {
      failure =
        error instanceof ApiError && error.status === 409
          ? 'You already submitted this scorecard for this stage.'
          : describeError(error, "We couldn't save the scorecard just now. Try again in a minute.");
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    <Sheet.Header>
      <Sheet.Title>Fill in a scorecard</Sheet.Title>
      <Sheet.Description>Your team sees it with your name, and it is final once submitted.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="@container flex flex-col gap-5 px-4 pb-4" {onsubmit}>
      <Field.Field>
        <Field.Label for={`${uid}-scorecard`}>Scorecard</Field.Label>
        <select
          id={`${uid}-scorecard`}
          class={selectClass}
          bind:value={() => chosenValue, (value) => ((chosenValue = value), clear())}
          aria-invalid={(tried && !choice) || undefined}
        >
          <option value="" disabled>Choose a scorecard</option>
          {#if stageChoices.length > 0}
            <optgroup label="This job's stages">
              {#each stageChoices as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </optgroup>
          {/if}
          {#if builtIns.length > 0}
            <optgroup label="Built in">
              {#each builtIns as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </optgroup>
          {/if}
          {#if own.length > 0}
            <optgroup label="Your scorecards">
              {#each own as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
            </optgroup>
          {/if}
        </select>
      </Field.Field>

      {#if library.isError && choice && !choice.questions}
        <QueryError message={describeError(library.error, 'Could not load this scorecard')} onretry={() => library.refetch()} inset />
      {:else if loading}
        <div aria-hidden="true" class="flex flex-col gap-2">
          {#each [0, 1, 2] as row (row)}<Skeleton class="h-10 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the scorecard…</p>
      {:else if choice}
        <ScorecardFields {questions} bind:values bind:picks bind:recommendation bind:note {tried} {unanswered} idPrefix={uid} />
      {/if}

      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={submit.isPending}><ClipboardCheckIcon aria-hidden="true" />Submit scorecard</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
