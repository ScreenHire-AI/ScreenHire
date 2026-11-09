<script lang="ts">
  import type { ListVerificationActivityParams } from '@screenhire/api-client';
  import { createListVerificationActivity } from '@screenhire/api-client/svelte-query';
  import Self from '#lib/components/verification-area/activity-rows.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { activityCheckLabel, activityGroupLabels, activityOutcomeLabels, activityOutcomeStatuses } from '#lib/activity-log.js';
  import { formatCost, formatTokens, keySourceLabels, pageSize } from '#lib/ai-usage.js';
  import { formatDateTime } from '#lib/dates.js';
  import { describeError } from '#lib/query.js';

  // Rows for one page of the activity log (newest first) and, after them, a button that renders the next page the same way:
  // the cursor is the `nextBefore` of the page above, so a page never needs to know the ones before it. Rendered inside the
  // table, one tbody per page. `usage` adds the columns that show what a call cost and the provider's request id.
  let { filters, usage, before }: { filters: ListVerificationActivityParams; usage: boolean; before?: string } = $props();

  const activity = createListVerificationActivity(() => ({ ...filters, before, limit: pageSize }));
  const page = $derived(activity.data?.data);
  const columns = $derived(usage ? 11 : 5);
  let more = $state(false);
</script>

{#if activity.isError && !page}
  <tbody>
    <tr>
      <td colspan={columns} class="p-4"><QueryError message={describeError(activity.error, 'Could not load the activity')} onretry={() => activity.refetch()} inset /></td>
    </tr>
  </tbody>
{:else if !page}
  <tbody>
    <tr>
      <td colspan={columns} class="p-4">
        <div aria-hidden="true" class="flex flex-col gap-2"><Skeleton class="h-10 w-full" /><Skeleton class="h-10 w-full" /></div>
        <p class="sr-only" role="status">Loading the activity…</p>
      </td>
    </tr>
  </tbody>
{:else}
  <Table.Body>
    {#each page.entries as entry (entry.id)}
      <Table.Row>
        <Table.Cell class="whitespace-nowrap tabular-nums"><time datetime={entry.at} title={formatDateTime(entry.at)}>{formatDateTime(entry.at)}</time></Table.Cell>
        <Table.Cell class="whitespace-nowrap">{activityCheckLabel(entry.check)}</Table.Cell>
        <Table.Cell class="whitespace-nowrap">{#if entry.group}{activityGroupLabels[entry.group]}{:else}<span class="sr-only">No group</span>{/if}</Table.Cell>
        <Table.Cell>
          <span class="flex items-center gap-2">
            <!-- A check that completed is the calm default, so its cell stays blank and only the exceptions are marked (T3). -->
            {#if entry.outcome === 'Completed'}
              <span class="sr-only">{activityOutcomeLabels.Completed}</span>
            {:else}
              <StatusBadge status={activityOutcomeStatuses[entry.outcome]} label={activityOutcomeLabels[entry.outcome]} />
            {/if}
            {#if entry.note}<TruncatedText text={entry.note} class="max-w-56 text-muted-foreground" />{/if}
          </span>
        </Table.Cell>
        <Table.Cell>
          {#if entry.applicationId}
            <a href={`/applications/${entry.applicationId}`} class="inline-flex min-h-6 items-center whitespace-nowrap underline underline-offset-2 pointer-coarse:min-h-11">{entry.jobTitle ?? 'Application'}</a>
          {:else}
            <span class="sr-only">No application</span>
          {/if}
        </Table.Cell>
        {#if usage}
          <Table.Cell>{#if entry.call}<TruncatedText text={entry.call.model} class="max-w-48" />{:else}<span class="sr-only">Not a model call</span>{/if}</Table.Cell>
          <Table.Cell class="whitespace-nowrap">{#if entry.call}{keySourceLabels[entry.call.keySource]}{/if}</Table.Cell>
          <Table.Cell>
            {#if entry.call?.providerRequestId}<TruncatedText text={entry.call.providerRequestId} class="max-w-48" />{:else if entry.call}<span class="sr-only">No request id sent</span>{/if}
          </Table.Cell>
          <Table.Cell class="text-right tabular-nums">{#if entry.call}{formatTokens(entry.call.inputTokens)}{#if entry.call.inputTokens === null}<span class="sr-only">Not reported</span>{/if}{/if}</Table.Cell>
          <Table.Cell class="text-right tabular-nums">{#if entry.call}{formatTokens(entry.call.outputTokens)}{#if entry.call.outputTokens === null}<span class="sr-only">Not reported</span>{/if}{/if}</Table.Cell>
          <Table.Cell class="text-right tabular-nums">{#if entry.call}{#if entry.call.cost === null}<span class="sr-only">Unknown cost</span>{:else}{formatCost(entry.call.cost)}{/if}{/if}</Table.Cell>
        {/if}
      </Table.Row>
    {:else}
      {#if !before}
        <Table.Row><Table.Cell colspan={columns} class="text-muted-foreground">No activity matches.</Table.Cell></Table.Row>
      {/if}
    {/each}
  </Table.Body>
  {#if page.nextBefore}
    {#if more}
      <Self {filters} {usage} before={page.nextBefore} />
    {:else}
      <tbody>
        <tr>
          <td colspan={columns} class="p-4 text-center"><Button variant="outline" size="lg" onclick={() => (more = true)}>Show more</Button></td>
        </tr>
      </tbody>
    {/if}
  {/if}
{/if}
