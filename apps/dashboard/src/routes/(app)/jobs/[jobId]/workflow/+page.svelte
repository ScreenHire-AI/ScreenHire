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
  import ActionBar from '#lib/components/action-bar.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import LeaveDialog from '#lib/components/leave-dialog.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import ProblemMark from '#lib/components/workflow/problem-mark.svelte';
  import ValidationBadge from '#lib/components/workflow/validation-badge.svelte';
  import WorkflowEditor from '#lib/components/workflow/workflow-editor.svelte';
  import WorkflowSummary from '#lib/components/workflow/workflow-summary.svelte';
  import WorkflowTabs from '#lib/components/workflow/workflow-tabs.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';
  import { createWorkflowDraft } from '#lib/workflow-draft.svelte.js';
  import type { PageProps } from './$types';

  // A job's own copy of its hiring workflow (workflow-engine.md §1), tailored for this job. A newer published version of
  // the template it came from shows as an update, with the differences, and applies only when someone asks. A closed
  // job's workflow can no longer change, so it reads as content.
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
  // The sidebar's own phone test (under md), where the primary action moves to the action bar.
  const sidebar = Sidebar.useSidebar();
  let tab = $state('pipeline');
  let notice = $state('');
  let failure = $state('');
  let reviewing = $state(false);
  let applyFailure = $state('');


  async function saveWorkflow() {
    if (!detail || !draft.workflow) return;
    failure = '';
    // Every save is a new version of the job's workflow, so an unchanged one is not sent.
    if (!draft.dirty) {
      notice = 'No changes to save.';
      return;
    }
    try {
      const response = await save.mutateAsync({
        jobId: params.jobId,
        data: { workflow: draft.workflow, expectedWorkflowVersion: baseVersion },
      });
      draft.saved(response.data.workflow, `${response.data.id}:${response.data.workflowVersion}`);
      notice = `Saved. The job's workflow is now version ${response.data.workflowVersion}.`;
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
      notice = `Applied version ${update.latestVersion} of ${detail.template?.templateName}.`;
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
  const workArea = 'mx-auto flex w-full max-w-page flex-col gap-5 px-4 py-6 md:px-6';
</script>

<svelte:head>
  <title>Workflow: {detail?.title ?? 'Job'} · ScreenHire</title>
</svelte:head>

<TopBar
  crumbs={[{ label: 'Jobs', href: '/' }, { label: detail?.title ?? 'Job', href: `/jobs/${params.jobId}` }, { label: 'Workflow' }]}
  back
/>

<main class={[sidebar.isMobile && !readonly && detail && draft.workflow && 'pb-28']}>
  {#if job.isError}
    <HeaderBand title={notFound ? 'Job not found' : 'Workflow'} />
    <div class={workArea}>
      {#if notFound}
        <Panel title="Details">
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>This job does not exist, or it belongs to another organization.</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button href="/" variant="outline" size="lg">Back to jobs</Button></Empty.Content>
          </Empty.Root>
        </Panel>
      {:else}
        <QueryError message={describeError(job.error, 'Could not load this job')} onretry={() => job.refetch()} />
      {/if}
    </div>
  {:else if !detail || !draft.workflow}
    <div aria-hidden="true">
      <div class="border-b border-border bg-card">
        <div class="mx-auto flex max-w-page flex-col gap-4 px-4 pt-5 md:px-6">
          <Skeleton class="h-9 w-80 max-w-full" />
          <Skeleton class="h-8 w-96 max-w-full" />
        </div>
      </div>
      <div class={workArea}>
        <Skeleton class="h-96 w-full rounded-panel" />
      </div>
    </div>
    <p class="sr-only" role="status">Loading the workflow…</p>
  {:else}
    <Tabs.Root bind:value={tab}>
      <HeaderBand title={`Workflow: ${detail.title}`}>
        {#snippet status()}
          <ValidationBadge errors={draft.errors} checking={draft.checking} editable={!readonly} />
          <span class="flex flex-wrap gap-1.5">
            <Badge variant="muted" class="tabular-nums">Workflow version {detail.workflowVersion}</Badge>
            {#if detail.template}
              <Badge variant="muted">From {detail.template.templateName} v{detail.template.version}</Badge>
            {:else}
              <Badge variant="muted">Not from a template</Badge>
            {/if}
            {#if readonly}<Badge variant="muted">Job closed</Badge>{/if}
          </span>
        {/snippet}
        {#snippet actions()}
          <p role="status" class="text-muted-foreground empty:hidden">{notice}</p>
          <p role="alert" class="inline-flex items-center gap-1.5 font-semibold text-status-verify empty:hidden">{#if failure}<ProblemMark />{failure}{/if}</p>
          <!-- The one primary action; a phone has it in the action bar (section 8). -->
          {#if !readonly && !sidebar.isMobile}{@render saveButton()}{/if}
        {/snippet}
        {#snippet tabs()}<WorkflowTabs />{/snippet}
      </HeaderBand>

      <div class={workArea}>
        {#if draft.outdated}
          <Panel title="Someone else saved this workflow" role="alert">
            {#snippet actions()}<Button variant="outline" onclick={() => draft.discardForNewer()}>Discard mine and load theirs</Button>{/snippet}
            <p>It is now version {detail.workflowVersion}. Your changes here are not saved, and saving them would be refused.</p>
          </Panel>
        {/if}
        {#if detail.template?.updateAvailable && !readonly}
          <Panel title="Template update available">
            {#snippet actions()}<Button variant="outline" onclick={() => (reviewing = true)}>Review update</Button>{/snippet}
            <p>
              {detail.template.templateName} has version {detail.template.latestPublishedVersion}; this job uses version
              {detail.template.version}. Nothing changes until you apply it.
            </p>
          </Panel>
        {/if}

        {#if readonly}
          <WorkflowSummary workflow={draft.workflow} errors={draft.errors} {tab} />
        {:else}
          <WorkflowEditor bind:workflow={() => draft.workflow!, (value) => (draft.workflow = value)} errors={draft.errors} {tab} />
        {/if}
      </div>
    </Tabs.Root>
  {/if}
  {#if sidebar.isMobile && !readonly && detail && draft.workflow}
    <ActionBar>{@render saveButton()}</ActionBar>
  {/if}
</main>

{#snippet saveButton()}
  <Button size="lg" onclick={saveWorkflow} disabled={save.isPending}>Save workflow</Button>
{/snippet}

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
      <QueryError message={describeError(diff.error, 'Could not load the differences')} onretry={() => diff.refetch()} inset />
    {:else if update}
      <ul aria-label="Differences" class="flex flex-col divide-y divide-border-subtle rounded-lg border border-border text-body">
        {#each update.changes as change, index (index)}
          <li class="flex flex-col gap-0.5 px-3 py-2">
            <span><span class="font-semibold">{changeWords[change.kind]}</span> <code class="font-mono text-code">{change.path}</code></span>
            {#if change.before !== null}<span class="break-all text-muted-foreground">Before: <code class="font-mono text-code">{change.before}</code></span>{/if}
            {#if change.after !== null}<span class="break-all">After: <code class="font-mono text-code">{change.after}</code></span>{/if}
          </li>
        {:else}
          <li class="px-3 py-2 text-muted-foreground">No differences in the workflow.</li>
        {/each}
      </ul>
    {/if}
    {#if draft.dirty}<p class="text-body font-semibold">Your unsaved changes on this page are discarded too.</p>{/if}
    <p role="alert" class="flex items-center gap-1.5 font-semibold text-status-verify empty:hidden">{#if applyFailure}<ProblemMark />{applyFailure}{/if}</p>
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline' }))}>Keep this workflow</Dialog.Close>
      <Button onclick={applyUpdate} disabled={apply.isPending || !update}>Apply update</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>

<LeaveDialog unsaved={draft.dirty && !readonly} title="Leave without saving this job's workflow?" />
