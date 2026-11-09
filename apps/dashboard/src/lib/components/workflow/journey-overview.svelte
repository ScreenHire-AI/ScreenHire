<script lang="ts">
  import type { HiringWorkflow } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import ZapIcon from '~icons/lucide/zap';
  import Panel from '#lib/components/panel.svelte';
  import StepMarker from '#lib/components/step-marker.svelte';
  import { journeyOf } from '#lib/journey-text.js';
  import { emailTemplateNames } from '#lib/workflow.js';
  import ScorecardName from './scorecard-name.svelte';

  // The Overview tab (design system section 11, Journey): the workflow read back as what happens to an applicant, a
  // column per stage with its time limit, scorecard and the automations that run there, then the end stages on one line.
  // Only automations carry an icon. A stage's header is the one button that opens its sheet (L3), when the page can
  // edit; a workflow that only reads shows the same words as text. Where the columns do not fit, or there are more than
  // five stages, it is the vertical list, as Stage flow is (S9, F2).
  let {
    workflow,
    onopen,
    questionsHref,
  }: {
    workflow: HiringWorkflow;
    /** Opens the stage's sheet. Left out where the workflow cannot be edited. */
    onopen?: (stageId: string) => void;
    /** Where the apply form's questions are edited or read, for the line that counts them. */
    questionsHref?: string;
  } = $props();

  // The organization's template names, for "Sends the ... email".
  const emailTemplates = createListEmailTemplates();
  const templateNames = $derived(emailTemplateNames(emailTemplates.data?.data.templates));
  const journey = $derived(journeyOf(workflow, templateNames));
  // More stages than this read better as a list than as a row of narrow columns.
  const MAX_COLUMNS = 5;
  const columns = $derived(journey.columns.length > MAX_COLUMNS ? 100 : journey.columns.length);
</script>

{#snippet automationLine(text: string)}
  <li class="flex items-start gap-1.5 break-words">
    <ZapIcon aria-hidden="true" class="mt-0.5 shrink-0 text-primary" />
    <span class="min-w-0">{text}</span>
  </li>
{/snippet}

<Panel title="Candidate journey">
  <div class="flex flex-col gap-5">
    <!-- The wrapper is the size container (F2); the choice between columns and a list is made in CSS with container
         query units, as in Stage flow, so the first paint is right. -->
    <div class="journey">
      <ol aria-label="Stages in order" style:--n={columns}>
        {#each journey.columns as column, index (column.stage.id)}
          <li class="border-border-subtle">
            {#if index < journey.columns.length - 1}<span aria-hidden="true" class="line-down bg-border"></span>{/if}
            <StepMarker>{column.number}</StepMarker>
            {#if onopen}
              <button
                type="button"
                data-stage-open={column.stage.id}
                aria-haspopup="dialog"
                class="min-w-0 rounded-lg px-2 py-1 text-left pointer-coarse:py-2.5 font-semibold break-words hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                onclick={() => onopen(column.stage.id)}
              >
                {column.stage.name}
              </button>
            {:else}
              <span class="px-2 py-1 font-semibold break-words">{column.stage.name}</span>
            {/if}
            {#if column.questions || column.limit || column.stage.scorecard || column.stage.scorecardTemplateId || column.automations.length > 0}
              <ul class="facts mt-1.5 flex flex-col gap-1.5 px-2">
                {#if column.questions}
                  <li class="break-words">
                    {#if questionsHref}
                      <a href={questionsHref} class="underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">Asks {column.questions} {column.questions === 1 ? 'question' : 'questions'}</a>
                    {:else}
                      Asks {column.questions} {column.questions === 1 ? 'question' : 'questions'}
                    {/if}
                  </li>
                {/if}
                {#if column.limit}<li class="text-muted-foreground tabular-nums">{column.limit}</li>{/if}
                {#if column.stage.scorecard || column.stage.scorecardTemplateId}
                  <li class="break-words text-muted-foreground"><span class="sr-only">Scorecard:</span>{' '}<ScorecardName stage={column.stage} /></li>
                {/if}
                {#each column.automations as text, line (line)}{@render automationLine(text)}{/each}
              </ul>
            {/if}
          </li>
        {/each}
      </ol>
    </div>

    {#if journey.anytime.length > 0}
      <div class="flex flex-col gap-1.5">
        <h3 class="text-heading">Any time</h3>
        <ul class="flex flex-col gap-1.5">
          {#each journey.anytime as text, line (line)}{@render automationLine(text)}{/each}
        </ul>
      </div>
    {/if}

    {#if journey.ends.length > 0}
      <div class="flex flex-wrap items-baseline gap-x-6 gap-y-1.5 border-t border-border-subtle pt-4">
        <h3 class="text-heading">Ends</h3>
        <ul class="flex flex-wrap gap-x-6 gap-y-1.5">
          {#each journey.ends as text, line (line)}<li class="break-words">{text}</li>{/each}
        </ul>
      </div>
    {/if}
  </div>
</Panel>

<style>
  .journey {
    container-type: inline-size;
  }

  ol {
    /* The width that holds every stage as a column: --short is positive while it does not fit (a big number, so the
       clamps below behave as a switch) and negative while it does. */
    --column: 13rem;
    --need: calc(var(--n) * var(--column));
    --short: calc((var(--need) - 100cqw) * 100000);
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(min(100%, max(var(--column), calc(var(--short) + var(--column)))), 1fr));
  }

  ol > li {
    position: relative;
    display: grid;
    grid-template-columns: 1.75rem minmax(0, 1fr);
    column-gap: 0.5rem;
    align-content: start;
    align-items: center;
    padding-bottom: clamp(0px, var(--short), 1.25rem);
  }

  ol > li:last-child {
    padding-bottom: 0;
  }

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
    align-self: start;
  }
</style>
