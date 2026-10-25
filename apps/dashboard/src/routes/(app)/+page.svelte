<script lang="ts">
  import BriefcaseIcon from '@lucide/svelte/icons/briefcase';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { JobStatus, JobSummary } from '@screenhire/api-client';
  import { createListJobs, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import {
    createColumnHelper,
    createSortedRowModel,
    createTable,
    FlexRender,
    renderSnippet,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import ActionBar from '#lib/components/action-bar.svelte';
  import CloseJobDialog from '#lib/components/close-job-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { employmentLabel } from '#lib/format.js';
  import { jobStatus } from '#lib/job-status.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  const queryClient = useQueryClient();
  // Live JobPublished, JobClosed and HiringTemplateVersionPublished events refetch this list (#lib/realtime.ts).
  const jobs = createListJobs();
  const publish = createPublishJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  // Tables become list items on phones (design system section 8), with the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();

  const all = $derived(jobs.data?.data ?? []);
  const statuses: JobStatus[] = ['Open', 'Draft', 'Closed'];
  let statusFilter = $state<JobStatus | null>(null);
  let search = $state('');
  const countOf = (status: JobStatus) => all.filter((job) => job.status === status).length;
  const shown = $derived.by(() => {
    const words = search.trim().toLowerCase();
    return all.filter(
      (job) =>
        (!statusFilter || job.status === statusFilter) &&
        (!words || [job.title, job.department ?? '', job.location].some((text) => text.toLowerCase().includes(words))),
    );
  });
  const filtering = $derived(Boolean(statusFilter || search.trim()));
  const panelTitle = $derived(statusFilter ? `${jobStatus[statusFilter].label} jobs` : 'All jobs');

  // Every column sorts (L2), and the list opens on Last updated, newest first, as things people edit do. The person's
  // sort is remembered as `jobs`; the phone's Sort chip offers the same choices.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, JobSummary>();
  // A job's status sorts in its natural order, not by its words.
  const statusOrder: Record<JobStatus, number> = { Open: 0, Draft: 1, Closed: 2 };
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, JobSummary, any>[] = [
    // The order is a number, which TanStack would sort descending first; Open comes first.
    column.accessor((job) => statusOrder[job.status], { id: 'status', header: 'Status', sortFn: 'basic', sortDescFirst: false, cell: ({ row }) => renderSnippet(statusCell, row.original) }),
    column.accessor('title', { header: 'Title', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(titleCell, row.original) }),
    column.accessor('location', { header: 'Location', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(locationCell, row.original) }),
    column.accessor((job) => employmentLabel(job.employmentType), { id: 'type', header: 'Type', sortFn: 'alphanumeric', cell: ({ row }) => employmentLabel(row.original.employmentType) }),
    // A draft has not been published, so it sorts last either way.
    column.accessor((job) => job.publishedAt ?? undefined, {
      id: 'published',
      header: 'Published',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(publishedCell, row.original),
    }),
    column.accessor((job) => job.lastUpdated.at ?? undefined, {
      id: 'updatedAt',
      header: 'Last updated',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(updatedCell, row.original),
    }),
    column.display({ id: 'actions', header: () => renderSnippet(actionsHeader, undefined), cell: ({ row }) => renderSnippet(actionsCell, row.original) }),
  ];
  const sort = createTableSort('jobs', { id: 'updatedAt', desc: true }, ['status', 'title', 'location', 'type', 'published', 'updatedAt']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return shown;
    },
    getRowId: (job) => job.id,
  });
  const rows = $derived(table.getRowModel().rows);

  let busyJobId = $state<string | null>(null);
  let failure = $state('');
  // The job whose "Close & remove everywhere" waits for its confirmation, which opens once its row menu has closed.
  let closingId = $state<string | null>(null);
  let closeChosen = false;
  let confirmClose = $state(false);

  async function publishJob(job: JobSummary) {
    busyJobId = job.id;
    failure = '';
    try {
      await publish.mutateAsync({ jobId: job.id });
    } catch (error) {
      failure = describeError(error, `Could not publish ${job.title}`);
    } finally {
      busyJobId = null;
    }
  }

  function askToClose(job: JobSummary) {
    closingId = job.id;
    closeChosen = true;
  }

  function onMenuClosed() {
    if (closeChosen) confirmClose = true;
    closeChosen = false;
  }

  function clearFilters() {
    statusFilter = null;
    search = '';
  }
</script>

