<script lang="ts">
  import ArrowLeftIcon from '@lucide/svelte/icons/arrow-left';
  import { ApiError, createGetJob } from '@screenhire/api-client/svelte-query';
  import { goto } from '$app/navigation';
  import HeaderBand from '#lib/components/header-band.svelte';
  import JobDetailsForm from '#lib/components/job-details-form.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';
  import type { PageProps } from './$types';

  let { params }: PageProps = $props();

  const job = createGetJob(() => params.jobId);
  const detail = $derived(job.data?.data);
  const jobHref = $derived(`/jobs/${params.jobId}`);
  const notFound = $derived(job.error instanceof ApiError && job.error.status === 404);

  // The job page says what saving did (the boards wait for the next publish), in its Distribution panel.
  const saved = (message: string) => goto(jobHref, { state: { saved: message } });
</script>

<svelte:head>
  <title>Edit {detail?.title ?? 'job'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs', href: '/' }, { label: detail?.title ?? 'Job', href: jobHref }, { label: 'Edit details' }]} back />

<main class="flex flex-col">
  <HeaderBand form title="Edit job details" />

  <!-- The form template (section 7): one 688px column, labels above fields, actions at the end. -->
  <div class="mx-auto w-full max-w-form px-4 py-6 md:px-6">
    <!-- A failed refetch keeps the form and what was typed in it; the error shows only when there is no job yet. -->
    {#if job.isError && !detail}
      <Panel title={notFound ? 'Job not found' : 'Could not load the job'}>
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
    {:else if !detail}
      <Panel title="Details">
        <div aria-hidden="true" class="flex flex-col gap-4">
          {#each [0, 1, 2, 3] as field (field)}<Skeleton class="h-14 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the job…</p>
      </Panel>
    {:else if detail.status === 'Closed'}
      <Panel title="Details">
        <Empty.Root>
          <Empty.Header>
            <Empty.Title>This job is closed</Empty.Title>
            <Empty.Description>A closed job is no longer posted, so its details stay as they were.</Empty.Description>
          </Empty.Header>
          <Empty.Content><Button href={jobHref} variant="outline" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to the job</Button></Empty.Content>
        </Empty.Root>
      </Panel>
    {:else}
      <JobDetailsForm job={detail} cancelHref={jobHref} onsaved={saved} />
    {/if}
  </div>
</main>
