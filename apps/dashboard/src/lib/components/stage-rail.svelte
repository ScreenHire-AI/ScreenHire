<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import type { Tracker } from '#lib/pipeline-tracker.js';

  // The stage rail (design system section 11): a candidate's place in the job's workflow as a line of square markers, left to
  // right, under their rating and tags. Stages they passed have a filled marker with a check, the stage they are in has a
  // larger marker and its name in semibold with "Here 3 days" under it (and the Overdue badge once its time limit passed),
  // later stages are plain. Only that stage says anything under its name; no date is drawn, because the meta line says when
  // they applied (W7). Once they left, the stage they left from keeps its marker and says how and when, dark. Screen readers
  // hear "Screening, step 2 of 6". In a narrow container only that stage shows, with "2 of 6". When no stage can be marked, a
  // line under the rail says where they stand. `tracker` comes from `trackerFor`.
  let { tracker, overdueAt }: { tracker: Tracker; /** When the current stage's time limit passes; the Overdue badge shows beside "Here 3 days" once it has. */ overdueAt?: string | null } =
    $props();
</script>

<ol aria-label="Pipeline" data-stage-rail class="@container grid min-w-0 auto-cols-fr grid-flow-col">
  {#each tracker.steps as step, index (step.id)}
    {@const here = step.state === 'current' || step.state === 'left'}
    {@const passed = step.state === 'passed'}
    <li
      aria-current={step.state === 'current' ? 'step' : undefined}
      data-state={step.state}
      class={['group flex min-w-0 flex-col gap-1 pr-3', !here && '@max-xl:sr-only']}
    >
      <span class="relative flex h-5 items-center">
        <span aria-hidden="true" class="absolute inset-x-0 top-1/2 h-0.5 -translate-y-1/2 bg-border group-last:hidden">
          <span class={['block h-full bg-primary', passed ? 'w-full' : here ? 'w-4' : 'w-0']}></span>
        </span>
        <span
          aria-hidden="true"
          class={[
            'relative grid shrink-0 place-items-center',
            passed && 'size-3.5 rounded-xs bg-primary text-primary-foreground',
            step.state === 'current' && 'size-4.5 rounded-sm bg-card ring-2 ring-primary',
            step.state === 'left' && 'size-4.5 rounded-sm bg-card ring-2 ring-foreground',
            step.state === 'upcoming' && 'size-3.5 rounded-xs border border-input bg-card',
          ]}
        >
          {#if passed}<CheckIcon class="size-2.5" />{/if}
          {#if here}<span class={['size-2 rounded-xs', step.state === 'left' ? 'bg-foreground' : 'bg-primary']}></span>{/if}
        </span>
      </span>
      <span class={['flex min-w-0 items-center gap-1.5', here ? 'font-semibold text-foreground' : passed ? 'font-medium' : 'text-muted-foreground']}>
        <span class="flex min-w-0"><TruncatedText text={step.name} /><span class="sr-only">, step {index + 1} of {tracker.total}</span></span>
        {#if here}<span aria-hidden="true" class="ml-auto shrink-0 text-caption font-normal text-muted-foreground @xl:hidden">{index + 1} of {tracker.total}</span>{/if}
      </span>
      {#if step.note || (step.state === 'current' && overdueAt)}
        <span class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-caption text-muted-foreground">
          {#if step.note}<span class="min-w-0 truncate">{step.note}</span>{/if}
          {#if step.state === 'current'}<OverdueBadge dueAt={overdueAt} />{/if}
        </span>
      {/if}
    </li>
  {/each}
</ol>
{#if tracker.standing}
  <p class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-caption text-muted-foreground">
    <span class="min-w-0">{tracker.standing}</span>
    {#if !tracker.outcome}<OverdueBadge dueAt={overdueAt} />{/if}
  </p>
{/if}
