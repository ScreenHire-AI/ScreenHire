<script lang="ts">
  import Hint from '#lib/components/hint.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime } from '#lib/dates.js';
  import { isOverdue, overdueLabel } from '#lib/sla.js';
  import { cn } from '#lib/utils.js';

  // The Overdue status (design system section 3.2): the review badge, by how much, and when the time limit passed in a
  // tooltip that opens on hover, focus or tap (T5, F4). Nothing while the application is within its stage's time limit.
  let { dueAt, class: className }: { dueAt: string | null | undefined; class?: string } = $props();
</script>

{#if dueAt && isOverdue({ slaDueAt: dueAt }, clock.now)}
  <Hint content={`Time limit passed ${formatDateTime(dueAt)}`}>
    {#snippet children({ props })}
      <span {...props} class={cn('inline-flex w-fit shrink-0 rounded-full', className)}>
        <StatusBadge status="overdue" label={overdueLabel(dueAt, clock.now)} />
      </span>
    {/snippet}
  </Hint>
{/if}
