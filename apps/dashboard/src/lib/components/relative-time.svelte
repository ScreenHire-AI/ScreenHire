<script lang="ts">
  import Hint from '#lib/components/hint.svelte';
  import { clock } from '#lib/clock.svelte.js';
  import { formatDateTime, timeAgo } from '#lib/dates.js';
  import { cn } from '#lib/utils.js';

  // Time ago in text, with the exact date and time on hover, focus or tap (design system section 13, T5).
  let { at, class: className }: { at: string; class?: string } = $props();
</script>

<Hint content={formatDateTime(at)}>
  {#snippet children({ props })}
    <time {...props} datetime={at} class={cn('w-fit', className)}>{timeAgo(at, clock.now)}</time>
  {/snippet}
</Hint>
