<script lang="ts">
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import SendIcon from '@lucide/svelte/icons/send';
  import WorkflowIcon from '@lucide/svelte/icons/workflow';
  import type { ApplicationSummary } from '@screenhire/api-client';
  import { ApiError, createGetJob, createListBoards, createListJobApplications, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import CloseJobDialog from '#lib/components/close-job-dialog.svelte';
  import DistributionPanel from '#lib/components/distribution-panel.svelte';
  import Fact from '#lib/components/fact.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import IntegrityOverview from '#lib/components/integrity-overview.svelte';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { boardRows } from '#lib/distribution.js';
  import { employmentLabel, formatSalary, sourceLabel } from '#lib/format.js';
  import { chipOf, noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { jobStatus } from '#lib/job-status.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  // ApplicationSubmitted, ApplicationMoved, IntegrityAssessed and JobPostingsChanged refetch them live (#lib/realtime.ts).
  const job = createGetJob(() => params.jobId);
  const applications = createListJobApplications(() => params.jobId);
  const boards = createListBoards();
  const moves = createMoves();
  // Phones fold the secondary actions into More (design system section 8), with the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();
  const publish = createPublishJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const detail = $derived(job.data?.data);
  const stages = $derived(detail?.workflow.stages ?? []);
  const all = $derived(applications.data?.data ?? []);
  const boardCount = $derived(detail ? boardRows(boards.data?.data ?? [], detail.postings).length : 0);

  // Set from the overview's legend or the pipeline's filter menu, through the pipeline's setIntegrityFilter.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  let pipeline = $state<JobPipeline>();
  let view = $state<'table' | 'board'>('table');
  let activeId = $state<string | null>(null);
  let confirmClose = $state(false);
  // "Close & remove everywhere" was chosen; its confirmation opens once the menu has closed.
  let closeChosen = false;
  let publishFailure = $state('');

  const active = $derived(all.find((application) => application.id === activeId));

  // A draft's primary action: publish to every board at once.
  async function publishEverywhere() {
    publishFailure = '';
    try {
      await publish.mutateAsync({ jobId: params.jobId });
    } catch (error) {
      publishFailure = describeError(error, 'Could not publish the job');
    }
  }

  const notFound = $derived(job.error instanceof ApiError && job.error.status === 404);
</script>

<svelte:head>
  <title>{detail?.title ?? 'Job'} · ScreenHire</title>
</svelte:head>

<TopBar
  crumbs={[{ label: 'Jobs', href: '/' }, ...(detail?.department ? [{ label: detail.department }] : []), { label: detail?.title ?? 'Job' }]}
  back
/>

<main class="flex flex-col">
  <!-- A failed refetch keeps the job on screen; the error replaces it only when there is nothing to show. -->
  {#if job.isError && !detail}
    <HeaderBand title={notFound ? 'Job not found' : 'Job'} />
    <div class="mx-auto w-full max-w-page px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Details' : 'Could not load the job'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It does not exist, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/" variant="outline" size="lg">Back to jobs</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail}
    <!-- Skeletons of the header band and the two columns (E1). -->
    <div aria-hidden="true" class="border-b border-border bg-card">
      <div class="mx-auto flex w-full max-w-page flex-col gap-4 px-4 py-5 md:px-6">
        <Skeleton class="h-9 w-full max-w-80" />
        <Skeleton class="h-10 w-full max-w-120" />
      </div>
    </div>
    <div aria-hidden="true" class="mx-auto flex w-full max-w-page flex-wrap items-start gap-5 px-4 py-6 md:px-6">
      <Skeleton class="h-80 min-w-0 flex-[999_1_520px] rounded-panel" />
      <Skeleton class="h-48 min-w-0 max-w-95 flex-[1_1_320px] rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the job…</p>
  {:else}
    <HeaderBand title={detail.title}>
      {#snippet status()}<StatusBadge {...jobStatus[detail.status]} />{/snippet}
      {#snippet actions()}
        {#if detail.status === 'Draft' && boardCount > 0}
          <!-- The one primary action; on a phone it sits in a bar at the bottom of the screen (section 8). -->
          <div class="max-md:fixed max-md:inset-x-0 max-md:bottom-0 max-md:z-20 max-md:border-t max-md:border-border max-md:bg-card max-md:px-4 max-md:py-3">
            <Button size="lg" class="max-md:w-full" disabled={publish.isPending} onclick={publishEverywhere}>
              <SendIcon aria-hidden="true" />Publish to {boardCount === 1 ? '1 board' : `${boardCount} boards`}
            </Button>
          </div>
        {/if}
        {#if !sidebar.isMobile}
          {#if detail.status !== 'Closed'}
            <Button href={`/jobs/${detail.id}/edit`} variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit details</Button>
          {/if}
          <Button href={`/jobs/${detail.id}/workflow`} variant="outline" size="lg"><WorkflowIcon aria-hidden="true" />Workflow</Button>
        {/if}
        <!-- Closing the job lives in More, confirmed in a dialog (A2); phones fold the other actions in too. -->
        {#if sidebar.isMobile || detail.status === 'Open'}
          <MoreMenu
            label="More actions"
            onclosed={() => {
              if (closeChosen) confirmClose = true;
              closeChosen = false;
            }}
          >
            {#if sidebar.isMobile}
              {#if detail.status !== 'Closed'}
                <DropdownMenu.Item onSelect={() => goto(`/jobs/${detail.id}/edit`)}><PencilIcon aria-hidden="true" />Edit details</DropdownMenu.Item>
              {/if}
              <DropdownMenu.Item onSelect={() => goto(`/jobs/${detail.id}/workflow`)}><WorkflowIcon aria-hidden="true" />Workflow</DropdownMenu.Item>
            {/if}
            {#if detail.status === 'Open'}
              {#if sidebar.isMobile}<DropdownMenu.Separator />{/if}
              <DropdownMenu.Item variant="destructive" onSelect={() => (closeChosen = true)}>Close &amp; remove everywhere</DropdownMenu.Item>
            {/if}
          </MoreMenu>
        {/if}
      {/snippet}
      {#snippet facts()}
        <Fact label="Location">{detail.location}</Fact>
        <Fact label="Type">{employmentLabel(detail.employmentType)}</Fact>
        {#if detail.salary}<Fact label="Salary">{formatSalary(detail.salary)}</Fact>{/if}
        {#if detail.status === 'Open' && detail.publishedAt}
          <Fact label="Opened"><RelativeTime at={detail.publishedAt} /></Fact>
        {:else if detail.status === 'Closed' && detail.closedAt}
          <Fact label="Closed"><RelativeTime at={detail.closedAt} /></Fact>
        {:else}
          <Fact label="Created"><RelativeTime at={detail.createdAt} /></Fact>
        {/if}
        {#if detail.template}
          <Fact label="Template">
            <span class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
              {detail.template.templateName} v{detail.template.version}
              {#if detail.template.updateAvailable}<Badge variant="muted">Update available</Badge>{/if}
            </span>
          </Fact>
        {/if}
      {/snippet}
    </HeaderBand>

    <!-- The detail template (section 7): a main column and a side column, in a 1280px container (S6). -->
    <div class={['mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6', detail.status === 'Draft' && 'max-md:pb-24']}>
      {#if applications.isSuccess && all.length > 0}
        <IntegrityOverview applications={all} bind:filter={() => integrityFilter, (next) => pipeline?.setIntegrityFilter(next)} />
      {/if}

      <div class="flex flex-wrap items-start gap-5">
        <div class="flex min-w-0 flex-[999_1_520px] flex-col gap-5">
          <JobPipeline
            bind:this={pipeline}
            bind:integrityFilter
            bind:view
            bind:activeId
            jobTitle={detail.title}
            {stages}
            applications={all}
            pending={applications.isPending}
            error={applications.isError ? applications.error : null}
            onretry={() => applications.refetch()}
            {moves}
          />
        </div>

        <div class="flex min-w-0 max-w-95 flex-[1_1_320px] flex-col gap-5">
          {#if view === 'table' && active}
            {@render candidatePanel(active)}
          {/if}
          {#if boards.data}
            <DistributionPanel job={detail} boards={boards.data.data} notice={page.state.saved ?? ''} {publishFailure} />
          {:else if boards.isError}
            <Panel title="Distribution">
              <QueryError message={describeError(boards.error, 'Could not load the job boards')} onretry={() => boards.refetch()} inset />
            </Panel>
          {:else}
            <!-- Rows of the panel's shape: a board's name, then its state (E1). -->
            <Panel title="Distribution" flush>
              <div aria-hidden="true" class="flex flex-col">
                {#each [0, 1] as row (row)}
                  <div class="flex flex-col gap-2 border-b border-border-subtle px-4.5 py-3 last:border-0 max-md:px-4">
                    <Skeleton class="h-5 w-32" />
                    <Skeleton class="h-5 w-20" />
                  </div>
                {/each}
              </div>
            </Panel>
          {/if}
        </div>
      </div>
    </div>

    <CloseJobDialog jobId={detail.id} bind:open={confirmClose} />
  {/if}
</main>

{#snippet candidatePanel(application: ApplicationSummary)}
  <Panel title="Selected candidate">
    <h3 class="mb-3 font-display text-panel-title break-words">{application.candidateName}</h3>
    <SummaryList>
      <SummaryItem label="Stage">{application.stage.name}</SummaryItem>
      <SummaryItem label="Entered stage"><RelativeTime at={application.stageEnteredAt} /></SummaryItem>
      <SummaryItem label="Applied"><RelativeTime at={application.submittedAt} /></SummaryItem>
      <SummaryItem label="Source">{sourceLabel(application.source)}</SummaryItem>
      {#each [{ kind: 'authenticity', label: 'Authenticity' }, { kind: 'aiAssistance', label: 'AI assistance' }] as const as row (row.kind)}
        {@const chip = chipOf(application.integrity, row.kind)}
        <SummaryItem label={row.label}>
          <IntegrityChip kind={row.kind} level={chip?.level ?? null} reasons={chip?.reasons ?? 0} dismissed={chip?.dismissed ?? false} />
        </SummaryItem>
      {/each}
    </SummaryList>
    {#snippet footer()}
      <div class="flex flex-wrap gap-2">
        <Button href={`/applications/${application.id}`} variant="outline">Open application</Button>
        <MoveToStage
          candidateName={application.candidateName}
          currentStageId={application.stage.id}
          {stages}
          disabled={moves.pending}
          onmove={(stage) => moves.move(application, stage)}
        />
      </div>
    {/snippet}
  </Panel>
{/snippet}
