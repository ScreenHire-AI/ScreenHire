<script lang="ts">
  import type { Snippet } from 'svelte';

  // One label and value in a `SummaryList`. A free-text answer can be long, so the value keeps the 72ch measure (S6).
  let { label, children }: { label: string; children: Snippet } = $props();
</script>

<div class="grid gap-1 border-b border-border-subtle py-3 first:pt-0 last:border-0 last:pb-0 @md:grid-cols-[minmax(8rem,12rem)_1fr] @md:gap-4">
  <dt class="text-muted-foreground">{label}</dt>
  <dd class="max-w-measure min-w-0 break-words">{@render children()}</dd>
</div>
