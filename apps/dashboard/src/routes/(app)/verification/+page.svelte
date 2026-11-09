<script lang="ts">
  import ClipboardCheckIcon from '~icons/lucide/clipboard-check';
  import type { VerificationGroupCount } from '@screenhire/api-client';
  import { createGetOrganizationVerificationSettings, createGetVerificationOverview } from '@screenhire/api-client/svelte-query';
  import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
  import ChartPanel, { type ChartColumn } from '#lib/components/chart-panel.svelte';
  import LevelMark from '#lib/components/level-mark.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import ReportStat from '#lib/components/report-stat.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import LimitsSummary from '#lib/components/verification-area/limits-summary.svelte';
  import { locales } from '#lib/dates.js';
  import { describeError } from '#lib/query.js';
  import { canSeeUsage } from '#lib/verification-area.js';
  import { verificationStateOf, type VerificationState } from '#lib/verification.js';
  import type { PageProps } from './$types';

  // The Overview tab: this month's applications by what the checks found, which groups of checks found something most, and,
  // for who can see AI usage, the month against the budget. Counts of applications only: no score, no percentage, no AI
  // assistance level, and nothing about a person (ADR-0004, design system section 16).
  let { data }: PageProps = $props();

  const overview = createGetVerificationOverview();
  const result = $derived(overview.data?.data);
  const usageVisible = $derived(canSeeUsage(data.user));
  const settings = createGetOrganizationVerificationSettings(() => ({ query: { enabled: usageVisible } }));
  const limits = $derived(usageVisible ? settings.data?.data.limits : null);

  const count = new Intl.NumberFormat(locales);
  // Each level's tile carries the level's own mark, as the chips draw it: the circle-check for Clear, the level mark for
  // Review and Verify (design system section 3.2), so the count is icon, colour and word (T1).
  const clear = verificationStateOf('NoConcerns', false);
  const review = verificationStateOf('ReviewSuggested', false);
  const verify = verificationStateOf('VerificationRecommended', false);
  const month = $derived(result ? new Intl.DateTimeFormat(locales, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(result.from)) : '');

  const groupRows = $derived<BarRow[]>(
    (result?.groups ?? []).map((group) => ({
      key: group.group,
      label: group.label,
      values: [group.applications],
      texts: [count.format(group.applications)],
      summary: `${group.label}: ${group.applications} ${group.applications === 1 ? 'application' : 'applications'}`,
    })),
  );
  const groupColumns: ChartColumn<VerificationGroupCount>[] = [
    { id: 'group', header: 'Group of checks', text: (group) => group.label, sortBy: (group) => group.label },
    { id: 'applications', header: 'Applications', text: (group) => count.format(group.applications), sortBy: (group) => group.applications },
  ];
</script>

<svelte:head>
  <title>Verification · ScreenHire</title>
</svelte:head>

{#if overview.isError && !result}
  <Panel title="This month">
    <QueryError message={describeError(overview.error, 'Could not load the overview')} onretry={() => overview.refetch()} inset />
  </Panel>
{:else if !result}
  <div aria-hidden="true" class="grid grid-cols-2 gap-4 md:grid-cols-[repeat(auto-fit,minmax(11rem,1fr))]">
    <Skeleton class="h-32 w-full" /><Skeleton class="h-32 w-full" /><Skeleton class="h-32 w-full" /><Skeleton class="h-32 w-full" />
  </div>
  <p class="sr-only" role="status">Loading the overview…</p>
{:else}
  <div class="grid grid-cols-2 gap-4 md:grid-cols-[repeat(auto-fit,minmax(11rem,1fr))]">
    <ReportStat
      icon={ClipboardCheckIcon}
      title="Applications checked"
      value={count.format(result.checked)}
      share={result.pending > 0 ? `${result.pending} still being checked` : null}
    />
    {#snippet levelMark(state: VerificationState)}
      {#if state.kind === 'review' || state.kind === 'verify'}
        <LevelMark steps={state.steps} tone={state.kind} size="lg" />
      {:else}
        <StatusIcon status={state.status} class="size-4" />
      {/if}
    {/snippet}
    {#each [{ state: clear, value: result.levels.clear }, { state: review, value: result.levels.review }, { state: verify, value: result.levels.verify }] as level (level.state.kind)}
      <ReportStat title={level.state.word} value={count.format(level.value)}>
        {#snippet mark()}{@render levelMark(level.state)}{/snippet}
      </ReportStat>
    {/each}
  </div>

  <div class="grid gap-5 @7xl:grid-cols-2">
    <ChartPanel
      title={`Findings by group, ${month}`}
      basis="Applications this month in which a group of checks counted a signal towards the level."
      tableId="verification-groups"
      columns={groupColumns}
      rows={result.groups}
      rowKey={(group) => group.group}
    >
      {#snippet chart()}
        <BarChart label="Applications by group of checks" series={[{ label: 'Applications', tone: 'chart-1' }]} rows={groupRows} />
      {/snippet}
      {#snippet empty()}
        <p class="text-muted-foreground">No group of checks found anything this month.</p>
      {/snippet}
    </ChartPanel>

    {#if limits}
      <Panel title="AI budget this month">
        <div class="flex flex-col gap-4">
          <LimitsSummary {limits} cap={false} />
          <p><a href="/verification/usage" class="underline underline-offset-2 pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center">See usage</a></p>
        </div>
      </Panel>
    {/if}
  </div>
{/if}
