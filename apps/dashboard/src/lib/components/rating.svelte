<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import StarIcon from '@lucide/svelte/icons/star';
  import type { ApplicationRating, ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import Hint from '#lib/components/hint.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import { sentenceStart } from '#lib/history.js';
  import { formatRating, ratingWords, scorecardCount, starFills } from '#lib/scorecards.js';
  import { personName } from '#lib/team.js';
  import { recommendationLabels } from '#lib/workflow.js';

  // The candidate's rating (design system section 11, Rating): five stars filled to the tenth, the number, and how many
  // scorecards it comes from. With the scorecards it opens to each one and its part of the rating. It averages people's
  // rating answers only, never integrity output, AI signals or the stage, and no automation reads it (ADR-0013). A
  // reviewer with their own scorecard to give reads "Rating after your scorecard": the API leaves the number out.
  let {
    rating,
    scorecards,
    stageName = (id) => id,
    members,
    currentUserId,
  }: {
    rating: ApplicationRating | null | undefined;
    /** The application's scorecards: given, the rating is a button that opens to each one that counts. */
    scorecards?: readonly ScorecardEntry[];
    stageName?: (stageId: string) => string;
    members?: readonly TeamMember[];
    currentUserId?: string;
  } = $props();

  const rated = $derived(rating?.state === 'Rated' && rating.value != null ? { value: rating.value, count: rating.scorecards ?? 0 } : null);
  const counting = $derived((scorecards ?? []).filter((scorecard) => scorecard.rating != null));
</script>

{#snippet stars(value: number, hidden = false)}
  <span
    role={hidden ? undefined : 'img'}
    aria-label={hidden ? undefined : `${formatRating(value)} of 5 stars`}
    aria-hidden={hidden || undefined}
    class="inline-flex gap-px"
  >
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

{#if rating?.state === 'AfterYourScorecard' && !scorecards}
  <!-- Short in a table cell (L1), the reason in the tooltip. -->
  <Hint content={ratingWords(rating)}>
    {#snippet children({ props })}<span {...props} class="text-muted-foreground">After yours</span>{/snippet}
  </Hint>
{:else if !rated}
  <span class="text-muted-foreground">{ratingWords(rating)}</span>
{:else if scorecards}
  <Popover.Root>
    <!-- Named by its own text, so what it says and what a screen reader hears match (WCAG 2.5.3): "Rated 4.1 out of 5
         from 3 scorecards", the words around the shown numbers kept for screen readers. -->
    <Popover.Trigger
      class="inline-flex min-h-8 items-center gap-1.5 rounded-lg border border-input px-2 hover:bg-muted focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none pointer-coarse:min-h-11"
    >
      {@render stars(rated.value, true)}
      <span class="sr-only">Rated</span>
      <span class="font-semibold tabular-nums">{formatRating(rated.value)}</span>
      <span class="sr-only">out of 5 from</span>
      <span class="inline-flex items-center gap-0.5 text-muted-foreground"><ClipboardCheckIcon class="size-3.5" aria-hidden="true" />{rated.count}</span>
      <span class="sr-only">{rated.count === 1 ? 'scorecard' : 'scorecards'}</span>
    </Popover.Trigger>
    <Popover.Content align="start" class="w-80">
      <Popover.Header>
        <Popover.Title>From {scorecardCount(rated.count)}</Popover.Title>
      </Popover.Header>
      <ul class="flex flex-col">
        {#each counting as scorecard (scorecard.id)}
          <li class="flex items-center gap-2 border-b border-border-subtle py-1.5 last:border-b-0">
            <span class="flex min-w-0 flex-1 flex-col">
              <!-- A name heading its row, so "you" starts with a capital (W2). -->
              <span class="truncate font-semibold">{sentenceStart(personName(scorecard.reviewerUserId, members, currentUserId))}</span>
              <span class="truncate text-caption text-muted-foreground">{stageName(scorecard.stageId)}</span>
            </span>
            <span class="tabular-nums">{formatRating(scorecard.rating!)}</span>
            <Badge variant="muted">{recommendationLabels[scorecard.recommendation]}</Badge>
          </li>
        {/each}
      </ul>
    </Popover.Content>
  </Popover.Root>
{:else}
  <span class="inline-flex items-center gap-1.5">
    {@render stars(rated.value)}
    <span class="font-semibold tabular-nums">{formatRating(rated.value)}</span>
    <Hint content={`From ${scorecardCount(rated.count)}`}>
      {#snippet children({ props })}
        <span {...props} class="inline-flex items-center gap-0.5 text-muted-foreground">
          <ClipboardCheckIcon class="size-3.5" aria-hidden="true" />
          <span aria-hidden="true">{rated.count}</span>
          <span class="sr-only">from {scorecardCount(rated.count)}</span>
        </span>
      {/snippet}
    </Hint>
  </span>
{/if}
