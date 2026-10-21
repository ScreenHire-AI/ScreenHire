<script lang="ts">
  import type { ApplicationSummary, WorkflowStage } from '@screenhire/api-client';
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import ChevronsUpDownIcon from '@lucide/svelte/icons/chevrons-up-down';
  import { createTable, FlexRender, renderSnippet } from '@tanstack/svelte-table';
  import { createVirtualizer } from '@tanstack/svelte-virtual';
  import { tick, type Snippet } from 'svelte';
  import { get } from 'svelte/store';
  import Hint from '#lib/components/hint.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { candidateRowHeight, candidateTableFeatures, type CandidateColumn } from '#lib/candidate-table.js';
  import { formatDate, formatDateTime } from '#lib/dates.js';
  import { initials, sourceLabel } from '#lib/format.js';
  import { IsMobile } from '#lib/hooks/is-mobile.svelte.js';
  import { chipOf } from '#lib/integrity.js';
  import type { Moves } from '#lib/moves.svelte.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { hasModifier, isTypingTarget } from '#lib/shortcuts.js';

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
  }: {
    applications: readonly ApplicationSummary[];
    /** The table's accessible name. */
    label?: string;
    /** Adds a Job column, for the table across every job. */
    showJob?: boolean;
    /** With `moves`: the job's stages, for each row's "Move" menu. */
    stages?: readonly WorkflowStage[];
    moves?: Moves;
    /** Columns between Stage and Source: the slot the integrity columns fill (#lib/candidate-table.ts). */
    extraColumns?: readonly CandidateColumn[];
    /** The keyboard cursor's row (J / K), tinted; the job page shows it in its side panel. */
    activeId?: string | null;
    /** Rows ticked with X or their checkbox. */
    selectedIds?: string[];
    emptyText?: string;
    /** While the page filters the rows: the empty state's one action (E1). */
    onclearfilters?: () => void;
  } = $props();

  const columns = $derived<CandidateColumn[]>([
    { id: 'select', header: () => renderSnippet(selectHeader, undefined), cell: ({ row }) => renderSnippet(selectCell, row.original) },
    { id: 'candidate', header: 'Candidate', cell: ({ row }) => renderSnippet(candidateCell, row.original) },
    ...(showJob ? [{ id: 'job', header: 'Job', cell: ({ row }) => renderSnippet(jobCell, row.original) } satisfies CandidateColumn] : []),
    { id: 'stage', header: 'Stage', accessorFn: (application) => application.stage.name, enableSorting: false, cell: ({ row }) => renderSnippet(stageCell, row.original) },
    ...extraColumns,
    { id: 'source', header: 'Source', cell: ({ row }) => renderSnippet(sourceCell, row.original) },
    { id: 'applied', header: 'Applied', cell: ({ row }) => renderSnippet(appliedCell, row.original) },
    ...(stages && moves ? [{ id: 'move', header: () => renderSnippet(moveHeader, undefined), cell: ({ row }) => renderSnippet(moveCell, row.original) } satisfies CandidateColumn] : []),
  ]);

  // Phones get two-line list items in place of the table (design system section 8), at the shell's own phone width.
  const phone = new IsMobile();
  // The checkbox and name columns stay put while a tablet scrolls the table sideways (design system section 8). The
  // checkbox column's width is fixed, so the name column knows where to stick: its 24px hit area (44px on touch) plus
  // padding.
  const selectColumn = 'sticky left-0 w-10 min-w-10 px-2 pointer-coarse:w-15 pointer-coarse:min-w-15';
  const nameColumn = 'sticky left-10 pointer-coarse:left-15';
  // While the table is scrolled sideways, the name column has an edge, so cells sliding under it do not show beside it.
  let scrolledSideways = $state(false);
  const nameEdge = 'after:absolute after:inset-y-0 after:right-0 after:w-px after:bg-border';

  const table = createTable({
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
      class="max-w-56 font-bold text-foreground no-underline hover:underline pointer-coarse:-my-3.5 pointer-coarse:py-3.5"
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
  <Badge variant="muted">{application.stage.name}</Badge>
{/snippet}

{#snippet sourceCell(application: ApplicationSummary)}
  <span class="text-muted-foreground">{sourceLabel(application.source)}</span>
{/snippet}

{#snippet appliedCell(application: ApplicationSummary)}
  <!-- A date in the table, the exact time on hover, focus or tap (design system section 13). -->
  <Hint content={formatDateTime(application.submittedAt)}>
    {#snippet children({ props })}
      <time {...props} datetime={application.submittedAt} class="text-muted-foreground tabular-nums">{formatDate(application.submittedAt)}</time>
    {/snippet}
  </Hint>
{/snippet}

{#snippet moveHeader()}
  <span class="sr-only">Move</span>
{/snippet}

{#snippet moveCell(application: ApplicationSummary)}
  {#if stages && moves}
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
      <Hint content={formatDateTime(application.submittedAt)}>
        {#snippet children({ props })}
          <time {...props} datetime={application.submittedAt} class="tabular-nums">{formatDate(application.submittedAt)}</time>
        {/snippet}
      </Hint>
    {/snippet}
    {#snippet facts()}
      <Badge variant="muted">{application.stage.name}</Badge>
      {#if showJob}
        <!-- relative: above the item's link, so the full title's tooltip opens on tap. -->
        <Badge variant="muted" class="relative max-w-full"><TruncatedText text={application.jobTitle} /></Badge>
      {/if}
      {#if ai && ai.level.rank > 0}
        <IntegrityChip kind="aiAssistance" compact level={ai.level} reasons={ai.reasons} dismissed={ai.dismissed} />
      {/if}
      {#if stages && moves}
        <span class="relative ml-auto">{@render moveCell(application)}</span>
      {/if}
    {/snippet}
  </ListItem>
{/snippet}

{#snippet empty()}
  <Empty.Root class="gap-3 p-6">
    <Empty.Description>{emptyText}</Empty.Description>
    {#if onclearfilters}
      <Button variant="outline" onclick={onclearfilters}>Clear filters</Button>
    {/if}
  </Empty.Root>
{/snippet}

{#if phone.current}
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
  <div class="flex flex-col" {onkeydown} {onfocusin}>
    <!-- relative: the containing block for the chips' absolutely placed sr-only labels, so this scroller clips them
         instead of the page scrolling sideways on a phone (F1). On a tablet the table scrolls sideways here, with the
         checkbox and name columns stuck to its left edge (section 8). -->
    <div
      bind:this={scrollElement}
      onscroll={(event) => (scrolledSideways = event.currentTarget.scrollLeft > 0)}
      class="relative max-h-[min(70vh,640px)] overflow-auto"
    >
      <table aria-label={label} aria-rowcount={rows.length + 1} class="w-full border-collapse text-left">
        <thead class="sticky top-0 z-10 bg-muted">
          {#each table.getHeaderGroups() as group (group.id)}
            <tr>
              {#each group.headers as header (header.id)}
                {@const sorted = header.column.getCanSort() ? header.column.getIsSorted() : false}
                <th
                  scope="col"
                  aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
                  class={[
                    'h-11 border-b border-border py-0 text-caption whitespace-nowrap text-muted-foreground',
                    header.id === 'select' ? selectColumn : 'px-3',
                    header.id === 'candidate' && nameColumn,
                    header.id === 'candidate' && scrolledSideways && nameEdge,
                    (header.id === 'select' || header.id === 'candidate') && 'z-[1] bg-muted',
                    header.id === 'move' && 'w-10',
                    (header.id === 'applied' || header.id === 'move') && 'text-right',
                  ]}
                >
                  {#if header.isPlaceholder}{:else if header.column.getCanSort()}
                    <!-- Each press cycles the sort (the column says which way comes first), then back to the API's order. aria-sort on the header tells screen readers which way it is sorted. -->
                    <button
                      type="button"
                      onclick={header.column.getToggleSortingHandler()}
                      class="-mx-1 inline-flex h-6 items-center gap-1 rounded-sm px-1 hover:text-foreground pointer-coarse:h-11"
                    >
                      <FlexRender {header} />
                      {#if sorted === 'desc'}
                        <ArrowDownIcon aria-hidden="true" class="size-3" />
                      {:else if sorted === 'asc'}
                        <ArrowUpIcon aria-hidden="true" class="size-3" />
                      {:else}
                        <ChevronsUpDownIcon aria-hidden="true" class="size-3 opacity-60" />
                      {/if}
                    </button>
                  {:else}
                    <FlexRender {header} />
                  {/if}
                </th>
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
                      (cell.column.id === 'applied' || cell.column.id === 'move') && 'text-right',
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
