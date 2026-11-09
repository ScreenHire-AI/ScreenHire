<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import CalendarPlusIcon from '~icons/lucide/calendar-plus';
  import MailCheckIcon from '~icons/lucide/mail-check';
  import MailIcon from '~icons/lucide/mail';
  import SendIcon from '~icons/lucide/send';
  import {
    ApiError,
    createGetApplication,
    createGetApplicationVerification,
    createGetApplicationTimeline,
    createGetCandidate,
    createGetJob,
    createListApplicationScorecards,
    createListCandidateEmails,
    createClearCandidateEmailIssue,
    createListApplicationInterviews,
    createListEmailTemplates,
    createGetJobHiringTeam,
    createListJobs,
    createListRoles,
    createListScorecardTemplates,
    createListTeamMembers,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { tick } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ActivityPanel from '#lib/components/candidate/activity-panel.svelte';
  import AskScorecardSheet from '#lib/components/ask-scorecard-sheet.svelte';
  import ApplicationTab from '#lib/components/candidate/application-tab.svelte';
  import FilesTab from '#lib/components/candidate/files-tab.svelte';
  import HeaderMeta from '#lib/components/candidate/header-meta.svelte';
  import HeaderTags from '#lib/components/candidate/header-tags.svelte';
  import MessagesTab from '#lib/components/candidate/messages-tab.svelte';
  import OverviewTab from '#lib/components/candidate/overview-tab.svelte';
  import ScorecardsTab from '#lib/components/candidate/scorecards-tab.svelte';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import InviteToApplySheet from '#lib/components/invite-to-apply-sheet.svelte';
  import VerificationPanel from '#lib/components/verification-panel.svelte';
  import SaveCandidateSheet from '#lib/components/save-candidate-sheet.svelte';
  import ManualAutomations from '#lib/components/manual-automations.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MoveNoteSheet from '#lib/components/move-note-sheet.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import Notice from '#lib/components/notice.svelte';
  import ScheduleInterviewSheet from '#lib/components/schedule-interview-sheet.svelte';
  import Panel from '#lib/components/panel.svelte';
  import Rating from '#lib/components/rating.svelte';
  import StageRail from '#lib/components/stage-rail.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';
  import ScorecardSheet from '#lib/components/scorecard-sheet.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import VerificationChip from '#lib/components/verification-chip.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { visibleEntries, type ActivityNames } from '#lib/activity.js';
  import { reviewActivities } from '#lib/verification-activity.js';
  import { clock } from '#lib/clock.svelte.js';
  import { emailIssueNotice } from '#lib/email-issue.js';
  import { shortDate } from '#lib/dates.js';
  import { authenticityLevels, dismissReasonLabels, reviewerName, verificationStateOf } from '#lib/verification.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { endedStatus, trackerFor } from '#lib/pipeline-tracker.js';
  import { canKeepInMind } from '#lib/talent-pool.js';
  import { membersWhoSee } from '#lib/hiring-team.js';
  import { isTerminal } from '#lib/workflow.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';
  import { choiceFor, defaultChoice, scorecardChoices } from '#lib/scorecard-choices.js';
  import { membersWith } from '#lib/team.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  // A candidate (design system sections 7 and 21): the header band holds who they are and where they stand (the name and the
  // verification state, one meta line, the rating and tags, then the stage rail) above the same seven tabs in the same order
  // for everyone, so the facts sit where the eye starts on every tab. The tab is in the address (?tab=), so a link can open
  // one: the Overview's "All in Activity", a mention email's link to the note. Each tab's body is its own component.
  const tabs = ['overview', 'application', 'files', 'messages', 'scorecards', 'verification', 'activity'] as const;
  type Tab = (typeof tabs)[number];
  // ?tab=integrity is the tab's address from before it was called Verification, kept so an old link still opens it.
  const requested = $derived(page.url.searchParams.get('tab') === 'integrity' ? 'verification' : page.url.searchParams.get('tab'));
  const tab = $derived<Tab>(tabs.find((value) => value === requested) ?? 'overview');

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
  // The Verification tab's marker; the tab shares the query.
  const verification = createGetApplicationVerification(() => params.applicationId);
  // Names the people in Activity, notes and mentions.
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const queryClient = useQueryClient();
  const detail = $derived(application.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  // Every member reads; each control that changes hiring data shows only for a role with the permission its route
  // needs (A3, ADR-0014), so an Interviewer can add a note and fill in a scorecard without being offered Move.
  const permissions = $derived(page.data.user?.organization?.permissions ?? []);
  const mayMove = $derived(can(page.data.user, 'MoveCandidates'));
  const mayNote = $derived(can(page.data.user, 'AddNotesAndTags'));
  const mayScore = $derived(can(page.data.user, 'FillInScorecards'));
  const mayAsk = $derived(can(page.data.user, 'CreateTasks'));
  let moveNoteOpen = $state(false);
  // Save candidate (docs/architecture/talent-pool.md) sits in Move to stage beside Rejected, for a candidate who allowed it.
  let keepOpen = $state(false);
  let keptMessage = $state('');
  let scorecardOpen = $state(false);
  let scorecardInitial = $state<string | undefined>();
  let askOpen = $state(false);
  let scorecardAnnouncement = $state('');
  // The job's workflow names the stages and labels the questions.
  const job = createGetJob(
    () => detail?.jobId ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );
  const moves = createMoves({ allowed: () => mayMove });
  // A mention names only someone who can see this candidate (ADR-0015): everyone who sees all jobs, and the job's team.
  const hiringTeam = createGetJobHiringTeam(
    () => detail?.jobId ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );
  const mentionable = $derived(members && hiringTeam.data ? membersWhoSee(members, hiringTeam.data.data.map((person) => person.memberId)) : undefined);
  // Phones move the primary action to a bar at the bottom (design system section 8), on the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();
  // Email shows only where this deployment can send it and the person may (A3).
  const emailTemplates = createListEmailTemplates();
  // An address the email provider flagged is sent nothing more, so the controls are hidden and the notice says why.
  const emailIssue = $derived(detail?.candidate.emailIssue ?? 'None');
  const canEmail = $derived(Boolean(emailTemplates.data?.data.sendingEnabled) && can(page.data.user, 'EmailCandidates') && emailIssue === 'None');
  const clearEmailIssue = createClearCandidateEmailIssue();
  let clearFailure = $state('');
  async function addressWorks() {
    if (!detail) return;
    clearFailure = '';
    try {
      await clearEmailIssue.mutateAsync({ candidateId: detail.candidate.id });
      invalidatePath(queryClient, '/v1/applications');
      invalidatePath(queryClient, '/v1/candidates');
      emailed = 'ScreenHire will email this address again.';
    } catch (error) {
      clearFailure = describeError(error, 'Could not clear the flag');
    }
  }
  let emailOpen = $state(false);
  // The sheet opens once the More menu has closed, so focus can go back to the menu's button.
  let emailChosen = false;
  let emailed = $state('');
  // The Overview's Interview panel reads the same query; InterviewChanged refetches it live.
  const interviews = createListApplicationInterviews(() => params.applicationId);
  // Schedule interview (docs/architecture/interview-scheduling.md) goes where Email goes: it emails the candidate a link, so
  // it takes the same permission and a deployment that can send. It is hidden once the application has ended (rejected,
  // hired, withdrawn or kept in mind) and while an interview is open or booked, because the API refuses both and a button
  // that can only fail is not offered (A3). It waits for the interviews to load, so it never appears and then goes.
  const canSchedule = $derived(
    canEmail &&
      Boolean(detail) &&
      !isTerminal(detail!.stage.kind) &&
      !detail!.keptInMindAt &&
      interviews.isSuccess &&
      !interviews.data.data.some((interview) => interview.status === 'Open' || interview.status === 'Booked'),
  );
  let scheduleOpen = $state(false);
  let scheduleChosen = false;
  let scheduled = $state('');

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
  // Saved candidates' entries name other jobs: where the candidate was invited, added or applied.
  const jobs = createListJobs();
  const jobName = (id: string | null | undefined) => jobs.data?.data.find((candidate) => candidate.id === id)?.title ?? 'another job';
  const names: ActivityNames = { stageName, followUpName, jobName };
  const entries = $derived(timeline.data?.data);
  // What people and API keys did to the candidate's verification: its entries are Activity lines too (design system section 16).
  const verificationLog = $derived(verification.data?.data.reviewLog);

  // The stage rail under the name: the job's stages, where the candidate has been and is, or how they left.
  const tracker = $derived(detail && stages.length > 0 ? trackerFor(stages, detail, entries, clock.now) : undefined);
  // Once they left the job a status says how, in place of the verification state beside the name.
  const ended = $derived(detail ? endedStatus(detail) : undefined);

  // A saved candidate has left the job, so the header offers to reach them or invite them to another job in place of Email
  // and Move to stage (docs/architecture/talent-pool.md). Invite is the page's primary action then.
  const saved = $derived(Boolean(detail?.keptInMindAt));
  let inviteOpen = $state(false);
  // The jobs the candidate already applied to are skipped by the invitation; the profile lists them all.
  const profile = createGetCandidate(
    () => detail?.candidate.id ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );
  const invitee = $derived(
    detail
      ? [
          {
            candidateId: detail.candidate.id,
            candidateName: detail.candidate.fullName,
            appliedJobIds: [detail.jobId, ...(profile.data?.data.applications.map((application) => application.jobId) ?? [])],
          },
        ]
      : [],
  );
  // The bottom bar on a phone holds the primary action: Invite for a saved candidate, otherwise Move to stage.
  const primaryOnPhone = $derived(saved ? canEmail : stages.length > 0 && mayMove);

  // Fill in offers every scorecard the reader can still give: the stages the application reached that have one (the
  // current stage first), then the library's, given at the current stage. A link names one (?scorecard=, My tasks' row
  // for a requested scorecard), and the sheet opens on it.
  const library = createListScorecardTemplates();
  const linkedScorecard = $derived(page.url.searchParams.get('scorecard'));
  // A review's link (?fill=stage, #lib/tasks.svelte.ts) opens the sheet on the scorecard Fill in starts on.
  const linkedFill = $derived(page.url.searchParams.get('fill') === 'stage');
  const reached = $derived(
    new Set([
      ...(detail ? [detail.stage.id] : []),
      ...(entries ?? []).filter((entry) => entry.type === 'Submitted' || entry.type === 'StageChanged').flatMap((entry) => entry.toStageId ?? []),
    ]),
  );
  const choices = $derived(
    scorecardChoices({
      stages,
      reached,
      currentStageId: detail?.stage.id,
      library: library.data?.data ?? [],
      mine: (scorecards.data?.data ?? []).filter((card) => card.reviewerUserId === currentUserId),
      alsoOffer: linkedScorecard,
    }),
  );
  // Ask for one names the teammates who can see this job (ADR-0015) and whose role fills in scorecards, never yourself.
  const roles = createListRoles(() => ({ query: { enabled: mayAsk } }));
  const teammates = $derived(
    mentionable && roles.data
      ? membersWith(mentionable, roles.data.data.roles, 'FillInScorecards').filter((member) => member.userId !== currentUserId)
      : undefined,
  );

  // The scorecard the sheet starts on: the one asked for, else the default for the stage the candidate is in, which can
  // arrive after the sheet opens (the sheet takes it then).
  function fillIn(value?: string) {
    scorecardInitial = value;
    scorecardOpen = true;
  }
  const sheetInitial = $derived(scorecardInitial ?? defaultChoice(choices, detail?.stage.id)?.value);

  // A link to a scorecard opens the sheet on it once everything it needs has loaded, once per visit. A scorecard the
  // reader gave already offers nothing, so the tab simply shows.
  let openedFromLink = false;
  $effect(() => {
    const key = linkedScorecard;
    const fill = linkedFill;
    if ((!key && !fill) || openedFromLink || !mayScore || !detail || !job.isSuccess || !scorecards.isSuccess || !timeline.isSuccess || !library.isSuccess) return;
    openedFromLink = true;
    const choice = key ? choiceFor(choices, key) : defaultChoice(choices, detail.stage.id);
    if (choice) fillIn(choice.value);
  });

  // The verification state beside the name: one word, with the level mark when it needs a look (design system section 3.2). Its
  // tooltip is the level's name and nothing more; a dismissed result says who set it aside and why.
  const authenticity = $derived(verification.data?.data.authenticity);
  const verificationState = $derived(verification.data ? verificationStateOf(authenticity?.level, Boolean(authenticity?.dismissal)) : undefined);
  const stateTooltip = $derived.by(() => {
    if (!verificationState) return undefined;
    if (verificationState.kind === 'review' || verificationState.kind === 'verify') return verificationState.label;
    const dismissal = authenticity?.dismissal;
    if (verificationState.kind !== 'dismissed' || !dismissal || !verificationState.assessedAs) return undefined;
    const who = reviewerName(dismissal.actorUserId, dismissal.actorName, currentUserId);
    return `${authenticityLevels[verificationState.assessedAs].label}, dismissed by ${who} on ${shortDate(dismissal.dismissedAt, clock.now)}: ${dismissReasonLabels[dismissal.reason]}.`;
  });
  // A count on each tab where one fits (E2). When the verification state needs a look, Verification's count is the signals
  // behind it, in the same grey as every tab, and the state is the candidate's status beside the name (W7).
  const attention = $derived.by(() => {
    if (!authenticity || !verificationState || (verificationState.kind !== 'review' && verificationState.kind !== 'verify')) return undefined;
    return { signals: authenticity.signals.filter((signal) => signal.countsTowardLevel).length };
  });
  const recordTabs = $derived<RecordTab[]>([
    { value: 'overview', label: 'Overview' },
    { value: 'application', label: 'Application' },
    { value: 'files', label: 'Files', count: detail?.files.length },
    { value: 'messages', label: 'Messages', count: candidateEmails.data?.data.length },
    { value: 'scorecards', label: 'Scorecards', count: scorecards.data?.data.length },
    { value: 'verification', label: 'Verification', count: attention?.signals },
    { value: 'activity', label: 'Activity', count: entries ? visibleEntries(entries).length + reviewActivities(verificationLog ?? [], currentUserId).length : undefined },
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
  {#if detail && stages.length > 0 && mayMove}
    <MoveToStage
      candidateName={detail.candidate.fullName}
      currentStageId={detail.stage.id}
      {stages}
      variant="default"
      size="lg"
      disabled={moves.pending}
      onmove={(stage) => moves.move({ id: detail.id, jobId: detail.jobId, candidateName: detail.candidate.fullName }, stage)}
      onnote={() => (moveNoteOpen = true)}
      onkeep={canKeepInMind(detail) ? () => (keepOpen = true) : undefined}
    />
  {/if}
{/snippet}

{#snippet invite()}
  {#if canEmail}
    <Button variant="default" size="lg" onclick={() => (inviteOpen = true)}><SendIcon aria-hidden="true" />Invite to another job</Button>
  {/if}
{/snippet}

{#snippet bandActions()}
  <FailureText message={moves.failure ? moves.announcement : ''} />
  {#if canEmail && sidebar.isMobile}
    <!-- A phone keeps the bottom bar for the primary action; the rest fold into More (design system section 8). -->
    <MoreMenu
      label="More actions"
      onclosed={() => {
        if (emailChosen) ((emailChosen = false), (emailOpen = true));
        else if (scheduleChosen) ((scheduleChosen = false), (scheduleOpen = true));
      }}
    >
      <DropdownMenu.Item onSelect={() => (emailChosen = true)}><MailIcon aria-hidden="true" />{saved ? 'Message' : 'Email'}</DropdownMenu.Item>
      {#if canSchedule}
        <DropdownMenu.Item onSelect={() => (scheduleChosen = true)}><CalendarPlusIcon aria-hidden="true" />Schedule interview</DropdownMenu.Item>
      {/if}
    </MoreMenu>
  {:else if canEmail}
    <Button variant="outline" size="lg" onclick={() => (emailOpen = true)}><MailIcon aria-hidden="true" />{saved ? 'Message' : 'Email'}</Button>
    {#if canSchedule}
      <Button variant="outline" size="lg" onclick={() => (scheduleOpen = true)}><CalendarPlusIcon aria-hidden="true" />Schedule interview</Button>
    {/if}
  {/if}
  {#if !sidebar.isMobile}{#if saved}{@render invite()}{:else}{@render moveToStage()}{/if}{/if}
{/snippet}

{#snippet headerMeta()}
  {#if detail}<HeaderMeta {detail} emails={candidateEmails.data?.data} />{/if}
{/snippet}

{#snippet headerTabs()}
  <RecordTabs label="Candidate sections" tabs={recordTabs} />
{/snippet}

<main class={['flex flex-col', sidebar.isMobile && detail && primaryOnPhone && 'pb-28']}>
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
        <Panel label="Application"><QueryError message={describeError(application.error, 'Could not load this application')} onretry={() => application.refetch()} inset /></Panel>
      </div>
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="@container border-b border-border bg-card">
      <div class="flex w-full flex-col gap-4 px-4 pt-5 md:px-6">
        <Skeleton class="h-8 w-64 max-w-full" />
        <Skeleton class="h-11 w-96 max-w-full" />
      </div>
    </div>
    <div aria-hidden="true" class="@container grid w-full items-start gap-5 px-4 py-5 md:px-6 @5xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
      <Skeleton class="h-96 rounded-panel" />
      <Skeleton class="h-48 rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the application…</p>
  {:else}
    <Tabs.Root bind:value={() => tab, selectTab}>
      <HeaderBand title={detail.candidate.fullName} actions={bandActions} tabs={headerTabs} meta={headerMeta}>
        {#snippet status()}
          <!-- The first line is the name and one status (S2, T2), on every tab. Once they left the job it says how (Saved,
               Rejected, Withdrawn, Hired); until then it is the verification state in one word (Clear, Review, Verify, Pending
               or Dismissed). A badge, never a link (A5); Needs you links to the reasons. The overdue and email-issue
               badges sit where they apply: beside "Here 3 days" and the address. -->
          {#if ended}<StatusBadge {...ended} />{:else if verificationState}<VerificationChip state={verificationState} size="lg" tooltip={stateTooltip} />{/if}
        {/snippet}
        {#snippet summary()}
          {#if detail.rating?.state === 'Rated' || detail.tags.length > 0 || mayNote}
            <div class="flex min-w-0 flex-wrap items-center gap-x-5 gap-y-2">
              <!-- From people's scorecards only (ADR-0013); a link to them (A5). -->
              <Rating rating={detail.rating} variant="link" href="?tab=scorecards" />
              <HeaderTags applicationId={detail.id} tags={detail.tags} editable={mayNote} />
            </div>
          {/if}
          {#if tracker}<StageRail {tracker} overdueAt={detail.slaDueAt} />{/if}
        {/snippet}
      </HeaderBand>

      <div class="@container w-full px-4 py-5 md:px-6 md:py-6">
        <!-- A move that worked shows in the stage rail and in Activity, so only screen readers hear it. A failure
             shows in the header band, beside Move to stage. -->
        <p role="status" class="sr-only">{moves.failure ? '' : moves.announcement}</p>
        <p role="status" class="sr-only">{emailed}</p>
        <p role="status" class="sr-only">{scheduled}</p>
        <p role="status" class="sr-only">{keptMessage}</p>

        <!-- The address was flagged by the email provider (E2): what it means for the team, and the way back. The badge on
             the meta line's email says which, so the sentence does not repeat it (W7). -->
        {#if emailIssue !== 'None'}
          {@const sentence = emailIssueNotice(detail.candidate.emailIssue, detail.candidate.emailIssueReason)}
          <div class="mb-5 flex flex-col items-start gap-3">
            {#if sentence}<Notice>{sentence}</Notice>{/if}
            {#if can(page.data.user, 'EmailCandidates')}
              <Button variant="outline" disabled={clearEmailIssue.isPending} onclick={addressWorks}><MailCheckIcon aria-hidden="true" />Allow email again</Button>
            {/if}
            <FailureText message={clearFailure} />
          </div>
        {/if}

        <!-- Only the selected tab's body is rendered: each loads what it shows, and a note's anchor is on the page once. -->
        <div class="flex min-w-0 flex-col gap-5">
          <Tabs.Content value="overview" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'overview'}
              <OverviewTab
                {detail}
                {members}
                {currentUserId}
                {permissions}
                scorecards={scorecards.data?.data}
                withheld={detail.rating?.state === 'AfterYourScorecard'}
                emails={candidateEmails.data?.data}
                onfill={mayScore ? () => fillIn() : undefined}
                oninterviewchanged={(message) => (scheduled = message)}
              />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="application" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'application'}
              <ApplicationTab {detail} {questions} />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="files" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'files'}
              <FilesTab {detail} />
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
                withheld={detail.rating?.state === 'AfterYourScorecard'}
                onfill={mayScore ? () => fillIn() : undefined}
                onask={mayAsk ? () => (askOpen = true) : undefined}
              />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="verification" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'verification'}
              <VerificationPanel applicationId={detail.id} />
            {/if}
          </Tabs.Content>
          <Tabs.Content value="activity" class="flex min-w-0 flex-col gap-5">
            {#if tab === 'activity'}
              <!-- Run by hand on this candidate, with what they did: an action's result lands in the timeline below. -->
              {#if permissions.includes('RunAutomations') && manualAutomations.length > 0}
                <ManualAutomations applicationId={detail.id} automations={manualAutomations} />
              {/if}
              <ActivityPanel
                applicationId={detail.id}
                {entries}
                {verificationLog}
                error={timeline.error}
                onretry={() => timeline.refetch()}
                {members}
                {mentionable}
                {currentUserId}
                {permissions}
                {names}
              />
            {/if}
          </Tabs.Content>
        </div>
      </div>
    </Tabs.Root>

    {#if sidebar.isMobile && primaryOnPhone}
      <ActionBar>{#if saved}{@render invite()}{:else}{@render moveToStage()}{/if}</ActionBar>
    {/if}

    {#if canEmail}
      <EmailComposerSheet
        bind:open={emailOpen}
        recipients={[{ id: detail.id, candidateName: detail.candidate.fullName }]}
        onsent={(message) => (emailed = message)}
      />
    {/if}
    {#if saved && canEmail}
      <InviteToApplySheet bind:open={inviteOpen} people={invitee} ondone={(message) => (emailed = message)} />
    {/if}

    {#if canSchedule}
      <ScheduleInterviewSheet
        bind:open={scheduleOpen}
        applicationId={detail.id}
        jobId={detail.jobId}
        candidateName={detail.candidate.fullName}
        {currentUserId}
        ondone={(message) => (scheduled = message)}
      />
    {/if}

    {#if mayScore}
      <ScorecardSheet
        bind:open={scorecardOpen}
        applicationId={detail.id}
        {choices}
        initial={sheetInitial}
        loadingChoices={library.isPending || scorecards.isPending || !workflow}
        libraryError={library.error ?? undefined}
        onretrylibrary={() => library.refetch()}
        ondone={scorecardSubmitted}
      />
    {/if}
    {#if mayAsk}
      <AskScorecardSheet
        bind:open={askOpen}
        applicationId={detail.id}
        {teammates}
        library={library.data?.data ?? []}
        currentStage={stages.find((stage) => stage.id === detail?.stage.id)}
        ondone={scorecardSubmitted}
      />
    {/if}
    {#if mayMove}
      <SaveCandidateSheet
        bind:open={keepOpen}
        applicationIds={[detail.id]}
        candidateName={detail.candidate.fullName}
        ondone={(message) => (keptMessage = message)}
      />
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
