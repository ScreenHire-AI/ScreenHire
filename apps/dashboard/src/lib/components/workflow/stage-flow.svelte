<script lang="ts">
  import type { HiringWorkflow, WorkflowStage } from '@screenhire/api-client';
  import ClipboardListIcon from '~icons/lucide/clipboard-list';
  import ClockIcon from '~icons/lucide/clock';
  import ZapIcon from '~icons/lucide/zap';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { laneOf } from '#lib/workflow.js';
  import ScorecardName from './scorecard-name.svelte';

  // A workflow's stages in order (design system section 11, Stage flow): a row of columns where every stage fits, and the
  // vertical list where they do not, so 5 stages and 20 stages both read (S9). Each stage is its name, then what is
  // attached to it as short facts, in one order: scorecard, time limit, automations. A stage with nothing attached shows
  // only its name. The stages that end a candidacy are not here; the page lists them in the panel's footer.
  let {
    stages,
    automations,
    label = 'Stages in order',
  }: {
    stages: readonly WorkflowStage[];
    /** Every automation of the workflow: a stage counts the ones that fire on it. */
    automations: HiringWorkflow['automations'];
    label?: string;
  } = $props();

  // One style for every fact: its icon, then a short muted line that wraps rather than cuts. The line can shrink below
  // its longest word (min-w-0), or a long unbroken name would run across the next column.
  const factClass = 'flex items-start gap-1.5 text-muted-foreground break-words';
  const iconClass = 'mt-0.5 shrink-0';
  const lineClass = 'min-w-0';

  // An automation that is off fires on nothing, so its stage does not count it.
  const automationsOn = (stage: WorkflowStage) => automations.filter((automation) => automation.enabled !== false && laneOf(automation) === stage.id).length;
</script>

<!-- The wrapper is the size container (F2). A container query's condition cannot read how many stages there are, and
     Tailwind's @container variants take a fixed width, so the choice is made with container query units against the
     width the stages need (--need, in the style block below): the same query, in a form that takes a variable. No
     script measures anything, so the first paint is right (S8). -->
<div class="stage-flow">
  <ol aria-label={label} style:--n={stages.length}>
    {#each stages as stage, index (stage.id)}
      {@const cards = automationsOn(stage)}
      <li class="border-border-subtle">
        <!-- The line down to the next stage, drawn only in the vertical list. -->
        {#if index < stages.length - 1}<span aria-hidden="true" class="line-down bg-border"></span>{/if}
        <StepMarker>{index + 1}</StepMarker>
        <span class="pt-1 font-semibold break-words">{stage.name}</span>
        {#if stage.scorecard || stage.scorecardTemplateId || stage.slaDays || cards > 0}
          <ul class="facts mt-1.5 flex flex-col gap-1">
            {#if stage.scorecard || stage.scorecardTemplateId}
              <li class={factClass}><ClipboardListIcon aria-hidden="true" class={iconClass} /><span class={lineClass}><span class="sr-only">Scorecard:</span>{' '}<ScorecardName {stage} /></span></li>
            {/if}
            {#if stage.slaDays}
              <li class={factClass}><ClockIcon aria-hidden="true" class={iconClass} /><span class={[lineClass, 'tabular-nums']}>{stage.slaDays}-day limit</span></li>
            {/if}
            {#if cards > 0}
              <li class={factClass}><ZapIcon aria-hidden="true" class={iconClass} /><span class={lineClass}>{cards} {cards === 1 ? 'automation' : 'automations'}</span></li>
            {/if}
          </ul>
        {/if}
      </li>
    {/each}
  </ol>
</div>

<style>
  .stage-flow {
    container-type: inline-size;
  }

  ol {
    /* The width that holds every stage as a column. --short is positive while it does not fit (a big number, so the
       clamps below behave as a switch) and negative while it does. A small multiplier would leave a sliver of widths
       just under --need where the track is only a part of the list, and the stages would wrap into rows of columns. */
    --column: 12rem;
    --need: calc(var(--n) * var(--column));
    --short: calc((var(--need) - 100cqw) * 100000);
    display: grid;
    /* A column when they all fit; otherwise one track as wide as the list, which stacks them. */
    grid-template-columns: repeat(auto-fit, minmax(min(100%, max(var(--column), calc(var(--short) + var(--column)))), 1fr));
  }

  ol > li {
    position: relative;
    display: grid;
    grid-template-columns: 1.75rem minmax(0, 1fr);
    column-gap: 0.75rem;
    align-content: start;
    /* Space under a stage for the line down; none when the stages sit in a row. */
    padding-bottom: clamp(0px, var(--short), 1.25rem);
  }

  ol > li:last-child {
    padding-bottom: 0;
  }

  /* Side by side, a hairline parts the columns, with room on both sides of it. */
  ol > li:not(:first-child) {
    padding-left: clamp(0px, calc(var(--short) * -1), 1rem);
    border-left-style: solid;
    border-left-width: clamp(0px, calc(var(--short) * -1), 1px);
  }

  ol > li:not(:last-child) {
    padding-right: clamp(0px, calc(var(--short) * -1), 1rem);
  }

  .line-down {
    position: absolute;
    top: 1.75rem;
    bottom: 0;
    left: 0.875rem;
    width: clamp(0px, var(--short), 1px);
  }

  .facts {
    grid-column-start: 2;
  }
</style>
