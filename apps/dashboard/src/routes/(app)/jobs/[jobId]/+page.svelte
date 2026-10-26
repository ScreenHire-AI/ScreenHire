<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import SendIcon from '@lucide/svelte/icons/send';
  import UsersRoundIcon from '@lucide/svelte/icons/users-round';
  import WorkflowIcon from '@lucide/svelte/icons/workflow';
  import {
    ApiError,
    createGetJob,
    createGetJobHiringTeam,
    createListBoards,
    createListJobApplications,
    createListTeamMembers,
    createPublishJob,
    getGetJobHistoryQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import CloseJobDialog from '#lib/components/close-job-dialog.svelte';
  import DistributionPanel from '#lib/components/distribution-panel.svelte';
  import Fact from '#lib/components/fact.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import HiringTeamSheet from '#lib/components/hiring-team-sheet.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { boardRows, postingCounts, postingProblems } from '#lib/distribution.js';
  import { employmentLabel, formatSalary } from '#lib/format.js';
  import { createHistoryPages } from '#lib/history-pages.svelte.js';
  import { jobStatus } from '#lib/job-status.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { can, seesAllJobs } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  // ApplicationSubmitted, ApplicationMoved, IntegrityAssessed and JobPostingsChanged refetch them live (#lib/realtime.ts).
  const job = createGetJob(() => params.jobId);
  const applications = createListJobApplications(() => params.jobId);
  const boards = createListBoards();
  // Every move control on the page (table, board, selection) shows only for a role with Move candidates (ADR-0014).
  const moves = createMoves({ allowed: () => can(page.data.user, 'MoveCandidates') });
  // Phones fold the secondary actions into More (design system section 8), with the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();
  const publish = createPublishJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const detail = $derived(job.data?.data);
  // Last updated and History (T6): every change to the posting, its status and its workflow.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  let historyOpen = $state(false);
  // Hiring team (ADR-0015): who works on the job, and who on Only jobs they're on sees it.
  const hiringTeam = createGetJobHiringTeam(() => params.jobId);
  const teamCount = $derived(hiringTeam.data?.data.length);
  let teamOpen = $state(false);
  let teamChosen = false;
  const currentMemberId = $derived(members?.find((member) => member.userId === currentUserId)?.id);
  const history = createHistoryPages(
    (before) => getGetJobHistoryQueryOptions(params.jobId, { before }),
    () => historyOpen,
  );
  const stages = $derived(detail?.workflow.stages ?? []);
  const all = $derived(applications.data?.data ?? []);
  const rows = $derived(detail ? boardRows(boards.data?.data ?? [], detail.postings) : []);
  const boardCount = $derived(rows.length);
  const publishable = $derived(detail?.status === 'Draft' && boardCount > 0);
  // A posting that needs fixes is a notice where people will see it, naming the board, with a link to the fix (E2). Only
  // an open job is on the boards.
  const problems = $derived(detail?.status === 'Open' && boards.data ? postingProblems(rows) : []);

  // The job's peer views (section 21): the work area first, then Distribution, whose count says how many boards the job
  // is on (E2). The tab is in the address, so a link reaches it and a reload keeps it.
  const tab = $derived((page.shallow?.url ?? page.url).searchParams.get('tab') === 'distribution' ? 'distribution' : 'candidates');
  const sections = $derived.by((): RecordTab[] => {
    const counts = postingCounts(rows);
    return [
      { value: 'candidates', label: 'Candidates', count: applications.isSuccess ? all.length : undefined },
      boards.data
        ? { value: 'distribution', label: 'Distribution', count: counts.posted, of: counts.total, countLabel: `posted on ${counts.posted} of ${counts.total} boards` }
        : { value: 'distribution', label: 'Distribution' },
    ];
  });

  // A tab is a view of the same page, so switching only changes the address (shallow), keeping the page's state.
  function showTab(next: string) {
    const url = new URL(page.url.href);
    if (next === 'distribution') url.searchParams.set('tab', next);
    else url.searchParams.delete('tab');
    void goto(url, { shallow: true, replace: true, state: page.state });
  }

  let view = $state<'table' | 'board'>('table');
  // The table or the board fills the window below the header and scrolls inside itself, so the page never scrolls
  // (S6, section 21).
  const boardFills = $derived(tab === 'candidates' && !sidebar.isMobile);
  let confirmClose = $state(false);
  // "Close & remove everywhere" was chosen; its confirmation opens once the menu has closed.
  let closeChosen = false;
  let publishFailure = $state('');

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

<main class={['flex flex-col', sidebar.isMobile && publishable && 'pb-28', boardFills && 'h-[calc(100svh-3.5rem)] min-h-160']}>
  <!-- A failed refetch keeps the job on screen; the error replaces it only when there is nothing to show. -->
  {#if job.isError && !detail}
    <HeaderBand title={notFound ? 'Job not found' : 'Job'} />
    <div class="w-full px-4 py-6 md:px-6">
      <Panel title={notFound ? 'Details' : 'Could not load the job'}>
        {#if notFound}
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It does not exist, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} inset />
        {/if}
      </Panel>
    </div>
  {:else if !detail}
    <!-- Skeletons of the header band and the work area (E1). -->
    <div aria-hidden="true" class="border-b border-border bg-card">
      <div class="flex w-full flex-col gap-4 px-4 py-5 md:px-6">
        <Skeleton class="h-9 w-full max-w-80" />
        <Skeleton class="h-10 w-full max-w-120" />
      </div>
    </div>
    <div aria-hidden="true" class="flex w-full px-4 py-6 md:px-6">
      <Skeleton class="h-80 w-full rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the job…</p>
  {:else}
    <Tabs.Root value={tab} onValueChange={showTab} class={['gap-0', boardFills && 'min-h-0 flex-1']}>
      <HeaderBand title={detail.title}>
        {#snippet status()}<StatusBadge {...jobStatus[detail.status]} />{/snippet}
        {#snippet actions()}
          <!-- What saving the details or publishing did, beside the actions that did it, as the workflow page says it. -->
          <p role="status" class="text-muted-foreground empty:hidden">{page.state.saved ?? ''}</p>
          <FailureText message={publishFailure} />
          <!-- The one primary action; on a phone it sits in the action bar at the bottom of the screen (section 8). -->
          {#if publishable && !sidebar.isMobile}{@render publishButton()}{/if}
          {#if !sidebar.isMobile}
            {#if detail.status !== 'Closed'}
              <Button href={`/jobs/${detail.id}/edit`} variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit details</Button>
            {/if}
            <Button href={`/jobs/${detail.id}/workflow`} variant="outline" size="lg"><WorkflowIcon aria-hidden="true" />Edit workflow</Button>
            <Button variant="outline" size="lg" aria-haspopup="dialog" onclick={() => (teamOpen = true)}>
              <UsersRoundIcon aria-hidden="true" />Hiring team{#if teamCount !== undefined}<Badge variant="count">{teamCount}</Badge>{/if}
            </Button>
          {/if}
          <!-- Closing the job lives in More, confirmed in a dialog (A2); phones fold the other actions in too. -->
          {#if sidebar.isMobile || detail.status === 'Open'}
            <MoreMenu
              label="More actions"
              onclosed={() => {
                if (closeChosen) confirmClose = true;
                if (teamChosen) teamOpen = true;
                closeChosen = false;
                teamChosen = false;
              }}
            >
              {#if sidebar.isMobile}
                {#if detail.status !== 'Closed'}
                  <!-- Items that go to a page are links (A5). -->
                  <DropdownMenu.Item>
                    {#snippet child({ props })}<a {...props} href={`/jobs/${detail.id}/edit`}><PencilIcon aria-hidden="true" />Edit details</a>{/snippet}
                  </DropdownMenu.Item>
                {/if}
                <DropdownMenu.Item>
                  {#snippet child({ props })}<a {...props} href={`/jobs/${detail.id}/workflow`}><WorkflowIcon aria-hidden="true" />Edit workflow</a>{/snippet}
                </DropdownMenu.Item>
                <DropdownMenu.Item onSelect={() => (teamChosen = true)}><UsersRoundIcon aria-hidden="true" />Hiring team</DropdownMenu.Item>
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
              <!-- The fact's state in its words, never a pill in a header (section 11, Fact pill). -->
              {detail.template.templateName} v{detail.template.version}{#if detail.template.updateAvailable}, update available{/if}
            </Fact>
          {/if}
          <Fact label="Last updated">
            <LastUpdated value={detail.lastUpdated} {members} {currentUserId} onhistory={() => (historyOpen = true)} />
          </Fact>
        {/snippet}
        {#snippet tabs()}<RecordTabs label="Job sections" tabs={sections} />{/snippet}
      </HeaderBand>
      <HiringTeamSheet
        bind:open={teamOpen}
        kind="job"
        id={detail.id}
        name={detail.title}
        mayEdit={can(page.data.user, 'EditJobs')}
        {currentMemberId}
        seesAllJobs={seesAllJobs(page.data.user)}
      />
      <HistorySheet
        bind:open={historyOpen}
        name={detail.title}
        entries={history.entries}
        error={history.error}
        onretry={history.retry}
        hasMore={history.hasMore}
        loadingMore={history.loadingMore}
        onmore={history.more}
        {members}
        {currentUserId}
      />

      <!-- The work area first, filling the window (S6), with no side column beside the table or the board (S7). -->
      <div class={['flex w-full flex-col gap-4 px-4 py-6 md:px-6', boardFills && 'min-h-0 flex-1']}>
        <Tabs.Content value="candidates" class={['flex flex-col gap-4', boardFills && 'min-h-0']}>
          {#each problems as problem (problem.kind)}
            <Notice href="?tab=distribution" link="Fix on Distribution">{problem.text}</Notice>
          {/each}
          <JobPipeline
            bind:view
            jobTitle={detail.title}
            {stages}
            applications={all}
            pending={applications.isPending}
            error={applications.isError ? applications.error : null}
            onretry={() => applications.refetch()}
            {moves}
          />
        </Tabs.Content>
        <Tabs.Content value="distribution">
          {#if boards.data}
            <DistributionPanel job={detail} boards={boards.data.data} />
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
        </Tabs.Content>
      </div>
    </Tabs.Root>

    {#if sidebar.isMobile && publishable}
      <ActionBar>{@render publishButton()}</ActionBar>
    {/if}

    <CloseJobDialog jobId={detail.id} bind:open={confirmClose} />
  {/if}
</main>

{#snippet publishButton()}
  <Button size="lg" disabled={publish.isPending} onclick={publishEverywhere}>
    <SendIcon aria-hidden="true" />Publish to {boardCount === 1 ? '1 board' : `${boardCount} boards`}
  </Button>
{/snippet}
