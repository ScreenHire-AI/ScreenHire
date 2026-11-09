<script lang="ts">
  import ActivityIcon from '~icons/lucide/activity';
  import CoinsIcon from '~icons/lucide/coins';
  import DollarSignIcon from '~icons/lucide/dollar-sign';
  import type { AiUsageDay } from '@screenhire/api-client';
  import { createGetAiUsageSummary } from '@screenhire/api-client/svelte-query';
  import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
  import ChartPanel, { type ChartColumn } from '#lib/components/chart-panel.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ReportStat from '#lib/components/report-stat.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import LimitsSummary from '#lib/components/verification-area/limits-summary.svelte';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { cn } from '#lib/utils.js';
  import { formatCost, formatDay, formatTokens, keySourceLabels } from '#lib/ai-usage.js';
  import { describeError } from '#lib/query.js';

  // The Usage tab (docs/architecture/ai-usage.md): tokens and cost by day and model, ScreenHire's key against the organization's
  // own, and the month against the budget and cap. For who can see AI usage (the tab's +page.ts). Never about a candidate.
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const ranges = [
    { value: 'month', label: 'This month', days: null },
    { value: '30', label: 'Last 30 days', days: 30 },
    { value: '90', label: 'Last 90 days', days: 90 },
  ] as const;
  let range = $state<string>('month');
  // Fixed when the range is chosen, so the same range is the same query.
  let from = $state<string | undefined>(undefined);
  let to = $state<string | undefined>(undefined);

  function chooseRange(value: string) {
    range = value;
    const days = ranges.find((r) => r.value === value)?.days;
    const now = Date.now();
    from = days ? new Date(now - days * 24 * 60 * 60 * 1000).toISOString() : undefined;
    to = days ? new Date(now).toISOString() : undefined;
  }

  const summary = createGetAiUsageSummary(() => ({ timeZone, ...(from ? { from } : {}), ...(to ? { to } : {}) }));
  const data = $derived(summary.data?.data);
  const period = $derived(ranges.find((r) => r.value === range)?.label.toLowerCase() ?? '');

  const dayRows = $derived<BarRow[]>(
    (data?.byDay ?? []).map((day) => ({
      key: day.date,
      label: formatDay(day.date),
      values: [day.builtInTokens, day.ownKeyTokens],
      texts: [formatTokens(day.builtInTokens), formatTokens(day.ownKeyTokens)],
      summary: `${formatDay(day.date)}: ${day.calls} calls, ${formatTokens(day.builtInTokens)} tokens on ScreenHire's key, ${formatTokens(day.ownKeyTokens)} on your own key, ${formatCost(day.cost)}`,
    })),
  );
  const dayColumns: ChartColumn<AiUsageDay>[] = [
    { id: 'day', header: 'Day', text: (day) => formatDay(day.date), sortBy: (day) => day.date, csv: (day) => day.date },
    { id: 'calls', header: 'Calls', text: (day) => formatTokens(day.calls), sortBy: (day) => day.calls },
    { id: 'builtIn', header: "ScreenHire's key tokens", text: (day) => formatTokens(day.builtInTokens), sortBy: (day) => day.builtInTokens },
    { id: 'ownKey', header: 'Your own key tokens', text: (day) => formatTokens(day.ownKeyTokens), sortBy: (day) => day.ownKeyTokens },
    { id: 'cost', header: 'Cost', text: (day) => formatCost(day.cost), sortBy: (day) => day.cost, csv: (day) => day.cost },
  ];
</script>

<svelte:head>
  <title>Usage · Verification · ScreenHire</title>
</svelte:head>

{#if summary.isError && !data}
  <Panel title="Usage">
    <QueryError message={describeError(summary.error, 'Could not load the AI usage')} onretry={() => summary.refetch()} inset />
  </Panel>
{:else if !data}
  <div aria-hidden="true" class="grid grid-cols-2 gap-4 md:grid-cols-[repeat(auto-fit,minmax(11rem,1fr))]">
    <Skeleton class="h-32 w-full" /><Skeleton class="h-32 w-full" /><Skeleton class="h-32 w-full" />
  </div>
  <p class="sr-only" role="status">Loading the AI usage…</p>
{:else}
  <div class="flex justify-end">
    <select class={cn(selectClass, 'w-auto shrink-0')} value={range} onchange={(event) => chooseRange(event.currentTarget.value)} aria-label="Date range">
      {#each ranges as option (option.value)}<option value={option.value}>{option.label}</option>{/each}
    </select>
  </div>

  <div class="grid grid-cols-2 gap-4 md:grid-cols-[repeat(auto-fit,minmax(11rem,1fr))]">
    <ReportStat icon={ActivityIcon} title="Model calls" value={formatTokens(data.totals.calls)} />
    <ReportStat
      icon={CoinsIcon}
      title="Tokens"
      value={formatTokens(data.totals.inputTokens + data.totals.outputTokens)}
      share={`${formatTokens(data.totals.inputTokens)} in, ${formatTokens(data.totals.outputTokens)} out`}
    />
    <ReportStat
      icon={DollarSignIcon}
      title="Cost"
      value={formatCost(data.totals.cost)}
      share={data.totals.callsWithUnknownCost > 0
        ? `${data.totals.callsWithUnknownCost} ${data.totals.callsWithUnknownCost === 1 ? 'call has' : 'calls have'} no known cost, counted as nothing`
        : null}
    />
  </div>

  <Panel title="This month against the limits">
    <LimitsSummary limits={data.limits} />
  </Panel>

  <ChartPanel
    title={`Use by day, ${period}`}
    basis="Tokens in and out of each day's model calls, on ScreenHire's key and on your own."
    tableId="verification-usage-days"
    columns={dayColumns}
    rows={data.byDay}
    rowKey={(day) => day.date}
  >
    {#snippet chart()}
      <BarChart
        label="Tokens used by day"
        series={[
          { label: keySourceLabels.BuiltIn, tone: 'chart-1' },
          { label: keySourceLabels.OwnKey, tone: 'chart-2' },
        ]}
        rows={dayRows}
      />
    {/snippet}
    {#snippet empty()}
      <p class="text-muted-foreground">No model calls in this range.</p>
    {/snippet}
  </ChartPanel>

  <Panel title="By model and key" flush>
    <Table.Root label="Usage by model and key">
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
          <Table.Row><Table.Cell colspan={7} class="text-muted-foreground">No model calls in this range.</Table.Cell></Table.Row>
        {/each}
      </Table.Body>
    </Table.Root>
  </Panel>
{/if}
