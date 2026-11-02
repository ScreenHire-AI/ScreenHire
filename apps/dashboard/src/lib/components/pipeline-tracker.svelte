<script lang="ts">
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import type { Tracker } from '#lib/pipeline-tracker.js';

  // The pipeline tracker (design system section 11): a candidate's place in the job's workflow, left to right. Each stage
  // is a dot and its name. Stages they passed are filled, with the date they reached them; the current one is ringed, with
  // how long they have been there; later ones are empty; once they left, the stage they left from is dark, with how and
  // when. Dots, because the position is a fact about the person, not a step in a form (StepMarkers). Screen readers hear
  // "Screening, step 2 of 6". In a narrow container only that stage shows, with "2 of 6". `tracker` comes from `trackerFor`.
  let { tracker, overdueAt }: { tracker: Tracker; /** When the current stage's time limit passes; the Overdue badge shows beside "Here 3 days" once it has. */ overdueAt?: string | null } =
    $props();
</script>

<ol aria-label="Pipeline" class="@container flex min-w-0 items-start">
  {#each tracker.steps as step, index (step.id)}
    {@const last = index === tracker.steps.length - 1}
    {@const here = step.state === 'current' || step.state === 'left'}
    <li
      aria-current={step.state === 'current' ? 'step' : undefined}
      data-state={step.state}
      class={['flex min-w-0 flex-col gap-1.5', last ? 'max-w-full shrink-0 @xl:max-w-40' : 'flex-1', !here && '@max-xl:sr-only']}
    >
      <span class="flex h-4 items-center">
        <span
          aria-hidden="true"
          class={[
            'size-3 shrink-0 rounded-full border-2',
            step.state === 'passed' && 'border-primary bg-primary',
            step.state === 'current' && 'border-primary bg-card outline-2 outline-offset-2 outline-primary/40',
            step.state === 'left' && 'border-foreground bg-foreground',
            step.state === 'upcoming' && 'border-muted-foreground bg-card',
          ]}
        ></span>
        {#if !last}
          <span aria-hidden="true" class={['mx-2 h-px min-w-3 flex-1 @max-xl:hidden', step.state === 'passed' ? 'bg-primary' : 'bg-border']}></span>
        {/if}
      </span>
      <span class="flex min-w-0 flex-col gap-0.5 pr-2">
        <span class={['flex min-w-0 items-baseline gap-2', here ? 'font-semibold' : 'text-muted-foreground']}>
          <span class="flex min-w-0"><TruncatedText text={step.name} /><span class="sr-only">, step {index + 1} of {tracker.total}</span></span>
          {#if here}<span aria-hidden="true" class="shrink-0 text-caption font-normal text-muted-foreground @xl:hidden">{index + 1} of {tracker.total}</span>{/if}
        </span>
        {#if step.note || (step.state === 'current' && overdueAt)}
          <span class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-caption text-muted-foreground">
            {#if step.note}<span class="min-w-0 truncate">{step.note}</span>{/if}
            {#if step.state === 'current'}<OverdueBadge dueAt={overdueAt} />{/if}
          </span>
        {/if}
      </span>
    </li>
  {/each}
</ol>
