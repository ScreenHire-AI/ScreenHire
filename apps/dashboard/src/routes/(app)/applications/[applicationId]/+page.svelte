<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationAnswer, type ApplicationTimelineEntry, type Question } from '@screenhire/api-client';
  import { ApiError, createGetApplication, createGetApplicationTimeline, createGetJob } from '@screenhire/api-client/svelte-query';
  import { page } from '$app/state';
  import IntegrityPanel from '#lib/components/integrity-panel.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import ResumePreview from '#lib/components/resume-preview.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { canDownload, withheldReason } from '#lib/files.js';
  import { initials, sourceLabel } from '#lib/format.js';
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

  const workflow = $derived(job.data?.data.workflow);
  const stages = $derived(workflow?.stages ?? []);
  const stageName = (id: string | null) => (id ? (stages.find((stage) => stage.id === id)?.name ?? id) : '');
  const questions = $derived(new Map((workflow?.questionnaire.questions ?? []).map((question) => [question.id, question])));
  const fileById = $derived(new Map((detail?.files ?? []).map((file) => [file.uploadId, file])));
  const resume = $derived(detail?.files.find((file) => file.questionId === 'resume'));

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
        // Levels and evidence are the integrity panel's to show; the timeline records only that the checks ran.
        return 'Integrity checks ran';
    }
  }

  function actor(entry: ApplicationTimelineEntry): string {
    if (entry.automationRunId) return 'by an automation';
    if (entry.actorUserId === page.data.user?.id) return 'by you';
    if (entry.actorUserId) return 'by a teammate';
    return '';
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
/>

<main class="flex flex-col gap-3.5 px-5 pt-4.5 pb-9">
  {#if application.isError}
    {#if notFound}
      <h1 class="font-display text-page-title">Application not found</h1>
      <p class="text-muted-foreground">It does not exist, or it belongs to another organization. <a href="/candidates">Back to candidates</a></p>
    {:else}
      <h1 class="font-display text-page-title">Application</h1>
      <QueryError message={describeError(application.error, 'Could not load this application')} onretry={() => application.refetch()} />
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="flex flex-col gap-2">
      <Skeleton class="h-10 w-72" />
      <Skeleton class="h-4 w-96" />
    </div>
    <p class="sr-only" role="status">Loading the application…</p>
  {:else}
    <section aria-label="Candidate" class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex items-center gap-3">
        <span aria-hidden="true" class="grid size-12 shrink-0 place-items-center rounded-full bg-secondary text-heading font-bold text-secondary-foreground"
          >{initials(detail.candidate.fullName)}</span
        >
        <div class="flex min-w-0 flex-col gap-1">
          <h1 class="font-display text-page-title">{detail.candidate.fullName}</h1>
          <ul aria-label="Application details" class="flex flex-wrap gap-x-3.5 gap-y-1 text-caption text-muted-foreground">
            <li><a href={`mailto:${detail.candidate.email}`}>{detail.candidate.email}</a></li>
            {#if detail.candidate.phone}<li>{detail.candidate.phone}</li>{/if}
            <li>{detail.stage.name} stage</li>
            <li>Applied <RelativeTime at={detail.submittedAt} /> via {sourceLabel(detail.source)}</li>
          </ul>
        </div>
      </div>
      {#if stages.length > 0}
        <MoveToStage
          candidateName={detail.candidate.fullName}
          currentStageId={detail.stage.id}
          {stages}
          disabled={moves.pending}
          onmove={(stage) => moves.move({ id: detail.id, jobId: detail.jobId, candidateName: detail.candidate.fullName }, stage)}
        />
      {/if}
    </section>
    <p role="status" class={['text-body empty:hidden', moves.failure ? 'text-status-verify' : 'text-muted-foreground']}>{moves.announcement}</p>

    <div class="flex flex-wrap items-start gap-3.5">
      <div class="flex min-w-0 flex-[999_1_520px] flex-col gap-3.5">
        <section aria-labelledby="resume-heading" class="rounded-xl border border-border bg-card px-4 py-3.5">
          <h2 id="resume-heading" class="mb-2 text-heading">Resume</h2>
          {#if resume}
            <ResumePreview applicationId={detail.id} file={resume} />
          {:else}
            <p class="text-muted-foreground">No resume was uploaded.</p>
          {/if}
        </section>

        <section aria-labelledby="answers-heading" class="rounded-xl border border-border bg-card px-4 py-3.5">
          <h2 id="answers-heading" class="mb-2 text-heading">Answers</h2>
          {#if detail.answers.filter((answer) => answer.questionId !== 'resume').length === 0}
            <p class="text-muted-foreground">No questionnaire answers.</p>
          {:else}
            <dl class="flex flex-col divide-y divide-border-subtle">
              {#each detail.answers.filter((answer) => answer.questionId !== 'resume') as answer (answer.questionId)}
                {@const question = questions.get(answer.questionId)}
                {@const file = question?.type === 'File' && answer.value ? fileById.get(answer.value) : undefined}
                <div class="flex flex-col gap-0.5 py-2">
                  <dt class="font-semibold">{question?.label ?? answer.questionId}</dt>
                  <dd class="whitespace-pre-line text-secondary-foreground">
                    {#if file && canDownload(file)}
                      <a href={getDownloadApplicationFileUrl(detail.id, file.id)} data-sveltekit-reload>Download {file.fileName}</a>
                    {:else if file}
                      {file.fileName}: {withheldReason(file)?.label}.
                    {:else}
                      {answerText(answer, question)}
                    {/if}
                  </dd>
                </div>
              {/each}
            </dl>
          {/if}
        </section>

        <section aria-labelledby="declarations-heading" class="rounded-xl border border-border bg-card px-4 py-3.5">
          <h2 id="declarations-heading" class="mb-2 text-heading">Declarations</h2>
          <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5">
            <dt class="text-muted-foreground">AI use</dt>
            <dd>
              {detail.aiUseDeclaration === 'Used' ? 'Used AI' : detail.aiUseDeclaration === 'DidNotUse' ? 'Did not use AI' : 'Not asked or not answered'}{detail.aiUseDetails
                ? `: ${detail.aiUseDetails}`
                : ''}
            </dd>
            <dt class="text-muted-foreground">Adjustments</dt>
            <dd>{detail.accommodationRequested ? 'Asked for adjustments to the process' : 'None requested'}</dd>
          </dl>
        </section>
      </div>

      <div class="flex min-w-0 flex-[1_1_360px] flex-col gap-3.5">
        <IntegrityPanel applicationId={detail.id} />

        <section aria-labelledby="timeline-heading" class="flex min-w-0 flex-col rounded-xl border border-border bg-card px-4 py-3.5">
          <h2 id="timeline-heading" class="mb-2 text-heading">Timeline</h2>
          {#if timeline.isError}
            <QueryError message={describeError(timeline.error, 'Could not load the timeline')} onretry={() => timeline.refetch()} inset />
          {:else if !timeline.data}
            <Skeleton class="h-16 w-full" />
          {:else}
            <ol class="flex flex-col gap-3 border-l border-border pl-4">
              {#each timeline.data.data.toReversed() as entry (entry.id)}
                <li class="flex flex-col gap-0.5">
                  <span class="font-semibold">{describeEvent(entry)} <span class="font-normal text-muted-foreground">{actor(entry)}</span></span>
                  {#if entry.note}<span class="text-secondary-foreground">"{entry.note}"</span>{/if}
                  <RelativeTime at={entry.occurredAt} class="text-caption text-muted-foreground" />
                </li>
              {/each}
            </ol>
          {/if}
        </section>
      </div>
    </div>
  {/if}
</main>
