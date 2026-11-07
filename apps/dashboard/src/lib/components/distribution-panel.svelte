<script lang="ts">
  import CircleOffIcon from '~icons/lucide/circle-off';
  import CopyIcon from '~icons/lucide/copy';
  import ExternalLinkIcon from '~icons/lucide/external-link';
  import PencilIcon from '~icons/lucide/pencil';
  import RotateCwIcon from '~icons/lucide/rotate-cw';
  import SendIcon from '~icons/lucide/send';
  import SlidersIcon from '~icons/lucide/sliders-horizontal';
  import type { BoardSummary, JobDetail, JobPostingStatus } from '@screenhire/api-client';
  import { createCloseJobPosting, createPublishJob } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import {
    createColumnHelper,
    createSortedRowModel,
    createTable,
    FlexRender,
    renderSnippet,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import CustomizePostingSheet from '#lib/components/customize-posting-sheet.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import PlanProblemNotice from '#lib/components/plan-problem-notice.svelte';
  import Hint from '#lib/components/hint.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import {
    boardAction,
    boardRows,
    canCustomize,
    describePosting,
    isCustomized,
    isPosted,
    needsDetailFixes,
    postedAt,
    type BoardAction,
    type BoardRow,
  } from '#lib/distribution.js';
  import { describeError, invalidatePath, planProblem, type PlanProblem } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { needsAttention } from '#lib/status.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // Where the job is posted, the job page's Job postings tab: a table with one row per board, its state, when the job was
  // posted there, when that last changed, and a row menu: a link to the live posting where the board has one, then what
  // the person can do there. A posting that needs fixes says what is wrong in words under its row (E2). Publishing a
  // draft to every board is the page's primary action, in its header band, and what it did shows above the tabs; closing
  // the job is its More menu. Customize, in the same menu, changes what one board receives (a sheet); a board with text
  // of its own carries one Customized badge after its name.
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

  const boardList = $derived(boardRows(boards, job.postings));
  let failure = $state('');
  // A post the plan refused: a notice with the way to upgrade, in place of the failure line.
  let refused = $state<PlanProblem | null>(null);
  let busyBoard = $state<string | null>(null);

  // Every column sorts (L2). The boards keep their natural order, the careers page first, until someone sorts.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, BoardRow>();
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, BoardRow, any>[] = [
    column.accessor((row) => describePosting(row.posting, now).label, { id: 'status', header: 'Status', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(statusCell, row.original) }),
    column.accessor('name', { id: 'board', header: 'Board', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(boardCell, row.original) }),
    // A board the job never reached has neither date, so it sorts last either way.
    column.accessor((row) => postedAt(row.posting), {
      id: 'posted',
      header: 'Posted',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(postedCell, row.original),
    }),
    column.accessor((row) => row.posting?.updatedAt, {
      id: 'updatedAt',
      header: 'Last updated',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(updatedCell, row.original),
    }),
    column.display({ id: 'actions', header: () => renderSnippet(actionsHeader, undefined), cell: ({ row }) => renderSnippet(actionsCell, row.original) }),
  ];
  const sort = createTableSort('job-postings', undefined, ['status', 'board', 'posted', 'updatedAt']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return boardList;
    },
    getRowId: (row) => row.boardId,
  });
  const rows = $derived(table.getRowModel().rows);

  // Customize opens a sheet for one board. From the menu it opens once the menu has closed and given focus back, so the
  // sheet can give it back in turn (MoreMenu's `onclosed`); from the words under a row it opens at once.
  let customizing = $state<{ boardId: string; name: string } | null>(null);
  let customizeOpen = $state(false);
  let chosen: { boardId: string; name: string } | null = null;
  function openCustomize(target: { boardId: string; name: string }) {
    customizing = target;
    customizeOpen = true;
  }

  // Live updates refetch the job when a board answers; refetching here as well shows our own change at once.
  const settle = { onSettled: () => invalidatePath(queryClient, jobsPath) };
  const publish = createPublishJob(() => ({ mutation: settle }));
  const takeDown = createCloseJobPosting(() => ({ mutation: settle }));

  async function act(action: NonNullable<BoardAction>, boardId: string, name: string) {
    failure = '';
    refused = null;
    busyBoard = boardId;
    try {
      if (action === 'takeDown') await takeDown.mutateAsync({ jobId: job.id, boardId });
      else await publish.mutateAsync({ jobId: job.id, data: { boards: [boardId] } });
    } catch (error) {
      refused = planProblem(error);
      if (!refused) failure = describeError(error, action === 'takeDown' ? `Could not take the job down from ${name}` : `Could not post the job to ${name}`);
    } finally {
      busyBoard = null;
    }
  }

  function actionItem(action: NonNullable<BoardAction>, posting: JobPostingStatus | undefined) {
    if (action === 'takeDown') return { label: 'Take down', icon: CircleOffIcon };
    if (action === 'retry') return { label: 'Retry', icon: RotateCwIcon };
    return { label: posting ? 'Post again' : 'Post', icon: SendIcon };
  }

  // What Copy link says for two seconds after a press. Without a clipboard it says the copy failed; View posting is beside
  // it.
  let copied = $state<string | null>(null);
  let copyTimer: ReturnType<typeof setTimeout> | undefined;
  async function copyLink(url: string) {
    clearTimeout(copyTimer);
    try {
      await navigator.clipboard.writeText(url);
      copied = 'Copied';
    } catch {
      copied = 'Could not copy';
    }
    copyTimer = setTimeout(() => (copied = null), 2000);
  }
  $effect(() => () => clearTimeout(copyTimer));
