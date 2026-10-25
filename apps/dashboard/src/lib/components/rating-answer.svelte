<script lang="ts">
  import type { ScorecardQuestionType } from '@screenhire/api-client';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { scaleOf } from '#lib/scorecards.js';
  import { cn } from '#lib/utils.js';

  // A rating or yes-or-no question on one row of joined buttons, with Low and High under the two ends of a number scale
  // (design system section 11, Rating answer). It is a single-choice ToggleGroup, so each button is a radio in a labelled
  // group: picking another moves the choice, and picking the chosen one again clears it, as an optional question allows.
  // The chosen one takes `accent`, as every pressed toggle in a labelled group does (check A6).
  let {
    type,
    value = $bindable(''),
    labelledby,
    describedby,
    invalid = false,
    class: className,
  }: {
    type: Extract<ScorecardQuestionType, 'Rating5' | 'Rating10' | 'RatingYesNo' | 'YesNo'>;
    /** The answer: a number as text ("4") on a scale, "yes" or "no", or empty for none. */
    value?: string;
    /** The question's label element. */
    labelledby: string;
    describedby?: string;
    invalid?: boolean;
    class?: string;
  } = $props();

  const scale = $derived(scaleOf(type));
  const choices = $derived(
    scale.length > 0 ? scale.map((n) => ({ value: String(n), label: String(n) })) : [{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }],
  );
</script>

<div class={cn('inline-flex w-fit max-w-full flex-col gap-1 self-start', className)}>
  <ToggleGroup.Root
    type="single"
    variant="outline"
    spacing={0}
    bind:value
    aria-labelledby={labelledby}
    aria-describedby={describedby}
    aria-invalid={invalid || undefined}
    class="max-w-full flex-wrap"
  >
    {#each choices as choice, index (choice.value)}
      <ToggleGroup.Item
        value={choice.value}
        aria-label={scale.length > 0 && index === 0 ? `${choice.label}, Low` : scale.length > 0 && index === choices.length - 1 ? `${choice.label}, High` : undefined}
        class="min-w-9 text-foreground"
      >
        {choice.label}
      </ToggleGroup.Item>
    {/each}
  </ToggleGroup.Root>
  {#if scale.length > 0}
    <div aria-hidden="true" class="flex justify-between text-caption text-muted-foreground"><span>Low</span><span>High</span></div>
  {/if}
</div>
