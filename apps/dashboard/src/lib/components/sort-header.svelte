<script lang="ts" generics="TFeatures extends TableFeatures, TData extends RowData">
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import ChevronsUpDownIcon from '@lucide/svelte/icons/chevrons-up-down';
  import { FlexRender, type Column_RowSorting, type Header, type RowData, type TableFeatures } from '@tanstack/svelte-table';
  import type { ClassValue } from 'svelte/elements';
  import { cn } from '#lib/utils.js';

  // A TanStack table's column header (design system L2). A sortable column's header is the one bare text button (A6): its
  // text and a faint up-down icon, which becomes an arrow on the sorted column, with `aria-sort` on that header. With
  // `tableSortOptions` (#lib/table-sort.svelte.ts) a second click reverses the sort. Other columns render their header.
  let {
    header,
    class: className,
  }: {
    // `any`: the header's value type, which differs from column to column.
    header: Header<TFeatures, TData, any>;
    class?: ClassValue;
  } = $props();

  // A table without the sorting feature has no sort API, and no column of it sorts.
  const column = $derived(header.column as unknown as Partial<Column_RowSorting<TFeatures, TData>>);
  const canSort = $derived(column.getCanSort?.() ?? false);
  const sorted = $derived(canSort ? column.getIsSorted!() : false);
</script>

<th
  scope="col"
  aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
  class={cn('h-11 border-b border-border px-3 py-0 text-left text-caption font-semibold whitespace-nowrap text-muted-foreground', className)}
>
  {#if header.isPlaceholder}{:else if canSort}
    <button
      type="button"
      onclick={column.getToggleSortingHandler?.()}
      class={[
        '-mx-1.5 inline-flex h-7 max-w-[calc(100%+0.75rem)] items-center gap-1 rounded-md px-1.5 hover:bg-secondary hover:text-foreground pointer-coarse:h-11',
        sorted && 'text-foreground',
      ]}
    >
      <span class="min-w-0 truncate"><FlexRender {header} /></span>
      {#if sorted === 'desc'}
        <ArrowDownIcon aria-hidden="true" class="size-3 shrink-0" />
      {:else if sorted === 'asc'}
        <ArrowUpIcon aria-hidden="true" class="size-3 shrink-0" />
      {:else}
        <ChevronsUpDownIcon aria-hidden="true" class="size-3 shrink-0 opacity-60" />
      {/if}
    </button>
  {:else}
    <FlexRender {header} />
  {/if}
</th>
