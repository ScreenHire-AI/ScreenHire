<script lang="ts">
  import type { HttpValidationProblemDetails, JobStatus, JobSummary } from '@screenhire/api-client';
  import {
    ApiError,
    createCloseJob,
    createCreateJob,
    createListHiringTemplates,
    createListJobs,
    createPublishJob,
  } from '@screenhire/api-client/svelte-query';
  import { CreateJobBody } from '@screenhire/api-client/zod';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import { goto } from '$app/navigation';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge, type BadgeVariant } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { formatShortDate } from '#lib/format.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

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

  // A new job is a draft with its own copy of a published template version's workflow (workflow-engine.md §1).
  const create = createCreateJob(() => ({ mutation: settle }));
  let creating = $state(false);
  const templates = createListHiringTemplates(() => ({ query: { enabled: creating } }));
  const usable = $derived((templates.data?.data ?? []).filter((template) => template.latestPublishedVersionId));

  // The select's "none" is an empty string, which the body's uuid would refuse; it becomes null on the way out.
  const newJobSchema = CreateJobBody.pick({ title: true }).extend({
    title: z.string().trim().min(1, 'Give the job a title.'),
    templateVersionId: z.string(),
  });
  const {
    form: newJob,
    errors: newJobErrors,
    enhance: newJobEnhance,
    submitting: newJobSubmitting,
    reset: resetNewJob,
  } = superForm(defaults({ title: '', templateVersionId: '' }, zod4(newJobSchema)), {
    SPA: true,
    validators: zod4(newJobSchema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form }) {
      if (!form.valid) return;
      try {
        const response = await create.mutateAsync({
          data: { title: form.data.title.trim(), templateVersionId: form.data.templateVersionId || null },
        });
        creating = false;
        await goto(`/jobs/${response.data.id}`);
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(form, error.body as HttpValidationProblemDetails);
        } else {
          form.valid = false;
          form.errors._errors = [describeError(error, 'Could not create the job')];
        }
      }
    },
  });

  $effect(() => {
    if (creating) resetNewJob({ data: { title: '', templateVersionId: '' } });
  });

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
    <Button onclick={() => (creating = true)}>New job</Button>
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
                  {#if job.templateUpdateAvailable}
                    <a href={`/jobs/${job.id}/workflow`} class="ml-2 text-[12px] font-semibold text-primary">Template update available</a>
                  {/if}
                </Table.Cell>
                <Table.Cell class="text-muted-foreground">{job.location}</Table.Cell>
                <Table.Cell><Badge variant={statusVariant[job.status]}>{job.status}</Badge></Table.Cell>
                <Table.Cell class="text-right font-mono text-xs text-muted-foreground">
                  {job.publishedAt ? formatShortDate(job.publishedAt) : '-'}
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

<Dialog.Root bind:open={creating}>
  <Dialog.Content class="sm:max-w-md">
    <Dialog.Header>
      <Dialog.Title>New job</Dialog.Title>
      <Dialog.Description>The job starts as a draft with its own copy of the template's workflow, which you can tailor.</Dialog.Description>
    </Dialog.Header>
    <form method="POST" use:newJobEnhance novalidate class="flex flex-col gap-4">
      <Field.Field data-invalid={$newJobErrors.title ? true : undefined}>
        <Field.Label for="new-job-title">Title</Field.Label>
        <Input
          id="new-job-title"
          name="title"
          maxlength={200}
          required
          bind:value={$newJob.title}
          aria-invalid={$newJobErrors.title ? 'true' : undefined}
          aria-describedby={$newJobErrors.title ? 'new-job-title-error' : undefined}
        />
        <Field.Error id="new-job-title-error" errors={$newJobErrors.title?.map((message) => ({ message }))} />
      </Field.Field>
      <Field.Field data-invalid={$newJobErrors.templateVersionId ? true : undefined}>
        <Field.Label for="new-job-template">Hiring template</Field.Label>
        <select
          id="new-job-template"
          name="templateVersionId"
          class={selectClass}
          bind:value={$newJob.templateVersionId}
          aria-invalid={$newJobErrors.templateVersionId ? 'true' : undefined}
          aria-describedby={$newJobErrors.templateVersionId ? 'new-job-template-error' : undefined}
        >
          <option value="">None: a basic pipeline</option>
          {#each usable as template (template.id)}
            <option value={template.latestPublishedVersionId}>{template.name} (v{template.latestPublishedVersion})</option>
          {/each}
        </select>
        <Field.Error id="new-job-template-error" errors={$newJobErrors.templateVersionId?.map((message) => ({ message }))} />
      </Field.Field>
      {#if $newJobErrors._errors}
        <p role="alert" class="text-sm text-destructive">{$newJobErrors._errors[0]}</p>
      {/if}
      <Dialog.Footer>
        <Dialog.Close type="button" class={cn(buttonVariants({ variant: 'outline' }))}>Cancel</Dialog.Close>
        <Button type="submit" disabled={$newJobSubmitting}>Create job</Button>
      </Dialog.Footer>
    </form>
  </Dialog.Content>
</Dialog.Root>
