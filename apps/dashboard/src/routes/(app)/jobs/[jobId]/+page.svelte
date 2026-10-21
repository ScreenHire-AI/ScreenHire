<script lang="ts">
  import { ApiError, createCloseJob, createGetJob, createListBoards, createListJobApplications } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import DistributionStrip from '#lib/components/distribution-strip.svelte';
  import IntegrityOverview from '#lib/components/integrity-overview.svelte';
  import JobDetailsSheet from '#lib/components/job-details-sheet.svelte';
  import JobPipeline from '#lib/components/job-pipeline.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { timeAgo } from '#lib/dates.js';
  import { employmentLabel, formatSalary } from '#lib/format.js';
  import { noIntegrityFilter, type IntegrityFilter } from '#lib/integrity.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  // ApplicationSubmitted, ApplicationMoved, IntegrityAssessed and JobPostingsChanged refetch them live (#lib/realtime.ts).
  const job = createGetJob(() => params.jobId);
  const applications = createListJobApplications(() => params.jobId);
  const boards = createListBoards();
  const close = createCloseJob(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));

  const detail = $derived(job.data?.data);
  const all = $derived(applications.data?.data ?? []);

  // Set from the overview's legend or the pipeline's filter menu, through the pipeline's setIntegrityFilter.
  let integrityFilter = $state<IntegrityFilter>({ ...noIntegrityFilter });
  let pipeline = $state<JobPipeline>();
  let confirmClose = $state(false);
  let editing = $state(false);
  let saved = $state('');
  let closeFailure = $state('');

  const meta = $derived.by(() => {
    if (!detail) return [];
    const parts = [detail.location, employmentLabel(detail.employmentType)];
    if (detail.salary) parts.push(formatSalary(detail.salary));
    if (applications.isSuccess) parts.push(`${all.length} ${all.length === 1 ? 'applicant' : 'applicants'}`);
    if (detail.status === 'Open' && detail.publishedAt) parts.push(`Opened ${timeAgo(detail.publishedAt, Date.now())}`);
    else if (detail.status === 'Closed' && detail.closedAt) parts.push(`Closed ${timeAgo(detail.closedAt, Date.now())}`);
    else parts.push(detail.status);
    return parts.filter(Boolean);
  });

  async function closeEverywhere() {
    closeFailure = '';
    try {
      await close.mutateAsync({ jobId: params.jobId });
      confirmClose = false;
    } catch (error) {
      closeFailure = describeError(error, 'Could not close the job');
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

<main class="flex flex-col gap-3.5 px-5 pt-4.5 pb-9">
  {#if job.isError}
    {#if notFound}
      <h1 class="font-display text-page-title">Job not found</h1>
      <p class="text-muted-foreground">This job does not exist, or it belongs to another organization. <a href="/">Back to jobs</a></p>
    {:else}
      <h1 class="font-display text-page-title">Job</h1>
      <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} />
    {/if}
  {:else if !detail}
    <div aria-hidden="true" class="flex flex-col gap-2">
      <Skeleton class="h-9 w-80" />
      <Skeleton class="h-4 w-96" />
      <Skeleton class="mt-2 h-10 w-full" />
    </div>
    <p class="sr-only" role="status">Loading the job…</p>
  {:else}
    <section aria-label="Job" class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex min-w-0 flex-col gap-1.5">
        <h1 class="font-display text-page-title">{detail.title}</h1>
        <ul aria-label="Job details" class="flex flex-wrap gap-x-3.5 gap-y-1 text-caption text-muted-foreground">
          {#each meta as part (part)}<li>{part}</li>{/each}
        </ul>
      </div>
      <div class="flex flex-wrap gap-2">
        {#if detail.status !== 'Closed'}
          <Button variant="outline" class="h-[34px] font-semibold" onclick={() => ((saved = ''), (editing = true))}>Edit details</Button>
        {/if}
        <a href={`/jobs/${detail.id}/workflow`} class={cn(buttonVariants({ variant: 'outline' }), 'h-[34px] font-semibold')}>
          Workflow{#if detail.template?.updateAvailable}<span class="text-primary">, update available</span>{/if}
        </a>
        {#if detail.status === 'Open'}
          <Button variant="outline" class="h-[34px] border-destructive/40 font-semibold text-destructive" onclick={() => (confirmClose = true)}
            >Close &amp; remove everywhere</Button
          >
        {/if}
      </div>
    </section>

    <p role="status" class="text-body text-muted-foreground empty:hidden">{saved}</p>
    <JobDetailsSheet job={detail} bind:open={editing} onsaved={(message) => (saved = message)} />

    <DistributionStrip job={detail} boards={boards.data?.data ?? []} />

    {#if applications.isSuccess && all.length > 0}
      <IntegrityOverview applications={all} bind:filter={() => integrityFilter, (next) => pipeline?.setIntegrityFilter(next)} />
    {/if}

    <JobPipeline
      bind:this={pipeline}
      bind:integrityFilter
      jobTitle={detail.title}
      stages={detail.workflow.stages}
      applications={all}
      pending={applications.isPending}
      error={applications.isError ? applications.error : null}
      onretry={() => applications.refetch()}
    />
  {/if}
</main>

<Dialog.Root bind:open={confirmClose}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>Close this job and remove it everywhere?</Dialog.Title>
      <Dialog.Description>
        The careers page stops taking applications at once, and every board is asked to remove the job. Boards that
        refresh on their own schedule may show it until they do. A closed job cannot be reopened.
      </Dialog.Description>
    </Dialog.Header>
    <p role="status" class="text-status-verify empty:hidden">{closeFailure}</p>
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline' }))}>Keep it open</Dialog.Close>
      <Button variant="destructive" disabled={close.isPending} onclick={closeEverywhere}>Close &amp; remove everywhere</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
