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
  import { needsAttention } from '#lib/status.js';

  // Where the job is posted, the job page's Distribution tab: one row per board with its state as a status badge, the
  // board's estimate as a fact pill and the one thing the person can do there. A posting that needs fixes says what is
  // wrong in words (E2). Publishing a draft to every board is the page's primary action, in its header band, and what it
  // did shows above the tabs; closing the job is its More menu.
  let {
    job,
    boards,
    now: fixedNow,
  }: {
    job: Pick<JobDetail, 'id' | 'status' | 'postings'>;
    boards: readonly BoardSummary[];
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

<!-- The tab's count says how many boards the job is on, so the panel's title has none (W4). -->
<Panel title="Distribution" flush footer={failure ? messages : undefined}>
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
                variant="outline"
                class="ml-auto"
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
                  <StatusBadge status={view.status} label={view.label} quiet={!needsAttention(view.status)} />
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
          <!-- What is wrong, in words (E2); a calm state's meaning is in its tooltip and for screen readers. -->
          {#if needsAttention(view.status)}
            <p data-posting-problem class="text-body text-muted-foreground">
              {view.detail}
              <!-- Issues the board's checks found are in the job's own details, which is where they are fixed. -->
              {#if row.posting?.state === 'Draft' && job.status !== 'Closed'}
                <a href={`/jobs/${job.id}/edit`} class="font-semibold text-primary underline underline-offset-2">Edit details</a>
              {/if}
            </p>
          {:else}
            <span class="sr-only">{view.detail}</span>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</Panel>

{#snippet messages()}
  <FailureText message={failure} />
{/snippet}
