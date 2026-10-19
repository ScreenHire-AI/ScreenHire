<script lang="ts">
  import type { JobStatus } from '@screenhire/api-client';
  import { Badge, type BadgeVariant } from '#lib/components/ui/badge/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import type { PageProps } from './$types';

  let { data }: PageProps = $props();

  const statusVariant: Record<JobStatus, BadgeVariant> = {
    Open: 'clear',
    Draft: 'secondary',
    Closed: 'outline',
  };

  const dateFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
  const openCount = $derived(data.jobs.filter((job) => job.status === 'Open').length);
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

  <section aria-label="Jobs" class="overflow-hidden rounded-lg border border-border bg-card">
    <Table.Root>
      <Table.Header>
        <Table.Row>
          <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Title</Table.Head>
          <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Location</Table.Head>
          <Table.Head class="font-mono text-[10.5px] tracking-[0.06em] uppercase">Status</Table.Head>
          <Table.Head class="text-right font-mono text-[10.5px] tracking-[0.06em] uppercase">Published</Table.Head>
        </Table.Row>
      </Table.Header>
      <Table.Body>
        {#each data.jobs as job (job.id)}
          <Table.Row>
            <Table.Cell class="font-semibold">{job.title}</Table.Cell>
            <Table.Cell class="text-muted-foreground">{job.location}</Table.Cell>
            <Table.Cell><Badge variant={statusVariant[job.status]}>{job.status}</Badge></Table.Cell>
            <Table.Cell class="text-right font-mono text-xs text-muted-foreground">
              {job.publishedAt ? dateFormat.format(new Date(job.publishedAt)) : '—'}
            </Table.Cell>
          </Table.Row>
        {:else}
          <Table.Row>
            <Table.Cell colspan={4} class="text-muted-foreground">No jobs yet.</Table.Cell>
          </Table.Row>
        {/each}
      </Table.Body>
    </Table.Root>
  </section>
</main>
