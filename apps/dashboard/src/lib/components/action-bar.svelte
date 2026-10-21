<script lang="ts">
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A phone's primary action, in a bar fixed to the bottom of the screen (design system section 8). It sits under the
  // overlay layer (z-50), so menus and dialogs opened from it cover it; the page leaves room for it with `pb-28`.
  let { children, class: className }: { children: Snippet; class?: string } = $props();
</script>

<div
  data-slot="action-bar"
  class={cn(
    'fixed inset-x-0 bottom-0 z-40 flex border-t border-border bg-card px-4 pt-3 pb-[max(--spacing(3),env(safe-area-inset-bottom))] *:flex-1 [&_svg:not([class*=size-])]:size-5!',
    className,
  )}
>
  {@render children()}
</div>
