<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { Recommendation, ScorecardAnswerInput, ScorecardQuestion, WorkflowStage } from '@screenhire/api-client';
  import { ApiError, createSubmitScorecard } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import ScorecardFields from '#lib/components/scorecard-fields.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { describeError } from '#lib/query.js';
  import { scaleOf } from '#lib/scorecards.js';
  import { cn } from '#lib/utils.js';

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

      <ScorecardFields {questions} bind:values bind:picks bind:recommendation bind:note {tried} {unanswered} idPrefix={uid} />

      <FailureText message={failure} />
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={submit.isPending || !stage}><ClipboardCheckIcon aria-hidden="true" />Submit scorecard</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
