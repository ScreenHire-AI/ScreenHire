<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { HTMLAttributes } from 'svelte/elements';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { cn } from '#lib/utils.js';

  // A section of a page (design system section 7, S3): the title inside a fixed-height header with an optional count
  // and actions, the body, and an optional footer inset. Panel titles are h2.
  let {
    title,
    count,
    actions,
    footer,
    children,
    flush = false,
    class: className,
    ...restProps
  }: HTMLAttributes<HTMLElement> & {
    title: string;
    /** Shown as a count pill after the title (T4). */
    count?: number;
    /** Controls on the header's right, 32px high (`size="sm"`). */
    actions?: Snippet;
    footer?: Snippet;
    /** No body padding, for a table or a list that runs edge to edge. */
    flush?: boolean;
  } = $props();

  const id = $props.id();
</script>

<section aria-labelledby={id} data-slot="panel" class={cn('flex min-w-0 flex-col rounded-panel border border-border bg-card', className)} {...restProps}>
  <!-- The header height follows the screen size (section 5), not the panel's width, so panels in a row line up. -->
  <header class="flex h-14 shrink-0 items-center gap-2 border-b border-border-subtle px-4.5 max-md:h-13 max-md:px-4">
    <h2 {id} class="flex min-w-0 text-heading"><TruncatedText text={title} /></h2>
    {#if count !== undefined}
      <Badge variant="count">{count}</Badge>
    {/if}
    {#if actions}
      <div class="ml-auto flex shrink-0 items-center gap-2">{@render actions()}</div>
    {/if}
  </header>
  {#if children}
    <div class={cn('min-w-0 flex-1', !flush && 'px-4.5 py-4 max-md:px-4')}>{@render children()}</div>
  {/if}
  {#if footer}
    <div class="rounded-b-panel border-t border-border-subtle bg-muted px-4.5 py-3 max-md:px-4">{@render footer()}</div>
  {/if}
</section>
