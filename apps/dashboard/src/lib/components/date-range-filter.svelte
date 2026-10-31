<script lang="ts">
  import CheckIcon from '@lucide/svelte/icons/check';
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { Separator } from '#lib/components/ui/separator/index.js';
  import { customRangeProblem, dayText, defaultRange, presets, rangeInstants, rangeLabel, sameRange, type Preset, type ReportRange } from '#lib/report-range.js';
  import { cn } from '#lib/utils.js';

  // A report's date range (design system section 11, Date range filter): an outline button that names the filter and its
  // choice ("Dates: Last 30 days"), as a filter menu does, and takes `primary` once it is not the default. It opens a
  // list of presets, the chosen one ticked, then a custom range of two date fields under a rule. A preset applies at
  // once; a custom range applies with Apply, which says what is wrong in words when the dates do not make a range.
  // Dates are the person's own days (W8).
  let {
    value = $bindable(defaultRange),
    onchange,
    class: className,
  }: {
    value?: ReportRange;
    onchange?: (range: ReportRange) => void;
    class?: string;
  } = $props();

  let open = $state(false);
  let start = $state('');
  let end = $state('');
  let problem = $state<string | null>(null);
  const id = $props.id();

  // Opening fills the custom fields from the range in use, so editing one end keeps the other.
  function onOpenChange(next: boolean) {
    if (!next) return;
    if (value.kind === 'custom') {
      start = value.start;
      end = value.end;
    } else {
      const { from, to } = rangeInstants(value, new Date());
      start = dayText(new Date(from));
      end = dayText(new Date(new Date(to).getTime() - 1));
    }
    problem = null;
  }

  const preset = (days: Preset): ReportRange => ({ kind: 'preset', days });

  function choose(range: ReportRange) {
    value = range;
    open = false;
    onchange?.(range);
  }

  function apply(event: SubmitEvent) {
    event.preventDefault();
    problem = customRangeProblem(start, end);
    if (problem === null) choose({ kind: 'custom', start, end });
  }
</script>

<Popover.Root bind:open {onOpenChange}>
  <Popover.Trigger
    class={cn(
      buttonVariants({ variant: 'outline', size: 'lg' }),
      'max-w-80 min-w-0 shrink-0',
      !sameRange(value, defaultRange) && 'border-primary text-primary',
      className,
    )}
  >
    <span class="min-w-0 truncate">Dates: {rangeLabel(value)}</span>
    <ChevronDownIcon aria-hidden="true" />
  </Popover.Trigger>
  <Popover.Content align="start" collisionPadding={16} class="w-80 max-w-[calc(100vw-2rem)] p-1">
    <div role="radiogroup" aria-label="Dates" class="flex flex-col">
      {#each presets as days (days)}
        {@const range = preset(days)}
        {@const chosen = sameRange(value, range)}
        <button
          type="button"
          role="radio"
          aria-checked={chosen}
          onclick={() => choose(range)}
          class="flex h-9 items-center gap-2 rounded-md px-2 text-left text-body hover:bg-secondary pointer-coarse:h-11"
        >
          <span class="flex size-4 shrink-0 items-center justify-center">{#if chosen}<CheckIcon aria-hidden="true" class="stroke-[2.5]" />{/if}</span>
          {rangeLabel(range)}
        </button>
      {/each}
    </div>
    <Separator class="my-1" />
    <form class="flex flex-col gap-3 p-2" onsubmit={apply} aria-labelledby="{id}-custom" novalidate>
      <p id="{id}-custom" class="flex items-center gap-2 text-body font-semibold">
        <span class="flex size-4 shrink-0 items-center justify-center">{#if value.kind === 'custom'}<CheckIcon aria-hidden="true" class="stroke-[2.5]" />{/if}</span>
        Custom range
      </p>
      <div class="grid grid-cols-2 gap-2">
        <Field.Field>
          <Field.Label for="{id}-start">From</Field.Label>
          <Input id="{id}-start" type="date" bind:value={start} oninput={() => (problem = null)} />
        </Field.Field>
        <Field.Field>
          <Field.Label for="{id}-end">To</Field.Label>
          <Input id="{id}-end" type="date" bind:value={end} oninput={() => (problem = null)} />
        </Field.Field>
      </div>
      <div class="flex items-center justify-between gap-3">
        <FailureText message={problem} class="min-w-0" />
        <Button type="submit" class="ml-auto shrink-0">Apply</Button>
      </div>
    </form>
  </Popover.Content>
</Popover.Root>
