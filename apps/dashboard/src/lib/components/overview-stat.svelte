<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import * as Card from '#lib/components/ui/card/index.js';
  import { cn } from '#lib/utils.js';

  // One number on Jobs above the table (design system section 11, Overview stat): the linked sibling of Report stat. An
  // icon chip and the title, then the value in the UI typeface (`metric`) as the one thing to read, and nothing else: no
  // change pill, no sparkline, no chart for one value. The whole card is the link to the list it counts, and its title is
  // that list's label (W6). The title is the card's whole label, so it wraps rather than cuts.
  let {
    icon: Icon,
    title,
    value,
    href,
    class: className,
  }: {
    icon: typeof LucideIcon;
    title: string;
    /** The value, formatted ("16", "3 of 5"). */
    value: string;
    /** The list the number counts. */
    href: string;
    class?: string;
  } = $props();
</script>

<a {href} data-slot="overview-stat" class="group/stat block rounded-panel text-foreground no-underline">
  <Card.Root class={cn('h-full group-hover/stat:border-input', className)}>
    <Card.Header class="gap-1.5">
      <Card.Title class="flex min-w-0 items-center gap-2.5 @max-[12rem]:min-h-12 @max-[9rem]:items-start">
        <span class="grid size-8 shrink-0 place-items-center rounded-lg bg-secondary text-sidebar-foreground @max-[9rem]:hidden"><Icon aria-hidden="true" /></span>
        <span class="min-w-0 break-words">{title}</span>
      </Card.Title>
    </Card.Header>
    <Card.Content>
      <span class="text-metric tabular-nums">{value}</span>
    </Card.Content>
  </Card.Root>
</a>
