<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import { createTable, FlexRender, renderSnippet } from '@tanstack/svelte-table';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import { tick, type Snippet } from 'svelte';
  import { get } from 'svelte/store';
  import DateText from '#lib/components/date-text.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import Rating from '#lib/components/rating.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { candidateRowHeight, candidateTableFeatures, type CandidateColumn } from '#lib/candidate-table.js';
  import { initials, sourceLabel } from '#lib/format.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { chipOf } from '#lib/integrity.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { hasModifier, isTypingTarget } from '#lib/shortcuts.js';
  import { isOverdue } from '#lib/sla.js';
  import { createTableSort, tableSortOptions, type TableSortState } from '#lib/table-sort.svelte.js';

  let {
    applications,
    label = 'Candidates',
    showJob = false,
    stages,
    moves,
    extraColumns = [],
    activeId = $bindable(null),
    selectedIds = $bindable([]),
    emptyText = 'No candidates yet.',
    onclearfilters,
    filters,
    fill = false,
    sortId,
    sort = createTableSort(sortId, stages ? { id: 'stage', desc: false } : { id: 'applied', desc: true }, [
      'candidate',
      ...extraColumns.flatMap((column) => (column.id ? [column.id] : [])),
      ...(showJob ? ['job'] : []),
      'stage',
      'source',
      'applied',
      'lastActivity',
    ]),
  }: {
    applications: readonly ApplicationSummary[];
    /**
     * The table fills its panel's height and scrolls inside itself, so it never stops short of the window (S6). The
     * panel and the page around it must fill the height too. Without it the table stops at a fixed height.
     */
    fill?: boolean;
    /** The table's accessible name. */
    label?: string;
    /** Adds a Job column, for the table across every job. */
    showJob?: boolean;
    /** With `moves`: the job's stages, for each row's "Move" menu. */
    stages?: readonly WorkflowStage[];
    moves?: Moves;
    /** Columns right after Candidate (status first, T2): the slot the integrity columns fill (#lib/candidate-table.ts). */
    extraColumns?: readonly CandidateColumn[];
    /** The keyboard cursor's row (J / K), tinted; the job page shows it in its side panel. */
    activeId?: string | null;
    /** Rows ticked with X or their checkbox. */
    selectedIds?: string[];
    emptyText?: string;
    /** While the page filters the rows: the empty state's one action (E1). */
    onclearfilters?: () => void;
    /** On a phone, the page's filters, in one row with the Sort chip above the list items (design system L2, section 8). */
    filters?: Snippet;
    /** Names the table in the person's remembered sorts (`candidates`, `job-candidates`); without it the sort is forgotten. */
    sortId?: string;
    /**
     * The sort, when the page needs it too. A job's table (with `stages`) opens with the candidates needing attention
     * first, every other table newest first by Applied (L2), unless the person chose another.
     */
    sort?: TableSortState;
  } = $props();

  const columns = $derived<CandidateColumn[]>([
    { id: 'select', header: () => renderSnippet(selectHeader, undefined), cell: ({ row }) => renderSnippet(selectCell, row.original) },
    // Every column sorts (L2); text columns by their words, A to Z first.
    { id: 'candidate', header: 'Candidate', accessorFn: (application) => application.candidateName, sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(candidateCell, row.original) },
    // Status first, right after the name (T2): the integrity columns, then the facts.
    ...extraColumns,
    ...(showJob
      ? [{ id: 'job', header: 'Job', accessorFn: (application) => application.jobTitle, sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(jobCell, row.original) } satisfies CandidateColumn]
      : []),
    // On a job's table the Stage column sorts by attention: overdue rows first, the longest overdue at the top, then the
    // rest in pipeline order. Overdue is the team's own delay (section 3.2), so the order never leans on integrity output.
    // Across jobs there is no one pipeline order, so it sorts by the stage's name.
    {
      id: 'stage',
      header: 'Stage',
      accessorFn: (application) => (stages ? attentionRank(application) : application.stage.name),
      sortFn: stages ? 'basic' : 'alphanumeric',
      // The rank is a number, which TanStack would sort descending first; attention comes first.
      sortDescFirst: false,
      cell: ({ row }) => renderSnippet(stageCell, row.original),
    },
    { id: 'source', header: 'Source', accessorFn: (application) => sourceLabel(application.source), sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(sourceCell, row.original) },
    // Applied sorts, newest first: the order every candidate table opens on (L2).
    { id: 'applied', header: 'Applied', accessorFn: (application) => application.submittedAt, sortFn: 'basic', sortDescFirst: true, cell: ({ row }) => renderSnippet(appliedCell, row.original) },
    // When anything last happened to the application, from the candidate or the team (the API's lastActivityAt), newest
    // first. The integrity checks never move it.
    { id: 'lastActivity', header: 'Last activity', accessorFn: (application) => application.lastActivityAt, sortFn: 'basic', sortDescFirst: true, cell: ({ row }) => renderSnippet(lastActivityCell, row.original) },
    ...(stages && moves?.allowed ? [{ id: 'move', header: () => renderSnippet(moveHeader, undefined), cell: ({ row }) => renderSnippet(moveCell, row.original) } satisfies CandidateColumn] : []),
  ]);

  // Overdue rows rank below every stage position, ordered by when they fell due; the rest rank by pipeline position.
  function attentionRank(application: ApplicationSummary): number {
    if (isOverdue(application, Date.now())) return Date.parse(application.slaDueAt!) - 1e15;
    const position = stages?.findIndex((stage) => stage.id === application.stage.id) ?? -1;
    return position < 0 ? Number.MAX_SAFE_INTEGER : position;
  }

  // Phones get two-line list items in place of the table (design system section 8), at the shell's own phone width.
  const phone = new IsMobile();
  // The checkbox and name columns stay put while a tablet scrolls the table sideways (design system section 8). The
  // checkbox column's width is fixed, so the name column knows where to stick: its 24px hit area (44px on touch) plus
  // padding.
  const selectColumn = 'sticky left-0 w-10 min-w-10 px-2 pointer-coarse:w-15 pointer-coarse:min-w-15';
  const nameColumn = 'sticky left-10 pointer-coarse:left-15';
  // Source is an optional column (section 8): it shows only where the table has room, so a job's pipeline beside its
  // side column fits a laptop without scrolling sideways.
  const optionalColumn = '@max-5xl/table:hidden';
  // While the table is scrolled sideways, the name column has an edge, so cells sliding under it do not show beside it.
  let scrolledSideways = $state(false);
  const nameEdge = 'after:absolute after:inset-y-0 after:right-0 after:w-px after:bg-border';

  const table = createTable({
    ...tableSortOptions(() => sort),
    features: candidateTableFeatures,
    get columns() {
      return columns;
    },
    get data() {
      return applications as ApplicationSummary[];
    },
    getRowId: (application) => application.id,
  });

  const rows = $derived(table.getRowModel().rows);
  let scrollElement = $state<HTMLDivElement>();
  const virtualizer = createVirtualizer<HTMLDivElement, HTMLTableRowElement>({
    count: 0,
    getScrollElement: () => scrollElement ?? null,
    estimateSize: () => candidateRowHeight,
    overscan: 8,
  });
  // The store adapter does not track getter options, so push the row count and the scroll element.
  $effect(() => {
    get(virtualizer).setOptions({
      ...get(virtualizer).options,
      count: rows.length,
      getScrollElement: () => scrollElement ?? null,
      getItemKey: (index) => rows[index]?.id ?? index,
    });
  });

  const items = $derived($virtualizer.getVirtualItems());
  const paddingTop = $derived(items[0]?.start ?? 0);
  const paddingBottom = $derived(items.length > 0 ? $virtualizer.getTotalSize() - items[items.length - 1]!.end : 0);

  const activeIndex = $derived(rows.findIndex((row) => row.id === activeId));
  const allSelected = $derived(applications.length > 0 && applications.every((application) => selectedIds.includes(application.id)));

  function toggle(id: string) {
    selectedIds = selectedIds.includes(id) ? selectedIds.filter((selected) => selected !== id) : [...selectedIds, id];
  }

  function toggleAll() {
    selectedIds = allSelected ? [] : applications.map((application) => application.id);
  }

  // J / K move the cursor and focus the row's link, so a screen reader follows and Enter opens it; a virtual row is
  // scrolled into view (and so rendered) first.
  async function moveCursor(index: number) {
    const target = rows[index];
    if (!target) return;
    activeId = target.id;
    get(virtualizer).scrollToIndex(index, { align: 'auto' });
    for (let attempt = 0; attempt < 5; attempt++) {
      await tick();
      const link = scrollElement?.querySelector<HTMLElement>(`[data-row-id="${target.id}"] [data-row-link]`);
      if (link) {
        link.focus({ preventScroll: true });
        return;
      }
      await new Promise((resolve) => requestAnimationFrame(resolve));
    }
  }

  function onkeydown(event: KeyboardEvent) {
    if (!preferences.shortcutsEnabled || event.defaultPrevented || hasModifier(event) || isTypingTarget(event.target) || rows.length === 0) return;
    const key = event.key.toLowerCase();
    if (key === 'j') {
      event.preventDefault();
      void moveCursor(Math.min(rows.length - 1, activeIndex + 1));
    } else if (key === 'k') {
      event.preventDefault();
      void moveCursor(Math.max(0, activeIndex - 1));
    } else if (key === 'x' && activeId) {
      event.preventDefault();
      toggle(activeId);
    }
  }

  // Clicking or tabbing into a row makes it the cursor's row.
  function onfocusin(event: FocusEvent) {
    const row = (event.target as Element | null)?.closest<HTMLElement>('[data-row-id]');
    if (row?.dataset.rowId) activeId = row.dataset.rowId;
  }
