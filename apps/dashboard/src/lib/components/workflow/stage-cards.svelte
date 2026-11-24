<script lang="ts">
  import type { DefinitionError, HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import { tick } from 'svelte';
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import ChevronRightIcon from '~icons/lucide/chevron-right';
  import MailIcon from '~icons/lucide/mail';
  import PencilIcon from '~icons/lucide/pencil';
  import PlusIcon from '~icons/lucide/plus';
  import ZapIcon from '~icons/lucide/zap';
  import Panel from '#lib/components/panel.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { plural } from '#lib/forms-timeline.js';
  import { anyStageChips, stageChips, type StageChipCounts } from '#lib/stage-facts.js';
  import { emailTemplateNames, errorsAt, errorsUnder, insertionIndex, insertStage, isTerminal, newStage } from '#lib/workflow.js';
  import FieldErrors from './field-errors.svelte';
  import StageChips from './stage-chips.svelte';

  // The Pipeline tab (design system section 11, Stage cards): the open stages as a flow of cards that share the panel's
  // width in equal columns, as the board's columns do, joined by a connector with its arrow. A card is one header
  // row, the grip, its number and its name (a button that opens the stage's sheet, with a pencil and the tooltip "Edit
  // <stage>"), then chips that count what happens there (Stage chips): no sentences and no menu, since the sheet holds
  // Remove stage and the scorecard, and automations are edited on their own tab. Reorder by the grip (A7: drag it, or press
  // it for Move left, Move right, Move to start and Move to end). A plus on the connector adds a stage there and opens its
  // sheet to name it. Grip and plus are always visible (F4). Under the flow, the automations that belong to no stage are
  // one row, Any stage, and the stages that end a candidacy are quiet buttons in one row, Ends, each opening its sheet.
  let {
    workflow = $bindable(),
    errors,
    onopen,
    onscorecard,
    onautomations,
    automationHref,
    questionsHref,
    onshow,
    onshowform,
  }: {
    workflow: HiringWorkflow;
    errors: readonly DefinitionError[];
    /** Opens the stage's sheet. */
    onopen: (stageId: string) => void;
    /** Opens the scorecard sheet for the stage at this index. */
    onscorecard: (index: number) => void;
    /** Goes to these automations of a stage, or of no stage (null), on the Automations tab. */
    onautomations: (stageId: string | null, ids: readonly string[]) => void;
    /** Where an automation's item links to: it on the Automations tab. */
    automationHref?: (automationId: string) => string;
    /** Where a form's item links to: the Forms tab, which the form's id is added to. */
    questionsHref?: string;
    /** Opens an automation in the page instead of following its link. */
    onshow?: (automationId: string) => void;
    /** Opens a form on the Forms tab instead of following its link. */
    onshowform?: (formId: string) => void;
  } = $props();

  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const open = $derived(workflow.stages.filter((stage) => !isTerminal(stage.kind)));
  const ends = $derived(workflow.stages.filter((stage) => isTerminal(stage.kind)));
  const anyStage = $derived(anyStageChips(workflow, templateNames));
  // An end's button says what it sends: "Edit Rejected: 1 email".
  function endLabel(name: string, chips: StageChipCounts): string {
    const counts = [chips.automations.length && plural(chips.automations.length, 'automation'), chips.emails.length && plural(chips.emails.length, 'email')].filter(Boolean);
    return counts.length > 0 ? `Edit ${name}: ${counts.join(', ')}` : `Edit ${name}`;
  }
  const formHref = $derived(questionsHref ? (formId: string) => `${questionsHref}${questionsHref.includes('?') ? '&' : '?'}form=${encodeURIComponent(formId)}` : undefined);

  // A name's last word, which the pencil stays beside when the name wraps.
  function splitLastWord(name: string): { head: string; last: string } {
    const at = name.trimEnd().lastIndexOf(' ') + 1;
    return { head: name.slice(0, at), last: name.slice(at) };
  }

  function addAfter(stage: WorkflowStage) {
    const added = newStage(workflow);
    workflow.stages = insertStage(workflow.stages, insertionIndex(workflow.stages, workflow.stages.indexOf(stage)), added);
    onopen(added.id);
  }

  // The grip orders the open stages; the stages that end a candidacy keep their place after them.
  const reorder = (next: WorkflowStage[]) => (workflow.stages = [...next, ...ends]);

  // The connector to the next card runs across the gap when the next card sits to the right, down when it sits below (one
  // card a line), and not at all from the last card of a row that wraps. Where the cards sit is the grid's choice (the
  // style block), so each card is marked from where it lands whenever the row's width or the stages change.
  let grid = $state<HTMLElement>();
  function markNext() {
    const cards = [...(grid?.querySelectorAll<HTMLElement>('[data-stage-card][role="group"]') ?? [])];
    cards.forEach((card, index) => {
      const next = cards[index + 1];
      if (!next) return void (card.dataset.next = 'end');
      const here = card.getBoundingClientRect();
      const there = next.getBoundingClientRect();
      card.dataset.next = there.left > here.left + 1 ? 'right' : Math.abs(there.left - here.left) <= 1 ? 'down' : 'none';
    });
  }
  $effect(() => {
    void open.map((stage) => stage.id).join();
    if (!grid || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(markNext);
    observer.observe(grid);
    void tick().then(markNext);
    return () => observer.disconnect();
  });
</script>

{#snippet stageName(stage: WorkflowStage)}
  {@const name = splitLastWord(stage.name)}
  <!-- The name is a button and looks like one: a hover fill, a pencil after its last word, and the tooltip saying what it
       does. -->
  <Tooltip.Root>
    <Tooltip.Trigger>
      {#snippet child({ props })}
        <button
          {...props}
          type="button"
          data-stage-open={stage.id}
          aria-haspopup="dialog"
          class="-mx-1 min-w-0 rounded-lg px-1 py-1 text-left font-semibold break-words hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring pointer-coarse:-my-2 pointer-coarse:py-3"
          onclick={() => onopen(stage.id)}
        >
          {name.head}<span class="inline-flex max-w-full items-start gap-1 align-top"><span class="min-w-0">{name.last}</span><PencilIcon aria-hidden="true" class="mt-0.75 size-3.5 shrink-0 text-muted-foreground" /></span>
        </button>
      {/snippet}
    </Tooltip.Trigger>
    <Tooltip.Content>Edit {stage.name}</Tooltip.Content>
  </Tooltip.Root>
{/snippet}

<Panel title="Stages">
  <div class="flex flex-col gap-6">
    <FieldErrors id="stages-errors" messages={errorsAt(errors, 'stages')} />
    <div class="stage-row" style:--n={open.length} bind:this={grid}>
      <ReorderList items={open} key={(stage) => stage.id} label="Stages" itemLabel={(stage) => stage.name} orientation="horizontal" onreorder={reorder}>
        {#snippet item(stage, grip)}
          {@const number = open.indexOf(stage) + 1}
          {@const index = workflow.stages.indexOf(stage)}
          {@const stageErrors = errorsUnder(errors, `stages[${index}]`).map((error) => error.message)}
          {@const last = number === open.length}
          <div data-stage-card aria-label={`Stage ${number}: ${stage.name}`} role="group" class="relative h-full">
            {#if !last}
              <!-- The connector to the next stage, with its arrow: across the gap in a row, down it when the cards stack. -->
              <span aria-hidden="true" data-stage-line class="stage-connector text-muted-foreground">
                <span class="stage-connector-line bg-input"></span>
                <ChevronRightIcon class="stage-connector-across size-4 shrink-0" />
                <ChevronDownIcon class="stage-connector-down size-4 shrink-0" />
              </span>
            {/if}
            <div
              class={[
                'relative flex h-full flex-col gap-2 rounded-panel border bg-card p-3 shadow-surface',
                stageErrors.length > 0 ? 'border-status-verify' : 'border-border',
              ]}
            >
              <!-- One row: the grip, the number and the name. The grip's box hangs in the card's padding, so its icon lines up
                   with the chips and its target stays 32px (44px on touch). A long name wraps; the grip and number stay on
                   its first line. -->
              <div data-stage-header class="flex items-start gap-1">
                <span class="stage-grip">{@render grip()}</span>
                <StepMarker>{number}</StepMarker>
                {@render stageName(stage)}
              </div>
              <StageChips
                chips={stageChips(workflow, stage, templateNames)}
                stageName={stage.name}
                {automationHref}
                {formHref}
                {onshow}
                {onshowform}
                onlimit={() => onopen(stage.id)}
                onscorecard={() => onscorecard(index)}
                onautomations={(ids) => onautomations(stage.id, ids)}
              />
              <FieldErrors id={`stage-${index}-errors`} messages={stageErrors} />
            </div>
            <!-- On the connector after the card: in the gap to its right in a row, below it when the cards stack, and on
                 the last card's bottom edge, where the next stage would go. -->
            <Tooltip.Root>
              <Tooltip.Trigger>
                {#snippet child({ props })}
                  <button
                    {...props}
                    type="button"
                    aria-label={`Add a stage after ${stage.name}`}
                    data-stage-add
                    class="stage-add group/add absolute z-10 grid size-6 place-items-center rounded-full focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring pointer-coarse:size-11"
                    onclick={() => addAfter(stage)}
                  >
                    <!-- The circle is smaller than its target, so it sits on the line and leaves the arrow showing. -->
                    <span
                      class="grid size-5 place-items-center rounded-full border border-input bg-card text-muted-foreground transition-colors duration-150 ease-in-out group-hover/add:border-foreground group-hover/add:bg-muted group-hover/add:text-foreground group-focus-visible/add:border-foreground group-focus-visible/add:text-foreground motion-reduce:transition-none pointer-coarse:size-6"
                    >
                      <PlusIcon aria-hidden="true" class="size-3.5 pointer-coarse:size-4" />
                    </span>
                  </button>
                {/snippet}
              </Tooltip.Trigger>
              <Tooltip.Content>Add a stage after {stage.name}</Tooltip.Content>
            </Tooltip.Root>
          </div>
        {/snippet}
      </ReorderList>
    </div>

    {#if anyStage.automations.length > 0}
      <section aria-labelledby="stage-cards-any" class="flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-border-subtle pt-4">
        <h3 id="stage-cards-any" class="shrink-0 text-heading">Any stage</h3>
        <StageChips chips={anyStage} stageName="any stage" onautomations={(ids) => onautomations(null, ids)} />
      </section>
    {/if}

    {#if ends.length > 0}
      <section aria-labelledby="stage-cards-ends" class="flex flex-col gap-2 border-t border-border-subtle pt-4">
        <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
          <h3 id="stage-cards-ends" class="shrink-0 text-heading">Ends</h3>
          <ul class="flex flex-wrap items-center gap-2">
            {#each ends as stage (stage.id)}
              {@const index = workflow.stages.indexOf(stage)}
              {@const chips = stageChips(workflow, stage, templateNames)}
              {@const failing = errorsUnder(errors, `stages[${index}]`).length > 0}
              <!-- An end is a quiet button: its name and what it sends, counted. It opens the stage's sheet, which holds
                   Remove stage and links to its automations on their tab. -->
              <li>
                <Button
                  variant="outline"
                  size="sm"
                  aria-haspopup="dialog"
                  data-stage-open={stage.id}
                  data-end-stage
                  aria-label={endLabel(stage.name, chips)}
                  class={['font-semibold tabular-nums', failing && 'border-status-verify']}
                  onclick={() => onopen(stage.id)}
                >
                  {stage.name}
                  {#if chips.automations.length > 0}<span class="inline-flex items-center gap-0.5 font-normal text-muted-foreground"><ZapIcon aria-hidden="true" class="text-primary" />{chips.automations.length}</span>{/if}
                  {#if chips.emails.length > 0}<span class="inline-flex items-center gap-0.5 font-normal text-muted-foreground"><MailIcon aria-hidden="true" />{chips.emails.length}</span>{/if}
                </Button>
              </li>
            {/each}
          </ul>
        </div>
        {#each ends as stage (stage.id)}
          {@const index = workflow.stages.indexOf(stage)}
          <FieldErrors id={`stage-${index}-errors`} messages={errorsUnder(errors, `stages[${index}]`).map((error) => error.message)} />
        {/each}
      </section>
    {/if}
  </div>
</Panel>

<style>
  /* The size container the cards lay out against (F2). Every open stage gets an equal share of the row while each share
     is at least --min; past that they wrap into rows of equal columns at --min, never one stretched last row. The row
     stops at --max a stage, so a short pipeline on a wide screen does not stretch its cards thin and long. Like the Stage
     flow, the choice is CSS (container units against the stage count), so the first paint is right. The share loses 1px
     so rounding never drops the last column into a row of its own. */
  .stage-row {
    container-type: inline-size;
    /* The header's sizes, which the connector and the plus line up with: the grip's box, how far it hangs past each side
       of its place, and the centre of the number from the card's corner (its border and padding, then half the marker). */
    --grip: 2rem;
    --grip-hang: 0.5rem;
    --marker-y: calc(1px + 0.75rem + 0.875rem);
    --marker-x: calc(1px + 0.75rem + var(--grip) - 2 * var(--grip-hang) + 0.25rem + 0.875rem);
  }

  .stage-row > :global(ol) {
    --gap: 2.25rem;
    --row-gap: 1.75rem;
    --min: 10.75rem;
    --max: 32rem;
    --share: min(var(--max), calc((100cqw - (var(--n) - 1) * var(--gap)) / var(--n) - 1px));
    display: grid;
    gap: var(--row-gap) var(--gap);
    grid-template-columns: repeat(auto-fill, minmax(max(var(--min), var(--share)), 1fr));
    max-width: calc(var(--n) * var(--max) + (var(--n) - 1) * var(--gap));
  }

  /* As tall as the number, so a grip 32px or 44px high does not make the header taller. */
  .stage-grip {
    display: inline-flex;
    height: 1.75rem;
    align-items: center;
    margin-inline: calc(-1 * var(--grip-hang));
  }

  /* The connector: a hairline through the markers with an arrow at its end. */
  .stage-row :global(.stage-connector) {
    position: absolute;
    display: none;
    align-items: center;
    pointer-events: none;
  }

  .stage-row :global(.stage-connector-line) {
    flex: 1;
  }

  .stage-row :global([data-next='right'] > .stage-connector) {
    display: flex;
    top: var(--marker-y);
    left: 100%;
    width: var(--gap);
    height: 1rem;
    transform: translateY(-50%);
  }

  .stage-row :global([data-next='right'] > .stage-connector .stage-connector-line) {
    height: 1px;
  }

  .stage-row :global([data-next='down'] > .stage-connector) {
    display: flex;
    flex-direction: column;
    top: 100%;
    left: var(--marker-x);
    height: var(--row-gap);
    width: 1rem;
    transform: translateX(-50%);
  }

  .stage-row :global([data-next='down'] > .stage-connector .stage-connector-line) {
    width: 1px;
  }

  .stage-row :global([data-next='right'] > .stage-connector .stage-connector-down),
  .stage-row :global([data-next='down'] > .stage-connector .stage-connector-across) {
    display: none;
  }

  /* The plus sits on the connector's line, before the arrow, whose icon takes the last 1rem of the gap. */
  .stage-row :global(.stage-add) {
    display: none;
  }

  .stage-row :global([data-next='right'] > .stage-add) {
    display: grid;
    top: var(--marker-y);
    left: calc(100% + (var(--gap) - 0.75rem) / 2);
    transform: translate(-50%, -50%);
  }

  /* The last card, and the last card of a row that wraps, have no connector: the plus sits on the bottom edge. */
  .stage-row :global([data-next='end'] > .stage-add),
  .stage-row :global([data-next='none'] > .stage-add) {
    display: grid;
    top: 100%;
    left: 50%;
    transform: translate(-50%, -50%);
  }

  .stage-row :global([data-next='down'] > .stage-add) {
    display: grid;
    top: calc(100% + var(--row-gap) / 2);
    left: 50%;
    transform: translate(-50%, -50%);
  }

  @media (pointer: coarse) {
    .stage-row {
      --grip: 2.75rem;
      --grip-hang: 0.75rem;
    }

    .stage-row > :global(ol) {
      --gap: 3.5rem;
      --row-gap: 3rem;
      --min: 12rem;
    }

    /* Lower than the edge, so its 44px target stays off the chips above it. */
    .stage-row :global([data-next='end'] > .stage-add),
    .stage-row :global([data-next='none'] > .stage-add) {
      top: calc(100% + 0.625rem);
    }
  }
</style>
