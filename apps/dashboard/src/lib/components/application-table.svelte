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
  import { Badge } from '#lib/components/ui/badge/index.js';
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
  } = $props();

  const columns = $derived<CandidateColumn[]>([
    { id: 'select', header: () => renderSnippet(selectHeader, undefined), cell: ({ row }) => renderSnippet(selectCell, row.original) },
    { id: 'candidate', header: 'Candidate', cell: ({ row }) => renderSnippet(candidateCell, row.original) },
    ...(showJob ? [{ id: 'job', header: 'Job', cell: ({ row }) => renderSnippet(jobCell, row.original) } satisfies CandidateColumn] : []),
    { id: 'stage', header: 'Stage', accessorFn: (application) => application.stage.name, enableSorting: false },
    ...extraColumns,
    { id: 'source', header: 'Source', cell: ({ row }) => renderSnippet(sourceCell, row.original) },
    { id: 'applied', header: 'Applied', cell: ({ row }) => renderSnippet(appliedCell, row.original) },
    ...(stages && moves ? [{ id: 'move', header: () => renderSnippet(moveHeader, undefined), cell: ({ row }) => renderSnippet(moveCell, row.original) } satisfies CandidateColumn] : []),
  ]);

  // Rows become two-line list items on phones (design system section 8): authenticity first, the name as the link,
  // facts as pills. Selection, J / K and the per-row Move are wide-screen tools; a phone moves from the application page.
  const phone = new IsMobile();

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
  <input
    type="checkbox"
    class="size-4 accent-primary"
    aria-label="Select all candidates"
    checked={allSelected}
    onchange={toggleAll}
  />
{/snippet}

{#snippet selectCell(application: ApplicationSummary)}
  <input
    type="checkbox"
    class="size-4 accent-primary"
    aria-label={`Select ${application.candidateName}`}
    checked={selectedIds.includes(application.id)}
    onchange={() => toggle(application.id)}
  />
{/snippet}

{#snippet candidateCell(application: ApplicationSummary)}
  <span class="flex items-center gap-2.5">
    <span
      aria-hidden="true"
      class="grid size-[26px] shrink-0 place-items-center rounded-full bg-secondary text-caption font-bold text-secondary-foreground"
      >{initials(application.candidateName)}</span
    >
    <a data-row-link href={`/applications/${application.id}`} class="font-bold text-foreground no-underline hover:underline"
      >{application.candidateName}</a
    >
  </span>
{/snippet}

{#snippet jobCell(application: ApplicationSummary)}
  <a href={`/jobs/${application.jobId}`} class="text-secondary-foreground no-underline hover:underline">{application.jobTitle}</a>
{/snippet}

{#snippet sourceCell(application: ApplicationSummary)}
  <span class="text-muted-foreground">{sourceLabel(application.source)}</span>
{/snippet}

{#snippet appliedCell(application: ApplicationSummary)}
  <time datetime={application.submittedAt} title={formatDateTime(application.submittedAt)} class="text-muted-foreground tabular-nums"
    >{formatDate(application.submittedAt)}</time
  >
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

{#if phone.current}
  {#if applications.length === 0}
    <p class="px-4 py-3 text-muted-foreground">{emptyText}</p>
  {:else}
    <ul aria-label={label}>
      {#each applications as application (application.id)}
        {@const chip = chipOf(application.integrity, 'authenticity')}
        <ListItem href={`/applications/${application.id}`} title={application.candidateName}>
          {#snippet status()}
            <IntegrityChip kind="authenticity" level={chip?.level ?? null} reasons={chip?.reasons ?? 0} dismissed={chip?.dismissed ?? false} compact />
          {/snippet}
          {#snippet meta()}
            <Hint content={formatDateTime(application.submittedAt)}>{#snippet children({ props })}<time {...props} datetime={application.submittedAt}>{formatDate(application.submittedAt)}</time>{/snippet}</Hint>
          {/snippet}
          {#snippet facts()}
            <Badge variant="muted">{application.stage.name}</Badge>
            {#if showJob}<Badge variant="muted" class="max-w-full"><span class="truncate">{application.jobTitle}</span></Badge>{/if}
            <Badge variant="muted">{sourceLabel(application.source)}</Badge>
          {/snippet}
        </ListItem>
      {/each}
    </ul>
  {/if}
{:else}
  <!-- svelte-ignore a11y_no_static_element_interactions: the keys are shortcuts for the focusable rows inside, which bubble here -->
  <div class="flex flex-col" {onkeydown} {onfocusin}>
    <div bind:this={scrollElement} class="max-h-[min(70vh,640px)] overflow-auto">
      <table aria-label={label} aria-rowcount={rows.length + 1} class="w-full border-collapse text-left">
        <thead class="sticky top-0 z-10 bg-card">
          {#each table.getHeaderGroups() as group (group.id)}
            <tr>
              {#each group.headers as header (header.id)}
                {@const sorted = header.column.getCanSort() ? header.column.getIsSorted() : false}
                <th
                  scope="col"
                  aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
                  class={[
                    'border-b border-border-subtle px-3 py-2 text-caption whitespace-nowrap text-muted-foreground',
                    (header.id === 'select' || header.id === 'move') && 'w-10',
                    (header.id === 'applied' || header.id === 'move') && 'text-right',
                  ]}
                >
                  {#if header.isPlaceholder}{:else if header.column.getCanSort()}
                    <!-- Each press cycles the sort (the column says which way comes first), then back to the API's order. aria-sort on the header tells screen readers which way it is sorted. -->
                    <button
                      type="button"
                      onclick={header.column.getToggleSortingHandler()}
                      class="-mx-1 inline-flex h-6 items-center gap-1 rounded-sm px-1 hover:text-foreground"
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
              <tr
                data-row-id={row.id}
                aria-rowindex={item.index + 2}
                class={['h-10', tinted ? 'bg-row-selected' : 'hover:bg-muted']}
              >
                {#each row.getAllCells() as cell (cell.id)}
                  <td
                    class={[
                      'border-b border-border-subtle px-3 py-0 whitespace-nowrap',
                      (cell.column.id === 'applied' || cell.column.id === 'move') && 'text-right',
                    ]}
                  >
                    <FlexRender {cell} />
                  </td>
                {/each}
              </tr>
            {/if}
          {:else}
            <tr><td colspan={columns.length} class="px-3 py-3 text-muted-foreground">{emptyText}</td></tr>
          {/each}
          {#if paddingBottom > 0}
            <tr aria-hidden="true"><td colspan={columns.length} style:height={`${paddingBottom}px`} class="p-0"></td></tr>
          {/if}
        </tbody>
      </table>
    </div>
  </div>
{/if}
