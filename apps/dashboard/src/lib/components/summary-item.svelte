<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';

  // One label and value in a `SummaryList`. A free-text answer can be long, so the value keeps the 72ch measure (S6).
  // `hideLabel` is for a value that explains itself: the label stays for screen readers (W9). A value that is a list says
  // how many it holds, in the count badge after the label (T4).
  let { label, count, hideLabel = false, children }: { label: string; count?: number; hideLabel?: boolean; children: Snippet } = $props();
</script>

<div class={['grid gap-1 border-b border-border-subtle py-3 first:pt-0 last:border-0 last:pb-0', !hideLabel && '@md:grid-cols-[minmax(8rem,12rem)_1fr] @md:gap-4']}>
  <dt class={hideLabel ? 'sr-only' : 'flex items-center gap-2 text-muted-foreground'}>{label}{#if count !== undefined}<Badge variant="count">{count}</Badge>{/if}</dt>
  <dd class="max-w-measure min-w-0 break-words">{@render children()}</dd>
</div>
