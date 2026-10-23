<script lang="ts">
  import type { DefinitionError, WorkflowStage } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import IconButton from '#lib/components/icon-button.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { defaultScorecard, errorsAt, errorsUnder, moveItem, newCriterion, ratingLabels, ratings, removeAt } from '#lib/workflow.js';
  import { insetClass, labelClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';

  // A stage's scorecard (workflow-engine.md §8): the job-related criteria reviewers rate on the fixed scale, and the
  // overall recommendation automations test. Criteria reorder with buttons, never only by dragging. Removing one, or the
  // whole scorecard, changes nothing until Save, so it is not destructive (design system section 10).
  let { stage = $bindable(), index, errors }: { stage: WorkflowStage; index: number; errors: readonly DefinitionError[] } = $props();

  const uid = $props.id();
  const path = $derived(`stages[${index}].scorecard`);
</script>

<div class="flex flex-col gap-4">
  <p class="text-body text-muted-foreground">Rate what the job needs: skills, experience and the work itself. Never personality, emotion or honesty.</p>
  <FieldErrors id={`${uid}-errors`} messages={[...errorsAt(errors, path), ...errorsAt(errors, `${path}.criteria`)]} />

  {#if stage.scorecard}
    {@const scorecard = stage.scorecard}
    <ol class="flex flex-col gap-2" aria-label="Criteria">
      {#each scorecard.criteria as criterion, criterionIndex (criterion.id)}
        {@const criterionPath = `${path}.criteria[${criterionIndex}]`}
        {@const criterionErrors = errorsUnder(errors, criterionPath).map((error) => error.message)}
        <li class={insetClass}>
          <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_auto]">
            <label class={labelClass}>
              <span>Criterion {criterionIndex + 1} <span class="font-mono text-code font-normal text-muted-foreground">{criterion.id}</span></span>
              <Input bind:value={criterion.label} maxlength={120} aria-invalid={errorsAt(errors, `${criterionPath}.label`).length > 0 || undefined} />
            </label>
            <label class={labelClass}>
              What to look for
              <Input
                bind:value={() => criterion.guidance ?? '', (value) => (criterion.guidance = value.trim() ? value : null)}
                maxlength={500}
                aria-invalid={errorsAt(errors, `${criterionPath}.guidance`).length > 0 || undefined}
              />
            </label>
            <div class="flex shrink-0 gap-0.5">
              {#if criterionIndex > 0}
                <IconButton label={`Move ${criterion.label || 'criterion'} up`} onclick={() => (scorecard.criteria = moveItem(scorecard.criteria, criterionIndex, -1))}><ArrowUpIcon /></IconButton>
              {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
              {#if criterionIndex < scorecard.criteria.length - 1}
                <IconButton label={`Move ${criterion.label || 'criterion'} down`} onclick={() => (scorecard.criteria = moveItem(scorecard.criteria, criterionIndex, 1))}><ArrowDownIcon /></IconButton>
              {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
              <IconButton label={`Remove ${criterion.label || 'criterion'}`} onclick={() => (scorecard.criteria = removeAt(scorecard.criteria, criterionIndex))}><Trash2Icon /></IconButton>
            </div>
          </div>
          <FieldErrors id={`${uid}-criterion-${criterionIndex}-errors`} messages={criterionErrors} />
        </li>
      {/each}
    </ol>
    <p class="text-caption text-muted-foreground">Rated {ratings.map((rating) => ratingLabels[rating].toLowerCase()).join(', ')}, then an overall recommendation.</p>
    <div class="flex flex-wrap gap-2">
      <Button variant="outline" size="lg" onclick={() => (scorecard.criteria = [...scorecard.criteria, newCriterion(scorecard)])}>
        <PlusIcon aria-hidden="true" />Add criterion
      </Button>
      <Button variant="outline" size="lg" onclick={() => (stage.scorecard = null)}><Trash2Icon aria-hidden="true" />Remove scorecard</Button>
    </div>
  {:else}
    <p class="text-body">No scorecard: reviewers record no decision in {stage.name}.</p>
    <div><Button variant="outline" size="lg" onclick={() => (stage.scorecard = defaultScorecard(stage.kind))}><PlusIcon aria-hidden="true" />Add scorecard</Button></div>
  {/if}
</div>
