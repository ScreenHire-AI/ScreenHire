<script lang="ts">
  import { createColumnHelper, createSortedRowModel, createTable, FlexRender, type ColumnDef, renderSnippet, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures } from '@tanstack/svelte-table';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // A sortable table (L1, L2) of hiring templates, as the Templates page lists them: the name with Built in after it, then
  // plain text. It opens newest first by Last updated and remembers the person's sort under `style-guide`.
  type Template = { id: string; name: string; builtIn: boolean; jobs: number; updatedAt: string };

  const now = Date.now();
  const day = 86_400_000;
  const templates: Template[] = [
    { id: 't1', name: 'Brightwater engineering', builtIn: false, jobs: 3, updatedAt: new Date(now - day).toISOString() },
    { id: 't2', name: 'Hourly role', builtIn: true, jobs: 2, updatedAt: new Date(now - 6 * day).toISOString() },
    { id: 't3', name: 'Software engineer', builtIn: true, jobs: 1, updatedAt: new Date(now - 6 * day - 3_600_000).toISOString() },
    { id: 't4', name: 'Summer interns 2025', builtIn: false, jobs: 0, updatedAt: new Date(now - 520 * day).toISOString() },
  ];

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, Template>();
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, Template, any>[] = [
    column.accessor('name', { header: 'Name', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(nameCell, row.original) }),
    column.accessor('jobs', { header: 'Used by', sortFn: 'basic', cell: ({ row }) => renderSnippet(usedByCell, row.original.jobs) }),
    column.accessor('updatedAt', { header: 'Last updated', sortFn: 'basic', sortDescFirst: true, cell: ({ row }) => renderSnippet(updatedCell, row.original.updatedAt) }),
  ];

  const sort = createTableSort('style-guide', { id: 'updatedAt', desc: true }, ['name', 'jobs', 'updatedAt']);
  const table = createTable({ ...tableSortOptions(() => sort), features, columns, data: templates, getRowId: (template) => template.id });
</script>

{#snippet nameCell(template: Template)}
  <span class="flex min-w-0 items-center gap-2">
    <a href="#sort-header" class="truncate font-semibold text-foreground no-underline hover:underline">{template.name}</a>
    {#if template.builtIn}<BuiltInBadge />{/if}
  </span>
{/snippet}
{#snippet usedByCell(jobs: number)}<span class={jobs === 0 ? 'text-muted-foreground' : ''}>{jobs === 0 ? 'None' : jobs === 1 ? '1 job' : `${jobs} jobs`}</span>{/snippet}
{#snippet updatedCell(at: string)}<DateText {at} />{/snippet}

<div class="flex flex-col">
  <div class="overflow-x-auto">
    <table aria-label="Hiring templates" class="w-full border-collapse text-left">
      <thead class="bg-muted">
        {#each table.getHeaderGroups() as group (group.id)}
          <tr>
            {#each group.headers as header (header.id)}
              <SortHeader {header} class={header.id === 'name' ? 'px-4.5' : undefined} />
            {/each}
          </tr>
        {/each}
      </thead>
      <tbody>
        {#each table.getRowModel().rows as row (row.id)}
          <tr class="h-12 border-b border-border-subtle last:border-b-0">
            {#each row.getAllCells() as cell (cell.id)}
              <td class={['px-3 whitespace-nowrap', cell.column.id === 'name' && 'px-4.5']}><FlexRender {cell} /></td>
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
  <figure class="flex flex-col gap-2 border-t border-border-subtle px-4.5 py-4 max-md:px-4">
    <figcaption class="text-caption text-muted-foreground">On a phone, the Sort chip beside the filters offers the same choices</figcaption>
    <div><SortMenu {table} /></div>
  </figure>
</div>
