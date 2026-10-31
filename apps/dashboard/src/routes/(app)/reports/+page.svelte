<script lang="ts">
  import ChartBarIcon from '@lucide/svelte/icons/chart-bar';
  import type { FunnelStage, HiringReport, JobTimeToHire, SourceCount } from '@screenhire/api-client';
  import { createGetHiringReport, createListJobs } from '@screenhire/api-client/svelte-query';
  import { keepPreviousData } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import BarChart, { type BarRow } from '#lib/components/bar-chart.svelte';
  import ChartPanel, { type ChartColumn } from '#lib/components/chart-panel.svelte';
  import DateRangeFilter from '#lib/components/date-range-filter.svelte';
  import type { FilterOption } from '#lib/components/filter-menu.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { sourceName } from '#lib/format.js';
  import { describeError } from '#lib/query.js';
  import { rangeInstants, readRange, writeRange, type ReportRange } from '#lib/report-range.js';
  import { counted, countText, daysText, shareText, stageKindLabel } from '#lib/reports.js';

  // Reports (design system section 7, Report; docs/architecture/reports.md): the funnel, time to hire and sources for the
  // jobs the person can see, over a date range and an optional set of jobs. The filters live in the address, so a link
  // or the trip back keeps them. One request feeds the three panels, so their numbers agree; while new numbers load the
  // panels keep the old ones, dimmed. Arriving and moving applications refresh it (#lib/realtime.ts). Nothing here
  // shows verification results (ADR-0004).

  const range = $derived(readRange(page.url.searchParams));
  const jobIds = $derived(page.url.searchParams.getAll('job'));
  const instants = $derived(rangeInstants(range, new Date()));

  const report = createGetHiringReport(
    () => ({ ...instants, jobId: jobIds.length > 0 ? jobIds : undefined }),
    () => ({ query: { placeholderData: keepPreviousData } }),
  );
  const jobs = createListJobs();

  const data = $derived<HiringReport | undefined>(report.data?.status === 200 ? report.data.data : undefined);
  const busy = $derived(report.isFetching && report.isPlaceholderData);
  const jobOptions = $derived<FilterOption[]>(
    (jobs.data?.data ?? []).map((job) => ({ value: job.id, label: job.title })).toSorted((a, b) => a.label.localeCompare(b.label)),
  );
  const longest = { kind: 'preset', days: 365 } as const satisfies ReportRange;

  function update(change: (url: URL) => void) {
    const url = new URL(page.url.href);
    change(url);
    void goto(url, { replace: true, reset: false });
  }

  const setRange = (next: ReportRange) => update((url) => writeRange(url, next));
  function setJobs(next: string[]) {
    update((url) => {
      url.searchParams.delete('job');
      for (const id of next) url.searchParams.append('job', id);
    });
  }

  // The funnel: each stage kind, its applications and the share that moved on to a later row.
  const funnelRows = $derived<BarRow[]>(
    (data?.funnel ?? []).map((stage) => {
      const share = stage.movedOn === null ? null : shareText(stage.movedOn, stage.applications);
      const applications = counted(stage.applications, 'application', 'applications');
      return {
        key: stage.kind,
        label: stageKindLabel(stage.kind),
        values: [stage.applications],
        texts: [countText(stage.applications)],
        note: share === null ? null : `${share} moved on`,
        summary: `${stageKindLabel(stage.kind)}: ${applications}${share === null ? '' : `, ${share} moved on`}`,
      };
    }),
  );
  const funnelColumns: ChartColumn<FunnelStage>[] = [
    { id: 'stage', header: 'Stage', text: (stage) => stageKindLabel(stage.kind), sortBy: (stage) => stageKindLabel(stage.kind) },
    { id: 'applications', header: 'Applications', text: (stage) => countText(stage.applications), sortBy: (stage) => stage.applications },
    {
      id: 'movedOn',
      header: 'Moved on',
      text: (stage) => (stage.movedOn === null ? '' : (shareText(stage.movedOn, stage.applications) ?? '')),
      sortBy: (stage) => (stage.movedOn === null || stage.applications === 0 ? -1 : stage.movedOn / stage.applications),
    },
  ];

  // Time to hire: the longest median first, so the job that takes longest leads.
  const hireJobs = $derived((data?.timeToHire.jobs ?? []).toSorted((a, b) => b.medianDays - a.medianDays || a.jobTitle.localeCompare(b.jobTitle)));
  const hireRows = $derived<BarRow[]>(
    hireJobs.map((job) => ({
      key: job.jobId,
      label: job.jobTitle,
      values: [job.medianDays],
      texts: [daysText(job.medianDays)],
      note: counted(job.hires, 'hire', 'hires'),
      summary: `${job.jobTitle}: ${daysText(job.medianDays)}, ${counted(job.hires, 'hire', 'hires')}`,
    })),
  );
  const hireColumns: ChartColumn<JobTimeToHire>[] = [
    { id: 'job', header: 'Job', text: (job) => job.jobTitle, sortBy: (job) => job.jobTitle },
    { id: 'hires', header: 'Hires', text: (job) => countText(job.hires), sortBy: (job) => job.hires },
    { id: 'median', header: 'Median days', text: (job) => daysText(job.medianDays), sortBy: (job) => job.medianDays },
  ];

  // Sources: applications and the hires among them, side by side.
  const sourceRows = $derived<BarRow[]>(
    (data?.sources ?? []).map((source) => ({
      key: source.source,
      label: sourceName(source.source),
      values: [source.applications, source.hires],
      texts: [countText(source.applications), countText(source.hires)],
      summary: `${sourceName(source.source)}: ${counted(source.applications, 'application', 'applications')}, ${counted(source.hires, 'hire', 'hires')}`,
    })),
  );
  const sourceColumns: ChartColumn<SourceCount>[] = [
    { id: 'source', header: 'Source', text: (source) => sourceName(source.source), sortBy: (source) => sourceName(source.source) },
    { id: 'applications', header: 'Applications', text: (source) => countText(source.applications), sortBy: (source) => source.applications },
    { id: 'hires', header: 'Hires', text: (source) => countText(source.hires), sortBy: (source) => source.hires },
  ];
