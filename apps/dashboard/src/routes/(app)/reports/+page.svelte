<script lang="ts">
  import CalendarCheckIcon from '~icons/lucide/calendar-check';
  import ChartBarIcon from '~icons/lucide/chart-bar';
  import DownloadIcon from '~icons/lucide/download';
  import FileCheckIcon from '~icons/lucide/file-check';
  import InboxIcon from '~icons/lucide/inbox';
  import TimerIcon from '~icons/lucide/timer';
  import UserCheckIcon from '~icons/lucide/user-check';
  import type { FunnelStage, HiringReport, ReportOpenJob, ReportWeek, SourceCount } from '@screenhire/api-client';
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
  import ReportStat from '#lib/components/report-stat.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import WeekChart, { type WeekPoint } from '#lib/components/week-chart.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { clock } from '#lib/clock.svelte.js';
  import { downloadCsv, toCsv, type CsvSection } from '#lib/csv.js';
  import { describeError } from '#lib/query.js';
  import { dayText, parseDay, previousInstants, rangeInstants, readRange, writeRange, zoneName, type ReportRange } from '#lib/report-range.js';
  import { biggestDrop, changeText, counted, countText, daysText, hireRate, oneDecimal, shareText, weekDateText, weekText } from '#lib/reports.js';
  import { stageKindLabels, stageKinds } from '#lib/workflow.js';

  // Reports (design system section 7, Report; docs/architecture/reports.md): headline numbers, applications and hires by
  // week, the funnel, time to hire, sources and open jobs for the jobs the person can see, over a date range and an optional
  // set of jobs. The filters live in the address, so a link or the trip back keeps them. One request feeds every panel, so
  // their numbers agree; while new numbers load the panels keep the old ones, dimmed. Arriving and moving applications
  // refresh it (#lib/realtime.ts). Nothing here shows verification results (ADR-0004), and no sentence sits under a
  // panel's title (W9).

  const range = $derived(readRange(page.url.searchParams));
  const jobIds = $derived(page.url.searchParams.getAll('job'));
  const compare = $derived(page.url.searchParams.get('compare') !== 'off');
  // Today, from the dashboard's minute clock, so a page left open past midnight moves its window on with the day.
  const today = $derived(dayText(new Date(clock.now)));
  const todayAt = $derived(parseDay(today) ?? new Date());
  const instants = $derived(rangeInstants(range, todayAt));
  const before = $derived(previousInstants(range, todayAt));
  const timeZone = zoneName();

  const report = createGetHiringReport(
    () => ({
      ...instants,
      ...(compare ? { previousFrom: before.from, previousTo: before.to } : {}),
      timeZone,
      jobId: jobIds.length > 0 ? jobIds : undefined,
    }),
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
  const setCompare = (on: boolean) =>
    update((url) => {
      if (on) url.searchParams.delete('compare');
      else url.searchParams.set('compare', 'off');
    });

  // A numeric column: the number sorts and exports, its text is how the table shows it.
  function numeric<T>(id: string, header: string, value: (row: T) => number, text: (row: T) => string): ChartColumn<T> {
    return { id, header, text, sortBy: value, csv: value };
  }
  // A share column: the table shows `62%`, the export holds 0.62, and a row with nothing to share sorts last.
  function share<T>(id: string, header: string, part: (row: T) => number | null, of: (row: T) => number, blank?: string): ChartColumn<T> {
    const fraction = (row: T) => {
      const value = part(row);
      return value === null || of(row) === 0 ? null : Math.round((value / of(row)) * 1000) / 1000;
    };
    return {
      id,
      header,
      text: (row) => {
        const value = part(row);
        return value === null ? '' : (shareText(value, of(row)) ?? '');
      },
      blank,
      sortBy: (row) => fraction(row) ?? -1,
      csv: fraction,
    };
  }

  // Headline numbers: each with its change from the previous period, when asked for.
  const previous = $derived(compare ? data?.previous : undefined);
  const medianText = $derived(data?.headline.medianDaysToHire == null ? 'None' : daysText(data.headline.medianDaysToHire));
  const interviewShare = $derived(data ? shareText(data.headline.reachedInterview, data.headline.applications) : null);
  // Five cards: one row on a wide page, three over two in the middle, two columns on a phone with the last across both,
  // so no card sits beside an empty cell.
  const statGrid = 'grid grid-cols-2 gap-5 @3xl:grid-cols-6 @5xl:grid-cols-5';
  const statSpans = [
    '@3xl:col-span-2 @5xl:col-span-1',
    '@3xl:col-span-2 @5xl:col-span-1',
    '@3xl:col-span-2 @5xl:col-span-1',
    '@3xl:col-span-3 @5xl:col-span-1',
    'col-span-2 @3xl:col-span-3 @5xl:col-span-1',
  ];

  // Applications and hires by week.
  const weekPoints = $derived<WeekPoint[]>(
    (data?.weekly ?? []).map((week) => ({
      key: week.weekStart,
      label: weekText(week.weekStart),
      applications: week.applications,
      hires: week.hires,
      summary: `Week of ${weekDateText(week.weekStart)}: ${counted(week.applications, 'application', 'applications')}, ${counted(week.hires, 'hire', 'hires')}`,
    })),
  );
  const weekColumns: ChartColumn<ReportWeek>[] = [
    { id: 'week', header: 'Week of', text: (week) => weekDateText(week.weekStart), sortBy: (week) => week.weekStart, csv: (week) => week.weekStart },
    numeric('applications', 'Applications', (week) => week.applications, (week) => countText(week.applications)),
    numeric('hires', 'Hires', (week) => week.hires, (week) => countText(week.hires)),
  ];

  // The funnel: each stage kind, its applications, its share of all applicants and the share that moved on. Each name links
  // to the candidates behind it: the applications that arrived in these dates and reached that stage.
  const dropKind = $derived(biggestDrop(data?.funnel ?? []));
  const reachedHref = (kind: string) => {
    const query = new URLSearchParams({ reached: kind, from: instants.from, to: instants.to });
    for (const id of jobIds) query.append('job', id);
    return `/candidates?${query}`;
  };
  const funnelRows = $derived<BarRow[]>(
    (data?.funnel ?? []).map((stage) => {
      const moved = stage.movedOn === null ? null : shareText(stage.movedOn, stage.applications);
      const ofAll = shareText(stage.applications, data?.funnel[0]?.applications ?? 0);
      const name = stageKindLabels[stage.kind];
      const drop = stage.kind === dropKind;
      return {
        key: stage.kind,
        label: name,
        values: [stage.applications],
        texts: [countText(stage.applications)],
        share: ofAll,
        note: moved === null ? null : `${moved} moved on`,
        drop,
        href: reachedHref(stage.kind),
        summary: `${name}: ${counted(stage.applications, 'application', 'applications')}${ofAll ? `, ${ofAll} of applicants` : ''}${moved === null ? '' : `, ${moved} moved on`}${drop ? ', the biggest drop' : ''}`,
      };
    }),
  );
  const funnelColumns = $derived<ChartColumn<FunnelStage>[]>([
    { id: 'stage', header: 'Stage', text: (stage) => stageKindLabels[stage.kind], sortBy: (stage) => stageKinds.indexOf(stage.kind), csv: (stage) => stageKindLabels[stage.kind], href: (stage) => reachedHref(stage.kind) },
    numeric('applications', 'Applications', (stage) => stage.applications, (stage) => countText(stage.applications)),
    share('ofApplicants', 'Of applicants', (stage) => stage.applications, () => data?.funnel[0]?.applications ?? 0),
    share('movedOn', 'Moved on', (stage) => stage.movedOn, (stage) => stage.applications, 'No later stage'),
  ]);

  // Time to hire: median days in each stage, then the median by job with the longest first. One table holds both.
  interface HireRow {
    key: string;
    by: 'Stage' | 'Job';
    name: string;
    hires: number;
    medianDays: number;
  }
  const hireStages = $derived<HireRow[]>(
    (data?.timeToHire.stages ?? []).map((stage) => ({ key: `stage-${stage.kind}`, by: 'Stage', name: stageKindLabels[stage.kind], hires: stage.hires, medianDays: stage.medianDays })),
  );
  const hireJobs = $derived<HireRow[]>(
    (data?.timeToHire.jobs ?? [])
      .toSorted((a, b) => b.medianDays - a.medianDays || a.jobTitle.localeCompare(b.jobTitle))
      .map((job) => ({ key: `job-${job.jobId}`, by: 'Job', name: job.jobTitle, hires: job.hires, medianDays: job.medianDays })),
  );
  const hireTable = $derived([...hireStages, ...hireJobs]);
  const hireBars = (rows: HireRow[]): BarRow[] =>
    rows.map((row) => ({
      key: row.key,
      label: row.name,
      values: [row.medianDays],
      texts: [daysText(row.medianDays)],
      note: counted(row.hires, 'hire', 'hires'),
      summary: `${row.name}: ${daysText(row.medianDays)}, ${counted(row.hires, 'hire', 'hires')}`,
    }));
  const hireColumns: ChartColumn<HireRow>[] = [
    { id: 'name', header: 'Name', text: (row) => row.name, sortBy: (row) => row.name, csv: (row) => row.name },
    { id: 'by', header: 'By', text: (row) => row.by, sortBy: (row) => row.by, csv: (row) => row.by },
    numeric('hires', 'Hires', (row) => row.hires, (row) => countText(row.hires)),
    { id: 'median', header: 'Median days', text: (row) => daysText(row.medianDays), sortBy: (row) => row.medianDays, csv: (row) => oneDecimal(row.medianDays) },
  ];

  // Sources: applications and the hires among them side by side, with the hire rate.
  const sourceRows = $derived<BarRow[]>(
    (data?.sources ?? []).map((source) => {
      const rate = hireRate(source.hires, source.applications);
      return {
        key: source.key,
        label: source.name,
        values: [source.applications, source.hires],
        texts: [countText(source.applications), countText(source.hires)],
        note: rate === null ? null : `${rate} hired`,
        summary: `${source.name}: ${counted(source.applications, 'application', 'applications')}, ${counted(source.hires, 'hire', 'hires')}${rate === null ? '' : `, ${rate} hire rate`}`,
      };
    }),
  );
  const sourceColumns: ChartColumn<SourceCount>[] = [
    { id: 'source', header: 'Source', text: (source) => source.name, sortBy: (source) => source.name, csv: (source) => source.name },
    numeric('applications', 'Applications', (source) => source.applications, (source) => countText(source.applications)),
    numeric('hires', 'Hires', (source) => source.hires, (source) => countText(source.hires)),
    share('hireRate', 'Hire rate', (source) => source.hires, (source) => source.applications),
  ];

  // Open jobs: a table only.
  const openJobColumns: ChartColumn<ReportOpenJob>[] = [
    { id: 'job', header: 'Job', text: (job) => job.title, sortBy: (job) => job.title, csv: (job) => job.title, href: (job) => `/jobs/${job.jobId}` },
    numeric('applications', 'Applications', (job) => job.applications, (job) => countText(job.applications)),
    numeric('inInterview', 'In interview', (job) => job.inInterview, (job) => countText(job.inInterview)),
    numeric('daysOpen', 'Days open', (job) => job.daysOpen, (job) => countText(job.daysOpen)),
    numeric('hires', 'Hires', (job) => job.hires, (job) => countText(job.hires)),
  ];

  // Export CSV: every panel's table, as the page shows it, with raw numbers.
  function section<T>(title: string, columns: readonly ChartColumn<T>[], rows: readonly T[]): CsvSection {
    return { title, header: columns.map((column) => column.header), rows: rows.map((row) => columns.map((column) => column.csv?.(row) ?? column.text(row))) };
  }
  function exportCsv() {
    if (!data) return;
    const withPrevious = compare && data.previous;
    const numbers = (get: (h: HiringReport['headline']) => number | null) => [get(data.headline), ...(withPrevious ? [get(data.previous!)] : [])];
    const measures: [string, (h: HiringReport['headline']) => number | null][] = [
      ['Applications', (h) => h.applications],
      ['Reached interview', (h) => h.reachedInterview],
      ['Offers', (h) => h.offers],
      ['Hires', (h) => h.hires],
      ['Median days to hire', (h) => (h.medianDaysToHire === null ? null : oneDecimal(h.medianDaysToHire))],
    ];
    const csv = toCsv([
      { title: 'Headline numbers', header: ['Measure', 'These dates', ...(withPrevious ? ['Previous dates'] : [])], rows: measures.map(([name, get]) => [name, ...numbers(get)]) },
      section('Applications and hires by week', weekColumns, data.weekly),
      section('Hiring funnel', funnelColumns, data.funnel),
      section('Time to hire', hireColumns, hireTable),
      section('Sources', sourceColumns, data.sources),
      section('Open jobs', openJobColumns, data.openJobs),
    ]);
    const last = new Date(new Date(data.to).getTime() - 1);
    downloadCsv(`screenhire-reports-${dayText(new Date(data.from))}-to-${dayText(last)}.csv`, csv);
  }
</script>

<svelte:head>
  <title>Reports · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Reports' }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand title="Reports">
    {#snippet actions()}
      <Button variant="outline" disabled={!data} onclick={exportCsv}><DownloadIcon aria-hidden="true" />Export CSV</Button>
    {/snippet}
    {#snippet tabs()}
      <!-- The filters scope every panel below, so they sit in one row on the band's bottom edge, and scroll rather than
           wrap (F5). -->
      <div data-filters class="-mx-1 flex min-w-0 items-center gap-2 overflow-x-auto px-1 pb-4">
        <DateRangeFilter bind:value={() => range, setRange} />
        <MultiFilterMenu label="Job" options={jobOptions} bind:values={() => jobIds, setJobs} />
        <span class="ml-2 flex shrink-0 items-center gap-2 pointer-coarse:min-h-11">
          <Switch id="reports-compare" checked={compare} onCheckedChange={setCompare} />
          <Label for="reports-compare" class="whitespace-nowrap">Compare periods</Label>
        </span>
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
        <div aria-hidden="true" class="flex flex-col gap-5">
          <div class={statGrid}>
            {#each statSpans as span, card (card)}
              <div class={['flex flex-col gap-3 rounded-panel border border-border bg-card p-4.5 shadow-surface', span]}>
                <Skeleton class="h-8 w-32" />
                <Skeleton class="h-9 w-20" />
              </div>
            {/each}
          </div>
          <div class="grid gap-5 @5xl:grid-cols-2">
            {#each [0, 1, 2] as panel (panel)}
              <div class={['flex flex-col gap-3 rounded-panel border border-border bg-card p-4.5 shadow-surface', panel === 0 && '@5xl:col-span-2']}>
                <Skeleton class="h-6 w-40" />
                {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-5" style="width: {90 - row * 18}%" />{/each}
              </div>
            {/each}
          </div>
        </div>
        <p class="sr-only" role="status">Loading reports…</p>
      {:else if data.headline.applications === 0 && data.headline.hires === 0}
        <!-- Nothing arrived and nobody was hired: one empty state for the page, not the same one in each panel (W7). -->
        <Panel label="Reports">{@render nothing('Nothing in these dates', true)}</Panel>
      {:else}
        <div class="flex flex-col gap-5">
          <div
            class={[statGrid, 'transition-opacity motion-reduce:transition-none', busy && 'opacity-60']}
            aria-busy={busy || undefined}
            role="group"
            aria-label="Headline numbers"
          >
            <ReportStat class={statSpans[0]} icon={InboxIcon} title="Applications" value={countText(data.headline.applications)} change={changeText(data.headline.applications, previous?.applications)} />
            <ReportStat
              class={statSpans[1]}
              icon={CalendarCheckIcon}
              title="Reached interview"
              value={countText(data.headline.reachedInterview)}
              share={interviewShare === null ? null : `${interviewShare} of applications`}
              change={changeText(data.headline.reachedInterview, previous?.reachedInterview)}
            />
            <ReportStat class={statSpans[2]} icon={FileCheckIcon} title="Offers" value={countText(data.headline.offers)} change={changeText(data.headline.offers, previous?.offers)} />
            <ReportStat class={statSpans[3]} icon={UserCheckIcon} title="Hires" value={countText(data.headline.hires)} change={changeText(data.headline.hires, previous?.hires)} />
            <ReportStat
              class={statSpans[4]}
              icon={TimerIcon}
              title="Median time to hire"
              value={medianText}
              change={changeText(data.headline.medianDaysToHire, previous?.medianDaysToHire, 'days')}
            />
          </div>

          <div class="grid items-start gap-5 @5xl:grid-cols-2">
            <ChartPanel
              title="Applications and hires by week"
              basis="Applications that arrived and hires made in each week of these dates."
              tableId="reports-weekly"
              columns={weekColumns}
              rows={data.weekly}
              rowKey={(week) => week.weekStart}
              {busy}
              class="@5xl:col-span-2"
            >
              {#snippet chart()}<WeekChart label="Applications and hires by week" weeks={weekPoints} />{/snippet}
            </ChartPanel>

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
              {#snippet empty()}{@render nothing('No applications in these dates', true)}{/snippet}
            </ChartPanel>

            <ChartPanel
              title="Time to hire"
              basis="Median days hires spent in each stage, and from applying to being hired by job, for hires made in these dates."
              tableId="reports-time-to-hire"
              columns={hireColumns}
              rows={hireTable}
              rowKey={(row) => row.key}
              {busy}
            >
              {#snippet chart()}
                <div class="flex flex-col gap-5">
                  {#if hireStages.length > 0}
                    <div class="flex flex-col gap-2">
                      <h3 class="text-caption font-semibold text-muted-foreground">In each stage</h3>
                      <BarChart label="Median days in each stage" series={[{ label: 'Median days', tone: 'chart-1' }]} rows={hireBars(hireStages)} />
                    </div>
                  {/if}
                  {#if hireJobs.length > 0}
                    <div class="flex flex-col gap-2">
                      <h3 class="text-caption font-semibold text-muted-foreground">By job</h3>
                      <BarChart label="Median days to hire by job" series={[{ label: 'Median days', tone: 'chart-1' }]} rows={hireBars(hireJobs)} />
                    </div>
                  {/if}
                </div>
              {/snippet}
              {#snippet empty()}{@render nothing('No hires in these dates', data.headline.applications > 0)}{/snippet}
            </ChartPanel>

            <ChartPanel
              title="Sources"
              basis="Applications that arrived in these dates and the hires among them, by source."
              tableId="reports-sources"
              columns={sourceColumns}
              rows={data.sources}
              rowKey={(source) => source.key}
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
              {#snippet empty()}{@render nothing('No applications in these dates', false)}{/snippet}
            </ChartPanel>

            <ChartPanel
              title="Open jobs"
              tableId="reports-open-jobs"
              columns={openJobColumns}
              rows={data.openJobs}
              rowKey={(job) => job.jobId}
              defaultSort={{ id: 'applications', desc: true }}
              {busy}
              class="@5xl:col-span-2"
            >
              {#snippet empty()}{@render nothing('No open jobs', false)}{/snippet}
            </ChartPanel>
          </div>
        </div>
      {/if}
    </div>
  </div>
</main>

<!-- `offer`: whether this empty state carries the one "Show the last 365 days", so a page shows it once (W7). -->
{#snippet nothing(title: string, offer: boolean)}
  <Empty.Root class="py-6">
    <Empty.Header>
      <Empty.Media variant="icon"><ChartBarIcon /></Empty.Media>
      <Empty.Title>{title}</Empty.Title>
      {#if offer}<Empty.Description>{jobIds.length > 0 ? 'Try more dates or more jobs.' : 'Try a longer range.'}</Empty.Description>{/if}
    </Empty.Header>
    {#if offer && range.kind === 'preset' && range.days < longest.days}
      <Empty.Content><Button variant="outline" size="lg" onclick={() => setRange(longest)}>Show the last 365 days</Button></Empty.Content>
    {/if}
  </Empty.Root>
{/snippet}
