<script lang="ts">
  import type { WorkflowChangeKind } from '@screenhire/api-client';
  import {
    ApiError,
    createApplyJobTemplateUpdate,
    createGetJob,
    createGetJobTemplateDiff,
    createUpdateJobWorkflow,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { beforeNavigate } from '$app/navigation';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import type { PageProps } from './$types';

  // A job's own copy of its hiring workflow (workflow-engine.md §1), tailored for this job. A newer published version of
  // the template it came from shows as an update, with the differences, and applies only when someone asks.
  let { params }: PageProps = $props();

  const queryClient = useQueryClient();
  const refresh = () => invalidatePath(queryClient, jobsPath);
  const job = createGetJob(() => params.jobId);
  const detail = $derived(job.data?.data);
  const diff = createGetJobTemplateDiff(
    () => params.jobId,
    () => ({ query: { enabled: !!detail?.template?.updateAvailable } }),
  );
  const update = $derived(diff.data?.data);
  const save = createUpdateJobWorkflow(() => ({ mutation: { onSettled: refresh } }));
  const apply = createApplyJobTemplateUpdate(() => ({ mutation: { onSettled: refresh } }));

  const draft = createWorkflowDraft();
  $effect(() => {
    if (detail) draft.load(detail.workflow, `${detail.id}:${detail.workflowVersion}`);
  });
  // The version the edit started from, not the newest one fetched: after someone else saves, the refetch moves
  // `detail.workflowVersion` on while the unsaved edit stays, and saving it must be refused rather than overwrite theirs.
  const baseVersion = $derived(Number(draft.source.split(':').at(-1)));

  const readonly = $derived(detail?.status === 'Closed');
  let status = $state('');
  let failure = $state('');
  let reviewing = $state(false);
  let applyFailure = $state('');

  beforeNavigate((navigation) => {
    if (draft.dirty && !readonly && !confirm("Leave without saving this job's workflow?")) navigation.cancel();
  });

  async function saveWorkflow() {
    if (!detail || !draft.workflow) return;
    failure = '';
    try {
      const response = await save.mutateAsync({
        jobId: params.jobId,
        data: { workflow: draft.workflow, expectedWorkflowVersion: baseVersion },
      });
      draft.saved(response.data.workflow, `${response.data.id}:${response.data.workflowVersion}`);
      status = `Saved. The job's workflow is now version ${response.data.workflowVersion}.`;
    } catch (error) {
      failure = draft.failed(error, 'Could not save the workflow');
    }
  }

  async function applyUpdate() {
    if (!detail || !update) return;
    applyFailure = '';
    try {
      const response = await apply.mutateAsync({
        jobId: params.jobId,
        data: { templateVersionId: update.latestVersionId, expectedWorkflowVersion: detail.workflowVersion },
      });
      draft.replace(response.data.workflow, `${response.data.id}:${response.data.workflowVersion}`);
      reviewing = false;
      status = `Applied version ${update.latestVersion} of ${detail.template?.templateName}.`;
    } catch (error) {
      applyFailure =
        error instanceof ApiError && error.status === 400
          ? Object.values((error.body as { errors?: Record<string, string[]> } | undefined)?.errors ?? {})
              .flat()
              .join(' ') || describeError(error, 'Could not apply the update')
          : describeError(error, 'Could not apply the update');
    }
  }

  const changeWords: Record<WorkflowChangeKind, string> = { Added: 'Added', Removed: 'Removed', Changed: 'Changed' };
  const notFound = $derived(job.error instanceof ApiError && job.error.status === 404);
</script>

<svelte:head>
  <title>Workflow: {detail?.title ?? 'Job'} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Jobs', href: '/' }, { label: detail?.title ?? 'Job', href: `/jobs/${params.jobId}` }, { label: 'Workflow' }]} />

<main class="flex flex-col gap-3.5 px-5 pt-4.5 pb-9">
  {#if job.isError}
    {#if notFound}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Job not found</h1>
      <p class="text-muted-foreground">This job does not exist, or it belongs to another organization. <a href="/">Back to jobs</a></p>
    {:else}
      <h1 class="font-display text-[30px] font-bold tracking-[-0.02em]">Workflow</h1>
      <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} />
    {/if}
  {:else if !detail || !draft.workflow}
    <div aria-hidden="true" class="flex flex-col gap-2">
      <Skeleton class="h-9 w-80" />
      <Skeleton class="mt-2 h-64 w-full" />
    </div>
    <p class="sr-only" role="status">Loading the workflow…</p>
  {:else}
    <section aria-label="Job workflow" class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex min-w-0 flex-col gap-1.5">
        <h1 class="font-display text-[30px] leading-[1.1] font-bold tracking-[-0.02em]">Workflow: {detail.title}</h1>
        <p class="font-mono text-[11.5px] text-muted-foreground uppercase">
          Workflow version {detail.workflowVersion}
          {#if detail.template}· from {detail.template.templateName} v{detail.template.version}{:else}· not from a template{/if}
        </p>
      </div>
      {#if !readonly}
        <Button onclick={saveWorkflow} disabled={save.isPending || !draft.dirty}>Save workflow</Button>
      {/if}
    </section>

    {#if detail.template?.updateAvailable}
      <section aria-labelledby="update-heading" class="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-card px-3.5 py-3">
        <div>
          <h2 id="update-heading" class="font-bold">Template update available</h2>
          <p class="text-[13px] text-muted-foreground">
            {detail.template.templateName} has version {detail.template.latestPublishedVersion}; this job uses version
            {detail.template.version}. Nothing changes until you apply it.
          </p>
        </div>
        <Button variant="outline" onclick={() => (reviewing = true)} disabled={readonly}>Review update</Button>
      </section>
    {/if}

    {#if draft.outdated}
      <div role="alert" class="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-card px-3.5 py-2.5 text-[13px]">
        <p>Someone else saved this job's workflow (now version {detail.workflowVersion}). Your changes here are not saved, and saving them would be refused.</p>
        <Button variant="outline" size="sm" onclick={() => draft.discardForNewer()}>Discard mine and load theirs</Button>
      </div>
    {/if}
    <p role="status" class="text-[13px] empty:hidden">{status}</p>
    <p role="alert" class="text-[13px] font-semibold text-destructive empty:hidden">{failure}</p>
    {#if readonly}<p class="text-[13px] text-muted-foreground">This job is closed, so its workflow can no longer change.</p>{/if}

    <WorkflowEditor
      bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)}
      errors={draft.errors}
      checking={draft.checking}
      {readonly}
    />
  {/if}
</main>

<Dialog.Root bind:open={reviewing}>
  <Dialog.Content class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
    <Dialog.Header>
      <Dialog.Title>Apply version {update?.latestVersion ?? ''} of {detail?.template?.templateName ?? 'the template'}?</Dialog.Title>
      <Dialog.Description>
        Applying replaces this job's workflow with the template's, including any changes made to this job alone. These
        are all the differences. Candidates already here stay where they are.
      </Dialog.Description>
    </Dialog.Header>
    {#if diff.isPending}
      <Skeleton class="h-24 w-full" />
    {:else if diff.isError}
      <QueryError message={describeError(diff.error, 'Could not load the differences')} onretry={() => diff.refetch()} />
    {:else if update}
      <ul aria-label="Differences" class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border text-[13px]">
        {#each update.changes as change, index (index)}
          <li class="flex flex-col gap-0.5 px-3 py-2">
            <span><span class="font-semibold">{changeWords[change.kind]}</span> <code class="font-mono text-[12px]">{change.path}</code></span>
            {#if change.before !== null}<span class="font-mono text-[11.5px] break-all text-muted-foreground">Before: {change.before}</span>{/if}
            {#if change.after !== null}<span class="font-mono text-[11.5px] break-all">After: {change.after}</span>{/if}
          </li>
        {:else}
          <li class="px-3 py-2 text-muted-foreground">No differences in the workflow.</li>
        {/each}
      </ul>
    {/if}
    {#if draft.dirty}<p class="text-[13px] font-semibold">Your unsaved changes on this page are discarded too.</p>{/if}
    <p role="alert" class="text-[13px] font-semibold text-destructive empty:hidden">{applyFailure}</p>
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline' }))}>Keep this workflow</Dialog.Close>
      <Button onclick={applyUpdate} disabled={apply.isPending || !update}>Apply update</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
