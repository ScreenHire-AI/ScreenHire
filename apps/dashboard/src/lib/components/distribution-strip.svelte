<script lang="ts">
  import type { BoardSummary, JobDetail } from '@screenhire/api-client';
  import { createCloseJobPosting, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import StatusMark from '#lib/components/status-mark.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { boardAction, boardRows, describePosting, type BoardAction } from '#lib/distribution.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  let {
    job,
    boards,
    now: fixedNow,
  }: {
    job: Pick<JobDetail, 'id' | 'status' | 'postings'>;
    boards: readonly BoardSummary[];
    /** A fixed clock for tests; the strip otherwise ticks every 15 s so estimates count down. */
    now?: number;
  } = $props();

  const queryClient = useQueryClient();
  let clock = $state(Date.now());
  const now = $derived(fixedNow ?? clock);
  $effect(() => {
    if (fixedNow !== undefined) return;
    const timer = setInterval(() => (clock = Date.now()), 15_000);
    return () => clearInterval(timer);
  });

  const rows = $derived(boardRows(boards, job.postings));
  let failure = $state('');
  let busyBoard = $state<string | null>(null);

  // Live updates refetch the job when a board answers; refetching here as well shows our own change at once.
  const settle = { onSettled: () => invalidatePath(queryClient, jobsPath) };
  const publish = createPublishJob(() => ({ mutation: settle }));
  const takeDown = createCloseJobPosting(() => ({ mutation: settle }));

  async function run(label: string, request: () => Promise<unknown>, boardId: string | null) {
    failure = '';
    busyBoard = boardId;
    try {
      await request();
    } catch (error) {
      failure = describeError(error, `Could not ${label}`);
    } finally {
      busyBoard = null;
    }
  }

  const publishEverywhere = () =>
    run('publish the job', () => publish.mutateAsync({ jobId: job.id }), null);

  function act(action: NonNullable<BoardAction>, boardId: string, name: string) {
    if (action === 'takeDown') {
      return run(`take the job down from ${name}`, () => takeDown.mutateAsync({ jobId: job.id, boardId }), boardId);
    }

    return run(`post the job to ${name}`, () => publish.mutateAsync({ jobId: job.id, data: { boards: [boardId] } }), boardId);
  }

  const actionLabels: Record<NonNullable<BoardAction>, (name: string) => { text: string; label: string }> = {
    post: (name) => ({ text: 'Post', label: `Post to ${name}` }),
    retry: (name) => ({ text: 'Retry', label: `Retry ${name}` }),
    takeDown: (name) => ({ text: 'Take down', label: `Take down from ${name}` }),
  };
</script>

<section aria-labelledby="distribution-heading" class="overflow-hidden rounded-xl border border-border bg-card">
  <div class="flex flex-wrap items-stretch">
    <h2
      id="distribution-heading"
      class="flex items-center border-r border-border-subtle px-3 py-2 text-caption text-muted-foreground"
    >
      Distribution
    </h2>
    <ul class="flex min-w-0 flex-1 flex-wrap">
      {#each rows as row (row.boardId)}
        {@const view = describePosting(row.posting, now)}
        {@const action = boardAction(job.status, row.board, row.posting)}
        <li
          class="flex items-center gap-2 border-r border-b border-border-subtle px-3 py-2 last:border-r-0"
          title={view.detail}
          data-board={row.boardId}
        >
          <StatusMark shape={view.shape} tone={view.tone} class="size-[7px]" />
          <span class="font-semibold">{row.name}</span>
          <span
            aria-hidden="true"
            data-state
            class={[
              'text-caption',
              view.tone === 'clear' && 'text-status-clear',
              view.tone === 'review' && 'text-status-review',
              view.tone === 'verify' && 'text-status-verify',
              view.tone === 'neutral' && 'text-muted-foreground',
            ]}>{view.label}</span
          >
          <span class="sr-only">{view.detail}</span>
          {#if action}
            {@const text = actionLabels[action](row.name)}
            <Button
              variant="ghost"
              size="xs"
              class="text-primary"
              aria-label={text.label}
              disabled={busyBoard === row.boardId}
              onclick={() => act(action, row.boardId, row.name)}>{text.text}</Button
            >
          {/if}
        </li>
      {:else}
        <li class="px-3 py-2 text-muted-foreground">No job boards are set up for this organization.</li>
      {/each}
    </ul>
    {#if job.status === 'Draft' && rows.length > 0}
      <div class="flex items-center px-3 py-1.5">
        <Button size="sm" disabled={publish.isPending} onclick={publishEverywhere}>Publish to {rows.length === 1 ? '1 board' : `${rows.length} boards`}</Button>
      </div>
    {/if}
  </div>
  <p role="status" class="border-t border-border-subtle px-3 py-2 text-status-verify empty:hidden">{failure}</p>
</section>
