<script lang="ts">
  import { createColumnHelper, createSortedRowModel, createTable, FlexRender, type ColumnDef, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures } from '@tanstack/svelte-table';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // A small sortable table: Name and Last updated, opening newest first, remembered under `sortId`.
  let { sortId }: { sortId?: string } = $props();

  type Row = { id: string; name: string; updatedAt: string };
  const rows: Row[] = [
    { id: 'b', name: 'Brightwater engineering', updatedAt: '2026-10-24T10:00:00Z' },
    { id: 'h', name: 'Hourly role', updatedAt: '2026-10-19T10:00:00Z' },
    { id: 's', name: 'Software engineer', updatedAt: '2026-10-21T10:00:00Z' },
  ];
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, Row>();
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, Row, any>[] = [
    column.accessor('name', { header: 'Name', sortFn: 'alphanumeric' }),
    column.accessor('updatedAt', { header: 'Last updated', sortFn: 'basic', sortDescFirst: true }),
  ];
  // svelte-ignore state_referenced_locally: the table's name is fixed for the harness's life.
  const sort = createTableSort(sortId, { id: 'updatedAt', desc: true }, ['name', 'updatedAt']);
  const table = createTable({ ...tableSortOptions(() => sort), features, columns, data: rows, getRowId: (row) => row.id });
</script>

<table aria-label="Templates">
  <thead>
    {#each table.getHeaderGroups() as group (group.id)}
      <tr>
        {#each group.headers as header (header.id)}<SortHeader {header} />{/each}
      </tr>
    {/each}
  </thead>
  <tbody>
    {#each table.getRowModel().rows as row (row.id)}
      <tr>
        {#each row.getAllCells() as cell (cell.id)}<td><FlexRender {cell} /></td>{/each}
      </tr>
    {/each}
  </tbody>
</table>
<SortMenu {table} />
