<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A detail page's details rail (design system section 7, section 11): one surface with the record's details, in
  // `DetailsRailSection`s (for a candidate: details, files, tags). It is the same on every tab. `DetailLayout` places it.
  let { label = 'Details', children, class: className }: { label?: string; children: Snippet; class?: string } = $props();
</script>

<aside aria-label={label} data-slot="details-rail" class={cn('flex min-w-0 flex-col rounded-panel border border-border bg-card shadow-surface', className)}>
  {@render children()}
</aside>
