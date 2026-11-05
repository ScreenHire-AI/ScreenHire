<script lang="ts">
  import { createGetPostingCustomization, getGetPostingCustomizationQueryKey } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import CustomizePostingForm from '#lib/components/customize-posting-form.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { describeError } from '#lib/query.js';

  // Customize in a sheet (design system section 11): the title and description one board receives, with a preview of
  // what it gets. Only that board is posted again; the job and every other board stay as they are. The sheet starts afresh
  // each time it opens, so the form mounts with the customization it loaded.
  let {
    open = $bindable(false),
    jobId,
    boardId,
    boardName,
  }: {
    open?: boolean;
    jobId: string;
    boardId: string;
    boardName: string;
  } = $props();

  const queryClient = useQueryClient();
  const customization = createGetPostingCustomization(
    () => jobId,
    () => boardId,
    () => ({ query: { enabled: open } }),
  );

  // The form copies what it loaded and keeps what is typed when a live update refetches this, so a reopened sheet must
  // not start from the last open's answer: it may be stale by then.
  $effect(() => {
    if (!open) queryClient.removeQueries({ queryKey: getGetPostingCustomizationQueryKey(jobId, boardId) });
  });
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="overflow-y-auto data-[side=right]:w-full data-[side=right]:sm:max-w-2xl">
    <Sheet.Header>
      <Sheet.Title>Customize for {boardName}</Sheet.Title>
      <Sheet.Description>Only {boardName} gets this text; a field that matches the job follows it.</Sheet.Description>
    </Sheet.Header>
    {#if open}
      <div class="px-4 pb-4">
        {#if customization.isError}
          <QueryError message={describeError(customization.error, `Could not load ${boardName}'s text`)} onretry={() => customization.refetch()} inset />
        {:else if customization.data}
          <CustomizePostingForm
            customization={customization.data.data}
            {jobId}
            oncancel={() => (open = false)}
            onsaved={() => (open = false)}
          />
        {:else}
          <div aria-hidden="true" class="flex flex-col gap-3">
            <Skeleton class="h-9 w-full" />
            <Skeleton class="h-64 w-full rounded-lg" />
          </div>
          <p class="sr-only" role="status">Loading…</p>
        {/if}
      </div>
    {/if}
  </Sheet.Content>
</Sheet.Root>
