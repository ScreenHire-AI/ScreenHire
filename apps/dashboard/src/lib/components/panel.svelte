<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { HTMLAttributes } from 'svelte/elements';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { cn } from '#lib/utils.js';

  // A section of a page (design system section 7, S3): the title inside a fixed-height header with an optional count
  // and actions, the body, and an optional footer inset. Panel titles are h2. A list's table panel holds its search and
  // filters in the header instead of a title; give it an `aria-label`.
  let {
    title,
    count,
    tools,
    actions,
    footer,
    children,
    flush = false,
    class: className,
    ...restProps
  }: HTMLAttributes<HTMLElement> & {
    title?: string;
    /** Shown as a count pill after the title (T4). */
    count?: number;
    /** A list's search and filters, on the header's left after any title. Containers query it as `@container/panel`. */
    tools?: Snippet;
    /** Controls on the header's right, 32px high (`size="default"`). */
    actions?: Snippet;
    footer?: Snippet;
    /** No body padding, for a table or a list that runs edge to edge. */
    flush?: boolean;
  } = $props();

  const id = $props.id();
</script>

<section
  aria-labelledby={title ? id : undefined}
  data-slot="panel"
  class={cn('flex min-w-0 flex-col rounded-panel border border-border bg-card', className)}
  {...restProps}
>
  <!-- The header height follows the screen size (section 5), not the panel's width, so panels in a row line up. -->
  <header class="@container/panel flex h-14 shrink-0 items-center gap-2 border-b border-border-subtle px-4.5 max-md:h-13 max-md:px-4">
    {#if title}
      <h2 {id} class="flex min-w-0 text-heading"><TruncatedText text={title} /></h2>
    {/if}
    {#if count !== undefined}
      <Badge variant="count">{count}</Badge>
    {/if}
    {#if tools}
      <div class="flex min-w-0 flex-1 items-center gap-2">{@render tools()}</div>
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
