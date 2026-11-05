<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import ArrowDownIcon from '~icons/lucide/arrow-down';
  import ArrowUpIcon from '~icons/lucide/arrow-up';
  import Hint from '#lib/components/hint.svelte';
  import * as Card from '#lib/components/ui/card/index.js';
  import type { Change } from '#lib/reports.js';
  import { cn } from '#lib/utils.js';

  // A headline number on the Reports page (design system section 11, Report stat): shadcn's Card with an icon chip and
  // title, the value in the UI typeface (`metric`) as the one thing to read, the change from the previous period as a small
  // pill at the end of its line, and an optional muted share under it. The pill is grey, never a status colour: a report
  // states how far a number moved and leaves it to the person to say whether that is good (T1 keeps status colours for
  // statuses). Its sentence is in the tooltip and for screen readers. No sparkline, no link.
  // The title is the card's whole label, so it wraps rather than cuts. `Card.Header` is the container these sizes measure:
  // a narrow card keeps room for two lines, so values in a row line up whichever titles wrapped, and the narrowest gives
  // up the chip for the title.
  let {
    icon: Icon,
    title,
    value,
    share,
    change,
    class: className,
  }: {
    icon: typeof LucideIcon;
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

<Card.Root class={cn('h-full', className)} data-slot="report-stat">
  <Card.Header class="gap-1.5">
    <Card.Title class="flex min-w-0 items-center gap-2.5 @max-[12rem]:min-h-12 @max-[9rem]:items-start">
      <span class="grid size-8 shrink-0 place-items-center rounded-lg bg-secondary text-sidebar-foreground @max-[9rem]:hidden"><Icon aria-hidden="true" /></span>
      <span class="min-w-0 break-words">{title}</span>
    </Card.Title>
  </Card.Header>
  <Card.Content class="flex flex-col gap-1">
    <div class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
      <span class="text-metric">{value}</span>
      {#if change}
        <Hint content={change.text}>
          {#snippet children({ props })}
            <span {...props} data-report-change class="inline-flex items-center gap-1 rounded-full bg-secondary px-2.5 py-0.5 text-caption font-semibold text-muted-foreground tabular-nums">
              {#if change.direction === 'up'}<ArrowUpIcon aria-hidden="true" class="size-3.5" />{:else if change.direction === 'down'}<ArrowDownIcon aria-hidden="true" class="size-3.5" />{/if}
              {#if change.direction === 'none'}{change.text}{:else}<span aria-hidden="true">{change.short}</span>{' '}<span class="sr-only">{change.text}</span>{/if}
            </span>
          {/snippet}
        </Hint>
      {/if}
    </div>
    {#if share}<p class="text-body text-muted-foreground">{share}</p>{/if}
  </Card.Content>
</Card.Root>
