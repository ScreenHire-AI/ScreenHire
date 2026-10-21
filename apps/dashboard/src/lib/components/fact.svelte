<script lang="ts">
  import type { Snippet } from 'svelte';

  // One fact in a header band's facts row: the label above its value, a thin rule before it.
  let { label, children }: { label: string; children: Snippet } = $props();
</script>

<div
  data-slot="fact"
  class="relative flex min-w-0 flex-col gap-0.5 before:absolute before:inset-y-0.5 before:-left-4 before:w-px before:bg-border-subtle"
>
  <dt class="text-caption text-muted-foreground">{label}</dt>
  <dd class="min-w-0 text-body">{@render children()}</dd>
</div>
