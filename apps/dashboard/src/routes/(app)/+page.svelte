<script lang="ts">
  import type { JobStatus, JobSummary } from '@screenhire/api-client';
  import { createCloseJob, createListJobs, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge, type BadgeVariant } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { formatShortDate } from '#lib/format.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  const queryClient = useQueryClient();
  // Live JobPublished, JobClosed and HiringTemplateVersionPublished events refetch this list (#lib/realtime.ts).
  const jobs = createListJobs();
  const settle = { onSettled: () => invalidatePath(queryClient, jobsPath) };
  const publish = createPublishJob(() => ({ mutation: settle }));
  const close = createCloseJob(() => ({ mutation: settle }));

  const statusVariant: Record<JobStatus, BadgeVariant> = {
    Open: 'clear',
    Draft: 'secondary',
    Closed: 'outline',
  };

  // The one change each status allows from this list. A closed job has none: it is never reopened. Closing from here
  // asks nothing more because the job page's "Close & remove everywhere" is where the boards are explained.
  const actions: Partial<Record<JobStatus, { label: string; run: (jobId: string) => Promise<unknown> }>> = {
    Draft: { label: 'Publish', run: (jobId) => publish.mutateAsync({ jobId }) },
    Open: { label: 'Close', run: (jobId) => close.mutateAsync({ jobId }) },
  };

  const list = $derived(jobs.data?.data ?? []);
  const openCount = $derived(list.filter((job) => job.status === 'Open').length);

  let busyJobId = $state<string | null>(null);
  let failure = $state('');

  async function act(job: JobSummary, action: NonNullable<(typeof actions)[JobStatus]>) {
    busyJobId = job.id;
    failure = '';
    try {
      await action.run(job.id);
    } catch (error) {
      failure = describeError(error, `Could not ${action.label.toLowerCase()} ${job.title}`);
    } finally {
      busyJobId = null;
    }
  }
</script>

<svelte:head>
  <title>Jobs · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs' }]} />

<main class="flex flex-col gap-3.5 px-5 py-4.5">
  <div class="flex flex-wrap items-end justify-between gap-3">
    <div class="flex flex-col gap-1.5">
      <h1 class="font-display text-[30px] leading-tight font-bold tracking-[-0.02em]">Jobs</h1>
      <p class="font-mono text-[11.5px] text-muted-foreground uppercase">
        {#if jobs.isSuccess}{list.length} jobs · {openCount} open{:else}&nbsp;{/if}
      </p>
    </div>
  </div>

  <p role="status" class="text-status-verify empty:hidden">{failure}</p>

  {#if jobs.isError}
    <QueryError message={describeError(jobs.error, 'Could not load jobs')} onretry={() => jobs.refetch()} />
  {:else}
    <section aria-label="Jobs" class="overflow-hidden rounded-xl border border-border bg-card">
      <Table.Root>
        <Table.Header>
          <Table.Row>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Title</Table.Head>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Location</Table.Head>
            <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Status</Table.Head>
            <Table.Head class="text-right font-mono text-[10.5px] tracking-[0.06em] uppercase">Published</Table.Head>
            <Table.Head class="text-right font-mono text-[10.5px] tracking-[0.06em] uppercase">
              <span class="sr-only">Actions</span>
            </Table.Head>
          </Table.Row>
        </Table.Header>
        <Table.Body>
          {#if jobs.isPending}
            {#each [0, 1, 2] as row (row)}
              <Table.Row aria-hidden="true">
                <Table.Cell colspan={5}><Skeleton class="h-5 w-full" /></Table.Cell>
              </Table.Row>
            {/each}
          {:else}
            {#each list as job (job.id)}
              {@const action = actions[job.status]}
              <Table.Row>
                <Table.Cell class="font-semibold">
                  <a href={`/jobs/${job.id}`} class="text-foreground no-underline hover:underline">{job.title}</a>
                </Table.Cell>
                <Table.Cell class="text-muted-foreground">{job.location}</Table.Cell>
                <Table.Cell><Badge variant={statusVariant[job.status]}>{job.status}</Badge></Table.Cell>
                <Table.Cell class="text-right font-mono text-xs text-muted-foreground">
                  {job.publishedAt ? formatShortDate(job.publishedAt) : '—'}
                </Table.Cell>
                <Table.Cell class="text-right">
                  {#if action}
                    <Button
                      variant="outline"
                      size="xs"
                      aria-label={`${action.label} ${job.title}`}
                      disabled={busyJobId === job.id}
                      onclick={() => act(job, action)}>{action.label}</Button
                    >
                  {/if}
                </Table.Cell>
              </Table.Row>
            {:else}
              <Table.Row>
                <Table.Cell colspan={5} class="text-muted-foreground">No jobs yet.</Table.Cell>
              </Table.Row>
            {/each}
          {/if}
        </Table.Body>
      </Table.Root>
    </section>
  {/if}
</main>
