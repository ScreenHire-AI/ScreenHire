<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import type { Snippet } from 'svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Card from '#lib/components/ui/card/index.js';
  import { cn } from '#lib/utils.js';

  // A summary card (design system section 11): shadcn's Card as it is, on a `SummaryBand`. An icon in a neutral 32px chip
  // and the title; what it counts as a badge in `Card.Action` ("2 signals"); a description of two lines at most. The icon
  // names the thing, never a status. With `href` the card is a link to what it counts (A5).
  let {
    icon: Icon,
    title,
    count,
    badge,
    description,
    href,
    class: className,
  }: {
    icon: typeof LucideIcon;
    title: string;
    /** What the card counts, in words ("2 signals"), in the count badge. */
    count?: string;
    /** In place of `count`: a badge of its own, such as the Verification tab's solid one for a card that fed the level. */
    badge?: Snippet;
    description: string;
    href?: string;
    class?: string;
  } = $props();
</script>

{#snippet card()}
  <Card.Root class={cn('h-full', href && 'group-hover/summary:border-input', className)}>
    <Card.Header class="gap-1.5">
      <Card.Title class="flex min-w-0 items-center gap-2.5">
        <span class="grid size-8 shrink-0 place-items-center rounded-lg bg-secondary text-sidebar-foreground"><Icon aria-hidden="true" /></span>
        <TruncatedText text={title} />
      </Card.Title>
      {#if badge || count}
        <!-- In a narrow card the badge or change drops under the title, so the title keeps its room. -->
        <Card.Action class="row-span-1 self-center @max-3xs/card-header:col-start-1 @max-3xs/card-header:row-start-2 @max-3xs/card-header:justify-self-start">
          {#if badge}{@render badge()}{:else}<Badge variant="count">{count}</Badge>{/if}
        </Card.Action>
      {/if}
      <!-- Two lines at most, the rest in a tooltip (F5). -->
      <Card.Description class="col-span-full min-w-0"><TruncatedText text={description} lines={2} /></Card.Description>
    </Card.Header>
  </Card.Root>
{/snippet}

{#if href}
  <a {href} class="group/summary block rounded-panel text-foreground no-underline">{@render card()}</a>
{:else}
  {@render card()}
{/if}
