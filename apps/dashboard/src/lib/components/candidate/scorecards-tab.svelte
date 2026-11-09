<script lang="ts">
  import ClipboardCheckIcon from '~icons/lucide/clipboard-check';
  import SendIcon from '~icons/lucide/send';
  import type { ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ScorecardRoster from '#lib/components/candidate/scorecard-roster.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import type { WaitingRow } from '#lib/scorecard-roster.js';

  // The scorecards reviewers submitted, as a roster (design system section 11): who, which stage, what they recommended,
  // when. Fill in shows only to someone who has not given a scorecard yet (A3); Ask for one gives a teammate a task done
  // when they submit it. A reviewer with their own scorecard to give sees who gave the others and when, never what they
  // said (ADR-0013), and the panel says what to do in one line with the button that does it.
  let {
    scorecards,
    waiting = [],
    error,
    onretry,
    stageName,
    members,
    currentUserId,
    announcement,
    withheld = false,
    phone = false,
    primaryFill = false,
    onfill,
    onask,
  }: {
    /** Undefined while they load. */
    scorecards: readonly ScorecardEntry[] | undefined;
    /** The scorecards asked for that are not in yet. */
    waiting?: readonly WaitingRow[];
    error?: unknown;
    onretry: () => void;
    stageName: (id: string) => string;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    announcement: string;
    /** The reader has a scorecard to give: the others' content shows once they submit theirs. */
    withheld?: boolean;
    phone?: boolean;
    /** Fill in your scorecard is the page's one filled button: only for a reader who cannot move the candidate (A1). */
    primaryFill?: boolean;
    /** Opens the scorecard sheet; only for a role that fills in scorecards who has not given one. */
    onfill?: () => void;
    /** Opens the sheet that asks a teammate for one; only for a role that creates tasks. */
    onask?: () => void;
  } = $props();
</script>

<Panel title="Scorecards" flush>
  {#snippet actions()}
    <!-- The withheld line below carries the reader's own Fill in, so the header does not offer it twice (W7). -->
    {#if onfill && !withheld}
      <Button variant="outline" onclick={onfill}><ClipboardCheckIcon aria-hidden="true" />Fill in</Button>
    {/if}
    {#if onask}
      <Button variant="outline" onclick={onask}><SendIcon aria-hidden="true" />Ask for one</Button>
    {/if}
  {/snippet}
  <div class="flex flex-col gap-3">
    {#if withheld && !error && scorecards}
      <div class="flex flex-wrap items-center justify-between gap-3 px-4.5 pt-3">
        <p>Submit yours to see the others.</p>
        {#if onfill}
          <Button variant={primaryFill ? 'default' : 'outline'} onclick={onfill}><ClipboardCheckIcon aria-hidden="true" />Fill in your scorecard</Button>
        {/if}
      </div>
    {/if}
    {#if error}
      <div class="p-4.5"><QueryError message={describeError(error, 'Could not load the scorecards')} {onretry} inset /></div>
    {:else if !scorecards}
      <div aria-hidden="true" class="flex flex-col gap-1 p-3">
        {#each [0, 1, 2] as row (row)}<Skeleton class="h-12 w-full" />{/each}
      </div>
      <p class="sr-only" role="status">Loading the scorecards…</p>
    {:else if scorecards.length > 0 || waiting.length > 0}
      <div class={phone ? 'px-4.5 py-1' : undefined}>
        <ScorecardRoster {scorecards} {waiting} {stageName} {members} {currentUserId} {phone} />
      </div>
    {:else if !withheld}
      <p class="p-4.5 text-muted-foreground">No scorecards yet.</p>
    {/if}
    <!-- For screen readers only: the sheet says "Scorecard submitted." and a new waiting row shows an ask (W7). -->
    <p role="status" class="sr-only">{announcement}</p>
  </div>
</Panel>
