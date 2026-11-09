<script lang="ts">
  import StarIcon from '~icons/lucide/star';
  import type { ApplicationRating } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import { formatRating, ratingWords, scorecardCount, starFills } from '#lib/scorecards.js';

  // The candidate's rating (design system section 11, Rating). It averages people's rating answers only, never
  // verification output, AI signals or the stage, and no automation reads it (ADR-0013). A reviewer with their own
  // scorecard to give reads "Rating after your scorecard": the API leaves the number out.
  //  - `cell` (a table cell or list item): the number only, no stars (L1), with how many scorecards in its tooltip.
  //  - `stars`: five stars filled to the tenth, the number and how many scorecards it comes from, as plain facts.
  //  - `link`: the same, as a link to `href` (a candidate's header links to the Scorecards tab, A5).
  let {
    rating,
    variant = 'cell',
    href,
  }: {
    rating: ApplicationRating | null | undefined;
    variant?: 'cell' | 'stars' | 'link';
    /** Where the `link` variant goes. */
    href?: string;
  } = $props();

  const rated = $derived(rating?.state === 'Rated' && rating.value != null ? { value: rating.value, count: rating.scorecards ?? 0 } : null);
</script>

{#snippet stars(value: number)}
  <span aria-hidden="true" class="inline-flex gap-px">
    {#each starFills(value) as fill, index (index)}
      <span class="relative inline-flex size-4 text-muted-foreground">
        <StarIcon class="size-4" aria-hidden="true" />
        <span class="absolute inset-y-0 left-0 overflow-hidden text-foreground" style:width={`${fill * 100}%`}>
          <StarIcon class="size-4 max-w-none fill-current" aria-hidden="true" />
        </span>
      </span>
    {/each}
  </span>
{/snippet}

{#snippet facts(value: number, count: number)}
  <!-- Named by its own text, so what it says and what a screen reader hears match (WCAG 2.5.3): "Rated 4.1 out of 5 from
       3 scorecards", the words around the shown numbers kept for screen readers. -->
  {@render stars(value)}
  <span class="sr-only">Rated</span>
  <span class="font-semibold tabular-nums">{formatRating(value)}</span>
  <span class="sr-only">out of 5 from</span>
  <!-- How many scorecards it comes from is a plain muted count: an icon beside a bare number said nothing on its own. -->
  <span aria-hidden="true" class="text-muted-foreground tabular-nums">({count})</span>
  <span class="sr-only">{count} {count === 1 ? 'scorecard' : 'scorecards'}</span>
{/snippet}

{#if variant === 'cell' && rating?.state === 'AfterYourScorecard'}
  <!-- Short in a table cell (L1), the reason in the tooltip. -->
  <Hint content={ratingWords(rating)}>
    {#snippet children({ props })}<span {...props} class="text-muted-foreground">After yours</span>{/snippet}
  </Hint>
{:else if !rated}
  <!-- Nothing to show until a scorecard counts: the cell is blank and the words are for screen readers only (L1). -->
  <span class="sr-only">{ratingWords(rating)}</span>
{:else if variant === 'link' && href}
  <a
    {href}
    data-rating-link
    class="inline-flex min-h-8 items-center gap-1.5 rounded-lg px-1 hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none pointer-coarse:min-h-11"
  >
    {@render facts(rated.value, rated.count)}
  </a>
{:else if variant === 'stars' || variant === 'link'}
  <span class="inline-flex items-center gap-1.5">{@render facts(rated.value, rated.count)}</span>
{:else}
  <Hint content={`From ${scorecardCount(rated.count)}`}>
    {#snippet children({ props })}
      <!-- The number only in a table cell (L1); how many scorecards it comes from is in the tooltip. -->
      <span {...props} class="font-semibold tabular-nums">
        {formatRating(rated.value)}
        <span class="sr-only">out of 5, from {scorecardCount(rated.count)}</span>
      </span>
    {/snippet}
  </Hint>
{/if}
