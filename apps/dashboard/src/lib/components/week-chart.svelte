<script lang="ts" module>
  /** One week of a week chart: its label and the two counts, with the week in words for the readout. */
  export interface WeekPoint {
    key: string;
    /** The week by its Monday ("3 Nov"). */
    label: string;
    applications: number;
    hires: number;
    /** "Week of 3 Nov: 12 applications, 2 hires". */
    summary: string;
  }
</script>

<script lang="ts">
  import { cn } from '#lib/utils.js';

  // Applications and hires by week (design system section 11, Week chart): paired columns per week from one baseline,
  // Applications in `chart-2` (the whole) and Hires in `chart-1` (the part in focus), with a legend above. Fifty weeks do
  // not leave room to write a value at every column, so the chart is one image-like group with one tab stop: pointing at
  // or tapping a week, or moving with the arrow keys, reads that week out above the columns (and to screen readers, politely),
  // and the panel's table view holds every number. Past about a year the columns keep a minimum width and the chart scrolls
  // sideways inside its card.
  let {
    label,
    weeks,
    class: className,
  }: {
    /** What the chart shows, for screen readers. */
    label: string;
    weeks: readonly WeekPoint[];
    class?: string;
  } = $props();

  const max = $derived(Math.max(0, ...weeks.flatMap((week) => [week.applications, week.hires])));
  const height = (value: number) => (max > 0 && value > 0 ? `max(2px, ${(value / max) * 100}%)` : '0');

  let pointed = $state<number | null>(null);
  let moved = $state<number | null>(null);
  // The week read out: the one pointed at, else the one the keys reached, else the latest.
  const active = $derived(Math.min(weeks.length - 1, pointed ?? moved ?? weeks.length - 1));

  function onkeydown(event: KeyboardEvent) {
    const at = pointed ?? moved ?? weeks.length - 1;
    const next = { ArrowLeft: at - 1, ArrowRight: at + 1, Home: 0, End: weeks.length - 1 }[event.key];
    if (next === undefined) return;
    event.preventDefault();
    pointed = null;
    moved = Math.min(weeks.length - 1, Math.max(0, next));
  }
</script>

<figure class={cn('flex min-w-0 flex-col gap-3', className)} data-slot="week-chart">
  <ul class="flex flex-wrap gap-x-4 gap-y-1 text-caption text-muted-foreground" aria-label="Legend">
    <li class="flex items-center gap-1.5"><span aria-hidden="true" class="size-3 rounded-sm bg-chart-2"></span>Applications</li>
    <li class="flex items-center gap-1.5"><span aria-hidden="true" class="size-3 rounded-sm bg-chart-1"></span>Hires</li>
  </ul>
  <!-- The readout has a line of its own, so reading a week never moves the columns. -->
  <p class="min-h-6 text-body font-semibold" aria-live="polite" data-week-readout>{weeks[active]?.summary ?? ''}</p>
  <div class="overflow-x-auto pb-1">
    <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions: the chart is one tab stop whose arrow keys move through the weeks -->
    <div
      role="group"
      tabindex="0"
      aria-label={label}
      {onkeydown}
      onfocus={() => (moved ??= weeks.length - 1)}
      onpointerleave={() => (pointed = null)}
      class="flex h-48 items-end gap-0.5 rounded-md border-b border-border"
      style:min-width={`${weeks.length * 0.875}rem`}
    >
      {#each weeks as week, index (week.key)}
        <div
          data-week
          role="presentation"
          aria-hidden="true"
          class={cn('flex h-full min-w-0 flex-1 items-end justify-center gap-px rounded-sm', index === active && 'bg-muted')}
          onpointerenter={() => (pointed = index)}
          onpointerdown={() => (pointed = index)}
        >
          <span class="w-full max-w-3 rounded-t-sm bg-chart-2" style:height={height(week.applications)}></span>
          <span class="w-full max-w-3 rounded-t-sm bg-chart-1" style:height={height(week.hires)}></span>
        </div>
      {/each}
    </div>
    <div aria-hidden="true" class="mt-1 flex justify-between text-caption text-muted-foreground">
      <span>{weeks[0]?.label ?? ''}</span>
      <span>{weeks.length > 1 ? (weeks.at(-1)?.label ?? '') : ''}</span>
    </div>
  </div>
</figure>
