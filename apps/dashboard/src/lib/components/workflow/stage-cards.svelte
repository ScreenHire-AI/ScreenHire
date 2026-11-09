<script lang="ts">
  import type { DefinitionError, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
  import { tick } from 'svelte';
  import ClipboardListIcon from '~icons/lucide/clipboard-list';
  import PencilIcon from '~icons/lucide/pencil';
  import PlusIcon from '~icons/lucide/plus';
  import Trash2Icon from '~icons/lucide/trash-2';
  import ZapIcon from '~icons/lucide/zap';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { automationsAt } from '#lib/journey-text.js';
  import { errorsAt, errorsUnder, insertionIndex, insertStage, isTerminal, newStage, withoutStage } from '#lib/workflow.js';
  import FieldErrors from './field-errors.svelte';
  import ScorecardName from './scorecard-name.svelte';

  // The Pipeline tab (design system section 11, Stage cards): the stages as cards in a row that wraps, and one card per
  // line on a phone. A card is its number, name and short facts; pressing it opens the stage's sheet. Reorder by the grip
  // (A7: drag it, or press it for Move left, Move right, Move to start and Move to end). A plus between two cards adds a
  // stage there and opens its sheet to name it. Grip and menu show on hover, focus and touch (F4).
  let {
    workflow = $bindable(),
    errors,
    onopen,
    onscorecard,
    onautomations,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Opens the stage's sheet. */
    onopen: (stageId: string) => void;
    /** Opens the scorecard sheet for the stage at this index. */
    onscorecard: (index: number) => void;
    /** Opens the automations drawer for a stage. */
    onautomations: (stageId: string) => void;
  } = $props();

  let root = $state<HTMLElement>();

  // A sheet opens once the card's menu has closed: opened from the item itself, it loses focus to the closing menu.
  let sheetChosen: (() => void) | undefined;
  function onMenuClosed() {
    sheetChosen?.();
    sheetChosen = undefined;
  }

  // The card and its menu leave with the stage, so focus moves to the next card's name, or to the last one.
  async function removeStage(index: number) {
    const next = withoutStage(workflow, index);
    workflow.stages = next.stages;
    workflow.automations = next.automations;
    await tick();
    const names = root?.querySelectorAll<HTMLElement>('[data-stage-card] [data-stage-open]');
    (names?.[index] ?? names?.[names.length - 1])?.focus();
  }

  function addAfter(index: number) {
    const stage = newStage(workflow);
    workflow.stages = insertStage(workflow.stages, insertionIndex(workflow.stages, index), stage);
    onopen(stage.id);
  }

  // The reminder for the time limit is the limit itself, so it is not counted as an automation too (W7).
  const automationCount = (stage: WorkflowStage) => automationsAt(workflow, stage.id, undefined, false).length;
  // A stage has a plus after it unless it ends the candidacy: a new stage goes before those.
  const canInsertAfter = (stage: WorkflowStage) => !isTerminal(stage.kind);
</script>

<div bind:this={root}>
  <Panel title="Stages">
    <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
    <div class="@container">
      <ReorderList
        items={workflow.stages}
        key={(stage) => stage.id}
        label="Stages"
        itemLabel={(stage) => stage.name}
        orientation="horizontal"
        onreorder={(next) => (workflow.stages = next)}
        class="grid grid-cols-1 gap-x-6 gap-y-6 pointer-coarse:gap-x-12 pointer-coarse:gap-y-12 @md:grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]"
      >
        {#snippet item(stage, grip)}
          {@const index = workflow.stages.indexOf(stage)}
          {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
          {@const count = automationCount(stage)}
          <div data-stage-card aria-label={`Stage ${index + 1}: ${stage.name}`} role="group" class="group relative h-full">
            <div
              class={[
                'relative flex h-full flex-col gap-2 rounded-panel border bg-card p-3 shadow-surface hover:bg-muted/40',
                stageErrors.length > 0 ? 'border-status-verify' : 'border-border',
              ]}
            >
              <div class="flex items-center gap-2">
                <StepMarker>{index + 1}</StepMarker>
                <span class="ml-auto"></span>
                <span class="relative z-10 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">{@render grip()}</span>
                <div data-stage-menu class="relative z-10 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">
                  <MoreMenu label={`Actions for ${stage.name}`} size="icon-lg" quiet onclosed={onMenuClosed}>
                    <DropdownMenu.Item onSelect={() => (sheetChosen = () => onopen(stage.id))}><PencilIcon aria-hidden="true" />Edit stage</DropdownMenu.Item>
                    <DropdownMenu.Item onSelect={() => (sheetChosen = () => onautomations(stage.id))}><ZapIcon aria-hidden="true" />Add an automation</DropdownMenu.Item>
                    {#if !isTerminal(stage.kind) || stage.scorecard || stage.scorecardTemplateId}
                      <DropdownMenu.Item onSelect={() => (sheetChosen = () => onscorecard(index))}><ClipboardListIcon aria-hidden="true" />Choose scorecard</DropdownMenu.Item>
                    {/if}
                    {#if index > 0}
                      <!-- Nothing changes until Save, so removing a stage from the draft does not confirm (section 10). -->
                      <DropdownMenu.Item onSelect={() => removeStage(index)}><Trash2Icon aria-hidden="true" />Remove stage</DropdownMenu.Item>
                    {/if}
                  </MoreMenu>
                </div>
              </div>
              <!-- The name is the card's one button: its box stretches over the card, under the grip and the menu. -->
              <button
                type="button"
                data-stage-open={stage.id}
                aria-haspopup="dialog"
                class="text-left font-semibold break-words after:absolute after:inset-0 after:rounded-panel focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:outline-offset-2 focus-visible:after:outline-ring"
                onclick={() => onopen(stage.id)}
              >
                {stage.name}
              </button>
              {#if (stage.slaDays && !isTerminal(stage.kind)) || stage.scorecard || stage.scorecardTemplateId || count > 0}
                <ul class="flex flex-col gap-1 text-muted-foreground">
                  {#if stage.slaDays && !isTerminal(stage.kind)}<li class="tabular-nums">{stage.slaDays}-day limit</li>{/if}
                  {#if !isTerminal(stage.kind) && (stage.scorecard || stage.scorecardTemplateId)}
                    <li class="break-words"><span class="sr-only">Scorecard:</span>{' '}<ScorecardName {stage} /></li>
                  {/if}
                  {#if count > 0}
                    <li class="flex items-center gap-1.5"><ZapIcon aria-hidden="true" class="shrink-0 text-primary" />{count} {count === 1 ? 'automation' : 'automations'}</li>
                  {/if}
                </ul>
              {/if}
              <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
            </div>
            {#if canInsertAfter(stage)}
              <!-- In the gap after the card: to its right in a row, below it when the cards stack. -->
              <button
                type="button"
                aria-label={`Add a stage after ${stage.name}`}
                data-stage-add
                class="absolute -bottom-[1.125rem] left-1/2 z-10 grid size-6 -translate-x-1/2 place-items-center rounded-full border border-input bg-card text-muted-foreground opacity-0 hover:bg-muted hover:text-foreground focus-visible:opacity-100 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:size-11 pointer-coarse:opacity-100 @md:top-1/2 @md:-right-[1.125rem] @md:bottom-auto @md:left-auto @md:-translate-y-1/2 @md:translate-x-0"
                onclick={() => addAfter(index)}
              >
                <PlusIcon aria-hidden="true" class="size-4" />
              </button>
            {/if}
          </div>
        {/snippet}
      </ReorderList>
    </div>
  </Panel>
</div>
