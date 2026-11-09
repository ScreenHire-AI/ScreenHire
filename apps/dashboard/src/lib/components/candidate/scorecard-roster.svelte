<script lang="ts">
  import type { ScorecardEntry, TeamMember } from '@screenhire/api-client';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import DateText from '#lib/components/date-text.svelte';
  import Hint from '#lib/components/hint.svelte';
  import ScorecardDetailSheet from '#lib/components/candidate/scorecard-detail-sheet.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import * as Table from '#lib/components/ui/table/index.js';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { formatDate } from '#lib/dates.js';
  import { sentenceStart } from '#lib/history.js';
  import { formatRating } from '#lib/scorecards.js';
  import { readable, rosterRows, type RosterRow, type WaitingRow } from '#lib/scorecard-roster.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { personName } from '#lib/team.js';
  import { recommendationLabels } from '#lib/workflow.js';

  // The scorecards given (design system section 11, Scorecard): a table with one row per reviewer and every header sorting
  // (L2), then the scorecards asked for that are not in yet under their own heading. A row opens the scorecard in a side
  // sheet, which steps through the others with Previous and Next in the order the table shows. A recommendation is words
  // in ink, never a status colour, so a Strong no never reads like a verification result. A scorecard the API withheld
  // (ADR-0013) shows who and when; its other cells are blank, with their words for screen readers.
  let {
    scorecards,
    waiting = [],
    stageName,
    members,
    currentUserId,
    phone = false,
  }: {
    scorecards: readonly ScorecardEntry[];
    waiting?: readonly WaitingRow[];
    stageName: (stageId: string) => string;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** Draw list items with a Sort chip in place of the table. */
    phone?: boolean;
  } = $props();

  const rows = $derived(rosterRows(scorecards, { stageName, members, currentUserId }));

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, RosterRow, any>[] = [
    { id: 'reviewer', header: 'Reviewer', accessorFn: (row) => row.reviewer, sortFn: 'alphanumeric' },
    { id: 'stage', header: 'Stage', accessorFn: (row) => row.stage, sortFn: 'alphanumeric' },
    { id: 'recommendation', header: 'Recommendation', accessorFn: (row) => row.recommendationRank, sortFn: 'basic', sortUndefined: 'last' },
    { id: 'rating', header: 'Rating', accessorFn: (row) => row.rating, sortFn: 'basic', sortDescFirst: true, sortUndefined: 'last' },
    { id: 'submitted', header: 'Submitted', accessorFn: (row) => row.submittedAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('scorecards', { id: 'submitted', desc: true }, columns.map((column) => column.id!));
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return rows;
    },
    getRowId: (row) => row.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
  const steps = $derived(readable(sorted));

  let openId = $state<string | undefined>();
  const active = $derived(steps.find((row) => row.id === openId));
  const askedBy = (row: WaitingRow) => (row.askedByUserId ? personName(row.askedByUserId, members, currentUserId) : undefined);
  const askedText = (row: WaitingRow) => `Asked ${formatDate(row.askedAt)}${askedBy(row) ? ` by ${askedBy(row)}` : ''}`;
  const waitingName = (row: WaitingRow) => sentenceStart(personName(row.assigneeUserId, members, currentUserId));
  // The whole row opens the scorecard: the reviewer's name is the one button, stretched over the row (L1).
  const rowButton =
    'min-w-0 truncate rounded-sm text-left font-semibold text-foreground after:absolute after:inset-0 hover:underline focus-visible:outline-2 focus-visible:outline-ring';
</script>

{#snippet recommendation(row: RosterRow)}
  {#if row.entry.recommendation}
    <span class="font-semibold text-foreground">{recommendationLabels[row.entry.recommendation]}</span>
  {:else}
    <span class="sr-only">Hidden until you submit yours</span>
  {/if}
{/snippet}

{#snippet ratingText(row: RosterRow)}
  {#if row.rating !== undefined}
    <span class="tabular-nums">{formatRating(row.rating)}</span>
  {:else}
    <span class="sr-only">{row.entry.withheld ? 'Hidden until you submit yours' : 'Not rated'}</span>
  {/if}
{/snippet}

{#snippet waitingBadge(row: WaitingRow)}
  <Hint content={row.scorecard}>
    {#snippet children({ props })}
      <span {...props}><StatusBadge status="waiting" label="Waiting" /><span class="sr-only"> for the {row.scorecard}</span></span>
    {/snippet}
  </Hint>
{/snippet}

{#snippet asked(row: WaitingRow)}
  <!-- One string after the date, so the space before "by" survives. -->
  Asked <DateText at={row.askedAt} />{askedBy(row) ? ` by ${askedBy(row)}` : ''}
{/snippet}

{#if phone}
  <div class="flex flex-col gap-2">
    {#if rows.length > 1}<SortMenu {table} class="w-fit" />{/if}
    <ul aria-label="Scorecards">
      {#each sorted as row (row.id)}
        <li class="relative flex min-w-0 flex-col gap-1 border-b border-border-subtle py-3 last:border-0">
          <div class="flex min-w-0 items-baseline justify-between gap-3">
            {#if row.entry.withheld}
              <span class="min-w-0 truncate font-semibold">{row.reviewer}</span>
            {:else}
              <button type="button" class={rowButton} onclick={() => (openId = row.id)}>{row.reviewer}</button>
            {/if}
            <span class="shrink-0 text-caption text-muted-foreground"><DateText at={row.submittedAt} /></span>
          </div>
          <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
            {@render recommendation(row)}
            {#if row.rating !== undefined || !row.entry.withheld}{@render ratingText(row)}{/if}
            <span class="text-caption text-muted-foreground">{row.stage}</span>
          </div>
        </li>
      {/each}
    </ul>
    {#if waiting.length > 0}
      <h3 class="mt-2 text-caption font-semibold text-muted-foreground">Asked for, not in yet</h3>
      <ul aria-label="Asked for, not in yet">
        {#each waiting as row (row.taskId)}
          <li class="flex min-w-0 flex-col gap-1 border-b border-border-subtle py-3 last:border-0">
            <span class="min-w-0 truncate font-semibold">{waitingName(row)}</span>
            <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
              {@render waitingBadge(row)}
              <span class="text-caption text-muted-foreground">{@render asked(row)}</span>
            </div>
          </li>
        {/each}
      </ul>
    {/if}
  </div>
{:else}
  <div class="overflow-x-auto">
    <Table.Root aria-label="Scorecards">
      <Table.Header>
        {#each table.getHeaderGroups() as group (group.id)}
          <Table.Row class="bg-muted hover:bg-muted">
            {#each group.headers as header (header.id)}<SortHeader {header} class="first:pl-4.5" />{/each}
          </Table.Row>
        {/each}
      </Table.Header>
      <Table.Body>
        {#each sorted as row (row.id)}
          <!-- A withheld scorecard opens nothing: there is nothing in it for this reader yet. -->
          <Table.Row class={['group relative h-12', !row.entry.withheld && 'hover:bg-muted']} aria-current={openId === row.id ? 'true' : undefined}>
            {#each table.getHeaderGroups()[0]!.headers as header (header.id)}
              {#if header.id === 'reviewer'}
                <Table.Cell class="max-w-56 pl-4.5">
                  <span class="flex min-w-0">
                    {#if row.entry.withheld}
                      <span class="min-w-0 truncate font-semibold">{row.reviewer}</span>
                    {:else}
                      <button type="button" class={rowButton} onclick={() => (openId = row.id)}>{row.reviewer}</button>
                    {/if}
                  </span>
                </Table.Cell>
              {:else if header.id === 'stage'}
                <Table.Cell class="max-w-48"><span class="flex min-w-0"><span class="truncate">{row.stage}</span></span></Table.Cell>
              {:else if header.id === 'recommendation'}
                <Table.Cell class="whitespace-nowrap">{@render recommendation(row)}</Table.Cell>
              {:else if header.id === 'rating'}
                <Table.Cell class="whitespace-nowrap">{@render ratingText(row)}</Table.Cell>
              {:else if header.id === 'submitted'}
                <Table.Cell class="whitespace-nowrap text-muted-foreground"><span class="relative"><DateText at={row.submittedAt} /></span></Table.Cell>
              {/if}
            {/each}
          </Table.Row>
        {/each}
        {#if waiting.length > 0}
          <Table.Row class="h-9 bg-muted hover:bg-muted">
            <Table.Cell colspan={columns.length} class="pl-4.5 text-caption font-semibold text-muted-foreground">Asked for, not in yet</Table.Cell>
          </Table.Row>
          {#each waiting as row (row.taskId)}
            <!-- Nothing to open until they submit it, so the row is not a control. -->
            <Table.Row class="h-12 hover:bg-transparent">
              <Table.Cell class="max-w-56 pl-4.5"><span class="flex min-w-0"><span class="truncate font-semibold">{waitingName(row)}</span></span></Table.Cell>
              <Table.Cell><span class="sr-only">No stage yet</span></Table.Cell>
              <Table.Cell class="whitespace-nowrap">{@render waitingBadge(row)}</Table.Cell>
              <Table.Cell><span class="sr-only">No rating yet</span></Table.Cell>
              <Table.Cell class="max-w-72 text-muted-foreground"><span class="flex min-w-0"><TruncatedText text={askedText(row)} /></span></Table.Cell>
            </Table.Row>
          {/each}
        {/if}
      </Table.Body>
    </Table.Root>
  </div>
{/if}

<ScorecardDetailSheet
  open={active !== undefined}
  onopenchange={(next) => {
    if (!next) openId = undefined;
  }}
  row={active}
  position={active ? steps.indexOf(active) : -1}
  count={steps.length}
  {stageName}
  {members}
  {currentUserId}
  onstep={(delta) => {
    const next = steps[steps.indexOf(active!) + delta];
    if (next) openId = next.id;
  }}
/>
