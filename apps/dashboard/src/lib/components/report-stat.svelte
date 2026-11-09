<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import ArrowDownIcon from '~icons/lucide/arrow-down';
  import ArrowUpIcon from '~icons/lucide/arrow-up';
  import type { Snippet } from 'svelte';
  import Hint from '#lib/components/hint.svelte';
  import * as Card from '#lib/components/ui/card/index.js';
  import type { Change } from '#lib/reports.js';
  import { cn } from '#lib/utils.js';

  // A headline number on the Reports page (design system section 11, Report stat): shadcn's Card, packed close. The title
  // is a muted one-line label with its small icon, and the value in the UI typeface (`metric`) is the one thing to read;
  // the change from the previous period is a small pill and the optional share a muted line, both on the value's own
  // baseline so a tile is two rows, not four. They wrap under the value when the tile is too narrow for them. The pill is
  // grey, never a status colour: a report states how far a number moved and leaves it to the person to say whether that is
  // good (T1 keeps status colours for statuses). Its sentence is in the tooltip and for screen readers. No sparkline, no
  // link. The title is the card's whole label, so it wraps rather than cuts. `Card.Header` is the container these sizes
  // measure: a narrow card keeps room for two lines, so values in a row line up whichever titles wrapped, and the narrowest
  // gives up the icon for the title. A stat that counts a status (the Verification Overview's Clear, Review and Verify)
  // passes `mark`, the status's own icon, in place of the decorative icon, so the tile carries icon, colour and word (T1, I2).
  let {
    icon: Icon,
    mark,
    title,
    value,
    share,
    change,
    class: className,
  }: {
    icon?: typeof LucideIcon;
    /** The status's own mark, drawn in the chip instead of `icon`. */
    mark?: Snippet;
    title: string;
    /** The value, formatted ("16", "21 days"). */
    value: string;
    /** A muted share under the value ("62% of applications"). */
    share?: string | null;
    /** The change from the previous period; null shows no line (compare off, or nothing to compare). */
    change?: Change | null;
    class?: string;
  } = $props();
</script>

<Card.Root class={cn('h-full gap-1.5 [--card-spacing:--spacing(3.5)]', className)} data-slot="report-stat">
  <Card.Header class="gap-0">
    <Card.Title class="flex min-w-0 items-center gap-2 text-body font-semibold text-muted-foreground @max-[12rem]:min-h-10 @max-[9rem]:items-start">
      <span class="grid size-5 shrink-0 place-items-center @max-[9rem]:hidden">{#if mark}{@render mark()}{:else if Icon}<Icon aria-hidden="true" class="size-4" />{/if}</span>
      <span class="min-w-0 break-words">{title}</span>
    </Card.Title>
  </Card.Header>
  <Card.Content class="flex flex-wrap items-baseline gap-x-2.5 gap-y-1">
    <span class="text-metric tabular-nums">{value}</span>
    {#if change}
      <Hint content={change.text}>
        {#snippet children({ props })}
          <span {...props} data-report-change class="inline-flex items-center gap-1 rounded-full bg-secondary px-2 py-0.5 text-caption font-semibold text-muted-foreground tabular-nums">
            {#if change.direction === 'up'}<ArrowUpIcon aria-hidden="true" class="size-3.5" />{:else if change.direction === 'down'}<ArrowDownIcon aria-hidden="true" class="size-3.5" />{/if}
            {#if change.direction === 'none'}{change.text}{:else}<span aria-hidden="true">{change.short}</span>{' '}<span class="sr-only">{change.text}</span>{/if}
          </span>
        {/snippet}
      </Hint>
    {/if}
    {#if share}<p class="basis-full text-caption text-muted-foreground">{share}</p>{/if}
  </Card.Content>
</Card.Root>
