<script lang="ts">
  import type { DayHours, DayOfWeek } from '@screenhire/api-client';
  import * as Field from '#lib/components/ui/field/index.js';
  import { checkboxClass, selectClass } from '#lib/components/workflow/controls.js';
  import { sortDays, weekDays } from '#lib/interview-options.js';
  import { defaultDayEnd, defaultDayStart, timeOptions } from '#lib/schedule-interview.js';
  import { cn } from '#lib/utils.js';

  // Hours in a week (design system section 11, Weekly hours): a row per day, Monday first, each a box that turns the day on
  // and its From and To. One stretch a day; a day that is off has no hours. The interview's hours and blocks on the Schedule
  // interview sheet, a stage's default, and a person's own interview hours all use it. In a narrow container (a phone's sheet)
  // a day's From and To go on the line under its name, so the times stay readable.
  let {
    value = $bindable([]),
    legend,
    id,
    errors,
    description,
    onchange,
  }: {
    value?: DayHours[];
    legend: string;
    /** Prefixes the ids of the rows' controls. */
    id: string;
    errors?: string[];
    /** One muted line under the legend. */
    description?: string;
    /** After every change, with the new hours: for a caller that holds them itself rather than binding. */
    onchange?: (days: DayHours[]) => void;
  } = $props();

  const of = (day: DayOfWeek) => value.find((row) => row.day === day);

  function toggle(day: DayOfWeek, on: boolean) {
    const without = value.filter((row) => row.day !== day);
    // A day turned on takes the hours of the day before it that has some, so a week fills in quickly.
    const like = value.at(-1);
    value = on ? sortDays([...without, { day, from: like?.from ?? defaultDayStart, to: like?.to ?? defaultDayEnd }]) : without;
    onchange?.(value);
  }

  function set(day: DayOfWeek, field: 'from' | 'to', time: string) {
    value = value.map((row) => (row.day === day ? { ...row, [field]: time } : row));
    onchange?.(value);
  }

  const errorId = $derived(`${id}-error`);
</script>

<fieldset class="@container flex min-w-0 flex-col gap-2" aria-describedby={errors?.length ? errorId : undefined}>
  <legend class="mb-1 text-body font-medium">{legend}</legend>
  {#if description}<p class="text-muted-foreground">{description}</p>{/if}
  <ul class="flex flex-col gap-2">
    {#each weekDays as { day, label } (day)}
      {@const row = of(day)}
      <li class="grid min-w-0 grid-cols-2 items-center gap-2 @sm:grid-cols-[minmax(0,7rem)_1fr_1fr]">
        <label class={cn('flex min-w-0 items-center gap-2 pointer-coarse:min-h-11', row && 'col-span-2 @sm:col-span-1')}>
          <input type="checkbox" class={checkboxClass} checked={row !== undefined} onchange={(event) => toggle(day, event.currentTarget.checked)} />
          <span class={cn('truncate', !row && 'text-muted-foreground')}>{label}</span>
        </label>
        {#if row}
          <select id={`${id}-${day}-from`} aria-label={`${label} from`} class={selectClass} value={row.from} onchange={(event) => set(day, 'from', event.currentTarget.value)}>
            {#each timeOptions as time (time.value)}<option value={time.value}>{time.label}</option>{/each}
          </select>
          <select id={`${id}-${day}-to`} aria-label={`${label} to`} class={selectClass} value={row.to} onchange={(event) => set(day, 'to', event.currentTarget.value)}>
            {#each timeOptions as time (time.value)}<option value={time.value}>{time.label}</option>{/each}
          </select>
        {:else}
          <span class="text-muted-foreground @sm:col-span-2">No hours</span>
        {/if}
      </li>
    {/each}
  </ul>
  <Field.Error id={errorId} errors={errors?.map((message) => ({ message }))} />
</fieldset>
