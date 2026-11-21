<script lang="ts">
  import RefreshCwIcon from '~icons/lucide/refresh-cw';
  import type { JobDetail, WorkflowChangeKind } from '@screenhire/api-client';
  import { ApiError, createApplyJobTemplateUpdate, createGetJobTemplateDiff } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { templateUsagePath } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // A job's update to its template's newest published version (workflow-engine.md section 1): what changes, then Apply
  // update, which replaces the job's pipeline with the template's, the job's own changes included, so it always asks
  // first. The job's pipeline page and the hiring template's Review updates both open it. The differences load only
  // while it is open.
  let {
    open = $bindable(false),
    jobId,
    jobTitle,
    templateName,
    expectedWorkflowVersion,
    discardsUnsaved = false,
    onapplied,
  }: {
    open?: boolean;
    jobId: string;
    /** The job's title, where the dialog opens away from the job (the template's Review updates). */
    jobTitle?: string;
    templateName: string;
    /** The job's pipeline version the update replaces; a newer one is refused (409). */
    expectedWorkflowVersion: number;
    /** Whether unsaved changes on the page are lost too, which it then says. */
    discardsUnsaved?: boolean;
    onapplied?: (job: JobDetail, version: number) => void;
  } = $props();

  const queryClient = useQueryClient();
  const diff = createGetJobTemplateDiff(
    () => jobId,
    () => ({ query: { enabled: open } }),
  );
  const update = $derived(diff.data?.data);
  const apply = createApplyJobTemplateUpdate(() => ({
    mutation: { onSettled: () => Promise.all([invalidatePath(queryClient, jobsPath), invalidatePath(queryClient, templateUsagePath)]) },
  }));
  let failure = $state('');

  $effect(() => {
    if (open) failure = '';
  });

  async function applyUpdate() {
    if (!update) return;
    failure = '';
    try {
      const response = await apply.mutateAsync({ jobId, data: { templateVersionId: update.latestVersionId, expectedWorkflowVersion } });
      open = false;
      onapplied?.(response.data, update.latestVersion);
    } catch (error) {
      // A 400 names what stops it (a stage still holding candidates) in the validator's sentences.
      failure =
        error instanceof ApiError && error.status === 400
          ? Object.values((error.body as { errors?: Record<string, string[]> } | undefined)?.errors ?? {})
              .flat()
              .join(' ') || describeError(error, 'Could not apply the update')
          : describeError(error, 'Could not apply the update');
    }
  }

  const changeWords: Record<WorkflowChangeKind, string> = { Added: 'Added', Removed: 'Removed', Changed: 'Changed' };
</script>

<Dialog.Root bind:open>
  <Dialog.Content class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
    <Dialog.Header>
      <Dialog.Title>Apply version {update?.latestVersion ?? ''} of {templateName}{jobTitle ? ` to ${jobTitle}` : ''}?</Dialog.Title>
      <Dialog.Description>This replaces the job's pipeline with the template's, including changes made to this job alone.</Dialog.Description>
    </Dialog.Header>
    {#if diff.isPending}
      <Skeleton class="h-24 w-full" />
    {:else if diff.isError}
      <QueryError message={describeError(diff.error, 'Could not load the differences')} onretry={() => diff.refetch()} inset />
    {:else if update}
      <ul aria-label="Differences" class="flex flex-col divide-y divide-border rounded-lg bg-secondary text-body">
        {#each update.changes as change, index (index)}
          <li class="flex flex-col gap-0.5 px-3 py-2">
            <span><span class="font-semibold">{changeWords[change.kind]}</span> <code class="font-mono text-code">{change.path}</code></span>
            {#if change.before !== null}<span class="break-all text-muted-foreground">Before: <code class="font-mono text-code">{change.before}</code></span>{/if}
            {#if change.after !== null}<span class="break-all">After: <code class="font-mono text-code">{change.after}</code></span>{/if}
          </li>
        {:else}
          <li class="px-3 py-2 text-muted-foreground">No differences in the pipeline.</li>
        {/each}
      </ul>
    {/if}
    {#if discardsUnsaved}<p class="text-body font-semibold">Your unsaved changes on this page are discarded too.</p>{/if}
    <FailureText message={failure} />
    <Dialog.Footer>
      <Dialog.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Keep this pipeline</Dialog.Close>
      <Button size="lg" onclick={applyUpdate} disabled={apply.isPending || !update}><RefreshCwIcon aria-hidden="true" />Apply update</Button>
    </Dialog.Footer>
  </Dialog.Content>
</Dialog.Root>
