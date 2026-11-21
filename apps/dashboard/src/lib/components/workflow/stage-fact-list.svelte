<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import CalendarIcon from '~icons/lucide/calendar';
  import ClipboardListIcon from '~icons/lucide/clipboard-list';
  import ClockIcon from '~icons/lucide/clock';
  import ListIcon from '~icons/lucide/list';
  import MailIcon from '~icons/lucide/mail';
  import ZapIcon from '~icons/lucide/zap';
  import type { StageFact, StageFactKind } from '#lib/stage-facts.js';
  import { isTerminal } from '#lib/workflow.js';
  import ScorecardName from './scorecard-name.svelte';

  // A stage's short facts (design system section 11, Stage cards and Stage flow): one fact a line, each its icon and a
  // muted line that wraps rather than cuts. Where the workflow can be edited, an automation's line is a link to it on the
  // Automations tab and the question count a link to Questions (A5); where it only reads, they are text. The scorecard is
  // its name, with the word Scorecard for screen readers only (W9).
  let {
    facts,
    stage,
    automationHref,
    questionsHref,
    onshow,
  }: {
    facts: readonly StageFact[];
    /** The stage, for its scorecard line; left out for a row that is not one stage's. */
    stage?: WorkflowStage;
    /** Where an automation's line goes; left out where the workflow only reads. */
    automationHref?: (automationId: string) => string;
    questionsHref?: string;
    /** Opens an automation in the page instead of following its link. */
    onshow?: (automationId: string) => void;
  } = $props();

  const icons: Record<StageFactKind, typeof ZapIcon> = { limit: ClockIcon, meeting: CalendarIcon, email: MailIcon, automation: ZapIcon, questions: ListIcon };
  // A stage that ends the candidacy has no scorecard to give, whatever it holds.
  const scorecard = $derived(Boolean(stage && !isTerminal(stage.kind) && (stage.scorecard || stage.scorecardTemplateId)));
  // Links are underlined text (A5): a light rule at rest that darkens on hover, so the muted lines stay calm.
  const linkClass = 'underline decoration-border underline-offset-2 hover:decoration-current focus-visible:decoration-current pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center';
</script>

{#if facts.length > 0 || scorecard}
  <ul class="flex flex-col gap-1">
    {#each facts as fact, index (index)}
      {@const Icon = icons[fact.kind]}
      {@const href = fact.automationId && automationHref ? automationHref(fact.automationId) : fact.kind === 'questions' ? questionsHref : undefined}
      <li class="flex items-start gap-1.5 break-words text-muted-foreground" data-fact={fact.kind}>
        <Icon aria-hidden="true" class={['mt-0.5 shrink-0', fact.kind === 'automation' && 'text-primary']} />
        {#if href}
          <a
            {href}
            class={['min-w-0 text-foreground', linkClass]}
            onclick={(event) => {
              if (!fact.automationId || !onshow || event.metaKey || event.ctrlKey || event.shiftKey) return;
              event.preventDefault();
              onshow(fact.automationId);
            }}>{fact.text}</a
          >
        {:else}
          <span class={['min-w-0', fact.kind === 'limit' && 'tabular-nums']}>{fact.text}</span>
        {/if}
      </li>
    {/each}
    {#if scorecard && stage}
      <li class="flex items-start gap-1.5 break-words text-muted-foreground" data-fact="scorecard">
        <ClipboardListIcon aria-hidden="true" class="mt-0.5 shrink-0" /><span class="min-w-0"><span class="sr-only">Scorecard:</span>{' '}<ScorecardName {stage} /></span>
      </li>
    {/if}
  </ul>
{/if}
