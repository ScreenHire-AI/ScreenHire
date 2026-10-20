<script lang="ts">
  import type { DefinitionError, HiringWorkflow, StageKind } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { errorsAt, errorsUnder, laneOf, moveItem, newStage, removeAt, stageKindLabels, stageKinds, summarize } from '#lib/workflow.js';
  import { kickerClass, labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import ProblemMark from './problem-mark.svelte';

  // The default builder view (workflow-engine.md §4): the stages in order, each with the automations that fire on it,
  // and a lane for the automations that belong to no one stage. Stages reorder with buttons, not dragging (WCAG 2.5.7).
  let {
    workflow = $bindable(),
    errors,
    readonly = false,
    onopen,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    readonly?: boolean;
    /** Opens the automations drawer for a stage, or for the whole workflow (null). */
    onopen: (stageId: string | null) => void;
  } = $props();

  const inLane = (stageId: string | null) => workflow.automations.filter((automation) => laneOf(automation) === stageId);
  const laneErrors = (stageId: string | null) =>
    workflow.automations.reduce(
      (count, automation, index) => count + (laneOf(automation) === stageId ? errorsUnder(errors, `automations[${index}]`).length : 0),
      0,
    );
</script>

{#snippet lane(stageId: string | null, name: string)}
  {@const automations = inLane(stageId)}
  {@const problems = laneErrors(stageId)}
  <div class="flex flex-col gap-1.5">
    {#if automations.length > 0}
      <ul class="flex flex-col gap-1 text-[13px]">
        {#each automations as automation (automation.id)}
          <li class={['flex gap-1.5', automation.enabled === false && 'text-muted-foreground']}>
            <span aria-hidden="true" class="text-muted-foreground">→</span>
            <span>{summarize(automation, workflow)}{automation.enabled === false ? ' (off)' : ''}</span>
          </li>
        {/each}
      </ul>
    {/if}
    <div class="flex flex-wrap items-center gap-2">
      <Button variant="outline" size="sm" onclick={() => onopen(stageId)}>
        Automations <span class="sr-only">for {name}</span>
        <span class="font-mono text-[11px] text-muted-foreground tabular-nums">{automations.length}</span>
      </Button>
      {#if problems > 0}
        <span class="inline-flex items-center gap-1.5 text-[12.5px] font-semibold text-destructive">
          <ProblemMark />{problems} {problems === 1 ? 'problem' : 'problems'} to fix
        </span>
      {/if}
    </div>
  </div>
{/snippet}

<div class="flex flex-col gap-3">
  <section aria-labelledby="lane-workflow" class="flex flex-col gap-2 rounded-xl border border-border bg-card p-3.5">
    <div>
      <h3 id="lane-workflow" class="font-bold">Whole workflow</h3>
      <p class="text-[12.5px] text-muted-foreground">
        Automations tied to no one stage: when an application arrives, when the integrity checks finish, or run by hand.
      </p>
    </div>
    {@render lane(null, 'the whole workflow')}
  </section>

  <section aria-labelledby="stages-heading" class="flex flex-col gap-2">
    <div class="flex items-center justify-between gap-2">
      <h3 id="stages-heading" class={kickerClass}>Stages, in order</h3>
      {#if !readonly}
        <Button variant="outline" size="sm" onclick={() => (workflow.stages = [...workflow.stages, newStage(workflow)])}>Add stage</Button>
      {/if}
    </div>
    <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
    <ol class="flex flex-col gap-2">
      {#each workflow.stages as stage, index (stage.id)}
        {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
        <li aria-label={`Stage ${index + 1}: ${stage.name}`} class="flex flex-col gap-2.5 rounded-xl border border-border bg-card p-3.5">
          <fieldset disabled={readonly} class="flex flex-col gap-2">
            <legend class="sr-only">Stage {index + 1}</legend>
            <div class="grid items-end gap-2 sm:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)_6rem_auto]">
              <label class={labelClass}>
                <span>Name <span class="font-mono text-[11px] font-normal text-muted-foreground">{stage.id}</span></span>
                <Input bind:value={stage.name} maxlength={80} aria-invalid={stageErrors.length > 0 || undefined} />
              </label>
              <label class={labelClass}>
                Kind
                <select class={selectClass} value={stage.kind} onchange={(event) => (stage.kind = event.currentTarget.value as StageKind)}>
                  {#each stageKinds as kind (kind)}<option value={kind}>{stageKindLabels[kind]}</option>{/each}
                </select>
              </label>
              <label class={labelClass}>
                SLA (days)
                <Input
                  type="number"
                  min={1}
                  max={365}
                  value={stage.slaDays ?? ''}
                  onchange={(event) => (stage.slaDays = event.currentTarget.value ? Number(event.currentTarget.value) : null)}
                />
              </label>
              {#if !readonly}
                <div class="flex gap-0.5">
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Move ${stage.name} earlier`}
                    disabled={index === 0}
                    onclick={() => (workflow.stages = moveItem(workflow.stages, index, -1))}><ArrowUpIcon aria-hidden="true" /></Button
                  >
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Move ${stage.name} later`}
                    disabled={index === workflow.stages.length - 1}
                    onclick={() => (workflow.stages = moveItem(workflow.stages, index, 1))}><ArrowDownIcon aria-hidden="true" /></Button
                  >
                  <Button
                    variant="ghost"
                    size="icon-sm"
                    aria-label={`Remove ${stage.name}`}
                    onclick={() => (workflow.stages = removeAt(workflow.stages, index))}><Trash2Icon aria-hidden="true" /></Button
                  >
                </div>
              {/if}
            </div>
            <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
          </fieldset>
          {@render lane(stage.id, stage.name)}
        </li>
      {/each}
    </ol>
  </section>
</div>
