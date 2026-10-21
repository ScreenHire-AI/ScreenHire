<script lang="ts">
  import type { BoardSummary, JobDetail } from '@screenhire/api-client';
  import { createCloseJobPosting, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import Hint from '#lib/components/hint.svelte';
  import Panel from '#lib/components/panel.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { formatDateTime } from '#lib/dates.js';
  import { boardAction, boardRows, describePosting, type BoardAction } from '#lib/distribution.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';

  // Where the job is posted: one row per board with its state as a status badge, the board's estimate as a fact pill
  // and the one thing the person can do there. Publishing a draft to every board is the page's primary action, in its
  // header band; closing the job is its More menu.
  let {
    job,
    boards,
    notice = '',
    publishFailure = '',
    now: fixedNow,
  }: {
    job: Pick<JobDetail, 'id' | 'status' | 'postings'>;
    boards: readonly BoardSummary[];
    /** A message about the boards from the page, such as what saving the job's details did. */
    notice?: string;
    /** Why the page's "Publish to N boards" failed. */
    publishFailure?: string;
    /** A fixed clock for tests; the panel otherwise ticks every 15 s so estimates count down. */
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

  async function run(label: string, request: () => Promise<unknown>, boardId: string) {
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

<Panel title="Distribution" count={rows.length} flush footer={failure || publishFailure || notice ? messages : undefined}>
  {#if rows.length === 0}
    <p class="px-4.5 py-4 text-muted-foreground max-md:px-4">No job boards are set up.</p>
  {:else}
    <ul>
      {#each rows as row (row.boardId)}
        {@const view = describePosting(row.posting, now)}
        {@const action = boardAction(job.status, row.board, row.posting)}
        <li data-board={row.boardId} class="flex min-w-0 flex-col gap-1.5 border-b border-border-subtle px-4.5 py-3 last:border-0 max-md:px-4">
          <div class="flex min-h-8 min-w-0 items-center gap-2">
            <TruncatedText text={row.name} class="font-semibold" />
            {#if action}
              {@const text = actionLabels[action](row.name)}
              <Button
                variant="ghost"
                class="ml-auto text-primary"
                aria-label={text.label}
                disabled={busyBoard === row.boardId}
                onclick={() => act(action, row.boardId, row.name)}>{text.text}</Button
              >
            {/if}
          </div>
          <!-- Status first, then the board's estimate (T2). The badge's tooltip says what the state means. -->
          <div class="flex min-w-0 flex-wrap items-center gap-1.5">
            <Hint content={view.detail}>
              {#snippet children({ props })}
                <span {...props} data-posting-state class="inline-flex rounded-full">
                  <StatusBadge status={view.status} label={view.label} quiet={view.status !== 'verify'} />
                </span>
              {/snippet}
            </Hint>
            <!-- The estimate's exact time is in its tooltip (T5). -->
            {#if view.eta && row.posting?.expectedBy}
              {@const expected = row.posting.expectedBy}
              <Hint content={formatDateTime(expected)}>
                {#snippet children({ props })}
                  <span {...props} class="inline-flex rounded-full"><Badge variant="muted">{view.eta}</Badge></span>
                {/snippet}
              </Hint>
            {/if}
          </div>
          <span class="sr-only">{view.detail}</span>
        </li>
      {/each}
    </ul>
  {/if}
</Panel>

{#snippet messages()}
  <div class="flex flex-col gap-1">
    <FailureText message={failure || publishFailure} />
    <p role="status" class="text-body text-muted-foreground empty:hidden">{notice}</p>
  </div>
{/snippet}
