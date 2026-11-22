<script lang="ts">
  import type { AiCreditDeduction } from '@screenhire/api-client';
  import { createListAiCreditDeductions } from '@screenhire/api-client/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import DateText from '#lib/components/date-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { formatCost } from '#lib/ai-usage.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { describeError } from '#lib/query.js';

  // What took credits, each traceable to the check that used it (docs/architecture/billing.md, "AI credits"): one row per AI check
  // with its calls and amount, linked to the application it ran for, and a credit's unused rest when it expired. The newest page; the
  // Activity log lists every call.
  const deductions = createListAiCreditDeductions(undefined, () => ({ query: { refetchOnMount: 'always' } }));
  const list = $derived(deductions.data?.data.deductions ?? []);
  const more = $derived(Boolean(deductions.data?.data.next));

  const what = (row: AiCreditDeduction) =>
    row.kind === 'Expiry' ? 'Credits expired' : row.candidateName ? `AI check, ${row.candidateName}` : 'AI check, application erased';

  // The table sorts (L2), newest first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, AiCreditDeduction, any>[] = [
    { id: 'date', header: 'Date', accessorFn: (row) => row.date, sortFn: 'basic', sortDescFirst: true },
    { id: 'what', header: 'Used by', accessorFn: what, sortFn: 'alphanumeric' },
    { id: 'calls', header: 'Calls', accessorFn: (row) => row.calls, sortFn: 'basic', sortDescFirst: true },
    { id: 'amount', header: 'Amount', accessorFn: (row) => row.amount, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('credit-deductions', { id: 'date', desc: true }, ['date', 'what', 'calls', 'amount']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (row) => `${row.kind}-${row.id}`,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));
</script>

<Panel title="Deductions" count={deductions.data ? list.length : undefined} flush={list.length > 0} footer={more ? moreFooter : undefined}>
  {#if deductions.isError && !deductions.data}
    <QueryError message={describeError(deductions.error, 'Could not load the deductions')} onretry={() => deductions.refetch()} inset />
  {:else if !deductions.data}
    <div aria-hidden="true" class="flex flex-col gap-3"><Skeleton class="h-5 w-full" /><Skeleton class="h-5 w-full" /></div>
    <p class="sr-only" role="status">Loading the deductions…</p>
  {:else if list.length === 0}
    <p class="text-muted-foreground">No credits used yet.</p>
  {:else}
    <div class="overflow-x-auto">
      <Table.Root label="Deductions">
        <Table.Header>
          {#each table.getHeaderGroups() as group (group.id)}
            <Table.Row class="bg-muted hover:bg-muted">
              {#each group.headers as header (header.id)}
                <SortHeader {header} class={header.id === 'amount' || header.id === 'calls' ? 'text-right' : undefined} />
              {/each}
            </Table.Row>
          {/each}
        </Table.Header>
        <Table.Body>
          {#each sorted as row (`${row.kind}-${row.id}`)}
            <Table.Row class="h-12">
              <Table.Cell class="font-semibold"><DateText at={row.date} /></Table.Cell>
              <Table.Cell>
                {#if row.kind === 'Check' && row.applicationId && row.candidateName}
                  <a
                    href={`/applications/${row.applicationId}?tab=verification`}
                    class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11">{what(row)}</a
                  >
                {:else}
                  {what(row)}
                {/if}
              </Table.Cell>
              <Table.Cell class="text-right tabular-nums">{row.kind === 'Check' ? row.calls : ''}</Table.Cell>
              <Table.Cell class="text-right tabular-nums">{formatCost(row.amount)}</Table.Cell>
            </Table.Row>
          {/each}
        </Table.Body>
      </Table.Root>
    </div>
  {/if}
</Panel>

{#snippet moreFooter()}
  <p class="text-muted-foreground">
    The newest {list.length}.
    <a href="/verification/activity" class="inline-flex min-h-6 items-center text-foreground underline underline-offset-2 pointer-coarse:min-h-11">See every call</a>
  </p>
{/snippet}
