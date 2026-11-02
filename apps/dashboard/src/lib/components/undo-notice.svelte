<script lang="ts">
  import Undo2Icon from '~icons/lucide/undo-2';
  import { Button } from '#lib/components/ui/button/index.js';
  import FailureText from '#lib/components/failure-text.svelte';
  import type { UndoOffer } from '#lib/undo.svelte.js';
  import { cn } from '#lib/utils.js';

  // The offer to undo a change that already happened, such as a deleted view (design system section 11, Undo notice): one
  // card at the bottom of the window for 10 seconds, with an outline Undo button. The wrapper is always in the page, so a
  // screen reader announces the sentence when it arrives. A second offer replaces the first.
  let { offer, class: className }: { offer: UndoOffer; class?: string } = $props();

  // The change was made from a menu that has just closed, so focus is nowhere (or in the sidebar). The Undo button takes it,
  // so the keyboard reaches Undo in time (WCAG 2.2.1); the clock waits while the pointer or focus is on the card.
  let undoButton = $state<HTMLButtonElement | null>(null);
  $effect(() => {
    if (!offer.message || !undoButton) return;
    const button = undoButton;
    const timer = setTimeout(() => {
      const active = document.activeElement;
      if (!active || active === document.body || active.closest('[data-slot="sidebar"]')) button.focus();
    }, 50);
    return () => clearTimeout(timer);
  });
</script>

<div
  role="status"
  aria-live="polite"
  class={cn('pointer-events-none fixed bottom-4 left-1/2 z-50 w-max max-w-[calc(100%-2rem)] -translate-x-1/2 max-md:bottom-24', className)}
>
  {#if offer.message}
    <div
      data-undo-notice
      role="group"
      aria-label="Undo"
      onpointerenter={() => offer.hold()}
      onpointerleave={() => offer.release()}
      onfocusin={() => offer.hold()}
      onfocusout={() => offer.release()}
      class="pointer-events-auto flex items-center gap-3 rounded-panel border border-border bg-card py-2 pr-2 pl-3 shadow-overlay">
      <p class="min-w-0 break-words">{offer.message}</p>
      <Button bind:ref={undoButton} variant="outline" size="lg" disabled={offer.working} onclick={() => void offer.run()}><Undo2Icon aria-hidden="true" />Undo</Button>
    </div>
  {:else if offer.failure}
    <div class="pointer-events-auto rounded-panel border border-border bg-card p-3 shadow-overlay">
      <FailureText message={offer.failure} role="status" />
    </div>
  {/if}
</div>
