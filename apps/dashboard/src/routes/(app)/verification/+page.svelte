<script lang="ts">
  import ClipboardCheckIcon from '~icons/lucide/clipboard-check';
  import type { VerificationGroupCount } from '@screenhire/api-client';
  import { createGetAiCredits, createGetVerificationOverview } from '@screenhire/api-client/svelte-query';
  import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
  import ChartPanel, { type ChartColumn } from '#lib/components/chart-panel.svelte';
  import LevelMark from '#lib/components/level-mark.svelte';
  import OverviewColumns from '#lib/components/overview-columns.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import ReportStat from '#lib/components/report-stat.svelte';
  import StatusIcon from '#lib/components/status-icon.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import { locales } from '#lib/dates.js';
  import { formatCredits } from '#lib/billing.js';
  import { describeError } from '#lib/query.js';
  import { canSeeUsage } from '#lib/verification-area.js';
  import { verificationStateOf, type VerificationState } from '#lib/verification.js';
  import type { PageProps } from './$types';

  // The Overview tab: this month's applications by what the checks found, what each of the six groups of checks found (a group
  // that is off says Off and links to Checks, so a quiet bar is never read as a clean one), and,
  // for who can see AI usage, the AI credits balance. Counts of applications only: no score, no percentage, no AI
  // assistance level, and nothing about a person (ADR-0004, design system section 16).
  let { data }: PageProps = $props();

  const overview = createGetVerificationOverview();
  const result = $derived(overview.data?.data);
  const usageVisible = $derived(canSeeUsage(data.user));
  // The AI credits balance, where a payment provider is set up (ADR-0032): at zero the AI checks pause, which the badge says.
  const creditsQuery = createGetAiCredits(() => ({ query: { enabled: usageVisible } }));
  const credits = $derived(usageVisible && creditsQuery.data?.data.available ? creditsQuery.data.data : null);

  const count = new Intl.NumberFormat(locales);
  // Each level's tile carries the level's own icon, as the chips draw it: circle-check for Clear, eye for Review and id-card
  // for Verify (design system section 3.2), so the count is icon, colour and word (T1). The first tile says when the newest
  // check finished (T7), so a quiet month reads as quiet rather than stuck.
  const clear = verificationStateOf('NoConcerns', false);
  const review = verificationStateOf('ReviewSuggested', false);
  const verify = verificationStateOf('VerificationRecommended', false);
  const month = $derived(result ? new Intl.DateTimeFormat(locales, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(result.from)) : '');

  const applicationsText = (group: VerificationGroupCount) => (group.on ? count.format(group.applications) : 'Off');
  const groupRows = $derived<BarRow[]>(
    (result?.groups ?? []).map((group) => ({
      key: group.group,
      label: group.label,
      values: [group.on ? group.applications : 0],
      texts: [applicationsText(group)],
      href: group.on ? undefined : '/verification/checks',
      summary: group.on
        ? `${group.label}: ${group.applications} ${group.applications === 1 ? 'application' : 'applications'}`
        : `${group.label}: off. Turn it on in Checks.`,
    })),
  );
  // Every group is listed; the chart has nothing to draw only when every group is on and found nothing.
  const anything = $derived((result?.groups ?? []).some((group) => group.applications > 0 || !group.on));
  const groupColumns: ChartColumn<VerificationGroupCount>[] = [
    { id: 'group', header: 'Group of checks', text: (group) => group.label, sortBy: (group) => group.label },
    { id: 'applications', header: 'Applications', text: applicationsText, sortBy: (group) => (group.on ? group.applications : -1), csv: applicationsText },
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
    {#snippet lastChecked()}
      {#if result.lastCheckedAt}Last checked <RelativeTime at={result.lastCheckedAt} class="inline" />{/if}
    {/snippet}
    <!-- Passed only when there is a date, so the tile has no empty line under its value (L1). -->
    <ReportStat
      icon={ClipboardCheckIcon}
      title="Applications checked"
      value={count.format(result.checked)}
      share={result.pending > 0 ? `${result.pending} still being checked` : null}
      footnote={result.lastCheckedAt ? lastChecked : undefined}
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

  <!-- An overview (design system section 7): what the checks found in the main column, the credits they draw on in the side
       column, each stacking on its own. -->
  <OverviewColumns>
    {#snippet main()}
      <ChartPanel
        title={`Findings by group, ${month}`}
        basis="Applications this month in which a group of checks counted a signal towards the level. A group that is off says Off."
        tableId="verification-groups"
        columns={groupColumns}
        rows={anything ? result.groups : []}
        rowKey={(group) => group.group}
      >
        {#snippet chart()}
          <BarChart label="Applications by group of checks" series={[{ label: 'Applications', tone: 'chart-1' }]} rows={groupRows} />
        {/snippet}
        {#snippet empty()}
          <p class="text-muted-foreground">No group of checks found anything this month.</p>
        {/snippet}
      </ChartPanel>
    {/snippet}
    {#snippet side()}
      {#if credits}
        <Panel title="AI credits">
          <div class="flex flex-col gap-4">
            <SummaryList>
              <SummaryItem label="Balance">
                <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
                  <span class="tabular-nums">{formatCredits(credits.balance)}</span>
                  {#if credits.paused}<StatusBadge status="notInEffect" label="AI checks paused" />{/if}
                </span>
              </SummaryItem>
            </SummaryList>
            <p>
              <a href="/settings/billing#ai-credits" class="inline-flex min-h-6 items-center underline underline-offset-2 pointer-coarse:min-h-11"
                >See AI credits</a
              >
            </p>
          </div>
        </Panel>
      {/if}
    {/snippet}
  </OverviewColumns>
{/if}