</script>

{#snippet selectHeader()}
  <!-- The label is the hit area, sized for mouse and touch (design system F4, section 17). -->
  <label class="inline-grid size-6 cursor-pointer place-items-center pointer-coarse:size-11">
    <input
      type="checkbox"
      class="size-4 accent-primary"
      aria-label="Select all candidates"
      checked={allSelected}
      onchange={toggleAll}
    />
  </label>
{/snippet}

{#snippet selectCell(application: ApplicationSummary)}
  <!-- The label is the hit area, sized for mouse and touch (design system F4, section 17). -->
  <label class="inline-grid size-6 cursor-pointer place-items-center pointer-coarse:size-11">
    <input
      type="checkbox"
      class="size-4 accent-primary"
      aria-label={`Select ${application.candidateName}`}
      checked={selectedIds.includes(application.id)}
      onchange={() => toggle(application.id)}
    />
  </label>
{/snippet}

{#snippet candidateCell(application: ApplicationSummary)}
  <span class="flex items-center gap-2.5">
    <span
      aria-hidden="true"
      class="grid size-7 shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
      >{initials(application.candidateName)}</span
    >
    <TruncatedText
      data-row-link
      href={`/applications/${application.id}`}
      text={application.candidateName}
      class="max-w-56 font-semibold text-foreground no-underline hover:underline pointer-coarse:-my-3.5 pointer-coarse:py-3.5"
    />
  </span>
{/snippet}

{#snippet jobCell(application: ApplicationSummary)}
  <TruncatedText
    href={`/jobs/${application.jobId}`}
    text={application.jobTitle}
    class="max-w-64 text-secondary-foreground no-underline hover:underline pointer-coarse:-my-3.5 pointer-coarse:py-3.5"
  />
{/snippet}

{#snippet stageCell(application: ApplicationSummary)}
  <!-- Overdue is a status, so it comes first in the cell (T2); the stage it is overdue in follows as plain text, with no
       pill (L1). -->
  <span class="flex min-w-0 items-center gap-1.5">
    <OverdueBadge dueAt={application.slaDueAt} />
    <TruncatedText text={application.stage.name} class="max-w-48" />
  </span>
{/snippet}

{#snippet sourceCell(application: ApplicationSummary)}
  <span class="text-muted-foreground">{sourceLabel(application.source)}</span>
{/snippet}

{#snippet appliedCell(application: ApplicationSummary)}
  <DateText at={application.submittedAt} class="text-muted-foreground" />
{/snippet}

{#snippet lastActivityCell(application: ApplicationSummary)}
  <DateText at={application.lastActivityAt} class="text-muted-foreground" />
{/snippet}

{#snippet moveHeader()}
  <span class="sr-only">Move</span>
{/snippet}

{#snippet moveCell(application: ApplicationSummary)}
  {#if stages && moves?.allowed}
    <MoveToStage
      compact
      candidateName={application.candidateName}
      currentStageId={application.stage.id}
      {stages}
      onmove={(stage) => moves.move(application, stage)}
    />
  {/if}
{/snippet}

{#snippet listItem(application: ApplicationSummary)}
  {@const authenticity = chipOf(application.integrity, 'authenticity')}
  {@const ai = chipOf(application.integrity, 'aiAssistance')}
  <ListItem href={`/applications/${application.id}`} title={application.candidateName}>
    {#snippet status()}
      <IntegrityChip
        kind="authenticity"
        compact
        level={authenticity?.level ?? null}
        reasons={authenticity?.reasons ?? 0}
        dismissed={authenticity?.dismissed ?? false}
      />
    {/snippet}
    {#snippet meta()}
      <DateText at={application.submittedAt} />
    {/snippet}
    {#snippet facts()}
      <!-- relative: above the item's link, so the due time's tooltip opens on tap. -->
      <OverdueBadge dueAt={application.slaDueAt} class="relative" />
      <Badge variant="muted">{application.stage.name}</Badge>
      {#if showJob}
        <!-- relative: above the item's link, so the full title's tooltip opens on tap. -->
        <Badge variant="muted" class="relative max-w-full"><TruncatedText text={application.jobTitle} /></Badge>
      {/if}
      {#if ai && ai.level.rank > 0}
        <IntegrityChip kind="aiAssistance" compact level={ai.level} reasons={ai.reasons} dismissed={ai.dismissed} />
      {/if}
      {#if application.rating?.state === 'Rated' || application.rating?.state === 'AfterYourScorecard'}
        <!-- relative: above the item's link, so the count's or "After yours"'s tooltip opens on tap. -->
        <span class="relative"><Rating rating={application.rating} /></span>
      {/if}
      {#if stages && moves?.allowed}
        <span class="relative ml-auto">{@render moveCell(application)}</span>
      {/if}
    {/snippet}
  </ListItem>
{/snippet}

{#snippet empty()}
  <Empty.Root class="gap-3 p-6">
    <Empty.Description>{emptyText}</Empty.Description>
    {#if onclearfilters}
      <Button variant="outline" size="lg" onclick={onclearfilters}>Clear filters</Button>
    {/if}
  </Empty.Root>
{/snippet}

{#if phone.current}
  <!-- List items have no headers, so the Sort chip offers the headers' choices, beside the page's filters (L2). The row
       stays while a filter leaves nothing to list, so the filter can still change. The filters scroll rather than wrap
       (F5), and the Sort chip stays in view at the end. -->
  <div class="flex items-center gap-2 border-b border-border-subtle px-4 py-2">
    <div class="flex min-w-0 flex-1 items-center gap-2 overflow-x-auto">{@render filters?.()}</div>
    <SortMenu {table} class="shrink-0" />
  </div>
  {#if rows.length === 0}
    {@render empty()}
  {:else}
    <ul aria-label={label}>
      {#each rows as row (row.id)}
        {@render listItem(row.original)}
      {/each}
    </ul>
  {/if}
{:else}
  <!-- svelte-ignore a11y_no_static_element_interactions: the keys are shortcuts for the focusable rows inside, which bubble here -->
  <div class={['flex flex-col', fill && 'min-h-0 flex-1']} {onkeydown} {onfocusin}>
    <!-- relative: the containing block for the chips' absolutely placed sr-only labels, so this scroller clips them
         instead of the page scrolling sideways on a phone (F1). On a tablet the table scrolls sideways here, with the
         checkbox and name columns stuck to its left edge (section 8). -->
    <div
      bind:this={scrollElement}
      onscroll={(event) => (scrolledSideways = event.currentTarget.scrollLeft > 0)}
      class={['@container/table relative overflow-auto', fill ? 'min-h-0 flex-1' : 'max-h-[min(70vh,640px)]']}
    >
      <table aria-label={label} aria-rowcount={rows.length + 1} class="w-full border-collapse text-left">
        <thead class="sticky top-0 z-10 bg-muted">
          {#each table.getHeaderGroups() as group (group.id)}
            <tr>
              {#each group.headers as header (header.id)}
                <SortHeader
                  {header}
                  class={[
                    header.id === 'select' ? selectColumn : 'px-3',
                    header.id === 'candidate' && nameColumn,
                    header.id === 'candidate' && scrolledSideways && nameEdge,
                    (header.id === 'select' || header.id === 'candidate') && 'z-[1] bg-muted',
                    header.id === 'move' && 'w-10',
                    header.id === 'source' && optionalColumn,
                    (header.id === 'applied' || header.id === 'lastActivity' || header.id === 'move') && 'text-right',
                  ]}
                />
              {/each}
            </tr>
          {/each}
        </thead>
        <tbody>
          {#if paddingTop > 0}
            <tr aria-hidden="true"><td colspan={columns.length} style:height={`${paddingTop}px`} class="p-0"></td></tr>
          {/if}
          {#each items as item (item.key)}
            {@const row = rows[item.index]}
            {#if row}
              {@const tinted = row.id === activeId || selectedIds.includes(row.id)}
              <tr data-row-id={row.id} aria-rowindex={item.index + 2} class={['group h-12', tinted ? 'bg-row-selected' : 'hover:bg-muted']}>
                {#each row.getAllCells() as cell (cell.id)}
                  <td
                    class={[
                      'border-b border-border-subtle py-0 whitespace-nowrap',
                      cell.column.id === 'select' ? selectColumn : 'px-3',
                      cell.column.id === 'candidate' && nameColumn,
                      cell.column.id === 'candidate' && scrolledSideways && nameEdge,
                      // A stuck cell covers what scrolls under it, so it carries the row's tint itself.
                      (cell.column.id === 'select' || cell.column.id === 'candidate') && ['z-[1]', tinted ? 'bg-row-selected' : 'bg-card group-hover:bg-muted'],
                      (cell.column.id === 'applied' || cell.column.id === 'lastActivity' || cell.column.id === 'move') && 'text-right',
                      cell.column.id === 'source' && optionalColumn,
                    ]}
                  >
                    <FlexRender {cell} />
                  </td>
                {/each}
              </tr>
            {/if}
          {/each}
          {#if paddingBottom > 0}
            <tr aria-hidden="true"><td colspan={columns.length} style:height={`${paddingBottom}px`} class="p-0"></td></tr>
          {/if}
        </tbody>
      </table>
    </div>
    <!-- Outside the scroller, so a table wider than its panel never pushes the message out of view (F1). -->
    {#if rows.length === 0}{@render empty()}{/if}
  </div>
{/if}
