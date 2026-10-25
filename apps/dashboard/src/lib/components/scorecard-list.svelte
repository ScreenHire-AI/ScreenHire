<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { personName } from '#lib/team.js';
  import { ratingLabels, recommendationLabels } from '#lib/workflow.js';

  // Submitted scorecards (design system section 11): an icon in a circle, the reviewer's recommendation and who gave it
  // when, the stage as a fact pill, then each criterion with its rating and the note quoted below. Recommendations and
  // ratings are words, never numbers, and nothing adds them up: each is one person's judgement (ADR-0004).
  let {
    scorecards,
    stageName,
    members,
    currentUserId,
  }: {
    scorecards: readonly ScorecardEntry[];
    stageName: (stageId: string) => string;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
  } = $props();
</script>

<ol aria-label="Scorecards" class="flex flex-col">
  {#each scorecards as scorecard (scorecard.id)}
    {@const reviewer = personName(scorecard.reviewerUserId, members, currentUserId)}
    <li class="flex gap-3 border-b border-border-subtle py-3 first:pt-0 last:border-b-0 last:pb-0">
      <span aria-hidden="true" class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-muted-foreground"><ClipboardCheckIcon /></span>
      <div class="flex min-w-0 flex-1 flex-col gap-2 pt-1">
        <p class="break-words">
          <span class="font-semibold">{recommendationLabels[scorecard.recommendation]}</span>
          <span class="text-muted-foreground">by {reviewer}</span>
        </p>
        <div class="flex flex-wrap items-center gap-2">
          <Badge variant="muted">{stageName(scorecard.stageId)}</Badge>
          <RelativeTime at={scorecard.submittedAt} class="text-caption text-muted-foreground" />
        </div>
        <SummaryList>
          {#each scorecard.ratings as rating (rating.criterionId)}
            <SummaryItem label={rating.label}>
              {#if rating.rating}{ratingLabels[rating.rating]}{:else}<span class="text-muted-foreground">Not rated</span>{/if}
            </SummaryItem>
          {/each}
        </SummaryList>
        {#if scorecard.note}<p class="max-w-measure break-words whitespace-pre-line">"{scorecard.note}"</p>{/if}
      </div>
    </li>
  {/each}
</ol>
