<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A detail page's columns (design system section 7): from `lg` the tabs' content takes the room the 340px details rail
  // leaves, 20px apart, and the rail stays on the right on every tab. Below `lg` the rail folds under the header, before
  // the content, so the order on screen and the reading order agree at every size.
  let { rail, children, class: className }: { rail: Snippet; children: Snippet; class?: string } = $props();
</script>

<div class={cn('flex flex-col gap-5 lg:grid lg:grid-cols-[minmax(0,1fr)_var(--container-rail)] lg:items-start', className)}>
  <div class="min-w-0 lg:col-start-2 lg:row-start-1">{@render rail()}</div>
  <div class="flex min-w-0 flex-col gap-5 lg:col-start-1 lg:row-start-1">{@render children()}</div>
</div>
