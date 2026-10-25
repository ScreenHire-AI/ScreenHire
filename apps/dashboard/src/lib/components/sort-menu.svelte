<script lang="ts" generics="TFeatures extends TableFeatures, TData extends RowData">
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpDownIcon from '@lucide/svelte/icons/arrow-up-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import type { Column_Core, Column_RowSorting, RowData, Table, TableFeatures } from '@tanstack/svelte-table';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { cn } from '#lib/utils.js';

  // The Sort chip (design system L2): on a phone, where a table becomes list items with no headers, it sits beside the
  // filters and offers the same choices as the headers. Choosing the sorted column again reverses it, as a second click
  // on its header does.
  let {
    table,
    class: className,
  }: {
    table: Table<TFeatures, TData>;
    class?: string;
  } = $props();

  type SortableColumn = Column_Core<TFeatures, TData, unknown> & Column_RowSorting<TFeatures, TData>;

  // The table has the sorting feature, so its columns have the sort API.
  const columns = $derived((table.getAllLeafColumns() as unknown as SortableColumn[]).filter((column) => column.getCanSort()));
  const sorted = $derived(columns.find((column) => column.getIsSorted()));

  const name = (column: SortableColumn) => (typeof column.columnDef.header === 'string' ? column.columnDef.header : column.id);

  function choose(column: SortableColumn) {
    if (column.getIsSorted()) column.toggleSorting();
    else column.toggleSorting(column.getFirstSortDir() === 'desc');
  }
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger class={cn(buttonVariants({ variant: 'outline', size: 'lg' }), className)}>
    <ArrowUpDownIcon aria-hidden="true" />Sort
  </DropdownMenu.Trigger>
  <DropdownMenu.Content align="end" class="min-w-52">
    <DropdownMenu.RadioGroup value={sorted?.id ?? ''} aria-label="Sort by">
      {#each columns as column (column.id)}
        {@const direction = column.getIsSorted()}
        <DropdownMenu.RadioItem value={column.id} closeOnSelect onSelect={() => choose(column)}>
          <!-- One line, so the words read "Last updated, descending" with no gap before the comma. -->
          {name(column)}{#if direction === 'desc'}<ArrowDownIcon aria-hidden="true" class="size-3" /><span class="sr-only">, descending</span>{:else if direction === 'asc'}<ArrowUpIcon aria-hidden="true" class="size-3" /><span class="sr-only">, ascending</span>{/if}
        </DropdownMenu.RadioItem>
      {/each}
    </DropdownMenu.RadioGroup>
  </DropdownMenu.Content>
</DropdownMenu.Root>