</script>

<!-- The tab's count says how many boards the job went to, so the panel's title has none (W4). -->
{#if refused}<PlanProblemNotice problem={refused} />{/if}
<Panel title="Job postings" flush footer={failure ? messages : undefined}>
  {#if boardList.length === 0}
    <p class="px-4.5 py-4 text-muted-foreground max-md:px-4">No job boards are set up.</p>
  {:else}
    <!-- A phone has no header to sort by, so the Sort chip offers the same choices (L2). -->
    <div class="flex border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
    <!-- relative: the Actions header's sr-only label is absolute, and would escape the scroller and widen the page (F1). -->
    <div class="relative overflow-x-auto">
      <table aria-label="Job postings" class="w-full border-collapse text-left">
        <!-- A phone has no header: each row carries its dates under the board's name, as a list item does (section 8). -->
        <thead class="bg-muted max-md:hidden">
          {#each table.getHeaderGroups() as group (group.id)}
            <tr>
              {#each group.headers as header (header.id)}
                <SortHeader
                  {header}
                  class={[
                    'px-3',
                    (header.id === 'status' || header.id === 'actions') && 'px-4',
                    header.id === 'actions' && 'w-14',
                    (header.id === 'posted' || header.id === 'updatedAt' || header.id === 'actions') && 'text-right',
                  ]}
                />
              {/each}
            </tr>
          {/each}
        </thead>
        <tbody>
          {#each rows as row (row.id)}
            <!-- A row keeps one height (L1): a problem is the notice above the tabs, its fixes are in the row's menu. -->
            <tr data-board={row.id} class="h-12 border-b border-border-subtle last:border-b-0 hover:bg-muted">
              {#each row.getAllCells() as cell (cell.id)}
                <td
                  class={[
                    'px-3 whitespace-nowrap max-md:py-2',
                    (cell.column.id === 'status' || cell.column.id === 'actions') && 'px-4',
                    // The board takes the room the other columns leave. A long name is cut, whole in its tooltip (F5).
                    cell.column.id === 'board' && 'w-full max-w-0 min-w-40',
                    (cell.column.id === 'posted' || cell.column.id === 'updatedAt') && 'text-right text-muted-foreground tabular-nums max-md:hidden',
                    cell.column.id === 'actions' && 'text-right',
                  ]}
                >
                  <FlexRender {cell} />
                </td>
              {/each}
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</Panel>

{#if customizing}
  <CustomizePostingSheet bind:open={customizeOpen} jobId={job.id} boardId={customizing.boardId} boardName={customizing.name} />
{/if}

{#snippet messages()}
  <FailureText message={failure} />
{/snippet}

{#snippet statusCell(row: BoardRow)}
  {@const view = describePosting(row.posting, now)}
  <!-- The tooltip says what the state means, with the board's estimate (T2). -->
  <Hint content={view.detail}>
    {#snippet children({ props })}
      <span {...props} data-posting-state class="inline-flex rounded-full">
        <StatusBadge status={view.status} label={view.label} quiet={!needsAttention(view.status)} />
      </span>
    {/snippet}
  </Hint>
  <span class="sr-only">{view.detail}</span>
{/snippet}

{#snippet boardCell(row: BoardRow)}
  {@const posted = postedAt(row.posting)}
  <!-- The board's name, with at most one badge after it (L1). -->
  <span class="flex min-w-0 items-center gap-2">
    <TruncatedText text={row.name} class="font-semibold" />
    {#if canCustomize(job.status, row.board, row.posting) && row.posting?.jobChangedSinceCustomized}
      <Hint content="The job changed since this board was customized.">
        {#snippet children({ props })}<span {...props} class="inline-flex shrink-0 rounded-full"><Badge variant="muted">Job changed</Badge></span>{/snippet}
      </Hint>
    {:else if isCustomized(row.posting)}
      <Badge variant="muted" class="shrink-0">Customized</Badge>
    {/if}
  </span>
  {#if posted && row.posting}
    <span class="flex flex-col text-caption text-muted-foreground md:hidden">
      <span>Posted <DateText at={posted} /></span>
      <span>Updated <DateText at={row.posting.updatedAt} /></span>
    </span>
  {/if}
{/snippet}

{#snippet postedCell(row: BoardRow)}
  {@const posted = postedAt(row.posting)}
  {#if posted}<DateText at={posted} />{/if}
{/snippet}

{#snippet updatedCell(row: BoardRow)}
  {#if row.posting}<DateText at={row.posting.updatedAt} />{/if}
{/snippet}

{#snippet actionsHeader()}<span class="sr-only">Actions</span>{/snippet}

{#snippet actionsCell(row: BoardRow)}
  {@const action = boardAction(job.status, row.board, row.posting)}
  {@const url = row.posting?.viewUrl}
  {@const fixable = needsDetailFixes(job.status, row.posting)}
  {@const customizable = canCustomize(job.status, row.board, row.posting)}
  <!-- A board that only closes with the job has nothing to do, and its menu says why (E3). -->
  {@const locked = job.status === 'Open' && isPosted(row.posting) && !action}
  {#if url || fixable || customizable || action || locked}
    <MoreMenu
      label={`Actions for ${row.name}`}
      size="icon"
      quiet
      onclosed={() => {
        if (chosen) openCustomize(chosen);
        chosen = null;
      }}
    >
      {#if url}
        <DropdownMenu.Item>
          {#snippet child({ props })}
            <a {...props} href={url} target="_blank" rel="noopener"><ExternalLinkIcon aria-hidden="true" />View posting<span class="sr-only"> (opens in a new tab)</span></a>
          {/snippet}
        </DropdownMenu.Item>
        <!-- Stays open, so the item can say the copy worked. -->
        <DropdownMenu.Item closeOnSelect={false} onSelect={() => copyLink(url)}><CopyIcon aria-hidden="true" />{copied ?? 'Copy link'}</DropdownMenu.Item>
      {/if}
      {#if customizable}
        <DropdownMenu.Item onSelect={() => (chosen = { boardId: row.boardId, name: row.name })}><SlidersIcon aria-hidden="true" />Customize for {row.name}</DropdownMenu.Item>
      {/if}
      {#if fixable}
        <DropdownMenu.Item>
          {#snippet child({ props })}<a {...props} href={`/jobs/${job.id}/edit`}><PencilIcon aria-hidden="true" />Edit details for every board</a>{/snippet}
        </DropdownMenu.Item>
      {/if}
      {#if (url || fixable || customizable) && (action || locked)}<DropdownMenu.Separator />{/if}
      {#if action}
        {@const item = actionItem(action, row.posting)}
        <DropdownMenu.Item disabled={busyBoard === row.boardId} onSelect={() => act(action, row.boardId, row.name)}><item.icon aria-hidden="true" />{item.label}</DropdownMenu.Item>
      {:else if locked}
        <DropdownMenu.Item disabled>Stays on while the job is open</DropdownMenu.Item>
      {/if}
    </MoreMenu>
  {/if}
{/snippet}
