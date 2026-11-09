<script lang="ts">
  import type { ApplicationRating, ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import DateText from '#lib/components/date-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import Rating from '#lib/components/rating.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { sentenceStart } from '#lib/history.js';
  import { personName } from '#lib/team.js';
  import { recommendationLabels } from '#lib/workflow.js';

  // The Overview's Scorecards panel (design system section 21): the candidate's rating, then one line per person who gave a
  // scorecard: their name, the scorecard, their recommendation in words and the date. The Scorecards tab has the answers.
  // It averages and lists people's own scorecards only, never verification output (ADR-0013). A reviewer with their own
  // scorecard to give sees only a line saying so until they submit it: the API leaves the others out. Nothing is drawn
  // while there is nothing to say.
  let {
    rating,
    scorecards,
    members,
    currentUserId,
    withheld = false,
    onfill,
  }: {
    rating: ApplicationRating | null | undefined;
    scorecards: readonly ScorecardEntry[] | undefined;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** The reader has a scorecard to give: the others show once they submit theirs. */
    withheld?: boolean;
    /** Opens the scorecard sheet; only for a role that fills in scorecards. */
    onfill?: () => void;
  } = $props();

  const given = $derived(scorecards ?? []);
</script>

{#if given.length > 0 || withheld}
  <Panel title="Scorecards">
    {#snippet actions()}
      <a href="?tab=scorecards" class="inline-flex min-h-6 items-center font-semibold text-primary underline underline-offset-2 pointer-coarse:min-h-11">All scorecards</a>
    {/snippet}
    <div class="flex flex-col gap-3">
      {#if withheld}
        <p class="text-muted-foreground">Rating after your scorecard.</p>
        {#if onfill}<Button variant="outline" class="w-fit" onclick={onfill}>Fill in your scorecard</Button>{/if}
      {:else}
        <div class="flex min-w-0"><Rating {rating} variant="stars" /></div>
      {/if}
      {#if given.length > 0}
        <ul aria-label="Scorecards given" class="flex flex-col divide-y divide-border-subtle">
          {#each given as scorecard (scorecard.id)}
            <li class="flex min-w-0 flex-wrap items-baseline gap-x-3 gap-y-0.5 py-2 first:pt-0 last:pb-0">
              <span class="min-w-0 truncate font-semibold">{sentenceStart(personName(scorecard.reviewerUserId, members, currentUserId))}</span>
              {#if scorecard.scorecardName}<span class="min-w-0 truncate text-muted-foreground">{scorecard.scorecardName}</span>{/if}
              <span class="ml-auto shrink-0 font-semibold">{recommendationLabels[scorecard.recommendation]}</span>
              <DateText at={scorecard.submittedAt} class="shrink-0 text-muted-foreground" />
            </li>
          {/each}
        </ul>
      {/if}
    </div>
  </Panel>
{/if}
