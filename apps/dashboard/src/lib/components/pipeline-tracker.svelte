<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import type { Tracker } from '#lib/pipeline-tracker.js';

  // The pipeline tracker (design system section 11): a candidate's place in the job's workflow, a bar of stages left to
  // right, so nobody has to scan for where they stand. The stage they are in is a solid block in the brand colour, with how
  // long they have been there. Stages they passed are tinted, with a check and the date they reached them; later ones are
  // plain. Once they left, the stage they left from is solid and dark, with how and when. Screen readers hear "Screening,
  // step 2 of 6". In a narrow container only that stage shows, with "2 of 6". When no stage can be marked, a line under the
  // bar says where they stand, so a narrow container still shows it. `tracker` comes from `trackerFor`.
  let { tracker, overdueAt }: { tracker: Tracker; /** When the current stage's time limit passes; the Overdue badge shows beside "Here 3 days" once it has. */ overdueAt?: string | null } =
    $props();
</script>

<ol aria-label="Pipeline" class="@container flex min-w-0 overflow-hidden rounded-panel border border-border bg-card">
  {#each tracker.steps as step, index (step.id)}
    {@const here = step.state === 'current' || step.state === 'left'}
    <li
      aria-current={step.state === 'current' ? 'step' : undefined}
      data-state={step.state}
      class={[
        'flex min-h-14 min-w-0 flex-1 flex-col justify-center gap-0.5 border-r border-border px-3.5 py-2 last:border-r-0',
        step.state === 'passed' && 'bg-accent text-accent-foreground',
        step.state === 'current' && 'bg-primary text-primary-foreground',
        step.state === 'left' && 'bg-foreground text-background',
        step.state === 'upcoming' && 'text-muted-foreground',
        !here && '@max-xl:sr-only',
      ]}
    >
      <span class={['flex min-w-0 items-center gap-1.5', here && 'font-semibold']}>
        {#if step.state === 'passed'}<CheckIcon aria-hidden="true" class="size-3.5 shrink-0" />{/if}
        <span class="flex min-w-0"><TruncatedText text={step.name} /><span class="sr-only">, step {index + 1} of {tracker.total}</span></span>
        {#if here}<span aria-hidden="true" class="ml-auto shrink-0 text-caption font-normal @xl:hidden">{index + 1} of {tracker.total}</span>{/if}
      </span>
      {#if step.note || (step.state === 'current' && overdueAt)}
        <span class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-caption">
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
