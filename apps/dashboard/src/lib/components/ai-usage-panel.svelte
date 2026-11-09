<script lang="ts">
  import DownloadIcon from '~icons/lucide/download';
  import type { AiCallOutcome, AiKeySource, AiUsageLimitView, ListAiUsageCallsParams } from '@screenhire/api-client';
  import { createGetAiUsageSummary, getExportAiUsageCallsUrl } from '@screenhire/api-client/svelte-query';
  import AiUsageCallPage from '#lib/components/ai-usage-call-page.svelte';
  import AiUsageLimitsForm from '#lib/components/ai-usage-limits-form.svelte';
  import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { budgetStatus, formatAmount, formatCost, formatDay, formatTokens, keySourceLabels, outcomeLabels } from '#lib/ai-usage.js';
  import { describeError } from '#lib/query.js';

  // The organization's AI usage (docs/architecture/ai-usage.md): this month against the budget and cap, use by day, model and
  // key, every call with the provider's own request id (so a bill can be compared 1:1 with the provider's dashboard), and a
  // CSV of it. For people who hold View AI usage; only an Owner sets the limits. Never about a candidate.
  let { canSetLimits }: { canSetLimits: boolean } = $props();

  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const summary = createGetAiUsageSummary(() => ({ timeZone }));
  const data = $derived(summary.data?.data);

  let keySource = $state<'' | AiKeySource>('');
  let outcome = $state<'' | AiCallOutcome>('');
  const filters = $derived<ListAiUsageCallsParams>({
    ...(data ? { from: data.from } : {}),
    ...(keySource ? { keySource } : {}),
    ...(outcome ? { outcome } : {}),
  });
  const exportUrl = $derived(getExportAiUsageCallsUrl(filters));

  const dayRows = $derived<BarRow[]>(
    (data?.byDay ?? []).map((day) => {
      const tokens = day.inputTokens + day.outputTokens;
      return {
        key: day.date,
        label: formatDay(day.date),
        values: [tokens],
        texts: [formatTokens(tokens)],
        summary: `${formatDay(day.date)}: ${day.calls} calls, ${formatTokens(tokens)} tokens, ${formatCost(day.cost)}`,
      };
    }),
  );

  const limitLine = (limit: AiUsageLimitView) => `${formatAmount(limit.used, limit.unit)} of ${formatAmount(limit.amount, limit.unit)}`;
</script>

