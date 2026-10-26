<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A summary band (design system section 6): an inset across the top of a panel, full width with no edge of its own.
  // Summary cards, metric cards, the next step and notices sit white on it; a flush panel holds it edge to edge.
  let { children, class: className }: { children: Snippet; class?: string } = $props();
</script>

<div data-slot="summary-band" class={cn('grid grid-cols-[repeat(auto-fit,minmax(min(100%,15rem),1fr))] gap-3 bg-secondary px-4.5 py-4 max-md:px-4', className)}>
  {@render children()}
</div>
