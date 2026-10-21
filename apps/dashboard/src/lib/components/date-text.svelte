<script lang="ts">
  import Hint from '#lib/components/hint.svelte';
  import { formatDate, formatDateTime } from '#lib/dates.js';
  import { cn } from '#lib/utils.js';

  // A date in a table or a list item: the date, so a column lines up, and the exact time in a tooltip that opens on
  // hover, focus or tap (design system section 13, T5). Text and headers use RelativeTime instead.
  let { at, class: className }: { at: string; class?: string } = $props();
</script>

<Hint content={formatDateTime(at)}>
  {#snippet children({ props })}<time {...props} datetime={at} class={cn('tabular-nums', className)}>{formatDate(at)}</time>{/snippet}
</Hint>
