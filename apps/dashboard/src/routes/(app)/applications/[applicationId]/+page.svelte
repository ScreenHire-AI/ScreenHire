<script lang="ts">
  import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
  import InboxIcon from '@lucide/svelte/icons/inbox';
  import MailIcon from '@lucide/svelte/icons/mail';
  import PhoneIcon from '@lucide/svelte/icons/phone';
  import ScanSearchIcon from '@lucide/svelte/icons/scan-search';
  import TagIcon from '@lucide/svelte/icons/tag';
  import type { ApplicationAnswer, ApplicationTimelineEntry, Question } from '@screenhire/api-client';
  import { ApiError, createGetApplication, createGetApplicationTimeline, createGetJob } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FileTile from '#lib/components/file-tile.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IntegrityPanel from '#lib/components/integrity-panel.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import ResumePreview from '#lib/components/resume-preview.svelte';
  import ReviewLog from '#lib/components/review-log.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { sourceName } from '#lib/format.js';
  import { createMoves } from '#lib/moves.svelte.js';
  import { describeError } from '#lib/query.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  // ApplicationMoved and AutomationRunFinished refetch the application and its timeline live (#lib/realtime.ts).
  const application = createGetApplication(() => params.applicationId);
  const timeline = createGetApplicationTimeline(() => params.applicationId);
  const detail = $derived(application.data?.data);
  // The job's workflow names the stages and labels the questions.
  const job = createGetJob(
    () => detail?.jobId ?? '',
    () => ({ query: { enabled: Boolean(detail) } }),
  );
  const moves = createMoves();
  // Phones move the primary action to a bar at the bottom (design system section 8), on the sidebar's own phone test.
  const sidebar = Sidebar.useSidebar();

  const workflow = $derived(job.data?.data.workflow);
  const stages = $derived(workflow?.stages ?? []);
  const stageName = (id: string | null) => (id ? (stages.find((stage) => stage.id === id)?.name ?? id) : '');
  const questions = $derived(new Map((workflow?.questionnaire.questions ?? []).map((question) => [question.id, question])));
  const fileById = $derived(new Map((detail?.files ?? []).map((file) => [file.uploadId, file])));
  const resume = $derived(detail?.files.find((file) => file.questionId === 'resume'));
  const answers = $derived(detail?.answers.filter((answer) => answer.questionId !== 'resume') ?? []);

  function answerText(answer: ApplicationAnswer, question: Question | undefined): string {
    const values = answer.values ?? (answer.value ? [answer.value] : []);
    const options = new Map((question?.options ?? []).map((option) => [option.id, option.label]));
    if (question?.type === 'YesNo') return values.map((value) => (value === 'yes' ? 'Yes' : value === 'no' ? 'No' : value)).join(', ');
    return values.map((value) => options.get(value) ?? value).join(', ');
  }

  function describeEvent(entry: ApplicationTimelineEntry): string {
    switch (entry.type) {
      case 'Submitted':
        return entry.actorUserId ? `Added to ${stageName(entry.toStageId)}` : `Applied on the careers page, into ${stageName(entry.toStageId)}`;
      case 'StageChanged':
        return `Moved from ${stageName(entry.fromStageId)} to ${stageName(entry.toStageId)}`;
      case 'TagAdded':
        return `Tagged "${entry.tag}"`;
      case 'IntegrityAssessed':
        // Levels and evidence are the integrity panel's to show; the activity records only that the checks ran.
        return 'Integrity checks ran';
    }
  }

  const eventIcons = { Submitted: InboxIcon, StageChanged: ArrowRightIcon, TagAdded: TagIcon, IntegrityAssessed: ScanSearchIcon };

  function actor(entry: ApplicationTimelineEntry): string | undefined {
    if (entry.automationRunId) return 'by an automation';
    if (entry.actorUserId === page.data.user?.id) return 'by you';
    if (entry.actorUserId) return 'by a teammate';
    return undefined;
  }

  const activity = $derived<Activity[]>(
    (timeline.data?.data ?? []).toReversed().map((entry) => ({
      id: entry.id,
      icon: eventIcons[entry.type],
      event: describeEvent(entry),
      actor: actor(entry),
      note: entry.note ?? undefined,
      at: entry.occurredAt,
    })),
  );

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
    />
  {/if}
{/snippet}

