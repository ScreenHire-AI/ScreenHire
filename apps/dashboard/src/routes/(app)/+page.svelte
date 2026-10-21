<script lang="ts">
  import BriefcaseIcon from '@lucide/svelte/icons/briefcase';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { JobStatus, JobSummary } from '@screenhire/api-client';
  import { createListJobs, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
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
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { employmentLabel } from '#lib/format.js';
  import { jobStatus } from '#lib/job-status.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

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
    wide
  />

  <!-- The content container (S6): the list template's one table panel. -->
  <div class="mx-auto flex w-full max-w-page flex-col px-4 py-6 md:px-6 2xl:max-w-page-wide">
    <Panel title={panelTitle} flush footer={failure ? failureFooter : undefined}>
      {#snippet tools()}
        <SearchField bind:value={search} label="Search jobs" class="ml-auto flex-[0_1_15rem]" />
      {/snippet}

      <!-- The status filter: one line that scrolls inside the panel rather than wrapping (F5). -->
      <div class="flex items-center overflow-x-auto border-b border-border-subtle px-4.5 py-2 max-md:px-4">
        <div role="group" aria-label="Status" class="flex shrink-0 gap-1">
          <Toggle size="lg" bind:pressed={() => statusFilter === null, () => (statusFilter = null)}>All</Toggle>
          {#each statuses as status (status)}
            <Toggle size="lg" bind:pressed={() => statusFilter === status, () => (statusFilter = status)}>
              {jobStatus[status].label}
              {#if jobs.isSuccess}<Badge variant="count">{countOf(status)}</Badge>{/if}
            </Toggle>
          {/each}
        </div>
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
          {#each shown as job (job.id)}
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
        <Table.Root aria-label="Jobs">
          <Table.Header>
            <Table.Row class="bg-muted hover:bg-muted">
              <Table.Head class="px-4 text-caption text-muted-foreground">Status</Table.Head>
              <Table.Head class="text-caption text-muted-foreground">Title</Table.Head>
              <Table.Head class="text-caption text-muted-foreground">Location</Table.Head>
              <Table.Head class="text-caption text-muted-foreground">Type</Table.Head>
              <Table.Head class="text-right text-caption text-muted-foreground">Published</Table.Head>
              <Table.Head class="w-14 px-4"><span class="sr-only">Actions</span></Table.Head>
            </Table.Row>
          </Table.Header>
          <Table.Body>
            {#each shown as job (job.id)}
              <!-- The whole row is the link (section 9): the title's link covers it, and the cells with their own
                   controls sit above it. -->
              <Table.Row class="relative h-12 border-border-subtle hover:bg-muted">
                <Table.Cell class="px-4"><StatusBadge {...jobStatus[job.status]} quiet /></Table.Cell>
                <!-- The title takes the room the other columns leave; a long one is cut, whole in its tooltip (F5). -->
                <Table.Cell class="w-full max-w-0">
                  <span class="flex min-w-0 items-center gap-2">
                    <TruncatedText
                      text={job.title}
                      href={`/jobs/${job.id}`}
                      class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
                    />
                    {#if job.templateUpdateAvailable}<Badge variant="muted" class="shrink-0">Template update</Badge>{/if}
                  </span>
                </Table.Cell>
                <Table.Cell class="max-w-60 text-muted-foreground">
                  <span class="relative block min-w-0"><TruncatedText text={job.location} /></span>
                </Table.Cell>
                <Table.Cell class="text-muted-foreground">{employmentLabel(job.employmentType)}</Table.Cell>
                <Table.Cell class="text-right text-muted-foreground tabular-nums"><span class="relative">{@render published(job)}</span></Table.Cell>
                <Table.Cell class="px-4 text-right">
                  {#if job.status !== 'Closed'}
                    <span class="relative inline-flex">{@render rowMenu(job)}</span>
                  {/if}
                </Table.Cell>
              </Table.Row>
            {/each}
          </Table.Body>
        </Table.Root>
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
