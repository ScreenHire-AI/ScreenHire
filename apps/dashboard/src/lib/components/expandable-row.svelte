<script lang="ts">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import type { Snippet } from 'svelte';
  import { cubicInOut } from 'svelte/easing';
  import { prefersReducedMotion } from 'svelte/motion';
  import { slide } from 'svelte/transition';
  import { cn } from '#lib/utils.js';

  // A row of a list that opens in place to show more (design system section 11, Expandable row; A9): the whole row is the
  // button, with `aria-expanded`, a hover background, and a chevron beside its words that turns up when it is open, so the row
  // looks like it opens before it is hovered (A13); what it shows follows the row. Its summary
  // wraps rather than truncating, because a tooltip has no place inside a button. A list of these is one surface's rows,
  // with the rows' dividers, never a card per row (S4). What it shows slides open and shut in 180ms, ease-in-out, and
  // appears at once with reduced motion (A17). Its top padding is the gap under the row, so the slide carries that too.
  let {
    expanded = $bindable(false),
    id,
    icon,
    summary,
    trailing,
    children,
  }: {
    expanded?: boolean;
    /** The id of what the row shows, for `aria-controls`. */
    id: string;
    /** A small round icon before the summary. */
    icon?: Snippet;
    /** The row's words: what it is, then a caption line. */
    summary: Snippet;
    /** Something after the summary, before the chevron, such as a badge. */
    trailing?: Snippet;
    /** What the row shows when it is open. */
    children: Snippet;
  } = $props();
</script>

<button
  type="button"
  aria-expanded={expanded}
  aria-controls={expanded ? id : undefined}
  class="-mx-2 flex min-h-11 items-center gap-3 rounded-lg px-2 py-1.5 text-left hover:bg-secondary focus-visible:outline-2 focus-visible:outline-ring"
  onclick={() => (expanded = !expanded)}
>
  {#if icon}
    <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground">{@render icon()}</span>
  {/if}
  <span class="flex min-w-0 flex-1 items-center gap-1.5">
    <span class="flex min-w-0 flex-col">{@render summary()}</span>
    <ChevronDownIcon
      aria-hidden="true"
      class={cn('size-4 shrink-0 self-start mt-0.5 text-muted-foreground transition-transform duration-180 ease-in-out motion-reduce:transition-none', expanded && 'rotate-180')}
    />
  </span>
  {@render trailing?.()}
</button>
{#if expanded}
  <div {id} transition:slide={{ duration: prefersReducedMotion.current ? 0 : 180, easing: cubicInOut }} class="flex flex-col gap-2 pt-3">
    {@render children()}
  </div>
{/if}
