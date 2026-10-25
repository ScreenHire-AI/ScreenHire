<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import MailIcon from '@lucide/svelte/icons/mail';
  import {
    ApiError,
    createGetApplication,
    createGetApplicationIntegrity,
    createGetApplicationTimeline,
    createGetJob,
    createListApplicationScorecards,
    createListCandidateEmails,
    createListEmailTemplates,
    createListTeamMembers,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { canChangeHiringData } from '#lib/auth.js';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ActivityPanel from '#lib/components/candidate/activity-panel.svelte';
  import ApplicationTab from '#lib/components/candidate/application-tab.svelte';
  import CandidateRail from '#lib/components/candidate/candidate-rail.svelte';
  import MessagesTab from '#lib/components/candidate/messages-tab.svelte';
  import OverviewTab from '#lib/components/candidate/overview-tab.svelte';
  import ScorecardsTab from '#lib/components/candidate/scorecards-tab.svelte';
  import DetailLayout from '#lib/components/detail-layout.svelte';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrityPanel from '#lib/components/integrity-panel.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MoveNoteSheet from '#lib/components/move-note-sheet.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';
  import ReviewLog from '#lib/components/review-log.svelte';
  import ScorecardSheet from '#lib/components/scorecard-sheet.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { visibleEntries, type ActivityNames } from '#lib/activity.js';
  import { authenticityLevels } from '#lib/integrity.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  // A candidate (design system sections 7 and 22): the header band with the same six tabs in the same order for
  // everyone, and the details rail, which never moves. The tab is in the address (?tab=), so a link can open one: the
  // Overview's "All in Activity", a mention email's link to the note. Each tab's body is its own component.
  const tabs = ['overview', 'application', 'messages', 'scorecards', 'integrity', 'activity'] as const;
  type Tab = (typeof tabs)[number];
  const tab = $derived<Tab>(tabs.find((value) => value === page.url.searchParams.get('tab')) ?? 'overview');

  function selectTab(value: string) {
    const url = new URL(page.url.href);
    if (value === 'overview') url.searchParams.delete('tab');
    else url.searchParams.set('tab', value);
    url.hash = '';
    void goto(url, { replace: true, reset: false });
  }

  // ApplicationMoved, the note and tag events and AutomationRunFinished refetch the application and its timeline live
  // (#lib/realtime.ts); ScorecardSubmitted the scorecards, for every reviewer on the page.
  const application = createGetApplication(() => params.applicationId);
  const timeline = createGetApplicationTimeline(() => params.applicationId);
  const scorecards = createListApplicationScorecards(() => params.applicationId);
  // The Integrity tab's marker; the tab shares the query.
  const integrity = createGetApplicationIntegrity(() => params.applicationId);
  // Names the people in Activity, notes and mentions.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const queryClient = useQueryClient();
  const detail = $derived(application.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  const role = $derived(page.data.user?.organization?.role);
  // Viewers read; the controls that change hiring data are left out for them (A3).
  const editable = $derived(canChangeHiringData(page.data.user));
  let moveNoteOpen = $state(false);
  let scorecardOpen = $state(false);
  let scorecardAnnouncement = $state('');
  // The job's workflow names the stages and labels the questions.
  const job = createGetJob(
    () => detail?.jobId ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );
  const moves = createMoves();
  // Phones move the primary action to a bar at the bottom (design system section 8), on the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();
  // Email shows only where this deployment can send it and the person may (A3).
  const emailTemplates = createListEmailTemplates();
  const canEmail = $derived(Boolean(emailTemplates.data?.data.sendingEnabled) && editable);
  let emailOpen = $state(false);
  // The sheet opens once the More menu has closed, so focus can go back to the menu's button.
  let emailChosen = false;
  let emailed = $state('');

  // Messages: every email to the candidate, about any of their applications. ApplicationEmailed refetches it live.
  const candidateEmails = createListCandidateEmails(
    () => detail?.candidate.id ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );

  const workflow = $derived(job.data?.data.workflow);
  const manualAutomations = $derived((workflow?.automations ?? []).filter((automation) => automation.enabled && automation.trigger.type === 'Manual'));
  const stages = $derived(workflow?.stages ?? []);
  const stageName = (id: string | null | undefined) => (id ? (stages.find((stage) => stage.id === id)?.name ?? id) : '');
  const questions = $derived(new Map((workflow?.questionnaire.questions ?? []).map((question) => [question.id, question])));
  const followUpName = (requestId: string | null | undefined) =>
    detail?.followUps.find((followUp) => followUp.id === requestId)?.name ?? 'a follow-up questionnaire';
  const names: ActivityNames = { stageName, followUpName };
  const entries = $derived(timeline.data?.data);

  // A reviewer scores a stage that has a scorecard, that the application has reached, and that they have not scored yet;
  // the current stage first.
  const reached = $derived(
    new Set([
      ...(detail ? [detail.stage.id] : []),
      ...(entries ?? []).filter((entry) => entry.type === 'Submitted' || entry.type === 'StageChanged').map((entry) => entry.toStageId),
    ]),
  );
  const scoredByMe = $derived(new Set((scorecards.data?.data ?? []).filter((card) => card.reviewerUserId === currentUserId).map((card) => card.stageId)));
  const scoreable = $derived(
    stages
      .filter((stage) => stage.scorecard && reached.has(stage.id) && !scoredByMe.has(stage.id))
      .toSorted((a, b) => Number(b.id === detail?.stage.id) - Number(a.id === detail?.stage.id)),
  );

  // Counts where a count fits, and a marker on Integrity when the level needs a look (E2).
  const authenticity = $derived(integrity.data?.data.authenticity);
  const marker = $derived.by((): RecordTab['marker'] => {
    if (!authenticity || authenticity.dismissal) return undefined;
    const level = authenticityLevels[authenticity.level];
    if (level.rank === 0) return undefined;
    return { label: level.label, tone: level.rank === 1 ? 'review' : 'verify' };
  });
  const recordTabs = $derived<RecordTab[]>([
    { value: 'overview', label: 'Overview' },
    { value: 'application', label: 'Application' },
    { value: 'messages', label: 'Messages', count: candidateEmails.data?.data.length },
    { value: 'scorecards', label: 'Scorecards', count: scorecards.data?.data.length },
    { value: 'integrity', label: 'Integrity', marker },
    { value: 'activity', label: 'Activity', count: entries ? visibleEntries(entries).length : undefined },
  ]);

  // A mention email links to its note (#note-{id} on Activity): once the timeline is in, the note scrolls into view.
  $effect(() => {
    const hash = page.url.hash;
    if (tab !== 'activity' || !hash.startsWith('#note-') || !entries) return;
    void tick().then(() => document.getElementById(hash.slice(1))?.scrollIntoView({ block: 'center' }));
  });

  async function scorecardSubmitted(message: string) {
    scorecardAnnouncement = message;
    await invalidatePath(queryClient, `${applicationsPath}/${params.applicationId}`);
  }

  const notFound = $derived(application.error instanceof ApiError && application.error.status === 404);
</script>

<svelte:head>
  <title>{detail?.candidate.fullName ?? 'Application'} · ScreenHire</title>
</svelte:head>

<TopBar
  crumbs={[
    { label: 'Candidates', href: '/candidates' },
    ...(detail ? [{ label: detail.jobTitle, href: `/jobs/${detail.jobId}` }] : []),
    { label: detail?.candidate.fullName ?? 'Application' },
  ]}
  back
/>

{#snippet moveToStage()}
  {#if detail && stages.length > 0}
    <MoveToStage
      candidateName={detail.candidate.fullName}
      currentStageId={detail.stage.id}
      {stages}
      variant="default"
      size="lg"
      disabled={moves.pending}
      onmove={(stage) => moves.move({ id: detail.id, jobId: detail.jobId, candidateName: detail.candidate.fullName }, stage)}
      onnote={editable ? () => (moveNoteOpen = true) : undefined}
    />
  {/if}
{/snippet}

{#snippet bandActions()}
  <FailureText message={moves.failure ? moves.announcement : ''} />
  {#if canEmail && sidebar.isMobile}
    <!-- A phone keeps the bottom bar for the primary action; the rest fold into More (design system section 8). -->
    <MoreMenu label="More actions" onclosed={() => emailChosen && ((emailChosen = false), (emailOpen = true))}>
      <DropdownMenu.Item onSelect={() => (emailChosen = true)}><MailIcon aria-hidden="true" />Email</DropdownMenu.Item>
    </MoreMenu>
  {:else if canEmail}
    <Button variant="outline" size="lg" onclick={() => (emailOpen = true)}><MailIcon aria-hidden="true" />Email</Button>
  {/if}
  {#if !sidebar.isMobile}{@render moveToStage()}{/if}
{/snippet}

{#snippet headerTabs()}
  <RecordTabs label="Candidate sections" tabs={recordTabs} />
{/snippet}

<main class={['flex flex-col', sidebar.isMobile && detail && stages.length > 0 && 'pb-28']}>
  {#if application.isError}
    {#if notFound}
      <HeaderBand title="Application not found" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It does not exist, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/candidates" variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to candidates</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      </div>
    {:else}
      <HeaderBand title="Application" />
      <div class="@container w-full px-4 py-5 md:px-6">
        <Panel title="Could not load the application"><QueryError message={describeError(application.error, 'Could not load this application')} onretry={() => application.refetch()} inset /></Panel>
      </div>
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="@container border-b border-border bg-card">
      <div class="flex w-full flex-col gap-4 px-4 pt-5 md:px-6">
        <Skeleton class="h-8 w-64 max-w-full" />
        <Skeleton class="h-11 w-96 max-w-full" />
      </div>
    </div>
    <div aria-hidden="true" class="@container flex w-full flex-col gap-5 px-4 py-5 md:px-6 lg:flex-row-reverse">
      <Skeleton class="h-48 rounded-panel lg:basis-rail" />
      <Skeleton class="h-96 flex-1 rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the application…</p>
  {:else}
    <Tabs.Root bind:value={() => tab, selectTab}>
      <HeaderBand title={detail.candidate.fullName} actions={bandActions} tabs={headerTabs}>
        {#snippet status()}<OverdueBadge dueAt={detail.slaDueAt} />{/snippet}
      </HeaderBand>

      <div class="@container w-full px-4 py-5 md:px-6 md:py-6">
        <!-- A move that worked shows in the rail's Stage and in Activity, so only screen readers hear it. A failure shows
             in the header band, beside Move to stage. -->
        <p role="status" class="sr-only">{moves.failure ? '' : moves.announcement}</p>
        <p role="status" class="sr-only">{emailed}</p>

        <!-- Only the selected tab's body is rendered: each loads what it shows, and a note's anchor is on the page once. -->
        <DetailLayout>
          {#snippet rail()}<CandidateRail {detail} {editable} />{/snippet}
          <Tabs.Content value="overview" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'overview'}
              <OverviewTab
                {detail}
                timeline={entries}
                timelineError={timeline.error}
                onretrytimeline={() => timeline.refetch()}
                {members}
                {currentUserId}
                {role}
                {editable}
                {names}
                {questions}
                {manualAutomations}
              />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="application" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'application'}
              <ApplicationTab {detail} {questions} />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="messages" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'messages'}
              <MessagesTab
                emails={candidateEmails.data?.data}
                error={candidateEmails.error}
                onretry={() => candidateEmails.refetch()}
                {members}
                {currentUserId}
                recipient={canEmail ? { id: detail.id, candidateName: detail.candidate.fullName } : undefined}
                onsent={(message) => (emailed = message)}
              />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="scorecards" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'scorecards'}
              <ScorecardsTab
                scorecards={scorecards.data?.data}
                error={scorecards.error}
                onretry={() => scorecards.refetch()}
                stageName={(id) => stageName(id)}
                {members}
                {currentUserId}
                announcement={scorecardAnnouncement}
                onfill={editable && scoreable.length > 0 ? () => (scorecardOpen = true) : undefined}
              />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="integrity" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'integrity'}
              <IntegrityPanel applicationId={detail.id} />
              <ReviewLog applicationId={detail.id} />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="activity" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'activity'}
              <ActivityPanel
                applicationId={detail.id}
                {entries}
                error={timeline.error}
                onretry={() => timeline.refetch()}
                {members}
                {currentUserId}
                {role}
                {editable}
                {names}
              />
            {/if}
          </Tabs.Content>
        </DetailLayout>
      </div>
    </Tabs.Root>

    {#if sidebar.isMobile && stages.length > 0}
      <ActionBar>{@render moveToStage()}</ActionBar>
    {/if}

    {#if canEmail}
      <EmailComposerSheet
        bind:open={emailOpen}
        recipients={[{ id: detail.id, candidateName: detail.candidate.fullName }]}
        onsent={(message) => (emailed = message)}
      />
    {/if}

    {#if editable}
      <ScorecardSheet bind:open={scorecardOpen} applicationId={detail.id} stages={scoreable} ondone={scorecardSubmitted} />
      <MoveNoteSheet
        bind:open={moveNoteOpen}
        candidateName={detail.candidate.fullName}
        currentStageId={detail.stage.id}
        {stages}
        failure={moves.failure}
        onmove={(stage, note) => moves.move({ id: detail.id, jobId: detail.jobId, candidateName: detail.candidate.fullName }, stage, note)}
      />
    {/if}
  {/if}
</main>
