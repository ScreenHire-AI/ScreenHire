<script lang="ts" generics="T">
  import type { Snippet } from 'svelte';
  import StepMarker from './step-marker.svelte';

  // Steps down a line (design system section 11): numbered square markers joined by a line, each step's content beside
  // its marker. For a sequence with no progress, such as a workflow's stages; `StepMarkers` draws progress across.
  let { items, label, key, children }: { items: readonly T[]; label: string; key: (item: T) => string; children: Snippet<[T]> } = $props();
</script>

<ol aria-label={label} class="flex flex-col">
  {#each items as item, index (key(item))}
    <li class="relative flex gap-3 pb-5 last:pb-0">
      {#if index < items.length - 1}
        <span aria-hidden="true" class="absolute top-7 bottom-0 left-3.5 w-px bg-border"></span>
      {/if}
      <StepMarker>{index + 1}</StepMarker>
      <div class="flex min-w-0 flex-col gap-1.5 pt-1">{@render children(item)}</div>
    </li>
  {/each}
</ol>
