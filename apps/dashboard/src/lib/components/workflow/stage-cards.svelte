<script lang="ts">
  import type { DefinitionError, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
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
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { anyStageFacts, stageFacts } from '#lib/stage-facts.js';
  import { emailTemplateNames, errorsAt, errorsUnder, insertionIndex, insertStage, isTerminal, newStage, withoutStage } from '#lib/workflow.js';
  import FieldErrors from './field-errors.svelte';
  import StageFactList from './stage-fact-list.svelte';

  // The Pipeline tab (design system section 11, Stage cards): the open stages as cards in a row that wraps, joined by a
  // line through their markers as the candidate's stage rail is, and one card per line on a phone. A card is its number,
  // its name (a button that opens the stage's sheet, with a pencil and the tooltip "Edit <stage>") and short facts, one a
  // line: the time limit, the meeting, each email, each automation as a short label linking to it, the question count, the
  // scorecard. Reorder by the grip (A7: drag it, or press it for Move left, Move right, Move to start and Move to end). A
  // plus on the line between two cards adds a stage there and opens its sheet to name it. Grip and menu show on hover,
  // focus and touch (F4). Under the cards, the automations that belong to no stage are one row, Any stage, and the stages
  // that end a candidacy are one row, Ends.
  let {
    workflow = $bindable(),
    errors,
    onopen,
    onscorecard,
    onautomations,
    automationHref,
    questionsHref,
    onshow,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Opens the stage's sheet. */
    onopen: (stageId: string) => void;
    /** Opens the scorecard sheet for the stage at this index. */
    onscorecard: (index: number) => void;
    /** Opens the automations drawer for a stage. */
    onautomations: (stageId: string) => void;
    /** Where an automation's line links to: it on the Automations tab. */
    automationHref?: (automationId: string) => string;
    /** Where the question count links to: the Questions tab. */
    questionsHref?: string;
    /** Opens an automation in the page instead of following its link. */
    onshow?: (automationId: string) => void;
  } = $props();

  let root = $state<HTMLElement>();

  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const open = $derived(workflow.stages.filter((stage) => !isTerminal(stage.kind)));
  const ends = $derived(workflow.stages.filter((stage) => isTerminal(stage.kind)));
  const anyStage = $derived(anyStageFacts(workflow, templateNames));

  // A sheet opens once the card's menu has closed: opened from the item itself, it loses focus to the closing menu.
  let sheetChosen: (() => void) | undefined;
  function onMenuClosed() {
    sheetChosen?.();
    sheetChosen = undefined;
  }

  // The card and its menu leave with the stage, so focus moves to the next card's name, or to the last one.
  async function removeStage(stage: WorkflowStage) {
    const at = open.indexOf(stage);
    const next = withoutStage(workflow, workflow.stages.indexOf(stage));
    workflow.stages = next.stages;
    workflow.automations = next.automations;
    await tick();
    const names = root?.querySelectorAll<HTMLElement>('[data-stage-card] [data-stage-open]');
    (names?.[at] ?? names?.[names.length - 1])?.focus();
  }

  function addAfter(stage: WorkflowStage) {
    const added = newStage(workflow);
    workflow.stages = insertStage(workflow.stages, insertionIndex(workflow.stages, workflow.stages.indexOf(stage)), added);
    onopen(added.id);
  }

  // The grip orders the open stages; the stages that end a candidacy keep their place after them.
  const reorder = (next: WorkflowStage[]) => (workflow.stages = [...next, ...ends]);

  // The line to the next card runs across the gap, so the last card of a row that wraps would point at nothing. CSS cannot
  // tell which card ends a row in an auto-fill grid, so the cards are marked from where they sit whenever the row's width
  // or the stages change: a card whose next card starts further left ends its row. In one column nothing is marked and
  // the line runs down.
  let grid = $state<HTMLElement>();
  function markRowEnds() {
    const cards = [...(grid?.querySelectorAll<HTMLElement>('[data-stage-card][role="group"]') ?? [])];
    cards.forEach((card, index) => {
      const next = cards[index + 1];
      const endsRow = next !== undefined && next.getBoundingClientRect().left < card.getBoundingClientRect().left - 1;
      card.toggleAttribute('data-row-end', endsRow);
    });
  }
  $effect(() => {
    void open.map((stage) => stage.id).join();
    if (!grid || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(markRowEnds);
    observer.observe(grid);
    void tick().then(markRowEnds);
    return () => observer.disconnect();
  });
</script>

{#snippet stageMenu(stage: WorkflowStage)}
  {@const index = workflow.stages.indexOf(stage)}
  <MoreMenu label={`Actions for ${stage.name}`} size="icon-lg" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item onSelect={() => (sheetChosen = () => onopen(stage.id))}><PencilIcon aria-hidden="true" />Edit stage</DropdownMenu.Item>
    <DropdownMenu.Item onSelect={() => (sheetChosen = () => onautomations(stage.id))}><ZapIcon aria-hidden="true" />Add an automation</DropdownMenu.Item>
    {#if !isTerminal(stage.kind) || stage.scorecard || stage.scorecardTemplateId}
      <DropdownMenu.Item onSelect={() => (sheetChosen = () => onscorecard(index))}><ClipboardListIcon aria-hidden="true" />Choose scorecard</DropdownMenu.Item>
    {/if}
    {#if index > 0}
      <!-- Nothing changes until Save, so removing a stage from the draft does not confirm (section 10). -->
      <DropdownMenu.Item onSelect={() => removeStage(stage)}><Trash2Icon aria-hidden="true" />Remove stage</DropdownMenu.Item>
    {/if}
  </MoreMenu>
{/snippet}

{#snippet stageName(stage: WorkflowStage)}
  <!-- The name is a button and looks like one: a hover fill, a pencil, and the tooltip saying what it does. -->
  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <button
          {...props}
          type="button"
          data-stage-open={stage.id}
          aria-haspopup="dialog"
          class="-mx-2 inline-flex min-w-0 items-start gap-1.5 rounded-lg px-2 py-1 text-left font-semibold break-words hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring pointer-coarse:min-h-11 pointer-coarse:items-center"
          onclick={() => onopen(stage.id)}
        >
          <span class="min-w-0">{stage.name}</span><PencilIcon aria-hidden="true" class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
        </button>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>Edit {stage.name}</Tooltip.Content>
  </Tooltip.Root>
{/snippet}

<div bind:this={root}>
  <Panel title="Stages">
    <!-- The container the Any stage and Ends rows lay out against; the stage grid keeps its own below. -->
    <div class="@container flex flex-col gap-6">
      <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
      <div class="@container" bind:this={grid}>
        <ReorderList
          items={open}
          key={(stage) => stage.id}
          label="Stages"
          itemLabel={(stage) => stage.name}
          orientation="horizontal"
          onreorder={reorder}
          class="grid grid-cols-1 gap-x-6 gap-y-6 pointer-coarse:gap-x-12 pointer-coarse:gap-y-12 @md:grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]"
        >
          {#snippet item(stage, grip)}
            {@const number = open.indexOf(stage) + 1}
            {@const index = workflow.stages.indexOf(stage)}
            {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
            {@const last = number === open.length}
            <div data-stage-card aria-label={`Stage ${number}: ${stage.name}`} role="group" class="group relative h-full">
              {#if !last}
                <!-- The line to the next stage, through the markers: across the gap in a row, down it when the cards stack. -->
                <span aria-hidden="true" data-stage-line class="absolute group-data-row-end:hidden top-full left-6.5 h-6 w-px bg-border pointer-coarse:h-12 @md:top-6.5 @md:left-full @md:h-px @md:w-6 @md:pointer-coarse:w-12"></span>
              {/if}
              <div
                class={[
                  'relative flex h-full flex-col gap-2 rounded-panel border bg-card p-3 shadow-surface',
                  stageErrors.length > 0 ? 'border-status-verify' : 'border-border',
                ]}
              >
                <div class="flex items-center gap-2">
                  <StepMarker>{number}</StepMarker>
                  <span class="ml-auto"></span>
                  <span class="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">{@render grip()}</span>
                  <div data-stage-menu class="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">
                    {@render stageMenu(stage)}
                  </div>
                </div>
                <div>{@render stageName(stage)}</div>
                <StageFactList facts={stageFacts(workflow, stage, templateNames)} {stage} {automationHref} {questionsHref} {onshow} />
                <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
              </div>
              <!-- On the line after the card: to its right in a row, below it when the cards stack. -->
              <button
                type="button"
                aria-label={`Add a stage after ${stage.name}`}
                data-stage-add
                class="absolute -bottom-[1.125rem] left-1/2 z-10 grid size-6 -translate-x-1/2 place-items-center rounded-full border border-input bg-card text-muted-foreground opacity-0 hover:bg-muted hover:text-foreground focus-visible:opacity-100 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:size-11 pointer-coarse:opacity-100 @md:top-6.5 @md:-right-[1.125rem] @md:bottom-auto @md:left-auto @md:-translate-y-1/2 @md:translate-x-0"
                onclick={() => addAfter(stage)}
              >
                <PlusIcon aria-hidden="true" class="size-4" />
              </button>
            </div>
          {/snippet}
        </ReorderList>
      </div>

      {#if anyStage.length > 0}
        <section aria-labelledby="stage-cards-any" class="flex flex-col gap-1.5 border-t border-border-subtle pt-4 @md:flex-row @md:items-baseline @md:gap-6">
          <h3 id="stage-cards-any" class="shrink-0 text-heading">Any stage</h3>
          <StageFactList facts={anyStage} {automationHref} {onshow} />
        </section>
      {/if}

      {#if ends.length > 0}
        <section aria-labelledby="stage-cards-ends" class="flex flex-col gap-3 border-t border-border-subtle pt-4">
          <h3 id="stage-cards-ends" class="text-heading">Ends</h3>
          <ul class="grid grid-cols-1 gap-3 @md:grid-cols-[repeat(auto-fill,minmax(14rem,1fr))]">
            {#each ends as stage (stage.id)}
              {@const index = workflow.stages.indexOf(stage)}
              {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
              <li>
                <div data-stage-card role="group" aria-label={`End stage: ${stage.name}`} class="group flex flex-col gap-1">
                  <div class="flex items-start gap-2">
                    <div class="min-w-0 flex-1">{@render stageName(stage)}</div>
                    <div data-stage-menu class="opacity-0 group-focus-within:opacity-100 group-hover:opacity-100 pointer-coarse:opacity-100">{@render stageMenu(stage)}</div>
                  </div>
                  <StageFactList facts={stageFacts(workflow, stage, templateNames)} {stage} {automationHref} {onshow} />
                  <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
                </div>
              </li>
            {/each}
          </ul>
        </section>
      {/if}
    </div>
  </Panel>
</div>
