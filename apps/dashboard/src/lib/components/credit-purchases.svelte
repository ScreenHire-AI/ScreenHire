<script lang="ts">
  import type { AiCreditPurchase } from '@screenhire/api-client';
  import { createListAiCreditPurchases } from '@screenhire/api-client/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import DateText from '#lib/components/date-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { creditPurchaseStatus, creditSourceLabels, formatCredits } from '#lib/billing.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { describeError } from '#lib/query.js';

  // The credits added, for an Owner (docs/architecture/billing.md, "AI credits"): the starting credit, each purchase and auto-reload, and
  // a reload whose charge was refused, newest first. A Checkout left unpaid is not a purchase and is not listed.
  const purchases = createListAiCreditPurchases(() => ({ query: { refetchOnMount: 'always' } }));
  const list = $derived(purchases.data?.data.purchases ?? []);

  // The table sorts (L2), newest first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, AiCreditPurchase, any>[] = [
    { id: 'date', header: 'Date', accessorFn: (row) => row.date, sortFn: 'basic', sortDescFirst: true },
    { id: 'source', header: 'From', accessorFn: (row) => creditSourceLabels[row.source], sortFn: 'alphanumeric' },
    { id: 'amount', header: 'Amount', accessorFn: (row) => row.amount, sortFn: 'basic', sortDescFirst: true },
    { id: 'status', header: 'Status', accessorFn: (row) => creditPurchaseStatus[row.status].label, sortFn: 'alphanumeric' },
    { id: 'expires', header: 'Expires', accessorFn: (row) => row.expiresAt ?? '', sortFn: 'basic' },
  ];
  const sort = createTableSort('credit-purchases', { id: 'date', desc: true }, ['date', 'source', 'amount', 'status', 'expires']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (row) => row.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
</script>

<Panel title="Purchases" count={purchases.data ? list.length : undefined} flush={list.length > 0}>
  {#if purchases.isError && !purchases.data}
    <QueryError message={describeError(purchases.error, 'Could not load the purchases')} onretry={() => purchases.refetch()} inset />
  {:else if !purchases.data}
    <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-full" /><Skeleton class="h-5 w-full" /></div>
    <p class="sr-only" role="status">Loading the purchases…</p>
  {:else if list.length === 0}
    <p class="text-muted-foreground">No credits bought yet.</p>
  {:else}
    <div class="overflow-x-auto">
      <Table.Root label="Purchases">
        <Table.Header>
          {#each table.getHeaderGroups() as group (group.id)}
            <Table.Row class="bg-muted hover:bg-muted">
              {#each group.headers as header (header.id)}
                <SortHeader {header} class={header.id === 'amount' ? 'text-right' : undefined} />
              {/each}
            </Table.Row>
          {/each}
        </Table.Header>
        <Table.Body>
          {#each sorted as row (row.id)}
            {@const state = creditPurchaseStatus[row.status]}
            <Table.Row class="h-12">
              <Table.Cell class="font-semibold"><DateText at={row.date} /></Table.Cell>
              <Table.Cell>{creditSourceLabels[row.source]}</Table.Cell>
              <Table.Cell class="text-right tabular-nums">{formatCredits(row.amount)}</Table.Cell>
              <Table.Cell><StatusBadge status={state.status} label={state.label} quiet /></Table.Cell>
              <Table.Cell class="text-muted-foreground">
                {#if row.expiresAt}<DateText at={row.expiresAt} />{:else if row.status === 'Failed'}<span class="sr-only">Nothing added</span>{:else}Never{/if}
              </Table.Cell>
            </Table.Row>
          {/each}
        </Table.Body>
      </Table.Root>
    </div>
  {/if}
</Panel>