<svelte:head>
  <title>Jobs · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs' }]} />

<main class={['flex flex-col', sidebar.isMobile && 'pb-28']}>
  <!-- The page's primary action; a phone has it in the action bar (section 8). -->
  <HeaderBand
    title="Jobs"
    count={jobs.isSuccess ? all.length : undefined}
    countLabel={all.length === 1 ? 'job' : 'jobs'}
    actions={sidebar.isMobile ? undefined : newJob}
  />

  <!-- The content container (S6): the list template's one table panel. -->
  <div class="flex w-full flex-col px-4 py-6 md:px-6">
    <Panel title={panelTitle} flush footer={failure ? failureFooter : undefined}>
      {#snippet tools()}
        <SearchField bind:value={search} label="Search jobs" class="ml-auto flex-[0_1_15rem]" />
      {/snippet}

      <!-- The status filter: one line that scrolls inside the panel rather than wrapping (F5). On a phone the Sort chip
           stays in view beside it, since list items have no headers (L2). -->
      <div class="flex items-center gap-2 border-b border-border-subtle px-4.5 py-2 max-md:px-4">
        <div class="min-w-0 flex-1 overflow-x-auto">
          <div role="group" aria-label="Status" class="flex w-max gap-1">
            <Toggle size="lg" bind:pressed={() => statusFilter === null, () => (statusFilter = null)}>
              All
              {#if jobs.isSuccess}<Badge variant="count">{all.length}</Badge>{/if}
            </Toggle>
            {#each statuses as status (status)}
              <Toggle size="lg" bind:pressed={() => statusFilter === status, () => (statusFilter = status)}>
                {jobStatus[status].label}
                {#if jobs.isSuccess}<Badge variant="count">{countOf(status)}</Badge>{/if}
              </Toggle>
            {/each}
          </div>
        </div>
        {#if sidebar.isMobile && jobs.isSuccess}<SortMenu {table} class="shrink-0" />{/if}
      </div>

      {#if jobs.isError}
        <div class="p-4">
          <QueryError message={describeError(jobs.error, 'Could not load jobs')} onretry={() => jobs.refetch()} inset />
        </div>
      {:else if jobs.isPending}
        <!-- Rows of the table's shape: a status, a title and a date (E1). -->
        <div aria-hidden="true" class="flex flex-col">
          {#each [0, 1, 2] as row (row)}
            <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4 last:border-0">
              <Skeleton class="h-4 w-16" />
              <Skeleton class="h-4 min-w-0 flex-1" />
              <Skeleton class="h-4 w-24" />
            </div>
          {/each}
        </div>
        <p class="sr-only" role="status">Loading jobs…</p>
      {:else if shown.length === 0}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon"><BriefcaseIcon /></Empty.Media>
            <Empty.Title>{filtering ? 'No jobs match' : 'No jobs yet'}</Empty.Title>
            <Empty.Description>
              {filtering ? 'Nothing matches this search and status.' : 'A new job starts as a draft that only your team sees.'}
            </Empty.Description>
          </Empty.Header>
          <Empty.Content>
            {#if filtering}
              <Button variant="outline" size="lg" onclick={clearFilters}>Clear filters</Button>
            {:else}
              <Button href="/jobs/new" variant="outline" size="lg"><PlusIcon aria-hidden="true" />New job</Button>
            {/if}
          </Empty.Content>
        </Empty.Root>
      {:else if sidebar.isMobile}
        <ul aria-label="Jobs">
          {#each rows as row (row.id)}
            {@const job = row.original}
            <ListItem href={`/jobs/${job.id}`} title={job.title}>
              {#snippet status()}<StatusBadge {...jobStatus[job.status]} quiet />{/snippet}
              {#snippet meta()}{@render published(job)}{/snippet}
              {#snippet facts()}
                <!-- A long location is cut to the item's width, whole in its tooltip (F5). relative: above the item's
                     link, so the tooltip opens on tap. -->
                <Badge variant="muted" class="relative max-w-full"><TruncatedText text={job.location} /></Badge>
                <Badge variant="muted">{employmentLabel(job.employmentType)}</Badge>
                {#if job.templateUpdateAvailable}<Badge variant="muted">Template update</Badge>{/if}
              {/snippet}
            </ListItem>
          {/each}
        </ul>
      {:else}
        <!-- Location gives way before the title in a narrow panel: the title is the row's name (F5). relative: the
             Actions header's sr-only label is absolute, and would escape the scroller and widen the page (F1). -->
        <div class="@container/jobs relative overflow-x-auto">
          <table aria-label="Jobs" class="w-full border-collapse text-left">
            <thead class="bg-muted">
              {#each table.getHeaderGroups() as group (group.id)}
                <tr>
                  {#each group.headers as header (header.id)}
                    <SortHeader
                      {header}
                      class={[
                        (header.id === 'status' || header.id === 'actions') && 'px-4',
                        header.id === 'actions' && 'w-14',
                        (header.id === 'published' || header.id === 'updatedAt' || header.id === 'actions') && 'text-right',
                      ]}
                    />
                  {/each}
                </tr>
              {/each}
            </thead>
            <tbody>
              {#each rows as row (row.id)}
                <!-- The whole row is the link (section 9): the title's link covers it, and the cells with their own
                     controls sit above it. -->
                <tr class="relative h-12 border-b border-border-subtle last:border-b-0 hover:bg-muted">
                  {#each row.getAllCells() as cell (cell.id)}
                    <td
                      class={[
                        'px-3 whitespace-nowrap',
                        (cell.column.id === 'status' || cell.column.id === 'actions') && 'px-4',
                        // The title takes the room the other columns leave, and never less than 12rem: below that the
                        // table scrolls in the panel. A long one is cut, whole in its tooltip (F5).
                        cell.column.id === 'title' && 'w-full max-w-0 min-w-48',
                        cell.column.id === 'location' && 'max-w-32 text-muted-foreground @4xl/jobs:max-w-60',
                        (cell.column.id === 'type' || cell.column.id === 'published' || cell.column.id === 'updatedAt') && 'text-muted-foreground',
                        (cell.column.id === 'published' || cell.column.id === 'updatedAt') && 'text-right tabular-nums',
                        cell.column.id === 'actions' && 'text-right',
                      ]}
                    >
                      <FlexRender {cell} />
                    </td>
                  {/each}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </Panel>
  </div>

  {#if sidebar.isMobile}
    <ActionBar>{@render newJob()}</ActionBar>
  {/if}
</main>

{#snippet newJob()}
  <Button href="/jobs/new" size="lg"><PlusIcon aria-hidden="true" />New job</Button>
{/snippet}

{#snippet statusCell(job: JobSummary)}<StatusBadge {...jobStatus[job.status]} quiet />{/snippet}

{#snippet titleCell(job: JobSummary)}
  <!-- The row's name, with at most one badge after it (L1). -->
  <span class="flex min-w-0 items-center gap-2">
    <TruncatedText text={job.title} href={`/jobs/${job.id}`} class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline" />
    {#if job.templateUpdateAvailable}<Badge variant="muted" class="shrink-0">Template update</Badge>{/if}
  </span>
{/snippet}

{#snippet locationCell(job: JobSummary)}<span class="relative block min-w-0"><TruncatedText text={job.location} /></span>{/snippet}

{#snippet publishedCell(job: JobSummary)}<span class="relative">{@render published(job)}</span>{/snippet}

{#snippet updatedCell(job: JobSummary)}
  {#if job.lastUpdated.at}<span class="relative"><DateText at={job.lastUpdated.at} /></span>{/if}
{/snippet}

{#snippet actionsHeader()}<span class="sr-only">Actions</span>{/snippet}

{#snippet actionsCell(job: JobSummary)}
  {#if job.status !== 'Closed'}
    <span class="relative inline-flex">{@render rowMenu(job)}</span>
  {/if}
{/snippet}

{#snippet published(job: JobSummary)}
  {#if job.publishedAt}
    <DateText at={job.publishedAt} />
  {:else}
    -
  {/if}
{/snippet}

{#snippet rowMenu(job: JobSummary)}
  <MoreMenu label={`Actions for ${job.title}`} size="icon" quiet onclosed={onMenuClosed}>
    {#if job.status === 'Draft'}
      <DropdownMenu.Item disabled={busyJobId === job.id} onSelect={() => publishJob(job)}>Publish to every board</DropdownMenu.Item>
    {:else}
      <DropdownMenu.Item variant="destructive" onSelect={() => askToClose(job)}>Close &amp; remove everywhere</DropdownMenu.Item>
    {/if}
  </MoreMenu>
{/snippet}

{#snippet failureFooter()}
  <FailureText message={failure} />
{/snippet}

<CloseJobDialog jobId={closingId} bind:open={confirmClose} />
