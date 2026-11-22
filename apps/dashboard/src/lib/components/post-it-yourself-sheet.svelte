<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import type { JobDetail, ManualPostingStatus } from '@screenhire/api-client';
  import { createMarkManualPostingUpdated } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import ManualPostingForm from '#lib/components/manual-posting-form.svelte';
  import PostingTextCopy from '#lib/components/posting-text-copy.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { changedList, type PostItYourselfMode } from '#lib/manual-postings.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { cn } from '#lib/utils.js';

  // Post it yourself (design system section 11), from the Job postings tab: for a board ScreenHire does not post to. New:
  // the job's text to copy, then where it was posted, saved as a row. Edit: the row's details. Update: the job's text with
  // what changed since it was posted marked, the link, and Mark as updated once the listing matches.
  let {
    open = $bindable(false),
    mode,
    job,
    jobUrl,
    boardIds,
  }: {
    open?: boolean;
    mode: PostItYourselfMode;
    job: Pick<JobDetail, 'id' | 'title' | 'location' | 'employmentType' | 'salary' | 'description'>;
    jobUrl: string;
    boardIds: readonly string[];
  } = $props();

  const queryClient = useQueryClient();
  const markUpdated = createMarkManualPostingUpdated(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, jobsPath) } }));
  let failure = $state('');
  $effect(() => {
    if (open) failure = '';
  });

  async function confirmUpdated(posting: ManualPostingStatus) {
    failure = '';
    try {
      await markUpdated.mutateAsync({ jobId: job.id, postingId: posting.id });
      open = false;
    } catch (error) {
      failure = describeError(error, 'Could not mark the posting updated');
    }
  }
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-2xl" data-post-it-yourself>
    <Sheet.Header>
      {#if mode.kind === 'new'}
        <Sheet.Title>Post it yourself</Sheet.Title>
        <Sheet.Description>For a board ScreenHire does not post to. Copy the job, post it there with its apply link, then save where you posted it.</Sheet.Description>
      {:else if mode.kind === 'edit'}
        <Sheet.Title>Edit the posting on {mode.posting.boardName}</Sheet.Title>
        <Sheet.Description>The apply link stays the same, so links already on the board keep counting.</Sheet.Description>
      {:else}
        <Sheet.Title>Update {mode.posting.boardName}</Sheet.Title>
        <Sheet.Description>Paste what changed into the listing on {mode.posting.boardName}, then mark it updated.</Sheet.Description>
      {/if}
    </Sheet.Header>
    {#if open}
      <div class="flex flex-col gap-5 px-4 pb-4">
        {#if mode.kind === 'new'}
          <section aria-labelledby="post-text-heading" class="flex flex-col gap-2">
            <h3 id="post-text-heading" class="text-heading">The job</h3>
            <PostingTextCopy {job} />
          </section>
          <section aria-labelledby="post-where-heading" class="flex flex-col gap-2">
            <h3 id="post-where-heading" class="text-heading">Where you posted it</h3>
            <ManualPostingForm jobId={job.id} {jobUrl} {boardIds} oncancel={() => (open = false)} onsaved={() => (open = false)} />
          </section>
        {:else if mode.kind === 'edit'}
          <ManualPostingForm jobId={job.id} {jobUrl} {boardIds} posting={mode.posting} oncancel={() => (open = false)} onsaved={() => (open = false)} />
        {:else}
          {@const posting = mode.posting}
          {#if posting.changedFields.length > 0}
            <p data-changed-since>
              Since you posted it, the job changed {changedList(posting.changedFields)}.
              {#if posting.changedFields.includes('Link')}The job's address changed, so paste the new apply link too.{/if}
            </p>
          {:else}
            <p>The listing matches the job as it is now.</p>
          {/if}
          <PostingTextCopy {job} changed={posting.changedFields} />
          <div class="flex min-w-0 flex-col gap-1">
            <span class="font-semibold">Apply link</span>
            <p class="font-mono text-code break-all text-muted-foreground" data-tracked-link>{posting.trackedUrl}</p>
          </div>
          <div class="flex flex-wrap items-center justify-end gap-2 border-t border-border-subtle pt-4">
            <div class="mr-auto min-w-0"><FailureText message={failure} /></div>
            <Sheet.Close class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Close</Sheet.Close>
            {#if posting.changedFields.length > 0}
              <Button size="lg" disabled={markUpdated.isPending} onclick={() => confirmUpdated(posting)}><CheckIcon aria-hidden="true" />Mark as updated</Button>
            {/if}
          </div>
        {/if}
      </div>
    {/if}
  </Sheet.Content>
</Sheet.Root>
