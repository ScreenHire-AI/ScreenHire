<script lang="ts">
  import { closeJob, publishJob, type JobStatus, type JobSummary } from '@screenhire/api-client';
  import { invalidate } from '$app/navigation';
  import { Badge, type BadgeVariant } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { jobsDependency } from '#lib/realtime.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  const statusVariant: Record<JobStatus, BadgeVariant> = {
    Open: 'clear',
    Draft: 'secondary',
    Closed: 'outline',
  };

  // The one change each status allows from this list. A closed job has none: it is never reopened.
  const actions: Partial<Record<JobStatus, { label: string; run: (jobId: string) => Promise<{ status: number }> }>> = {
    Draft: { label: 'Publish', run: publishJob },
    Open: { label: 'Close', run: closeJob },
  };

  const dateFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
  const openCount = $derived(data.jobs.filter((job) => job.status === 'Open').length);

  let busyJobId = $state<string | null>(null);
  let failure = $state('');

  async function act(job: JobSummary, action: NonNullable<(typeof actions)[JobStatus]>) {
    busyJobId = job.id;
    failure = '';
    try {
      const response = await action.run(job.id);
      if (response.status !== 200) {
        failure = `Could not ${action.label.toLowerCase()} ${job.title} (HTTP ${response.status}).`;
      }
      // Refetch now; other signed-in tabs and teammates get the same change through live updates.
      await invalidate(jobsDependency);
    } finally {
      busyJobId = null;
    }
  }
</script>

<svelte:head>
  <title>Jobs · ScreenHire</title>
</svelte:head>

<header class="flex flex-wrap items-center gap-2.5 border-b border-border bg-card px-5 py-2.5">
  <span class="text-muted-foreground">Jobs</span>
</header>

<main class="flex flex-col gap-3.5 px-5 py-4.5">
  <div class="flex flex-wrap items-end justify-between gap-3">
    <div class="flex flex-col gap-1.5">
      <h1 class="font-display text-[30px] leading-tight font-bold tracking-[-0.02em]">Jobs</h1>
      <p class="font-mono text-[11.5px] text-muted-foreground uppercase">
        {data.jobs.length} jobs · {openCount} open
      </p>
    </div>
  </div>

  <p role="status" class="text-status-verify empty:hidden">{failure}</p>

  <section aria-label="Jobs" class="overflow-hidden rounded-lg border border-border bg-card">
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
        {#each data.jobs as job (job.id)}
          {@const action = actions[job.status]}
          <Table.Row>
            <Table.Cell class="font-semibold">{job.title}</Table.Cell>
            <Table.Cell class="text-muted-foreground">{job.location}</Table.Cell>
            <Table.Cell><Badge variant={statusVariant[job.status]}>{job.status}</Badge></Table.Cell>
            <Table.Cell class="text-right font-mono text-xs text-muted-foreground">
              {job.publishedAt ? dateFormat.format(new Date(job.publishedAt)) : '—'}
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
      </Table.Body>
    </Table.Root>
  </section>
</main>
