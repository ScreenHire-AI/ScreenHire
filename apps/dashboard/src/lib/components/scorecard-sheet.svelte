<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { Recommendation, WorkflowStage } from '@screenhire/api-client';
  import { ApiError, createSubmitScorecard } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';
  import { type Rating, ratingLabels, ratings, recommendationLabels, recommendations } from '#lib/workflow.js';

  // A reviewer fills a stage's scorecard in a sheet, so the candidate stays behind it (design system section 9). Each
  // criterion is a group of labelled radios on the fixed scale; a criterion left alone is not rated. The overall
  // recommendation is required: it is what ReviewerDecision automations test. Nothing here shows or takes integrity
  // output (CLAUDE.md non-negotiable 2), and nothing totals the ratings.
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

  /** As the API allows it (ApplicationScorecard.MaxNoteLength). */
  const maxNoteLength = 2000;
  const uid = $props.id();
  const submit = createSubmitScorecard();
  let stageId = $state('');
  let given = $state<Record<string, Rating | undefined>>({});
  let recommendation = $state<Recommendation | ''>('');
  let note = $state('');
  let tried = $state(false);
  let failure = $state('');

  const stage = $derived(stages.find((candidate) => candidate.id === stageId) ?? stages[0]);
  const criteria = $derived(stage?.scorecard?.criteria ?? []);
  const missingRecommendation = $derived(tried && !recommendation);

  // Starts fresh each time it opens, on the first stage offered (the current one when it is offered).
  $effect(() => {
    if (!open) return;
    untrack(() => {
      stageId = stages[0]?.id ?? '';
      given = {};
      recommendation = '';
      note = '';
      tried = false;
      failure = '';
    });
  });

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    tried = true;
    failure = '';
    if (!stage || !recommendation) return;
    try {
      await submit.mutateAsync({
        applicationId,
        data: {
          stageId: stage.id,
          ratings: criteria.flatMap((criterion) => (given[criterion.id] ? [{ criterionId: criterion.id, rating: given[criterion.id]! }] : [])),
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
      <Sheet.Description>Rate what the job needs. Your team sees it with your name.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="@container flex flex-col gap-5 px-4 pb-4" {onsubmit}>
      {#if stages.length > 1}
        <Field.Field>
          <Field.Label for={`${uid}-stage`}>Stage</Field.Label>
          <select id={`${uid}-stage`} class={selectClass} bind:value={() => stage?.id ?? '', (id) => ((stageId = id), (given = {}))}>
            {#each stages as option (option.id)}<option value={option.id}>{option.name}</option>{/each}
          </select>
        </Field.Field>
      {:else if stage}
        <p class="text-body">Stage: <span class="font-semibold">{stage.name}</span></p>
      {/if}

      {#each criteria as criterion (criterion.id)}
        <fieldset class="flex flex-col gap-2" aria-describedby={criterion.guidance ? `${uid}-${criterion.id}-guidance` : undefined}>
          <legend class="text-body font-semibold">{criterion.label}</legend>
          {#if criterion.guidance}<p id={`${uid}-${criterion.id}-guidance`} class="-mt-1 text-muted-foreground">{criterion.guidance}</p>{/if}
          <div class="grid gap-1.5 @sm:grid-cols-2">
            {#each ratings as rating (rating)}
              <label
                class="flex min-h-9 items-center gap-2 rounded-lg border border-input px-3 text-body has-checked:border-primary has-checked:bg-accent has-checked:text-accent-foreground pointer-coarse:min-h-11"
              >
                <input type="radio" class="size-4 flex-none accent-primary" name={`${uid}-${criterion.id}`} value={rating} bind:group={given[criterion.id]} />
                {ratingLabels[rating]}
              </label>
            {/each}
          </div>
        </fieldset>
      {/each}

      <fieldset class="flex flex-col gap-2" aria-describedby={missingRecommendation ? `${uid}-recommendation-error` : undefined}>
        <legend class="text-body font-semibold">Recommendation <span class="text-caption font-normal text-muted-foreground">Required</span></legend>
        <div class="grid gap-1.5 @sm:grid-cols-2">
          {#each recommendations as value (value)}
            <label
              class="flex min-h-9 items-center gap-2 rounded-lg border border-input px-3 text-body has-checked:border-primary has-checked:bg-accent has-checked:text-accent-foreground pointer-coarse:min-h-11"
            >
              <input type="radio" class="size-4 flex-none accent-primary" name={`${uid}-recommendation`} {value} bind:group={recommendation} />
              {recommendationLabels[value]}
            </label>
          {/each}
        </div>
        {#if missingRecommendation}<Field.Error id={`${uid}-recommendation-error`} errors={[{ message: 'Choose a recommendation.' }]} />{/if}
      </fieldset>

      <Field.Field>
        <Field.Label for={`${uid}-note`}>Note</Field.Label>
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
