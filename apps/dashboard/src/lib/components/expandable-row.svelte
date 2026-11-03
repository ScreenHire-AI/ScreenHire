<script lang="ts">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import type { Snippet } from 'svelte';
  import { cn } from '#lib/utils.js';

  // A row of a list that opens in place to show more (design system section 11, Expandable row): the whole row is the
  // button, with `aria-expanded` and a chevron that turns up when it is open; what it shows follows the row. Its summary
  // wraps rather than truncating, because a tooltip has no place inside a button. A list of these is one surface's rows,
  // with the rows' dividers, never a card per row (S4).
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
  <span class="flex min-w-0 flex-1 flex-col">{@render summary()}</span>
  {@render trailing?.()}
  <ChevronDownIcon aria-hidden="true" class={cn('size-4 shrink-0 text-muted-foreground transition-transform', expanded && 'rotate-180')} />
</button>
{#if expanded}
  <div {id} class="flex flex-col gap-2">{@render children()}</div>
{/if}
