<script lang="ts" module>
  /** One point on a metric card's line: its label for the readout ("Oct 13 week") and its value. */
  export interface MetricPoint {
    label: string;
    value: number;
  }
</script>

<script lang="ts">
  // Every Lucide icon has this one component type.
  import type LucideIcon from '~icons/lucide/circle';
  import TrendingDownIcon from '~icons/lucide/trending-down';
  import TrendingUpIcon from '~icons/lucide/trending-up';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import * as Card from '#lib/components/ui/card/index.js';
  import { cn } from '#lib/utils.js';

  // A metric card (design system section 11): shadcn's Card with the value in the UI typeface, its signed change against a
  // named period ("+7 vs last week") and a 12-point line; no description. The change is green when it is good and amber
  // when it needs a look, decided per metric by `better` (fewer days to hire is good). The line is grey history with this
  // period in the accent; pointing at or tapping it reads a point out, and so does focusing the card (its latest point). With
  // `href` the card opens its report.
  let {
    icon: Icon,
    title,
    value,
    change,
    period,
    better,
    points,
    href,
    class: className,
  }: {
    icon: typeof LucideIcon;
    title: string;
    /** The value, formatted ("16", "21"). */
    value: string;
    /** The change against `period`, signed by its number. */
    change: number;
    /** The period it is compared with ("last week"). */
    period: string;
    /** Which way is good for this metric. */
    better: 'up' | 'down';
    /** Up to 12 points, oldest first; the last is this period. */
    points: readonly MetricPoint[];
    href?: string;
    class?: string;
  } = $props();

  const width = 220;
  const height = 48;
  const shown = $derived(points.slice(-12));
  const range = $derived.by(() => {
    const values = shown.map((point) => point.value);
    const low = Math.min(...values);
    return { low, span: Math.max(...values) - low || 1 };
  });
  const x = (index: number) => (shown.length < 2 ? width : (index / (shown.length - 1)) * width);
  const y = (point: MetricPoint) => 4 + (1 - (point.value - range.low) / range.span) * (height - 8);
  const path = (from: number, to: number) =>
    shown
      .slice(from, to)
      .map((point, index) => `${index === 0 ? 'M' : 'L'}${x(from + index)} ${y(point)}`)
      .join(' ');
  const history = $derived(path(0, shown.length - 1));
  const current = $derived(path(Math.max(0, shown.length - 2), shown.length));
  const area = $derived(`${path(0, shown.length)} L${width} ${height} L0 ${height} Z`);

  const tone = $derived(change === 0 ? 'text-muted-foreground' : change > 0 === (better === 'up') ? 'text-status-clear' : 'text-status-review');
  const signed = $derived(change > 0 ? `+${change}` : `${change}`);
  const summary = $derived(`${title}: ${value}, ${change === 0 ? 'no change' : `${change > 0 ? 'up' : 'down'} ${Math.abs(change)}`} on ${period}`);

  let pointed = $state<number | null>(null);
  let focused = $state(false);
  const readout = $derived(pointed ?? (focused ? shown.length - 1 : null));

  function onpointermove(event: PointerEvent) {
    const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const share = box.width ? (event.clientX - box.left) / box.width : 1;
    pointed = Math.round(Math.min(1, Math.max(0, share)) * (shown.length - 1));
  }
</script>

{#snippet card()}
  <Card.Root class={cn('h-full', href && 'group-hover/metric:border-input', className)}>
    <Card.Header class="gap-1.5">
      <Card.Title class="flex min-w-0 items-center gap-2.5">
        <span class="grid size-8 shrink-0 place-items-center rounded-lg bg-secondary text-sidebar-foreground"><Icon aria-hidden="true" /></span>
        <TruncatedText text={title} />
      </Card.Title>
      <!-- In a narrow card the badge or change drops under the title, so the title keeps its room. -->
      <Card.Action class="row-span-1 self-center @max-xs/card-header:col-start-1 @max-xs/card-header:row-start-2 @max-xs/card-header:justify-self-start">
        <span data-metric-change class={cn('inline-flex items-center gap-1 font-semibold whitespace-nowrap tabular-nums', tone)}>
          {#if change > 0}<TrendingUpIcon aria-hidden="true" />{:else if change < 0}<TrendingDownIcon aria-hidden="true" />{/if}
          {signed} <span class="font-normal text-muted-foreground">vs {period}</span>
        </span>
      </Card.Action>
    </Card.Header>
    <Card.Content class="flex flex-col gap-1">
      <p class="text-metric">{value}</p>
      {#if shown.length > 1}
        <!-- The line pictures the numbers the card already says; the readout is for looking closer. -->
        <div aria-hidden="true" class="relative h-12" {onpointermove} onpointerdown={onpointermove} onpointerleave={() => (pointed = null)}>
          <svg viewBox={`0 0 ${width} ${height}`} preserveAspectRatio="none" class="block size-full overflow-visible">
            <path d={area} class="fill-secondary" />
            <!-- At 3:1 or more against the card and the area under it, in both themes (X1). -->
            <path d={history} class="fill-none stroke-muted-foreground/75" stroke-width="2" stroke-linejoin="round" stroke-linecap="round" vector-effect="non-scaling-stroke" />
            <path d={current} class="fill-none stroke-primary" stroke-width="2" stroke-linecap="round" vector-effect="non-scaling-stroke" />
          </svg>
          {#if readout !== null}
            {@const point = shown[readout]!}
            <span
              class="pointer-events-none absolute size-2 -translate-1/2 rounded-full bg-primary ring-2 ring-card"
              style:left={`${(x(readout) / width) * 100}%`}
              style:top={`${(y(point) / height) * 100}%`}
            ></span>
            <span
              data-metric-readout
              class={[
                'pointer-events-none absolute -top-7 rounded-md bg-foreground px-2 py-1 text-caption font-semibold whitespace-nowrap text-background',
                readout > shown.length / 2 ? 'right-0' : 'left-0',
              ]}>{point.label}: {point.value}</span
            >
          {/if}
        </div>
      {/if}
    </Card.Content>
  </Card.Root>
{/snippet}

{#if href}
  <a {href} aria-label={summary} class="group/metric block rounded-panel text-foreground no-underline" onfocus={() => (focused = true)} onblur={() => (focused = false)}>
    {@render card()}
  </a>
{:else}
  <!-- A tab stop of its own, so the readout opens on focus as it does on hover and tap (F4). -->
  <!-- svelte-ignore a11y_no_noninteractive_tabindex: the card's readout opens on focus, and the card has no link to carry it -->
  <div role="group" tabindex="0" aria-label={summary} class="h-full rounded-panel" onfocus={() => (focused = true)} onblur={() => (focused = false)}>{@render card()}</div>
{/if}
