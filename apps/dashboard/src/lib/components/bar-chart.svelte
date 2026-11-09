<script lang="ts" module>
  /** A series of a bar chart: its name for the legend and its colour token (design system section 3.5). */
  export interface BarSeries {
    label: string;
    /** `chart-1` for the series or the part in focus, `chart-2` for the whole it is part of. */
    tone: 'chart-1' | 'chart-2';
  }

  /** One row of a bar chart: its name and one value per series. */
  export interface BarRow {
    key: string;
    label: string;
    /** One value per series, in the series' order. */
    values: readonly number[];
    /** The values as the row shows them at each bar's tip ("24", "21 days"). */
    texts: readonly string[];
    /** A muted figure after the first bar's value, such as "62%" of all applicants. */
    share?: string | null;
    /** A muted figure after the bars, such as "62% moved on". */
    note?: string | null;
    /** The row is the one with the biggest drop: its note is semibold with a small mark, and screen readers hear it. */
    drop?: boolean;
    /** Makes the row's name a link, to what the row counts. */
    href?: string;
    /** The row in words, for its readout and screen readers ("Interview: 24 applications, 62% moved on"). */
    summary: string;
  }
</script>

<script lang="ts">
  import ArrowDownRightIcon from '~icons/lucide/arrow-down-right';
  import Hint from '#lib/components/hint.svelte';
  import { cn } from '#lib/utils.js';

  // A horizontal bar chart drawn in HTML (design system section 11, Bar chart): each row's name, its bars growing from one
  // baseline with the value at each tip in text colours, and an optional muted note. Bars are at most 24px thick, square
  // at the baseline and rounded at the end; a zero draws nothing. Two series sit in pairs with a 2px gap and a legend
  // above; one series needs none, because the panel's title names it. Every value is written out, so the soft colour's
  // low contrast loses nothing, and the panel's table view holds the same numbers. The bars and note are one image that
  // reads out the row's summary on hover, focus and tap (F4); the name beside it is a link when the row has an `href`,
  // which an image would hide from screen readers. In a narrow container the name goes above the bars.
  let {
    label,
    series,
    rows,
    class: className,
  }: {
    /** What the chart shows, for screen readers ("Hiring funnel"). */
    label: string;
    series: readonly BarSeries[];
    rows: readonly BarRow[];
    class?: string;
  } = $props();

  const max = $derived(Math.max(0, ...rows.flatMap((row) => row.values)));
  const tones = { 'chart-1': 'bg-chart-1', 'chart-2': 'bg-chart-2' } as const;

  // The bar's share of its line, leaving room at the end for the value written at its tip.
  const width = (value: number) => (max > 0 && value > 0 ? `max(2px, calc((100% - 5.5rem) * ${value / max}))` : '0');
</script>

<figure class={cn('@container/chart flex min-w-0 flex-col gap-3', className)} data-slot="bar-chart">
  {#if series.length > 1}
    <ul class="flex flex-wrap gap-x-4 gap-y-1 text-caption text-muted-foreground" aria-label="Legend">
      {#each series as item (item.label)}
        <li class="flex items-center gap-1.5">
          <span aria-hidden="true" class={cn('size-3 rounded-sm', tones[item.tone])}></span>
          {item.label}
        </li>
      {/each}
    </ul>
  {/if}
  <ul aria-label={label} class="flex flex-col">
    {#each rows as row (row.key)}
      <li>
        <div class="group/row grid grid-cols-[minmax(0,1fr)] items-center gap-x-4 gap-y-1 rounded-md px-1 py-1 hover:bg-muted @md/chart:grid-cols-[minmax(4rem,9rem)_minmax(0,1fr)]">
          {#if row.href}
            <a href={row.href} class="inline-flex min-h-6 min-w-0 items-center truncate text-body text-primary underline underline-offset-2 pointer-coarse:min-h-11">{row.label}</a>
          {:else}
            <span aria-hidden="true" class="min-w-0 truncate text-body">{row.label}</span>
          {/if}
          <Hint content={row.summary}>
            {#snippet children({ props })}
              <div {...props} role="img" aria-label={row.summary} class="flex min-w-0 items-center gap-x-4">
                <span aria-hidden="true" class="flex min-w-0 flex-1 flex-col gap-0.5">
                  {#each row.values as value, index (index)}
                    <span class="flex min-w-0 items-center gap-2">
                      <!-- A zero draws no bar and leaves no gap, so its value starts at the baseline. -->
                      <span
                        class={cn(
                          'shrink-0 rounded-r-sm group-hover/row:opacity-85',
                          series.length > 1 ? 'h-3.5' : 'h-5',
                          tones[series[index]?.tone ?? 'chart-1'],
                          !(max > 0 && value > 0) && 'hidden',
                        )}
                        style:width={width(value)}
                      ></span>
                      <span class="shrink-0 text-body whitespace-nowrap tabular-nums">{row.texts[index]}</span>
                      {#if index === 0 && row.share}
                        <span class="shrink-0 text-body whitespace-nowrap text-muted-foreground tabular-nums">{row.share}</span>
                      {/if}
                    </span>
                  {/each}
                </span>
                {#if row.note !== undefined}
                  <!-- Inside the image, so a screen reader hears the row's summary, which says "the biggest drop". -->
                  <span
                    class={cn('inline-flex shrink-0 items-center justify-end gap-1 text-body whitespace-nowrap tabular-nums @md/chart:min-w-24', row.drop ? 'font-semibold text-foreground' : 'text-muted-foreground')}
                  >
                    {#if row.drop}<ArrowDownRightIcon aria-hidden="true" class="size-4" />{/if}{row.note ?? ''}
                  </span>
                {/if}
              </div>
            {/snippet}
          </Hint>
        </div>
      </li>
    {/each}
  </ul>
</figure>
