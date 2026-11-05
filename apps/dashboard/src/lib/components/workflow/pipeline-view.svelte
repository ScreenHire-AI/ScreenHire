<script lang="ts">
  import { createListTeamMembers } from '@screenhire/api-client/svelte-query';
  import type { DefinitionError, HiringWorkflow, StageKind } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { tick } from 'svelte';
  import ArrowRightIcon from '~icons/lucide/arrow-right';
  import ClipboardListIcon from '~icons/lucide/clipboard-list';
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import ZapIcon from '~icons/lucide/zap';
  import Hint from '#lib/components/hint.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import ProblemIcon from '#lib/components/problem-icon.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import {
    emailTemplateNames,
    errorsAt,
    errorsUnder,
    isTerminal,
    laneOf,
    newStage,
    removeAt,
    stageKindLabels,
    stageKinds,
    summarize,
  } from '#lib/workflow.js';
  import { labelClass, selectClass } from './controls.js';
  import FieldErrors from './field-errors.svelte';
  import ScorecardName from './scorecard-name.svelte';

  // The default builder view (workflow-engine.md §4): the stages in order, each with the automations that fire on it,
  // then a panel for the automations that belong to no one stage, full width under the stages (S7). Stages reorder with
  // a grip (A7): drag it, open its Move menu, or press Space and the arrow keys (WCAG 2.5.7). What each stage offers
  // beyond its fields (Edit automations, Choose scorecard, Remove stage) is its row's menu, not a button repeated on
  // every row.
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

  let root = $state<HTMLElement>();

  // A sheet opens once the stage's menu has closed: opened from the item itself, it loses focus to the closing menu.
  let sheetChosen: (() => void) | undefined;
  function onMenuClosed() {
    sheetChosen?.();
    sheetChosen = undefined;
  }

  // The row and its menu button leave with the stage, so focus moves to the next stage's menu, or to Add stage after the last.
  async function removeStage(index: number) {
    workflow.stages = removeAt(workflow.stages, index);
    await tick();
    const next = root?.querySelectorAll<HTMLElement>('[data-stage-menu] button')[index];
    (next ?? root?.querySelector<HTMLElement>('[data-stage-add]'))?.focus();
  }

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
      <ProblemIcon class="size-3.5 self-center" />{problems} {problems === 1 ? 'problem' : 'problems'} to fix
    </p>
  {/if}
{/snippet}

<div bind:this={root} class="flex flex-col gap-5">
  <Panel title="Stages" count={workflow.stages.length}>
    {#snippet actions()}
      <Button data-stage-add variant="outline" onclick={() => (workflow.stages = [...workflow.stages, newStage(workflow)])}><PlusIcon aria-hidden="true" />Add stage</Button>
    {/snippet}
    <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
    <ReorderList
      items={workflow.stages}
      key={(stage) => stage.id}
      label="Stages"
      itemLabel={(stage) => stage.name}
      onreorder={(next) => (workflow.stages = next)}
      class="@container divide-y divide-border-subtle [&>li:first-child>div]:pt-0 [&>li:last-child>div]:pb-0"
    >
      {#snippet item(stage, grip)}
        {@const index = workflow.stages.indexOf(stage)}
        {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
        <div aria-label={`Stage ${index + 1}: ${stage.name}`} role="group" class="flex gap-2 bg-card py-4">
          <span class="pt-6">{@render grip()}</span>
          <!-- The stage's id is detail: it is in the marker's tooltip (W6). -->
          <span class="pt-6">
            <Hint content={`Stage id: ${stage.id}`}>
              {#snippet children({ props })}<span {...props}><StepMarker>{index + 1}</StepMarker></span>{/snippet}
            </Hint>
          </span>
          <div class="flex min-w-0 flex-1 flex-col gap-3 pl-1">
            <!-- Narrow, the fields stack and the menu shares the last one's line, so a stage gains no row of its own for it. -->
            <div class="grid grid-cols-[minmax(0,1fr)_auto] items-end gap-2 @xl:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)_7rem_auto]">
              <label class={[labelClass, 'col-span-2 @xl:col-span-1']}>
                Name
                <Input bind:value={stage.name} maxlength={80} aria-invalid={stageErrors.length > 0 || undefined} />
              </label>
              <label class={[labelClass, 'col-span-2 @xl:col-span-1']}>
                Kind
                <select class={selectClass} value={stage.kind} onchange={(event) => (stage.kind = event.currentTarget.value as StageKind)}>
                  {#each stageKinds as kind (kind)}<option value={kind}>{stageKindLabels[kind]}</option>{/each}
                </select>
              </label>
              <!-- A stage that ends the application ignores a time limit (WorkflowStage.HasSla): the input is left out, its
                   place kept (A3); one left from another kind still shows, to clear. -->
              {#if !isTerminal(stage.kind) || stage.slaDays}
                <label class={labelClass}>
                  Time limit (days)
                  <Input
                    type="number"
                    min={1}
                    max={365}
                    placeholder="No limit"
                    value={stage.slaDays ?? ''}
                    onchange={(event) => (stage.slaDays = event.currentTarget.value ? Number(event.currentTarget.value) : null)}
                  />
                </label>
              {:else}<span class="hidden @xl:block"></span>{/if}
              <div data-stage-menu class="col-start-2 flex shrink-0 justify-end @xl:col-start-auto">
                <MoreMenu label={`Actions for ${stage.name}`} size="icon-lg" quiet onclosed={onMenuClosed}>
                  <DropdownMenu.Item onSelect={() => (sheetChosen = () => onopen(stage.id))}><ZapIcon aria-hidden="true" />Edit automations</DropdownMenu.Item>
                  <!-- A stage that ends the application has no scorecard (A3); one left from another kind still shows, to remove. -->
                  {#if !isTerminal(stage.kind) || stage.scorecard || stage.scorecardTemplateId}
                    <DropdownMenu.Item onSelect={() => (sheetChosen = () => onscorecard(index))}><ClipboardListIcon aria-hidden="true" />Choose scorecard</DropdownMenu.Item>
                  {/if}
                  <!-- Nothing changes until Save, so removing a stage from the draft does not confirm (section 10). -->
                  <DropdownMenu.Item onSelect={() => removeStage(index)}><Trash2Icon aria-hidden="true" />Remove stage</DropdownMenu.Item>
                </MoreMenu>
              </div>
            </div>
            <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
            {@render summaries(stage.id)}
            {#if (stage.scorecard || stage.scorecardTemplateId) && !isTerminal(stage.kind)}
              <p class="flex items-center gap-1.5 text-muted-foreground"><ClipboardListIcon aria-hidden="true" /><span><span class="sr-only">Scorecard:</span>{' '}<ScorecardName {stage} /></span></p>
            {/if}
          </div>
        </div>
      {/snippet}
    </ReorderList>
  </Panel>

  <Panel title="Whole workflow" count={inLane(null).length}>
    {#snippet actions()}<Button variant="outline" onclick={() => onopen(null)}>Edit automations <span class="sr-only">for the whole workflow</span></Button>{/snippet}
    <div class="flex flex-col gap-2">
      {@render summaries(null)}
      {#if inLane(null).length === 0}<p class="text-muted-foreground">None yet.</p>{/if}
    </div>
  </Panel>
</div>
