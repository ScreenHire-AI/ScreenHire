<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CircleOffIcon from '~icons/lucide/circle-off';
  import ClipboardCopyIcon from '~icons/lucide/clipboard-copy';
  import CopyIcon from '~icons/lucide/copy';
  import ExternalLinkIcon from '~icons/lucide/external-link';
  import PencilIcon from '~icons/lucide/pencil';
  import PlusIcon from '~icons/lucide/plus';
  import RotateCwIcon from '~icons/lucide/rotate-cw';
  import SendIcon from '~icons/lucide/send';
  import SlidersIcon from '~icons/lucide/sliders-horizontal';
  import type { BoardSummary, JobDetail, JobPostingStatus, ManualPostingStatus } from '@screenhire/api-client';
  import {
    createCloseJobPosting,
    createListManualPostings,
    createListSourceApplications,
    createMarkManualPostingUpdated,
    createPublishJob,
    createTakeDownManualPosting,
  } from '@screenhire/api-client/svelte-query';
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
  import PostItYourselfSheet from '#lib/components/post-it-yourself-sheet.svelte';
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
  import { Button } from '#lib/components/ui/button/index.js';
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
    type PostingView,
  } from '#lib/distribution.js';
  import { CopyFeedback } from '#lib/copy-feedback.svelte.js';
  import { formatDay } from '#lib/ai-usage.js';
  import { applicationsBySource, changedSentence, describeManualPosting, localToday, type PostItYourselfMode } from '#lib/manual-postings.js';
  import { can } from '#lib/permissions.js';
  import { page } from '$app/state';
  import { describeError, invalidatePath, planProblem, type PlanProblem } from '#lib/query.js';
  import { jobsPath } from '#lib/realtime.js';
  import { rowLink } from '#lib/row-link.js';
  import { needsAttention } from '#lib/status.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // Where the job is posted, the job page's Job postings tab: a table with one row per board, its state, when the job was
  // posted there, how many applications came through it, when that last changed, and a row menu: a link to the live
  // posting where the board has one, then what the person can do there. A posting that needs fixes says what is wrong in
  // words under its row (E2). Publishing a draft to every board is the page's primary action, in its header band, and what
  // it did shows above the tabs; closing the job is its More menu. Customize, in the same menu, changes what one board
  // receives (a sheet); a board with text of its own carries one Customized badge after its name.
  // Below the boards ScreenHire posts to come the ones the team posted on themselves (Post it yourself, in the panel's
  // header, job-distribution.md "Posted by you"): Posted by you, with the board's name linking to the listing, until it is
  // marked taken down. A closed job, or a passed end date, makes the row Still posted by you, which asks for that.
  let {
    job,
    boards,
    now: fixedNow,
  }: {
    job: Pick<JobDetail, 'id' | 'status' | 'postings' | 'title' | 'location' | 'employmentType' | 'salary' | 'description'>;
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
  const today = $derived(localToday(new Date(now)));

  const manual = createListManualPostings(() => job.id);
  const sources = createListSourceApplications(() => job.id);
  // Blank until the counts arrive, rather than a zero that is not yet true.
  const applicationsOf = $derived(sources.data ? applicationsBySource(sources.data.data) : () => null);
  const mayEdit = $derived(can(page.data.user, 'EditJobs'));

  type Row =
    | { kind: 'board'; id: string; name: string; board: BoardRow }
    | { kind: 'manual'; id: string; name: string; posting: ManualPostingStatus };
  const rowList = $derived<Row[]>([
    ...boardRows(boards, job.postings).map((board): Row => ({ kind: 'board', id: board.boardId, name: board.name, board })),
    ...(manual.data?.data.postings ?? []).map((posting): Row => ({ kind: 'manual', id: `manual:${posting.id}`, name: posting.boardName, posting })),
  ]);
  const viewOf = (row: Row): PostingView =>
    row.kind === 'board' ? describePosting(row.board.posting, now) : describeManualPosting(row.posting, job.status, today);
  const postedOf = (row: Row) => (row.kind === 'board' ? postedAt(row.board.posting) : row.posting.postedOn);
  const updatedOf = (row: Row) => (row.kind === 'board' ? row.board.posting?.updatedAt : row.posting.updatedAt);
  const countOf = (row: Row) => (row.kind === 'board' ? (row.board.posting ? applicationsOf(row.board.boardId) : null) : applicationsOf(row.posting.sourceKey));

  let failure = $state('');
  // A post the plan refused: a notice with the way to upgrade, in place of the failure line.
  let refused = $state<PlanProblem | null>(null);
  let busyBoard = $state<string | null>(null);

  // Every column sorts (L2). The boards keep their natural order, the careers page first and the team's own last, until
  // someone sorts.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  const column = createColumnHelper<typeof features, Row>();
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, Row, any>[] = [
    column.accessor((row) => viewOf(row).label, { id: 'status', header: 'Status', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(statusCell, row.original) }),
    column.accessor('name', { id: 'board', header: 'Board', sortFn: 'alphanumeric', cell: ({ row }) => renderSnippet(boardCell, row.original) }),
    // A board the job never reached has neither date, so it sorts last either way.
    column.accessor((row) => postedOf(row), {
      id: 'posted',
      header: 'Posted',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(postedCell, row.original),
    }),
    // Applications through the board's own link. Google for Jobs and the public job-board API send candidates to the careers
    // page's own address, so their count is the careers page's and their cell is empty.
    column.accessor((row) => countOf(row) ?? undefined, {
      id: 'applications',
      header: 'Applications',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(applicationsCell, row.original),
    }),
    column.accessor((row) => updatedOf(row), {
      id: 'updatedAt',
      header: 'Last updated',
      sortFn: 'basic',
      sortDescFirst: true,
      sortUndefined: 'last',
      cell: ({ row }) => renderSnippet(updatedCell, row.original),
    }),
    column.display({ id: 'actions', header: () => renderSnippet(actionsHeader, undefined), cell: ({ row }) => renderSnippet(actionsCell, row.original) }),
  ];
  const sort = createTableSort('job-postings', undefined, ['status', 'board', 'posted', 'applications', 'updatedAt']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return rowList;
    },
    getRowId: (row) => row.id,
  });
  const rows = $derived(table.getRowModel().rows);
  // A phone has no Applications column, so the count joins the date under the board's name.
  const applicationWords = (count: number) => `${count} ${count === 1 ? 'application' : 'applications'}`;
  const numeric = (id: string) => id === 'posted' || id === 'applications' || id === 'updatedAt';

  // Customize opens a sheet for one board. From the menu it opens once the menu has closed and given focus back, so the
  // sheet can give it back in turn (MoreMenu's `onclosed`); from the words under a row it opens at once.
  let customizing = $state<{ boardId: string; name: string } | null>(null);
  let customizeOpen = $state(false);
  let chosen: { boardId: string; name: string } | null = null;
  function openCustomize(target: { boardId: string; name: string }) {
    customizing = target;
    customizeOpen = true;
  }

  // Post it yourself, Edit and Copy the updated text share one sheet; from a row menu it opens once the menu has closed.
  let postMode = $state<PostItYourselfMode>({ kind: 'new' });
  let postOpen = $state(false);
  let postChosen: PostItYourselfMode | null = null;
  function openPost(mode: PostItYourselfMode) {
    postMode = mode;
    postOpen = true;
  }
  const boardIds = $derived(boards.map((board) => board.id));

  // Live updates refetch the job when a board answers; refetching here as well shows our own change at once.
  const settle = { onSettled: () => invalidatePath(queryClient, jobsPath) };
  const publish = createPublishJob(() => ({ mutation: settle }));
  const takeDown = createCloseJobPosting(() => ({ mutation: settle }));
  const markUpdated = createMarkManualPostingUpdated(() => ({ mutation: settle }));
  const markTakenDown = createTakeDownManualPosting(() => ({ mutation: settle }));

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

  async function actManual(action: 'updated' | 'takenDown', posting: ManualPostingStatus) {
    failure = '';
    busyBoard = posting.id;
    try {
      if (action === 'updated') await markUpdated.mutateAsync({ jobId: job.id, postingId: posting.id });
      else await markTakenDown.mutateAsync({ jobId: job.id, postingId: posting.id });
    } catch (error) {
      failure = describeError(error, action === 'updated' ? `Could not mark ${posting.boardName} updated` : `Could not mark ${posting.boardName} taken down`);
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
  const copyFeedback = new CopyFeedback();
</script>

<!-- The tab's count says how many boards the job went to, so the panel's title has none (W4). -->
{#if refused}<PlanProblemNotice problem={refused} />{/if}
<Panel title="Job postings" flush footer={failure ? messages : undefined} actions={mayEdit && job.status === 'Open' ? postActions : undefined}>
  {#if rowList.length === 0}
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
                    header.id === 'status' && 'sticky left-0 bg-muted',
                    header.id === 'actions' && 'sticky right-0 w-14 bg-muted',
                    (numeric(header.id) || header.id === 'actions') && 'text-right',
                  ]}
                />
              {/each}
            </tr>
          {/each}
        </thead>
        <tbody>
          {#each rows as row (row.id)}
            <!-- A row keeps one height (L1): a problem is the notice above the tabs, its fixes are in the row's menu. On a
                 phone a row with dates has three more lines (posted, applications, updated), so every row is held to that height. -->
            <!-- A board that can be customized opens Customize from anywhere on its row (L5): its name is the button. -->
            <tr {@attach rowLink} data-board={row.original.kind === 'board' ? row.id : undefined} data-manual-posting={row.original.kind === 'manual' ? row.original.posting.id : undefined} class="group h-12 border-b border-border-subtle last:border-b-0 hover:bg-muted max-md:h-21">
              {#each row.getAllCells() as cell (cell.id)}
                <td
                  class={[
                    'px-3 whitespace-nowrap max-md:py-2',
                    (cell.column.id === 'status' || cell.column.id === 'actions') && 'px-4',
                    // Status and the row menu stick while a tablet scrolls the table (section 8); a stuck cell covers what
                    // scrolls under it, so it carries the row's hover itself.
                    cell.column.id === 'status' && 'sticky left-0 bg-card group-hover:bg-muted',
                    // The board takes the room the other columns leave. A long name is cut, whole in its tooltip (F5). On a
                    // phone it has no minimum, or the row menu is pushed past the panel's edge.
                    cell.column.id === 'board' && 'w-full max-w-0 min-w-40 max-md:min-w-0',
                    numeric(cell.column.id) && 'text-right text-muted-foreground tabular-nums max-md:hidden',
                    cell.column.id === 'actions' && 'sticky right-0 bg-card text-right group-hover:bg-muted',
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
{#if manual.data}
  <PostItYourselfSheet bind:open={postOpen} mode={postMode} {job} jobUrl={manual.data.data.jobUrl} {boardIds} />
{/if}

{#snippet postActions()}
  <!-- For a board ScreenHire does not post to: an outline button, because the page's primary is Publish (A1). -->
  <Button variant="outline" disabled={!manual.data} onclick={() => openPost({ kind: 'new' })} data-post-it-yourself-open><PlusIcon aria-hidden="true" />Post it yourself</Button>
{/snippet}

{#snippet messages()}
  <FailureText message={failure} />
{/snippet}

{#snippet statusCell(row: Row)}
  {@const view = viewOf(row)}
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

{#snippet boardCell(row: Row)}
  {#if row.kind === 'board'}
    {@const board = row.board}
    {@const posted = postedAt(board.posting)}
    <!-- The board's name, with at most one badge after it (L1). -->
    <span class="flex min-w-0 items-center gap-2">
      {#if canCustomize(job.status, board.board, board.posting)}
        <TruncatedText
          text={board.name}
          data-row-link
          aria-label={`Customize for ${board.name}`}
          class="font-semibold underline-offset-2 hover:underline pointer-coarse:min-h-11"
          onclick={() => openCustomize({ boardId: board.boardId, name: board.name })}
        />
      {:else}
        <TruncatedText text={board.name} class="font-semibold" />
      {/if}
      {#if canCustomize(job.status, board.board, board.posting) && board.posting?.jobChangedSinceCustomized}
        <Hint content="The job changed since this board was customized.">
          {#snippet children({ props })}<span {...props} class="inline-flex shrink-0 rounded-full"><Badge variant="muted">Job changed</Badge></span>{/snippet}
        </Hint>
      {:else if isCustomized(board.posting)}
        <Badge variant="muted" class="shrink-0">Customized</Badge>
      {/if}
    </span>
    {#if posted && board.posting}
      {@const count = countOf(row)}
      <!-- One line each, cut rather than run under the row menu (L1); the count has its own line, so it is never the part cut. -->
      <span class="flex min-w-0 flex-col text-caption text-muted-foreground md:hidden">
        <span class="truncate">Posted <DateText at={posted} /></span>
        {#if count !== null}<span class="truncate">{applicationWords(count)}</span>{/if}
        <span class="truncate">Updated <DateText at={board.posting.updatedAt} /></span>
      </span>
    {/if}
  {:else}
    {@const posting = row.posting}
    <!-- The board's name links to the listing the person gave; nothing reads that address (job-distribution.md). -->
    <span class="flex min-w-0 items-center gap-2">
      {#if posting.listingUrl}
        <a href={posting.listingUrl} target="_blank" rel="noopener noreferrer" class="flex min-w-0 font-semibold underline-offset-2 hover:underline pointer-coarse:min-h-11 pointer-coarse:items-center" data-listing-link>
          <TruncatedText text={posting.boardName} /><span class="sr-only"> (opens the listing in a new tab)</span>
        </a>
      {:else}
        <TruncatedText text={posting.boardName} class="font-semibold" />
      {/if}
      {#if !posting.takenDownAt && posting.changedFields.length > 0}
        <Hint content={changedSentence(posting.changedFields)}>
          {#snippet children({ props })}<span {...props} class="inline-flex shrink-0 rounded-full" data-job-changed><Badge variant="muted">Job changed since you posted</Badge></span>{/snippet}
        </Hint>
      {/if}
    </span>
    {@const count = countOf(row)}
    <span class="flex min-w-0 flex-col text-caption text-muted-foreground md:hidden">
      <span class="truncate">Posted {formatDay(posting.postedOn)}</span>
      {#if count !== null}<span class="truncate">{applicationWords(count)}</span>{/if}
      <span class="truncate">Updated <DateText at={posting.updatedAt} /></span>
    </span>
  {/if}
{/snippet}

{#snippet postedCell(row: Row)}
  {#if row.kind === 'manual'}
    <!-- A day the person chose, not an instant, so it reads the same in every time zone. -->
    <time datetime={row.posting.postedOn}>{formatDay(row.posting.postedOn)}</time>
  {:else}
    {@const posted = postedAt(row.board.posting)}
    {#if posted}<DateText at={posted} />{/if}
  {/if}
{/snippet}

{#snippet applicationsCell(row: Row)}
  {@const count = countOf(row)}
  {#if count !== null}
    <span data-applications>{count}</span>
  {:else if sources.data && row.kind === 'board' && row.board.posting}
    <span class="sr-only">Counted with the careers page</span>
  {/if}
{/snippet}

{#snippet updatedCell(row: Row)}
  {@const updated = updatedOf(row)}
  {#if updated}<DateText at={updated} />{/if}
{/snippet}

{#snippet actionsHeader()}<span class="sr-only">Actions</span>{/snippet}

{#snippet actionsCell(row: Row)}
  {#if row.kind === 'board'}
    {@render boardActions(row.board)}
  {:else}
    {@render manualActions(row.posting)}
  {/if}
{/snippet}

{#snippet manualActions(posting: ManualPostingStatus)}
  {@const up = !posting.takenDownAt}
  {#if posting.listingUrl || mayEdit}
    <MoreMenu
      label={`Actions for ${posting.boardName}`}
      size="icon"
      quiet
      onclosed={() => {
        if (postChosen) openPost(postChosen);
        postChosen = null;
      }}
    >
      {#if mayEdit}
        <DropdownMenu.Item onSelect={() => (postChosen = { kind: 'edit', posting })}><PencilIcon aria-hidden="true" />Edit</DropdownMenu.Item>
      {/if}
      {#if posting.listingUrl}
        <DropdownMenu.Item>
          {#snippet child({ props })}
            <a {...props} href={posting.listingUrl} target="_blank" rel="noopener noreferrer"><ExternalLinkIcon aria-hidden="true" />Open the listing<span class="sr-only"> (opens in a new tab)</span></a>
          {/snippet}
        </DropdownMenu.Item>
      {/if}
      {#if mayEdit && up}
        <DropdownMenu.Item onSelect={() => (postChosen = { kind: 'update', posting })}><ClipboardCopyIcon aria-hidden="true" />Copy the updated text</DropdownMenu.Item>
        {#if posting.changedFields.length > 0}
          <DropdownMenu.Item disabled={busyBoard === posting.id} onSelect={() => actManual('updated', posting)}><CheckIcon aria-hidden="true" />Mark as updated</DropdownMenu.Item>
        {/if}
        <DropdownMenu.Separator />
        <DropdownMenu.Item disabled={busyBoard === posting.id} onSelect={() => actManual('takenDown', posting)}><CircleOffIcon aria-hidden="true" />Mark as taken down</DropdownMenu.Item>
      {/if}
    </MoreMenu>
  {/if}
{/snippet}

{#snippet boardActions(row: BoardRow)}
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
        <DropdownMenu.Item closeOnSelect={false} onSelect={() => copyFeedback.copyText('link', url)}><CopyIcon aria-hidden="true" />{copyFeedback.said('link', 'Copy link')}</DropdownMenu.Item>
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
