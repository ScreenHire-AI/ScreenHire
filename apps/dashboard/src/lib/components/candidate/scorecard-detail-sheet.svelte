<script lang="ts">
  import ChevronLeftIcon from '~icons/lucide/chevron-left';
  import ChevronRightIcon from '~icons/lucide/chevron-right';
  import type { TeamMember } from '@screenhire/api-client';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { formatDateTime } from '#lib/dates.js';
  import { sentenceStart } from '#lib/history.js';
  import { answerText } from '#lib/scorecards.js';
  import { roleOf, scorecardTitle, type RosterRow } from '#lib/scorecard-roster.js';
  import { personName } from '#lib/team.js';
  import { ratingLabels, recommendationLabels } from '#lib/workflow.js';

  // One scorecard, read-only, beside the roster (design system section 11, Scorecard): the scorecard's name for the title,
  // the reviewer and their role and when they submitted it for the subtitle, then each question with the answer and the
  // note. The row's own facts (recommendation, rating, stage) are not repeated here (W7), and a label that explains itself
  // is left out (W9). Nothing to submit, so no primary button (A1): Previous and Next step through the roster in its order.
  // It never shows verification output.
  let {
    open,
    onopenchange,
    row,
    position,
    count,
    stageName,
    members,
    currentUserId,
    onstep,
  }: {
    open: boolean;
    onopenchange: (open: boolean) => void;
    /** The scorecard shown; kept while the sheet closes. */
    row: RosterRow | undefined;
    /** Its place among the scorecards that can be read, and how many there are. */
    position: number;
    count: number;
    stageName: (stageId: string) => string;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    onstep: (delta: -1 | 1) => void;
  } = $props();

  // The row goes as the sheet closes, so the last one, and its place for Previous and Next, stay drawn until the
  // animation ends.
  let shown = $state<RosterRow | undefined>();
  let shownPosition = $state(0);
  $effect(() => {
    if (!row) return;
    shown = row;
    shownPosition = position;
  });
  const entry = $derived(shown?.entry);
  const who = $derived(entry ? sentenceStart(personName(entry.reviewerUserId, members, currentUserId)) : '');
  const role = $derived(entry ? roleOf(entry.reviewerUserId, members) : '');
</script>

<Sheet.Root {open} onOpenChange={onopenchange}>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-xl">
    {#if entry}
      <Sheet.Header>
        <Sheet.Title>{scorecardTitle(entry, stageName)}</Sheet.Title>
        <Sheet.Description>{who}{role ? `, ${role}` : ''}. Submitted {formatDateTime(entry.submittedAt)}.</Sheet.Description>
      </Sheet.Header>
      <div class="flex flex-col gap-4 px-4">
        <!-- The roster is hidden behind a sheet this wide on a phone, so the recommendation, which the row shows, is said here too
             until the table is beside it (W7). -->
        {#if entry.recommendation}<p class="font-semibold sm:hidden">{recommendationLabels[entry.recommendation]}</p>{/if}
        <SummaryList>
          {#each entry.answers as answer (answer.questionId)}
            {@const text = answerText(answer)}
            <SummaryItem label={answer.label}>
              {#if text !== null}<span class="break-words whitespace-pre-line">{text}</span>{:else}<span class="text-muted-foreground">Not answered</span>{/if}
            </SummaryItem>
          {/each}
          <!-- A v0.2 scorecard shows its criteria with their labelled ratings, as it was given. -->
          {#each entry.ratings as rating (rating.criterionId)}
            <SummaryItem label={rating.label}>
              {#if rating.rating}{ratingLabels[rating.rating]}{:else}<span class="text-muted-foreground">Not rated</span>{/if}
            </SummaryItem>
          {/each}
        </SummaryList>
        <!-- The note is the reviewer's own words, quoted; the quote marks say what it is, so it has no label. -->
        {#if entry.note}<p class="max-w-measure break-words whitespace-pre-line">"{entry.note}"</p>{/if}
      </div>
      <!-- With one scorecard there is nothing to step to, so no buttons (A3). -->
      {#if count > 1}
        <Sheet.Footer class="flex-row justify-between">
          <Button type="button" variant="outline" size="lg" disabled={shownPosition <= 0} onclick={() => onstep(-1)}>
            <ChevronLeftIcon aria-hidden="true" />Previous
          </Button>
          <Button type="button" variant="outline" size="lg" disabled={shownPosition < 0 || shownPosition >= count - 1} onclick={() => onstep(1)}>
            Next<ChevronRightIcon aria-hidden="true" />
          </Button>
        </Sheet.Footer>
      {/if}
    {/if}
  </Sheet.Content>
</Sheet.Root>
