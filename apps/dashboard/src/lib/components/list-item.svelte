<script lang="ts">
  import type { Snippet } from 'svelte';

  // A table row on a phone (design system section 8): the status first, then the name, which links the whole item; facts
  // as pills on the second line. Place it in a `<ul>`.
  let {
    href,
    title,
    status,
    facts,
    meta,
  }: {
    href: string;
    title: string;
    /** A status badge, first in the row (T2). */
    status?: Snippet;
    /** Fact pills. */
    facts?: Snippet;
    /** One short value on the right, such as a date. It stays clickable above the row's link. */
    meta?: Snippet;
  } = $props();
</script>

<li data-slot="list-item" class="relative flex min-w-0 flex-col gap-1.5 border-b border-border-subtle px-4 py-3 last:border-0 hover:bg-muted">
  <div class="flex min-w-0 items-center gap-2">
    {@render status?.()}
    <a {href} class="min-w-0 font-semibold break-words text-foreground no-underline after:absolute after:inset-0 hover:underline">{title}</a>
    {#if meta}
      <span class="relative ml-auto shrink-0 text-caption text-muted-foreground">{@render meta()}</span>
    {/if}
  </div>
  {#if facts}
    <div class="flex flex-wrap gap-1.5">{@render facts()}</div>
  {/if}
</li>