</script>

<svelte:head>
  <title>Reports · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Reports' }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand title="Reports">
    {#snippet tabs()}
      <!-- The filters scope every panel below, so they sit in one row on the band's bottom edge, and scroll rather than
           wrap (F5). -->
      <div data-filters class="-mx-1 flex min-w-0 items-center gap-2 overflow-x-auto px-1 pb-4">
        <DateRangeFilter bind:value={() => range, setRange} />
        <MultiFilterMenu label="Job" options={jobOptions} bind:values={() => jobIds, setJobs} />
      </div>
    {/snippet}
  </HeaderBand>

  <div class="@container">
    <div class="flex w-full flex-col px-4 py-6 md:px-6">
      {#if report.isError && !data}
        <Panel label="Reports">
          <QueryError message={describeError(report.error, 'Could not load the reports')} onretry={() => report.refetch()} inset />
        </Panel>
      {:else if !data}
        <div aria-hidden="true" class="grid gap-5 @5xl:grid-cols-2">
          {#each [0, 1, 2] as panel (panel)}
            <div class={['flex flex-col gap-3 rounded-panel border border-border bg-card p-4.5 shadow-surface', panel === 0 && '@5xl:col-span-2']}>
              <Skeleton class="h-6 w-40" />
              {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-5" style="width: {90 - row * 18}%" />{/each}
            </div>
          {/each}
        </div>
        <p class="sr-only" role="status">Loading reports…</p>
      {:else}
        <div class="grid items-start gap-5 @5xl:grid-cols-2">
          <ChartPanel
            title="Hiring funnel"
            basis="Applications that arrived in these dates, by the furthest stage they reached."
            tableId="reports-funnel"
            columns={funnelColumns}
            rows={data.funnel}
            rowKey={(stage) => stage.kind}
            {busy}
            class="@5xl:col-span-2"
          >
            {#snippet chart()}<BarChart label="Hiring funnel" series={[{ label: 'Applications', tone: 'chart-1' }]} rows={funnelRows} />{/snippet}
            {#snippet empty()}{@render nothing('No applications in these dates')}{/snippet}
          </ChartPanel>

          <ChartPanel
            title="Time to hire"
            basis="Median days from applying to being hired, for hires made in these dates."
            tableId="reports-time-to-hire"
            columns={hireColumns}
            rows={hireJobs}
            rowKey={(job) => job.jobId}
            defaultSort={{ id: 'median', desc: true }}
            {busy}
          >
            {#snippet chart()}
              {#if data.timeToHire.medianDays !== null}
                <div class="flex flex-col gap-0.5">
                  <p class="text-metric">{daysText(data.timeToHire.medianDays)}</p>
                  <p class="text-muted-foreground">Median across {counted(data.timeToHire.hires, 'hire', 'hires')}</p>
                </div>
              {/if}
              <BarChart label="Time to hire by job" series={[{ label: 'Median days', tone: 'chart-1' }]} rows={hireRows} />
            {/snippet}
            {#snippet empty()}{@render nothing('No hires in these dates')}{/snippet}
          </ChartPanel>

          <ChartPanel
            title="Sources"
            basis="Applications that arrived in these dates, and the hires among them, by where they came from."
            tableId="reports-sources"
            columns={sourceColumns}
            rows={data.sources}
            rowKey={(source) => source.source}
            defaultSort={{ id: 'applications', desc: true }}
            {busy}
          >
            {#snippet chart()}
              <BarChart
                label="Sources"
                series={[
                  { label: 'Applications', tone: 'chart-2' },
                  { label: 'Hires', tone: 'chart-1' },
                ]}
                rows={sourceRows}
              />
            {/snippet}
            {#snippet empty()}{@render nothing('No applications in these dates')}{/snippet}
          </ChartPanel>
        </div>
      {/if}
    </div>
  </div>
</main>

{#snippet nothing(title: string)}
  <Empty.Root class="py-6">
    <Empty.Header>
      <Empty.Media variant="icon"><ChartBarIcon /></Empty.Media>
      <Empty.Title>{title}</Empty.Title>
      <Empty.Description>{jobIds.length > 0 ? 'Try more dates or more jobs.' : 'Try a longer range.'}</Empty.Description>
    </Empty.Header>
    {#if range.kind === 'preset' && range.days < longest.days}
      <Empty.Content><Button variant="outline" size="lg" onclick={() => setRange(longest)}>Show the last 365 days</Button></Empty.Content>
    {/if}
  </Empty.Root>
{/snippet}
