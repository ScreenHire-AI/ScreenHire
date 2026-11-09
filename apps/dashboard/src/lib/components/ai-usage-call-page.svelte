<script lang="ts">
  import { createListAiUsageCalls } from '@screenhire/api-client/svelte-query';
  import type { ListAiUsageCallsParams } from '@screenhire/api-client';
  import Self from '#lib/components/ai-usage-call-page.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { checkLabels, formatCost, formatTokens, keySourceLabels, outcomeLabels, pageSize } from '#lib/ai-usage.js';
  import { formatDateTime } from '#lib/dates.js';
  import { describeError } from '#lib/query.js';

  // Rows for one page of the call list (newest first) and, after them, a button that renders the next page the same way:
  // the cursor is the `nextBefore` of the page above, so a page never needs to know the ones before it. Rendered inside the
  // call table, one tbody per page.
  let { filters, before }: { filters: ListAiUsageCallsParams; before?: string } = $props();

  const calls = createListAiUsageCalls(() => ({ ...filters, before, limit: pageSize }));
  const page = $derived(calls.data?.data);
  let more = $state(false);
</script>

{#if calls.isError && !page}
  <tbody>
    <tr>
      <td colspan={9} class="p-4"><QueryError message={describeError(calls.error, 'Could not load the calls')} onretry={() => calls.refetch()} inset /></td>
    </tr>
  </tbody>
{:else if !page}
  <tbody>
    <tr>
      <td colspan={9} class="p-4">
        <div aria-hidden="true" class="flex flex-col gap-2"><Skeleton class="h-10 w-full" /><Skeleton class="h-10 w-full" /></div>
        <p class="sr-only" role="status">Loading the calls…</p>
      </td>
    </tr>
  </tbody>
{:else}
  <Table.Body>
    {#each page.calls as call (call.id)}
      <Table.Row>
        <Table.Cell class="whitespace-nowrap tabular-nums"><time datetime={call.occurredAt} title={formatDateTime(call.occurredAt)}>{formatDateTime(call.occurredAt)}</time></Table.Cell>
        <Table.Cell class="whitespace-nowrap">{checkLabels[call.check] ?? call.check}</Table.Cell>
        <Table.Cell><TruncatedText text={call.model} class="max-w-48" /></Table.Cell>
        <Table.Cell class="whitespace-nowrap">{keySourceLabels[call.keySource]}</Table.Cell>
        <Table.Cell>{#if call.providerRequestId}<TruncatedText text={call.providerRequestId} class="max-w-48" />{:else}<span class="sr-only">No request id sent</span>{/if}</Table.Cell>
        <Table.Cell class="text-right tabular-nums">{formatTokens(call.inputTokens)}{#if call.inputTokens === null}<span class="sr-only">Not reported</span>{/if}</Table.Cell>
        <Table.Cell class="text-right tabular-nums">{formatTokens(call.outputTokens)}{#if call.outputTokens === null}<span class="sr-only">Not reported</span>{/if}</Table.Cell>
        <Table.Cell class="text-right tabular-nums">{#if call.cost === null}<span class="sr-only">Unknown cost</span>{:else}{formatCost(call.cost)}{/if}</Table.Cell>
        <Table.Cell>
          <StatusBadge status={call.outcome === 'Ok' ? 'done' : 'needsFixes'} label={outcomeLabels[call.outcome]} quiet={call.outcome === 'Ok'} />
        </Table.Cell>
      </Table.Row>
    {:else}
      {#if !before}
        <Table.Row><Table.Cell colspan={9} class="text-muted-foreground">No model calls in this range.</Table.Cell></Table.Row>
      {/if}
    {/each}
  </Table.Body>
  {#if page.nextBefore}
    {#if more}
      <Self {filters} before={page.nextBefore} />
    {:else}
      <tbody>
        <tr>
          <td colspan={9} class="p-4 text-center"><Button variant="outline" size="lg" onclick={() => (more = true)}>Show more</Button></td>
        </tr>
      </tbody>
    {/if}
  {/if}
{/if}
