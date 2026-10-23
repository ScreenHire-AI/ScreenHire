<script lang="ts">
  import { createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { DefinitionError, HiringWorkflow, StageKind } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import IconButton from '#lib/components/icon-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import {
    emailTemplateNames,
    errorsAt,
    errorsUnder,
    isTerminal,
    laneOf,
    moveItem,
    newStage,
    removeAt,
    stageKindLabels,
    stageKinds,
    summarize,
  } from '#lib/workflow.js';
  import { labelClass, rowClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import ProblemMark from './problem-mark.svelte';

  // The default builder view (workflow-engine.md §4): the stages in order, each with the automations that fire on it,
  // and a panel for the automations that belong to no one stage. Stages reorder with buttons, not dragging (WCAG 2.5.7).
  let {
    workflow = $bindable(),
    errors,
    onopen,
    onscorecard,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Opens the automations drawer for a stage, or for the whole workflow (null). */
    onopen: (stageId: string | null) => void;
    /** Opens the scorecard sheet for the stage at this index. */
    onscorecard: (index: number) => void;
  } = $props();
  // Names an AssignReviewer action's reviewer.
  const team = createListTeamMembers();

  // The organization's template names, for "Send the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));

  const inLane = (stageId: string | null) => workflow.automations.filter((automation) => laneOf(automation) === stageId);
  const laneErrors = (stageId: string | null) =>
    workflow.automations.reduce(
      (count, automation, index) => count + (laneOf(automation) === stageId ? errorsUnder(errors, `automations[${index}]`).length : 0),
      0,
    );
</script>

{#snippet summaries(stageId: string | null)}
  {@const automations = inLane(stageId)}
  {@const problems = laneErrors(stageId)}
  {#if automations.length > 0}
    <ul class="flex flex-col gap-1">
      {#each automations as automation (automation.id)}
        <li class={['flex gap-1.5', automation.enabled === false && 'text-muted-foreground']}>
          <ArrowRightIcon aria-hidden="true" class="mt-0.5 shrink-0 text-muted-foreground" />
          <span>{summarize(automation, workflow, team.data?.data, templateNames)}{automation.enabled === false ? ' (off)' : ''}</span>
        </li>
      {/each}
    </ul>
  {/if}
  {#if problems > 0}
    <p class="inline-flex items-center gap-1.5 font-semibold text-status-verify">
      <ProblemMark />{problems} {problems === 1 ? 'problem' : 'problems'} to fix
    </p>
  {/if}
{/snippet}

<!-- 36px in a stage's row, 32px in a panel header (section 5). -->
{#snippet automationsButton(stageId: string | null, name: string, size: 'default' | 'lg')}
  <Button variant="outline" {size} onclick={() => onopen(stageId)}>Automations <span class="sr-only">for {name}</span></Button>
{/snippet}

<div class="flex flex-wrap items-start gap-5">
  <Panel title="Stages" count={workflow.stages.length} class="flex-[999_1_520px]">
    {#snippet actions()}
      <Button variant="outline" onclick={() => (workflow.stages = [...workflow.stages, newStage(workflow)])}><PlusIcon aria-hidden="true" />Add stage</Button>
    {/snippet}
    <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
    <ol class="@container flex flex-col">
      {#each workflow.stages as stage, index (stage.id)}
        {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
        <li aria-label={`Stage ${index + 1}: ${stage.name}`} class={['flex gap-3', rowClass]}>
          <span class="pt-6"><StepMarker>{index + 1}</StepMarker></span>
          <div class="flex min-w-0 flex-1 flex-col gap-3">
            <div class="grid items-end gap-2 @xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)_7rem_auto]">
              <label class={labelClass}>
                <span>Name <span class="font-mono text-code font-normal text-muted-foreground">{stage.id}</span></span>
                <Input bind:value={stage.name} maxlength={80} aria-invalid={stageErrors.length > 0 || undefined} />
              </label>
              <label class={labelClass}>
                Kind
                <select class={selectClass} value={stage.kind} onchange={(event) => (stage.kind = event.currentTarget.value as StageKind)}>
                  {#each stageKinds as kind (kind)}<option value={kind}>{stageKindLabels[kind]}</option>{/each}
                </select>
              </label>
              <label class={labelClass}>
                Time limit (days)
                <Input
                  type="number"
                  min={1}
                  max={365}
                  value={stage.slaDays ?? ''}
                  onchange={(event) => (stage.slaDays = event.currentTarget.value ? Number(event.currentTarget.value) : null)}
                />
              </label>
              <div class="flex shrink-0 justify-end gap-0.5">
                <!-- The first stage cannot move earlier nor the last later: the button is left out, its place kept (A3). -->
                {#if index > 0}
                  <IconButton label={`Move ${stage.name} earlier`} onclick={() => (workflow.stages = moveItem(workflow.stages, index, -1))}><ArrowUpIcon /></IconButton>
                {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
                {#if index < workflow.stages.length - 1}
                  <IconButton label={`Move ${stage.name} later`} onclick={() => (workflow.stages = moveItem(workflow.stages, index, 1))}><ArrowDownIcon /></IconButton>
                {:else}<span class="size-8 pointer-coarse:size-11"></span>{/if}
                <IconButton label={`Remove ${stage.name}`} onclick={() => (workflow.stages = removeAt(workflow.stages, index))}><Trash2Icon /></IconButton>
              </div>
            </div>
            <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
            {@render summaries(stage.id)}
            {#if stage.scorecard && !isTerminal(stage.kind)}
              {@const criteria = stage.scorecard.criteria.length}
              <p class="text-muted-foreground">Scorecard: {criteria} {criteria === 1 ? 'criterion' : 'criteria'} and a recommendation</p>
            {/if}
            <div class="flex flex-wrap gap-2">
              {@render automationsButton(stage.id, stage.name, 'lg')}
              <!-- A stage that ends the application has no scorecard (A3); one left from another kind still shows, to remove. -->
              {#if !isTerminal(stage.kind) || stage.scorecard}
                <Button variant="outline" size="lg" onclick={() => onscorecard(index)}>Scorecard <span class="sr-only">for {stage.name}</span></Button>
              {/if}
            </div>
          </div>
        </li>
      {/each}
    </ol>
  </Panel>

  <Panel title="Whole workflow" count={inLane(null).length} class="flex-[1_1_320px]">
    {#snippet actions()}{@render automationsButton(null, 'the whole workflow', 'default')}{/snippet}
    <div class="flex flex-col gap-2">
      {@render summaries(null)}
      {#if inLane(null).length === 0}<p class="text-muted-foreground">None yet.</p>{/if}
    </div>
  </Panel>
</div>