{#if summary.isError && !data}
  <Panel title="AI usage">
    <QueryError message={describeError(summary.error, 'Could not load the AI usage')} onretry={() => summary.refetch()} inset />
  </Panel>
{:else if !data}
  <Panel title="AI usage">
    <div aria-hidden="true" class="flex flex-col gap-4"><Skeleton class="h-10 w-full" /><Skeleton class="h-14 w-full" /></div>
    <p class="sr-only" role="status">Loading the AI usage…</p>
  </Panel>
{:else}
  <Panel title="AI usage">
    <div class="flex flex-col gap-4">
      <p class="max-w-measure text-muted-foreground">Every model call is listed below with the provider's own id, to compare with your provider's dashboard.</p>
      <SummaryList>
        <SummaryItem label="Model calls"><span class="tabular-nums">{formatTokens(data.totals.calls)}</span></SummaryItem>
        <SummaryItem label="Tokens in and out">
          <span class="tabular-nums">{formatTokens(data.totals.inputTokens)} in, {formatTokens(data.totals.outputTokens)} out</span>
        </SummaryItem>
        <SummaryItem label="Cost"><span class="tabular-nums">{formatCost(data.totals.cost)}</span></SummaryItem>
        {#if data.limits.effectiveBudget}
          {@const budget = data.limits.effectiveBudget}
          {@const state = budgetStatus(budget)}
          <SummaryItem label="Monthly budget">
            <span class="flex flex-wrap items-center gap-2">
              <span class="tabular-nums">{limitLine(budget)}</span>
              <StatusBadge status={state.status} label={state.label} quiet={state.status === 'done'} />
              {#if !budget.isDefault}<span class="text-muted-foreground">Set by you</span>{/if}
            </span>
          </SummaryItem>
        {/if}
        {#if data.limits.effectiveCap}
          {@const cap = data.limits.effectiveCap}
          {@const state = budgetStatus(cap)}
          <SummaryItem label="Monthly cap">
            <span class="flex flex-wrap items-center gap-2">
              <span class="tabular-nums">{limitLine(cap)}</span>
              <StatusBadge status={state.status} label={cap.percent >= 100 ? 'Reached, AI checks paused' : state.label} quiet={state.status === 'done'} />
              {#if !cap.isDefault}<span class="text-muted-foreground">Set by you</span>{/if}
            </span>
          </SummaryItem>
        {/if}
      </SummaryList>
      {#if data.totals.callsWithUnknownCost > 0}
        <p class="text-muted-foreground">
          {data.totals.callsWithUnknownCost} {data.totals.callsWithUnknownCost === 1 ? 'call has' : 'calls have'} no known cost, so the cost counts them as nothing.
        </p>
      {/if}
    </div>
  </Panel>

  {#if canSetLimits}
    <Panel title="Monthly limits"><AiUsageLimitsForm limits={data.limits} /></Panel>
  {/if}

  {#if dayRows.length > 0}
    <Panel title="Usage by day">
      <BarChart label="Tokens used by day" series={[{ label: 'Tokens', tone: 'chart-1' }]} rows={dayRows} />
    </Panel>
  {/if}

  <Panel title="By model and key" flush>
    <Table.Root>
      <Table.Header>
        <Table.Row>
          <Table.Head>Model</Table.Head>
          <Table.Head>Provider</Table.Head>
          <Table.Head>Key</Table.Head>
          <Table.Head class="text-right">Calls</Table.Head>
          <Table.Head class="text-right">Tokens in</Table.Head>
          <Table.Head class="text-right">Tokens out</Table.Head>
          <Table.Head class="text-right">Cost</Table.Head>
        </Table.Row>
      </Table.Header>
      <Table.Body>
        {#each data.byModel as group (`${group.provider}|${group.model}|${group.keySource}`)}
          <Table.Row>
            <Table.Cell><TruncatedText text={group.model} class="max-w-56" /></Table.Cell>
            <Table.Cell class="whitespace-nowrap">{group.provider}</Table.Cell>
            <Table.Cell class="whitespace-nowrap">{keySourceLabels[group.keySource]}</Table.Cell>
            <Table.Cell class="text-right tabular-nums">{formatTokens(group.calls)}</Table.Cell>
            <Table.Cell class="text-right tabular-nums">{formatTokens(group.inputTokens)}</Table.Cell>
            <Table.Cell class="text-right tabular-nums">{formatTokens(group.outputTokens)}</Table.Cell>
            <Table.Cell class="text-right tabular-nums">{group.callsWithUnknownCost > 0 && group.cost === 0 ? 'Unknown' : formatCost(group.cost)}</Table.Cell>
          </Table.Row>
        {:else}
          <Table.Row><Table.Cell colspan={7} class="text-muted-foreground">No model calls this month.</Table.Cell></Table.Row>
        {/each}
      </Table.Body>
    </Table.Root>
  </Panel>

  <Panel title="Calls" flush>
    {#snippet tools()}
      <select class={`${selectClass} w-auto`} bind:value={keySource} aria-label="Filter by key">
        <option value="">Any key</option>
        <option value="BuiltIn">{keySourceLabels.BuiltIn}</option>
        <option value="OwnKey">{keySourceLabels.OwnKey}</option>
      </select>
      <select class={`${selectClass} w-auto`} bind:value={outcome} aria-label="Filter by outcome">
        <option value="">Any outcome</option>
        <option value="Ok">{outcomeLabels.Ok}</option>
        <option value="Error">{outcomeLabels.Error}</option>
        <option value="TimedOut">{outcomeLabels.TimedOut}</option>
      </select>
    {/snippet}
    {#snippet actions()}
      <Button href={exportUrl} download variant="outline" size="default"><DownloadIcon aria-hidden="true" />Export CSV</Button>
    {/snippet}
    <Table.Root>
      <Table.Header>
        <Table.Row>
          <Table.Head>When</Table.Head>
          <Table.Head>Check</Table.Head>
          <Table.Head>Model</Table.Head>
          <Table.Head>Key</Table.Head>
          <Table.Head>Provider request id</Table.Head>
          <Table.Head class="text-right">In</Table.Head>
          <Table.Head class="text-right">Out</Table.Head>
          <Table.Head class="text-right">Cost</Table.Head>
          <Table.Head>Outcome</Table.Head>
        </Table.Row>
      </Table.Header>
      {#key `${keySource}|${outcome}|${data.from}`}
        <AiUsageCallPage {filters} />
      {/key}
    </Table.Root>
  </Panel>
{/if}
