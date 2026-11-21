<script lang="ts">
  import type { BillingInvoice } from '@screenhire/api-client';
  import { createListBillingInvoices } from '@screenhire/api-client/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import DateText from '#lib/components/date-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { formatMoney, invoiceStatus } from '#lib/billing.js';
  import { formatDate } from '#lib/dates.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { describeError } from '#lib/query.js';

  // The organization's invoices on the Billing page (docs/architecture/billing.md, "The Billing page"): date, amount, status and
  // the PDF, newest first, from the payment provider (or the local sandbox's samples). For an Owner once there is a customer.
  const invoices = createListBillingInvoices(() => ({ query: { refetchOnMount: 'always' } }));
  const list = $derived(invoices.data?.data.invoices ?? []);

  // The table sorts (L2), newest first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, BillingInvoice, any>[] = [
    { id: 'date', header: 'Date', accessorFn: (invoice) => invoice.date, sortFn: 'basic', sortDescFirst: true },
    { id: 'amount', header: 'Amount', accessorFn: (invoice) => invoice.amount, sortFn: 'basic', sortDescFirst: true },
    { id: 'status', header: 'Status', accessorFn: (invoice) => invoiceStatus[invoice.status].label, sortFn: 'alphanumeric' },
  ];
  const sort = createTableSort('billing-invoices', { id: 'date', desc: true }, ['date', 'amount', 'status']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (invoice) => invoice.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
</script>

<Panel title="Invoices" count={invoices.data ? list.length : undefined} flush={list.length > 0}>
  {#if invoices.isError && !invoices.data}
    <QueryError message={describeError(invoices.error, 'Could not load the invoices')} onretry={() => invoices.refetch()} inset />
  {:else if !invoices.data}
    <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-full" /><Skeleton class="h-5 w-full" /></div>
    <p class="sr-only" role="status">Loading the invoices…</p>
  {:else if list.length === 0}
    <p class="text-muted-foreground">No invoices yet. The first comes with your first payment.</p>
  {:else}
    <div class="overflow-x-auto">
      <Table.Root label="Invoices">
        <Table.Header>
          {#each table.getHeaderGroups() as group (group.id)}
            <Table.Row class="bg-muted hover:bg-muted">
              {#each group.headers as header (header.id)}
                <SortHeader {header} class={header.id === 'amount' ? 'text-right' : undefined} />
              {/each}
              <Table.Head><span class="sr-only">PDF</span></Table.Head>
            </Table.Row>
          {/each}
        </Table.Header>
        <Table.Body>
          {#each sorted as invoice (invoice.id)}
            {@const state = invoiceStatus[invoice.status]}
            <Table.Row class="h-12">
              <Table.Cell class="font-semibold"><DateText at={invoice.date} /></Table.Cell>
              <Table.Cell class="text-right tabular-nums">{formatMoney(invoice.amount, invoice.currency)}</Table.Cell>
              <Table.Cell><StatusBadge status={state.status} label={state.label} quiet /></Table.Cell>
              <Table.Cell class="text-right">
                {#if invoice.pdfUrl}
                  <a
                    href={invoice.pdfUrl}
                    target="_blank"
                    rel="noreferrer"
                    class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11"
                  >
                    PDF<span class="sr-only">, invoice of {formatDate(invoice.date)} (opens in a new tab)</span>
                  </a>
                {:else}
                  <span class="sr-only">No PDF</span>
                {/if}
              </Table.Cell>
            </Table.Row>
          {/each}
        </Table.Body>
      </Table.Root>
    </div>
  {/if}
</Panel>
