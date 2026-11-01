<script lang="ts">
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import type { WorkflowStage } from '@screenhire/api-client';
  import { untrack } from 'svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { cn } from '#lib/utils.js';

  // Move to stage with a note, in a sheet (design system section 9), from Move to stage's "With a note" item. The note
  // stays on the move's entry in Activity. A plain move from the menu stays one click.
  let {
    open = $bindable(false),
    candidateName,
    currentStageId,
    stages,
    failure,
    onmove,
  }: {
    open?: boolean;
    candidateName: string;
    currentStageId: string;
    stages: readonly WorkflowStage[];
    /** Why the last move failed, from the page's moves. */
    failure: string;
    /** Moves the candidate; true when it worked. */
    onmove: (stage: WorkflowStage, note: string) => Promise<boolean>;
  } = $props();

  /** As the API allows it (MoveApplicationRequestValidator). */
  const maxNoteLength = 1000;
  const targets = $derived(stages.filter((stage) => stage.id !== currentStageId));
  let stageId = $state('');
  let note = $state('');
  let pending = $state(false);
  let tried = $state(false);

  // Starts fresh each time it opens; a live refetch of the stages while it is open keeps what the person chose.
  $effect(() => {
    if (!open) return;
    untrack(() => {
      stageId = targets[0]?.id ?? '';
      note = '';
      tried = false;
    });
  });

  async function onsubmit(event: SubmitEvent) {
    event.preventDefault();
    const stage = targets.find((candidate) => candidate.id === stageId);
    if (!stage) return;
    pending = true;
    tried = true;
    const moved = await onmove(stage, note);
    pending = false;
    if (moved) open = false;
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto">
    <Sheet.Header>
      <Sheet.Title>Move {candidateName}</Sheet.Title>
      <Sheet.Description>The note stays with the move in Activity.</Sheet.Description>
    </Sheet.Header>
    <form novalidate class="flex flex-col gap-4 px-4 pb-4" {onsubmit}>
      <Field.Field>
        <Field.Label for="move-stage">Stage</Field.Label>
        <select id="move-stage" class={selectClass} bind:value={stageId}>
          {#each targets as stage (stage.id)}<option value={stage.id}>{stage.name}</option>{/each}
        </select>
      </Field.Field>
      <Field.Field>
        <Field.Label for="move-note">Note</Field.Label>
        <Textarea id="move-note" rows={4} maxlength={maxNoteLength} bind:value={note} />
      </Field.Field>
      {#if tried}<FailureText message={failure} />{/if}
      <div class="flex justify-end gap-2">
        <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
        <Button type="submit" size="lg" disabled={pending || !stageId}><ArrowRightIcon aria-hidden="true" />Move</Button>
      </div>
    </form>
  </Sheet.Content>
</Sheet.Root>