<main class={['flex flex-col', sidebar.isMobile && detail && stages.length > 0 && 'pb-28']}>
  {#if application.isError}
    {#if notFound}
      <HeaderBand title="Application not found" />
      <div class="@container mx-auto w-full max-w-page px-4 py-5 @3xl:px-6">
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Description>It does not exist, or it belongs to another organization.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button href="/candidates" variant="outline" size="lg">Back to candidates</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      </div>
    {:else}
      <HeaderBand title="Application" />
      <div class="@container mx-auto w-full max-w-page px-4 py-5 @3xl:px-6">
        <QueryError message={describeError(application.error, 'Could not load this application')} onretry={() => application.refetch()} />
      </div>
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="@container border-b border-border bg-card">
      <div class="mx-auto flex w-full max-w-page flex-col gap-4 px-4 py-5 @3xl:px-6">
        <Skeleton class="h-8 w-64 max-w-full" />
        <Skeleton class="h-4 w-80 max-w-full" />
        <Skeleton class="h-10 w-96 max-w-full" />
      </div>
    </div>
    <div aria-hidden="true" class="@container mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-5 @3xl:px-6 lg:flex-row-reverse">
      <Skeleton class="h-48 rounded-panel lg:basis-side" />
      <Skeleton class="h-96 flex-1 rounded-panel" />
    </div>
    <p class="sr-only" role="status">Loading the application…</p>
  {:else}
    <HeaderBand title={detail.candidate.fullName} actions={sidebar.isMobile ? undefined : moveToStage}>
      {#snippet details()}
        <TruncatedText text={detail.candidate.email} href={`mailto:${detail.candidate.email}`} icon={MailIcon} underlined class="text-muted-foreground" />
        {#if detail.candidate.phone}
          <TruncatedText text={detail.candidate.phone} href={`tel:${detail.candidate.phone}`} icon={PhoneIcon} underlined class="text-muted-foreground" />
        {/if}
      {/snippet}
      {#snippet facts()}
        <Fact label="Stage">{detail.stage.name}</Fact>
        <Fact label="Job"><TruncatedText text={detail.jobTitle} href={`/jobs/${detail.jobId}`} underlined /></Fact>
        <Fact label="Applied"><RelativeTime at={detail.submittedAt} /></Fact>
        <Fact label="Source">{sourceName(detail.source)}</Fact>
      {/snippet}
    </HeaderBand>

    <div class="@container mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-5 @3xl:px-6 @3xl:py-6">
      <!-- A move that worked shows in the Stage fact and in Activity, so only screen readers hear it; a failure shows. -->
      <p role="status" class={moves.failure ? 'rounded-panel border border-border bg-card px-4 py-3 text-status-verify' : 'sr-only'}>{moves.announcement}</p>

      <!-- Integrity comes first in the page's order, so one column (tablet and phone) starts with it. From lg it sits at the
           top of the side column, and the main column spans both of the side column's rows. -->
      <div class="flex flex-col gap-5 lg:grid lg:grid-cols-[minmax(0,1fr)_var(--container-side)] lg:grid-rows-[auto_1fr] lg:items-start">
        <div class="min-w-0 lg:col-start-2 lg:row-start-1">
          <IntegrityPanel applicationId={detail.id} />
        </div>

        <div class="flex min-w-0 flex-col gap-5 lg:col-start-1 lg:row-span-2 lg:row-start-1">
          <Panel title="Resume">
            {#if resume}
              <ResumePreview applicationId={detail.id} file={resume} />
            {:else}
              <p class="text-muted-foreground">No resume was uploaded.</p>
            {/if}
          </Panel>

          <Panel title="Answers">
            {#if answers.length === 0}
              <p class="text-muted-foreground">No questionnaire answers.</p>
            {:else}
              <SummaryList>
                {#each answers as answer (answer.questionId)}
                  {@const question = questions.get(answer.questionId)}
                  {@const file = question?.type === 'File' && answer.value ? fileById.get(answer.value) : undefined}
                  <SummaryItem label={question?.label ?? answer.questionId}>
                    {#if file}
                      <FileTile applicationId={detail.id} {file} />
                    {:else}
                      <span class="whitespace-pre-line">{answerText(answer, question)}</span>
                    {/if}
                  </SummaryItem>
                {/each}
              </SummaryList>
            {/if}
          </Panel>

          <Panel title="Declarations">
            <SummaryList>
              <SummaryItem label="AI use">
                {detail.aiUseDeclaration === 'Used' ? 'Used AI' : detail.aiUseDeclaration === 'DidNotUse' ? 'Did not use AI' : 'Not asked or not answered'}{detail.aiUseDetails
                  ? `: ${detail.aiUseDetails}`
                  : ''}
              </SummaryItem>
              <SummaryItem label="Adjustments">{detail.accommodationRequested ? 'Asked for adjustments to the process' : 'None requested'}</SummaryItem>
            </SummaryList>
          </Panel>
        </div>

        <div class="flex min-w-0 flex-col gap-5 lg:col-start-2 lg:row-start-2">
          <ReviewLog applicationId={detail.id} />

          <Panel title="Activity">
            {#if timeline.isError}
              <QueryError message={describeError(timeline.error, 'Could not load the activity')} onretry={() => timeline.refetch()} inset />
            {:else}
              <ActivityList label="Events" items={timeline.data && activity} />
            {/if}
          </Panel>
        </div>
      </div>
    </div>

    {#if sidebar.isMobile && stages.length > 0}
      <ActionBar>{@render moveToStage()}</ActionBar>
    {/if}
  {/if}
</main>
