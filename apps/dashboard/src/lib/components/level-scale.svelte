<script lang="ts">
  import StatusBadge from '#lib/components/status-badge.svelte';
  import type { LevelView } from '#lib/integrity.js';
  import { statuses, type StatusTone } from '#lib/status.js';
  import { cn } from '#lib/utils.js';

  // An assessment's level on its three-step scale (design system section 16): the current step is marked "This
  // candidate", with a thick bar and its badge; the others are thin and neutral, never faded like disabled controls.
  // Levels are categories, so the scale has steps, not a needle, and nothing on it is a score (ADR-0004). Labels keep
  // their full names (section 16): the other steps' labels wrap rather than truncate.
  let {
    levels,
    current,
    name,
    compact = false,
  }: {
    /** Lowest first. */
    levels: readonly LevelView[];
    current: LevelView;
    /** The assessment, for screen readers: "Authenticity". */
    name: string;
    /**
     * A small meter of three short steps, for a panel's header beside the level's badge, which carries the words: the
     * current step in the level's colour, the others neutral.
     */
    compact?: boolean;
  } = $props();

  const marks: Record<StatusTone, string> = {
    clear: 'bg-status-clear-mark',
    review: 'bg-status-review-mark',
    verify: 'bg-status-verify-mark',
    ai: 'bg-status-ai-mark',
    neutral: 'bg-muted-foreground',
  };
  // The steps share the width equally until the current step's badge would not fit its third (a phone): then its column
  // grows to the badge and the other two share what is left, their labels wrapping.
  const columns = [
    'grid-cols-[minmax(max-content,1fr)_minmax(0,1fr)_minmax(0,1fr)]',
    'grid-cols-[minmax(0,1fr)_minmax(max-content,1fr)_minmax(0,1fr)]',
    'grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(max-content,1fr)]',
  ];
  const position = $derived(levels.findIndex((level) => level.label === current.label));
  const place = $derived(position === 0 ? 'the lowest' : position === levels.length - 1 ? 'the highest' : 'the middle');
</script>

{#if compact}
  <span role="img" aria-label={`${name}: ${current.label}, ${place} of ${levels.length}`} data-level-scale class="inline-flex items-center gap-1">
    {#each levels as level (level.label)}
      {@const here = level.label === current.label}
      <span aria-hidden="true" data-current={here || undefined} class={cn('h-1.5 w-6 rounded-full', here ? marks[statuses[level.status].tone] : 'bg-border')}></span>
    {/each}
  </span>
{:else}
<div
  role="img"
  aria-label={`${name}: ${current.label}, ${place} of ${levels.length}`}
  data-level-scale
  class={cn('grid gap-2', columns[position] ?? 'grid-cols-3')}
>
  {#each levels as level (level.label)}
    {@const here = level.label === current.label}
    <div aria-hidden="true" data-current={here || undefined} class="flex min-w-0 flex-col gap-1.5">
      <!-- One line in every column, so the bars stay level: the hidden copies only hold the row's height. -->
      <span class={cn('truncate text-caption font-semibold text-muted-foreground', !here && 'invisible')}>This candidate</span>
      <span class={cn('w-full rounded-full', here ? cn('h-2', marks[statuses[level.status].tone]) : 'h-1 bg-border')}></span>
      {#if here}
        <StatusBadge status={level.status} label={level.label} class="self-start" />
      {:else}
        <span class="text-caption text-muted-foreground">{level.label}</span>
      {/if}
    </div>
  {/each}
</div>
{/if}
