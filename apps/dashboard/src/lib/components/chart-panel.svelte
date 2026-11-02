<script lang="ts" module>
  /** A column of a chart's table view: its header, its text and the value it sorts by. */
  export interface ChartColumn<T> {
    id: string;
    header: string;
    text: (row: T) => string;
    /** What screen readers hear for a cell whose text is empty, which shows blank (L1). */
    blank?: string;
    /** What the column sorts by; a number sorts biggest first on its first click. */
    sortBy: (row: T) => number | string;
    /** The value in a CSV export, when it is not the cell's text ("21 days" is 21); the text when absent. */
    csv?: (row: T) => string | number | null;
    /** Makes the cell a link; only the first column (the row's name) takes one. */
    href?: (row: T) => string;
  }
</script>

<script lang="ts" generics="T extends RowData">
  import ChartBarIcon from '~icons/lucide/chart-bar';
  import TableIcon from '~icons/lucide/table';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef, type RowData } from '@tanstack/svelte-table';
  import { untrack, type Snippet } from 'svelte';
  import Panel from '#lib/components/panel.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import * as Table from '#lib/components/ui/table/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import type { TableSort } from '#lib/preferences.svelte.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { cn } from '#lib/utils.js';

  // A report's panel (design system section 11, Chart panel): the title, a Chart / Table switch on the header's right,
  // then the chart or the same numbers as a table. No sentence sits under the title (W9); what the numbers count is read
  // out to screen readers only. Every chart has the table (WCAG 1.1.1), and the table sorts like every other (L2),
  // remembered per table. The first column is the row's name. A panel with no `chart` is a table alone, with no switch.
  // `empty` replaces both views when there is nothing to show; `busy` dims them while new numbers load, keeping the frame.
  let {
    title,
    basis,
    tableId,
    columns,
    rows,
    rowKey,
    defaultSort,
    busy = false,
    chart,
    empty,
    class: className,
  }: {
    title: string;
    /** What the numbers count, in one line, for screen readers ("Applications that arrived in these dates"). */
    basis?: string;
    /** The table's name for its remembered sort (`reports-funnel`). */
    tableId: string;
    columns: readonly ChartColumn<T>[];
    rows: readonly T[];
    rowKey: (row: T) => string;
    /** The table's opening sort; none keeps the rows' own order (a funnel's pipeline order). */
    defaultSort?: TableSort;
    busy?: boolean;
    chart?: Snippet;
    /** Shown instead of the chart and the table when there are no rows. */
    empty?: Snippet;
    class?: string;
  } = $props();

  let view = $state<'chart' | 'table'>('chart');

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: each column's value type differs.
  const tableColumns = $derived<ColumnDef<typeof features, T, any>[]>(
    columns.map((column, index) => ({
      id: column.id,
      header: column.header,
      accessorFn: column.sortBy,
      sortFn: index === 0 ? 'alphanumeric' : 'basic',
      sortDescFirst: index > 0,
    })),
  );
  // A table keeps the sort it opened with; its id, default and columns are fixed for the panel.
  const sort = untrack(() => createTableSort(tableId, defaultSort, columns.map((column) => column.id)));
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    get columns() {
      return tableColumns;
    },
    get data() {
      return rows as T[];
    },
    getRowId: (row) => rowKey(row),
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
</script>

<Panel {title} class={className} aria-busy={busy || undefined}>
  {#snippet actions()}
    {#if chart}
      <div role="group" aria-label="View" class="flex gap-1">
        <Toggle bind:pressed={() => view === 'chart', () => (view = 'chart')}><ChartBarIcon aria-hidden="true" />Chart</Toggle>
        <Toggle bind:pressed={() => view === 'table', () => (view = 'table')}><TableIcon aria-hidden="true" />Table</Toggle>
      </div>
    {/if}
  {/snippet}
  <div class={cn('flex min-w-0 flex-col gap-4 transition-opacity motion-reduce:transition-none', busy && 'opacity-60')}>
    {#if basis}<p class="sr-only">{basis}</p>{/if}
    {#if rows.length === 0 && empty}
      {@render empty()}
    {:else if chart && view === 'chart'}
      {@render chart()}
    {:else}
      <div class="-mx-4.5 overflow-x-auto max-md:-mx-4">
        <Table.Root>
          <Table.Header>
            {#each table.getHeaderGroups() as group (group.id)}
              <Table.Row class="bg-muted hover:bg-muted">
                {#each group.headers as header, index (header.id)}
                  <SortHeader {header} class={index === 0 ? 'px-4.5 max-md:px-4' : undefined} />
                {/each}
              </Table.Row>
            {/each}
          </Table.Header>
          <Table.Body>
            {#each sorted as row (rowKey(row))}
              <Table.Row class="h-12">
                {#each columns as column, index (column.id)}
                  {#if index === 0}
                    <Table.Cell class="w-full max-w-0 min-w-40 px-4.5 font-semibold max-md:px-4">
                      {#if column.href}
                        <a href={column.href(row)} class="inline-flex min-h-6 max-w-full min-w-0 items-center text-primary underline underline-offset-2 pointer-coarse:min-h-11"
                          ><TruncatedText text={column.text(row)} /></a
                        >
                      {:else}
                        <span class="flex min-w-0"><TruncatedText text={column.text(row)} /></span>
                      {/if}
                    </Table.Cell>
                  {:else}
                    {@const text = column.text(row)}
                    <Table.Cell class="whitespace-nowrap tabular-nums">
                      {#if text}{text}{:else}<span class="sr-only">{column.blank ?? 'None'}</span>{/if}
                    </Table.Cell>
                  {/if}
                {/each}
              </Table.Row>
            {/each}
          </Table.Body>
        </Table.Root>
      </div>
    {/if}
  </div>
</Panel>
