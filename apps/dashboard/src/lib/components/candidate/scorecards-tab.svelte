<script lang="ts">
  import ClipboardCheckIcon from '@lucide/svelte/icons/clipboard-check';
  import type { ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ScorecardList from '#lib/components/scorecard-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';

  // The scorecards reviewers submitted, with Fill scorecard while a reached stage with a scorecard is left to score
  // (design system section 11). A reviewer with their own scorecard to give reads only theirs until they give it: the
  // API leaves the others out, so they cannot anchor the review (ADR-0013), and the panel says so.
  let {
    scorecards,
    error,
    onretry,
    stageName,
    members,
    currentUserId,
    announcement,
    withheld = false,
    onfill,
  }: {
    /** Undefined while they load. */
    scorecards: readonly ScorecardEntry[] | undefined;
    error?: unknown;
    onretry: () => void;
    stageName: (id: string) => string;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    announcement: string;
    /** The reader has a review to give: the others' scorecards show once they submit theirs. */
    withheld?: boolean;
    /** Opens the scorecard sheet; only for a role that changes hiring data, while a stage is left to score. */
    onfill?: () => void;
  } = $props();
</script>

<Panel title="Scorecards">
  {#snippet actions()}
    {#if onfill}
      <Button variant="outline" onclick={onfill}><ClipboardCheckIcon aria-hidden="true" />Fill scorecard</Button>
    {/if}
  {/snippet}
  <div class="flex flex-col gap-3">
    {#if error}
      <QueryError message={describeError(error, 'Could not load the scorecards')} {onretry} inset />
    {:else if !scorecards}
      <div aria-hidden="true" class="flex gap-3">
        <Skeleton class="size-7 shrink-0 rounded-full" />
        <div class="flex min-w-0 flex-1 flex-col gap-1.5 pt-1">
          <Skeleton class="h-4 w-1/2" />
          <Skeleton class="h-16 w-full" />
        </div>
      </div>
    {:else if scorecards.length > 0}
      <ScorecardList {scorecards} {stageName} {members} {currentUserId} />
    {:else if !withheld}
      <p class="text-muted-foreground">No scorecards yet.</p>
    {/if}
    {#if withheld && !error && scorecards}<p class="text-muted-foreground">Other scorecards show after you submit yours.</p>{/if}
    <p role="status" class="text-muted-foreground empty:hidden">{announcement}</p>
  </div>
</Panel>
